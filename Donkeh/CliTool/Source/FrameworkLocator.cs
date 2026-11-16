namespace Donkeh.Cli;

/// <summary>Finds a local Donkeh framework checkout, when one is available.</summary>
internal static class FrameworkLocator
{
    /// <summary>Finds the framework root directory.</summary>
    /// <param name="explicitPath">Optional user-supplied framework or repository path.</param>
    /// <returns>
    /// The absolute path to the <c>Donkeh</c> directory, or <see langword="null"/>
    /// when no checkout is present and the CLI should use NuGet packages.
    /// </returns>
    /// <exception cref="CliException">An explicit path does not contain a framework.</exception>
    public static string? Find(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var resolved = Normalize(explicitPath);

            if (resolved is not null)
            {
                return resolved;
            }

            throw new CliException(
                $"Could not find a Donkeh framework at '{explicitPath}'. " +
                "Pass the repository root or the Donkeh directory.");
        }

        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));

            while (directory is not null)
            {
                var resolved = Normalize(directory.FullName);

                if (resolved is not null)
                {
                    return resolved;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    /// <summary>Resolves a framework or repository path to the framework directory.</summary>
    private static string? Normalize(string path)
    {
        var full = Path.GetFullPath(path);

        // A solution or project file path selects the directory that contains it.
        if (File.Exists(full))
        {
            full = Path.GetDirectoryName(full) ?? full;
        }

        if (File.Exists(Path.Combine(full, "Build", "Donkeh.props")))
        {
            return full;
        }

        var framework = Path.Combine(full, "Donkeh");

        if (File.Exists(Path.Combine(framework, "Build", "Donkeh.props")))
        {
            return framework;
        }

        if (File.Exists(Path.Combine(full, "Donkeh.sln"))
            && Directory.Exists(framework))
        {
            return framework;
        }

        return null;
    }
}
