using System.Net;
using System.Reflection;

namespace HttpClientCache.Files;

/// <summary>
/// Provides a file-based implementation of <see cref="IHttpCache"/> for persistent HTTP response caching on disk.
/// </summary>
public class FileCache : IHttpCache
{
    private readonly DirectoryInfo _rootDirectory;
    private readonly DirectoryInfo _tempDirectory;
    private readonly ICacheKeyComputer _cacheKeyComputer;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _timer;

    public static FileCache Default { get; } = new();

    public DirectoryInfo RootDirectory => _rootDirectory;

    /// <summary>
    /// Gets or sets the maximum number of cache entries allowed in the cache directory.
    /// Note that this is a soft limit, and the actual number of entries may exceed this value temporarily during purging.
    /// </summary>
    public int MaxEntries { get; set; } = 1000;

    /// <summary>
    /// The default duration for which a cached entry remains valid <i>after it is first seen</i> if no explicit expiration is set in the response.
    /// </summary>
    public TimeSpan DefaultInitialExpiration { get; set; } = TimeSpan.FromDays(2);

    /// <summary>
    /// The default duration for which a cached entry remains valid <i>when it is seen again</i> if no explicit expiration is set in the response.
    /// </summary>
    public TimeSpan DefaultRefreshExpiration { get; set; } = TimeSpan.FromDays(2);

    /// <summary>
    /// Creates a new instance of <see cref="FileCache"/> with the default root directory.
    /// </summary>
    public FileCache()
        : this(
            Path.Combine(
                Path.GetTempPath(),
                "HttpClient.FileCache",
                Assembly.GetEntryAssembly()!.GetName().Name!
            )
        ) { }

    /// <summary>
    /// Creates a new instance of <see cref="FileCache"/> with the specified root directory.
    /// </summary>
    /// <param name="rootDirectory">The directory where cache files are stored.</param>
    public FileCache(string rootDirectory)
        : this(rootDirectory, new CacheKeyComputer(), TimeProvider.System) { }

    /// <summary>
    /// Creates a new instance of <see cref="FileCache"/>.
    /// </summary>
    /// <param name="rootDirectory">The directory where cache files are stored</param>
    /// <param name="cacheKeyComputer">The mechanism used to provide cache keys for a given request</param>
    /// <param name="timeProvider">The time provider</param>
    public FileCache(
        string rootDirectory,
        ICacheKeyComputer cacheKeyComputer,
        TimeProvider timeProvider
    )
    {
        _cacheKeyComputer = cacheKeyComputer;
        _timeProvider = timeProvider;

        _rootDirectory = new(rootDirectory);
        if (!_rootDirectory.Exists)
        {
            _rootDirectory.Create();
        }

        // 1) Ensure temp directory is separate to avoid conflicts
        // 2) Let temp files be on the same file system for performance and atomicity during file moves
        // 3) Allow for easier cleanup as the temp directory can be traversed when recursively deleting files in the root directory
        _tempDirectory = new(Path.Combine(rootDirectory, "temp"));
        if (!_tempDirectory.Exists)
        {
            _tempDirectory.Create();
        }

        _timer = timeProvider.CreateTimer(
            _ => Purge(),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5)
        );
    }

    public async ValueTask<HttpResponseMessage?> GetResponseAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default
    )
    {
        var response = await GetResponseWithVariationAsync(request);
        return response?.Response;
    }

    public async ValueTask<ResponseWithVariation?> GetResponseWithVariationAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default
    )
    {
        var responseKey = _cacheKeyComputer.ComputeKey(request, new(CacheType.Shared));
        if (responseKey is null)
        {
            return null;
        }

        var fileInfo = FindJsonFile(responseKey);
        if (fileInfo is null)
        {
            return null;
        }

        var filename = FileName.FromFileInfo(fileInfo);
        if (filename.IsMetadataFile)
        {
            // A response was found directly from the response key without a variation

            var filePair = ResponseFilePair.FromMetadataFileInfo(fileInfo);
            var response = await filePair.GetResponseAsync(_timeProvider.GetUtcNow());
            if (response is null)
            {
                // Response is expired
                filePair.TryDelete();
                return null;
            }

            response.RequestMessage = request;
            return new(response, new Variation(CacheType.Shared));
        }
        else if (filename.IsVariationFile)
        {
            // A variation file was found, so we need to read the variation to find the response

            var file = VariationFile.FromVariationFileInfo(fileInfo);

            // Indicate that we have used the variation file
            filename.Refresh(fileInfo, _timeProvider.GetUtcNow());

            var variation = await file.GetVariationAsync(_timeProvider.GetUtcNow());
            if (variation is null)
            {
                return null;
            }

            // Recompute the response key using the variation
            responseKey = _cacheKeyComputer.ComputeKey(request, variation);
            if (responseKey is null)
            {
                return null;
            }

            fileInfo = FindJsonFile(responseKey);
            if (fileInfo is null)
            {
                return null;
            }

            filename = FileName.FromFileInfo(fileInfo);
            if (filename.IsMetadataFile)
            {
                var filePair = ResponseFilePair.FromMetadataFileInfo(fileInfo);
                var response = await filePair.GetResponseAsync(_timeProvider.GetUtcNow());
                if (response is null)
                {
                    // Response is expired
                    filePair.TryDelete();
                    return null;
                }

                response.RequestMessage = request;
                return new(response, variation);
            }
        }

        return null;
    }

    public async ValueTask<HttpResponseMessage?> SetResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default
    )
    {
        var variation = Variation.FromResponseMessage(response);
        if (variation.CacheType == CacheType.None)
        {
            // Response is not cacheable
            return null;
        }

        var request =
            response.RequestMessage
            ?? throw new ArgumentNullException(
                nameof(response),
                "Response must have a RequestMessage set."
            );

        var responseKey = _cacheKeyComputer.ComputeKey(request, new(CacheType.Shared));
        if (responseKey is null)
        {
            // Response cannot be cached as the cache key could not be computed
            return null;
        }

        if (variation.CacheType == CacheType.Shared && variation.NormalizedVaryHeaders.Count == 0)
        {
            // No Vary header, cache the response directly in the shared cache using the entry key

            var now = _timeProvider.GetUtcNow();
            var cachedResponse = await SetResponseImplAsync(
                responseKey,
                response,
                now,
                cancellationToken
            );
            cachedResponse.RequestMessage = request;
            return cachedResponse;
        }
        else
        {
            // The response is private or has a Vary header, so we need to cache both the response and the variation

            // Let the computed response key be the key for the variation,
            // and compute a new response key using the variation
            var variationKey = responseKey;
            responseKey = _cacheKeyComputer.ComputeKey(request, variation);
            if (responseKey is null)
            {
                // Response cannot be cached as the variation key could not be computed
                return null;
            }

            var now = _timeProvider.GetUtcNow();
            var modified = response.GetModified() ?? now;
            var expiration = response.GetExpiration(now) ?? (now + DefaultInitialExpiration);

            // Write the response
            var cachedResponse = await SetResponseImplAsync(
                responseKey,
                response,
                now,
                cancellationToken
            );
            cachedResponse.RequestMessage = request;

            var variationFileName = FileName.Variation(
                variationKey,
                modified,
                response.Headers.ETag
            );
            var variationFile = VariationFile.CreateTemp(_tempDirectory);

            await variationFile.WriteAsync(variationKey, variation);

            // Let the variation file have the same (possibly updated) expiration as the response
            variationFileName.SetExpiration(variationFile.Info, expiration);

            variationFile.TryMakePermanent(_rootDirectory, variationFileName);

            return cachedResponse;
        }
    }

    private async Task<HttpResponseMessage> SetResponseImplAsync(
        string responseKey,
        HttpResponseMessage response,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var modified = response.GetModified() ?? now;
        var expiration = response.GetExpiration(now) ?? (now + DefaultInitialExpiration);
        var metadata = new MetadataModel()
        {
            Key = responseKey,
            Url = response.RequestMessage!.RequestUri!,
            Version = response.Version,
            StatusCode = response.StatusCode,
            ReasonPhrase = response.ReasonPhrase,
            ResponseHeaders = response
                .Headers.Select(x => new KeyValuePair<string, List<string>>(
                    x.Key,
                    x.Value.ToList()
                ))
                .ToList(),
            ContentHeaders = response
                .Content.Headers.Select(x => new KeyValuePair<string, List<string>>(
                    x.Key,
                    x.Value.ToList()
                ))
                .ToList(),
            TrailingHeaders = response
                .TrailingHeaders.Select(x => new KeyValuePair<string, List<string>>(
                    x.Key,
                    x.Value.ToList()
                ))
                .ToList(),
        };

        var metadataFileName = FileName.Metadata(responseKey, modified, response.Headers.ETag);
        var filePair = ResponseFilePair.CreateTemp(_tempDirectory);

        await using (var responseFile = filePair.ResponseInfo.OpenWrite())
        {
            // Generate the cached response from the response
            await using var httpStream = await response.Content.ReadAsStreamAsync(
                cancellationToken
            );
            await httpStream.CopyToAsync(responseFile, cancellationToken);
        }

        await filePair.WriteMetadataAsync(metadata);

        // Set the expiration on the metadata file
        metadataFileName.SetExpiration(filePair.MetadataInfo, expiration);

        // Try and make the file pair permanent
        // If it fails then it really does not matter, as we will then just serve the temporary file and clean up later
        filePair.TryMakePermanent(_rootDirectory, metadataFileName);

        // Allow expired responses to be returned in case that the expiration was immediate
        var cachedResponse = await filePair.GetResponseAsync(now, allowExpired: true);
        return cachedResponse!;
    }

    public ValueTask RefreshResponseAsync(
        HttpResponseMessage cachedResponse,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();
        RefreshResponseImpl(cachedResponse, now, expiration: now + DefaultRefreshExpiration);
        return ValueTask.CompletedTask;
    }

    public ValueTask RefreshResponseAsync(
        HttpResponseMessage cachedResponse,
        HttpResponseMessage notModifiedResponse,
        CancellationToken cancellationToken = default
    )
    {
        if (notModifiedResponse.StatusCode != HttpStatusCode.NotModified)
        {
            throw new ArgumentException(
                "The response must be a 304 NOT MODIFIED response.",
                nameof(notModifiedResponse)
            );
        }

        var now = _timeProvider.GetUtcNow();
        var expiration = notModifiedResponse.GetExpiration(now) ?? (now + DefaultRefreshExpiration);
        RefreshResponseImpl(cachedResponse, now, expiration);
        return ValueTask.CompletedTask;
    }

    private void RefreshResponseImpl(
        HttpResponseMessage cachedResponse,
        DateTimeOffset now,
        DateTimeOffset expiration
    )
    {
        var request = cachedResponse.RequestMessage!;
        var responseKey = _cacheKeyComputer.ComputeKey(request, new(CacheType.Shared));
        if (responseKey is null)
        {
            return;
        }

        var fileInfo = FindJsonFile(responseKey);
        if (fileInfo is null)
        {
            return;
        }

        var filename = FileName.FromFileInfo(fileInfo);
        filename.Refresh(fileInfo, now);
        filename.SetExpiration(fileInfo, expiration);

        if (filename.IsVariationFile)
        {
            // If the file is a variation file, we need to refresh the actual response as well

            var variation = Variation.FromResponseMessage(cachedResponse);
            responseKey = _cacheKeyComputer.ComputeKey(request, variation);
            if (responseKey is null)
            {
                return;
            }

            fileInfo = FindJsonFile(responseKey);
            if (fileInfo is null)
            {
                return;
            }

            filename = FileName.FromFileInfo(fileInfo);
            if (!filename.IsMetadataFile)
            {
                return;
            }

            filename.Refresh(fileInfo, now);
            filename.SetExpiration(fileInfo, expiration);
        }
    }

    private FileInfo? FindJsonFile(string key)
    {
        var hash = Hash.ComputeHash(key);

        // Rely on that the file name includes the "modified" timestamp right after the hash
        return _rootDirectory.EnumerateFiles($"{hash}_*.json").MaxBy(x => x.Name);
    }

    public void Clear()
    {
        foreach (var fileInfo in _rootDirectory.GetFiles("*.json", SearchOption.AllDirectories))
        {
            var fileName = FileName.FromFileInfo(fileInfo);
            if (fileName.IsMetadataFile)
            {
                var filePair = ResponseFilePair.FromMetadataFileInfo(fileInfo);
                filePair.TryDelete();
            }
            else if (fileName.IsVariationFile)
            {
                var variationFile = VariationFile.FromVariationFileInfo(fileInfo);
                variationFile.TryDelete();
            }
        }

        RemoveOrphanedResponseFiles();
    }

    public void Purge()
    {
        var files = Enumerable.Concat(
            _rootDirectory
                .GetFiles("*.json")
                .OrderByDescending(x => x.LastAccessTimeUtc)
                .Skip(MaxEntries),
            _tempDirectory.GetFiles("*.json")
        );
        foreach (var fileInfo in files)
        {
            var fileName = FileName.FromFileInfo(fileInfo);
            if (fileName.IsMetadataFile)
            {
                var filePair = ResponseFilePair.FromMetadataFileInfo(fileInfo);
                filePair.TryDelete();
            }
            else if (fileName.IsVariationFile)
            {
                var variationFile = VariationFile.FromVariationFileInfo(fileInfo);
                variationFile.TryDelete();
            }
        }

        RemoveOrphanedResponseFiles();
    }

    private void RemoveOrphanedResponseFiles()
    {
        var orphanedResponseFiles = new Dictionary<FileName, FileInfo?>();
        foreach (var fileInfo in _rootDirectory.GetFiles("*.*", SearchOption.AllDirectories))
        {
            var fileName = FileName.FromFileInfo(fileInfo);
            if (fileName.IsMetadataFile)
            {
                var responseFileName = fileName.ToResponseFileName();
                if (!orphanedResponseFiles.Remove(responseFileName))
                {
                    // Response file is not yet iterated
                    orphanedResponseFiles.Add(responseFileName, null);
                }
            }
            else if (fileName.IsResponseFile)
            {
                if (!orphanedResponseFiles.TryAdd(fileName, fileInfo))
                {
                    // Metadata file was already iterated
                    orphanedResponseFiles.Remove(fileName);
                }
            }
        }

        foreach (var orphanedResponseFileInfo in orphanedResponseFiles.Values)
        {
            if (orphanedResponseFileInfo is null)
            {
                throw new InvalidOperationException(
                    "Orphaned response file found without corresponding metadata file."
                );
            }

            try
            {
                orphanedResponseFileInfo.Delete();
            }
            catch { }
        }
    }

    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _timer.DisposeAsync();
    }

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
        _timer.Dispose();
    }
}
