namespace Donkeh.Cli;

/// <summary>Minimal, formatting-preserving edits to SDK-style project files.</summary>
internal static class CsprojEditor
{
    /// <summary>Adds an EmbeddedResource item when it is not already present.</summary>
    /// <param name="projectText">Current project file text.</param>
    /// <param name="newLine">Line ending used by the project file.</param>
    /// <param name="relativePath">Project-relative CSS path.</param>
    /// <returns>The updated project text.</returns>
    public static string AddEmbeddedResource(
        string projectText,
        string newLine,
        string relativePath)
    {
        if (ContainsResource(projectText, relativePath))
        {
            return projectText;
        }

        const string closingTag = "</Project>";
        var index = projectText.LastIndexOf(closingTag, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            throw new CliException("The project file does not contain a closing </Project> element.");
        }

        var block =
            $"  <ItemGroup>{newLine}" +
            $"    <EmbeddedResource Include=\"{relativePath}\" />{newLine}" +
            $"  </ItemGroup>{newLine}";

        var prefix = projectText[..index];

        if (!prefix.EndsWith(newLine, StringComparison.Ordinal))
        {
            prefix += newLine;
        }

        return prefix + block + projectText[index..];
    }

    /// <summary>Checks whether the project already references a resource path.</summary>
    private static bool ContainsResource(string projectText, string relativePath)
    {
        var windowsPath = relativePath.Replace('/', '\\');

        return projectText.Contains(relativePath, StringComparison.OrdinalIgnoreCase)
            || projectText.Contains(windowsPath, StringComparison.OrdinalIgnoreCase);
    }
}
