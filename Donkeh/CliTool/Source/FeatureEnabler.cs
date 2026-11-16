using System.Text.RegularExpressions;

namespace Donkeh.Cli;

/// <summary>Adds optional development features to a project.</summary>
internal static class FeatureEnabler
{
    private const string HotReloadPackage = "Donkeh.HotReload";

    /// <summary>Enables one development feature on a project.</summary>
    /// <param name="feature">The feature name, currently <c>hot-reload</c>.</param>
    /// <param name="projectPath">Project file, project directory or framework root.</param>
    /// <returns>The project file that was updated.</returns>
    /// <exception cref="CliException">
    /// The feature is unknown or no project could be found.
    /// </exception>
    public static string Enable(string feature, string? projectPath)
    {
        if (!string.Equals(feature, "hot-reload", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(feature, "css-hot-reload", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException(
                $"Unknown feature '{feature}'. Available features: hot-reload");
        }

        var projectFile = FindTargetProject(projectPath);
        var (projectText, newLine) = TextFiles.Read(projectFile);

        if (IsHotReloadEnabled(projectText))
        {
            return projectFile;
        }

        var directory = Path.GetDirectoryName(projectFile)
            ?? throw new CliException($"Could not resolve the directory of '{projectFile}'.");

        var item = TryGetCheckoutRoot(projectText, directory) is { } frameworkRoot
            ? ProjectReferenceItem(frameworkRoot, directory, newLine)
            : PackageReferenceItem(projectText, newLine);

        var updated = CsprojEditor.AddItemGroup(projectText, newLine, item);
        TextFiles.Write(projectFile, updated);
        return projectFile;
    }

    /// <summary>Finds the executable project that a command should target.</summary>
    /// <param name="projectPath">Project file, project directory or framework root.</param>
    /// <returns>The absolute project file path.</returns>
    /// <exception cref="CliException">The path is missing or ambiguous.</exception>
    /// <remarks>
    /// A framework root contains no project at the top level, so its
    /// <c>test-container</c> project is selected. Passing a project file uses that
    /// file directly.
    /// </remarks>
    public static string FindTargetProject(string? projectPath)
    {
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var full = Path.GetFullPath(projectPath);

            if (File.Exists(full))
            {
                return full;
            }

            if (!Directory.Exists(full))
            {
                throw new CliException($"Project path not found: {projectPath}");
            }

            return FindTestContainer(full) ?? ComponentGenerator.FindProject(full);
        }

        return FindTestContainer(Environment.CurrentDirectory)
            ?? ComponentGenerator.FindProject(null);
    }

    /// <summary>Returns whether a project already references the hot reload package.</summary>
    /// <param name="projectText">Project file text.</param>
    /// <returns><see langword="true"/> when the reference is present.</returns>
    public static bool IsHotReloadEnabled(string projectText) =>
        projectText.Contains(HotReloadPackage, StringComparison.OrdinalIgnoreCase);

    private static string? FindTestContainer(string directory)
    {
        var container = Path.Combine(directory, "test-container");

        if (!Directory.Exists(container))
        {
            return null;
        }

        var projects = Directory.GetFiles(container, "*.csproj", SearchOption.TopDirectoryOnly);

        return projects.Length switch
        {
            1 => projects[0],
            0 => null,
            _ => throw new CliException(
                $"Multiple project files were found in '{container}'. Pass --project <path>.")
        };
    }

    /// <summary>Builds a Debug-only package reference for a NuGet project.</summary>
    private static string PackageReferenceItem(string projectText, string newLine)
    {
        var version = DonkehVersion(projectText);

        return
            $"  <ItemGroup Condition=\"'$(Configuration)' == 'Debug' and '$(TargetFramework)' == 'net10.0'\">{newLine}" +
            $"    <PackageReference Include=\"{HotReloadPackage}\" Version=\"{version}\" />{newLine}" +
            $"  </ItemGroup>{newLine}";
    }

    /// <summary>Builds a Debug-only project reference for a framework checkout.</summary>
    private static string ProjectReferenceItem(
        string frameworkRoot,
        string projectDirectory,
        string newLine)
    {
        var reference = Path.GetRelativePath(
                projectDirectory,
                Path.Combine(frameworkRoot, "HotReload", "Source", "Donkeh.HotReload.csproj"))
            .Replace('\\', '/');

        return
            $"  <ItemGroup Condition=\"'$(Configuration)' == 'Debug' and '$(TargetFramework)' == 'net10.0'\">{newLine}" +
            $"    <ProjectReference Include=\"{reference}\" />{newLine}" +
            $"  </ItemGroup>{newLine}";
    }

    /// <summary>Resolves the Donkeh package version used by a project.</summary>
    private static string DonkehVersion(string projectText)
    {
        if (projectText.Contains("<DonkehVersion", StringComparison.OrdinalIgnoreCase))
        {
            return "$(DonkehVersion)";
        }

        var reference = Regex.Match(
            projectText,
            "<PackageReference[^>]*Include=\"Donkeh\\.[^\"]*\"[^>]*Version=\"(?<version>[^\"]+)\"",
            RegexOptions.IgnoreCase);

        if (reference.Success)
        {
            return reference.Groups["version"].Value;
        }

        return ProjectGenerator.ToolVersion();
    }

    /// <summary>
    /// Finds the framework checkout root from the project's <c>Donkeh.props</c>
    /// import, or returns <see langword="null"/> for a package project.
    /// </summary>
    private static string? TryGetCheckoutRoot(string projectText, string projectDirectory)
    {
        var import = Regex.Match(
            projectText,
            "<Import[^>]*Project=\"(?<path>[^\"]*Donkeh\\.props)\"",
            RegexOptions.IgnoreCase);

        if (!import.Success)
        {
            return null;
        }

        var propsPath = Path.GetFullPath(
            Path.Combine(projectDirectory, import.Groups["path"].Value));
        var buildDirectory = Path.GetDirectoryName(propsPath);

        return buildDirectory is null ? null : Path.GetDirectoryName(buildDirectory);
    }
}
