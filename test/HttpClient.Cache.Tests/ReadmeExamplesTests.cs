using HttpClient.Cache.Files;
using Microsoft.Extensions.DependencyInjection;

namespace HttpClient.Cache.Tests;

public class ReadmeExamplesTests
{
    [Fact]
    public async Task EnsureReadmeExamplesWorkAsync()
    {
        var services = new ServiceCollection();

        // Register the caching client on a HttpClient in your Program.cs
        var cache = new FileCache();
        cache.Clear();
        services.AddHttpClient("cachedClient").AddResponseCache(cache); // Use cleared cache for testing

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
}
