namespace Donkeh.Llm.Components;

/// <summary>One public property in a component's props type.</summary>
public sealed class ComponentProperty
{
    /// <summary>Property name as written in C#.</summary>
    public required string Name { get; init; }

    /// <summary>Fully qualified or source-level property type.</summary>
    public required string Type { get; init; }

    /// <summary>Default value expression, when one is declared.</summary>
    public string? Default { get; init; }

    /// <summary>True when the property is declared with the <c>required</c> modifier.</summary>
    public bool Required { get; init; }

    /// <summary>XML documentation summary, when available.</summary>
    public string? Description { get; init; }

    /// <summary>True when the property is an event handler rather than data.</summary>
    public bool IsEvent { get; init; }
}

/// <summary>One event exposed by a component props type.</summary>
public sealed class ComponentEvent
{
    /// <summary>Handler property name, for example <c>OnClick</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Delegate type, for example <c>Action&lt;ClickEvent&gt;</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Event argument type, when the delegate carries one.</summary>
    public string? EventArgsType { get; init; }

    /// <summary>XML documentation summary, when available.</summary>
    public string? Description { get; init; }
}

/// <summary>Everything the CLI knows about one component.</summary>
public sealed class ComponentMetadata
{
    /// <summary>Component name without a namespace.</summary>
    public required string Name { get; init; }

    /// <summary>Namespace, when known.</summary>
    public string? Namespace { get; init; }

    /// <summary>Package or assembly that supplies the component.</summary>
    public string? Package { get; init; }

    /// <summary>Source file for project components or assembly path for packaged components.</summary>
    public string? Source { get; init; }

    /// <summary>class, csx, html-element or assembly.</summary>
    public required string Kind { get; init; }

    /// <summary>Base type as written in source or metadata.</summary>
    public string? BaseType { get; init; }

    /// <summary>Short description from XML docs or a generated summary.</summary>
    public string? Description { get; init; }

    /// <summary>Name of the props type, when the component is generic.</summary>
    public string? PropsType { get; init; }

    /// <summary>Public data properties.</summary>
    public IReadOnlyList<ComponentProperty> Properties { get; init; } = [];

    /// <summary>Public event handlers.</summary>
    public IReadOnlyList<ComponentEvent> Events { get; init; } = [];

    /// <summary>True when the component accepts child nodes.</summary>
    public bool SupportsChildren { get; init; }

    /// <summary>True when the component source uses hooks.</summary>
    public bool SupportsState { get; init; }

    /// <summary>Lifecycle overrides found in source.</summary>
    public IReadOnlyList<string> Lifecycle { get; init; } = [];

    /// <summary>Namespaces and component names referenced by the component.</summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    /// <summary>Documented or generated minimal usage examples.</summary>
    public IReadOnlyList<string> Examples { get; init; } = [];

    /// <summary>Stylesheets declared by the assembly or embedded in the project.</summary>
    public IReadOnlyList<string> Stylesheets { get; init; } = [];

    /// <summary>Search terms derived from the component, package and description.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>True for abstract types that cannot be mounted directly.</summary>
    public bool IsAbstract { get; init; }

    /// <summary>One-based source line for source components.</summary>
    public int? Line { get; init; }
}

/// <summary>Filters applied when listing or searching components.</summary>
public sealed class ComponentQuery
{
    /// <summary>Free-text query.</summary>
    public string? Text { get; init; }

    /// <summary>Optional package filter.</summary>
    public string? Package { get; init; }

    /// <summary>Optional kind filter: class, csx, html-element or assembly.</summary>
    public string? Kind { get; init; }

    /// <summary>Maximum number of results.</summary>
    public int Limit { get; init; } = 50;
}
