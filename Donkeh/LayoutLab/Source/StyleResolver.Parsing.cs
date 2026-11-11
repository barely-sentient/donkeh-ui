using System.Globalization;
using System.Numerics;
using System.Text;
using Donkeh.Parser;

namespace Donkeh.LayoutLab;

public sealed partial class StyleResolver
{
    /// <summary>
    /// The width, style and colour carried by a <c>border</c> or
    /// <c>border-&lt;side&gt;</c> shorthand.
    /// </summary>
    /// <param name="Width">
    /// The parsed width. It stays a <see cref="CssLength"/> so percentages and
    /// relative units can be resolved by the layout pass. The default is
    /// <c>medium</c> (3px).
    /// </param>
    /// <param name="Style">
    /// The parsed line style. The initial value is <see cref="BorderStyle.None"/>,
    /// which makes a width alone invisible, as in CSS.
    /// </param>
    /// <param name="Color">
    /// The packed ARGB colour, or <see cref="ComputedStyle.CurrentColor"/> when the
    /// shorthand did not name a colour.
    /// </param>
    /// <remarks>
    /// The shorthand is order-insensitive: components are recognised by their
    /// shape, and the last width, style or colour wins if a component is repeated.
    /// </remarks>
    internal readonly record struct ParsedBorder(CssLength Width, BorderStyle Style, uint Color);

    /// <summary>The four side styles produced by the <c>border-style</c> shorthand.</summary>
    /// <param name="Top">Top edge style.</param>
    /// <param name="Right">Right edge style.</param>
    /// <param name="Bottom">Bottom edge style.</param>
    /// <param name="Left">Left edge style.</param>
    internal readonly record struct BorderStyle4(
        BorderStyle Top,
        BorderStyle Right,
        BorderStyle Bottom,
        BorderStyle Left);

    /// <summary>
    /// The four colours produced by the <c>border-color</c> shorthand.
    /// </summary>
    /// <param name="Top">Top edge colour, packed ARGB.</param>
    /// <param name="Right">Right edge colour, packed ARGB.</param>
    /// <param name="Bottom">Bottom edge colour, packed ARGB.</param>
    /// <param name="Left">Left edge colour, packed ARGB.</param>
    /// <remarks>
    /// Any component may be <see cref="ComputedStyle.CurrentColor"/>; callers
    /// resolve that sentinel against the element's own colour.
    /// </remarks>
    internal readonly record struct Color4(uint Top, uint Right, uint Bottom, uint Left);

    /// <summary>
    /// Trims a declaration value and removes a trailing <c>!important</c> flag.
    /// </summary>
    /// <param name="valueId">
    /// The interned id of <paramref name="rawValue"/>. It is the cache key, so the
    /// same id must always identify the same text.
    /// </param>
    /// <param name="rawValue">The value as stored in the rule.</param>
    /// <returns>The cleaned value, which may be empty.</returns>
    /// <remarks>
    /// Results are memoised in <see cref="StyleValueCache.CleanValues"/>. The
    /// check uses <c>LastIndexOf('!')</c> and then validates that everything
    /// after it is exactly <c>important</c>, so only a final flag is removed.
    /// </remarks>
    private string CleanValue(int valueId, string rawValue)
    {
        if (_cache.CleanValues.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var value = rawValue.Trim();
        var bang = value.LastIndexOf('!');

        if (bang >= 0
            && value[(bang + 1)..].Trim().Equals("important", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..bang].TrimEnd();
        }

        _cache.CleanValues[valueId] = value;
        return value;
    }

    /// <summary>
    /// Determines whether a raw declaration value ends with an
    /// <c>!important</c> flag.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="rawValue">The raw value from the rule.</param>
    /// <returns><see langword="true"/> for an important declaration.</returns>
    /// <remarks>
    /// The test trims trailing whitespace and accepts any casing. It is
    /// deliberately cheaper than <see cref="CleanValue"/> because the cascade
    /// asks the question for every matched declaration in both passes. Results
    /// are memoised in <see cref="StyleValueCache.Important"/>.
    /// </remarks>
    private bool IsImportant(int valueId, string rawValue)
    {
        if (_cache.Important.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var value = rawValue.TrimEnd();
        var important = value.EndsWith("!important", StringComparison.OrdinalIgnoreCase);
        _cache.Important[valueId] = important;
        return important;
    }

    /// <summary>
    /// Substitutes <c>var()</c> references in a declaration value.
    /// </summary>
    /// <param name="value">The cleaned declaration value.</param>
    /// <param name="style">
    /// The style being built; custom properties are looked up on it and its
    /// <see cref="ComputedStyle.StyleParent"/> chain.
    /// </param>
    /// <param name="valueId">The interned id of the original value.</param>
    /// <returns>
    /// The value with every resolvable reference replaced. Missing references
    /// without a fallback become empty strings.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The fast path is important for hot declarations: whether a value contains
    /// <c>var(</c> is memoised in <see cref="StyleValueCache.HasVariables"/>, so
    /// ordinary values pay only a dictionary probe and are returned untouched.
    /// </para>
    /// <para>
    /// Resolved strings are cached in <see cref="StyleValueCache.ResolvedValues"/>
    /// under <c>(valueId, CustomPropertyHash)</c>. The hash covers the inherited
    /// custom-property chain, so descendants that share a variable environment
    /// reuse one string while a different environment gets a different entry.
    /// </para>
    /// <para>
    /// The cache is cleared wholesale once it exceeds 4096 entries. This bounds
    /// memory for documents that vary variables per node at the cost of
    /// re-resolving after a clear.
    /// </para>
    /// </remarks>
    private string ResolveVariables(string value, ComputedStyle style, int valueId)
    {
        var hasVariables = _cache.HasVariables.TryGetValue(valueId, out var known)
            ? known
            : value.Contains("var(", StringComparison.OrdinalIgnoreCase);

        _cache.HasVariables[valueId] = hasVariables;

        if (!hasVariables)
        {
            return value;
        }

        var key = (valueId, style.CustomPropertyHash);

        if (_cache.ResolvedValues.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var resolved = ResolveVariablesCore(value, style, 0);

        if (_cache.ResolvedValues.Count > 4096)
        {
            _cache.ResolvedValues.Clear();
        }

        _cache.ResolvedValues[key] = resolved;
        return resolved;
    }

    /// <summary>
    /// Performs the actual left-to-right <c>var()</c> substitution, recursively
    /// resolving references introduced by other custom properties.
    /// </summary>
    /// <param name="value">The text to scan.</param>
    /// <param name="style">
    /// The style whose custom-property chain is consulted, or
    /// <see langword="null"/> to resolve only fallbacks.
    /// </param>
    /// <param name="depth">The current recursion depth; the caller starts at 0.</param>
    /// <returns>The substituted text.</returns>
    /// <remarks>
    /// <para>
    /// Each reference is parsed as <c>var(&lt;name&gt;[, &lt;fallback&gt;])</c>.
    /// The fallback is used when the name is not declared on the style chain.
    /// Both the replacement and the fallback may themselves contain references.
    /// </para>
    /// <para>
    /// Recursion is capped at a depth of 16 to break self-referential or mutually
    /// referential custom properties. At the cap the innermost value is returned
    /// unchanged rather than throwing. An unmatched <c>(</c> leaves the rest of
    /// the text verbatim.
    /// </para>
    /// <para>
    /// The builder is only allocated after a reference is found; values without
    /// <c>var(</c> return the original string instance.
    /// </para>
    /// </remarks>
    private static string ResolveVariablesCore(
        string value,
        ComputedStyle? style,
        int depth)
    {
        if (depth > 16)
        {
            return value;
        }

        var start = value.IndexOf("var(", StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 16);
        var cursor = 0;

        while (start >= 0)
        {
            builder.Append(value, cursor, start - cursor);

            var open = start + 4;
            var close = FindMatchingParenthesis(value, open - 1);

            if (close < 0)
            {
                builder.Append(value, start, value.Length - start);
                return builder.ToString();
            }

            var content = value[open..close];
            var comma = FindTopLevelComma(content);
            var name = (comma < 0 ? content : content[..comma]).Trim();
            var fallback = comma < 0 ? null : content[(comma + 1)..].Trim();

            string? replacement = null;

            if (name.StartsWith("--", StringComparison.Ordinal)
                && style is not null
                && style.TryGetCustomProperty(name, out var custom))
            {
                replacement = custom;
            }

            replacement ??= fallback ?? string.Empty;

            if (replacement.Contains("var(", StringComparison.OrdinalIgnoreCase))
            {
                replacement = ResolveVariablesCore(replacement, style, depth + 1);
            }

            builder.Append(replacement);
            cursor = close + 1;
            start = value.IndexOf("var(", cursor, StringComparison.OrdinalIgnoreCase);
        }

        builder.Append(value, cursor, value.Length - cursor);
        return builder.ToString();
    }

    /// <summary>
    /// Finds the closing parenthesis that balances the one at
    /// <paramref name="openIndex"/>.
    /// </summary>
    /// <param name="value">The text to scan.</param>
    /// <param name="openIndex">The index of the opening <c>(</c>.</param>
    /// <returns>The index of the matching <c>)</c>, or -1 when unbalanced.</returns>
    /// <remarks>
    /// Nested parentheses are counted. Quoted strings are not treated specially,
    /// so a parenthesis inside a string still affects the depth.
    /// </remarks>
    private static int FindMatchingParenthesis(string value, int openIndex)
    {
        var depth = 0;

        for (var i = openIndex; i < value.Length; i++)
        {
            if (value[i] == '(')
            {
                depth++;
            }
            else if (value[i] == ')')
            {
                depth--;

                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds the first comma that is not nested inside parentheses.
    /// </summary>
    /// <param name="value">The text to scan, normally the inside of a <c>var()</c>.</param>
    /// <returns>The index of the comma, or -1 when there is no top-level comma.</returns>
    /// <remarks>
    /// Used to separate a custom property name from its fallback without being
    /// confused by commas in functions such as <c>rgb(1, 2, 3)</c>.
    /// </remarks>
    private static int FindTopLevelComma(string value)
    {
        var depth = 0;

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth--;
            else if (value[i] == ',' && depth == 0) return i;
        }

        return -1;
    }

    /// <summary>
    /// Returns the memoised answer to whether a value contains <c>var(</c>.
    /// </summary>
    /// <param name="valueId">The interned value id.</param>
    /// <returns>
    /// <see langword="true"/> only when the value has been seen by
    /// <see cref="ResolveVariables"/> and found to contain a reference.
    /// </returns>
    /// <remarks>
    /// A <see langword="false"/> result also covers values that have not been
    /// observed yet, so callers must not use this to prove the absence of
    /// variables. The parse helpers use it to decide whether their
    /// <c>valueId</c>-keyed cache is safe: a value that depends on the variable
    /// environment must not be stored under the value id alone.
    /// </remarks>
    private bool HasVariables(int valueId)
    {
        return _cache.HasVariables.TryGetValue(valueId, out var has) && has;
    }

    /// <summary>
    /// Determines whether a value contains the <c>currentColor</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value to scan.</param>
    /// <returns><see langword="true"/> for either CSS spelling.</returns>
    /// <remarks>
    /// Both <c>currentcolor</c> and the legacy hyphenated
    /// <c>current-color</c> are recognised, case-insensitively. Values containing
    /// the keyword cannot be cached under the value id alone because the result
    /// depends on the element's <c>color</c>.
    /// </remarks>
    private bool HasCurrentColor(string value)
    {
        return value.Contains("currentcolor", StringComparison.OrdinalIgnoreCase)
            || value.Contains("current-color", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses one CSS length, reusing the cached result for a variable-free value.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <returns>
    /// The parsed length. Percentages, relative units and <c>auto</c> remain
    /// unresolved.
    /// </returns>
    /// <remarks>
    /// Values containing <c>var()</c> bypass both the read and the write because
    /// their text depends on the custom-property environment. Absolute values
    /// are shared across every node that uses the same interned declaration.
    /// </remarks>
    private CssLength ParseLengthCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Lengths.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = CssValueParser.ParseLength(value);

        if (!HasVariables(valueId))
        {
            _cache.Lengths[valueId] = parsed;
        }

        return parsed;
    }

    /// <summary>
    /// Parses a one-to-four value box shorthand, with caching.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <returns>
    /// Top, right, bottom and left components; omitted components follow the CSS
    /// one/two/three/four-value expansion rules.
    /// </returns>
    /// <remarks>
    /// Used by <c>margin</c>, <c>padding</c>, <c>inset</c>,
    /// <c>border-width</c> and <c>gap</c>. Values containing <c>var()</c> are
    /// not cached.
    /// </remarks>
    private CssLength4 ParseLength4Cached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Length4.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = CssLengths.ParseShorthand(value);

        if (!HasVariables(valueId))
        {
            _cache.Length4[valueId] = parsed;
        }

        return parsed;
    }

    /// <summary>
    /// Parses a unitless number, with caching.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <returns>The parsed number, or 0 when no number is present.</returns>
    /// <remarks>
    /// Used by <c>opacity</c>, <c>z-index</c>, flex factors, <c>order</c>,
    /// <c>column-count</c> and <c>-webkit-line-clamp</c>. Callers clamp or round
    /// the result to their property's range. Variable-dependent values are not
    /// cached.
    /// </remarks>
    private float ParseNumberCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Numbers.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = CssValueParser.ParseNumber(value);

        if (!HasVariables(valueId))
        {
            _cache.Numbers[valueId] = parsed;
        }

        return parsed;
    }

    /// <summary>
    /// Parses one colour, with caching when the result does not depend on context.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <param name="currentColor">
    /// The element's computed colour, used to resolve <c>currentcolor</c>.
    /// </param>
    /// <returns>The packed ARGB colour, or <paramref name="currentColor"/> when
    /// the value cannot be parsed.</returns>
    /// <remarks>
    /// Values containing <c>var()</c> or <c>currentcolor</c> are parsed but never
    /// cached under <paramref name="valueId"/>, because the same id can produce a
    /// different colour on a different element.
    /// </remarks>
    private uint ParseColorCached(int valueId, string value, uint currentColor)
    {
        var cacheable = !HasVariables(valueId) && !HasCurrentColor(value);

        if (cacheable && _cache.Colors.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = CssColor.ParseOr(value, currentColor, currentColor);

        if (cacheable)
        {
            _cache.Colors[valueId] = parsed;
        }

        return parsed;
    }

    /// <summary>
    /// Parses a colour while preserving the <c>currentcolor</c> sentinel.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <returns>
    /// A packed ARGB colour or <see cref="ComputedStyle.CurrentColor"/> when the
    /// keyword is present.
    /// </returns>
    /// <remarks>
    /// This is used for the first component of <c>border-color</c>, so the
    /// sentinel survives until the element's own colour is known. The cache is
    /// keyed by the whole shorthand's value id; that is safe because the first
    /// component is a stable function of the value text, but callers must not use
    /// this method for later components. Values containing <c>var()</c> or
    /// <c>currentcolor</c> are not cached.
    /// </remarks>
    private uint ParseColorSentinel(int valueId, string value)
    {
        var cacheable = !HasVariables(valueId) && !HasCurrentColor(value);

        if (cacheable && _cache.Colors.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = CssColor.ParseOr(value, ComputedStyle.CurrentColor, ComputedStyle.CurrentColor);

        if (cacheable)
        {
            _cache.Colors[valueId] = parsed;
        }

        return parsed;
    }

    /// <summary>
    /// Parses the one-to-four colour form used by <c>border-color</c>.
    /// </summary>
    /// <param name="valueId">
    /// The interned value id, used for the first component's cache entry.
    /// </param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <param name="currentColor">
    /// The element's computed colour, used when the value has no parsable colour.
    /// </param>
    /// <returns>Top, right, bottom and left colours.</returns>
    /// <remarks>
    /// <para>
    /// Expansion follows CSS: one value sets all sides, two set
    /// vertical/horizontal, three set top/horizontal/bottom, and four set
    /// top/right/bottom/left.
    /// </para>
    /// <para>
    /// Only the first component goes through the value-id-keyed cache. The
    /// remaining components are parsed directly: reusing the shorthand's value id
    /// for them would make the second colour return the first colour's entry.
    /// </para>
    /// </remarks>
    private Color4 ParseColor4Cached(int valueId, string value, uint currentColor)
    {
        var parts = CssValueParser.SplitTopLevel(value, ' ');

        if (parts.Count == 0)
        {
            return new Color4(currentColor, currentColor, currentColor, currentColor);
        }

        var first = ParseColorSentinel(valueId, parts[0]);

        static uint ParsePart(string part)
        {
            return CssColor.ParseOr(part, ComputedStyle.CurrentColor, ComputedStyle.CurrentColor);
        }

        if (parts.Count == 1)
        {
            return new Color4(first, first, first, first);
        }

        if (parts.Count == 2)
        {
            var second = ParsePart(parts[1]);
            return new Color4(first, second, first, second);
        }

        if (parts.Count == 3)
        {
            var second = ParsePart(parts[1]);
            return new Color4(first, second, ParsePart(parts[2]), second);
        }

        return new Color4(first, ParsePart(parts[1]), ParsePart(parts[2]), ParsePart(parts[3]));
    }

    /// <summary>
    /// Parses a <c>border</c>-style shorthand into width, style and colour.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <returns>
    /// The parsed components. Missing components use the CSS initial values:
    /// <c>medium</c> (3px), <see cref="BorderStyle.None"/> and
    /// <see cref="ComputedStyle.CurrentColor"/>.
    /// </returns>
    /// <remarks>
    /// Tokens are classified independently, so order does not matter. Width
    /// keywords map to 1px, 3px and 5px; a length overrides the current width,
    /// and the last width or style wins if repeated. A colour keyword or function
    /// is parsed with the <c>currentColor</c> sentinel preserved for the painter.
    /// </remarks>
    private ParsedBorder ParseBorderCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Borders.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var width = CssLength.Px(3);
        var style = BorderStyle.None;
        var color = ComputedStyle.CurrentColor;
        var parts = CssValueParser.SplitTopLevel(value, ' ');

        foreach (var part in parts)
        {
            if (TryParseBorderStyle(part, out var parsedStyle))
            {
                style = parsedStyle;
                continue;
            }

            if (part.Equals("thin", StringComparison.OrdinalIgnoreCase))
            {
                width = CssLength.Px(1);
                continue;
            }

            if (part.Equals("medium", StringComparison.OrdinalIgnoreCase))
            {
                width = CssLength.Px(3);
                continue;
            }

            if (part.Equals("thick", StringComparison.OrdinalIgnoreCase))
            {
                width = CssLength.Px(5);
                continue;
            }

            if (CssColor.TryParse(part, ComputedStyle.CurrentColor, out var parsedColor))
            {
                color = parsedColor;
                continue;
            }

            var length = CssValueParser.ParseLength(part);

            if (!length.IsAuto)
            {
                width = length;
            }
        }

        var border = new ParsedBorder(width, style, color);

        if (!HasVariables(valueId))
        {
            _cache.Borders[valueId] = border;
        }

        return border;
    }

    /// <summary>
    /// Parses <c>border-radius</c> and caches the four corner lengths.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <returns>Top-left, top-right, bottom-right and bottom-left radii.</returns>
    /// <remarks>
    /// The slash form defines horizontal and vertical radii separately. This cut
    /// keeps only the horizontal set, which is exact for circular corners and a
    /// documented approximation for elliptical ones. Values with <c>var()</c> are
    /// not cached.
    /// </remarks>
    private CssCornerRadius4 ParseRadiusCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Radii.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        // The slash form separates horizontal and vertical radii. This cut keeps
        // the horizontal radii, which is exact for the common circular case.
        var slash = value.IndexOf('/');

        if (slash >= 0)
        {
            value = value[..slash].Trim();
        }

        var lengths = CssLengths.ParseShorthand(value);
        var radius = new CssCornerRadius4(lengths.Top, lengths.Right, lengths.Bottom, lengths.Left);

        if (!HasVariables(valueId))
        {
            _cache.Radii[valueId] = radius;
        }

        return radius;
    }

    /// <summary>
    /// Parses a grid track list, with caching.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted track list.</param>
    /// <returns>The parsed <see cref="GridTrackList"/>.</returns>
    /// <remarks>
    /// Used by <c>grid-template-columns</c>, <c>grid-template-rows</c>,
    /// <c>grid-auto-columns</c> and <c>grid-auto-rows</c>. Variable-dependent
    /// values are not cached.
    /// </remarks>
    private GridTrackList ParseTracksCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Tracks.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = GridTrackList.Parse(value);

        if (!HasVariables(valueId))
        {
            _cache.Tracks[valueId] = parsed;
        }

        return parsed;
    }

    /// <summary>
    /// Parses a <c>grid-column</c> or <c>grid-row</c> placement, with caching.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted placement.</param>
    /// <returns>The parsed <see cref="GridPlacement"/>.</returns>
    /// <remarks>
    /// The parser accepts line numbers, names, <c>span N</c>, <c>auto</c> and the
    /// two-value <c>start / end</c> form. Variable-dependent values are not cached.
    /// </remarks>
    private GridPlacement ParsePlacementCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Placements.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = GridPlacementParser.Parse(value);

        if (!HasVariables(valueId))
        {
            _cache.Placements[valueId] = parsed;
        }

        return parsed;
    }

    /// <summary>
    /// Parses <c>grid-template-areas</c>, caching only successful results.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted area matrix.</param>
    /// <returns>The parsed areas, or <see langword="null"/> when the value is invalid.</returns>
    /// <remarks>
    /// A <see langword="null"/> result is not cached, so an invalid value is
    /// retried if the same declaration is seen again. Variable-dependent values
    /// are not cached.
    /// </remarks>
    private GridTemplateAreas? ParseGridAreasCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.GridAreas.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = GridTemplateAreas.Parse(value);

        if (parsed is not null && !HasVariables(valueId))
        {
            _cache.GridAreas[valueId] = parsed;
        }

        return parsed;
    }

    /// <summary>
    /// Parses a <c>filter</c> or <c>backdrop-filter</c> function list, with caching.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <param name="currentColor">
    /// The element's colour, used by <c>drop-shadow()</c>.
    /// </param>
    /// <returns>The parsed filter chain, or <see cref="FilterSpec.None"/>.</returns>
    /// <remarks>
    /// Values containing <c>var()</c> or <c>currentcolor</c> are parsed on every
    /// use and never cached.
    /// </remarks>
    private FilterSpec ParseFilterCached(int valueId, string value, uint currentColor)
    {
        if (!HasVariables(valueId) && !HasCurrentColor(value)
            && _cache.Filters.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = ParseFilter(value, currentColor);

        if (!HasVariables(valueId) && !HasCurrentColor(value))
        {
            _cache.Filters[valueId] = parsed;
        }

        return parsed;
    }

    /// <summary>
    /// Parses a <c>clip-path</c> value, with caching.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <returns>The parsed clip path, or <see cref="ClipPathSpec.None"/>.</returns>
    /// <remarks>Variable-dependent values are not cached.</remarks>
    private ClipPathSpec ParseClipPathCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.ClipPaths.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = ParseClipPath(value);

        if (!HasVariables(valueId))
        {
            _cache.ClipPaths[valueId] = parsed;
        }

        return parsed;
    }

    /// <summary>
    /// Parses a <c>mask</c> shorthand, with caching.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <returns>The parsed mask, or <see cref="MaskSpec.None"/>.</returns>
    /// <remarks>Variable-dependent values are not cached.</remarks>
    private MaskSpec ParseMaskCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Masks.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = ParseMask(value);

        if (!HasVariables(valueId))
        {
            _cache.Masks[valueId] = parsed;
        }

        return parsed;
    }

    /// <summary>
    /// Parses a whitespace-separated chain of filter functions.
    /// </summary>
    /// <param name="value">The filter value.</param>
    /// <param name="currentColor">
    /// The element's colour, used as the fallback for <c>drop-shadow()</c>.
    /// </param>
    /// <returns>A filter chain, or <see cref="FilterSpec.None"/> for nothing.</returns>
    /// <remarks>
    /// Recognised functions are blur, brightness, contrast, grayscale, invert,
    /// opacity, saturate, sepia, hue-rotate, drop-shadow and url. Functions with
    /// no parentheses or unknown names are ignored. Blur distances are resolved
    /// against a default <see cref="LengthContext"/> (16px font, zero viewport),
    /// so percentages resolve to zero and viewport units vanish. Shadow colours
    /// are resolved against the <paramref name="currentColor"/> argument.
    /// </remarks>
    private static FilterSpec ParseFilter(string value, uint currentColor)
    {
        var text = value.Trim();

        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return FilterSpec.None;
        }

        var parts = CssValueParser.SplitTopLevel(text, ' ');
        var functions = new List<FilterFunction>(parts.Count);

        foreach (var raw in parts)
        {
            var part = raw.Trim();

            if (part.Length == 0)
            {
                continue;
            }

            var open = part.IndexOf('(');

            if (open <= 0 || !part.EndsWith(')'))
            {
                continue;
            }

            var name = part[..open].Trim().ToLowerInvariant();
            var args = part[(open + 1)..^1].Trim();
            var kind = name switch
            {
                "blur" => FilterFunctionKind.Blur,
                "brightness" => FilterFunctionKind.Brightness,
                "contrast" => FilterFunctionKind.Contrast,
                "grayscale" => FilterFunctionKind.Grayscale,
                "invert" => FilterFunctionKind.Invert,
                "opacity" => FilterFunctionKind.Opacity,
                "saturate" => FilterFunctionKind.Saturate,
                "sepia" => FilterFunctionKind.Sepia,
                "hue-rotate" => FilterFunctionKind.HueRotate,
                "drop-shadow" => FilterFunctionKind.DropShadow,
                "url" => FilterFunctionKind.Url,
                _ => (FilterFunctionKind)255
            };

            switch (kind)
            {
                case FilterFunctionKind.Blur:
                {
                    var length = CssValueParser.ParseLength(args);
                    functions.Add(new FilterFunction(kind, MathF.Max(0, length.Resolve(new LengthContext()))));
                    break;
                }

                case FilterFunctionKind.HueRotate:
                    functions.Add(new FilterFunction(kind, ParseAngle(args)));
                    break;

                case FilterFunctionKind.Brightness:
                case FilterFunctionKind.Contrast:
                case FilterFunctionKind.Grayscale:
                case FilterFunctionKind.Invert:
                case FilterFunctionKind.Opacity:
                case FilterFunctionKind.Saturate:
                case FilterFunctionKind.Sepia:
                    functions.Add(new FilterFunction(kind, ParseFilterAmount(args, 1)));
                    break;

                case FilterFunctionKind.DropShadow:
                    functions.Add(new FilterFunction(kind, 0, ParseShadow(args, currentColor)));
                    break;

                case FilterFunctionKind.Url:
                    functions.Add(new FilterFunction(kind, 0, default, args));
                    break;
            }
        }

        return functions.Count == 0 ? FilterSpec.None : new FilterSpec(functions.ToArray());
    }

    /// <summary>
    /// Parses the amount argument of a filter function.
    /// </summary>
    /// <param name="text">The argument text, with or without a trailing <c>%</c>.</param>
    /// <param name="fallback">The value to use when no number is present.</param>
    /// <returns>
    /// The amount as a factor: <c>50%</c> becomes 0.5, while a bare number is
    /// returned unchanged.
    /// </returns>
    /// <remarks>
    /// Percentage parsing scales by 0.01. The fallback is first expressed as a
    /// percentage so a missing argument keeps the caller's default factor.
    /// </remarks>
    private static float ParseFilterAmount(string text, float fallback)
    {
        text = text.Trim();

        if (text.Length == 0)
        {
            return fallback;
        }

        if (text.EndsWith('%'))
        {
            return CssValueParser.ParseNumber(text[..^1], fallback * 100) * 0.01f;
        }

        return CssValueParser.ParseNumber(text, fallback);
    }

    /// <summary>
    /// Parses a <c>clip-path</c> shape.
    /// </summary>
    /// <param name="value">The clip-path value.</param>
    /// <returns>
    /// A shape specification, or <see cref="ClipPathSpec.None"/> for
    /// <c>none</c>/empty input.
    /// </returns>
    /// <remarks>
    /// Supports <c>inset()</c>, <c>circle()</c>, <c>ellipse()</c>,
    /// <c>polygon()</c> and any other function treated as a URL reference. Box
    /// keywords (<c>margin-box</c>, <c>border-box</c>, and so on) collapse to an
    /// inset of zero because the painter already works in border-box space.
    /// Circle and ellipse centres default to 50% 50% when no <c>at</c> clause is
    /// present. Polygon fill-rule keywords are skipped.
    /// </remarks>
    private static ClipPathSpec ParseClipPath(string value)
    {
        var text = value.Trim();

        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return ClipPathSpec.None;
        }

        var open = text.IndexOf('(');

        if (open <= 0 || !text.EndsWith(')'))
        {
            // margin-box / border-box / padding-box / content-box all clip to
            // the box itself, which is the identity for our border-box scope.
            return new ClipPathSpec { Kind = ClipPathKind.Inset };
        }

        var name = text[..open].Trim().ToLowerInvariant();
        var args = text[(open + 1)..^1].Trim();

        switch (name)
        {
            case "inset":
            {
                var lengths = new List<CssLength>(4);
                var tokens = CssValueParser.SplitTopLevel(args, ' ');

                foreach (var token in tokens)
                {
                    if (token.Equals("round", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    lengths.Add(CssValueParser.ParseLength(token));
                }

                var top = lengths.Count > 0 ? lengths[0] : CssLength.Zero;
                var right = lengths.Count > 1 ? lengths[1] : top;
                var bottom = lengths.Count > 2 ? lengths[2] : top;
                var left = lengths.Count > 3 ? lengths[3] : right;

                return new ClipPathSpec
                {
                    Kind = ClipPathKind.Inset,
                    InsetTop = top,
                    InsetRight = right,
                    InsetBottom = bottom,
                    InsetLeft = left
                };
            }

            case "circle":
            {
                var (radius, center) = SplitAt(args);
                var radiusX = radius.Length > 0 && !radius.Equals("closest-side", StringComparison.OrdinalIgnoreCase)
                    ? CssValueParser.ParseLength(radius)
                    : CssLength.Percent(50);
                var centerX = CssLength.Percent(50);
                var centerY = CssLength.Percent(50);

                if (center.Length > 0)
                {
                    var centerParts = CssValueParser.SplitTopLevel(center, ' ');
                    centerX = centerParts.Count > 0 ? CssValueParser.ParseLength(centerParts[0]) : centerX;
                    centerY = centerParts.Count > 1 ? CssValueParser.ParseLength(centerParts[1]) : centerY;
                }

                return new ClipPathSpec
                {
                    Kind = ClipPathKind.Circle,
                    RadiusX = radiusX,
                    RadiusY = radiusX,
                    CenterX = centerX,
                    CenterY = centerY
                };
            }

            case "ellipse":
            {
                var (radius, center) = SplitAt(args);
                var radiusParts = CssValueParser.SplitTopLevel(radius, ' ');
                var radiusX = radiusParts.Count > 0 ? CssValueParser.ParseLength(radiusParts[0]) : CssLength.Percent(50);
                var radiusY = radiusParts.Count > 1 ? CssValueParser.ParseLength(radiusParts[1]) : radiusX;
                var centerX = CssLength.Percent(50);
                var centerY = CssLength.Percent(50);

                if (center.Length > 0)
                {
                    var centerParts = CssValueParser.SplitTopLevel(center, ' ');
                    centerX = centerParts.Count > 0 ? CssValueParser.ParseLength(centerParts[0]) : centerX;
                    centerY = centerParts.Count > 1 ? CssValueParser.ParseLength(centerParts[1]) : centerY;
                }

                return new ClipPathSpec
                {
                    Kind = ClipPathKind.Ellipse,
                    RadiusX = radiusX,
                    RadiusY = radiusY,
                    CenterX = centerX,
                    CenterY = centerY
                };
            }

            case "polygon":
            {
                var parts = CssValueParser.SplitTopLevel(args, ',');
                var points = new List<ClipPathPoint>(parts.Count);

                foreach (var raw in parts)
                {
                    var point = raw.Trim();

                    if (point.Length == 0)
                    {
                        continue;
                    }

                    if (point.Equals("nonzero", StringComparison.OrdinalIgnoreCase)
                        || point.Equals("evenodd", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var coords = CssValueParser.SplitTopLevel(point, ' ');

                    if (coords.Count >= 2)
                    {
                        points.Add(new ClipPathPoint(
                            CssValueParser.ParseLength(coords[0]),
                            CssValueParser.ParseLength(coords[1])));
                    }
                }

                return new ClipPathSpec
                {
                    Kind = ClipPathKind.Polygon,
                    Points = points.ToArray()
                };
            }

            default:
                return new ClipPathSpec
                {
                    Kind = ClipPathKind.Url,
                    Url = args
                };
        }
    }

    /// <summary>
    /// Splits a shape argument at the <c>at</c> keyword used by circle, ellipse
    /// and radial gradients.
    /// </summary>
    /// <param name="text">The argument text.</param>
    /// <returns>
    /// The radius/position text before <c>at</c> and the centre text after it.
    /// When there is no <c>at</c>, the whole text is the radius and the centre is
    /// empty.
    /// </returns>
    /// <remarks>The match is case-insensitive and requires surrounding spaces.</remarks>
    private static (string Radius, string Center) SplitAt(string text)
    {
        var index = text.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);

        return index < 0
            ? (text.Trim(), string.Empty)
            : (text[..index].Trim(), text[(index + 4)..].Trim());
    }

    /// <summary>
    /// Parses a simplified <c>mask</c> shorthand.
    /// </summary>
    /// <param name="value">The mask value.</param>
    /// <returns>
    /// The mask source, mode and repeat, or <see cref="MaskSpec.None"/> when no
    /// image source is present.
    /// </returns>
    /// <remarks>
    /// Only a single <c>url()</c> or gradient layer is represented. The parser
    /// recognises <c>alpha</c>, <c>luminance</c>, <c>match-source</c> and the
    /// repeat keywords; other tokens are ignored rather than rejected. If no
    /// source is found the entire shorthand is discarded.
    /// </remarks>
    private static MaskSpec ParseMask(string value)
    {
        var text = value.Trim();

        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return MaskSpec.None;
        }

        var source = text;
        var kind = ImageKind.None;
        var mode = MaskMode.MatchSource;
        var repeat = BackgroundRepeat.Repeat;

        var tokens = CssValueParser.SplitTopLevel(text, ' ');

        foreach (var raw in tokens)
        {
            var token = raw.Trim();
            var lower = token.ToLowerInvariant();

            if (token.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            {
                source = token.Length > 5 && token.EndsWith(')') ? token[4..^1] : token;
                kind = ImageKind.Url;
            }
            else if (lower.Contains("gradient(", StringComparison.Ordinal))
            {
                source = token;
                kind = lower.Contains("radial", StringComparison.Ordinal)
                    ? ImageKind.RadialGradient
                    : lower.Contains("conic", StringComparison.Ordinal)
                        ? ImageKind.ConicGradient
                        : ImageKind.LinearGradient;
            }
            else if (lower is "alpha" or "luminance" or "match-source")
            {
                mode = lower switch
                {
                    "alpha" => MaskMode.Alpha,
                    "luminance" => MaskMode.Luminance,
                    _ => MaskMode.MatchSource
                };
            }
            else if (lower is "repeat-x")
            {
                repeat = BackgroundRepeat.RepeatX;
            }
            else if (lower is "repeat-y")
            {
                repeat = BackgroundRepeat.RepeatY;
            }
            else if (lower is "no-repeat")
            {
                repeat = BackgroundRepeat.NoRepeat;
            }
        }

        return kind == ImageKind.None
            ? MaskSpec.None
            : new MaskSpec
            {
                Kind = kind,
                Source = source,
                Mode = mode,
                Repeat = repeat
            };
    }

    /// <summary>
    /// Parses comma-separated <c>box-shadow</c> layers, with caching.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <param name="currentColor">
    /// The element's colour, used by shadows that omit a colour.
    /// </param>
    /// <returns>
    /// One <see cref="ShadowSpec"/> per layer, or an empty array for <c>none</c>.
    /// </returns>
    /// <remarks>
    /// The returned array is shared through the cache and must be treated as
    /// immutable by callers. Values containing <c>var()</c> or
    /// <c>currentcolor</c> are not cached.
    /// </remarks>
    private ShadowSpec[] ParseShadowsCached(int valueId, string value, uint currentColor)
    {
        if (!HasVariables(valueId) && !HasCurrentColor(value)
            && _cache.Shadows.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var layers = CssValueParser.SplitTopLevel(value, ',');
        var shadows = new ShadowSpec[layers.Count];

        for (var i = 0; i < layers.Count; i++)
        {
            shadows[i] = ParseShadow(layers[i], currentColor);
        }

        if (!HasVariables(valueId) && !HasCurrentColor(value))
        {
            _cache.Shadows[valueId] = shadows;
        }

        return shadows;
    }

    /// <summary>
    /// Parses one shadow layer.
    /// </summary>
    /// <param name="value">A single layer without top-level commas.</param>
    /// <param name="currentColor">The colour to use when none is specified.</param>
    /// <returns>
    /// The resolved offset-x, offset-y, blur, spread, colour and inset flag.
    /// </returns>
    /// <remarks>
    /// Lengths are resolved with a default <see cref="LengthContext"/>, so
    /// percentages become zero; omitted lengths are zero. The <c>inset</c>
    /// keyword is accepted in any position, and the last colour wins.
    /// </remarks>
    private static ShadowSpec ParseShadow(string value, uint currentColor)
    {
        var parts = CssValueParser.SplitTopLevel(value, ' ');
        var inset = false;
        var color = currentColor;
        var lengths = new List<CssLength>(4);

        foreach (var part in parts)
        {
            if (part.Equals("inset", StringComparison.OrdinalIgnoreCase))
            {
                inset = true;
                continue;
            }

            if (CssColor.TryParse(part, currentColor, out var parsedColor))
            {
                color = parsedColor;
                continue;
            }

            lengths.Add(CssValueParser.ParseLength(part));
        }

        var context = new LengthContext();

        return new ShadowSpec(
            lengths.Count > 0 ? lengths[0].Resolve(in context) : 0,
            lengths.Count > 1 ? lengths[1].Resolve(in context) : 0,
            lengths.Count > 2 ? lengths[2].Resolve(in context) : 0,
            lengths.Count > 3 ? lengths[3].Resolve(in context) : 0,
            color,
            inset);
    }

    /// <summary>
    /// Parses <c>background-image</c> layers, with caching.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <param name="currentColor">
    /// The element's colour, used by gradient stops that omit a colour.
    /// </param>
    /// <returns>
    /// One spec per valid layer, or an empty array for <c>none</c>.
    /// </returns>
    /// <remarks>
    /// The returned array is shared through the cache and must be treated as
    /// immutable. Values containing <c>var()</c> or <c>currentcolor</c> are not
    /// cached.
    /// </remarks>
    private BackgroundImageSpec[] ParseBackgroundsCached(int valueId, string value, uint currentColor)
    {
        if (!HasVariables(valueId) && !HasCurrentColor(value)
            && _cache.Backgrounds.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var result = ParseBackgroundLayers(value, currentColor);

        if (!HasVariables(valueId) && !HasCurrentColor(value))
        {
            _cache.Backgrounds[valueId] = result;
        }

        return result;
    }

    /// <summary>
    /// Splits a background-image value into comma-separated layers and parses
    /// each one.
    /// </summary>
    /// <param name="value">The background-image value.</param>
    /// <param name="currentColor">The colour used by gradient stops.</param>
    /// <returns>The valid layers in source order, or an empty array.</returns>
    /// <remarks>
    /// Layers that contain no image (for example a bare colour) are skipped
    /// rather than producing a placeholder.
    /// </remarks>
    private static BackgroundImageSpec[] ParseBackgroundLayers(string value, uint currentColor)
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var layers = CssValueParser.SplitTopLevel(value, ',');
        var backgrounds = new List<BackgroundImageSpec>(layers.Count);

        foreach (var layer in layers)
        {
            var parsed = ParseBackgroundLayer(layer.Trim(), currentColor);

            if (parsed is not null)
            {
                backgrounds.Add(parsed);
            }
        }

        return backgrounds.Count == 0 ? [] : backgrounds.ToArray();
    }

    /// <summary>
    /// Parses one background-image layer.
    /// </summary>
    /// <param name="value">A single layer without top-level commas.</param>
    /// <param name="currentColor">The colour used by gradient stops.</param>
    /// <returns>
    /// A URL or gradient spec, or <see langword="null"/> for <c>none</c> and
    /// values that are not a recognised image.
    /// </returns>
    /// <remarks>
    /// The three gradient kinds are recognised by substring, so a
    /// <c>linear-gradient()</c> nested inside another function is still found.
    /// </remarks>
    private static BackgroundImageSpec? ParseBackgroundLayer(string value, uint currentColor)
    {
        if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var close = FindMatchingParenthesis(value, 3);

            if (close < 0)
            {
                return null;
            }

            var source = CssValueParser.Unquote(value[4..close].Trim());
            return new BackgroundImageSpec { Kind = ImageKind.Url, Url = source };
        }

        if (value.Contains("linear-gradient(", StringComparison.OrdinalIgnoreCase))
        {
            return ParseGradient(value, GradientKind.Linear, currentColor);
        }

        if (value.Contains("radial-gradient(", StringComparison.OrdinalIgnoreCase))
        {
            return ParseGradient(value, GradientKind.Radial, currentColor);
        }

        if (value.Contains("conic-gradient(", StringComparison.OrdinalIgnoreCase))
        {
            return ParseGradient(value, GradientKind.Conic, currentColor);
        }

        return null;
    }

    /// <summary>
    /// Parses a linear, radial or conic gradient.
    /// </summary>
    /// <param name="value">The complete gradient function text.</param>
    /// <param name="kind">Which gradient grammar to apply.</param>
    /// <param name="currentColor">The colour used by stops without one.</param>
    /// <returns>The gradient spec, or <see langword="null"/> for malformed text.</returns>
    /// <remarks>
    /// <para>
    /// The first argument is treated as the direction/position only when it does
    /// not itself look like a colour stop. Linear gradients default to 180
    /// degrees (to bottom); radial and conic gradients default to a centred
    /// 50% radius.
    /// </para>
    /// <para>
    /// Stop offsets are normalised by <see cref="AssignStopOffsets"/> after
    /// parsing. Absolute positions are kept as <see cref="float.NaN"/> for the
    /// painter to distribute against the actual gradient box.
    /// </para>
    /// </remarks>
    private static BackgroundImageSpec? ParseGradient(string value, GradientKind kind, uint currentColor)
    {
        var open = value.IndexOf('(');
        var close = value.LastIndexOf(')');

        if (open < 0 || close <= open)
        {
            return null;
        }

        var inner = value[(open + 1)..close];
        var parts = CssValueParser.SplitTopLevel(inner, ',');
        var stops = new List<GradientStop>(parts.Count);
        var angle = kind == GradientKind.Linear ? 180f : 0f;
        var center = new Point(0.5f, 0.5f);
        var radius = new Size(0.5f, 0.5f);
        var startIndex = 0;

        if (parts.Count > 0)
        {
            var first = parts[0].Trim();

            if (kind == GradientKind.Linear && !LooksLikeColorStop(first))
            {
                angle = ParseGradientAngle(first, angle);
                startIndex = 1;
            }
            else if (kind is GradientKind.Radial or GradientKind.Conic && !LooksLikeColorStop(first))
            {
                ParseGradientPosition(first, kind, ref center, ref radius);
                startIndex = 1;
            }
        }

        for (var i = startIndex; i < parts.Count; i++)
        {
            if (TryParseGradientStop(parts[i], currentColor, out var stop))
            {
                stops.Add(stop);
            }
        }

        AssignStopOffsets(stops);

        return new BackgroundImageSpec
        {
            Kind = kind switch
            {
                GradientKind.Linear => ImageKind.LinearGradient,
                GradientKind.Radial => ImageKind.RadialGradient,
                _ => ImageKind.ConicGradient
            },
            GradientKind = kind,
            Angle = angle,
            Center = center,
            Radius = radius,
            Stops = stops.ToArray()
        };
    }

    /// <summary>
    /// Determines whether a gradient argument begins with a colour stop rather
    /// than a direction or position.
    /// </summary>
    /// <param name="value">The first comma-separated gradient argument.</param>
    /// <returns><see langword="true"/> when it looks like a stop.</returns>
    /// <remarks>
    /// Named colours, hex colours, <c>rgb()</c>/<c>hsl()</c> functions and a stop
    /// carrying a position such as <c>red 10%</c> are recognised. Only the first
    /// token is inspected, which is enough to distinguish a stop from
    /// <c>to right</c> or <c>circle at center</c>.
    /// </remarks>
    private static bool LooksLikeColorStop(string value)
    {
        if (CssColor.TryParse(value, ComputedStyle.CurrentColor, out _))
        {
            return true;
        }

        if (value.StartsWith('#')
            || value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("hsl(", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("hsla(", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A stop may carry a position: "red 10%". Check just the first token.
        var tokens = CssValueTokenizer.Tokenize(value);

        if (tokens.Length == 0)
        {
            return false;
        }

        var first = tokens[0];

        if (first.Kind == CssValueTokenKind.Function)
        {
            var name = CssValueTokenizer.GetSpan(value, in first);
            return name.Equals("rgb", StringComparison.OrdinalIgnoreCase)
                || name.Equals("rgba", StringComparison.OrdinalIgnoreCase)
                || name.Equals("hsl", StringComparison.OrdinalIgnoreCase)
                || name.Equals("hsla", StringComparison.OrdinalIgnoreCase);
        }

        return first.Kind == CssValueTokenKind.Word
            && CssColor.TryParse(CssValueTokenizer.GetText(value, first), ComputedStyle.CurrentColor, out _);
    }

    /// <summary>
    /// Parses one gradient colour stop.
    /// </summary>
    /// <param name="value">The stop text, optionally ending in a position.</param>
    /// <param name="currentColor">The colour used by <c>currentcolor</c>.</param>
    /// <param name="stop">
    /// Receives the stop on success. Its offset is <see cref="float.NaN"/> when
    /// no position was given.
    /// </param>
    /// <returns><see langword="true"/> when a colour was found.</returns>
    /// <remarks>
    /// Two forms are accepted: a bare colour, or a colour followed by a position
    /// after the final space. The latter split keeps multi-word colour functions
    /// such as <c>hsl(0 100% 50%)</c> intact.
    /// </remarks>
    private static bool TryParseGradientStop(string value, uint currentColor, out GradientStop stop)
    {
        stop = default;
        var text = value.Trim();

        if (CssColor.TryParse(text, currentColor, out var color))
        {
            stop = new GradientStop(float.NaN, color);
            return true;
        }

        // Split the position from the colour. A colour may itself contain spaces
        // (hsl(0 100% 50%)), so try progressively shorter prefixes.
        var space = text.LastIndexOf(' ');

        if (space > 0)
        {
            var colorPart = text[..space].Trim();
            var positionPart = text[(space + 1)..].Trim();

            if (CssColor.TryParse(colorPart, currentColor, out color))
            {
                var position = ParseStopOffset(positionPart);
                stop = new GradientStop(position, color);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parses a gradient stop position into a normalised offset.
    /// </summary>
    /// <param name="value">A percentage or angle, or another length.</param>
    /// <returns>
    /// A value in the range 0 to 1 for percentages and degrees, or
    /// <see cref="float.NaN"/> for absolute lengths that need the gradient box.
    /// </returns>
    /// <remarks>
    /// A degree value is divided by 360, matching the painter's normalised
    /// gradient axis. Absolute positions deliberately stay unresolved.
    /// </remarks>
    private static float ParseStopOffset(string value)
    {
        var text = value.Trim();

        if (text.EndsWith('%'))
        {
            return Math.Clamp(CssValueParser.ParseNumber(text[..^1]) / 100f, 0, 1);
        }

        if (text.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Clamp(CssValueParser.ParseNumber(text[..^3]) / 360f, 0, 1);
        }

        // Absolute stop positions need the gradient box; keep them unresolved
        // as a NaN so the painter can distribute them proportionally.
        return float.NaN;
    }

    /// <summary>
    /// Fills in unspecified gradient stop offsets.
    /// </summary>
    /// <param name="stops">The stops to update in place.</param>
    /// <remarks>
    /// The first stop defaults to 0 and the last to 1; for a single unpositioned
    /// stop the last rule wins. Any run of unpositioned stops between two known
    /// offsets is spaced evenly, so <c>red, green, blue 80%</c> places green at
    /// 40%. A stop's <see cref="float.NaN"/> offset is the marker for
    /// "not specified".
    /// </remarks>
    private static void AssignStopOffsets(List<GradientStop> stops)
    {
        if (stops.Count == 0)
        {
            return;
        }

        if (float.IsNaN(stops[0].Offset))
        {
            stops[0] = stops[0] with { Offset = 0 };
        }

        if (float.IsNaN(stops[^1].Offset))
        {
            stops[^1] = stops[^1] with { Offset = 1 };
        }

        var lastKnown = 0;

        for (var i = 1; i < stops.Count; i++)
        {
            if (!float.IsNaN(stops[i].Offset))
            {
                if (i - lastKnown > 1)
                {
                    var start = stops[lastKnown].Offset;
                    var end = stops[i].Offset;
                    var step = (end - start) / (i - lastKnown);

                    for (var j = lastKnown + 1; j < i; j++)
                    {
                        stops[j] = stops[j] with { Offset = start + step * (j - lastKnown) };
                    }
                }

                lastKnown = i;
            }
        }
    }

    /// <summary>
    /// Parses a linear-gradient direction into degrees.
    /// </summary>
    /// <param name="value">
    /// An angle with a unit, a <c>to &lt;side&gt;</c> form, or a bare number.
    /// </param>
    /// <param name="fallback">The value used for an unrecognised direction.</param>
    /// <returns>The direction in degrees, clockwise from <c>to top</c>.</returns>
    /// <remarks>
    /// Supports <c>deg</c>, <c>turn</c> and <c>rad</c>, plus the eight
    /// <c>to &lt;side&gt;</c> keywords. A bare number is treated as degrees.
    /// </remarks>
    private static float ParseGradientAngle(string value, float fallback)
    {
        var text = value.Trim().ToLowerInvariant();

        if (text.EndsWith("deg", StringComparison.Ordinal))
        {
            return CssValueParser.ParseNumber(text[..^3]);
        }

        if (text.EndsWith("turn", StringComparison.Ordinal))
        {
            return CssValueParser.ParseNumber(text[..^4]) * 360f;
        }

        if (text.EndsWith("rad", StringComparison.Ordinal))
        {
            return CssValueParser.ParseNumber(text[..^3]) * (180f / MathF.PI);
        }

        if (text.StartsWith("to ", StringComparison.Ordinal))
        {
            return text switch
            {
                "to top" => 0,
                "to top right" or "to right top" => 45,
                "to right" => 90,
                "to bottom right" or "to right bottom" => 135,
                "to bottom" => 180,
                "to bottom left" or "to left bottom" => 225,
                "to left" => 270,
                "to top left" or "to left top" => 315,
                _ => fallback
            };
        }

        return CssValueParser.ParseNumber(text, fallback);
    }

    /// <summary>
    /// Parses the position and size part of a radial or conic gradient.
    /// </summary>
    /// <param name="value">The first gradient argument when it is not a colour stop.</param>
    /// <param name="kind">The gradient kind.</param>
    /// <param name="center">The centre, updated when an <c>at</c> clause is present.</param>
    /// <param name="radius">The normalised radii, updated for <c>circle</c>.</param>
    /// <remarks>
    /// Centres are normalised to the 0-1 box. For <c>circle</c>, a percentage
    /// diameter sets both radii; a keyword diameter leaves the existing 50%
    /// default. Ellipse keywords other than <c>circle</c> are ignored.
    /// </remarks>
    private static void ParseGradientPosition(
        string value,
        GradientKind kind,
        ref Point center,
        ref Size radius)
    {
        var text = value.Trim().ToLowerInvariant();
        var at = text.IndexOf(" at ", StringComparison.Ordinal);

        if (at >= 0)
        {
            var centerText = text[(at + 4)..].Trim();
            center = ParsePositionPair(centerText, center);
            text = text[..at].Trim();
        }

        if (kind == GradientKind.Radial && text.StartsWith("circle", StringComparison.Ordinal))
        {
            var parts = CssValueParser.SplitTopLevel(text, ' ');
            var diameter = parts.Count > 1 ? CssValueParser.ParseLength(parts[1]) : CssLength.Percent(50);
            var amount = diameter.IsPercent ? diameter.Value / 100f : 0.5f;
            radius = new Size(amount, amount);
        }
    }

    /// <summary>
    /// Parses a two-component position such as <c>left top</c> into a normalised
    /// point.
    /// </summary>
    /// <param name="value">The position text.</param>
    /// <param name="fallback">The point used for missing or unrecognised components.</param>
    /// <returns>The normalised x/y point.</returns>
    /// <remarks>
    /// A single component sets x and leaves y at the fallback value.
    /// </remarks>
    private static Point ParsePositionPair(string value, Point fallback)
    {
        var parts = CssValueParser.SplitTopLevel(value, ' ');

        if (parts.Count == 0)
        {
            return fallback;
        }

        var x = ParsePositionComponent(parts[0], horizontal: true, fallback.X);
        var y = parts.Count > 1 ? ParsePositionComponent(parts[1], horizontal: false, fallback.Y) : fallback.Y;
        return new Point(x, y);
    }

    /// <summary>
    /// Parses one x or y position component into a normalised fraction.
    /// </summary>
    /// <param name="value">The component text.</param>
    /// <param name="horizontal">
    /// Indicates the x axis. The flag documents intent; the keyword table is
    /// shared, so <c>left</c>/<c>top</c> both map to 0 and
    /// <c>right</c>/<c>bottom</c> both map to 1.
    /// </param>
    /// <param name="fallback">The value used for unrecognised components.</param>
    /// <returns>A fraction in the range 0 to 1.</returns>
    /// <remarks>
    /// <c>center</c> is 0.5. Percentages are clamped; other lengths are not
    /// supported and return the fallback.
    /// </remarks>
    private static float ParsePositionComponent(string value, bool horizontal, float fallback)
    {
        var text = value.Trim().ToLowerInvariant();

        return text switch
        {
            "left" or "top" => 0,
            "center" => 0.5f,
            "right" or "bottom" => 1,
            _ => text.EndsWith('%')
                ? Math.Clamp(CssValueParser.ParseNumber(text[..^1]) / 100f, 0, 1)
                : fallback
        };
    }

    /// <summary>
    /// Parses a <c>transform</c> function list, with caching.
    /// </summary>
    /// <param name="valueId">The interned value id, used as the cache key.</param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <returns>The parsed transform, or <see langword="null"/> for <c>none</c>.</returns>
    /// <remarks>
    /// The cache stores a <c>(HasTransform, Transform)</c> pair so a
    /// <see langword="null"/> result is also memoised. Variable-dependent values
    /// are not cached.
    /// </remarks>
    private TransformSpec? ParseTransformCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Transforms.TryGetValue(valueId, out var cached))
        {
            return cached.Transform;
        }

        var parsed = ParseTransform(value);

        if (!HasVariables(valueId))
        {
            _cache.Transforms[valueId] = (parsed is not null, parsed);
        }

        return parsed;
    }

    /// <summary>
    /// Parses a <c>transform</c> value into an ordered operation list.
    /// </summary>
    /// <param name="value">The transform value.</param>
    /// <returns>The operations in source order, or <see langword="null"/> when
    /// the value is <c>none</c> or contains no recognised function.</returns>
    /// <remarks>
    /// <para>
    /// Functions are applied in the order written. Supported functions are
    /// <c>translate</c>/<c>translateX</c>/<c>translateY</c>,
    /// <c>scale</c>/<c>scaleX</c>/<c>scaleY</c>,
    /// <c>rotate</c>/<c>rotateZ</c>, <c>skew</c> and <c>matrix</c>.
    /// Unsupported functions and non-function tokens are ignored, which keeps
    /// the list usable for 2D layout rather than rejecting the whole value.
    /// </para>
    /// <para>
    /// Arguments are split on commas when there is more than one; otherwise they
    /// are split on whitespace, so both <c>translate(1px, 2px)</c> and
    /// <c>translate(1px 2px)</c> work. A <c>matrix</c> needs at least six
    /// arguments and is otherwise ignored.
    /// </para>
    /// </remarks>
    internal static TransformSpec? ParseTransform(string value)
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var source = value;
        var tokens = CssValueTokenizer.Tokenize(source);

        if (tokens.Length == 0)
        {
            return null;
        }

        var operations = new List<TransformOperation>(tokens.Length);
        var index = 0;

        while (index < tokens.Length)
        {
            var token = tokens[index];

            if (token.Kind != CssValueTokenKind.Function)
            {
                index++;
                continue;
            }

            var name = CssValueTokenizer.GetText(source, token);
            var open = token.Start + token.Length;
            var close = FindMatchingParenthesis(source, open);
            var arguments = close < 0 ? string.Empty : source[(open + 1)..close];
            index++;

            while (index < tokens.Length && tokens[index].Start <= close)
            {
                index++;
            }

            var parts = CssValueParser.SplitTopLevel(arguments, ',');

            if (parts.Count == 1)
            {
                parts = CssValueParser.SplitTopLevel(arguments, ' ');
            }

            switch (name.ToLowerInvariant())
            {
                case "translate":
                    operations.Add(new TransformOperation(
                        TransformKind.Translate,
                        CssValueParser.ParseLength(parts[0]),
                        parts.Count > 1 ? CssValueParser.ParseLength(parts[1]) : CssLength.Zero,
                        0,
                        Matrix3x2.Identity));
                    break;

                case "translatex":
                    operations.Add(new TransformOperation(
                        TransformKind.Translate,
                        CssValueParser.ParseLength(parts[0]),
                        CssLength.Zero,
                        0,
                        Matrix3x2.Identity));
                    break;

                case "translatey":
                    operations.Add(new TransformOperation(
                        TransformKind.Translate,
                        CssLength.Zero,
                        CssValueParser.ParseLength(parts[0]),
                        0,
                        Matrix3x2.Identity));
                    break;

                case "scale":
                {
                    var x = CssValueParser.ParseLength(parts[0]);

                    operations.Add(new TransformOperation(
                        TransformKind.Scale,
                        x,
                        parts.Count > 1 ? CssValueParser.ParseLength(parts[1]) : CssLength.Auto,
                        0,
                        Matrix3x2.Identity));
                    break;
                }

                case "scalex":
                    operations.Add(new TransformOperation(
                        TransformKind.Scale,
                        CssValueParser.ParseLength(parts[0]),
                        CssLength.Auto,
                        0,
                        Matrix3x2.Identity));
                    break;

                case "scaley":
                    operations.Add(new TransformOperation(
                        TransformKind.Scale,
                        CssLength.Number(1),
                        CssValueParser.ParseLength(parts[0]),
                        0,
                        Matrix3x2.Identity));
                    break;

                case "rotate":
                case "rotatez":
                    operations.Add(new TransformOperation(
                        TransformKind.Rotate,
                        CssLength.Zero,
                        CssLength.Zero,
                        ParseAngle(parts[0]),
                        Matrix3x2.Identity));
                    break;

                case "skew":
                    operations.Add(new TransformOperation(
                        TransformKind.Skew,
                        CssLength.Zero,
                        parts.Count > 1 ? CssLength.Number(ParseAngle(parts[1])) : CssLength.Zero,
                        ParseAngle(parts[0]),
                        Matrix3x2.Identity));
                    break;

                case "matrix":
                    if (parts.Count >= 6)
                    {
                        operations.Add(new TransformOperation(
                            TransformKind.Matrix,
                            CssLength.Zero,
                            CssLength.Zero,
                            0,
                            new Matrix3x2(
                                CssValueParser.ParseNumber(parts[0]),
                                CssValueParser.ParseNumber(parts[1]),
                                CssValueParser.ParseNumber(parts[2]),
                                CssValueParser.ParseNumber(parts[3]),
                                CssValueParser.ParseNumber(parts[4]),
                                CssValueParser.ParseNumber(parts[5]))));
                    }

                    break;
            }
        }

        return operations.Count == 0 ? null : new TransformSpec(operations.ToArray());
    }

    /// <summary>
    /// Parses a CSS angle into degrees.
    /// </summary>
    /// <param name="value">An angle with an optional unit.</param>
    /// <returns>The angle in degrees; a unitless value is already degrees.</returns>
    /// <remarks>
    /// Supports <c>deg</c>, <c>rad</c> and <c>turn</c>. Gradians are not
    /// recognised and fall through to number parsing.
    /// </remarks>
    private static float ParseAngle(string value)
    {
        var text = value.Trim().ToLowerInvariant();

        if (text.EndsWith("deg", StringComparison.Ordinal))
        {
            return CssValueParser.ParseNumber(text[..^3]);
        }

        if (text.EndsWith("rad", StringComparison.Ordinal))
        {
            return CssValueParser.ParseNumber(text[..^3]) * (180f / MathF.PI);
        }

        if (text.EndsWith("turn", StringComparison.Ordinal))
        {
            return CssValueParser.ParseNumber(text[..^4]) * 360f;
        }

        return CssValueParser.ParseNumber(text);
    }

    /// <summary>
    /// Parses an <c>aspect-ratio</c> value into width divided by height.
    /// </summary>
    /// <param name="value">A ratio such as <c>16 / 9</c> or a single number.</param>
    /// <returns>
    /// The ratio, or <see langword="null"/> for <c>auto</c>, a zero denominator,
    /// a non-positive value, or unparsable text.
    /// </returns>
    /// <remarks>
    /// Only the first two components of the slash form are used; <c>auto</c>
    /// before or after a ratio is not represented.
    /// </remarks>
    private static float? ParseAspectRatio(string value)
    {
        var parts = CssValueParser.SplitTopLevel(value, '/');

        if (parts.Count >= 2)
        {
            var width = CssValueParser.ParseNumber(parts[0]);
            var height = CssValueParser.ParseNumber(parts[1]);
            return height == 0 ? null : width / height;
        }

        var ratio = CssValueParser.ParseNumber(value, float.NaN);
        return float.IsNaN(ratio) || ratio <= 0 ? null : ratio;
    }
}
