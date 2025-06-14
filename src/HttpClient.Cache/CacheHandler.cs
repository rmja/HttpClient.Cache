using System.Net;
using Microsoft.Extensions.Logging;

namespace HttpClient.Cache;

/// <summary>
/// HTTP/1.1 caching compliant <see cref="System.Net.Http.HttpClient"/> message handler.
/// See https://tools.ietf.org/html/rfc7234 for details on HTTP caching.
/// </summary>
public class CacheHandler(
    IHttpCache cache,
    ICacheKeyComputer cacheKeyProvider,
    ILogger<CacheHandler> logger
) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        CacheHit? cacheHit = null;
        var entryKey = cacheKeyProvider.ComputeKey(request, variation: null);

        if (CanServeFromCache(request) && entryKey is not null)
        {
            cacheHit = await LookupResponseInCacheAsync(entryKey, request, cancellationToken);

            if (cacheHit is not null)
            {
                var cachedResponse = cacheHit.ResponseMessage;
                if (cachedResponse.Headers.CacheControl?.MustRevalidate == true)
                {
                    // Set appropriate conditional request headers

                    if (cachedResponse.Headers.ETag is not null)
                    {
                        request.Headers.IfNoneMatch.Add(cachedResponse.Headers.ETag);
                    }
                    else if (cachedResponse.Content.Headers.LastModified is not null)
                    {
                        request.Headers.IfModifiedSince = cachedResponse
                            .Content
                            .Headers
                            .LastModified;
                    }
                }
                // rfc7234 §4 the stored response does not contain the no-cache cache directive
                else if (cachedResponse.Headers.CacheControl?.NoCache != true)
                {
                    await cache.RefreshResponseAsync(cacheHit.ResponseKey, cancellationToken);

                    logger.LogInformation(
                        "Serving not validated, possibly stall response {HttpMethod} {RequestPath}",
                        request.Method,
                        request.RequestUri
                    );

                    request.Options.Set(CacheConstants.CacheTypeOptionKey, cacheHit.CacheType);
                    return cachedResponse;
                }
            }
        }

        // Forward the request to the inner handler
        var response = await base.SendAsync(request, cancellationToken);

        if (cacheHit is not null)
        {
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                await cache.RefreshResponseAsync(
                    cacheHit.ResponseKey,
                    notModifiedResponse: response,
                    cancellationToken
                );

                // We are done with the received response as we plan to serve the cached response instead
                response.Dispose();

                logger.LogInformation(
                    "Cached response is not modified {HttpMethod} {RequestPath}",
                    request.Method,
                    request.RequestUri
                );

                request.Options.Set(CacheConstants.CacheTypeOptionKey, cacheHit.CacheType);
                return cacheHit.ResponseMessage;
            }
            else
            {
                // The response was modified so the cache hit is now obsolete
                cacheHit.ResponseMessage.Dispose();
            }
        }

        if (entryKey is not null)
        {
            var variation = Variation.FromResponseMessage(response);
            if (
                variation.CacheType == CacheType.Shared
                && variation.NormalizedVaryHeaders.Count == 0
            )
            {
                // No Vary header, cache the response directly using the entry key

                var cachedResponse = await cache.SetResponseAsync(
                    entryKey,
                    response,
                    cancellationToken
                );
                cachedResponse.RequestMessage = request;

                // Replace the response with the cached response that has a reset content stream
                response.Dispose();
                response = cachedResponse;
            }
            else if (
                variation.CacheType == CacheType.Private
                || variation.NormalizedVaryHeaders.Count > 0
            )
            {
                var responseKey = cacheKeyProvider.ComputeKey(request, variation);
                if (responseKey is not null)
                {
                    // We are done with the received reponse - we will be serving a cached response
                    // Store the cached response in the unique vary key
                    var cachedResponse = await cache.SetResponseAsync(
                        responseKey,
                        response,
                        variationKey: entryKey,
                        variation,
                        cancellationToken
                    );
                    cachedResponse.RequestMessage = request;

                    // Replace the response with the cached response that has a reset content stream
                    response.Dispose();
                    response = cachedResponse;
                }
            }
        }

        return response;
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

    record CacheHit(string ResponseKey, HttpResponseMessage ResponseMessage, CacheType CacheType);

    private async Task<CacheHit?> LookupResponseInCacheAsync(
        string entryKey,
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        logger.LogTrace("Request {RequestUri} can be served from cache.", request.RequestUri);

        var entry = await cache.GetAsync(entryKey, cancellationToken);
        return await entry.Match(
            response =>
            {
                logger.LogTrace(
                    "Cached response was found for {RequestUri} using entry key {EntryKey}.",
                    request.RequestUri,
                    entryKey
                );

                // The cached response did not have a Vary header and was not private

                response.RequestMessage = request;
                return ValueTask.FromResult<CacheHit?>(new(entryKey, response, CacheType.Shared));
            },
            async variation =>
            {
                logger.LogTrace("Cached variation was found for {RequestUri}.", request.RequestUri);

                // Regenerate an exact cache key from the cached vary by rules and header values in the request
                var responseKey = cacheKeyProvider.ComputeKey(request, variation);
                if (responseKey is not null)
                {
                    entry = await cache.GetAsync(responseKey, cancellationToken);
                    if (entry.TryGetResponse(out var response))
                    {
                        logger.LogTrace(
                            "Cached response was found for {RequestUri} using response key {ResponseKey}.",
                            request.RequestUri,
                            responseKey
                        );

                        // The previous response had a Vary header, and the request headers and the cached response had equal header values
                        response.RequestMessage = request;
                        return new(responseKey, response, variation.CacheType);
                    }
                    else
                    {
                        logger.LogTrace(
                            "No response cache hit was found for request {RequestUri} using response key {ResponseKey}.",
                            request.RequestUri,
                            responseKey
                        );
                    }
                }

                return null;
            },
            notFound =>
            {
                logger.LogTrace(
                    "No cache entry was found for request {RequestUri} using entry key {EntryKey}.",
                    request.RequestUri,
                    entryKey
                );
                return ValueTask.FromResult<CacheHit?>(null);
            }
        );
    }
}
