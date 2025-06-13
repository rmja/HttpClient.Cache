using System.Net;
using System.Net.Http.Headers;

namespace HttpClient.Cache;

public sealed class Response : ICacheEntry, IAsyncDisposable, IDisposable
{
    private bool _responseMessageObtained = false;

    public required Uri Url { get; init; }
    public required Version Version { get; init; }
    public required HttpStatusCode StatusCode { get; init; }
    public required string? ReasonPhrase { get; init; }
    public required List<KeyValuePair<string, List<string>>> Headers { get; init; }
    public required List<KeyValuePair<string, List<string>>> ContentHeaders { get; init; }
    public required List<KeyValuePair<string, List<string>>>? TrailingHeaders { get; init; }

    public required EntityTagHeaderValue? Etag { get; init; }
    public required TimeSpan? MaxAge { get; init; }
    public required DateTimeOffset? LastModified { get; init; }

    public required Stream ContentStream { get; init; }

    public HttpResponseMessage ToResponseMessage(HttpRequestMessage request)
    {
        if (_responseMessageObtained)
        {
            throw new InvalidOperationException("Response message has already been obtained.");
        }

        var response = new HttpResponseMessage()
        {
            RequestMessage = request,
            Version = Version,
            StatusCode = StatusCode,
            ReasonPhrase = ReasonPhrase,
            Content = new StreamContent(ContentStream),
        };

        foreach (var header in Headers)
        {
            response.Headers.TryAddWithoutValidation(header.Key, header.Value.AsEnumerable());
        }

        foreach (var header in ContentHeaders)
        {
            response.Content.Headers.TryAddWithoutValidation(
                header.Key,
                header.Value.AsEnumerable()
            );
        }

        if (TrailingHeaders is not null)
        {
            foreach (var header in TrailingHeaders)
            {
                response.TrailingHeaders.TryAddWithoutValidation(
                    header.Key,
                    header.Value.AsEnumerable()
                );
            }
        }

        _responseMessageObtained = true;
        return response;
    }

    public ValueTask DisposeAsync()
    {
        if (_responseMessageObtained)
        {
            return ValueTask.CompletedTask;
        }

        return ContentStream.DisposeAsync();
    }

    public void Dispose()
    {
        if (_responseMessageObtained)
        {
            return;
        }

        ContentStream.Dispose();
    }
}
