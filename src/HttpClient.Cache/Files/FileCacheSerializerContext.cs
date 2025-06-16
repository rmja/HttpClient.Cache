using System.Text.Json.Serialization;

namespace HttpClientCache.Files;

[JsonSerializable(typeof(MetadataModel))]
[JsonSerializable(typeof(VariationModel))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    Converters = [typeof(CamelCaseJsonStringEnumConverter<CacheType>)]
)]
internal sealed partial class FileCacheSerializerContext : JsonSerializerContext { }
