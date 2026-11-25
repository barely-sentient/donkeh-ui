using System.Text.Json;

namespace Donkeh.Llm.Project;

/// <summary>One assembly resolved from the project's restore output or project references.</summary>
public sealed class ResolvedAssembly
{
    /// <summary>Absolute assembly path.</summary>
    public required string Path { get; init; }

    /// <summary>Package id that supplied the assembly, or the project name.</summary>
    public string? PackageId { get; init; }

    /// <summary>Package version, when known.</summary>
    public string? Version { get; init; }
}

/// <summary>One package resolved from the project's restore output.</summary>
public sealed class ResolvedPackage
{
    /// <summary>Package id.</summary>
    public required string Id { get; init; }

    /// <summary>Resolved package version.</summary>
    public required string Version { get; init; }

    /// <summary>Absolute package root directory.</summary>
    public required string Root { get; init; }
}

/// <summary>
/// Finds assemblies a project can load without running the project. The reader
/// uses <c>project.assets.json</c> and build output directories, never the
/// project's own process.
/// </summary>
public static class ProjectDependencyResolver
{
    /// <summary>Resolves package roots from the last restore.</summary>
    public static List<ResolvedPackage> ResolvePackages(DonkehProject project)
    {
        var assets = ReadAssets(project.ProjectDirectory);

        return assets is null
            ? []
            : assets.Libraries.Values
                .Select(library => new ResolvedPackage
                {
                    Id = library.Id,
                    Version = library.Version,
                    Root = library.PackageRoot
                })
                .ToList();
    }

    /// <summary>Resolves candidate assemblies for component discovery.</summary>
    public static List<ResolvedAssembly> ResolveAssemblies(DonkehProject project)
    {
        var results = new Dictionary<string, ResolvedAssembly>(StringComparer.OrdinalIgnoreCase);
        var assets = ReadAssets(project.ProjectDirectory);

        if (assets is not null)
        {
            foreach (var library in assets.Libraries.Values)
            {
                foreach (var compile in library.CompilePaths)
                {
                    var path = Path.GetFullPath(Path.Combine(library.PackageRoot, compile));

                    if (IsCandidate(path))
                    {
                        results[path] = new ResolvedAssembly
                        {
                            Path = path,
                            PackageId = library.Id,
                            Version = library.Version
                        };
                    }
                }
            }
        }

        foreach (var output in OutputAssemblies(project.ProjectDirectory))
        {
            if (!results.ContainsKey(output))
            {
                results[output] = new ResolvedAssembly
                {
                    Path = output,
                    PackageId = project.Name,
                    Version = null
                };
            }
        }

        return results.Values
            .OrderBy(assembly => assembly.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(assembly => assembly.Path, StringComparer.OrdinalIgnoreCase)
            .Take(400)
            .ToList();
    }

    private static bool IsCandidate(string path)
    {
        if (!File.Exists(path) || !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(path);

        return !name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("System", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("Newtonsoft.", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("Silk.NET", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("Stb", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("Meziantou.", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> OutputAssemblies(string projectDirectory)
    {
        var bin = Path.Combine(projectDirectory, "bin");

        if (!Directory.Exists(bin))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(bin, "*.dll", SearchOption.AllDirectories)
                .Where(IsCandidate)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(100);
        }
        catch (Exception)
        {
            return [];
        }
    }

    private sealed class AssetsData
    {
        public Dictionary<string, AssetsLibrary> Libraries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> PackageFolders { get; } = [];
    }

    private sealed class AssetsLibrary
    {
        public required string Id { get; init; }
        public required string Version { get; init; }
        public required string PackageRoot { get; init; }
        public List<string> CompilePaths { get; } = [];
    }

    private static AssetsData? ReadAssets(string directory)
    {
        var path = Path.Combine(directory, "obj", "project.assets.json");

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var data = new AssetsData();

            if (root.TryGetProperty("packageFolders", out var folders))
            {
                foreach (var folder in folders.EnumerateObject())
                {
                    data.PackageFolders.Add(folder.Name);
                }
            }

            if (!root.TryGetProperty("libraries", out var libraries))
            {
                return data;
            }

            foreach (var library in libraries.EnumerateObject())
            {
                var slash = library.Name.IndexOf('/');

                if (slash <= 0)
                {
                    continue;
                }

                var id = library.Name[..slash];
                var version = library.Name[(slash + 1)..];
                var relativePath = library.Value.TryGetProperty("path", out var pathElement)
                    ? pathElement.GetString()
                    : $"{id.ToLowerInvariant()}/{version.ToLowerInvariant()}";
                var packageRoot = FindPackageRoot(data.PackageFolders, relativePath ?? "");

                if (packageRoot is null)
                {
                    continue;
                }

                data.Libraries[id] = new AssetsLibrary
                {
                    Id = id,
                    Version = version,
                    PackageRoot = packageRoot
                };
            }

            if (root.TryGetProperty("targets", out var targets))
            {
                foreach (var target in targets.EnumerateObject())
                {
                    foreach (var library in target.Value.EnumerateObject())
                    {
                        var slash = library.Name.IndexOf('/');

                        if (slash <= 0)
                        {
                            continue;
                        }

                        var id = library.Name[..slash];

                        if (!data.Libraries.TryGetValue(id, out var info)
                            || !library.Value.TryGetProperty("compile", out var compile))
                        {
                            continue;
                        }

                        foreach (var item in compile.EnumerateObject())
                        {
                            if (item.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                                && !info.CompilePaths.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
                            {
                                info.CompilePaths.Add(item.Name);
                            }
                        }
                    }

                    // The first target is normally the project's primary TFM.
                    if (data.Libraries.Values.Any(library => library.CompilePaths.Count > 0))
                    {
                        break;
                    }
                }
            }

            return data;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? FindPackageRoot(List<string> packageFolders, string relativePath)
    {
        foreach (var folder in packageFolders)
        {
            var candidate = Path.Combine(folder, relativePath);

            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        // Fall back to the conventional global packages folder.
        var global = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");
        var fallback = Path.Combine(global, relativePath);

        return Directory.Exists(fallback) ? fallback : null;
    }
}
