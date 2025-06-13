namespace HttpClient.Cache;

internal static class CacheConstants
{
    public static readonly HttpRequestOptionsKey<CacheType> CacheTypeOptionKey = new(
        "HttpClient.Cache.CacheType"
    );
}
