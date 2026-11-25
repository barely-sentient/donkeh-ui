using System.Text.RegularExpressions;

namespace Donkeh.Cli;

/// <summary>Minimal, formatting-preserving edits to SDK-style project files.</summary>
internal static class CsprojEditor
{
    /// <summary>
    /// Reports whether a project actively imports a local framework checkout.
    /// </summary>
    /// <param name="projectText">Project file text.</param>
    /// <returns>
    /// <see langword="true"/> when an unconditional <c>Donkeh.props</c> import is
    /// present. Conditional imports used by dual-mode projects are not treated as
    /// local because they can be switched off.
    /// </returns>
    public static bool UsesLocalCheckout(string projectText)
    {
        var imports = Regex.Matches(
            projectText,
            @"<Import\b[^>]*>",
            RegexOptions.IgnoreCase);

        foreach (Match import in imports)
        {
            if (!import.Value.Contains("Donkeh.props", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!import.Value.Contains("Condition=", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reports whether a project defines a property element.</summary>
    /// <param name="projectText">Project file text.</param>
    /// <param name="name">Property name, without angle brackets.</param>
    /// <returns><see langword="true"/> when the element is present.</returns>
    public static bool HasProperty(string projectText, string name)
    {
        var pattern = $@"<{Regex.Escape(name)}(?![A-Za-z])";
        return Regex.IsMatch(projectText, pattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Sets a property value, preserving any attributes on the existing element,
    /// or adds the element to the first property group when absent.
    /// </summary>
    /// <param name="projectText">Current project file text.</param>
    /// <param name="newLine">Line ending used by the project file.</param>
    /// <param name="name">Property name, without angle brackets.</param>
    /// <param name="value">Value to write.</param>
    /// <returns>The updated project text.</returns>
    public static string SetProperty(
        string projectText,
        string newLine,
        string name,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var escaped = Regex.Escape(name);
        var property = new Regex(
            $@"<{escaped}(?<attributes>[^>]*)>.*?</{escaped}>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (property.IsMatch(projectText))
        {
            return property.Replace(
                projectText,
                match => $"<{name}{match.Groups["attributes"].Value}>{value}</{name}>");
        }

        var group = new Regex(
            @"<PropertyGroup(?<attributes>[^>]*)>",
            RegexOptions.IgnoreCase);

        var matchGroup = group.Match(projectText);

        if (!matchGroup.Success)
        {
            throw new CliException("The project file does not contain a PropertyGroup.");
        }

        var insertion = $"{newLine}    <{name}>{value}</{name}>";
        return projectText.Insert(matchGroup.Index + matchGroup.Length, insertion);
    }

    /// <summary>Removes a property element when present.</summary>
    /// <param name="projectText">Current project file text.</param>
    /// <param name="name">Property name, without angle brackets.</param>
    /// <returns>The updated project text.</returns>
    public static string RemoveProperty(string projectText, string name)
    {
        var escaped = Regex.Escape(name);
        var property = new Regex(
            $@"[ \t]*<{escaped}(?![A-Za-z])[^>]*>.*?</{escaped}>[ \t]*\r?\n?",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return property.Replace(projectText, string.Empty);
    }

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
        string version) =>
        SetProperty(projectText, newLine, "DonkehVersion", version);

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

    /// <summary>Adds a NuGet package reference when the project does not have it.</summary>
    /// <param name="projectText">Current project file text.</param>
    /// <param name="newLine">Line ending used by the project file.</param>
    /// <param name="packageId">Package id, such as <c>Donkeh.Windowing</c>.</param>
    /// <param name="version">Version or MSBuild version expression.</param>
    /// <returns>The updated project text.</returns>
    public static string EnsurePackageReference(
        string projectText,
        string newLine,
        string packageId,
        string version,
        string? condition = null)
    {
        var existing = new Regex(
            $@"<PackageReference\b[^>]*Include\s*=\s*""{Regex.Escape(packageId)}""",
            RegexOptions.IgnoreCase);

        if (existing.IsMatch(projectText))
        {
            return projectText;
        }

        var conditionAttribute = string.IsNullOrWhiteSpace(condition)
            ? string.Empty
            : $" Condition=\"{condition}\"";
        var block =
            $"  <ItemGroup>{newLine}" +
            $"    <PackageReference Include=\"{packageId}\" Version=\"{version}\"{conditionAttribute} />{newLine}" +
            $"  </ItemGroup>{newLine}";

        return AddItemGroup(projectText, newLine, block);
    }

    /// <summary>Adds a project reference when the project does not have it.</summary>
    /// <param name="projectText">Current project file text.</param>
    /// <param name="newLine">Line ending used by the project file.</param>
    /// <param name="relativePath">Project-relative path to the referenced project.</param>
    /// <returns>The updated project text.</returns>
    public static string EnsureProjectReference(
        string projectText,
        string newLine,
        string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        var existing = new Regex(
            $@"<ProjectReference\b[^>]*Include\s*=\s*""[^""]*{Regex.Escape(fileName)}""",
            RegexOptions.IgnoreCase);

        if (existing.IsMatch(projectText))
        {
            return projectText;
        }

        var block =
            $"  <ItemGroup>{newLine}" +
            $"    <ProjectReference Include=\"{relativePath}\" />{newLine}" +
            $"  </ItemGroup>{newLine}";

        return AddItemGroup(projectText, newLine, block);
    }

    /// <summary>Removes project references that point at a file name.</summary>
    /// <param name="projectText">Current project file text.</param>
    /// <param name="fileName">Referenced project file name, such as <c>Donkeh.UILab.csproj</c>.</param>
    /// <returns>The updated project text.</returns>
    public static string RemoveProjectReferences(string projectText, string fileName)
    {
        var references = new Regex(
            $@"[ \t]*<ProjectReference\b(?=[^>]*Include\s*=\s*""[^""]*{Regex.Escape(fileName)}"")[^>]*(?:/>|>.*?</ProjectReference>)[ \t]*\r?\n?",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return references.Replace(projectText, string.Empty);
    }

    /// <summary>Removes every <c>Donkeh.*</c> package reference.</summary>
    /// <param name="projectText">Current project file text.</param>
    /// <returns>The updated project text.</returns>
    public static string RemoveDonkehPackageReferences(string projectText)
    {
        var references = new Regex(
            @"[ \t]*<PackageReference\b(?=[^>]*Include\s*=\s*""Donkeh\.[^""]*"")[^>]*(?:/>|>.*?</PackageReference>)[ \t]*\r?\n?",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return references.Replace(projectText, string.Empty);
    }

    /// <summary>Removes one package reference, if present.</summary>
    /// <param name="projectText">Current project file text.</param>
    /// <param name="packageId">Package id to remove.</param>
    /// <returns>The updated project text.</returns>
    public static string RemovePackageReference(string projectText, string packageId)
    {
        var references = new Regex(
            $@"[ \t]*<PackageReference\b(?=[^>]*Include\s*=\s*""{Regex.Escape(packageId)}"")[^>]*(?:/>|>.*?</PackageReference>)[ \t]*\r?\n?",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return references.Replace(projectText, string.Empty);
    }

    /// <summary>Removes every import of a file name, such as <c>Donkeh.props</c>.</summary>
    /// <param name="projectText">Current project file text.</param>
    /// <param name="fileName">Import file name to remove.</param>
    /// <returns>The updated project text.</returns>
    public static string RemoveImports(string projectText, string fileName)
    {
        var imports = new Regex(
            $@"[ \t]*<Import\b[^>]*{Regex.Escape(fileName)}[^>]*>[ \t]*\r?\n?",
            RegexOptions.IgnoreCase);

        return imports.Replace(projectText, string.Empty);
    }

    /// <summary>Inserts an import immediately after the opening project element.</summary>
    /// <param name="projectText">Current project file text.</param>
    /// <param name="newLine">Line ending used by the project file.</param>
    /// <param name="importPath">Import path, escaped for XML when written.</param>
    /// <returns>The updated project text.</returns>
    public static string AddImportAfterProject(
        string projectText,
        string newLine,
        string importPath)
    {
        var opening = Regex.Match(
            projectText,
            @"<Project\b[^>]*>",
            RegexOptions.IgnoreCase);

        if (!opening.Success)
        {
            throw new CliException("The project file does not contain an opening <Project> element.");
        }

        var escaped = System.Security.SecurityElement.Escape(importPath);
        var insertion = $"{newLine}  <Import Project=\"{escaped}\" />";
        return projectText.Insert(opening.Index + opening.Length, insertion);
    }

    /// <summary>
    /// Inserts a preformatted item group immediately before the closing
    /// <c>&lt;/Project&gt;</c> element.
    /// </summary>
    /// <param name="projectText">Current project file text.</param>
    /// <param name="newLine">Line ending used by the project file.</param>
    /// <param name="block">The complete item group, including its line ending.</param>
    /// <returns>The updated project text.</returns>
    /// <exception cref="CliException">
    /// The project file does not contain a closing <c>&lt;/Project&gt;</c> element.
    /// </exception>
    public static string AddItemGroup(string projectText, string newLine, string block)
    {
        const string closingTag = "</Project>";
        var index = projectText.LastIndexOf(closingTag, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            throw new CliException("The project file does not contain a closing </Project> element.");
        }

        var prefix = projectText[..index];

        if (!prefix.EndsWith(newLine, StringComparison.Ordinal))
        {
            prefix += newLine;
        }

        return prefix + block + projectText[index..];
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
