# HttpClient.Cache

HTTP/1.1 compliant caching layer for the .NET `HttpClient`.
A prerelease package is availble on nuget [![HttpClient.Cache](https://img.shields.io/nuget/vpre/HttpClient.Cache.svg)](https://www.nuget.org/packages/HttpClient.Cache)

It supports file based caching of responses based on the HTTP/1.1 caching headers specified in [RFC7234](https://tools.ietf.org/html/rfc7234).

## Features

- Pluggable caching for `HttpClient`
- Customizable cache key computation (supports both shared and private caching)
- Extensible cache entry and cache handler interfaces (defaults to file based caching)
- Easy integration with dependency injection

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

### FileCache Configuration
The `FileCache` class implements persistent caching by storing HTTP responses as files on disk. Below are the key properties of `FileCache` that can be used to for configuration:


- **DefaultInitialExpiration**  
  The default duration for which a cached entry remains valid _after it is first seen_ if no explicit expiration is set in the response.

- **DefaultRefreshExpiration**  
  The default duration for which a cached entry remains valid _when it is seen again_ if no explicit expiration is set in the response.

- **MaxEntries**  
  The maximum number of cache entries allowed in the cache directory.
  If the number of cached files exceeds this value, the cache will evict older or least-used entries to maintain the limit.


## Key Components

- [`CacheHandler`](src/HttpClient.Cache/CacheHandler.cs): HTTP message handler that manages caching logic. This handler can be registered on a `HttpClient` as a message handler layer.
- [`IHttpCache`](src/HttpClient.Cache/IHttpCache.cs): Interface for cache storage implementations. The default file based implementation is [`FileCache`](src/HttpClient.Cache/Files/FileCache.cs).

## Alternatives

* [Replicant](https://github.com/SimonCropp/Replicant)

## License

This project is licensed under the [MIT License](LICENSE).

## Contributing

Contributions are welcome! Please open issues or submit pull requests for improvements and bug fixes.
