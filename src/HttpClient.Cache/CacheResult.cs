using OneOf;
using OneOf.Types;

namespace HttpClient.Cache;

[GenerateOneOf]
public partial class CacheResult : OneOfBase<HttpResponseMessage, Variation, NotFound>
{
    public HttpResponseMessage AsResponse => AsT0;
    public Variation AsVariation => AsT1;
    public bool Exists => !IsT2;

    public bool TryGetResponse(out HttpResponseMessage response) => TryPickT0(out response, out _);
}
