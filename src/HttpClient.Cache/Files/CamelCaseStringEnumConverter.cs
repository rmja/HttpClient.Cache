using System.Text.Json;
using System.Text.Json.Serialization;

namespace HttpClientCache.Files;

internal class CamelCaseJsonStringEnumConverter<TEnum>()
    : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase)
    where TEnum : struct, Enum { }
