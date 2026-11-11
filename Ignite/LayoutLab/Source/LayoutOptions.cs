using Ignite.Data;
using Ignite.Data.Dom;

namespace Ignite.LayoutLab;

/// <summary>Configuration for one layout frame.</summary>
public sealed class LayoutOptions
{
    public float ViewportWidth { get; set; } = 1024;
    public float ViewportHeight { get; set; } = 768;
    public Orientation Orientation { get; set; } = Orientation.Horizontal;
    public float DevicePixelRatio { get; set; } = 1;
    public float RootFontSize { get; set; } = 16;
    public float DefaultFontSize { get; set; } = 16;

    /// <summary>Measured in CSS pixels. The default is a deterministic approximation.</summary>
    public ITextMeasurer? TextMeasurer { get; set; }

    public bool ClipToViewport { get; set; } = true;
    public bool Optimise { get; set; } = true;
    public bool CullOffscreen { get; set; } = true;
    public bool MergeDraws { get; set; } = true;
    public bool CompactConstants { get; set; }
    public bool IncludeFrameMarkers { get; set; }
    public bool EnableCaching { get; set; } = true;
    public bool ApplyScrollOffsets { get; set; } = true;
    public int MaxLayoutDepth { get; set; } = 512;

    /// <summary>Optional per-node scroll overrides; the node fields are used otherwise.</summary>
    public Dictionary<IgniteSupportedDomNode, Point>? ScrollOffsets { get; set; }

    /// <summary>The renderer environment used for media queries and container queries.</summary>
    public RendererContext? Context { get; set; }

    /// <summary>Provider for the text carried by a node. Defaults to the node's TextContent.</summary>
    public Func<IgniteSupportedDomNode, string?>? TextProvider { get; set; }

    /// <summary>
    /// Optional colour override for a node's text. The host uses this to paint
    /// placeholders with their own colour without the control's normal
    /// <c>color</c> (including <c>:focus</c> rules) affecting them.
    /// </summary>
    public Func<IgniteSupportedDomNode, string, uint?>? TextColorProvider { get; set; }

    /// <summary>
    /// Supplies the intrinsic (content) size of a replaced element such as an
    /// image. When a width or height is auto the returned value is used instead
    /// of filling the containing block.
    /// </summary>
    public Func<IgniteSupportedDomNode, Size?>? IntrinsicSizeProvider { get; set; }

    /// <summary>
    /// Supplies the bitmap drawn for a replaced element. The instruction is
    /// emitted after the element's background, border and outline and before
    /// its children, so overlays keep their natural paint order.
    /// </summary>
    public Func<LayoutBox, ImageSpec?>? ImageProvider { get; set; }

    /// <summary>
    /// Optional host painter for element-specific content such as form controls,
    /// progress bars and media placeholders. It runs once per visible box in
    /// paint order.
    /// </summary>
    public Action<LayoutBox, InstructionBuffer, ConstantPool>? CustomPainter { get; set; }

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

    public RendererContext CreateRendererContext()
    {
        var context = Context ?? new RendererContext();
        context.ViewportWidth = (int)MathF.Round(ViewportWidth);
        context.ViewportHeight = (int)MathF.Round(ViewportHeight);
        context.Orientation = Orientation;
        return context;
    }

    /// <summary>Everything that changes layout but is not encoded in the DOM.</summary>
    public long FrameSignature()
    {
        var hash = new HashCode();
        hash.Add(ViewportWidth);
        hash.Add(ViewportHeight);
        hash.Add(Orientation);
        hash.Add(RootFontSize);
        hash.Add(DefaultFontSize);
        hash.Add(DevicePixelRatio);
        hash.Add(ApplyScrollOffsets);

        if (ScrollOffsets is not null)
        {
            foreach (var pair in ScrollOffsets)
            {
                hash.Add(pair.Key);
                hash.Add(pair.Value);
            }
        }

        return hash.ToHashCode();
    }
}
