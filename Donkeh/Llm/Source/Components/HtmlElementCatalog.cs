using System.Reflection;
using System.Xml.Linq;
using Donkeh.UILab;

namespace Donkeh.Llm.Components;

/// <summary>Describes the built-in HTML element helpers as inspectable components.</summary>
public static class HtmlElementCatalog
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta",
        "param", "source", "track", "wbr"
    };

    /// <summary>Returns every HTML helper as a component-like descriptor.</summary>
    public static List<ComponentMetadata> All()
    {
        var propsDoc = ReadDocs(typeof(Props).Assembly);
        var props = PropsProperties(propsDoc);

        var result = new List<ComponentMetadata>();

        foreach (var method in typeof(HtmlHelper).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            var parameters = method.GetParameters();

            if (parameters.Length != 2
                || !typeof(Props).IsAssignableFrom(parameters[0].ParameterType)
                || parameters[1].ParameterType != typeof(Node[]))
            {
                continue;
            }

            var tag = method.Name.ToLowerInvariant();
            var propsType = parameters[0].ParameterType;
            var specific = propsType == typeof(Props)
                ? []
                : PropsProperties(propsDoc, propsType);
            var allProperties = specific.Count == 0
                ? props
                : props.Concat(specific.Where(property =>
                    props.All(existing => !existing.Name.Equals(property.Name, StringComparison.Ordinal)))).ToList();
            var allEvents = allProperties.Where(property => property.IsEvent)
                .Select(property => new ComponentEvent
                {
                    Name = property.Name,
                    Type = property.Type,
                    EventArgsType = EventArgumentType(property.Type),
                    Description = property.Description
                })
                .ToList();
            var description = $"HTML <{tag}> element.";

            result.Add(new ComponentMetadata
            {
                Name = method.Name,
                Namespace = "Donkeh.UILab",
                Package = "Donkeh.UILab",
                Source = null,
                Kind = "html-element",
                BaseType = "Props",
                Description = description,
                PropsType = propsType.Name,
                Properties = allProperties.Where(property => !property.IsEvent).ToList(),
                Events = allEvents,
                SupportsChildren = !VoidElements.Contains(tag),
                SupportsState = false,
                Lifecycle = [],
                Dependencies = ["Donkeh.UILab.HtmlHelper"],
                Examples =
                [
                    VoidElements.Contains(tag)
                        ? $"<{tag} className=\"example\" />"
                        : $"<{tag} className=\"example\">Content</{tag}>"
                ],
                Tags = Tags(tag, propsType.Name),
                IsAbstract = false
            });
        }

        return result.OrderBy(component => component.Name, StringComparer.Ordinal).ToList();
    }

    private static List<ComponentProperty> PropsProperties(
        Dictionary<string, string> docs,
        Type? type = null)
    {
        type ??= typeof(Props);
        var result = new List<ComponentProperty>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetMethod is null || !property.GetMethod.IsPublic)
            {
                continue;
            }

            var name = property.Name;
            var propertyType = FormatType(property.PropertyType);
            var isEvent = name.StartsWith("On", StringComparison.Ordinal)
                && (propertyType.Contains("Action", StringComparison.Ordinal)
                    || propertyType.Contains("Func", StringComparison.Ordinal)
                    || propertyType.Contains("Delegate", StringComparison.Ordinal));

            result.Add(new ComponentProperty
            {
                Name = name,
                Type = propertyType,
                Required = false,
                Default = null,
                Description = docs.TryGetValue($"P:{type.FullName}.{name}", out var description)
                    ? description
                    : null,
                IsEvent = isEvent
            });
        }

        return result.OrderBy(property => property.Name, StringComparer.Ordinal).ToList();
    }

    private static string FormatType(Type type)
    {
        if (type == typeof(void)) return "void";
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "long";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(object)) return "object";
        if (type == typeof(char)) return "char";
        if (type == typeof(byte)) return "byte";

        var nullable = Nullable.GetUnderlyingType(type);

        if (nullable is not null)
        {
            return FormatType(nullable) + "?";
        }

        if (type.IsArray)
        {
            return FormatType(type.GetElementType()!) + "[]";
        }

        if (type.IsGenericType)
        {
            var name = type.Name;
            var tick = name.IndexOf('`');
            name = tick > 0 ? name[..tick] : name;
            var arguments = string.Join(", ", type.GetGenericArguments().Select(FormatType));
            return $"{name}<{arguments}>";
        }

        return type.Name;
    }

    private static string? EventArgumentType(string type)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            type, @"(?:Action|Func)\s*<\s*(?<args>[^>]+)>");

        if (!match.Success)
        {
            return null;
        }

        var args = match.Groups["args"].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return args.Length == 0 ? null : args[^1];
    }

    private static readonly Dictionary<string, string[]> TagAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["input"] = ["field", "textbox", "form", "file", "selection", "search"],
        ["textarea"] = ["field", "textbox", "form", "editor"],
        ["select"] = ["dropdown", "picker", "selection", "option", "form"],
        ["option"] = ["choice", "selection", "dropdown"],
        ["dialog"] = ["modal", "popup", "overlay", "window"],
        ["button"] = ["action", "click", "submit", "command"],
        ["a"] = ["link", "navigation", "href"],
        ["nav"] = ["navigation", "menu", "sidebar"],
        ["table"] = ["grid", "rows", "columns", "data", "list"],
        ["ul"] = ["list", "items"],
        ["ol"] = ["list", "items"],
        ["li"] = ["list", "item"],
        ["img"] = ["image", "picture", "photo", "icon"],
        ["canvas"] = ["drawing", "graphics", "paint"],
        ["form"] = ["field", "validation", "submit", "input"],
        ["details"] = ["accordion", "collapse", "expand"],
        ["summary"] = ["accordion", "collapse", "expand"],
        ["progress"] = ["loading", "status", "bar"],
        ["label"] = ["form", "field", "caption"]
    };

    private static List<string> Tags(string tag, string propsType)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "html", "element", tag };

        foreach (var word in SourceScanner.Words(propsType.Replace("Props", "", StringComparison.Ordinal)))
        {
            tags.Add(word);
        }

        if (TagAliases.TryGetValue(tag, out var aliases))
        {
            foreach (var alias in aliases)
            {
                tags.Add(alias);
            }
        }

        return tags.ToList();
    }

    private static Dictionary<string, string> ReadDocs(Assembly assembly)
    {
        var docs = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");

            if (!File.Exists(xmlPath))
            {
                return docs;
            }

            var document = XDocument.Load(xmlPath);

            foreach (var member in document.Descendants("member"))
            {
                var name = member.Attribute("name")?.Value;
                var summary = member.Element("summary");

                if (!string.IsNullOrWhiteSpace(name) && summary is not null)
                {
                    docs[name] = SourceScanner.CleanDoc(summary.Value) ?? "";
                }
            }
        }
        catch (Exception)
        {
            // Descriptions are optional.
        }

        return docs;
    }
}
