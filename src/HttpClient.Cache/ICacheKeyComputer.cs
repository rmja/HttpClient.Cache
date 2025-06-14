namespace HttpClientCache;

public interface ICacheKeyComputer
{
    /// <summary>
    /// Compute a cache key for a given request and variation.
    /// </summary>
    /// <param name="request">The request</param>
    /// <param name="variation">The variation that must be used to differentiate the computed key</param>
    /// <returns></returns>
    string? ComputeKey(HttpRequestMessage request, Variation variation);
}
