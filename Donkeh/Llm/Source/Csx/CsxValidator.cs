using System.Text.Json;
using CSX.Transformer;
using Donkeh.Llm.Components;
using Donkeh.Llm.Extensibility;
using Donkeh.Llm.Project;

namespace Donkeh.Llm.Csx;

/// <summary>Validates CSX files with the shipped transformer and project component metadata.</summary>
public static class CsxValidator
{
    /// <summary>Validates one CSX file.</summary>
    public static CsxValidationResult Validate(string file, string workingDirectory, string? projectPath = null)
    {
        var fullPath = Path.GetFullPath(file);
        var diagnostics = new List<LlmDiagnostic>();
        var inspection = CsxAnalyzer.Analyze(fullPath);
        diagnostics.AddRange(inspection.Diagnostics);
        var source = File.ReadAllText(fullPath);
        var config = LoadConfig(fullPath, out var configError);

        if (configError is not null)
        {
            diagnostics.Add(LlmDiagnostics.Warning("CSX0006", configError, new LlmLocation { File = fullPath }));
        }

        try
        {
            new Transformer().TransformWithSourceMap(source, config);
        }
        catch (Exception exception)
        {
            diagnostics.Add(LlmDiagnostics.Error(
                "CSX0001",
                $"The CSX transformer rejected this file: {exception.Message}",
                new LlmLocation { File = fullPath, Line = 1, Column = 1 },
                ["Fix the markup reported by the transformer and run validation again."]));
        }

        var projectFile = string.IsNullOrWhiteSpace(projectPath)
            ? ProjectLocator.Find(workingDirectory, null)
            : ProjectLocator.Find(workingDirectory, projectPath);
        var components = new List<ComponentMetadata>();
        DonkehProject? project = null;

        if (projectFile is not null)
        {
            try
            {
                project = ProjectInspector.ReadProject(projectFile);
                components = ComponentCatalog.Load(project)
                    .Concat(ManifestRegistry.Components(project, workingDirectory))
                    .GroupBy(component => component.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                diagnostics.AddRange(project.Warnings);
            }
            catch (LlmException)
            {
                // A standalone .cx file has no project; validate against built-in elements only.
            }
        }

        if (components.Count == 0)
        {
            var fileDirectory = Path.GetDirectoryName(fullPath) ?? workingDirectory;
            components = SourceComponentReader.ReadProject(fileDirectory)
                .Concat(ManifestRegistry.Components(null, fileDirectory))
                .Concat(HtmlElementCatalog.All())
                .ToList();
        }

        var declared = DeclaredTypes(source);
        diagnostics.AddRange(ValidateComponents(inspection, components, declared, fullPath));
        diagnostics = diagnostics
            .GroupBy(diagnostic => (diagnostic.Code, diagnostic.Message, diagnostic.Location?.Line ?? 0,
                diagnostic.Location?.Column ?? 0))
            .Select(group => group.First())
            .OrderBy(diagnostic => diagnostic.Location?.Line ?? 0)
            .ThenBy(diagnostic => diagnostic.Location?.Column ?? 0)
            .ToList();

        return new CsxValidationResult
        {
            File = file,
            Valid = diagnostics.All(diagnostic => diagnostic.Severity != "error"),
            Diagnostics = diagnostics
        };
    }

    /// <summary>Loads <c>csx.json</c> by walking up from the source file.</summary>
    internal static TransformConfig LoadConfig(string file, out string? error)
    {
        error = null;
        var directory = new DirectoryInfo(Path.GetDirectoryName(file) ?? Environment.CurrentDirectory);

        for (var level = 0; level < 5 && directory is not null; level++)
        {
            var candidate = Path.Combine(directory.FullName, "csx.json");

            if (File.Exists(candidate))
            {
                try
                {
                    return JsonSerializer.Deserialize<TransformConfig>(
                        File.ReadAllText(candidate),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? new TransformConfig();
                }
                catch (Exception exception)
                {
                    error = $"{candidate} is not valid: {exception.Message}";
                    return new TransformConfig();
                }
            }

            directory = directory.Parent;
        }

        return new TransformConfig();
    }

    private static List<LlmDiagnostic> ValidateComponents(
        CsxInspectionResult inspection,
        IReadOnlyList<ComponentMetadata> components,
        HashSet<string> declared,
        string file)
    {
        var diagnostics = new List<LlmDiagnostic>();
        var byName = components
            .GroupBy(component => component.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var byNameInsensitive = components
            .GroupBy(component => component.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var element in inspection.Elements)
        {
            if (element.Kind == "fragment")
            {
                continue;
            }

            ComponentMetadata? component = null;

            if (element.Kind == "component")
            {
                if (!byName.TryGetValue(element.Name, out component))
                {
                    byNameInsensitive.TryGetValue(element.Name, out component);
                }

                if (component is null)
                {
                    if (declared.Contains(element.Name))
                    {
                        continue;
                    }

                    diagnostics.Add(LlmDiagnostics.Error(
                        "CSX1001",
                        $"Unknown component '{element.Name}'.",
                        new LlmLocation
                        {
                            File = file,
                            Line = element.Line,
                            Column = element.Column
                        },
                        Closest(element.Name, components.Select(item => item.Name)).ToList()));
                    continue;
                }
            }
            else
            {
                var helperName = Identifiers.ComponentName(element.Name);
                byName.TryGetValue(helperName, out component);
                byNameInsensitive.TryGetValue(helperName, out component);

                if (component is null || component.Kind != "html-element")
                {
                    // Unknown lowercase tags are still valid intrinsic elements:
                    // the transformer emits _.H("tag", ...) for any tag name.
                    continue;
                }
            }

            if (component is null)
            {
                continue;
            }

            var properties = component.Properties
                .Concat(component.Events.Select(eventInfo => new ComponentProperty
                {
                    Name = eventInfo.Name,
                    Type = eventInfo.Type,
                    Description = eventInfo.Description,
                    IsEvent = true
                }))
                .ToList();
            var propertyNames = properties.Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
            var given = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var attribute in element.Attributes)
            {
                if (attribute.Name.Equals("key", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                given.Add(attribute.Name);

                if (propertyNames.Contains(attribute.Name))
                {
                    continue;
                }

                if (component.Kind == "html-element")
                {
                    var alias = properties.FirstOrDefault(property =>
                        property.Name.Equals(attribute.Name, StringComparison.OrdinalIgnoreCase));

                    if (alias is not null)
                    {
                        continue;
                    }

                    diagnostics.Add(LlmDiagnostics.Error(
                        "CSX1010",
                        $"Unknown HTML attribute '{attribute.Name}' on <{element.Name}>.",
                        new LlmLocation
                        {
                            File = file,
                            Line = attribute.Line,
                            Column = attribute.Column
                        },
                        Closest(attribute.Name, propertyNames).ToList()));
                    continue;
                }

                var candidate = properties.FirstOrDefault(property =>
                    property.Name.Equals(attribute.Name, StringComparison.OrdinalIgnoreCase));

                if (candidate is not null)
                {
                    diagnostics.Add(LlmDiagnostics.Error(
                        "CSX1004",
                        $"Unknown component property '{attribute.Name}' on {component.Name}.",
                        new LlmLocation
                        {
                            File = file,
                            Line = attribute.Line,
                            Column = attribute.Column
                        },
                        [candidate.Name]));
                    continue;
                }

                var suggestions = Closest(attribute.Name, propertyNames).ToList();

                if (attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    var eventName = "On" + attribute.Name[2..];

                    if (propertyNames.Contains(eventName))
                    {
                        suggestions.Insert(0, eventName);
                    }
                }

                diagnostics.Add(LlmDiagnostics.Error(
                    "CSX1004",
                    $"Unknown component property '{attribute.Name}' on {component.Name}.",
                    new LlmLocation
                    {
                        File = file,
                        Line = attribute.Line,
                        Column = attribute.Column
                    },
                    suggestions));
            }

            foreach (var required in properties.Where(property => property.Required && !property.IsEvent))
            {
                if (!given.Contains(required.Name))
                {
                    diagnostics.Add(LlmDiagnostics.Error(
                        "CSX1008",
                        $"Required property '{required.Name}' is missing on {component.Name}.",
                        new LlmLocation
                        {
                            File = file,
                            Line = element.Line,
                            Column = element.Column
                        },
                        [$"Add {required.Name}=\"value\" or {required.Name}={{expression}}."]));
                }
            }
        }

        return diagnostics;
    }

    private static HashSet<string> DeclaredTypes(string source) =>
        System.Text.RegularExpressions.Regex
            .Matches(SourceScanner.Mask(source), @"\bclass\s+(?<name>[A-Za-z_]\w*)")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

    internal static IEnumerable<string> Closest(string value, IEnumerable<string> candidates)
    {
        return candidates
            .Select(candidate => (Candidate: candidate, Distance: Levenshtein(value, candidate)))
            .Where(item => item.Distance <= Math.Max(2, value.Length / 3))
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Candidate, StringComparer.Ordinal)
            .Take(3)
            .Select(item => item.Candidate);
    }

    private static int Levenshtein(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= right.Length; j++)
            {
                var cost = char.ToLowerInvariant(left[i - 1]) == char.ToLowerInvariant(right[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
