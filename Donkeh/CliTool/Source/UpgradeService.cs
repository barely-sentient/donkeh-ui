using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Donkeh.Cli;

/// <summary>Upgrades a generated project and the installed CLI to a NuGet version.</summary>
internal static class UpgradeService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly Regex VersionPattern = new(
        @"^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant);

    /// <summary>Runs the upgrade sequence for one project.</summary>
    /// <param name="projectPath">Project file or project directory.</param>
    /// <param name="requestedVersion">Requested version, or null for the latest stable release.</param>
    /// <returns>The version used.</returns>
    public static string Upgrade(string? projectPath, string? requestedVersion)
    {
        var projectFile = ComponentGenerator.FindProject(projectPath);
        var (projectText, newLine) = TextFiles.Read(projectFile);

        if (projectText.Contains("Donkeh.props", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException(
                "This project uses a local framework checkout. Upgrade its checkout with git instead.");
        }

        if (!projectText.Contains("Donkeh", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException(
                $"The project '{Path.GetFileName(projectFile)}' does not reference Donkeh.");
        }

        var version = string.IsNullOrWhiteSpace(requestedVersion)
            ? GetLatestVersion()
            : NormalizeVersion(requestedVersion);

        Console.WriteLine($"Upgrading to Donkeh {version}...");

        Console.WriteLine("  clearing NuGet caches");
        RunDotnet(["nuget", "locals", "all", "--clear"], workingDirectory: null);

        var updated = CsprojEditor.SetDonkehVersion(projectText, newLine, version);
        updated = CsprojEditor.SetDonkehPackageVersions(updated, version);

        if (!string.Equals(projectText, updated, StringComparison.Ordinal))
        {
            TextFiles.Write(projectFile, updated);
            Console.WriteLine($"  updated {Path.GetFileName(projectFile)}");
        }

        Console.WriteLine("  restoring packages");
        RunDotnet(
            ["restore", projectFile, "--force", "--no-cache"],
            Path.GetDirectoryName(projectFile));

        Console.WriteLine("  updating the donkeh CLI");
        var update = RunDotnet(
            ["tool", "update", "--global", "Donkeh.CliTool", "--version", version],
            workingDirectory: null,
            throwOnError: false);

        if (update.ExitCode != 0)
        {
            // A global tool can be locked while its own process is running. Schedule
            // the same update for after this process exits and tell the user.
            if (IsToolInstalled())
            {
                ScheduleToolUpdate(version);
                Console.WriteLine("  CLI update scheduled for after this command exits");
            }
            else
            {
                var install = RunDotnet(
                    ["tool", "install", "--global", "Donkeh.CliTool", "--version", version],
                    workingDirectory: null,
                    throwOnError: false);

                if (install.ExitCode != 0)
                {
                    throw new CliException(
                        "The project was updated, but the donkeh CLI could not be installed." +
                        (install.Output.Length > 0
                            ? Environment.NewLine + install.Output.Trim()
                            : string.Empty));
                }

                Console.WriteLine("  installed the donkeh CLI");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Upgraded to Donkeh {version}.");
        return version;
    }

    /// <summary>Finds the latest stable CLI version on NuGet.</summary>
    private static string GetLatestVersion()
    {
        const string url = "https://api.nuget.org/v3-flatcontainer/donkeh.clitool/index.json";

        try
        {
            using var document = JsonDocument.Parse(Http.GetStringAsync(url).GetAwaiter().GetResult());

            var best = new Version(0, 0);

            foreach (var element in document.RootElement.GetProperty("versions").EnumerateArray())
            {
                var text = element.GetString();

                if (string.IsNullOrWhiteSpace(text) || text.Contains('-', StringComparison.Ordinal))
                {
                    continue;
                }

                if (Version.TryParse(text, out var candidate) && candidate > best)
                {
                    best = candidate;
                }
            }

            if (best > new Version(0, 0))
            {
                return best.ToString(3);
            }
        }
        catch (Exception exception)
        {
            throw new CliException(
                $"Could not query the latest Donkeh.CliTool version: {exception.Message}. " +
                "Pass --version <version> to upgrade offline.");
        }

        throw new CliException(
            "No stable Donkeh.CliTool version was found on NuGet. Pass --version <version>.");
    }

    /// <summary>Normalises a user-supplied version.</summary>
    private static string NormalizeVersion(string value)
    {
        var version = value.Trim().TrimStart('v', 'V');

        if (!VersionPattern.IsMatch(version))
        {
            throw new CliException(
                $"Invalid version '{value}'. Use a version such as 1.2.3 or 1.2.3-beta.1.");
        }

        return version;
    }

    /// <summary>Runs a dotnet command and throws when it exits non-zero.</summary>
    private static (int ExitCode, string Output) RunDotnet(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        bool throwOnError = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var output = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => Append(output, e.Data);
        process.ErrorDataReceived += (_, e) => Append(output, e.Data);

        try
        {
            process.Start();
        }
        catch (Exception exception)
        {
            return (-1, $"Could not start dotnet: {exception.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        process.WaitForExit();

        if (process.ExitCode != 0 && throwOnError)
        {
            throw new CliException(
                $"dotnet {string.Join(' ', arguments)} failed with exit code {process.ExitCode}." +
                (output.Length > 0 ? Environment.NewLine + output.ToString().Trim() : string.Empty));
        }

        return (process.ExitCode, output.ToString());
    }

    /// <summary>Writes one process line to the captured output.</summary>
    private static void Append(StringBuilder output, string? line)
    {
        if (!string.IsNullOrEmpty(line))
        {
            output.AppendLine(line);
        }
    }

    /// <summary>Returns whether the global donkeh tool is installed.</summary>
    private static bool IsToolInstalled()
    {
        var (exitCode, output) = RunDotnet(
            ["tool", "list", "--global"],
            workingDirectory: null,
            throwOnError: false);

        return exitCode == 0
            && output.Contains("donkeh.clitool", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Starts a detached helper that updates the global tool after exit.</summary>
    private static void ScheduleToolUpdate(string version)
    {
        var command =
            $"dotnet tool update --global Donkeh.CliTool --version {version}";

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var script = Path.Combine(
                    Path.GetTempPath(),
                    "donkeh-tool-update-" + Guid.NewGuid().ToString("N") + ".cmd");
                File.WriteAllText(
                    script,
                    $"@echo off\r\ntimeout /t 2 /nobreak >nul\r\n{command}\r\n");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"\" /b \"{script}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-c \"sleep 2; {command}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }
        catch
        {
            // The user still has the synchronous error output and the project update.
        }
    }
}
