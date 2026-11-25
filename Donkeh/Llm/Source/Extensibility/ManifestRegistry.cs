using System.Reflection;
using System.Text.Json;
using Donkeh.Llm.Components;
using Donkeh.Llm.Project;

namespace Donkeh.Llm.Extensibility;

/// <summary>A manifest loaded from a project or installed package.</summary>
public sealed class LoadedManifest
{
    /// <summary>Parsed manifest.</summary>
    public required DonkehLlmManifest Manifest { get; init; }

    /// <summary>Absolute manifest path.</summary>
    public required string Path { get; init; }

    /// <summary>Directory that owns relative provider paths.</summary>
    public required string Root { get; init; }
}

/// <summary>An executable provider loaded from a manifest.</summary>
public sealed class LoadedProvider
{
    /// <summary>Provider implementation.</summary>
    public required ILlmCapabilityProvider Provider { get; init; }

    /// <summary>Package that registered the provider.</summary>
    public required string Package { get; init; }

    /// <summary>Manifest that declared the provider.</summary>
    public required string ManifestPath { get; init; }
}

/// <summary>Discovers declarative and executable LLM capabilities from packages.</summary>
public static class ManifestRegistry
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Discovers manifests from the project, environment and installed packages.</summary>
    public static (List<LoadedManifest> Manifests, List<LoadedProvider> Providers, List<string> Warnings)
        Discover(DonkehProject? project, string workingDirectory, bool includeProviders)
    {
        var manifests = new List<LoadedManifest>();
        var providers = new List<LoadedProvider>();
        var warnings = new List<string>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in ProjectManifestPaths(workingDirectory, project))
        {
            Add(path);
        }

        if (project is not null)
        {
            foreach (var package in ProjectDependencyResolver.ResolvePackages(project))
            {
                foreach (var candidate in new[]
                {
                    Path.Combine(package.Root, "donkeh.llm.json"),
                    Path.Combine(package.Root, ".donkeh", "llm.json"),
                    Path.Combine(package.Root, "contentFiles", "any", "any", "donkeh.llm.json")
                })
                {
                    Add(candidate);
                }
            }
        }

        var environment = Environment.GetEnvironmentVariable("DONKEH_LLM_MANIFEST");

        if (!string.IsNullOrWhiteSpace(environment))
        {
            foreach (var item in environment.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Add(item);
            }
        }

        foreach (var manifest in manifests)
        {
            if (!includeProviders || manifest.Manifest.Provider is null)
            {
                continue;
            }

            var provider = LoadProvider(manifest, warnings);

            if (provider is not null)
            {
                providers.Add(provider);
            }
        }

        return (manifests, providers, warnings);

        void Add(string path)
        {
            if (!File.Exists(path) || !paths.Add(Path.GetFullPath(path)))
            {
                return;
            }

            try
            {
                var manifest = JsonSerializer.Deserialize<DonkehLlmManifest>(
                    File.ReadAllText(path), Options);

                if (manifest is null)
                {
                    warnings.Add($"{path}: manifest is empty.");
                    return;
                }

                if (manifest.SchemaVersion != 1)
                {
                    warnings.Add($"{path}: unsupported schemaVersion {manifest.SchemaVersion}; expected 1.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(manifest.Package))
                {
                    warnings.Add($"{path}: manifest package is required.");
                    return;
                }

                manifests.Add(new LoadedManifest
                {
                    Manifest = manifest,
                    Path = Path.GetFullPath(path),
                    Root = Path.GetDirectoryName(Path.GetFullPath(path)) ?? workingDirectory
                });
            }
            catch (Exception exception)
            {
                warnings.Add($"{path}: {exception.Message}");
            }
        }
    }

    /// <summary>Returns component metadata contributed by discovered manifests.</summary>
    public static List<ComponentMetadata> Components(DonkehProject? project, string workingDirectory)
    {
        var (manifests, _, _) = Discover(project, workingDirectory, includeProviders: false);
        var result = new List<ComponentMetadata>();

        foreach (var manifest in manifests)
        {
            foreach (var declared in manifest.Manifest.Components)
            {
                if (string.IsNullOrWhiteSpace(declared.Name))
                {
                    continue;
                }

                result.Add(new ComponentMetadata
                {
                    Name = declared.Name,
                    Namespace = declared.Namespace,
                    Package = declared.Package ?? manifest.Manifest.Package,
                    Source = null,
                    Kind = string.IsNullOrWhiteSpace(declared.Kind) ? "assembly" : declared.Kind,
                    BaseType = null,
                    Description = declared.Description,
                    PropsType = declared.PropsType,
                    Properties = declared.Properties,
                    Events = declared.Events,
                    SupportsChildren = declared.SupportsChildren,
                    SupportsState = declared.SupportsState,
                    Lifecycle = declared.Lifecycle,
                    Dependencies = declared.Dependencies,
                    Examples = declared.Examples,
                    Stylesheets = declared.Stylesheets,
                    Tags = declared.Tags,
                    IsAbstract = false
                });
            }
        }

        return result;
    }

    private static IEnumerable<string> ProjectManifestPaths(string workingDirectory, DonkehProject? project)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(workingDirectory)
        };

        if (project is not null)
        {
            directories.Add(project.ProjectDirectory);
        }

        foreach (var directory in directories)
        {
            yield return Path.Combine(directory, "donkeh.llm.json");
            yield return Path.Combine(directory, ".donkeh", "llm.json");

            var folder = Path.Combine(directory, ".donkeh", "llm");

            if (Directory.Exists(folder))
            {
                IEnumerable<string> files;

                try
                {
                    files = Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
                }
                catch (Exception)
                {
                    files = [];
                }

                foreach (var file in files)
                {
                    yield return file;
                }
            }
        }
    }

    private static LoadedProvider? LoadProvider(LoadedManifest manifest, List<string> warnings)
    {
        var declaration = manifest.Manifest.Provider!;

        if (string.IsNullOrWhiteSpace(declaration.Assembly) || string.IsNullOrWhiteSpace(declaration.Type))
        {
            warnings.Add($"{manifest.Path}: provider.assembly and provider.type are required.");
            return null;
        }

        if (Path.IsPathRooted(declaration.Assembly))
        {
            warnings.Add($"{manifest.Path}: provider.assembly must be relative to the manifest.");
            return null;
        }

        var root = Path.GetFullPath(manifest.Root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var assemblyPath = Path.GetFullPath(Path.Combine(root, declaration.Assembly));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!assemblyPath.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            warnings.Add($"{manifest.Path}: provider.assembly escapes the manifest directory.");
            return null;
        }

        if (!File.Exists(assemblyPath))
        {
            warnings.Add($"{manifest.Path}: provider assembly not found: {declaration.Assembly}");
            return null;
        }

        try
        {
            var assembly = Assembly.LoadFrom(assemblyPath);
            var type = assembly.GetType(declaration.Type, throwOnError: false);

            if (type is null || !typeof(ILlmCapabilityProvider).IsAssignableFrom(type))
            {
                warnings.Add($"{manifest.Path}: provider type '{declaration.Type}' was not found or does not implement ILlmCapabilityProvider.");
                return null;
            }

            if (Activator.CreateInstance(type) is not ILlmCapabilityProvider provider)
            {
                warnings.Add($"{manifest.Path}: provider type '{declaration.Type}' could not be constructed.");
                return null;
            }

            return new LoadedProvider
            {
                Provider = provider,
                Package = manifest.Manifest.Package,
                ManifestPath = manifest.Path
            };
        }
        catch (Exception exception)
        {
            warnings.Add($"{manifest.Path}: provider load failed: {exception.Message}");
            return null;
        }
    }
}
