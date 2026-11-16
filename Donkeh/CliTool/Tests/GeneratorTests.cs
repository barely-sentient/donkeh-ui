namespace Donkeh.Cli.Tests;

public sealed class GeneratorTests
{
    [Fact]
    public void NewProjectCreatesMinimumApp()
    {
        using var directory = new TemporaryDirectory();

        var files = ProjectGenerator.Generate(
            directory.Path,
            "SampleApp",
            FrameworkLocator.Find(null),
            force: false);

        Assert.Equal(4, files.Count);
        Assert.True(File.Exists(Path.Combine(directory.Path, "SampleApp.csproj")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "Program.cs")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "Directory.Build.targets")));

        var project = File.ReadAllText(Path.Combine(directory.Path, "SampleApp.csproj"));
        var program = File.ReadAllText(Path.Combine(directory.Path, "Program.cs"));

        Assert.Contains("<DonkehDesktop>true</DonkehDesktop>", project, StringComparison.Ordinal);
        Assert.Contains("<TargetFrameworks>$(DonkehTargetFrameworks)</TargetFrameworks>", project, StringComparison.Ordinal);
        Assert.Contains("Donkeh.props", project, StringComparison.Ordinal);
        Assert.Contains("[AppRoot", program, StringComparison.Ordinal);
        Assert.Contains("class App : Component", program, StringComparison.Ordinal);
        Assert.Contains("Div(new()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Donkeh.HotReload", project, StringComparison.Ordinal);
    }

    [Fact]
    public void NewProjectOutsideCheckoutUsesNuGetPackages()
    {
        using var directory = new TemporaryDirectory();

        var files = ProjectGenerator.Generate(
            directory.Path,
            "SampleApp",
            frameworkRoot: null,
            force: false);

        Assert.Equal(3, files.Count);
        Assert.False(File.Exists(Path.Combine(directory.Path, "Directory.Build.targets")));

        var project = File.ReadAllText(Path.Combine(directory.Path, "SampleApp.csproj"));
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Donkeh.Windowing\" Version=\"$(DonkehVersion)\" />", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Donkeh.HotReload", project, StringComparison.Ordinal);
        Assert.Contains("<DonkehVersion", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Donkeh.props", project, StringComparison.Ordinal);
    }

    [Fact]
    public void EnableTargetAddsAndroidAndBrowserPackages()
    {
        using var directory = new TemporaryDirectory();

        ProjectGenerator.Generate(directory.Path, "SampleApp", frameworkRoot: null, force: false);

        var projectFile = Path.Combine(directory.Path, "SampleApp.csproj");

        TargetEnabler.Enable("android", projectFile);

        var project = File.ReadAllText(projectFile);
        Assert.Contains(
            "<TargetFrameworks>net10.0;net10.0-android</TargetFrameworks>",
            project,
            StringComparison.Ordinal);
        Assert.Contains("<SupportedOSPlatformVersion Condition=\"'$(TargetFramework)' == 'net10.0-android'\">24</SupportedOSPlatformVersion>", project, StringComparison.Ordinal);
        Assert.Contains("Include=\"Donkeh.Windowing.Android\"", project, StringComparison.Ordinal);

        TargetEnabler.Enable("web", projectFile);

        project = File.ReadAllText(projectFile);
        Assert.Contains("net10.0-browser", project, StringComparison.Ordinal);
        Assert.Contains("<RuntimeIdentifiers Condition=\"'$(TargetFramework)' == ''\">browser-wasm</RuntimeIdentifiers>", project, StringComparison.Ordinal);
        Assert.Contains("Include=\"Donkeh.Windowing.Browser\"", project, StringComparison.Ordinal);
    }

    [Fact]
    public void EnableTargetUpdatesCheckoutProjectProperties()
    {
        using var directory = new TemporaryDirectory();

        ProjectGenerator.Generate(
            directory.Path,
            "SampleApp",
            FrameworkLocator.Find(null),
            force: false);

        var projectFile = Path.Combine(directory.Path, "SampleApp.csproj");
        TargetEnabler.Enable("android", projectFile);

        var project = File.ReadAllText(projectFile);
        Assert.Contains("<DonkehAndroid>true</DonkehAndroid>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Donkeh.Windowing.Android", project, StringComparison.Ordinal);
    }

    [Fact]
    public void EnableFeatureAddsDebugOnlyPackageReference()
    {
        using var directory = new TemporaryDirectory();

        ProjectGenerator.Generate(directory.Path, "SampleApp", frameworkRoot: null, force: false);

        var projectFile = Path.Combine(directory.Path, "SampleApp.csproj");
        FeatureEnabler.Enable("hot-reload", projectFile);
        FeatureEnabler.Enable("hot-reload", projectFile);

        var project = File.ReadAllText(projectFile);

        Assert.Contains("<PackageReference Include=\"Donkeh.HotReload\" Version=\"$(DonkehVersion)\" />", project, StringComparison.Ordinal);
        Assert.Contains("<EmbeddedResource Update=\"**/*.css\" Watch=\"false\" />", project, StringComparison.Ordinal);
        Assert.Contains("'Debug'", project, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(project, "Include=\"Donkeh.HotReload\""));
    }

    [Fact]
    public void EnableFeatureAddsCheckoutProjectReference()
    {
        using var directory = new TemporaryDirectory();

        ProjectGenerator.Generate(
            directory.Path,
            "SampleApp",
            FrameworkLocator.Find(null),
            force: false);

        var projectFile = Path.Combine(directory.Path, "SampleApp.csproj");
        FeatureEnabler.Enable("hot-reload", projectFile);

        var project = File.ReadAllText(projectFile);

        Assert.Contains("Donkeh.HotReload.csproj", project, StringComparison.Ordinal);
        Assert.Contains("<EmbeddedResource Update=\"**/*.css\" Watch=\"false\" />", project, StringComparison.Ordinal);
        Assert.Contains("'Debug'", project, StringComparison.Ordinal);
        Assert.Contains("<ProjectReference Include=", project, StringComparison.Ordinal);
    }

    [Fact]
    public void EnableFeatureSelectsTestContainerFromFrameworkRoot()
    {
        using var directory = new TemporaryDirectory();

        ProjectGenerator.Generate(
            directory.Path,
            "Acme.Components",
            frameworkRoot: null,
            force: false,
            kind: ProjectKind.Framework);

        var projectFile = FeatureEnabler.Enable("hot-reload", directory.Path);
        var container = File.ReadAllText(Path.Combine(directory.Path, "test-container", "TestContainer.csproj"));
        var library = File.ReadAllText(Path.Combine(directory.Path, "src", "Acme.Components.csproj"));

        Assert.Equal(
            Path.Combine(directory.Path, "test-container", "TestContainer.csproj"),
            projectFile);
        Assert.Contains("Donkeh.HotReload", container, StringComparison.Ordinal);
        Assert.DoesNotContain("Donkeh.HotReload", library, StringComparison.Ordinal);
    }

    [Fact]
    public void NewFrameworkProjectCreatesLibraryAndTestContainer()
    {
        using var directory = new TemporaryDirectory();

        var files = ProjectGenerator.Generate(
            directory.Path,
            "Acme.Components",
            frameworkRoot: null,
            force: false,
            kind: ProjectKind.Framework);

        Assert.Equal(9, files.Count);
        Assert.True(File.Exists(Path.Combine(directory.Path, "src", "Acme.Components.csproj")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "src", "Components", "HelloCard", "HelloCard.cs")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "test-container", "TestContainer.csproj")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "test-container", "Program.cs")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "Acme.Components.sln")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "README.md")));

        var library = File.ReadAllText(Path.Combine(directory.Path, "src", "Acme.Components.csproj"));
        var component = File.ReadAllText(Path.Combine(
            directory.Path,
            "src",
            "Components",
            "HelloCard",
            "HelloCard.cs"));
        var container = File.ReadAllText(Path.Combine(directory.Path, "test-container", "TestContainer.csproj"));
        var program = File.ReadAllText(Path.Combine(directory.Path, "test-container", "Program.cs"));

        Assert.Contains("<IsPackable>true</IsPackable>", library, StringComparison.Ordinal);
        Assert.Contains("<PackageId>Acme.Components</PackageId>", library, StringComparison.Ordinal);
        Assert.Contains("Donkeh.UILab", library, StringComparison.Ordinal);
        Assert.Contains("<EmbeddedResource Include=\"Components/HelloCard/HelloCard.css\" />", library, StringComparison.Ordinal);
        Assert.Contains("class HelloCard : Component", component, StringComparison.Ordinal);
        Assert.Contains("ProjectReference Include=\"../src/Acme.Components.csproj\"", container, StringComparison.Ordinal);
        Assert.Contains("[AppRoot", program, StringComparison.Ordinal);
        Assert.Contains("Each(ComponentTypes()", program, StringComparison.Ordinal);
        Assert.Contains("C(type)", program, StringComparison.Ordinal);
        Assert.Contains("[ModuleInitializer]", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Donkeh.HotReload", container, StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeUpdatesDonkehVersionAndLiteralPackageVersions()
    {
        var project = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Donkeh.Windowing" Version="1.0.1" />
                <PackageReference Include="Donkeh.UILab" Version="$(DonkehVersion)" />
              </ItemGroup>
            </Project>
            """;

        var updated = CsprojEditor.SetDonkehVersion(project, "\n", "2.3.4");
        updated = CsprojEditor.SetDonkehPackageVersions(updated, "2.3.4");

        Assert.Contains("<DonkehVersion>2.3.4</DonkehVersion>", updated, StringComparison.Ordinal);
        Assert.Contains("Include=\"Donkeh.Windowing\" Version=\"2.3.4\"", updated, StringComparison.Ordinal);
        Assert.Contains("Include=\"Donkeh.UILab\" Version=\"$(DonkehVersion)\"", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeRejectsProjectsWithoutDonkehReferences()
    {
        using var directory = new TemporaryDirectory();

        var projectFile = Path.Combine(directory.Path, "Other.csproj");
        File.WriteAllText(
            projectFile,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var exception = Assert.Throws<CliException>(() =>
            UpgradeService.Upgrade(projectFile, "2.0.0"));

        Assert.Contains("does not reference Donkeh", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeRejectsLocalFrameworkCheckoutProjects()
    {
        using var directory = new TemporaryDirectory();

        var projectFile = Path.Combine(directory.Path, "Sample.csproj");
        File.WriteAllText(
            projectFile,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="..\Donkeh.props" />
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var exception = Assert.Throws<CliException>(() =>
            UpgradeService.Upgrade(projectFile, "2.0.0"));

        Assert.Contains("local framework checkout", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NewComponentCreatesFilesAndUpdatesProject()
    {
        using var directory = new TemporaryDirectory();

        ProjectGenerator.Generate(
            directory.Path,
            "SampleApp",
            FrameworkLocator.Find(null),
            force: false);

        var projectFile = Path.Combine(directory.Path, "SampleApp.csproj");
        ComponentGenerator.Generate("my-widget", projectFile, force: false);

        var componentFile = Path.Combine(
            directory.Path,
            "src",
            "Components",
            "MyWidget",
            "MyWidget.cs");
        var cssFile = Path.Combine(
            directory.Path,
            "src",
            "Components",
            "MyWidget",
            "MyWidget.css");

        Assert.True(File.Exists(componentFile));
        Assert.True(File.Exists(cssFile));

        var component = File.ReadAllText(componentFile);
        var css = File.ReadAllText(cssFile);
        var project = File.ReadAllText(projectFile);

        Assert.Contains("class MyWidget : Component", component, StringComparison.Ordinal);
        Assert.Contains("Div(new()", component, StringComparison.Ordinal);
        Assert.Contains(
            "[assembly: Donkeh.UILab.Stylesheet(\"src/Components/MyWidget/MyWidget.css\")]",
            component,
            StringComparison.Ordinal);
        Assert.Contains(".my-widget", css, StringComparison.Ordinal);
        Assert.Contains(
            "<EmbeddedResource Include=\"src/Components/MyWidget/MyWidget.css\" />",
            project,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingComponentRequiresForce()
    {
        using var directory = new TemporaryDirectory();

        ProjectGenerator.Generate(
            directory.Path,
            "SampleApp",
            FrameworkLocator.Find(null),
            force: false);

        var projectFile = Path.Combine(directory.Path, "SampleApp.csproj");
        ComponentGenerator.Generate("Card", projectFile, force: false);

        Assert.Throws<CliException>(() =>
            ComponentGenerator.Generate("Card", projectFile, force: false));
    }

    [Fact]
    public void ComponentNamesAreNormalized()
    {
        Assert.Equal("MyWidget", Identifiers.ComponentName("my-widget"));
        Assert.Equal("MyComponent", Identifiers.ComponentName("my_component"));
        Assert.Equal("my-widget", Identifiers.KebabCase("MyWidget"));
        Assert.Equal("my-http-client", Identifiers.KebabCase("MyHTTPClient"));
        Assert.Equal("Acme.App", Identifiers.Namespace("Acme.App"));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "donkeh-cli-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
