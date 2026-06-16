using System.Net;
using HttpClientCache.Files;
using Microsoft.Extensions.DependencyInjection;

namespace HttpClientCache.Tests;

public class ReadmeExamplesTests
{
    [Fact]
    public async Task EnsureReadmeExamplesWorkAsync()
    {
        var services = new ServiceCollection();

        // Register the caching client on a HttpClient in your Program.cs
        var cache = new FileCache();
        cache.Clear();

        services
            .AddHttpClient("cachedClient")
            // The README hits the network; the test uses a deterministic, offline origin instead.
            .ConfigurePrimaryHttpMessageHandler(() => new CacheableOriginHandler())
            .AddResponseCache(cache); // Use cleared cache for testing

        var httpClientFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>();

        // Use the client as normally
        var client = httpClientFactory.CreateClient("cachedClient");

        var firstResponse = await client.GetAsync("https://example.com/"); // Returns a "non-private" Cache-Control header
        var secondResponse = await client.GetAsync("https://example.com/");
        Assert.Equal(CacheType.None, firstResponse.GetCacheType()); // This response was not obtained from cache.
        Assert.Equal(CacheType.Shared, secondResponse.GetCacheType()); // This response was obtained from the shared (not private) cache.
    }

    /// <summary>
    /// A stand-in origin that returns a publicly cacheable response, so the example is
    /// deterministic and does not depend on network access.
    /// </summary>
    private sealed class CacheableOriginHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("Hello"),
                Headers = { CacheControl = new() { MaxAge = TimeSpan.FromMinutes(5) } },
            };
            return Task.FromResult(response);
        }
    }
}
