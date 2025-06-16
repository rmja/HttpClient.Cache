namespace HttpClientCache.Files;

internal class VariationModel
{
    public required string VariationKey { get; init; }
    public required CacheType CacheType { get; init; }
    public required List<string> NormalizedVaryHeaders { get; init; }
}
