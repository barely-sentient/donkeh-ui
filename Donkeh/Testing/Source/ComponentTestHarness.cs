using Donkeh.Data;
using Donkeh.Data.Dom;
using Donkeh.DataEngine;
using Donkeh.LayoutLab;
using Donkeh.Parser;
using Donkeh.UILab;
using Donkeh.Windowing;
using DataEngineFactory = Donkeh.DataEngine.DataEngine;

namespace Donkeh.Testing;

/// <summary>
/// Mounts a UILab component tree without a native window and drives it with
/// synthetic input.
/// </summary>
/// <remarks>
/// <para>
/// The harness owns a <see cref="UILab.Renderer"/> and a compiled stylesheet
/// containing the built-in HTML defaults, any <c>[Stylesheet(...)]</c>
/// declarations, and the extra sheets supplied in
/// <see cref="ComponentTestOptions"/>. After mount it runs the same layout engine
/// used by the window, so computed styles, boxes and scroll metrics reflect the
/// rendered component rather than the virtual description.
/// </para>
/// <para>
/// Every event helper calls <see cref="Settle"/> afterwards. That flushes queued
/// component state updates and reruns layout, which makes assertions immediately
/// observable:
/// </para>
/// <code>
/// var page = ComponentTest.Render(_.C&lt;Counter&gt;());
/// page.Click("button");
/// Assert.Equal("1", page.Find("button").Text);
/// </code>
/// <para>
/// Events are invoked with the same nearest-handler bubbling contract as the
/// window. Handler exceptions are not swallowed: a failing handler should fail
/// the test with its original stack trace.
/// </para>
/// </remarks>
public sealed class ComponentTestHarness
{
    private readonly ComponentTestOptions _options;
    private readonly DataEngineFactory _dataEngine = new(MediaConditionCompiler.Instance);
    private readonly Dictionary<string, DataTable> _queryTables = new(StringComparer.Ordinal);
    private MatchedRule[] _queryMatches = new MatchedRule[8];

    private Renderer? _renderer;
    private LayoutEngine? _layoutEngine;
    private DataTable? _table;
    private LayoutOptions? _layoutOptions;
    private LayoutResult? _layout;
    private Node? _root;
    private ElementNode? _focused;
    private ElementNode? _hovered;
    private long _interactionVersion;

    /// <summary>Creates a harness with the supplied options.</summary>
    /// <param name="options">
    /// Frame and stylesheet options, or <see langword="null"/> for the defaults.
    /// </param>
    public ComponentTestHarness(ComponentTestOptions? options = null)
    {
        _options = options ?? new ComponentTestOptions();
    }

    /// <summary>Gets the options this harness was created with.</summary>
    public ComponentTestOptions Options => _options;

    /// <summary>Gets the UILab renderer, or <see langword="null"/> before mount.</summary>
    public Renderer? Renderer => _renderer;

    /// <summary>Gets the live root node, or <see langword="null"/> before mount.</summary>
    public Node? Root => _root;

    /// <summary>Gets the root as a queryable element, if the root is an element.</summary>
    public TestElement? RootElement =>
        _root is ElementNode element ? new TestElement(this, element) : null;

    /// <summary>Gets the compiled rule table, or <see langword="null"/> before mount.</summary>
    public DataTable? Rules => _table;

    /// <summary>
    /// Gets the latest layout result, settling pending renders first.
    /// </summary>
    /// <remarks>
    /// Reading this property may run layout. Use <see cref="Settle"/> explicitly
    /// when a test wants to attribute timing or failures to the settle step.
    /// </remarks>
    public LayoutResult? Layout
    {
        get
        {
            Settle();
            return _layout;
        }
    }

    /// <summary>Mounts a virtual tree and runs the first layout pass.</summary>
    /// <param name="vdom">The component or element tree to mount.</param>
    /// <returns>This harness, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="vdom"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A tree is already mounted.</exception>
    public ComponentTestHarness Mount(Node vdom)
    {
        ArgumentNullException.ThrowIfNull(vdom);

        if (_renderer is not null)
        {
            throw new InvalidOperationException("A component tree is already mounted.");
        }

        _renderer = new Renderer();
        _root = _renderer.Mount(vdom);
        RebuildStyles();
        return Settle();
    }

    /// <summary>
    /// Patches the mounted root with a new virtual description.
    /// </summary>
    /// <param name="vdom">The new root, typically new props for the same component.</param>
    /// <returns>This harness, for chaining.</returns>
    /// <remarks>
    /// The root kind must match to preserve component state and element identity.
    /// A different root kind unmounts the old subtree and mounts the new one.
    /// </remarks>
    public ComponentTestHarness Update(Node vdom)
    {
        ArgumentNullException.ThrowIfNull(vdom);

        if (_renderer is null)
        {
            throw new InvalidOperationException("The component tree is not mounted.");
        }

        _root = _renderer.Update(vdom);
        return Settle();
    }

    /// <summary>
    /// Flushes queued renders and rebuilds the layout for the current tree.
    /// </summary>
    /// <returns>This harness, for chaining.</returns>
    /// <remarks>
    /// This is the explicit synchronisation point for tests that disable
    /// automatic flushing or that mutate a node directly before asserting.
    /// </remarks>
    public ComponentTestHarness Settle()
    {
        if (_renderer is null || _root is null || _layoutEngine is null || _layoutOptions is null)
        {
            return this;
        }

        _renderer.Flush();

        _layoutOptions.ViewportWidth = _options.ViewportWidth;
        _layoutOptions.ViewportHeight = _options.ViewportHeight;
        _layoutOptions.RootFontSize = _options.RootFontSize;
        _layoutOptions.DefaultFontSize = _options.DefaultFontSize;
        _layoutOptions.TextMeasurer = _options.TextMeasurer;
        _layoutOptions.Optimise = _options.Optimise;
        _layoutOptions.InteractionVersion = _interactionVersion;
        _layout = _layoutEngine.Layout(_root, _layoutOptions);
        return this;
    }

    /// <summary>Recompiles the stylesheet and relayouts a mounted tree.</summary>
    /// <returns>This harness, for chaining.</returns>
    /// <remarks>
    /// Call this after loading an assembly that declares new
    /// <c>[Stylesheet(...)]</c> metadata, or after changing
    /// <see cref="ComponentTestOptions.Stylesheets"/> directly.
    /// </remarks>
    public ComponentTestHarness RebuildStyles()
    {
        var table = new DataTable();

        if (_options.IncludeDefaultStyles)
        {
            Append(table, HtmlDefaults.Css);
        }

        if (_options.IncludeDeclaredStylesheets)
        {
            Append(table, Donkeh.UILab.Stylesheet.Load());
        }

        for (var i = 0; i < _options.Stylesheets.Count; i++)
        {
            Append(table, _options.Stylesheets[i]);
        }

        _table = table;
        _layoutEngine = new LayoutEngine(table);
        _queryTables.Clear();
        _layoutOptions = CreateLayoutOptions();
        _layout = null;
        return Settle();
    }

    /// <summary>Adds a stylesheet after the sheets already compiled.</summary>
    /// <param name="css">The CSS text to append.</param>
    /// <returns>This harness, for chaining.</returns>
    /// <remarks>
    /// The sheet is appended to <see cref="ComponentTestOptions.Stylesheets"/> and
    /// the whole table is rebuilt, so its rules win ties against earlier sheets.
    /// </remarks>
    public ComponentTestHarness AddStylesheet(string css)
    {
        ArgumentNullException.ThrowIfNull(css);
        _options.Stylesheets.Add(css);
        return RebuildStyles();
    }

    /// <summary>Changes the viewport and relayouts.</summary>
    /// <param name="width">Viewport width in CSS pixels.</param>
    /// <param name="height">Viewport height in CSS pixels.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Resize(float width, float height)
    {
        _options.ViewportWidth = width;
        _options.ViewportHeight = height;
        return Settle();
    }

    /// <summary>Finds every element matching a CSS selector.</summary>
    /// <param name="selector">A selector such as <c>.card button</c>.</param>
    /// <returns>Matches in document order, including the root when it matches.</returns>
    public IReadOnlyList<TestElement> FindAll(string selector)
    {
        Settle();

        if (_root is null)
        {
            return [];
        }

        return FindAllWithin(_root, selector, includeSelf: true);
    }

    /// <summary>Finds the first element matching a CSS selector.</summary>
    /// <param name="selector">A selector such as <c>.card button</c>.</param>
    /// <returns>The first match, or <see langword="null"/> when none exists.</returns>
    public TestElement? TryFind(string selector)
    {
        var matches = FindAll(selector);
        return matches.Count > 0 ? matches[0] : null;
    }

    /// <summary>Finds the first element matching a CSS selector.</summary>
    /// <param name="selector">A selector such as <c>.card button</c>.</param>
    /// <returns>The first match.</returns>
    /// <exception cref="ComponentQueryException">No element matched.</exception>
    public TestElement Find(string selector)
    {
        var matches = FindAll(selector);

        if (matches.Count == 0)
        {
            throw new ComponentQueryException($"No element matched the selector '{selector}'.");
        }

        return matches[0];
    }

    /// <summary>Finds every element whose flattened text equals a value.</summary>
    /// <param name="text">The text to compare.</param>
    /// <param name="comparison">The string comparison to use.</param>
    /// <returns>Matches in document order.</returns>
    public IReadOnlyList<TestElement> FindAllByText(
        string text,
        StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(text);
        Settle();

        var matches = new List<TestElement>();

        if (_root is not null)
        {
            CollectByText(_root, text, comparison, matches);
        }

        return matches;
    }

    /// <summary>Finds the first element whose flattened text equals a value.</summary>
    /// <param name="text">The text to compare.</param>
    /// <param name="comparison">The string comparison to use.</param>
    /// <returns>The first match, or <see langword="null"/> when none exists.</returns>
    public TestElement? TryFindByText(
        string text,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var matches = FindAllByText(text, comparison);
        return matches.Count > 0 ? matches[0] : null;
    }

    /// <summary>Finds the first element whose flattened text equals a value.</summary>
    /// <param name="text">The text to compare.</param>
    /// <param name="comparison">The string comparison to use.</param>
    /// <returns>The first match.</returns>
    /// <exception cref="ComponentQueryException">No element matched.</exception>
    public TestElement FindByText(
        string text,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var element = TryFindByText(text, comparison);

        if (element is null)
        {
            throw new ComponentQueryException($"No element contained the text '{text}'.");
        }

        return element;
    }

    /// <summary>Dispatches a click event to an element or its nearest ancestor handler.</summary>
    /// <param name="target">The deepest element involved.</param>
    /// <param name="button">The DOM button number: 0 left, 1 middle, 2 right.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Click(
        TestElement target,
        int button = 0,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        ArgumentNullException.ThrowIfNull(target);
        var (x, y) = Center(target);
        Dispatch(target, new ClickEvent(x, y, 0, 0, button, modifiers));
        return this;
    }

    /// <summary>Dispatches a click event to the first element matching a selector.</summary>
    /// <param name="selector">The target selector.</param>
    /// <param name="button">The DOM button number.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Click(
        string selector,
        int button = 0,
        KeyModifiers modifiers = KeyModifiers.None) =>
        Click(Find(selector), button, modifiers);

    /// <summary>Dispatches a mouse-button press and marks the target active.</summary>
    /// <param name="target">The deepest element involved.</param>
    /// <param name="button">The DOM button number.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness MouseDown(
        TestElement target,
        int button = 0,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        ArgumentNullException.ThrowIfNull(target);
        SetActiveChain(target.Element, true);
        var (x, y) = Center(target);
        Dispatch(target, new MouseDownEvent(x, y, 0, 0, button, modifiers));
        return this;
    }

    /// <summary>Dispatches a mouse-button press to the first matching element.</summary>
    /// <param name="selector">The target selector.</param>
    /// <param name="button">The DOM button number.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness MouseDown(
        string selector,
        int button = 0,
        KeyModifiers modifiers = KeyModifiers.None) =>
        MouseDown(Find(selector), button, modifiers);

    /// <summary>Dispatches a mouse-button release and clears active state.</summary>
    /// <param name="target">The deepest element involved.</param>
    /// <param name="button">The DOM button number.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness MouseUp(
        TestElement target,
        int button = 0,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        ArgumentNullException.ThrowIfNull(target);
        SetActiveChain(target.Element, false);
        var (x, y) = Center(target);
        Dispatch(target, new MouseUpEvent(x, y, 0, 0, button, modifiers));
        return this;
    }

    /// <summary>Dispatches a mouse-button release to the first matching element.</summary>
    /// <param name="selector">The target selector.</param>
    /// <param name="button">The DOM button number.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness MouseUp(
        string selector,
        int button = 0,
        KeyModifiers modifiers = KeyModifiers.None) =>
        MouseUp(Find(selector), button, modifiers);

    /// <summary>Dispatches a pointer press.</summary>
    /// <param name="target">The deepest element involved.</param>
    /// <param name="button">The DOM button number.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness PointerDown(
        TestElement target,
        int button = 0,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        ArgumentNullException.ThrowIfNull(target);
        var (x, y) = Center(target);
        Dispatch(target, new PointerDownEvent(x, y, 0, 0, button, modifiers));
        return this;
    }

    /// <summary>Dispatches a pointer press to the first matching element.</summary>
    /// <param name="selector">The target selector.</param>
    /// <param name="button">The DOM button number.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness PointerDown(
        string selector,
        int button = 0,
        KeyModifiers modifiers = KeyModifiers.None) =>
        PointerDown(Find(selector), button, modifiers);

    /// <summary>Dispatches a pointer move over an element.</summary>
    /// <param name="target">The element under the pointer.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness PointerMove(
        TestElement target,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        ArgumentNullException.ThrowIfNull(target);
        var (x, y) = Center(target);
        Dispatch(target, new PointerMoveEvent(x, y, modifiers: modifiers));
        return this;
    }

    /// <summary>Dispatches a pointer move over the first matching element.</summary>
    /// <param name="selector">The target selector.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness PointerMove(
        string selector,
        KeyModifiers modifiers = KeyModifiers.None) =>
        PointerMove(Find(selector), modifiers);

    /// <summary>Dispatches a pointer release.</summary>
    /// <param name="target">The element under the pointer.</param>
    /// <param name="button">The DOM button number.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness PointerUp(
        TestElement target,
        int button = 0,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        ArgumentNullException.ThrowIfNull(target);
        var (x, y) = Center(target);
        Dispatch(target, new PointerUpEvent(x, y, 0, 0, button, modifiers));
        return this;
    }

    /// <summary>Dispatches a pointer release to the first matching element.</summary>
    /// <param name="selector">The target selector.</param>
    /// <param name="button">The DOM button number.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness PointerUp(
        string selector,
        int button = 0,
        KeyModifiers modifiers = KeyModifiers.None) =>
        PointerUp(Find(selector), button, modifiers);

    /// <summary>Moves the pointer over an element and marks its hover chain.</summary>
    /// <param name="target">The element being entered.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Hover(
        TestElement target,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_hovered is not null)
        {
            SetHoverChain(_hovered, false);
        }

        _hovered = target.Element;
        SetHoverChain(_hovered, true);
        MarkInteractionChanged();

        var (x, y) = Center(target);
        Dispatch(target, new MouseOverEvent(x, y, 0, 0, modifiers));
        return this;
    }

    /// <summary>Moves the pointer over the first matching element.</summary>
    /// <param name="selector">The target selector.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Hover(
        string selector,
        KeyModifiers modifiers = KeyModifiers.None) =>
        Hover(Find(selector), modifiers);

    /// <summary>Moves the pointer out of the currently hovered element.</summary>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Unhover(KeyModifiers modifiers = KeyModifiers.None)
    {
        if (_hovered is null)
        {
            return this;
        }

        var target = _hovered;
        SetHoverChain(target, false);
        _hovered = null;
        MarkInteractionChanged();
        var element = new TestElement(this, target);
        var (x, y) = Center(element);
        Dispatch(element, new MouseOutEvent(x, y, 0, 0, modifiers));
        return this;
    }

    /// <summary>
    /// Adds a scroll delta to an element and dispatches a scroll event.
    /// </summary>
    /// <param name="target">The scroll container, or a descendant of one.</param>
    /// <param name="deltaX">Horizontal delta in CSS pixels.</param>
    /// <param name="deltaY">Vertical delta in CSS pixels.</param>
    /// <param name="applyToScrollOffset">
    /// <see langword="true"/> to update the node's scroll offset; the next layout
    /// clamps it to the scrollable range.
    /// </param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Scroll(
        TestElement target,
        float deltaX,
        float deltaY,
        bool applyToScrollOffset = true)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (applyToScrollOffset)
        {
            target.Element.ScrollOffsetX += deltaX;
            target.Element.ScrollOffsetY += deltaY;
        }

        Dispatch(target, new ScrollEvent(deltaX, deltaY));
        return this;
    }

    /// <summary>Scrolls the first matching element.</summary>
    /// <param name="selector">The target selector.</param>
    /// <param name="deltaX">Horizontal delta in CSS pixels.</param>
    /// <param name="deltaY">Vertical delta in CSS pixels.</param>
    /// <param name="applyToScrollOffset">Whether to update the node scroll offset.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Scroll(
        string selector,
        float deltaX,
        float deltaY,
        bool applyToScrollOffset = true) =>
        Scroll(Find(selector), deltaX, deltaY, applyToScrollOffset);

    /// <summary>Sets a form control's value and dispatches <c>input</c>.</summary>
    /// <param name="target">The input or textarea.</param>
    /// <param name="value">The new value.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Input(TestElement target, string value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(value);
        Settle();
        SetControlValue(target.Element, value);
        Dispatch(target, new InputEvent(value));
        return this;
    }

    /// <summary>Sets the first matching control's value and dispatches <c>input</c>.</summary>
    /// <param name="selector">The target selector.</param>
    /// <param name="value">The new value.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Input(string selector, string value) =>
        Input(Find(selector), value);

    /// <summary>
    /// Appends text one character at a time, dispatching <c>input</c> after each
    /// character.
    /// </summary>
    /// <param name="target">The input or textarea.</param>
    /// <param name="text">The text to append.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Type(TestElement target, string text)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(text);
        Settle();
        var value = GetControlValue(target.Element);

        for (var i = 0; i < text.Length; i++)
        {
            value += text[i];
            SetControlValue(target.Element, value);
            Dispatch(target, new InputEvent(value));
        }

        return this;
    }

    /// <summary>Appends text to the first matching control.</summary>
    /// <param name="selector">The target selector.</param>
    /// <param name="text">The text to append.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Type(string selector, string text) =>
        Type(Find(selector), text);

    /// <summary>Sets a form control's value and dispatches <c>change</c>.</summary>
    /// <param name="target">The input, textarea or select.</param>
    /// <param name="value">The committed value.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Change(TestElement target, string value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(value);
        Settle();
        SetControlValue(target.Element, value);
        Dispatch(target, new ChangeEvent(value));
        return this;
    }

    /// <summary>Changes the first matching control.</summary>
    /// <param name="selector">The target selector.</param>
    /// <param name="value">The committed value.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Change(string selector, string value) =>
        Change(Find(selector), value);

    /// <summary>Dispatches a paste event.</summary>
    /// <param name="target">The target control.</param>
    /// <param name="text">The pasted text.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Paste(TestElement target, string text)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(text);
        Dispatch(target, new PasteEvent(text));
        return this;
    }

    /// <summary>Dispatches a paste event to the first matching element.</summary>
    /// <param name="selector">The target selector.</param>
    /// <param name="text">The pasted text.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Paste(string selector, string text) =>
        Paste(Find(selector), text);

    /// <summary>Dispatches a submit event.</summary>
    /// <param name="target">The form or submit control.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Submit(TestElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Dispatch(target, new SubmitEvent());
        return this;
    }

    /// <summary>Dispatches a submit event to the first matching element.</summary>
    /// <param name="selector">The target selector.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Submit(string selector) => Submit(Find(selector));

    /// <summary>Dispatches a keydown event.</summary>
    /// <param name="target">The element that owns focus.</param>
    /// <param name="key">The key name, such as <c>Enter</c>.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness KeyDown(
        TestElement target,
        string key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(key);
        Dispatch(target, new KeyDownEvent(key, modifiers));
        return this;
    }

    /// <summary>Dispatches a keydown event to the first matching element.</summary>
    /// <param name="selector">The target selector.</param>
    /// <param name="key">The key name, such as <c>Enter</c>.</param>
    /// <param name="modifiers">Modifier keys held during the event.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness KeyDown(
        string selector,
        string key,
        KeyModifiers modifiers = KeyModifiers.None) =>
        KeyDown(Find(selector), key, modifiers);

    /// <summary>Moves focus to an element and dispatches <c>focus</c>.</summary>
    /// <param name="target">The element receiving focus.</param>
    /// <param name="focusVisible">Whether <c>:focus-visible</c> should also match.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Focus(TestElement target, bool focusVisible = true)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (ReferenceEquals(_focused, target.Element))
        {
            return this;
        }

        if (_focused is not null)
        {
            var previous = new TestElement(this, _focused);
            _focused.IsFocused = false;
            _focused.IsFocusVisible = false;
            MarkInteractionChanged();
            Dispatch(previous, new BlurEvent());
        }

        _focused = target.Element;
        _focused.IsFocused = true;
        _focused.IsFocusVisible = focusVisible;
        MarkInteractionChanged();
        Dispatch(target, new FocusEvent());
        return this;
    }

    /// <summary>Moves focus to the first matching element.</summary>
    /// <param name="selector">The target selector.</param>
    /// <param name="focusVisible">Whether <c>:focus-visible</c> should match.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Focus(string selector, bool focusVisible = true) =>
        Focus(Find(selector), focusVisible);

    /// <summary>Removes focus from an element and dispatches <c>blur</c>.</summary>
    /// <param name="target">The element losing focus.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Blur(TestElement target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!ReferenceEquals(_focused, target.Element) && !target.Element.IsFocused)
        {
            return this;
        }

        target.Element.IsFocused = false;
        target.Element.IsFocusVisible = false;

        if (ReferenceEquals(_focused, target.Element))
        {
            _focused = null;
        }

        MarkInteractionChanged();
        Dispatch(target, new BlurEvent());
        return this;
    }

    /// <summary>Removes focus from the first matching element.</summary>
    /// <param name="selector">The target selector.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Blur(string selector) => Blur(Find(selector));

    /// <summary>Dispatches a media play event.</summary>
    /// <param name="target">The audio or video element.</param>
    /// <param name="time">The playback position in seconds.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Play(TestElement target, float time = 0) =>
        Media(target, new PlayEvent(time));

    /// <summary>Dispatches a media pause event.</summary>
    /// <param name="target">The audio or video element.</param>
    /// <param name="time">The playback position in seconds.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Pause(TestElement target, float time = 0) =>
        Media(target, new PauseEvent(time));

    /// <summary>Dispatches a media ended event.</summary>
    /// <param name="target">The audio or video element.</param>
    /// <param name="time">The playback position in seconds.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness Ended(TestElement target, float time = 0) =>
        Media(target, new EndedEvent(time));

    /// <summary>Dispatches a media timeupdate event.</summary>
    /// <param name="target">The audio or video element.</param>
    /// <param name="time">The playback position in seconds.</param>
    /// <returns>This harness, for chaining.</returns>
    public ComponentTestHarness TimeUpdate(TestElement target, float time) =>
        Media(target, new TimeUpdateEvent(time));

    /// <summary>
    /// Dispatches an arbitrary event to an element or its nearest ancestor
    /// handler.
    /// </summary>
    /// <param name="target">The deepest element involved.</param>
    /// <param name="uiEvent">The event payload to deliver.</param>
    /// <param name="bubble">
    /// <see langword="true"/> to walk ancestors until a handler is found;
    /// <see langword="false"/> to test only the target.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a handler was found and invoked.
    /// </returns>
    /// <remarks>
    /// This is the escape hatch for custom events and for the richer event types
    /// that do not have a dedicated convenience method. Target and current-target
    /// fields are filled in before the handler runs.
    /// </remarks>
    public bool Dispatch(TestElement target, Event uiEvent, bool bubble = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(uiEvent);
        Settle();

        if (!TryFindHandler(target.Element, uiEvent.Type, bubble, out var current, out var handler))
        {
            return false;
        }

        uiEvent.Target ??= target.Element;
        uiEvent.CurrentTarget = current;

        if (uiEvent is MouseEvent mouse)
        {
            PrepareMouseEvent(mouse, current);
        }

        try
        {
            if (!uiEvent.TryInvokeHandler(handler))
            {
                InvokeLegacyHandler(handler, uiEvent);
            }
        }
        finally
        {
            Settle();
        }

        return true;
    }

    internal IReadOnlyList<TestElement> FindAllWithin(ElementNode root, string selector)
    {
        return FindAllWithin(root, selector, includeSelf: false);
    }

    private IReadOnlyList<TestElement> FindAllWithin(
        IgniteSupportedDomNode root,
        string selector,
        bool includeSelf)
    {
        var table = GetQueryTable(selector);
        var context = _layoutOptions?.CreateRendererContext();
        var matches = new List<TestElement>();

        CollectMatches(root, table, context, matches, includeSelf);
        return matches;
    }

    private DataTable GetQueryTable(string selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);

        if (_queryTables.TryGetValue(selector, out var table))
        {
            return table;
        }

        try
        {
            // A custom property makes the selector match without altering any
            // supported style value. The table is private to querying.
            table = _dataEngine.Build(
                selector + " { --donkeh-test-query: 1; }");
        }
        catch (Exception ex)
        {
            throw new ComponentQueryException(
                $"The selector '{selector}' could not be parsed.",
                ex);
        }

        _queryTables[selector] = table;
        return table;
    }

    private void CollectMatches(
        IgniteSupportedDomNode node,
        DataTable table,
        RendererContext? context,
        List<TestElement> matches,
        bool includeSelf)
    {
        if (includeSelf && node is ElementNode element && MatchesQuery(table, element, context))
        {
            matches.Add(new TestElement(this, element));
        }

        var children = node.Children;

        for (var i = 0; i < children.Length; i++)
        {
            CollectMatches(children[i], table, context, matches, includeSelf: true);
        }
    }

    private bool MatchesQuery(DataTable table, ElementNode element, RendererContext? context)
    {
        var count = table.MatchRules(element, context, _queryMatches);

        if (count > _queryMatches.Length)
        {
            Array.Resize(ref _queryMatches, Math.Max(count, _queryMatches.Length * 2));
            count = table.MatchRules(element, context, _queryMatches);
        }

        return count > 0;
    }

    private void CollectByText(
        IgniteSupportedDomNode node,
        string text,
        StringComparison comparison,
        List<TestElement> matches)
    {
        if (node is ElementNode element)
        {
            var wrapper = new TestElement(this, element);

            if (string.Equals(wrapper.InnerText, text, comparison))
            {
                matches.Add(wrapper);
            }
        }

        var children = node.Children;

        for (var i = 0; i < children.Length; i++)
        {
            CollectByText(children[i], text, comparison, matches);
        }
    }

    private LayoutOptions CreateLayoutOptions()
    {
        var options = new LayoutOptions
        {
            ViewportWidth = _options.ViewportWidth,
            ViewportHeight = _options.ViewportHeight,
            RootScrolls = true,
            RootFontSize = _options.RootFontSize,
            DefaultFontSize = _options.DefaultFontSize,
            TextMeasurer = _options.TextMeasurer,
            TextProvider = GetDisplayText,
            IntrinsicSizeProvider = GetIntrinsicSize,
            Optimise = _options.Optimise,
            CullOffscreen = false,
            MergeDraws = false,
            EnableCaching = true,
            CacheTextLayouts = true,
            AnimationsEnabled = false,
            ReuseLayoutBoxes = false,
            SnapshotResults = true
        };

        return options;
    }

    private void Append(DataTable table, string? css)
    {
        if (!string.IsNullOrWhiteSpace(css))
        {
            _dataEngine.Build(Tokenizer.Parse(css), table);
        }
    }

    private void MarkInteractionChanged() => _interactionVersion++;

    private void SetHoverChain(ElementNode? element, bool hovered)
    {
        for (var current = element; current is not null; current = current.Parent as ElementNode)
        {
            current.IsHovered = hovered;
        }
    }

    private void SetActiveChain(ElementNode? element, bool active)
    {
        for (var current = element; current is not null; current = current.Parent as ElementNode)
        {
            current.IsActive = active;
        }

        MarkInteractionChanged();
    }

    private (float X, float Y) Center(TestElement target)
    {
        Settle();
        var layout = _layout;

        if (layout is null || !layout.TryGetBox(target.Element, out var box))
        {
            return (0, 0);
        }

        var rect = PaintedBorderBox(box);
        return (rect.X + rect.Width * 0.5f, rect.Y + rect.Height * 0.5f);
    }

    private void PrepareMouseEvent(MouseEvent mouse, ElementNode current)
    {
        var layout = _layout;

        if (layout is null || !layout.TryGetBox(current, out var box))
        {
            return;
        }

        var rect = PaintedBorderBox(box);
        mouse.LocalX = mouse.ClientX - rect.X;
        mouse.LocalY = mouse.ClientY - rect.Y;
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

    private static bool TryFindHandler(
        ElementNode target,
        string eventName,
        bool bubble,
        out ElementNode current,
        out Delegate handler)
    {
        for (var node = (IgniteSupportedDomNode?)target;
            node is not null;
            node = bubble ? node.Parent : null)
        {
            if (node is ElementNode candidate
                && candidate.TryGetEventHandler(eventName, out var candidateHandler)
                && candidateHandler is not null)
            {
                current = candidate;
                handler = candidateHandler;
                return true;
            }
        }

        current = null!;
        handler = null!;
        return false;
    }

    private static void InvokeLegacyHandler(Delegate handler, Event uiEvent)
    {
        switch (handler)
        {
            case Action action:
                action();
                return;

            case Action<string> stringAction when TryGetEventString(uiEvent, out var value):
                stringAction(value);
                return;

            case Action<float, float> pairAction when uiEvent is ScrollEvent scroll:
                pairAction(scroll.DeltaX, scroll.DeltaY);
                return;

            case Action<float, float> pairAction when uiEvent is PointerEvent pointer:
                pairAction(pointer.LocalX, pointer.LocalY);
                return;

            case Action<float, float> pairAction when uiEvent is MouseEvent mouse:
                pairAction(mouse.LocalX, mouse.LocalY);
                return;

            case Action<float> floatAction when uiEvent is MediaEvent media:
                floatAction(media.Time);
                return;
        }

        var parameters = handler.Method.GetParameters();

        if (parameters.Length == 0)
        {
            handler.DynamicInvoke();
            return;
        }

        throw new InvalidOperationException(
            $"The handler '{handler.Method.DeclaringType?.Name}.{handler.Method.Name}' " +
            $"cannot receive a '{uiEvent.Type}' event.");
    }

    private static bool TryGetEventString(Event uiEvent, out string value)
    {
        switch (uiEvent)
        {
            case InputEvent input:
                value = input.Value;
                return true;

            case ChangeEvent change:
                value = change.Value;
                return true;

            case PasteEvent paste:
                value = paste.Text;
                return true;

            case KeyDownEvent keyDown:
                value = keyDown.Chord;
                return true;

            default:
                value = string.Empty;
                return false;
        }
    }

    private static string GetControlValue(ElementNode element)
    {
        if (element.Tag == "textarea")
        {
            return element.TextContent ?? string.Empty;
        }

        return element.TryGetAttribute("value", out var value) ? value ?? string.Empty : string.Empty;
    }

    private static void SetControlValue(ElementNode element, string value)
    {
        if (element.Tag == "textarea")
        {
            element.TextContent = value;
        }
        else
        {
            element.SetAttribute("value", value);
        }
    }

    private ComponentTestHarness Media(TestElement target, MediaEvent mediaEvent)
    {
        ArgumentNullException.ThrowIfNull(target);
        Dispatch(target, mediaEvent);
        return this;
    }

    private static string? GetDisplayText(IgniteSupportedDomNode node)
    {
        if (node is not ElementNode element)
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

                var value = GetAttribute(element, "value");
                return string.IsNullOrEmpty(value)
                    ? GetAttribute(element, "placeholder")
                    : value;
            }

            case "textarea":
                return element.TextContent
                    ?? GetAttribute(element, "value")
                    ?? GetAttribute(element, "placeholder");

            case "select":
            {
                var options = FindOptions(element);

                if (options.Count == 0)
                {
                    return string.Empty;
                }

                for (var i = 0; i < options.Count; i++)
                {
                    if (options[i].TryGetAttribute("selected", out var ignoredSelected))
                    {
                        return options[i].TextContent
                            ?? GetAttribute(options[i], "value")
                            ?? string.Empty;
                    }
                }

                return options[0].TextContent
                    ?? GetAttribute(options[0], "value")
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

    private static Size? GetIntrinsicSize(IgniteSupportedDomNode node)
    {
        if (node is not ElementNode element)
        {
            return null;
        }

        switch (element.Tag)
        {
            case "img":
            {
                var width = HtmlDefaults.AttributeLength(element, "width", 0);
                var height = HtmlDefaults.AttributeLength(element, "height", 0);
                return width > 0 && height > 0 ? new Size(width, height) : null;
            }

            case "canvas":
            case "video":
            case "iframe":
            case "object":
            case "embed":
                return new Size(
                    HtmlDefaults.AttributeLength(element, "width", 300),
                    HtmlDefaults.AttributeLength(element, "height", 150));

            default:
                return null;
        }
    }

    private static List<ElementNode> FindOptions(ElementNode select)
    {
        var options = new List<ElementNode>();
        CollectOptions(select, options);
        return options;
    }

    private static void CollectOptions(IgniteSupportedDomNode node, List<ElementNode> options)
    {
        var children = node.Children;

        for (var i = 0; i < children.Length; i++)
        {
            if (children[i] is ElementNode element)
            {
                if (element.Tag == "option")
                {
                    options.Add(element);
                }

                CollectOptions(element, options);
            }
        }
    }

    private static string? GetAttribute(ElementNode element, string name) =>
        element.TryGetAttribute(name, out var value) ? value : null;
}
