namespace Donkeh.Llm;

/// <summary>
/// Describes one argument accepted by a capability. The same descriptor drives
/// CLI validation, help text and the MCP JSON input schema.
/// </summary>
public sealed class LlmCapabilityArgument
{
    /// <summary>Argument name without a leading dash.</summary>
    public required string Name { get; init; }

    /// <summary>JSON-schema-style type: string, boolean, integer or array.</summary>
    public string Type { get; init; } = "string";

    /// <summary>One-line explanation of what the argument changes.</summary>
    public required string Description { get; init; }

    /// <summary>True when a caller must supply the argument.</summary>
    public bool Required { get; init; }

    /// <summary>Default value as written on the command line.</summary>
    public string? Default { get; init; }

    /// <summary>Accepted literal values, when the argument is an enumeration.</summary>
    public IReadOnlyList<string>? Values { get; init; }
}

/// <summary>
/// A discoverable capability exposed to LLMs through the CLI and, when
/// applicable, through MCP.
/// </summary>
public sealed class LlmCapability
{
    /// <summary>Stable dotted identifier, for example <c>csx.validate</c>.</summary>
    public required string Name { get; init; }

    /// <summary>MCP tool name, for example <c>csx_validate</c>. Null when not exposed through MCP.</summary>
    public string? McpName { get; init; }

    /// <summary>Discovery group, for example <c>Project</c> or <c>CSX</c>.</summary>
    public required string Category { get; init; }

    /// <summary>Short sentence shown by <c>donkeh mcp show</c>.</summary>
    public required string Summary { get; init; }

    /// <summary>Longer description shown by <c>donkeh mcp capabilities &lt;name&gt;</c>.</summary>
    public string? Description { get; init; }

    /// <summary>Copy-paste CLI invocation.</summary>
    public required string Usage { get; init; }

    /// <summary>Arguments accepted by the capability.</summary>
    public IReadOnlyList<LlmCapabilityArgument> Arguments { get; init; } = [];

    /// <summary>Short description of the returned data.</summary>
    public required string Output { get; init; }

    /// <summary>Minimal examples; each entry is a complete CLI command.</summary>
    public IReadOnlyList<string> Examples { get; init; } = [];

    /// <summary>Terms used by capability and documentation search.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Package that registered the capability; null for built-in capabilities.</summary>
    public string? RegisteredBy { get; init; }

    /// <summary>False when the capability should not appear in the short overview.</summary>
    public bool VisibleInShow { get; init; } = true;

    /// <summary>True when the capability can be invoked without a Donkeh project.</summary>
    public bool WorksOutsideProject { get; init; }

    /// <summary>True when this capability is backed by a third-party provider.</summary>
    public bool IsProviderBacked { get; init; }
}
