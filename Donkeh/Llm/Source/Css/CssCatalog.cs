using System.Text.Json;
using Donkeh.LayoutLab;
using Donkeh.Parser;
using Donkeh.Parser.Builder;
using Donkeh.Parser.Objects;

namespace Donkeh.Llm.Css;

/// <summary>One registered CSS property and its support metadata.</summary>
public sealed class CssPropertyInfo
{
    /// <summary>Property name.</summary>
    public required string Name { get; init; }

    /// <summary>True when the LayoutLab resolver has a property id for the name.</summary>
    public bool Supported { get; init; }

    /// <summary>supported, partial or unsupported.</summary>
    public required string Status { get; init; }

    /// <summary>Support notes from the documentation snapshot.</summary>
    public string? Notes { get; init; }

    /// <summary>Stages that consume the computed value.</summary>
    public string? UsedBy { get; init; }

    /// <summary>Common values, when the engine has a closed value set.</summary>
    public IReadOnlyList<string>? Values { get; init; }
}

/// <summary>One selector construct and whether the matcher supports it.</summary>
public sealed class CssSelectorInfo
{
    /// <summary>Category: type, class, id, universal, attribute, combinator, pseudo-class or pseudo-element.</summary>
    public required string Category { get; init; }

    /// <summary>Selector text.</summary>
    public required string Name { get; init; }

    /// <summary>supported, recognized or unsupported.</summary>
    public required string Status { get; init; }

    /// <summary>Behaviour notes.</summary>
    public string? Notes { get; init; }
}

/// <summary>One registered CSS function.</summary>
public sealed class CssFunctionInfo
{
    /// <summary>Function name without parentheses.</summary>
    public required string Name { get; init; }

    /// <summary>Accepted argument shape when known.</summary>
    public string? Arguments { get; init; }

    /// <summary>Behaviour notes.</summary>
    public string? Notes { get; init; }
}

/// <summary>CSS validation result.</summary>
public sealed class CssValidationResult
{
    /// <summary>File path as supplied by the caller.</summary>
    public required string File { get; init; }

    /// <summary>True when no error-severity diagnostics were produced.</summary>
    public bool Valid { get; init; }

    /// <summary>All diagnostics.</summary>
    public IReadOnlyList<LlmDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Compact counters.</summary>
    public CssValidationSummary Summary { get; init; } = new();
}

/// <summary>Counters for a CSS validation result.</summary>
public sealed class CssValidationSummary
{
    /// <summary>Number of style rules.</summary>
    public int RuleCount { get; init; }

    /// <summary>Number of selectors.</summary>
    public int SelectorCount { get; init; }

    /// <summary>Number of selectors the matcher cannot evaluate.</summary>
    public int UnsupportedSelectorCount { get; init; }

    /// <summary>Number of unsupported property declarations.</summary>
    public int UnsupportedPropertyCount { get; init; }
}

/// <summary>Reads the CSS engine's registered vocabulary.</summary>
public static class CssCatalog
{
    private static readonly Dictionary<string, string> CommonValues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["display"] = "none|block|inline|inline-block|flex|inline-flex|grid|inline-grid|table|inline-table|table-row-group|table-header-group|table-footer-group|table-row|table-cell|table-column|table-column-group|table-caption|list-item|contents|flow-root",
        ["position"] = "static|relative|absolute|fixed|sticky",
        ["float"] = "none|left|right|inline-start|inline-end",
        ["clear"] = "none|left|right|both|inline-start|inline-end",
        ["box-sizing"] = "content-box|border-box",
        ["visibility"] = "visible|hidden|collapse",
        ["overflow"] = "visible|hidden|clip|scroll|auto",
        ["overflow-x"] = "visible|hidden|clip|scroll|auto",
        ["overflow-y"] = "visible|hidden|clip|scroll|auto",
        ["flex-direction"] = "row|row-reverse|column|column-reverse",
        ["flex-wrap"] = "nowrap|wrap|wrap-reverse",
        ["align-items"] = "stretch|flex-start|flex-end|center|baseline|start|end|self-start|self-end",
        ["align-content"] = "stretch|flex-start|flex-end|center|space-between|space-around|space-evenly|start|end",
        ["justify-content"] = "flex-start|flex-end|center|space-between|space-around|space-evenly|start|end|left|right",
        ["justify-items"] = "stretch|start|end|center|left|right|baseline",
        ["text-align"] = "start|end|left|right|center|justify|match-parent",
        ["font-weight"] = "normal|bold|bolder|lighter|100|200|300|400|500|600|700|800|900",
        ["border-style"] = "none|hidden|dotted|dashed|solid|double|groove|ridge|inset|outset",
        ["background-repeat"] = "repeat|repeat-x|repeat-y|no-repeat|space|round",
        ["object-fit"] = "fill|contain|cover|none|scale-down",
        ["white-space"] = "normal|nowrap|pre|pre-wrap|pre-line|break-spaces",
        ["cursor"] = "auto|default|none|context-menu|help|pointer|progress|wait|cell|crosshair|text|vertical-text|alias|copy|move|no-drop|not-allowed|grab|grabbing|all-scroll|col-resize|row-resize|n-resize|e-resize|s-resize|w-resize|ne-resize|nw-resize|se-resize|sw-resize|ew-resize|ns-resize|nesw-resize|nwse-resize|zoom-in|zoom-out"
    };

    private static readonly HashSet<string> MatcherUnsupported = new(StringComparer.Ordinal)
    {
        ":is", ":where", ":has", ":has-slotted", ":host", ":host-context",
        ":active-view-transition", ":active-view-transition-type", ":autofill", ":blank",
        ":buffering", ":current", ":default", ":defined", ":dir", ":fullscreen", ":future",
        ":heading", ":in-range", ":indeterminate", ":interest-source", ":interest-target",
        ":invalid", ":lang", ":local-link", ":modal", ":muted", ":open", ":optional",
        ":out-of-range", ":past", ":paused", ":picture-in-picture", ":playing",
        ":popover-open", ":required", ":right", ":left", ":scope", ":seeking", ":stalled",
        ":target", ":target-after", ":target-before", ":target-current", ":user-invalid",
        ":user-valid", ":valid", ":visited", ":volume-locked", ":xr-overlay"
    };

    private static readonly HashSet<string> MatcherSupported = new(StringComparer.Ordinal)
    {
        ":active", ":any-link", ":checked", ":disabled", ":empty", ":enabled",
        ":first-child", ":first-of-type", ":focus", ":focus-visible", ":focus-within",
        ":hover", ":last-child", ":last-of-type", ":link", ":not", ":only-child",
        ":only-of-type", ":placeholder-shown", ":read-only", ":read-write", ":root",
        ":nth-child", ":nth-last-child", ":nth-of-type", ":nth-last-of-type"
    };

    private static readonly Dictionary<string, string> FunctionArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["calc"] = "calc(expression)",
        ["var"] = "var(--custom-property[, fallback])",
        ["min"] = "min(value, value)",
        ["max"] = "max(value, value)",
        ["clamp"] = "clamp(min, preferred, max)",
        ["rgb"] = "rgb(r, g, b[, a])",
        ["rgba"] = "rgba(r, g, b, a)",
        ["hsl"] = "hsl(h, s%, l%[, a])",
        ["hwb"] = "hwb(h, w%, b%[, a])",
        ["lab"] = "lab(l, a, b[, a])",
        ["lch"] = "lch(l, c, h[, a])",
        ["oklab"] = "oklab(l, a, b[, a])",
        ["oklch"] = "oklch(l, c, h[, a])",
        ["color"] = "color(profile, components[, a])",
        ["color-mix"] = "color-mix(in color-space, color, color)",
        ["url"] = "url(path)",
        ["attr"] = "attr(name type?, fallback?)",
        ["linear-gradient"] = "linear-gradient(direction?, color-stop, ...)",
        ["radial-gradient"] = "radial-gradient(shape?, color-stop, ...)",
        ["conic-gradient"] = "conic-gradient(from angle?, color-stop, ...)",
        ["repeating-linear-gradient"] = "repeating-linear-gradient(direction?, color-stop, ...)",
        ["repeating-radial-gradient"] = "repeating-radial-gradient(shape?, color-stop, ...)",
        ["repeating-conic-gradient"] = "repeating-conic-gradient(from angle?, color-stop, ...)",
        ["rotate"] = "rotate(angle)",
        ["rotate3d"] = "rotate3d(x, y, z, angle)",
        ["scale"] = "scale(x[, y])",
        ["scale3d"] = "scale3d(x, y, z)",
        ["translate"] = "translate(x[, y])",
        ["translate3d"] = "translate3d(x, y, z)",
        ["matrix"] = "matrix(a, b, c, d, e, f)",
        ["matrix3d"] = "matrix3d(16 values)",
        ["skew"] = "skew(x-angle[, y-angle])",
        ["perspective"] = "perspective(distance)",
        ["cubic-bezier"] = "cubic-bezier(x1, y1, x2, y2)",
        ["steps"] = "steps(count[, position])",
        ["blur"] = "blur(radius)",
        ["brightness"] = "brightness(amount)",
        ["contrast"] = "contrast(amount)",
        ["drop-shadow"] = "drop-shadow(offset-x offset-y blur color)",
        ["grayscale"] = "grayscale(amount)",
        ["hue-rotate"] = "hue-rotate(angle)",
        ["invert"] = "invert(amount)",
        ["opacity"] = "opacity(amount)",
        ["saturate"] = "saturate(amount)",
        ["sepia"] = "sepia(amount)",
        ["circle"] = "circle(radius?, at position?)",
        ["ellipse"] = "ellipse(rx ry?, at position?)",
        ["inset"] = "inset(top right bottom left round radius?)",
        ["polygon"] = "polygon(fill-rule?, point, ...)",
        ["path"] = "path(fill-rule?, path-data)",
        ["minmax"] = "minmax(min, max)",
        ["fit-content"] = "fit-content(length-percentage)",
        ["repeat"] = "repeat(count, track-list)",
        ["counter"] = "counter(name, style?)",
        ["counters"] = "counters(name, separator, style?)"
    };

    private static List<CssPropertyInfo>? _properties;
    private static List<CssFunctionInfo>? _functions;
    private static List<CssSelectorInfo>? _selectors;

    /// <summary>Returns every property the LayoutLab resolver recognizes.</summary>
    public static List<CssPropertyInfo> Properties()
    {
        if (_properties is not null)
        {
            return _properties;
        }

        var docs = LoadPropertyDocs();
        var result = new Dictionary<string, CssPropertyInfo>(StringComparer.OrdinalIgnoreCase);
        _ = Tokenizer.Parse("");

        foreach (var entry in RuleBuilder.ExactRules)
        {
            if (entry.Value != TokenKind.PropertyName)
            {
                continue;
            }

            var name = InternTable.GetString(entry.Key);

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var property = CssPropertyIds.Get(entry.Key);
            var supported = property != CssProperty.Unknown;

            if (!supported)
            {
                continue;
            }

            docs.TryGetValue(name, out var doc);
            result[name] = new CssPropertyInfo
            {
                Name = name,
                Supported = true,
                Status = doc?.Status ?? "supported",
                Notes = doc?.Notes,
                UsedBy = doc?.UsedBy,
                Values = CommonValues.TryGetValue(name, out var values)
                    ? values.Split('|')
                    : null
            };
        }

        _properties = result.Values.OrderBy(property => property.Name, StringComparer.Ordinal).ToList();
        return _properties;
    }

    /// <summary>Returns registered CSS functions.</summary>
    public static List<CssFunctionInfo> Functions()
    {
        if (_functions is not null)
        {
            return _functions;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _ = Tokenizer.Parse("");

        foreach (var entry in RuleBuilder.ExactRules)
        {
            if (entry.Value != TokenKind.Function)
            {
                continue;
            }

            var name = InternTable.GetString(entry.Key);

            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        // These names are both properties and functions; the rule table stores
        // PropertyName so the tokenizer can upgrade on a following '('.
        foreach (var name in new[] { "color", "inset", "opacity", "rotate", "scale", "translate" })
        {
            names.Add(name);
        }

        _functions = names
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new CssFunctionInfo
            {
                Name = name,
                Arguments = FunctionArguments.GetValueOrDefault(name),
                Notes = FunctionArguments.ContainsKey(name) ? null : "Registered by the CSS tokenizer."
            })
            .ToList();
        return _functions;
    }

    /// <summary>Returns the selector vocabulary and matcher support notes.</summary>
    public static List<CssSelectorInfo> Selectors()
    {
        if (_selectors is not null)
        {
            return _selectors;
        }

        _ = Tokenizer.Parse("");
        var result = new List<CssSelectorInfo>
        {
            new() { Category = "type", Name = "element", Status = "supported", Notes = "Matches a tag name." },
            new() { Category = "class", Name = ".class", Status = "supported" },
            new() { Category = "id", Name = "#id", Status = "supported" },
            new() { Category = "universal", Name = "*", Status = "supported" },
            new() { Category = "attribute", Name = "[attr], [attr=value], [attr~=value], [attr|=value], [attr^=value], [attr$=value], [attr*=value]", Status = "supported" },
            new() { Category = "combinator", Name = "descendant (space)", Status = "supported" },
            new() { Category = "combinator", Name = "child (>)", Status = "supported" },
            new() { Category = "combinator", Name = "adjacent sibling (+)", Status = "supported" },
            new() { Category = "combinator", Name = "general sibling (~)", Status = "supported" },
            new() { Category = "combinator", Name = "column (||)", Status = "recognized", Notes = "Parsed by the tokenizer; verify matcher support for the target host." }
        };

        foreach (var entry in RuleBuilder.ExactRules)
        {
            if (entry.Value is not (TokenKind.PseudoClass or TokenKind.PseudoElement))
            {
                continue;
            }

            var name = InternTable.GetString(entry.Key);

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (entry.Value == TokenKind.PseudoClass)
            {
                var status = MatcherSupported.Contains(name)
                    ? "supported"
                    : MatcherUnsupported.Contains(name)
                        ? "unsupported"
                        : "recognized";
                var notes = status switch
                {
                    "supported" when name == ":not" =>
                        "Supports a single simple compound, for example :not(.disabled). Selector lists and combinators inside the argument are rejected.",
                    "supported" => "Evaluated by the selector matcher.",
                    "unsupported" => "Recognized by the tokenizer but rejected by the matcher.",
                    _ => "Recognized by the tokenizer; verify matcher support before relying on it."
                };
                result.Add(new CssSelectorInfo
                {
                    Category = "pseudo-class",
                    Name = name,
                    Status = status,
                    Notes = notes
                });
            }
            else
            {
                var status = name is "::before" or "::after" or "::selection" ? "supported" : "recognized";
                var notes = name switch
                {
                    "::before" or "::after" => "Generates a box when resolved content is a string.",
                    "::selection" => "Styles the text highlight; color is stored on the computed style.",
                    _ => "Recognized by the tokenizer; other generated pseudo-elements are ignored by the engine."
                };
                result.Add(new CssSelectorInfo
                {
                    Category = "pseudo-element",
                    Name = name,
                    Status = status,
                    Notes = notes
                });
            }
        }

        _selectors = result
            .OrderBy(item => item.Category, StringComparer.Ordinal)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
        return _selectors;
    }

    /// <summary>Returns the common values for a property, when known.</summary>
    public static IReadOnlyList<string>? ValuesFor(string property) =>
        CommonValues.TryGetValue(property, out var values) ? values.Split('|') : null;

    private static Dictionary<string, PropertyDoc> LoadPropertyDocs()
    {
        var result = new Dictionary<string, PropertyDoc>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var stream = typeof(CssCatalog).Assembly
                .GetManifestResourceStream("Donkeh.Llm.Resources.css-properties.json");

            if (stream is null)
            {
                return result;
            }

            using var document = JsonDocument.Parse(stream);
            var properties = document.RootElement.GetProperty("properties");

            foreach (var property in properties.EnumerateArray())
            {
                var name = property.GetProperty("name").GetString();
                var status = property.GetProperty("status").GetString();
                var notes = property.GetProperty("notes").GetString();
                var usedBy = property.GetProperty("usedBy").GetString();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    result[name] = new PropertyDoc(
                        status ?? "supported",
                        string.IsNullOrWhiteSpace(notes) ? null : notes,
                        string.IsNullOrWhiteSpace(usedBy) ? null : usedBy);
                }
            }
        }
        catch (Exception)
        {
            // The docs snapshot is optional metadata.
        }

        return result;
    }

    private sealed record PropertyDoc(string Status, string? Notes, string? UsedBy);
}
