namespace HttpClient.Cache;

public interface IHttpCache : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Get a cache entry by its key.
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <returns>A cache entry which can be any of <see cref="Response"/> or an <see cref="Variation"/> </returns>
    ValueTask<CacheResult> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set a response entry in the cache.
    /// </summary>
    /// <param name="responseKey">The cache key</param>
    /// <param name="response">The response to set</param>
    /// <returns>The cached response</returns>
    Task<Response> SetResponseAsync(
        string responseKey,
        HttpResponseMessage response,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Set a response entry in the cache with a variation dependency.
    /// </summary>
    /// <param name="responseKey">The response cache key</param>
    /// <param name="response">The response to set</param>
    /// <param name="variationKey">The variation cache key</param>
    /// <param name="variation">The variation</param>
    /// <returns>The cached response</returns>
    Task<Response> SetResponseAsync(
        string responseKey,
        HttpResponseMessage response,
        string variationKey,
        Variation variation,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Refresh a response to indicate that it was used
    /// </summary>
    /// <param name="responseKey">The response cache key</param>
    ValueTask RefreshResponseAsync(
        string responseKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Refresh a response to indicate that it was used and set an updated known expiration time.
    /// </summary>
    /// <param name="responseKey"></param>
    /// <param name="notModifiedResponse">The 302 "NOT MODIFIED" response from the server</param>
    ValueTask RefreshResponseAsync(
        string responseKey,
        HttpResponseMessage notModifiedResponse,
        CancellationToken cancellationToken = default
    );
}
