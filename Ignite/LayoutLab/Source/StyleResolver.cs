using System.Globalization;
using Ignite.Data;
using Ignite.Data.Dom;
using Ignite.Parser;

namespace Ignite.LayoutLab;

/// <summary>
/// Resolves the rules that matched a node into a <see cref="ComputedStyle"/>.
/// </summary>
/// <remarks>
/// Declarations are applied weakest-first, exactly as
/// <see cref="DataTable.MatchRules(IgniteSupportedDomNode, RendererContext)"/>
/// returns them. Important declarations get a
/// second pass so they beat every normal declaration without allocating a
/// per-property cascade map. Values that need a containing block stay as
/// <see cref="CssLength"/> and are resolved by the layout pass.
/// <para>
/// A resolver is cheap and frame-scoped, but it writes into the supplied
/// <see cref="StyleValueCache"/>. It is not thread-safe and must not be used
/// concurrently with another resolver sharing the same cache. The
/// <see cref="DataTable"/> it reads is expected to be immutable while resolving.
/// </para>
/// </remarks>
/// <seealso cref="ComputedStyle"/>
/// <seealso cref="StyleValueCache"/>
/// <seealso cref="CssProperty"/>
public sealed partial class StyleResolver
{
    /// <summary>The compiled stylesheet that supplies the rules being cascaded.</summary>
    /// <remarks>
    /// The resolver only reads from the table; it is owned by the caller and must
    /// outlive the resolver.
    /// </remarks>
    private readonly DataTable _table;
    /// <summary>Shared cache of parsed declaration values, keyed by interned value id.</summary>
    /// <remarks>
    /// The cache is mutable and is not thread-safe. It is normally owned by
    /// <see cref="LayoutEngine"/> and survives across layout passes until
    /// <see cref="StyleValueCache.Clear"/> is called.
    /// </remarks>
    private readonly StyleValueCache _cache;
    /// <summary>The frame options used for the default font size and root length context.</summary>
    private readonly LayoutOptions _options;
    /// <summary>
    /// The root <see cref="LengthContext"/> captured when the resolver was created.
    /// </summary>
    /// <remarks>
    /// It is the fallback context for font-size resolution, including the
    /// <c>rem</c> basis and the case with no parent. It is a snapshot, so changing
    /// <see cref="LayoutOptions.ViewportWidth"/> or
    /// <see cref="LayoutOptions.RootFontSize"/> afterwards does not affect this
    /// instance; create a new resolver for a new frame.
    /// </remarks>
    private readonly LengthContext _baseContext;

    /// <summary>
    /// Creates a resolver over a compiled stylesheet, a value cache and the
    /// options for the frame being laid out.
    /// </summary>
    /// <param name="table">The compiled rules to cascade. The resolver does not own it.</param>
    /// <param name="cache">
    /// The parsed-value cache to populate. Pass the engine's cache so repeated
    /// values are parsed once; the resolver does not own it.
    /// </param>
    /// <param name="options">
    /// The frame options. They supply <see cref="LayoutOptions.DefaultFontSize"/>
    /// and the root length context captured by <see cref="_baseContext"/>.
    /// </param>
    /// <exception cref="NullReferenceException">
    /// Thrown when <paramref name="options"/> is <see langword="null"/>, because
    /// the constructor immediately reads its font and viewport settings.
    /// </exception>
    /// <remarks>
    /// <paramref name="table"/> and <paramref name="cache"/> are stored without
    /// validation; a <see langword="null"/> value fails when first used.
    /// </remarks>
    public StyleResolver(DataTable table, StyleValueCache cache, LayoutOptions options)
    {
        _table = table;
        _cache = cache;
        _options = options;
        _baseContext = options.CreateRootLengthContext();
    }

    /// <summary>Gets the parsed-value cache this resolver reads and writes.</summary>
    /// <value>
    /// The same instance passed to the constructor. It accumulates entries for
    /// every distinct interned declaration value seen so far.
    /// </value>
    /// <remarks>
    /// Exposed so a host can inspect the cache or clear it between documents.
    /// The cache is not thread-safe; do not share it between concurrent
    /// resolvers without external synchronisation.
    /// </remarks>
    public StyleValueCache Cache => _cache;

    /// <summary>
    /// Resolves the matched declarations for one node into a new
    /// <see cref="ComputedStyle"/>.
    /// </summary>
    /// <param name="node">
    /// The node being styled. Text nodes and fragments receive framework display
    /// overrides after the cascade.
    /// </param>
    /// <param name="parent">
    /// The parent's computed style, or <see langword="null"/> for a root node.
    /// Inherited properties and custom properties are read from it.
    /// </param>
    /// <param name="matches">
    /// The rules that matched the node, ordered weakest first exactly as
    /// <see cref="DataTable.MatchRules(IgniteSupportedDomNode, RendererContext)"/>
    /// returns them.
    /// </param>
    /// <returns>A fully populated style owned by the caller.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="node"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// var matches = table.MatchRules(node, context);
    /// var style = resolver.Resolve(node, parentStyle, matches);
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// The pipeline is deliberate and order-sensitive:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// Inherit the scalar properties (colour, font, writing mode, and so on) from
    /// <paramref name="parent"/> and link the style parent for variable lookup.
    /// </description></item>
    /// <item><description>
    /// Collect custom properties in two passes (normal, then important) and
    /// recompute <see cref="ComputedStyle.CustomPropertyHash"/> so resolved values
    /// can be cached by variable environment.
    /// </description></item>
    /// <item><description>
    /// Apply normal declarations, then important declarations. The second pass
    /// lets <c>!important</c> win regardless of source position without building
    /// a per-property cascade map.
    /// </description></item>
    /// <item><description>
    /// Force <see cref="Display.Inline"/> for text nodes and
    /// <see cref="Display.Contents"/> for fragments, which are framework
    /// constructs rather than CSS elements.
    /// </description></item>
    /// </list>
    /// <para>
    /// Because <paramref name="matches"/> is applied in the supplied order,
    /// passing an unsorted sequence changes which declaration wins. The span is
    /// only read for the duration of the call.
    /// </para>
    /// <para>
    /// Relative lengths are not resolved here: percentages, <c>em</c>,
    /// <c>rem</c>, viewport units and <c>auto</c> remain
    /// <see cref="CssLength"/> values for the layout pass, which has the correct
    /// containing block.
    /// </para>
    /// </remarks>
    public ComputedStyle Resolve(
        IgniteSupportedDomNode node,
        ComputedStyle? parent,
        ReadOnlySpan<MatchedRule> matches)
    {
        ArgumentNullException.ThrowIfNull(node);

        var style = new ComputedStyle();

        if (parent is not null)
        {
            style.CopyInheritedFromShared(parent);
        }

        CollectCustomProperties(style, matches);
        style.RefreshCustomPropertyHash();

        ApplyMatches(style, parent, matches, important: false);
        ApplyMatches(style, parent, matches, important: true);

        // Text nodes and fragments are framework concepts, not CSS elements.
        // Text flows inline and a fragment is transparent to layout.
        if (node.IsTextNode)
        {
            style.Display = Display.Inline;
        }
        else if (node.IsFragment)
        {
            style.Display = Display.Contents;
        }

        return style;
    }

    /// <summary>
    /// Collects <c>--*</c> custom properties from the matched declarations.
    /// </summary>
    /// <param name="style">The style receiving the properties.</param>
    /// <param name="matches">The matched rules in weakest-first cascade order.</param>
    /// <remarks>
    /// <para>
    /// Two passes implement the cascade for custom properties. The first pass
    /// records normal declarations, so later matches overwrite earlier ones. The
    /// second pass records important declarations and therefore overwrites every
    /// normal declaration, even one that appeared later in source order.
    /// </para>
    /// <para>
    /// Values are trimmed and stripped of <c>!important</c> by
    /// <see cref="CleanValue"/>. References to other custom properties are left
    /// intact; they are substituted when the property is used by
    /// <see cref="ResolveVariables"/>.
    /// </para>
    /// <para>
    /// Custom properties are intentionally not routed through
    /// <see cref="ApplyDeclaration"/>: they have no typed representation in
    /// <see cref="ComputedStyle"/>.
    /// </para>
    /// </remarks>
    private void CollectCustomProperties(ComputedStyle style, ReadOnlySpan<MatchedRule> matches)
    {
       // Matches arrive weakest first, so later declarations overwrite earlier
        // ones. Normal custom properties are collected first, then important ones
        // overwrite them regardless of where they appear in the cascade.
        for (var i = 0; i < matches.Length; i++)
        {
            var rule = matches[i].Rule;
            var name = InternTable.GetString(rule.PropertyId);

            if (name is null || !name.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var value = InternTable.GetString(rule.ValueId);

            if (value is null || IsImportant(rule.ValueId, value))
            {
                continue;
            }

            SetCustomProperty(style, name, CleanValue(rule.ValueId, value));
        }

        for (var i = 0; i < matches.Length; i++)
        {
            var rule = matches[i].Rule;
            var name = InternTable.GetString(rule.PropertyId);

            if (name is null || !name.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var value = InternTable.GetString(rule.ValueId);

            if (value is not null && IsImportant(rule.ValueId, value))
            {
                SetCustomProperty(style, name, CleanValue(rule.ValueId, value));
            }
        }
    }

    /// <summary>
    /// Determines whether a value contains a viewport-height unit.
    /// </summary>
    /// <param name="value">The declaration value to scan.</param>
    /// <returns>
    /// <see langword="true"/> when <c>vh</c>, <c>dvh</c>, <c>svh</c> or
    /// <c>lvh</c> follows a digit or decimal point; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// This is a cheap textual heuristic, not a full tokenizer. Requiring a
    /// preceding digit or dot avoids matching identifiers such as
    /// <c>svg</c>. The result feeds <see cref="ComputedStyle.UsesViewportHeight"/>,
    /// which makes the layout cache signature include the viewport height.
    /// </remarks>
    private static bool ContainsViewportHeightUnit(string value)
    {
        for (var i = 1; i < value.Length; i++)
        {
            if (!char.IsDigit(value[i - 1]) && value[i - 1] != '.')
            {
                continue;
            }

            var rest = value.AsSpan(i);

            if (rest.StartsWith("dvh", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("svh", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("lvh", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("vh", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Stores one custom property, allocating the ordinal dictionary on first use.
    /// </summary>
    /// <param name="style">The style receiving the property.</param>
    /// <param name="name">The custom property name, including the leading <c>--</c>.</param>
    /// <param name="value">The cleaned value, without <c>!important</c>.</param>
    /// <remarks>
    /// Names are compared with <see cref="StringComparer.Ordinal"/> because CSS
    /// custom property names are case-sensitive.
    /// </remarks>
    private static void SetCustomProperty(ComputedStyle style, string name, string value)
    {
        style.CustomProperties ??= new Dictionary<string, string>(StringComparer.Ordinal);
        style.CustomProperties[name] = value;
    }

    /// <summary>
    /// Applies all matched declarations of one importance level in cascade order.
    /// </summary>
    /// <param name="style">The style being built.</param>
    /// <param name="parent">The parent style, used by CSS-wide keywords.</param>
    /// <param name="matches">The matched rules in weakest-first order.</param>
    /// <param name="important">
    /// <see langword="true"/> to apply only <c>!important</c> declarations;
    /// <see langword="false"/> to apply only normal declarations.
    /// </param>
    /// <remarks>
    /// <para>
    /// Later entries in <paramref name="matches"/> overwrite earlier ones for the
    /// same property, which is why the caller runs this method twice: normal
    /// first, important second. Important declarations therefore beat normal
    /// declarations without a separate priority comparison.
    /// </para>
    /// <para>
    /// A property the engine does not understand is recorded in
    /// <see cref="ComputedStyle.UnsupportedProperties"/> for diagnostics, except
    /// for custom properties, which were already collected and must not be
    /// reported as unsupported. Unknown declarations therefore still consume
    /// cascade time but have no layout effect.
    /// </para>
    /// </remarks>
    private void ApplyMatches(
        ComputedStyle style,
        ComputedStyle? parent,
        ReadOnlySpan<MatchedRule> matches,
        bool important)
    {
        for (var i = 0; i < matches.Length; i++)
        {
            var rule = matches[i].Rule;
            var property = CssPropertyIds.Get(rule.PropertyId);
            var value = InternTable.GetString(rule.ValueId);

            if (value is null || IsImportant(rule.ValueId, value) != important)
            {
                continue;
            }

            if (property == CssProperty.Unknown)
            {
                var name = InternTable.GetString(rule.PropertyId);

                if (name is not null && name.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                style.UnsupportedProperties ??= [];
                style.UnsupportedProperties.Add(rule.PropertyId);
                continue;
            }

            ApplyDeclaration(style, parent, property, rule.ValueId, value);
        }
    }

    /// <summary>
    /// Applies a single known declaration to the style under construction.
    /// </summary>
    /// <param name="style">The style being built.</param>
    /// <param name="parent">The parent style, used for relative font sizes and keywords.</param>
    /// <param name="property">The recognised property to apply.</param>
    /// <param name="valueId">The interned id of the raw value, used as a cache key.</param>
    /// <param name="rawValue">The raw declaration value, including any <c>!important</c>.</param>
    /// <remarks>
    /// <para>
    /// The value is first cleaned (trimmed, <c>!important</c> removed), then
    /// <c>var()</c> references are substituted. An empty result is ignored,
    /// leaving any weaker declaration that was already applied in place.
    /// </para>
    /// <para>
    /// <c>all</c> is handled before variable substitution because its value is a
    /// CSS-wide keyword, not a property value. Any other CSS-wide keyword is
    /// handled before the property switch.
    /// </para>
    /// <para>
    /// The large switch distinguishes shorthands (which write several fields)
    /// from longhands (which write one field). Values that need a containing
    /// block remain <see cref="CssLength"/> values.
    /// </para>
    /// </remarks>
    private void ApplyDeclaration(
        ComputedStyle style,
        ComputedStyle? parent,
        CssProperty property,
        int valueId,
        string rawValue)
    {
        var value = CleanValue(valueId, rawValue);

        if (value.Length == 0)
        {
            return;
        }

        if (property == CssProperty.All)
        {
            ApplyAll(style, parent, value);
            return;
        }

        value = ResolveVariables(value, style, valueId);

        if (value.Length == 0)
        {
            return;
        }

        if (!style.UsesViewportHeight && ContainsViewportHeightUnit(value))
        {
            style.UsesViewportHeight = true;
        }

        if (TryGetWideKeyword(value, out var keyword))
        {
            ApplyWideKeyword(style, parent, property, keyword);
            return;
        }

        switch (property)
        {
            case CssProperty.Display:
                style.Display = ParseDisplay(value);
                break;

            case CssProperty.Position:
                style.Position = ParsePosition(value);
                break;

            case CssProperty.Float:
                style.Float = value.Equals("left", StringComparison.OrdinalIgnoreCase)
                    ? Float.Left
                    : value.Equals("right", StringComparison.OrdinalIgnoreCase)
                        ? Float.Right
                        : Float.None;
                break;

            case CssProperty.Clear:
                style.Clear = value.Equals("both", StringComparison.OrdinalIgnoreCase) ? Clear.Both
                    : value.Equals("left", StringComparison.OrdinalIgnoreCase) ? Clear.Left
                    : value.Equals("right", StringComparison.OrdinalIgnoreCase) ? Clear.Right
                    : Clear.None;
                break;

            case CssProperty.BoxSizing:
                style.BoxSizing = value.Equals("border-box", StringComparison.OrdinalIgnoreCase)
                    ? BoxSizing.BorderBox
                    : BoxSizing.ContentBox;
                break;

            case CssProperty.Visibility:
                style.Visibility = value.Equals("hidden", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Hidden
                    : value.Equals("collapse", StringComparison.OrdinalIgnoreCase)
                        ? Visibility.Collapse
                        : Visibility.Visible;
                break;

            case CssProperty.Opacity:
                style.Opacity = Math.Clamp(ParseNumberCached(valueId, value), 0, 1);
                break;

            case CssProperty.ZIndex:
                if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    style.HasZIndex = false;
                    style.ZIndex = 0;
                }
                else
                {
                    style.HasZIndex = true;
                    style.ZIndex = (int)MathF.Round(ParseNumberCached(valueId, value));
                }

                break;

            case CssProperty.Width:
                style.Width = ParseLengthCached(valueId, value);
                break;

            case CssProperty.Height:
                style.Height = ParseLengthCached(valueId, value);
                break;

            case CssProperty.MinWidth:
                style.MinWidth = ParseLengthCached(valueId, value);
                break;

            case CssProperty.MinHeight:
                style.MinHeight = ParseLengthCached(valueId, value);
                break;

            case CssProperty.MaxWidth:
                style.MaxWidth = value.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? CssLength.Auto
                    : ParseLengthCached(valueId, value);
                break;

            case CssProperty.MaxHeight:
                style.MaxHeight = value.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? CssLength.Auto
                    : ParseLengthCached(valueId, value);
                break;

            case CssProperty.AspectRatio:
                style.AspectRatio = ParseAspectRatio(value);
                break;

            case CssProperty.Top:
                style.Top = ParseLengthCached(valueId, value);
                break;

            case CssProperty.Right:
                style.Right = ParseLengthCached(valueId, value);
                break;

            case CssProperty.Bottom:
                style.Bottom = ParseLengthCached(valueId, value);
                break;

            case CssProperty.Left:
                style.Left = ParseLengthCached(valueId, value);
                break;

            case CssProperty.Inset:
                style.Top = ParseLength4Cached(valueId, value).Top;
                style.Right = ParseLength4Cached(valueId, value).Right;
                style.Bottom = ParseLength4Cached(valueId, value).Bottom;
                style.Left = ParseLength4Cached(valueId, value).Left;
                break;

            case CssProperty.Margin:
                style.Margin = ParseLength4Cached(valueId, value);
                break;

            case CssProperty.MarginTop:
                style.Margin = style.Margin.WithTop(ParseLengthCached(valueId, value));
                break;

            case CssProperty.MarginRight:
                style.Margin = style.Margin.WithRight(ParseLengthCached(valueId, value));
                break;

            case CssProperty.MarginBottom:
                style.Margin = style.Margin.WithBottom(ParseLengthCached(valueId, value));
                break;

            case CssProperty.MarginLeft:
                style.Margin = style.Margin.WithLeft(ParseLengthCached(valueId, value));
                break;

            case CssProperty.Padding:
                style.Padding = ParseLength4Cached(valueId, value);
                break;

            case CssProperty.PaddingTop:
                style.Padding = style.Padding.WithTop(ParseLengthCached(valueId, value));
                break;

            case CssProperty.PaddingRight:
                style.Padding = style.Padding.WithRight(ParseLengthCached(valueId, value));
                break;

            case CssProperty.PaddingBottom:
                style.Padding = style.Padding.WithBottom(ParseLengthCached(valueId, value));
                break;

            case CssProperty.PaddingLeft:
                style.Padding = style.Padding.WithLeft(ParseLengthCached(valueId, value));
                break;

            case CssProperty.Border:
            {
                var border = ParseBorderCached(valueId, value);
                style.BorderWidth = CssLengths.All(border.Width);
                style.BorderStyleTop = style.BorderStyleRight = style.BorderStyleBottom = style.BorderStyleLeft = border.Style;
                style.BorderColorTop = style.BorderColorRight = style.BorderColorBottom = style.BorderColorLeft = border.Color;
                break;
            }

            case CssProperty.BorderWidth:
                style.BorderWidth = ParseLength4Cached(valueId, value);
                break;

            case CssProperty.BorderStyle:
            {
                var styles = ParseBorderStyles(value);
                style.BorderStyleTop = styles.Top;
                style.BorderStyleRight = styles.Right;
                style.BorderStyleBottom = styles.Bottom;
                style.BorderStyleLeft = styles.Left;
                break;
            }

            case CssProperty.BorderColor:
            {
                var colors = ParseColor4Cached(valueId, value, style.Color);
                style.BorderColorTop = colors.Top;
                style.BorderColorRight = colors.Right;
                style.BorderColorBottom = colors.Bottom;
                style.BorderColorLeft = colors.Left;
                break;
            }

            case CssProperty.BorderTop:
                ApplyBorderSide(style, 0, ParseBorderCached(valueId, value));
                break;

            case CssProperty.BorderRight:
                ApplyBorderSide(style, 1, ParseBorderCached(valueId, value));
                break;

            case CssProperty.BorderBottom:
                ApplyBorderSide(style, 2, ParseBorderCached(valueId, value));
                break;

            case CssProperty.BorderLeft:
                ApplyBorderSide(style, 3, ParseBorderCached(valueId, value));
                break;

            case CssProperty.BorderTopWidth:
                style.BorderWidth = style.BorderWidth.WithTop(ParseLengthCached(valueId, value));
                break;

            case CssProperty.BorderRightWidth:
                style.BorderWidth = style.BorderWidth.WithRight(ParseLengthCached(valueId, value));
                break;

            case CssProperty.BorderBottomWidth:
                style.BorderWidth = style.BorderWidth.WithBottom(ParseLengthCached(valueId, value));
                break;

            case CssProperty.BorderLeftWidth:
                style.BorderWidth = style.BorderWidth.WithLeft(ParseLengthCached(valueId, value));
                break;

            case CssProperty.BorderTopStyle:
                style.BorderStyleTop = ParseBorderStyle(value);
                break;

            case CssProperty.BorderRightStyle:
                style.BorderStyleRight = ParseBorderStyle(value);
                break;

            case CssProperty.BorderBottomStyle:
                style.BorderStyleBottom = ParseBorderStyle(value);
                break;

            case CssProperty.BorderLeftStyle:
                style.BorderStyleLeft = ParseBorderStyle(value);
                break;

            case CssProperty.BorderTopColor:
                style.BorderColorTop = ParseColorCached(valueId, value, style.Color);
                break;

            case CssProperty.BorderRightColor:
                style.BorderColorRight = ParseColorCached(valueId, value, style.Color);
                break;

            case CssProperty.BorderBottomColor:
                style.BorderColorBottom = ParseColorCached(valueId, value, style.Color);
                break;

            case CssProperty.BorderLeftColor:
                style.BorderColorLeft = ParseColorCached(valueId, value, style.Color);
                break;

            case CssProperty.BorderRadius:
                style.BorderRadius = ParseRadiusCached(valueId, value);
                break;

            case CssProperty.BorderTopLeftRadius:
                style.BorderRadius = style.BorderRadius with { TopLeft = ParseLengthCached(valueId, value) };
                break;

            case CssProperty.BorderTopRightRadius:
                style.BorderRadius = style.BorderRadius with { TopRight = ParseLengthCached(valueId, value) };
                break;

            case CssProperty.BorderBottomRightRadius:
                style.BorderRadius = style.BorderRadius with { BottomRight = ParseLengthCached(valueId, value) };
                break;

            case CssProperty.BorderBottomLeftRadius:
                style.BorderRadius = style.BorderRadius with { BottomLeft = ParseLengthCached(valueId, value) };
                break;

            case CssProperty.Background:
                ApplyBackgroundShorthand(style, value);
                break;

            case CssProperty.BackgroundColor:
                style.BackgroundColor = ParseColorCached(valueId, value, style.Color);
                break;

            case CssProperty.BackgroundImage:
                style.BackgroundImages = ParseBackgroundsCached(valueId, value, style.Color);
                break;

            case CssProperty.BackgroundSize:
                ApplyBackgroundSize(style, value);
                break;

            case CssProperty.BackgroundPosition:
                ApplyBackgroundPosition(style, value);
                break;

            case CssProperty.BackgroundRepeat:
                style.BackgroundRepeat = ParseBackgroundRepeat(value);
                break;

            case CssProperty.BackgroundClip:
                style.BackgroundClip = ParseBackgroundBox(value);
                break;

            case CssProperty.BackgroundOrigin:
                style.BackgroundOrigin = ParseBackgroundBox(value);
                break;

            case CssProperty.BackgroundAttachment:
                style.BackgroundFixed = value.Equals("fixed", StringComparison.OrdinalIgnoreCase);
                break;

            case CssProperty.Color:
                style.Color = ParseColorCached(valueId, value, style.Color);
                break;

            case CssProperty.Font:
                ApplyFontShorthand(style, parent, value);
                break;

            case CssProperty.FontFamily:
                style.FontFamily = CssValueParser.Unquote(value.Trim());
                break;

            case CssProperty.FontSize:
                ApplyFontSize(style, parent, valueId, value);
                break;

            case CssProperty.FontWeight:
                style.FontWeight = ParseFontWeight(value);
                break;

            case CssProperty.FontStyle:
                style.Italic = value.StartsWith("italic", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("oblique", StringComparison.OrdinalIgnoreCase);
                break;

            case CssProperty.LineHeight:
                style.LineHeight = ParseLineHeight(value);
                break;

            case CssProperty.LetterSpacing:
                style.LetterSpacing = value.Equals("normal", StringComparison.OrdinalIgnoreCase)
                    ? CssLength.Zero
                    : ParseLengthCached(valueId, value);
                break;

            case CssProperty.TextAlign:
                style.TextAlign = ParseTextAlign(value);
                break;

            case CssProperty.TextDecoration:
            case CssProperty.TextDecorationLine:
                style.TextDecoration = ParseTextDecoration(value);
                break;

            case CssProperty.TextTransform:
                style.TextTransform = value.Equals("uppercase", StringComparison.OrdinalIgnoreCase)
                    ? TextTransform.Uppercase
                    : value.Equals("lowercase", StringComparison.OrdinalIgnoreCase)
                        ? TextTransform.Lowercase
                        : value.Equals("capitalize", StringComparison.OrdinalIgnoreCase)
                            ? TextTransform.Capitalize
                            : TextTransform.None;
                break;

            case CssProperty.WhiteSpace:
                style.WhiteSpace = ParseWhiteSpace(value);
                break;

            case CssProperty.WordBreak:
                if (value.Equals("break-all", StringComparison.OrdinalIgnoreCase))
                {
                    style.OverflowWrap = OverflowWrap.Anywhere;
                }

                break;

            case CssProperty.OverflowWrap:
                style.OverflowWrap = value.Equals("break-word", StringComparison.OrdinalIgnoreCase)
                    ? OverflowWrap.BreakWord
                    : value.Equals("anywhere", StringComparison.OrdinalIgnoreCase)
                        ? OverflowWrap.Anywhere
                        : OverflowWrap.Normal;
                break;

            case CssProperty.TextOverflow:
                style.TextOverflow = value.Equals("ellipsis", StringComparison.OrdinalIgnoreCase)
                    ? TextOverflow.Ellipsis
                    : TextOverflow.Clip;
                break;

            case CssProperty.Direction:
                style.Direction = value.Equals("rtl", StringComparison.OrdinalIgnoreCase)
                    ? Direction.RightToLeft
                    : Direction.LeftToRight;
                break;

            case CssProperty.WritingMode:
                style.WritingMode = value.Equals("vertical-rl", StringComparison.OrdinalIgnoreCase)
                    ? WritingMode.VerticalRightToLeft
                    : value.Equals("vertical-lr", StringComparison.OrdinalIgnoreCase)
                        ? WritingMode.VerticalLeftToRight
                        : WritingMode.HorizontalTopToBottom;
                break;

            case CssProperty.WebkitLineClamp:
                style.WebkitLineClamp = Math.Max(0, (int)MathF.Round(ParseNumberCached(valueId, value)));
                break;

            case CssProperty.Flex:
                ApplyFlexShorthand(style, value);
                break;

            case CssProperty.FlexDirection:
                style.FlexDirection = ParseFlexDirection(value);
                break;

            case CssProperty.FlexWrap:
                style.FlexWrap = ParseFlexWrap(value);
                break;

            case CssProperty.FlexFlow:
                foreach (var part in CssValueParser.SplitTopLevel(value, ' '))
                {
                    if (part.Contains("wrap", StringComparison.OrdinalIgnoreCase))
                    {
                        style.FlexWrap = ParseFlexWrap(part);
                    }
                    else if (part is "row" or "column" or "row-reverse" or "column-reverse")
                    {
                        style.FlexDirection = ParseFlexDirection(part);
                    }
                }

                break;

            case CssProperty.JustifyContent:
                style.JustifyContent = ParseJustifyContent(value);
                break;

            case CssProperty.AlignItems:
                style.AlignItems = ParseAlignItems(value);
                break;

            case CssProperty.AlignContent:
                style.AlignContent = ParseAlignContent(value);
                break;

            case CssProperty.AlignSelf:
                style.AlignSelf = ParseAlignSelf(value);
                break;

            case CssProperty.FlexGrow:
                style.FlexGrow = Math.Max(0, ParseNumberCached(valueId, value));
                break;

            case CssProperty.FlexShrink:
                style.FlexShrink = Math.Max(0, ParseNumberCached(valueId, value));
                break;

            case CssProperty.FlexBasis:
                style.FlexBasis = value.Equals("content", StringComparison.OrdinalIgnoreCase)
                    ? CssLength.MaxContent
                    : ParseLengthCached(valueId, value);
                break;

            case CssProperty.Gap:
            {
                var lengths = CssLengths.ParseShorthand(value);
                style.RowGap = lengths.Top;
                style.ColumnGap = lengths.Right;
                break;
            }

            case CssProperty.RowGap:
                style.RowGap = value.Equals("normal", StringComparison.OrdinalIgnoreCase)
                    ? CssLength.Zero
                    : ParseLengthCached(valueId, value);
                break;

            case CssProperty.ColumnGap:
                style.ColumnGap = value.Equals("normal", StringComparison.OrdinalIgnoreCase)
                    ? CssLength.Zero
                    : ParseLengthCached(valueId, value);
                break;

            case CssProperty.Order:
                style.Order = (int)MathF.Round(ParseNumberCached(valueId, value));
                break;

            case CssProperty.Grid:
            case CssProperty.GridTemplate:
                ApplyGridTemplate(style, value);
                break;

            case CssProperty.GridTemplateColumns:
                style.GridTemplateColumns = ParseTracksCached(valueId, value);
                break;

            case CssProperty.GridTemplateRows:
                style.GridTemplateRows = ParseTracksCached(valueId, value);
                break;

            case CssProperty.GridAutoColumns:
                style.GridAutoColumns = ParseTracksCached(valueId, value);
                break;

            case CssProperty.GridAutoRows:
                style.GridAutoRows = ParseTracksCached(valueId, value);
                break;

            case CssProperty.GridAutoFlow:
                style.GridAutoFlow = ParseGridAutoFlow(value);
                break;

            case CssProperty.GridColumn:
                style.GridColumn = ParsePlacementCached(valueId, value);
                break;

            case CssProperty.GridRow:
                style.GridRow = ParsePlacementCached(valueId, value);
                break;

            case CssProperty.GridColumnStart:
                style.GridColumn = ApplyGridLine(style.GridColumn, value, true);
                break;

            case CssProperty.GridColumnEnd:
                style.GridColumn = ApplyGridLine(style.GridColumn, value, false);
                break;

            case CssProperty.GridRowStart:
                style.GridRow = ApplyGridLine(style.GridRow, value, true);
                break;

            case CssProperty.GridRowEnd:
                style.GridRow = ApplyGridLine(style.GridRow, value, false);
                break;

            case CssProperty.GridArea:
            {
                var parts = CssValueParser.SplitTopLevel(value, '/');

                if (parts.Count == 1)
                {
                    var name = parts[0].Trim();

                    if (name.Length > 0
                        && !name.Equals("auto", StringComparison.OrdinalIgnoreCase)
                        && !int.TryParse(name, out _))
                    {
                        style.GridAreaName = name;
                        style.GridRow = GridPlacement.Auto;
                        style.GridColumn = GridPlacement.Auto;
                    }
                    else if (int.TryParse(name, out var line))
                    {
                        style.GridAreaName = null;
                        style.GridRow = new GridPlacement(line, 0, false, true);
                        style.GridColumn = new GridPlacement(line, 0, false, true);
                    }

                    break;
                }

                style.GridAreaName = null;

                // grid-area: row-start / column-start / row-end / column-end
                style.GridRow = GridPlacementParser.Parse(
                    (parts.Count > 0 ? parts[0] : "auto")
                    + " / " + (parts.Count > 2 ? parts[2] : "auto"));

                style.GridColumn = GridPlacementParser.Parse(
                    (parts.Count > 1 ? parts[1] : "auto")
                    + " / " + (parts.Count > 3 ? parts[3] : "auto"));

                break;
            }

            case CssProperty.JustifyItems:
                style.JustifyItems = ParseJustifyItems(value);
                break;

            case CssProperty.JustifySelf:
                style.JustifySelf = ParseJustifySelf(value);
                break;

            case CssProperty.PlaceItems:
            {
                var parts = CssValueParser.SplitTopLevel(value, ' ');
                style.AlignItems = ParseAlignItems(parts.Count > 0 ? parts[0] : value);
                style.JustifyItems = ParseJustifyItems(parts.Count > 1 ? parts[1] : (parts.Count > 0 ? parts[0] : value));
                break;
            }

            case CssProperty.PlaceContent:
            {
                var parts = CssValueParser.SplitTopLevel(value, ' ');
                style.AlignContent = ParseAlignContent(parts.Count > 0 ? parts[0] : value);
                break;
            }

            case CssProperty.Overflow:
            {
                var parts = CssValueParser.SplitTopLevel(value, ' ');
                style.OverflowX = ParseOverflow(parts.Count > 0 ? parts[0] : value);
                style.OverflowY = ParseOverflow(parts.Count > 1 ? parts[1] : (parts.Count > 0 ? parts[0] : value));
                break;
            }

            case CssProperty.OverflowX:
                style.OverflowX = ParseOverflow(value);
                break;

            case CssProperty.OverflowY:
                style.OverflowY = ParseOverflow(value);
                break;

            case CssProperty.Transform:
                style.Transform = ParseTransformCached(valueId, value);
                break;

            case CssProperty.TransformOrigin:
                ApplyTransformOrigin(style, value);
                break;

            case CssProperty.BoxShadow:
                style.BoxShadows = ParseShadowsCached(valueId, value, style.Color);
                break;

            case CssProperty.Outline:
                ApplyOutlineShorthand(style, valueId, value);
                break;

            case CssProperty.OutlineWidth:
                style.OutlineWidth = ParseLengthCached(valueId, value);
                break;

            case CssProperty.OutlineStyle:
                style.OutlineStyle = ParseBorderStyle(value);
                break;

            case CssProperty.OutlineColor:
                style.OutlineColor = ParseColorCached(valueId, value, style.Color);
                break;

            case CssProperty.OutlineOffset:
                style.OutlineOffset = ParseLengthCached(valueId, value);
                break;

            case CssProperty.Filter:
                style.Filter = ParseFilterCached(valueId, value, style.Color);
                break;

            case CssProperty.BackdropFilter:
                style.BackdropFilter = ParseFilterCached(valueId, value, style.Color);
                break;

            case CssProperty.ClipPath:
                style.ClipPath = ParseClipPathCached(valueId, value);
                break;

            case CssProperty.Mask:
                style.Mask = ParseMaskCached(valueId, value);
                break;

            case CssProperty.MixBlendMode:
                style.MixBlendMode = ParseMixBlendMode(value);
                break;

            case CssProperty.Isolation:
                style.Isolate = value.Equals("isolate", StringComparison.OrdinalIgnoreCase);
                break;

            case CssProperty.GridTemplateAreas:
                style.GridTemplateAreas = ParseGridAreasCached(valueId, value);
                break;

            case CssProperty.ObjectFit:
                style.ObjectFit = ParseObjectFit(value);
                break;

            case CssProperty.TableLayout:
                style.TableLayout = value.Equals("fixed", StringComparison.OrdinalIgnoreCase)
                    ? TableLayout.Fixed
                    : TableLayout.Auto;
                break;

            case CssProperty.BorderCollapse:
                style.BorderCollapse = value.Equals("collapse", StringComparison.OrdinalIgnoreCase)
                    ? BorderCollapse.Collapse
                    : BorderCollapse.Separate;
                break;

            case CssProperty.BorderSpacing:
                style.BorderSpacing = ParseLengthCached(valueId, value);
                break;

            case CssProperty.ColumnCount:
                style.ColumnCount = Math.Max(1, (int)MathF.Round(ParseNumberCached(valueId, value)));
                break;

            case CssProperty.ScrollbarColor:
            {
                var parts = CssValueParser.SplitTopLevel(value, ' ');

                if (parts.Count >= 2)
                {
                    // Parse the two colours directly: the memo cache is keyed by
                    // the declaration's value id, so caching sub-values under it
                    // would make the second colour reuse the first colour's entry.
                    style.ScrollbarThumbColor = CssColor.ParseOr(parts[0], style.Color, style.Color);
                    style.ScrollbarTrackColor = CssColor.ParseOr(parts[1], style.Color, style.Color);
                }

                break;
            }

            case CssProperty.ScrollbarWidth:
                style.ShowScrollbars = !value.Equals("none", StringComparison.OrdinalIgnoreCase);
                break;

            case CssProperty.ScrollBehavior:
                style.ScrollBehavior = value.Equals("smooth", StringComparison.OrdinalIgnoreCase)
                    ? ScrollBehavior.Smooth
                    : ScrollBehavior.Auto;
                break;

            case CssProperty.PointerEvents:
            case CssProperty.Cursor:
            case CssProperty.UserSelect:
            case CssProperty.WillChange:
            case CssProperty.Contain:
            case CssProperty.Content:
            case CssProperty.ObjectPosition:
                // Parsed and ignored by the first-cut painter. Keeping the case
                // explicit documents that these are intentionally not layout
                // inputs rather than accidental omissions.
                break;

            case CssProperty.Animation:
                style.Animations = ParseAnimationShorthand(value);
                break;

            case CssProperty.AnimationName:
                ApplyAnimationName(style, value);
                break;

            case CssProperty.AnimationDuration:
                ApplyAnimationTime(style, value, static (spec, seconds) => spec.Duration = seconds);
                break;

            case CssProperty.AnimationDelay:
                ApplyAnimationTime(style, value, static (spec, seconds) => spec.Delay = seconds);
                break;

            case CssProperty.AnimationIterationCount:
                ApplyAnimationIterationCount(style, value);
                break;

            case CssProperty.AnimationDirection:
                ApplyAnimationDirection(style, value);
                break;

            case CssProperty.AnimationFillMode:
                ApplyAnimationFillMode(style, value);
                break;

            case CssProperty.AnimationPlayState:
                ApplyAnimationPlayState(style, value);
                break;

            case CssProperty.AnimationTimingFunction:
                ApplyAnimationTimingFunction(style, value);
                break;

            default:
                style.UnsupportedProperties ??= [];
                style.UnsupportedProperties.Add(rulePropertyId(property));
                break;
        }
    }

    /// <summary>
    /// Maps a property that reached the switch's default arm to the diagnostic
    /// unsupported-property list.
    /// </summary>
    /// <param name="property">The recognised property with no apply case.</param>
    /// <returns>The enum's numeric value.</returns>
    /// <remarks>
    /// This is only a diagnostic marker, not an interned property-name id. It
    /// deliberately avoids interning a string on the hot declaration path. The
    /// returned value may therefore collide with an interned id in the same list.
    /// </remarks>
    private static int rulePropertyId(CssProperty property)
    {
        // Only used for the diagnostic unsupported list; the id is not needed
        // for layout and interning it here keeps the hot switch allocation free.
        return (int)property;
    }

    /// <summary>
    /// Applies a parsed border shorthand to one side only.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="side">
    /// The side index: 0 = top, 1 = right, 2 = bottom, 3 = left.
    /// </param>
    /// <param name="border">The width, style and colour parsed from the shorthand.</param>
    /// <remarks>
    /// An out-of-range side is ignored silently; callers only pass the four
    /// constants implied by <c>border-top</c> through <c>border-left</c>.
    /// </remarks>
    private void ApplyBorderSide(ComputedStyle style, int side, ParsedBorder border)
    {
        switch (side)
        {
            case 0:
                style.BorderWidth = style.BorderWidth.WithTop(border.Width);
                style.BorderStyleTop = border.Style;
                style.BorderColorTop = border.Color;
                break;
            case 1:
                style.BorderWidth = style.BorderWidth.WithRight(border.Width);
                style.BorderStyleRight = border.Style;
                style.BorderColorRight = border.Color;
                break;
            case 2:
                style.BorderWidth = style.BorderWidth.WithBottom(border.Width);
                style.BorderStyleBottom = border.Style;
                style.BorderColorBottom = border.Color;
                break;
            case 3:
                style.BorderWidth = style.BorderWidth.WithLeft(border.Width);
                style.BorderStyleLeft = border.Style;
                style.BorderColorLeft = border.Color;
                break;
        }
    }
}
