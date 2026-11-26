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
        bool force,
        ProjectKind kind = ProjectKind.Application,
        bool csx = true)
    {
        targetDirectory = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(targetDirectory);

        var projectName = ResolveProjectName(targetDirectory, name);

        if (kind == ProjectKind.Framework)
        {
            return GenerateFramework(targetDirectory, projectName, frameworkRoot, force, csx);
        }

        if (csx)
        {
            return GenerateCsxApplication(targetDirectory, projectName, frameworkRoot, force);
        }

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

    /// <summary>
    /// Writes a component library plus a console test container that renders every
    /// component in that library.
    /// </summary>
    private static IReadOnlyList<string> GenerateFramework(
        string targetDirectory,
        string projectName,
        string? frameworkRoot,
        bool force,
        bool csx)
    {
        var rootNamespace = Identifiers.Namespace(projectName);
        var sourceDirectory = Path.Combine(targetDirectory, "src");
        var testDirectory = Path.Combine(targetDirectory, "test-container");
        var libraryProject = Path.Combine(sourceDirectory, projectName + ".csproj");
        var componentDirectory = Path.Combine(sourceDirectory, "Components", "HelloCard");
        var componentFile = Path.Combine(
            componentDirectory,
            "HelloCard" + (csx ? ".cx" : ".cs"));
        var componentCss = Path.Combine(componentDirectory, "HelloCard.css");
        var testProject = Path.Combine(testDirectory, "TestContainer.csproj");
        var testProgram = Path.Combine(testDirectory, csx ? "Program.cx" : "Program.cs");
        var testTargets = Path.Combine(testDirectory, "Directory.Build.targets");
        var libraryCsxConfig = Path.Combine(sourceDirectory, "csx.json");
        var testCsxConfig = Path.Combine(testDirectory, "csx.json");
        var testStyles = Path.Combine(testDirectory, "Styles", "gallery.css");
        var gitignore = Path.Combine(targetDirectory, ".gitignore");
        var solution = Path.Combine(targetDirectory, projectName + ".sln");
        var readme = Path.Combine(targetDirectory, "README.md");
        var useCheckout = !string.IsNullOrWhiteSpace(frameworkRoot);

        var files = new List<string>
        {
            libraryProject,
            componentFile,
            componentCss,
            testProject,
            testProgram,
            testStyles,
            gitignore,
            solution,
            readme
        };

        if (csx)
        {
            files.Add(libraryCsxConfig);
            files.Add(testCsxConfig);
        }

        if (useCheckout)
        {
            files.Add(testTargets);
        }

        if (!force)
        {
            var existing = files.Where(File.Exists).ToArray();

            if (existing.Length > 0)
            {
                throw new CliException(
                    "The target already contains generated files: " +
                    string.Join(", ", existing.Select(Path.GetFileName)) +
                    ". Pass --force to overwrite them.");
            }
        }

        var title = JsonSerializer.Serialize(projectName + " gallery");
        var packageId = rootNamespace;

        if (useCheckout)
        {
            var uiLabReference = Path.GetRelativePath(
                sourceDirectory,
                Path.Combine(frameworkRoot!, "UILab", "Source", "Donkeh.UILab.csproj"))
                .Replace('\\', '/');
            var propsPath = Path.GetRelativePath(
                testDirectory,
                Path.Combine(frameworkRoot!, "Build", "Donkeh.props"))
                .Replace('\\', '/');
            var targetsPath = Path.GetRelativePath(
                testDirectory,
                Path.Combine(frameworkRoot!, "Build", "Donkeh.targets"))
                .Replace('\\', '/');

            TextFiles.Write(libraryProject, $$"""
                <Project Sdk="Microsoft.NET.Sdk">

                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <RootNamespace>{{rootNamespace}}</RootNamespace>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <IsPackable>true</IsPackable>
                    <PackageId>{{packageId}}</PackageId>
                    <Description>{{packageId}} components for Donkeh.</Description>
                  </PropertyGroup>

                  <ItemGroup>
                    <ProjectReference Include="{{uiLabReference}}" />
                    <EmbeddedResource Include="Components/HelloCard/HelloCard.css" />
                  </ItemGroup>

                </Project>
                """);

            TextFiles.Write(testProject, $$"""
                <Project Sdk="Microsoft.NET.Sdk">

                  <PropertyGroup>
                    <DonkehDesktop>true</DonkehDesktop>
                    <!-- <DonkehAndroid>true</DonkehAndroid> -->
                    <!-- <DonkehWeb>true</DonkehWeb> -->
                  </PropertyGroup>

                  <Import Project="{{propsPath}}" />

                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFrameworks>$(DonkehTargetFrameworks)</TargetFrameworks>
                    <RootNamespace>{{rootNamespace}}.TestContainer</RootNamespace>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <DefaultItemExcludes>$(DefaultItemExcludes);builds/**</DefaultItemExcludes>
                  </PropertyGroup>

                  <ItemGroup>
                    <ProjectReference Include="../src/{{projectName}}.csproj" />
                    <EmbeddedResource Include="Styles/gallery.css" />
                  </ItemGroup>

                </Project>
                """);

            TextFiles.Write(testTargets, $$"""
                <Project>
                  <!-- Framework-owned host wiring. -->
                  <Import Project="{{targetsPath}}" />
                </Project>
                """);
        }
        else
        {
            var version = ToolVersion();

            TextFiles.Write(libraryProject, $$"""
                <Project Sdk="Microsoft.NET.Sdk">

                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <RootNamespace>{{rootNamespace}}</RootNamespace>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <IsPackable>true</IsPackable>
                    <PackageId>{{packageId}}</PackageId>
                    <Description>{{packageId}} components for Donkeh.</Description>
                    <DonkehVersion Condition="'$(DonkehVersion)' == ''">{{version}}</DonkehVersion>
                  </PropertyGroup>

                  <ItemGroup>
                    <PackageReference Include="Donkeh.UILab" Version="$(DonkehVersion)" />
                    <EmbeddedResource Include="Components/HelloCard/HelloCard.css" />
                  </ItemGroup>

                </Project>
                """);

            TextFiles.Write(testProject, $$"""
                <Project Sdk="Microsoft.NET.Sdk">

                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RootNamespace>{{rootNamespace}}.TestContainer</RootNamespace>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <DefaultItemExcludes>$(DefaultItemExcludes);builds/**</DefaultItemExcludes>
                    <DonkehVersion Condition="'$(DonkehVersion)' == ''">{{version}}</DonkehVersion>
                  </PropertyGroup>

                  <ItemGroup>
                    <PackageReference Include="Donkeh.Windowing" Version="$(DonkehVersion)" />
                    <ProjectReference Include="../src/{{projectName}}.csproj" />
                    <EmbeddedResource Include="Styles/gallery.css" />
                  </ItemGroup>

                </Project>
                """);
        }

        TextFiles.Write(componentFile, csx
            ? $$"""
                using Donkeh.UILab;

                [assembly: Donkeh.UILab.Stylesheet("Components/HelloCard/HelloCard.css")]

                namespace {{rootNamespace}}.Components;

                /// <summary>Props for the starter component.</summary>
                public sealed class HelloCardProps : PropsWithChildren
                {
                }

                /// <summary>A starter component for the component library.</summary>
                public sealed class HelloCard : Component<HelloCardProps>
                {
                    /// <inheritdoc/>
                    public override Node Render() =>
                        <div className="hello-card">
                            <h2 className="hello-card-title">Hello from {{packageId}}</h2>
                            <p className="hello-card-copy">
                                Edit this component and the test container will show the change.
                            </p>
                        </div>;
                }
                """
            : $$"""
                using Donkeh.UILab;
                using static Donkeh.UILab.HtmlHelper;
                using static Donkeh.UILab._;

                [assembly: Donkeh.UILab.Stylesheet("Components/HelloCard/HelloCard.css")]

                namespace {{rootNamespace}}.Components;

                /// <summary>Props for the starter component.</summary>
                public sealed class HelloCardProps : PropsWithChildren
                {
                }

                /// <summary>A starter component for the component library.</summary>
                public sealed class HelloCard : Component<HelloCardProps>
                {
                    /// <inheritdoc/>
                    public override Node Render() =>
                        Div(new() { ClassName = "hello-card" },
                            H2(new() { ClassName = "hello-card-title" }, Text("Hello from {{packageId}}")),
                            P(new() { ClassName = "hello-card-copy" },
                                Text("Edit this component and the test container will show the change.")));
                }
                """);

        TextFiles.Write(componentCss, """
            .hello-card {
                padding: 20px;
                border: 1px solid #dce2ec;
                border-radius: 14px;
                background: #ffffff;
            }

            .hello-card-title {
                margin: 0 0 8px;
                font-size: 18px;
            }

            .hello-card-copy {
                margin: 0;
                color: #5d697c;
            }
            """);

        TextFiles.Write(testProgram, $$"""
            using Donkeh.Hosting;
            using Donkeh.UILab;
            using static Donkeh.UILab.HtmlHelper;
            using static Donkeh.UILab._;
            using System.Runtime.CompilerServices;
            using {{rootNamespace}}.Components;

            [assembly: Donkeh.UILab.Stylesheet("Styles/gallery.css")]

            namespace {{rootNamespace}}.TestContainer;

            internal static class LibraryLoader
            {
                [ModuleInitializer]
                internal static void Initialize() => GC.KeepAlive(typeof(HelloCard));
            }

            [AppRoot(Width = 1200, Height = 800, Title = {{title}})]
            public sealed class App : Component
            {
                /// <inheritdoc/>
                public override Node Render() =>
                    Div(new() { ClassName = "gallery" },
                        Div(new() { ClassName = "gallery-header" },
                            H1(new() { ClassName = "gallery-title" }, Text({{title}})),
                            P(new() { ClassName = "gallery-subtitle" },
                                Text("Live preview for every component in the library."))),
                        Div(new() { ClassName = "gallery-grid" },
                            Each(ComponentTypes(), type =>
                                Div(new() { ClassName = "gallery-item" },
                                    H2(new() { ClassName = "gallery-name" }, Text(type.Name)),
                                    Div(new() { ClassName = "gallery-preview" }, C(type))))));

                /// <summary>Finds every public, constructible component in the library.</summary>
                private static Type[] ComponentTypes() =>
                    typeof(HelloCard).Assembly
                        .GetTypes()
                        .Where(type => typeof(Component).IsAssignableFrom(type)
                            && !type.IsAbstract
                            && type.GetConstructor(Type.EmptyTypes) is not null)
                        .OrderBy(type => type.Name, StringComparer.Ordinal)
                        .ToArray();
            }
            """);

        if (csx)
        {
            TextFiles.Write(libraryCsxConfig, CsxConfig("generated"));
            TextFiles.Write(testCsxConfig, CsxConfig("generated"));
        }

        TextFiles.Write(testStyles, """
            .gallery {
                display: flex;
                flex-direction: column;
                gap: 28px;
                min-height: 100vh;
                padding: 36px;
                background: #f6f7fb;
            }

            .gallery-header {
                display: flex;
                flex-direction: column;
                gap: 6px;
            }

            .gallery-title {
                margin: 0;
                font-size: 32px;
            }

            .gallery-subtitle {
                margin: 0;
                color: #5d697c;
            }

            .gallery-grid {
                display: grid;
                grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
                gap: 20px;
            }

            .gallery-item {
                padding: 18px;
                border: 1px solid #dce2ec;
                border-radius: 18px;
                background: #ffffff;
            }

            .gallery-name {
                margin: 0 0 12px;
                font-size: 14px;
                color: #5d697c;
                text-transform: uppercase;
                letter-spacing: .06em;
            }

            .gallery-preview {
                display: block;
            }
            """);

        TextFiles.Write(gitignore, """
            bin/
            obj/
            builds/
            """);

        TextFiles.Write(readme, $$"""
            # {{projectName}}

            A Donkeh component library with a live test container.

            ## Preview

            ```powershell
            donkeh run
            ```

            The container discovers every public component in `src/` and renders it
            in a gallery. CSX sources are transformed automatically before the run.

            ## Publish

            ```powershell
            dotnet pack src/{{projectName}}.csproj -c Release
            ```

            The library is marked packable and carries the component stylesheets as
            embedded resources.
            """);

        TextFiles.Write(
            solution,
            BuildSolution(
                projectName,
                libraryProject,
                testProject,
                targetDirectory,
                Guid.NewGuid(),
                Guid.NewGuid()));

        return files;
    }

    /// <summary>Builds a minimal solution containing the library and test container.</summary>
    private static string BuildSolution(
        string name,
        string libraryProject,
        string testProject,
        string rootDirectory,
        Guid libraryGuid,
        Guid testGuid)
    {
        var libraryPath = Path.GetRelativePath(rootDirectory, libraryProject).Replace('/', '\\');
        var testPath = Path.GetRelativePath(rootDirectory, testProject).Replace('/', '\\');

        var template = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            VisualStudioVersion = 17.0.31903.59
            MinimumVisualStudioVersion = 10.0.40219.1
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "__LIB_NAME__", "__LIB_PATH__", "__LIB_GUID__"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TestContainer", "__TEST_PATH__", "__TEST_GUID__"
            EndProject
            Global
            	GlobalSection(SolutionConfigurationPlatforms) = preSolution
            		Debug|Any CPU = Debug|Any CPU
            		Release|Any CPU = Release|Any CPU
            	EndGlobalSection
            	GlobalSection(ProjectConfigurationPlatforms) = postSolution
            		__LIB_GUID__.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		__LIB_GUID__.Debug|Any CPU.Build.0 = Debug|Any CPU
            		__LIB_GUID__.Release|Any CPU.ActiveCfg = Release|Any CPU
            		__LIB_GUID__.Release|Any CPU.Build.0 = Release|Any CPU
            		__TEST_GUID__.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		__TEST_GUID__.Debug|Any CPU.Build.0 = Debug|Any CPU
            		__TEST_GUID__.Release|Any CPU.ActiveCfg = Release|Any CPU
            		__TEST_GUID__.Release|Any CPU.Build.0 = Release|Any CPU
            	EndGlobalSection
            	GlobalSection(SolutionProperties) = preSolution
            		HideSolutionNode = FALSE
            	EndGlobalSection
            EndGlobal
            """;

        return template
            .Replace("__LIB_NAME__", name)
            .Replace("__LIB_PATH__", libraryPath)
            .Replace("__TEST_PATH__", testPath)
            .Replace("__LIB_GUID__", libraryGuid.ToString("B").ToUpperInvariant())
            .Replace("__TEST_GUID__", testGuid.ToString("B").ToUpperInvariant());
    }

    /// <summary>Generates a CSX-style application project.</summary>
    private static IReadOnlyList<string> GenerateCsxApplication(
        string targetDirectory,
        string projectName,
        string? frameworkRoot,
        bool force)
    {
        var rootNamespace = Identifiers.Namespace(projectName);
        var projectFile = Path.Combine(targetDirectory, projectName + ".csproj");
        var programFile = Path.Combine(targetDirectory, "Program.cx");
        var csxConfig = Path.Combine(targetDirectory, "csx.json");
        var targetsFile = Path.Combine(targetDirectory, "Directory.Build.targets");
        var gitignoreFile = Path.Combine(targetDirectory, ".gitignore");
        var useCheckout = !string.IsNullOrWhiteSpace(frameworkRoot);

        var files = useCheckout
            ? new[] { projectFile, programFile, csxConfig, targetsFile, gitignoreFile }
            : new[] { projectFile, programFile, csxConfig, gitignoreFile };

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
            var propsPath = Path.GetRelativePath(
                targetDirectory,
                Path.Combine(frameworkRoot!, "Build", "Donkeh.props"));
            var targetsPath = Path.GetRelativePath(
                targetDirectory,
                Path.Combine(frameworkRoot!, "Build", "Donkeh.targets"));

            TextFiles.Write(projectFile, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="{{SecurityElement.Escape(propsPath)}}" />

                  <PropertyGroup>
                    <DonkehDesktop>true</DonkehDesktop>
                    <!-- <DonkehAndroid>true</DonkehAndroid> -->
                    <!-- <DonkehWeb>true</DonkehWeb> -->
                  </PropertyGroup>

                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFrameworks>$(DonkehTargetFrameworks)</TargetFrameworks>
                    <RootNamespace>{{rootNamespace}}</RootNamespace>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <DefaultItemExcludes>$(DefaultItemExcludes);builds/**;**/bin/**;**/obj/**</DefaultItemExcludes>
                  </PropertyGroup>

                </Project>
                """);

            TextFiles.Write(targetsFile, $$"""
                <Project>
                  <!-- Framework-owned host wiring. -->
                  <Import Project="{{SecurityElement.Escape(targetsPath)}}" />
                </Project>
                """);
        }
        else
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
                    <DefaultItemExcludes>$(DefaultItemExcludes);builds/**;**/bin/**;**/obj/**</DefaultItemExcludes>
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
        }

        TextFiles.Write(programFile, $$"""
            using Donkeh.Hosting;
            using Donkeh.UILab;

            namespace {{rootNamespace}};

            [AppRoot(Width = 1000, Height = 700, Title = {{title}})]
            public sealed class App : Component
            {
                /// <inheritdoc/>
                public override Node Render() =>
                    <div className="app">
                        <h1>Hello, Donkeh!</h1>
                    </div>;
            }
            """);

        TextFiles.Write(csxConfig, CsxConfig("src/generated"));

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

    /// <summary>Builds the <c>csx.json</c> configuration shared by generated projects.</summary>
    private static string CsxConfig(string generatedFileOutput) => $$"""
        {
          "FrameworkHtmlClass": "_",
          "FrameworkHtmlClassMethod": "H",
          "FrameworkComponentMethod": "C",
          "FrameworkFragmentMethod": "F",
          "GeneratedFileOutput": "{{generatedFileOutput}}",
          "InjectedUsings": [
            "using static Donkeh.UILab._;",
            "using static Donkeh.UILab.HtmlHelper;"
          ]
        }
        """;

    /// <summary>Gets the CLI version used to reference matching NuGet packages.</summary>
    internal static string ToolVersion()
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
