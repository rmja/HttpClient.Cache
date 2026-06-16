using System.Text;
using System.Text.Json;

namespace HttpClientCache;

public class CacheKeyComputer : ICacheKeyComputer
{
    private const char Separator = '\x1E';
    private const char Null = '\x00';

    [ThreadStatic]
    private static StringBuilder? _stringBuilder;

    public bool RequireJwtToken { get; set; } = false;

    public string? ComputeKey(HttpRequestMessage request, Variation variation)
    {
        var builder = _stringBuilder ??= new();

        try
        {
            var url = request.RequestUri!;

            builder.Append(request.Method.Method.ToLowerInvariant());
            builder.Append(Separator);
            builder.Append(url.Scheme.ToLowerInvariant());
            builder.Append(Separator);
            builder.Append(url.Host.ToLowerInvariant());
            builder.Append(Separator);
            builder.Append(url.Port);
            builder.Append(Separator);
            builder.Append(url.PathAndQuery);

            builder.Append(Separator);

            if (variation.CacheType == CacheType.Private)
            {
                var userId = GetUserId(request);
                if (userId is null)
                {
                    return null;
                }

                builder.Append(userId);
            }
            else
            {
                builder.Append(Null);
            }

            foreach (var headerName in variation.NormalizedVaryHeaders)
            {
                builder.Append(Separator).Append(headerName).Append('=');

                if (request.Headers.TryGetValues(headerName, out var headerValues))
                {
                    var headerValuesArray = headerValues.ToArray();
                    Array.Sort(headerValuesArray, StringComparer.Ordinal);

                    builder.Append(headerValuesArray[0]);

                    for (var i = 1; i < headerValuesArray.Length; i++)
                    {
                        builder.Append(',');
                        builder.Append(headerValuesArray[i]);
                    }
                }
                else
                {
                    builder.Append(Null);
                }
            }

            return builder.ToString();
        }
        finally
        {
            builder.Clear();
        }
    }

    protected virtual string? GetUserId(HttpRequestMessage request)
    {
        if (!request.Headers.TryGetValues("Authorization", out var headerValues))
        {
            return null;
        }

        var headerValue = headerValues.First();
        if (headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var accessToken = headerValue.Substring(7);
            if (JwtDecoder.TryDecodePayload(accessToken, out var payloadDocument))
            {
                // A valid JWT was supplied. Use the identifier derived from its claims even if
                // that identifier is null (e.g. neither "sub" nor "client_id" is present) so that
                // such a token is treated consistently rather than falling back to the raw value.
                using (payloadDocument)
                {
                    return GetUserId(payloadDocument.RootElement);
                }
            }
        }

        if (RequireJwtToken)
        {
            return null;
        }

        // Fallback to using the Authorization header value directly
        // This is not ideal if the token value recycles often, but it is fine if the token is stable (e.g. an api key)
        return headerValue;
    }

    /// <summary>
    /// Derive a stable user identifier from a decoded JWT payload. The default implementation uses
    /// the <c>sub</c> claim, falling back to <c>client_id</c>. Override to use different claims.
    /// </summary>
    /// <param name="jwtPayload">
    /// The decoded JWT payload. This value is only valid for the duration of the call.
    /// </param>
    protected virtual string? GetUserId(JsonElement jwtPayload)
    {
        if (
            jwtPayload.TryGetProperty("sub", out var sub)
            && sub.ValueKind == JsonValueKind.String
        )
        {
            return "sub:" + sub.GetString();
        }

        if (
            jwtPayload.TryGetProperty("client_id", out var clientId)
            && clientId.ValueKind == JsonValueKind.String
        )
        {
            return "client_id:" + clientId.GetString();
        }

        return null;
    }
}
