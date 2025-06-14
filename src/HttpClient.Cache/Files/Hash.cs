using System.Security.Cryptography;
using System.Text;

namespace HttpClientCache.Files;

internal static class Hash
{
    public static string ComputeHash(string value)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(hash);
    }
}
