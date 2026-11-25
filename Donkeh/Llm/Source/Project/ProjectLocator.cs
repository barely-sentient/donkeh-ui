namespace Donkeh.Llm.Project;

/// <summary>Finds the Donkeh project a command should inspect.</summary>
public static class ProjectLocator
{
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    /// <summary>
    /// Resolves a project file from an explicit path, or from the working
    /// directory by walking up until exactly one project is found.
    /// </summary>
    /// <param name="workingDirectory">Directory the command was launched from.</param>
    /// <param name="explicitPath">Optional <c>--project</c> value.</param>
    /// <returns>The absolute project file path, or null when none exists.</returns>
    public static string? Find(string workingDirectory, string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var full = Path.GetFullPath(explicitPath);

            if (File.Exists(full))
            {
                return full;
            }

            if (!Directory.Exists(full))
            {
                throw new LlmException("LLM1001", $"Project path not found: {explicitPath}",
                    "Pass --project <dir> or run the command inside a Donkeh project.");
            }

            return FindInDirectory(full) ?? throw new LlmException(
                "LLM1002",
                $"No project file was found in '{full}'.",
                "Pass --project <project.csproj> or create a project with 'donkeh new project'.");
        }

        var current = new DirectoryInfo(Path.GetFullPath(workingDirectory));

        for (var level = 0; level < 8 && current is not null; level++)
        {
            var found = FindInDirectory(current.FullName);

            if (found is not null)
            {
                return found;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>Finds a project in one directory or its conventional subdirectories.</summary>
    private static string? FindInDirectory(string directory)
    {
        var direct = ProjectFiles(directory);

        if (direct.Length == 1)
        {
            return direct[0];
        }

        if (direct.Length > 1)
        {
            throw new LlmException(
                "LLM1003",
                $"Multiple project files were found in '{directory}'.",
                "Pass --project <project.csproj> to select one.");
        }

        foreach (var child in new[] { "src", "test-container" })
        {
            var nested = Path.Combine(directory, child);

            if (!Directory.Exists(nested))
            {
                continue;
            }

            var projects = ProjectFiles(nested);

            if (projects.Length == 1)
            {
                return projects[0];
            }
        }

        return null;
    }

    private static string[] ProjectFiles(string directory)
    {
        try
        {
            return Directory
                .GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(file => ProjectExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Returns the project directory for a project file.</summary>
    public static string DirectoryOf(string projectFile) =>
        Path.GetDirectoryName(Path.GetFullPath(projectFile))
        ?? throw new LlmException("LLM1004", $"Could not resolve the directory of '{projectFile}'.");
}
