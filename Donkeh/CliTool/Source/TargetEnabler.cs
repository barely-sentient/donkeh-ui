using System.Text.RegularExpressions;

namespace Donkeh.Cli;

/// <summary>Adds platform targets to a generated project.</summary>
internal static class TargetEnabler
{
    private const string AndroidTargetFramework = "net10.0-android";
    private const string BrowserTargetFramework = "net10.0-browser";

    /// <summary>Enables a target in a project file.</summary>
    /// <param name="target">Target name: desktop, android or web.</param>
    /// <param name="projectPath">Optional project file or project directory.</param>
    /// <returns>The updated project file.</returns>
    public static string Enable(string target, string? projectPath)
    {
        var normalized = NormalizeTarget(target);
        var projectFile = ComponentGenerator.FindProject(projectPath);
        var (projectText, newLine) = TextFiles.Read(projectFile);
        var checkout = CsprojEditor.UsesLocalCheckout(projectText);
        var updated = normalized switch
        {
            "desktop" => EnableDesktop(projectText, newLine, checkout),
            "android" => EnableAndroid(projectText, newLine, projectFile, checkout),
            "web" => EnableWeb(projectText, newLine, checkout),
            _ => throw new CliException($"Unknown target '{target}'.")
        };

        if (!string.Equals(projectText, updated, StringComparison.Ordinal))
        {
            TextFiles.Write(projectFile, updated);
        }

        return projectFile;
    }

    internal static string NormalizeTarget(string target) =>
        target.Trim().ToLowerInvariant() switch
        {
            "desktop" or "windows" or "linux" or "macos" or "mac" => "desktop",
            "android" or "mobile" => "android",
            "web" or "browser" or "wasm" => "web",
            _ => target
        };

    private static string EnableDesktop(string projectText, string newLine, bool checkout) =>
        checkout
            ? SetCheckoutFlag(projectText, newLine, "DonkehDesktop")
            : projectText;

    private static string EnableAndroid(
        string projectText,
        string newLine,
        string projectFile,
        bool checkout)
    {
        var updated = checkout
            ? SetCheckoutFlag(projectText, newLine, "DonkehAndroid")
            : projectText;

        if (!checkout)
        {
            if (CsprojEditor.HasProperty(updated, "DonkehAndroid"))
            {
                // A dual-mode project already has the platform switch. Turn it on
                // so a plain restore includes the Android TFM.
                updated = CsprojEditor.SetProperty(updated, newLine, "DonkehAndroid", "true");
            }
            else
            {
                updated = EnsureTargetFramework(updated, newLine, AndroidTargetFramework);
            }
        }

        updated = EnsureProperty(
            updated,
            newLine,
            "SupportedOSPlatformVersion",
            "24",
            "'$(TargetFramework)' == '" + AndroidTargetFramework + "'");

        var projectName = Path.GetFileNameWithoutExtension(projectFile);
        var title = System.Security.SecurityElement.Escape(projectName);
        var applicationId = DefaultApplicationId(projectName);

        updated = EnsureProperty(
            updated,
            newLine,
            "ApplicationId",
            applicationId,
            "'$(TargetFramework)' == '" + AndroidTargetFramework + "'");

        updated = EnsureProperty(
            updated,
            newLine,
            "ApplicationTitle",
            title,
            "'$(TargetFramework)' == '" + AndroidTargetFramework + "'");

        if (checkout)
        {
            return updated;
        }

        // Dual-mode projects keep the NuGet host package inactive in local mode.
        var condition = "'$(TargetFramework)' == '" + AndroidTargetFramework + "'";

        if (CsprojEditor.HasProperty(updated, "UseLocalDonkeh"))
        {
            condition = "'$(UseLocalDonkeh)' != 'true' and " + condition;
        }

        updated = CsprojEditor.RemovePackageReference(updated, "Donkeh.Windowing.Android");

        return AddPackageReference(
            updated,
            newLine,
            "Donkeh.Windowing.Android",
            condition);
    }

    private static string DefaultApplicationId(string projectName)
    {
        var slug = new string(projectName
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

        return "com.donkeh." + (slug.Length == 0 ? "app" : slug);
    }

    private static string EnableWeb(string projectText, string newLine, bool checkout)
    {
        if (checkout)
        {
            return SetCheckoutFlag(projectText, newLine, "DonkehWeb");
        }

        var updated = EnsureTargetFramework(projectText, newLine, BrowserTargetFramework);
        updated = EnsureProperty(
            updated,
            newLine,
            "RuntimeIdentifiers",
            "browser-wasm",
            "'$(TargetFramework)' == ''");
        updated = AddPackageReference(
            updated,
            newLine,
            "Donkeh.Windowing.Browser",
            "'$(TargetFramework)' == '" + BrowserTargetFramework + "'");

        updated = AddPackageReference(
            updated,
            newLine,
            "Microsoft.AspNetCore.Components.WebAssembly",
            "'$(TargetFramework)' == '" + BrowserTargetFramework + "'",
            "10.0.10");

        return AddPackageReference(
            updated,
            newLine,
            "Microsoft.NET.Sdk.WebAssembly.Pack",
            "'$(TargetFramework)' == '" + BrowserTargetFramework + "'",
            "10.0.11",
            " PrivateAssets=\"all\" GeneratePathProperty=\"true\"");
    }

    private static string SetCheckoutFlag(string projectText, string newLine, string property)
    {
        var enabled = $"""<{property}>true</{property}>""";

        foreach (Match match in Regex.Matches(
            projectText,
            $@"<{Regex.Escape(property)}>\s*true\s*</{Regex.Escape(property)}>",
            RegexOptions.IgnoreCase))
        {
            var before = projectText[..match.Index].TrimEnd();

            if (!before.EndsWith("<!--", StringComparison.Ordinal))
            {
                return projectText;
            }
        }

        var commented = new Regex(
            $@"<!--\s*<{Regex.Escape(property)}>\s*true\s*</{Regex.Escape(property)}>\s*-->",
            RegexOptions.IgnoreCase);

        if (commented.IsMatch(projectText))
        {
            return commented.Replace(projectText, enabled);
        }

        return InsertIntoFirstPropertyGroup(projectText, newLine, enabled);
    }

    private static string EnsureTargetFramework(
        string projectText,
        string newLine,
        string targetFramework)
    {
        var plural = new Regex(
            @"<TargetFrameworks>(?<value>[^<]*)</TargetFrameworks>",
            RegexOptions.IgnoreCase);

        if (plural.Match(projectText) is { Success: true } pluralMatch)
        {
            var values = pluralMatch.Groups["value"].Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (values.Contains(targetFramework, StringComparer.OrdinalIgnoreCase))
            {
                return projectText;
            }

            values.Add(targetFramework);
            return plural.Replace(
                projectText,
                $"<TargetFrameworks>{string.Join(';', values)}</TargetFrameworks>",
                1);
        }

        var single = new Regex(
            @"<TargetFramework>(?<value>[^<]+)</TargetFramework>",
            RegexOptions.IgnoreCase);

        if (single.Match(projectText) is { Success: true } singleMatch)
        {
            var current = singleMatch.Groups["value"].Value.Trim();

            if (string.Equals(current, targetFramework, StringComparison.OrdinalIgnoreCase))
            {
                return projectText;
            }

            return single.Replace(
                projectText,
                $"<TargetFrameworks>{current};{targetFramework}</TargetFrameworks>",
                1);
        }

        return InsertIntoFirstPropertyGroup(
            projectText,
            newLine,
            $"<TargetFrameworks>net10.0;{targetFramework}</TargetFrameworks>");
    }

    private static string EnsureProperty(
        string projectText,
        string newLine,
        string property,
        string value,
        string? condition = null)
    {
        if (Regex.IsMatch(
            projectText,
            $@"<{Regex.Escape(property)}(\s|>)",
            RegexOptions.IgnoreCase))
        {
            return projectText;
        }

        var conditionAttribute = condition is null ? string.Empty : $" Condition=\"{condition}\"";

        return InsertIntoFirstPropertyGroup(
            projectText,
            newLine,
            $"<{property}{conditionAttribute}>{value}</{property}>");
    }

    private static string AddPackageReference(
        string projectText,
        string newLine,
        string packageId,
        string condition,
        string version = "$(DonkehVersion)",
        string attributes = "")
    {
        if (projectText.Contains(
            $"Include=\"{packageId}\"",
            StringComparison.OrdinalIgnoreCase))
        {
            return projectText;
        }

        var block =
            $"  <ItemGroup>{newLine}" +
            $"    <PackageReference Include=\"{packageId}\" Version=\"{version}\" " +
            $"Condition=\"{condition}\"{attributes} />{newLine}" +
            $"  </ItemGroup>{newLine}";

        return InsertBeforeClosingProject(projectText, newLine, block);
    }

    private static string InsertIntoFirstPropertyGroup(
        string projectText,
        string newLine,
        string element)
    {
        var match = Regex.Match(
            projectText,
            @"<PropertyGroup(?<attributes>[^>]*)>",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            throw new CliException("The project file does not contain a PropertyGroup.");
        }

        var indent = "    ";
        var insertion = $"{newLine}{indent}{element}";

        return projectText.Insert(match.Index + match.Length, insertion);
    }

    private static string InsertBeforeClosingProject(
        string projectText,
        string newLine,
        string block)
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
}
