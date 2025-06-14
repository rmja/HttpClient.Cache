namespace HttpClientCache;

public sealed record ResponseWithVariation(HttpResponseMessage Response, Variation Variation)
    : IDisposable
{
    public void Dispose() => Response.Dispose();
}
