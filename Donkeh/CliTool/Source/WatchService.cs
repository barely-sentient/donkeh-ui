using System.Diagnostics;

namespace Donkeh.Cli;

/// <summary>Runs a project in the development loop with CSS hot reload enabled.</summary>
internal static class WatchService
{
    /// <summary>Starts the project and blocks until it exits.</summary>
    /// <param name="projectPath">Project file, project directory or framework root.</param>
    /// <param name="appArguments">Arguments forwarded to the application after <c>--</c>.</param>
    /// <returns>The application's exit code.</returns>
    /// <exception cref="CliException">
    /// Hot reload is not enabled for the project, or the process cannot start.
    /// </exception>
    /// <remarks>
    /// The command runs <c>dotnet run</c> rather than <c>dotnet watch</c>. CSS files
    /// are embedded resources, so a build watcher would restart the process for
    /// every stylesheet edit. The hot reload package already polls the source files
    /// and rebuilds the cascade in place, which keeps component state and layout
    /// stable while styles change.
    /// </remarks>
    public static int Run(string? projectPath, IReadOnlyList<string> appArguments)
    {
        var projectFile = FeatureEnabler.FindTargetProject(projectPath);
        var (projectText, _) = TextFiles.Read(projectFile);

        if (!FeatureEnabler.IsHotReloadEnabled(projectText))
        {
            throw new CliException(
                $"CSS hot reload is not enabled for '{Path.GetFileName(projectFile)}'. " +
                $"Run: donkeh enable-feature hot-reload --project \"{projectFile}\"");
        }

        Console.WriteLine($"Watching CSS for {projectFile}");
        Console.WriteLine("Press Ctrl+C to stop.");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false
        };

        startInfo.Environment["DONKEH_HOT_RELOAD"] = "1";
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectFile);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Debug");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("net10.0");

        if (appArguments.Count > 0)
        {
            startInfo.ArgumentList.Add("--");

            foreach (var argument in appArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new CliException("Could not start dotnet run.");

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (CliException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CliException($"Could not start dotnet run: {exception.Message}");
        }
    }
}
