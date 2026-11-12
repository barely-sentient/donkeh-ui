using System.Runtime.CompilerServices;
using Donkeh.Data;
using Donkeh.Data.Dom;

namespace Donkeh.LayoutLab;

/// <summary>Configuration for one layout frame.</summary>
/// <remarks>
/// <para>
/// An instance is read at the start of <see cref="LayoutEngine.Layout(IgniteSupportedDomNode, LayoutOptions)"/>
/// and should not be mutated while that call is running. The engine keeps a
/// reference for the duration of the frame, so mutating it between frames is
/// the normal way to change viewport, scroll or animation state.
/// </para>
/// <para>
/// Dimensions are CSS pixels. <see cref="DevicePixelRatio"/> is a rendering
/// hint and does not scale layout geometry. Several options trade result
/// lifetime for throughput; see <see cref="SnapshotResults"/> and
/// <see cref="ReuseLayoutBoxes"/> before sharing a <see cref="LayoutResult"/>
/// across frames.
/// </para>
/// </remarks>
/// <seealso cref="LayoutResult"/>
/// <seealso cref="LayoutOptions.FrameSignature"/>
public sealed class LayoutOptions
{
    /// <summary>Gets or sets the viewport width in CSS pixels.</summary>
    /// <remarks>Basis for <c>vw</c>, <c>vmin</c> and <c>vmax</c>, and the initial containing block width.</remarks>
    public float ViewportWidth { get; set; } = 1024;

    /// <summary>Gets or sets the viewport height in CSS pixels.</summary>
    /// <remarks>Basis for <c>vh</c>, <c>vmin</c> and <c>vmax</c>, and the initial containing block height.</remarks>
    public float ViewportHeight { get; set; } = 768;

    /// <summary>
    /// Gets or sets whether the root element gets the browser's default window
    /// behaviour: viewport height when its height is auto, and vertical scrolling
    /// when its overflow is visible.
    /// </summary>
    /// <remarks>
    /// Off by default so the layout library can be used for embedded or
    /// content-sized roots. The window host enables it for application trees.
    /// </remarks>
    public bool RootScrolls { get; set; }

    /// <summary>Gets or sets the device orientation reported to media and container queries.</summary>
    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    /// <summary>Gets or sets the physical pixels per CSS pixel; defaults to 1.</summary>
    /// <remarks>
    /// The layout engine does not scale coordinates by this value. It is
    /// included in the engine's style-cache signature so a DPI change
    /// invalidates resolved styles; the host also uses it for rasterisation.
    /// </remarks>
    public float DevicePixelRatio { get; set; } = 1;

    /// <summary>Gets or sets the root font size in CSS pixels, the basis for <c>rem</c>.</summary>
    public float RootFontSize { get; set; } = 16;

    /// <summary>Gets or sets the font size assumed before the root style is resolved, in CSS pixels.</summary>
    /// <remarks>
    /// Also the <see cref="LengthContext.FontSize"/> of the root context
    /// created by <see cref="CreateRootLengthContext"/>.
    /// </remarks>
    public float DefaultFontSize { get; set; } = 16;

    /// <summary>Measured in CSS pixels. The default is a deterministic approximation.</summary>
    /// <remarks>
    /// When <see langword="null"/> the engine uses
    /// <see cref="SimpleTextMeasurer.Instance"/>. Cached text layouts include
    /// the measurer instance in their key, so swapping measurers on an existing
    /// engine cannot reuse layouts produced by the previous metrics.
    /// </remarks>
    public ITextMeasurer? TextMeasurer { get; set; }

    /// <summary>Gets or sets whether the viewport rectangle is the initial clip during optimisation.</summary>
    /// <remarks>When <see langword="false"/> the optimiser starts from an effectively unbounded clip of +/-1e7 px.</remarks>
    public bool ClipToViewport { get; set; } = true;

    /// <summary>Gets or sets whether the display list is optimised before it is returned.</summary>
    /// <remarks>
    /// When <see langword="false"/> <see cref="Optimiser.Run"/> returns the
    /// source buffer unchanged with empty statistics.
    /// </remarks>
    public bool Optimise { get; set; } = true;

    /// <summary>Gets or sets whether draws outside the active clip are removed when no transform or filter is active.</summary>
    public bool CullOffscreen { get; set; } = true;

    /// <summary>Gets or sets whether adjacent opaque rectangles with identical paint state are fused into one draw.</summary>
    public bool MergeDraws { get; set; } = true;

    /// <summary>Gets or sets whether the constant pool is rebuilt so it only contains referenced constants.</summary>
    /// <remarks>Compaction renumbers payloads in the returned instruction buffer and allocates a new pool.</remarks>
    public bool CompactConstants { get; set; }

    /// <summary>Gets or sets whether <c>BeginFrame</c> and <c>EndFrame</c> markers survive optimisation.</summary>
    public bool IncludeFrameMarkers { get; set; }

    /// <summary>Gets or sets the master switch for the engine's style and text layout caches.</summary>
    /// <remarks>
    /// When disabled the engine neither reads nor writes its style cache or text
    /// layout cache, which makes it useful for measuring uncached cost.
    /// Geometry is unchanged; <see cref="CacheTextLayouts"/> is ignored while
    /// this is off. Parsed-value caching in <see cref="StyleValueCache"/> is not
    /// affected.
    /// </remarks>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Gets or sets a host-owned counter that changes whenever interaction state
    /// (<c>:hover</c>, <c>:active</c>, <c>:focus</c>, …) changes.
    /// </summary>
    /// <remarks>
    /// Interaction state lives on the DOM nodes, but a state change on one node
    /// can affect a different node through sibling or descendant selectors. The
    /// style cache therefore includes this counter so a focused input can refresh
    /// its sibling label without bumping every node's revision.
    /// </remarks>
    public long InteractionVersion { get; set; }

    /// <summary>
    /// Reuses <see cref="LayoutBox"/> instances across layout passes. The box
    /// tree is mutated in place, so leave this off when a previous
    /// <see cref="LayoutResult"/> must remain an immutable snapshot.
    /// </summary>
    /// <remarks>
    /// Cached boxes keep their <see cref="LayoutBox.Node"/> identity and child
    /// list capacity; <see cref="LayoutBox.Reset"/> clears all derived state
    /// before the box is laid out again. <see cref="SnapshotResults"/> cannot
    /// make a reused box tree immutable because only the instruction stream and
    /// node map are copied.
    /// </remarks>
    public bool ReuseLayoutBoxes { get; set; }

    /// <summary>
    /// Copies the display list and node map into the <see cref="LayoutResult"/>.
    /// Leave this on when a result must stay valid after the next layout pass.
    /// Hosts that consume the result synchronously can turn it off to avoid
    /// copying the instruction stream and node map every frame.
    /// </summary>
    /// <remarks>
    /// With snapshotting enabled the instruction stream, constant pool and node
    /// map belong to the result. The box tree is still owned by the engine and
    /// may be mutated by a later layout, so treat it as read-only.
    /// </remarks>
    public bool SnapshotResults { get; set; } = true;

    /// <summary>
    /// Caches shaped text layouts by text/style/width. Hosts that render highly
    /// dynamic text (a live performance monitor, for example) can disable this
    /// to keep the cache from retaining thousands of one-off strings.
    /// </summary>
    /// <remarks>
    /// The cache keeps two generations of at most 1024 entries each; see
    /// <see cref="TextLayoutEngine.Layout"/>. Disabling it only affects the
    /// text layout cache, not style resolution.
    /// </remarks>
    public bool CacheTextLayouts { get; set; } = true;

    /// <summary>Gets or sets whether scroll offsets are applied while building the display list.</summary>
    /// <remarks>
    /// When <see langword="false"/> both <see cref="GetScrollOffsetX"/> and
    /// <see cref="GetScrollOffsetY"/> return zero, so layout and painting behave
    /// as if the document were scrolled to the origin; node fields are not
    /// modified.
    /// </remarks>
    public bool ApplyScrollOffsets { get; set; } = true;

    /// <summary>Gets or sets the maximum box-tree recursion depth; defaults to 512.</summary>
    /// <remarks>
    /// Values below 1 are clamped to 1. A node at the limit still receives a
    /// box, but its children are not visited.
    /// </remarks>
    public int MaxLayoutDepth { get; set; } = 512;

    /// <summary>Enables evaluation of CSS animations during layout.</summary>
    public bool AnimationsEnabled { get; set; } = true;

    /// <summary>Monotonic animation clock in seconds.</summary>
    /// <remarks>
    /// The engine never reads the wall clock; advancing this value between
    /// frames drives the animation runtime. It is intentionally not
    /// part of <see cref="FrameSignature"/> because animations change style
    /// values rather than layout inputs.
    /// </remarks>
    public double AnimationTimeSeconds { get; set; }

    /// <summary>Optional per-node scroll overrides; the node fields are used otherwise.</summary>
    /// <remarks>
    /// Values are in CSS pixels and take precedence over
    /// <c>ScrollOffsetX</c>/<c>ScrollOffsetY</c> on the node when
    /// <see cref="ApplyScrollOffsets"/> is enabled.
    /// </remarks>
    public Dictionary<IgniteSupportedDomNode, Point>? ScrollOffsets { get; set; }

    /// <summary>The renderer environment used for media queries and container queries.</summary>
    /// <remarks>
    /// <see cref="CreateRendererContext"/> mutates and returns this instance when
    /// it is supplied, writing the rounded viewport size and orientation into
    /// it. A new context is allocated when the property is <see langword="null"/>.
    /// </remarks>
    public RendererContext? Context { get; set; }

    /// <summary>Provider for the text carried by a node. Defaults to the node's TextContent.</summary>
    /// <remarks>
    /// Called once per element that is considered for text layout; returning
    /// <see langword="null"/> means no text.
    /// </remarks>
    public Func<IgniteSupportedDomNode, string?>? TextProvider { get; set; }

    /// <summary>
    /// Optional colour override for a node's text. The host uses this to paint
    /// placeholders with their own colour without the control's normal
    /// <c>color</c> (including <c>:focus</c> rules) affecting them.
    /// </summary>
    /// <remarks>
    /// The delegate receives the node and the text about to be laid out and
    /// returns a packed ARGB colour, or <see langword="null"/> to keep the
    /// computed <see cref="ComputedStyle.Color"/>.
    /// </remarks>
    public Func<IgniteSupportedDomNode, string, uint?>? TextColorProvider { get; set; }

    /// <summary>
    /// Supplies the intrinsic (content) size of a replaced element such as an
    /// image. When a width or height is auto the returned value is used instead
    /// of filling the containing block.
    /// </summary>
    /// <remarks>The size is in CSS pixels and is used for both axes independently when only one is auto.</remarks>
    public Func<IgniteSupportedDomNode, Size?>? IntrinsicSizeProvider { get; set; }

    /// <summary>
    /// Supplies the bitmap drawn for a replaced element. The instruction is
    /// emitted after the element's background, border and outline and before
    /// its children, so overlays keep their natural paint order.
    /// </summary>
    /// <remarks>Returning <see langword="null"/> omits the image; the content box is still laid out.</remarks>
    public Func<LayoutBox, ImageSpec?>? ImageProvider { get; set; }

    /// <summary>
    /// Optional host painter for element-specific content such as form controls,
    /// progress bars and media placeholders. It runs once per visible box in
    /// paint order.
    /// </summary>
    /// <remarks>
    /// The callback may append instructions and constants; it runs while the
    /// engine's clip/opacity scopes are active, so emitted draws inherit them.
    /// </remarks>
    public Action<LayoutBox, InstructionBuffer, ConstantPool>? CustomPainter { get; set; }

    /// <summary>Gets the horizontal scroll offset to use for <paramref name="node"/>.</summary>
    /// <param name="node">The scroll container node.</param>
    /// <returns>The offset in CSS pixels, or 0 when <see cref="ApplyScrollOffsets"/> is disabled.</returns>
    /// <remarks>A matching <see cref="ScrollOffsets"/> entry wins over the node's own <c>ScrollOffsetX</c>.</remarks>
    public float GetScrollOffsetX(IgniteSupportedDomNode node)
    {
        if (!ApplyScrollOffsets)
        {
            return 0;
        }

        if (ScrollOffsets is not null && ScrollOffsets.TryGetValue(node, out var point))
        {
            return point.X;
        }

        return node.ScrollOffsetX;
    }

    /// <summary>Gets the vertical scroll offset to use for <paramref name="node"/>.</summary>
    /// <param name="node">The scroll container node.</param>
    /// <returns>The offset in CSS pixels, or 0 when <see cref="ApplyScrollOffsets"/> is disabled.</returns>
    /// <remarks>A matching <see cref="ScrollOffsets"/> entry wins over the node's own <c>ScrollOffsetY</c>.</remarks>
    public float GetScrollOffsetY(IgniteSupportedDomNode node)
    {
        if (!ApplyScrollOffsets)
        {
            return 0;
        }

        if (ScrollOffsets is not null && ScrollOffsets.TryGetValue(node, out var point))
        {
            return point.Y;
        }

        return node.ScrollOffsetY;
    }

    /// <summary>Creates the length context used for the root element.</summary>
    /// <returns>
    /// A context whose font size is <see cref="DefaultFontSize"/>, root font
    /// size is <see cref="RootFontSize"/>, viewport is
    /// <see cref="ViewportWidth"/> by <see cref="ViewportHeight"/>, percentage
    /// basis is the viewport width and available space is the full viewport.
    /// </returns>
    /// <remarks>Unitless and percentage values on the root resolve against the viewport, not against a parent box.</remarks>
    public LengthContext CreateRootLengthContext()
    {
        return new LengthContext(
            DefaultFontSize,
            RootFontSize,
            ViewportWidth,
            ViewportHeight,
            ViewportWidth,
            ViewportWidth,
            ViewportHeight);
    }

    /// <summary>Creates or updates the renderer context for this frame.</summary>
    /// <returns>The supplied <see cref="Context"/>, or a newly allocated context when it is <see langword="null"/>.</returns>
    /// <remarks>
    /// The viewport dimensions are rounded to the nearest integer and written
    /// into the context together with <see cref="Orientation"/>. Because the
    /// supplied instance is mutated, callers should not share one context
    /// between concurrent frames.
    /// </remarks>
    public RendererContext CreateRendererContext()
    {
        var context = Context ?? new RendererContext();
        context.ViewportWidth = (int)MathF.Round(ViewportWidth);
        context.ViewportHeight = (int)MathF.Round(ViewportHeight);
        context.Orientation = Orientation;
        return context;
    }

    /// <summary>Everything that changes frame output but is not encoded in the DOM.</summary>
    /// <returns>A process-local hash of the output-relevant option values.</returns>
    /// <remarks>
    /// <para>
    /// The hash covers the viewport size, orientation, fonts, device pixel ratio,
    /// scroll settings and overrides, layout depth, the provider and painter
    /// delegates (by identity), the animation-enable flag and every
    /// display-list and caching flag. This makes it safe as a frame cache key:
    /// changing any input that can change geometry or instructions changes the
    /// result.
    /// </para>
    /// <para>
    /// <see cref="AnimationTimeSeconds"/> and the mutable state reached through
    /// delegates are intentionally not hashed; advance the clock or change the
    /// DOM between frames and key those changes separately. The value is stable
    /// only within one process.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var options = new LayoutOptions { ViewportWidth = 1280, ViewportHeight = 720 };
    /// long signature = options.FrameSignature();
    /// // Reuse cached style/box data only while signature stays the same.
    /// </code>
    /// </example>
    public long FrameSignature()
    {
        var hash = new HashCode();
        hash.Add(ViewportWidth);
        hash.Add(ViewportHeight);
        hash.Add(RootScrolls);
        hash.Add(Orientation);
        hash.Add(RootFontSize);
        hash.Add(DefaultFontSize);
        hash.Add(DevicePixelRatio);
        hash.Add(ApplyScrollOffsets);
        hash.Add(MaxLayoutDepth);
        hash.Add(AnimationsEnabled);
        hash.Add(InteractionVersion);

        if (ScrollOffsets is not null)
        {
            foreach (var pair in ScrollOffsets)
            {
                hash.Add(pair.Key);
                hash.Add(pair.Value);
            }
        }

        // Delegates are compared by identity: a different provider can produce
        // different text, intrinsic sizes or paint output even for the same DOM.
        hash.Add(TextMeasurer is null ? 0 : RuntimeHelpers.GetHashCode(TextMeasurer));
        hash.Add(TextProvider is null ? 0 : RuntimeHelpers.GetHashCode(TextProvider));
        hash.Add(TextColorProvider is null ? 0 : RuntimeHelpers.GetHashCode(TextColorProvider));
        hash.Add(IntrinsicSizeProvider is null ? 0 : RuntimeHelpers.GetHashCode(IntrinsicSizeProvider));
        hash.Add(ImageProvider is null ? 0 : RuntimeHelpers.GetHashCode(ImageProvider));
        hash.Add(CustomPainter is null ? 0 : RuntimeHelpers.GetHashCode(CustomPainter));

        hash.Add(Optimise);
        hash.Add(ClipToViewport);
        hash.Add(CullOffscreen);
        hash.Add(MergeDraws);
        hash.Add(CompactConstants);
        hash.Add(IncludeFrameMarkers);
        hash.Add(EnableCaching);
        hash.Add(CacheTextLayouts);
        hash.Add(ReuseLayoutBoxes);
        hash.Add(SnapshotResults);

        return hash.ToHashCode();
    }
}
