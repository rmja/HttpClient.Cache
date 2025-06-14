using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;

namespace HttpClient.Cache.Files;

internal readonly record struct FileName(
    string KeyHash,
    DateTime ModifiedUtc,
    string? EtagHash,
    string Extension
)
{
    private static readonly int _guidLength = Guid.Empty.ToString().Length;

    public const string MetadataExtension = ".response.json";
    public const string ResponseExtension = ".response.bin";
    public const string VariationExtension = ".variation.json";

    public bool IsTempFile => KeyHash.Length == _guidLength && ModifiedUtc == default;
    public bool IsMetadataFile => Extension == MetadataExtension;
    public bool IsResponseFile => Extension == ResponseExtension;
    public bool IsVariationFile => Extension == VariationExtension;

    private FileName(string tempGuid, string extension)
        : this(tempGuid, default, null, extension) { }

    public FileName ToResponseFileName()
    {
        Debug.Assert(IsMetadataFile, "Cannot convert non-metadata file to response file name.");
        return new(KeyHash, ModifiedUtc, EtagHash, ResponseExtension);
    }

    public void Refresh(FileInfo fileInfo, DateTimeOffset now)
    {
        Debug.Assert(fileInfo.Name.EndsWith(Extension), "FileInfo name does not match FileName.");
        fileInfo.LastAccessTimeUtc = now.UtcDateTime;
    }

    public DateTimeOffset? GetExpiration(FileInfo fileInfo)
    {
        Debug.Assert(fileInfo.Name.EndsWith(Extension), "FileInfo name does not match FileName.");
        var lastWriteTime = fileInfo.LastWriteTimeUtc;
        return new DateTimeOffset(lastWriteTime, TimeSpan.Zero);
    }

    public void SetExpiration(FileInfo fileInfo, DateTimeOffset expiration)
    {
        Debug.Assert(fileInfo.Name.EndsWith(Extension), "FileInfo name does not match FileName.");
        fileInfo.LastWriteTimeUtc = expiration.UtcDateTime;
    }

    public override string ToString() =>
        IsTempFile
            ? KeyHash + Extension
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0}_{1:yyyy-MM-ddTHHmmss}Z_{2}{3}",
                KeyHash,
                ModifiedUtc,
                EtagHash ?? string.Empty,
                Extension
            );

    public static FileName Metadata(
        string key,
        DateTimeOffset modified,
        EntityTagHeaderValue? etag
    ) =>
        new(
            Hash.ComputeHash(key),
            modified.UtcDateTime,
            etag is not null ? Hash.ComputeHash(etag.ToString()) : null,
            MetadataExtension
        );

    public static FileName Variation(
        string key,
        DateTimeOffset modified,
        EntityTagHeaderValue? etag
    ) =>
        new(
            Hash.ComputeHash(key),
            modified.UtcDateTime,
            etag is not null ? Hash.ComputeHash(etag.ToString()) : null,
            VariationExtension
        );

    public static FileName FromFileInfo(FileInfo fileInfo)
    {
        var basenameLength = fileInfo.Name.IndexOf('.');
        var basename = fileInfo.Name.AsSpan(0, basenameLength);
        var extension = fileInfo.Name[basenameLength..];

        if (basenameLength == _guidLength)
        {
            return new(basename.ToString(), extension);
        }

        var index = basename.IndexOf('_');
        var hash = basename[..index].ToString();

        const string DateTimeFormat = "yyyy-MM-ddTHHmmssZ";
        var modifiedPart = basename.Slice(index + 1, DateTimeFormat.Length);
        var modifiedUtc = DateTime.ParseExact(
            modifiedPart,
            DateTimeFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind
        );
        Debug.Assert(modifiedUtc.Kind == DateTimeKind.Utc);

        var etagHash = basename[(index + 1 + DateTimeFormat.Length + 1)..].ToString();

        return new(hash, modifiedUtc, etagHash, extension);
    }

    public static implicit operator string(FileName filename) => filename.ToString();
}
