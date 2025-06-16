namespace HttpClientCache.Files;

internal class VariationModel
{
    public required string Key { get; init; } = string.Empty;
    public required CacheType CacheType { get; init; }
    public required List<string> NormalizedVaryHeaders { get; init; }
}
