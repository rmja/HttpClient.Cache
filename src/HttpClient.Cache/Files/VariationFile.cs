using System.Text.Json;

namespace HttpClientCache.Files;

internal record struct VariationFile(FileInfo Info)
{
    public static VariationFile FromVariationFileInfo(FileInfo fileInfo) => new(fileInfo);

    public static VariationFile CreateTemp(DirectoryInfo tempDirectory)
    {
        var basename = Guid.CreateVersion7().ToString();
        var filePath = Path.Combine(tempDirectory.FullName, basename + FileName.VariationExtension);
        return new(new(filePath));
    }

    public readonly bool TryMakePermanent(DirectoryInfo rootDirectory, FileName variationFileName)
    {
        var variationPath = Path.Combine(rootDirectory.FullName, variationFileName);

        try
        {
            Info.MoveTo(variationPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public readonly async ValueTask<Variation?> GetVariationAsync(DateTimeOffset now)
    {
        var variationFileName = FileName.FromFileInfo(Info);
        var expires = variationFileName.GetExpiration(Info);
        if (expires < now)
        {
            return null;
        }

        await using var stream = Info.OpenRead();
        var model = await JsonSerializer.DeserializeAsync(
            stream,
            FileCacheSerializerContext.Default.VariationModel
        );
        return new Variation(model!.CacheType)
        {
            NormalizedVaryHeaders = model.NormalizedVaryHeaders,
        };
    }

    public readonly async ValueTask WriteAsync(string key, Variation variation)
    {
        var model = new VariationModel
        {
            Key = key,
            CacheType = variation.CacheType,
            NormalizedVaryHeaders = variation.NormalizedVaryHeaders,
        };
        using var stream = Info.OpenWrite();
        await JsonSerializer.SerializeAsync(
            stream,
            model,
            FileCacheSerializerContext.Default.VariationModel
        );
    }

    public readonly bool TryDelete()
    {
        try
        {
            Info.Delete();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
