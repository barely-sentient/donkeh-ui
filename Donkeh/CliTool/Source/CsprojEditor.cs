using System.Text.RegularExpressions;

namespace Donkeh.Cli;

/// <summary>Minimal, formatting-preserving edits to SDK-style project files.</summary>
internal static class CsprojEditor
{
    /// <summary>
    /// Sets the <c>DonkehVersion</c> property, adding it to the first property
    /// group when absent.
    /// </summary>
    /// <param name="projectText">Current project file text.</param>
    /// <param name="newLine">Line ending used by the project file.</param>
    /// <param name="version">Package version to write.</param>
    /// <returns>The updated project text.</returns>
    public static string SetDonkehVersion(
        string projectText,
        string newLine,
        string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var property = new Regex(
            @"<DonkehVersion(?<attributes>[^>]*)>.*?</DonkehVersion>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (property.IsMatch(projectText))
        {
            return property.Replace(
                projectText,
                match => $"<DonkehVersion{match.Groups["attributes"].Value}>{version}</DonkehVersion>");
        }

        var group = new Regex(
            @"<PropertyGroup(?<attributes>[^>]*)>",
            RegexOptions.IgnoreCase);

        var matchGroup = group.Match(projectText);

        if (!matchGroup.Success)
        {
            throw new CliException("The project file does not contain a PropertyGroup.");
        }

        var insertion = $"{newLine}    <DonkehVersion>{version}</DonkehVersion>";
        return projectText.Insert(matchGroup.Index + matchGroup.Length, insertion);
    }

    /// <summary>
    /// Updates literal <c>Donkeh.*</c> package versions, leaving property-based
    /// versions such as <c>$(DonkehVersion)</c> to follow the property.
    /// </summary>
    /// <param name="projectText">Current project file text.</param>
    /// <param name="version">Version to apply.</param>
    /// <returns>The updated project text.</returns>
    public static string SetDonkehPackageVersions(string projectText, string version)
    {
        var includeFirst = new Regex(
            """(?<start><PackageReference[^>]*Include="Donkeh\.[^"]*"[^>]*Version=")(?<version>[^"]*)(?<end>")""",
            RegexOptions.IgnoreCase);

        projectText = includeFirst.Replace(projectText, match =>
            match.Groups["version"].Value.StartsWith("$(", StringComparison.Ordinal)
                ? match.Value
                : match.Groups["start"].Value + version + match.Groups["end"].Value);

        var versionFirst = new Regex(
            """(?<start><PackageReference[^>]*Version=")(?<version>[^"]*)(?<end>"[^>]*Include="Donkeh\.[^"]*")""",
            RegexOptions.IgnoreCase);

        return versionFirst.Replace(projectText, match =>
            match.Groups["version"].Value.StartsWith("$(", StringComparison.Ordinal)
                ? match.Value
                : match.Groups["start"].Value + version + match.Groups["end"].Value);
    }

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
