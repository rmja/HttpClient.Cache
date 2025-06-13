using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;

namespace HttpClient.Cache.Files;

internal readonly record struct FileName(
    string Hash,
    DateTime ModifiedUtc,
    EntityTagHeaderValue? Etag,
    string Extension
)
{
    private static readonly int _guidLength = Guid.Empty.ToString().Length;

    public const string MetadataExtension = ".response.json";
    public const string ResponseExtension = ".response.bin";
    public const string VariationExtension = ".variation.json";

    public bool IsTempFile => Hash.Length == _guidLength && ModifiedUtc == default;
    public bool IsMetadataFile => Extension == MetadataExtension;
    public bool IsResponseFile => Extension == ResponseExtension;
    public bool IsVariationFile => Extension == VariationExtension;

    private FileName(string tempGuid, string extension)
        : this(tempGuid, default, null, extension) { }

    public FileName ToResponseFileName()
    {
        Debug.Assert(IsMetadataFile, "Cannot convert non-metadata file to response file name.");
        return new(Hash, ModifiedUtc, Etag, ResponseExtension);
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
        return lastWriteTime == FileConstants.NoExpirationWriteTime
            ? null
            : new DateTimeOffset(lastWriteTime, TimeSpan.Zero);
    }

    public void SetExpiration(FileInfo fileInfo, DateTimeOffset? expiration)
    {
        Debug.Assert(fileInfo.Name.EndsWith(Extension), "FileInfo name does not match FileName.");
        fileInfo.LastWriteTimeUtc = expiration?.UtcDateTime ?? FileConstants.NoExpirationWriteTime;
    }

    public override string ToString() =>
        IsTempFile
            ? Hash + Extension
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0}_{1:yyyy-MM-ddTHHmmss}Z_{2}{3}",
                Hash,
                ModifiedUtc,
                FormatEtag(Etag),
                Extension
            );

    public static FileName Metadata(
        string hash,
        DateTimeOffset modified,
        EntityTagHeaderValue? etag
    ) => new(hash, modified.UtcDateTime, etag, MetadataExtension);

    public static FileName Variation(
        string hash,
        DateTimeOffset modified,
        EntityTagHeaderValue? etag
    ) => new(hash, modified.UtcDateTime, etag, VariationExtension);

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

        var etagPart = basename[(index + 1 + DateTimeFormat.Length + 1)..];
        var etag = ParseEtag(etagPart);

        return new(hash, modifiedUtc, etag, extension);
    }

    public static implicit operator string(FileName filename) => filename.ToString();

    private static string FormatEtag(EntityTagHeaderValue? etag)
    {
        if (etag is null)
        {
            return string.Empty;
        }
        return etag.IsWeak ? $"W{etag.Tag.Trim('"')}" : $"S{etag.Tag.Trim('"')}";
    }

    private static EntityTagHeaderValue? ParseEtag(ReadOnlySpan<char> etagPart)
    {
        if (etagPart.Length == 0)
        {
            return null;
        }
        var isWeak = etagPart.StartsWith('W');
        var tag = isWeak ? etagPart[1..] : etagPart;
        return new EntityTagHeaderValue($"\"{etagPart}\"", isWeak);
    }
}
