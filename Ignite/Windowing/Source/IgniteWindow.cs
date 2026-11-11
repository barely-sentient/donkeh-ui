using System.Diagnostics;
using System.Numerics;
using Ignite.Data;
using Ignite.Data.Dom;
using Ignite.Inspector;
using Ignite.LayoutLab;
using Ignite.Parser;
using Ignite.UILab;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Ignite.Windowing;

/// <summary>
/// Owns the native window, the CSS table, layout, input routing and the OpenGL
/// display-list renderer. A UI tree is mounted once and patched by UILab; frames
/// only relayout when the tree, stylesheet, viewport or scroll state changed.
/// </summary>
public sealed partial class IgniteWindow : IDisposable
{
    private static readonly int ValueAttribute = InternTable.Intern("value");
    private static readonly int CheckedAttribute = InternTable.Intern("checked");
    private static readonly int SelectedAttribute = InternTable.Intern("selected");

    private readonly WindowSettings _settings;
    private readonly List<string> _stylesheets = [];
    private readonly FontManager _fonts = new();
    private readonly ImageCache _images = new();
    private readonly Renderer _ui = new();
    private readonly TrueTypeTextMeasurer _measurer;

    private IWindow? _window;
    private IInputContext? _input;
    private GL? _gl;
    private GLRenderer? _renderer;
    private LayoutEngine? _layoutEngine;
    private DataTable? _table;
    private LayoutResult? _layoutResult;
    private Node? _root;
    private Node? _appRoot;
    private readonly RendererContext _context = new();
    private readonly RendererContext _devToolsContext = new();
    private readonly DevToolsState _devTools = new();
    private DevToolsWindowHost? _devToolsWindow;
    private InspectorPanel? _devToolsPanel;
    private Renderer? _devToolsUi;
    private Node? _devToolsRoot;
    private LayoutEngine? _devToolsLayoutEngine;
    private LayoutResult? _devToolsLayout;
    private int _lastDevToolsVersion = -1;
    private int _lastDevToolsWidth = -1;
    private int _lastDevToolsHeight = -1;
    private int _lastDevToolsDock = -1;
    private bool _lastDevToolsVisible;
    private float _lastDevToolsDpi;
    private double _lastDevToolsRefresh;
    private double _performanceSampleTimer;
    private readonly long _animationStartTicks = Stopwatch.GetTimestamp();
    private double _animationTimeSeconds;
    private double _lastAnimationLayoutTime;
    private double _lastDevToolsAnimationLayoutTime;
    private const double AnimationLayoutInterval = 1.0 / 60.0;

    private bool _needsLayout = true;
    private bool _needsPaint;
    private bool _needsAnimatedPaint;
    private bool _needsDevToolsPaint;
    private readonly InstructionBuffer _devToolsOverlay = new(16);
    private readonly ConstantPool _devToolsOverlayConstants = new();
    private readonly InstructionBuffer _overlay = new(64);
    private readonly ConstantPool _overlayConstants = new();
    private readonly LayoutOptions _appOptions = new();
    private readonly LayoutOptions _devToolsOptions = new();
    private readonly Func<IgniteSupportedDomNode, string?> _textProvider;
    private readonly Func<IgniteSupportedDomNode, string, uint?> _textColorProvider;
    private readonly Func<IgniteSupportedDomNode, Size?> _intrinsicSizeProvider;
    private readonly Func<LayoutBox, ImageSpec?> _imageProvider;
    private readonly Action<LayoutBox, InstructionBuffer, ConstantPool> _appPainter;
    private readonly Action<LayoutBox, InstructionBuffer, ConstantPool> _devToolsPainter;
    private bool _disposed;
    private bool _gpuDisposed;
    private int _lastUiVersion = -1;
    private int _lastInspectorAppVersion = -1;
    private int _lastWidth;
    private int _lastHeight;
    private int _lastAppWidth;
    private int _lastAppHeight;
    private float _lastDpi;
    private int _cachedLayoutBoxCount;
    private int _cachedNodeCount;

    /// <summary>
    /// The shared inspector state when this window hosts a standalone panel.
    /// </summary>
    public DevToolsState? SharedDevToolsState { get; set; }

    private string? _screenshotPath;

    private readonly List<LayoutBox> _hitFixedBoxes = [];
    private readonly List<ElementNode> _hoverChain = [];
    private readonly List<ElementNode> _activeChain = [];
    private ElementNode? _hovered;
    private ElementNode? _pressed;
    private ElementNode? _pointerTarget;
    private bool _inspectPressed;
    private StandardCursor _cursorKind = StandardCursor.Arrow;
    private ElementNode? _focused;
    private string? _editText;
    private int _caretIndex;
    private long _caretBlinkStart;
    private readonly Dictionary<ElementNode, int> _selectIndices = [];
    private readonly List<ElementNode> _staleSelects = [];
    private ElementNode? _openSelect;
    private int _openSelectHover = -1;
    private LengthContext _lengthContext;
    private float _mouseX;
    private float _mouseY;
    private readonly Dictionary<IgniteSupportedDomNode, ScrollAnimation> _scrollAnimations = [];
    private readonly List<IgniteSupportedDomNode> _staleScrollAnimations = [];
    private ScrollbarDrag? _scrollDrag;

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
    public event Action<IgniteWindow>? Loaded;

    /// <summary>Raised once per frame before pending UI work is flushed.</summary>
    public event Action<IgniteWindow, double>? BeforeFrame;

    /// <summary>Raised when the native window closes.</summary>
    public event Action<IgniteWindow>? Closed;

    public IWindow? SilkWindow => _window;

    public GL? Gl => _gl;

    public IInputContext? Input => _input;

    public Renderer UI => _ui;

    public Node? Root => _root;

    /// <summary>The application root, without the inspector wrapper.</summary>
    public Node? AppRoot => _appRoot;

    /// <summary>Live inspector state. Read or change it to open, close or move devtools.</summary>
    public DevToolsState DevTools => _devTools;

    public LayoutResult? FrameResult => _layoutResult;

    public int FrameCount { get; private set; }

    public double LastFrameMilliseconds { get; private set; }

    public double LastLayoutMilliseconds { get; private set; }

    public double LastRenderMilliseconds { get; private set; }

    public int LastInstructionCount =>
        (_layoutResult?.Opcodes.Length ?? 0) + (_devToolsLayout?.Opcodes.Length ?? 0);

    /// <summary>Milliseconds spent flushing pending UI updates on the last frame.</summary>
    public double LastUpdateMilliseconds { get; private set; }

    /// <summary>Shows or hides the built-in inspector.</summary>
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
    public void AddStylesheet(string css)
    {
        ArgumentNullException.ThrowIfNull(css);
        _stylesheets.Add(css);
        RebuildStyles();
    }

    /// <summary>Marks the current tree as needing a new layout pass.</summary>
    public void Invalidate() => _needsLayout = true;

    /// <summary>
    /// Saves the next finished frame to a 24-bit BMP and closes the window. This
    /// is useful for headless smoke tests and visual regression fixtures.
    /// </summary>
    public void RequestScreenshot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _screenshotPath = path;
    }

    /// <summary>Creates the native window and enters the frame loop.</summary>
    public void Run() => Run(null, null);

    /// <summary>Mounts a tree, optionally sets a stylesheet and enters the frame loop.</summary>
    public void Run(Node? root, string? stylesheet = null)
    {
        // When no stylesheet is supplied explicitly, pick up every stylesheet
        // declared through [Stylesheet(...)] and merge them into one sheet, so
        // a developer only has to annotate the source files.
        if (stylesheet is null)
        {
            stylesheet = Ignite.UILab.Stylesheet.Load();
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
            WindowState = _settings.FullScreen ? WindowState.Fullscreen : WindowState.Normal,
            API = new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                ContextFlags.ForwardCompatible,
                new APIVersion(3, 3)),
            PreferredDepthBufferBits = null,
            PreferredStencilBufferBits = 8
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Resize += OnResize;
        _window.Closing += OnClosing;
        _window.Run();

        // Silk owns the native window until the loop exits; tearing it down from
        // inside Closing resets an active context. Dispose after Run returns.
        Dispose();
    }

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

    private void OnResize(Vector2D<int> size)
    {
        _needsLayout = true;
    }

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

    private void RebuildStyles()
    {
        var table = new DataTable();
        var engine = new Ignite.DataEngine.DataEngine(Ignite.DataEngine.MediaConditionCompiler.Instance);
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

    private const uint PlaceholderTextColor = 0xFF8C959F;

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

    private static List<ElementNode> FindOptions(ElementNode select)
    {
        var results = new List<ElementNode>();
        CollectOptions(select, results);
        return results;
    }

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

    private void CloseSelectMenu()
    {
        _openSelect = null;
        _openSelectHover = -1;
    }

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

    private readonly record struct SelectMenuLayout(Rect Menu, float RowHeight);

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

    private const float SelectMenuPadding = 4;

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

    private LayoutResult? LayoutFor(ElementNode? element) => LayoutForNode(element);

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

    private bool IsLiveNode(ElementNode node)
    {
        var layout = LayoutForNode(node);
        return layout is not null && layout.TryGetBox(node, out var ignoredBox);
    }

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

    private (float X, float Y) ContentOrigin(LayoutBox box)
    {
        var painted = PaintedBorderBox(box);
        return (
            painted.X + (box.ContentBox.X - box.BorderBox.X),
            painted.Y + (box.ContentBox.Y - box.BorderBox.Y));
    }

    private TextStyle GetControlTextStyle(LayoutBox box, uint color)
    {
        var fontSize = box.Style.ResolveFontSize(in _lengthContext);
        return box.Style.ToTextStyle(in _lengthContext, fontSize, color);
    }

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

    private ImageSpec? GetImage(LayoutBox box)
    {
        if (box.Node is not ElementNode element || !string.Equals(element.Tag, "img", StringComparison.OrdinalIgnoreCase))
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

    private void PaintElementContent(LayoutBox box, InstructionBuffer instructions, ConstantPool constants)
    {
        PaintSelection(box, instructions);

        if (box.Node is not ElementNode element)
        {
            return;
        }

        if (element.HasClass("spinner-border"))
        {
            PaintSpinner(box, instructions);
            return;
        }

        // Bootstrap draws its hamburger from an SVG data URI, which the engine
        // does not decode. Draw the three bars directly so the toggler does not
        // appear as an empty pill.
        if (element.HasClass("navbar-toggler-icon"))
        {
            PaintTogglerIcon(box, instructions);
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
        }
    }

    private void PaintSpinner(LayoutBox box, InstructionBuffer instructions)
    {
        var rect = PaintedBorderBox(box);

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var size = MathF.Min(rect.Width, rect.Height);
        var thickness = MathF.Max(2f, size * 0.125f);
        var radius = size / 2f - thickness / 2f;
        var centerX = rect.X + rect.Width / 2f;
        var centerY = rect.Y + rect.Height / 2f;
        var color = box.Style.Color == ComputedStyle.CurrentColor
            ? 0xFF212529u
            : box.Style.Color;
        var duration = 0.75f;

        if (box.Style.Animations is { Count: > 0 } animations)
        {
            for (var i = 0; i < animations.Count; i++)
            {
                if (animations[i].Name.Contains("spinner", StringComparison.OrdinalIgnoreCase)
                    && animations[i].Duration > 0)
                {
                    duration = animations[i].Duration;
                    break;
                }
            }
        }

        var spin = (float)(_animationTimeSeconds % duration) / duration * MathF.PI * 2f;
        const int segments = 40;
        var sweep = MathF.PI * 1.5f;

        for (var i = 0; i <= segments; i++)
        {
            var angle = spin + sweep * i / segments;
            var x = centerX + MathF.Cos(angle) * radius;
            var y = centerY + MathF.Sin(angle) * radius;
            instructions.AddDrawRect(
                new Rect(x - thickness / 2f, y - thickness / 2f, thickness, thickness),
                color);
        }
    }

    private static void PaintTogglerIcon(LayoutBox box, InstructionBuffer instructions)
    {
        var rect = box.ContentBox;

        if (rect.IsEmpty)
        {
            rect = box.BorderBox.Deflate(box.Style.BorderWidth.Left.Value, box.Style.BorderWidth.Right.Value,
                box.Style.BorderWidth.Top.Value, box.Style.BorderWidth.Bottom.Value);
        }

        if (rect.IsEmpty)
        {
            return;
        }

        var horizontalInset = rect.Width * 0.18f;
        var barWidth = MathF.Max(1, rect.Width - horizontalInset * 2);
        var barHeight = MathF.Max(1.5f, rect.Height * 0.08f);
        var x = rect.X + horizontalInset;

        for (var i = 0; i < 3; i++)
        {
            var centerY = rect.Y + rect.Height * (0.28f + i * 0.22f);
            instructions.AddDrawRect(
                new Rect(x, centerY - barHeight * 0.5f, barWidth, barHeight),
                0xFFB0B0B0);
        }
    }

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

    private void OnMouseMove(float x, float y)
    {
        _mouseX = x;
        _mouseY = y;
        _devTools.PointerX = x;
        _devTools.PointerY = y;

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

    private void OnMouseUp(MouseButton button)
    {
        _devTools.PointerX = _mouseX;
        _devTools.PointerY = _mouseY;

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

        if (ReferenceEquals(element, _pressed)
            && element is not null
            && !element.TryGetAttribute("disabled", out var ignoredDisabled))
        {
            if (element.Tag == "input")
            {
                ToggleInput(element);
            }

            Invoke(element, "click");

            if (element.Tag is "input" or "textarea" or "select")
            {
                Focus(element);
            }
        }

        _pressed = null;
        _selectionDragged = false;
    }

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
        // for it (this keeps Bootstrap's :root rule effective). Hidden overflow
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

    private void ResetCaretBlink()
    {
        _caretBlinkStart = Stopwatch.GetTimestamp();
    }

    private bool IsCaretVisible
    {
        get
        {
            var elapsed = (Stopwatch.GetTimestamp() - _caretBlinkStart) / (double)Stopwatch.Frequency;
            return elapsed % 1.0 < 0.5;
        }
    }

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

    private ElementNode? HitTest(float x, float y)
    {
        var box = HitTestBox(x, y);
        var node = box?.Node;
        return node as ElementNode ?? FindElement(node);
    }

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

        if (!isContents && !painted.Contains(x, y))
        {
            return null;
        }

        if (box.IsScrollContainer && !isContents)
        {
            var clip = box.PaddingBox.Translate(-scrollX, -scrollY);

            if (!clip.Contains(x, y))
            {
                return null;
            }

            scrollX += box.ScrollOffsetX;
            scrollY += box.ScrollOffsetY;
        }

        for (var i = box.Children.Count - 1; i >= 0; i--)
        {
            var hit = HitTestBox(box.Children[i], x, y, scrollX, scrollY, skipFixed);

            if (hit is not null)
            {
                return hit;
            }
        }

        // A contents box has no geometry of its own. Returning it would make
        // it swallow points that belong to a later sibling (for example the
        // disclosure triangle next to a tree row's content fragment).
        return isContents ? null : box;
    }

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

    private static string? GetAttribute(ElementNode element, string name)
    {
        return element.TryGetAttribute(name, out var value) ? value : null;
    }

    private static Vector4 Color(uint argb)
    {
        return new Vector4(
            ((argb >> 16) & 0xFF) / 255f,
            ((argb >> 8) & 0xFF) / 255f,
            (argb & 0xFF) / 255f,
            ((argb >> 24) & 0xFF) / 255f);
    }

    private void OnClosing()
    {
        // The GLFW window and OpenGL context are still alive here, which is the
        // only safe time to release input devices, textures and buffers. The
        // native window itself is disposed by Run() after the loop has finished.
        _input?.Dispose();
        _input = null;
        DisposeGpu();
        Closed?.Invoke(this);
    }

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

    private sealed class ScrollAnimation
    {
        public float FromX;
        public float FromY;
        public float ToX;
        public float ToY;
        public double Elapsed;
        public double Duration;
    }

    private sealed class ScrollbarDrag
    {
        public required IgniteSupportedDomNode Node;
        public bool Vertical;
        public float PointerStart;
        public float ScrollStart;
    }

    private readonly record struct ScrollbarHit(LayoutBox Box, ScrollbarMetrics Metrics);
}
