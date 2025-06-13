namespace HttpClient.Cache;

public sealed class Variation : IEquatable<Variation>, ICacheEntry
{
    public required CacheType CacheType { get; init; }

    public required List<string> NormalizedVaryHeaders { get; init; }

    public static Variation FromResponseMessage(HttpResponseMessage response)
    {
        var request = response.RequestMessage!;
        var type = GetCacheType(request, response);
        var normalizedVaryHeaders = response
            .Headers.Vary.Select(x => x.ToLowerInvariant())
            .Order(StringComparer.Ordinal)
            .ToList();
        return new Variation() { CacheType = type, NormalizedVaryHeaders = normalizedVaryHeaders };
    }

    private static CacheType GetCacheType(HttpRequestMessage request, HttpResponseMessage response)
    {
        // https://datatracker.ietf.org/doc/html/rfc7234#section-3

        // The request method is understood by the cache and defined as being cacheable
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            return CacheType.None;
        }

        // the response status code is understood by the cache
        if (!response.IsSuccessStatusCode)
        {
            return CacheType.None;
        }

        var requestCacheControl = request.Headers.CacheControl;
        var responseCacheControl = response.Headers.CacheControl;

        // the "no-store" cache directive does not appear in request or response header fields
        if (requestCacheControl?.NoStore == true || responseCacheControl?.NoStore == true)
        {
            return CacheType.None;
        }

        // the "private" response directive does not appear in the response, if the cache is shared
        if (responseCacheControl?.Private == true)
        {
            return CacheType.Private;
        }

        // the Authorization header field does not appear in the request, if the cache is shared, unless the response explicitly allows it
        if (request.Headers.Authorization is not null && responseCacheControl?.Public != true)
        {
            return CacheType.Private;
        }

        return CacheType.Shared;
    }

    public bool Equals(Variation? other) =>
        other is not null
        && CacheType == other.CacheType
        && NormalizedVaryHeaders.SequenceEqual(other.NormalizedVaryHeaders, StringComparer.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as Variation);

    public override int GetHashCode() => HashCode.Combine(CacheType, NormalizedVaryHeaders);
}
