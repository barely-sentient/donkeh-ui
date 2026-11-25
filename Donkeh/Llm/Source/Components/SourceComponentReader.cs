using System.Text.RegularExpressions;

namespace Donkeh.Llm.Components;

/// <summary>Discovers components declared in a project's C# and CSX sources.</summary>
public static class SourceComponentReader
{
    private static readonly Regex ClassPattern = new(
        @"\bclass\s+(?<name>[A-Za-z_]\w*)(?<generic><[^>{]*>)?\s*(?::\s*(?<bases>[^{]+))?\{",
        RegexOptions.Compiled);

    private static readonly Regex PropertyPattern = new(
        @"\bpublic\s+(?<mods>(?:(?:static|required|new|override|virtual|readonly|sealed)\s+)*)(?<type>[A-Za-z_][\w.<>,?\[\]\s:]*?)\s+(?<name>[A-Za-z_]\w*)\s*(?:\{\s*(?<accessors>[^}]*)\}\s*(?:=\s*(?<defaultAfter>[^;]+);)?|=\s*(?<default>[^;]+);)",
        RegexOptions.Compiled);

    private static readonly Regex StylesheetPattern = new(
        @"\[assembly:\s*Donkeh\.UILab\.Stylesheet\(\s*""(?<path>[^""]+)""\s*\)\]",
        RegexOptions.Compiled);

    /// <summary>Reads every component declared under a project directory.</summary>
    public static List<ComponentMetadata> ReadProject(string projectDirectory)
    {
        var files = SourceFiles(projectDirectory);
        var parsed = new List<SourceType>();
        var stylesheets = new List<string>();

        foreach (var file in files)
        {
            string source;

            try
            {
                source = File.ReadAllText(file);
            }
            catch (Exception)
            {
                continue;
            }

            var masked = SourceScanner.Mask(source);
            var usings = SourceScanner.Usings(masked);

            foreach (Match match in StylesheetPattern.Matches(source))
            {
                var path = match.Groups["path"].Value;

                if (!stylesheets.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    stylesheets.Add(path);
                }
            }

            foreach (Match match in ClassPattern.Matches(masked))
            {
                var name = match.Groups["name"].Value;
                var open = masked.IndexOf('{', match.Index + match.Length - 1);
                var close = SourceScanner.FindBlockEnd(masked, open);
                var bases = match.Groups["bases"].Value.Trim();
                var namespaceName = SourceScanner.NamespaceAt(masked, match.Index);

                parsed.Add(new SourceType
                {
                    Name = name,
                    Namespace = namespaceName,
                    Bases = bases,
                    BaseType = bases.Split(',', 2)[0].Trim(),
                    File = file,
                    Source = source,
                    Masked = masked,
                    Usings = usings,
                    BodyStart = open,
                    BodyEnd = close,
                    Offset = match.Index,
                    IsAbstract = Regex.IsMatch(
                        masked[Math.Max(0, match.Index - 80)..match.Index],
                        @"\babstract\s*$")
                });
            }
        }

        var propsTypes = parsed
            .Where(type => IsPropsType(type))
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var components = new List<ComponentMetadata>();

        foreach (var type in parsed)
        {
            if (!IsComponentType(type))
            {
                continue;
            }

            var propsName = PropsTypeName(type.BaseType);
            propsTypes.TryGetValue(propsName ?? "", out var props);
            var properties = props is null ? [] : ReadProperties(props);
            var events = properties.Where(property => property.IsEvent).Select(property => new ComponentEvent
            {
                Name = property.Name,
                Type = property.Type,
                EventArgsType = EventArgumentType(property.Type),
                Description = property.Description
            }).ToList();
            var lifecycle = Lifecycle(type);
            var dependencies = Dependencies(type);
            var isCsx = type.File.EndsWith(".cx", StringComparison.OrdinalIgnoreCase);
            var examples = Examples(type, isCsx, propsName);
            var description = SourceScanner.Summary(type.Source, type.Offset)
                ?? $"{(isCsx ? "CSX" : "C#")} component {type.Name}.";

            components.Add(new ComponentMetadata
            {
                Name = type.Name,
                Namespace = type.Namespace,
                Package = Path.GetFileNameWithoutExtension(type.File),
                Source = type.File,
                Kind = isCsx ? "csx" : "class",
                BaseType = type.BaseType,
                Description = description,
                PropsType = propsName,
                Properties = properties.Where(property => !property.IsEvent).ToList(),
                Events = events,
                SupportsChildren = SupportsChildren(type, props),
                SupportsState = Regex.IsMatch(
                    type.Masked[type.BodyStart..type.BodyEnd],
                    @"\bUse(State|Reducer|Context|Ref|Effect|LayoutEffect|Memo|Callback)\b"),
                Lifecycle = lifecycle,
                Dependencies = dependencies,
                Examples = examples,
                Stylesheets = stylesheets
                    .Where(path => Path.GetFileNameWithoutExtension(path)
                        .Equals(type.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
                Tags = Tags(type.Name, type.Namespace, description, propsName),
                IsAbstract = type.IsAbstract,
                Line = LineOf(type.Source, type.Offset)
            });
        }

        return components;
    }

    /// <summary>Reads a single source file, used by component diagnostics.</summary>
    public static List<ComponentMetadata> ReadFile(string file)
    {
        var directory = Path.GetDirectoryName(file) ?? Environment.CurrentDirectory;
        return ReadProject(directory).Where(component =>
            string.Equals(component.Source, Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static List<string> SourceFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(directory, "*.cx", SearchOption.AllDirectories))
                .Where(file => !IsIgnored(file))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static bool IsIgnored(string file)
    {
        var parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return parts.Any(part =>
            part.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || part.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
            || part.Equals("builds", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsComponentType(SourceType type)
    {
        var baseType = StripGeneric(type.BaseType);

        return baseType is "Component" or "Donkeh.UILab.Component"
            || type.BaseType.StartsWith("Component<", StringComparison.Ordinal);
    }

    private static bool IsPropsType(SourceType type)
    {
        var baseType = StripGeneric(type.BaseType);

        return baseType is "Props" or "PropsWithChildren"
            || type.BaseType.StartsWith("Props", StringComparison.Ordinal)
            || type.BaseType.Contains("PropsWithChildren", StringComparison.Ordinal)
            || type.Name.EndsWith("Props", StringComparison.Ordinal);
    }

    private static string? PropsTypeName(string baseType)
    {
        var match = Regex.Match(baseType, @"Component\s*<\s*(?<props>[A-Za-z_][\w.]*)");
        return match.Success ? match.Groups["props"].Value.Split('.').Last() : null;
    }

    private static string StripGeneric(string type)
    {
        var index = type.IndexOf('<');
        return (index > 0 ? type[..index] : type).Trim();
    }

    private static List<ComponentProperty> ReadProperties(SourceType props)
    {
        var body = props.Masked[props.BodyStart..Math.Min(props.BodyEnd + 1, props.Masked.Length)];
        var properties = new List<ComponentProperty>();

        foreach (Match match in PropertyPattern.Matches(body))
        {
            var modifiers = match.Groups["mods"].Value;

            if (modifiers.Contains("static", StringComparison.Ordinal))
            {
                continue;
            }

            var accessors = match.Groups["accessors"].Value;

            if (!string.IsNullOrWhiteSpace(accessors)
                && !Regex.IsMatch(accessors, @"\b(get|set|init)\b"))
            {
                continue;
            }

            var name = match.Groups["name"].Value;
            var type = Regex.Replace(match.Groups["type"].Value, @"\s+", " ").Trim();
            var declarationStart = props.BodyStart + match.Index;
            var declaration = props.Source.Substring(
                declarationStart,
                Math.Min(match.Length, props.Source.Length - declarationStart));
            var defaultMatch = Regex.Match(declaration, @"=\s*(?<value>[^;]+);");
            var defaultValue = defaultMatch.Success
                ? defaultMatch.Groups["value"].Value.Trim()
                : null;

            if (defaultValue is not null && defaultValue.StartsWith('>'))
            {
                defaultValue = null;
            }

            var declarationOffset = declarationStart;
            var isEvent = name.StartsWith("On", StringComparison.Ordinal)
                && (type.Contains("Action", StringComparison.Ordinal)
                    || type.Contains("Func", StringComparison.Ordinal)
                    || type.Contains("Delegate", StringComparison.Ordinal));

            properties.Add(new ComponentProperty
            {
                Name = name,
                Type = type,
                Default = defaultValue,
                Required = modifiers.Contains("required", StringComparison.Ordinal),
                Description = SourceScanner.Summary(props.Source, declarationOffset),
                IsEvent = isEvent
            });
        }

        return properties;
    }

    private static string? EventArgumentType(string type)
    {
        var match = Regex.Match(type, @"(?:Action|Func)\s*<\s*(?<args>[^>]+)>");

        if (!match.Success)
        {
            return null;
        }

        var args = match.Groups["args"].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return args.Length == 0 ? null : args[^1];
    }

    private static List<string> Lifecycle(SourceType type)
    {
        var body = type.Masked[type.BodyStart..type.BodyEnd];
        return Regex.Matches(
                body,
                @"\boverride\s+(?:protected\s+)?(?:void|bool)\s+(?<name>OnMount|OnUpdate|OnUnmount|ShouldRender)\b")
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> Dependencies(SourceType type)
    {
        var values = new List<string>(type.Usings);

        foreach (Match match in Regex.Matches(type.Masked[type.BodyStart..type.BodyEnd], @"<\s*(?<name>[A-Z]\w*)"))
        {
            var name = match.Groups["name"].Value;

            if (!values.Contains(name, StringComparer.Ordinal))
            {
                values.Add(name);
            }
        }

        foreach (Match match in Regex.Matches(type.Masked[type.BodyStart..type.BodyEnd], @"\bC\s*<\s*(?<name>[A-Z]\w*)"))
        {
            var name = match.Groups["name"].Value;

            if (!values.Contains(name, StringComparer.Ordinal))
            {
                values.Add(name);
            }
        }

        return values;
    }

    private static bool SupportsChildren(SourceType type, SourceType? props)
    {
        if (props is not null)
        {
            if (props.BaseType.Contains("PropsWithChildren", StringComparison.Ordinal))
            {
                return true;
            }

            if (ReadProperties(props).Any(property => property.Name.Equals("Children", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return type.Masked[type.BodyStart..type.BodyEnd].Contains("Props.Children", StringComparison.Ordinal);
    }

    private static List<string> Examples(SourceType type, bool isCsx, string? propsType)
    {
        var example = SourceScanner.DocElement(type.Source, type.Offset, "example");

        if (!string.IsNullOrWhiteSpace(example))
        {
            return [example];
        }

        if (isCsx)
        {
            return [$"<{type.Name} />"];
        }

        return propsType is null
            ? [$"C<{type.Name}>(new())"]
            : [$"C<{type.Name}, {propsType}>(new {propsType}())"];
    }

    private static List<string> Tags(
        string name,
        string? namespaceName,
        string? description,
        string? propsType)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in SourceScanner.Words(name))
        {
            tags.Add(word);
        }

        if (namespaceName is not null)
        {
            foreach (var part in namespaceName.Split('.'))
            {
                foreach (var word in SourceScanner.Words(part))
                {
                    tags.Add(word);
                }
            }
        }

        if (propsType is not null)
        {
            foreach (var word in SourceScanner.Words(propsType.Replace("Props", "", StringComparison.Ordinal)))
            {
                tags.Add(word);
            }
        }

        if (description is not null)
        {
            foreach (var word in description.Split(
                [' ', ',', '.', ';', ':', '(', ')', '[', ']', '/', '\\', '-', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (word.Length > 2)
                {
                    tags.Add(word.ToLowerInvariant());
                }
            }
        }

        return tags.ToList();
    }

    private static int LineOf(string source, int offset) =>
        source.Take(Math.Clamp(offset, 0, source.Length)).Count(character => character == '\n') + 1;

    private sealed class SourceType
    {
        public required string Name { get; init; }
        public string? Namespace { get; init; }
        public required string Bases { get; init; }
        public required string BaseType { get; init; }
        public required string File { get; init; }
        public required string Source { get; init; }
        public required string Masked { get; init; }
        public required List<string> Usings { get; init; }
        public required int BodyStart { get; init; }
        public required int BodyEnd { get; init; }
        public required int Offset { get; init; }
        public required bool IsAbstract { get; init; }
    }
}
