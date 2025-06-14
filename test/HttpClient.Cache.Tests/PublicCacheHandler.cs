using Microsoft.Extensions.Logging;

namespace HttpClientCache.Tests;

public class PublicCacheHandler(IHttpCache cache, ILogger<CacheHandler> logger)
    : CacheHandler(cache, logger)
{
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) =>
        base.SendAsync(request, TestContext.Current.CancellationToken);
}
