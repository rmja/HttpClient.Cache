using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using HttpClientCache.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace HttpClientCache.Tests;

public sealed class CacheHandlerTests : IDisposable
{
    private const string RequestUri = "http://google.dk/q=Rasmus?a=!&b=*";

    private readonly DirectoryInfo _rootDirectory = new(
        Path.Combine(Path.GetTempPath(), typeof(CacheHandlerTests).FullName!)
    );
    private readonly CacheKeyComputer _cacheKeyComputer;
    private readonly FileCache _cache;
    private readonly Mock<HttpMessageHandler> _nextMock = new();
    private readonly PublicCacheHandler _handler;
    private readonly FakeTimeProvider _timeProvider = new();

    public CacheHandlerTests()
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<CacheKeyComputer>()
            .AddSingleton<ICacheKeyComputer>(x => x.GetRequiredService<CacheKeyComputer>())
            .AddSingleton<TimeProvider>(_timeProvider)
            .BuildServiceProvider();

        _cacheKeyComputer = provider.GetRequiredService<CacheKeyComputer>();
        _cache = new FileCache(_rootDirectory.FullName, _cacheKeyComputer, _timeProvider);
        _cache.Clear();
        Assert.Empty(_rootDirectory.EnumerateFiles("*.*", SearchOption.AllDirectories));

        _handler = ActivatorUtilities.CreateInstance<PublicCacheHandler>(provider, _cache);
        _handler.InnerHandler = _nextMock.Object;
    }

    public void Dispose()
    {
        _cache.Clear();
        Assert.Empty(_rootDirectory.EnumerateFiles("*.*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Send_NoHit()
    {
        // Given
        var request = new HttpRequestMessage(HttpMethod.Get, RequestUri);

        _nextMock.SetupSendAsync(
            x => x == request,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("Hello"),
            }
        );

        // When
        using var response = await _handler.SendAsync(request);

        // Then
        _nextMock.Verify();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Hello", await response.ReadAsStringAsync());
    }

    [Fact]
    public async Task Send_Shared()
    {
        // Given
        var request = new HttpRequestMessage(HttpMethod.Get, RequestUri);

        _nextMock.SetupSendAsync(
            x => x == request,
            () =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent("Hello"),
                }
        );

        // When
        using var _ = await _handler.SendAsync(request);
        using var response = await _handler.SendAsync(request);

        // Then
        _nextMock.Verify();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Hello", await response.ReadAsStringAsync());
        Assert.Equal(CacheType.Shared, response.GetCacheType());
    }

    [Fact]
    public async Task Send_Shared_Vary()
    {
        // Given
        var request1 = new HttpRequestMessage(HttpMethod.Get, RequestUri)
        {
            Headers = { AcceptLanguage = { new("da") } },
        };
        var request2 = new HttpRequestMessage(HttpMethod.Get, RequestUri)
        {
            Headers = { AcceptLanguage = { new("en") } },
        };

        _nextMock.SetupSendAsync(
            x => x == request1,
            () =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request1,
                    Content = new StringContent("Hej"),
                    Headers = { Vary = { "Accept-Language" } },
                }
        );
        _nextMock.SetupSendAsync(
            x => x == request2,
            () =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request2,
                    Content = new StringContent("Hello"),
                    Headers = { Vary = { "Accept-Language" } },
                }
        );

        // When
        using var _1 = await _handler.SendAsync(request1);
        using var _2 = await _handler.SendAsync(request2);
        using var response1 = await _handler.SendAsync(request1);
        using var response2 = await _handler.SendAsync(request2);

        // Then
        _nextMock.Verify();

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal("Hej", await response1.ReadAsStringAsync());
        Assert.Equal(CacheType.Shared, response1.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("Hello", await response2.ReadAsStringAsync());
        Assert.Equal(CacheType.Shared, response2.GetCacheType());
    }

    [Fact]
    public async Task Send_Shared_Vary_MissingHeader()
    {
        // Given
        var request1 = new HttpRequestMessage(HttpMethod.Get, RequestUri)
        {
            Headers = { AcceptLanguage = { new("da") } },
        };
        var request2 = new HttpRequestMessage(HttpMethod.Get, RequestUri);

        _nextMock.SetupSendAsync(
            x => x == request1,
            () =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request1,
                    Content = new StringContent("Hej"),
                    Headers = { Vary = { "Accept-Language" } },
                }
        );
        _nextMock.SetupSendAsync(
            x => x == request2,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request2,
                Content = new StringContent("Hello"),
                Headers = { Vary = { "Accept-Language" } }, // Sends Vary header even when the header is not in the request
            }
        );

        // When
        using var _1 = await _handler.SendAsync(request1);
        using var _2 = await _handler.SendAsync(request2);
        using var response1 = await _handler.SendAsync(request1);
        using var response2 = await _handler.SendAsync(request2);
        using var response1b = await _handler.SendAsync(request1);
        using var response2b = await _handler.SendAsync(request2);

        // Then
        _nextMock.Verify();

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal("Hej", await response1.ReadAsStringAsync());
        Assert.Equal(CacheType.Shared, response1.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("Hello", await response2.ReadAsStringAsync());
        Assert.Equal(CacheType.Shared, response2.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response1b.StatusCode);
        Assert.Equal("Hej", await response1b.ReadAsStringAsync());
        Assert.Equal(CacheType.Shared, response1b.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response2b.StatusCode);
        Assert.Equal("Hello", await response2b.ReadAsStringAsync());
        Assert.Equal(CacheType.Shared, response2b.GetCacheType());
    }

    [Fact]
    public async Task Send_Shared_BypassCannotReplaceCacheEntry()
    {
        // Given
        var request1 = new HttpRequestMessage(HttpMethod.Get, RequestUri);
        var request2 = new HttpRequestMessage(HttpMethod.Get, RequestUri)
        {
            Headers = { CacheControl = new() { NoCache = true } },
        };
        var request3 = new HttpRequestMessage(HttpMethod.Get, RequestUri);

        _nextMock.SetupSendAsync(
            x => x == request1,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request1,
                Content = new StringContent("Hej"),
            }
        );
        _nextMock.SetupSendAsync(
            x => x == request2,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request2,
                Content = new StringContent("Hello"),
            }
        );
        _nextMock.SetupSendAsync(
            x => x == request3,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request3,
                Content = new StringContent("Hello"),
            }
        );

        // When
        using var response1 = await _handler.SendAsync(request1);
        // Time is not advanced here, so the modified time of the response file is the same as request1
        using var response2 = await _handler.SendAsync(request2); // Cannot overwrite cache entry as the first request has the response file open
        using var response3 = await _handler.SendAsync(request3); // This will therefore find the original response

        // Then
        _nextMock.Verify();

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal("Hej", await response1.ReadAsStringAsync());
        Assert.Equal(CacheType.None, response1.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("Hello", await response2.ReadAsStringAsync());
        Assert.Equal(CacheType.None, response2.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
        Assert.Equal("Hej", await response3.ReadAsStringAsync()); // Find the first response as we were unable to replace
        Assert.Equal(CacheType.Shared, response3.GetCacheType());
    }

    [Fact]
    public async Task Send_Shared_BypassReplacesCacheEntry()
    {
        // Given
        var request1 = new HttpRequestMessage(HttpMethod.Get, RequestUri);
        var request2 = new HttpRequestMessage(HttpMethod.Get, RequestUri)
        {
            Headers = { CacheControl = new() { NoCache = true } },
        };
        var request3 = new HttpRequestMessage(HttpMethod.Get, RequestUri);

        _nextMock.SetupSendAsync(
            x => x == request1,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request1,
                Content = new StringContent("Hej"),
            }
        );
        _nextMock.SetupSendAsync(
            x => x == request2,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request2,
                Content = new StringContent("Hello"),
            }
        );
        _nextMock.SetupSendAsync(
            x => x == request3,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request3,
                Content = new StringContent("Hello"),
            }
        );

        // When
        using var response1 = await _handler.SendAsync(request1);
        _timeProvider.Advance(TimeSpan.FromSeconds(1)); // Ensure that the modified time is different
        using var response2 = await _handler.SendAsync(request2); // Can overwrite cache entry as the first request is done
        using var response3 = await _handler.SendAsync(request3); // This will therefore find the replaced response

        // Then
        _nextMock.Verify();

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal("Hej", await response1.ReadAsStringAsync());
        Assert.Equal(CacheType.None, response1.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("Hello", await response2.ReadAsStringAsync());
        Assert.Equal(CacheType.None, response2.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
        Assert.Equal("Hello", await response3.ReadAsStringAsync()); // Find the second, updated response
        Assert.Equal(CacheType.Shared, response3.GetCacheType());
    }

    [Fact]
    public async Task Send_Private()
    {
        // Given
        var request = new HttpRequestMessage(HttpMethod.Get, RequestUri)
        {
            Headers = { Authorization = new("Bearer", CreateJwt("userId")) },
        };

        _nextMock.SetupSendAsync(
            x => x == request,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("Hello"),
            }
        );

        // When
        using var _ = await _handler.SendAsync(request);
        using var response = await _handler.SendAsync(request);

        // Then
        _nextMock.Verify();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Hello", await response.ReadAsStringAsync());
        Assert.Equal(CacheType.Private, response.GetCacheType());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Send_Private_InvalidJwtToken(bool requireJwtToken)
    {
        // Given
        _cacheKeyComputer.RequireJwtToken = requireJwtToken;

        var request = new HttpRequestMessage(HttpMethod.Get, RequestUri)
        {
            Headers = { Authorization = new("Bearer", "not-a-jwt-token") },
        };

        _nextMock.SetupSendAsync(
            x => x == request,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("Hello"),
            }
        );

        // When
        using var _ = await _handler.SendAsync(request);
        using var response = await _handler.SendAsync(request);

        // Then
        _nextMock.Verify();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Hello", await response.ReadAsStringAsync());
        Assert.Equal(requireJwtToken ? CacheType.None : CacheType.Private, response.GetCacheType());
    }

    [Fact]
    public async Task Send_Private_Vary()
    {
        // Given
        var request1 = new HttpRequestMessage(HttpMethod.Get, RequestUri)
        {
            Headers =
            {
                Authorization = new("Bearer", CreateJwt("userId")),
                AcceptLanguage = { new("da") },
            },
        };
        var request2 = new HttpRequestMessage(HttpMethod.Get, RequestUri)
        {
            Headers =
            {
                Authorization = new("Bearer", CreateJwt("userId")),
                AcceptLanguage = { new("en") },
            },
        };

        _nextMock.SetupSendAsync(
            x => x == request1,
            () =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request1,
                    Content = new StringContent("Hej"),
                    Headers = { Vary = { "Accept-Language" } },
                }
        );
        _nextMock.SetupSendAsync(
            x => x == request2,
            () =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request2,
                    Content = new StringContent("Hello"),
                    Headers = { Vary = { "Accept-Language" } },
                }
        );

        // When
        using var _1 = await _handler.SendAsync(request1);
        using var _2 = await _handler.SendAsync(request2);
        using var response1 = await _handler.SendAsync(request1);
        using var response2 = await _handler.SendAsync(request2);

        // Then
        _nextMock.Verify();

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal("Hej", await response1.ReadAsStringAsync());
        Assert.Equal(CacheType.Private, response1.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("Hello", await response2.ReadAsStringAsync());
        Assert.Equal(CacheType.Private, response2.GetCacheType());
    }

    [Fact]
    public async Task Send_Private_Vary_MissingHeader()
    {
        // Given
        var request1 = new HttpRequestMessage(HttpMethod.Get, RequestUri)
        {
            Headers =
            {
                Authorization = new("Bearer", CreateJwt("userId")),
                AcceptLanguage = { new("da") },
            },
        };
        var request2 = new HttpRequestMessage(HttpMethod.Get, RequestUri)
        {
            Headers = { Authorization = new("Bearer", CreateJwt("userId")) },
        };

        _nextMock.SetupSendAsync(
            x => x == request1,
            () =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request1,
                    Content = new StringContent("Hej"),
                    Headers = { Vary = { "Accept-Language" } },
                }
        );
        _nextMock.SetupSendAsync(
            x => x == request2,
            () =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request2,
                    Content = new StringContent("Hello"),
                    Headers = { Vary = { "Accept-Language" } }, // Sends Vary header even when the header is not in the request
                }
        );

        // When
        using var _1 = await _handler.SendAsync(request1);
        using var _2 = await _handler.SendAsync(request2);
        using var response1 = await _handler.SendAsync(request1);
        using var response2 = await _handler.SendAsync(request2);
        using var response1b = await _handler.SendAsync(request1);
        using var response2b = await _handler.SendAsync(request2);

        // Then
        _nextMock.Verify();

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal("Hej", await response1.ReadAsStringAsync());
        Assert.Equal(CacheType.Private, response1.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("Hello", await response2.ReadAsStringAsync());
        Assert.Equal(CacheType.Private, response2.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response1b.StatusCode);
        Assert.Equal("Hej", await response1b.ReadAsStringAsync());
        Assert.Equal(CacheType.Private, response1b.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response2b.StatusCode);
        Assert.Equal("Hello", await response2b.ReadAsStringAsync());
        Assert.Equal(CacheType.Private, response2b.GetCacheType());
    }

    [Fact]
    public async Task Send_WithAuthorizationHeaderButResponseIsPublic()
    {
        // Given
        var request = new HttpRequestMessage(HttpMethod.Get, RequestUri)
        {
            Headers = { Authorization = new("Bearer", CreateJwt("userId")) },
        };

        _nextMock.SetupSendAsync(
            x => x == request,
            () =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent("Hello"),
                    Headers = { CacheControl = new() { Public = true } },
                }
        );

        // When
        using var _ = await _handler.SendAsync(request);
        using var response = await _handler.SendAsync(request);

        // Then
        _nextMock.Verify();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Hello", await response.ReadAsStringAsync());
        Assert.Equal(CacheType.Shared, response.GetCacheType());
    }

    [Fact]
    public async Task Send_ShouldRevalidateWhenRequested_NotModified()
    {
        // Given
        var request1 = new HttpRequestMessage(HttpMethod.Get, RequestUri);
        var request2 = new HttpRequestMessage(HttpMethod.Get, RequestUri);

        _nextMock.SetupSendAsync(
            x => x == request1,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request1,
                Content = new StringContent("Hello"),
                Headers = { CacheControl = new() { MustRevalidate = true } },
            }
        );
        _nextMock.SetupSendAsync(
            x => x == request2,
            new HttpResponseMessage(HttpStatusCode.NotModified)
            {
                RequestMessage = request2,
                Headers = { CacheControl = new() { MustRevalidate = true } },
            }
        );

        // When
        using var _ = await _handler.SendAsync(request1);
        using var response = await _handler.SendAsync(request2);

        // Then
        _nextMock.Verify();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Hello", await response.ReadAsStringAsync());
        Assert.Equal(CacheType.Shared, response.GetCacheType());
    }

    [Fact]
    public async Task Send_ShouldRevalidateWhenRequested_ModifiedCannotReplaceCacheEntry()
    {
        // Given
        var request1 = new HttpRequestMessage(HttpMethod.Get, RequestUri);
        var request2 = new HttpRequestMessage(HttpMethod.Get, RequestUri);
        var request3 = new HttpRequestMessage(HttpMethod.Get, RequestUri);

        _nextMock.SetupSendAsync(
            x => x == request1,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request1,
                Content = new StringContent("Hello"),
                Headers = { CacheControl = new() { MustRevalidate = true } },
            }
        );
        _nextMock.SetupSendAsync(
            x => x == request2,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request2,
                Content = new StringContent("World"),
                Headers = { CacheControl = new() { MustRevalidate = true } },
            }
        );
        _nextMock.SetupSendAsync(
            x => x == request3,
            new HttpResponseMessage(HttpStatusCode.NotModified)
            {
                RequestMessage = request3,
                Headers = { CacheControl = new() { MustRevalidate = true } },
            }
        );

        // When
        using var response1 = await _handler.SendAsync(request1);
        // Time is not advanced here, so the modified time of the response file is the same as request1
        using var response2 = await _handler.SendAsync(request2);
        using var response3 = await _handler.SendAsync(request3);

        // Then
        _nextMock.Verify();

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal("Hello", await response1.ReadAsStringAsync());
        Assert.Equal(CacheType.None, response1.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("World", await response2.ReadAsStringAsync());
        Assert.Equal(CacheType.None, response2.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
        Assert.Equal("Hello", await response3.ReadAsStringAsync()); // Find the first response as we were unable to replace
        Assert.Equal(CacheType.Shared, response3.GetCacheType());
    }

    [Fact]
    public async Task Send_ShouldRevalidateWhenRequested_ModifiedReplaceCacheEntry()
    {
        // Given
        var request1 = new HttpRequestMessage(HttpMethod.Get, RequestUri);
        var request2 = new HttpRequestMessage(HttpMethod.Get, RequestUri);
        var request3 = new HttpRequestMessage(HttpMethod.Get, RequestUri);

        _nextMock.SetupSendAsync(
            x => x == request1,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request1,
                Content = new StringContent("Hello"),
                Headers = { CacheControl = new() { MustRevalidate = true } },
            }
        );
        _nextMock.SetupSendAsync(
            x => x == request2,
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request2,
                Content = new StringContent("World"),
                Headers = { CacheControl = new() { MustRevalidate = true } },
            }
        );
        _nextMock.SetupSendAsync(
            x => x == request3,
            new HttpResponseMessage(HttpStatusCode.NotModified)
            {
                RequestMessage = request3,
                Headers = { CacheControl = new() { MustRevalidate = true } },
            }
        );

        // When
        using var response1 = await _handler.SendAsync(request1);
        _timeProvider.Advance(TimeSpan.FromSeconds(1)); // Ensure that the modified time is different
        using var response2 = await _handler.SendAsync(request2);
        using var response3 = await _handler.SendAsync(request3);

        // Then
        _nextMock.Verify();

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal("Hello", await response1.ReadAsStringAsync());
        Assert.Equal(CacheType.None, response1.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("World", await response2.ReadAsStringAsync());
        Assert.Equal(CacheType.None, response2.GetCacheType());

        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
        Assert.Equal("World", await response3.ReadAsStringAsync());
        Assert.Equal(CacheType.Shared, response3.GetCacheType());
    }

    private static string CreateJwt(string userId)
    {
        var tokenHandler = new JwtSecurityTokenHandler();

        var token = tokenHandler.CreateJwtSecurityToken(
            subject: new ClaimsIdentity([new Claim("sub", userId)])
        );

        return tokenHandler.WriteToken(token);
    }
}
