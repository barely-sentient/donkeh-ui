using System.Numerics;

namespace Ignite.LayoutLab;

/// <summary>
/// Parsed-value cache. Declaration values are interned by the stylesheet
/// compiler, so the same <c>1px solid #ddd</c> on ten thousand nodes is parsed
/// once and then shared by value id.
/// </summary>
/// <remarks>
/// <para>
/// The dictionaries are keyed by the interned declaration value id. That makes
/// caching safe only for values whose meaning does not depend on the element:
/// values containing <c>var()</c> or <c>currentcolor</c> bypass the value-id
/// caches, and variable-resolved strings use a composite key containing the
/// custom-property hash.
/// </para>
/// <para>
/// An instance is normally owned by <see cref="LayoutEngine"/> and shared across
/// frames. It is mutable and not thread-safe; access must be confined to one
/// layout at a time. Entries are never evicted individually, so a long-lived
/// cache retains one entry per distinct interned value until
/// <see cref="Clear"/> is called.
/// </para>
/// </remarks>
/// <seealso cref="StyleResolver"/>
/// <seealso cref="Ignite.Parser.InternTable"/>
public sealed class StyleValueCache
{
    /// <summary>Single parsed lengths, keyed by interned value id.</summary>
    /// <remarks>Includes percentages and relative units, which stay unresolved.</remarks>
    internal readonly Dictionary<int, CssLength> Lengths = [];
    /// <summary>One-to-four value box shorthands such as <c>margin: 1px 2px</c>.</summary>
    internal readonly Dictionary<int, CssLength4> Length4 = [];
    /// <summary>Packed ARGB colours, keyed by interned value id.</summary>
    /// <remarks>
    /// A value may contain <see cref="ComputedStyle.CurrentColor"/> rather than a
    /// concrete colour; the painter resolves the sentinel.
    /// </remarks>
    internal readonly Dictionary<int, uint> Colors = [];
    /// <summary>Unitless numbers such as opacity, flex factors and z-index.</summary>
    internal readonly Dictionary<int, float> Numbers = [];
    /// <summary>Parsed border shorthands, including width, style and colour.</summary>
    internal readonly Dictionary<int, StyleResolver.ParsedBorder> Borders = [];
    /// <summary>Four corner radii parsed from <c>border-radius</c>.</summary>
    internal readonly Dictionary<int, CssCornerRadius4> Radii = [];
    /// <summary>Box-shadow layer arrays, keyed by interned value id.</summary>
    /// <remarks>The arrays are shared and must be treated as immutable.</remarks>
    internal readonly Dictionary<int, ShadowSpec[]> Shadows = [];
    /// <summary>Background-image layer arrays, keyed by interned value id.</summary>
    /// <remarks>The arrays are shared and must be treated as immutable.</remarks>
    internal readonly Dictionary<int, BackgroundImageSpec[]> Backgrounds = [];
    /// <summary>Parsed grid track lists for template and auto tracks.</summary>
    internal readonly Dictionary<int, GridTrackList> Tracks = [];
    /// <summary>Parsed grid row/column placements.</summary>
    internal readonly Dictionary<int, GridPlacement> Placements = [];
    /// <summary>Parsed <c>grid-template-areas</c> matrices.</summary>
    /// <remarks>Only successful parses are stored; invalid values are retried.</remarks>
    internal readonly Dictionary<int, GridTemplateAreas> GridAreas = [];
    /// <summary>Parsed filter and backdrop-filter chains.</summary>
    internal readonly Dictionary<int, FilterSpec> Filters = [];
    /// <summary>Parsed clip-path shapes.</summary>
    internal readonly Dictionary<int, ClipPathSpec> ClipPaths = [];
    /// <summary>Parsed mask shorthands.</summary>
    internal readonly Dictionary<int, MaskSpec> Masks = [];
    /// <summary>
    /// Parsed transforms, including a flag that distinguishes a cached
    /// <see langword="null"/> (for <c>none</c>) from a miss.
    /// </summary>
    internal readonly Dictionary<int, (bool HasTransform, TransformSpec? Transform)> Transforms = [];
    /// <summary>
    /// Whether each raw value ends with <c>!important</c>, keyed by interned
    /// value id.
    /// </summary>
    /// <remarks>Metadata used by the cascade; not part of <see cref="Count"/>.</remarks>
    internal readonly Dictionary<int, bool> Important = [];
    /// <summary>
    /// Values trimmed of whitespace and any trailing <c>!important</c>, keyed by
    /// interned value id.
    /// </summary>
    /// <remarks>Metadata used by the cascade; not part of <see cref="Count"/>.</remarks>
    internal readonly Dictionary<int, string> CleanValues = [];
    /// <summary>
    /// Whether each value was observed to contain <c>var()</c>, keyed by interned
    /// value id.
    /// </summary>
    /// <remarks>
    /// A missing key means "not observed", not "no variables". Parse helpers use
    /// the entry to decide whether value-id-keyed caching is safe. Not part of
    /// <see cref="Count"/>.
    /// </remarks>
    internal readonly Dictionary<int, bool> HasVariables = [];
    /// <summary>
    /// Variable-substituted declaration strings keyed by value id and the
    /// custom-property hash of the using element.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hash component makes the key depend on the inherited custom-property
    /// chain, so two elements with different variables do not share a resolved
    /// string. See <see cref="ComputedStyle.CustomPropertyHash"/>.
    /// </para>
    /// <para>
    /// The resolver clears this dictionary wholesale when it grows beyond 4096
    /// entries to bound memory. It is not part of <see cref="Count"/>.
    /// </para>
    /// </remarks>
    internal readonly Dictionary<(int ValueId, long Variables), string> ResolvedValues = [];

    /// <summary>
    /// Gets the number of cached parsed values across the typed value
    /// dictionaries.
    /// </summary>
    /// <value>
    /// The sum of <see cref="Lengths"/>, <see cref="Length4"/>,
    /// <see cref="Colors"/>, <see cref="Numbers"/>, <see cref="Borders"/>,
    /// <see cref="Radii"/>, <see cref="Shadows"/>, <see cref="Backgrounds"/>,
    /// <see cref="Tracks"/>, <see cref="Placements"/>, <see cref="Transforms"/>,
    /// <see cref="GridAreas"/>, <see cref="Filters"/>, <see cref="ClipPaths"/>
    /// and <see cref="Masks"/>.
    /// </value>
    /// <remarks>
    /// Cascade metadata (<see cref="Important"/>, <see cref="CleanValues"/>,
    /// <see cref="HasVariables"/> and <see cref="ResolvedValues"/>) is excluded.
    /// The value is therefore a lower bound and is intended for diagnostics and
    /// tests, not as an exact memory measure.
    /// </remarks>
    public int Count =>
        Lengths.Count + Length4.Count + Colors.Count + Numbers.Count + Borders.Count
        + Radii.Count + Shadows.Count + Backgrounds.Count + Tracks.Count
        + Placements.Count + Transforms.Count + GridAreas.Count + Filters.Count
        + ClipPaths.Count + Masks.Count;

    /// <summary>
    /// Removes every cached value and every piece of cascade metadata.
    /// </summary>
    /// <remarks>
    /// After clearing, the cache repopulates on the next resolution. This is
    /// called by <see cref="LayoutEngine.ClearCaches"/> and is safe to call on an
    /// empty cache. The dictionaries retain their allocated capacity, so clearing
    /// does not necessarily release all backing memory.
    /// </remarks>
    public void Clear()
    {
        Lengths.Clear();
        Length4.Clear();
        Colors.Clear();
        Numbers.Clear();
        Borders.Clear();
        Radii.Clear();
        Shadows.Clear();
        Backgrounds.Clear();
        Tracks.Clear();
        Placements.Clear();
        GridAreas.Clear();
        Filters.Clear();
        ClipPaths.Clear();
        Masks.Clear();
        Transforms.Clear();
        Important.Clear();
        CleanValues.Clear();
        HasVariables.Clear();
        ResolvedValues.Clear();
    }
}
