namespace HttpClientCache.Files;

internal class VariationModel
{
    // The variation key is intentionally not persisted (see MetadataModel). The hashed
    // key lives in the file name; the URL is kept only for inspection/debugging.
    public required Uri Url { get; init; }
    public required CacheType CacheType { get; init; }
    public required List<string> NormalizedVaryHeaders { get; init; }
}
