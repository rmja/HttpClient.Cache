namespace HttpClientCache;

public enum CacheType
{
    // No cahe
    None,

    // Shared cache, can be used by multiple clients
    Shared,

    // Private cache, can only be used by the client that made the request
    Private,
}
