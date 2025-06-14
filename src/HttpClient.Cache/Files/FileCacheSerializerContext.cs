using System.Text.Json.Serialization;

namespace HttpClientCache.Files;

[JsonSerializable(typeof(Metadata))]
[JsonSerializable(typeof(Variation))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    Converters = [typeof(CamelCaseJsonStringEnumConverter<CacheType>)]
)]
internal sealed partial class FileCacheSerializerContext : JsonSerializerContext { }
