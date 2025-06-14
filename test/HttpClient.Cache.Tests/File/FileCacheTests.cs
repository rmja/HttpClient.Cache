using System.Net;
using System.Text;
using HttpClientCache.Files;
using Microsoft.Extensions.Time.Testing;

namespace HttpClientCache.Tests.File;

public sealed class FileCacheTests : IDisposable
{
    private const string RepoUrl = "https://github.com/rmja/HttpClient.Cache";

    private readonly DirectoryInfo _rootDirectory = new(
        Path.Combine(Path.GetTempPath(), typeof(FileCacheTests).FullName!)
    );
    private readonly FileCache _cache;
    private readonly FakeTimeProvider _timeProvider = new();

    public FileCacheTests()
    {
        _cache = new FileCache(_rootDirectory.FullName, new CacheKeyComputer(), _timeProvider);
        _cache.Clear();
        Assert.Empty(_rootDirectory.EnumerateFiles("*.*", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        _cache.Clear();
        Assert.Empty(_rootDirectory.EnumerateFiles("*.*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Get_KeyDoesNotExist()
    {
        // Given
        var request = new HttpRequestMessage(HttpMethod.Get, RepoUrl);

        // When
        Assert.Null(await _cache.GetAsync(request, TestContext.Current.CancellationToken));

        // Then
    }

    [Fact]
    public async Task Get_WithoutVariation()
    {
        // Given
        var key = Guid.NewGuid().ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, RepoUrl);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Headers = { { "Single", "Value1" }, { "Multi", ["Value2", "Value3"] } },
            Content = new StringContent("Hello world", Encoding.UTF8)
            {
                Headers =
                {
                    { "SingleContent", "ValueContent1" },
                    { "MultiContent", ["ValueContent2", "ValueContent3"] },
                },
            },
        };

        using var cachedResponse = await _cache.SetResponseAsync(
            response,
            TestContext.Current.CancellationToken
        );

        // When
        using var found = await _cache.GetAsync(request, TestContext.Current.CancellationToken);

        // Then
        Assert.NotNull(found);
        Assert.Equal(response.Version, found.Version);
        Assert.Equal(response.StatusCode, found.StatusCode);
        Assert.Equal(response.ReasonPhrase, found.ReasonPhrase);
        Assert.Equal(response.Headers, found.Headers);
        Assert.Equal(response.Content.Headers, found.Content.Headers);
        Assert.Empty(found.TrailingHeaders);

        var content = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal("Hello world", content);
    }

    [Fact]
    public async Task Get_WithVariation_SharedSingleHeader()
    {
        // Given
        var request = new HttpRequestMessage(HttpMethod.Get, RepoUrl);
        var response = new HttpResponseMessage
        {
            RequestMessage = request,
            Headers = { Vary = { "Header" } },
        };

        using var cachedResponse = await _cache.SetResponseAsync(
            response,
            TestContext.Current.CancellationToken
        );

        // When
        using var found = await _cache.GetWithVariationAsync(
            request,
            TestContext.Current.CancellationToken
        );

        // Then
        var variation = found?.Variation;
        Assert.Equal(new(CacheType.Shared) { NormalizedVaryHeaders = ["header"] }, variation);
    }

    [Fact]
    public async Task Get_WithVariation_PrivateMultipleHeaders()
    {
        // Given
        var variationKey = Guid.NewGuid().ToString();
        var responseKey = Guid.NewGuid().ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, RepoUrl)
        {
            Headers = { Authorization = new("Bearer", "token") },
        };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,

            Headers = { Vary = { "Header1", "Header2" } },
        };

        using var cachedResponse = await _cache.SetResponseAsync(
            response,
            TestContext.Current.CancellationToken
        );

        // When
        using var found = await _cache.GetWithVariationAsync(
            request,
            TestContext.Current.CancellationToken
        );

        // Then
        Assert.Equal(
            new(CacheType.Private) { NormalizedVaryHeaders = ["header1", "header2"] },
            found?.Variation
        );
    }

    [Fact]
    public async Task Set_ResponseExpiration_WithoutVariation()
    {
        // Given
        var expiration = TimeSpan.FromSeconds(10);

        var request = new HttpRequestMessage(HttpMethod.Get, RepoUrl);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Headers = { CacheControl = new() { MaxAge = expiration } },
        };

        // When
        using var cachedResponse = await _cache.SetResponseAsync(
            response,
            TestContext.Current.CancellationToken
        );

        // Then
        _timeProvider.Advance(TimeSpan.FromSeconds(8));
        using var notExpired = await _cache.GetAsync(
            request,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(TimeSpan.FromSeconds(2), notExpired?.Headers.CacheControl?.MaxAge);

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        using var expired = await _cache.GetAsync(request, TestContext.Current.CancellationToken);
        Assert.Null(expired);
    }

    [Fact]
    public async Task Set_ResponseExpiration_WithVariation()
    {
        // Given
        var expiration = TimeSpan.FromSeconds(10);

        var request = new HttpRequestMessage(HttpMethod.Get, RepoUrl);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Headers =
            {
                CacheControl = new() { MaxAge = expiration },
                Vary = { "Header" },
            },
        };

        // When
        using var cachedResponse = await _cache.SetResponseAsync(
            response,
            TestContext.Current.CancellationToken
        );

        // Then
        using var notExpired = await _cache.GetWithVariationAsync(
            request,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(TimeSpan.FromSeconds(10), notExpired?.Response.Headers.CacheControl?.MaxAge);
        Assert.NotNull(notExpired);
        Assert.Equal(
            new(CacheType.Shared) { NormalizedVaryHeaders = ["header"] },
            notExpired.Variation
        );

        _timeProvider.Advance(TimeSpan.FromSeconds(20));
        using var expired = await _cache.GetAsync(request, TestContext.Current.CancellationToken);
        Assert.Null(expired);
    }

    [Fact]
    public async Task Refresh_MaxAge()
    {
        // Given
        var expiration = TimeSpan.FromSeconds(10);

        var request = new HttpRequestMessage(HttpMethod.Get, RepoUrl);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Headers = { CacheControl = new() { MaxAge = expiration } },
        };

        using var notModifiedResponse = new HttpResponseMessage(HttpStatusCode.NotModified)
        {
            RequestMessage = request,
            Headers = { CacheControl = new() { MaxAge = expiration } },
        };

        // When
        using var cachedResponse = await _cache.SetResponseAsync(
            response,
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(cachedResponse);
        Assert.Equal(TimeSpan.FromSeconds(10), cachedResponse.Headers.CacheControl?.MaxAge);

        _timeProvider.Advance(TimeSpan.FromSeconds(8));
        await _cache.RefreshResponseAsync(
            cachedResponse,
            notModifiedResponse,
            TestContext.Current.CancellationToken
        );

        // Then
        _timeProvider.Advance(TimeSpan.FromSeconds(8));
        using var notExpired = await _cache.GetAsync(
            request,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(TimeSpan.FromSeconds(2), notExpired?.Headers.CacheControl?.MaxAge);

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        using var expired = await _cache.GetAsync(request, TestContext.Current.CancellationToken);
        Assert.Null(expired);
    }

    [Fact]
    public async Task Refresh_NoMaxAge()
    {
        // Given
        _cache.DefaultInitialExpiration = TimeSpan.FromSeconds(10);
        _cache.DefaultRefreshExpiration = TimeSpan.FromSeconds(20);

        var request = new HttpRequestMessage(HttpMethod.Get, RepoUrl);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
        };

        // When
        using var cachedResponse = await _cache.SetResponseAsync(
            response,
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(cachedResponse);

        _timeProvider.Advance(TimeSpan.FromSeconds(8));
        await _cache.RefreshResponseAsync(cachedResponse, TestContext.Current.CancellationToken);

        // Then
        _timeProvider.Advance(TimeSpan.FromSeconds(18));
        using var notExpired = await _cache.GetAsync(
            request,
            TestContext.Current.CancellationToken
        );

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        using var expired = await _cache.GetAsync(request, TestContext.Current.CancellationToken);
        Assert.Null(expired);
    }
}
