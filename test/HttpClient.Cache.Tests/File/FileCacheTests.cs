using System.Net;
using System.Text;
using HttpClient.Cache.Files;
using Microsoft.Extensions.Time.Testing;

namespace HttpClient.Cache.Tests.File;

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
        _cache = new FileCache(_rootDirectory.FullName, _timeProvider);
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
        var key = Guid.NewGuid().ToString();

        // When
        Assert.Null(await _cache.GetAsync(key, TestContext.Current.CancellationToken));

        // Then
    }

    [Fact]
    public async Task Get_Response()
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

        using var _ = await _cache.SetResponseAsync(
            key,
            response,
            TestContext.Current.CancellationToken
        );

        // When
        var value = await _cache.GetAsync(key, TestContext.Current.CancellationToken);

        // Then
        using var responseEntry = Assert.IsType<Response>(value);
        using var cachedResponse = responseEntry.ToResponseMessage(request);
        Assert.Equal(response.Version, cachedResponse.Version);
        Assert.Equal(response.StatusCode, cachedResponse.StatusCode);
        Assert.Equal(response.ReasonPhrase, cachedResponse.ReasonPhrase);
        Assert.Equal(response.Headers, cachedResponse.Headers);
        Assert.Equal(response.Content.Headers, cachedResponse.Content.Headers);
        Assert.Empty(cachedResponse.TrailingHeaders);

        var content = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken
        );
        Assert.Equal("Hello world", content);
    }

    [Fact]
    public async Task Get_Variation_SharedSingleHeader()
    {
        // Given
        var variationKey = Guid.NewGuid().ToString();
        var responseKey = Guid.NewGuid().ToString();
        var response = new HttpResponseMessage
        {
            RequestMessage = new(HttpMethod.Get, RepoUrl),
            Headers = { Vary = { "Header" } },
        };

        using var _ = await _cache.SetResponseAsync(
            responseKey,
            response,
            variationKey,
            Variation.FromResponseMessage(response),
            TestContext.Current.CancellationToken
        );

        // When
        var value = await _cache.GetAsync(variationKey, TestContext.Current.CancellationToken);

        // Then
        var variation = Assert.IsType<Variation>(value);
        Assert.Equal(
            new() { CacheType = CacheType.Shared, NormalizedVaryHeaders = ["header"] },
            variation
        );
    }

    [Fact]
    public async Task Get_Variation_PrivateMultipleHeaders()
    {
        // Given
        var variationKey = Guid.NewGuid().ToString();
        var responseKey = Guid.NewGuid().ToString();

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new(HttpMethod.Get, RepoUrl)
            {
                Headers = { Authorization = new("Bearer", "token") },
            },
            Headers = { Vary = { "Header1", "Header2" } },
        };

        using var cachedResponse = await _cache.SetResponseAsync(
            responseKey,
            response,
            variationKey,
            Variation.FromResponseMessage(response),
            TestContext.Current.CancellationToken
        );

        // When
        var value = await _cache.GetAsync(variationKey, TestContext.Current.CancellationToken);

        // Then
        var variation = Assert.IsType<Variation>(value);
        Assert.Equal(
            new() { CacheType = CacheType.Private, NormalizedVaryHeaders = ["header1", "header2"] },
            variation
        );
    }

    [Fact]
    public async Task Set_ResponseExpiration()
    {
        // Given
        var responseKey = Guid.NewGuid().ToString();
        var expiration = TimeSpan.FromSeconds(10);

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new(HttpMethod.Get, RepoUrl),
            Headers = { CacheControl = new() { MaxAge = expiration } },
        };

        // When
        using var cachedResponse = await _cache.SetResponseAsync(
            responseKey,
            response,
            TestContext.Current.CancellationToken
        );

        // Then
        _timeProvider.Advance(TimeSpan.FromSeconds(8));
        var notExpired = await _cache.GetAsync(responseKey, TestContext.Current.CancellationToken);
        using var notExpiredResponse = Assert.IsType<Response>(notExpired);
        Assert.Equal(TimeSpan.FromSeconds(2), notExpiredResponse.MaxAge);

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        var expired = await _cache.GetAsync(responseKey, TestContext.Current.CancellationToken);
        Assert.Null(expired);
    }

    [Fact]
    public async Task Set_ResponseExpirationRemovesVariation()
    {
        // Given
        var responseKey = Guid.NewGuid().ToString();
        var variationKey = Guid.NewGuid().ToString();
        var expiration = TimeSpan.FromSeconds(10);

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new(HttpMethod.Get, RepoUrl),
            Headers = { CacheControl = new() { MaxAge = expiration } },
        };
        var variation = new Variation()
        {
            CacheType = CacheType.Shared,
            NormalizedVaryHeaders = ["header"],
        };

        // When
        using var cachedResponse = await _cache.SetResponseAsync(
            responseKey,
            response,
            variationKey,
            variation,
            TestContext.Current.CancellationToken
        );

        // Then
        var notExpired = await _cache.GetAsync(responseKey, TestContext.Current.CancellationToken);
        using var notExpiredResponse = Assert.IsType<Response>(notExpired);
        Assert.Equal(TimeSpan.FromSeconds(10), notExpiredResponse.MaxAge);

        notExpired = await _cache.GetAsync(variationKey, TestContext.Current.CancellationToken);
        Assert.NotNull(notExpired);
        Assert.Equal(variation, notExpired);

        _timeProvider.Advance(TimeSpan.FromSeconds(20));
        var expired = await _cache.GetAsync(responseKey, TestContext.Current.CancellationToken);
        Assert.Null(expired);

        expired = await _cache.GetAsync(variationKey, TestContext.Current.CancellationToken);
        Assert.Null(expired);
    }

    [Fact]
    public async Task Refresh_MaxAge()
    {
        // Given
        var responseKey = Guid.NewGuid().ToString();
        var expiration = TimeSpan.FromSeconds(10);

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new(HttpMethod.Get, RepoUrl),
            Headers = { CacheControl = new() { MaxAge = expiration } },
        };

        using var notModifiedResponse = new HttpResponseMessage(HttpStatusCode.NotModified)
        {
            RequestMessage = new(HttpMethod.Get, RepoUrl),
            Headers = { CacheControl = new() { MaxAge = expiration } },
        };

        // When
        using var cachedResponse = await _cache.SetResponseAsync(
            responseKey,
            response,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(TimeSpan.FromSeconds(10), cachedResponse.MaxAge);

        _timeProvider.Advance(TimeSpan.FromSeconds(8));
        await _cache.RefreshResponseAsync(
            responseKey,
            notModifiedResponse,
            TestContext.Current.CancellationToken
        );

        // Then
        _timeProvider.Advance(TimeSpan.FromSeconds(8));
        var notExpired = await _cache.GetAsync(responseKey, TestContext.Current.CancellationToken);
        using var notExpiredResponse = Assert.IsType<Response>(notExpired);
        Assert.Equal(TimeSpan.FromSeconds(2), notExpiredResponse.MaxAge);

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        var expired = await _cache.GetAsync(responseKey, TestContext.Current.CancellationToken);
        Assert.Null(expired);
    }

    [Fact]
    public async Task Refresh_NoMaxAge()
    {
        // Given
        var responseKey = Guid.NewGuid().ToString();

        _cache.DefaultInitialExpiration = TimeSpan.FromSeconds(10);
        _cache.DefaultRefreshExpiration = TimeSpan.FromSeconds(20);

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new(HttpMethod.Get, RepoUrl),
        };

        using var notModifiedResponse = new HttpResponseMessage(HttpStatusCode.NotModified)
        {
            RequestMessage = new(HttpMethod.Get, RepoUrl),
        };

        // When
        using var cachedResponse = await _cache.SetResponseAsync(
            responseKey,
            response,
            TestContext.Current.CancellationToken
        );

        _timeProvider.Advance(TimeSpan.FromSeconds(8));
        await _cache.RefreshResponseAsync(responseKey, TestContext.Current.CancellationToken);

        // Then
        _timeProvider.Advance(TimeSpan.FromSeconds(18));
        var notExpired = await _cache.GetAsync(responseKey, TestContext.Current.CancellationToken);
        using var _ = Assert.IsType<Response>(notExpired);

        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        var expired = await _cache.GetAsync(responseKey, TestContext.Current.CancellationToken);
        Assert.Null(expired);
    }
}
