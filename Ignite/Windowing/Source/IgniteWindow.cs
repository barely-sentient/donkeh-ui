using System.Diagnostics;
using System.Numerics;
using Ignite.Data;
using Ignite.Data.Dom;
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
public sealed class IgniteWindow : IDisposable
{
    private static readonly int ValueAttribute = InternTable.Intern("value");
    private static readonly int CheckedAttribute = InternTable.Intern("checked");
    private const string HoverClassName = "is-hover";
    private const string FocusClassName = "is-focus";
    private const string ActiveClassName = "is-active";

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
    private RendererContext _context = new();

    private bool _needsLayout = true;
    private bool _disposed;
    private bool _gpuDisposed;
    private int _lastUiVersion = -1;
    private int _lastWidth;
    private int _lastHeight;
    private float _lastDpi;

    private string? _screenshotPath;

    private ElementNode? _hovered;
    private ElementNode? _pressed;
    private ElementNode? _focused;
    private string? _editText;
    private float _mouseX;
    private float _mouseY;

    public IgniteWindow(WindowSettings settings)
    {
        _settings = settings;
        _measurer = new TrueTypeTextMeasurer(_fonts);
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

    public LayoutResult? FrameResult => _layoutResult;

    public int FrameCount { get; private set; }

    public double LastFrameMilliseconds { get; private set; }

    public double LastLayoutMilliseconds { get; private set; }

    public double LastRenderMilliseconds { get; private set; }

    public int LastInstructionCount => _layoutResult?.Opcodes.Length ?? 0;

    /// <summary>The mount point of the live tree. Call <see cref="Mount"/> first.</summary>
    public void Mount(Node root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (_root is not null)
        {
            throw new InvalidOperationException("A tree is already mounted.");
        }

        _root = _ui.Mount(root);
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
        if (stylesheet is not null)
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

        _ui.Flush();

        var size = _window.Size;
        var framebuffer = _window.FramebufferSize;
        var width = Math.Max(1, size.X);
        var height = Math.Max(1, size.Y);
        var dpi = framebuffer.X / (float)width;

        if (dpi <= 0 || float.IsNaN(dpi))
        {
            dpi = 1;
        }

        if (_needsLayout
            || _lastUiVersion != _ui.Version
            || _lastWidth != width
            || _lastHeight != height
            || MathF.Abs(_lastDpi - dpi) > 0.001f)
        {
            var start = Stopwatch.GetTimestamp();
            var options = CreateLayoutOptions(width, height, dpi);
            _layoutResult = _layoutEngine!.Layout(_root, options);
            LastLayoutMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _lastUiVersion = _ui.Version;
            _lastWidth = width;
            _lastHeight = height;
            _lastDpi = dpi;
            _needsLayout = false;
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
            _renderer.Render(_layoutResult);
        }

        LastRenderMilliseconds = Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds;
        FrameCount++;
        LastFrameMilliseconds = delta * 1000;

        if (_screenshotPath is { } path)
        {
            _screenshotPath = null;
            SaveFramebuffer(path, framebuffer.X, framebuffer.Y);
            _window.Close();
        }
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

    private LayoutOptions CreateLayoutOptions(int width, int height, float dpi)
    {
        _context.ViewportWidth = width;
        _context.ViewportHeight = height;

        return new LayoutOptions
        {
            ViewportWidth = width,
            ViewportHeight = height,
            DevicePixelRatio = dpi,
            Context = _context,
            TextMeasurer = _measurer,
            TextProvider = GetDisplayText,
            IntrinsicSizeProvider = GetIntrinsicSize,
            ImageProvider = GetImage,
            CustomPainter = PaintElementContent,
            Optimise = true,
            CullOffscreen = true,
            MergeDraws = true,
            CompactConstants = false,
            EnableCaching = true
        };
    }

    private void RebuildStyles()
    {
        var table = new DataTable();
        var engine = new Ignite.DataEngine.DataEngine(Condition.AlwaysTrueCompiler);
        engine.Build(Tokenizer.Parse(HtmlDefaults.Css), table);

        foreach (var sheet in _stylesheets)
        {
            engine.Build(Tokenizer.Parse(sheet), table);
        }

        _table = table;
        _layoutEngine = new LayoutEngine(table);
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
                    return _editText ?? string.Empty;
                }

                var value = GetAttribute(element, "value");
                return string.IsNullOrEmpty(value) ? GetAttribute(element, "placeholder") : value;
            }

            case "textarea":
                return ReferenceEquals(element, _focused)
                    ? _editText ?? string.Empty
                    : element.TextContent ?? GetAttribute(element, "value") ?? GetAttribute(element, "placeholder");

            case "select":
            {
                var options = FindOptions(element);
                return options.Count > 0 ? options[0].TextContent ?? string.Empty : string.Empty;
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
                return new Size(
                    HtmlDefaults.AttributeLength(element, "width", 220),
                    MathF.Max(24, HtmlDefaults.AttributeLength(element, "height", 32)));

            case "textarea":
                return new Size(
                    HtmlDefaults.AttributeLength(element, "width", 320),
                    HtmlDefaults.AttributeLength(element, "height", 96));

            case "select":
                return new Size(
                    HtmlDefaults.AttributeLength(element, "width", 180),
                    HtmlDefaults.AttributeLength(element, "height", 32));

            case "progress":
            case "meter":
                return new Size(
                    HtmlDefaults.AttributeLength(element, "width", 180),
                    HtmlDefaults.AttributeLength(element, "height", 14));
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

    private void PaintElementContent(LayoutBox box, InstructionBuffer instructions, ConstantPool constants)
    {
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
            keyboard.KeyDown += (_, key, _) => OnKeyDown(key);
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
        var element = HitTest(x, y);
        SetHovered(element);
    }

    private void OnMouseDown(MouseButton button)
    {
        var element = HitTest(_mouseX, _mouseY);
        _pressed = element;

        if (element is not null)
        {
            ApplyClass(element, ActiveClassName, true);
            Focus(element);
        }

        Invoke(element, "mousedown");
    }

    private void OnMouseUp(MouseButton button)
    {
        var element = HitTest(_mouseX, _mouseY);

        if (_pressed is not null)
        {
            ApplyClass(_pressed, ActiveClassName, false);
        }

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
            if (element.TryGetAttribute(CheckedAttribute, out var ignoredChecked))
            {
                element.RemoveAttribute(CheckedAttribute);
            }
            else
            {
                element.SetAttribute(CheckedAttribute, InternTable.Intern(string.Empty));
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
                        sibling.RemoveAttribute(CheckedAttribute);
                    }
                }
            }

            element.SetAttribute(CheckedAttribute, InternTable.Intern(string.Empty));
            Invoke(element, "change");
            _needsLayout = true;
        }
    }

    private void OnScroll(float vertical, float horizontal)
    {
        var box = HitTestBox(_mouseX, _mouseY);
        var scroll = FindScrollContainer(box);

        if (scroll is null)
        {
            return;
        }

        scroll.Node.ScrollOffsetY = MathF.Max(0, scroll.Node.ScrollOffsetY - vertical * 48);
        scroll.Node.ScrollOffsetX = MathF.Max(0, scroll.Node.ScrollOffsetX - horizontal * 48);
        _needsLayout = true;
    }

    private void OnKeyDown(Key key)
    {
        if (_focused is null)
        {
            return;
        }

        switch (key)
        {
            case Key.Backspace:
                if (_editText is { Length: > 0 })
                {
                    _editText = _editText[..^1];
                    CascadeEdit(_focused);
                    Invoke(_focused, "input", _editText ?? string.Empty);
                }

                break;

            case Key.Enter:
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

        var type = GetAttribute(_focused, "type");

        if (type is "checkbox" or "radio" or "range")
        {
            return;
        }

        _editText = (_editText ?? string.Empty) + character;
        CascadeEdit(_focused);
        Invoke(_focused, "input", _editText ?? string.Empty);
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
    }

    private void Focus(ElementNode? element)
    {
        if (element is not null && element.TryGetAttribute("disabled", out var ignoredDisabled))
        {
            return;
        }

        if (ReferenceEquals(_focused, element))
        {
            return;
        }

        if (_focused is not null)
        {
            ApplyClass(_focused, FocusClassName, false);

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
            ApplyClass(element, FocusClassName, true);
            _editText = GetAttribute(element, "value") ?? element.TextContent ?? string.Empty;

            if (element.TryGetEventHandler("focus", out var handler) && handler is Action action)
            {
                action();
            }
        }
        else
        {
            _editText = null;
        }

        _needsLayout = true;
    }

    private void FocusNext()
    {
        var focusable = new List<ElementNode>();
        CollectFocusable(_root, focusable);

        if (focusable.Count == 0)
        {
            return;
        }

        var index = _focused is null ? -1 : focusable.IndexOf(_focused);
        Focus(focusable[(index + 1) % focusable.Count]);
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

    private void SetHovered(ElementNode? element)
    {
        if (ReferenceEquals(_hovered, element))
        {
            return;
        }

        if (_hovered is not null)
        {
            ApplyClass(_hovered, HoverClassName, false);
        }

        _hovered = element;

        if (element is not null)
        {
            ApplyClass(element, HoverClassName, true);
        }
    }

    private static void ApplyClass(ElementNode element, string className, bool add)
    {
        if (add)
        {
            element.AddClass(className);
        }
        else if (element.HasClass(className))
        {
            element.RemoveClass(className);
        }
    }

    private ElementNode? HitTest(float x, float y) => HitTestBox(x, y)?.Node is ElementNode element
        ? element
        : FindElement(HitTestBox(x, y)?.Node);

    private LayoutBox? HitTestBox(float x, float y)
    {
        if (_layoutResult?.Root is not { } root)
        {
            return null;
        }

        return HitTestBox(root, x, y);
    }

    private static LayoutBox? HitTestBox(LayoutBox box, float x, float y)
    {
        if (box.Style.Display == Display.None || box.Style.Visibility is Visibility.Hidden or Visibility.Collapse)
        {
            return null;
        }

        if (!box.BorderBox.Contains(x, y))
        {
            return null;
        }

        if (box.IsScrollContainer && !box.PaddingBox.Contains(x, y))
        {
            return null;
        }

        for (var i = box.Children.Count - 1; i >= 0; i--)
        {
            var hit = HitTestBox(box.Children[i], x, y);

            if (hit is not null)
            {
                return hit;
            }
        }

        return box;
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

    private static LayoutBox? FindScrollContainer(LayoutBox? box)
    {
        while (box is not null)
        {
            if (box.IsScrollContainer)
            {
                return box;
            }

            box = box.Parent;
        }

        return null;
    }

    private static void Invoke(ElementNode? element, string eventName, string? value = null)
    {
        var node = element;

        while (node is not null)
        {
            if (node.TryGetEventHandler(eventName, out var handler) && handler is not null)
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

            node = node.Parent as ElementNode;
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
}
