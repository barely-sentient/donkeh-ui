using System.Text.RegularExpressions;
using Donkeh.Llm.Components;
using Donkeh.Llm.Csx;
using Donkeh.Llm.Extensibility;
using Donkeh.Llm.Project;

namespace Donkeh.Llm.Diagnostics;

/// <summary>Component-level diagnostics rooted in source, metadata and CSX validation.</summary>
public sealed class ComponentDiagnosis
{
    /// <summary>Component name as requested.</summary>
    public required string Component { get; init; }

    /// <summary>Resolved component metadata, when found.</summary>
    public ComponentMetadata? Metadata { get; init; }

    /// <summary>ok, warning or error.</summary>
    public required string Status { get; init; }

    /// <summary>Every established issue.</summary>
    public IReadOnlyList<LlmDiagnostic> Issues { get; init; } = [];
}

/// <summary>Runs component diagnostics without speculative warnings.</summary>
public static class ComponentDiagnostics
{
    /// <summary>Diagnoses one component.</summary>
    public static ComponentDiagnosis Diagnose(
        string name,
        string workingDirectory,
        string? projectPath = null)
    {
        var project = ProjectInspector.Read(workingDirectory, projectPath);
        var components = ComponentCatalog.Load(project)
            .Concat(ManifestRegistry.Components(project, workingDirectory))
            .GroupBy(component => component.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var component = components.FirstOrDefault(item =>
                            item.Name.Equals(name, StringComparison.Ordinal))
                        ?? components.FirstOrDefault(item =>
                            item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (component is null)
        {
            throw new LlmException(
                "DIAG2001",
                $"Component '{name}' was not found in the project or its packages.",
                "Run 'donkeh component search <query>' to find available components.");
        }

        var issues = new List<LlmDiagnostic>();

        if (component.Kind == "html-element")
        {
            return new ComponentDiagnosis
            {
                Component = component.Name,
                Metadata = component,
                Status = "ok",
                Issues = issues
            };
        }

        if (component.Kind is "class" or "csx" && component.Source is not null)
        {
            var sourcePath = ResolveSource(project, component);

            if (sourcePath is not null && File.Exists(sourcePath))
            {
                CheckSource(sourcePath, component, issues);

                if (component.Kind == "csx")
                {
                    try
                    {
                        var validation = CsxValidator.Validate(sourcePath, workingDirectory, project.ProjectFile);
                        issues.AddRange(validation.Diagnostics.Where(diagnostic =>
                            diagnostic.Severity != "info"));
                    }
                    catch (LlmException exception)
                    {
                        issues.Add(LlmDiagnostics.Error("DIAG2002", exception.Message,
                            new LlmLocation { File = component.Source ?? "" }));
                    }
                }
            }
        }

        if (component.PropsType is not null
            && component.Properties.Count == 0
            && component.Events.Count == 0
            && component.Kind is "class" or "csx")
        {
            issues.Add(LlmDiagnostics.Warning(
                "DIAG2003",
                $"Props type '{component.PropsType}' was not found or exposes no public properties.",
                new LlmLocation { File = component.Source ?? "" },
                ["Check that the props class is in the project and its properties are public."]));
        }

        foreach (var stylesheet in component.Stylesheets)
        {
            var path = Path.GetFullPath(Path.Combine(project.ProjectDirectory, stylesheet));

            if (!File.Exists(path))
            {
                issues.Add(LlmDiagnostics.Error(
                    "DIAG2004",
                    $"Declared stylesheet '{stylesheet}' does not exist.",
                    new LlmLocation { File = component.Source ?? "" },
                    ["Create the stylesheet or update the Stylesheet attribute."]));
            }
        }

        issues = issues
            .GroupBy(issue => (issue.Code, issue.Message, issue.Location?.Line ?? 0, issue.Location?.Column ?? 0))
            .Select(group => group.First())
            .OrderByDescending(issue => issue.Severity == "error")
            .ThenBy(issue => issue.Location?.Line ?? 0)
            .ToList();

        return new ComponentDiagnosis
        {
            Component = component.Name,
            Metadata = component,
            Status = issues.Any(issue => issue.Severity == "error")
                ? "error"
                : issues.Any(issue => issue.Severity == "warning") ? "warning" : "ok",
            Issues = issues
        };
    }

    private static string? ResolveSource(DonkehProject project, ComponentMetadata component)
    {
        if (component.Source is null)
        {
            return null;
        }

        return Path.IsPathRooted(component.Source)
            ? component.Source
            : Path.GetFullPath(Path.Combine(project.ProjectDirectory, component.Source));
    }

    private static void CheckSource(
        string sourcePath,
        ComponentMetadata component,
        List<LlmDiagnostic> issues)
    {
        var source = File.ReadAllText(sourcePath);
        var masked = SourceScanner.Mask(source);
        var classMatch = Regex.Match(
            masked,
            $@"\bclass\s+{Regex.Escape(component.Name)}\b[^{{]*\{{");

        if (!classMatch.Success)
        {
            issues.Add(LlmDiagnostics.Warning(
                "DIAG2005",
                $"Component type '{component.Name}' was not found in '{Path.GetFileName(sourcePath)}'.",
                new LlmLocation { File = component.Source ?? "" }));
            return;
        }

        var open = masked.IndexOf('{', classMatch.Index);
        var close = SourceScanner.FindBlockEnd(masked, open);
        var body = masked[open..Math.Min(close + 1, masked.Length)];

        if (Regex.IsMatch(body, @"\bRender\s*\(\s*\)\s*=>\s*null\s*;"))
        {
            issues.Add(LlmDiagnostics.Error(
                "DIAG2006",
                $"Component '{component.Name}' returns null from Render.",
                new LlmLocation { File = component.Source ?? "" },
                ["Return an empty fragment instead of null."]));
        }

        var constructors = Regex.Matches(
            body,
            $@"\bpublic\s+{Regex.Escape(component.Name)}\s*\((?<args>[^)]*)\)");

        if (constructors.Count > 0
            && constructors.All(match => !string.IsNullOrWhiteSpace(match.Groups["args"].Value)))
        {
            issues.Add(LlmDiagnostics.Error(
                "DIAG2007",
                $"Component '{component.Name}' has no public parameterless constructor.",
                new LlmLocation { File = component.Source ?? "" },
                ["Add a public parameterless constructor; the renderer requires one."]));
        }

        foreach (var eventInfo in component.Events)
        {
            if (!eventInfo.Type.Contains("Action", StringComparison.Ordinal)
                && !eventInfo.Type.Contains("Func", StringComparison.Ordinal)
                && !eventInfo.Type.Contains("Delegate", StringComparison.Ordinal))
            {
                issues.Add(LlmDiagnostics.Warning(
                    "DIAG2008",
                    $"Event '{eventInfo.Name}' has type '{eventInfo.Type}', which is not a delegate.",
                    new LlmLocation { File = component.Source ?? "" }));
            }
        }

        if (component.Kind == "class"
            && !body.Contains("override Node Render", StringComparison.Ordinal)
            && !body.Contains("override Donkeh.UILab.Node Render", StringComparison.Ordinal))
        {
            issues.Add(LlmDiagnostics.Warning(
                "DIAG2009",
                $"Component '{component.Name}' does not appear to override Render.",
                new LlmLocation { File = component.Source ?? "" }));
        }
    }
}
