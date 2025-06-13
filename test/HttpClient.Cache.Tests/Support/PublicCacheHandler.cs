using Microsoft.Extensions.Logging;

namespace HttpClient.Cache.Tests.Support;

public class PublicCacheHandler(
    IHttpCache cache,
    ICacheKeyComputer cacheKeyProvider,
    ILogger<CacheHandler> logger
) : CacheHandler(cache, cacheKeyProvider, logger)
{
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) =>
        base.SendAsync(request, TestContext.Current.CancellationToken);
}
