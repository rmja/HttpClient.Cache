namespace HttpClient.Cache;

public sealed record ResponseWithVariation(HttpResponseMessage Response, Variation Variation)
    : IDisposable
{
    public void Dispose() => Response.Dispose();
}
