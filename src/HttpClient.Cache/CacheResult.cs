using OneOf;
using OneOf.Types;

namespace HttpClient.Cache;

[GenerateOneOf]
public partial class CacheResult : OneOfBase<Response, Variation, NotFound>
{
    public Response AsResponse => AsT0;
    public Variation AsVariation => AsT1;
    public bool Exists => !IsT2;

    public bool TryGetResponse(out Response response) => TryPickT0(out response, out _);
}
