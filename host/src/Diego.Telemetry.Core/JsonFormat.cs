using System.Text.Json;
using System.Text.Json.Serialization;

namespace Diego.Telemetry;

public static class JsonFormat
{
    public static JsonSerializerOptions Config { get; } = CreateConfig();

    public static JsonSerializerOptions Pretty { get; } = CreateDocument(writeIndented: true);

    public static JsonSerializerOptions Compact { get; } = CreateDocument(writeIndented: false);

    public static string Serialize<T>(T value, bool indented)
    {
        return JsonSerializer.Serialize(value, indented ? Pretty : Compact);
    }

    private static JsonSerializerOptions CreateConfig()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            NumberHandling = JsonNumberHandling.Strict
        };
        return options;
    }

    private static JsonSerializerOptions CreateDocument(bool writeIndented)
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = writeIndented,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            NumberHandling = JsonNumberHandling.Strict
        };
    }
}
