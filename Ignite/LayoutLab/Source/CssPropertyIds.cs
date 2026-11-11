using Ignite.Parser;

namespace Ignite.LayoutLab;

/// <summary>The subset of CSS properties the layout engine understands.</summary>
/// <remarks>
/// <para>
/// The ordinals are an internal implementation detail: only
/// <see cref="Unknown"/> is guaranteed to be zero. They are dense and assigned
/// by declaration order, so code must compare against the named members rather
/// than integer literals. The enum is <see cref="ushort"/>-backed to keep
/// per-declaration records compact.
/// </para>
/// <para>
/// Shorthand properties such as <see cref="Margin"/> and <see cref="Border"/>
/// are included alongside their longhands; the style resolver expands them
/// during cascade processing. <see cref="VisibilityCollapse"/> exists as a
/// separate id for the <c>visibility: collapse</c> value but is not currently
/// registered in <see cref="CssPropertyIds"/>.
/// </para>
/// </remarks>
public enum CssProperty : ushort
{
    /// <summary>No recognised property; returned when a name cannot be mapped.</summary>
    Unknown = 0,
    /// <summary>
    /// <c>all</c>; resets every property (except <c>direction</c> and
    /// <c>unicode-bidi</c>).
    /// </summary>
    All,
    /// <summary><c>display</c>.</summary>
    Display,
    /// <summary><c>position</c>.</summary>
    Position,
    /// <summary><c>float</c>.</summary>
    Float,
    /// <summary><c>clear</c>.</summary>
    Clear,
    /// <summary><c>box-sizing</c>.</summary>
    BoxSizing,
    /// <summary><c>visibility</c>.</summary>
    Visibility,
    /// <summary><c>opacity</c>.</summary>
    Opacity,
    /// <summary><c>z-index</c>.</summary>
    ZIndex,
    /// <summary><c>width</c>.</summary>
    Width,
    /// <summary><c>height</c>.</summary>
    Height,
    /// <summary><c>min-width</c>.</summary>
    MinWidth,
    /// <summary><c>min-height</c>.</summary>
    MinHeight,
    /// <summary><c>max-width</c>.</summary>
    MaxWidth,
    /// <summary><c>max-height</c>.</summary>
    MaxHeight,
    /// <summary><c>aspect-ratio</c>.</summary>
    AspectRatio,
    /// <summary><c>top</c>; a physical inset for positioned boxes.</summary>
    Top,
    /// <summary><c>right</c>; a physical inset for positioned boxes.</summary>
    Right,
    /// <summary><c>bottom</c>; a physical inset for positioned boxes.</summary>
    Bottom,
    /// <summary><c>left</c>; a physical inset for positioned boxes.</summary>
    Left,
    /// <summary>
    /// <c>inset</c>; shorthand for <c>top</c>, <c>right</c>, <c>bottom</c> and
    /// <c>left</c>.
    /// </summary>
    Inset,
    /// <summary><c>margin</c>; four-side shorthand.</summary>
    Margin,
    /// <summary><c>margin-top</c>.</summary>
    MarginTop,
    /// <summary><c>margin-right</c>.</summary>
    MarginRight,
    /// <summary><c>margin-bottom</c>.</summary>
    MarginBottom,
    /// <summary><c>margin-left</c>.</summary>
    MarginLeft,
    /// <summary><c>padding</c>; four-side shorthand.</summary>
    Padding,
    /// <summary><c>padding-top</c>.</summary>
    PaddingTop,
    /// <summary><c>padding-right</c>.</summary>
    PaddingRight,
    /// <summary><c>padding-bottom</c>.</summary>
    PaddingBottom,
    /// <summary><c>padding-left</c>.</summary>
    PaddingLeft,
    /// <summary><c>border</c>; shorthand for width, style and colour on all sides.</summary>
    Border,
    /// <summary><c>border-width</c>; four-side shorthand.</summary>
    BorderWidth,
    /// <summary><c>border-style</c>; four-side shorthand.</summary>
    BorderStyle,
    /// <summary><c>border-color</c>; four-side shorthand.</summary>
    BorderColor,
    /// <summary><c>border-top</c>; width, style and colour for one side.</summary>
    BorderTop,
    /// <summary><c>border-right</c>; width, style and colour for one side.</summary>
    BorderRight,
    /// <summary><c>border-bottom</c>; width, style and colour for one side.</summary>
    BorderBottom,
    /// <summary><c>border-left</c>; width, style and colour for one side.</summary>
    BorderLeft,
    /// <summary><c>border-top-width</c>.</summary>
    BorderTopWidth,
    /// <summary><c>border-right-width</c>.</summary>
    BorderRightWidth,
    /// <summary><c>border-bottom-width</c>.</summary>
    BorderBottomWidth,
    /// <summary><c>border-left-width</c>.</summary>
    BorderLeftWidth,
    /// <summary><c>border-top-style</c>.</summary>
    BorderTopStyle,
    /// <summary><c>border-right-style</c>.</summary>
    BorderRightStyle,
    /// <summary><c>border-bottom-style</c>.</summary>
    BorderBottomStyle,
    /// <summary><c>border-left-style</c>.</summary>
    BorderLeftStyle,
    /// <summary><c>border-top-color</c>.</summary>
    BorderTopColor,
    /// <summary><c>border-right-color</c>.</summary>
    BorderRightColor,
    /// <summary><c>border-bottom-color</c>.</summary>
    BorderBottomColor,
    /// <summary><c>border-left-color</c>.</summary>
    BorderLeftColor,
    /// <summary><c>border-radius</c>; shorthand for the four corner radii.</summary>
    BorderRadius,
    /// <summary><c>border-top-left-radius</c>.</summary>
    BorderTopLeftRadius,
    /// <summary><c>border-top-right-radius</c>.</summary>
    BorderTopRightRadius,
    /// <summary><c>border-bottom-right-radius</c>.</summary>
    BorderBottomRightRadius,
    /// <summary><c>border-bottom-left-radius</c>.</summary>
    BorderBottomLeftRadius,
    /// <summary><c>background</c>; shorthand for colour, image, repeat, position, size and boxes.</summary>
    Background,
    /// <summary><c>background-color</c>.</summary>
    BackgroundColor,
    /// <summary><c>background-image</c>; a URL, gradient or <c>none</c>.</summary>
    BackgroundImage,
    /// <summary><c>background-size</c>.</summary>
    BackgroundSize,
    /// <summary><c>background-position</c>.</summary>
    BackgroundPosition,
    /// <summary><c>background-repeat</c>.</summary>
    BackgroundRepeat,
    /// <summary><c>background-clip</c>.</summary>
    BackgroundClip,
    /// <summary><c>background-origin</c>.</summary>
    BackgroundOrigin,
    /// <summary>
    /// <c>background-attachment</c>; recognised but fixed attachment is not
    /// fully modelled.
    /// </summary>
    BackgroundAttachment,
    /// <summary><c>color</c>.</summary>
    Color,
    /// <summary><c>font</c>; shorthand for family, size, weight, style and line height.</summary>
    Font,
    /// <summary><c>font-family</c>.</summary>
    FontFamily,
    /// <summary><c>font-size</c>.</summary>
    FontSize,
    /// <summary><c>font-weight</c>; numeric or keyword weight.</summary>
    FontWeight,
    /// <summary><c>font-style</c>; <c>normal</c>, <c>italic</c> or <c>oblique</c>.</summary>
    FontStyle,
    /// <summary><c>line-height</c>; a length, percentage or unitless multiplier.</summary>
    LineHeight,
    /// <summary><c>letter-spacing</c>.</summary>
    LetterSpacing,
    /// <summary><c>text-align</c>.</summary>
    TextAlign,
    /// <summary><c>text-decoration</c>; shorthand for line, style, colour and thickness.</summary>
    TextDecoration,
    /// <summary><c>text-decoration-line</c>; a flag set such as <c>underline line-through</c>.</summary>
    TextDecorationLine,
    /// <summary><c>text-transform</c>.</summary>
    TextTransform,
    /// <summary><c>white-space</c>.</summary>
    WhiteSpace,
    /// <summary><c>word-break</c>.</summary>
    WordBreak,
    /// <summary><c>overflow-wrap</c>.</summary>
    OverflowWrap,
    /// <summary><c>text-overflow</c>.</summary>
    TextOverflow,
    /// <summary><c>direction</c>.</summary>
    Direction,
    /// <summary><c>writing-mode</c>.</summary>
    WritingMode,
    /// <summary><c>-webkit-line-clamp</c>; limits a block to a number of lines before ellipsis.</summary>
    WebkitLineClamp,
    /// <summary><c>flex</c>; shorthand for grow, shrink and basis.</summary>
    Flex,
    /// <summary><c>flex-direction</c>.</summary>
    FlexDirection,
    /// <summary><c>flex-wrap</c>.</summary>
    FlexWrap,
    /// <summary><c>flex-flow</c>; shorthand for direction and wrap.</summary>
    FlexFlow,
    /// <summary><c>justify-content</c>.</summary>
    JustifyContent,
    /// <summary><c>align-items</c>.</summary>
    AlignItems,
    /// <summary><c>align-content</c>.</summary>
    AlignContent,
    /// <summary><c>align-self</c>.</summary>
    AlignSelf,
    /// <summary><c>flex-grow</c>.</summary>
    FlexGrow,
    /// <summary><c>flex-shrink</c>.</summary>
    FlexShrink,
    /// <summary><c>flex-basis</c>.</summary>
    FlexBasis,
    /// <summary><c>gap</c>; shorthand for row and column gaps.</summary>
    Gap,
    /// <summary><c>row-gap</c>.</summary>
    RowGap,
    /// <summary><c>column-gap</c>.</summary>
    ColumnGap,
    /// <summary><c>order</c>; changes flex/grid item painting and layout order.</summary>
    Order,
    /// <summary><c>grid</c>; shorthand for the template and auto properties.</summary>
    Grid,
    /// <summary><c>grid-template</c>; shorthand for rows, columns and areas.</summary>
    GridTemplate,
    /// <summary><c>grid-template-columns</c>.</summary>
    GridTemplateColumns,
    /// <summary><c>grid-template-rows</c>.</summary>
    GridTemplateRows,
    /// <summary><c>grid-template-areas</c>.</summary>
    GridTemplateAreas,
    /// <summary><c>grid-auto-columns</c>.</summary>
    GridAutoColumns,
    /// <summary><c>grid-auto-rows</c>.</summary>
    GridAutoRows,
    /// <summary><c>grid-auto-flow</c>; row, column or dense packing.</summary>
    GridAutoFlow,
    /// <summary><c>grid-column</c>; shorthand for start and end.</summary>
    GridColumn,
    /// <summary><c>grid-column-start</c>.</summary>
    GridColumnStart,
    /// <summary><c>grid-column-end</c>.</summary>
    GridColumnEnd,
    /// <summary><c>grid-row</c>; shorthand for start and end.</summary>
    GridRow,
    /// <summary><c>grid-row-start</c>.</summary>
    GridRowStart,
    /// <summary><c>grid-row-end</c>.</summary>
    GridRowEnd,
    /// <summary>
    /// <c>grid-area</c>; shorthand for row start/end and column start/end, or a
    /// named area.
    /// </summary>
    GridArea,
    /// <summary><c>justify-items</c>.</summary>
    JustifyItems,
    /// <summary><c>justify-self</c>.</summary>
    JustifySelf,
    /// <summary><c>place-items</c>; shorthand for <c>align-items</c> and <c>justify-items</c>.</summary>
    PlaceItems,
    /// <summary>
    /// <c>place-content</c>; shorthand for <c>align-content</c> and
    /// <c>justify-content</c>.
    /// </summary>
    PlaceContent,
    /// <summary><c>overflow</c>; shorthand for the x and y axes.</summary>
    Overflow,
    /// <summary><c>overflow-x</c>.</summary>
    OverflowX,
    /// <summary><c>overflow-y</c>.</summary>
    OverflowY,
    /// <summary><c>transform</c>.</summary>
    Transform,
    /// <summary><c>transform-origin</c>.</summary>
    TransformOrigin,
    /// <summary><c>box-shadow</c>.</summary>
    BoxShadow,
    /// <summary><c>outline</c>; shorthand for width, style and colour.</summary>
    Outline,
    /// <summary><c>outline-width</c>.</summary>
    OutlineWidth,
    /// <summary><c>outline-style</c>.</summary>
    OutlineStyle,
    /// <summary><c>outline-color</c>.</summary>
    OutlineColor,
    /// <summary><c>outline-offset</c>.</summary>
    OutlineOffset,
    /// <summary><c>filter</c>.</summary>
    Filter,
    /// <summary><c>backdrop-filter</c>.</summary>
    BackdropFilter,
    /// <summary><c>object-fit</c>.</summary>
    ObjectFit,
    /// <summary><c>object-position</c>.</summary>
    ObjectPosition,
    /// <summary><c>table-layout</c>.</summary>
    TableLayout,
    /// <summary><c>border-collapse</c>.</summary>
    BorderCollapse,
    /// <summary><c>border-spacing</c>; horizontal and vertical spacing between separate cells.</summary>
    BorderSpacing,
    /// <summary><c>column-count</c>; requested number of columns for multi-column layout.</summary>
    ColumnCount,
    /// <summary><c>scrollbar-color</c>; thumb and track colours.</summary>
    ScrollbarColor,
    /// <summary><c>scrollbar-width</c>; <c>auto</c>, <c>thin</c> or <c>none</c>.</summary>
    ScrollbarWidth,
    /// <summary><c>scroll-behavior</c>.</summary>
    ScrollBehavior,
    /// <summary><c>content</c>; generated content for pseudo-elements.</summary>
    Content,
    /// <summary><c>clip-path</c>.</summary>
    ClipPath,
    /// <summary><c>mask</c>; shorthand for mask image and related properties.</summary>
    Mask,
    /// <summary><c>mix-blend-mode</c>.</summary>
    MixBlendMode,
    /// <summary><c>isolation</c>; forces or prevents a stacking context for blending.</summary>
    Isolation,
    /// <summary><c>pointer-events</c>.</summary>
    PointerEvents,
    /// <summary><c>cursor</c>.</summary>
    Cursor,
    /// <summary><c>user-select</c>.</summary>
    UserSelect,
    /// <summary><c>will-change</c>; hints which properties may animate.</summary>
    WillChange,
    /// <summary><c>contain</c>; layout, paint and size containment hints.</summary>
    Contain,
    /// <summary>
    /// An id reserved for the <c>visibility: collapse</c> value, distinct from
    /// the <c>visibility</c> property.
    /// </summary>
    /// <remarks>This id is not present in the <see cref="CssPropertyIds"/> name map.</remarks>
    VisibilityCollapse,
    /// <summary><c>animation</c>; shorthand for all animation longhands.</summary>
    Animation,
    /// <summary><c>animation-name</c>.</summary>
    AnimationName,
    /// <summary><c>animation-duration</c>.</summary>
    AnimationDuration,
    /// <summary><c>animation-timing-function</c>.</summary>
    AnimationTimingFunction,
    /// <summary><c>animation-delay</c>.</summary>
    AnimationDelay,
    /// <summary><c>animation-iteration-count</c>.</summary>
    AnimationIterationCount,
    /// <summary><c>animation-direction</c>.</summary>
    AnimationDirection,
    /// <summary><c>animation-fill-mode</c>.</summary>
    AnimationFillMode,
    /// <summary><c>animation-play-state</c>.</summary>
    AnimationPlayState
}

/// <summary>
/// Maps interned CSS property names to <see cref="CssProperty"/> values.
/// </summary>
/// <remarks>
/// <para>
/// The stylesheet parser interns property names through
/// <see cref="Ignite.Parser.InternTable"/>, so a declaration carries only an
/// integer id. This class builds the id-to-property lookup once in its static
/// constructor and never mutates it afterwards, making all members thread safe.
/// </para>
/// <para>
/// Only the properties listed in <see cref="CssProperty"/> and registered in
/// the constructor table are recognised. <see cref="CssProperty.All"/> is a
/// registered pseudo-property; <see cref="CssProperty.Unknown"/> and
/// <see cref="CssProperty.VisibilityCollapse"/> intentionally have no name.
/// </para>
/// </remarks>
/// <seealso cref="CssProperty"/>
public static class CssPropertyIds
{
    /// <summary>
    /// Lookup from an interned property-name id to its property.
    /// </summary>
    /// <remarks>
    /// Keys are process-wide intern ids, not ordinal property numbers. The
    /// dictionary is fully populated during type initialisation and is read
    /// without locking afterwards.
    /// </remarks>
    private static readonly Dictionary<int, CssProperty> ById = [];

    /// <summary>The registered properties in table order, exposed through <see cref="All"/>.</summary>
    /// <remarks>
    /// This array excludes <see cref="CssProperty.Unknown"/> and
    /// <see cref="CssProperty.VisibilityCollapse"/> because neither has a name
    /// entry. It must not be mutated; <see cref="All"/> exposes it as a
    /// read-only span.
    /// </remarks>
    private static readonly CssProperty[] Values;

    /// <summary>
    /// Initialises the name table and interns every supported property name.
    /// </summary>
    /// <remarks>
    /// Runs once per process. <see cref="InternTable.Intern(string)"/> assigns
    /// stable ids, so the resulting dictionary remains valid for the lifetime
    /// of the process. Duplicate names would silently overwrite earlier
    /// entries; the table is assumed to be unique.
    /// </remarks>
    static CssPropertyIds()
    {
        var entries = new (string Name, CssProperty Property)[]
        {
            ("all", CssProperty.All),
            ("display", CssProperty.Display),
            ("position", CssProperty.Position),
            ("float", CssProperty.Float),
            ("clear", CssProperty.Clear),
            ("box-sizing", CssProperty.BoxSizing),
            ("visibility", CssProperty.Visibility),
            ("opacity", CssProperty.Opacity),
            ("z-index", CssProperty.ZIndex),
            ("width", CssProperty.Width),
            ("height", CssProperty.Height),
            ("min-width", CssProperty.MinWidth),
            ("min-height", CssProperty.MinHeight),
            ("max-width", CssProperty.MaxWidth),
            ("max-height", CssProperty.MaxHeight),
            ("aspect-ratio", CssProperty.AspectRatio),
            ("top", CssProperty.Top),
            ("right", CssProperty.Right),
            ("bottom", CssProperty.Bottom),
            ("left", CssProperty.Left),
            ("inset", CssProperty.Inset),
            ("margin", CssProperty.Margin),
            ("margin-top", CssProperty.MarginTop),
            ("margin-right", CssProperty.MarginRight),
            ("margin-bottom", CssProperty.MarginBottom),
            ("margin-left", CssProperty.MarginLeft),
            ("padding", CssProperty.Padding),
            ("padding-top", CssProperty.PaddingTop),
            ("padding-right", CssProperty.PaddingRight),
            ("padding-bottom", CssProperty.PaddingBottom),
            ("padding-left", CssProperty.PaddingLeft),
            ("border", CssProperty.Border),
            ("border-width", CssProperty.BorderWidth),
            ("border-style", CssProperty.BorderStyle),
            ("border-color", CssProperty.BorderColor),
            ("border-top", CssProperty.BorderTop),
            ("border-right", CssProperty.BorderRight),
            ("border-bottom", CssProperty.BorderBottom),
            ("border-left", CssProperty.BorderLeft),
            ("border-top-width", CssProperty.BorderTopWidth),
            ("border-right-width", CssProperty.BorderRightWidth),
            ("border-bottom-width", CssProperty.BorderBottomWidth),
            ("border-left-width", CssProperty.BorderLeftWidth),
            ("border-top-style", CssProperty.BorderTopStyle),
            ("border-right-style", CssProperty.BorderRightStyle),
            ("border-bottom-style", CssProperty.BorderBottomStyle),
            ("border-left-style", CssProperty.BorderLeftStyle),
            ("border-top-color", CssProperty.BorderTopColor),
            ("border-right-color", CssProperty.BorderRightColor),
            ("border-bottom-color", CssProperty.BorderBottomColor),
            ("border-left-color", CssProperty.BorderLeftColor),
            ("border-radius", CssProperty.BorderRadius),
            ("border-top-left-radius", CssProperty.BorderTopLeftRadius),
            ("border-top-right-radius", CssProperty.BorderTopRightRadius),
            ("border-bottom-right-radius", CssProperty.BorderBottomRightRadius),
            ("border-bottom-left-radius", CssProperty.BorderBottomLeftRadius),
            ("background", CssProperty.Background),
            ("background-color", CssProperty.BackgroundColor),
            ("background-image", CssProperty.BackgroundImage),
            ("background-size", CssProperty.BackgroundSize),
            ("background-position", CssProperty.BackgroundPosition),
            ("background-repeat", CssProperty.BackgroundRepeat),
            ("background-clip", CssProperty.BackgroundClip),
            ("background-origin", CssProperty.BackgroundOrigin),
            ("background-attachment", CssProperty.BackgroundAttachment),
            ("color", CssProperty.Color),
            ("font", CssProperty.Font),
            ("font-family", CssProperty.FontFamily),
            ("font-size", CssProperty.FontSize),
            ("font-weight", CssProperty.FontWeight),
            ("font-style", CssProperty.FontStyle),
            ("line-height", CssProperty.LineHeight),
            ("letter-spacing", CssProperty.LetterSpacing),
            ("text-align", CssProperty.TextAlign),
            ("text-decoration", CssProperty.TextDecoration),
            ("text-decoration-line", CssProperty.TextDecorationLine),
            ("text-transform", CssProperty.TextTransform),
            ("white-space", CssProperty.WhiteSpace),
            ("word-break", CssProperty.WordBreak),
            ("overflow-wrap", CssProperty.OverflowWrap),
            ("text-overflow", CssProperty.TextOverflow),
            ("direction", CssProperty.Direction),
            ("writing-mode", CssProperty.WritingMode),
            ("-webkit-line-clamp", CssProperty.WebkitLineClamp),
            ("flex", CssProperty.Flex),
            ("flex-direction", CssProperty.FlexDirection),
            ("flex-wrap", CssProperty.FlexWrap),
            ("flex-flow", CssProperty.FlexFlow),
            ("justify-content", CssProperty.JustifyContent),
            ("align-items", CssProperty.AlignItems),
            ("align-content", CssProperty.AlignContent),
            ("align-self", CssProperty.AlignSelf),
            ("flex-grow", CssProperty.FlexGrow),
            ("flex-shrink", CssProperty.FlexShrink),
            ("flex-basis", CssProperty.FlexBasis),
            ("gap", CssProperty.Gap),
            ("row-gap", CssProperty.RowGap),
            ("column-gap", CssProperty.ColumnGap),
            ("order", CssProperty.Order),
            ("grid", CssProperty.Grid),
            ("grid-template", CssProperty.GridTemplate),
            ("grid-template-columns", CssProperty.GridTemplateColumns),
            ("grid-template-rows", CssProperty.GridTemplateRows),
            ("grid-template-areas", CssProperty.GridTemplateAreas),
            ("grid-auto-columns", CssProperty.GridAutoColumns),
            ("grid-auto-rows", CssProperty.GridAutoRows),
            ("grid-auto-flow", CssProperty.GridAutoFlow),
            ("grid-column", CssProperty.GridColumn),
            ("grid-column-start", CssProperty.GridColumnStart),
            ("grid-column-end", CssProperty.GridColumnEnd),
            ("grid-row", CssProperty.GridRow),
            ("grid-row-start", CssProperty.GridRowStart),
            ("grid-row-end", CssProperty.GridRowEnd),
            ("grid-area", CssProperty.GridArea),
            ("justify-items", CssProperty.JustifyItems),
            ("justify-self", CssProperty.JustifySelf),
            ("place-items", CssProperty.PlaceItems),
            ("place-content", CssProperty.PlaceContent),
            ("overflow", CssProperty.Overflow),
            ("overflow-x", CssProperty.OverflowX),
            ("overflow-y", CssProperty.OverflowY),
            ("transform", CssProperty.Transform),
            ("transform-origin", CssProperty.TransformOrigin),
            ("box-shadow", CssProperty.BoxShadow),
            ("outline", CssProperty.Outline),
            ("outline-width", CssProperty.OutlineWidth),
            ("outline-style", CssProperty.OutlineStyle),
            ("outline-color", CssProperty.OutlineColor),
            ("outline-offset", CssProperty.OutlineOffset),
            ("filter", CssProperty.Filter),
            ("backdrop-filter", CssProperty.BackdropFilter),
            ("object-fit", CssProperty.ObjectFit),
            ("object-position", CssProperty.ObjectPosition),
            ("table-layout", CssProperty.TableLayout),
            ("border-collapse", CssProperty.BorderCollapse),
            ("border-spacing", CssProperty.BorderSpacing),
            ("column-count", CssProperty.ColumnCount),
            ("scrollbar-color", CssProperty.ScrollbarColor),
            ("scrollbar-width", CssProperty.ScrollbarWidth),
            ("scroll-behavior", CssProperty.ScrollBehavior),
            ("content", CssProperty.Content),
            ("clip-path", CssProperty.ClipPath),
            ("mask", CssProperty.Mask),
            ("mix-blend-mode", CssProperty.MixBlendMode),
            ("isolation", CssProperty.Isolation),
            ("pointer-events", CssProperty.PointerEvents),
            ("cursor", CssProperty.Cursor),
            ("user-select", CssProperty.UserSelect),
            ("will-change", CssProperty.WillChange),
            ("contain", CssProperty.Contain),
            ("animation", CssProperty.Animation),
            ("animation-name", CssProperty.AnimationName),
            ("animation-duration", CssProperty.AnimationDuration),
            ("animation-timing-function", CssProperty.AnimationTimingFunction),
            ("animation-delay", CssProperty.AnimationDelay),
            ("animation-iteration-count", CssProperty.AnimationIterationCount),
            ("animation-direction", CssProperty.AnimationDirection),
            ("animation-fill-mode", CssProperty.AnimationFillMode),
            ("animation-play-state", CssProperty.AnimationPlayState)
        };

        Values = new CssProperty[entries.Length];

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            ById[InternTable.Intern(entry.Name)] = entry.Property;
            Values[i] = entry.Property;
        }
    }

    /// <summary>Resolves an interned property-name id to a property.</summary>
    /// <param name="propertyId">Interned id produced by <see cref="Ignite.Parser.InternTable"/>.</param>
    /// <returns>
    /// The matching property, or <see cref="CssProperty.Unknown"/> when the
    /// name is unsupported.
    /// </returns>
    /// <remarks>
    /// This is a dictionary lookup and is safe for concurrent readers. The id
    /// must come from the same process-wide intern table; an id from another
    /// source cannot be interpreted.
    /// </remarks>
    public static CssProperty Get(int propertyId)
    {
        return ById.TryGetValue(propertyId, out var property) ? property : CssProperty.Unknown;
    }

    /// <summary>Gets every registered property.</summary>
    /// <value>
    /// A read-only span over the registration table, in declaration order.
    /// Excludes <see cref="CssProperty.Unknown"/> and
    /// <see cref="CssProperty.VisibilityCollapse"/>.
    /// </value>
    /// <remarks>
    /// The span aliases a static array, so it remains valid for the lifetime of
    /// the process and must not be mutated. Useful for enumeration, for example
    /// when building per-property caches.
    /// </remarks>
    public static ReadOnlySpan<CssProperty> All => Values;
}
