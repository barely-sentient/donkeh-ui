using System.Text.Json;
using System.Xml.Linq;
using Donkeh.Llm.Project;

namespace Donkeh.Llm;

/// <summary>One package reference in a project, with the resolved version when available.</summary>
public sealed class ProjectPackageReference
{
    /// <summary>NuGet package id.</summary>
    public required string Id { get; init; }

    /// <summary>Version expression written in the project file.</summary>
    public string? RequestedVersion { get; init; }

    /// <summary>Version resolved by the last restore, when an assets file exists.</summary>
    public string? ResolvedVersion { get; init; }

    /// <summary>Condition attached to the reference, when any.</summary>
    public string? Condition { get; init; }

    /// <summary>True for Donkeh packages and packages whose id starts with <c>Donkeh.</c>.</summary>
    public bool IsDonkeh { get; init; }

    /// <summary>Package role: framework, renderer, component-library or tooling.</summary>
    public required string Kind { get; init; }
}

/// <summary>One project reference in a project.</summary>
public sealed class ProjectProjectReference
{
    /// <summary>Path exactly as written in the project file.</summary>
    public required string Include { get; init; }

    /// <summary>Absolute path, when it can be resolved.</summary>
    public string? FullPath { get; init; }

    /// <summary>Referenced project name, when the file exists.</summary>
    public string? Name { get; init; }

    /// <summary>True when the referenced project is packable (a component library).</summary>
    public bool IsComponentLibrary { get; init; }
}

/// <summary>Parsed <c>csx.json</c> configuration.</summary>
public sealed class CsxConfiguration
{
    /// <summary>True when a <c>csx.json</c> file was found.</summary>
    public bool Present { get; init; }

    /// <summary>Absolute path to the configuration file.</summary>
    public string? File { get; init; }

    /// <summary>True when the file contains valid JSON and the known keys have string values.</summary>
    public bool Valid { get; init; } = true;

    /// <summary>Configuration error, when <see cref="Valid"/> is false.</summary>
    public string? Error { get; init; }

    /// <summary>Class that receives HTML helper calls.</summary>
    public string? FrameworkHtmlClass { get; init; }

    /// <summary>Method that receives HTML tags.</summary>
    public string? FrameworkHtmlClassMethod { get; init; }

    /// <summary>Method that receives component tags.</summary>
    public string? FrameworkComponentMethod { get; init; }

    /// <summary>Method that receives fragments.</summary>
    public string? FrameworkFragmentMethod { get; init; }

    /// <summary>Directory receiving generated C# files.</summary>
    public string? GeneratedFileOutput { get; init; }
}

/// <summary>The renderer selected by a project.</summary>
public sealed class RendererSelection
{
    /// <summary>Logical renderer name: desktop, android, web or unknown.</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable platform name.</summary>
    public required string Platform { get; init; }

    /// <summary>Package or project reference that supplies the renderer.</summary>
    public string? Package { get; init; }

    /// <summary>True when the project explicitly enables the renderer.</summary>
    public bool Configured { get; init; }
}

/// <summary>
/// Everything agent-facing project inspection needs, read from the project
/// file, <c>csx.json</c> and the last restore's <c>project.assets.json</c>.
/// </summary>
public sealed class DonkehProject
{
    /// <summary>Absolute project file path.</summary>
    public required string ProjectFile { get; init; }

    /// <summary>Directory containing the project file.</summary>
    public required string ProjectDirectory { get; init; }

    /// <summary>Project name without the extension.</summary>
    public required string Name { get; init; }

    /// <summary>Root namespace declared by the project, or the project name.</summary>
    public required string RootNamespace { get; init; }

    /// <summary>application, framework, test-container or library.</summary>
    public required string ProjectType { get; init; }

    /// <summary>Output type, when declared.</summary>
    public string? OutputType { get; init; }

    /// <summary>Target frameworks exactly as written or resolved by restore.</summary>
    public IReadOnlyList<string> TargetFrameworks { get; init; } = [];

    /// <summary>Donkeh package version declared by the project.</summary>
    public string? DonkehVersion { get; init; }

    /// <summary>True when the project imports a local framework checkout.</summary>
    public bool UsesLocalCheckout { get; init; }

    /// <summary>All package references, including non-Donkeh packages.</summary>
    public IReadOnlyList<ProjectPackageReference> Packages { get; init; } = [];

    /// <summary>All project references.</summary>
    public IReadOnlyList<ProjectProjectReference> ProjectReferences { get; init; } = [];

    /// <summary>Selected MSBuild properties that affect Donkeh behaviour.</summary>
    public IReadOnlyDictionary<string, string?> Settings { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>CSX configuration, if any.</summary>
    public required CsxConfiguration Csx { get; init; }

    /// <summary>Active renderer.</summary>
    public required RendererSelection Renderer { get; init; }

    /// <summary>Available renderers for the project.</summary>
    public IReadOnlyList<RendererSelection> Renderers { get; init; } = [];

    /// <summary>Detected framework features.</summary>
    public IReadOnlyList<string> Features { get; init; } = [];

    /// <summary>Declared stylesheet paths.</summary>
    public IReadOnlyList<string> Stylesheets { get; init; } = [];

    /// <summary>True when the last restore produced <c>project.assets.json</c>.</summary>
    public bool HasRestoredAssets { get; init; }

    /// <summary>Non-fatal problems found while reading the project.</summary>
    public IReadOnlyList<LlmDiagnostic> Warnings { get; init; } = [];

    /// <summary>Donkeh package references only.</summary>
    public IEnumerable<ProjectPackageReference> DonkehPackages =>
        Packages.Where(package => package.IsDonkeh);
}
