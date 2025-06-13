namespace HttpClient.Cache;

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
        var maxAge = response.Headers.CacheControl?.MaxAge;
        if (maxAge is null)
        {
            return null;
        }
        return now + maxAge.Value;
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
