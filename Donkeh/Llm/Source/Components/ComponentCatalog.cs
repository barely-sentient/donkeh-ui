using Donkeh.Llm.Project;

namespace Donkeh.Llm.Components;

/// <summary>Loads and searches every component available to a project.</summary>
public static class ComponentCatalog
{
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["file"] = ["files", "folder", "directory", "explorer", "browser", "picker", "document"],
        ["selection"] = ["select", "selected", "choose", "picker", "pick", "multiple", "checkbox"],
        ["button"] = ["action", "click", "submit", "command"],
        ["input"] = ["textbox", "field", "edit", "editor", "entry", "search"],
        ["list"] = ["items", "grid", "table", "collection", "rows"],
        ["dialog"] = ["modal", "popup", "window", "overlay", "prompt"],
        ["navigation"] = ["nav", "menu", "tabs", "sidebar", "breadcrumb", "router"],
        ["image"] = ["picture", "img", "photo", "icon", "avatar"],
        ["tree"] = ["hierarchy", "nodes", "expand", "collapse", "explorer"],
        ["state"] = ["hook", "usestate", "reducer", "context", "observable"],
        ["chart"] = ["graph", "plot", "visualization", "data"],
        ["form"] = ["field", "validation", "submit", "input", "label"],
        ["table"] = ["grid", "rows", "columns", "data", "sort", "filter"],
        ["file selection"] = ["file", "picker", "explorer", "dialog", "browse", "choose"]
    };

    /// <summary>Loads all components available to a project.</summary>
    public static List<ComponentMetadata> Load(DonkehProject project)
    {
        var components = new List<ComponentMetadata>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new List<(string Directory, string Package)>
        {
            (project.ProjectDirectory, project.Name)
        };

        foreach (var reference in project.ProjectReferences)
        {
            if (reference.FullPath is not null)
            {
                var directory = ProjectLocator.DirectoryOf(reference.FullPath);
                directories.Add((directory, reference.Name ?? Path.GetFileNameWithoutExtension(reference.FullPath)));
            }
        }

        foreach (var (directory, package) in directories.DistinctBy(item => item.Directory, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var sourceComponent in SourceComponentReader.ReadProject(directory))
            {
                var key = $"source:{sourceComponent.Namespace}.{sourceComponent.Name}";
                var component = WithPackage(sourceComponent, package);
                component = WithDisplayName(component, project.ProjectDirectory);

                if (seen.Add(key))
                {
                    components.Add(component);
                }
            }
        }

        foreach (var component in MetadataComponentReader.ReadProject(project))
        {
            if (seen.Add($"assembly:{component.Namespace}.{component.Name}"))
            {
                components.Add(component);
            }
        }

        foreach (var component in HtmlElementCatalog.All())
        {
            if (components.Any(existing =>
                    existing.Name.Equals(component.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (seen.Add($"html:{component.Name}"))
            {
                components.Add(component);
            }
        }

        // Source components win over packaged duplicates; the first insertion
        // already guaranteed that order.
        return components
            .OrderBy(component => component.Kind == "html-element" ? 2 : 1)
            .ThenBy(component => component.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Searches a component list with a conceptual query.</summary>
    public static List<ComponentMetadata> Search(
        IReadOnlyList<ComponentMetadata> components,
        ComponentQuery query)
    {
        var text = query.Text?.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return Filter(components, query).Take(Math.Clamp(query.Limit, 1, 100)).ToList();
        }

        var tokens = Tokenize(text);
        var expanded = Expand(tokens);
        var scored = new List<(ComponentMetadata Component, int Score)>();

        foreach (var component in Filter(components, query))
        {
            var score = Score(component, text, tokens, expanded);

            if (score > 0)
            {
                scored.Add((component, score));
            }
        }

        return scored
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Component.Name, StringComparer.Ordinal)
            .Take(Math.Clamp(query.Limit, 1, 100))
            .Select(item => item.Component)
            .ToList();
    }

    private static IEnumerable<ComponentMetadata> Filter(
        IReadOnlyList<ComponentMetadata> components,
        ComponentQuery query)
    {
        return components.Where(component =>
            (query.Package is null
                || string.Equals(component.Package, query.Package, StringComparison.OrdinalIgnoreCase))
            && (query.Kind is null
                || string.Equals(component.Kind, query.Kind, StringComparison.OrdinalIgnoreCase))
            && !component.IsAbstract);
    }

    private static int Score(
        ComponentMetadata component,
        string text,
        List<string> tokens,
        List<string> expanded)
    {
        var name = component.Name;
        var score = 0;

        if (name.Equals(text, StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }
        else if (name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }
        else if (name.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        var haystack = string.Join(' ', new[]
        {
            component.Name,
            component.Description ?? "",
            component.Namespace ?? "",
            component.Package ?? "",
            string.Join(' ', component.Tags)
        });

        foreach (var token in tokens)
        {
            if (name.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 40;
            }
            else if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 24;
            }

            if (haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }
        }

        foreach (var token in expanded.Except(tokens, StringComparer.OrdinalIgnoreCase))
        {
            if (name.Contains(token, StringComparison.OrdinalIgnoreCase)
                || haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 6;
            }
        }

        return score;
    }

    private static List<string> Tokenize(string text) =>
        text.Split(
                [' ', ',', '.', ';', ':', '(', ')', '[', ']', '/', '\\', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1 && token is not "the" and not "a" and not "an" and not "for" and not "with")
            .Select(token => token.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> Expand(List<string> tokens)
    {
        var values = new List<string>(tokens);
        var joined = string.Join(' ', tokens);

        if (Synonyms.TryGetValue(joined, out var phrase))
        {
            values.AddRange(phrase);
        }

        foreach (var token in tokens)
        {
            if (Synonyms.TryGetValue(token, out var words))
            {
                values.AddRange(words);
            }
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ComponentMetadata WithPackage(ComponentMetadata component, string package) =>
        new()
        {
            Name = component.Name,
            Namespace = component.Namespace,
            Package = package,
            Source = component.Source,
            Kind = component.Kind,
            BaseType = component.BaseType,
            Description = component.Description,
            PropsType = component.PropsType,
            Properties = component.Properties,
            Events = component.Events,
            SupportsChildren = component.SupportsChildren,
            SupportsState = component.SupportsState,
            Lifecycle = component.Lifecycle,
            Dependencies = component.Dependencies,
            Examples = component.Examples,
            Stylesheets = component.Stylesheets,
            Tags = component.Tags,
            IsAbstract = component.IsAbstract,
            Line = component.Line
        };

    private static ComponentMetadata WithDisplayName(
        ComponentMetadata component,
        string projectDirectory)
    {
        if (component.Source is null)
        {
            return component;
        }

        var relative = Path.GetRelativePath(projectDirectory, component.Source);

        return new ComponentMetadata
        {
            Name = component.Name,
            Namespace = component.Namespace,
            Package = component.Package,
            Source = relative.Replace('\\', '/'),
            Kind = component.Kind,
            BaseType = component.BaseType,
            Description = component.Description,
            PropsType = component.PropsType,
            Properties = component.Properties,
            Events = component.Events,
            SupportsChildren = component.SupportsChildren,
            SupportsState = component.SupportsState,
            Lifecycle = component.Lifecycle,
            Dependencies = component.Dependencies,
            Examples = component.Examples,
            Stylesheets = component.Stylesheets,
            Tags = component.Tags,
            IsAbstract = component.IsAbstract,
            Line = component.Line
        };
    }
}
