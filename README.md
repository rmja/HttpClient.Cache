# HttpClient.Cache

[![CI](https://github.com/rmja/HttpClient.Cache/actions/workflows/ci.yaml/badge.svg)](https://github.com/rmja/HttpClient.Cache/actions/workflows/ci.yml)
[![HttpClient.Cache](https://img.shields.io/nuget/vpre/HttpClient.Cache.svg)](https://www.nuget.org/packages/HttpClient.Cache)

HTTP/1.1 compliant caching layer for the .NET `HttpClient`.
A prerelease package is availble on nuget.

It supports file based caching of responses based on the HTTP/1.1 caching headers specified in [RFC7234](https://tools.ietf.org/html/rfc7234).

## Features

- Pluggable caching for `HttpClient`
- Customizable cache key computation (supports both shared and private caching)
- Extensible cache entry and cache handler interfaces (defaults to file based caching)

## Getting Started

### Installation

Add the package reference to your project:

```sh
dotnet add package HttpClient.Cache
```

### Usage

1. Register the cache handler and related services on your `HttpClient`.
2. Use the `HttpClient` as usual.

Note that all uses of the cache goes via `HttpClient`, so caching can easily be obtained using e.g generated clients such as [`Refit`](https://github.com/reactiveui/refit) clients.

#### Example

```csharp
// Register the caching client on a HttpClient in your Program.cs
services.AddHttpClient("cachedClient")
        .AddResponseCache(); // Defaults to using the shared, file based cache.

// Use the client as normally
var client = httpClientFactory.CreateClient("cachedClient");

var firstResponse = await client.GetAsync("https://example.com/"); // Returns a "non-private" Cache-Control header
var secondResponse = await client.GetAsync("https://example.com/");
Assert.Equal(CacheType.None, firstResponse.GetCacheType()); // This response was not obtained from cache.
Assert.Equal(CacheType.Shared, secondResponse.GetCacheType()); // This response was obtained from the shared (not private) cache.
```

Note that the usage here is different compared to e.g. [Replicant](https://github.com/SimonCropp/Replicant).

#### Custom Caches
It is possible to use a custom cache for a client. Consider the following example:

```csharp

var cache = new FileCache("/some/cache/directory");
services.AddHttpClient("cachedClient")
        .AddResponseCache(cache);
```

`FileCache` implements `IHttpCache` and this is an extension point if one wants to use a different, non-file based cache implementation.

## Private Cache Handling

`HttpClient.Cache` supports the concept of private caches, ensuring that cached responses are only reused for the same user or context that originally requested them.
This is particularly important for scenarios where responses may contain user-specific or sensitive data.

### How Private Caches Work

- **Detection:**  
  The library inspects HTTP response headers such as `Cache-Control: private` to determine if a response should be stored in a private cache.

- **Isolation:**  
  Private cache entries are isolated based on a user identifier computed from the request. This prevents one user's cached data from being served to another user.

- **Retrieval:**  
  When a request is made, the cache handler checks for a matching private cache entry using the current user's context. Only if a valid private entry exists for that user will it be returned.

### Example

If a request have an `Authorization` header with a JWT and a response contains the header `Cache-Control: private`, the cache handler will:

1. Compute a cache key that includes the user identifier - since the `Authorization` header is a JWT, it will use the `sub` or `client_id` claim from the token. This behavior can be changed to any other header or set of claims by using a different [`ICacheKeyComputer`](src/HttpClient.Cache/ICacheKeyComputer.cs) other than the default [`CacheKeyComputer`](src/HttpClient.Cache/CacheKeyComputer.cs).
2. Store the response in a location computed from the computed cache key.
3. On subsequent requests, retrieve the cached response only if the same cache key is computed.

This approach ensures compliance with HTTP caching semantics and protects user privacy by preventing cross-user cache leaks.
The mechanism by deriving the `sub`/`client_id` allows for using the cache even if the JWT token for the same user is renewed.

## Response Variation Handling

`HttpClient.Cache` fully supports the HTTP `Vary` header, which instructs caches to store and serve different versions of a response based on specified request headers. This ensures that clients receive the correct cached response variant according to their request characteristics.

### How Vary Headers Are Handled

- **Detection:**  
  When a response includes a `Vary` header (e.g., `Vary: Accept-Encoding, User-Agent`), the cache handler records which request headers affect the cache entry.

- **Cache Key Computation:**  
  The cache key is computed by combining the request URI with the values of all headers listed in the `Vary` header. This means each unique combination of those header values results in a separate cache entry.

- **Storage:**  
  Each variant is stored independently, ensuring that responses tailored for different header values (such as language, encoding, or user agent) are not mixed.

- **Retrieval:**  
  On subsequent requests, the cache handler checks the current request’s headers against the `Vary` criteria and retrieves the correct cached variant if available.

### Example

If a server responds with:

```
Vary: Accept-Language
```

The cache will store separate entries for each value of the `Accept-Language` header (e.g., `en-US`, `da-DK`). When a client requests the same resource with a different `Accept-Language`, the appropriate cached variant is served.

This mechanism ensures compliance with HTTP caching standards and delivers accurate, context-sensitive responses from the cache.

## Key Components

- [`CacheHandler`](src/HttpClient.Cache/CacheHandler.cs): HTTP message handler that manages caching logic. This handler can be registered on a `HttpClient` as a message handler layer.
- [`IHttpCache`](src/HttpClient.Cache/IHttpCache.cs): Interface for cache storage implementations. The default file based implementation is [`FileCache`](src/HttpClient.Cache/Files/FileCache.cs).

## Alternatives

* [Replicant](https://github.com/SimonCropp/Replicant)

## License

This project is licensed under the [MIT License](LICENSE).

## Contributing

Contributions are welcome! Please open issues or submit pull requests for improvements and bug fixes.
