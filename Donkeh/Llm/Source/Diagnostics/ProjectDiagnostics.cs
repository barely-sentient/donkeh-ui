using Donkeh.Llm.Components;
using Donkeh.Llm.Csx;
using Donkeh.Llm.Css;
using Donkeh.Llm.Project;
using Donkeh.Llm.Renderers;

namespace Donkeh.Llm.Diagnostics;

/// <summary>Framework-aware project diagnostics.</summary>
public sealed class ProjectDiagnosis
{
    /// <summary>Project file that was inspected.</summary>
    public required string Project { get; init; }

    /// <summary>ok, warning or error.</summary>
    public required string Status { get; init; }

    /// <summary>Issue counts by severity.</summary>
    public ProjectDiagnosisSummary Summary { get; init; } = new();

    /// <summary>Every issue found by actual inspection or validation.</summary>
    public IReadOnlyList<LlmDiagnostic> Issues { get; init; } = [];
}

/// <summary>Issue counts for a project diagnosis.</summary>
public sealed class ProjectDiagnosisSummary
{
    /// <summary>Number of error diagnostics.</summary>
    public int Errors { get; init; }

    /// <summary>Number of warning diagnostics.</summary>
    public int Warnings { get; init; }

    /// <summary>Number of info diagnostics.</summary>
    public int Info { get; init; }
}

/// <summary>Runs project-level checks against the files and metadata the project actually contains.</summary>
public static class ProjectDiagnostics
{
    /// <summary>Diagnoses a project.</summary>
    public static ProjectDiagnosis Diagnose(string workingDirectory, string? projectPath = null)
    {
        var project = ProjectInspector.Read(workingDirectory, projectPath);
        var issues = new List<LlmDiagnostic>(project.Warnings);

        CheckPackages(project, issues);
        CheckConfiguration(project, issues);
        CheckReferences(project, issues);
        CheckStylesheets(project, issues);
        CheckCsx(project, issues);
        CheckCss(project, issues);
        CheckRenderer(project, issues);

        issues = issues
            .GroupBy(issue => (issue.Code, issue.Message, issue.Location?.File, issue.Location?.Line ?? 0))
            .Select(group => group.First())
            .OrderByDescending(issue => SeverityRank(issue.Severity))
            .ThenBy(issue => issue.Location?.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.Location?.Line ?? 0)
            .ToList();

        var errors = issues.Count(issue => issue.Severity == "error");
        var warnings = issues.Count(issue => issue.Severity == "warning");
        var info = issues.Count(issue => issue.Severity == "info");

        return new ProjectDiagnosis
        {
            Project = project.ProjectFile,
            Status = errors > 0 ? "error" : warnings > 0 ? "warning" : "ok",
            Summary = new ProjectDiagnosisSummary { Errors = errors, Warnings = warnings, Info = info },
            Issues = issues
        };
    }

    private static void CheckPackages(DonkehProject project, List<LlmDiagnostic> issues)
    {
        var donkehPackages = project.DonkehPackages.ToList();

        if (donkehPackages.Count == 0 && !project.UsesLocalCheckout)
        {
            issues.Add(LlmDiagnostics.Warning(
                "DIAG1001",
                "The project does not reference any Donkeh packages.",
                new LlmLocation { File = project.ProjectFile },
                ["Add Donkeh.Windowing or Donkeh.UILab, or create a project with 'donkeh new project'."]));
        }

        if (donkehPackages.Count > 0
            && !project.UsesLocalCheckout
            && !donkehPackages.Any(package => package.Id.Equals("Donkeh.UILab", StringComparison.OrdinalIgnoreCase))
            && !project.ProjectReferences.Any(reference =>
                reference.Include.Contains("UILab", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(LlmDiagnostics.Warning(
                "DIAG1002",
                "No Donkeh.UILab package or project reference was found.",
                new LlmLocation { File = project.ProjectFile },
                ["Components derive from Donkeh.UILab.Component, so a UILab reference is normally required."]));
        }

        var versions = donkehPackages
            .Where(package => !string.IsNullOrWhiteSpace(package.ResolvedVersion)
                && !package.ResolvedVersion!.StartsWith("$(", StringComparison.Ordinal))
            .GroupBy(package => package.ResolvedVersion!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (versions.Count > 1)
        {
            issues.Add(LlmDiagnostics.Error(
                "DIAG1003",
                "Donkeh packages resolve to more than one version: " +
                string.Join(", ", versions.Select(group => group.Key)),
                new LlmLocation { File = project.ProjectFile },
                ["Align all Donkeh package versions or update them with 'donkeh upgrade project'."]));
        }

        if (project.Packages.Any(package =>
                string.Equals(package.RequestedVersion, "$(DonkehVersion)", StringComparison.OrdinalIgnoreCase))
            && string.IsNullOrWhiteSpace(project.DonkehVersion))
        {
            issues.Add(LlmDiagnostics.Error(
                "DIAG1004",
                "A package uses $(DonkehVersion) but the property is not defined.",
                new LlmLocation { File = project.ProjectFile },
                ["Define <DonkehVersion>...</DonkehVersion> in the project or remove the property reference."]));
        }
    }

    private static void CheckConfiguration(DonkehProject project, List<LlmDiagnostic> issues)
    {
        if (project.TargetFrameworks.Count == 0)
        {
            issues.Add(LlmDiagnostics.Warning(
                "DIAG1005",
                "The project does not declare a target framework.",
                new LlmLocation { File = project.ProjectFile },
                ["Add <TargetFramework>net10.0</TargetFramework> or <TargetFrameworks>...</TargetFrameworks>."]));
        }

        if (project.Csx.Present && !project.Csx.Valid)
        {
            issues.Add(LlmDiagnostics.Error(
                "DIAG1006",
                $"CSX configuration is invalid: {project.Csx.Error}",
                new LlmLocation { File = project.Csx.File ?? project.ProjectFile }));
        }

        if (project.Csx.Present && project.Csx.File is null)
        {
            issues.Add(LlmDiagnostics.Warning(
                "DIAG1007",
                "CSX files were found but there is no csx.json.",
                new LlmLocation { File = project.ProjectFile },
                ["Run 'csx build' or create csx.json with the documented defaults."]));
        }

        if (!project.HasRestoredAssets && project.DonkehPackages.Any())
        {
            issues.Add(LlmDiagnostics.Info(
                "DIAG1008",
                "No restore assets were found; dependency versions may be unresolved.",
                new LlmLocation { File = project.ProjectFile },
                ["Run 'dotnet restore' before relying on dependency inspection."]));
        }
    }

    private static void CheckReferences(DonkehProject project, List<LlmDiagnostic> issues)
    {
        foreach (var reference in project.ProjectReferences.Where(reference => reference.FullPath is null))
        {
            issues.Add(LlmDiagnostics.Error(
                "DIAG1009",
                $"Project reference '{reference.Include}' does not exist.",
                new LlmLocation { File = project.ProjectFile },
                ["Fix the reference path or remove the reference."]));
        }

        if (project.UsesLocalCheckout)
        {
            try
            {
                var document = System.Xml.Linq.XDocument.Load(project.ProjectFile);
                var importElement = document.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "Import"
                        && element.Attribute("Project")?.Value.Contains("Donkeh.props", StringComparison.OrdinalIgnoreCase) == true);

                if (importElement is not null)
                {
                    var path = Path.GetFullPath(Path.Combine(
                        project.ProjectDirectory,
                        importElement.Attribute("Project")!.Value));

                    if (!File.Exists(path))
                    {
                        issues.Add(LlmDiagnostics.Error(
                            "DIAG1010",
                            $"The local framework import '{path}' does not exist.",
                            new LlmLocation { File = project.ProjectFile },
                            ["Switch back to NuGet with 'donkeh use-nuget' or fix the framework path."]));
                    }
                }
            }
            catch (Exception)
            {
                // The project file itself is reported by the reader.
            }
        }
    }

    private static void CheckStylesheets(DonkehProject project, List<LlmDiagnostic> issues)
    {
        foreach (var stylesheet in project.Stylesheets)
        {
            if (stylesheet.Contains('*', StringComparison.Ordinal))
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(project.ProjectDirectory, stylesheet));

            if (!File.Exists(path))
            {
                issues.Add(LlmDiagnostics.Error(
                    "DIAG1011",
                    $"Embedded stylesheet '{stylesheet}' does not exist.",
                    new LlmLocation { File = project.ProjectFile },
                    ["Create the file or remove the EmbeddedResource entry."]));
            }
        }
    }

    private static void CheckCsx(DonkehProject project, List<LlmDiagnostic> issues)
    {
        if (!project.Csx.Present)
        {
            return;
        }

        foreach (var file in SourceFiles(project.ProjectDirectory, "*.cx").Take(100))
        {
            try
            {
                var result = CsxValidator.Validate(file, project.ProjectDirectory, project.ProjectFile);
                issues.AddRange(result.Diagnostics.Where(diagnostic => diagnostic.Severity != "info"));
            }
            catch (LlmException exception)
            {
                issues.Add(LlmDiagnostics.Error("DIAG1012", exception.Message,
                    new LlmLocation { File = file }));
            }
        }
    }

    private static void CheckCss(DonkehProject project, List<LlmDiagnostic> issues)
    {
        foreach (var file in SourceFiles(project.ProjectDirectory, "*.css").Take(100))
        {
            try
            {
                var result = CssValidator.Validate(file);
                issues.AddRange(result.Diagnostics);
            }
            catch (LlmException exception)
            {
                issues.Add(LlmDiagnostics.Error("DIAG1013", exception.Message,
                    new LlmLocation { File = file }));
            }
        }
    }

    private static void CheckRenderer(DonkehProject project, List<LlmDiagnostic> issues)
    {
        if (project.Renderer.Name == "unknown")
        {
            issues.Add(LlmDiagnostics.Warning(
                "DIAG1014",
                "No renderer was detected for the project.",
                new LlmLocation { File = project.ProjectFile },
                ["Add a renderer package such as Donkeh.Windowing or set DonkehDesktop/DonkehAndroid/DonkehWeb."]));
        }
        else if (RendererCatalog.Find(project.Renderer.Name) is null)
        {
            issues.Add(LlmDiagnostics.Warning(
                "DIAG1015",
                $"Renderer '{project.Renderer.Name}' is configured but no capability metadata is available.",
                new LlmLocation { File = project.ProjectFile }));
        }
    }

    private static IEnumerable<string> SourceFiles(string directory, string pattern)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
                .Where(file => !file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(part => part.Equals("bin", StringComparison.OrdinalIgnoreCase)
                        || part.Equals("obj", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "error" => 0,
        "warning" => 1,
        _ => 2
    };
}
