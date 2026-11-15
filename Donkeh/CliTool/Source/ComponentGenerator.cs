using System.Xml.Linq;

namespace Donkeh.Cli;

/// <summary>Generates a component source file and its stylesheet.</summary>
internal static class ComponentGenerator
{
    /// <summary>Creates component files and registers the stylesheet.</summary>
    /// <param name="componentName">User-supplied component name.</param>
    /// <param name="projectPath">Optional project file or project directory.</param>
    /// <param name="force">Whether existing files may be replaced.</param>
    /// <returns>The paths written or updated.</returns>
    public static IReadOnlyList<string> Generate(
        string componentName,
        string? projectPath,
        bool force)
    {
        var projectFile = FindProject(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectFile)
            ?? throw new CliException($"Could not resolve the directory of '{projectFile}'.");

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
        var componentFile = Path.Combine(componentDirectory, className + ".cs");
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

    /// <summary>Finds the project file to update.</summary>
    private static string FindProject(string? projectPath)
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
