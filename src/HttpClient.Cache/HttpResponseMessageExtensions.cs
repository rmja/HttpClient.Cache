namespace HttpClientCache;

public static class HttpResponseMessageExtensions
{
    internal static DateTimeOffset? GetModified(this HttpResponseMessage response)
    {
        return response.Content.Headers.LastModified;
    }

    internal static DateTimeOffset? GetExpiration(
        this HttpResponseMessage response,
        DateTimeOffset now
    )
    {
        // RFC 7234 §4.2.1 freshness precedence for a (potentially) shared cache:
        // s-maxage > max-age > Expires header.
        var cacheControl = response.Headers.CacheControl;

        var sharedMaxAge = cacheControl?.SharedMaxAge;
        if (sharedMaxAge is not null)
        {
            return now + sharedMaxAge.Value;
        }

        var maxAge = cacheControl?.MaxAge;
        if (maxAge is not null)
        {
            return now + maxAge.Value;
        }

        // Fall back to the Expires header if no explicit max-age is present.
        var expires = response.Content.Headers.Expires;
        if (expires is not null)
        {
            return expires;
        }

        return null;
    }

    /// <summary>
    /// Get the cache type that was used to produce this response.
    /// </summary>
    public static CacheType GetCacheType(this HttpResponseMessage response)
    {
        var request = response.RequestMessage;
        if (
            request is null
            || !request.Options.TryGetValue(CacheConstants.CacheTypeOptionKey, out var type)
        )
        {
            return CacheType.None;
        }
        return type;
    }
}
