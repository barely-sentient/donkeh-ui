using System.Text.Json;
using System.Xml.Linq;

namespace Donkeh.Llm.Project;

/// <summary>Reads the project files and restore output an agent needs to orient itself.</summary>
public static class ProjectInspector
{
    private static readonly string[] DonkehSettingNames =
    [
        "TargetFramework", "TargetFrameworks", "RootNamespace", "AssemblyName", "OutputType",
        "DonkehVersion", "DonkehTargetFrameworks", "UseLocalDonkeh", "DonkehRoot",
        "DonkehDesktop", "DonkehAndroid", "DonkehWeb", "IsPackable", "PackageId",
        "ApplicationId", "ApplicationTitle", "RuntimeIdentifier", "RuntimeIdentifiers",
        "SupportedOSPlatformVersion", "LangVersion", "Nullable", "ImplicitUsings",
        "DefaultItemExcludes"
    ];

    /// <summary>Reads a project, or throws a structured error when none exists.</summary>
    public static DonkehProject Read(string workingDirectory, string? projectPath)
    {
        var projectFile = ProjectLocator.Find(workingDirectory, projectPath)
            ?? throw new LlmException(
                "LLM1005",
                "No Donkeh project was found.",
                "Run inside a project directory or pass --project <path>.");

        return ReadProject(projectFile);
    }

    /// <summary>Reads a project from an absolute project file path.</summary>
    public static DonkehProject ReadProject(string projectFile)
    {
        projectFile = Path.GetFullPath(projectFile);
        var directory = ProjectLocator.DirectoryOf(projectFile);
        var warnings = new List<LlmDiagnostic>();
        XDocument document;

        try
        {
            document = XDocument.Load(projectFile);
        }
        catch (Exception exception)
        {
            throw new LlmException("LLM1006", $"Could not read '{Path.GetFileName(projectFile)}': {exception.Message}");
        }

        var properties = ReadProperties(document);
        var name = Value(properties, "AssemblyName")
            ?? Value(properties, "PackageId")
            ?? Path.GetFileNameWithoutExtension(projectFile);
        var rootNamespace = Value(properties, "RootNamespace") ?? Identifiers.Namespace(name);
        var outputType = Value(properties, "OutputType");
        var targetFrameworks = ReadTargetFrameworks(properties, document);
        var packageReferences = ReadPackageReferences(document);
        var projectReferences = ReadProjectReferences(document, directory);
        var assets = ReadAssets(directory);
        var packages = ResolvePackages(packageReferences, assets);
        var csx = ReadCsx(directory, warnings);
        var renderers = ReadRenderers(properties, packages, directory, outputType);
        var features = ReadFeatures(directory, properties, packages, csx, projectReferences);
        var projectType = ReadProjectType(directory, properties, outputType);
        var stylesheets = ReadStylesheets(document);
        var usesLocal = UsesLocalCheckout(document, properties);

        return new DonkehProject
        {
            ProjectFile = projectFile,
            ProjectDirectory = directory,
            Name = name,
            RootNamespace = rootNamespace,
            ProjectType = projectType,
            OutputType = outputType,
            TargetFrameworks = targetFrameworks,
            DonkehVersion = Value(properties, "DonkehVersion")
                ?? packages.FirstOrDefault(package => package.IsDonkeh)?.ResolvedVersion,
            UsesLocalCheckout = usesLocal,
            Packages = packages,
            ProjectReferences = projectReferences,
            Settings = DonkehSettingNames
                .Where(properties.ContainsKey)
                .ToDictionary(key => key, key => Value(properties, key), StringComparer.OrdinalIgnoreCase),
            Csx = csx,
            Renderer = renderers.FirstOrDefault(renderer => renderer.Configured) ?? renderers[0],
            Renderers = renderers,
            Features = features,
            Stylesheets = stylesheets,
            HasRestoredAssets = assets is not null,
            Warnings = warnings
        };
    }

    private static Dictionary<string, string> ReadProperties(XDocument document)
    {
        return document
            .Descendants()
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup")
            .GroupBy(element => element.Name.LocalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? Value(IReadOnlyDictionary<string, string> properties, string name) =>
        properties.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static List<string> ReadTargetFrameworks(
        IReadOnlyDictionary<string, string> properties,
        XDocument document)
    {
        var values = new List<string>();
        var combined = Value(properties, "TargetFrameworks");

        if (!string.IsNullOrWhiteSpace(combined))
        {
            values.AddRange(combined.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var single = Value(properties, "TargetFramework");

        if (!string.IsNullOrWhiteSpace(single) && !values.Contains(single, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(single);
        }

        // A local checkout project writes $(DonkehTargetFrameworks). The imported
        // props file is the only place that resolves the expression, so expose the
        // literal values that are visible without evaluating MSBuild.
        foreach (var element in document.Descendants())
        {
            if (element.Name.LocalName == "DonkehTargetFrameworks" && !string.IsNullOrWhiteSpace(element.Value))
            {
                foreach (var item in element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!values.Contains(item, StringComparer.OrdinalIgnoreCase))
                    {
                        values.Add(item);
                    }
                }
            }
        }

        if (values.Count == 0 && Value(properties, "DonkehDesktop") is "true")
        {
            values.Add("net10.0");
        }

        if (Value(properties, "DonkehAndroid") is "true" && !values.Contains("net10.0-android", StringComparer.OrdinalIgnoreCase))
        {
            values.Add("net10.0-android");
        }

        if (Value(properties, "DonkehWeb") is "true" && !values.Contains("net10.0-browser", StringComparer.OrdinalIgnoreCase))
        {
            values.Add("net10.0-browser");
        }

        return values;
    }

    private static List<(string Id, string? Version, string? Condition)> ReadPackageReferences(
        XDocument document)
    {
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => (
                Id: element.Attribute("Include")?.Value.Trim()
                    ?? element.Attribute("Update")?.Value.Trim()
                    ?? "",
                Version: element.Attribute("Version")?.Value.Trim()
                    ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version")?.Value.Trim(),
                Condition: element.Attribute("Condition")?.Value.Trim()))
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Id))
            .ToList();
    }

    private static List<ProjectProjectReference> ReadProjectReferences(XDocument document, string directory)
    {
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value =>
            {
                var full = Path.GetFullPath(Path.Combine(directory, value!));
                var exists = File.Exists(full);
                var isLibrary = false;

                if (exists)
                {
                    try
                    {
                        var referenced = XDocument.Load(full);
                        isLibrary = referenced.Descendants()
                            .Any(child => child.Name.LocalName == "IsPackable"
                                && string.Equals(child.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
                    }
                    catch (Exception)
                    {
                        // A malformed referenced project is reported by project diagnostics.
                    }
                }

                return new ProjectProjectReference
                {
                    Include = value!,
                    FullPath = exists ? full : null,
                    Name = exists ? Path.GetFileNameWithoutExtension(full) : null,
                    IsComponentLibrary = isLibrary
                };
            })
            .ToList();
    }

    private static List<ProjectPackageReference> ResolvePackages(
        List<(string Id, string? Version, string? Condition)> references,
        AssetsInfo? assets)
    {
        return references.Select(reference =>
        {
            var resolved = assets?.Libraries.TryGetValue(reference.Id, out var version) == true
                ? version
                : null;

            return new ProjectPackageReference
            {
                Id = reference.Id,
                RequestedVersion = reference.Version,
                ResolvedVersion = resolved,
                Condition = reference.Condition,
                IsDonkeh = IsDonkehPackage(reference.Id),
                Kind = PackageKind(reference.Id)
            };
        }).ToList();
    }

    private static bool IsDonkehPackage(string id) =>
        id.StartsWith("Donkeh.", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("CSX.", StringComparison.OrdinalIgnoreCase);

    private static string PackageKind(string id)
    {
        if (id.Contains("Windowing", StringComparison.OrdinalIgnoreCase))
        {
            return "renderer";
        }

        if (id.Contains("Inspector", StringComparison.OrdinalIgnoreCase)
            || id.Contains("HotReload", StringComparison.OrdinalIgnoreCase)
            || id.Contains("Testing", StringComparison.OrdinalIgnoreCase))
        {
            return "tooling";
        }

        if (id.StartsWith("CSX.", StringComparison.OrdinalIgnoreCase))
        {
            return "tooling";
        }

        return id.StartsWith("Donkeh.", StringComparison.OrdinalIgnoreCase) ? "framework" : "other";
    }

    private static CsxConfiguration ReadCsx(string directory, List<LlmDiagnostic> warnings)
    {
        var configFile = FindUp(directory, "csx.json", 4);
        var hasCsx = configFile is not null
            || File.Exists(Path.Combine(directory, "Program.cx"))
            || HasFiles(directory, "*.cx");

        if (configFile is null)
        {
            return new CsxConfiguration
            {
                Present = hasCsx,
                File = null,
                Valid = true,
                FrameworkHtmlClass = hasCsx ? "_" : null,
                FrameworkHtmlClassMethod = hasCsx ? "H" : null,
                FrameworkComponentMethod = hasCsx ? "C" : null,
                FrameworkFragmentMethod = hasCsx ? "F" : null,
                GeneratedFileOutput = hasCsx ? "src/generated" : null
            };
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configFile));
            var root = document.RootElement;

            return new CsxConfiguration
            {
                Present = true,
                File = configFile,
                Valid = true,
                FrameworkHtmlClass = StringProperty(root, "FrameworkHtmlClass") ?? "_",
                FrameworkHtmlClassMethod = StringProperty(root, "FrameworkHtmlClassMethod") ?? "H",
                FrameworkComponentMethod = StringProperty(root, "FrameworkComponentMethod") ?? "C",
                FrameworkFragmentMethod = StringProperty(root, "FrameworkFragmentMethod") ?? "F",
                GeneratedFileOutput = StringProperty(root, "GeneratedFileOutput") ?? "src/generated"
            };
        }
        catch (Exception exception)
        {
            warnings.Add(LlmDiagnostics.Warning(
                "CSXCFG001",
                $"{Path.GetFileName(configFile)} is not valid JSON: {exception.Message}",
                new LlmLocation { File = configFile }));

            return new CsxConfiguration
            {
                Present = true,
                File = configFile,
                Valid = false,
                Error = exception.Message
            };
        }
    }

    private static string? StringProperty(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static List<RendererSelection> ReadRenderers(
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyList<ProjectPackageReference> packages,
        string directory,
        string? outputType)
    {
        var renderers = new List<RendererSelection>();
        var hasDesktopPackage = packages.Any(package =>
            package.Id.Equals("Donkeh.Windowing", StringComparison.OrdinalIgnoreCase));
        var hasAndroidPackage = packages.Any(package =>
            package.Id.Equals("Donkeh.Windowing.Android", StringComparison.OrdinalIgnoreCase));
        var hasWebPackage = packages.Any(package =>
            package.Id.Equals("Donkeh.Windowing.Browser", StringComparison.OrdinalIgnoreCase));
        var desktop = IsTrue(properties, "DonkehDesktop") || hasDesktopPackage;
        var isExecutable = string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase);
        var android = IsTrue(properties, "DonkehAndroid") || hasAndroidPackage;
        var web = IsTrue(properties, "DonkehWeb") || hasWebPackage;

        if (desktop || (isExecutable && !android && !web))
        {
            renderers.Add(new RendererSelection
            {
                Name = "desktop",
                Platform = "Desktop",
                Package = hasDesktopPackage
                    ? "Donkeh.Windowing"
                    : FindProjectRenderer(directory, "Windowing"),
                Configured = desktop
            });
        }

        if (android)
        {
            renderers.Add(new RendererSelection
            {
                Name = "android",
                Platform = "Android",
                Package = hasAndroidPackage
                    ? "Donkeh.Windowing.Android"
                    : FindProjectRenderer(directory, "Windowing.Android"),
                Configured = true
            });
        }

        if (web)
        {
            renderers.Add(new RendererSelection
            {
                Name = "web",
                Platform = "Browser",
                Package = hasWebPackage
                    ? "Donkeh.Windowing.Browser"
                    : FindProjectRenderer(directory, "Windowing.Browser"),
                Configured = true
            });
        }

        if (renderers.Count == 0)
        {
            renderers.Add(new RendererSelection
            {
                Name = "unknown",
                Platform = "Unknown",
                Package = null,
                Configured = false
            });
        }

        return renderers;
    }

    private static string? FindProjectRenderer(string directory, string suffix)
    {
        try
        {
            var projects = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly)
                : [];

            return projects.FirstOrDefault(project =>
                Path.GetFileName(project).Contains(suffix, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<string> ReadFeatures(
        string directory,
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyList<ProjectPackageReference> packages,
        CsxConfiguration csx,
        IReadOnlyList<ProjectProjectReference> projectReferences)
    {
        var features = new List<string>();

        if (csx.Present)
        {
            features.Add("csx");
        }

        if (packages.Any(package => package.Id.Equals("Donkeh.HotReload", StringComparison.OrdinalIgnoreCase))
            || projectReferences.Any(reference =>
                reference.Include.Contains("HotReload", StringComparison.OrdinalIgnoreCase)))
        {
            features.Add("hot-reload");
        }

        if (packages.Any(package => package.Id.Equals("Donkeh.Inspector", StringComparison.OrdinalIgnoreCase))
            || projectReferences.Any(reference =>
                reference.Include.Contains("Inspector", StringComparison.OrdinalIgnoreCase)))
        {
            features.Add("inspector");
        }

        if (packages.Any(package => package.Id.Equals("Donkeh.Testing", StringComparison.OrdinalIgnoreCase))
            || projectReferences.Any(reference =>
                reference.Include.Contains("Testing", StringComparison.OrdinalIgnoreCase)))
        {
            features.Add("component-testing");
        }

        if (UsesLocalCheckoutValue(properties))
        {
            features.Add("local-framework");
        }

        if (IsTrue(properties, "DonkehDesktop")
            || packages.Any(package => package.Id.Equals("Donkeh.Windowing", StringComparison.OrdinalIgnoreCase)))
        {
            features.Add("desktop-target");
        }

        if (IsTrue(properties, "DonkehAndroid")
            || packages.Any(package => package.Id.Equals("Donkeh.Windowing.Android", StringComparison.OrdinalIgnoreCase)))
        {
            features.Add("android-target");
        }

        if (IsTrue(properties, "DonkehWeb")
            || packages.Any(package => package.Id.Equals("Donkeh.Windowing.Browser", StringComparison.OrdinalIgnoreCase)))
        {
            features.Add("web-target");
        }

        if (IsTrue(properties, "IsPackable")
            || projectReferences.Any(reference => reference.IsComponentLibrary)
            || Path.GetFileName(directory).Equals("test-container", StringComparison.OrdinalIgnoreCase))
        {
            features.Add("component-library");
        }

        return features.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsTrue(IReadOnlyDictionary<string, string> properties, string name) =>
        string.Equals(properties.GetValueOrDefault(name), "true", StringComparison.OrdinalIgnoreCase);

    private static bool UsesLocalCheckout(XDocument document, IReadOnlyDictionary<string, string> properties) =>
        UsesLocalCheckoutValue(properties)
        || document.Descendants().Any(element =>
            element.Name.LocalName == "Import"
            && element.Attribute("Project")?.Value.Contains("Donkeh.props", StringComparison.OrdinalIgnoreCase) == true
            && string.IsNullOrWhiteSpace(element.Attribute("Condition")?.Value));

    private static bool UsesLocalCheckoutValue(IReadOnlyDictionary<string, string> properties) =>
        IsTrue(properties, "UseLocalDonkeh");

    private static string ReadProjectType(
        string directory,
        IReadOnlyDictionary<string, string> properties,
        string? outputType)
    {
        if (Path.GetFileName(directory).Equals("test-container", StringComparison.OrdinalIgnoreCase)
            || directory.Split(Path.DirectorySeparatorChar).Contains("test-container", StringComparer.OrdinalIgnoreCase))
        {
            return "test-container";
        }

        if (IsTrue(properties, "IsPackable"))
        {
            return "framework";
        }

        if (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase))
        {
            return "application";
        }

        return "library";
    }

    private static List<string> ReadStylesheets(XDocument document) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == "EmbeddedResource")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value)
                && value!.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            .Select(value => value!.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? FindUp(string directory, string fileName, int maximumLevels)
    {
        var current = new DirectoryInfo(directory);

        for (var level = 0; level < maximumLevels && current is not null; level++)
        {
            var candidate = Path.Combine(current.FullName, fileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool HasFiles(string directory, string pattern)
    {
        try
        {
            return Directory.Exists(directory)
                && Directory.GetFiles(directory, pattern, SearchOption.AllDirectories).Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed class AssetsInfo
    {
        public Dictionary<string, string> Libraries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> PackageFolders { get; } = [];
    }

    private static AssetsInfo? ReadAssets(string directory)
    {
        var path = Path.Combine(directory, "obj", "project.assets.json");

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var info = new AssetsInfo();

            if (document.RootElement.TryGetProperty("libraries", out var libraries))
            {
                foreach (var library in libraries.EnumerateObject())
                {
                    var slash = library.Name.IndexOf('/');

                    if (slash > 0)
                    {
                        info.Libraries[library.Name[..slash]] = library.Name[(slash + 1)..];
                    }
                }
            }

            if (document.RootElement.TryGetProperty("packageFolders", out var folders))
            {
                foreach (var folder in folders.EnumerateObject())
                {
                    info.PackageFolders.Add(folder.Name);
                }
            }

            return info;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
