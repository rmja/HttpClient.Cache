namespace HttpClient.Cache;

public interface IHttpCache : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Get a cached response from a request.
    /// </summary>
    /// <param name="request">The request</param>
    /// <returns>The cached response, or <see langword="null"/> if the response was not found</returns>
    ValueTask<HttpResponseMessage?> GetAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Get a cached response from a request, including the variation if it exists.
    /// </summary>
    /// <param name="request">The request</param>
    /// <returns>The cached response including variation details, or <see langword="null"/> if the response was not found</returns>
    ValueTask<ResponseWithVariation?> GetWithVariationAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Set a response entry in the cache and return the cached equivalent.
    /// </summary>
    /// <param name="response">The response to set</param>
    /// <returns>The cached response, or <see langword="null"/> if the response could not be cached</returns>
    Task<HttpResponseMessage?> SetResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Refresh a response to indicate that it was used
    /// </summary>
    /// <param name="cachedResponse">The cached response to be refreshed</param>
    ValueTask RefreshResponseAsync(
        HttpResponseMessage cachedResponse,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Refresh a response to indicate that it was used and set an updated known expiration time.
    /// </summary>
    /// <param name="cachedResponse">The cached response to be refreshed</param>
    /// <param name="notModifiedResponse">The 302 "NOT MODIFIED" response from the server that corresponds to the response to be refreshed</param>
    ValueTask RefreshResponseAsync(
        HttpResponseMessage cachedResponse,
        HttpResponseMessage notModifiedResponse,
        CancellationToken cancellationToken = default
    );
}
