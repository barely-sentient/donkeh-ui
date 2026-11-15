using System.Security;
using System.Text.Json;

namespace Donkeh.Cli;

/// <summary>Generates a minimal Donkeh application project.</summary>
internal static class ProjectGenerator
{
    /// <summary>Creates the project files in a target directory.</summary>
    /// <param name="targetDirectory">Directory that receives the project.</param>
    /// <param name="name">Optional project name; defaults to the directory name.</param>
    /// <param name="frameworkRoot">
    /// Absolute path to a local framework checkout, or <see langword="null"/> to
    /// generate a project that consumes the published NuGet packages.
    /// </param>
    /// <param name="force">Whether existing files may be replaced.</param>
    /// <returns>The paths written, in display order.</returns>
    public static IReadOnlyList<string> Generate(
        string targetDirectory,
        string? name,
        string? frameworkRoot,
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
        var useCheckout = !string.IsNullOrWhiteSpace(frameworkRoot);

        var files = useCheckout
            ? new[] { projectFile, programFile, targetsFile, gitignoreFile }
            : new[] { projectFile, programFile, gitignoreFile };

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

        var title = JsonSerializer.Serialize(projectName);

        if (useCheckout)
        {
            WriteCheckoutProject(
                frameworkRoot!,
                targetDirectory,
                projectFile,
                programFile,
                targetsFile,
                rootNamespace,
                title);
        }
        else
        {
            WritePackageProject(projectFile, programFile, rootNamespace, title);
        }

        TextFiles.Write(gitignoreFile, """
            bin/
            obj/
            builds/
            """);

        return files;
    }

    /// <summary>Writes a project that references the framework source tree.</summary>
    private static void WriteCheckoutProject(
        string frameworkRoot,
        string targetDirectory,
        string projectFile,
        string programFile,
        string targetsFile,
        string rootNamespace,
        string title)
    {
        var propsPath = Path.GetRelativePath(
            targetDirectory,
            Path.Combine(frameworkRoot, "Build", "Donkeh.props"));
        var targetsPath = Path.GetRelativePath(
            targetDirectory,
            Path.Combine(frameworkRoot, "Build", "Donkeh.targets"));

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

        TextFiles.Write(programFile, ProgramTemplate(rootNamespace, title));

        TextFiles.Write(targetsFile, $$"""
            <Project>
              <!-- Framework-owned host wiring. -->
              <Import Project="{{SecurityElement.Escape(targetsPath)}}" />
            </Project>
            """);
    }

    /// <summary>Writes a self-contained project that consumes the NuGet packages.</summary>
    private static void WritePackageProject(
        string projectFile,
        string programFile,
        string rootNamespace,
        string title)
    {
        var version = ToolVersion();

        TextFiles.Write(projectFile, $$"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>{{rootNamespace}}</RootNamespace>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <DefaultItemExcludes>$(DefaultItemExcludes);builds/**</DefaultItemExcludes>
                <DonkehVersion Condition="'$(DonkehVersion)' == ''">{{version}}</DonkehVersion>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Donkeh.Windowing" Version="$(DonkehVersion)" />
              </ItemGroup>

              <!-- Add Android or browser targets with:
                   donkeh enable-target android
                   donkeh enable-target web -->

            </Project>
            """);

        TextFiles.Write(programFile, ProgramTemplate(rootNamespace, title));
    }

    /// <summary>Builds the shared attributed root component.</summary>
    private static string ProgramTemplate(string rootNamespace, string title) => $$"""
        using Donkeh.Hosting;
        using Donkeh.UILab;
        using static Donkeh.UILab.HtmlHelper;

        namespace {{rootNamespace}};

        [AppRoot(Width = 1000, Height = 700, Title = {{title}})]
        public sealed class App : Component
        {
            /// <inheritdoc/>
            public override Node Render() =>
                Div(new()
                {
                    ClassName = "app",
                    Attributes = new Dictionary<string, string>
                    {
                        ["style"] = "margin:0;width:100vw;height:100vh;background:#ffffff"
                    }
                });
        }
        """;

    /// <summary>Gets the CLI version used to reference matching NuGet packages.</summary>
    private static string ToolVersion()
    {
        var version = typeof(ProjectGenerator).Assembly.GetName().Version;

        if (version is null || version == new Version(0, 0, 0, 0))
        {
            return "1.0.22";
        }

        return version.Revision > 0
            ? version.ToString(4)
            : version.ToString(3);
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
