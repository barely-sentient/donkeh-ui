using System.Text.Json;
using System.Text.Json.Serialization;

namespace Donkeh.Llm;

/// <summary>Shared JSON settings for machine-readable Donkeh LLM output.</summary>
public static class LlmJson
{
    /// <summary>Serializer options used by every agent-facing command and MCP result.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Serializes a value with the shared options.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Serializes a value with indentation for human-facing JSON.</summary>
    public static string SerializeIndented<T>(T value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions(Options) { WriteIndented = true });
}
