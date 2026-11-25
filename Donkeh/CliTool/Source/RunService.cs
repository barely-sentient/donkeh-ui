using System.ComponentModel;
using System.Diagnostics;

namespace Donkeh.Cli;

/// <summary>Runs a project, transforming CSX sources when the project uses them.</summary>
internal static class RunService
{
    /// <summary>Runs the project and blocks until it exits.</summary>
    /// <param name="projectPath">Project file, project directory or framework root.</param>
    /// <param name="appArguments">Arguments forwarded to the application after <c>--</c>.</param>
    /// <returns>The application's exit code.</returns>
    /// <exception cref="CliException">
    /// The project cannot be resolved or its process cannot start.
    /// </exception>
    /// <remarks>
    /// A framework root resolves to its <c>test-container</c>. CSX projects are
    /// transformed by the CSX CLI first; referenced CSX projects are transformed
    /// and built so a library change is part of the run.
    /// </remarks>
    public static int Run(string? projectPath, IReadOnlyList<string> appArguments)
    {
        var projectFile = FeatureEnabler.FindTargetProject(projectPath);
        var directory = Path.GetDirectoryName(projectFile) ?? Environment.CurrentDirectory;

        if (!ComponentGenerator.IsCsxProject(projectFile))
        {
            return Start("dotnet", ["run", "--project", projectFile, .. Forward(appArguments)], directory);
        }

        foreach (var dependency in ComponentGenerator.FindCsxDependencies(projectFile))
        {
            var dependencyDirectory = Path.GetDirectoryName(dependency) ?? directory;
            var buildCode = Start(
                "csx",
                ["build", "--project", dependency, "-c", "Debug"],
                dependencyDirectory);

            if (buildCode != 0)
            {
                return buildCode;
            }
        }

        return Start(
            "csx",
            ["run", "--project", projectFile, "-c", "Debug", .. Forward(appArguments)],
            directory);
    }

    private static string[] Forward(IReadOnlyList<string> appArguments) =>
        appArguments.Count == 0 ? [] : ["--", .. appArguments];

    private static int Start(string fileName, string[] arguments, string directory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = directory,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new CliException($"Could not start {fileName}.");

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception exception) when (fileName.Equals("csx", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException(
                $"Could not start the CSX tool: {exception.Message}. " +
                "Install it with: dotnet tool install --global CSX.CliTool");
        }
        catch (CliException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CliException($"Could not start {fileName}: {exception.Message}");
        }
    }
}
