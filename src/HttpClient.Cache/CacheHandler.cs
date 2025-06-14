using System.Net;
using Microsoft.Extensions.Logging;

namespace HttpClient.Cache;

/// <summary>
/// HTTP/1.1 caching compliant <see cref="System.Net.Http.HttpClient"/> message handler.
/// See https://tools.ietf.org/html/rfc7234 for details on HTTP caching.
/// </summary>
public class CacheHandler(IHttpCache cache, ILogger<CacheHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        HttpResponseMessage? foundResponse = null;
        CacheType foundType = CacheType.None;

        if (CanServeFromCache(request))
        {
            var result = await cache.GetWithVariationAsync(request, cancellationToken);
            if (result is not null)
            {
                foundResponse = result.Response;
                foundType = result.Variation.CacheType;

                logger.LogTrace("Cached response was found for {RequestUri}.", request.RequestUri);

                if (foundResponse.Headers.CacheControl?.MustRevalidate == true)
                {
                    // Set appropriate conditional request headers

                    if (foundResponse.Headers.ETag is not null)
                    {
                        request.Headers.IfNoneMatch.Add(foundResponse.Headers.ETag);
                    }
                    else if (foundResponse.Content.Headers.LastModified is not null)
                    {
                        request.Headers.IfModifiedSince = foundResponse
                            .Content
                            .Headers
                            .LastModified;
                    }
                }
                // rfc7234 §4 the stored response does not contain the no-cache cache directive
                else if (foundResponse.Headers.CacheControl?.NoCache != true)
                {
                    await cache.RefreshResponseAsync(foundResponse, cancellationToken);

                    logger.LogInformation(
                        "Serving not validated, possibly stall response {HttpMethod} {RequestPath}",
                        request.Method,
                        request.RequestUri
                    );

                    request.Options.Set(CacheConstants.CacheTypeOptionKey, foundType);
                    return foundResponse;
                }
            }
        }

        // Forward the request to the inner handler
        var webResponse = await base.SendAsync(request, cancellationToken);

        if (foundResponse is not null)
        {
            if (webResponse.StatusCode == HttpStatusCode.NotModified)
            {
                //await cache.RefreshResponseAsync(notModifiedResponse: response, cancellationToken);

                // We are done with the received response as we plan to serve the cached response instead
                webResponse.Dispose();

                logger.LogInformation(
                    "Cached response is not modified {HttpMethod} {RequestPath}",
                    request.Method,
                    request.RequestUri
                );

                request.Options.Set(CacheConstants.CacheTypeOptionKey, foundType);
                return foundResponse;
            }
            else
            {
                // The response was modified so the cache hit is now obsolete
                foundResponse.Dispose();
            }
        }

        var storedResponse = await cache.SetResponseAsync(webResponse, cancellationToken);
        if (storedResponse is not null)
        {
            // We are done with the received reponse - we will be serving a cached response
            webResponse.Dispose();

            return storedResponse;
        }

        return webResponse;
    }

    private static bool CanServeFromCache(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            return false;
        }

        // See https://docs.microsoft.com/en-us/aspnet/core/performance/caching/response

        var cacheControl = request.Headers.CacheControl;
        if (cacheControl is null)
        {
            // https://datatracker.ietf.org/doc/html/rfc7234#section-2
            // Although caching is an entirely OPTIONAL feature
            // of HTTP, it can be assumed that reusing a cached response is
            // desirable and that such reuse is the default behavior when no
            // requirement or local configuration prevents it.

            return true;
        }
        else
        {
            // https://datatracker.ietf.org/doc/html/rfc7234#section-4
            if (cacheControl.NoCache)
            {
                return false;
            }

            return true;
        }
    }
}
