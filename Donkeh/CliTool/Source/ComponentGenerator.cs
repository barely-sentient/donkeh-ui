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
    /// C# (<c>.cs</c>) one. When <see langword="false"/> the project is
    /// inspected and CSX is used automatically when it is the dominant mode.
    /// </param>
    /// <returns>The paths written or updated.</returns>
    public static IReadOnlyList<string> Generate(
        string componentName,
        string? projectPath,
        bool force,
        bool csx = false)
    {
        var projectFile = FindProject(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectFile)
            ?? throw new CliException($"Could not resolve the directory of '{projectFile}'.");
        var useCsx = csx || IsCsxProject(projectFile);

        var (projectText, newLine) = TextFiles.Read(projectFile);
        var rootNamespace = ReadRootNamespace(
            projectText,
            Path.GetFileNameWithoutExtension(projectFile));
        var className = Identifiers.ComponentName(componentName);
        var cssClass = Identifiers.KebabCase(className);
        var componentNamespace = rootNamespace.EndsWith(".Components", StringComparison.Ordinal)
            ? rootNamespace
            : rootNamespace + ".Components";

        var componentDirectory = Path.Combine(
            projectDirectory,
            "src",
            "Components",
            className);
        var componentExtension = useCsx ? ".cx" : ".cs";
        var componentFile = Path.Combine(componentDirectory, className + componentExtension);
        var cssFile = Path.Combine(componentDirectory, className + ".css");
        var relativeCss = $"src/Components/{className}/{className}.css";

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

                /// <summary>{{className}} component.</summary>
                public sealed class {{className}} : Component
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

                /// <summary>{{className}} component.</summary>
                public sealed class {{className}} : Component
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

        return projects.Length switch
        {
            1 => projects[0],
            0 => throw new CliException($"No .csproj file was found in '{directory}'."),
            _ => throw new CliException(
                $"Multiple .csproj files were found in '{directory}'. Pass --project <path>.")
        };
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
