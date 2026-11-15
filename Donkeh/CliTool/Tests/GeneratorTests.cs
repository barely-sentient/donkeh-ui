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
            "MyWidget.cs");
        var cssFile = Path.Combine(
            directory.Path,
            "src",
            "Components",
            "MyWidget.css");

        Assert.True(File.Exists(componentFile));
        Assert.True(File.Exists(cssFile));

        var component = File.ReadAllText(componentFile);
        var css = File.ReadAllText(cssFile);
        var project = File.ReadAllText(projectFile);

        Assert.Contains("class MyWidget : Component", component, StringComparison.Ordinal);
        Assert.Contains(
            "[assembly: Donkeh.UILab.Stylesheet(\"src/Components/MyWidget.css\")]",
            component,
            StringComparison.Ordinal);
        Assert.Contains(".my-widget", css, StringComparison.Ordinal);
        Assert.Contains(
            "<EmbeddedResource Include=\"src/Components/MyWidget.css\" />",
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
