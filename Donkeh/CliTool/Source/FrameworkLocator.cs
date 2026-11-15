namespace Donkeh.Cli;

/// <summary>Finds the Donkeh framework checkout used by generated projects.</summary>
internal static class FrameworkLocator
{
    /// <summary>Finds the framework root directory.</summary>
    /// <param name="explicitPath">Optional user-supplied framework or repository path.</param>
    /// <returns>The absolute path to the <c>Donkeh</c> directory.</returns>
    /// <exception cref="CliException">No framework checkout was found.</exception>
    public static string Find(string? explicitPath)
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

        throw new CliException(
            "Could not find the Donkeh framework checkout. " +
            "Run the command inside the repository or pass --framework <path>.");
    }

    /// <summary>Resolves a framework or repository path to the framework directory.</summary>
    private static string? Normalize(string path)
    {
        var full = Path.GetFullPath(path);

        if (File.Exists(Path.Combine(full, "Build", "Donkeh.props")))
        {
            return full;
        }

        var framework = Path.Combine(full, "Donkeh");

        if (File.Exists(Path.Combine(framework, "Build", "Donkeh.props")))
        {
            return framework;
        }

        if (File.Exists(Path.Combine(full, "Donkeh.sln")))
        {
            return Directory.Exists(framework) ? framework : null;
        }

        return null;
    }
}
