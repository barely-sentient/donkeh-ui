namespace Donkeh.Llm.Csx;

/// <summary>One attribute found in a CSX element.</summary>
public sealed class CsxAttributeInfo
{
    /// <summary>Attribute name exactly as written.</summary>
    public required string Name { get; init; }

    /// <summary>Literal value, expression text or <c>true</c> for a valueless attribute.</summary>
    public string? Value { get; init; }

    /// <summary>literal, expression or boolean.</summary>
    public required string ValueKind { get; init; }

    /// <summary>One-based line.</summary>
    public int Line { get; init; }

    /// <summary>One-based column.</summary>
    public int Column { get; init; }
}

/// <summary>One element or component in a CSX file.</summary>
public sealed class CsxElementInfo
{
    /// <summary>Tag or component name; empty for a fragment.</summary>
    public required string Name { get; init; }

    /// <summary>element, component or fragment.</summary>
    public required string Kind { get; init; }

    /// <summary>Attributes on the element.</summary>
    public IReadOnlyList<CsxAttributeInfo> Attributes { get; init; } = [];

    /// <summary>One-based line.</summary>
    public int Line { get; init; }

    /// <summary>One-based column.</summary>
    public int Column { get; init; }

    /// <summary>True when the element closes itself.</summary>
    public bool SelfClosing { get; init; }

    /// <summary>Nesting depth, starting at zero.</summary>
    public int Depth { get; init; }
}

/// <summary>One C# expression embedded in CSX markup.</summary>
public sealed class CsxExpressionInfo
{
    /// <summary>Expression text without the surrounding braces.</summary>
    public required string Text { get; init; }

    /// <summary>One-based line.</summary>
    public int Line { get; init; }

    /// <summary>One-based column.</summary>
    public int Column { get; init; }

    /// <summary>attribute or child.</summary>
    public required string Context { get; init; }
}

/// <summary>Structural analysis of one CSX file.</summary>
public sealed class CsxInspectionResult
{
    /// <summary>File path as supplied by the caller.</summary>
    public required string File { get; init; }

    /// <summary>True when no parse-level diagnostics were found.</summary>
    public bool Valid { get; init; }

    /// <summary>Every element and component in source order.</summary>
    public IReadOnlyList<CsxElementInfo> Elements { get; init; } = [];

    /// <summary>Distinct component names used by the file.</summary>
    public IReadOnlyList<string> Components { get; init; } = [];

    /// <summary>Every attribute in source order.</summary>
    public IReadOnlyList<CsxAttributeInfo> Attributes { get; init; } = [];

    /// <summary>Every embedded C# expression.</summary>
    public IReadOnlyList<CsxExpressionInfo> Expressions { get; init; } = [];

    /// <summary>Using directives and component references.</summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    /// <summary>Component names declared outside this file.</summary>
    public IReadOnlyList<string> ReferencedTypes { get; init; } = [];

    /// <summary>Parse-level diagnostics.</summary>
    public IReadOnlyList<LlmDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Compact counters for progressive disclosure.</summary>
    public CsxSummary Summary { get; init; } = new();
}

/// <summary>Counters describing a CSX file without returning every node.</summary>
public sealed class CsxSummary
{
    /// <summary>Number of HTML elements.</summary>
    public int ElementCount { get; init; }

    /// <summary>Number of component usages.</summary>
    public int ComponentCount { get; init; }

    /// <summary>Number of attributes.</summary>
    public int AttributeCount { get; init; }

    /// <summary>Number of embedded expressions.</summary>
    public int ExpressionCount { get; init; }

    /// <summary>Number of distinct component names.</summary>
    public int DistinctComponentCount { get; init; }
}

/// <summary>Validation result for one CSX file.</summary>
public sealed class CsxValidationResult
{
    /// <summary>File path as supplied by the caller.</summary>
    public required string File { get; init; }

    /// <summary>True when no error-severity diagnostics were produced.</summary>
    public bool Valid { get; init; }

    /// <summary>All diagnostics, including warnings and info.</summary>
    public IReadOnlyList<LlmDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Number of errors.</summary>
    public int ErrorCount => Diagnostics.Count(diagnostic => diagnostic.Severity == "error");

    /// <summary>Number of warnings.</summary>
    public int WarningCount => Diagnostics.Count(diagnostic => diagnostic.Severity == "warning");
}

/// <summary>Compilation result for one CSX file.</summary>
public sealed class CsxCompileResult
{
    /// <summary>File path as supplied by the caller.</summary>
    public required string File { get; init; }

    /// <summary>True when transformation completed.</summary>
    public bool Success { get; init; }

    /// <summary>Diagnostics from transformation and semantic validation.</summary>
    public IReadOnlyList<LlmDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Generated C# when requested.</summary>
    public string? Generated { get; init; }

    /// <summary>Source map entries when requested.</summary>
    public IReadOnlyList<CsxSourceMapEntry>? SourceMap { get; init; }

    /// <summary>Resolved component and property metadata.</summary>
    public CsxResolvedMetadata? Resolved { get; init; }
}

/// <summary>One source-map segment.</summary>
public sealed class CsxSourceMapEntry
{
    /// <summary>Offset in the generated file.</summary>
    public int GeneratedStart { get; init; }

    /// <summary>Length in the generated file.</summary>
    public int GeneratedLength { get; init; }

    /// <summary>Offset in the original file.</summary>
    public int OriginalStart { get; init; }

    /// <summary>Length in the original file.</summary>
    public int OriginalLength { get; init; }
}

/// <summary>Component, property and event metadata resolved during compilation.</summary>
public sealed class CsxResolvedMetadata
{
    /// <summary>Components used by the file.</summary>
    public IReadOnlyList<CsxResolvedComponent> Components { get; init; } = [];

    /// <summary>Property bindings found on components.</summary>
    public IReadOnlyList<CsxResolvedBinding> Properties { get; init; } = [];

    /// <summary>Event bindings found on components.</summary>
    public IReadOnlyList<CsxResolvedBinding> Events { get; init; } = [];
}

/// <summary>A component resolved against project and package metadata.</summary>
public sealed class CsxResolvedComponent
{
    /// <summary>Component name.</summary>
    public required string Name { get; init; }

    /// <summary>Package or project that supplies the component.</summary>
    public string? Package { get; init; }

    /// <summary>class, csx, html-element or assembly.</summary>
    public required string Kind { get; init; }

    /// <summary>Props type name.</summary>
    public string? PropsType { get; init; }
}

/// <summary>A property or event binding on a component.</summary>
public sealed class CsxResolvedBinding
{
    /// <summary>Component name.</summary>
    public required string Component { get; init; }

    /// <summary>Source attribute name.</summary>
    public required string Attribute { get; init; }

    /// <summary>Resolved property name.</summary>
    public string? Property { get; init; }

    /// <summary>Resolved property type.</summary>
    public string? Type { get; init; }

    /// <summary>One-based line.</summary>
    public int Line { get; init; }
}
