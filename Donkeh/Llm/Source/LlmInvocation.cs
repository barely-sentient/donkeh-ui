using System.Text.Json;

namespace Donkeh.Llm;

/// <summary>Context passed to a capability implementation.</summary>
public sealed class LlmInvocation
{
    /// <summary>Directory the CLI was launched from.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Optional project file or directory supplied with <c>--project</c>.</summary>
    public string? ProjectPath { get; init; }

    /// <summary>Positional arguments, in command-line order.</summary>
    public IReadOnlyList<string> Positionals { get; init; } = [];

    /// <summary>Named arguments. Values are strings, booleans, integers or JSON elements.</summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads a string argument, or returns <paramref name="fallback"/>.</summary>
    public string? String(string name, string? fallback = null)
    {
        if (!Arguments.TryGetValue(name, out var value) || value is null)
        {
            return fallback;
        }

        return value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element => element.ToString(),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Reads a boolean argument.</summary>
    public bool Flag(string name, bool fallback = false)
    {
        if (!Arguments.TryGetValue(name, out var value) || value is null)
        {
            return fallback;
        }

        return value switch
        {
            bool flag => flag,
            string text => bool.TryParse(text, out var parsed) ? parsed : fallback,
            JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False =>
                element.GetBoolean(),
            _ => fallback
        };
    }

    /// <summary>Reads an integer argument.</summary>
    public int Int(string name, int fallback = 0)
    {
        if (!Arguments.TryGetValue(name, out var value) || value is null)
        {
            return fallback;
        }

        return value switch
        {
            int number => number,
            long number => (int)number,
            string text when int.TryParse(text, out var parsed) => parsed,
            JsonElement element when element.ValueKind == JsonValueKind.Number =>
                element.TryGetInt32(out var parsed) ? parsed : fallback,
            _ => fallback
        };
    }
}

/// <summary>An error that should be returned as a structured capability failure.</summary>
public sealed class LlmException : Exception
{
    /// <summary>Creates a structured capability error.</summary>
    public LlmException(string code, string message, string? hint = null)
        : base(message)
    {
        Code = code;
        Hint = hint;
    }

    /// <summary>Stable diagnostic-style code, for example <c>LLM0001</c>.</summary>
    public string Code { get; }

    /// <summary>Optional next action for the caller.</summary>
    public string? Hint { get; }
}

/// <summary>
/// Optional in-process provider contract for packages that need executable
/// capabilities. A package registers a provider through <c>donkeh.llm.json</c>;
/// the CLI never loads an unregistered assembly.
/// </summary>
public interface ILlmCapabilityProvider
{
    /// <summary>Package id that owns the provider.</summary>
    string Package { get; }

    /// <summary>Capabilities contributed by the provider.</summary>
    IEnumerable<LlmCapability> GetCapabilities();

    /// <summary>Invokes one capability returned by <see cref="GetCapabilities"/>.</summary>
    object? Invoke(string capabilityName, LlmInvocation invocation);
}
