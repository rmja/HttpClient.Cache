using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace HttpClientCache.Tests;

public class CacheKeyComputerTests
{
    private const string Url = "https://example.com/resource";

    private readonly CacheKeyComputer _computer = new();

    [Fact]
    public void PrivateKey_UsesSubClaim()
    {
        var request = BearerRequest(CreateJwt(new Claim("sub", "alice")));

        var key = _computer.ComputeKey(request, new(CacheType.Private));

        Assert.NotNull(key);
        Assert.Contains("sub:alice", key);
    }

    [Fact]
    public void PrivateKey_UsesClientIdClaim_WhenNoSub()
    {
        var request = BearerRequest(CreateJwt(new Claim("client_id", "app1")));

        var key = _computer.ComputeKey(request, new(CacheType.Private));

        Assert.NotNull(key);
        Assert.Contains("client_id:app1", key);
    }

    [Fact]
    public void PrivateKey_DoesNotLeakRawToken_ForJwt()
    {
        var request = BearerRequest(CreateJwt(new Claim("sub", "alice")));
        var rawToken = request.Headers.Authorization!.Parameter!;

        var key = _computer.ComputeKey(request, new(CacheType.Private))!;

        Assert.DoesNotContain(rawToken, key);
    }

    [Fact]
    public void PrivateKey_FallsBackToRawHeader_ForNonJwt()
    {
        var request = BearerRequest("raw-api-key");

        var key = _computer.ComputeKey(request, new(CacheType.Private));

        Assert.NotNull(key);
    }

    [Fact]
    public void PrivateKey_IsNull_ForNonJwt_WhenRequireJwtToken()
    {
        _computer.RequireJwtToken = true;
        var request = BearerRequest("raw-api-key");

        var key = _computer.ComputeKey(request, new(CacheType.Private));

        Assert.Null(key);
    }

    [Fact]
    public void SharedAndPrivateKeys_DifferForSameRequest()
    {
        var request = BearerRequest(CreateJwt(new Claim("sub", "alice")));

        var sharedKey = _computer.ComputeKey(request, new(CacheType.Shared));
        var privateKey = _computer.ComputeKey(request, new(CacheType.Private));

        Assert.NotEqual(sharedKey, privateKey);
    }

    private static HttpRequestMessage BearerRequest(string token) =>
        new(HttpMethod.Get, Url) { Headers = { Authorization = new("Bearer", token) } };

    private static string CreateJwt(params Claim[] claims)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateJwtSecurityToken(subject: new ClaimsIdentity(claims));
        return handler.WriteToken(token);
    }
}
