using System.Security;
using System.Text.Json;

namespace Donkeh.Cli;

/// <summary>Generates a minimal Donkeh application project.</summary>
internal static class ProjectGenerator
{
    /// <summary>Creates the project files in a target directory.</summary>
    /// <param name="targetDirectory">Directory that receives the project.</param>
    /// <param name="name">Optional project name; defaults to the directory name.</param>
    /// <param name="frameworkRoot">Absolute path to the framework directory.</param>
    /// <param name="force">Whether existing files may be replaced.</param>
    /// <returns>The paths written, in display order.</returns>
    public static IReadOnlyList<string> Generate(
        string targetDirectory,
        string? name,
        string frameworkRoot,
        bool force)
    {
        targetDirectory = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(targetDirectory);

        var projectName = ResolveProjectName(targetDirectory, name);
        var rootNamespace = Identifiers.Namespace(projectName);
        var projectFile = Path.Combine(targetDirectory, projectName + ".csproj");
        var programFile = Path.Combine(targetDirectory, "Program.cs");
        var targetsFile = Path.Combine(targetDirectory, "Directory.Build.targets");
        var gitignoreFile = Path.Combine(targetDirectory, ".gitignore");

        var files = new[] { projectFile, programFile, targetsFile, gitignoreFile };

        if (!force)
        {
            var existing = files.Where(File.Exists).ToArray();

            if (existing.Length > 0)
            {
                throw new CliException(
                    "The target already contains project files: " +
                    string.Join(", ", existing.Select(Path.GetFileName)) +
                    ". Pass --force to overwrite them.");
            }
        }

        var propsPath = Path.GetRelativePath(
            targetDirectory,
            Path.Combine(frameworkRoot, "Build", "Donkeh.props"));
        var targetsPath = Path.GetRelativePath(
            targetDirectory,
            Path.Combine(frameworkRoot, "Build", "Donkeh.targets"));

        var title = JsonSerializer.Serialize(projectName);

        TextFiles.Write(projectFile, $$"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <DonkehDesktop>true</DonkehDesktop>
                <!-- <DonkehAndroid>true</DonkehAndroid> -->
                <!-- <DonkehWeb>true</DonkehWeb> -->
              </PropertyGroup>

              <Import Project="{{SecurityElement.Escape(propsPath)}}" />

              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFrameworks>$(DonkehTargetFrameworks)</TargetFrameworks>
                <RootNamespace>{{rootNamespace}}</RootNamespace>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <DefaultItemExcludes>$(DefaultItemExcludes);builds/**</DefaultItemExcludes>
              </PropertyGroup>

            </Project>
            """);

        TextFiles.Write(programFile, $$"""
            using Donkeh.Hosting;
            using Donkeh.UILab;
            using static Donkeh.UILab._;

            namespace {{rootNamespace}};

            [AppRoot(Width = 1000, Height = 700, Title = {{title}})]
            public sealed class App : Component
            {
                /// <inheritdoc/>
                public override Node Render() =>
                    H("main", new Props
                    {
                        Attributes = new Dictionary<string, string>
                        {
                            ["style"] = "margin:0;width:100vw;height:100vh;background:#ffffff"
                        }
                    });
            }
            """);

        TextFiles.Write(targetsFile, $$"""
            <Project>
              <!-- Framework-owned host wiring. -->
              <Import Project="{{SecurityElement.Escape(targetsPath)}}" />
            </Project>
            """);

        TextFiles.Write(gitignoreFile, """
            bin/
            obj/
            builds/
            """);

        return files;
    }

    /// <summary>Resolves and sanitizes the project name.</summary>
    private static string ResolveProjectName(string targetDirectory, string? name)
    {
        var value = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(targetDirectory))
            : name.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            value = "DonkehApp";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "DonkehApp" : sanitized;
    }
}
