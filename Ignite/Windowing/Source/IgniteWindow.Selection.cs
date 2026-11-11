using System.Diagnostics;
using System.Text;
using Ignite.Data.Dom;
using Ignite.LayoutLab;
using Ignite.UILab;
using Silk.NET.Input;

namespace Ignite.Windowing;

/// <summary>
/// Text selection, clipboard integration and the browser-style editing
/// behaviours that go with them. Selection is painted by the layout engine's
/// custom painter, so highlights sit behind the glyphs and follow scrolling.
/// </summary>
public sealed partial class IgniteWindow
{
    // A translucent blue works on both the light app and the dark inspector.
    private const uint SelectionColor = 0x803390FF;

    private TextSelection? _selection;
    private bool _selectionDragging;
    private bool _selectionDragged;
    private float _selectionDownX;
    private float _selectionDownY;

    private long _lastClickTicks;
    private float _lastClickX;
    private float _lastClickY;
    private ElementNode? _lastClickElement;
    private int _clickCount;

    private List<TextRun>? _textRuns;
    private LayoutBox? _textRunsRoot;
    private bool _textRunsDevTools;
    private Dictionary<ElementNode, TextRun>? _textRunIndex;

    private sealed class TextSelection
    {
        public bool IsControl;
        public bool IsDevTools;
        public ElementNode? AnchorElement;
        public ElementNode? FocusElement;
        public int AnchorOffset;
        public int FocusOffset;
    }

    private sealed class TextRun
    {
        public ElementNode Element = null!;
        public LayoutBox Box = null!;
        public string Text = string.Empty;
        public TextLine[] Lines = [];
        public int[] LineStarts = [];
        public TextStyle Style;
        public bool IsDevTools;
        public bool IsInline;
        public int Index;
    }

    private bool HasControlSelection =>
        _selection is { IsControl: true } selection
        && _focused is not null
        && ReferenceEquals(selection.AnchorElement, _focused)
        && selection.AnchorOffset != selection.FocusOffset;

    // ------------------------------------------------------------- text runs

    private List<TextRun> GetTextRuns(bool devTools)
    {
        var layout = devTools ? _devToolsLayout : _layoutResult;

        if (layout?.Root is not { } root)
        {
            _textRuns = null;
            _textRunsRoot = null;
            _textRunIndex = null;
            return [];
        }

        if (_textRuns is not null
            && ReferenceEquals(_textRunsRoot, root)
            && _textRunsDevTools == devTools)
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
        _textRunIndex = index;
        return runs;
    }

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

    private static bool IsNonSelectableTag(ElementNode element)
    {
        return element.Tag is "select" or "option" or "textarea";
    }

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

    private static Rect PaintedPaddingBox(LayoutBox box)
    {
        var (x, y) = AccumulatedScroll(box.Parent);
        return box.PaddingBox.Translate(-x, -y);
    }

    // ------------------------------------------------------------ hit testing

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

    private static int NearestLine(TextLine[] lines, float y)
    {
        var best = 0;
        var bestDistance = float.MaxValue;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var distance = y < line.Y
                ? line.Y - y
                : y > line.Y + line.Height
                    ? y - (line.Y + line.Height)
                    : 0;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private int OffsetInLine(string text, float x, in TextStyle style)
    {
        var target = MathF.Max(0, x);
        var best = 0;
        var bestDistance = MathF.Abs(target);

        for (var i = 0; i < text.Length;)
        {
            var next = NextTextElement(text, i);
            var width = MeasurePrefix(text, next, in style);
            var distance = MathF.Abs(width - target);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = next;
            }

            i = next;
        }

        return best;
    }

    private float MeasurePrefix(string text, int length, in TextStyle style)
    {
        return length <= 0
            ? 0
            : _measurer.Measure(text.AsSpan(0, length), in style).Width;
    }

    // -------------------------------------------------------------- selection

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

        for (var i = 0; i < lines.Length; i++)
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

    private static int[] LineStarts(string text, TextLine[] lines)
    {
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

        return starts;
    }

    // ------------------------------------------------------------ interactions

    private void BeginSelectionDrag()
    {
        _selectionDragging = true;
        _selectionDragged = false;
        _selectionDownX = _mouseX;
        _selectionDownY = _mouseY;
    }

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

    private void SelectWordInControl(int offset)
    {
        var text = _editText ?? string.Empty;
        var (start, end) = WordAt(text, offset);
        SetControlSelection(start, end);
    }

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

    private void SelectAllInControl()
    {
        SetControlSelection(0, _editText?.Length ?? 0);
    }

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

    private static bool IsWordChar(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_' || value == '\'';
    }

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

    private static string NormalizeEditText(ElementNode element, string text)
    {
        // Browsers expose textarea values with LF endings and strip line breaks
        // from single-line inputs. Keeping the edit buffer in that form makes
        // caret/selection offsets agree with the laid-out lines.
        return element.Tag == "textarea"
            ? text.Replace("\r\n", "\n").Replace('\r', '\n')
            : text.Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

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
