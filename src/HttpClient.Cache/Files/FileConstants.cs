namespace HttpClient.Cache.Files;

internal static class FileConstants
{
    public static DateTime NoExpirationWriteTime { get; } = DateTime.FromFileTimeUtc(1);
}
