using System.Text.Json;

namespace HttpClientCache.Files;

internal record struct ResponseFilePair(FileInfo MetadataInfo, FileInfo ResponseInfo)
{
    public static ResponseFilePair FromMetadataFileInfo(FileInfo metadataInfo)
    {
        var metadataFileName = FileName.FromFileInfo(metadataInfo);
        var responseFileName = metadataFileName.ToResponseFileName();
        var responseInfo = new FileInfo(
            Path.Combine(metadataInfo.DirectoryName!, responseFileName)
        );
        return new(metadataInfo, responseInfo);
    }

    public static ResponseFilePair CreateTemp(DirectoryInfo tempDirectory)
    {
        var basename = Guid.CreateVersion7().ToString();
        var metadataPath = Path.Combine(
            tempDirectory.FullName,
            basename + FileName.MetadataExtension
        );
        var responsePath = Path.Combine(
            tempDirectory.FullName,
            basename + FileName.ResponseExtension
        );
        return new(new(metadataPath), new(responsePath));
    }

    public readonly bool TryMakePermanent(DirectoryInfo rootDirectory, FileName metadataFileName)
    {
        var responseFileName = metadataFileName.ToResponseFileName();
        var metadataPath = Path.Combine(rootDirectory.FullName, metadataFileName);
        var responsePath = Path.Combine(rootDirectory.FullName, responseFileName);

        try
        {
            // Ensure that the response file is moved before we "commit" with the metadata file
            ResponseInfo.MoveTo(responsePath);
            MetadataInfo.MoveTo(metadataPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public readonly async ValueTask<HttpResponseMessage?> GetResponseAsync(
        DateTimeOffset now,
        bool allowExpired = false
    )
    {
        // Open the response file and keep it open
        var responseFileStream = ResponseInfo.OpenRead();

        var metadata = await ReadMetadataAsync();

        var metadataFileName = FileName.FromFileInfo(MetadataInfo);
        var expires = metadataFileName.GetExpiration(MetadataInfo);
        var maxAge = expires.HasValue ? expires.Value - now : (TimeSpan?)null;
        if (maxAge < TimeSpan.Zero)
        {
            if (allowExpired)
            {
                maxAge = TimeSpan.Zero;
            }
            else
            {
                await responseFileStream.DisposeAsync();
                return null; // Response is expired
            }
        }

        var response = new HttpResponseMessage()
        {
            Version = metadata.Version,
            StatusCode = metadata.StatusCode,
            ReasonPhrase = metadata.ReasonPhrase,
            Content = new StreamContent(responseFileStream),
        };

        foreach (var header in metadata.ResponseHeaders)
        {
            response.Headers.TryAddWithoutValidation(header.Key, header.Value.AsEnumerable());
        }

        foreach (var header in metadata.ContentHeaders)
        {
            response.Content.Headers.TryAddWithoutValidation(
                header.Key,
                header.Value.AsEnumerable()
            );
        }

        if (metadata.TrailingHeaders is not null)
        {
            foreach (var header in metadata.TrailingHeaders)
            {
                response.TrailingHeaders.TryAddWithoutValidation(
                    header.Key,
                    header.Value.AsEnumerable()
                );
            }
        }

        if (response.Headers.CacheControl?.MaxAge is not null)
        {
            response.Headers.CacheControl.MaxAge = maxAge;
        }

        return response;
    }

    public readonly async ValueTask<MetadataModel> ReadMetadataAsync()
    {
        await using var stream = MetadataInfo.OpenRead();
        var metadata = await JsonSerializer.DeserializeAsync(
            stream,
            FileCacheSerializerContext.Default.MetadataModel
        );
        return metadata!;
    }

    public readonly async ValueTask WriteMetadataAsync(MetadataModel metadata)
    {
        await using var stream = MetadataInfo.OpenWrite();
        await JsonSerializer.SerializeAsync(
            stream,
            metadata,
            FileCacheSerializerContext.Default.MetadataModel
        );
    }

    public readonly bool TryDelete()
    {
        try
        {
            MetadataInfo.Delete();
        }
        catch
        {
            return false;
        }

        // No-one can find the response file now, so we can delete it too

        try
        {
            // Try and delete the response file immediately
            // This may fail if the response file is still open

            ResponseInfo.Delete();
        }
        catch
        {
            // Cleaned up as an orphaned response file later
        }

        return true;
    }
}
