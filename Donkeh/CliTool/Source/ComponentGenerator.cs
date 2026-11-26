using System.Xml.Linq;

namespace Donkeh.Cli;

/// <summary>Generates a component source file and its stylesheet.</summary>
internal static class ComponentGenerator
{
    /// <summary>Creates component files and registers the stylesheet.</summary>
    /// <param name="componentName">User-supplied component name.</param>
    /// <param name="projectPath">Optional project file or project directory.</param>
    /// <param name="force">Whether existing files may be replaced.</param>
    /// <param name="csx">
    /// Whether to generate a CSX (<c>.cx</c>) component instead of a plain
    /// C# (<c>.cs</c>) one. <see langword="null"/> follows the project: CSX
    /// projects get <c>.cx</c>, plain C# projects get <c>.cs</c>.
    /// </param>
    /// <returns>The paths written or updated.</returns>
    public static IReadOnlyList<string> Generate(
        string componentName,
        string? projectPath,
        bool force,
        bool? csx = null)
    {
        var projectFile = FindProject(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectFile)
            ?? throw new CliException($"Could not resolve the directory of '{projectFile}'.");
        var useCsx = csx ?? IsCsxProject(projectFile);

        var (projectText, newLine) = TextFiles.Read(projectFile);
        var rootNamespace = ReadRootNamespace(
            projectText,
            Path.GetFileNameWithoutExtension(projectFile));
        var className = Identifiers.ComponentName(componentName);
        var cssClass = Identifiers.KebabCase(className);
        var componentNamespace = rootNamespace.EndsWith(".Components", StringComparison.Ordinal)
            ? rootNamespace
            : rootNamespace + ".Components";

        var componentDirectory = Path.Combine(ComponentRoot(projectFile), className);
        var componentExtension = useCsx ? ".cx" : ".cs";
        var componentFile = Path.Combine(componentDirectory, className + componentExtension);
        var cssFile = Path.Combine(componentDirectory, className + ".css");
        var relativeCss = Path.GetRelativePath(projectDirectory, cssFile).Replace('\\', '/');

        if (!force)
        {
            var existing = new[] { componentFile, cssFile }.Where(File.Exists).ToArray();

            if (existing.Length > 0)
            {
                throw new CliException(
                    "The component already exists: " +
                    string.Join(", ", existing.Select(path => Path.GetFileName(path))) +
                    ". Pass --force to overwrite it.");
            }
        }

        if (useCsx)
        {
            TextFiles.Write(componentFile, $$"""
                using Donkeh.UILab;

                [assembly: Donkeh.UILab.Stylesheet("{{relativeCss}}")]

                namespace {{componentNamespace}};

                /// <summary>{{className}} props.</summary>
                public sealed class {{className}}Props : PropsWithChildren
                {
                }

                /// <summary>{{className}} component.</summary>
                public sealed class {{className}} : Component<{{className}}Props>
                {
                    /// <inheritdoc/>
                    public override Node Render() =>
                        // Equivalent C#: Div(new() { ClassName = "{{cssClass}}" })
                        <div className="{{cssClass}}" />;
                }
                """);
        }
        else
        {
            TextFiles.Write(componentFile, $$"""
                using Donkeh.UILab;
                using static Donkeh.UILab.HtmlHelper;

                [assembly: Donkeh.UILab.Stylesheet("{{relativeCss}}")]

                namespace {{componentNamespace}};

                /// <summary>{{className}} props.</summary>
                public sealed class {{className}}Props : PropsWithChildren
                {
                }

                /// <summary>{{className}} component.</summary>
                public sealed class {{className}} : Component<{{className}}Props>
                {
                    /// <inheritdoc/>
                    public override Node Render() =>
                        Div(new() { ClassName = "{{cssClass}}" });
                }
                """);
        }

        TextFiles.Write(cssFile, $$"""
            .{{cssClass}} {
                display: block;
            }
            """);

        var updatedProject = CsprojEditor.AddEmbeddedResource(
            projectText,
            newLine,
            relativeCss);

        if (!string.Equals(projectText, updatedProject, StringComparison.Ordinal))
        {
            TextFiles.Write(projectFile, updatedProject);
        }

        return [componentFile, cssFile, projectFile];
    }

    /// <summary>
    /// Gets the directory that holds generated component folders for a project.
    /// </summary>
    /// <param name="projectFile">Absolute project file path.</param>
    /// <returns>
    /// <c>Components</c> for a framework library that lives in the <c>src</c>
    /// folder of a framework project; otherwise <c>src/Components</c> under the
    /// project directory.
    /// </returns>
    internal static string ComponentRoot(string projectFile)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)
            ?? throw new CliException($"Could not resolve the directory of '{projectFile}'.");

        return IsFrameworkLibrary(projectFile)
            ? Path.Combine(projectDirectory, "Components")
            : Path.Combine(projectDirectory, "src", "Components");
    }

    /// <summary>
    /// Reports whether a project is the component library of a framework
    /// project, recognised by a sibling <c>test-container</c> project.
    /// </summary>
    /// <param name="projectFile">Absolute project file path.</param>
    /// <returns><see langword="true"/> for a framework library.</returns>
    internal static bool IsFrameworkLibrary(string projectFile)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile);

        if (projectDirectory is null)
        {
            return false;
        }

        var parent = Path.GetDirectoryName(projectDirectory);

        if (parent is null)
        {
            return false;
        }

        var container = Path.Combine(parent, "test-container");

        try
        {
            return Directory.Exists(container)
                && Directory.GetFiles(container, "*.csproj", SearchOption.TopDirectoryOnly).Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Reports whether the project is a CSX-dominant project, based on how it
    /// is built and started: a <c>csx.json</c> configuration file, a
    /// <c>Program.cx</c> entry point, or any <c>.cx</c> source files.
    /// </summary>
    internal static bool IsCsxProject(string? projectPath)
    {
        string? projectFile;

        try
        {
            projectFile = FindProject(projectPath);
        }
        catch (CliException)
        {
            return false;
        }

        var directory = Path.GetDirectoryName(projectFile);

        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var current = new DirectoryInfo(directory);

        for (var level = 0; level < 4 && current is not null; level++)
        {
            if (File.Exists(Path.Combine(current.FullName, "csx.json")))
            {
                return true;
            }

            if (File.Exists(Path.Combine(current.FullName, "Program.cx")))
            {
                return true;
            }

            try
            {
                if (Directory.GetFiles(current.FullName, "*.cx", SearchOption.TopDirectoryOnly).Length > 0)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Ignore inaccessible directories.
            }

            current = current.Parent;
        }

        try
        {
            if (Directory.GetFiles(directory, "*.cx", SearchOption.AllDirectories).Length > 0)
            {
                return true;
            }
        }
        catch (Exception)
        {
            // Ignore inaccessible directories.
        }

        return false;
    }

    /// <summary>Finds the project file to update.</summary>
    internal static string FindProject(string? projectPath)
    {
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var full = Path.GetFullPath(projectPath);

            if (File.Exists(full))
            {
                return full;
            }

            if (Directory.Exists(full))
            {
                return FindProjectInDirectory(full);
            }

            throw new CliException($"Project path not found: {projectPath}");
        }

        return FindProjectInDirectory(Environment.CurrentDirectory);
    }

    /// <summary>Finds exactly one project file in a directory.</summary>
    private static string FindProjectInDirectory(string directory)
    {
        var projects = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);

        if (projects.Length == 1)
        {
            return projects[0];
        }

        if (projects.Length > 1)
        {
            throw new CliException(
                $"Multiple .csproj files were found in '{directory}'. Pass --project <path>.");
        }

        // A framework root has no project at its top level. Its library lives
        // under src/, so resolve that before giving up.
        var library = FindNestedProject(directory, "src");

        if (library is not null)
        {
            return library;
        }

        var container = FindNestedProject(directory, "test-container");

        return container
            ?? throw new CliException($"No .csproj file was found in '{directory}'.");
    }

    /// <summary>
    /// Finds every project that a mode switch should update. A framework root
    /// resolves to both its library and its test container.
    /// </summary>
    /// <param name="projectPath">Optional project file or project directory.</param>
    /// <returns>The absolute project file paths.</returns>
    internal static IReadOnlyList<string> FindModeProjects(string? projectPath)
    {
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var full = Path.GetFullPath(projectPath);

            if (File.Exists(full))
            {
                return [full];
            }

            if (!Directory.Exists(full))
            {
                throw new CliException($"Project path not found: {projectPath}");
            }

            return FindProjectsInDirectory(full);
        }

        return FindProjectsInDirectory(Environment.CurrentDirectory);
    }

    private static IReadOnlyList<string> FindProjectsInDirectory(string directory)
    {
        var projects = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);

        if (projects.Length == 1)
        {
            return [projects[0]];
        }

        if (projects.Length > 1)
        {
            throw new CliException(
                $"Multiple .csproj files were found in '{directory}'. Pass --project <path>.");
        }

        var found = new List<string>();

        foreach (var child in new[] { "src", "test-container" })
        {
            var project = FindNestedProject(directory, child);

            if (project is not null)
            {
                found.Add(project);
            }
        }

        if (found.Count == 0)
        {
            throw new CliException($"No .csproj file was found in '{directory}'.");
        }

        return found;
    }

    private static string? FindNestedProject(string directory, string child)
    {
        var nested = Path.Combine(directory, child);

        if (!Directory.Exists(nested))
        {
            return null;
        }

        var projects = Directory.GetFiles(nested, "*.csproj", SearchOption.TopDirectoryOnly);

        return projects.Length switch
        {
            1 => projects[0],
            > 1 => throw new CliException(
                $"Multiple .csproj files were found in '{nested}'. Pass --project <path>."),
            _ => null
        };
    }

    /// <summary>
    /// Finds CSX projects that a target project references and that must be
    /// transformed before the target can build.
    /// </summary>
    /// <param name="projectFile">Absolute project file path.</param>
    /// <returns>Referenced CSX project files.</returns>
    internal static IReadOnlyList<string> FindCsxDependencies(string projectFile)
    {
        var directory = Path.GetDirectoryName(projectFile);

        if (directory is null)
        {
            return [];
        }

        var (text, _) = TextFiles.Read(projectFile);
        var references = System.Text.RegularExpressions.Regex.Matches(
            text,
            "<ProjectReference\\b[^>]*Include=\"(?<path>[^\"]+)\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var dependencies = new List<string>();

        foreach (System.Text.RegularExpressions.Match reference in references)
        {
            var path = reference.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(directory, path));

            if (File.Exists(candidate)
                && !string.Equals(candidate, projectFile, StringComparison.OrdinalIgnoreCase)
                && IsCsxProject(candidate)
                && !dependencies.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                dependencies.Add(candidate);
            }
        }

        return dependencies;
    }

    /// <summary>Reads RootNamespace, or derives one from the project file name.</summary>
    private static string ReadRootNamespace(string projectText, string projectName)
    {
        try
        {
            var document = XDocument.Parse(projectText);
            var value = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "RootNamespace")
                ?.Value
                .Trim();

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        catch (Exception)
        {
            // Fall through to the project-name-derived namespace.
        }

        return Identifiers.Namespace(projectName);
    }
}
