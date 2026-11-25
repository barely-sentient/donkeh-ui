using Donkeh.Llm.Project;
using Donkeh.Llm.Extensibility;

namespace Donkeh.Llm.Capabilities;

/// <summary>Built-in plus package-registered capability catalogue.</summary>
public sealed class CapabilityRegistry
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "mcp", "project", "component", "components", "csx", "css", "renderer", "docs",
        "diagnose", "doctor"
    };

    private CapabilityRegistry(
        List<LlmCapability> capabilities,
        List<LoadedManifest> manifests,
        List<LoadedProvider> providers,
        List<string> warnings)
    {
        Capabilities = capabilities;
        Manifests = manifests;
        Providers = providers;
        Warnings = warnings;
    }

    /// <summary>All capabilities in discovery order.</summary>
    public IReadOnlyList<LlmCapability> Capabilities { get; }

    /// <summary>Loaded package manifests.</summary>
    public IReadOnlyList<LoadedManifest> Manifests { get; }

    /// <summary>Loaded executable providers.</summary>
    public IReadOnlyList<LoadedProvider> Providers { get; }

    /// <summary>Non-fatal discovery problems.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Builds the catalogue for a working directory.</summary>
    public static CapabilityRegistry Build(
        string workingDirectory,
        string? projectPath = null,
        bool includeProviders = false)
    {
        DonkehProject? project = null;

        try
        {
            var projectFile = ProjectLocator.Find(workingDirectory, projectPath);

            if (projectFile is not null)
            {
                project = ProjectInspector.ReadProject(projectFile);
            }
        }
        catch (LlmException)
        {
            // Discovery still works outside a project.
        }

        var (manifests, providers, warnings) =
            ManifestRegistry.Discover(project, workingDirectory, includeProviders);
        var capabilities = BuiltInCapabilities.All();
        var names = capabilities.Select(capability => capability.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mcpNames = capabilities
            .Where(capability => capability.McpName is not null)
            .Select(capability => capability.McpName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var manifest in manifests)
        {
            foreach (var capability in manifest.Manifest.Capabilities)
            {
                var name = capability.Name.Trim();

                if (string.IsNullOrWhiteSpace(name))
                {
                    warnings.Add($"{manifest.Path}: a capability is missing its name.");
                    continue;
                }

                if (names.Contains(name) || IsReserved(name))
                {
                    warnings.Add($"{manifest.Path}: capability '{name}' collides with a built-in name and was ignored.");
                    continue;
                }

                if (!HasPackagePrefix(name, manifest.Manifest.Package))
                {
                    warnings.Add($"{manifest.Path}: capability '{name}' must start with the package's first namespace segment ('{PackagePrefix(manifest.Manifest.Package)}.').");
                    continue;
                }

                var mcpName = name.Replace('.', '_');

                if (mcpNames.Contains(mcpName))
                {
                    warnings.Add($"{manifest.Path}: MCP tool name '{mcpName}' is already in use; capability '{name}' will be CLI-only.");
                    mcpName = null!;
                }
                else
                {
                    mcpNames.Add(mcpName);
                }

                names.Add(name);
                capabilities.Add(new LlmCapability
                {
                    Name = name,
                    McpName = capability.Mcp && !string.IsNullOrWhiteSpace(mcpName) ? mcpName : null,
                    Category = string.IsNullOrWhiteSpace(capability.Category) ? "Package" : capability.Category,
                    Summary = string.IsNullOrWhiteSpace(capability.Summary) ? name : capability.Summary,
                    Description = capability.Description,
                    Usage = capability.Usage ?? $"donkeh mcp capabilities {name}",
                    Arguments = capability.Arguments,
                    Output = capability.Output ?? "Static JSON declared by the package manifest.",
                    Examples = capability.Examples,
                    Tags = capability.Tags,
                    RegisteredBy = manifest.Manifest.Package,
                    VisibleInShow = capability.VisibleInShow,
                    WorksOutsideProject = capability.WorksOutsideProject
                });
            }
        }

        foreach (var provider in providers)
        {
            IEnumerable<LlmCapability> provided;

            try
            {
                provided = provider.Provider.GetCapabilities() ?? [];
            }
            catch (Exception exception)
            {
                warnings.Add($"{provider.ManifestPath}: provider GetCapabilities failed: {exception.Message}");
                continue;
            }

            foreach (var capability in provided)
            {
                if (capability is null || string.IsNullOrWhiteSpace(capability.Name))
                {
                    continue;
                }

                var name = capability.Name.Trim();

                if (names.Contains(name) || IsReserved(name))
                {
                    warnings.Add($"{provider.ManifestPath}: provider capability '{name}' collides with an existing name and was ignored.");
                    continue;
                }

                if (!HasPackagePrefix(name, provider.Package))
                {
                    warnings.Add($"{provider.ManifestPath}: provider capability '{name}' must start with the package's first namespace segment ('{PackagePrefix(provider.Package)}.').");
                    continue;
                }

                var mcpName = capability.McpName ?? name.Replace('.', '_');

                if (mcpNames.Contains(mcpName))
                {
                    warnings.Add($"{provider.ManifestPath}: provider MCP tool '{mcpName}' is already in use; capability '{name}' will be CLI-only.");
                    mcpName = null!;
                }
                else
                {
                    mcpNames.Add(mcpName);
                }

                names.Add(name);
                capabilities.Add(new LlmCapability
                {
                    Name = name,
                    McpName = mcpName,
                    Category = capability.Category,
                    Summary = capability.Summary,
                    Description = capability.Description,
                    Usage = capability.Usage,
                    Arguments = capability.Arguments,
                    Output = capability.Output,
                    Examples = capability.Examples,
                    Tags = capability.Tags,
                    RegisteredBy = provider.Package,
                    VisibleInShow = capability.VisibleInShow,
                    WorksOutsideProject = capability.WorksOutsideProject,
                    IsProviderBacked = true
                });
            }
        }

        return new CapabilityRegistry(capabilities, manifests, providers, warnings);
    }

    /// <summary>Finds a capability by name or MCP name.</summary>
    public LlmCapability? Find(string name)
    {
        return Capabilities.FirstOrDefault(capability =>
            capability.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? Capabilities.FirstOrDefault(capability =>
                string.Equals(capability.McpName, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds a manifest capability result by name.</summary>
    public LoadedManifest? FindManifest(string capabilityName, out ManifestCapability? capability)
    {
        foreach (var manifest in Manifests)
        {
            capability = manifest.Manifest.Capabilities.FirstOrDefault(item =>
                item.Name.Equals(capabilityName, StringComparison.OrdinalIgnoreCase));

            if (capability is not null)
            {
                return manifest;
            }
        }

        capability = null;
        return null;
    }

    private static bool IsReserved(string name)
    {
        var first = name.Split('.')[0];
        return Reserved.Contains(first);
    }

    private static bool HasPackagePrefix(string capability, string package)
    {
        var prefix = PackagePrefix(package);
        return prefix.Length == 0
            || capability.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static string PackagePrefix(string package)
    {
        var first = package.Split('.')[0];
        return new string(first.Where(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_').ToArray()).ToLowerInvariant();
    }
}
