using System.Security;
using System.Text.RegularExpressions;

namespace Donkeh.Cli;

/// <summary>Switches a project between a local framework checkout and NuGet packages.</summary>
internal static class ProjectModeService
{
    /// <summary>Points a project at a local framework checkout.</summary>
    /// <param name="frameworkPath">Framework repository, <c>Donkeh</c> directory or solution file.</param>
    /// <param name="projectPath">Optional project file or project directory.</param>
    /// <returns>The updated project file.</returns>
    public static string UseLocal(string frameworkPath, string? projectPath)
    {
        var framework = FrameworkLocator.Find(frameworkPath)
            ?? throw new CliException(
                $"Could not find a Donkeh framework at '{frameworkPath}'. " +
                "Pass the repository root, Donkeh directory or solution file.");

        var projectFile = ComponentGenerator.FindProject(projectPath);
        var (projectText, newLine) = TextFiles.Read(projectFile);
        var updated = projectText;

        if (CsprojEditor.HasProperty(projectText, "UseLocalDonkeh"))
        {
            // Dual-mode projects keep both paths in one file and switch with the
            // property. This preserves the NuGet references for a later switch back.
            updated = CsprojEditor.SetProperty(updated, newLine, "UseLocalDonkeh", "true");
            updated = CsprojEditor.SetProperty(updated, newLine, "DonkehRoot", framework);

            if (!updated.Contains("Donkeh.props", StringComparison.OrdinalIgnoreCase))
            {
                var propsPath = RelativePath(
                    projectFile,
                    Path.Combine(framework, "Build", "Donkeh.props"));
                updated = InsertConditionalImport(updated, newLine, propsPath);
            }
        }
        else
        {
            updated = ConvertToCheckout(updated, newLine, projectFile, framework);
        }

        if (!string.Equals(projectText, updated, StringComparison.Ordinal))
        {
            TextFiles.Write(projectFile, updated);
        }

        Restore(projectFile);
        return projectFile;
    }

    /// <summary>Points a project at the published NuGet packages.</summary>
    /// <param name="requestedVersion">Version to use, or <see langword="null"/> for the latest stable release.</param>
    /// <param name="projectPath">Optional project file or project directory.</param>
    /// <returns>The updated project file.</returns>
    public static string UseNuget(string? requestedVersion, string? projectPath)
    {
        var projectFile = ComponentGenerator.FindProject(projectPath);
        var (projectText, newLine) = TextFiles.Read(projectFile);
        var version = string.IsNullOrWhiteSpace(requestedVersion)
            ? UpgradeService.GetLatestVersion()
            : UpgradeService.NormalizeVersion(requestedVersion);
        var updated = projectText;

        if (CsprojEditor.HasProperty(projectText, "UseLocalDonkeh"))
        {
            updated = CsprojEditor.SetProperty(updated, newLine, "UseLocalDonkeh", "false");
        }
        else
        {
            updated = CsprojEditor.RemoveImports(updated, "Donkeh.props");
            updated = CsprojEditor.RemoveProperty(updated, "TargetFrameworks");
            updated = CsprojEditor.SetProperty(updated, newLine, "TargetFramework", "net10.0");
            RemoveDirectoryBuildTargets(projectFile);
        }

        updated = CsprojEditor.SetDonkehVersion(updated, newLine, version);
        updated = CsprojEditor.SetDonkehPackageVersions(updated, version);

        var packageId = IsExecutable(updated) ? "Donkeh.Windowing" : "Donkeh.UILab";
        updated = CsprojEditor.EnsurePackageReference(
            updated,
            newLine,
            packageId,
            "$(DonkehVersion)");

        if (Regex.IsMatch(updated, @"net10\.0-android", RegexOptions.IgnoreCase))
        {
            if (CsprojEditor.HasProperty(updated, "DonkehAndroid"))
            {
                updated = CsprojEditor.SetProperty(updated, newLine, "DonkehAndroid", "true");
            }

            var condition = "'$(TargetFramework)' == 'net10.0-android'";

            if (CsprojEditor.HasProperty(updated, "UseLocalDonkeh"))
            {
                condition = "'$(UseLocalDonkeh)' != 'true' and " + condition;
            }

            updated = CsprojEditor.EnsurePackageReference(
                updated,
                newLine,
                "Donkeh.Windowing.Android",
                "$(DonkehVersion)",
                condition);
        }

        if (!string.Equals(projectText, updated, StringComparison.Ordinal))
        {
            TextFiles.Write(projectFile, updated);
        }

        Restore(projectFile);
        return projectFile;
    }

    private static string ConvertToCheckout(
        string projectText,
        string newLine,
        string projectFile,
        string framework)
    {
        var updated = CsprojEditor.RemoveDonkehPackageReferences(projectText);
        updated = CsprojEditor.RemoveImports(updated, "Donkeh.props");
        updated = CsprojEditor.RemoveProperty(updated, "TargetFramework");
        updated = CsprojEditor.RemoveProperty(updated, "TargetFrameworks");
        updated = CsprojEditor.SetProperty(
            updated,
            newLine,
            "TargetFrameworks",
            "$(DonkehTargetFrameworks)");
        updated = CsprojEditor.SetProperty(updated, newLine, "DonkehDesktop", "true");

        var propsPath = RelativePath(
            projectFile,
            Path.Combine(framework, "Build", "Donkeh.props"));
        updated = CsprojEditor.AddImportAfterProject(updated, newLine, propsPath);

        WriteDirectoryBuildTargets(projectFile, framework);
        return updated;
    }

    private static string InsertConditionalImport(
        string projectText,
        string newLine,
        string propsPath)
    {
        // Place the import after the first property group so a DonkehRoot
        // property defined there is available when the condition is evaluated.
        var group = Regex.Match(
            projectText,
            @"</PropertyGroup>",
            RegexOptions.IgnoreCase);

        int index;

        if (group.Success)
        {
            index = group.Index + group.Length;
        }
        else
        {
            var opening = Regex.Match(
                projectText,
                @"<Project\b[^>]*>",
                RegexOptions.IgnoreCase);

            if (!opening.Success)
            {
                throw new CliException("The project file does not contain an opening <Project> element.");
            }

            index = opening.Index + opening.Length;
        }

        var escaped = SecurityElement.Escape(propsPath);
        var insertion =
            $"{newLine}  <Import Project=\"{escaped}\" Condition=\"'$(UseLocalDonkeh)' == 'true'\" />";
        return projectText.Insert(index, insertion);
    }

    private static void WriteDirectoryBuildTargets(string projectFile, string framework)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)
            ?? throw new CliException("Could not determine the project directory.");
        var targetsFile = Path.Combine(projectDirectory, "Directory.Build.targets");
        var targetsPath = RelativePath(
            targetsFile,
            Path.Combine(framework, "Build", "Donkeh.targets"));

        if (!File.Exists(targetsFile))
        {
            TextFiles.Write(
                targetsFile,
                $"<Project>{Environment.NewLine}" +
                $"  <Import Project=\"{SecurityElement.Escape(targetsPath)}\" />{Environment.NewLine}" +
                $"</Project>{Environment.NewLine}");
            return;
        }

        var (text, newLine) = TextFiles.Read(targetsFile);

        if (text.Contains("Donkeh.targets", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        text = CsprojEditor.AddImportAfterProject(text, newLine, targetsPath);
        TextFiles.Write(targetsFile, text);
    }

    private static void RemoveDirectoryBuildTargets(string projectFile)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile);

        if (projectDirectory is null)
        {
            return;
        }

        var targetsFile = Path.Combine(projectDirectory, "Directory.Build.targets");

        if (!File.Exists(targetsFile))
        {
            return;
        }

        var (text, _) = TextFiles.Read(targetsFile);

        if (!text.Contains("Donkeh.targets", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var cleaned = CsprojEditor.RemoveImports(text, "Donkeh.targets");

        if (Regex.IsMatch(cleaned, @"<(ItemGroup|PropertyGroup|Target|Import|Using)\b", RegexOptions.IgnoreCase))
        {
            TextFiles.Write(targetsFile, cleaned);
        }
        else
        {
            File.Delete(targetsFile);
        }
    }

    private static bool IsExecutable(string projectText) =>
        Regex.IsMatch(
            projectText,
            @"<OutputType>\s*Exe\s*</OutputType>",
            RegexOptions.IgnoreCase);

    private static string RelativePath(string fromFile, string toPath)
    {
        var directory = Path.GetDirectoryName(fromFile)
            ?? Environment.CurrentDirectory;
        return Path.GetRelativePath(directory, toPath).Replace('\\', '/');
    }

    private static void Restore(string projectFile)
    {
        Console.WriteLine($"  restoring {Path.GetFileName(projectFile)}");
        UpgradeService.RunDotnet(
            ["restore", projectFile, "--force", "--no-cache"],
            Path.GetDirectoryName(projectFile));
    }
}
