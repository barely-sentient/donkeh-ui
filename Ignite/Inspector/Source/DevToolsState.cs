using System.Collections.Concurrent;
using System.Diagnostics;
using Ignite.Data;
using Ignite.Data.Dom;
using Ignite.DataEngine;
using Ignite.LayoutLab;
using Ignite.Parser;
using Ignite.UILab;

namespace Ignite.Inspector;

/// <summary>Where the inspector is docked.</summary>
public enum DevToolsDock
{
    Bottom,
    Right,
    Window
}

/// <summary>The inspector's active tab.</summary>
public enum DevToolsTab
{
    Elements,
    Performance
}

/// <summary>A range of rules that came from one stylesheet.</summary>
public readonly record struct StyleSheetRange(string Name, int StartRule, int EndRule)
{
    public bool Contains(int ruleIndex) => ruleIndex >= StartRule && ruleIndex < EndRule;
}

/// <summary>
/// Live state shared by the inspector panel and the window that hosts it.
/// Everything here points at the real, mounted DOM; no copies are taken.
/// </summary>
public sealed class DevToolsState
{
    private long _version;

    public bool Visible { get; set; }

    public DevToolsDock Dock { get; set; } = DevToolsDock.Bottom;

    public DevToolsTab Tab { get; set; } = DevToolsTab.Elements;

    /// <summary>The real app root node, not a clone.</summary>
    public Node? AppRoot { get; set; }

    public DataTable? Table { get; set; }

    public LayoutResult? Layout { get; set; }

    public RendererContext? Context { get; set; }

    public IReadOnlyList<StyleSheetRange> StyleSheets { get; set; } = [];

    /// <summary>The element or component selected in the DOM tree.</summary>
    public Node? Selected { get; set; }

    /// <summary>The node under the pointer in the DOM tree, highlighted in the app.</summary>
    public Node? Hovered { get; set; }

    /// <summary>A CSS class to focus after the panel next renders, if any.</summary>
    public string? PendingFocusClass { get; set; }

    /// <summary>
    /// Set by the host when Enter or ';' committed a value, so the panel can
    /// open a fresh "Add property" row instead of closing the current one.
    /// </summary>
    public bool AdvanceAfterCommit { get; set; }

    /// <summary>Logical nodes that are expanded in the tree.</summary>
    public HashSet<Node> Expanded { get; } = new(ReferenceEqualityComparer.Instance);

    public PerformanceStats Performance { get; } = new();

    private readonly ConcurrentQueue<Action> _pendingEdits = new();
    private readonly ConcurrentDictionary<ElementNode, int> _elementStyleSelectors =
        new(ReferenceEqualityComparer.Instance);
    private int _nextElementStyleId;

    /// <summary>
    /// Returns the selector id of the inspector-owned rule that targets this
    /// element, if one has been created.
    /// </summary>
    public bool TryGetElementStyleSelector(ElementNode element, out int selectorId) =>
        _elementStyleSelectors.TryGetValue(element, out selectorId);

    /// <summary>
    /// Adds a declaration to the selected element's own rule. The first call
    /// tags the element with a private attribute and creates a selector that
    /// matches only that element; later calls append to the same rule.
    /// </summary>
    public void AddElementStyleDeclaration(ElementNode element, int propertyId, int valueId)
    {
        var table = Table ?? throw new InvalidOperationException("No stylesheet is loaded.");

        if (_elementStyleSelectors.TryGetValue(element, out var existing))
        {
            table.AddRule(existing, propertyId, valueId, -1, -1);
            return;
        }

        var marker = "ignite-" + Interlocked.Increment(ref _nextElementStyleId);
        element.SetAttribute(
            InternTable.Intern("data-ignite-style"),
            InternTable.Intern(marker));

        var property = InternTable.GetString(propertyId) ?? string.Empty;
        var value = InternTable.GetString(valueId) ?? string.Empty;
        var css = "[data-ignite-style=\"" + marker + "\"] { " + property + ": " + value + "; }";
        var start = table.RuleCount;

        new Ignite.DataEngine.DataEngine(MediaConditionCompiler.Instance)
            .Build(Tokenizer.Parse(css), table);

        if (table.RuleCount > start)
        {
            _elementStyleSelectors[element] = table.GetRule(start).SelectorId;
        }
    }

    /// <summary>
    /// Raised on the host frame thread after an inspector edit changed the
    /// stylesheet. The host drops cached styles and lays the tree out again.
    /// </summary>
    public Action? StylesChanged { get; set; }

    /// <summary>
    /// Queues a stylesheet mutation so it runs on the window's frame thread.
    /// The inspector can live in a separate window on another thread.
    /// </summary>
    public void QueueEdit(Action edit) => _pendingEdits.Enqueue(edit);

    public bool TryDequeueEdit(out Action edit) => _pendingEdits.TryDequeue(out edit!);

    public void NotifyStylesChanged()
    {
        StylesChanged?.Invoke();
        Refresh();
    }

    public long Version => Interlocked.Read(ref _version);

    public void Refresh() => Interlocked.Increment(ref _version);
}

/// <summary>
/// Samples frame, layout, memory and CPU numbers. The window calls
/// <see cref="Sample"/> once per frame and the inspector reads the current
/// values plus a short history for the graph.
/// </summary>
public sealed class PerformanceStats
{
    private const int HistoryLength = 120;

    private readonly float[] _frameHistory = new float[HistoryLength];
    private readonly float[] _layoutHistory = new float[HistoryLength];
    private readonly float[] _renderHistory = new float[HistoryLength];
    private int _historyIndex;
    private int _historyCount;

    private long _lastCpuTimestamp;
    private TimeSpan _lastCpuTime;
    private bool _hasCpuSample;
    private static readonly Process CurrentProcess = Process.GetCurrentProcess();

    public double FrameMilliseconds { get; private set; }
    public double LayoutMilliseconds { get; private set; }
    public double RenderMilliseconds { get; private set; }
    public double UpdateMilliseconds { get; private set; }
    public double CpuPercent { get; private set; }
    public int FrameCount { get; private set; }
    public int InstructionCount { get; private set; }
    public int LayoutBoxCount { get; private set; }
    public int NodeCount { get; private set; }
    public long ManagedMemory { get; private set; }
    public long WorkingSet { get; private set; }
    public long PrivateMemory { get; private set; }
    public int ThreadCount { get; private set; }
    public int Gen0 { get; private set; }
    public int Gen1 { get; private set; }
    public int Gen2 { get; private set; }
    public double AverageFrameMilliseconds { get; private set; }
    public double MaxFrameMilliseconds { get; private set; }

    public ReadOnlySpan<float> FrameHistory => _frameHistory.AsSpan(0, _historyCount);
    public ReadOnlySpan<float> LayoutHistory => _layoutHistory.AsSpan(0, _historyCount);
    public ReadOnlySpan<float> RenderHistory => _renderHistory.AsSpan(0, _historyCount);

    public void Sample(
        double frameMs,
        double layoutMs,
        double renderMs,
        double updateMs,
        int frameCount,
        int instructionCount,
        int layoutBoxCount,
        int nodeCount)
    {
        FrameMilliseconds = frameMs;
        LayoutMilliseconds = layoutMs;
        RenderMilliseconds = renderMs;
        UpdateMilliseconds = updateMs;
        FrameCount = frameCount;
        InstructionCount = instructionCount;
        LayoutBoxCount = layoutBoxCount;
        NodeCount = nodeCount;

        _frameHistory[_historyIndex] = (float)frameMs;
        _layoutHistory[_historyIndex] = (float)layoutMs;
        _renderHistory[_historyIndex] = (float)renderMs;
        _historyIndex = (_historyIndex + 1) % HistoryLength;

        if (_historyCount < HistoryLength)
        {
            _historyCount++;
        }

        var sum = 0d;
        var max = 0d;

        for (var i = 0; i < _historyCount; i++)
        {
            sum += _frameHistory[i];
            max = Math.Max(max, _frameHistory[i]);
        }

        AverageFrameMilliseconds = _historyCount == 0 ? 0 : sum / _historyCount;
        MaxFrameMilliseconds = max;

        ManagedMemory = GC.GetTotalMemory(forceFullCollection: false);
        WorkingSet = Environment.WorkingSet;
        Gen0 = GC.CollectionCount(0);
        Gen1 = GC.CollectionCount(1);
        Gen2 = GC.CollectionCount(2);

        try
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = (now - _lastCpuTimestamp) / (double)Stopwatch.Frequency;

            // Process-wide counters (thread enumeration and kernel time) are
            // expensive; a once-per-second snapshot is plenty for the panel.
            if (!_hasCpuSample || elapsed >= 1.0)
            {
                var cpu = CurrentProcess.TotalProcessorTime;

                if (_hasCpuSample && elapsed > 0.0001)
                {
                    var cpuSeconds = (cpu - _lastCpuTime).TotalSeconds;
                    CpuPercent = Math.Clamp(
                        cpuSeconds / (elapsed * Environment.ProcessorCount) * 100.0,
                        0,
                        100);
                }

                PrivateMemory = CurrentProcess.PrivateMemorySize64;
                ThreadCount = CurrentProcess.Threads.Count;
                _lastCpuTimestamp = now;
                _lastCpuTime = cpu;
                _hasCpuSample = true;
            }
        }
        catch
        {
            // Process counters are best-effort.
        }
    }
}
