using System.Collections.Concurrent;
using System.Diagnostics;
using Ignite.Data;
using Ignite.Data.Dom;
using Ignite.DataEngine;
using Ignite.LayoutLab;
using Ignite.Parser;
using Ignite.UILab;

namespace Ignite.Inspector;

/// <summary>Identifies where the inspector is docked.</summary>
/// <remarks>
/// <see cref="Bottom"/> is the zero value, so a freshly created
/// <see cref="DevToolsState"/> docks at the bottom until the host applies a saved
/// preference.
/// </remarks>
public enum DevToolsDock
{
    /// <summary>
    /// Dock the panel across the bottom of the app viewport. Numeric value 0.
    /// </summary>
    Bottom,

    /// <summary>
    /// Dock the panel along the right edge of the app viewport. Numeric value 1.
    /// </summary>
    Right,

    /// <summary>
    /// Host the panel in its own native window. Numeric value 2.
    /// </summary>
    /// <remarks>
    /// The window is created by the windowing layer on a background STA thread
    /// and shares the same <see cref="DevToolsState"/> instance.
    /// </remarks>
    Window
}

/// <summary>Identifies the inspector tab currently shown in the panel body.</summary>
public enum DevToolsTab
{
    /// <summary>DOM tree and styles editor. Numeric value 0, the default.</summary>
    Elements,

    /// <summary>
    /// Live frame, layout, render, memory and CPU statistics. Numeric value 1.
    /// </summary>
    /// <remarks>
    /// While this tab is active the host samples performance roughly four times
    /// per second and refreshes the panel once per second.
    /// </remarks>
    Performance,

    /// <summary>
    /// Registered <c>@keyframes</c> groups and their declarations. Numeric
    /// value 2.
    /// </summary>
    Animations
}

/// <summary>
/// Maps a contiguous half-open range of rules in a stylesheet back to the sheet
/// that produced it.
/// </summary>
/// <param name="Name">
/// Display name of the origin, for example <c>user-agent</c>, <c>author #1</c>
/// or <c>devtools</c>.
/// </param>
/// <param name="StartRule">Index of the first rule in the range, inclusive.</param>
/// <param name="EndRule">Index one past the last rule, exclusive.</param>
/// <remarks>
/// Ranges are produced in cascade order by the host when it rebuilds styles:
/// user agent first, then each author sheet, then the inspector stylesheet. A
/// range owns no rules; it only labels indices in the shared stylesheet table.
/// </remarks>
public readonly record struct StyleSheetRange(string Name, int StartRule, int EndRule)
{
    /// <summary>
    /// Determines whether a rule index falls inside this range.
    /// </summary>
    /// <param name="ruleIndex">The zero-based rule index to test.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="ruleIndex"/> is at least
    /// <see cref="StartRule"/> and less than <see cref="EndRule"/>.
    /// </returns>
    public bool Contains(int ruleIndex) => ruleIndex >= StartRule && ruleIndex < EndRule;
}

/// <summary>
/// Live state shared by the inspector panel and the window that hosts it.
/// Everything here points at the real, mounted DOM; no copies are taken.
/// </summary>
/// <remarks>
/// <para>
/// A window owns exactly one instance, exposed as <c>IgniteWindow.DevTools</c>,
/// and shares it with the standalone devtools window when <see cref="Dock"/> is
/// <see cref="DevToolsDock.Window"/>. The host updates it from the frame loop;
/// the panel polls <see cref="Version"/> through <c>InspectorPanel.Tick</c> and
/// rebuilds only when the version changes.
/// </para>
/// <para>
/// Stylesheet and DOM mutations are never applied by the panel directly. They are
/// queued with <see cref="QueueEdit"/> and drained by the host on its frame
/// thread, because the standalone panel runs on a different thread. Pointer and
/// selection members are plain properties without locking; they are expected to
/// be written on the thread that owns the panel and read shortly afterwards on
/// the frame thread.
/// </para>
/// <para>
/// Only <see cref="Version"/>, <see cref="Refresh"/>, <see cref="QueueEdit"/> and
/// <see cref="TryDequeueEdit"/> are safe to call from multiple threads.
/// </para>
/// </remarks>
public sealed class DevToolsState
{
    /// <summary>
    /// Backing counter for <see cref="Version"/>, accessed through
    /// <see cref="Interlocked"/>.
    /// </summary>
    private long _version;

    /// <summary>
    /// Gets or sets a value indicating whether the inspector is shown.
    /// </summary>
    /// <remarks>
    /// Toggled by the host shortcut and the toolbar close button. Hiding the
    /// panel also exits inspect mode and closes the colour picker in the host;
    /// while false the host skips inspector layout and refresh work.
    /// </remarks>
    public bool Visible { get; set; }

    /// <summary>
    /// Gets or sets the edge the inspector is docked to, or whether it lives in
    /// its own native window.
    /// </summary>
    /// <remarks>
    /// <see cref="DevToolsDock.Window"/> makes the host open the standalone
    /// window; any other value closes it. Switching between the docked edges
    /// reflows the app viewport to make room for the panel.
    /// </remarks>
    public DevToolsDock Dock { get; set; } = DevToolsDock.Bottom;

    /// <summary>Gets or sets the height of the bottom dock in CSS pixels.</summary>
    /// <value>
    /// A value in the range 160..900, normally 320
    /// (<see cref="InspectorStyles.DockBottomHeight"/>).
    /// </value>
    /// <remarks>
    /// The value is used both to shrink the app viewport and as the
    /// <c>dt-dockh-N</c> height class. Splitter drags clamp it to
    /// 160..min(900, viewport height - 120), so values outside that range have no
    /// matching class in <see cref="InspectorStyles.HeightClasses"/>.
    /// </remarks>
    public int DockBottomHeight { get; set; } = (int)InspectorStyles.DockBottomHeight;

    /// <summary>Gets or sets the width of the right dock in CSS pixels.</summary>
    /// <value>
    /// A value in the range 280..1000, normally 460
    /// (<see cref="InspectorStyles.DockRightWidth"/>).
    /// </value>
    /// <remarks>
    /// Used to shrink the app viewport and as the <c>dt-dockw-N</c> width class.
    /// Splitter drags clamp it to 280..min(1000, viewport width - 320), leaving at
    /// least 320 px for the app.
    /// </remarks>
    public int DockRightWidth { get; set; } = (int)InspectorStyles.DockRightWidth;

    /// <summary>
    /// Gets or sets the app viewport width in CSS pixels, used to clamp
    /// right-dock resizing.
    /// </summary>
    /// <remarks>Updated by the host on every layout; zero until the first frame.</remarks>
    public int ViewportWidth { get; set; }

    /// <summary>
    /// Gets or sets the app viewport height in CSS pixels, used to clamp
    /// bottom-dock resizing.
    /// </summary>
    /// <remarks>Updated by the host on every layout; zero until the first frame.</remarks>
    public int ViewportHeight { get; set; }

    /// <summary>
    /// Gets or sets the last pointer X position in window client coordinates.
    /// </summary>
    /// <remarks>
    /// Updated on pointer moves by the host. The dock splitter converts it into a
    /// panel width or height when a drag is active.
    /// </remarks>
    public float PointerX { get; set; }

    /// <summary>
    /// Gets or sets the last pointer Y position in window client coordinates.
    /// </summary>
    /// <remarks>
    /// Updated on pointer moves by the host. The dock splitter converts it into a
    /// panel height when a drag is active.
    /// </remarks>
    public float PointerY { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a dock splitter is being dragged.
    /// Pointer moves must not resize the dock on hover: changing the app viewport
    /// invalidates every cached style and makes the whole page relayout on every
    /// pointer move.
    /// </summary>
    /// <remarks>
    /// Set on splitter pointer-down and cleared on pointer-up. While true, hover
    /// moves resize the dock; while false they are ignored.
    /// </remarks>
    public bool IsResizingDock { get; set; }

    /// <summary>Gets or sets the tab shown in the panel body.</summary>
    /// <remarks>
    /// Defaults to <see cref="DevToolsTab.Elements"/>. The host refreshes the
    /// Performance tab on a one-second timer and rebuilds Elements only when the
    /// mounted app changes.
    /// </remarks>
    public DevToolsTab Tab { get; set; } = DevToolsTab.Elements;

    /// <summary>Gets or sets the real app root node, not a clone.</summary>
    /// <remarks>
    /// Set by the host after mount. The DOM tree renders the logical children of
    /// this node and uses node identity for selection and expansion. The
    /// inspector never changes app structure, but it does add a private
    /// <c>data-ignite-style</c> marker attribute to elements it styles.
    /// </remarks>
    public Node? AppRoot { get; set; }

    /// <summary>
    /// Gets or sets the compiled stylesheet the inspector matches rules against.
    /// </summary>
    /// <remarks>
    /// This is the same stylesheet table the app uses, including any inspector
    /// rules. The host rebuilds it when stylesheets change; rule indices are only
    /// valid for the current table instance.
    /// </remarks>
    public DataTable? Table { get; set; }

    /// <summary>
    /// Gets or sets the most recent layout result, used to display computed
    /// styles.
    /// </summary>
    /// <remarks>Null before the first layout pass or when layout was skipped.</remarks>
    public LayoutResult? Layout { get; set; }

    /// <summary>
    /// Gets or sets the renderer context used for selector matching.
    /// </summary>
    /// <remarks>
    /// Container queries and media conditions must be evaluated with the same
    /// context the app laid out with, so the panel passes this to the table's
    /// matching API.
    /// </remarks>
    public RendererContext? Context { get; set; }

    /// <summary>
    /// Gets or sets the ordered rule ranges that identify which stylesheet each
    /// rule came from.
    /// </summary>
    /// <remarks>
    /// Populated by the host in cascade order. Empty when no table has been
    /// built; rule indices that match no range are labelled <c>inspector</c> by
    /// the panel.
    /// </remarks>
    public IReadOnlyList<StyleSheetRange> StyleSheets { get; set; } = [];

    /// <summary>Gets or sets the element or component selected in the DOM tree.</summary>
    /// <remarks>
    /// Compared by reference and cleared by the panel when the node leaves the
    /// mounted tree. Selecting a row in the tree also closes the colour picker.
    /// </remarks>
    public Node? Selected { get; set; }

    /// <summary>
    /// Gets or sets the node under the pointer in the DOM tree, highlighted in
    /// the app.
    /// </summary>
    /// <remarks>
    /// Set by row mouse-over and by inspect mode while the pointer moves over the
    /// app. Cleared when the pointer leaves the row, when inspect mode is
    /// toggled, or when the node unmounts.
    /// </remarks>
    public Node? Hovered { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the next app click selects an
    /// element instead of activating it.
    /// </summary>
    /// <remarks>
    /// Toggled by the toolbar picker button. The host clears it as soon as an
    /// element is selected, so it is a one-shot mode rather than a sticky one.
    /// </remarks>
    public bool InspectMode { get; set; }

    /// <summary>
    /// Gets or sets a one-shot request for the DOM tree to scroll the selected
    /// row into view.
    /// </summary>
    /// <remarks>
    /// Set by the host after inspect-mode selection; the host consumes and resets
    /// it on the next frame. It is a flag, not an event, so repeated requests
    /// before consumption collapse into one scroll.
    /// </remarks>
    public bool ScrollSelectedIntoView { get; set; }

    /// <summary>
    /// Gets or sets the CSS class to focus after the panel next renders.
    /// </summary>
    /// <remarks>
    /// The host consumes and clears this after layout and focuses the first
    /// inspector element carrying the class. Used to place the caret in a new
    /// "Add property" row.
    /// </remarks>
    public string? PendingFocusClass { get; set; }

    /// <summary>
    /// Set by the host when Enter or ';' committed a value, so the panel can
    /// open a fresh "Add property" row instead of closing the current one.
    /// </summary>
    /// <remarks>
    /// The next value or add commit consumes this flag and immediately opens
    /// another add row, matching DevTools' rapid-entry workflow. The panel clears
    /// it in the commit handlers before doing any other work.
    /// </remarks>
    public bool AdvanceAfterCommit { get; set; }

    /// <summary>
    /// Gets or sets the declaration whose colour swatch opened the picker, or
    /// -1 when the picker is closed.
    /// </summary>
    /// <remarks>
    /// The value indexes <see cref="Table"/>; it can become stale if the table is
    /// rebuilt while the picker is open. The picker is only rendered on the
    /// Elements tab.
    /// </remarks>
    public int ColorPickerRuleIndex { get; set; } = -1;

    /// <summary>
    /// Gets or sets the <c>@keyframes</c> name selected in the Animations tab.
    /// </summary>
    /// <remarks>
    /// Null or an unknown name makes the panel fall back to the first registered
    /// animation group.
    /// </remarks>
    public string? SelectedAnimation { get; set; }

    /// <summary>
    /// Gets or sets the colour text currently shown in the picker.
    /// </summary>
    /// <remarks>
    /// Normalised to <c>#RRGGBB</c> or <c>#RRGGBBAA</c> when the picker opens and
    /// the text parses; unparseable text is kept verbatim. Thereafter it is updated
    /// as valid values are typed or picked.
    /// </remarks>
    public string ColorPickerValue { get; set; } = string.Empty;

    /// <summary>Gets the logical nodes that are expanded in the DOM tree.</summary>
    /// <remarks>
    /// Uses reference equality, so entries must be the exact mounted node
    /// instances. The panel prunes entries for nodes that unmount. The set is not
    /// synchronized and is mutated during render and on row clicks.
    /// </remarks>
    public HashSet<Node> Expanded { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Gets the performance sampler shared with the window's frame loop.
    /// </summary>
    /// <remarks>
    /// Created once with the state. The host calls
    /// <see cref="PerformanceStats.Sample"/> while the Performance tab is
    /// visible; the panel only reads the values.
    /// </remarks>
    public PerformanceStats Performance { get; } = new();

    /// <summary>Edits queued by the panel and drained by the host frame thread.</summary>
    private readonly ConcurrentQueue<Action> _pendingEdits = new();

    /// <summary>
    /// Selector id of the inspector-owned rule for each element, so later
    /// declarations append to the same rule instead of creating another marker.
    /// </summary>
    private readonly ConcurrentDictionary<ElementNode, int> _elementStyleSelectors =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Reused buffer for <see cref="PruneElementStyles"/>; avoids allocating a
    /// list on every render.
    /// </summary>
    private readonly List<ElementNode> _staleElementStyles = [];

    /// <summary>Monotonic suffix for the private marker attribute values.</summary>
    private int _nextElementStyleId;

    /// <summary>
    /// Drops inspector-created element-style bookkeeping for nodes that
    /// unmounted.
    /// </summary>
    /// <param name="live">
    /// Set of nodes still reachable from the mounted app root, usually built by
    /// the panel's expansion pruning pass.
    /// </param>
    /// <remarks>
    /// <para>
    /// Forgets the inspector rule mapping for every element no longer in
    /// <paramref name="live"/>, which stops later edits from targeting a dead
    /// node. The compiled rules themselves are not removed from
    /// <see cref="Table"/>; only the bookkeeping is dropped.
    /// </para>
    /// <para>
    /// The scan is linear in the number of inspected elements and reuses an
    /// internal list, so it is cheap enough to run on every rebuild.
    /// </para>
    /// </remarks>
    public void PruneElementStyles(IReadOnlySet<Node> live)
    {
        if (_elementStyleSelectors.IsEmpty)
        {
            return;
        }

        _staleElementStyles.Clear();

        foreach (var pair in _elementStyleSelectors)
        {
            if (!live.Contains(pair.Key))
            {
                _staleElementStyles.Add(pair.Key);
            }
        }

        for (var i = 0; i < _staleElementStyles.Count; i++)
        {
            _elementStyleSelectors.TryRemove(_staleElementStyles[i], out var ignoredSelector);
        }
    }

    /// <summary>
    /// Returns the selector id of the inspector-owned rule that targets this
    /// element, if one has been created.
    /// </summary>
    /// <param name="element">The mounted element to look up.</param>
    /// <param name="selectorId">
    /// Receives the selector id when found; otherwise <c>0</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if an inspector rule exists for the element.
    /// </returns>
    /// <remarks>
    /// The mapping is created by <see cref="AddElementStyleDeclaration"/> and
    /// removed by <see cref="PruneElementStyles"/>; it is not persisted.
    /// </remarks>
    public bool TryGetElementStyleSelector(ElementNode element, out int selectorId) =>
        _elementStyleSelectors.TryGetValue(element, out selectorId);

    /// <summary>
    /// Adds a declaration to the selected element's own rule. The first call
    /// tags the element with a private attribute and creates a selector that
    /// matches only that element; later calls append to the same rule.
    /// </summary>
    /// <param name="element">The mounted element to style.</param>
    /// <param name="propertyId">Interned property name id.</param>
    /// <param name="valueId">Interned value id.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="Table"/> is <see langword="null"/>; no stylesheet
    /// has been built yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The first declaration adds a <c>data-ignite-style="ignite-N"</c> attribute
    /// to the element and compiles the generated rule
    /// <c>[data-ignite-style="ignite-N"] { property: value; }</c> into the shared
    /// table. The rule's selector id is remembered so subsequent declarations can
    /// be appended with <c>AddRule</c>.
    /// </para>
    /// <para>
    /// This mutates both the DOM and the stylesheet table, so callers normally
    /// wrap it in <see cref="QueueEdit"/> so it runs on the frame thread. If the
    /// generated rule does not increase the rule count, no mapping is recorded
    /// and the next call will try again.
    /// </para>
    /// </remarks>
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
    /// <remarks>
    /// Assigned by the host window during construction. Exceptions thrown by the
    /// handler propagate to the code that called
    /// <see cref="NotifyStylesChanged"/>.
    /// </remarks>
    public Action? StylesChanged { get; set; }

    /// <summary>
    /// Queues a stylesheet mutation so it runs on the window's frame thread.
    /// The inspector can live in a separate window on another thread.
    /// </summary>
    /// <param name="edit">
    /// The mutation to run. It must be quick and must not block waiting on the
    /// panel thread.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="edit"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Thread-safe and non-blocking. Edits run in FIFO order before layout in the
    /// host's update step; an exception from one aborts the drain loop and
    /// propagates out of the host update.
    /// </remarks>
    public void QueueEdit(Action edit) => _pendingEdits.Enqueue(edit);

    /// <summary>Attempts to dequeue the next queued stylesheet edit.</summary>
    /// <param name="edit">Receives the next edit when one is available.</param>
    /// <returns>
    /// <see langword="true"/> if an edit was dequeued; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Thread-safe and non-blocking; called only by the host frame loop.
    /// </remarks>
    public bool TryDequeueEdit(out Action edit) => _pendingEdits.TryDequeue(out edit!);

    /// <summary>
    /// Raises <see cref="StylesChanged"/> and bumps <see cref="Version"/>.
    /// </summary>
    /// <remarks>
    /// Call after mutating the stylesheet, normally from inside a queued edit, so
    /// the host relayouts and the panel rebuilds. Handlers run synchronously on
    /// the calling thread.
    /// </remarks>
    public void NotifyStylesChanged()
    {
        StylesChanged?.Invoke();
        Refresh();
    }

    /// <summary>
    /// Gets the current state version, incremented by <see cref="Refresh"/>.
    /// </summary>
    /// <remarks>
    /// Starts at 0. The panel caches the last version it rendered and skips work
    /// while it is unchanged. The read is atomic, but the counter can eventually
    /// wrap after 2^63 increments.
    /// </remarks>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>Marks the state as changed so observers rebuild.</summary>
    /// <remarks>
    /// Thread-safe. Does not itself notify the panel; <c>InspectorPanel.Tick</c>
    /// compares <see cref="Version"/> and requests a render when it changes.
    /// </remarks>
    public void Refresh() => Interlocked.Increment(ref _version);
}

/// <summary>
/// Samples frame, layout, memory and CPU numbers. The window calls
/// <see cref="Sample"/> periodically while the Performance tab is visible, and
/// the inspector reads the current values plus a short history for the graph.
/// </summary>
/// <remarks>
/// <para>
/// The host samples at most every 0.25 seconds while the Performance tab is
/// visible. The three history buffers hold 120 samples each in a ring; the
/// instantaneous properties always reflect the latest sample.
/// </para>
/// <para>
/// Process-wide counters (CPU, private bytes, thread count) are refreshed at most
/// once per second because enumerating threads and reading kernel times is
/// comparatively expensive. Failures are swallowed so sampling never breaks a
/// frame.
/// </para>
/// <para>
/// This type is not thread-safe: <see cref="Sample"/> is expected on the frame
/// thread, and the span-returning history properties are views over internal
/// arrays that the next sample overwrites.
/// </para>
/// </remarks>
public sealed class PerformanceStats
{
    /// <summary>Number of samples retained for each graph history.</summary>
    private const int HistoryLength = 120;

    /// <summary>Ring buffer of recent frame times in milliseconds.</summary>
    private readonly float[] _frameHistory = new float[HistoryLength];

    /// <summary>Ring buffer of recent layout pass times in milliseconds.</summary>
    private readonly float[] _layoutHistory = new float[HistoryLength];

    /// <summary>Ring buffer of recent render submission times in milliseconds.</summary>
    private readonly float[] _renderHistory = new float[HistoryLength];

    /// <summary>Next slot to write in the ring buffers.</summary>
    private int _historyIndex;

    /// <summary>Number of valid samples, up to <see cref="HistoryLength"/>.</summary>
    private int _historyCount;

    /// <summary>Timestamp of the previous process-wide CPU snapshot.</summary>
    private long _lastCpuTimestamp;

    /// <summary>Total processor time at the previous CPU snapshot.</summary>
    private TimeSpan _lastCpuTime;

    /// <summary>
    /// True once a baseline CPU sample exists; the first sample reports 0% CPU
    /// because there is no interval to divide by.
    /// </summary>
    private bool _hasCpuSample;

    /// <summary>
    /// Cached current process, used by the throttled process-wide counters.
    /// Disposed only when the process exits.
    /// </summary>
    private static readonly Process CurrentProcess = Process.GetCurrentProcess();

    /// <summary>Gets the duration of the last frame in milliseconds.</summary>
    /// <value>Wall-clock frame time; 0 before the first sample.</value>
    public double FrameMilliseconds { get; private set; }

    /// <summary>Gets the duration of the last layout pass in milliseconds.</summary>
    public double LayoutMilliseconds { get; private set; }

    /// <summary>
    /// Gets the duration of the last render submission in milliseconds.
    /// </summary>
    public double RenderMilliseconds { get; private set; }

    /// <summary>Gets the duration of the last UI update pass in milliseconds.</summary>
    public double UpdateMilliseconds { get; private set; }

    /// <summary>
    /// Gets the process CPU usage as a percentage of all logical cores.
    /// </summary>
    /// <value>
    /// 0..100, normalised by <see cref="Environment.ProcessorCount"/>; 0 until two
    /// CPU snapshots exist.
    /// </value>
    public double CpuPercent { get; private set; }

    /// <summary>
    /// Gets the total number of frames presented since the window started.
    /// </summary>
    public int FrameCount { get; private set; }

    /// <summary>
    /// Gets the number of draw instructions submitted in the last frame.
    /// </summary>
    public int InstructionCount { get; private set; }

    /// <summary>
    /// Gets the number of layout boxes produced by the last layout pass.
    /// </summary>
    public int LayoutBoxCount { get; private set; }

    /// <summary>Gets the number of live UI nodes in the mounted tree.</summary>
    public int NodeCount { get; private set; }

    /// <summary>
    /// Gets the managed heap size in bytes, measured without forcing a
    /// collection.
    /// </summary>
    public long ManagedMemory { get; private set; }

    /// <summary>Gets the process working set in bytes.</summary>
    public long WorkingSet { get; private set; }

    /// <summary>
    /// Gets the process private memory in bytes, refreshed at most once per
    /// second.
    /// </summary>
    public long PrivateMemory { get; private set; }

    /// <summary>
    /// Gets the process thread count, refreshed at most once per second.
    /// </summary>
    public int ThreadCount { get; private set; }

    /// <summary>Gets the number of generation 0 garbage collections so far.</summary>
    public int Gen0 { get; private set; }

    /// <summary>Gets the number of generation 1 garbage collections so far.</summary>
    public int Gen1 { get; private set; }

    /// <summary>Gets the number of generation 2 garbage collections so far.</summary>
    public int Gen2 { get; private set; }

    /// <summary>
    /// Gets the mean frame time in milliseconds across the retained history.
    /// </summary>
    public double AverageFrameMilliseconds { get; private set; }

    /// <summary>
    /// Gets the maximum frame time in milliseconds across the retained history.
    /// </summary>
    public double MaxFrameMilliseconds { get; private set; }

    /// <summary>Gets the retained frame-time samples in milliseconds.</summary>
    /// <value>
    /// Up to 120 values. Until the ring wraps these are in sample order; after
    /// that they are the raw ring-buffer contents, not chronological order.
    /// </value>
    /// <remarks>
    /// The span aliases internal storage and is invalidated by the next
    /// <see cref="Sample"/> call.
    /// </remarks>
    public ReadOnlySpan<float> FrameHistory => _frameHistory.AsSpan(0, _historyCount);

    /// <summary>Gets the retained layout-time samples in milliseconds.</summary>
    /// <value>
    /// Up to 120 values. Until the ring wraps these are in sample order; after
    /// that they are the raw ring-buffer contents, not chronological order.
    /// </value>
    /// <remarks>
    /// The span aliases internal storage and is invalidated by the next
    /// <see cref="Sample"/> call.
    /// </remarks>
    public ReadOnlySpan<float> LayoutHistory => _layoutHistory.AsSpan(0, _historyCount);

    /// <summary>Gets the retained render-time samples in milliseconds.</summary>
    /// <value>
    /// Up to 120 values. Until the ring wraps these are in sample order; after
    /// that they are the raw ring-buffer contents, not chronological order.
    /// </value>
    /// <remarks>
    /// The span aliases internal storage and is invalidated by the next
    /// <see cref="Sample"/> call.
    /// </remarks>
    public ReadOnlySpan<float> RenderHistory => _renderHistory.AsSpan(0, _historyCount);

    /// <summary>
    /// Records one frame's measurements and refreshes the process counters.
    /// </summary>
    /// <param name="frameMs">Total frame time in milliseconds.</param>
    /// <param name="layoutMs">Layout pass time in milliseconds.</param>
    /// <param name="renderMs">Render submission time in milliseconds.</param>
    /// <param name="updateMs">UI update pass time in milliseconds.</param>
    /// <param name="frameCount">
    /// Cumulative number of frames presented, owned by the host.
    /// </param>
    /// <param name="instructionCount">
    /// Number of draw instructions submitted in this frame.
    /// </param>
    /// <param name="layoutBoxCount">
    /// Number of layout boxes produced by the most recent layout pass.
    /// </param>
    /// <param name="nodeCount">Number of live UI nodes in the mounted tree.</param>
    /// <remarks>
    /// <para>
    /// Appends to the three history ring buffers and recomputes
    /// <see cref="AverageFrameMilliseconds"/> and
    /// <see cref="MaxFrameMilliseconds"/> by scanning the retained samples (120
    /// iterations). Managed memory and GC counts are read on every call; process
    /// CPU, private memory and thread count at most once per second.
    /// </para>
    /// <para>
    /// No exceptions escape. Failures while reading process counters leave the
    /// previous values in place.
    /// </para>
    /// </remarks>
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
