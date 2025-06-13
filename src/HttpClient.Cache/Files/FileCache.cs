using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace HttpClient.Cache.Files;

public class FileCache : IHttpCache
{
    private readonly DirectoryInfo _rootDirectory;
    private readonly DirectoryInfo _tempDirectory;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _timer;

    public static FileCache Default { get; } = new();

    public int MaxEntries { get; set; } = 1000;
    public TimeSpan DefaultInitialExpiration { get; set; } = TimeSpan.FromDays(2);
    public TimeSpan DefaultRefreshExpiration { get; set; } = TimeSpan.FromDays(2);

    public FileCache()
        : this(Path.Combine(Path.GetTempPath(), "HttpClient.FileCache")) { }

    public FileCache(string rootDirectory)
        : this(rootDirectory, TimeProvider.System) { }

    public FileCache(string rootDirectory, TimeProvider timeProvider)
    {
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

    public async ValueTask<ICacheEntry?> GetAsync(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        var fileInfo = FindJsonFile(key);
        if (fileInfo is null)
        {
            return null;
        }

        var filename = FileName.FromFileInfo(fileInfo);
        if (filename.IsMetadataFile)
        {
            var filePair = ResponseFilePair.FromMetadataFileInfo(fileInfo);
            return await filePair.GetResponseAsync(_timeProvider.GetUtcNow());
        }
        else if (filename.IsVariationFile)
        {
            var file = VariationFile.FromVariationFileInfo(fileInfo);
            filename.Refresh(fileInfo, _timeProvider.GetUtcNow());
            return await file.GetVariationAsync(_timeProvider.GetUtcNow());
        }

        return null;
    }

    public Task<Response> SetResponseAsync(
        string responseKey,
        HttpResponseMessage responseMessage,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();
        return SetResponseImplAsync(responseKey, responseMessage, now, cancellationToken);
    }

    public async Task<Response> SetResponseAsync(
        string responseKey,
        HttpResponseMessage response,
        string variationKey,
        Variation variation,
        CancellationToken cancellationToken = default
    )
    {
        var now = _timeProvider.GetUtcNow();
        var modified = response.GetModified() ?? now;
        var expiration = response.GetExpiration(now) ?? (now + DefaultInitialExpiration);

        var responseEntry = await SetResponseImplAsync(
            responseKey,
            response,
            now,
            cancellationToken
        );

        var hash = ComputeHash(variationKey);
        var variationFileName = FileName.Variation(hash, modified, response.Headers.ETag);
        var variationFile = VariationFile.CreateTemp(_tempDirectory);

        await variationFile.WriteAsync(variation);

        // Let the variation file have the same (possibly updated) expiration as the response
        variationFileName.SetExpiration(variationFile.Info, expiration);

        variationFile.TryMakePermanent(_rootDirectory, variationFileName);

        return responseEntry;
    }

    private async Task<Response> SetResponseImplAsync(
        string responseKey,
        HttpResponseMessage response,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var modified = response.GetModified() ?? now;
        var expiration = response.GetExpiration(now) ?? (now + DefaultInitialExpiration);
        var metadata = new Metadata()
        {
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
            Etag = response.Headers.ETag?.ToString(),
            LastModified = response.Content.Headers.LastModified,
        };

        var hash = ComputeHash(responseKey);
        var metadataFileName = FileName.Metadata(hash, modified, response.Headers.ETag);
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
        var responseEntry = await filePair.GetResponseAsync(now, allowExpired: true);
        return responseEntry!;
    }

    public ValueTask RefreshResponseAsync(string key, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        RefreshResponseImpl(key, now, expiration: now + DefaultRefreshExpiration);
        return ValueTask.CompletedTask;
    }

    public ValueTask RefreshResponseAsync(
        string key,
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
        RefreshResponseImpl(key, now, expiration);
        return ValueTask.CompletedTask;
    }

    private void RefreshResponseImpl(string key, DateTimeOffset now, DateTimeOffset expiration)
    {
        var fileInfo = FindJsonFile(key);
        if (fileInfo is null)
        {
            return;
        }

        var filename = FileName.FromFileInfo(fileInfo);
        filename.Refresh(fileInfo, now);
        filename.SetExpiration(fileInfo, expiration);
    }

    private FileInfo? FindJsonFile(string key)
    {
        var hash = ComputeHash(key);

        // Rely on that the file name includes the "modified" timestamp right after the hash
        return _rootDirectory.EnumerateFiles($"{hash}_*.json").MaxBy(x => x.Name);
    }

    private static string ComputeHash(string key)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexStringLower(hash);
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
