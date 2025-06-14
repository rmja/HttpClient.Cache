using System.Net;

namespace HttpClientCache.Files;

public record Metadata
{
    public required Uri Url { get; init; }
    public required Version Version { get; init; }
    public required HttpStatusCode StatusCode { get; init; }
    public required string? ReasonPhrase { get; init; }
    public required List<KeyValuePair<string, List<string>>> ResponseHeaders { get; init; }
    public required List<KeyValuePair<string, List<string>>> ContentHeaders { get; init; }
    public required List<KeyValuePair<string, List<string>>>? TrailingHeaders { get; init; }
    public required string? Etag { get; init; }
    public required DateTimeOffset? LastModified { get; init; }
}
