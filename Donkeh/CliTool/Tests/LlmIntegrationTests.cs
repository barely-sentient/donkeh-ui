using Donkeh.Llm.Capabilities;
using Donkeh.Llm.Css;
using Donkeh.Llm.Docs;
using Donkeh.Llm;

namespace Donkeh.Cli.Tests;

public sealed class LlmIntegrationTests
{
    [Fact]
    public void McpShowListsCapabilityCategories()
    {
        using var directory = new TemporaryDirectory();
        var service = new CapabilityService(directory.Path, "1.2.3");
        var result = service.Invoke("mcp.show", Invocation(directory.Path));
        var json = System.Text.Json.JsonSerializer.SerializeToElement(result, LlmJson.Options);

        Assert.Equal("1.2.3", json.GetProperty("version").GetString());
        Assert.True(json.GetProperty("categories").GetArrayLength() >= 6);
        Assert.Contains(
            json.GetProperty("categories").EnumerateArray(),
            category => category.GetProperty("name").GetString() == "CSX");
    }

    [Fact]
    public void CssCatalogReportsDisplayValuesFromTheResolver()
    {
        var display = CssCatalog.Properties()
            .FirstOrDefault(property => property.Name == "display");

        Assert.NotNull(display);
        Assert.True(display!.Supported);
        Assert.Contains("flex", display.Values ?? []);
        Assert.DoesNotContain(
            CssCatalog.Properties(),
            property => property.Name == "not-a-real-property");
    }

    [Fact]
    public void CsxValidationReportsUnknownComponentProperty()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "Card.cx"),
            """
            using Donkeh.UILab;

            namespace Demo;

            public sealed class CardProps : Props
            {
                public string Title { get; init; } = "";
            }

            public sealed class Card : Component<CardProps>
            {
                public override Node Render() => <section>{Props.Title}</section>;
            }

            public sealed class Usage : Component
            {
                public override Node Render() => <Card Title="Hello" OnSelect={() => {}} />;
            }
            """);

        var result = Donkeh.Llm.Csx.CsxValidator.Validate(
            Path.Combine(directory.Path, "Card.cx"),
            directory.Path);

        Assert.False(result.Valid);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "CSX1004");
        Assert.Contains("OnSelect", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentationSearchFindsStateManagement()
    {
        var results = DocsIndex.Search("component state management", 3);

        Assert.NotEmpty(results);
        Assert.Contains(results, result => result.Topic == "using-hooks");
    }

    [Fact]
    public void ManifestCapabilitiesAreDiscoveredFromProject()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, ".donkeh"));
        File.WriteAllText(
            Path.Combine(directory.Path, "Demo.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(directory.Path, ".donkeh", "llm.json"),
            """
            {
              "schemaVersion": 1,
              "package": "Acme.Components",
              "capabilities": [
                {
                  "name": "acme.components.list",
                  "category": "Package",
                  "summary": "List Acme components.",
                  "result": { "components": ["FileExplorer"] }
                }
              ],
              "components": [
                {
                  "name": "FileExplorer",
                  "namespace": "Acme.Components",
                  "kind": "assembly",
                  "description": "A file browser with selection support.",
                  "properties": [
                    { "name": "ViewMode", "type": "string", "default": "\"List\"" }
                  ],
                  "tags": ["file", "explorer", "selection"]
                }
              ]
            }
            """);

        var service = new CapabilityService(directory.Path, "1.0.0", defaultProjectPath: directory.Path);
        var capability = service.Registry.Find("acme.components.list");
        Assert.NotNull(capability);
        Assert.Equal("Acme.Components", capability!.RegisteredBy);

        var result = service.Invoke("acme.components.list", Invocation(directory.Path));
        var json = System.Text.Json.JsonSerializer.SerializeToElement(result, LlmJson.Options);
        Assert.Equal("FileExplorer", json.GetProperty("components")[0].GetString());

        var components = service.Invoke("project.components", Invocation(directory.Path));
        var componentsJson = System.Text.Json.JsonSerializer.SerializeToElement(components, LlmJson.Options);
        Assert.Contains(
            componentsJson.GetProperty("components").EnumerateArray(),
            component => component.GetProperty("name").GetString() == "FileExplorer");
    }

    private static LlmInvocation Invocation(string directory) => new()
    {
        WorkingDirectory = directory,
        ProjectPath = directory
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "donkeh-llm-tests",
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
