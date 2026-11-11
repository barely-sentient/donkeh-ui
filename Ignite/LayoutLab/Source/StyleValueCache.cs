using System.Numerics;

namespace Ignite.LayoutLab;

/// <summary>
/// Parsed-value cache. Declaration values are interned by the stylesheet
/// compiler, so the same <c>1px solid #ddd</c> on ten thousand nodes is parsed
/// once and then shared by value id.
/// </summary>
public sealed class StyleValueCache
{
    internal readonly Dictionary<int, CssLength> Lengths = [];
    internal readonly Dictionary<int, CssLength4> Length4 = [];
    internal readonly Dictionary<int, uint> Colors = [];
    internal readonly Dictionary<int, float> Numbers = [];
    internal readonly Dictionary<int, StyleResolver.ParsedBorder> Borders = [];
    internal readonly Dictionary<int, CssCornerRadius4> Radii = [];
    internal readonly Dictionary<int, ShadowSpec[]> Shadows = [];
    internal readonly Dictionary<int, BackgroundImageSpec[]> Backgrounds = [];
    internal readonly Dictionary<int, GridTrackList> Tracks = [];
    internal readonly Dictionary<int, GridPlacement> Placements = [];
    internal readonly Dictionary<int, GridTemplateAreas> GridAreas = [];
    internal readonly Dictionary<int, FilterSpec> Filters = [];
    internal readonly Dictionary<int, ClipPathSpec> ClipPaths = [];
    internal readonly Dictionary<int, MaskSpec> Masks = [];
    internal readonly Dictionary<int, (bool HasTransform, TransformSpec? Transform)> Transforms = [];
    internal readonly Dictionary<int, bool> Important = [];
    internal readonly Dictionary<int, string> CleanValues = [];
    internal readonly Dictionary<int, bool> HasVariables = [];
    internal readonly Dictionary<(int ValueId, long Variables), string> ResolvedValues = [];

    public int Count =>
        Lengths.Count + Length4.Count + Colors.Count + Numbers.Count + Borders.Count
        + Radii.Count + Shadows.Count + Backgrounds.Count + Tracks.Count
        + Placements.Count + Transforms.Count + GridAreas.Count + Filters.Count
        + ClipPaths.Count + Masks.Count;

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
