using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Donkeh.Data;
using Donkeh.Data.Dom;
using Donkeh.Inspector;
using Donkeh.LayoutLab;
using Donkeh.Parser;
using Donkeh.UILab;
using Silk.NET.Core;
using Silk.NET.Input;
using Silk.NET.Maths;
using StbImageSharp;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Donkeh.Windowing;

/// <summary>
/// Owns the native window, the CSS table, layout, input routing and the OpenGL
/// display-list renderer. A UI tree is mounted once and patched by UILab; frames
/// only relayout when the tree, stylesheet, viewport or scroll state changed.
/// </summary>
/// <remarks>
/// <para>
/// An instance moves through three phases: construction (settings, text
/// measurer and an initial stylesheet build), <see cref="Mount"/> (the UILab
/// tree plus the optional inspector tree), and <see cref="Run(Donkeh.UILab.Node, string)"/> (native window,
/// OpenGL context and the frame loop). Everything before <see cref="Run(Donkeh.UILab.Node, string)"/> runs
/// on the caller's thread; <see cref="Run(Donkeh.UILab.Node, string)"/> blocks until the window closes and
/// disposes the window before returning.
/// </para>
/// <para>
/// Frames are demand driven. A layout rebuild is requested through
/// <see cref="Invalidate"/> or the internal <c>_needsLayout</c> flag, a display
/// list rebuild through <c>_needsPaint</c> (scroll offsets never move boxes),
/// and paint-only animation styles through <c>_needsAnimatedPaint</c>. The UILab
/// version counter plus the last viewport, dock and DPI values are compared each
/// frame, so an idle window does no work beyond presenting the previous frame.
/// </para>
/// <para>
/// The inspector is a second, independent renderer composited over the app (or
/// docked beside it); it never participates in the app's box tree. Docking
/// shrinks the app viewport instead of overlapping it, so the page reflows
/// exactly as it would in a smaller window.
/// </para>
/// <para>
/// All mutable interaction state (hover, press, pointer capture, focus, caret,
/// open select and text selection) is owned by this object and is pruned when
/// the referenced node leaves the tree; see <see cref="PruneInteractionNodes"/>.
/// </para>
/// </remarks>
/// <seealso cref="WindowSettings"/>
/// <seealso cref="Donkeh.LayoutLab.LayoutEngine"/>
/// <seealso cref="GLRenderer"/>
public sealed partial class IgniteWindow : IDisposable
{
    // Attribute names are probed for almost every control operation, so they
    // are interned once instead of being allocated per lookup.
    /// <summary>Interned id of the <c>value</c> attribute written by text edits.</summary>
    private static readonly int ValueAttribute = InternTable.Intern("value");
    /// <summary>Interned id of the boolean <c>checked</c> attribute.</summary>
    private static readonly int CheckedAttribute = InternTable.Intern("checked");
    /// <summary>Interned id of the boolean <c>selected</c> option attribute.</summary>
    private static readonly int SelectedAttribute = InternTable.Intern("selected");

    /// <summary>The creation options this instance was built with, copied by value.</summary>
    private readonly WindowSettings _settings;

    /// <summary>Reentrancy guard for min/max size clamping in <see cref="OnResize"/>.</summary>
    private bool _clampingSize;

    /// <summary>Shared audio engine and per-element players, created on demand.</summary>
    private AudioHost? _audioHost;

    /// <summary>Previous playing state per audio element, used to raise play/pause events.</summary>
    private readonly Dictionary<ElementNode, bool> _audioWasPlaying = [];

    /// <summary>Scratch set of audio elements present in the current layout.</summary>
    private readonly HashSet<ElementNode> _audioLive = [];

    /// <summary>Shared LibVLC host and per-element video players, created on demand.</summary>
    private VideoHost? _videoHost;

    /// <summary>Previous playing state per video element, used to raise play/pause events.</summary>
    private readonly Dictionary<ElementNode, bool> _videoWasPlaying = [];

    /// <summary>Scratch set of video elements present in the current layout.</summary>
    private readonly HashSet<ElementNode> _videoLive = [];

    /// <summary>Author stylesheets in cascade order; the last one wins on ties.</summary>
    private readonly List<string> _stylesheets = [];

    /// <summary>
    /// Process-wide font cache; initialized with the OpenGL context in
    /// <see cref="OnLoad"/>.
    /// </summary>
    private readonly FontManager _fonts = new();

    /// <summary>Decoded image cache backing <c>img</c> replaced content.</summary>
    private readonly ImageCache _images = new();

    /// <summary>UILab renderer for the application tree. Flushed once per frame.</summary>
    private readonly Renderer _ui = new();

    /// <summary>Measures text runs for layout, caret placement and hit testing.</summary>
    private readonly TrueTypeTextMeasurer _measurer;

    /// <summary>
    /// The Silk.NET window; <see langword="null"/> until <see cref="Run(Donkeh.UILab.Node, string)"/> creates
    /// it.
    /// </summary>
    private IWindow? _window;

    /// <summary>
    /// The Silk.NET input context; created in <see cref="OnLoad"/> and released in
    /// <see cref="OnClosing"/>.
    /// </summary>
    private IInputContext? _input;

    /// <summary>The OpenGL context, valid only while the frame loop is running.</summary>
    private GL? _gl;

    /// <summary>The display-list renderer; disposed with the GPU resources.</summary>
    private GLRenderer? _renderer;

    /// <summary>
    /// Reusable layout engine for the app tree. Replaced by
    /// <see cref="RebuildStyles"/>.
    /// </summary>
    private LayoutEngine? _layoutEngine;

    /// <summary>The compiled HTML defaults plus author and inspector sheets.</summary>
    private DataTable? _table;

    /// <summary>The app's most recent layout result; drives hit testing and painting.</summary>
    private LayoutResult? _layoutResult;

    /// <summary>The live app root as returned by <see cref="Renderer.Mount"/>.</summary>
    private Node? _root;

    /// <summary>The caller's root node, i.e. the tree without any inspector wrapper.</summary>
    private Node? _appRoot;

    /// <summary>Root length context for the app tree (font-relative and viewport units).</summary>
    private readonly RendererContext _context = new();

    /// <summary>Root length context for the inspector tree, kept separate from the app's.</summary>
    private readonly RendererContext _devToolsContext = new();

    /// <summary>Live inspector model shared with <see cref="InspectorPanel"/>.</summary>
    private readonly DevToolsState _devTools = new();

    /// <summary>
    /// The standalone inspector window host, created on demand for
    /// <see cref="DevToolsDock.Window"/>.
    /// </summary>
    private DevToolsWindowHost? _devToolsWindow;

    /// <summary>The mounted inspector panel component, if devtools are enabled.</summary>
    private InspectorPanel? _devToolsPanel;

    /// <summary>
    /// UILab renderer for the inspector tree; <see langword="null"/> when devtools
    /// are disabled.
    /// </summary>
    private Renderer? _devToolsUi;

    /// <summary>The inspector tree's live root, separate from the app root.</summary>
    private Node? _devToolsRoot;

    /// <summary>
    /// Layout engine for the inspector tree; <see langword="null"/> when devtools
    /// are disabled.
    /// </summary>
    private LayoutEngine? _devToolsLayoutEngine;

    /// <summary>The inspector's most recent layout result.</summary>
    private LayoutResult? _devToolsLayout;

    /// <summary>
    /// UILab version the inspector was last laid out against; <c>-1</c> forces a
    /// rebuild.
    /// </summary>
    private int _lastDevToolsVersion = -1;

    /// <summary>
    /// Window width the inspector was last laid out at; <c>-1</c> forces a rebuild.
    /// </summary>
    private int _lastDevToolsWidth = -1;

    /// <summary>
    /// Window height the inspector was last laid out at; <c>-1</c> forces a rebuild.
    /// </summary>
    private int _lastDevToolsHeight = -1;

    /// <summary>
    /// Dock position the inspector was last laid out for; <c>-1</c> forces a rebuild.
    /// </summary>
    private int _lastDevToolsDock = -1;

    /// <summary>
    /// Whether the inspector was visible at the last layout; drives the show/hide
    /// relayout.
    /// </summary>
    private bool _lastDevToolsVisible;

    /// <summary>
    /// DPI scale the inspector was last laid out at; compared with a small epsilon.
    /// </summary>
    private float _lastDevToolsDpi;

    /// <summary>Seconds accumulated since the inspector's last model refresh.</summary>
    private double _lastDevToolsRefresh;

    /// <summary>
    /// Seconds accumulated since the performance tab last sampled counters
    /// (0.25 s cadence).
    /// </summary>
    private double _performanceSampleTimer;

    /// <summary>Timestamp the window was constructed at; the base for all animation time.</summary>
    private readonly long _animationStartTicks = Stopwatch.GetTimestamp();

    /// <summary>Monotonic animation clock in seconds, sampled once per frame.</summary>
    private double _animationTimeSeconds;

    /// <summary>Animation clock value at the last app layout driven by an animation.</summary>
    private double _lastAnimationLayoutTime;

    /// <summary>
    /// Animation clock value at the last inspector layout driven by an animation.
    /// </summary>
    private double _lastDevToolsAnimationLayoutTime;

    /// <summary>
    /// Minimum spacing between animation-driven layouts. Animations still
    /// present at the display refresh rate, but the box tree is only rebuilt at
    /// 60 Hz, which bounds CPU cost on high-refresh displays.
    /// </summary>
    private const double AnimationLayoutInterval = 1.0 / 60.0;

    /// <summary>
    /// Set when the box tree must be rebuilt. Cleared by a full
    /// <see cref="Donkeh.LayoutLab.LayoutEngine.Layout(IgniteSupportedDomNode, LayoutOptions)"/>
    /// pass, which also clears
    /// the two lighter paint flags.
    /// </summary>
    private bool _needsLayout = true;

    /// <summary>
    /// Set when only the display list must be rebuilt (scroll offset change).
    /// Cleared by <see cref="Donkeh.LayoutLab.LayoutEngine.Repaint"/> and by any
    /// full layout.
    /// </summary>
    private bool _needsPaint;

    /// <summary>
    /// Set when transform/opacity animations need new computed styles but the
    /// geometry is unchanged. Cleared by
    /// <see cref="Donkeh.LayoutLab.LayoutEngine.RepaintAnimated"/>.
    /// </summary>
    private bool _needsAnimatedPaint;

    /// <summary>
    /// Set when the inspector's display list must be rebuilt; cleared by its
    /// repaint.
    /// </summary>
    private bool _needsDevToolsPaint;

    /// <summary>Scratch display list for inspector overlays (caret); rebuilt every frame.</summary>
    private readonly InstructionBuffer _devToolsOverlay = new(16);

    /// <summary>Constants referenced by <see cref="_devToolsOverlay"/>.</summary>
    private readonly ConstantPool _devToolsOverlayConstants = new();

    /// <summary>Reusable display list for app overlays (select menu, caret, hover box).</summary>
    private readonly InstructionBuffer _overlay = new(64);

    /// <summary>Constants referenced by <see cref="_overlay"/>.</summary>
    private readonly ConstantPool _overlayConstants = new();

    /// <summary>Layout options reused by the app tree; mutated in place each layout.</summary>
    private readonly LayoutOptions _appOptions = new();

    /// <summary>Layout options reused by the inspector tree; never shared with the app.</summary>
    private readonly LayoutOptions _devToolsOptions = new();

    /// <summary>Supplies display text for form controls and replaced elements.</summary>
    private readonly Func<IgniteSupportedDomNode, string?> _textProvider;

    /// <summary>Overrides the text colour for placeholder runs.</summary>
    private readonly Func<IgniteSupportedDomNode, string, uint?> _textColorProvider;

    /// <summary>Supplies intrinsic sizes for images and embedded content.</summary>
    private readonly Func<IgniteSupportedDomNode, Size?> _intrinsicSizeProvider;

    /// <summary>Supplies the image to draw for a resolved box.</summary>
    private readonly Func<LayoutBox, ImageSpec?> _imageProvider;

    /// <summary>
    /// Custom painter that draws app controls after the box background and border.
    /// </summary>
    private readonly Action<LayoutBox, InstructionBuffer, ConstantPool> _appPainter;

    /// <summary>Custom painter for inspector chrome (colour wheel, swatches).</summary>
    private readonly Action<LayoutBox, InstructionBuffer, ConstantPool> _devToolsPainter;

    /// <summary>True once <see cref="Dispose"/> has run; makes disposal idempotent.</summary>
    private bool _disposed;

    /// <summary>True once GL objects were released; guards against double GPU teardown.</summary>
    private bool _gpuDisposed;

    /// <summary>
    /// UILab version the app was last laid out against; <c>-1</c> forces a rebuild.
    /// </summary>
    private int _lastUiVersion = -1;

    /// <summary>UILab version the inspector model was last refreshed against.</summary>
    private int _lastInspectorAppVersion = -1;

    /// <summary>Window width of the last layout, in CSS pixels.</summary>
    private int _lastWidth;

    /// <summary>Window height of the last layout, in CSS pixels.</summary>
    private int _lastHeight;

    /// <summary>
    /// App viewport width of the last layout, after docked inspector space is
    /// removed.
    /// </summary>
    private int _lastAppWidth;

    /// <summary>
    /// App viewport height of the last layout, after docked inspector space is
    /// removed.
    /// </summary>
    private int _lastAppHeight;

    /// <summary>DPI scale of the last layout; changes above 0.001 force a relayout.</summary>
    private float _lastDpi;

    /// <summary>Box count of the last layout, reported to the performance tab.</summary>
    private int _cachedLayoutBoxCount;

    /// <summary>DOM node count of the last layout, reported to the performance tab.</summary>
    private int _cachedNodeCount;

    /// <summary>
    /// The shared inspector state when this window hosts a standalone panel.
    /// </summary>
    /// <value>
    /// A state object owned by another window's inspector; <see langword="null"/>
    /// when the inspector is docked or disabled.
    /// </value>
    /// <remarks>
    /// The frame loop honours <see cref="DevToolsState.ScrollSelectedIntoView"/>
    /// from this state as well as its own, which lets the app window scroll a
    /// selection made in a separate inspector window.
    /// </remarks>
    public DevToolsState? SharedDevToolsState { get; set; }

    /// <summary>
    /// Target path for the next completed frame. When set, the frame is saved and
    /// the window closes; see <see cref="RequestScreenshot"/>.
    /// </summary>
    private string? _screenshotPath;

    /// <summary>Scratch list of <c>position: fixed</c> boxes, rebuilt per hit test.</summary>
    private readonly List<LayoutBox> _hitFixedBoxes = [];

    /// <summary>
    /// The hovered element and all of its ancestors, used to clear <c>:hover</c>.
    /// </summary>
    private readonly List<ElementNode> _hoverChain = [];

    /// <summary>
    /// The pressed element and all of its ancestors, used to clear <c>:active</c>.
    /// </summary>
    private readonly List<ElementNode> _activeChain = [];

    /// <summary>The element under the pointer, or <see langword="null"/>.</summary>
    private ElementNode? _hovered;

    /// <summary>The element the last press landed on; drives the click on release.</summary>
    private ElementNode? _pressed;

    /// <summary>Pointer position where the current press started.</summary>
    private float _pressX;

    /// <summary>Pointer position where the current press started.</summary>
    private float _pressY;

    /// <summary>
    /// The element that captured the pointer (a <c>pointerdown</c> handler target).
    /// </summary>
    private ElementNode? _pointerTarget;

    /// <summary>True while an inspect-mode press is pending, awaiting its release.</summary>
    private bool _inspectPressed;

    /// <summary>The last cursor requested, cached to avoid redundant native cursor sets.</summary>
    private StandardCursor _cursorKind = StandardCursor.Arrow;

    /// <summary>The focused element; text input, caret and key handling target it.</summary>
    private ElementNode? _focused;

    /// <summary>Working copy of the focused control's text, committed back on blur.</summary>
    private string? _editText;

    /// <summary>Caret offset into <see cref="_editText"/>, in UTF-16 code units.</summary>
    private int _caretIndex;

    /// <summary>Timestamp the caret blink cycle was last restarted.</summary>
    private long _caretBlinkStart;

    /// <summary>Committed option index per <c>select</c> element, overriding markup.</summary>
    private readonly Dictionary<ElementNode, int> _selectIndices = [];

    /// <summary>Scratch list of select entries whose element left the tree.</summary>
    private readonly List<ElementNode> _staleSelects = [];

    /// <summary>The <c>select</c> whose dropdown is open, or <see langword="null"/>.</summary>
    private ElementNode? _openSelect;

    /// <summary>Highlighted row in the open dropdown; <c>-1</c> when no row is hovered.</summary>
    private int _openSelectHover = -1;

    /// <summary>
    /// Root length context of the most recent app layout, used to resolve lengths.
    /// </summary>
    private LengthContext _lengthContext;

    /// <summary>Last known pointer X in window CSS pixels.</summary>
    private float _mouseX;

    /// <summary>Last known pointer Y in window CSS pixels.</summary>
    private float _mouseY;

    /// <summary>Smooth-scroll animations keyed by the scrolling node.</summary>
    private readonly Dictionary<IgniteSupportedDomNode, ScrollAnimation> _scrollAnimations = [];

    /// <summary>Scratch list of scroll animations whose node left the tree.</summary>
    private readonly List<IgniteSupportedDomNode> _staleScrollAnimations = [];

    /// <summary>Active thumb drag on a native scrollbar, if any.</summary>
    private ScrollbarDrag? _scrollDrag;

    /// <summary>Audio or video element whose progress thumb is being dragged, if any.</summary>
    private ElementNode? _mediaSeekElement;

    /// <summary>Whether the native window is being moved by an <c>app-region: drag</c> element.</summary>
    private bool _windowDragging;

    /// <summary>Screen-space pointer position when the window drag started.</summary>
    private Vector2D<int> _windowDragStartScreen;

    /// <summary>Native window position when the window drag started.</summary>
    private Vector2D<int> _windowDragStartPosition;

    /// <summary>
    /// Creates the window controller, its text measurer and the initial CSS
    /// table. The native window is not created until <see cref="Run(Donkeh.UILab.Node, string)"/> is called.
    /// </summary>
    /// <param name="settings">
    /// Creation options. The value is copied, so later changes to the caller's
    /// copy have no effect.
    /// </param>
    /// <remarks>
    /// The constructor compiles the built-in HTML stylesheet, the inspector
    /// stylesheet when <see cref="WindowSettings.EnableDevTools"/> is set, and
    /// wires the inspector's <c>StylesChanged</c> notifications. It allocates no
    /// GPU resources, so it is safe to call before a graphics context exists.
    /// </remarks>
    /// <seealso cref="WindowSettings"/>
    public IgniteWindow(WindowSettings settings)
    {
        _settings = settings;
        _measurer = new TrueTypeTextMeasurer(_fonts);
        _devTools.Dock = settings.DevToolsPosition;
        _devTools.StylesChanged += OnDevToolsStylesChanged;
        _textProvider = GetDisplayText;
        _textColorProvider = GetTextColorOverride;
        _intrinsicSizeProvider = GetIntrinsicSize;
        _imageProvider = GetImage;
        _appPainter = PaintElementContent;
        _devToolsPainter = PaintDevToolsContent;
        RebuildStyles();
    }

    /// <summary>Raised after the OpenGL context and input devices are ready.</summary>
    /// <remarks>
    /// Fires from the frame thread inside the Silk.NET load callback, before the
    /// first frame. <see cref="Gl"/>, <see cref="SilkWindow"/> and
    /// <see cref="Input"/> are already non-null here, so handlers may upload
    /// textures or register extra input callbacks. The handler receives the
    /// window that finished initializing.
    /// </remarks>
    public event Action<IgniteWindow>? Loaded;

    /// <summary>Raised once per frame before pending UI work is flushed.</summary>
    /// <remarks>
    /// This is the earliest hook in the frame and the only one guaranteed to run
    /// before UILab flushes queued state updates. The handler receives the window
    /// about to render and the seconds elapsed since the previous frame. Handlers
    /// run on the frame thread; blocking here stalls the window.
    /// </remarks>
    public event Action<IgniteWindow, double>? BeforeFrame;

    /// <summary>Raised when the native window closes.</summary>
    /// <remarks>
    /// Fires from <see cref="OnClosing"/> while the GL context is still alive but
    /// after input devices and GPU resources have been released. The handler
    /// receives the window being closed. Use it for non-GPU cleanup; do not issue
    /// draw calls.
    /// </remarks>
    public event Action<IgniteWindow>? Closed;

    /// <summary>Gets the underlying Silk.NET window.</summary>
    /// <value>
    /// The native window after <see cref="Run(Donkeh.UILab.Node, string)"/> has created it, otherwise
    /// <see langword="null"/>.
    /// </value>
    public IWindow? SilkWindow => _window;

    /// <summary>Gets the OpenGL API surface bound to the window.</summary>
    /// <value>
    /// The GL context while the frame loop is running, otherwise
    /// <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// Usable from <see cref="Loaded"/> onwards; <see cref="DisposeGpu"/> releases it.
    /// </remarks>
    public GL? Gl => _gl;

    /// <summary>Gets the Silk.NET input context that feeds pointer and keyboard events.</summary>
    /// <value>
    /// The input context between <see cref="Loaded"/> and window close, otherwise
    /// <see langword="null"/>.
    /// </value>
    public IInputContext? Input => _input;

    /// <summary>Gets the UILab renderer that owns the application tree.</summary>
    /// <value>A renderer that is mounted at most once, by <see cref="Mount"/>.</value>
    /// <remarks>
    /// Its <see cref="Renderer.Version"/> is the change signal the frame loop uses
    /// to decide whether the box tree must be rebuilt.
    /// </remarks>
    public Renderer UI => _ui;

    /// <summary>Gets the live application root node returned by UILab.</summary>
    /// <value>
    /// The mounted root, including any wrapper nodes UILab introduced, or
    /// <see langword="null"/> before <see cref="Mount"/>.
    /// </value>
    public Node? Root => _root;

    /// <summary>The application root, without the inspector wrapper.</summary>
    /// <value>
    /// The node passed to <see cref="Mount"/>, or <see langword="null"/> before
    /// the first mount.
    /// </value>
    /// <remarks>
    /// The inspector tree is never reachable through this node; it lives in a
    /// separate UILab renderer and is composited over the app.
    /// </remarks>
    public Node? AppRoot => _appRoot;

    /// <summary>Live inspector state. Read or change it to open, close or move devtools.</summary>
    /// <remarks>
    /// Changes that affect layout, such as <see cref="DevToolsState.Visible"/> or
    /// <see cref="DevToolsState.Dock"/>, are picked up on the next frame; the
    /// convenience methods <see cref="ShowDevTools"/>, <see cref="HideDevTools"/>
    /// and <see cref="ToggleDevTools"/> additionally force the relayout.
    /// </remarks>
    public DevToolsState DevTools => _devTools;

    /// <summary>Gets the layout result painted by the most recent frame.</summary>
    /// <value>
    /// The app's box tree, display list and per-node box map, or
    /// <see langword="null"/> before the first layout.
    /// </value>
    /// <remarks>
    /// Unless the inspector runs in its own window, the boxes in this result are
    /// reused and mutated by the next layout; read it only between frames.
    /// </remarks>
    public LayoutResult? FrameResult => _layoutResult;

    /// <summary>Gets the number of frames presented since the window opened.</summary>
    public int FrameCount { get; private set; }

    /// <summary>
    /// Gets the duration of the last frame, in milliseconds, as reported by the
    /// window loop.
    /// </summary>
    /// <remarks>This is the loop delta, not the CPU time spent producing the frame.</remarks>
    public double LastFrameMilliseconds { get; private set; }

    /// <summary>
    /// Gets the time spent in the last layout, repaint or animated repaint, in
    /// milliseconds.
    /// </summary>
    /// <remarks>Zero on frames that reused the previous geometry entirely.</remarks>
    public double LastLayoutMilliseconds { get; private set; }

    /// <summary>
    /// Gets the time spent submitting the last frame to the renderer, in
    /// milliseconds.
    /// </summary>
    public double LastRenderMilliseconds { get; private set; }

    /// <summary>
    /// Gets the number of display-list instructions produced by the last layout.
    /// </summary>
    /// <value>
    /// The app instructions plus the inspector instructions, measured in
    /// <c>Instruction</c> entries.
    /// </value>
    public int LastInstructionCount =>
        (_layoutResult?.Opcodes.Length ?? 0) + (_devToolsLayout?.Opcodes.Length ?? 0);

    /// <summary>Milliseconds spent flushing pending UI updates on the last frame.</summary>
    /// <remarks>Measures <see cref="Renderer.Flush"/> for both the app and the inspector.</remarks>
    public double LastUpdateMilliseconds { get; private set; }

    /// <summary>Shows or hides the built-in inspector.</summary>
    /// <remarks>
    /// <para>
    /// Does nothing when <see cref="WindowSettings.EnableDevTools"/> is
    /// <see langword="false"/>. Hiding also leaves inspect mode, clears the
    /// inspected hover target and resets the pointer to an arrow so a hidden
    /// inspector cannot keep intercepting picks.
    /// </para>
    /// <para>
    /// The inspector model is refreshed immediately and the tree is marked for
    /// layout, so the first frame after the call is already consistent.
    /// </para>
    /// </remarks>
    /// <seealso cref="ShowDevTools"/>
    /// <seealso cref="HideDevTools"/>
    public void ToggleDevTools()
    {
        if (!_settings.EnableDevTools)
        {
            return;
        }

        _devTools.Visible = !_devTools.Visible;

        if (!_devTools.Visible)
        {
            _devTools.InspectMode = false;
            _devTools.Hovered = null;
            _devTools.ColorPickerRuleIndex = -1;
            SetCursor(StandardCursor.Arrow);
        }

        _devTools.Refresh();
        _devToolsPanel?.Tick();
        _needsLayout = true;
    }

    /// <summary>Shows the built-in inspector.</summary>
    /// <remarks>
    /// No-op when devtools are disabled by <see cref="WindowSettings.EnableDevTools"/>.
    /// Showing is idempotent; it refreshes the inspector model and schedules a
    /// layout so docked panels take their space immediately.
    /// </remarks>
    public void ShowDevTools()
    {
        if (!_settings.EnableDevTools)
        {
            return;
        }

        _devTools.Visible = true;
        _devTools.Refresh();
        _devToolsPanel?.Tick();
        _needsLayout = true;
    }

    /// <summary>Hides the built-in inspector.</summary>
    /// <remarks>
    /// Always safe to call, even when devtools are disabled. Hiding clears
    /// inspect mode, the hovered element, the colour-picker target and the
    /// pointer cursor, and restores the full app viewport.
    /// </remarks>
    public void HideDevTools()
    {
        _devTools.Visible = false;
        _devTools.InspectMode = false;
        _devTools.Hovered = null;
        _devTools.ColorPickerRuleIndex = -1;
        SetCursor(StandardCursor.Arrow);
        _devTools.Refresh();
        _devToolsPanel?.Tick();
        _needsLayout = true;
    }

    /// <summary>The mount point of the live tree. Call <see cref="Mount"/> first.</summary>
    /// <param name="root">The UILab tree to mount; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="root"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A tree is already mounted. A window owns exactly one tree; create another
    /// instance for a second document.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Mounts the app tree into <see cref="UI"/> and, when devtools are enabled,
    /// builds the inspector tree in a second renderer. The inspector is
    /// composited separately and never wraps or alters the app's nodes.
    /// </para>
    /// <para>
    /// The method only records and mounts; no layout runs until the next frame.
    /// It must be called on the thread that owns the window.
    /// </para>
    /// </remarks>
    /// <seealso cref="Run(Node, string)"/>
    public void Mount(Node root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (_root is not null)
        {
            throw new InvalidOperationException("A tree is already mounted.");
        }

        _appRoot = root;
        _devTools.AppRoot = root;

        // The application root is mounted exactly as it would be without
        // devtools. The inspector lives in its own renderer and is composited
        // as a fixed overlay, so it never changes the app's box tree.
        _root = _ui.Mount(root);

        if (_settings.EnableDevTools)
        {
            var devToolsUi = new Renderer();
            var panel = _.C<InspectorPanel, InspectorPanelProps>(
                new InspectorPanelProps { State = _devTools });

            _devToolsRoot = devToolsUi.Mount(
                _.H("div", new Props { ClassName = "dt-host" }, panel));
            _devToolsUi = devToolsUi;
            _devToolsPanel = devToolsUi.FindComponent<InspectorPanel>();
        }

        _needsLayout = true;
    }

    /// <summary>Replaces all author stylesheets. The built-in HTML sheet always applies first.</summary>
    /// <param name="css">
    /// A single CSS document, or <see langword="null"/> or whitespace to keep only
    /// the user-agent sheet.
    /// </param>
    /// <remarks>
    /// <para>
    /// The previous author sheets are dropped and the whole
    /// <see cref="DataTable"/> is rebuilt: user-agent rules first, then this
    /// sheet, then the inspector sheet when devtools are enabled. Existing style
    /// caches and layout engines are discarded, so the next frame performs a
    /// full restyle and relayout.
    /// </para>
    /// <para>
    /// Call this before or after <see cref="Mount"/>; a mounted tree is
    /// re-matched against the new table without being remounted.
    /// </para>
    /// </remarks>
    public void SetStylesheet(string? css)
    {
        _stylesheets.Clear();

        if (!string.IsNullOrWhiteSpace(css))
        {
            _stylesheets.Add(css);
        }

        RebuildStyles();
    }

    /// <summary>Appends an author stylesheet after the existing sheets.</summary>
    /// <param name="css">The CSS document to append; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="css"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Appended sheets cascade after earlier author sheets and therefore win on
    /// equal specificity. The table is rebuilt and the tree relaid out on the
    /// next frame, exactly like <see cref="SetStylesheet"/>.
    /// </remarks>
    public void AddStylesheet(string css)
    {
        ArgumentNullException.ThrowIfNull(css);
        _stylesheets.Add(css);
        RebuildStyles();
    }

    /// <summary>Marks the current tree as needing a new layout pass.</summary>
    /// <remarks>
    /// Use after mutating DOM state or CSS variables outside a UILab render, where
    /// no <see cref="Renderer.Version"/> bump signal exists. This is a full box
    /// rebuild, not just a repaint: prefer scroll offsets or animation state when
    /// geometry is unchanged. Call on the frame thread.
    /// </remarks>
    public void Invalidate() => _needsLayout = true;

    /// <summary>
    /// Saves the next finished frame to a 24-bit BMP and closes the window. This
    /// is useful for headless smoke tests and visual regression fixtures.
    /// </summary>
    /// <param name="path">Destination file path; missing directories are created.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is <see langword="null"/>, empty or whitespace.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Capture happens after the frame has been rendered and before the next one
    /// starts: the framebuffer is read back bottom-up, converted from RGB to BGR
    /// and written with 4-byte aligned rows. The window then closes, so
    /// <see cref="Run(Donkeh.UILab.Node, string)"/> returns and the instance is disposed.
    /// </para>
    /// <para>
    /// The file is 24-bit uncompressed BMP; alpha and multisample resolve are
    /// whatever OpenGL produced for the default framebuffer.
    /// </para>
    /// </remarks>
    public void RequestScreenshot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _screenshotPath = path;
    }

    /// <summary>Creates the native window and enters the frame loop.</summary>
    /// <exception cref="InvalidOperationException">
    /// No tree has been mounted through <see cref="Mount"/>.
    /// </exception>
    /// <remarks>
    /// Equivalent to <see cref="Run(Node, string)"/> with no tree and no
    /// stylesheet, for hosts that mount and configure the window themselves.
    /// </remarks>
    public void Run() => Run(null, null);

    /// <summary>Mounts a tree, optionally sets a stylesheet and enters the frame loop.</summary>
    /// <param name="root">
    /// The tree to mount when <see cref="Mount"/> has not been called yet;
    /// <see langword="null"/> keeps the existing tree.
    /// </param>
    /// <param name="stylesheet">
    /// An author stylesheet to install. When <see langword="null"/>, the sheets
    /// declared through <c>[Stylesheet(...)]</c> are loaded and merged instead.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// No tree is mounted after the optional <paramref name="root"/> has been
    /// applied.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Window options are derived from <see cref="WindowSettings"/>: logical size,
    /// title, vsync and frame cap, MSAA samples, border and fullscreen state, an
    /// OpenGL 3.3 forward-compatible core context, no depth buffer and an 8-bit
    /// stencil buffer.
    /// </para>
    /// <para>
    /// The call blocks on the Silk.NET loop. When the loop exits, the native
    /// window cannot be torn down from inside its closing callback without
    /// resetting the active GL context, so resources are released in
    /// <see cref="OnClosing"/> and the window itself is disposed here before the
    /// method returns.
    /// </para>
    /// </remarks>
    /// <seealso cref="RequestScreenshot"/>
    public void Run(Node? root, string? stylesheet = null)
    {
        // When no stylesheet is supplied explicitly, pick up every stylesheet
        // declared through [Stylesheet(...)] and merge them into one sheet, so
        // a developer only has to annotate the source files.
        if (stylesheet is null)
        {
            stylesheet = Donkeh.UILab.Stylesheet.Load();
        }

        if (!string.IsNullOrWhiteSpace(stylesheet))
        {
            SetStylesheet(stylesheet);
        }

        if (root is not null)
        {
            Mount(root);
        }

        if (_root is null)
        {
            throw new InvalidOperationException("Mount a tree before calling Run().");
        }

        var width = _settings.Width > 0 ? _settings.Width : 1280;
        var height = _settings.Height > 0 ? _settings.Height : 800;

        // Honour the configured minimum/maximum size, if any.
        if (_settings.MinWidth > 0) width = Math.Max(width, _settings.MinWidth);
        if (_settings.MinHeight > 0) height = Math.Max(height, _settings.MinHeight);
        if (_settings.MaxWidth > 0) width = Math.Min(width, _settings.MaxWidth);
        if (_settings.MaxHeight > 0) height = Math.Min(height, _settings.MaxHeight);

        var windowState = WindowState.Normal;
        if (_settings.FullScreen) windowState = WindowState.Fullscreen;
        else if (_settings.Maximized) windowState = WindowState.Maximized;

        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(width, height),
            Title = string.IsNullOrWhiteSpace(_settings.Title) ? "Ignite" : _settings.Title,
            VSync = _settings.VSync,
            FramesPerSecond = _settings.TargetFramesPerSecond > 0
                ? _settings.TargetFramesPerSecond
                : 0,
            Samples = _settings.Samples > 0 ? _settings.Samples : null,
            WindowBorder = _settings.Frameless
                ? WindowBorder.Hidden
                : _settings.Resizable ? WindowBorder.Resizable : WindowBorder.Fixed,
            WindowState = windowState,
            TopMost = _settings.TopMost,
            TransparentFramebuffer = _settings.Transparent,
            API = new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                ContextFlags.ForwardCompatible,
                new APIVersion(3, 3)),
            PreferredDepthBufferBits = 24,
            PreferredStencilBufferBits = 8
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Resize += OnResize;
        _window.Closing += OnClosing;
        ApplyWindowIcon();
        _window.Run();

        // Silk owns the native window until the loop exits; tearing it down from
        // inside Closing resets an active context. Dispose after Run returns.
        Dispose();
    }

    /// <summary>
    /// Creates the GL API, font atlas, image cache, renderer and input context,
    /// then raises <see cref="Loaded"/>.
    /// </summary>
    /// <remarks>
    /// Runs once from the Silk.NET load callback, on the frame thread, with the
    /// OpenGL context current. The forced relayout makes the first frame paint a
    /// full tree rather than a cleared window.
    /// </remarks>
    private void OnLoad()
    {
        _gl = GL.GetApi(_window);
        _fonts.Initialize(_gl);
        _images.Initialize(_gl);
        _renderer = new GLRenderer(_gl, _fonts, _images);
        _input = _window!.CreateInput();
        ConfigureInput();
        Loaded?.Invoke(this);
        _needsLayout = true;
    }

    /// <summary>
    /// Handles a native resize by forcing a full relayout on the next frame.
    /// </summary>
    /// <param name="size">
    /// The new logical client size in CSS pixels; it is not read here because the
    /// frame loop samples the native window <c>Size</c> and the framebuffer size
    /// itself, which also covers DPI changes and multi-monitor moves.
    /// </param>
    /// <remarks>
    /// The event is not debounced: a live resize marks the tree once per resize
    /// event, but the flags collapse into a single layout per rendered frame.
    /// </remarks>
    private void OnResize(Vector2D<int> size)
    {
        // Enforce configured minimum/maximum client size by nudging the native
        // window back into range. The guard prevents the synthetic resize that
        // follows from re-entering this handler.
        if (!_clampingSize && _window is not null)
        {
            var clampedX = size.X;
            var clampedY = size.Y;
            if (_settings.MinWidth > 0) clampedX = Math.Max(clampedX, _settings.MinWidth);
            if (_settings.MinHeight > 0) clampedY = Math.Max(clampedY, _settings.MinHeight);
            if (_settings.MaxWidth > 0) clampedX = Math.Min(clampedX, _settings.MaxWidth);
            if (_settings.MaxHeight > 0) clampedY = Math.Min(clampedY, _settings.MaxHeight);

            if (clampedX != size.X || clampedY != size.Y)
            {
                _clampingSize = true;
                try { _window.Size = new Vector2D<int>(clampedX, clampedY); }
                finally { _clampingSize = false; }
            }
        }

        _needsLayout = true;
    }

    /// <summary>
    /// Decodes <see cref="WindowSettings.Icon"/> and applies it to the native window,
    /// if a path was supplied. Failures are ignored so a bad icon never prevents
    /// the window from opening.
    /// </summary>
    private void ApplyWindowIcon()
    {
        if (string.IsNullOrWhiteSpace(_settings.Icon) || _window is null)
        {
            return;
        }

        try
        {
            var data = File.ReadAllBytes(_settings.Icon!);
            var image = StbImageSharp.ImageResult.FromMemory(data, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            if (image.Width <= 0 || image.Height <= 0 || image.Data is null)
            {
                return;
            }

            var raw = new Silk.NET.Core.RawImage(image.Width, image.Height, new Memory<byte>(image.Data));
            _window.SetWindowIcon(new[] { raw });
        }
        catch
        {
            // Ignore icon loading failures.
        }
    }

    /// <summary>
    /// Creates or updates one audio player per <c>audio</c> element, dispatches
    /// its media events and keeps the controls repainting while it plays.
    /// </summary>
    private void SyncAudioElements()
    {
        if (_layoutResult?.Root is null)
        {
            return;
        }

        _audioLive.Clear();

        foreach (var box in EnumerateLayoutBoxes(_layoutResult.Root))
        {
            if (box.Node is not ElementNode element
                || !string.Equals(element.Tag, "audio", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = GetAttribute(element, "src");

            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            _audioLive.Add(element);

            var options = new AudioElementOptions(
                source,
                element.TryGetAttribute("autoplay", out var ignoredAutoplay),
                element.TryGetAttribute("loop", out var ignoredLoop),
                element.TryGetAttribute("muted", out var ignoredMuted),
                ReadFloatAttribute(element, "volume", 1f),
                ReadFloatAttribute(element, "playbackrate", 1f));

            _audioHost ??= new AudioHost();
            var known = _audioHost.GetPlayer(element) is not null;
            var player = _audioHost.Ensure(element, options);

            if (player is null)
            {
                continue;
            }

            if (!known && element.AudioReadyHandler is { } ready)
            {
                try { ready.DynamicInvoke(player); }
                catch { }
            }

            var ended = _audioHost.ConsumeEnded(element);

            if (ended)
            {
                Invoke(element, "ended");
                _audioWasPlaying[element] = false;
            }

            var playing = player.IsPlaying;

            if (!ended
                && _audioWasPlaying.TryGetValue(element, out var wasPlaying)
                && wasPlaying != playing)
            {
                Invoke(element, playing ? "play" : "pause");
            }

            _audioWasPlaying[element] = playing;

            if (playing)
            {
                _needsPaint = true;

                if (element.TryGetEventHandler("timeupdate", out var handler)
                    && handler is Action<float> timeUpdate)
                {
                    try { timeUpdate(player.Time); }
                    catch { }
                }
            }
        }

        _audioHost?.Prune(_audioLive);

        if (_mediaSeekElement is not null && !_audioLive.Contains(_mediaSeekElement))
        {
            _mediaSeekElement = null;
        }

        if (_audioWasPlaying.Count > 0)
        {
            List<ElementNode>? stale = null;

            foreach (var element in _audioWasPlaying.Keys)
            {
                if (!_audioLive.Contains(element))
                {
                    (stale ??= []).Add(element);
                }
            }

            if (stale is not null)
            {
                foreach (var element in stale)
                {
                    _audioWasPlaying.Remove(element);
                }
            }
        }
    }

    /// <summary>
    /// Creates or updates one LibVLC player per <c>video</c> element, uploads
    /// decoded frames and dispatches the media events.
    /// </summary>
    private void SyncVideoElements()
    {
        if (_layoutResult?.Root is null)
        {
            return;
        }

        _videoLive.Clear();

        foreach (var box in EnumerateLayoutBoxes(_layoutResult.Root))
        {
            if (box.Node is not ElementNode element
                || !string.Equals(element.Tag, "video", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = GetAttribute(element, "src");

            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            _videoLive.Add(element);

            var options = new VideoElementOptions(
                source,
                element.TryGetAttribute("autoplay", out var ignoredAutoplay),
                element.TryGetAttribute("loop", out var ignoredLoop),
                element.TryGetAttribute("muted", out var ignoredMuted),
                ReadFloatAttribute(element, "volume", 1f),
                ReadFloatAttribute(element, "playbackrate", 1f));

            _videoHost ??= new VideoHost();
            var known = _videoHost.GetPlayer(element) is not null;
            var textureKey = "videoframe:" + element.GetHashCode().ToString(CultureInfo.InvariantCulture);
            var player = _videoHost.Ensure(element, options, textureKey);

            if (player is null)
            {
                continue;
            }

            if (!known && element.VideoReadyHandler is { } ready)
            {
                try { ready.DynamicInvoke(player); }
                catch { }
            }

            var ended = _videoHost.ConsumeEnded(element);

            if (ended)
            {
                Invoke(element, "ended");
                _videoWasPlaying[element] = false;
            }

            var playing = player.IsPlaying;

            if (!ended
                && _videoWasPlaying.TryGetValue(element, out var wasPlaying)
                && wasPlaying != playing)
            {
                Invoke(element, playing ? "play" : "pause");
            }

            _videoWasPlaying[element] = playing;

            if (playing)
            {
                _needsPaint = true;

                if (element.TryGetEventHandler("timeupdate", out var handler)
                    && handler is Action<float> timeUpdate)
                {
                    try { timeUpdate(player.Time); }
                    catch { }
                }
            }
        }

        _videoHost?.UpdateFrames(_images);
        _videoHost?.Prune(_videoLive);

        if (_mediaSeekElement is not null && !_videoLive.Contains(_mediaSeekElement))
        {
            if (!_audioLive.Contains(_mediaSeekElement))
            {
                _mediaSeekElement = null;
            }
        }

        if (_videoWasPlaying.Count > 0)
        {
            List<ElementNode>? stale = null;

            foreach (var element in _videoWasPlaying.Keys)
            {
                if (!_videoLive.Contains(element))
                {
                    (stale ??= []).Add(element);
                }
            }

            if (stale is not null)
            {
                foreach (var element in stale)
                {
                    _videoWasPlaying.Remove(element);
                }
            }
        }
    }

    /// <summary>Reads a float attribute with an invariant fallback.</summary>
    private static float ReadFloatAttribute(ElementNode element, string name, float fallback)
    {
        return element.TryGetAttribute(name, out var value)
            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    /// <summary>
    /// Walks the current layout tree and lets every <c>canvas</c> element render
    /// into its own bounds using a fresh Silk.NET OpenGL context.
    /// </summary>
    private void DrawCanvasElements()
    {
        if (_layoutResult is null || _gl is null || _window is null)
        {
            return;
        }

        var framebuffer = _window.FramebufferSize;
        var size = _window.Size;
        var width = Math.Max(1, size.X);
        var height = Math.Max(1, size.Y);
        var dpi = framebuffer.X / (float)width;
        if (dpi <= 0 || float.IsNaN(dpi))
        {
            dpi = 1;
        }

        foreach (var box in EnumerateLayoutBoxes(_layoutResult.Root))
        {
            if (box.Node is not ElementNode element
                || !string.Equals(element.Tag, "canvas", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var handler = element.CanvasContextHandler as Action<GL>;
            if (handler is null)
            {
                continue;
            }

            var rect = box.BorderBox;
            var px = (int)(rect.X * dpi);
            var py = (int)(framebuffer.Y - (rect.Y + rect.Height) * dpi);
            var pw = (int)(rect.Width * dpi);
            var ph = (int)(rect.Height * dpi);

            if (pw <= 0 || ph <= 0)
            {
                continue;
            }

            _gl.Viewport(px, py, (uint)pw, (uint)ph);
            _gl.Enable(EnableCap.ScissorTest);
            _gl.Scissor(px, py, (uint)pw, (uint)ph);
            _gl.Enable(EnableCap.DepthTest);

            var silk = GL.GetApi(_window);
            try { handler(silk); }
            catch { }

            _gl.Disable(EnableCap.ScissorTest);
            _gl.Disable(EnableCap.DepthTest);
        }

        _gl.Viewport(0, 0, (uint)Math.Max(1, framebuffer.X), (uint)Math.Max(1, framebuffer.Y));
    }

    /// <summary>Depth-first enumeration of every box in a layout tree.</summary>
    private static IEnumerable<LayoutBox> EnumerateLayoutBoxes(LayoutBox? root)
    {
        if (root is null)
        {
            yield break;
        }

        var stack = new Stack<LayoutBox>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var box = stack.Pop();
            yield return box;

            for (var i = box.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(box.Children[i]);
            }
        }
    }

    /// <summary>
    /// Produces one frame: refreshes UI state and scroll animations, runs only the
    /// layout work that the dirty flags and version counters call for, builds the
    /// overlays, submits the display lists and records frame timings.
    /// </summary>
    /// <param name="delta">Seconds elapsed since the previous frame.</param>
    /// <remarks>
    /// <para>The order is deliberate:</para>
    /// <list type="number">
    /// <item><description>
    /// <see cref="BeforeFrame"/> runs first, before any queued UILab update is
    /// flushed, so a handler can still change state for this frame.
    /// </description></item>
    /// <item><description>
    /// Scroll animations advance and the animation clock is sampled.
    /// </description></item>
    /// <item><description>
    /// Both UILab renderers flush, and the elapsed time is published as
    /// <see cref="LastUpdateMilliseconds"/>.
    /// </description></item>
    /// <item><description>
    /// Queued inspector edits are applied on this thread, and devtools
    /// visibility/window hosting is synchronized.
    /// </description></item>
    /// <item><description>
    /// Geometry is rebuilt only when a dirty flag, a UILab version change, a
    /// viewport or app-viewport change, or a DPI change is detected; a repaint or
    /// animated repaint is used instead when possible.
    /// </description></item>
    /// <item><description>
    /// Dead interaction references are pruned, animation work is re-requested at
    /// the 60 Hz layout cadence, overlays are rebuilt and the frame is submitted.
    /// </description></item>
    /// </list>
    /// <para>
    /// The framebuffer size is read separately from the logical size: their ratio
    /// is the DPI scale used for layout and rasterization. Docked inspectors
    /// reduce the app viewport before layout, never after.
    /// </para>
    /// <para>
    /// A pending screenshot is written after presentation and then closes the
    /// window, which ends the loop.
    /// </para>
    /// </remarks>
    private void OnRender(double delta)
    {
        if (_renderer is null || _gl is null || _window is null || _root is null)
        {
            return;
        }

        BeforeFrame?.Invoke(this, delta);

        if (_renderer is null || _window is null)
        {
            return;
        }

        AdvanceScrollAnimations(delta);
        _animationTimeSeconds = Stopwatch.GetElapsedTime(_animationStartTicks).TotalSeconds;

        var flushStart = Stopwatch.GetTimestamp();
        _ui.Flush();
        _devToolsUi?.Flush();
        LastUpdateMilliseconds = Stopwatch.GetElapsedTime(flushStart).TotalMilliseconds;

        UpdateDevTools(delta);
        SyncAudioElements();
        SyncVideoElements();

        var size = _window.Size;
        var framebuffer = _window.FramebufferSize;
        var width = Math.Max(1, size.X);
        var height = Math.Max(1, size.Y);
        var dpi = framebuffer.X / (float)width;

        if (dpi <= 0 || float.IsNaN(dpi))
        {
            dpi = 1;
        }

        // Docked devtools take space from the app viewport instead of covering
        // it, so the page reflows exactly as it would in a smaller window.
        var appWidth = width;
        var appHeight = height;
        var docked = _settings.EnableDevTools && _devTools.Visible && _devTools.Dock != DevToolsDock.Window;
        _devTools.ViewportWidth = width;
        _devTools.ViewportHeight = height;

        if (docked && _devTools.Dock == DevToolsDock.Bottom)
        {
            appHeight = Math.Max(1, height - _devTools.DockBottomHeight);
        }
        else if (docked && _devTools.Dock == DevToolsDock.Right)
        {
            appWidth = Math.Max(1, width - _devTools.DockRightWidth);
        }

        if (_needsLayout
            || _lastUiVersion != _ui.Version
            || _lastWidth != width
            || _lastHeight != height
            || _lastAppWidth != appWidth
            || _lastAppHeight != appHeight
            || MathF.Abs(_lastDpi - dpi) > 0.001f)
        {
            var start = Stopwatch.GetTimestamp();
            var options = CreateLayoutOptions(appWidth, appHeight, dpi, _context, isApp: true);
            _layoutResult = _layoutEngine!.Layout(_root, options);
            LastLayoutMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _lastUiVersion = _ui.Version;
            _lastWidth = width;
            _lastHeight = height;
            _lastAppWidth = appWidth;
            _lastAppHeight = appHeight;
            _lastDpi = dpi;
            _needsLayout = false;
            _needsPaint = false;
            _needsAnimatedPaint = false;
            _needsDevToolsPaint = false;
            _cachedLayoutBoxCount = CountLayoutBoxes(_layoutResult.Root);
            _cachedNodeCount = CountNodes(_appRoot);
        }
        else if (_needsAnimatedPaint && _layoutResult is not null)
        {
            // Transform/opacity animations only change paint state. Refresh
            // their styles and the display list without rebuilding geometry.
            var start = Stopwatch.GetTimestamp();
            var options = CreateLayoutOptions(
                _lastAppWidth,
                _lastAppHeight,
                _lastDpi,
                _context,
                isApp: true);
            _layoutResult = _layoutEngine!.RepaintAnimated(_layoutResult, options);
            LastLayoutMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _needsAnimatedPaint = false;
            _needsPaint = false;
        }
        else if (_needsPaint && _layoutResult is not null)
        {
            // Scroll offsets never affect geometry, so a scroll only rebuilds
            // the display list rather than the whole box tree.
            var start = Stopwatch.GetTimestamp();
            var options = CreateLayoutOptions(
                _lastAppWidth,
                _lastAppHeight,
                _lastDpi,
                _context,
                isApp: true);
            _layoutResult = _layoutEngine!.Repaint(_layoutResult, options);
            LastLayoutMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _needsPaint = false;
        }

        var devToolsVisible = _settings.EnableDevTools
            && _devTools.Visible
            && _devTools.Dock != DevToolsDock.Window;

        if (devToolsVisible
            && _devToolsRoot is not null
            && _devToolsLayoutEngine is not null
            && (_lastDevToolsVersion != _devToolsUi!.Version
                || _lastDevToolsWidth != width
                || _lastDevToolsHeight != height
                || _lastDevToolsDock != (int)_devTools.Dock
                || _lastDevToolsVisible != devToolsVisible
                || MathF.Abs(_lastDevToolsDpi - dpi) > 0.001f))
        {
            _devToolsLayout = _devToolsLayoutEngine.Layout(
                _devToolsRoot,
                CreateLayoutOptions(width, height, dpi, _devToolsContext, isApp: false));
            _lastDevToolsVersion = _devToolsUi.Version;
            _lastDevToolsWidth = width;
            _lastDevToolsHeight = height;
            _lastDevToolsDock = (int)_devTools.Dock;
            _lastDevToolsVisible = devToolsVisible;
            _lastDevToolsDpi = dpi;
            _needsDevToolsPaint = false;
        }
        else if (!devToolsVisible)
        {
            _devToolsLayout = null;
            _lastDevToolsVisible = false;
            _needsDevToolsPaint = false;
        }
        else if (_needsDevToolsPaint
            && _devToolsLayout is not null
            && _devToolsLayoutEngine is not null)
        {
            // Scroll offsets are paint-time state. A wheel over the inspector
            // updates the node offset, so its display list has to be rebuilt
            // just like the app's.
            _devToolsLayout = _devToolsLayoutEngine.Repaint(
                _devToolsLayout,
                CreateLayoutOptions(width, height, dpi, _devToolsContext, isApp: false));
            _needsDevToolsPaint = false;
        }

        PruneInteractionNodes();

        // Animated styles change every frame, so keep the box tree and display
        // list fresh while any CSS animation is running.
        // CSS animations do not need a fresh box tree at the display refresh
        // rate. Re-layout at 60 Hz and let the renderer reuse the display list
        // in between, which keeps 144 Hz presentation while bounding CPU cost.
        if (_layoutResult?.HasAnimations == true)
        {
            if (_animationTimeSeconds - _lastAnimationLayoutTime >= AnimationLayoutInterval)
            {
                _lastAnimationLayoutTime = _animationTimeSeconds;

                if (_layoutResult.HasLayoutAnimations)
                {
                    _needsLayout = true;
                }
                else
                {
                    _needsAnimatedPaint = true;
                }
            }
        }
        else
        {
            _lastAnimationLayoutTime = _animationTimeSeconds;
        }

        if (_devToolsLayout?.HasAnimations == true)
        {
            if (_animationTimeSeconds - _lastDevToolsAnimationLayoutTime >= AnimationLayoutInterval)
            {
                _lastDevToolsAnimationLayoutTime = _animationTimeSeconds;
                _lastDevToolsVersion = -1;
            }
        }
        else
        {
            _lastDevToolsAnimationLayoutTime = _animationTimeSeconds;
        }

        var sharedDevTools = SharedDevToolsState;
        var shouldScrollSelection =
            _devTools.Dock != DevToolsDock.Window && _devTools.ScrollSelectedIntoView
            || sharedDevTools is not null && sharedDevTools.ScrollSelectedIntoView;

        if (shouldScrollSelection)
        {
            ScrollDevToolsSelectionIntoView();
            _devTools.ScrollSelectedIntoView = false;

            if (sharedDevTools is not null)
            {
                sharedDevTools.ScrollSelectedIntoView = false;
            }
        }

        _devToolsOverlay.Clear();

        if (_focused is not null && IsInDevToolsTree(_focused))
        {
            BuildCaretOverlay(_devToolsOverlay);
        }

        var renderStart = Stopwatch.GetTimestamp();
        var clear = _settings.ClearColor != 0 ? _settings.ClearColor : 0xFFFFFFFF;
        _renderer.BeginFrame(
            framebuffer.X,
            framebuffer.Y,
            width,
            height,
            dpi,
            Color(clear));

        if (_layoutResult is not null)
        {
            var hasOverlay = BuildOverlays(out var overlay, out var overlayConstants);

            if (hasOverlay && _devToolsOverlay.Count > 0)
            {
                _renderer.Render(
                    _layoutResult,
                    overlay.Span,
                    overlayConstants,
                    _devToolsLayout,
                    _devToolsOverlay.Span,
                    _devToolsOverlayConstants);
            }
            else if (hasOverlay)
            {
                _renderer.Render(_layoutResult, overlay.Span, overlayConstants, _devToolsLayout);
            }
            else if (_devToolsOverlay.Count > 0)
            {
                _renderer.Render(
                    _layoutResult,
                    ReadOnlySpan<Instruction>.Empty,
                    _devToolsOverlayConstants,
                    _devToolsLayout,
                    _devToolsOverlay.Span,
                    _devToolsOverlayConstants);
            }
            else
            {
                _renderer.Render(_layoutResult, _devToolsLayout);
            }
        }

        // Canvas callbacks own their element's pixels, so run them after the
        // regular display list (including the canvas background) has painted.
        DrawCanvasElements();

        LastRenderMilliseconds = Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds;

        FrameCount++;
        LastFrameMilliseconds = delta * 1000;

        if (_settings.EnableDevTools)
        {
            _devTools.Layout = _layoutResult;
            _devTools.Context = _context;

            // Sampling the process, the heap and the tree walk every frame is
            // far more expensive than the frame itself. A live monitor only
            // needs a handful of updates per second.
            if (_devTools.Visible && _devTools.Tab == DevToolsTab.Performance)
            {
                _performanceSampleTimer += delta;

                if (_performanceSampleTimer >= 0.25)
                {
                    _performanceSampleTimer = 0;
                    _devTools.Performance.Sample(
                        LastFrameMilliseconds,
                        LastLayoutMilliseconds,
                        LastRenderMilliseconds,
                        LastUpdateMilliseconds,
                        FrameCount,
                        LastInstructionCount,
                        _cachedLayoutBoxCount,
                        _cachedNodeCount);
                }
            }
            else
            {
                _performanceSampleTimer = 0;
            }
        }

        if (_screenshotPath is { } path)
        {
            _screenshotPath = null;
            SaveFramebuffer(path, framebuffer.X, framebuffer.Y);
            _window.Close();
        }
    }

    /// <summary>
    /// Applies queued inspector edits on the frame thread, keeps the standalone
    /// inspector window in sync and refreshes the inspector model when it is due.
    /// </summary>
    /// <param name="delta">Seconds elapsed since the previous frame.</param>
    /// <remarks>
    /// <para>
    /// The inspector may live in its own window on another thread, so edits are
    /// queued and drained here, before layout, rather than applied directly.
    /// </para>
    /// <para>
    /// Refreshes are throttled: the Performance tab rebuilds once per second, the
    /// Elements tab only when the app's UILab version changed. Selection changes
    /// refresh the model at their source instead of through this timer.
    /// </para>
    /// </remarks>
    private void UpdateDevTools(double delta)
    {
        if (!_settings.EnableDevTools)
        {
            return;
        }

        // The inspector may live in its own window on another thread, so edits
        // are queued and applied here, on the frame thread, before layout.
        while (_devTools.TryDequeueEdit(out var edit))
        {
            edit();
        }

        if (_devToolsPanel is null)
        {
            return;
        }

        if (_devTools.Visible && _devTools.Dock == DevToolsDock.Window)
        {
            _devToolsWindow ??= new DevToolsWindowHost();
            _devToolsWindow.Open(_devTools);
        }
        else
        {
            _devToolsWindow?.Close();
        }

        if (!_devTools.Visible)
        {
            _lastDevToolsRefresh = 0;
            return;
        }

        _lastDevToolsRefresh += delta;

        // The Performance tab is a live monitor and refreshes on a timer. The
        // Elements tab only needs to rebuild when the mounted app changed or
        // the user selected something, which already bumps the state version.
        var appChanged = _ui.Version != _lastInspectorAppVersion;
        var due = _devTools.Tab == DevToolsTab.Performance
            ? _lastDevToolsRefresh >= 1.0
            : appChanged;

        if (due || _devTools.Version == 0)
        {
            _lastDevToolsRefresh = 0;
            _lastInspectorAppVersion = _ui.Version;
            _devTools.Refresh();
        }

        _devToolsPanel.Tick();

        if (_devTools.Dock != DevToolsDock.Window)
        {
            FocusPendingDevToolsInput();
        }
    }

    /// <summary>
    /// Focuses an inspector control requested by the panel (for example the
    /// property field of a newly opened "Add property" row).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a pending class was present and a matching
    /// element was found and focused; <see langword="false"/> otherwise.
    /// </returns>
    /// <remarks>
    /// The pending class is consumed even when no element matches, so a stale
    /// request cannot fire on a later frame. Called automatically while a docked
    /// inspector is visible.
    /// </remarks>
    public bool FocusPendingDevToolsInput()
    {
        if (_devTools.PendingFocusClass is not { } className || _devToolsRoot is null)
        {
            return false;
        }

        _devTools.PendingFocusClass = null;
        var element = FindByClass(_devToolsRoot, className);

        if (element is null)
        {
            return false;
        }

        Focus(element);
        return true;
    }

    /// <summary>
    /// Finds the first element carrying <paramref name="className"/> in a subtree.
    /// </summary>
    /// <param name="node">Subtree root; may be <see langword="null"/>.</param>
    /// <param name="className">The exact class token to match.</param>
    /// <returns>The first matching element in document order, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Depth-first and recursive; intended for one-off lookups in the inspector tree.
    /// </remarks>
    private static ElementNode? FindByClass(IgniteSupportedDomNode? node, string className)
    {
        if (node is ElementNode element && element.HasClass(className))
        {
            return element;
        }

        var children = node?.Children;

        if (children is null)
        {
            return null;
        }

        for (var i = 0; i < children.Length; i++)
        {
            if (FindByClass(children[i], className) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Invalidates every cached style after a CSS declaration was edited in the
    /// inspector, then forces both trees to restyle and the panel to rebuild.
    /// </summary>
    /// <remarks>
    /// Both engines share one <see cref="DataTable"/>, so both caches must be
    /// dropped; clearing only the app's would leave the inspector showing stale
    /// computed values.
    /// </remarks>
    private void OnDevToolsStylesChanged()
    {
        // A declaration was edited. Drop every cached style so the next layout
        // re-matches the changed rule, and force the inspector to rebuild too.
        _layoutEngine?.ClearCaches();
        _devToolsLayoutEngine?.ClearCaches();
        _needsLayout = true;
        _needsDevToolsPaint = false;
        _lastDevToolsVersion = -1;
    }

    /// <summary>Counts boxes in a laid-out subtree, including the root box itself.</summary>
    /// <param name="box">Subtree root; <see langword="null"/> counts as zero.</param>
    /// <returns>The total number of boxes in the subtree.</returns>
    private static int CountLayoutBoxes(LayoutBox? box)
    {
        if (box is null)
        {
            return 0;
        }

        var count = 1;

        for (var i = 0; i < box.Children.Count; i++)
        {
            count += CountLayoutBoxes(box.Children[i]);
        }

        return count;
    }

    /// <summary>Counts DOM nodes in a subtree, including <paramref name="node"/> itself.</summary>
    /// <param name="node">Subtree root; <see langword="null"/> counts as zero.</param>
    /// <returns>The total number of nodes in the subtree.</returns>
    private static int CountNodes(IgniteSupportedDomNode? node)
    {
        if (node is null)
        {
            return 0;
        }

        var count = 1;
        var children = node.Children;

        for (var i = 0; i < children.Length; i++)
        {
            count += CountNodes(children[i]);
        }

        return count;
    }

    /// <summary>
    /// Reads the default framebuffer back and writes it as an uncompressed
    /// 24-bit BMP.
    /// </summary>
    /// <param name="path">Destination file; missing parent directories are created.</param>
    /// <param name="width">Framebuffer width in physical pixels.</param>
    /// <param name="height">Framebuffer height in physical pixels.</param>
    /// <remarks>
    /// <para>
    /// The read uses <c>PackAlignment = 1</c> so rows are tightly packed, and the
    /// data is written bottom-up as BMP expects. Each row is padded to a 4-byte
    /// boundary and RGB is swapped to BGR.
    /// </para>
    /// <para>
    /// Must run while the GL context is current. Silent no-op for a null context
    /// or non-positive dimensions; file or GL failures propagate to the caller.
    /// </para>
    /// </remarks>
    private unsafe void SaveFramebuffer(string path, int width, int height)
    {
        if (_gl is null || width <= 0 || height <= 0)
        {
            return;
        }

        var pixels = new byte[width * height * 3];

        fixed (byte* data = pixels)
        {
            _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
            _gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgb, PixelType.UnsignedByte, data);
        }

        var rowSize = (width * 3 + 3) & ~3;
        var fileSize = 54 + rowSize * height;
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new BinaryWriter(stream);

        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(rowSize * height);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        var row = new byte[rowSize];

        for (var y = 0; y < height; y++)
        {
            var source = y * width * 3;
            Array.Clear(row);

            for (var x = 0; x < width; x++)
            {
                row[x * 3] = pixels[source + x * 3 + 2];
                row[x * 3 + 1] = pixels[source + x * 3 + 1];
                row[x * 3 + 2] = pixels[source + x * 3];
            }

            writer.Write(row);
        }
    }

    /// <summary>
    /// Populates the cached layout options for one tree and returns them for the
    /// engine.
    /// </summary>
    /// <param name="width">Viewport width in CSS pixels.</param>
    /// <param name="height">Viewport height in CSS pixels.</param>
    /// <param name="dpi">Device pixel ratio applied to the viewport context.</param>
    /// <param name="context">
    /// Root length context to publish the viewport into. The app and inspector
    /// each keep their own so a viewport change in one cannot invalidate the
    /// other's style cache.
    /// </param>
    /// <param name="isApp">
    /// <see langword="true"/> for the application tree, <see langword="false"/>
    /// for the inspector.
    /// </param>
    /// <returns>
    /// The shared options instance for that tree, mutated in place. Do not store
    /// it beyond the layout call.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Box reuse is disabled for the app while the inspector runs in its own
    /// window: that inspector can read the app's <see cref="LayoutResult"/> from
    /// another thread, and in-place box reuse would mutate the tree while it is
    /// being read. In that mode results are snapshotted instead.
    /// </para>
    /// <para>
    /// Text layout caching is enabled for the app only; the inspector renders
    /// live numbers whose strings would otherwise accumulate in the cache.
    /// </para>
    /// <para>
    /// Only the app call assigns <c>_lengthContext</c>, which the painters use to
    /// resolve font and line-height lengths against the current viewport.
    /// </para>
    /// </remarks>
    private LayoutOptions CreateLayoutOptions(
        int width,
        int height,
        float dpi,
        RendererContext context,
        bool isApp)
    {
        context.ViewportWidth = width;
        context.ViewportHeight = height;

        // The standalone inspector reads the app's LayoutResult from another
        // thread. Box reuse mutates that tree in place, so keep a stable
        // snapshot while the inspector owns a separate window.
        var reuseBoxes = !isApp
            || !(_settings.EnableDevTools
                && _devTools.Visible
                && _devTools.Dock == DevToolsDock.Window);

        var options = isApp ? _appOptions : _devToolsOptions;
        options.ViewportWidth = width;
        options.ViewportHeight = height;
        options.RootScrolls = isApp;
        options.DevicePixelRatio = dpi;
        options.Context = context;
        options.TextMeasurer = _measurer;
        options.TextProvider = _textProvider;
        options.TextColorProvider = _textColorProvider;
        options.IntrinsicSizeProvider = _intrinsicSizeProvider;
        options.ImageProvider = _imageProvider;
        options.CustomPainter = isApp ? _appPainter : _devToolsPainter;
        options.Optimise = true;
        options.CullOffscreen = true;
        options.MergeDraws = true;
        options.CompactConstants = false;
        options.EnableCaching = true;
        options.ReuseLayoutBoxes = reuseBoxes;
        options.SnapshotResults = !reuseBoxes;
        // The inspector renders live, constantly changing numbers. Caching
        // those one-off strings would retain thousands of text layouts.
        options.CacheTextLayouts = isApp;
        options.AnimationsEnabled = true;
        options.AnimationTimeSeconds = _animationTimeSeconds;

        // The app and the inspector must not share a RendererContext: each
        // layout writes its viewport into the context, and a changed context
        // version invalidates every cached style. Sharing one context while
        // docked made both trees restyle on every frame.
        if (isApp)
        {
            _lengthContext = options.CreateRootLengthContext();
        }

        return options;
    }

    /// <summary>
    /// Recompiles the cascade from user-agent rules, author sheets and the
    /// inspector sheet, then recreates both layout engines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the constructor and by every stylesheet change. Rule ranges are
    /// recorded per sheet so the inspector can attribute a matched declaration to
    /// its source.
    /// </para>
    /// <para>
    /// Both engines are replaced rather than reused: their style caches, value
    /// caches and box maps are all keyed to the previous table and must not
    /// survive it. The mounted tree is unaffected; the next layout re-matches it.
    /// </para>
    /// </remarks>
    private void RebuildStyles()
    {
        var table = new DataTable();
        var engine = new Donkeh.DataEngine.DataEngine(Donkeh.DataEngine.MediaConditionCompiler.Instance);
        var ranges = new List<StyleSheetRange>();

        var start = table.RuleCount;
        engine.Build(Tokenizer.Parse(HtmlDefaults.Css), table);
        ranges.Add(new StyleSheetRange("user-agent", start, table.RuleCount));

        for (var i = 0; i < _stylesheets.Count; i++)
        {
            start = table.RuleCount;
            engine.Build(Tokenizer.Parse(_stylesheets[i]), table);
            ranges.Add(new StyleSheetRange($"author #{i + 1}", start, table.RuleCount));
        }

        if (_settings.EnableDevTools)
        {
            start = table.RuleCount;
            engine.Build(Tokenizer.Parse(InspectorStyles.Css + InspectorStyles.HeightClasses), table);
            ranges.Add(new StyleSheetRange("devtools", start, table.RuleCount));
        }

        _table = table;
        _devTools.Table = table;
        _devTools.StyleSheets = ranges;
        _layoutEngine = new LayoutEngine(table);
        _devToolsLayoutEngine = _settings.EnableDevTools ? new LayoutEngine(table) : null;
        _needsLayout = true;
    }

    // ------------------------------------------------------------------ text

    /// <summary>
    /// Resolves the text a node contributes to layout, accounting for form
    /// control values, placeholders and replaced elements.
    /// </summary>
    /// <param name="node">The node being laid out.</param>
    /// <returns>
    /// The text to lay out, or <see langword="null"/> when the node contributes
    /// no text (image with a source, progress/meter/br, non-text inputs).
    /// </returns>
    /// <remarks>
    /// <para>
    /// A focused text control reports its live <c>_editText</c> instead of the
    /// stored value, so typing reflows without committing to the DOM. A focused
    /// but empty control keeps showing its placeholder: browsers do the same, and
    /// it keeps the line box from collapsing on focus.
    /// </para>
    /// <para>
    /// A <c>select</c> reports the label of its currently selected option, using
    /// the committed index when the user has picked one and the markup otherwise.
    /// </para>
    /// </remarks>
    private string? GetDisplayText(IgniteSupportedDomNode node)
    {
        if (node is not ElementNode element || HtmlDefaults.IsHidden(element.Tag))
        {
            return node.TextContent;
        }

        switch (element.Tag)
        {
            case "input":
            {
                var type = GetAttribute(element, "type");
                if (type is "checkbox" or "radio" or "range" or "color" or "file")
                {
                    return null;
                }

                if (ReferenceEquals(element, _focused))
                {
                    // Browsers keep the placeholder visible while the control is
                    // focused but still empty. Besides matching that behaviour,
                    // it keeps the text run (and therefore the line box height)
                    // in place instead of collapsing the input on focus.
                    return string.IsNullOrEmpty(_editText)
                        ? GetAttribute(element, "placeholder") ?? string.Empty
                        : _editText;
                }

                var value = GetAttribute(element, "value");
                return string.IsNullOrEmpty(value) ? GetAttribute(element, "placeholder") : value;
            }

            case "textarea":
                if (ReferenceEquals(element, _focused))
                {
                    return string.IsNullOrEmpty(_editText)
                        ? GetAttribute(element, "placeholder") ?? string.Empty
                        : _editText;
                }

                return element.TextContent ?? GetAttribute(element, "value") ?? GetAttribute(element, "placeholder");

            case "select":
            {
                var options = FindOptions(element);

                if (options.Count == 0)
                {
                    return string.Empty;
                }

                var index = GetSelectedIndex(element, options);
                return options[index].TextContent
                    ?? GetAttribute(options[index], "value")
                    ?? string.Empty;
            }

            case "progress":
            case "meter":
            case "br":
                return null;

            case "img":
                return string.IsNullOrEmpty(GetAttribute(element, "src"))
                    ? GetAttribute(element, "alt")
                    : null;

            default:
                return element.TextContent;
        }
    }

    /// <summary>
    /// Placeholder text colour, in the engine's packed 0xAARRGGBB form, chosen to
    /// match the default HTML sheet's muted foreground.
    /// </summary>
    private const uint PlaceholderTextColor = 0xFF8C959F;

    /// <summary>
    /// Returns an override colour for placeholder text, or <see langword="null"/>
    /// to use the element's computed colour.
    /// </summary>
    /// <param name="node">The node whose text is being painted.</param>
    /// <param name="text">The text run being painted; not inspected.</param>
    /// <returns>
    /// <see cref="PlaceholderTextColor"/> for an empty text control that has a
    /// placeholder, otherwise <see langword="null"/>.
    /// </returns>
    private static uint? GetTextColorOverride(IgniteSupportedDomNode node, string text)
    {
        if (node is not ElementNode element || !IsTextControl(element))
        {
            return null;
        }

        if (string.IsNullOrEmpty(GetAttribute(element, "placeholder")))
        {
            return null;
        }

        var value = GetAttribute(element, "value");

        if (!string.IsNullOrEmpty(value) || !string.IsNullOrEmpty(element.TextContent))
        {
            return null;
        }

        return PlaceholderTextColor;
    }

    /// <summary>
    /// Collects the <c>option</c> descendants of a <c>select</c> in document order.
    /// </summary>
    /// <param name="select">The select element to search.</param>
    /// <returns>A new list, empty when the select has no options.</returns>
    private static List<ElementNode> FindOptions(ElementNode select)
    {
        var results = new List<ElementNode>();
        CollectOptions(select, results);
        return results;
    }

    /// <summary>
    /// Appends <c>option</c> descendants to <paramref name="results"/>, descending
    /// through wrappers such as <c>optgroup</c>.
    /// </summary>
    /// <param name="node">Subtree to walk.</param>
    /// <param name="results">Accumulator; not cleared.</param>
    /// <remarks>Stops descending once an option is found, since options are not nested.</remarks>
    private static void CollectOptions(Node node, List<ElementNode> results)
    {
        if (node is ElementNode element)
        {
            if (string.Equals(element.Tag, "option", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(element);
                return;
            }

            var children = element.Children;

            for (var i = 0; i < children.Length; i++)
            {
                if (children[i] is Node child)
                {
                    CollectOptions(child, results);
                }
            }
        }
    }

    // ---------------------------------------------------------------- select

    /// <summary>
    /// Resolves which option a <c>select</c> currently shows.
    /// </summary>
    /// <param name="select">The select element.</param>
    /// <param name="options">Its options, in document order.</param>
    /// <returns>
    /// A valid index into <paramref name="options"/>; <c>0</c> when nothing is marked.
    /// </returns>
    /// <remarks>
    /// A committed index wins over markup, but is ignored if it fell out of range
    /// (options added or removed since the user picked). The markup fallback is
    /// the first option carrying the <c>selected</c> attribute.
    /// </remarks>
    private int GetSelectedIndex(ElementNode select, List<ElementNode> options)
    {
        if (_selectIndices.TryGetValue(select, out var index) && index >= 0 && index < options.Count)
        {
            return index;
        }

        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].TryGetAttribute(SelectedAttribute, out var ignored))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Opens the dropdown of a <c>select</c> and highlights its current option.
    /// </summary>
    /// <param name="select">The select to open.</param>
    /// <remarks>
    /// No-op for a select without options. Opening does not invalidate layout: the
    /// menu is an overlay rebuilt from <c>_openSelect</c> every frame. Only one
    /// menu can be open at a time, and <see cref="Focus"/> closes it when focus
    /// moves elsewhere.
    /// </remarks>
    private void OpenSelectMenu(ElementNode select)
    {
        var options = FindOptions(select);

        if (options.Count == 0)
        {
            return;
        }

        _openSelect = select;
        _openSelectHover = GetSelectedIndex(select, options);
    }

    /// <summary>Closes any open dropdown and forgets its highlight row.</summary>
    private void CloseSelectMenu()
    {
        _openSelect = null;
        _openSelectHover = -1;
    }

    /// <summary>
    /// Commits an option as the selection of a <c>select</c> and dispatches its
    /// <c>change</c> event.
    /// </summary>
    /// <param name="select">The select being changed.</param>
    /// <param name="index">
    /// Zero-based option index; clamped into the valid range instead of throwing.
    /// </param>
    /// <remarks>
    /// The committed index is stored per element, the <c>selected</c> attribute is
    /// moved onto exactly one option, and a full relayout is requested because the
    /// control's displayed text changed. The <c>change</c> event receives the
    /// option's text, falling back to its <c>value</c> attribute.
    /// </remarks>
    private void SelectOption(ElementNode select, int index)
    {
        var options = FindOptions(select);

        if (options.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, options.Count - 1);
        _selectIndices[select] = index;

        for (var i = 0; i < options.Count; i++)
        {
            if (i == index)
            {
                options[i].SetAttribute(SelectedAttribute, InternTable.Intern(string.Empty));
            }
            else
            {
                options[i].RemoveAttribute(SelectedAttribute);
            }
        }

        _needsLayout = true;
        Invoke(select, "change", options[index].TextContent ?? GetAttribute(options[index], "value") ?? string.Empty);
    }

    /// <summary>Geometry of an open dropdown, in window CSS pixels.</summary>
    /// <param name="Menu">The full menu rectangle, including its padding.</param>
    /// <param name="RowHeight">Height of one option row.</param>
    private readonly record struct SelectMenuLayout(Rect Menu, float RowHeight);

    /// <summary>
    /// Tests whether a window point falls on a row of an open dropdown.
    /// </summary>
    /// <param name="select">The select whose dropdown is open.</param>
    /// <param name="x">Pointer X in window CSS pixels.</param>
    /// <param name="y">Pointer Y in window CSS pixels.</param>
    /// <param name="index">The row index under the point, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when the point is inside a valid option row.</returns>
    /// <remarks>
    /// The row is derived from the same geometry used for painting
    /// (<see cref="ComputeSelectMenu"/>), so hit testing and rendering cannot drift.
    /// </remarks>
    private bool TryHitSelectOption(ElementNode select, float x, float y, out int index)
    {
        index = -1;

        if (_layoutResult is null || !_layoutResult.TryGetBox(select, out var box))
        {
            return false;
        }

        var options = FindOptions(select);

        if (options.Count == 0)
        {
            return false;
        }

        var layout = ComputeSelectMenu(box, options.Count);

        if (!layout.Menu.Contains(x, y))
        {
            return false;
        }

        var row = (int)MathF.Floor((y - layout.Menu.Y - SelectMenuPadding) / layout.RowHeight);

        if (row < 0 || row >= options.Count)
        {
            return false;
        }

        index = row;
        return true;
    }

    /// <summary>Padding in CSS pixels between the dropdown edge and its rows.</summary>
    private const float SelectMenuPadding = 4;

    /// <summary>
    /// Computes where a dropdown opens relative to its control.
    /// </summary>
    /// <param name="box">The select's layout box, in unscrolled layout coordinates.</param>
    /// <param name="optionCount">Number of rows to fit.</param>
    /// <returns>The menu rectangle and row height, already clamped to the window.</returns>
    /// <remarks>
    /// The menu prefers the space below the control, flips above when it does not
    /// fit, and finally clamps inside the viewport. It is at least as wide as the
    /// control and never narrower than 140 CSS px. Row height is the line height
    /// plus 10 px, with a 28 px floor.
    /// </remarks>
    private SelectMenuLayout ComputeSelectMenu(LayoutBox box, int optionCount)
    {
        var selectRect = PaintedBorderBox(box);
        var fontSize = box.Style.ResolveFontSize(in _lengthContext);
        var lineHeight = box.Style.ResolveLineHeight(in _lengthContext);
        var rowHeight = MathF.Max(28, lineHeight + 10);

        var width = MathF.Max(selectRect.Width, 140);
        var height = optionCount * rowHeight + SelectMenuPadding * 2;
        var viewportWidth = MathF.Max(1, _lastWidth);
        var viewportHeight = MathF.Max(1, _lastHeight);

        var x = Math.Clamp(selectRect.X, 0, MathF.Max(0, viewportWidth - width));
        var below = selectRect.Bottom + 2;
        var above = selectRect.Y - height - 2;
        float y;

        if (below + height <= viewportHeight)
        {
            y = below;
        }
        else if (above >= 0)
        {
            y = above;
        }
        else
        {
            y = Math.Clamp(below, 0, MathF.Max(0, viewportHeight - height));
        }

        return new SelectMenuLayout(new Rect(x, y, width, height), rowHeight);
    }

    /// <summary>
    /// Returns a box's border box translated into painted (window) coordinates by
    /// subtracting every scrolling ancestor's offset.
    /// </summary>
    /// <param name="box">The box to project.</param>
    /// <returns>The border box as it appears on screen, in CSS pixels.</returns>
    /// <remarks>
    /// Layout boxes keep unscrolled absolute coordinates; the display list applies
    /// the scroll translation during paint. Anything comparing against pointer
    /// coordinates must apply the same translation, which is why hit testing,
    /// overlays and caret placement all route through this helper.
    /// </remarks>
    private static Rect PaintedBorderBox(LayoutBox box)
    {
        var scrollX = 0f;
        var scrollY = 0f;

        for (var parent = box.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.IsScrollContainer)
            {
                scrollX += parent.ScrollOffsetX;
                scrollY += parent.ScrollOffsetY;
            }
        }

        return box.BorderBox.Translate(-scrollX, -scrollY);
    }

    /// <summary>
    /// Rebuilds the app overlay display list (open dropdown, caret and inspector
    /// hover highlight).
    /// </summary>
    /// <param name="overlay">Receives the overlay instructions.</param>
    /// <param name="constants">Receives the constants those instructions reference.</param>
    /// <returns>
    /// <see langword="true"/> when at least one overlay instruction was produced.
    /// </returns>
    /// <remarks>
    /// Returns early without clearing the buffers when no overlay can exist, which
    /// keeps the common idle frame allocation-free. The order matters: the dropdown
    /// paints first so the caret stays visible above it, and the inspector highlight
    /// paints last so it always sits on top.
    /// </remarks>
    private bool BuildOverlays(out InstructionBuffer overlay, out ConstantPool constants)
    {
        var hasHover = _settings.EnableDevTools
            && _devTools.Visible
            && _devTools.Hovered is not null;

        if (_openSelect is null
            && (_focused is null || IsInDevToolsTree(_focused))
            && !hasHover)
        {
            overlay = null!;
            constants = null!;
            return false;
        }

        overlay = _overlay;
        constants = _overlayConstants;
        overlay.Clear();
        constants.Clear();

        BuildSelectOverlay(overlay, constants);

        if (_focused is not null && !IsInDevToolsTree(_focused))
        {
            BuildCaretOverlay(overlay);
        }

        BuildHoverOverlay(overlay, constants);

        return overlay.Count > 0;
    }

    /// <summary>
    /// Paints the inspect-mode highlight: a translucent fill, a 1 px outline and a
    /// size label for the element hovered in the inspector.
    /// </summary>
    /// <param name="overlay">Overlay display list to append to.</param>
    /// <param name="constants">Constant pool for the label text and corner radius.</param>
    /// <remarks>
    /// The label is positioned above the highlight and flips below when it would
    /// leave the top of the window. Label width is estimated from the character
    /// count rather than measured, so it is deliberately padded.
    /// </remarks>
    private void BuildHoverOverlay(InstructionBuffer overlay, ConstantPool constants)
    {
        if (!_settings.EnableDevTools
            || !_devTools.Visible
            || _devTools.Hovered is not { } hovered
            || _layoutResult is not { } layout
            || !TryFindBox(layout, hovered, out var box))
        {
            return;
        }

        var rect = PaintedBorderBox(box);

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        overlay.AddDrawRect(rect, 0x33007FD4);

        const uint border = 0xFF007FD4;
        overlay.AddDrawRect(new Rect(rect.X, rect.Y, rect.Width, 1), border);
        overlay.AddDrawRect(new Rect(rect.X, rect.Bottom - 1, rect.Width, 1), border);
        overlay.AddDrawRect(new Rect(rect.X, rect.Y, 1, rect.Height), border);
        overlay.AddDrawRect(new Rect(rect.Right - 1, rect.Y, 1, rect.Height), border);

        var label = $"{MathF.Round(rect.Width):0} × {MathF.Round(rect.Height):0}";
        var textStyle = TextStyle.Default with
        {
            FontFamily = "Segoe UI",
            FontSize = 12,
            Color = 0xFF202124,
            WhiteSpace = WhiteSpace.NoWrap
        };
        var textId = constants.AddText(label, textStyle);
        var labelWidth = label.Length * 7f + 10f;
        const float labelHeight = 18f;
        var labelX = MathF.Min(rect.X, MathF.Max(0, _lastWidth - labelWidth - 4));
        var labelY = rect.Y - labelHeight - 2;

        if (labelY < 0)
        {
            labelY = rect.Y + 2;
        }

        var labelRect = new Rect(labelX, labelY, labelWidth, labelHeight);
        var radius = checked((ushort)constants.AddRadius(CornerRadius.All(2)));
        overlay.AddDrawRoundedRect(labelRect, 0xFFFFFFFF, radius);
        overlay.Add(Instruction.DrawText(labelRect.Deflate(5, 5, 2, 2), textId));
    }

    /// <summary>
    /// Finds a laid-out box for a node, falling back to a descendant and then to
    /// the nearest ancestor.
    /// </summary>
    /// <param name="layout">Layout result to search.</param>
    /// <param name="node">The DOM node to locate.</param>
    /// <param name="box">The first usable box, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a box was found.</returns>
    /// <remarks>
    /// The inspector can hover a wrapper, a text node's parent or a
    /// <c>display: contents</c> element that owns no box; outlining the descendant
    /// or ancestor box is more useful than drawing nothing.
    /// </remarks>
    private static bool TryFindBox(
        LayoutResult layout,
        IgniteSupportedDomNode node,
        out LayoutBox box)
    {
        if (layout.TryGetBox(node, out box!))
        {
            return true;
        }

        var children = node.Children;

        for (var i = 0; i < children.Length; i++)
        {
            if (TryFindBox(layout, children[i], out box))
            {
                return true;
            }
        }

        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (layout.TryGetBox(parent, out box))
            {
                return true;
            }
        }

        box = null!;
        return false;
    }

    /// <summary>
    /// Paints the open dropdown: rounded border, translucent background, the hover
    /// row, the selected row and one text run per option.
    /// </summary>
    /// <param name="overlay">Overlay display list to append to.</param>
    /// <param name="constants">Constant pool for borders, radii and text.</param>
    /// <remarks>
    /// The current row is highlighted more strongly than the committed selection,
    /// matching native combo boxes. Text uses the select's computed font and
    /// colour, with the selected row in the accent colour and a fallback to the
    /// default foreground when the colour is <c>currentColor</c>.
    /// </remarks>
    private void BuildSelectOverlay(InstructionBuffer overlay, ConstantPool constants)
    {
        if (_openSelect is null
            || _layoutResult is null
            || !_layoutResult.TryGetBox(_openSelect, out var box))
        {
            return;
        }

        var options = FindOptions(_openSelect);

        if (options.Count == 0)
        {
            return;
        }

        var layout = ComputeSelectMenu(box, options.Count);
        var menu = layout.Menu;
        var innerRadius = constants.AddRadius(CornerRadius.All(7));

        var border = new BorderSpec(
            new BorderSide(1, BorderStyle.Solid, 0xFFCED4DA),
            new BorderSide(1, BorderStyle.Solid, 0xFFCED4DA),
            new BorderSide(1, BorderStyle.Solid, 0xFFCED4DA),
            new BorderSide(1, BorderStyle.Solid, 0xFFCED4DA),
            CornerRadius.All(8));

        overlay.Add(Instruction.DrawBorder(menu, constants.AddBorder(border)));
        overlay.AddDrawRoundedRect(
            menu.Deflate(1, 1, 1, 1),
            0xFFFFFFFF,
            checked((ushort)innerRadius));

        var fontSize = box.Style.ResolveFontSize(in _lengthContext);
        var lineHeight = box.Style.ResolveLineHeight(in _lengthContext);
        var textY = (layout.RowHeight - lineHeight) * 0.5f;
        var textColor = box.Style.Color == ComputedStyle.CurrentColor
            ? 0xFF212529u
            : box.Style.Color;
        var selected = GetSelectedIndex(_openSelect, options);
        var rowRadius = checked((ushort)constants.AddRadius(CornerRadius.All(4)));

        for (var i = 0; i < options.Count; i++)
        {
            var rowY = menu.Y + SelectMenuPadding + i * layout.RowHeight;
            var row = new Rect(
                menu.X + SelectMenuPadding,
                rowY,
                menu.Width - SelectMenuPadding * 2,
                layout.RowHeight);

            if (i == _openSelectHover)
            {
                overlay.AddDrawRoundedRect(row, 0xFFE9ECEF, rowRadius);
            }
            else if (i == selected)
            {
                overlay.AddDrawRoundedRect(row, 0xFFE7F1FF, rowRadius);
            }

            var text = options[i].TextContent ?? GetAttribute(options[i], "value") ?? string.Empty;

            if (text.Length == 0)
            {
                continue;
            }

            var color = i == selected ? 0xFF0D6EFD : textColor;
            var textStyle = box.Style.ToTextStyle(in _lengthContext, fontSize, color);
            var textId = constants.AddText(text, textStyle);
            var textRect = new Rect(
                row.X + 10,
                rowY + textY,
                MathF.Max(0, row.Width - 20),
                lineHeight);

            overlay.Add(Instruction.DrawText(textRect, textId));
        }
    }

    /// <summary>
    /// Appends a caret quad for the focused control to an overlay display list.
    /// </summary>
    /// <param name="overlay">Overlay display list to append to.</param>
    /// <remarks>
    /// Uses the control's computed colour, falling back to the default foreground
    /// when it resolves to <c>currentColor</c>. Produces nothing while the control
    /// owns a text selection, while the blink phase is off, or when the focused
    /// element is not a text control; see <see cref="TryGetCaretRect"/>.
    /// </remarks>
    private void BuildCaretOverlay(InstructionBuffer overlay)
    {
        var layout = LayoutFor(_focused);

        if (_focused is null
            || layout is null
            || !layout.TryGetBox(_focused, out var box)
            || !TryGetCaretRect(out var caret))
        {
            return;
        }

        var color = box.Style.Color == ComputedStyle.CurrentColor
            ? 0xFF212529u
            : box.Style.Color;

        overlay.AddDrawRect(caret, color);
    }

    /// <summary>Returns the layout result that contains the given element.</summary>
    /// <param name="element">Element to locate; may be <see langword="null"/>.</param>
    /// <returns>The inspector layout for inspector nodes, otherwise the app layout.</returns>
    private LayoutResult? LayoutFor(ElementNode? element) => LayoutForNode(element);

    /// <summary>
    /// Chooses the layout result that owns a node: the inspector's tree when the
    /// node descends from the inspector root, the app's tree otherwise.
    /// </summary>
    /// <param name="node">Node to classify; may be <see langword="null"/>.</param>
    /// <returns>
    /// The matching <see cref="LayoutResult"/>, which may be
    /// <see langword="null"/> before the first layout.
    /// </returns>
    private LayoutResult? LayoutForNode(IgniteSupportedDomNode? node)
    {
        if (node is not null
            && _devToolsRoot is not null
            && IsInDevToolsTree(node))
        {
            return _devToolsLayout;
        }

        return _layoutResult;
    }

    /// <summary>
    /// Drops interaction references to nodes that left the tree. Keeping a
    /// hovered, focused or open-select node alive also keeps its whole unmounted
    /// subtree and event delegates reachable.
    /// </summary>
    /// <remarks>
    /// Runs once per frame after layout, when box lookups can finally answer
    /// whether a node still exists. It clears committed select indices, the open
    /// dropdown, the pressed and pointer-capture targets, scroll animations, the
    /// focused element (dispatching <c>blur</c> and <c>change</c>) and the hover
    /// chain; the app tree and the inspector tree are both considered.
    /// </remarks>
    private void PruneInteractionNodes()
    {
        if (_layoutResult is null && _devToolsLayout is null)
        {
            return;
        }

        if (_selectIndices.Count > 0)
        {
            _staleSelects.Clear();

            foreach (var pair in _selectIndices)
            {
                if (!IsLiveNode(pair.Key))
                {
                    _staleSelects.Add(pair.Key);
                }
            }

            for (var i = 0; i < _staleSelects.Count; i++)
            {
                _selectIndices.Remove(_staleSelects[i]);
            }
        }

        if (_openSelect is not null && !IsLiveNode(_openSelect))
        {
            CloseSelectMenu();
        }

        if (_pressed is not null && !IsLiveNode(_pressed))
        {
            _pressed = null;
        }

        if (_pointerTarget is not null && !IsLiveNode(_pointerTarget))
        {
            _pointerTarget = null;
        }

        if (_scrollAnimations.Count > 0)
        {
            _staleScrollAnimations.Clear();

            foreach (var pair in _scrollAnimations)
            {
                if (pair.Key is ElementNode element && !IsLiveNode(element))
                {
                    _staleScrollAnimations.Add(pair.Key);
                }
            }

            for (var i = 0; i < _staleScrollAnimations.Count; i++)
            {
                _scrollAnimations.Remove(_staleScrollAnimations[i]);
            }
        }

        if (_focused is not null && !IsLiveNode(_focused))
        {
            Focus(null);
        }

        if (_hovered is not null && !IsLiveNode(_hovered))
        {
            SetChainState(
                _hoverChain,
                null,
                static node => node.IsHovered = true,
                static node => node.IsHovered = false);
            _hovered = null;
        }
    }

    /// <summary>Reports whether a node still has a box in the layout that owns it.</summary>
    /// <param name="node">The element to check.</param>
    /// <returns><see langword="true"/> when the node is still part of a laid-out tree.</returns>
    /// <remarks>
    /// Box presence is the cheapest reliable liveness signal: a
    /// <see cref="Display.None"/> node is not laid out, so it is treated as gone
    /// here and its interaction state is dropped.
    /// </remarks>
    private bool IsLiveNode(ElementNode node)
    {
        var layout = LayoutForNode(node);
        return layout is not null && layout.TryGetBox(node, out var ignoredBox);
    }

    /// <summary>
    /// Reports whether an element accepts typed text: <c>textarea</c>, or an
    /// <c>input</c> whose type is text-like or absent.
    /// </summary>
    /// <param name="element">The element to test.</param>
    /// <returns><see langword="true"/> for editable text controls.</returns>
    /// <remarks>
    /// Checkbox, radio, range, colour and file inputs are excluded; they respond to
    /// clicks and keys but not to character input.
    /// </remarks>
    private static bool IsTextControl(ElementNode element)
    {
        if (element.Tag == "textarea")
        {
            return true;
        }

        if (element.Tag != "input")
        {
            return false;
        }

        var type = GetAttribute(element, "type");
        return type is null or "text" or "search" or "email" or "url" or "tel"
            or "password" or "number";
    }

    /// <summary>
    /// Returns the painted top-left corner of a box's content area, in window CSS
    /// pixels.
    /// </summary>
    /// <param name="box">The box to project.</param>
    /// <returns>The content origin after ancestor scroll offsets are applied.</returns>
    /// <remarks>
    /// Computed as the painted border box plus the content box's offset inside it,
    /// so padding, borders and scrolling are all accounted for.
    /// </remarks>
    private (float X, float Y) ContentOrigin(LayoutBox box)
    {
        var painted = PaintedBorderBox(box);
        return (
            painted.X + (box.ContentBox.X - box.BorderBox.X),
            painted.Y + (box.ContentBox.Y - box.BorderBox.Y));
    }

    /// <summary>
    /// Builds the text style used to measure and paint a control's editable text.
    /// </summary>
    /// <param name="box">The control's layout box.</param>
    /// <param name="color">ARGB colour to force, usually the caret or text colour.</param>
    /// <returns>The computed style adapted to a <see cref="TextStyle"/>.</returns>
    /// <remarks>Font size is resolved against the current root length context.</remarks>
    private TextStyle GetControlTextStyle(LayoutBox box, uint color)
    {
        var fontSize = box.Style.ResolveFontSize(in _lengthContext);
        return box.Style.ToTextStyle(in _lengthContext, fontSize, color);
    }

    /// <summary>
    /// Moves the caret of a text control to the character boundary nearest a
    /// pointer X coordinate, and restarts the blink cycle.
    /// </summary>
    /// <param name="element">The control whose caret moves.</param>
    /// <param name="x">Pointer X in window CSS pixels.</param>
    /// <remarks>
    /// <para>
    /// Single-line inputs walk text elements (respecting surrogate pairs) and stop
    /// at the first boundary whose measured prefix is already right of the pointer,
    /// so placement snaps left rather than to the globally closest boundary.
    /// </para>
    /// <para>
    /// A <c>textarea</c> first derives the line from the last known pointer Y, then
    /// measures every prefix and keeps the boundary on that line whose trailing edge
    /// is closest to the pointer. This is O(n) text layouts per click and is
    /// intended for the click path only.
    /// </para>
    /// </remarks>
    private void PlaceCaretFromPoint(ElementNode element, float x)
    {
        var boxLayout = LayoutFor(element);

        if (!IsTextControl(element)
            || boxLayout is null
            || !boxLayout.TryGetBox(element, out var box))
        {
            return;
        }

        var text = _editText ?? string.Empty;

        if (text.Length == 0)
        {
            _caretIndex = 0;
            ResetCaretBlink();
            return;
        }

        var color = box.Style.Color == ComputedStyle.CurrentColor
            ? 0xFF212529u
            : box.Style.Color;
        var textStyle = GetControlTextStyle(box, color);
        var (originX, originY) = ContentOrigin(box);
        var contentWidth = MathF.Max(0, box.ContentBox.Width);

        if (element.Tag == "textarea")
        {
            var layout = TextLayoutEngine.Layout(
                text,
                in textStyle,
                _measurer,
                contentWidth,
                box.Style.WhiteSpace,
                box.Style.OverflowWrap,
                box.Style.WebkitLineClamp,
                box.Style.TextOverflow);

            if (layout.Lines.Length == 0)
            {
                _caretIndex = 0;
                ResetCaretBlink();
                return;
            }

            var lineHeight = MathF.Max(1, textStyle.LineHeight);
            var line = Math.Clamp(
                (int)MathF.Floor((_mouseY - originY) / lineHeight),
                0,
                layout.Lines.Length - 1);
            var best = 0;
            var bestDistance = float.MaxValue;

            for (var index = 0; index <= text.Length; index = NextTextElement(text, index))
            {
                var prefix = TextLayoutEngine.Layout(
                    text[..index],
                    in textStyle,
                    _measurer,
                    contentWidth,
                    box.Style.WhiteSpace,
                    box.Style.OverflowWrap,
                    box.Style.WebkitLineClamp,
                    box.Style.TextOverflow);

                if (prefix.Lines.Length == 0 || prefix.Lines.Length - 1 != line)
                {
                    continue;
                }

                var candidate = prefix.Lines[^1];
                var distance = MathF.Abs(originX + candidate.X + candidate.Width - x);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
            }

            _caretIndex = best;
        }
        else
        {
            var target = x - originX;
            var index = 0;

            while (index < text.Length)
            {
                var next = NextTextElement(text, index);
                var width = _measurer.Measure(text.AsSpan(0, next), in textStyle).Width;

                if (width > target)
                {
                    break;
                }

                index = next;
            }

            _caretIndex = index;
        }

        ResetCaretBlink();
    }

    /// <summary>
    /// Computes the caret rectangle for the focused control, in window CSS pixels.
    /// </summary>
    /// <param name="caret">
    /// The rectangle to paint; <c>default</c> when this returns <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a caret should be drawn right now.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Returns <see langword="false"/> when nothing is focused, the focus is not a
    /// text control, the blink phase is off, or the control owns a text selection
    /// (a selected run replaces the caret visually).
    /// </para>
    /// <para>
    /// A <c>textarea</c> caret sits at the end of the last laid-out line of the
    /// text before the index, which follows wrapping, <c>white-space</c> and line
    /// clamping. Single-line carets use the measured prefix width.
    /// </para>
    /// <para>
    /// The caret is clamped inside the content box and is at least one pixel tall;
    /// its width scales with font size so it stays visible at small text sizes.
    /// </para>
    /// </remarks>
    private bool TryGetCaretRect(out Rect caret)
    {
        caret = default;
        var boxLayout = LayoutFor(_focused);

        if (_focused is null
            || !IsTextControl(_focused)
            || !IsCaretVisible
            || HasControlSelection
            || boxLayout is null
            || !boxLayout.TryGetBox(_focused, out var box))
        {
            return false;
        }

        var text = _editText ?? string.Empty;
        var color = box.Style.Color == ComputedStyle.CurrentColor
            ? 0xFF212529u
            : box.Style.Color;
        var textStyle = GetControlTextStyle(box, color);
        var (originX, originY) = ContentOrigin(box);
        var contentWidth = MathF.Max(0, box.ContentBox.Width);
        var contentHeight = MathF.Max(0, box.ContentBox.Height);
        var lineHeight = box.Style.ResolveLineHeight(in _lengthContext);
        var index = Math.Clamp(_caretIndex, 0, text.Length);
        float x;
        float y;
        float height;

        if (_focused.Tag == "textarea")
        {
            var layout = TextLayoutEngine.Layout(
                text[..index],
                in textStyle,
                _measurer,
                contentWidth,
                box.Style.WhiteSpace,
                box.Style.OverflowWrap,
                box.Style.WebkitLineClamp,
                box.Style.TextOverflow);

            if (layout.Lines.Length == 0)
            {
                x = originX;
                y = originY;
                height = lineHeight;
            }
            else
            {
                var line = layout.Lines[^1];
                x = originX + line.X + line.Width;
                y = originY + line.Y;
                height = line.Height;
            }
        }
        else
        {
            x = originX + _measurer.Measure(text.AsSpan(0, index), in textStyle).Width;
            y = originY;
            height = lineHeight;
        }

        if (contentHeight > 0)
        {
            height = MathF.Min(height, contentHeight);
        }

        height = MathF.Max(1, height);

        var maxX = originX + MathF.Max(0, contentWidth - 1);
        x = Math.Clamp(x, originX, maxX);
        y = Math.Clamp(y, originY, originY + MathF.Max(0, contentHeight - height));

        var width = MathF.Max(1, textStyle.FontSize * 0.07f);
        caret = new Rect(x, y, width, height);
        return true;
    }

    // -------------------------------------------------------------- replaced

    /// <summary>
    /// Supplies the intrinsic size of replaced elements so layout can size them
    /// before CSS constraints apply.
    /// </summary>
    /// <param name="node">The node being laid out.</param>
    /// <returns>
    /// The intrinsic size, or <see langword="null"/> when the element is not
    /// replaced or has no usable size.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Images prefer the decoded pixel size so aspect ratio is preserved, then the
    /// <c>width</c>/<c>height</c> attributes, then nothing. Canvas, video, iframe,
    /// object and embed default to the HTML 300 × 150 box.
    /// </para>
    /// <para>
    /// Form controls deliberately return <see langword="null"/>: they are sized by
    /// CSS plus their text line box, and reporting an intrinsic height here would
    /// be treated as a border-box height and clip that line box.
    /// </para>
    /// </remarks>
    private Size? GetIntrinsicSize(IgniteSupportedDomNode node)
    {
        if (node is not ElementNode element)
        {
            return null;
        }

        switch (element.Tag)
        {
            case "img":
            {
                var source = GetAttribute(element, "src");

                if (!string.IsNullOrEmpty(source) && _images.TryGetInfo(source, out var width, out var height))
                {
                    return new Size(width, height);
                }

                var attrWidth = HtmlDefaults.AttributeLength(element, "width", 0);
                var attrHeight = HtmlDefaults.AttributeLength(element, "height", 0);

                if (attrWidth > 0 && attrHeight > 0)
                {
                    return new Size(attrWidth, attrHeight);
                }

                return null;
            }

            case "canvas":
                return new Size(
                    HtmlDefaults.AttributeLength(element, "width", 300),
                    HtmlDefaults.AttributeLength(element, "height", 150));

            case "audio":
                return element.TryGetAttribute("controls", out var ignoredAudioControls)
                    ? new Size(
                        HtmlDefaults.AttributeLength(element, "width", 300),
                        HtmlDefaults.AttributeLength(element, "height", 54))
                    : new Size(0, 0);

            case "video":
            case "iframe":
            case "object":
            case "embed":
                return new Size(
                    HtmlDefaults.AttributeLength(element, "width", 300),
                    HtmlDefaults.AttributeLength(element, "height", 150));

            case "input":
            case "textarea":
            case "select":
            case "progress":
            case "meter":
                // Form controls are sized by their CSS width/min-height and their
                // text line box. Supplying an "intrinsic height" here would be
                // interpreted as a border-box height and clip the line box.
                return null;
        }

        return null;
    }

    /// <summary>
    /// Resolves the image to draw for a box: an <c>img</c> with a non-empty
    /// <c>src</c> attribute.
    /// </summary>
    /// <param name="box">The box being painted.</param>
    /// <returns>The image descriptor, or <see langword="null"/> for any other box.</returns>
    /// <remarks>
    /// The fit mode comes from the computed <c>object-fit</c>. The descriptor is
    /// produced every layout, so the caller must treat it as transient; the image
    /// cache owns the decoded texture.
    /// </remarks>
    private ImageSpec? GetImage(LayoutBox box)
    {
        if (box.Node is not ElementNode element)
        {
            return null;
        }

        if (string.Equals(element.Tag, "video", StringComparison.OrdinalIgnoreCase))
        {
            var videoKey = _videoHost?.GetTextureKey(element);

            return videoKey is null
                ? null
                : new ImageSpec(videoKey, box.Style.ObjectFit, 1, 0xFFFFFFFF);
        }

        if (!string.Equals(element.Tag, "img", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var source = GetAttribute(element, "src");

        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        return new ImageSpec(
            source,
            box.Style.ObjectFit,
            1,
            0xFFFFFFFF);
    }

    // --------------------------------------------------------------- controls

    /// <summary>
    /// Custom painter for inspector chrome; runs after the box's background and
    /// border have been written.
    /// </summary>
    /// <param name="box">The box being painted.</param>
    /// <param name="instructions">Display list to append to.</param>
    /// <param name="constants">Constant pool for radii and colours.</param>
    /// <remarks>
    /// Paints the text selection first, then delegates to the colour wheel and
    /// swatch painters based on the element's classes. Everything else is left to
    /// the standard painter.
    /// </remarks>
    private void PaintDevToolsContent(
        LayoutBox box,
        InstructionBuffer instructions,
        ConstantPool constants)
    {
        PaintSelection(box, instructions);

        if (box.Node is not ElementNode element)
        {
            return;
        }

        if (element.HasClass("dt-color-wheel"))
        {
            PaintColorWheel(box, instructions, element);
            return;
        }

        if (!element.HasClass("dt-swatch"))
        {
            return;
        }

        if (!ColorPreview.TryParse(GetAttribute(element, "data-color"), out var color))
        {
            return;
        }

        var rect = PaintedBorderBox(box);

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var radius = checked((ushort)constants.AddRadius(CornerRadius.All(2)));
        instructions.AddDrawRoundedRect(rect, 0xFF5F6368, radius);

        var inner = rect.Deflate(1, 1, 1, 1);

        if (inner.Width <= 0 || inner.Height <= 0)
        {
            return;
        }

        // A light base keeps translucent colours readable on the dark panel.
        instructions.AddDrawRoundedRect(inner, 0xFFFFFFFF, radius);
        instructions.AddDrawRoundedRect(inner, color, radius);
    }

    /// <summary>
    /// Paints a hue/saturation wheel as a grid of small ellipses, plus the current
    /// colour knob.
    /// </summary>
    /// <param name="box">The wheel's layout box.</param>
    /// <param name="instructions">Display list to append to.</param>
    /// <param name="element">The element holding <c>data-color</c>.</param>
    /// <remarks>
    /// This is a software approximation, not a shader: 96 hues × 10 saturation
    /// rings of ellipses approximate a continuous wheel at low cost. The centre is
    /// drawn as a white dot to hide the innermost gap.
    /// </remarks>
    private static void PaintColorWheel(
        LayoutBox box,
        InstructionBuffer instructions,
        ElementNode element)
    {
        var rect = PaintedBorderBox(box);

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var centerX = rect.X + rect.Width / 2f;
        var centerY = rect.Y + rect.Height / 2f;
        var radius = MathF.Min(rect.Width, rect.Height) / 2f - 2f;
        const int angles = 96;
        const int rings = 10;
        var ring = radius / rings;
        var size = ring * 1.3f;

        for (var r = 1; r <= rings; r++)
        {
            var saturation = r / (float)rings;
            var distance = ring * (r - 0.5f);

            for (var a = 0; a < angles; a++)
            {
                var hue = a / (float)angles;
                var angle = hue * MathF.PI * 2f;
                var x = centerX + MathF.Cos(angle) * distance;
                var y = centerY + MathF.Sin(angle) * distance;
                var color = ColorPreview.FromHsv(hue, saturation, 1f);

                instructions.Add(new Instruction(
                    InstructionOpCode.DrawEllipse,
                    x - size / 2f,
                    y - size / 2f,
                    size,
                    size,
                    unchecked((int)color)));
            }
        }

        instructions.Add(new Instruction(
            InstructionOpCode.DrawEllipse,
            centerX - ring / 2f,
            centerY - ring / 2f,
            ring,
            ring,
            unchecked((int)0xFFFFFFFF)));

        PaintColorWheelKnob(box, instructions, element);
    }

    /// <summary>
    /// Draws the two-tone knob marking the current hue and saturation on a colour
    /// wheel.
    /// </summary>
    /// <param name="box">The wheel's layout box.</param>
    /// <param name="instructions">Display list to append to.</param>
    /// <param name="element">The element holding <c>data-color</c>.</param>
    /// <remarks>Silently draws nothing when the attribute is missing or unparsable.</remarks>
    private static void PaintColorWheelKnob(
        LayoutBox box,
        InstructionBuffer instructions,
        ElementNode element)
    {
        if (!ColorPreview.TryParse(GetAttribute(element, "data-color"), out var color))
        {
            return;
        }

        var rect = PaintedBorderBox(box);

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var (hue, saturation, value) = ColorPreview.ToHsv(color);
        var centerX = rect.X + rect.Width / 2f;
        var centerY = rect.Y + rect.Height / 2f;
        var radius = MathF.Min(rect.Width, rect.Height) / 2f - 9f;
        var angle = hue * MathF.PI * 2f;
        var x = centerX + MathF.Cos(angle) * radius * saturation;
        var y = centerY + MathF.Sin(angle) * radius * saturation;

        instructions.Add(new Instruction(
            InstructionOpCode.DrawEllipse,
            x - 6f,
            y - 6f,
            12f,
            12f,
            unchecked((int)0xFF202124)));
        instructions.Add(new Instruction(
            InstructionOpCode.DrawEllipse,
            x - 4.5f,
            y - 4.5f,
            9f,
            9f,
            unchecked((int)0xFFFFFFFF)));
    }

    /// <summary>
    /// Custom painter for app content that CSS alone cannot express: spinners, the
    /// the host toggler icon and native-looking form controls.
    /// </summary>
    /// <param name="box">The box being painted.</param>
    /// <param name="instructions">Display list to append to.</param>
    /// <param name="constants">Constant pool for radii and colours.</param>
    /// <remarks>
    /// Paints the text selection first so highlights stay behind glyphs. Control
    /// painting is additive: the box background and border have already been
    /// emitted by the layout engine.
    /// </remarks>
    private void PaintElementContent(LayoutBox box, InstructionBuffer instructions, ConstantPool constants)
    {
        PaintSelection(box, instructions);

        if (box.Node is not ElementNode element)
        {
            return;
        }

        switch (element.Tag)
        {
            case "input":
            {
                var type = GetAttribute(element, "type");

                if (type == "checkbox" || type == "radio")
                {
                    PaintCheckControl(box, instructions, constants, type == "radio");
                }
                else if (type == "range")
                {
                    PaintRange(box, instructions, constants, element);
                }

                break;
            }

            case "progress":
            case "meter":
                PaintProgress(box, instructions, constants, element);
                break;

            case "audio":
            case "video":
                PaintMediaControls(box, element, instructions, constants);
                break;
        }
    }

    /// <summary>Gets the playback state for an audio or video element.</summary>
    private (bool IsPlaying, float Time, float Duration)? GetMediaState(ElementNode element)
    {
        if (string.Equals(element.Tag, "audio", StringComparison.OrdinalIgnoreCase))
        {
            var audio = _audioHost?.GetPlayer(element);
            return audio is null ? null : (audio.IsPlaying, audio.Time, audio.Duration);
        }

        if (string.Equals(element.Tag, "video", StringComparison.OrdinalIgnoreCase))
        {
            var video = _videoHost?.GetPlayer(element);
            return video is null ? null : (video.IsPlaying, video.Time, video.Duration);
        }

        return null;
    }

    /// <summary>Starts or pauses an audio/video element.</summary>
    private void ToggleMedia(ElementNode element)
    {
        if (string.Equals(element.Tag, "audio", StringComparison.OrdinalIgnoreCase))
        {
            var audio = _audioHost?.GetPlayer(element);

            if (audio is null)
            {
                return;
            }

            if (audio.IsPlaying)
            {
                audio.Pause();
            }
            else
            {
                audio.Play();
            }

            return;
        }

        var video = _videoHost?.GetPlayer(element);

        if (video is null)
        {
            return;
        }

        if (video.IsPlaying)
        {
            video.Pause();
        }
        else
        {
            video.Play();
        }
    }

    /// <summary>Seeks an audio/video element to a fraction of its duration.</summary>
    private void SeekMedia(ElementNode element, float fraction)
    {
        var duration = GetMediaState(element)?.Duration ?? 0;

        if (duration <= 0)
        {
            return;
        }

        if (string.Equals(element.Tag, "audio", StringComparison.OrdinalIgnoreCase))
        {
            _audioHost?.GetPlayer(element)?.Seek(duration * fraction);
        }
        else
        {
            _videoHost?.GetPlayer(element)?.Seek(duration * fraction);
        }
    }

    /// <summary>
    /// Paints the optional transport bar of an <c>audio</c> element: a
    /// play/pause button, a clickable progress bar and elapsed/total time.
    /// </summary>
    private void PaintMediaControls(
        LayoutBox box,
        ElementNode element,
        InstructionBuffer instructions,
        ConstantPool constants)
    {
        if (!element.TryGetAttribute("controls", out var ignoredControls))
        {
            return;
        }

        var (bar, button, progress) = GetAudioControlRects(box);

        if (bar.Width <= 0 || bar.Height <= 0)
        {
            return;
        }

        var state = GetMediaState(element);
        var playing = state?.IsPlaying == true;
        var time = state?.Time ?? 0;
        var duration = state?.Duration ?? 0;

        instructions.AddDrawRect(bar, 0xFFF6F8FA);
        instructions.AddDrawRect(new Rect(bar.X, bar.Y, bar.Width, 1), 0xFFD0D7DE);

        var iconStyle = TextStyle.Default with
        {
            FontFamily = box.Style.FontFamily,
            FontSize = MathF.Max(10, button.Height * 0.8f),
            LineHeight = button.Height,
            Color = 0xFF212529
        };
        var icon = constants.AddText(playing ? "❚❚" : "▶", iconStyle);
        instructions.Add(Instruction.DrawText(button, icon));

        instructions.AddDrawRect(progress, 0xFFD0D7DE);

        var fraction = duration > 0 ? Math.Clamp(time / duration, 0f, 1f) : 0f;

        if (fraction > 0)
        {
            instructions.AddDrawRect(
                new Rect(progress.X, progress.Y, progress.Width * fraction, progress.Height),
                0xFF0969DA);
        }

        var thumbSize = MathF.Max(8f, progress.Height * 2.5f);
        instructions.AddDrawRect(
            new Rect(
                progress.X + progress.Width * fraction - thumbSize / 2f,
                progress.Y - (thumbSize - progress.Height) / 2f,
                thumbSize,
                thumbSize),
            0xFF0969DA);

        var label = $"{FormatAudioTime(time)} / {FormatAudioTime(duration)}";
        var labelStyle = TextStyle.Default with
        {
            FontFamily = box.Style.FontFamily,
            FontSize = 11,
            LineHeight = bar.Height,
            Color = 0xFF57606A
        };
        var labelId = constants.AddText(label, labelStyle);
        var labelX = progress.X + progress.Width + 8;
        var labelWidth = MathF.Max(0, bar.X + bar.Width - labelX - 8);

        if (labelWidth > 0)
        {
            instructions.Add(Instruction.DrawText(
                new Rect(labelX, bar.Y, labelWidth, bar.Height),
                labelId));
        }
    }

    /// <summary>Computes the transport-bar, button and progress rects for an audio box.</summary>
    private static (Rect Bar, Rect Button, Rect Progress) GetAudioControlRects(LayoutBox box)
    {
        var rect = PaintedBorderBox(box);
        var barHeight = MathF.Min(rect.Height, 48f);
        var bar = new Rect(rect.X, rect.Y + rect.Height - barHeight, rect.Width, barHeight);
        var buttonSize = MathF.Min(24f, MathF.Max(12f, barHeight - 12f));
        var button = new Rect(
            rect.X + 12f,
            bar.Y + (barHeight - buttonSize) / 2f,
            buttonSize,
            buttonSize);
        var progressX = button.X + button.Width + 12f;
        const float labelWidth = 96f;
        var progressWidth = MathF.Max(0f, rect.X + rect.Width - 12f - labelWidth - progressX);
        var progress = new Rect(progressX, bar.Y + barHeight / 2f - 2f, progressWidth, 4f);
        return (bar, button, progress);
    }

    /// <summary>Formats a duration as <c>m:ss</c> for the audio control bar.</summary>
    private static string FormatAudioTime(float seconds)
    {
        if (float.IsNaN(seconds) || seconds <= 0)
        {
            return "0:00";
        }

        var total = (int)seconds;
        return $"{total / 60}:{total % 60:00}";
    }

    /// <summary>
    /// Starts a native window drag when the press lands in an
    /// <c>app-region: drag</c> area.
    /// </summary>
    private bool TryBeginWindowDrag(ElementNode? element)
    {
        if (_window is null || element is null || ResolveAppRegion(element) != AppRegion.Drag)
        {
            return false;
        }

        var point = new Vector2D<int>(
            (int)MathF.Round(_mouseX),
            (int)MathF.Round(_mouseY));

        _windowDragStartScreen = _window.PointToScreen(point);
        _windowDragStartPosition = _window.Position;
        _windowDragging = true;
        _pressed = null;
        _pointerTarget = null;
        SetActive(null);
        return true;
    }

    /// <summary>Moves the window by the pointer delta accumulated since the drag began.</summary>
    private void UpdateWindowDrag()
    {
        if (_window is null || !_windowDragging)
        {
            return;
        }

        var point = new Vector2D<int>(
            (int)MathF.Round(_mouseX),
            (int)MathF.Round(_mouseY));
        var screen = _window.PointToScreen(point);
        var deltaX = screen.X - _windowDragStartScreen.X;
        var deltaY = screen.Y - _windowDragStartScreen.Y;

        _window.Position = new Vector2D<int>(
            _windowDragStartPosition.X + deltaX,
            _windowDragStartPosition.Y + deltaY);
    }

    /// <summary>
    /// Resolves the nearest explicit <c>app-region</c> on the hit element or its
    /// ancestors. A closer <c>no-drag</c> region opts out of an enclosing drag
    /// region.
    /// </summary>
    private AppRegion ResolveAppRegion(ElementNode element)
    {
        for (IgniteSupportedDomNode? node = element; node is not null; node = node.Parent)
        {
            if (node is not ElementNode current)
            {
                continue;
            }

            var layout = LayoutFor(current);

            if (layout is not null
                && layout.TryGetBox(current, out var box)
                && box.Style.AppRegion != AppRegion.None)
            {
                return box.Style.AppRegion;
            }
        }

        return AppRegion.None;
    }

    /// <summary>
    /// Handles a press on an audio or video transport bar. Returns
    /// <see langword="true"/> when the press landed on the bar and was consumed.
    /// </summary>
    private bool TryHandleMediaPointer(ElementNode element, float x, float y)
    {
        if (!element.TryGetAttribute("controls", out var ignoredControls))
        {
            return false;
        }

        var layout = LayoutFor(element);

        if (layout is null || !layout.TryGetBox(element, out var box))
        {
            return false;
        }

        var (bar, button, progress) = GetAudioControlRects(box);

        if (x < bar.X || x > bar.X + bar.Width || y < bar.Y || y > bar.Y + bar.Height)
        {
            return false;
        }

        if (GetMediaState(element) is null)
        {
            return true;
        }

        if (x >= button.X && x <= button.X + button.Width
            && y >= button.Y && y <= button.Y + button.Height)
        {
            ToggleMedia(element);
        }
        else if (progress.Width > 0
            && x >= progress.X && x <= progress.X + progress.Width)
        {
            _mediaSeekElement = element;
            SeekMediaFromX(element, progress, x);
        }

        _needsPaint = true;
        return true;
    }

    /// <summary>Continues a thumb drag on an audio/video progress bar.</summary>
    private void UpdateMediaSeekDrag(float x)
    {
        if (_mediaSeekElement is null)
        {
            return;
        }

        var layout = LayoutFor(_mediaSeekElement);

        if (layout is null || !layout.TryGetBox(_mediaSeekElement, out var box))
        {
            return;
        }

        var (_, _, progress) = GetAudioControlRects(box);
        SeekMediaFromX(_mediaSeekElement, progress, x);
        _needsPaint = true;
    }

    /// <summary>Maps a horizontal pointer position to a media seek operation.</summary>
    private void SeekMediaFromX(ElementNode element, Rect progress, float x)
    {
        if (progress.Width <= 0)
        {
            return;
        }

        SeekMedia(element, Math.Clamp((x - progress.X) / progress.Width, 0f, 1f));
    }


    /// <summary>
    /// Draws the accent mark of a checked checkbox (rounded square) or radio
    /// button (ellipse).
    /// </summary>
    /// <param name="box">The input's layout box.</param>
    /// <param name="instructions">Display list to append to.</param>
    /// <param name="constants">Constant pool for the checkbox radius.</param>
    /// <param name="radio">Whether to draw a radio dot instead of a check box.</param>
    /// <remarks>
    /// The mark is inset to 56% of the content box. Unchecked controls paint
    /// nothing: their appearance comes from CSS borders and background.
    /// </remarks>
    private static void PaintCheckControl(
        LayoutBox box,
        InstructionBuffer instructions,
        ConstantPool constants,
        bool radio)
    {
        var boxRect = box.ContentBox;

        if (boxRect.IsEmpty)
        {
            boxRect = box.BorderBox.Deflate(box.Style.BorderWidth.Top.Value, box.Style.BorderWidth.Right.Value,
                box.Style.BorderWidth.Bottom.Value, box.Style.BorderWidth.Left.Value);
        }

        var checkedState = box.Node.TryGetAttribute(CheckedAttribute, out var ignored);

        if (!checkedState)
        {
            return;
        }

        var accent = 0xFF0969DA;
        var rect = boxRect.Deflate(boxRect.Width * 0.22f, boxRect.Height * 0.22f,
            boxRect.Width * 0.22f, boxRect.Height * 0.22f);

        if (rect.IsEmpty)
        {
            return;
        }

        if (radio)
        {
            instructions.Add(new Instruction(
                InstructionOpCode.DrawEllipse,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                unchecked((int)accent)));
        }
        else
        {
            var radius = constants.AddRadius(CornerRadius.All(MathF.Max(2, rect.Width * 0.2f)));
            instructions.AddDrawRoundedRect(rect, accent, checked((ushort)radius));
        }
    }

    /// <summary>
    /// Draws a range input as a rounded track with an accent-colored fill up to the
    /// current value.
    /// </summary>
    /// <param name="box">The input's layout box.</param>
    /// <param name="instructions">Display list to append to.</param>
    /// <param name="constants">Constant pool for the track radius.</param>
    /// <param name="element">The input, read for <c>min</c>, <c>max</c> and <c>value</c>.</param>
    /// <remarks>
    /// The ratios are clamped to 0–1 and the divisor is floored at 0.0001, so equal
    /// bounds or a zero-height box cannot produce NaN geometry. No thumb is drawn;
    /// the fill edge stands in for it.
    /// </remarks>
    private static void PaintRange(
        LayoutBox box,
        InstructionBuffer instructions,
        ConstantPool constants,
        ElementNode element)
    {
        var rect = box.ContentBox;

        if (rect.IsEmpty)
        {
            return;
        }

        var min = HtmlDefaults.AttributeLength(element, "min", 0);
        var max = HtmlDefaults.AttributeLength(element, "max", 100);
        var value = HtmlDefaults.AttributeLength(element, "value", min);
        var ratio = Math.Clamp((value - min) / MathF.Max(0.0001f, max - min), 0, 1);
        var track = new Rect(rect.X, rect.Y + rect.Height * 0.4f, rect.Width, MathF.Max(4, rect.Height * 0.2f));
        var radius = constants.AddRadius(CornerRadius.All(track.Height * 0.5f));
        instructions.AddDrawRoundedRect(track, 0xFFD0D7DE, checked((ushort)radius));
        instructions.AddDrawRoundedRect(
            track.WithSize(MathF.Max(track.Height, track.Width * ratio), track.Height),
            0xFF0969DA,
            checked((ushort)radius));
    }

    /// <summary>
    /// Draws a <c>progress</c> or <c>meter</c> element as a pill track with a fill
    /// proportional to its value.
    /// </summary>
    /// <param name="box">The element's layout box.</param>
    /// <param name="instructions">Display list to append to.</param>
    /// <param name="constants">Constant pool for the track radius.</param>
    /// <param name="element">The element, read for <c>min</c>, <c>max</c> and <c>value</c>.</param>
    /// <remarks>
    /// <c>max</c> defaults to 1, matching the HTML progress default, whereas range
    /// inputs default it to 100. The fill is at least one track height wide so a
    /// tiny non-zero value stays visible.
    /// </remarks>
    private static void PaintProgress(
        LayoutBox box,
        InstructionBuffer instructions,
        ConstantPool constants,
        ElementNode element)
    {
        var rect = box.ContentBox;

        if (rect.IsEmpty)
        {
            return;
        }

        var min = HtmlDefaults.AttributeLength(element, "min", 0);
        var max = HtmlDefaults.AttributeLength(element, "max", 1);
        var value = HtmlDefaults.AttributeLength(element, "value", min);
        var ratio = Math.Clamp((value - min) / MathF.Max(0.0001f, max - min), 0, 1);
        var radius = constants.AddRadius(CornerRadius.All(MathF.Max(1, rect.Height * 0.5f)));
        instructions.AddDrawRoundedRect(rect, 0xFFEFF2F5, checked((ushort)radius));

        if (ratio > 0)
        {
            instructions.AddDrawRoundedRect(
                rect.WithSize(MathF.Max(rect.Height, rect.Width * ratio), rect.Height),
                0xFF0969DA,
                checked((ushort)radius));
        }
    }

    // ------------------------------------------------------------------ input

    /// <summary>
    /// Subscribes every keyboard and mouse reported by the input context to the
    /// window's routing handlers.
    /// </summary>
    /// <remarks>
    /// Called once from <see cref="OnLoad"/>. Handlers are anonymous closures that
    /// forward to the instance methods; they are never unsubscribed because the
    /// whole input context is disposed on close.
    /// </remarks>
    private void ConfigureInput()
    {
        if (_input is null)
        {
            return;
        }

        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) => OnKeyDown(keyboard, key);
            keyboard.KeyChar += (_, character) => OnKeyChar(character);
        }

        foreach (var mouse in _input.Mice)
        {
            mouse.MouseMove += (_, position) => OnMouseMove(position.X, position.Y);
            mouse.MouseDown += (_, button) => OnMouseDown(button);
            mouse.MouseUp += (_, button) => OnMouseUp(button);
            mouse.Scroll += (_, wheel) => OnScroll(wheel.Y, wheel.X);
        }
    }

    /// <summary>
    /// Routes a pointer move: drags win first, then pointer capture, then an open
    /// dropdown, and finally hit testing, cursor and hover chain updates.
    /// </summary>
    /// <param name="x">Pointer X in window CSS pixels.</param>
    /// <param name="y">Pointer Y in window CSS pixels.</param>
    /// <remarks>
    /// <para>
    /// The last position is cached for click handling and for wheel routing. An
    /// active selection drag or scrollbar drag consumes the move entirely; a
    /// captured pointer target receives coordinates relative to its own painted
    /// border box.
    /// </para>
    /// <para>
    /// While a dropdown is open, hovering a row suppresses page hover so the row
    /// highlight is not competed with by <c>:hover</c> styles underneath.
    /// </para>
    /// <para>
    /// In inspect mode the hit result is published to
    /// <see cref="DevToolsState.Hovered"/> but only for app nodes, never for the
    /// inspector's own chrome.
    /// </para>
    /// </remarks>
    private void OnMouseMove(float x, float y)
    {
        _mouseX = x;
        _mouseY = y;
        _devTools.PointerX = x;
        _devTools.PointerY = y;

        if (_windowDragging)
        {
            UpdateWindowDrag();
            return;
        }

        if (_selectionDragging)
        {
            UpdateSelectionDrag(x, y);
            return;
        }

        if (_scrollDrag is not null)
        {
            UpdateScrollbarDrag(x, y);
            return;
        }

        if (_mediaSeekElement is not null)
        {
            UpdateMediaSeekDrag(x);
            return;
        }

        if (_pointerTarget is not null)
        {
            UpdateCursor(_pointerTarget);
            InvokePointer(_pointerTarget, "pointermove", x, y);
            return;
        }

        if (_openSelect is not null)
        {
            var overOption = TryHitSelectOption(_openSelect, x, y, out var optionIndex);
            _openSelectHover = overOption ? optionIndex : -1;

            if (overOption)
            {
                SetHovered(null);
                return;
            }
        }

        var element = HitTest(x, y);
        UpdateCursor(element);
        if (_devTools.InspectMode)
        {
            _devTools.Hovered = element is not null && !IsInDevToolsTree(element)
                ? element
                : null;
        }

        SetHovered(element);
    }

    /// <summary>
    /// Routes a pointer press, from inspect-mode picking and pointer capture down
    /// to focus, text selection, dropdowns and control activation.
    /// </summary>
    /// <param name="button">
    /// The pressed button. Only the left button has browser-like semantics; other
    /// buttons run the same path because the engine exposes no context menu.
    /// </param>
    /// <remarks>
    /// <para>Precedence, highest first:</para>
    /// <list type="number">
    /// <item><description>
    /// Inspect mode consumes app hits so picking never activates the app.
    /// </description></item>
    /// <item><description>
    /// A <c>pointerdown</c> handler on the hit element or an ancestor captures the
    /// pointer; the element becomes active and no other press work runs.
    /// </description></item>
    /// <item><description>
    /// A scrollbar track or thumb starts a scroll interaction.
    /// </description></item>
    /// <item><description>
    /// An open dropdown either commits a row or closes; pressing the control itself
    /// re-focuses it without reopening.
    /// </description></item>
    /// <item><description>
    /// A text control focuses, places the caret, starts a selection drag and counts
    /// multi-clicks (2 selects a word, 3 or more selects everything).
    /// </description></item>
    /// <item><description>
    /// A selectable text run starts a text selection with the same click counting.
    /// </description></item>
    /// <item><description>
    /// Anything else focuses the target, opens a <c>select</c> and dispatches
    /// <c>mousedown</c>.
    /// </description></item>
    /// </list>
    /// <para>
    /// Shift suppresses the initial selection clear, so a click extends the existing
    /// selection instead of replacing it.
    /// </para>
    /// </remarks>
    private void OnMouseDown(MouseButton button)
    {
        _devTools.PointerX = _mouseX;
        _devTools.PointerY = _mouseY;

        if (_devTools.InspectMode)
        {
            var inspectHit = HitTest(_mouseX, _mouseY);

            if (inspectHit is not null && !IsInDevToolsTree(inspectHit))
            {
                _inspectPressed = true;
                return;
            }
        }

        if (button == MouseButton.Left && TryBeginWindowDrag(HitTest(_mouseX, _mouseY)))
        {
            return;
        }

        if (!IsShiftPressed())
        {
            ClearSelection();
        }

        var pointerTarget = FindPointerTarget(HitTest(_mouseX, _mouseY), "pointerdown");

        if (pointerTarget is not null)
        {
            _pointerTarget = pointerTarget;
            _pressed = null;
            SetActive(pointerTarget);
            InvokePointer(pointerTarget, "pointerdown", _mouseX, _mouseY);
            return;
        }

        if (TryBeginScrollbarInteraction(_mouseX, _mouseY))
        {
            return;
        }

        var element = HitTest(_mouseX, _mouseY);
        _pressX = _mouseX;
        _pressY = _mouseY;

        if (element is not null
            && (element.Tag == "audio" || element.Tag == "video")
            && TryHandleMediaPointer(element, _mouseX, _mouseY))
        {
            _pressed = element;
            SetActive(element);
            return;
        }

        if (_openSelect is not null)
        {
            if (TryHitSelectOption(_openSelect, _mouseX, _mouseY, out var optionIndex))
            {
                SelectOption(_openSelect, optionIndex);
                CloseSelectMenu();
                _pressed = null;
                SetActive(null);
                return;
            }

            var closed = _openSelect;
            CloseSelectMenu();

            // Clicking the open control itself closes the menu; clicking
            // anything else continues through the normal press handling.
            if (ReferenceEquals(element, closed))
            {
                _pressed = element;
                SetActive(element);
                Focus(element, focusVisible: false);
                Invoke(element, "mousedown");
                return;
            }
        }

        _pressed = element;
        SetActive(element);

        var shift = IsShiftPressed();

        if (element is not null && IsTextControl(element))
        {
            Focus(element, focusVisible: false, preserveSelection: shift);
            PlaceCaretFromPoint(element, _mouseX);

            var anchor = shift && HasControlSelection
                ? _selection!.AnchorOffset
                : _caretIndex;
            SetControlSelection(anchor, _caretIndex);
            BeginSelectionDrag();

            var clicks = RegisterClick(element, _mouseX, _mouseY);

            if (clicks == 2)
            {
                SelectWordInControl(_caretIndex);
            }
            else if (clicks >= 3)
            {
                SelectAllInControl();
            }

            Invoke(element, "mousedown");
            return;
        }

        if (element is not null
            && TryHitText(_mouseX, _mouseY, IsInDevToolsTree(element), out var run, out var offset))
        {
            var previous = _selection;
            Focus(element, focusVisible: false, preserveSelection: shift);

            if (shift
                && previous is { IsControl: false } preserved
                && preserved.IsDevTools == run.IsDevTools)
            {
                _selection = preserved;
                SetTextSelectionFocus(run, offset);
            }
            else
            {
                StartTextSelection(run, offset);
            }

            BeginSelectionDrag();

            var clicks = RegisterClick(element, _mouseX, _mouseY);

            if (clicks == 2)
            {
                SelectWord(run, offset);
            }
            else if (clicks >= 3)
            {
                SelectAllInRun(run);
            }

            Invoke(element, "mousedown");
            return;
        }

        if (!shift)
        {
            ClearSelection();
        }

        if (element is not null)
        {
            Focus(element, focusVisible: false);
        }

        if (element is { Tag: "select" })
        {
            OpenSelectMenu(element);
        }

        Invoke(element, "mousedown");
    }

    /// <summary>
    /// Routes a pointer release: completes inspect picks, selection drags, pointer
    /// capture and scrollbar drags, then synthesizes <c>mouseup</c> and <c>click</c>.
    /// </summary>
    /// <param name="button">The released button; all buttons share one path.</param>
    /// <remarks>
    /// <para>
    /// A pick is committed when the release lands on an app node, not on the
    /// inspector's own chrome; a drag that actually moved ends the selection and
    /// suppresses the click, which prevents selecting text from also activating
    /// the element under the pointer.
    /// </para>
    /// <para>
    /// A click is synthesized only when press and release hit the same element and
    /// that element is not disabled. It runs control activation first (checkbox,
    /// radio), then dispatches <c>click</c>, and finally focuses text controls so a
    /// click always leaves the caret in the field.
    /// </para>
    /// <para>
    /// <c>mouseup</c> is dispatched to the pressed element even when the pointer
    /// moved away, mirroring browsers; the click is not.
    /// </para>
    /// </remarks>
    private void OnMouseUp(MouseButton button)
    {
        _devTools.PointerX = _mouseX;
        _devTools.PointerY = _mouseY;

        if (_windowDragging)
        {
            _windowDragging = false;
            return;
        }

        if (_inspectPressed)
        {
            _inspectPressed = false;
            var inspectHit = HitTest(_mouseX, _mouseY);

            if (inspectHit is not null && !IsInDevToolsTree(inspectHit))
            {
                SelectInspectElement(inspectHit);
            }

            return;
        }

        if (_mediaSeekElement is not null)
        {
            _mediaSeekElement = null;
            _pressed = null;
            SetActive(null);
            _needsPaint = true;
            return;
        }

        if (_selectionDragging)
        {
            _selectionDragging = false;

            if (_selectionDragged)
            {
                _selectionDragged = false;

                if (_pressed is not null)
                {
                    SetActive(null);
                    Invoke(_pressed, "mouseup");
                    _pressed = null;
                }

                return;
            }
        }

        if (_pointerTarget is not null)
        {
            InvokePointer(_pointerTarget, "pointerup", _mouseX, _mouseY);
            _pointerTarget = null;
            SetActive(null);
            return;
        }

        if (_scrollDrag is not null)
        {
            _scrollDrag = null;
            _pressed = null;
            SetActive(null);
            return;
        }

        var element = HitTest(_mouseX, _mouseY);

        SetActive(null);
        Invoke(_pressed, "mouseup");

        // Focus and :focus/:active styles can relayout between press and
        // release, which moves a button under the pointer. As long as the
        // pointer did not actually drag, the press target remains the click
        // target, matching what the user sees and expects.
        var nearPress = _pressed is not null
            && MathF.Abs(_mouseX - _pressX) < 4
            && MathF.Abs(_mouseY - _pressY) < 4;

        if ((ReferenceEquals(element, _pressed) || nearPress)
            && _pressed is not null
            && !_pressed.TryGetAttribute("disabled", out var ignoredDisabled))
        {
            if (_pressed.Tag == "input")
            {
                ToggleInput(_pressed);
            }

            Invoke(_pressed, "click");

            if (_pressed.Tag is "input" or "textarea" or "select")
            {
                Focus(_pressed);
            }
        }

        _pressed = null;
        _selectionDragged = false;
    }

    /// <summary>
    /// Activates an <c>input</c> on click: toggles checkboxes, and selects radios
    /// while clearing same-named siblings.
    /// </summary>
    /// <param name="element">The input element that was clicked.</param>
    /// <remarks>
    /// <para>
    /// Checkbox state is mirrored into both the DOM attribute and the node's
    /// <c>IsChecked</c> flag so CSS selectors and layout agree. Radio grouping is by
    /// parent and <c>name</c> attribute only, matching the simplest browser rule;
    /// a radio without a name still clears nothing.
    /// </para>
    /// <para>
    /// Both paths dispatch <c>change</c> with no value and request a relayout. Other
    /// input types are ignored.
    /// </para>
    /// </remarks>
    private void ToggleInput(ElementNode element)
    {
        if (element.TryGetAttribute("disabled", out var ignoredDisabled))
        {
            return;
        }

        var type = GetAttribute(element, "type");

        if (type == "checkbox")
        {
            var checkedState = !element.IsChecked
                && !element.TryGetAttribute(CheckedAttribute, out var ignoredChecked);
            element.IsChecked = checkedState;

            if (checkedState)
            {
                element.SetAttribute(CheckedAttribute, InternTable.Intern(string.Empty));
            }
            else
            {
                element.RemoveAttribute(CheckedAttribute);
            }

            Invoke(element, "change");
            _needsLayout = true;
        }
        else if (type == "radio")
        {
            var parent = element.Parent as ElementNode;

            if (parent is not null)
            {
                var siblings = parent.Children;

                for (var i = 0; i < siblings.Length; i++)
                {
                    if (siblings[i] is ElementNode sibling
                        && sibling.Tag == "input"
                        && GetAttribute(sibling, "type") == "radio"
                        && GetAttribute(sibling, "name") is { } name
                        && name == GetAttribute(element, "name"))
                    {
                        sibling.IsChecked = false;
                        sibling.RemoveAttribute(CheckedAttribute);
                    }
                }
            }

            element.IsChecked = true;
            element.SetAttribute(CheckedAttribute, InternTable.Intern(string.Empty));
            Invoke(element, "change");
            _needsLayout = true;
        }
    }

    /// <summary>
    /// Routes wheel input to the nearest scrollable ancestor of the pointer.
    /// </summary>
    /// <param name="vertical">
    /// Vertical wheel delta in notches, positive away from the user.
    /// </param>
    /// <param name="horizontal">Horizontal wheel delta in notches.</param>
    /// <remarks>
    /// <para>
    /// An open dropdown is closed first so a wheel cannot leave a stale menu
    /// floating. Each axis is only applied when the chosen container actually
    /// overflows on that axis, which lets a nested horizontal scroller pass vertical
    /// input through to its parent.
    /// </para>
    /// <para>
    /// One notch is 48 CSS px, negated so scrolling down moves content up, and the
    /// result is clamped to the scrollable range to keep the opposite direction
    /// responsive.
    /// </para>
    /// </remarks>
    private void OnScroll(float vertical, float horizontal)
    {
        CloseSelectMenu();

        var box = HitTestBox(_mouseX, _mouseY);
        var scroll = FindScrollContainer(box, vertical, horizontal);

        if (scroll is null)
        {
            return;
        }

        // Only adjust the axes the chosen container can actually scroll, so a
        // nested non-scrolling container (such as a horizontally scrolling
        // table wrapper) never swallows vertical wheel input. Clamp to the
        // scrollable range so the stored offset cannot grow past the end and
        // make subsequent scrolls in the other direction feel stuck.
        var dx = 0f;
        var dy = 0f;

        if (vertical != 0 && scroll.ScrollHeight > scroll.ClientHeight + 0.5f)
        {
            dy = -vertical * 48;
        }

        if (horizontal != 0 && scroll.ScrollWidth > scroll.ClientWidth + 0.5f)
        {
            dx = -horizontal * 48;
        }

        if (dx != 0 || dy != 0)
        {
            ScrollBy(scroll, dx, dy);
        }
    }

    /// <summary>
    /// Adds a relative scroll delta to a container and clamps it to its range.
    /// </summary>
    /// <param name="scroll">The container box providing scroll metrics.</param>
    /// <param name="dx">Horizontal delta in CSS pixels.</param>
    /// <param name="dy">Vertical delta in CSS pixels.</param>
    /// <remarks>
    /// While a smooth scroll is animating, the delta is applied to the animation's
    /// pending target rather than the current offset, so a fast train of wheel
    /// events accumulates instead of fighting the interpolation.
    /// </remarks>
    private void ScrollBy(LayoutBox scroll, float dx, float dy)
    {
        var node = scroll.Node;

        // Build on the pending target while an animation is running so a fast
        // train of wheel events accumulates instead of fighting the current
        // interpolation.
        var animating = _scrollAnimations.TryGetValue(node, out var animation);
        var baseX = animating ? animation!.ToX : node.ScrollOffsetX;
        var baseY = animating ? animation!.ToY : node.ScrollOffsetY;

        var maxX = MathF.Max(0, scroll.ScrollWidth - scroll.ClientWidth);
        var maxY = MathF.Max(0, scroll.ScrollHeight - scroll.ClientHeight);

        ScrollTo(scroll, Math.Clamp(baseX + dx, 0, maxX), Math.Clamp(baseY + dy, 0, maxY));
    }

    /// <summary>
    /// Requests a scroll offset, animating it when the container asks for smooth
    /// scrolling and applying it immediately otherwise.
    /// </summary>
    /// <param name="scroll">The container box providing scroll metrics.</param>
    /// <param name="x">Target horizontal offset in CSS pixels; clamped to range.</param>
    /// <param name="y">Target vertical offset in CSS pixels; clamped to range.</param>
    /// <remarks>
    /// Distance-dependent duration (0.18–0.45 s) keeps short hops quick and long
    /// jumps readable. A target within half a pixel of the current offset cancels
    /// any running animation instead of starting a new one.
    /// </remarks>
    private void ScrollTo(LayoutBox scroll, float x, float y)
    {
        var maxX = MathF.Max(0, scroll.ScrollWidth - scroll.ClientWidth);
        var maxY = MathF.Max(0, scroll.ScrollHeight - scroll.ClientHeight);
        x = Math.Clamp(x, 0, maxX);
        y = Math.Clamp(y, 0, maxY);

        if (!IsSmoothScrolling(scroll))
        {
            SetScrollOffset(scroll.Node, x, y);
            return;
        }

        var node = scroll.Node;
        var fromX = node.ScrollOffsetX;
        var fromY = node.ScrollOffsetY;

        if (MathF.Abs(x - fromX) < 0.5f && MathF.Abs(y - fromY) < 0.5f)
        {
            _scrollAnimations.Remove(node);
            return;
        }

        var distance = MathF.Sqrt((x - fromX) * (x - fromX) + (y - fromY) * (y - fromY));
        var duration = Math.Clamp(0.18 + distance * 0.0004, 0.18, 0.45);

        _scrollAnimations[node] = new ScrollAnimation
        {
            FromX = fromX,
            FromY = fromY,
            ToX = x,
            ToY = y,
            Elapsed = 0,
            Duration = duration
        };
    }

    /// <summary>
    /// Decides whether a scroll should be animated.
    /// </summary>
    /// <param name="scroll">The container being scrolled.</param>
    /// <returns><see langword="true"/> when the offset should ease to its target.</returns>
    /// <remarks>
    /// <c>scroll-behavior: smooth</c> on the container always animates.
    /// <c>auto</c> inherits a smooth root style only for the outermost scrollable
    /// container: browsers apply the root's behaviour to the viewport, and the
    /// engine has no viewport scroller, so the outermost container stands in.
    /// Overflow-hidden containers are skipped when deciding "outermost" because
    /// they cannot scroll.
    /// </remarks>
    private bool IsSmoothScrolling(LayoutBox scroll)
    {
        if (scroll.Style.ScrollBehavior == ScrollBehavior.Smooth)
        {
            return true;
        }

        if (scroll.Style.ScrollBehavior != ScrollBehavior.Auto
            || _layoutResult?.Root is not { } root
            || root.Style.ScrollBehavior != ScrollBehavior.Smooth)
        {
            return false;
        }

        // Browsers apply a root scroll-behavior to the viewport. The engine has
        // no viewport scroller, so the outermost scrollable container stands in
        // for it (this keeps the host's :root rule effective). Hidden overflow
        // boxes are scroll containers too, but they cannot actually scroll, so
        // they do not make a nested container less "outermost" here.
        for (var parent = scroll.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.IsScrollContainer
                && (parent.ScrollHeight > parent.ClientHeight + 0.5f
                    || parent.ScrollWidth > parent.ClientWidth + 0.5f))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies a scroll offset immediately, cancelling any animation for the node.
    /// </summary>
    /// <param name="node">The scrolling node.</param>
    /// <param name="x">New horizontal offset in CSS pixels.</param>
    /// <param name="y">New vertical offset in CSS pixels.</param>
    /// <remarks>
    /// A change smaller than a thousandth of a pixel is ignored, which avoids
    /// re-building display lists for no visual difference. A real change sets both
    /// the app and inspector paint flags, because a shared scroll container can feed
    /// painters of either tree.
    /// </remarks>
    private void SetScrollOffset(IgniteSupportedDomNode node, float x, float y)
    {
        _scrollAnimations.Remove(node);

        if (MathF.Abs(node.ScrollOffsetX - x) < 0.001f
            && MathF.Abs(node.ScrollOffsetY - y) < 0.001f)
        {
            return;
        }

        node.ScrollOffsetX = x;
        node.ScrollOffsetY = y;
        _needsPaint = true;
        _needsDevToolsPaint = true;
    }

    /// <summary>
    /// Advances every smooth-scroll animation by one frame and publishes the new
    /// offsets.
    /// </summary>
    /// <param name="delta">Seconds elapsed since the previous frame.</param>
    /// <remarks>
    /// Uses a cubic ease-out. Finished animations snap exactly to their target and
    /// are removed after iteration; a sub-hundredth-of-a-pixel move is skipped to
    /// avoid needless repaints. Any actual movement sets both paint flags.
    /// </remarks>
    private void AdvanceScrollAnimations(double delta)
    {
        if (_scrollAnimations.Count == 0)
        {
            return;
        }

        List<IgniteSupportedDomNode>? finished = null;
        var changed = false;

        foreach (var (node, animation) in _scrollAnimations)
        {
            animation.Elapsed += delta;
            var progress = animation.Duration <= 0
                ? 1
                : Math.Clamp(animation.Elapsed / animation.Duration, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);

            var x = (float)(animation.FromX + (animation.ToX - animation.FromX) * eased);
            var y = (float)(animation.FromY + (animation.ToY - animation.FromY) * eased);

            if (progress >= 1)
            {
                x = animation.ToX;
                y = animation.ToY;
                (finished ??= []).Add(node);
            }

            if (MathF.Abs(node.ScrollOffsetX - x) > 0.01f
                || MathF.Abs(node.ScrollOffsetY - y) > 0.01f)
            {
                node.ScrollOffsetX = x;
                node.ScrollOffsetY = y;
                changed = true;
            }
        }

        if (finished is not null)
        {
            foreach (var node in finished)
            {
                _scrollAnimations.Remove(node);
            }
        }

        if (changed)
        {
            _needsPaint = true;
            _needsDevToolsPaint = true;
        }
    }

    /// <summary>
    /// Starts a scrollbar interaction at a point: dragging a thumb or jumping to a
    /// track position.
    /// </summary>
    /// <param name="x">Pointer X in window CSS pixels.</param>
    /// <param name="y">Pointer Y in window CSS pixels.</param>
    /// <returns>
    /// <see langword="true"/> when a scrollbar was hit and the press was consumed.
    /// </returns>
    /// <remarks>
    /// A thumb press records the pointer and scroll origins so subsequent moves are
    /// relative and do not jump; a track press centres the thumb on the pointer.
    /// Hitting a scrollbar also closes the dropdown and clears press/active state,
    /// because scrollbars are chrome rather than content.
    /// </remarks>
    private bool TryBeginScrollbarInteraction(float x, float y)
    {
        var hit = FindScrollbarAt(x, y);

        if (hit is null)
        {
            return false;
        }

        CloseSelectMenu();
        _pressed = null;
        SetActive(null);

        var box = hit.Value.Box;
        var metrics = hit.Value.Metrics;
        var node = box.Node;

        if (metrics.ThumbRect.Contains(x, y))
        {
            _scrollDrag = new ScrollbarDrag
            {
                Node = node,
                Vertical = metrics.Vertical,
                PointerStart = metrics.Vertical ? y : x,
                ScrollStart = metrics.Vertical ? node.ScrollOffsetY : node.ScrollOffsetX
            };
            return true;
        }

        var maxScroll = metrics.Vertical
            ? MathF.Max(0, box.ScrollHeight - box.ClientHeight)
            : MathF.Max(0, box.ScrollWidth - box.ClientWidth);
        var target = metrics.ScrollOffsetForPoint(
            metrics.Vertical ? y : x,
            maxScroll);

        if (metrics.Vertical)
        {
            ScrollTo(box, node.ScrollOffsetX, target);
        }
        else
        {
            ScrollTo(box, target, node.ScrollOffsetY);
        }

        return true;
    }

    /// <summary>
    /// Continues an active scrollbar thumb drag for the current pointer position.
    /// </summary>
    /// <param name="x">Pointer X in window CSS pixels.</param>
    /// <param name="y">Pointer Y in window CSS pixels.</param>
    /// <remarks>
    /// The scrollbar metrics are recomputed from the live box, so a resize or reflow
    /// mid-drag cannot desynchronize the thumb. Silently stops when the node or its
    /// scrollbar disappeared.
    /// </remarks>
    private void UpdateScrollbarDrag(float x, float y)
    {
        if (_scrollDrag is not { } drag
            || LayoutForNode(drag.Node)?.TryGetBox(drag.Node, out var box) != true)
        {
            return;
        }

        ScrollbarMetrics metrics;

        if (drag.Vertical)
        {
            if (!ScrollbarLayout.TryGetVertical(box, out metrics))
            {
                return;
            }
        }
        else if (!ScrollbarLayout.TryGetHorizontal(box, out metrics))
        {
            return;
        }

        var maxScroll = drag.Vertical
            ? MathF.Max(0, box.ScrollHeight - box.ClientHeight)
            : MathF.Max(0, box.ScrollWidth - box.ClientWidth);
        var offset = metrics.ScrollOffsetForDrag(
            drag.ScrollStart,
            (drag.Vertical ? y : x) - drag.PointerStart,
            maxScroll);

        SetScrollOffset(
            drag.Node,
            drag.Vertical ? drag.Node.ScrollOffsetX : offset,
            drag.Vertical ? offset : drag.Node.ScrollOffsetY);
    }

    /// <summary>
    /// Finds the scrollbar under a window point, checking a docked inspector first.
    /// </summary>
    /// <param name="x">Pointer X in window CSS pixels.</param>
    /// <param name="y">Pointer Y in window CSS pixels.</param>
    /// <returns>The hit box and its scrollbar metrics, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The inspector is composited above the app, so it gets first refusal. A
    /// standalone inspector window has its own input loop and is not tested here.
    /// </remarks>
    private ScrollbarHit? FindScrollbarAt(float x, float y)
    {
        if (_settings.EnableDevTools
            && _devTools.Visible
            && _devTools.Dock != DevToolsDock.Window
            && _devToolsLayout?.Root is { } devRoot)
        {
            var devHit = FindScrollbarAt(devRoot, x, y, 0, 0);

            if (devHit is not null)
            {
                return devHit;
            }
        }

        return _layoutResult?.Root is { } root
            ? FindScrollbarAt(root, x, y, 0, 0)
            : null;
    }

    /// <summary>
    /// Recursively searches a box subtree for a scrollbar track under a point.
    /// </summary>
    /// <param name="box">Subtree root.</param>
    /// <param name="x">Pointer X in window CSS pixels.</param>
    /// <param name="y">Pointer Y in window CSS pixels.</param>
    /// <param name="scrollX">Accumulated horizontal scroll offset from ancestors.</param>
    /// <param name="scrollY">Accumulated vertical scroll offset from ancestors.</param>
    /// <returns>The deepest matching scrollbar, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Children are visited in reverse paint order so the topmost scrollbar wins.
    /// A container's own scrollbar is tested only after its children, since content
    /// can overlap the track. Scroll offsets are accumulated exactly as the painter
    /// applies them.
    /// </remarks>
    private static ScrollbarHit? FindScrollbarAt(
        LayoutBox box,
        float x,
        float y,
        float scrollX,
        float scrollY)
    {
        if (box.Style.Display == Display.None
            || box.Style.Visibility is Visibility.Hidden or Visibility.Collapse)
        {
            return null;
        }

        var painted = box.BorderBox.Translate(-scrollX, -scrollY);

        if (!painted.Contains(x, y))
        {
            return null;
        }

        var childScrollX = scrollX;
        var childScrollY = scrollY;

        if (box.IsScrollContainer)
        {
            var clip = box.PaddingBox.Translate(-scrollX, -scrollY);

            if (!clip.Contains(x, y))
            {
                return null;
            }

            childScrollX += box.ScrollOffsetX;
            childScrollY += box.ScrollOffsetY;
        }

        for (var i = box.Children.Count - 1; i >= 0; i--)
        {
            var hit = FindScrollbarAt(box.Children[i], x, y, childScrollX, childScrollY);

            if (hit is not null)
            {
                return hit;
            }
        }

        if (!box.IsScrollContainer)
        {
            return null;
        }

        if (ScrollbarLayout.TryGetVertical(box, out var vertical))
        {
            var track = vertical.Track.Translate(-scrollX, -scrollY);

            if (track.Contains(x, y))
            {
                return new ScrollbarHit(box, vertical with { Track = track });
            }
        }

        if (ScrollbarLayout.TryGetHorizontal(box, out var horizontal))
        {
            var track = horizontal.Track.Translate(-scrollX, -scrollY);

            if (track.Contains(x, y))
            {
                return new ScrollbarHit(box, horizontal with { Track = track });
            }
        }

        return null;
    }

    /// <summary>
    /// Routes a key press: window shortcuts first, then <c>select</c> navigation,
    /// then text editing keys on the focused element.
    /// </summary>
    /// <param name="keyboard">
    /// The keyboard that reported the event, read for modifiers and clipboard.
    /// </param>
    /// <param name="key">The pressed key.</param>
    /// <remarks>
    /// <para>Order of handling:</para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>Ctrl+Shift+I</c> and <c>F12</c> toggle devtools when enabled.
    /// </description></item>
    /// <item><description>
    /// <c>Ctrl+C</c> and <c>Ctrl+X</c> run selection commands and consume the key
    /// only when they actually had something to act on; <c>Ctrl+A</c> always
    /// selects all text.
    /// </description></item>
    /// <item><description>
    /// With no focused element, nothing further happens.
    /// </description></item>
    /// <item><description>
    /// <c>Ctrl+V</c> on a text control prefers a <c>paste</c> handler, otherwise
    /// normalizes the clipboard text and inserts it.
    /// </description></item>
    /// <item><description>
    /// A focused <c>select</c> handles arrows, Enter/Space and Escape to navigate
    /// its dropdown.
    /// </description></item>
    /// <item><description>
    /// Backspace, Delete, arrows, Home/End, Enter, Escape and Tab edit text, commit
    /// or move focus, with Ctrl switching between character and word steps and
    /// Shift extending the selection.
    /// </description></item>
    /// </list>
    /// <para>
    /// Enter commits the working text, dispatches <c>change</c> and relayouts;
    /// Escape drops focus without committing. In the inspector, Enter on a value
    /// field also advances to the next row via
    /// <see cref="DevToolsState.AdvanceAfterCommit"/>.
    /// </para>
    /// </remarks>
    private void OnKeyDown(IKeyboard keyboard, Key key)
    {
        var control = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
        var shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);

        if (_settings.EnableDevTools
            && ((key == Key.I && control && shift) || key == Key.F12))
        {
            ToggleDevTools();
            return;
        }

        if (control && key == Key.C && CopySelection(keyboard))
        {
            return;
        }

        if (control && key == Key.X && CutSelection(keyboard))
        {
            return;
        }

        if (control && key == Key.A)
        {
            SelectAllText();
            return;
        }

        if (_focused is null)
        {
            return;
        }

        if (key == Key.V && control && IsTextControl(_focused))
        {
            var text = keyboard.ClipboardText;

            if (!string.IsNullOrEmpty(text))
            {
                if (_focused.TryGetEventHandler("paste", out var pasteHandler)
                    && pasteHandler is not null)
                {
                    Invoke(_focused, "paste", text);
                    return;
                }

                text = NormalizeEditText(_focused, text);
                DeleteControlSelectionCore();
                _editText ??= string.Empty;
                _caretIndex = Math.Clamp(_caretIndex, 0, _editText.Length);
                _editText = _editText.Insert(_caretIndex, text);
                _caretIndex += text.Length;
                ResetCaretBlink();
                CascadeEdit(_focused);
                Invoke(_focused, "input", _editText);
                return;
            }
        }

        if (_focused.Tag is "select")
        {
            var options = FindOptions(_focused);

            switch (key)
            {
                case Key.Down:
                case Key.Up:
                    if (options.Count > 0)
                    {
                        if (_openSelect is null)
                        {
                            OpenSelectMenu(_focused);
                        }
                        else
                        {
                            var step = key == Key.Down ? 1 : -1;
                            _openSelectHover = (_openSelectHover + step + options.Count) % options.Count;
                        }
                    }

                    return;

                case Key.Enter:
                case Key.Space:
                    if (options.Count > 0)
                    {
                        if (_openSelect is null)
                        {
                            OpenSelectMenu(_focused);
                        }
                        else
                        {
                            SelectOption(_focused, _openSelectHover);
                            CloseSelectMenu();
                        }
                    }

                    return;

                case Key.Escape:
                    if (_openSelect is not null)
                    {
                        CloseSelectMenu();
                        return;
                    }

                    break;
            }
        }

        switch (key)
        {
            case Key.Backspace:
                if (HasControlSelection)
                {
                    if (DeleteControlSelectionCore())
                    {
                        AfterControlEdit();
                    }
                }
                else if (_editText is { Length: > 0 } && _caretIndex > 0)
                {
                    var start = control && IsTextControl(_focused)
                        ? PreviousWord(_editText, _caretIndex)
                        : PreviousTextElement(_editText, _caretIndex);
                    _editText = _editText.Remove(start, _caretIndex - start);
                    _caretIndex = start;
                    ResetCaretBlink();
                    CascadeEdit(_focused);
                    Invoke(_focused, "input", _editText ?? string.Empty);
                }

                break;

            case Key.Delete:
                if (HasControlSelection)
                {
                    if (DeleteControlSelectionCore())
                    {
                        AfterControlEdit();
                    }
                }
                else if (_editText is { Length: > 0 } && _caretIndex < _editText.Length)
                {
                    var end = control && IsTextControl(_focused)
                        ? NextWord(_editText, _caretIndex)
                        : NextTextElement(_editText, _caretIndex);
                    _editText = _editText.Remove(_caretIndex, end - _caretIndex);
                    ResetCaretBlink();
                    CascadeEdit(_focused);
                    Invoke(_focused, "input", _editText ?? string.Empty);
                }

                break;

            case Key.Left:
            case Key.Right:
                if (IsTextControl(_focused) && _editText is { } editText)
                {
                    var current = Math.Clamp(_caretIndex, 0, editText.Length);
                    var target = key == Key.Left
                        ? control
                            ? PreviousWord(editText, current)
                            : PreviousTextElement(editText, current)
                        : control
                            ? NextWord(editText, current)
                            : NextTextElement(editText, current);

                    if (shift)
                    {
                        ExtendControlSelection(target);
                    }
                    else if (HasControlSelection)
                    {
                        CollapseControlSelection(toStart: key == Key.Left);
                    }
                    else
                    {
                        _caretIndex = target;
                        ResetCaretBlink();
                    }
                }
                else if (_editText is { Length: > 0 })
                {
                    _caretIndex = key == Key.Left
                        ? PreviousTextElement(_editText, _caretIndex)
                        : NextTextElement(_editText, _caretIndex);

                    if (!shift)
                    {
                        ClearSelection();
                    }

                    ResetCaretBlink();
                }
                else if (!shift)
                {
                    ClearSelection();
                }

                break;

            case Key.Home:
                if (IsTextControl(_focused) && shift)
                {
                    ExtendControlSelection(0);
                }
                else
                {
                    _caretIndex = 0;
                    ClearSelection();
                    ResetCaretBlink();
                }

                break;

            case Key.End:
                if (IsTextControl(_focused) && shift)
                {
                    ExtendControlSelection(_editText?.Length ?? 0);
                }
                else
                {
                    _caretIndex = _editText?.Length ?? 0;
                    ClearSelection();
                    ResetCaretBlink();
                }

                break;

            case Key.Enter:
                if (IsInDevToolsTree(_focused) && _focused.HasClass("dt-value-input"))
                {
                    _devTools.AdvanceAfterCommit = true;
                }

                ClearSelection();
                CommitEdit(_focused);
                Invoke(_focused, "change", _editText ?? string.Empty);
                _needsLayout = true;
                break;

            case Key.Escape:
                Focus(null);
                break;

            case Key.Tab:
                FocusNext();
                break;
        }
    }

    /// <summary>
    /// Moves one text element backwards, keeping surrogate pairs together.
    /// </summary>
    /// <param name="text">The text being edited.</param>
    /// <param name="index">Current caret offset in UTF-16 code units.</param>
    /// <returns>The previous caret offset, or <c>0</c> at the start.</returns>
    /// <remarks>
    /// Only surrogate pairing is handled; combining marks and grapheme clusters are
    /// not grouped.
    /// </remarks>
    private static int PreviousTextElement(string text, int index)
    {
        if (index <= 0)
        {
            return 0;
        }

        index--;

        if (index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]))
        {
            index--;
        }

        return index;
    }

    /// <summary>
    /// Moves one text element forwards, keeping surrogate pairs together.
    /// </summary>
    /// <param name="text">The text being edited.</param>
    /// <param name="index">Current caret offset in UTF-16 code units.</param>
    /// <returns>The next caret offset, or <c>text.Length</c> at the end.</returns>
    /// <remarks>
    /// Mirrors <see cref="PreviousTextElement"/>; grapheme clusters are not grouped.
    /// </remarks>
    private static int NextTextElement(string text, int index)
    {
        if (index >= text.Length)
        {
            return text.Length;
        }

        index++;

        if (index < text.Length && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]))
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// Inserts a typed character into the focused control, replacing any control
    /// selection, then dispatches <c>input</c>.
    /// </summary>
    /// <param name="character">The character produced by the platform keyboard layout.</param>
    /// <remarks>
    /// <para>
    /// Control characters are ignored (Enter, Tab and Escape arrive separately), as
    /// are disabled elements and element types that do not accept text: select,
    /// button, anchor, checkbox, radio and range.
    /// </para>
    /// <para>
    /// In the inspector, <c>:</c> on a property-name field moves focus to the next
    /// field and <c>;</c> on a value field commits and advances, matching the
    /// spreadsheet-style entry the panel is designed around.
    /// </para>
    /// <para>
    /// Any non-control text selection is cleared before inserting, so typing over a
    /// selection behaves as expected.
    /// </para>
    /// </remarks>
    private void OnKeyChar(char character)
    {
        if (_focused is null || char.IsControl(character))
        {
            return;
        }

        if (_focused.TryGetAttribute("disabled", out var ignored))
        {
            return;
        }

        if (_focused.Tag is "select" or "button" or "a")
        {
            return;
        }

        var type = GetAttribute(_focused, "type");

        if (type is "checkbox" or "radio" or "range")
        {
            return;
        }

        if (IsInDevToolsTree(_focused))
        {
            if (character == ':' && _focused.HasClass("dt-prop-input"))
            {
                FocusNext();
                return;
            }

            if (character == ';' && _focused.HasClass("dt-value-input"))
            {
                _devTools.AdvanceAfterCommit = true;
                CommitEdit(_focused);
                Invoke(_focused, "change", _editText ?? string.Empty);
                _needsLayout = true;
                return;
            }
        }

        if (_selection is { IsControl: false })
        {
            ClearSelection();
        }

        DeleteControlSelectionCore();
        _editText ??= string.Empty;
        _caretIndex = Math.Clamp(_caretIndex, 0, _editText.Length);
        _editText = _editText.Insert(_caretIndex, character.ToString());
        _caretIndex++;
        ResetCaretBlink();
        CascadeEdit(_focused);
        Invoke(_focused, "input", _editText ?? string.Empty);
    }

    /// <summary>
    /// Restarts the caret blink so the caret is visible immediately after an edit or
    /// focus change.
    /// </summary>
    private void ResetCaretBlink()
    {
        _caretBlinkStart = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Gets whether the caret is in the visible half of its one-second blink cycle.
    /// </summary>
    /// <value>
    /// <see langword="true"/> for the first 500 ms after <see cref="ResetCaretBlink"/>,
    /// <see langword="false"/> for the next 500 ms.
    /// </value>
    /// <remarks>
    /// The phase is derived from a timestamp rather than a frame counter, so the
    /// blink rate is independent of the refresh rate and of skipped frames.
    /// </remarks>
    private bool IsCaretVisible
    {
        get
        {
            var elapsed = (Stopwatch.GetTimestamp() - _caretBlinkStart) / (double)Stopwatch.Frequency;
            return elapsed % 1.0 < 0.5;
        }
    }

    /// <summary>
    /// Mirrors the working edit text into the DOM on every keystroke so both CSS
    /// selectors (<c>:placeholder-shown</c>, <c>value</c>) and layout see it.
    /// </summary>
    /// <param name="element">The focused control being edited.</param>
    /// <remarks>
    /// A <c>textarea</c> stores the text as its <c>TextContent</c>, an <c>input</c>
    /// stores it in the interned <c>value</c> attribute. The layout is invalidated
    /// because the control's intrinsic width and text run change; the inspector is
    /// invalidated only when the element belongs to its tree.
    /// </remarks>
    private void CascadeEdit(ElementNode element)
    {
        if (string.Equals(element.Tag, "textarea", StringComparison.OrdinalIgnoreCase))
        {
            element.TextContent = _editText;
        }
        else
        {
            element.SetAttribute(ValueAttribute, InternTable.Intern(_editText ?? string.Empty));
        }

        _needsLayout = true;

        if (IsInDevToolsTree(element))
        {
            _lastDevToolsVersion = -1;
        }
    }

    /// <summary>
    /// Writes the working edit text into the DOM when an edit is accepted (Enter or
    /// focus loss).
    /// </summary>
    /// <param name="element">The control whose edit is being committed.</param>
    /// <remarks>
    /// Semantically separate from <see cref="CascadeEdit"/> but currently performs
    /// the same attributes update and invalidation; it exists so the commit points
    /// read clearly and can diverge from per-keystroke mirroring later.
    /// </remarks>
    private void CommitEdit(ElementNode element)
    {
        if (string.Equals(element.Tag, "textarea", StringComparison.OrdinalIgnoreCase))
        {
            element.TextContent = _editText;
        }
        else
        {
            element.SetAttribute(ValueAttribute, InternTable.Intern(_editText ?? string.Empty));
        }

        _needsLayout = true;

        if (IsInDevToolsTree(element))
        {
            _lastDevToolsVersion = -1;
        }
    }

    /// <summary>
    /// Moves keyboard focus to an element, or clears focus when passed
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="element">The element to focus, or <see langword="null"/> to blur.</param>
    /// <param name="focusVisible">
    /// Whether the focus should use the <c>:focus-visible</c> style. Text controls
    /// are always treated as visible because a caret is drawn.
    /// </param>
    /// <param name="preserveSelection">
    /// Keep the current text selection across the focus change, used by shift-click
    /// so extending a selection does not collapse it.
    /// </param>
    /// <remarks>
    /// <para>
    /// Focusing a disabled element is ignored and leaves the current focus intact.
    /// An open dropdown closes when focus moves to a different element.
    /// </para>
    /// <para>
    /// Leaving a control commits its working text first, dispatches <c>change</c>
    /// when the control was an input or textarea, then dispatches <c>blur</c>.
    /// Entering a control loads its current value into the working text and puts the
    /// caret at the end, then dispatches <c>focus</c>.
    /// </para>
    /// <para>
    /// The hover flag <c>:focus-visible</c> is only written when it actually
    /// changes, and the inspector's layout is invalidated so its own focus rings
    /// update even though it is a separate tree.
    /// </para>
    /// </remarks>
    private void Focus(ElementNode? element, bool focusVisible = false, bool preserveSelection = false)
    {
        if (element is not null && element.TryGetAttribute("disabled", out var ignoredDisabled))
        {
            return;
        }

        if (_openSelect is not null && !ReferenceEquals(_openSelect, element))
        {
            CloseSelectMenu();
        }

        var visible = focusVisible
            || element is not null && element.Tag is "input" or "textarea";

        if (ReferenceEquals(_focused, element))
        {
            if (element is not null && element.IsFocusVisible != visible)
            {
                element.IsFocusVisible = visible;
                _needsLayout = true;
                _lastDevToolsVersion = -1;
            }

            return;
        }

        if (!preserveSelection)
        {
            ClearSelection();
        }

        if (_focused is not null)
        {
            _focused.IsFocused = false;
            _focused.IsFocusVisible = false;

            if (_focused.Tag is "input" or "textarea")
            {
                CommitEdit(_focused);
                Invoke(_focused, "change", _editText ?? string.Empty);
            }

            Invoke(_focused, "blur");
        }

        _focused = element;

        if (element is not null)
        {
            element.IsFocused = true;
            element.IsFocusVisible = visible;
            _editText = NormalizeEditText(
                element,
                GetAttribute(element, "value") ?? element.TextContent ?? string.Empty);
            _caretIndex = _editText.Length;
            ResetCaretBlink();

            if (element.TryGetEventHandler("focus", out var handler) && handler is Action action)
            {
                action();
            }
        }
        else
        {
            _editText = null;
            _caretIndex = 0;
        }

        _needsLayout = true;
        _lastDevToolsVersion = -1;
    }

    /// <summary>
    /// Moves focus to the next focusable element in document order, wrapping at the
    /// end.
    /// </summary>
    /// <remarks>
    /// The candidate list is rebuilt on every Tab press by walking the app tree, or
    /// the inspector tree when focus is inside it, so focus roving never escapes
    /// its own document. The new element is focused with
    /// <c>focusVisible: true</c>. Disabled elements are not filtered out of the
    /// list, so a disabled control can consume one Tab stop without taking focus.
    /// </remarks>
    private void FocusNext()
    {
        var focusable = new List<ElementNode>();
        var root = _focused is not null
            && _devToolsRoot is not null
            && IsInDevToolsTree(_focused)
            ? _devToolsRoot
            : _root;
        CollectFocusable(root, focusable);

        if (focusable.Count == 0)
        {
            return;
        }

        var index = _focused is null ? -1 : focusable.IndexOf(_focused);
        Focus(focusable[(index + 1) % focusable.Count], focusVisible: true);
    }

    /// <summary>Reports whether a node is the inspector root or a descendant of it.</summary>
    /// <param name="node">Node to classify; may be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for the inspector subtree.</returns>
    /// <remarks>
    /// The check walks parent references, so it stays O(depth) and never depends on
    /// the node still having a layout box.
    /// </remarks>
    private bool IsInDevToolsTree(IgniteSupportedDomNode? node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, _devToolsRoot))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Appends every focusable element in document order: inputs, textareas,
    /// selects, buttons and anchors.
    /// </summary>
    /// <param name="node">Subtree root; may be <see langword="null"/>.</param>
    /// <param name="result">Accumulator; not cleared.</param>
    /// <remarks>
    /// Recurses through non-element nodes (text, fragments) so focusables nested in
    /// them are still found. Tab order is plain DOM order; there is no
    /// <c>tabindex</c> support and no radio-group skipping.
    /// </remarks>
    private static void CollectFocusable(Node? node, List<ElementNode> result)
    {
        if (node is not ElementNode element)
        {
            if (node is not null)
            {
                var children = node.Children;

                for (var i = 0; i < children.Length; i++)
                {
                    if (children[i] is Node child)
                    {
                        CollectFocusable(child, result);
                    }
                }
            }

            return;
        }

        if (element.Tag is "input" or "textarea" or "select" or "button" or "a")
        {
            result.Add(element);
        }

        var elementChildren = element.Children;

        for (var i = 0; i < elementChildren.Length; i++)
        {
            if (elementChildren[i] is Node child)
            {
                CollectFocusable(child, result);
            }
        }
    }

    /// <summary>
    /// Chooses the pointer cursor for the element under the pointer.
    /// </summary>
    /// <param name="element">The hovered element, or <see langword="null"/>.</param>
    /// <remarks>
    /// Inspector splitters resize; text controls and elements with selectable text
    /// show an I-beam; everything else uses the arrow. CSS <c>cursor</c> is not
    /// modelled.
    /// </remarks>
    private void UpdateCursor(ElementNode? element)
    {
        if (element is not null && element.HasClass("dt-splitter"))
        {
            SetCursor(element.HasClass("dt-splitter-bottom")
                ? StandardCursor.VResize
                : StandardCursor.HResize);
        }
        else if (element is not null
            && (IsTextControl(element) || HasSelectableText(element)))
        {
            SetCursor(StandardCursor.IBeam);
        }
        else
        {
            SetCursor(StandardCursor.Arrow);
        }
    }

    /// <summary>Applies a standard cursor to every attached mouse, if it changed.</summary>
    /// <param name="cursor">The cursor shape to show.</param>
    /// <remarks>
    /// The last shape is cached so pointer moves over the same element do not issue
    /// redundant platform calls. Backends that reject cursor changes are ignored:
    /// a missing hand cursor must never break input.
    /// </remarks>
    private void SetCursor(StandardCursor cursor)
    {
        if (_cursorKind == cursor)
        {
            return;
        }

        _cursorKind = cursor;

        if (_input is null)
        {
            return;
        }

        foreach (var mouse in _input.Mice)
        {
            try
            {
                var pointer = mouse.Cursor;

                if (pointer is null)
                {
                    continue;
                }

                pointer.Type = CursorType.Standard;
                pointer.StandardCursor = cursor;
            }
            catch
            {
                // Cursor changes are best-effort per backend.
            }
        }
    }

    /// <summary>
    /// Updates the hovered element, propagating <c>IsHovered</c> to its ancestors
    /// and dispatching <c>mouseout</c>/<c>mouseover</c>.
    /// </summary>
    /// <param name="element">The new hovered element, or <see langword="null"/>.</param>
    /// <remarks>
    /// Hover is stored on the whole ancestor chain because CSS <c>:hover</c> matches
    /// every ancestor of the pointed element, not just the leaf. A no-op move keeps
    /// the previous chain. Both events are dispatched through
    /// <see cref="Invoke"/>, which walks up to the
    /// nearest handler.
    /// </remarks>
    private void SetHovered(ElementNode? element)
    {
        if (ReferenceEquals(_hovered, element))
        {
            return;
        }

        if (_hovered is not null)
        {
            Invoke(_hovered, "mouseout");
        }

        SetChainState(_hoverChain, element, static node => node.IsHovered = true,
            static node => node.IsHovered = false);

        _hovered = element;

        if (element is not null)
        {
            Invoke(element, "mouseover");
        }

        _needsLayout = true;
    }

    /// <summary>
    /// Updates the pressed element, propagating <c>IsActive</c> to its ancestors.
    /// </summary>
    /// <param name="element">
    /// The element to mark active, or <see langword="null"/> to clear.
    /// </param>
    /// <remarks>
    /// Like hover, active state is applied up the ancestor chain so <c>:active</c>
    /// matches parents. Clearing is skipped when nothing was active, which keeps
    /// every mouse-up from forcing a layout. The inspector's layout is invalidated
    /// when either the old or the new chain belongs to it.
    /// </remarks>
    private void SetActive(ElementNode? element)
    {
        if (element is null && _activeChain.Count == 0)
        {
            return;
        }

        var wasDevTools = _activeChain.Count > 0 && IsInDevToolsTree(_activeChain[0]);
        SetChainState(_activeChain, element, static node => node.IsActive = true,
            static node => node.IsActive = false);

        if (wasDevTools || element is not null && IsInDevToolsTree(element))
        {
            _lastDevToolsVersion = -1;
        }

        _needsLayout = true;
    }

    /// <summary>
    /// Walks from a hit element up to the nearest ancestor that handles a
    /// two-coordinate pointer event.
    /// </summary>
    /// <param name="element">The hit element, or <see langword="null"/>.</param>
    /// <param name="eventName">The handler key, such as <c>pointerdown</c>.</param>
    /// <returns>The capturing element, or <see langword="null"/> when nobody handles it.</returns>
    /// <remarks>
    /// Only <c>Action&lt;float, float&gt;</c> handlers are accepted;
    /// other delegate shapes are treated as no handler and are not invoked.
    /// </remarks>
    private static ElementNode? FindPointerTarget(ElementNode? element, string eventName)
    {
        for (var node = (IgniteSupportedDomNode?)element; node is not null; node = node.Parent)
        {
            if (node is ElementNode current
                && current.TryGetEventHandler(eventName, out var handler)
                && handler is Action<float, float>)
            {
                return current;
            }
        }

        return null;
    }

    /// <summary>
    /// Dispatches a two-coordinate pointer event with coordinates relative to the
    /// target's painted border box.
    /// </summary>
    /// <param name="target">The element that captured the pointer.</param>
    /// <param name="eventName">The handler key, such as <c>pointermove</c>.</param>
    /// <param name="x">Pointer X in window CSS pixels.</param>
    /// <param name="y">Pointer Y in window CSS pixels.</param>
    /// <remarks>
    /// Local coordinates make handlers independent of scrolling and of where the
    /// element happens to sit. Nothing is dispatched when the target has no box in
    /// its layout result.
    /// </remarks>
    private void InvokePointer(ElementNode target, string eventName, float x, float y)
    {
        if (!target.TryGetEventHandler(eventName, out var handler)
            || handler is not Action<float, float> action)
        {
            return;
        }

        var layout = LayoutFor(target);

        if (layout is null || !layout.TryGetBox(target, out var box))
        {
            return;
        }

        var painted = PaintedBorderBox(box);
        action(x - painted.X, y - painted.Y);
    }

    /// <summary>
    /// Selects an app element from an inspect-mode pick, expands the tree to it and
    /// requests that it be scrolled into view.
    /// </summary>
    /// <param name="element">The picked application element.</param>
    /// <remarks>
    /// Turns inspect mode off after a pick, clears the hover highlight and forces
    /// the inspector to rebuild. <see cref="DevToolsState.ScrollSelectedIntoView"/>
    /// is consumed by the next frame, which calls
    /// <see cref="ScrollDevToolsSelectionIntoView"/>.
    /// </remarks>
    private void SelectInspectElement(ElementNode element)
    {
        _devTools.Selected = element;
        _devTools.Hovered = null;
        _devTools.InspectMode = false;
        InspectorPanel.ExpandPathTo(_devTools, element);

        _devTools.Refresh();
        _lastDevToolsVersion = -1;
        _needsLayout = true;
        _devTools.ScrollSelectedIntoView = true;
    }

    /// <summary>
    /// Scrolls the inspector's DOM tree pane so the selected row is centred.
    /// </summary>
    /// <remarks>
    /// Finds the row by its <c>dt-tree-selected</c> class, walks up to the nearest
    /// vertical scroll container and sets its offset directly, clamped to its range.
    /// The offset is applied immediately without smooth scrolling because this is a
    /// programmatic reveal. Uses the inspector layout when docked and the app layout
    /// when the inspector is composed into the app.
    /// </remarks>
    private void ScrollDevToolsSelectionIntoView()
    {
        var root = _devToolsRoot ?? _root;
        var layout = _devToolsLayout ?? _layoutResult;

        if (root is null || layout is null)
        {
            return;
        }

        var row = FindByClass(root, "dt-tree-selected");

        if (row is null || !layout.TryGetBox(row, out var rowBox))
        {
            return;
        }

        var pane = rowBox.Parent;

        while (pane is not null
            && !(pane.IsScrollContainer
                && pane.Style.OverflowY is Overflow.Auto or Overflow.Scroll))
        {
            pane = pane.Parent;
        }

        if (pane is null)
        {
            return;
        }

        var max = MathF.Max(0, pane.ScrollHeight - pane.ClientHeight);
        var target = rowBox.BorderBox.Y
            - pane.ContentBox.Y
            - (pane.ClientHeight - rowBox.BorderBox.Height) / 2f;
        pane.Node.ScrollOffsetY = Math.Clamp(target, 0, max);

        if (_devToolsLayout is not null)
        {
            _needsDevToolsPaint = true;
        }
        else
        {
            _needsPaint = true;
        }
    }

    /// <summary>
    /// Replaces a cached ancestor chain and applies the new state to every element
    /// in it.
    /// </summary>
    /// <param name="chain">The list holding the previous chain; cleared and refilled.</param>
    /// <param name="element">The leaf element, or <see langword="null"/> to clear only.</param>
    /// <param name="apply">Sets the state on one element.</param>
    /// <param name="clear">Clears the state on one element.</param>
    /// <remarks>
    /// The chain always runs leaf-first up to the root element. Every old entry is
    /// cleared before the new chain is built, so a move between two branches cannot
    /// leave a stale hover/active flag behind.
    /// </remarks>
    private static void SetChainState(
        List<ElementNode> chain,
        ElementNode? element,
        Action<ElementNode> apply,
        Action<ElementNode> clear)
    {
        for (var i = 0; i < chain.Count; i++)
        {
            clear(chain[i]);
        }

        chain.Clear();

        var node = element;

        while (node is not null)
        {
            chain.Add(node);
            apply(node);
            node = node.Parent as ElementNode;
        }
    }

    /// <summary>Finds the element under a window point.</summary>
    /// <param name="x">Pointer X in window CSS pixels.</param>
    /// <param name="y">Pointer Y in window CSS pixels.</param>
    /// <returns>
    /// The topmost element at the point, or <see langword="null"/> when the point is
    /// outside every box.
    /// </returns>
    /// <remarks>
    /// Text and other non-element boxes resolve to their nearest element ancestor.
    /// </remarks>
    private ElementNode? HitTest(float x, float y)
    {
        var box = HitTestBox(x, y);
        var node = box?.Node;
        return node as ElementNode ?? FindElement(node);
    }

    /// <summary>Finds the box under a window point, giving a docked inspector priority.</summary>
    /// <param name="x">Pointer X in window CSS pixels.</param>
    /// <param name="y">Pointer Y in window CSS pixels.</param>
    /// <returns>The topmost box, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The inspector is composited above the app but lives in a separate box tree, so
    /// it is tested first. A hit on the inspector's root is treated as a miss: the
    /// root covers the whole window and would otherwise swallow every app click.
    /// </remarks>
    private LayoutBox? HitTestBox(float x, float y)
    {
        // The inspector is composited above the app but lives in its own box
        // tree, so it gets first refusal on pointer input.
        if (_settings.EnableDevTools
            && _devTools.Visible
            && _devTools.Dock != DevToolsDock.Window
            && _devToolsLayout?.Root is { } devRoot)
        {
            var devHit = HitTestRoot(devRoot, x, y);

            if (devHit is not null && !ReferenceEquals(devHit, devRoot))
            {
                return devHit;
            }
        }

        if (_layoutResult?.Root is not { } root)
        {
            return null;
        }

        return HitTestRoot(root, x, y);
    }

    /// <summary>
    /// Tests a point against a root box after first testing <c>position: fixed</c>
    /// boxes.
    /// </summary>
    /// <param name="root">The root of the box tree to test.</param>
    /// <param name="x">Pointer X in window CSS pixels.</param>
    /// <param name="y">Pointer Y in window CSS pixels.</param>
    /// <returns>The topmost hit box, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Fixed boxes paint after the normal tree, so they are tested first, in reverse
    /// paint order, and with zero scroll offset so an ancestor's scroll cannot move
    /// them. The normal tree is then walked with fixed boxes skipped.
    /// </remarks>
    private LayoutBox? HitTestRoot(LayoutBox root, float x, float y)
    {
        _hitFixedBoxes.Clear();
        CollectFixedBoxes(root, _hitFixedBoxes);

        // Fixed boxes are painted after the normal tree, so they are tested
        // first and in reverse order. Starting at the fixed box with zero
        // scroll also keeps ancestor scroll offsets out of the hit test.
        for (var i = _hitFixedBoxes.Count - 1; i >= 0; i--)
        {
            var hit = HitTestBox(_hitFixedBoxes[i], x, y, 0, 0, skipFixed: false);

            if (hit is not null)
            {
                return hit;
            }
        }

        return HitTestBox(root, x, y, 0, 0, skipFixed: true);
    }

    /// <summary>
    /// Collects fixed-position boxes in paint order, skipping invisible subtrees.
    /// </summary>
    /// <param name="box">Subtree root.</param>
    /// <param name="boxes">Accumulator; not cleared.</param>
    /// <remarks>Hidden and <c>display: none</c> subtrees contribute no fixed boxes.</remarks>
    private static void CollectFixedBoxes(LayoutBox box, List<LayoutBox> boxes)
    {
        if (box.Style.Display == Display.None
            || box.Style.Visibility is Visibility.Hidden or Visibility.Collapse)
        {
            return;
        }

        if (box.IsFixed)
        {
            boxes.Add(box);
        }

        for (var i = 0; i < box.Children.Count; i++)
        {
            CollectFixedBoxes(box.Children[i], boxes);
        }
    }

    /// <summary>
    /// Recursively hit-tests a box subtree in reverse paint order.
    /// </summary>
    /// <param name="box">Subtree root.</param>
    /// <param name="x">Pointer X in window CSS pixels.</param>
    /// <param name="y">Pointer Y in window CSS pixels.</param>
    /// <param name="scrollX">Accumulated horizontal scroll offset from ancestors.</param>
    /// <param name="scrollY">Accumulated vertical scroll offset from ancestors.</param>
    /// <param name="skipFixed">Skip fixed-position boxes, which are tested separately.</param>
    /// <returns>The deepest hit box, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// Layout boxes keep unscrolled coordinates, so every containment test applies the
    /// accumulated ancestor scroll offset. Scroll containers additionally clip their
    /// children to the padding box and add their own offset before recursing.
    /// </para>
    /// <para>
    /// A <c>display: contents</c> box has no geometry of its own: it is never
    /// rejected for an empty border box, and it is never returned, so it cannot
    /// swallow a point meant for a sibling.
    /// </para>
    /// </remarks>
    private static LayoutBox? HitTestBox(
        LayoutBox box,
        float x,
        float y,
        float scrollX,
        float scrollY,
        bool skipFixed)
    {
        if (box.Style.Display == Display.None || box.Style.Visibility is Visibility.Hidden or Visibility.Collapse)
        {
            return null;
        }

        if (skipFixed && box.IsFixed)
        {
            return null;
        }

        // Layout boxes keep unscrolled absolute coordinates. The display list
        // translates a scroll container's descendants by -ScrollOffset, so hit
        // testing has to apply the same accumulated translation.
        var painted = box.BorderBox.Translate(-scrollX, -scrollY);

        // A display:contents box has no box of its own; its children are laid
        // out in the parent. Never reject a point against its (often empty)
        // border box or the children can never be hit.
        var isContents = box.IsContents || box.Style.Display == Display.Contents;

        // Clipping containers reject points outside their padding box before
        // their subtree is considered. Everything else keeps descending even
        // when the point lies outside the box: an absolutely positioned
        // dropdown menu, for example, paints outside the cell that placed it
        // and must still receive pointer input.
        if (!isContents && box.IsScrollContainer)
        {
            var clip = box.PaddingBox.Translate(-scrollX, -scrollY);

            if (!clip.Contains(x, y))
            {
                return null;
            }

            scrollX += box.ScrollOffsetX;
            scrollY += box.ScrollOffsetY;
        }

        var inside = isContents || painted.Contains(x, y);

        for (var i = box.Children.Count - 1; i >= 0; i--)
        {
            var hit = HitTestBox(box.Children[i], x, y, scrollX, scrollY, skipFixed);

            if (hit is not null)
            {
                return hit;
            }
        }

        // Only the box's own border box makes the box itself a hit. A contents
        // box has no geometry of its own. Returning it would make it swallow
        // points that belong to a later sibling (for example the disclosure
        // triangle next to a tree row's content fragment).
        return inside && !isContents ? box : null;
    }

    /// <summary>Climbs from a node to the nearest element ancestor, including itself.</summary>
    /// <param name="node">Starting node; may be <see langword="null"/>.</param>
    /// <returns>The nearest <see cref="ElementNode"/>, or <see langword="null"/>.</returns>
    private static ElementNode? FindElement(IgniteSupportedDomNode? node)
    {
        while (node is not null)
        {
            if (node is ElementNode element)
            {
                return element;
            }

            node = node.Parent;
        }

        return null;
    }

    /// <summary>
    /// Walks up from a hit box to the container that should consume a wheel event.
    /// </summary>
    /// <param name="box">The box under the pointer, or <see langword="null"/>.</param>
    /// <param name="vertical">Vertical wheel delta; zero when the wheel is horizontal only.</param>
    /// <param name="horizontal">
    /// Horizontal wheel delta; zero when the wheel is vertical only.
    /// </param>
    /// <returns>The best scroll container, or <see langword="null"/> for none.</returns>
    /// <remarks>
    /// A container qualifies only when the CSS overflow allows user scrolling on the
    /// requested axis and it actually overflows; this keeps <c>overflow: hidden</c>
    /// boxes, such as a text input clipping its text, from swallowing the wheel. If
    /// no ancestor overflows on the requested axis, the nearest user-scrollable
    /// ancestor is returned so scrolling still lands somewhere sensible.
    /// </remarks>
    private static LayoutBox? FindScrollContainer(LayoutBox? box, float vertical, float horizontal)
    {
        var firstScrollContainer = (LayoutBox?)null;

        while (box is not null)
        {
            if (box.IsScrollContainer)
            {
                // overflow:hidden/clip creates a scroll container for clipping
                // but the user cannot scroll it. Skipping those keeps a text
                // input (which clips its text) from swallowing the wheel.
                var userScrollY = box.Style.OverflowY is Overflow.Auto or Overflow.Scroll;
                var userScrollX = box.Style.OverflowX is Overflow.Auto or Overflow.Scroll;

                if (userScrollX || userScrollY)
                {
                    firstScrollContainer ??= box;

                    if ((vertical != 0 && userScrollY && box.ScrollHeight > box.ClientHeight + 0.5f)
                        || (horizontal != 0 && userScrollX && box.ScrollWidth > box.ClientWidth + 0.5f))
                    {
                        return box;
                    }
                }
            }

            box = box.Parent;
        }

        return firstScrollContainer;
    }

    /// <summary>
    /// Dispatches a DOM-style event to the nearest handler at or above an element.
    /// </summary>
    /// <param name="element">The target, or <see langword="null"/> for no dispatch.</param>
    /// <param name="eventName">The handler key, such as <c>click</c> or <c>change</c>.</param>
    /// <param name="value">
    /// Optional payload passed to string handlers; <see langword="null"/> invokes a
    /// parameterless handler instead.
    /// </param>
    /// <remarks>
    /// <para>
    /// Bubbling is modelled by walking parents and invoking only the first handler
    /// found, so an event never fires twice. Delegate shapes are matched against
    /// <see cref="Action"/> and <see cref="Action{T}"/> of <see cref="string"/>,
    /// falling back to dynamic invocation for anything else.
    /// </para>
    /// <para>
    /// Handler exceptions are swallowed deliberately: a throwing app handler must not
    /// tear down the frame loop. The trade-off is that a bug in a handler is silent.
    /// </para>
    /// </remarks>
    private static void Invoke(ElementNode? element, string eventName, string? value = null)
    {
        IgniteSupportedDomNode? node = element;

        while (node is not null)
        {
            if (node is ElementNode current
                && current.TryGetEventHandler(eventName, out var handler)
                && handler is not null)
            {
                try
                {
                    if (handler is Action action)
                    {
                        action();
                    }
                    else if (value is not null && handler is Action<string> stringAction)
                    {
                        stringAction(value);
                    }
                    else if (value is null)
                    {
                        handler.DynamicInvoke();
                    }
                    else
                    {
                        handler.DynamicInvoke(value);
                    }
                }
                catch
                {
                    // A host event must not tear down the frame loop.
                }

                return;
            }

            node = node.Parent;
        }
    }

    /// <summary>
    /// Reads an attribute, treating a missing attribute as <see langword="null"/>.
    /// </summary>
    /// <param name="element">The element to read.</param>
    /// <param name="name">The attribute name.</param>
    /// <returns>The attribute value, or <see langword="null"/> when absent.</returns>
    private static string? GetAttribute(ElementNode element, string name)
    {
        return element.TryGetAttribute(name, out var value) ? value : null;
    }

    /// <summary>Converts a packed 0xAARRGGBB colour to normalized RGBA components.</summary>
    /// <param name="argb">The packed colour.</param>
    /// <returns>RGBA components in the 0–1 range, as expected by OpenGL clear calls.</returns>
    private static Vector4 Color(uint argb)
    {
        return new Vector4(
            ((argb >> 16) & 0xFF) / 255f,
            ((argb >> 8) & 0xFF) / 255f,
            (argb & 0xFF) / 255f,
            ((argb >> 24) & 0xFF) / 255f);
    }

    /// <summary>
    /// Releases input devices and GPU objects while the native window and OpenGL
    /// context are still alive, then raises <see cref="Closed"/>.
    /// </summary>
    /// <remarks>
    /// This is the only safe moment to destroy textures, buffers and fonts: the
    /// context becomes invalid as soon as the Silk.NET loop returns. The native
    /// window itself is disposed later, by <see cref="Run(Donkeh.UILab.Node, string)"/>, because disposing it
    /// from inside the closing callback resets an active context.
    /// </remarks>
    private void OnClosing()
    {
        // The GLFW window and OpenGL context are still alive here, which is the
        // only safe time to release input devices, textures and buffers. The
        // native window itself is disposed by Run() after the loop has finished.
        _input?.Dispose();
        _input = null;
        DisposeGpu();
        try { _settings.OnClose?.Invoke(this); }
        catch { }
        Closed?.Invoke(this);
    }

    /// <summary>
    /// Disposes the display-list renderer, image textures and font atlas exactly
    /// once.
    /// </summary>
    /// <remarks>
    /// Must run with the OpenGL context current. The instance stays marked as
    /// disposed so both <see cref="OnClosing"/> and <see cref="Dispose"/> can call it
    /// safely. After this the renderer reference is null and the frame loop stops
    /// producing frames.
    /// </remarks>
    private void DisposeGpu()
    {
        if (_gpuDisposed)
        {
            return;
        }

        _gpuDisposed = true;
        _renderer?.Dispose();
        _renderer = null;
        _images.Dispose();
        _fonts.Dispose();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Idempotent: the first call releases the input context, the GPU objects and the
    /// native window; later calls do nothing. <see cref="Run(Donkeh.UILab.Node, string)"/> invokes it when the
    /// frame loop exits, so hosts rarely need to call it directly.
    /// </para>
    /// <para>
    /// GPU teardown requires a current OpenGL context. Disposing after the loop has
    /// exited, or without ever calling <see cref="Run(Donkeh.UILab.Node, string)"/>, is safe because there is
    /// nothing left to release in that case.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _input?.Dispose();
        DisposeGpu();
        _window?.Dispose();
    }

    /// <summary>State of one in-flight smooth scroll.</summary>
    /// <remarks>
    /// Stored per scrolling node; a new target replaces the entry rather than
    /// stacking animations. Distances and offsets are CSS pixels, times seconds.
    /// </remarks>
    private sealed class ScrollAnimation
    {
        /// <summary>Horizontal offset when the animation started.</summary>
        public float FromX;

        /// <summary>Vertical offset when the animation started.</summary>
        public float FromY;

        /// <summary>Horizontal target offset, already clamped to range.</summary>
        public float ToX;

        /// <summary>Vertical target offset, already clamped to range.</summary>
        public float ToY;

        /// <summary>Seconds elapsed since the animation started.</summary>
        public double Elapsed;

        /// <summary>Total duration in seconds, in the range 0.18–0.45.</summary>
        public double Duration;
    }

    /// <summary>State of an active scrollbar thumb drag.</summary>
    private sealed class ScrollbarDrag
    {
        /// <summary>The node whose scrollbar is being dragged.</summary>
        public required IgniteSupportedDomNode Node;

        /// <summary><see langword="true"/> for a vertical scrollbar.</summary>
        public bool Vertical;

        /// <summary>Pointer coordinate on the drag axis when the drag began.</summary>
        public float PointerStart;

        /// <summary>Scroll offset on the drag axis when the drag began.</summary>
        public float ScrollStart;
    }

    /// <summary>A scrollbar hit: the owning box and the metrics used to resolve the drag.</summary>
    /// <param name="Box">The scroll container whose scrollbar was hit.</param>
    /// <param name="Metrics">Track and thumb geometry, already in window coordinates.</param>
    private readonly record struct ScrollbarHit(LayoutBox Box, ScrollbarMetrics Metrics);
}
