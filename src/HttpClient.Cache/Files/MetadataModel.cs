using System.Net;

namespace HttpClientCache.Files;

public record MetadataModel
{
    // NOTE: The computed cache keys (response key / variation key) are intentionally
    // NOT persisted here. For private entries the response key embeds a user identifier
    // that, for non-JWT credentials, can be the raw Authorization header value. Writing it
    // to disk would leak the credential in plaintext, and the keys were never read back.
    // The hashed key lives only in the file name; the URL below is kept for inspection.
    public required Uri Url { get; init; }
    public required Version Version { get; init; }
    public required HttpStatusCode StatusCode { get; init; }
    public required string? ReasonPhrase { get; init; }
    public required List<KeyValuePair<string, List<string>>> ResponseHeaders { get; init; }
    public required List<KeyValuePair<string, List<string>>> ContentHeaders { get; init; }
    public required List<KeyValuePair<string, List<string>>>? TrailingHeaders { get; init; }
}
