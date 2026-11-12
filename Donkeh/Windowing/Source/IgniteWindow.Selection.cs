using System.Diagnostics;
using System.Text;
using Donkeh.Data.Dom;
using Donkeh.LayoutLab;
using Donkeh.UILab;
using Silk.NET.Input;

namespace Donkeh.Windowing;

/// <summary>
/// Text selection, clipboard integration and the browser-style editing
/// behaviours that go with them. Selection is painted by the layout engine's
/// custom painter, so highlights sit behind the glyphs and follow scrolling.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of selection share the <see cref="TextSelection"/> record. A
/// <b>control selection</b> belongs to the focused text input or textarea and is
/// expressed as offsets into the edit buffer (<c>_editText</c>). A
/// <b>document selection</b> spans one or more laid-out
/// <see cref="TextRun"/>s in the page or in the inspector and is expressed as an
/// element plus an offset into that element's run text.
/// </para>
/// <para>
/// All offsets are zero-based UTF-16 code-unit indices. Caret and hit-test
/// movement snaps to text-element boundaries so surrogate pairs are never split,
/// but combining marks are not grouped into full grapheme clusters.
/// </para>
/// <para>
/// Anchor and focus are stored in click order and normalized only when a
/// selection is consumed (copy, delete or paint), so the anchor keeps the
/// original origin of a shift-extension.
/// </para>
/// <para>
/// Pointer coordinates are client-space pixels with the origin at the top-left
/// of the window. Scroll offsets are added back before text is hit-tested, which
/// keeps selection correct inside scrolled containers.
/// </para>
/// </remarks>
public sealed partial class IgniteWindow
{
    // A translucent blue works on both the light app and the dark inspector.
    /// <summary>
    /// Packed <c>0xAARRGGBB</c> colour of the selection highlight: a translucent
    /// blue that stays legible on both the light application and the dark
    /// inspector.
    /// </summary>
    private const uint SelectionColor = 0x803390FF;

    /// <summary>
    /// The active selection, or <see langword="null"/> when nothing is selected.
    /// </summary>
    /// <remarks>
    /// A control selection is only honoured while <c>_focused</c> is still its
    /// anchor element. A document selection can no longer be resolved once either
    /// of its elements stops being a selectable run.
    /// </remarks>
    private TextSelection? _selection;

    /// <summary>
    /// <see langword="true"/> while the primary button is held after a press that
    /// began on selectable text; mouse moves are then routed to
    /// <see cref="UpdateSelectionDrag"/> instead of hover handling.
    /// </summary>
    private bool _selectionDragging;

    /// <summary>
    /// <see langword="true"/> once the pointer has moved more than three pixels
    /// since the press, which suppresses the synthetic <c>click</c> on release.
    /// </summary>
    private bool _selectionDragged;

    /// <summary>
    /// Client-space X coordinate of the button press that began the current
    /// drag, used for the drag threshold in <see cref="UpdateSelectionDrag"/>.
    /// </summary>
    private float _selectionDownX;

    /// <summary>
    /// Client-space Y coordinate of the button press that began the current
    /// drag, used for the drag threshold in <see cref="UpdateSelectionDrag"/>.
    /// </summary>
    private float _selectionDownY;

    /// <summary>
    /// <see cref="Stopwatch.GetTimestamp"/> of the previous click, used to detect
    /// double and triple clicks within a 0.5 second window.
    /// </summary>
    private long _lastClickTicks;

    /// <summary>
    /// Client-space X coordinate of the previous click, part of the multi-click
    /// identity test in <see cref="RegisterClick"/>.
    /// </summary>
    private float _lastClickX;

    /// <summary>
    /// Client-space Y coordinate of the previous click, part of the multi-click
    /// identity test in <see cref="RegisterClick"/>.
    /// </summary>
    private float _lastClickY;

    /// <summary>
    /// Element that received the previous press. A click only continues the
    /// current count when it lands on the same element.
    /// </summary>
    private ElementNode? _lastClickElement;

    /// <summary>Edit-buffer string that produced the cached control line starts.</summary>
    private string? _controlLineStartsText;

    /// <summary>Lay-out line array that produced the cached control line starts.</summary>
    private TextLine[]? _controlLineStartsLines;

    /// <summary>Cached buffer offset for each control visual line.</summary>
    private int[]? _controlLineStarts;

    /// <summary>
    /// Consecutive click count for the current element and position, in the
    /// range 1 to 3. A fourth rapid click stays at three.
    /// </summary>
    private int _clickCount;

    /// <summary>
    /// Cached selectable text runs in document order, or <see langword="null"/>
    /// when no layout is available.
    /// </summary>
    /// <remarks>
    /// The cached runs hold live <see cref="ElementNode"/> and
    /// <see cref="LayoutBox"/> references. <see cref="GetTextRuns"/> rebuilds the
    /// cache when the layout root, either tree version or the requested surface
    /// changes, so a subtree that unmounts does not stay alive through the cache.
    /// </remarks>
    private List<TextRun>? _textRuns;

    /// <summary>
    /// Layout root the run cache was built from, compared by reference to detect
    /// a replacement layout tree.
    /// </summary>
    private LayoutBox? _textRunsRoot;

    /// <summary>
    /// Whether the cached runs describe the inspector tree rather than the
    /// application tree.
    /// </summary>
    private bool _textRunsDevTools;

    /// <summary>
    /// Renderer tree version recorded when the cache was built; a mismatch forces
    /// a rebuild. A value of <c>-1</c> means no cache.
    /// </summary>
    private int _textRunsUiVersion = -1;

    /// <summary>
    /// Inspector tree version recorded when the cache was built; a mismatch
    /// forces a rebuild. A value of <c>-1</c> means no cache.
    /// </summary>
    private long _textRunsDevToolsVersion = -1;

    /// <summary>
    /// Maps each element that owns selectable text to its run, so hit testing can
    /// walk a box's ancestors to find the text owner.
    /// </summary>
    private Dictionary<ElementNode, TextRun>? _textRunIndex;

    /// <summary>
    /// One selection, either inside a text control or across laid-out document
    /// runs.
    /// </summary>
    /// <remarks>
    /// Offsets are UTF-16 code-unit indices and are not normalized:
    /// <see cref="AnchorOffset"/> and <see cref="FocusOffset"/> may appear in
    /// either order. Use <see cref="TryGetControlSelectionRange"/> for a control
    /// selection and <see cref="TryResolveSelectionRuns"/> for a document
    /// selection to obtain ordered ranges.
    /// </remarks>
    private sealed class TextSelection
    {
        /// <summary>
        /// Whether this is a selection in the focused text control rather than a
        /// document selection.
        /// </summary>
        public bool IsControl;

        /// <summary>
        /// Whether the selection belongs to the inspector surface; selects which
        /// paint flag is invalidated.
        /// </summary>
        public bool IsDevTools;

        /// <summary>
        /// Element where the selection began. The caret stays conceptually fixed
        /// here while the focus end is extended.
        /// </summary>
        public ElementNode? AnchorElement;

        /// <summary>
        /// Element where the selection currently ends. Equal to
        /// <see cref="AnchorElement"/> for a control selection, but may be a later
        /// run for a document selection.
        /// </summary>
        public ElementNode? FocusElement;

        /// <summary>
        /// Offset of the anchor within its run text or within the edit buffer.
        /// Not necessarily less than <see cref="FocusOffset"/>.
        /// </summary>
        public int AnchorOffset;

        /// <summary>
        /// Offset of the focus within its run text or within the edit buffer.
        /// Not necessarily greater than <see cref="AnchorOffset"/>.
        /// </summary>
        public int FocusOffset;
    }

    /// <summary>
    /// A laid-out block of selectable text belonging to one element, cached for
    /// hit testing, extraction and painting.
    /// </summary>
    /// <remarks>
    /// <see cref="Text"/> joins the visual lines with a separator so a single
    /// contiguous offset space can address the whole run, and
    /// <see cref="LineStarts"/> maps each visual line back into that space.
    /// </remarks>
    private sealed class TextRun
    {
        /// <summary>
        /// Element that owns the text; also the key used by the run index.
        /// </summary>
        public ElementNode Element = null!;

        /// <summary>
        /// Layout box the run was created from. Hit testing reads the text origin
        /// and scroll from it; painting prefers the box currently being emitted.
        /// </summary>
        public LayoutBox Box = null!;

        /// <summary>
        /// Run text with visual line breaks replaced by the separator chosen from
        /// the white-space mode.
        /// </summary>
        public string Text = string.Empty;

        /// <summary>
        /// Visual lines as produced by the layout engine, in top-to-bottom order.
        /// </summary>
        public TextLine[] Lines = [];

        /// <summary>
        /// Offset of each entry in <see cref="Lines"/> within
        /// <see cref="Text"/>; the array is parallel to <see cref="Lines"/>.
        /// </summary>
        public int[] LineStarts = [];

        /// <summary>
        /// Resolved text style used when measuring prefix widths.
        /// </summary>
        public TextStyle Style;

        /// <summary>
        /// Whether the run belongs to the inspector surface.
        /// </summary>
        public bool IsDevTools;

        /// <summary>
        /// Whether the element generates an inline box. Adjacent inline runs are
        /// concatenated without a newline when copied.
        /// </summary>
        public bool IsInline;

        /// <summary>
        /// Zero-based position of the run in document order, assigned when the
        /// cache is rebuilt.
        /// </summary>
        public int Index;
    }

    /// <summary>
    /// Gets whether the focused element has a non-empty control selection.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when the selection is a control selection anchored
    /// on the currently focused element and its two offsets differ.
    /// </value>
    /// <remarks>
    /// This guard is checked before copy, cut and delete so a stale selection that
    /// survived a focus change cannot be applied to the wrong control.
    /// </remarks>
    private bool HasControlSelection =>
        _selection is { IsControl: true } selection
        && _focused is not null
        && ReferenceEquals(selection.AnchorElement, _focused)
        && selection.AnchorOffset != selection.FocusOffset;

    // ------------------------------------------------------------- text runs

    /// <summary>
    /// Gets the selectable text runs for the application or inspector surface,
    /// rebuilding the cache when it is stale.
    /// </summary>
    /// <param name="devTools">
    /// <see langword="true"/> to enumerate the inspector tree instead of the
    /// application tree.
    /// </param>
    /// <returns>
    /// Runs in document order; an empty list when no layout is available.
    /// </returns>
    /// <remarks>
    /// The cache is rebuilt when the layout root reference, either tree version or
    /// the requested surface differs from the values recorded at build time.
    /// Callers must not retain the returned list across frames, because a rebuild
    /// replaces it.
    /// </remarks>
    private List<TextRun> GetTextRuns(bool devTools)
    {
        var layout = devTools ? _devToolsLayout : _layoutResult;

        if (layout?.Root is not { } root)
        {
            _textRuns = null;
            _textRunsRoot = null;
            _textRunsUiVersion = -1;
            _textRunsDevToolsVersion = -1;
            _textRunIndex = null;
            return [];
        }

        // Rebuild after any tree update. The cached runs hold element and box
        // references; if a modal or conditional panel unmounts they would keep
        // the old subtree alive even though the box tree was reused.
        var uiVersion = _ui.Version;
        var devToolsVersion = _devTools.Version;

        if (_textRuns is not null
            && ReferenceEquals(_textRunsRoot, root)
            && _textRunsDevTools == devTools
            && _textRunsUiVersion == uiVersion
            && _textRunsDevToolsVersion == devToolsVersion)
        {
            return _textRuns;
        }

        var runs = new List<TextRun>();
        CollectTextRuns(root, devTools, runs);

        var index = new Dictionary<ElementNode, TextRun>(runs.Count);

        for (var i = 0; i < runs.Count; i++)
        {
            runs[i].Index = i;
            index[runs[i].Element] = runs[i];
        }

        _textRuns = runs;
        _textRunsRoot = root;
        _textRunsDevTools = devTools;
        _textRunsUiVersion = uiVersion;
        _textRunsDevToolsVersion = devToolsVersion;
        _textRunIndex = index;
        return runs;
    }

    /// <summary>
    /// Walks the box tree depth-first in document order and appends a run for
    /// every visible element that paints selectable text.
    /// </summary>
    /// <param name="box">Box to inspect; its children are visited recursively.</param>
    /// <param name="devTools">Surface flag copied onto each created run.</param>
    /// <param name="runs">Destination list, appended to in document order.</param>
    /// <remarks>
    /// Subtrees with <c>display: none</c> or hidden/collapsed visibility are
    /// skipped entirely. Text controls and non-selectable tags are excluded
    /// because their text is edited or chosen rather than selected.
    /// </remarks>
    private static void CollectTextRuns(LayoutBox box, bool devTools, List<TextRun> runs)
    {
        if (box.Style.Display == Display.None
            || box.Style.Visibility is Visibility.Hidden or Visibility.Collapse)
        {
            return;
        }

        if (box.Node is ElementNode element
            && box.Text is { Lines.Length: > 0 } text
            && !IsTextControl(element)
            && !IsNonSelectableTag(element))
        {
            runs.Add(CreateTextRun(element, box, text, devTools));
        }

        for (var i = 0; i < box.Children.Count; i++)
        {
            CollectTextRuns(box.Children[i], devTools, runs);
        }
    }

    /// <summary>
    /// Gets whether an element's text is excluded from document selection because
    /// the element is a form-owned control rather than prose.
    /// </summary>
    /// <param name="element">Element to test.</param>
    /// <returns>
    /// <see langword="true"/> for <c>select</c>, <c>option</c> and
    /// <c>textarea</c>.
    /// </returns>
    /// <remarks>
    /// A <c>textarea</c> is also a text control, so it is excluded here and
    /// handled through the edit-buffer selection path instead.
    /// </remarks>
    private static bool IsNonSelectableTag(ElementNode element)
    {
        return element.Tag is "select" or "option" or "textarea";
    }

    /// <summary>
    /// Builds a <see cref="TextRun"/> from an element's laid-out text block.
    /// </summary>
    /// <param name="element">Element that owns the text.</param>
    /// <param name="box">Box supplying the text layout and white-space mode.</param>
    /// <param name="text">Laid-out lines for the element.</param>
    /// <param name="devTools">Whether the run belongs to the inspector surface.</param>
    /// <returns>
    /// A run whose <see cref="TextRun.Text"/> gives every line a share of one
    /// contiguous offset space.
    /// </returns>
    /// <remarks>
    /// Lines are joined with <c>"\n"</c> when white-space preserves newlines
    /// (<c>pre</c>, <c>pre-wrap</c>, <c>pre-line</c> and <c>break-spaces</c>) and
    /// with a single space otherwise. The separator is not present in any
    /// <see cref="TextRun.Lines"/> entry, so copying a wrapped paragraph does not
    /// introduce hard breaks that were never authored.
    /// </remarks>
    private static TextRun CreateTextRun(
        ElementNode element,
        LayoutBox box,
        TextLayout text,
        bool devTools)
    {
        var lines = text.Lines;
        var separator = box.Style.WhiteSpace is WhiteSpace.Pre
            or WhiteSpace.PreWrap
            or WhiteSpace.PreLine
            or WhiteSpace.BreakSpaces
            ? "\n"
            : " ";
        var builder = new StringBuilder(text.Text.Length + lines.Length);
        var starts = new int[lines.Length];

        for (var i = 0; i < lines.Length; i++)
        {
            starts[i] = builder.Length;
            builder.Append(lines[i].Text);

            if (i < lines.Length - 1)
            {
                builder.Append(separator);
            }
        }

        return new TextRun
        {
            Element = element,
            Box = box,
            Text = builder.ToString(),
            Lines = lines,
            LineStarts = starts,
            Style = text.Style,
            IsDevTools = devTools,
            IsInline = box.Style.Display is Display.Inline
                or Display.InlineBlock
                or Display.InlineFlex
                or Display.InlineGrid
                or Display.InlineTable
        };
    }

    /// <summary>
    /// Sums the scroll offsets of every scroll container on the path from a box to
    /// the layout root.
    /// </summary>
    /// <param name="box">
    /// Starting box. The box itself is included when it scrolls, and
    /// <see langword="null"/> yields zero.
    /// </param>
    /// <returns>
    /// The total scroll translation, in pixels, already applied to descendant
    /// painted positions.
    /// </returns>
    private static (float X, float Y) AccumulatedScroll(LayoutBox? box)
    {
        var x = 0f;
        var y = 0f;

        for (var current = box; current is not null; current = current.Parent)
        {
            if (current.IsScrollContainer)
            {
                x += current.ScrollOffsetX;
                y += current.ScrollOffsetY;
            }
        }

        return (x, y);
    }

    /// <summary>
    /// Gets a box's padding box in painted client coordinates by subtracting the
    /// scroll offset accumulated by its ancestors.
    /// </summary>
    /// <param name="box">Box to translate.</param>
    /// <returns>The padding box relative to the window's client area.</returns>
    /// <remarks>
    /// The box's own scroll offset is deliberately excluded: scrolling moves the
    /// content inside the box, not the box border or padding itself.
    /// </remarks>
    private static Rect PaintedPaddingBox(LayoutBox box)
    {
        var (x, y) = AccumulatedScroll(box.Parent);
        return box.PaddingBox.Translate(-x, -y);
    }

    // ------------------------------------------------------------ hit testing

    /// <summary>
    /// Hit-tests a client-space point against selectable text.
    /// </summary>
    /// <param name="x">Pointer X in client coordinates.</param>
    /// <param name="y">Pointer Y in client coordinates.</param>
    /// <param name="devTools">Whether to test the inspector tree.</param>
    /// <param name="run">Receives the run whose element contains the hit.</param>
    /// <param name="offset">Receives the text offset closest to the point.</param>
    /// <returns>
    /// <see langword="true"/> when the point lands on a selectable run.
    /// </returns>
    /// <remarks>
    /// The hit box's ancestor chain is walked until an element with a cached run is
    /// found, so a click on nested inline content still resolves to the element
    /// that owns the text. Both outputs are defaulted before the test, so they are
    /// safe to read even when the method returns <see langword="false"/>.
    /// </remarks>
    private bool TryHitText(
        float x,
        float y,
        bool devTools,
        out TextRun run,
        out int offset)
    {
        run = null!;
        offset = 0;

        var layout = devTools ? _devToolsLayout : _layoutResult;

        if (layout?.Root is not { } root)
        {
            return false;
        }

        var hit = HitTestRoot(root, x, y);

        if (hit is null)
        {
            return false;
        }

        GetTextRuns(devTools);

        if (_textRunIndex is null)
        {
            return false;
        }

        for (var box = hit; box is not null; box = box.Parent)
        {
            if (box.Node is ElementNode element
                && _textRunIndex.TryGetValue(element, out var candidate))
            {
                run = candidate;
                offset = HitTestTextLines(candidate.Box, candidate.Lines,
                    candidate.LineStarts, candidate.Style, x, y);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the selectable run and offset closest to a client-space point,
    /// whether or not the point is actually over text.
    /// </summary>
    /// <param name="x">Pointer X in client coordinates.</param>
    /// <param name="y">Pointer Y in client coordinates.</param>
    /// <param name="devTools">Whether to search the inspector tree.</param>
    /// <param name="offset">
    /// Receives the clamped offset in the returned run, or zero when no run is
    /// found.
    /// </param>
    /// <returns>The nearest run, or <see langword="null"/> when there are no runs.</returns>
    /// <remarks>
    /// Distance is measured to the nearest edge of each visual line and compared
    /// squared, so no square root is taken. Ties keep the earlier run in document
    /// order. This is the fallback used while dragging outside the text, so the
    /// selection can extend to the nearest end of the nearest run instead of
    /// stalling.
    /// </remarks>
    private TextRun? NearestTextRun(float x, float y, bool devTools, out int offset)
    {
        offset = 0;
        var runs = GetTextRuns(devTools);
        TextRun? best = null;
        var bestDistance = float.MaxValue;
        var bestOffset = 0;

        for (var r = 0; r < runs.Count; r++)
        {
            var run = runs[r];
            var (scrollX, scrollY) = AccumulatedScroll(run.Box);

            for (var i = 0; i < run.Lines.Length; i++)
            {
                var line = run.Lines[i];
                var left = run.Box.TextOrigin.X + line.X - scrollX;
                var top = run.Box.TextOrigin.Y + line.Y - scrollY;
                var right = left + MathF.Max(1, line.Width);
                var bottom = top + line.Height;
                var dx = x < left ? left - x : x > right ? x - right : 0;
                var dy = y < top ? top - y : y > bottom ? y - bottom : 0;
                var distance = dx * dx + dy * dy;

                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                best = run;
                bestOffset = run.LineStarts[i] + OffsetInLine(
                    line.Text,
                    x + scrollX - (run.Box.TextOrigin.X + line.X),
                    run.Style);
            }
        }

        offset = bestOffset;
        return best;
    }

    /// <summary>
    /// Maps a client-space point to an offset in a run's concatenated text.
    /// </summary>
    /// <param name="box">Box that owns the lines; supplies origin and scroll.</param>
    /// <param name="lines">Visual lines of the run.</param>
    /// <param name="starts">Offset of each line within the concatenated text.</param>
    /// <param name="style">Style used to measure prefix widths.</param>
    /// <param name="x">Pointer X in client coordinates.</param>
    /// <param name="y">Pointer Y in client coordinates.</param>
    /// <returns>
    /// The nearest text-element boundary in the nearest line.
    /// </returns>
    /// <remarks>
    /// The vertical position selects the line, then the horizontal position
    /// selects the boundary within it. A point left of the text resolves to the
    /// line start and a point past its end resolves to the line's last boundary,
    /// so the result is always a valid offset in the run.
    /// </remarks>
    private int HitTestTextLines(
        LayoutBox box,
        TextLine[] lines,
        int[] starts,
        in TextStyle style,
        float x,
        float y)
    {
        var (scrollX, scrollY) = AccumulatedScroll(box);
        var localX = x + scrollX - box.TextOrigin.X;
        var localY = y + scrollY - box.TextOrigin.Y;
        var lineIndex = NearestLine(lines, localY);
        var line = lines[lineIndex];
        var offset = OffsetInLine(line.Text, localX - line.X, in style);
        return starts[lineIndex] + offset;
    }

    /// <summary>
    /// Gets the index of the visual line whose vertical span is closest to a Y
    /// coordinate.
    /// </summary>
    /// <param name="lines">Visual lines to search.</param>
    /// <param name="y">Y position relative to the text origin.</param>
    /// <returns>The index of the first line with the smallest distance.</returns>
    /// <remarks>
    /// Points inside a line have zero distance, so the first enclosing line wins.
    /// The caller must pass a non-empty array.
    /// </remarks>
    private static int NearestLine(TextLine[] lines, float y)
    {
        // Lines are laid out top-to-bottom without overlap, so the answer is one
        // of the two lines around the insertion point.
        var low = 0;
        var high = lines.Length;

        while (low < high)
        {
            var middle = low + (high - low) / 2;

            if (lines[middle].Y + lines[middle].Height < y)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if (low <= 0)
        {
            return 0;
        }

        if (low >= lines.Length)
        {
            return lines.Length - 1;
        }

        var previous = lines[low - 1];
        var next = lines[low];
        var previousDistance = y - (previous.Y + previous.Height);
        var nextDistance = next.Y - y;

        return nextDistance < previousDistance ? low : low - 1;
    }

    /// <summary>
    /// Gets the text offset nearest to a horizontal position within one visual
    /// line.
    /// </summary>
    /// <param name="text">Text of the line.</param>
    /// <param name="x">Position relative to the line's left edge.</param>
    /// <param name="style">Style used to measure candidate prefixes.</param>
    /// <returns>
    /// An offset in <paramref name="text"/>, always on a text-element boundary.
    /// </returns>
    /// <remarks>
    /// Candidates are the boundaries produced by <c>NextTextElement</c>, which
    /// keeps surrogate pairs intact. A negative position clamps to zero, and a
    /// position past the end selects the final boundary, equal to the line length.
    /// Zero is also the initial candidate, so a position before the first glyph
    /// resolves to the line start.
    /// </remarks>
    private int OffsetInLine(string text, float x, in TextStyle style)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var target = MathF.Max(0, x);

        if (target <= 0)
        {
            return 0;
        }

        if (MeasurePrefix(text, text.Length, in style) <= target)
        {
            return text.Length;
        }

        // Prefix widths are monotonic for normal text, so binary search finds
        // the boundary without measuring every prefix from the start.
        var low = 0;
        var high = text.Length;

        while (low < high)
        {
            var middle = low + (high - low) / 2;

            if (MeasurePrefix(text, middle, in style) < target)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        var lower = low;

        if (lower > 0 && lower < text.Length && char.IsLowSurrogate(text[lower]))
        {
            lower--;
        }

        var upper = lower < text.Length ? NextTextElement(text, lower) : text.Length;
        var lowerWidth = MeasurePrefix(text, lower, in style);
        var upperWidth = MeasurePrefix(text, upper, in style);

        return MathF.Abs(lowerWidth - target) <= MathF.Abs(upperWidth - target)
            ? lower
            : upper;
    }

    /// <summary>
    /// Measures the width of a prefix of a line in a given style.
    /// </summary>
    /// <param name="text">Line text.</param>
    /// <param name="length">
    /// Number of leading UTF-16 code units to measure; zero or less yields zero.
    /// </param>
    /// <param name="style">Style to measure with.</param>
    /// <returns>The prefix width in pixels.</returns>
    /// <remarks>
    /// No surrogate-pair guard is applied here; callers pass boundaries produced
    /// by <c>NextTextElement</c>.
    /// </remarks>
    private float MeasurePrefix(string text, int length, in TextStyle style)
    {
        return length <= 0
            ? 0
            : _measurer.Measure(text.AsSpan(0, length), in style).Width;
    }

    // -------------------------------------------------------------- selection

    /// <summary>
    /// Begins a collapsed document selection at a run offset.
    /// </summary>
    /// <param name="run">Run that becomes both the anchor and the focus.</param>
    /// <param name="offset">
    /// Offset in <see cref="TextRun.Text"/>. It is not clamped here; callers pass
    /// an offset produced by hit testing.
    /// </param>
    /// <remarks>
    /// Anchor and focus start equal, so a plain click leaves an empty selection
    /// that a later drag or shift-click extends from this point.
    /// </remarks>
    private void StartTextSelection(TextRun run, int offset)
    {
        _selection = new TextSelection
        {
            IsControl = false,
            IsDevTools = run.IsDevTools,
            AnchorElement = run.Element,
            FocusElement = run.Element,
            AnchorOffset = offset,
            FocusOffset = offset
        };

        InvalidateSelectionPaint(run.IsDevTools);
    }

    /// <summary>
    /// Moves the focus end of the current document selection to a run offset,
    /// keeping the anchor fixed.
    /// </summary>
    /// <param name="run">
    /// Run containing the new focus; it may differ from the anchor run.
    /// </param>
    /// <param name="offset">
    /// Requested offset, clamped to the run's text length.
    /// </param>
    /// <remarks>
    /// Does nothing unless the active selection is a document selection. Extending
    /// across runs is what lets shift-click and drag selection span paragraphs and
    /// sibling elements.
    /// </remarks>
    private void SetTextSelectionFocus(TextRun run, int offset)
    {
        if (_selection is not { IsControl: false } selection)
        {
            return;
        }

        selection.FocusElement = run.Element;
        selection.FocusOffset = Math.Clamp(offset, 0, run.Text.Length);
        InvalidateSelectionPaint(selection.IsDevTools);
    }

    /// <summary>
    /// Creates or replaces the selection inside the focused text control.
    /// </summary>
    /// <param name="anchor">Anchor offset, clamped to the edit buffer length.</param>
    /// <param name="focus">Focus offset, clamped to the edit buffer length.</param>
    /// <remarks>
    /// The two offsets may be equal, which collapses the selection. The caret is
    /// moved to the focus end and its blink restarted so it does not remain hidden
    /// mid-blink. Does nothing when no element is focused.
    /// </remarks>
    private void SetControlSelection(int anchor, int focus)
    {
        if (_focused is null)
        {
            return;
        }

        var length = _editText?.Length ?? 0;
        _selection = new TextSelection
        {
            IsControl = true,
            IsDevTools = IsInDevToolsTree(_focused),
            AnchorElement = _focused,
            FocusElement = _focused,
            AnchorOffset = Math.Clamp(anchor, 0, length),
            FocusOffset = Math.Clamp(focus, 0, length)
        };

        _caretIndex = _selection.FocusOffset;
        ResetCaretBlink();
        InvalidateSelectionPaint(_selection.IsDevTools);
    }

    /// <summary>
    /// Removes the active selection and stops any in-progress drag.
    /// </summary>
    /// <remarks>
    /// The caret is left untouched, so callers that collapse a selection must
    /// position it first. Only the surface the selection belonged to is
    /// invalidated.
    /// </remarks>
    private void ClearSelection()
    {
        if (_selection is not { } selection)
        {
            return;
        }

        _selection = null;
        _selectionDragging = false;
        InvalidateSelectionPaint(selection.IsDevTools);
    }

    /// <summary>
    /// Marks the surface that owns a selection as needing a repaint.
    /// </summary>
    /// <param name="devTools">
    /// <see langword="true"/> for the inspector surface, otherwise the
    /// application surface.
    /// </param>
    private void InvalidateSelectionPaint(bool devTools)
    {
        if (devTools)
        {
            _needsDevToolsPaint = true;
        }
        else
        {
            _needsPaint = true;
        }
    }

    /// <summary>
    /// Gets the ordered, non-empty range of the focused control's selection.
    /// </summary>
    /// <param name="start">Receives the inclusive start offset.</param>
    /// <param name="end">Receives the exclusive end offset.</param>
    /// <returns>
    /// <see langword="true"/> when a non-empty control selection applies to the
    /// focused text control; otherwise <see langword="false"/> with both outputs
    /// set to zero.
    /// </returns>
    /// <remarks>
    /// Fails when the selection is a document selection, when the anchor element is
    /// no longer focused, or when the focused element is not a text input or
    /// textarea. Both offsets are clamped to the current edit buffer length, which
    /// may have changed since the selection was created.
    /// </remarks>
    private bool TryGetControlSelectionRange(out int start, out int end)
    {
        start = 0;
        end = 0;

        if (_selection is not { IsControl: true } selection
            || _focused is null
            || !ReferenceEquals(selection.AnchorElement, _focused)
            || !IsTextControl(_focused))
        {
            return false;
        }

        var length = _editText?.Length ?? 0;
        start = Math.Clamp(Math.Min(selection.AnchorOffset, selection.FocusOffset), 0, length);
        end = Math.Clamp(Math.Max(selection.AnchorOffset, selection.FocusOffset), 0, length);
        return start != end;
    }

    /// <summary>
    /// Deletes the selected range from the edit buffer without raising events.
    /// </summary>
    /// <returns><see langword="true"/> when text was removed.</returns>
    /// <remarks>
    /// The caret is placed at the start of the removed range and the selection is
    /// cleared. Callers are responsible for cascading the new value to the DOM and
    /// firing <c>input</c>, normally through <see cref="AfterControlEdit"/> when no
    /// further edit follows.
    /// </remarks>
    private bool DeleteControlSelectionCore()
    {
        if (!TryGetControlSelectionRange(out var start, out var end))
        {
            return false;
        }

        _editText = (_editText ?? string.Empty).Remove(start, end - start);
        _caretIndex = start;
        ClearSelection();
        return true;
    }

    /// <summary>
    /// Publishes an edit made to the focused control: restarts the caret blink,
    /// writes the buffer back to the DOM and raises <c>input</c>.
    /// </summary>
    /// <remarks>
    /// Does nothing when no element is focused. The <c>input</c> event receives the
    /// current edit buffer, which may be empty but is never
    /// <see langword="null"/>. Committing the value to the attribute or text content
    /// is what makes the change visible to layout and to scripts.
    /// </remarks>
    private void AfterControlEdit()
    {
        if (_focused is null)
        {
            return;
        }

        ResetCaretBlink();
        CascadeEdit(_focused);
        Invoke(_focused, "input", _editText ?? string.Empty);
    }

    /// <summary>
    /// Collapses the control selection to one of its ends and moves the caret
    /// there.
    /// </summary>
    /// <param name="toStart">
    /// <see langword="true"/> to keep the lower offset; <see langword="false"/> to
    /// keep the higher one.
    /// </param>
    /// <remarks>
    /// Matches arrow-key behaviour after a selection: Left collapses to the start
    /// and Right to the end. Does nothing when the selection is not a control
    /// selection.
    /// </remarks>
    private void CollapseControlSelection(bool toStart)
    {
        if (_selection is not { IsControl: true } selection)
        {
            return;
        }

        _caretIndex = toStart
            ? Math.Min(selection.AnchorOffset, selection.FocusOffset)
            : Math.Max(selection.AnchorOffset, selection.FocusOffset);
        ClearSelection();
        ResetCaretBlink();
    }

    /// <summary>
    /// Extends the control selection towards a new focus offset, as Shift plus an
    /// arrow key or Home/End does.
    /// </summary>
    /// <param name="focus">
    /// Requested focus offset, clamped to the edit buffer length.
    /// </param>
    /// <remarks>
    /// <para>
    /// The anchor is the existing selection anchor when one is active, otherwise
    /// the current caret, so repeated extensions keep a fixed origin.
    /// </para>
    /// <para>
    /// If the clamped focus equals the anchor the selection collapses and the caret
    /// moves there; otherwise a new selection is created through
    /// <see cref="SetControlSelection"/>.
    /// </para>
    /// <para>
    /// Does nothing when no element is focused or the focused element is not a text
    /// control.
    /// </para>
    /// </remarks>
    private void ExtendControlSelection(int focus)
    {
        if (_focused is null || !IsTextControl(_focused))
        {
            return;
        }

        var length = _editText?.Length ?? 0;
        focus = Math.Clamp(focus, 0, length);
        var anchor = HasControlSelection
            ? _selection!.AnchorOffset
            : _caretIndex;

        if (anchor == focus)
        {
            _caretIndex = focus;
            ClearSelection();
            ResetCaretBlink();
            return;
        }

        SetControlSelection(anchor, focus);
    }

    /// <summary>
    /// Selects everything that can be selected in the focused surface.
    /// </summary>
    /// <remarks>
    /// For a text control this is the whole edit buffer. Otherwise every selectable
    /// run from the first to the last is selected. When the initially chosen
    /// surface has no runs, the other surface is tried once, so Ctrl+A works in the
    /// inspector without first focusing it.
    /// </remarks>
    private void SelectAllText()
    {
        if (_focused is not null && IsTextControl(_focused))
        {
            var length = _editText?.Length ?? 0;
            SetControlSelection(0, length);
            return;
        }

        var devTools = _focused is not null && IsInDevToolsTree(_focused)
            || _selection is { IsDevTools: true };

        var runs = GetTextRuns(devTools);

        if (runs.Count == 0)
        {
            devTools = !devTools;
            runs = GetTextRuns(devTools);
        }

        if (runs.Count == 0)
        {
            return;
        }

        var first = runs[0];
        var last = runs[^1];

        _selection = new TextSelection
        {
            IsControl = false,
            IsDevTools = devTools,
            AnchorElement = first.Element,
            FocusElement = last.Element,
            AnchorOffset = 0,
            FocusOffset = last.Text.Length
        };

        InvalidateSelectionPaint(devTools);
    }

    // --------------------------------------------------------------- clipboard

    /// <summary>
    /// Copies the selected text to the clipboard.
    /// </summary>
    /// <param name="keyboard">Input device that owns the clipboard.</param>
    /// <returns><see langword="true"/> when non-empty text was copied.</returns>
    /// <remarks>
    /// A <see langword="false"/> result lets the key handler fall through, so other
    /// shortcuts are not swallowed when nothing is selected.
    /// </remarks>
    private bool CopySelection(IKeyboard keyboard)
    {
        var text = GetSelectedText();

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        keyboard.ClipboardText = text;
        return true;
    }

    /// <summary>
    /// Copies the control selection to the clipboard and removes it from the edit
    /// buffer.
    /// </summary>
    /// <param name="keyboard">Input device that owns the clipboard.</param>
    /// <returns><see langword="true"/> when text was copied and deleted.</returns>
    /// <remarks>
    /// Only control selections can be cut; document text is read-only in this
    /// engine. Deletion publishes the change through
    /// <see cref="AfterControlEdit"/>, which raises <c>input</c>.
    /// </remarks>
    private bool CutSelection(IKeyboard keyboard)
    {
        if (!HasControlSelection)
        {
            return false;
        }

        var text = GetSelectedText();

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        keyboard.ClipboardText = text;

        if (DeleteControlSelectionCore())
        {
            AfterControlEdit();
        }

        return true;
    }

    /// <summary>
    /// Extracts the text currently covered by the selection.
    /// </summary>
    /// <returns>
    /// The selected text, or <see langword="null"/> when there is no selection or
    /// it cannot be resolved against the current layout.
    /// </returns>
    /// <remarks>
    /// <para>
    /// For a control selection the range is normalized before slicing the edit
    /// buffer. For a document selection the runs are walked in document order and
    /// only the clipped portion of the first and last run is included.
    /// </para>
    /// <para>
    /// A newline is inserted between adjacent runs only when both are block-level.
    /// Inline runs, such as links or spans inside a paragraph, are concatenated
    /// directly so copied prose keeps its flow.
    /// </para>
    /// <para>
    /// A collapsed document selection yields the empty string; <see langword="null"/>
    /// is reserved for selections that cannot be mapped to runs or a control range.
    /// </para>
    /// </remarks>
    private string? GetSelectedText()
    {
        if (_selection is not { } selection)
        {
            return null;
        }

        if (selection.IsControl)
        {
            return TryGetControlSelectionRange(out var start, out var end)
                ? (_editText ?? string.Empty)[start..end]
                : null;
        }

        var runs = GetTextRuns(selection.IsDevTools);

        if (!TryResolveSelectionRuns(selection, runs, out var startRun, out var startOffset,
                out var endRun, out var endOffset))
        {
            return null;
        }

        var builder = new StringBuilder();

        for (var i = startRun; i <= endRun; i++)
        {
            var run = runs[i];
            var from = i == startRun ? startOffset : 0;
            var to = i == endRun ? endOffset : run.Text.Length;
            from = Math.Clamp(from, 0, run.Text.Length);
            to = Math.Clamp(to, 0, run.Text.Length);

            if (to > from)
            {
                builder.Append(run.Text, from, to - from);
            }

            if (i < endRun)
            {
                var next = runs[i + 1];

                if (!run.IsInline && !next.IsInline)
                {
                    builder.Append('\n');
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Resolves a document selection to ordered run indices and offsets.
    /// </summary>
    /// <param name="selection">Selection to resolve.</param>
    /// <param name="runs">Current run list, in document order.</param>
    /// <param name="startRun">Receives the index of the run containing the start.</param>
    /// <param name="startOffset">Receives the start offset in that run's text.</param>
    /// <param name="endRun">Receives the index of the run containing the end.</param>
    /// <param name="endOffset">Receives the end offset in that run's text.</param>
    /// <returns>
    /// <see langword="false"/> when either element is no longer a selectable run.
    /// </returns>
    /// <remarks>
    /// The pair is swapped when the focus precedes the anchor, so callers always get
    /// <c>startRun &lt;= endRun</c> without losing the original direction stored in
    /// the selection. Offsets are returned unchanged and are not validated against
    /// run lengths; consumers clamp them.
    /// </remarks>
    private static bool TryResolveSelectionRuns(
        TextSelection selection,
        List<TextRun> runs,
        out int startRun,
        out int startOffset,
        out int endRun,
        out int endOffset)
    {
        startRun = 0;
        startOffset = 0;
        endRun = 0;
        endOffset = 0;

        if (!TryFindRun(runs, selection.AnchorElement, out startRun)
            || !TryFindRun(runs, selection.FocusElement, out endRun))
        {
            return false;
        }

        startOffset = selection.AnchorOffset;
        endOffset = selection.FocusOffset;

        if (startRun > endRun || startRun == endRun && startOffset > endOffset)
        {
            (startRun, endRun) = (endRun, startRun);
            (startOffset, endOffset) = (endOffset, startOffset);
        }

        return true;
    }

    /// <summary>
    /// Finds the index of the run owned by an element.
    /// </summary>
    /// <param name="runs">Runs in document order.</param>
    /// <param name="element">
    /// Element to find; <see langword="null"/> always fails.
    /// </param>
    /// <param name="index">Receives the zero-based run index when found.</param>
    /// <returns>
    /// <see langword="true"/> when a run references the element.
    /// </returns>
    /// <remarks>
    /// Compares by reference and scans linearly, which is cheap because selections
    /// are only resolved a few times per frame.
    /// </remarks>
    private static bool TryFindRun(List<TextRun> runs, ElementNode? element, out int index)
    {
        index = 0;

        if (element is null)
        {
            return false;
        }

        for (var i = 0; i < runs.Count; i++)
        {
            if (ReferenceEquals(runs[i].Element, element))
            {
                index = i;
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- painting

    /// <summary>
    /// Emits highlight rectangles for the selection in one box.
    /// </summary>
    /// <param name="box">Box currently being emitted by the painter.</param>
    /// <param name="instructions">Destination draw list.</param>
    /// <remarks>
    /// <para>
    /// Does nothing unless the box's element is one of the selected elements on
    /// the selecting surface. A control selection is clipped to the control's
    /// content box because the input scrolls internally; a document selection is
    /// clipped only by ancestor scroll containers.
    /// </para>
    /// <para>
    /// Geometry for a document selection is rebuilt from the box being emitted
    /// rather than from the cached run. A full layout creates new boxes while the
    /// cache still describes the previous frame, and stale geometry would paint the
    /// highlight one frame behind after a resize or reflow.
    /// </para>
    /// </remarks>
    private void PaintSelection(LayoutBox box, InstructionBuffer instructions)
    {
        if (_selection is not { } selection || box.Node is not ElementNode element)
        {
            return;
        }

        if (selection.IsControl)
        {
            if (!ReferenceEquals(element, selection.AnchorElement)
                || !IsTextControl(element)
                || !TryGetControlSelectionRange(out var start, out var end)
                || box.Text is not { Lines.Length: > 0 } text)
            {
                return;
            }

            var controlStarts = LineStarts(_editText ?? string.Empty, text.Lines);
            PaintSelectionRects(instructions, box, text.Lines, controlStarts, start, end, clip: true);
            return;
        }

        if (selection.IsDevTools != IsInDevToolsTree(element))
        {
            return;
        }

        var runs = GetTextRuns(selection.IsDevTools);

        if (_textRunIndex is null
            || !_textRunIndex.TryGetValue(element, out var run)
            || !TryResolveSelectionRuns(selection, runs, out var startRun, out var startOffset,
                out var endRun, out var endOffset))
        {
            return;
        }

        if (run.Index < startRun || run.Index > endRun)
        {
            return;
        }

        var from = run.Index == startRun ? startOffset : 0;
        var to = run.Index == endRun ? endOffset : run.Text.Length;

        // Paint from the box being emitted rather than the cached run: a full
        // layout builds new boxes while the cache still describes the previous
        // frame, and using stale geometry would draw the highlight in the wrong
        // place for one frame after a resize or reflow.
        if (box.Text is { Lines.Length: > 0 } currentText)
        {
            var current = CreateTextRun(element, box, currentText, selection.IsDevTools);
            PaintSelectionRects(instructions, box, current.Lines, current.LineStarts, from, to, clip: false);
        }
        else
        {
            PaintSelectionRects(instructions, box, run.Lines, run.LineStarts, from, to, clip: false);
        }
    }

    /// <summary>
    /// Adds one rectangle per visual line for an offset range in a run.
    /// </summary>
    /// <param name="instructions">Destination draw list.</param>
    /// <param name="box">Box supplying origin, scroll and clip geometry.</param>
    /// <param name="lines">Visual lines of the run.</param>
    /// <param name="starts">Offset of each line within the run's text.</param>
    /// <param name="start">Inclusive start offset.</param>
    /// <param name="end">Exclusive end offset.</param>
    /// <param name="clip">
    /// <see langword="true"/> to clip to the content box instead of ancestor scroll
    /// containers. Used for text controls.
    /// </param>
    /// <remarks>
    /// Each line is clipped to its own span, so a range that starts mid-line does
    /// not highlight the whole line. Rectangles are forced to at least one pixel
    /// wide and tall so glyph runs that measure smaller than a pixel stay visible,
    /// and intersections that collapse to nothing are skipped.
    /// </remarks>
    private void PaintSelectionRects(
        InstructionBuffer instructions,
        LayoutBox box,
        TextLine[] lines,
        int[] starts,
        int start,
        int end,
        bool clip)
    {
        if (start >= end || box.Text is not { } text)
        {
            return;
        }

        var (scrollX, scrollY) = AccumulatedScroll(box);
        var originX = box.TextOrigin.X - scrollX;
        var originY = box.TextOrigin.Y - scrollY;
        Rect? clipRect = clip
            ? box.ContentBox.Translate(-scrollX, -scrollY)
            : SelectionClipRect(box);
        var style = text.Style;
        var firstLine = 0;
        var lastLine = lines.Length;

        // Only touch lines that can contribute to the painted clip. This keeps
        // selecting a very large textarea proportional to what is visible
        // instead of the whole buffer.
        if (clipRect is { } visible)
        {
            firstLine = FirstLineAtOrAfter(lines, visible.Y);
            lastLine = FirstLineAfter(lines, visible.Bottom);
        }

        for (var i = firstLine; i < lastLine; i++)
        {
            var line = lines[i];
            var lineStart = starts[i];
            var lineEnd = lineStart + line.Text.Length;
            var from = Math.Max(start, lineStart);
            var to = Math.Min(end, lineEnd);

            if (to <= from)
            {
                continue;
            }

            var x1 = originX + line.X + MeasurePrefix(line.Text, from - lineStart, in style);
            var x2 = originX + line.X + MeasurePrefix(line.Text, to - lineStart, in style);
            var rect = new Rect(
                x1,
                originY + line.Y,
                MathF.Max(1, x2 - x1),
                MathF.Max(1, line.Height));

            if (clipRect is { } limit)
            {
                rect = rect.Intersect(limit);
            }

            if (rect.IsEmpty)
            {
                continue;
            }

            instructions.AddDrawRect(rect, SelectionColor);
        }
    }

    /// <summary>Finds the first line whose bottom edge reaches a Y coordinate.</summary>
    private static int FirstLineAtOrAfter(TextLine[] lines, float y)
    {
        var low = 0;
        var high = lines.Length;

        while (low < high)
        {
            var middle = low + (high - low) / 2;
            var line = lines[middle];

            if (line.Y + line.Height < y)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>Finds the first line whose top edge is below a Y coordinate.</summary>
    private static int FirstLineAfter(TextLine[] lines, float y)
    {
        var low = 0;
        var high = lines.Length;

        while (low < high)
        {
            var middle = low + (high - low) / 2;

            if (lines[middle].Y <= y)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// Intersects the painted padding boxes of every scrolling box on the path from
    /// a box to the layout root.
    /// </summary>
    /// <param name="box">Box whose painted highlight is being clipped.</param>
    /// <returns>
    /// The combined clip rectangle, or <see langword="null"/> when nothing on the
    /// path scrolls and no clipping is needed.
    /// </returns>
    /// <remarks>
    /// Using the padding box keeps the highlight inside the scrollport and under any
    /// borders, and follows the same scroll translation the box was painted with.
    /// </remarks>
    private static Rect? SelectionClipRect(LayoutBox box)
    {
        Rect? clip = null;

        for (var current = box; current is not null; current = current.Parent)
        {
            if (current.IsScrollContainer)
            {
                var next = PaintedPaddingBox(current);
                clip = clip is { } existing ? existing.Intersect(next) : next;
            }
        }

        return clip;
    }

    /// <summary>
    /// Computes the offset of each laid-out line within a control's edit buffer.
    /// </summary>
    /// <param name="text">Edit buffer the lines were laid out from.</param>
    /// <param name="lines">Visual lines produced by the layout engine.</param>
    /// <returns>
    /// An array parallel to <paramref name="lines"/> holding buffer offsets.
    /// </returns>
    /// <remarks>
    /// Lines are matched forward with an ordinal search starting at the end of the
    /// previous line, which keeps repeated text unambiguous. Empty lines take the
    /// current search position. If a line cannot be found, for example because
    /// layout trimmed or transformed it, the current position is used so offsets
    /// stay monotonic instead of failing.
    /// </remarks>
    private int[] LineStarts(string text, TextLine[] lines)
    {
        if (ReferenceEquals(_controlLineStartsText, text)
            && ReferenceEquals(_controlLineStartsLines, lines)
            && _controlLineStarts is not null)
        {
            return _controlLineStarts;
        }

        var starts = new int[lines.Length];
        var search = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var lineText = lines[i].Text;

            if (lineText.Length == 0)
            {
                starts[i] = Math.Min(search, text.Length);
                continue;
            }

            var index = text.IndexOf(lineText, Math.Min(search, text.Length), StringComparison.Ordinal);

            if (index < 0)
            {
                index = Math.Min(search, text.Length);
            }

            starts[i] = index;
            search = Math.Min(text.Length, index + lineText.Length);
        }

        _controlLineStartsText = text;
        _controlLineStartsLines = lines;
        _controlLineStarts = starts;
        return starts;
    }

    // ------------------------------------------------------------ interactions

    /// <summary>
    /// Arms drag selection after a press on selectable text.
    /// </summary>
    /// <remarks>
    /// Records the press position for the three-pixel threshold and clears the
    /// dragged flag so the release can distinguish a click from a drag.
    /// </remarks>
    private void BeginSelectionDrag()
    {
        _selectionDragging = true;
        _selectionDragged = false;
        _selectionDownX = _mouseX;
        _selectionDownY = _mouseY;
    }

    /// <summary>
    /// Updates the selection while the primary button is held and the pointer moves.
    /// </summary>
    /// <param name="x">Pointer X in client coordinates.</param>
    /// <param name="y">Pointer Y in client coordinates.</param>
    /// <remarks>
    /// <para>
    /// Movement beyond three pixels in either axis sets the dragged flag, which
    /// suppresses the synthetic click on release.
    /// </para>
    /// <para>
    /// For a control selection the pointer is mapped through the laid-out lines.
    /// Above the content box the focus snaps to the start of the buffer and below
    /// it to the end, which is how dragging past the control selects everything in
    /// between.
    /// </para>
    /// <para>
    /// For a document selection the pointer is hit-tested against selectable text
    /// first; once it leaves the text the nearest run and offset are used so the
    /// selection keeps extending instead of freezing. A press that did not produce
    /// a selection is ignored.
    /// </para>
    /// </remarks>
    private void UpdateSelectionDrag(float x, float y)
    {
        if (MathF.Abs(x - _selectionDownX) > 3 || MathF.Abs(y - _selectionDownY) > 3)
        {
            _selectionDragged = true;
        }

        if (_selection is not { } selection)
        {
            return;
        }

        if (selection.IsControl)
        {
            if (_focused is null || !IsTextControl(_focused))
            {
                return;
            }

            var layout = LayoutFor(_focused);

            if (layout is null
                || !layout.TryGetBox(_focused, out var box)
                || box.Text is not { Lines.Length: > 0 } text)
            {
                return;
            }

            var textValue = _editText ?? string.Empty;
            var starts = LineStarts(textValue, text.Lines);
            var scroll = AccumulatedScroll(box);
            var top = box.ContentBox.Y - scroll.Y;
            var bottom = box.ContentBox.Bottom - scroll.Y;
            var offset = y < top
                ? 0
                : y > bottom
                    ? textValue.Length
                    : HitTestTextLines(box, text.Lines, starts, text.Style, x, y);
            _caretIndex = Math.Clamp(offset, 0, textValue.Length);
            selection.FocusOffset = _caretIndex;
            ResetCaretBlink();
            InvalidateSelectionPaint(selection.IsDevTools);
            return;
        }

        if (TryHitText(x, y, selection.IsDevTools, out var run, out var hitOffset))
        {
            SetTextSelectionFocus(run, hitOffset);
            return;
        }

        var nearest = NearestTextRun(x, y, selection.IsDevTools, out var nearestOffset);

        if (nearest is not null)
        {
            SetTextSelectionFocus(nearest, nearestOffset);
        }
    }

    /// <summary>
    /// Classifies a press as a single, double or triple click.
    /// </summary>
    /// <param name="element">Element that received the press.</param>
    /// <param name="x">Press X in client coordinates.</param>
    /// <param name="y">Press Y in client coordinates.</param>
    /// <returns>The click count, from 1 to 3.</returns>
    /// <remarks>
    /// A press continues the run when it lands within 0.5 seconds and six pixels of
    /// the previous one on the same element; otherwise the count restarts at one.
    /// The count saturates at three, so a fourth rapid click remains a triple click.
    /// </remarks>
    private int RegisterClick(ElementNode element, float x, float y)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _lastClickTicks) / (double)Stopwatch.Frequency;

        if (elapsed <= 0.5
            && ReferenceEquals(element, _lastClickElement)
            && MathF.Abs(x - _lastClickX) < 6
            && MathF.Abs(y - _lastClickY) < 6)
        {
            _clickCount = Math.Min(3, _clickCount + 1);
        }
        else
        {
            _clickCount = 1;
        }

        _lastClickTicks = now;
        _lastClickX = x;
        _lastClickY = y;
        _lastClickElement = element;
        return _clickCount;
    }

    /// <summary>
    /// Selects the word around an offset in a document run.
    /// </summary>
    /// <param name="run">Run containing the word.</param>
    /// <param name="offset">
    /// Offset used to find the word; clamped by <see cref="WordAt"/>.
    /// </param>
    /// <remarks>
    /// Used for double clicks on page text. The selection is replaced rather than
    /// extended, matching browser behaviour.
    /// </remarks>
    private void SelectWord(TextRun run, int offset)
    {
        var (start, end) = WordAt(run.Text, offset);
        _selection = new TextSelection
        {
            IsControl = false,
            IsDevTools = run.IsDevTools,
            AnchorElement = run.Element,
            FocusElement = run.Element,
            AnchorOffset = start,
            FocusOffset = end
        };

        InvalidateSelectionPaint(run.IsDevTools);
    }

    /// <summary>
    /// Selects all text of one document run.
    /// </summary>
    /// <param name="run">Run to select.</param>
    /// <remarks>
    /// Used for triple clicks on page text, so only the clicked text block is
    /// selected rather than the whole document.
    /// </remarks>
    private void SelectAllInRun(TextRun run)
    {
        _selection = new TextSelection
        {
            IsControl = false,
            IsDevTools = run.IsDevTools,
            AnchorElement = run.Element,
            FocusElement = run.Element,
            AnchorOffset = 0,
            FocusOffset = run.Text.Length
        };

        InvalidateSelectionPaint(run.IsDevTools);
    }

    /// <summary>
    /// Selects the word around an offset in the focused text control.
    /// </summary>
    /// <param name="offset">
    /// Offset used to find the word; clamped by <see cref="WordAt"/>.
    /// </param>
    /// <remarks>
    /// Used for double clicks inside inputs and textareas. The caret is moved to the
    /// end of the selected word because the focus end becomes the caret.
    /// </remarks>
    private void SelectWordInControl(int offset)
    {
        var text = _editText ?? string.Empty;
        var (start, end) = WordAt(text, offset);
        SetControlSelection(start, end);
    }

    /// <summary>
    /// Gets whether either Shift key is currently held on any connected keyboard.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a Shift key is down; <see langword="false"/> when
    /// there is no input context.
    /// </returns>
    /// <remarks>
    /// The keyboard state is polled from every connected keyboard rather than
    /// tracked from key events, so the answer reflects the device even if a
    /// key-down was never delivered to this window.
    /// </remarks>
    private bool IsShiftPressed()
    {
        if (_input is null)
        {
            return false;
        }

        foreach (var keyboard in _input.Keyboards)
        {
            if (keyboard.IsKeyPressed(Key.ShiftLeft)
                || keyboard.IsKeyPressed(Key.ShiftRight))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Selects the entire edit buffer of the focused control.
    /// </summary>
    /// <remarks>
    /// Used by triple clicks in a control and by Ctrl+A when a control is focused.
    /// The caret ends up at the end of the buffer.
    /// </remarks>
    private void SelectAllInControl()
    {
        SetControlSelection(0, _editText?.Length ?? 0);
    }

    /// <summary>
    /// Finds the word boundaries around an offset.
    /// </summary>
    /// <param name="text">Text to inspect.</param>
    /// <param name="offset">Offset to expand from; clamped to the text length.</param>
    /// <returns>
    /// The inclusive start and exclusive end of the selected word.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A word character is a letter, digit, underscore or apostrophe, so
    /// <c>don't</c> and <c>snake_case</c> stay together.
    /// </para>
    /// <para>
    /// When the offset sits on punctuation or at the end of the text, the search
    /// steps back one position if the preceding character is a word character.
    /// Otherwise the single character at the offset is returned, or an empty range
    /// when the offset is already at the end, so a double click on punctuation still
    /// selects that character.
    /// </para>
    /// </remarks>
    private static (int Start, int End) WordAt(string text, int offset)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (0, 0);
        }

        var index = Math.Clamp(offset, 0, text.Length);

        if (index == text.Length || !IsWordChar(text[index]))
        {
            if (index > 0 && IsWordChar(text[index - 1]))
            {
                index--;
            }
            else
            {
                return (index, Math.Min(text.Length, index + 1));
            }
        }

        var start = index;

        while (start > 0 && IsWordChar(text[start - 1]))
        {
            start--;
        }

        var end = index;

        while (end < text.Length && IsWordChar(text[end]))
        {
            end++;
        }

        return (start, end);
    }

    /// <summary>
    /// Gets whether a character counts as part of a word for selection and
    /// Ctrl+arrow navigation.
    /// </summary>
    /// <param name="value">Character to test.</param>
    /// <returns>
    /// <see langword="true"/> for letters, digits, underscore and apostrophe.
    /// </returns>
    /// <remarks>
    /// The classification is culture-independent and treats every punctuation mark
    /// other than the apostrophe as a word separator.
    /// </remarks>
    private static bool IsWordChar(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_' || value == '\'';
    }

    /// <summary>
    /// Finds the start of the word before an index.
    /// </summary>
    /// <param name="text">Text to search.</param>
    /// <param name="index">Index to search back from; clamped to the text length.</param>
    /// <returns>
    /// The index of the first character of the preceding word, or zero.
    /// </returns>
    /// <remarks>
    /// Skips trailing whitespace and then word characters. Punctuation is not
    /// consumed, so Ctrl+Backspace immediately after a punctuation mark returns the
    /// index unchanged and the caller deletes nothing.
    /// </remarks>
    private int PreviousWord(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);

        while (index > 0 && char.IsWhiteSpace(text[index - 1]))
        {
            index--;
        }

        while (index > 0 && IsWordChar(text[index - 1]))
        {
            index--;
        }

        return index;
    }

    /// <summary>
    /// Finds the index just past the word after an index.
    /// </summary>
    /// <param name="text">Text to search.</param>
    /// <param name="index">Index to search forward from; clamped to the text length.</param>
    /// <returns>
    /// The index after the following word and its trailing whitespace, or the text
    /// length.
    /// </returns>
    /// <remarks>
    /// Skips word characters and then whitespace. Punctuation is not treated as a
    /// separator, so Ctrl+Delete with the caret immediately before punctuation
    /// returns the index unchanged and the caller deletes nothing.
    /// </remarks>
    private int NextWord(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);

        while (index < text.Length && IsWordChar(text[index]))
        {
            index++;
        }

        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// Normalizes text bound for the edit buffer to the form browsers expose.
    /// </summary>
    /// <param name="element">Control receiving the text.</param>
    /// <param name="text">
    /// Raw text, typically clipboard content or an element attribute value.
    /// </param>
    /// <returns>
    /// LF-only text for a <c>textarea</c>, or text with all line breaks removed for
    /// a single-line input.
    /// </returns>
    /// <remarks>
    /// Keeping the buffer in this form makes caret and selection offsets agree with
    /// the laid-out lines, which never contain carriage returns. A single-line input
    /// silently joins lines rather than inserting spaces, so pasting two lines
    /// yields their concatenation.
    /// </remarks>
    private static string NormalizeEditText(ElementNode element, string text)
    {
        // Browsers expose textarea values with LF endings and strip line breaks
        // from single-line inputs. Keeping the edit buffer in that form makes
        // caret/selection offsets agree with the laid-out lines.
        return element.Tag == "textarea"
            ? text.Replace("\r\n", "\n").Replace('\r', '\n')
            : text.Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

    /// <summary>
    /// Gets whether an element paints text that can participate in document
    /// selection.
    /// </summary>
    /// <param name="element">Element to test.</param>
    /// <returns>
    /// <see langword="true"/> when the element is not a control or link-like widget
    /// and its box has laid-out text.
    /// </returns>
    /// <remarks>
    /// Links, buttons, selects and options are excluded even though they may paint
    /// text, matching their inability to be selected in this engine. The check needs
    /// a current layout, so an element without a box, or with a box but no text
    /// lines, returns <see langword="false"/>.
    /// </remarks>
    private bool HasSelectableText(ElementNode element)
    {
        if (IsTextControl(element)
            || element.Tag is "a" or "button" or "select" or "option")
        {
            return false;
        }

        var layout = LayoutFor(element);
        return layout is not null
            && layout.TryGetBox(element, out var box)
            && box.Text is { Lines.Length: > 0 };
    }
}
