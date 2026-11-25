using System.Text.Json;
using Donkeh.Llm.Components;

namespace Donkeh.Llm.Extensibility;

/// <summary>
/// Declarative manifest a package uses to contribute LLM capabilities. The
/// manifest is discovered from the package root or a project's
/// <c>.donkeh/llm.json</c> file.
/// </summary>
public sealed class DonkehLlmManifest
{
    /// <summary>Manifest schema version. Supported value: 1.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Package id that owns the registration.</summary>
    public string Package { get; set; } = "";

    /// <summary>Human-readable package name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Static capabilities contributed by the package.</summary>
    public List<ManifestCapability> Capabilities { get; set; } = [];

    /// <summary>Component metadata contributed by the package.</summary>
    public List<ManifestComponent> Components { get; set; } = [];

    /// <summary>Documentation topics contributed by the package.</summary>
    public List<ManifestDoc> Docs { get; set; } = [];

    /// <summary>Optional executable provider for tools that need code.</summary>
    public ManifestProvider? Provider { get; set; }

    /// <summary>Non-fatal discovery problems, filled by the loader.</summary>
    public List<string> Warnings { get; } = [];
}

/// <summary>A static capability declared by a package manifest.</summary>
public sealed class ManifestCapability
{
    /// <summary>Dotted capability name. Must not collide with a built-in capability.</summary>
    public string Name { get; set; } = "";

    /// <summary>Discovery category.</summary>
    public string Category { get; set; } = "Package";

    /// <summary>Short summary.</summary>
    public string Summary { get; set; } = "";

    /// <summary>Longer description.</summary>
    public string? Description { get; set; }

    /// <summary>Copy-paste CLI usage.</summary>
    public string? Usage { get; set; }

    /// <summary>Return shape description.</summary>
    public string? Output { get; set; }

    /// <summary>Argument descriptors.</summary>
    public List<LlmCapabilityArgument> Arguments { get; set; } = [];

    /// <summary>Minimal CLI examples.</summary>
    public List<string> Examples { get; set; } = [];

    /// <summary>Search terms.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Static JSON result returned when the capability is invoked.</summary>
    public JsonElement? Result { get; set; }

    /// <summary>Expose the capability as an MCP tool.</summary>
    public bool Mcp { get; set; } = true;

    /// <summary>Include the capability in the short <c>mcp show</c> overview.</summary>
    public bool VisibleInShow { get; set; } = true;

    /// <summary>True when the capability works outside a Donkeh project.</summary>
    public bool WorksOutsideProject { get; set; }
}

/// <summary>A component contributed by a package manifest.</summary>
public sealed class ManifestComponent
{
    /// <summary>Component name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Namespace.</summary>
    public string? Namespace { get; set; }

    /// <summary>Package id. Defaults to the manifest package.</summary>
    public string? Package { get; set; }

    /// <summary>class, csx or assembly.</summary>
    public string Kind { get; set; } = "assembly";

    /// <summary>Short description.</summary>
    public string? Description { get; set; }

    /// <summary>Props type name.</summary>
    public string? PropsType { get; set; }

    /// <summary>Public data properties.</summary>
    public List<ComponentProperty> Properties { get; set; } = [];

    /// <summary>Event handlers.</summary>
    public List<ComponentEvent> Events { get; set; } = [];

    /// <summary>True when the component accepts children.</summary>
    public bool SupportsChildren { get; set; }

    /// <summary>True when the component uses hooks.</summary>
    public bool SupportsState { get; set; }

    /// <summary>Lifecycle overrides.</summary>
    public List<string> Lifecycle { get; set; } = [];

    /// <summary>Dependencies or referenced types.</summary>
    public List<string> Dependencies { get; set; } = [];

    /// <summary>Minimal usage examples.</summary>
    public List<string> Examples { get; set; } = [];

    /// <summary>Stylesheets declared by the component.</summary>
    public List<string> Stylesheets { get; set; } = [];

    /// <summary>Search terms.</summary>
    public List<string> Tags { get; set; } = [];
}

/// <summary>A documentation topic contributed by a package manifest.</summary>
public sealed class ManifestDoc
{
    /// <summary>Stable topic key.</summary>
    public string Topic { get; set; } = "";

    /// <summary>Page title.</summary>
    public string Title { get; set; } = "";

    /// <summary>Lead paragraph.</summary>
    public string? Summary { get; set; }

    /// <summary>Reference to the complete documentation.</summary>
    public string? Reference { get; set; }

    /// <summary>Sections with free-text content and code samples.</summary>
    public List<ManifestDocSection> Sections { get; set; } = [];

    /// <summary>API signatures.</summary>
    public List<string> ApiSignatures { get; set; } = [];

    /// <summary>Minimal examples.</summary>
    public List<Docs.DocsExample> Examples { get; set; } = [];
}

/// <summary>One section in a manifest documentation topic.</summary>
public sealed class ManifestDocSection
{
    /// <summary>Stable section id.</summary>
    public string Id { get; set; } = "";

    /// <summary>Section heading.</summary>
    public string Title { get; set; } = "";

    /// <summary>Plain-text content.</summary>
    public string? Text { get; set; }

    /// <summary>Code samples.</summary>
    public List<Docs.DocsCodeBlock> Code { get; set; } = [];
}

/// <summary>An explicitly registered executable provider.</summary>
public sealed class ManifestProvider
{
    /// <summary>Path relative to the manifest directory.</summary>
    public string Assembly { get; set; } = "";

    /// <summary>Type implementing <see cref="ILlmCapabilityProvider"/>.</summary>
    public string Type { get; set; } = "";
}
