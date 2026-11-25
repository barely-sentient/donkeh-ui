using CSX.Transformer;
using Donkeh.Llm.Components;
using Donkeh.Llm.Extensibility;
using Donkeh.Llm.Project;

namespace Donkeh.Llm.Csx;

/// <summary>Runs the shipped CSX transformer and returns structured compilation data.</summary>
public static class CsxCompiler
{
    /// <summary>Compiles one CSX file to generated C#, optionally returning the source and map.</summary>
    public static CsxCompileResult Compile(
        string file,
        string workingDirectory,
        string? projectPath = null,
        bool includeGenerated = false,
        bool includeSourceMap = false,
        bool includeResolved = true)
    {
        var fullPath = Path.GetFullPath(file);

        if (!File.Exists(fullPath))
        {
            throw new LlmException("CSX0000", $"CSX file not found: {file}");
        }

        var source = File.ReadAllText(fullPath);
        var config = CsxValidator.LoadConfig(fullPath, out var configError);
        var diagnostics = new List<LlmDiagnostic>();

        if (configError is not null)
        {
            diagnostics.Add(LlmDiagnostics.Warning(
                "CSX0006", configError, new LlmLocation { File = fullPath }));
        }

        string? generated = null;
        SourceMap? map = null;

        try
        {
            (generated, map) = new Transformer().TransformWithSourceMap(source, config);
        }
        catch (Exception exception)
        {
            diagnostics.Add(LlmDiagnostics.Error(
                "CSX0001",
                $"The CSX transformer rejected this file: {exception.Message}",
                new LlmLocation { File = fullPath, Line = 1, Column = 1 }));
        }

        var validation = CsxValidator.Validate(fullPath, workingDirectory, projectPath);
        diagnostics.AddRange(validation.Diagnostics);
        diagnostics = diagnostics
            .GroupBy(diagnostic => (diagnostic.Code, diagnostic.Message, diagnostic.Location?.Line ?? 0,
                diagnostic.Location?.Column ?? 0))
            .Select(group => group.First())
            .OrderBy(diagnostic => diagnostic.Location?.Line ?? 0)
            .ThenBy(diagnostic => diagnostic.Location?.Column ?? 0)
            .ToList();

        CsxResolvedMetadata? resolved = null;

        if (includeResolved)
        {
            resolved = Resolve(fullPath, workingDirectory, projectPath);
        }

        return new CsxCompileResult
        {
            File = file,
            Success = generated is not null && diagnostics.All(diagnostic => diagnostic.Severity != "error"),
            Diagnostics = diagnostics,
            Generated = includeGenerated ? generated : null,
            SourceMap = includeSourceMap && map is not null
                ? map.Mappings.Select(entry => new CsxSourceMapEntry
                {
                    GeneratedStart = entry.GeneratedStart,
                    GeneratedLength = entry.GeneratedLength,
                    OriginalStart = entry.OriginalStart,
                    OriginalLength = entry.OriginalLength
                }).ToList()
                : null,
            Resolved = resolved
        };
    }

    private static CsxResolvedMetadata? Resolve(
        string file,
        string workingDirectory,
        string? projectPath)
    {
        var projectFile = string.IsNullOrWhiteSpace(projectPath)
            ? ProjectLocator.Find(workingDirectory, null)
            : ProjectLocator.Find(workingDirectory, projectPath);

        List<ComponentMetadata> components;
        DonkehProject? project = null;

        if (projectFile is null)
        {
            components = SourceComponentReader
                .ReadProject(Path.GetDirectoryName(Path.GetFullPath(file)) ?? workingDirectory)
                .Concat(HtmlElementCatalog.All())
                .ToList();
        }
        else
        {
            try
            {
                project = ProjectInspector.ReadProject(projectFile);
                components = ComponentCatalog.Load(project);
            }
            catch (LlmException)
            {
                return null;
            }
        }

        components = components
            .Concat(ManifestRegistry.Components(
                project,
                project?.ProjectDirectory ?? Path.GetDirectoryName(Path.GetFullPath(file)) ?? workingDirectory))
            .GroupBy(component => component.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var byName = components
            .GroupBy(component => component.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var inspection = CsxAnalyzer.Analyze(file);
        var resolvedComponents = new List<CsxResolvedComponent>();
        var properties = new List<CsxResolvedBinding>();
        var events = new List<CsxResolvedBinding>();

        foreach (var element in inspection.Elements)
        {
            if (element.Kind == "fragment")
            {
                continue;
            }

            var helperName = element.Kind == "component"
                ? element.Name
                : Identifiers.ComponentName(element.Name);

            if (!byName.TryGetValue(helperName, out var component))
            {
                continue;
            }

            if (resolvedComponents.All(item => !item.Name.Equals(component.Name, StringComparison.Ordinal)))
            {
                resolvedComponents.Add(new CsxResolvedComponent
                {
                    Name = component.Name,
                    Package = component.Package,
                    Kind = component.Kind,
                    PropsType = component.PropsType
                });
            }

            var allProperties = component.Properties
                .Concat(component.Events.Select(eventInfo => new ComponentProperty
                {
                    Name = eventInfo.Name,
                    Type = eventInfo.Type,
                    IsEvent = true
                }))
                .ToList();

            foreach (var attribute in element.Attributes)
            {
                var property = allProperties.FirstOrDefault(item =>
                    item.Name.Equals(attribute.Name, StringComparison.Ordinal));

                if (property is null)
                {
                    property = allProperties.FirstOrDefault(item =>
                        item.Name.Equals(attribute.Name, StringComparison.OrdinalIgnoreCase));
                }

                if (property is null)
                {
                    continue;
                }

                var binding = new CsxResolvedBinding
                {
                    Component = component.Name,
                    Attribute = attribute.Name,
                    Property = property.Name,
                    Type = property.Type,
                    Line = attribute.Line
                };

                (property.IsEvent ? events : properties).Add(binding);
            }
        }

        return new CsxResolvedMetadata
        {
            Components = resolvedComponents,
            Properties = properties,
            Events = events
        };
    }
}
