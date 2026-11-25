using Donkeh.Llm.Css;
using Donkeh.Llm.Docs;
using Donkeh.Llm.Project;
using Donkeh.Llm.Renderers;

namespace Donkeh.Llm.Diagnostics;

/// <summary>One environment or project health check.</summary>
public sealed class DoctorCheck
{
    /// <summary>Stable check name.</summary>
    public required string Name { get; init; }

    /// <summary>ok, warning or error.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable result.</summary>
    public required string Message { get; init; }

    /// <summary>Additional machine-readable detail.</summary>
    public IReadOnlyDictionary<string, object?>? Details { get; init; }
}

/// <summary>Result of <c>donkeh doctor</c>.</summary>
public sealed class DoctorResult
{
    /// <summary>ok, warning or error.</summary>
    public required string Status { get; init; }

    /// <summary>Environment summary.</summary>
    public required DoctorEnvironment Environment { get; init; }

    /// <summary>Every check in execution order.</summary>
    public IReadOnlyList<DoctorCheck> Checks { get; init; } = [];
}

/// <summary>Runtime environment details for doctor.</summary>
public sealed class DoctorEnvironment
{
    /// <summary>CLI version, when supplied by the caller.</summary>
    public string? CliVersion { get; init; }

    /// <summary>.NET runtime version.</summary>
    public required string RuntimeVersion { get; init; }

    /// <summary>Operating system description.</summary>
    public required string OperatingSystem { get; init; }

    /// <summary>Current working directory.</summary>
    public required string WorkingDirectory { get; init; }
}

/// <summary>General Donkeh environment and project health checks.</summary>
public static class Doctor
{
    /// <summary>Runs doctor checks.</summary>
    public static DoctorResult Run(string workingDirectory, string? cliVersion = null, string? projectPath = null)
    {
        var checks = new List<DoctorCheck>
        {
            new()
            {
                Name = "runtime",
                Status = "ok",
                Message = $".NET {Environment.Version} on {Environment.OSVersion.Platform}.",
                Details = new Dictionary<string, object?>
                {
                    ["runtime"] = Environment.Version.ToString(),
                    ["os"] = Environment.OSVersion.ToString()
                }
            }
        };

        var projectFile = ProjectLocator.Find(workingDirectory, projectPath);

        if (projectFile is null)
        {
            checks.Add(new DoctorCheck
            {
                Name = "project",
                Status = "warning",
                Message = "No Donkeh project was found from the current directory.",
                Details = new Dictionary<string, object?> { ["workingDirectory"] = workingDirectory }
            });
        }
        else
        {
            try
            {
                var project = ProjectInspector.ReadProject(projectFile);
                checks.Add(new DoctorCheck
                {
                    Name = "project",
                    Status = "ok",
                    Message = $"Project {project.Name} ({project.ProjectType}) at {project.ProjectFile}.",
                    Details = new Dictionary<string, object?>
                    {
                        ["project"] = project.ProjectFile,
                        ["type"] = project.ProjectType,
                        ["targetFrameworks"] = project.TargetFrameworks,
                        ["renderer"] = project.Renderer.Name,
                        ["csx"] = project.Csx.Present,
                        ["restored"] = project.HasRestoredAssets
                    }
                });

                checks.Add(new DoctorCheck
                {
                    Name = "donkeh-packages",
                    Status = project.DonkehPackages.Any() || project.UsesLocalCheckout ? "ok" : "warning",
                    Message = project.DonkehPackages.Any()
                        ? $"{project.DonkehPackages.Count()} Donkeh-related package reference(s)."
                        : project.UsesLocalCheckout
                            ? "Using a local Donkeh framework checkout."
                            : "No Donkeh-related package references were found.",
                    Details = new Dictionary<string, object?>
                    {
                        ["packages"] = project.DonkehPackages
                            .Select(package => new { package.Id, package.ResolvedVersion })
                            .ToList(),
                        ["localCheckout"] = project.UsesLocalCheckout
                    }
                });

                var diagnosis = ProjectDiagnostics.Diagnose(workingDirectory, projectFile);
                checks.Add(new DoctorCheck
                {
                    Name = "diagnostics",
                    Status = diagnosis.Status,
                    Message = $"{diagnosis.Summary.Errors} error(s), {diagnosis.Summary.Warnings} warning(s), {diagnosis.Summary.Info} info.",
                    Details = new Dictionary<string, object?>
                    {
                        ["errors"] = diagnosis.Summary.Errors,
                        ["warnings"] = diagnosis.Summary.Warnings
                    }
                });
            }
            catch (LlmException exception)
            {
                checks.Add(new DoctorCheck
                {
                    Name = "project",
                    Status = "error",
                    Message = exception.Message,
                    Details = new Dictionary<string, object?> { ["code"] = exception.Code }
                });
            }
        }

        var csxPackage = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "csx.transformer");

        checks.Add(new DoctorCheck
        {
            Name = "csx",
            Status = Directory.Exists(csxPackage) ? "ok" : "warning",
            Message = Directory.Exists(csxPackage)
                ? "CSX transformer package is available in the NuGet cache."
                : "The CSX transformer package was not found in the NuGet cache; CSX commands still use the bundled transformer.",
            Details = new Dictionary<string, object?> { ["cache"] = csxPackage }
        });

        var docs = DocsIndex.Topics();
        checks.Add(new DoctorCheck
        {
            Name = "documentation",
            Status = docs.Count > 0 ? "ok" : "warning",
            Message = docs.Count > 0
                ? $"{docs.Count} documentation topics are available for search."
                : "The embedded documentation index is empty."
        });

        var cssProperties = CssCatalog.Properties();
        checks.Add(new DoctorCheck
        {
            Name = "css-engine",
            Status = cssProperties.Count > 0 ? "ok" : "error",
            Message = cssProperties.Count > 0
                ? $"{cssProperties.Count} CSS properties are registered with the resolver."
                : "The CSS property resolver returned no registered properties."
        });

        var renderers = RendererCatalog.All();
        checks.Add(new DoctorCheck
        {
            Name = "renderers",
            Status = renderers.Count > 0 ? "ok" : "warning",
            Message = renderers.Count > 0
                ? $"{renderers.Count} renderer capability sets are available."
                : "No renderer capability metadata is available."
        });

        checks.Add(new DoctorCheck
        {
            Name = "mcp",
            Status = "ok",
            Message = "MCP stdio hosting is available. Run 'donkeh mcp start' to serve tools.",
            Details = new Dictionary<string, object?> { ["transport"] = "stdio" }
        });

        var status = checks.Any(check => check.Status == "error")
            ? "error"
            : checks.Any(check => check.Status == "warning") ? "warning" : "ok";

        return new DoctorResult
        {
            Status = status,
            Environment = new DoctorEnvironment
            {
                CliVersion = cliVersion,
                RuntimeVersion = Environment.Version.ToString(),
                OperatingSystem = Environment.OSVersion.ToString(),
                WorkingDirectory = workingDirectory
            },
            Checks = checks
        };
    }
}
