using System.Text;

namespace HttpClientCache.Tests;

public class JwtDecoderTests
{
    [Fact]
    public void TryDecodePayload_ValidJwt_ReturnsPayload()
    {
        // {"alg":"none"} . {"sub":"alice"} . (empty signature)
        var token = "eyJhbGciOiJub25lIn0." + Base64UrlEncode("{\"sub\":\"alice\"}") + ".";

        Assert.True(JwtDecoder.TryDecodePayload(token, out var payload));
        using (payload)
        {
            Assert.Equal("alice", payload!.RootElement.GetProperty("sub").GetString());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("header.@@@not-base64@@@.signature")]
    [InlineData("header..signature")] // empty payload segment
    public void TryDecodePayload_Invalid_ReturnsFalse(string token)
    {
        Assert.False(JwtDecoder.TryDecodePayload(token, out var payload));
        Assert.Null(payload);
    }

    [Fact]
    public void TryDecodePayload_NonObjectPayload_ReturnsFalse()
    {
        // Payload is a JSON array, not an object.
        var token = "eyJhbGciOiJub25lIn0." + Base64UrlEncode("[1,2,3]") + ".";

        Assert.False(JwtDecoder.TryDecodePayload(token, out var payload));
        Assert.Null(payload);
    }

    private static string Base64UrlEncode(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
