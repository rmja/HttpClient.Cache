using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace HttpClientCache;

/// <summary>
/// Minimal, allocation-light, AOT/trim-safe reader for the payload (claims) of a JWS compact
/// serialized JWT. The signature is intentionally <b>not</b> validated: the cache only needs a
/// stable user identifier to isolate private cache entries, and signature validation is the
/// responsibility of the layer that actually trusts the token.
/// </summary>
internal static class JwtDecoder
{
    /// <summary>
    /// Attempts to decode the payload segment of a JWS compact serialized JWT
    /// (<c>header.payload.signature</c>) without validating its signature.
    /// </summary>
    /// <param name="token">The raw access token (without the "Bearer " prefix).</param>
    /// <param name="payload">
    /// The decoded payload as a <see cref="JsonDocument"/> when the method returns
    /// <see langword="true"/>. The caller owns the document and must dispose it.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="token"/> is a structurally valid JWT whose
    /// payload is a JSON object; otherwise <see langword="false"/>.
    /// </returns>
    public static bool TryDecodePayload(
        string token,
        [NotNullWhen(true)] out JsonDocument? payload
    )
    {
        payload = null;

        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        // The payload is the second segment, between the first and second '.' separators.
        var firstDot = token.IndexOf('.');
        if (firstDot < 0)
        {
            return false;
        }

        var rest = token.AsSpan(firstDot + 1);
        var nextDot = rest.IndexOf('.');
        var payloadSegment = nextDot >= 0 ? rest[..nextDot] : rest;
        if (payloadSegment.IsEmpty)
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(payloadSegment);
        }
        catch (FormatException)
        {
            // Not valid base64url - not a JWT.
            return false;
        }

        try
        {
            var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                return false;
            }

            payload = document;
            return true;
        }
        catch (JsonException)
        {
            // Payload is not valid JSON - not a JWT.
            return false;
        }
    }
}
