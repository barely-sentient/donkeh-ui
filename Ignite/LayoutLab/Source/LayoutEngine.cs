using System.Globalization;
using System.Text;
using Ignite.Data;
using Ignite.Data.Dom;

namespace Ignite.LayoutLab;

/// <summary>
/// Converts the matched CSS rules of a DOM tree into positioned layout boxes
/// and a compact paint-instruction stream.
/// </summary>
/// <remarks>
/// <para>
/// One engine instance is reusable across frames. It owns the style/text caches;
/// caches are invalidated by the DOM revision, the containing style identity,
/// the renderer context version and the layout viewport.
/// </para>
/// <para>
/// The layout tree keeps unscrolled absolute coordinates. Scroll offsets are
/// clamped per scroll container and applied by the display-list builder, which
/// makes scrolling a paint-only operation.
/// </para>
/// </remarks>
public sealed partial class LayoutEngine
{
    private readonly DataTable _table;
    private readonly StyleValueCache _valueCache;
    private readonly Dictionary<IgniteSupportedDomNode, StyleCacheEntry> _styleCache = [];
    private readonly Dictionary<TextLayoutKey, TextLayout> _textCache = [];
    private readonly Dictionary<IgniteSupportedDomNode, LayoutBox> _byNode = [];
    private readonly List<LayoutBox> _absolutes = [];
    private readonly HashSet<LayoutBox> _absoluteLaidOut = [];

    private MatchedRule[] _matchBuffer = new MatchedRule[16];
    private LayoutOptions _options = new();
    private StyleResolver _resolver = null!;
    private LengthContext _baseContext;
    private RendererContext _rendererContext = new();
    private ITextMeasurer _measurer = SimpleTextMeasurer.Instance;
    private long _styleSignature;
    private long _contextVersion;

    private InstructionBuffer _instructions = new();
    private ConstantPool _constants = new();
    private LayoutBox? _root;
    private int _maxDepth = 512;

    public LayoutEngine(DataTable table, StyleValueCache? valueCache = null)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _valueCache = valueCache ?? new StyleValueCache();
    }

    public StyleValueCache ValueCache => _valueCache;

    public LayoutResult Layout(IgniteSupportedDomNode root)
    {
        return Layout(root, null);
    }

    /// <summary>Lays out a frame, reusing the engine's style and text caches.</summary>
    public LayoutResult Layout(IgniteSupportedDomNode root, LayoutOptions? options)
    {
        ArgumentNullException.ThrowIfNull(root);

        _options = options ?? new LayoutOptions();
        _maxDepth = Math.Max(1, _options.MaxLayoutDepth);
        _baseContext = _options.CreateRootLengthContext();
        _rendererContext = _options.CreateRendererContext();
        _contextVersion = _rendererContext.Version;
        _styleSignature = ComputeStyleSignature();
        _measurer = _options.TextMeasurer ?? SimpleTextMeasurer.Instance;
        _resolver = new StyleResolver(_table, _valueCache, _options);

        _instructions.Clear();
        _constants = new ConstantPool();
        _byNode.Clear();
        _absolutes.Clear();
        _absoluteLaidOut.Clear();

        _root = BuildBox(root, null, null, null, 0);

        var viewport = new Rect(0, 0, MathF.Max(0, _options.ViewportWidth), MathF.Max(0, _options.ViewportHeight));

        if (_root is not null)
        {
            LayoutRootBox(_root, viewport);
            ProcessAbsoluteBoxes(viewport);
            BuildDisplayList(_root, viewport);
        }

        var frame = _options.Optimise
            ? Optimiser.Run(_instructions, _constants, viewport, _options)
            : new OptimisationResult(_instructions, _constants, OptimisationStats.Empty);

        return new LayoutResult(_root, frame.Instructions, frame.Constants, _byNode, frame.Stats);
    }

    /// <summary>Drops every cached style and text layout.</summary>
    public void ClearCaches()
    {
        _styleCache.Clear();
        _textCache.Clear();
        _valueCache.Clear();
    }

    private long ComputeStyleSignature()
    {
        var hash = new HashCode();
        hash.Add(_options.ViewportWidth);
        hash.Add(_options.ViewportHeight);
        hash.Add(_options.Orientation);
        hash.Add(_options.RootFontSize);
        hash.Add(_options.DefaultFontSize);
        hash.Add(_options.DevicePixelRatio);
        hash.Add(_contextVersion);
        return hash.ToHashCode();
    }

    // ---------------------------------------------------------------- styles

    private sealed class StyleCacheEntry
    {
        public required ComputedStyle Style;
        public int Revision;
        public ComputedStyle? Parent;
        public long Signature;
        public long ContextVersion;
    }

    private ComputedStyle ResolveStyle(IgniteSupportedDomNode node, ComputedStyle? parent)
    {
        if (_styleCache.TryGetValue(node, out var entry)
            && entry.Revision == node.Revision
            && ReferenceEquals(entry.Parent, parent)
            && entry.Signature == _styleSignature
            && entry.ContextVersion == _contextVersion)
        {
            return entry.Style;
        }

        var count = _table.MatchRules(node, _rendererContext, _matchBuffer);

        if (count > _matchBuffer.Length)
        {
            Array.Resize(ref _matchBuffer, Math.Max(count, _matchBuffer.Length * 2));
            count = _table.MatchRules(node, _rendererContext, _matchBuffer);
        }

        var style = _resolver.Resolve(node, parent, _matchBuffer.AsSpan(0, count));

        _styleCache[node] = new StyleCacheEntry
        {
            Style = style,
            Revision = node.Revision,
            Parent = parent,
            Signature = _styleSignature,
            ContextVersion = _contextVersion
        };

        return style;
    }

    private string? GetNodeText(IgniteSupportedDomNode node)
    {
        return _options.TextProvider is { } provider ? provider(node) : node.TextContent;
    }

    // -------------------------------------------------------------- box tree

    private LayoutBox? BuildBox(
        IgniteSupportedDomNode node,
        ComputedStyle? parentStyle,
        LayoutBox? parentBox,
        LayoutBox? positionedAncestor,
        int depth)
    {
        var style = ResolveStyle(node, parentStyle);

        if (style.Display == Display.None)
        {
            return null;
        }

        var box = new LayoutBox(node, style, depth)
        {
            Parent = parentBox,
            IsContents = style.Display == Display.Contents
        };

        if (style.IsAbsolutelyPositioned)
        {
            box.ContainingBlock = style.Position == Position.Fixed ? null : positionedAncestor;
            _absolutes.Add(box);
        }

        _byNode[node] = box;

        if (depth >= _maxDepth)
        {
            return box;
        }

        var childPositionedAncestor = CreatesContainingBlock(style) ? box : positionedAncestor;
        var children = node.Children;

        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];

            if (child is null)
            {
                continue;
            }

            var childBox = BuildBox(child, style, box, childPositionedAncestor, depth + 1);

            if (childBox is not null)
            {
                box.Children.Add(childBox);
            }
        }

        return box;
    }

    private static bool CreatesContainingBlock(ComputedStyle style)
    {
        return style.Position is not Position.Static || style.Transform is not null;
    }

    private void LayoutRootBox(LayoutBox root, Rect viewport)
    {
        if (root.IsContents)
        {
            root.Margin = Thickness.Zero;
            root.Border = Thickness.Zero;
            root.Padding = Thickness.Zero;
            root.ApplyBorderBox(viewport.X, viewport.Y, viewport.Width, viewport.Height);
            var height = LayoutContent(root, root.ContentBox, float.NaN);
            root.ApplyBorderBox(viewport.X, viewport.Y, viewport.Width, MathF.Max(viewport.Height, height));
            UpdateScrollMetrics(root);
            return;
        }

        var borderWidth = PrepareAndMeasureWidth(
            root,
            viewport.Width,
            forcedBorderWidth: null,
            fillWidth: true,
            shrinkToFit: false);

        var borderX = viewport.X + root.Margin.Left;
        var borderY = viewport.Y + root.Margin.Top;

        LayoutPreparedBox(
            root,
            borderX,
            borderY,
            viewport.Width,
            viewport.Height,
            forcedBorderHeight: null,
            fillHeight: false,
            definiteContentHeight: null);
    }

    // -------------------------------------------------------------- box sizing

    private void PrepareBox(LayoutBox box, float cbWidth)
    {
        var style = box.Style;
        var context = CreateContext(style);

        box.Padding = style.Padding.Resolve(in context, cbWidth);
        box.Border = ResolveBorders(style, in context, cbWidth);
        box.Margin = style.Margin.Resolve(in context, cbWidth);
    }

    private LengthContext CreateContext(ComputedStyle style)
    {
        var fontSize = style.ResolveFontSize(in _baseContext);
        return _baseContext with
        {
            FontSize = fontSize,
            PercentBasis = fontSize
        };
    }

    private static Thickness ResolveBorders(ComputedStyle style, in LengthContext context, float cbWidth)
    {
        var widths = style.BorderWidth.Resolve(in context, cbWidth);

        return new Thickness(
            style.BorderStyleTop is BorderStyle.None or BorderStyle.Hidden ? 0 : widths.Top,
            style.BorderStyleRight is BorderStyle.None or BorderStyle.Hidden ? 0 : widths.Right,
            style.BorderStyleBottom is BorderStyle.None or BorderStyle.Hidden ? 0 : widths.Bottom,
            style.BorderStyleLeft is BorderStyle.None or BorderStyle.Hidden ? 0 : widths.Left);
    }

    private float PrepareAndMeasureWidth(
        LayoutBox box,
        float cbWidth,
        float? forcedBorderWidth,
        bool fillWidth,
        bool shrinkToFit,
        bool allowAutoMargins = true)
    {
        PrepareBox(box, cbWidth);

        var style = box.Style;
        var context = CreateContext(style);
        var paddingWidth = box.Padding.Horizontal;
        var borderWidth = box.Border.Horizontal;
        var marginWidth = box.Margin.Horizontal;
        var width = forcedBorderWidth;

        if (width is null)
        {
            var resolved = style.Width.ResolveOr(in context, cbWidth, float.NaN);

            if (float.IsFinite(resolved))
            {
                width = style.BoxSizing == BoxSizing.BorderBox
                    ? resolved
                    : resolved + paddingWidth + borderWidth;
            }
            else if (_options.IntrinsicSizeProvider?.Invoke(box.Node) is { } intrinsic)
            {
                width = style.BoxSizing == BoxSizing.BorderBox
                    ? intrinsic.Width
                    : intrinsic.Width + paddingWidth + borderWidth;
            }
            else if (fillWidth)
            {
                width = MathF.Max(0, cbWidth - marginWidth);
            }
            else if (shrinkToFit)
            {
                var available = MathF.Max(0, cbWidth - marginWidth);
                var maxContent = MeasureMaxContentWidth(box);
                width = MathF.Min(maxContent, available);
            }
            else
            {
                width = MathF.Max(0, cbWidth - marginWidth);
            }
        }

        width = ClampBorderWidth(style, in context, cbWidth, width.Value);

        if (allowAutoMargins)
        {
            ResolveAutoMargins(box, cbWidth, width.Value);
        }

        box.ApplyBorderBox(0, 0, width.Value, 0);
        return width.Value;
    }

    private float? ResolveBorderHeight(
        LayoutBox box,
        float cbWidth,
        float cbHeight,
        float? forcedBorderHeight,
        bool fillHeight)
    {
        var style = box.Style;
        var context = CreateContext(style);
        var verticalExtra = box.Padding.Vertical + box.Border.Vertical;
        float? height = forcedBorderHeight;

        if (height is null)
        {
            var resolved = style.Height.ResolveOr(in context, cbHeight, float.NaN);

            if (float.IsFinite(resolved))
            {
                height = style.BoxSizing == BoxSizing.BorderBox
                    ? resolved
                    : resolved + verticalExtra;
            }
            else if (_options.IntrinsicSizeProvider?.Invoke(box.Node) is { } intrinsic)
            {
                height = style.BoxSizing == BoxSizing.BorderBox
                    ? intrinsic.Height
                    : intrinsic.Height + verticalExtra;
            }
            else if (fillHeight && float.IsFinite(cbHeight))
            {
                height = MathF.Max(0, cbHeight - box.Margin.Vertical);
            }
        }

        if (height is { } value)
        {
            var minHeight = style.MinHeight.ResolveOr(in context, cbHeight, 0);
            var maxHeight = style.MaxHeight.IsAuto
                ? float.PositiveInfinity
                : style.MaxHeight.ResolveOr(in context, cbHeight, float.PositiveInfinity);

            if (style.BoxSizing == BoxSizing.ContentBox)
            {
                minHeight += verticalExtra;
                if (float.IsFinite(maxHeight))
                {
                    maxHeight += verticalExtra;
                }
            }

            height = MathF.Max(minHeight, MathF.Min(value, maxHeight));
        }

        return height;
    }

    private static float ClampBorderWidth(
        ComputedStyle style,
        in LengthContext context,
        float cbWidth,
        float width)
    {
        var extra = 0f;

        if (style.BoxSizing == BoxSizing.ContentBox)
        {
            extra = style.Padding.Resolve(in context, cbWidth).Horizontal
                + style.BorderWidth.Resolve(in context, cbWidth).Horizontal;
        }

        var min = style.MinWidth.ResolveOr(in context, cbWidth, 0) + extra;
        var max = style.MaxWidth.IsAuto
            ? float.PositiveInfinity
            : style.MaxWidth.ResolveOr(in context, cbWidth, float.PositiveInfinity) + extra;

        return MathF.Max(0, MathF.Max(min, MathF.Min(width, max)));
    }

    private static void ResolveAutoMargins(LayoutBox box, float cbWidth, float borderWidth)
    {
        var style = box.Style;
        var leftAuto = style.Margin.Left.IsAuto;
        var rightAuto = style.Margin.Right.IsAuto;

        if (!leftAuto && !rightAuto)
        {
            return;
        }

        var remaining = cbWidth - borderWidth - box.Margin.Horizontal;

        if (remaining <= 0)
        {
            return;
        }

        if (leftAuto && rightAuto)
        {
            box.Margin = box.Margin with { Left = remaining * 0.5f, Right = remaining * 0.5f };
        }
        else if (leftAuto)
        {
            box.Margin = box.Margin with { Left = remaining };
        }
        else
        {
            box.Margin = box.Margin with { Right = remaining };
        }
    }

    /// <summary>
    /// Lays out a box whose width and edges have already been resolved by
    /// <see cref="PrepareAndMeasureWidth"/>. Returns the used border-box height.
    /// </summary>
    private float LayoutPreparedBox(
        LayoutBox box,
        float borderX,
        float borderY,
        float cbWidth,
        float cbHeight,
        float? forcedBorderHeight,
        bool fillHeight,
        float? definiteContentHeight)
    {
        var height = forcedBorderHeight;

        if (height is null && definiteContentHeight is { } content)
        {
            height = content + box.Padding.Vertical + box.Border.Vertical;
        }

        height ??= ResolveBorderHeight(box, cbWidth, cbHeight, null, fillHeight);

        if (height is null
            && box.Style.AspectRatio is { } aspectRatio
            && aspectRatio > 0)
        {
            var width = box.BorderBox.Width;
            height = box.Style.BoxSizing == BoxSizing.BorderBox
                ? width / aspectRatio
                : MathF.Max(0, width - box.Padding.Horizontal - box.Border.Horizontal) / aspectRatio
                    + box.Padding.Vertical + box.Border.Vertical;
        }

        var startHeight = height ?? 0;
        box.ApplyBorderBox(borderX, borderY, box.BorderBox.Width, startHeight);

        var usedContentHeight = LayoutContent(box, box.ContentBox, height is { } definite
            ? definite - box.Padding.Vertical - box.Border.Vertical
            : float.NaN);

        if (height is null)
        {
            startHeight = usedContentHeight + box.Padding.Vertical + box.Border.Vertical;
        }

        box.ApplyBorderBox(borderX, borderY, box.BorderBox.Width, startHeight);
        box.Baseline = BaselineOf(box);
        ResolveRadius(box);
        UpdateScrollMetrics(box);
        ApplyRelativeOffset(box);

        return box.BorderBox.Height;
    }

    private void ResolveRadius(LayoutBox box)
    {
        var style = box.Style;

        if (style.BorderRadius.TopLeft.IsAuto
            && style.BorderRadius.TopRight.IsAuto
            && style.BorderRadius.BottomRight.IsAuto
            && style.BorderRadius.BottomLeft.IsAuto)
        {
            box.Radius = CornerRadius.Zero;
            return;
        }

        var context = CreateContext(style);
        box.Radius = style.BorderRadius.Resolve(in context, box.BorderBox.Width, box.BorderBox.Height);
    }

    private static float BaselineOf(LayoutBox box)
    {
        if (box.Text is { Lines.Length: > 0 } text)
        {
            return MathF.Max(0, text.Lines[0].Y + text.Lines[0].Baseline);
        }

        for (var i = 0; i < box.Children.Count; i++)
        {
            var child = box.Children[i];

            if (!child.IsOutOfFlow && child.BorderBox.Height > 0)
            {
                return child.BorderBox.Bottom - box.BorderBox.Y;
            }
        }

        return box.BorderBox.Height;
    }

    private void ApplyRelativeOffset(LayoutBox box)
    {
        if (box.Style.Position is not (Position.Relative or Position.Sticky))
        {
            return;
        }

        var context = CreateContext(box.Style);
        var parent = box.Parent;

        var widthBasis = (parent?.ContentBox.Width ?? _options.ViewportWidth);
        var heightBasis = (parent?.ContentBox.Height ?? _options.ViewportHeight);

        var x = box.Style.Left.ResolveOr(in context, widthBasis, float.NaN);
        var y = box.Style.Top.ResolveOr(in context, heightBasis, float.NaN);

        if (!float.IsFinite(x))
        {
            x = -box.Style.Right.ResolveOr(in context, widthBasis, 0);
        }

        if (!float.IsFinite(y))
        {
            y = -box.Style.Bottom.ResolveOr(in context, heightBasis, 0);
        }

        box.Translate(x, y);
    }

    // ------------------------------------------------------------- scrolling

    private void UpdateScrollMetrics(LayoutBox box)
    {
        var content = box.ContentBox;
        var maxRight = content.Right;
        var maxBottom = content.Bottom;

        for (var i = 0; i < box.Children.Count; i++)
        {
            var child = box.Children[i];
            var bounds = child.MarginBox.Width > 0 || child.MarginBox.Height > 0
                ? child.MarginBox
                : child.BorderBox;

            maxRight = MathF.Max(maxRight, bounds.Right);
            maxBottom = MathF.Max(maxBottom, bounds.Bottom);
        }

        box.ScrollWidth = MathF.Max(content.Width, maxRight - content.X);
        box.ScrollHeight = MathF.Max(content.Height, maxBottom - content.Y);
        box.IsScrollContainer = box.Style.IsScrollContainer;

        if (!box.IsScrollContainer)
        {
            box.ScrollOffsetX = 0;
            box.ScrollOffsetY = 0;
            return;
        }

        var maxX = MathF.Max(0, box.ScrollWidth - box.PaddingBox.Width);
        var maxY = MathF.Max(0, box.ScrollHeight - box.PaddingBox.Height);

        box.ScrollOffsetX = Math.Clamp(_options.GetScrollOffsetX(box.Node), 0, maxX);
        box.ScrollOffsetY = Math.Clamp(_options.GetScrollOffsetY(box.Node), 0, maxY);
    }

    // -------------------------------------------------------------- absolutes

    private void ProcessAbsoluteBoxes(Rect viewport)
    {
        for (var i = 0; i < _absolutes.Count; i++)
        {
            var box = _absolutes[i];

            if (!_absoluteLaidOut.Add(box))
            {
                continue;
            }

            var containingBlock = box.ContainingBlock?.PaddingBox ?? viewport;
            LayoutAbsoluteBox(box, containingBlock);
        }
    }

    private void LayoutAbsoluteBox(LayoutBox box, Rect containingBlock)
    {
        var style = box.Style;
        var context = CreateContext(style);

        PrepareBox(box, containingBlock.Width);

        var left = style.Left.ResolveOr(in context, containingBlock.Width, float.NaN);
        var right = style.Right.ResolveOr(in context, containingBlock.Width, float.NaN);
        var top = style.Top.ResolveOr(in context, containingBlock.Height, float.NaN);
        var bottom = style.Bottom.ResolveOr(in context, containingBlock.Height, float.NaN);

        var widthStyle = style.Width.ResolveOr(in context, containingBlock.Width, float.NaN);
        float? forcedWidth = null;

        if (float.IsFinite(widthStyle))
        {
            forcedWidth = style.BoxSizing == BoxSizing.BorderBox
                ? widthStyle
                : widthStyle + box.Padding.Horizontal + box.Border.Horizontal;
        }
        else if (float.IsFinite(left) && float.IsFinite(right))
        {
            forcedWidth = MathF.Max(
                0,
                containingBlock.Width - left - right - box.Margin.Horizontal);
        }

        var borderWidth = forcedWidth ?? MathF.Min(
            MeasureMaxContentWidth(box),
            MathF.Max(0, containingBlock.Width - box.Margin.Horizontal));

        borderWidth = ClampBorderWidth(style, in context, containingBlock.Width, borderWidth);

        var heightStyle = style.Height.ResolveOr(in context, containingBlock.Height, float.NaN);
        float? forcedHeight = null;

        if (float.IsFinite(heightStyle))
        {
            forcedHeight = style.BoxSizing == BoxSizing.BorderBox
                ? heightStyle
                : heightStyle + box.Padding.Vertical + box.Border.Vertical;
        }
        else if (float.IsFinite(top) && float.IsFinite(bottom))
        {
            forcedHeight = MathF.Max(
                0,
                containingBlock.Height - top - bottom - box.Margin.Vertical);
        }

        float x;
        float y;

        if (float.IsFinite(left))
        {
            x = containingBlock.X + left + box.Margin.Left;
        }
        else if (float.IsFinite(right))
        {
            x = containingBlock.Right - right - borderWidth - box.Margin.Right;
        }
        else
        {
            x = box.StaticPosition.X + box.Margin.Left;
        }

        if (float.IsFinite(top))
        {
            y = containingBlock.Y + top + box.Margin.Top;
        }
        else if (float.IsFinite(bottom))
        {
            y = containingBlock.Bottom - bottom - (forcedHeight ?? box.BorderBox.Height) - box.Margin.Bottom;
        }
        else
        {
            y = box.StaticPosition.Y + box.Margin.Top;
        }

        box.ApplyBorderBox(x, y, borderWidth, forcedHeight ?? 0);

        LayoutPreparedBox(
            box,
            x,
            y,
            containingBlock.Width,
            containingBlock.Height,
            forcedHeight,
            fillHeight: false,
            definiteContentHeight: null);

        // The box may have added nested absolutes while laying out.
        ProcessAbsoluteBoxes(containingBlock);
    }

    // ------------------------------------------------------------- intrinsic

    private float MeasureMaxContentWidth(LayoutBox box)
    {
        if (box.HasIntrinsicWidth)
        {
            return box.IntrinsicWidth;
        }

        var style = box.Style;
        var context = CreateContext(style);
        var padding = style.Padding.Resolve(in context, 0);
        var border = ResolveBorders(style, in context, 0);
        // A percentage width has no meaningful max-content value: resolve it
        // against the child's own content instead, otherwise a flex item such
        // as Bootstrap's width:100% .form-control measures as zero and the
        // container collapses around it.
        var width = style.Width.Unit == LengthUnit.Percent
            ? float.NaN
            : style.Width.ResolveOr(in context, float.NaN, float.NaN);

        float content;

        if (float.IsFinite(width))
        {
            content = width;
        }
        else if (_options.IntrinsicSizeProvider?.Invoke(box.Node) is { } intrinsic)
        {
            content = intrinsic.Width;
        }
        else
        {
            content = MeasureContentMaxWidth(box, in context);
        }

        var result = content + padding.Horizontal + border.Horizontal;
        box.IntrinsicWidth = result;
        return result;
    }

    private float MeasureContentMaxWidth(LayoutBox box, in LengthContext context)
    {
        var textWidth = 0f;
        var text = GetNodeText(box.Node);

        if (!string.IsNullOrEmpty(text))
        {
            var textStyle = CreateTextStyle(box.Style);
            var transformed = ApplyTextTransform(text, box.Style.TextTransform);
            textWidth = _measurer.Measure(transformed, in textStyle).Width;
        }

        var style = box.Style;
        var inlineFlow = style.Display is Display.Inline or Display.InlineBlock
            or Display.InlineFlex or Display.InlineGrid or Display.InlineTable
            || (style.IsFlexContainer
                && style.FlexDirection is FlexDirection.Row or FlexDirection.RowReverse)
            || style.IsGridContainer;

        var sum = 0f;
        var max = 0f;

        for (var i = 0; i < box.Children.Count; i++)
        {
            var child = box.Children[i];

            if (child.IsOutOfFlow || child.IsContents)
            {
                continue;
            }

            var childContext = CreateContext(child.Style);
            var margin = child.Style.Margin.Resolve(in childContext, 0);
            var childWidth = MeasureMaxContentWidth(child) + margin.Horizontal;

            sum += childWidth;
            max = MathF.Max(max, childWidth);
        }

        // An inline-level container puts its text and inline children on one
        // line, so their intrinsic widths add up. A block container's intrinsic
        // width is the widest child instead.
        return inlineFlow
            ? textWidth + sum
            : MathF.Max(textWidth, max);
    }

    // ------------------------------------------------------------------ text

    private readonly record struct TextLayoutKey(
        string Text,
        TextStyle Style,
        int Width,
        WhiteSpace WhiteSpace,
        OverflowWrap OverflowWrap,
        int LineClamp,
        TextOverflow Overflow);

    private TextStyle CreateTextStyle(ComputedStyle style)
    {
        var context = CreateContext(style);
        var fontSize = context.FontSize;
        return style.ToTextStyle(in context, fontSize, style.Color);
    }

    private TextLayout GetTextLayout(
        ComputedStyle style,
        string text,
        float maxWidth,
        out TextStyle textStyle)
    {
        text = ApplyTextTransform(text, style.TextTransform);
        textStyle = CreateTextStyle(style);

        // Round the cache key up: rounding down makes a line that exactly fits
        // re-wrap because the cached layout width is narrower than the box.
        var widthKey = float.IsFinite(maxWidth)
            ? Math.Max(0, (int)MathF.Ceiling(maxWidth * 4))
            : int.MaxValue;

        var key = new TextLayoutKey(
            text,
            textStyle,
            widthKey,
            style.WhiteSpace,
            style.OverflowWrap,
            style.WebkitLineClamp,
            style.TextOverflow);

        if (_textCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var layoutWidth = widthKey == int.MaxValue ? float.PositiveInfinity : widthKey * 0.25f;

        var layout = TextLayoutEngine.Layout(
            text,
            in textStyle,
            _measurer,
            layoutWidth,
            style.WhiteSpace,
            style.OverflowWrap,
            style.WebkitLineClamp,
            style.TextOverflow);

        if (_textCache.Count > 8192)
        {
            _textCache.Clear();
        }

        _textCache[key] = layout;
        return layout;
    }

    /// <summary>
    /// Uncached text layout that asks the float context for the available band
    /// on every line. It is only used when text overlaps active floats.
    /// </summary>
    private TextLayout GetTextLayoutInBands(
        ComputedStyle style,
        string text,
        in Rect content,
        float startY,
        FloatContext floats)
    {
        text = ApplyTextTransform(text, style.TextTransform);
        var textStyle = CreateTextStyle(style);
        var contentLeft = content.X;
        var contentRight = content.Right;

        return TextLayoutEngine.LayoutWithBands(
            text,
            in textStyle,
            _measurer,
            contentLeft,
            startY,
            style.WhiteSpace,
            style.OverflowWrap,
            style.WebkitLineClamp,
            style.TextOverflow,
            y =>
            {
                var band = floats.BandAt(y, contentLeft, contentRight);
                return (band.Left, band.Right);
            });
    }

    private static string ApplyTextTransform(string text, TextTransform transform)
    {
        if (transform == TextTransform.None || text.Length == 0)
        {
            return text;
        }

        if (transform == TextTransform.Uppercase)
        {
            return text.ToUpperInvariant();
        }

        if (transform == TextTransform.Lowercase)
        {
            return text.ToLowerInvariant();
        }

        var builder = new StringBuilder(text.Length);
        var capitalize = true;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                capitalize = true;
                builder.Append(c);
                continue;
            }

            builder.Append(capitalize ? char.ToUpperInvariant(c) : c);
            capitalize = false;
        }

        return builder.ToString();
    }

    // -------------------------------------------------------------- dispatch

    private float LayoutContent(LayoutBox box, Rect content, float definiteContentHeight)
    {
        box.FloatContext = EstablishesBlockFormattingContext(box.Style) || ReferenceEquals(box, _root)
            ? new FloatContext()
            : box.Parent?.FloatContext ?? new FloatContext();

        if (box.IsContents)
        {
            return LayoutBlockChildren(box, content, definiteContentHeight);
        }

        if (box.Style.Display is Display.Table or Display.InlineTable)
        {
            return LayoutTable(box, content, definiteContentHeight);
        }

        if (box.Style.IsFlexContainer)
        {
            return LayoutFlex(box, content, definiteContentHeight);
        }

        if (box.Style.IsGridContainer)
        {
            return LayoutGrid(box, content, definiteContentHeight);
        }

        return LayoutBlockChildren(box, content, definiteContentHeight);
    }

    private static bool IsInlineLevel(LayoutBox box)
    {
        return box.Style.Display is Display.Inline or Display.InlineBlock or Display.InlineFlex
            or Display.InlineGrid or Display.InlineTable;
    }

    /// <summary>
    /// True when the box starts a new block formatting context, which means it
    /// gets its own float context and its border box avoids the parent's floats.
    /// </summary>
    private static bool EstablishesBlockFormattingContext(ComputedStyle style)
    {
        return style.Float != Float.None
            || style.Position is Position.Absolute or Position.Fixed
            || style.Display is Display.FlowRoot or Display.InlineBlock or Display.Table
                or Display.InlineTable or Display.TableCell or Display.TableCaption
            || style.IsFlexContainer
            || style.IsGridContainer
            || style.IsScrollContainer;
    }

    private static int IndexOfChild(List<LayoutBox> children, LayoutBox child)
    {
        for (var i = 0; i < children.Count; i++)
        {
            if (ReferenceEquals(children[i], child))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Stable <c>order</c> sort. <see cref="List{T}.Sort(Comparison{T})"/> is
    /// unstable, which would let equal-order siblings swap paint and layout
    /// positions between frames.
    /// </summary>
    private static void StableSortByOrder(List<LayoutBox> items)
    {
        for (var i = 1; i < items.Count; i++)
        {
            var item = items[i];
            var order = item.Style.Order;
            var j = i - 1;

            while (j >= 0 && items[j].Style.Order > order)
            {
                items[j + 1] = items[j];
                j--;
            }

            items[j + 1] = item;
        }
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
