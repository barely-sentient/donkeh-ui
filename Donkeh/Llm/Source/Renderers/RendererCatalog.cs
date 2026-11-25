using System.Text.Json;
using Donkeh.Llm.Project;

namespace Donkeh.Llm.Renderers;

/// <summary>One renderer capability entry.</summary>
public sealed class RendererCapability
{
    /// <summary>supported, partial or unsupported.</summary>
    public required string Status { get; init; }

    /// <summary>Implementation notes and limitations.</summary>
    public string? Notes { get; init; }
}

/// <summary>Capabilities for one renderer.</summary>
public sealed class RendererCapabilitySet
{
    /// <summary>desktop, android or web.</summary>
    public required string Name { get; init; }

    /// <summary>Desktop, Android or Browser.</summary>
    public required string Platform { get; init; }

    /// <summary>Packages that provide the renderer.</summary>
    public IReadOnlyList<string> Packages { get; init; } = [];

    /// <summary>Short implementation description.</summary>
    public string? Description { get; init; }

    /// <summary>Capability category to support entry.</summary>
    public IReadOnlyDictionary<string, RendererCapability> Capabilities { get; init; } =
        new Dictionary<string, RendererCapability>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Renderer selection and configuration for one project.</summary>
public sealed class RendererInspection
{
    /// <summary>Active renderer name.</summary>
    public required string Active { get; init; }

    /// <summary>Active platform.</summary>
    public required string Platform { get; init; }

    /// <summary>Package or project that supplies the renderer.</summary>
    public string? Package { get; init; }

    /// <summary>True when the project explicitly enables the renderer.</summary>
    public bool Configured { get; init; }

    /// <summary>Target frameworks declared by the project.</summary>
    public IReadOnlyList<string> TargetFrameworks { get; init; } = [];

    /// <summary>Every renderer enabled by the project.</summary>
    public IReadOnlyList<string> Available { get; init; } = [];

    /// <summary>Project file used for the inspection.</summary>
    public string? Project { get; init; }

    /// <summary>Capability set for the active renderer.</summary>
    public RendererCapabilitySet? Capabilities { get; init; }
}

/// <summary>Reads renderer capability metadata and selects the active renderer.</summary>
public static class RendererCatalog
{
    private static List<RendererCapabilitySet>? _renderers;

    /// <summary>Returns every known renderer.</summary>
    public static List<RendererCapabilitySet> All()
    {
        if (_renderers is not null)
        {
            return _renderers;
        }

        try
        {
            using var stream = typeof(RendererCatalog).Assembly
                .GetManifestResourceStream("Donkeh.Llm.Resources.renderer-capabilities.json");

            if (stream is null)
            {
                return _renderers = [];
            }

            using var document = JsonDocument.Parse(stream);
            var renderers = new List<RendererCapabilitySet>();

            foreach (var renderer in document.RootElement.GetProperty("renderers").EnumerateArray())
            {
                var capabilities = new Dictionary<string, RendererCapability>(StringComparer.OrdinalIgnoreCase);

                foreach (var capability in renderer.GetProperty("capabilities").EnumerateObject())
                {
                    var value = capability.Value;
                    capabilities[capability.Name] = new RendererCapability
                    {
                        Status = value.GetProperty("status").GetString() ?? "partial",
                        Notes = value.GetProperty("notes").GetString()
                    };
                }

                renderers.Add(new RendererCapabilitySet
                {
                    Name = renderer.GetProperty("name").GetString() ?? "",
                    Platform = renderer.GetProperty("platform").GetString() ?? "",
                    Description = renderer.GetProperty("description").GetString(),
                    Packages = renderer.GetProperty("packages").EnumerateArray()
                        .Select(item => item.GetString() ?? "")
                        .Where(item => item.Length > 0)
                        .ToList(),
                    Capabilities = capabilities
                });
            }

            return _renderers = renderers;
        }
        catch (Exception)
        {
            return _renderers = [];
        }
    }

    /// <summary>Finds a renderer by name or platform.</summary>
    public static RendererCapabilitySet? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var value = name.Trim().ToLowerInvariant();
        value = value switch
        {
            "win" or "windows" or "win32" => "desktop",
            "browser" or "wasm" or "webassembly" => "web",
            "android" => "android",
            _ => value
        };

        return All().FirstOrDefault(renderer =>
            renderer.Name.Equals(value, StringComparison.OrdinalIgnoreCase)
            || renderer.Platform.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Inspects the renderer selected by a project.</summary>
    public static RendererInspection Inspect(DonkehProject project)
    {
        var renderer = project.Renderer;

        return new RendererInspection
        {
            Active = renderer.Name,
            Platform = renderer.Platform,
            Package = renderer.Package,
            Configured = renderer.Configured,
            TargetFrameworks = project.TargetFrameworks,
            Available = project.Renderers
                .Where(item => item.Configured)
                .Select(item => item.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Project = project.ProjectFile,
            Capabilities = null
        };
    }
}
