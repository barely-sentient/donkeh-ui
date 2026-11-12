using System.Text;
using Donkeh.Data;
using Donkeh.Data.Dom;

namespace Donkeh.LayoutLab;

/// <summary>
/// Converts the matched CSS rules of a DOM tree into positioned layout boxes
/// and a compact paint-instruction stream.
/// </summary>
/// <remarks>
/// <para>
/// One engine instance is reusable across frames. It owns the style, text and
/// layout-box caches, together with the animation state of every animated node.
/// Cache entries are validated by the DOM revision, the inherited-value hash of
/// the containing style, a frame signature derived from the viewport and font
/// settings, and the renderer context version. Changing a key re-resolves the
/// affected node instead of forcing a cold rebuild of the whole tree.
/// </para>
/// <para>
/// A frame builds the box tree with <see cref="BuildBox"/>, lays it out through
/// <see cref="LayoutRootBox"/>, drains absolutely positioned boxes with
/// <see cref="ProcessAbsoluteBoxes"/> and finally emits paint instructions with
/// <see cref="BuildDisplayList"/>. The tree keeps unscrolled absolute
/// coordinates; scroll offsets are clamped per scroll container and applied as
/// paint-time translations, so scrolling never invalidates geometry.
/// </para>
/// <para>
/// Instances are not thread-safe and expect to be driven by a single layout
/// thread. When <see cref="LayoutOptions.ReuseLayoutBoxes"/> is enabled the
/// cached <see cref="LayoutBox"/> instances are mutated in place, which means a
/// previous <see cref="LayoutResult"/> is only stable when
/// <see cref="LayoutOptions.SnapshotResults"/> copied it.
/// </para>
/// </remarks>
/// <seealso cref="LayoutOptions"/>
/// <seealso cref="LayoutResult"/>
public sealed partial class LayoutEngine
{
    /// <summary>The compiled rule table used for selector matching and style resolution.</summary>
    private readonly DataTable _table;

    /// <summary>
    /// Shared cache of parsed CSS values, owned by the host when one is supplied.
    /// </summary>
    private readonly StyleValueCache _valueCache;

    /// <summary>
    /// Computed styles keyed by node. Entries are reused until the node revision,
    /// the parent's inherited-value hash, the frame signature or the renderer
    /// context version changes. See <see cref="ResolveStyle"/>.
    /// </summary>
    private readonly Dictionary<IgniteSupportedDomNode, StyleCacheEntry> _styleCache = [];

    /// <summary>Current-generation text layouts keyed by text, style and quantised width.</summary>
    private Dictionary<TextLayoutKey, TextLayout> _textCache = [];

    /// <summary>
    /// Previous-generation text layouts. The two maps rotate when the current
    /// map reaches 1024 entries, giving a bounded approximation of an LRU cache.
    /// </summary>
    private Dictionary<TextLayoutKey, TextLayout> _textCachePrevious = [];

    /// <summary>
    /// Reusable boxes keyed by node. Populated only when
    /// <see cref="LayoutOptions.ReuseLayoutBoxes"/> is enabled; detached entries
    /// are removed by <see cref="PruneBoxCache"/>.
    /// </summary>
    private readonly Dictionary<IgniteSupportedDomNode, LayoutBox> _boxCache = [];

    /// <summary>Scratch list of detached box-cache keys filled during pruning.</summary>
    private readonly List<IgniteSupportedDomNode> _staleBoxes = [];

    /// <summary>Scratch list of detached style-cache keys filled during pruning.</summary>
    private readonly List<IgniteSupportedDomNode> _staleStyles = [];

    /// <summary>
    /// Every node reached by <see cref="BuildBox"/> in the current frame,
    /// including <c>display:none</c> nodes that produced no box. Pruning uses it
    /// to keep styles for live but unpainted subtrees.
    /// </summary>
    private readonly HashSet<IgniteSupportedDomNode> _visited = [];

    /// <summary>
    /// Node-to-box lookup for the frame being laid out, exposed through
    /// <see cref="LayoutResult"/>.
    /// </summary>
    private readonly Dictionary<IgniteSupportedDomNode, LayoutBox> _byNode = [];

    /// <summary>
    /// Absolutely and fixed positioned boxes discovered while building the tree,
    /// in discovery order. They are laid out after normal flow by
    /// <see cref="ProcessAbsoluteBoxes"/>.
    /// </summary>
    private readonly List<LayoutBox> _absolutes = [];

    /// <summary>
    /// Boxes already processed by <see cref="ProcessAbsoluteBoxes"/>. It makes
    /// the recursive drain idempotent when nested absolutes are appended while
    /// an outer absolute is being laid out.
    /// </summary>
    private readonly HashSet<LayoutBox> _absoluteLaidOut = [];

    /// <summary>
    /// Scratch buffer for
    /// <see cref="DataTable.MatchRules(IgniteSupportedDomNode, RendererContext, Span{MatchedRule}, PseudoElementKind)"/>
    /// overload; grows to fit the largest match set.
    /// </summary>
    private MatchedRule[] _matchBuffer = new MatchedRule[16];

    /// <summary>Scratch buffer for pseudo-element rule matches.</summary>
    private MatchedRule[] _pseudoMatchBuffer = new MatchedRule[8];

    /// <summary>Synthetic DOM nodes used by generated <c>::before</c>/<c>::after</c> boxes.</summary>
    private readonly Dictionary<(IgniteSupportedDomNode Node, PseudoElementKind Kind), IgniteSupportedDomNode>
        _pseudoNodes = [];

    /// <summary>
    /// Synthetic text nodes used to give a flex/grid container's direct text an
    /// anonymous item, so it participates in the container's layout instead of
    /// being dropped when the element also has element children.
    /// </summary>
    private readonly Dictionary<IgniteSupportedDomNode, IgniteSupportedDomNode> _textNodes = [];

    /// <summary>
    /// Per-frame configuration, including the viewport, providers and scroll
    /// overrides.
    /// </summary>
    private LayoutOptions _options = new();

    /// <summary>
    /// Resolver created per frame; it writes into the shared
    /// <see cref="_valueCache"/>.
    /// </summary>
    private StyleResolver _resolver = null!;

    /// <summary>
    /// Root length context carrying viewport and root font metrics for the frame.
    /// </summary>
    private LengthContext _baseContext;

    /// <summary>Renderer environment used for media and container queries on this frame.</summary>
    private RendererContext _rendererContext = new();

    /// <summary>
    /// Text measurer selected from <see cref="LayoutOptions.TextMeasurer"/>
    /// for this frame.
    /// </summary>
    private ITextMeasurer _measurer = SimpleTextMeasurer.Instance;

    /// <summary>
    /// Hash of viewport, font, orientation, pixel-ratio and renderer-context
    /// inputs. Style-cache entries are invalid when this value changes.
    /// </summary>
    private long _styleSignature;

    /// <summary>
    /// Renderer-context version used as a style-cache key. It is the full
    /// viewport-sensitive version when viewport height can affect styles.
    /// </summary>
    private long _contextVersion;

    /// <summary>Paint-instruction buffer for the current frame.</summary>
    private InstructionBuffer _instructions = new();

    /// <summary>Reusable destination buffer for <see cref="Optimiser.Run"/>.</summary>
    private readonly InstructionBuffer _optimisedInstructions = new();

    /// <summary>Constant pool referenced by the current frame's instructions.</summary>
    private ConstantPool _constants = new();

    /// <summary>Holds per-node animation clocks and running states across frames.</summary>
    private readonly AnimationRuntime _animations;

    /// <summary>Scratch list of fixed boxes deferred to the second paint pass.</summary>
    private readonly List<LayoutBox> _fixedBoxes = [];

    /// <summary>Positive z-index boxes deferred above the normal paint tree.</summary>
    private readonly List<(LayoutBox Box, float Tx, float Ty)> _overlayBoxes = [];

    /// <summary>True while the deferred positive z-index pass is running.</summary>
    private bool _paintingOverlays;

    /// <summary>
    /// Pooled child lists indexed by flex/grid nesting depth. Each nested layout
    /// gets its own list so an outer item list is not overwritten mid-iteration.
    /// </summary>
    private readonly List<List<LayoutBox>> _flowItemLists = [];

    /// <summary>Current nesting depth into <see cref="_flowItemLists"/>.</summary>
    private int _flowItemDepth;

    /// <summary>True while the deferred fixed-position paint pass is running.</summary>
    private bool _paintingFixed;

    /// <summary>
    /// Set when any resolved style uses a viewport-height unit. The style
    /// signature is computed before this flag is reset, so the next signature
    /// includes the viewport height.
    /// </summary>
    private bool _usesViewportHeight;

    /// <summary>Layout viewport used as the containing block for fixed boxes.</summary>
    private Rect _layoutViewport;

    /// <summary>
    /// Root box of the current frame, or <see langword="null"/> when the root is
    /// not displayed.
    /// </summary>
    private LayoutBox? _root;

    /// <summary>
    /// Effective tree-depth cap for <see cref="BuildBox"/>, clamped to at least one.
    /// </summary>
    private int _maxDepth = 512;

    /// <summary>Initializes a layout engine bound to a compiled rule table.</summary>
    /// <param name="table">The CSS table used to match rules and resolve computed styles.</param>
    /// <param name="valueCache">
    /// Optional parsed-value cache to share with other consumers. When omitted the
    /// engine creates and owns one.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="table"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The engine retains both dependencies for its lifetime and never disposes
    /// them. Reuse one engine per document host so the style, text and box caches
    /// survive between frames; create a separate instance for independent
    /// documents or threads.
    /// </remarks>
    public LayoutEngine(DataTable table, StyleValueCache? valueCache = null)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _valueCache = valueCache ?? new StyleValueCache();
        _animations = new AnimationRuntime(table);
    }

    /// <summary>Gets the parsed CSS value cache used by the style resolver.</summary>
    /// <value>
    /// The cache supplied to the constructor, or a cache created by the engine.
    /// </value>
    /// <remarks>
    /// The cache deduplicates resolved values such as lengths and colours. It is
    /// cleared by <see cref="ClearCaches"/>; other engine caches do not affect it.
    /// </remarks>
    public StyleValueCache ValueCache => _valueCache;

    /// <summary>Number of nodes with retained animation state, for tests and diagnostics.</summary>
    /// <value>The number of entries currently held by the animation runtime.</value>
    /// <remarks>
    /// Entries are created for animated nodes and pruned after a layout or
    /// animated repaint when their node is no longer visited.
    /// </remarks>
    public int AnimationStateCount => _animations.StateCount;

    /// <summary>Lays out a frame with default <see cref="LayoutOptions"/>.</summary>
    /// <param name="root">Root of the DOM subtree to lay out.</param>
    /// <returns>The frame's box tree, display list, constant pool and node map.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="root"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Equivalent to <see cref="Layout(IgniteSupportedDomNode, LayoutOptions?)"/>
    /// with <see langword="null"/> options. A fresh options object is created and
    /// discarded, so viewport size, providers and scroll offsets use their
    /// defaults.
    /// </remarks>
    public LayoutResult Layout(IgniteSupportedDomNode root)
    {
        return Layout(root, null);
    }

    /// <summary>Lays out a frame, reusing the engine's style and text caches.</summary>
    /// <param name="root">
    /// Root of the DOM subtree to lay out. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="options">
    /// Frame configuration. When <see langword="null"/>, a default instance is used.
    /// </param>
    /// <returns>
    /// A <see cref="LayoutResult"/> containing the root box, the optimised or raw
    /// instruction stream, constants, the node-to-box map and optimisation stats.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="root"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The pass runs in this order: cache selection and frame setup, box-tree
    /// construction, cache pruning, root layout, absolute-position drain and
    /// display-list building. Normal flow, flex, grid and table layout all share
    /// the same resolved style and box caches.
    /// </para>
    /// <para>
    /// When <see cref="LayoutOptions.Optimise"/> is enabled the instruction
    /// stream is passed through <see cref="Optimiser.Run"/>; otherwise the raw
    /// buffer is returned. <see cref="LayoutOptions.SnapshotResults"/> decides
    /// whether the returned stream and node map are copies or aliases of the
    /// engine's reusable storage.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var engine = new LayoutEngine(table);
    /// var options = new LayoutOptions
    /// {
    ///     ViewportWidth = 1280,
    ///     ViewportHeight = 720,
    ///     TextMeasurer = hostMeasurer
    /// };
    ///
    /// var frame = engine.Layout(root, options);
    /// var instructions = frame.Opcodes;
    /// </code>
    /// </example>
    public LayoutResult Layout(IgniteSupportedDomNode root, LayoutOptions? options)
    {
        ArgumentNullException.ThrowIfNull(root);

        _options = options ?? new LayoutOptions();
        _maxDepth = Math.Max(1, _options.MaxLayoutDepth);
        _baseContext = _options.CreateRootLengthContext();
        _rendererContext = _options.CreateRendererContext();
        _contextVersion = _usesViewportHeight || _table.HasHeightMediaQueries
            ? _rendererContext.Version
            : _rendererContext.ConditionVersion;
        _styleSignature = ComputeStyleSignature();
        _measurer = _options.TextMeasurer ?? SimpleTextMeasurer.Instance;
        _resolver = new StyleResolver(_table, _valueCache, _options);

        _instructions.Clear();
        _constants.Clear();
        _byNode.Clear();
        _absolutes.Clear();
        _absoluteLaidOut.Clear();
        _visited.Clear();
        _usesViewportHeight = false;
        _animations.BeginFrame();

        if (!_options.ReuseLayoutBoxes)
        {
            _boxCache.Clear();
        }

        RefreshSuppression(root);

        _root = BuildBox(root, null, null, null, 0);

        if (_options.ReuseLayoutBoxes)
        {
            PruneBoxCache();
        }

        _animations.Prune();

        var viewport = new Rect(0, 0, MathF.Max(0, _options.ViewportWidth), MathF.Max(0, _options.ViewportHeight));
        _layoutViewport = viewport;

        if (_root is not null)
        {
            LayoutRootBox(_root, viewport);
            ProcessAbsoluteBoxes(viewport);
            BuildDisplayList(_root);
        }

        var frame = _options.Optimise
            ? Optimiser.Run(_instructions, _constants, viewport, _options, _optimisedInstructions)
            : new OptimisationResult(_instructions, _constants, OptimisationStats.Empty);

        return new LayoutResult(
            _root,
            frame.Instructions,
            frame.Constants,
            _byNode,
            frame.Stats,
            _animations.HasRunningAnimations,
            snapshot: _options.SnapshotResults,
            hasLayoutAnimations: _animations.HasLayoutAnimations);
    }

    /// <summary>
    /// Rebuilds only the display list for a layout whose geometry is still
    /// valid. Scroll offsets never move boxes, so a scroll can repaint without
    /// rebuilding the box tree or re-resolving styles.
    /// </summary>
    /// <param name="previous">A frame whose geometry is still valid.</param>
    /// <param name="options">
    /// Configuration for the repaint. Its viewport must match the viewport used
    /// to produce <paramref name="previous"/>; changed styles are not re-resolved.
    /// </param>
    /// <returns>A new result that reuses <paramref name="previous"/>'s box tree.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="previous"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Scroll metrics are recomputed from the existing geometry so offsets can
    /// be re-clamped, then the display list is emitted with the new offsets.
    /// Style and text caches are not consulted. Box geometry is not mutated;
    /// only scroll metrics and scroll offsets are refreshed.
    /// </para>
    /// <para>
    /// When <paramref name="previous"/> was created without
    /// <see cref="LayoutOptions.SnapshotResults"/> its node map is the engine's
    /// live map; the engine detects that case instead of clearing it. Passing a
    /// result from a different engine is not supported.
    /// </para>
    /// </remarks>
    public LayoutResult Repaint(LayoutResult previous, LayoutOptions? options)
    {
        ArgumentNullException.ThrowIfNull(previous);

        _options = options ?? new LayoutOptions();
        _maxDepth = Math.Max(1, _options.MaxLayoutDepth);
        _baseContext = _options.CreateRootLengthContext();
        _rendererContext = _options.CreateRendererContext();
        _contextVersion = _usesViewportHeight || _table.HasHeightMediaQueries
            ? _rendererContext.Version
            : _rendererContext.ConditionVersion;
        _styleSignature = ComputeStyleSignature();
        _measurer = _options.TextMeasurer ?? SimpleTextMeasurer.Instance;
        _resolver = new StyleResolver(_table, _valueCache, _options);

        _instructions.Clear();
        _constants.Clear();
        _animations.BeginFrame();

        // When results are not snapshotted the previous result already points
        // at this engine's node map. Clearing it would erase the source and
        // leave every box lookup empty after a scroll repaint.
        if (!ReferenceEquals(previous.Boxes, _byNode))
        {
            _byNode.Clear();

            foreach (var pair in previous.Boxes)
            {
                _byNode[pair.Key] = pair.Value;
            }
        }

        _root = previous.Root;

        var viewport = new Rect(0, 0, MathF.Max(0, _options.ViewportWidth), MathF.Max(0, _options.ViewportHeight));
        _layoutViewport = viewport;

        if (_root is not null)
        {
            ApplyRootScrollBehavior(_root);
            RefreshScrollMetrics(_root);
            BuildDisplayList(_root);
        }

        var frame = _options.Optimise
            ? Optimiser.Run(_instructions, _constants, viewport, _options, _optimisedInstructions)
            : new OptimisationResult(_instructions, _constants, OptimisationStats.Empty);

        return new LayoutResult(
            _root,
            frame.Instructions,
            frame.Constants,
            _byNode,
            frame.Stats,
            previous.HasAnimations,
            snapshot: _options.SnapshotResults,
            hasLayoutAnimations: previous.HasLayoutAnimations);
    }

    /// <summary>
    /// Re-evaluates paint-only animations and rebuilds the display list without
    /// running geometry. Transform/opacity animations do not move boxes, so
    /// this avoids a full layout allocation every animation frame.
    /// </summary>
    /// <param name="previous">A frame whose geometry is still valid.</param>
    /// <param name="options">
    /// Configuration for the repaint, including
    /// <see cref="LayoutOptions.AnimationTimeSeconds"/>.
    /// </param>
    /// <returns>A new result whose styles reflect the current animation time.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="previous"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Each box style is rebuilt from the cached base style plus the animation
    /// values for the supplied clock, using the animated parent style for
    /// inheritance. No style resolution, box construction or geometry pass runs.
    /// If an animation can change geometry, use
    /// <see cref="Layout(IgniteSupportedDomNode, LayoutOptions)"/> instead;
    /// <see cref="LayoutResult.HasLayoutAnimations"/> reports that condition.
    /// </remarks>
    public LayoutResult RepaintAnimated(LayoutResult previous, LayoutOptions? options)
    {
        ArgumentNullException.ThrowIfNull(previous);

        _options = options ?? new LayoutOptions();
        _maxDepth = Math.Max(1, _options.MaxLayoutDepth);
        _baseContext = _options.CreateRootLengthContext();
        _rendererContext = _options.CreateRendererContext();
        _contextVersion = _usesViewportHeight || _table.HasHeightMediaQueries
            ? _rendererContext.Version
            : _rendererContext.ConditionVersion;
        _styleSignature = ComputeStyleSignature();
        _measurer = _options.TextMeasurer ?? SimpleTextMeasurer.Instance;
        _resolver = new StyleResolver(_table, _valueCache, _options);

        _instructions.Clear();
        _constants.Clear();
        _animations.BeginFrame();

        if (!ReferenceEquals(previous.Boxes, _byNode))
        {
            _byNode.Clear();

            foreach (var pair in previous.Boxes)
            {
                _byNode[pair.Key] = pair.Value;
            }
        }

        _root = previous.Root;

        if (_root is not null)
        {
            RefreshAnimatedStyles(_root, null);

            // Animation refresh rebuilds every style from the cached base
            // style, which does not contain the root's viewport-scrolling
            // behaviour. Reapply it so scrolling survives an animated frame.
            ApplyRootScrollBehavior(_root);
        }

        _animations.Prune();

        var viewport = new Rect(0, 0, MathF.Max(0, _options.ViewportWidth), MathF.Max(0, _options.ViewportHeight));
        _layoutViewport = viewport;

        if (_root is not null)
        {
            BuildDisplayList(_root);
        }

        var frame = _options.Optimise
            ? Optimiser.Run(_instructions, _constants, viewport, _options, _optimisedInstructions)
            : new OptimisationResult(_instructions, _constants, OptimisationStats.Empty);

        return new LayoutResult(
            _root,
            frame.Instructions,
            frame.Constants,
            _byNode,
            frame.Stats,
            _animations.HasRunningAnimations,
            snapshot: _options.SnapshotResults,
            hasLayoutAnimations: _animations.HasLayoutAnimations);
    }

    /// <summary>
    /// Rewrites the style of an existing box tree from cached base styles and
    /// the animation clock, without re-resolving selectors or recomputing geometry.
    /// </summary>
    /// <param name="box">Subtree root to refresh, or <see langword="null"/>.</param>
    /// <param name="parentStyle">
    /// Animated style of the box's parent, used as the inheritance source.
    /// </param>
    /// <remarks>
    /// The base style comes from <see cref="_styleCache"/> when present so
    /// repeated repaints do not compound animation transforms; otherwise the
    /// box's current style is treated as the base. Styles are shared by
    /// reference with descendants through the recursive call.
    /// </remarks>
    private void RefreshAnimatedStyles(LayoutBox? box, ComputedStyle? parentStyle)
    {
        if (box is null)
        {
            return;
        }

        var baseStyle = _options.EnableCaching
            && _styleCache.TryGetValue(box.Node, out var entry)
            ? entry.Style
            : box.Style;
        var style = _animations.Apply(
            box.Node,
            baseStyle,
            parentStyle,
            _options.AnimationTimeSeconds);
        box.Style = style;

        for (var i = 0; i < box.Children.Count; i++)
        {
            RefreshAnimatedStyles(box.Children[i], style);
        }
    }

    /// <summary>Re-clamps scroll offsets without touching box geometry.</summary>
    /// <param name="box">Subtree root whose scroll metrics are refreshed.</param>
    /// <remarks>
    /// Recursive and allocation-free. It recomputes overflow from the existing
    /// child boxes and re-reads offsets from <see cref="LayoutOptions"/>, which
    /// makes it the geometry-preserving half of <see cref="Repaint"/>.
    /// </remarks>
    private void RefreshScrollMetrics(LayoutBox box)
    {
        UpdateScrollMetrics(box);

        for (var i = 0; i < box.Children.Count; i++)
        {
            RefreshScrollMetrics(box.Children[i]);
        }
    }

    /// <summary>Drops every cached style, text layout and reusable box.</summary>
    /// <remarks>
    /// Also clears the shared <see cref="ValueCache"/>, so callers that supplied
    /// that cache lose its parsed values too. Animation state, the constant pool
    /// and layout options are left untouched. Use this when the rule table or
    /// font environment changes in a way the per-entry keys cannot observe.
    /// </remarks>
    public void ClearCaches()
    {
        _styleCache.Clear();
        _textCache.Clear();
        _textCachePrevious.Clear();
        _boxCache.Clear();
        _valueCache.Clear();
    }

    /// <summary>
    /// Removes cached boxes for nodes that are no longer part of the mounted
    /// tree. Without this the cache would keep detached devtools or rerendered
    /// subtrees alive forever.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Box entries are compared against <see cref="_byNode"/>, which contains
    /// only nodes that produced a box this frame. Style entries are compared
    /// against <see cref="_visited"/> instead, because <c>display:none</c> nodes
    /// (head, closed dropdowns, options) have no box but are still live and must
    /// keep their resolved style.
    /// </para>
    /// <para>
    /// Runs only when <see cref="LayoutOptions.ReuseLayoutBoxes"/> is enabled.
    /// Both loops fill a scratch list first so the dictionaries are not mutated
    /// during enumeration.
    /// </para>
    /// </remarks>
    private void PruneBoxCache()
    {
        _staleBoxes.Clear();

        foreach (var pair in _boxCache)
        {
            if (!_byNode.ContainsKey(pair.Key))
            {
                _staleBoxes.Add(pair.Key);
            }
        }

        for (var i = 0; i < _staleBoxes.Count; i++)
        {
            _boxCache.Remove(_staleBoxes[i]);
        }

        // The devtools rebuilds its inspector tree periodically, so styles for
        // detached nodes would otherwise stay reachable forever and promote
        // into gen2.
        _staleStyles.Clear();

        foreach (var pair in _styleCache)
        {
            // Use the visited set rather than the box map: display:none nodes
            // (head, closed dropdowns, select options) have no box but are
            // still part of the live tree and must keep their cached style.
            if (!_visited.Contains(pair.Key))
            {
                _staleStyles.Add(pair.Key);
            }
        }

        for (var i = 0; i < _staleStyles.Count; i++)
        {
            _styleCache.Remove(_staleStyles[i]);
        }
    }

    /// <summary>
    /// Builds the frame-level style-cache key from viewport, font, orientation,
    /// pixel-ratio and renderer-context inputs.
    /// </summary>
    /// <returns>A hash that changes whenever a global style input changes.</returns>
    /// <remarks>
    /// Viewport width is always included. Viewport height is included only when
    /// the previous build saw a viewport-height unit or the table contains
    /// height media queries; otherwise resizing the height alone leaves the key
    /// stable. This runs before <see cref="_usesViewportHeight"/> is reset for
    /// the new frame, which is what carries that knowledge forward.
    /// </remarks>
    private long ComputeStyleSignature()
    {
        var hash = new HashCode();
        hash.Add(_options.ViewportWidth);

        if (_usesViewportHeight || _table.HasHeightMediaQueries)
        {
            hash.Add(_options.ViewportHeight);
        }

        hash.Add(_options.Orientation);
        hash.Add(_options.RootFontSize);
        hash.Add(_options.DefaultFontSize);
        hash.Add(_options.DevicePixelRatio);
        hash.Add(_contextVersion);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Hash of the properties a child inherits. It lets the style cache notice
    /// when an animated parent's inherited values changed even though the
    /// parent style object itself is reused between frames.
    /// </summary>
    /// <param name="style">Parent style whose inheritable values are hashed.</param>
    /// <returns>
    /// A hash of colour, font, text, writing-mode and border-collapse state.
    /// </returns>
    /// <remarks>
    /// Two parent styles that hash equal may still differ in non-inherited
    /// properties; that is intentional, because only inherited values can change
    /// a child's computed style without the child itself changing.
    /// </remarks>
    private static long InheritedHash(ComputedStyle style)
    {
        var hash = new HashCode();
        hash.Add(style.Color);
        hash.Add(style.FontFamily);
        hash.Add(style.FontSize);
        hash.Add(style.FontWeight);
        hash.Add(style.Italic);
        hash.Add(style.LineHeight);
        hash.Add(style.LetterSpacing);
        hash.Add(style.TextAlign);
        hash.Add(style.WhiteSpace);
        hash.Add(style.OverflowWrap);
        hash.Add(style.TextOverflow);
        hash.Add(style.TextTransform);
        hash.Add(style.Direction);
        hash.Add(style.WritingMode);
        hash.Add(style.Visibility);
        hash.Add(style.BorderCollapse);
        hash.Add(style.BorderSpacing);
        return hash.ToHashCode();
    }

    // ---------------------------------------------------------------- styles

    /// <summary>
    /// A cached computed style plus the keys that decide whether it is still valid.
    /// </summary>
    private sealed class StyleCacheEntry
    {
        /// <summary>The resolved style returned to callers while the entry is valid.</summary>
        public required ComputedStyle Style;

        /// <summary>DOM revision of the node when the style was resolved.</summary>
        public int Revision;

        /// <summary>Hash of the parent's inheritable values at resolution time.</summary>
        public long ParentInheritedHash;

        /// <summary>Frame signature (<see cref="_styleSignature"/>) at resolution time.</summary>
        public long Signature;

        /// <summary>Renderer-context version used when the entry was created.</summary>
        public long ContextVersion;
    }

    /// <summary>
    /// Returns the computed style for a node, reusing a cached entry when all
    /// keys match.
    /// </summary>
    /// <param name="node">Node whose style is requested.</param>
    /// <param name="parent">
    /// Parent computed style, or <see langword="null"/> for the root.
    /// </param>
    /// <returns>
    /// The node's computed style, shared by reference with later frames while valid.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A hit requires the node revision, the parent's inherited-value hash, the
    /// frame signature and the renderer context version to match. Comparing the
    /// inherited hash instead of the parent object identity avoids re-resolving
    /// descendants when an animation clones a parent style without changing any
    /// inherited value.
    /// </para>
    /// <para>
    /// On a miss the rules are matched twice when the scratch buffer is too
    /// small: the first call reports the required count, the buffer is resized
    /// and the match is repeated. The resulting entry replaces any previous one.
    /// </para>
    /// </remarks>
    private ComputedStyle ResolveStyle(IgniteSupportedDomNode node, ComputedStyle? parent)
    {
        // Compare the inherited values rather than the parent object identity.
        // Animation clones and other transient parent styles can be new objects
        // while their inherited values are unchanged; keying on identity made
        // every descendant re-resolve its whole style chain each frame.
        var parentInheritedHash = parent is not null ? InheritedHash(parent) : 0;
        var useCache = _options.EnableCaching;
        StyleCacheEntry? entry = null;

        if (useCache)
        {
            _styleCache.TryGetValue(node, out entry);

            if (entry is not null
                && entry.Revision == node.Revision
                && entry.ParentInheritedHash == parentInheritedHash
                && entry.Signature == _styleSignature
                && entry.ContextVersion == _contextVersion)
            {
                return entry.Style;
            }
        }

        var count = _table.MatchRules(node, _rendererContext, _matchBuffer);

        if (count > _matchBuffer.Length)
        {
            Array.Resize(ref _matchBuffer, Math.Max(count, _matchBuffer.Length * 2));
            count = _table.MatchRules(node, _rendererContext, _matchBuffer);
        }

        var style = _resolver.Resolve(node, parent, _matchBuffer.AsSpan(0, count));

        if (useCache)
        {
            _styleCache[node] = new StyleCacheEntry
            {
                Style = style,
                Revision = node.Revision,
                ParentInheritedHash = parentInheritedHash,
                Signature = _styleSignature,
                ContextVersion = _contextVersion
            };
        }

        return style;
    }

    /// <summary>
    /// Marks every element whose computed <c>display</c> is <c>none</c> as
    /// suppressed so structural selectors only see rendered siblings.
    /// </summary>
    /// <param name="root">Root of the frame's DOM tree.</param>
    /// <remarks>
    /// <para>
    /// Host trees keep declarative scaffolding in the child list that a browser
    /// would not mount, for example a closed dropdown menu. Those nodes generate
    /// no box, but a browser built from the mounted markup would not have them as
    /// siblings at all; <c>:last-child</c> and <c>:first-child</c> would see a
    /// different child list. Suppressing them keeps selector matching and box
    /// construction consistent.
    /// </para>
    /// <para>
    /// A first pass resolves display for the whole tree. If any flag changed, the
    /// styles resolved before the change were matched against the old sibling set,
    /// so the pass repeats with a cleared style cache until the flags settle. Later
    /// frames normally make a single pass that hits every cached style.
    /// </para>
    /// </remarks>
    private void RefreshSuppression(IgniteSupportedDomNode root)
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            var changed = false;
            ResolveSuppression(root, null, ref changed);

            if (!changed)
            {
                return;
            }

            _styleCache.Clear();
        }

        var settled = false;
        ResolveSuppression(root, null, ref settled);
    }

    /// <summary>Resolves display for one subtree and updates suppression flags.</summary>
    /// <param name="node">Subtree root.</param>
    /// <param name="parentStyle">Inherited style of the parent, or <see langword="null"/>.</param>
    /// <param name="changed">Set to <see langword="true"/> when a flag flipped.</param>
    private void ResolveSuppression(
        IgniteSupportedDomNode node,
        ComputedStyle? parentStyle,
        ref bool changed)
    {
        var style = ResolveStyle(node, parentStyle);
        var suppressed = style.Display == Display.None;

        if (node.IsSuppressed != suppressed)
        {
            node.IsSuppressed = suppressed;
            changed = true;
        }

        if (suppressed)
        {
            return;
        }

        var children = node.Children;

        for (var i = 0; i < children.Length; i++)
        {
            if (children[i] is { } child)
            {
                ResolveSuppression(child, style, ref changed);
            }
        }
    }

    /// <summary>Returns the text a node contributes to layout.</summary>
    /// <param name="node">Node whose text is requested.</param>    /// <returns>
    /// The text from <see cref="LayoutOptions.TextProvider"/> when supplied,
    /// otherwise <see cref="IgniteSupportedDomNode.TextContent"/>.
    /// </returns>
    private string? GetNodeText(IgniteSupportedDomNode node)
    {
        return _options.TextProvider is { } provider ? provider(node) : node.TextContent;
    }

    // -------------------------------------------------------------- box tree

    /// <summary>
    /// Builds the box tree for a node and its descendants, resolving styles and
    /// reusing cached boxes when enabled.
    /// </summary>
    /// <param name="node">Node to build a box for.</param>
    /// <param name="parentStyle">
    /// Parent computed style, or <see langword="null"/> for the root.
    /// </param>
    /// <param name="parentBox">Box of the parent, or <see langword="null"/> for the root.</param>
    /// <param name="positionedAncestor">
    /// Nearest ancestor that creates a containing block for absolutely positioned
    /// descendants, or <see langword="null"/> when the viewport is their containing block.
    /// </param>
    /// <param name="depth">Zero-based depth used for the recursion cap and box metadata.</param>
    /// <returns>
    /// The new or recycled box, or <see langword="null"/> when the node is
    /// <c>display:none</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The node is added to <see cref="_visited"/> before the
    /// <c>display:none</c> check, so pruning keeps styles for live but unpainted
    /// subtrees. When <see cref="LayoutOptions.ReuseLayoutBoxes"/> is enabled a
    /// cached box is reset and reused instead of allocated.
    /// </para>
    /// <para>
    /// Animations are applied on top of the cached base style. Absolutely and
    /// fixed positioned boxes are queued in <see cref="_absolutes"/>; fixed boxes
    /// record <see langword="null"/> as their containing block and are later
    /// resolved against the layout viewport. A positioned or transformed box
    /// becomes the containing block for absolutely positioned descendants.
    /// The recursion stops adding children once <paramref name="depth"/> reaches
    /// <see cref="_maxDepth"/>, but still returns the box itself.
    /// </para>
    /// </remarks>
    private LayoutBox? BuildBox(
        IgniteSupportedDomNode node,
        ComputedStyle? parentStyle,
        LayoutBox? parentBox,
        LayoutBox? positionedAncestor,
        int depth)
    {
        _visited.Add(node);
        var style = ResolveStyle(node, parentStyle);

        if (_options.AnimationsEnabled)
        {
            style = _animations.Apply(node, style, parentStyle, _options.AnimationTimeSeconds);
        }

        if (style.Display == Display.None)
        {
            return null;
        }

        if (style.UsesViewportHeight)
        {
            _usesViewportHeight = true;
        }

        LayoutBox box;

        if (_options.ReuseLayoutBoxes && _boxCache.TryGetValue(node, out var cached))
        {
            cached.Reset(style, depth, parentBox);
            cached.IsContents = style.Display == Display.Contents;
            box = cached;
        }
        else
        {
            box = new LayoutBox(node, style, depth, node.Children.Length)
            {
                Parent = parentBox,
                IsContents = style.Display == Display.Contents
            };

            if (_options.ReuseLayoutBoxes)
            {
                _boxCache[node] = box;
            }
        }

        if (style.IsAbsolutelyPositioned)
        {
            box.ContainingBlock = style.Position == Position.Fixed
                ? FindFixedContainingAncestor(parentBox)
                : positionedAncestor;
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

        // A flex or grid container's own text is an anonymous item. When the
        // element also has real children (an accordion button with a generated
        // ::after icon, for example), giving the text a synthetic text child is
        // the only way it can take part in flex/grid placement. Text-only
        // containers still use the block path in LayoutContent.
        if ((style.IsFlexContainer || style.IsGridContainer)
            && !string.IsNullOrEmpty(node.TextContent))
        {
            var textStyle = new ComputedStyle();
            textStyle.CopyInheritedFromShared(style);
            textStyle.Display = Display.Inline;

            var textNode = GetOrCreateTextNode(node);

            var textBox = new LayoutBox(textNode, textStyle, depth + 1, 0)
            {
                Parent = box
            };

            box.Children.Insert(0, textBox);
        }

        // Generated content participates in the element's formatting context:
        // ::before is the first child and ::after is the last child. They are
        // matched against the originating element and inherit its style.
        var before = BuildPseudoBox(node, style, box, childPositionedAncestor, depth + 1, PseudoElementKind.Before);

        if (before is not null)
        {
            box.Children.Insert(0, before);
        }

        var after = BuildPseudoBox(node, style, box, childPositionedAncestor, depth + 1, PseudoElementKind.After);

        if (after is not null)
        {
            box.Children.Add(after);
        }

        return box;
    }

    /// <summary>Returns the cached synthetic text node for an element.</summary>
    /// <param name="owner">Element whose direct text is being represented.</param>
    /// <returns>A text node carrying the element's current text.</returns>
    private IgniteSupportedDomNode GetOrCreateTextNode(IgniteSupportedDomNode owner)
    {
        if (!_textNodes.TryGetValue(owner, out var node))
        {
            node = new IgniteSupportedDomNode { IsTextNode = true, Parent = owner };
            _textNodes[owner] = node;
        }

        node.TextContent = owner.TextContent;
        return node;
    }

    /// <summary>
    /// Builds a generated <c>::before</c>/<c>::after</c> box when the
    /// stylesheet declares one for an element.
    /// </summary>
    /// <param name="node">Originating element.</param>
    /// <param name="parentStyle">The element's computed style.</param>
    /// <param name="parentBox">The element's layout box.</param>
    /// <param name="positionedAncestor">Containing block for absolute pseudo content.</param>
    /// <param name="depth">Depth assigned to the generated box.</param>
    /// <param name="kind">Which pseudo-element to build.</param>
    /// <returns>The generated box, or <see langword="null"/> when the rule set has no content.</returns>
    /// <remarks>
    /// The pseudo rules are matched with the element node as the subject, then
    /// applied to a fresh style inherited from the element. This keeps the
    /// selector engine unchanged for normal elements while allowing the cascade
    /// and custom properties to flow into generated content.
    /// </remarks>
    private LayoutBox? BuildPseudoBox(
        IgniteSupportedDomNode node,
        ComputedStyle parentStyle,
        LayoutBox parentBox,
        LayoutBox? positionedAncestor,
        int depth,
        PseudoElementKind kind)
    {
        var count = _table.MatchRules(node, _rendererContext, _pseudoMatchBuffer, kind);

        if (count == 0)
        {
            return null;
        }

        if (count > _pseudoMatchBuffer.Length)
        {
            Array.Resize(ref _pseudoMatchBuffer, Math.Max(count, _pseudoMatchBuffer.Length * 2));
            count = _table.MatchRules(node, _rendererContext, _pseudoMatchBuffer, kind);
        }

        var style = _resolver.ResolvePseudo(node, parentStyle, _pseudoMatchBuffer.AsSpan(0, count));

        if (style.Display == Display.None || style.Content is null)
        {
            return null;
        }

        var key = (node, kind);

        if (!_pseudoNodes.TryGetValue(key, out var pseudoNode))
        {
            pseudoNode = new IgniteSupportedDomNode
            {
                TagName = node.TagName,
                Parent = node
            };

            _pseudoNodes[key] = pseudoNode;
        }

        pseudoNode.TextContent = style.Content;

        var box = new LayoutBox(pseudoNode, style, depth, 0)
        {
            Parent = parentBox,
            IsContents = style.Display == Display.Contents,
            PseudoElementKind = kind
        };

        if (style.IsAbsolutelyPositioned)
        {
            box.ContainingBlock = style.Position == Position.Fixed
                ? FindFixedContainingAncestor(parentBox)
                : positionedAncestor;
            _absolutes.Add(box);
        }

        return box;
    }

    /// <summary>
    /// Returns whether a style makes its box a containing block for positioned
    /// descendants.
    /// </summary>
    /// <param name="style">Style to inspect.</param>
    /// <returns>
    /// <see langword="true"/> for non-static positioning or any transform;
    /// otherwise <see langword="false"/>.
    /// </returns>
    private static bool CreatesContainingBlock(ComputedStyle style)
    {
        return style.Position is not Position.Static
            || style.Transform is not null
            || style.Filter is not null
            || style.BackdropFilter is not null;
    }

    /// <summary>Finds the nearest ancestor that establishes a fixed containing block.</summary>
    /// <param name="parent">Parent box to start from, or <see langword="null"/>.</param>
    /// <returns>
    /// The nearest ancestor with a transform or filter, or <see langword="null"/>
    /// when fixed positioning should use the viewport.
    /// </returns>
    /// <remarks>
    /// Positioned ancestors alone do not contain fixed boxes. Opacity is not
    /// included because it creates a stacking context but not a containing block.
    /// </remarks>
    private static LayoutBox? FindFixedContainingAncestor(LayoutBox? parent)
    {
        for (var box = parent; box is not null; box = box.Parent)
        {
            if (box.Style.Transform is not null
                || box.Style.Filter is not null
                || box.Style.BackdropFilter is not null)
            {
                return box;
            }
        }

        return null;
    }

    /// <summary>Lays out the root box against the viewport containing block.</summary>
    /// <param name="root">Root box built for the frame.</param>
    /// <param name="viewport">Layout viewport in absolute CSS pixels.</param>
    /// <remarks>
    /// A <c>display:contents</c> root generates no box of its own: its margins,
    /// border and padding are zeroed, its border box is forced to the viewport
    /// and its children are laid out directly. A regular root fills the viewport
    /// width when its width is auto, keeps its margins as a left/top offset, and
    /// only fills the viewport height when the style requests it.
    /// </remarks>
    private void LayoutRootBox(LayoutBox root, Rect viewport)
    {
        ApplyRootScrollBehavior(root);

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
            fillHeight: _options.RootScrolls && root.Style.Height.IsAuto,
            definiteContentHeight: null);
    }

    /// <summary>
    /// Gives an application root the browser's default window scrolling
    /// behaviour when the author leaves <c>overflow-y</c> visible.
    /// </summary>
    /// <param name="root">Root box about to be laid out or repainted.</param>
    /// <remarks>
    /// <para>
    /// The root style is an ordinary resolved style and must be kept in sync
    /// with this behaviour. A full layout applies it before geometry; an
    /// animation-only repaint rebuilds every box style from the cached base
    /// style, so it applies the rule again afterwards. Without that second
    /// application an animated app (a spinner is enough) would silently lose
    /// its viewport scroll container on the first repaint.
    /// </para>
    /// <para>
    /// An explicit <c>overflow-y</c> other than <c>visible</c> is honoured.
    /// </para>
    /// </remarks>
    private void ApplyRootScrollBehavior(LayoutBox root)
    {
        if (!_options.RootScrolls || root.Style.OverflowY != Overflow.Visible)
        {
            return;
        }

        var style = root.Style.Clone();
        style.OverflowY = Overflow.Auto;
        root.Style = style;
    }

    // -------------------------------------------------------------- box sizing

    /// <summary>Resolves the used margin, border and padding of a box.</summary>
    /// <param name="box">Box whose edges are resolved.</param>
    /// <param name="cbWidth">
    /// Width of the containing block, used as the percentage basis for all four
    /// edges as required by CSS.
    /// </param>
    /// <remarks>
    /// Writes the resolved values back to <see cref="LayoutBox.Margin"/>,
    /// <see cref="LayoutBox.Border"/> and <see cref="LayoutBox.Padding"/>.
    /// Border widths on sides whose style is <c>none</c> or <c>hidden</c> are
    /// forced to zero by <see cref="ResolveBorders"/>.
    /// </remarks>
    private void PrepareBox(LayoutBox box, float cbWidth)
    {
        var style = box.Style;
        var context = CreateContext(style);

        box.Padding = style.Padding.Resolve(in context, cbWidth);
        box.Border = ResolveBorders(style, in context, cbWidth);
        box.Margin = style.Margin.Resolve(in context, cbWidth);
    }

    /// <summary>Creates a length context scoped to one element's font metrics.</summary>
    /// <param name="style">Style of the element being sized.</param>
    /// <returns>
    /// A copy of the frame's root context whose font size is the element's used
    /// font size and whose default percentage basis is that same font size.
    /// </returns>
    /// <remarks>
    /// Properties that resolve percentages against a containing block pass the
    /// basis explicitly to <see cref="CssLength.Resolve(in LengthContext, float)"/>
    /// or <see cref="CssLength.ResolveOr(in LengthContext, float, float)"/>. A
    /// caller that omits a basis gets the element font size as the percentage basis.
    /// </remarks>
    private LengthContext CreateContext(ComputedStyle style)
    {
        var fontSize = style.ResolveFontSize(in _baseContext);
        return _baseContext with
        {
            FontSize = fontSize,
            PercentBasis = fontSize
        };
    }

    /// <summary>Resolves border widths, suppressing sides with no visible style.</summary>
    /// <param name="style">Style providing border widths and styles.</param>
    /// <param name="context">Length context with the element's font metrics.</param>
    /// <param name="cbWidth">Containing-block width used as the percentage basis.</param>
    /// <returns>Used border widths in CSS pixels.</returns>
    private static Thickness ResolveBorders(ComputedStyle style, in LengthContext context, float cbWidth)
    {
        var widths = style.BorderWidth.Resolve(in context, cbWidth);

        return new Thickness(
            style.BorderStyleTop is BorderStyle.None or BorderStyle.Hidden ? 0 : widths.Top,
            style.BorderStyleRight is BorderStyle.None or BorderStyle.Hidden ? 0 : widths.Right,
            style.BorderStyleBottom is BorderStyle.None or BorderStyle.Hidden ? 0 : widths.Bottom,
            style.BorderStyleLeft is BorderStyle.None or BorderStyle.Hidden ? 0 : widths.Left);
    }

    /// <summary>
    /// Resolves a box's edges and used border-box width, then applies a
    /// zero-height border box at the origin.
    /// </summary>
    /// <param name="box">Box to size.</param>
    /// <param name="cbWidth">Containing-block width in CSS pixels.</param>
    /// <param name="forcedBorderWidth">
    /// Explicit used border-box width. When supplied, the style width is ignored
    /// but min/max clamping and auto margins still apply.
    /// </param>
    /// <param name="fillWidth">
    /// When the width is auto and no intrinsic size exists, use the full
    /// containing-block width minus horizontal margins.
    /// </param>
    /// <param name="shrinkToFit">
    /// When the width is auto, no intrinsic size exists and
    /// <paramref name="fillWidth"/> is off, use the smaller of the max-content
    /// width and the available width.
    /// </param>
    /// <param name="allowAutoMargins">
    /// Resolve <c>margin:auto</c> against the remaining horizontal space.
    /// Inline-level layout passes <see langword="false"/> because auto margins do
    /// not absorb space on an inline line.
    /// </param>
    /// <param name="edgesPrepared">
    /// <see langword="true"/> when the caller already resolved the box's
    /// margin, border and padding for the same containing-block width, which
    /// avoids resolving them a second time before placement.
    /// </param>
    /// <returns>The used border-box width in CSS pixels.</returns>
    /// <remarks>
    /// <para>
    /// Width selection order is: <paramref name="forcedBorderWidth"/>, the
    /// resolved style width, the intrinsic-size provider, then the fill or
    /// shrink-to-fit fallback. Content-box widths have padding and border added;
    /// border-box widths are used as-is.
    /// </para>
    /// <para>
    /// The result is clamped by <c>min-width</c>/<c>max-width</c> (adjusted for
    /// content-box sizing) and can never be negative. The box is left at
    /// <c>(0, 0)</c> with zero height; the caller supplies the final position and
    /// height through <see cref="LayoutPreparedBox"/>.
    /// </para>
    /// </remarks>
    private float PrepareAndMeasureWidth(
        LayoutBox box,
        float cbWidth,
        float? forcedBorderWidth,
        bool fillWidth,
        bool shrinkToFit,
        bool allowAutoMargins = true,
        bool edgesPrepared = false)
    {
        if (!edgesPrepared)
        {
            PrepareBox(box, cbWidth);
        }

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

    /// <summary>
    /// Resolves a box's border-box height from its style, intrinsics or
    /// containing block.
    /// </summary>
    /// <param name="box">Box whose height is resolved.</param>
    /// <param name="cbWidth">
    /// Containing-block width for percentage padding and borders.
    /// </param>
    /// <param name="cbHeight">Containing-block height used as the percentage basis.</param>
    /// <param name="forcedBorderHeight">
    /// Explicit height that bypasses style resolution. It is still clamped by
    /// <c>min-height</c>/<c>max-height</c>.
    /// </param>
    /// <param name="fillHeight">
    /// When the height is auto, no intrinsic size exists and the containing-block
    /// height is definite, fill that height minus vertical margins.
    /// </param>
    /// <returns>
    /// The used border-box height, or <see langword="null"/> when the height is
    /// still auto and cannot be resolved before content layout.
    /// </returns>
    /// <remarks>
    /// A content-box height has vertical padding and border added before
    /// clamping. Min and max heights are adjusted the same way so the comparison
    /// happens in border-box units.
    /// </remarks>
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

    /// <summary>Clamps a border-box height to the box's used min and max height.</summary>
    /// <param name="box">Box whose constraints apply.</param>
    /// <param name="cbHeight">Containing-block height used as the percentage basis.</param>
    /// <param name="value">Border-box height to clamp, in CSS pixels.</param>
    /// <returns>The clamped height, never negative.</returns>
    /// <remarks>
    /// Content-box min and max heights are converted to border-box units by
    /// adding the vertical padding and border before the comparison.
    /// </remarks>
    private float ClampBorderHeight(LayoutBox box, float cbHeight, float value)
    {
        var style = box.Style;
        var context = CreateContext(style);
        var verticalExtra = box.Padding.Vertical + box.Border.Vertical;
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

        return MathF.Max(0, MathF.Max(minHeight, MathF.Min(value, maxHeight)));
    }

    /// <summary>Clamps a border-box width to the style's used min and max width.</summary>
    /// <param name="style">Style providing the width constraints and box sizing.</param>
    /// <param name="context">Length context with the element's font metrics.</param>
    /// <param name="cbWidth">Containing-block width used as the percentage basis.</param>
    /// <param name="width">Border-box width to clamp, in CSS pixels.</param>
    /// <returns>The clamped width, never negative.</returns>
    /// <remarks>
    /// For content-box sizing, horizontal padding and border are added to both
    /// constraints so the clamp is performed in border-box units.
    /// </remarks>
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

    /// <summary>Distributes leftover horizontal space to auto left and right margins.</summary>
    /// <param name="box">Box whose already-resolved margins may contain auto sides.</param>
    /// <param name="cbWidth">Containing-block width in CSS pixels.</param>
    /// <param name="borderWidth">Used border-box width of the box.</param>
    /// <remarks>
    /// Only positive leftover space is distributed; an over-constrained box keeps
    /// its resolved margins. When both sides are auto each receives half, when
    /// one is auto it receives all of it.
    /// </remarks>
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
    /// <param name="box">Box to lay out. Its edges must already be resolved.</param>
    /// <param name="borderX">Absolute x coordinate of the border box.</param>
    /// <param name="borderY">Absolute y coordinate of the border box.</param>
    /// <param name="cbWidth">Containing-block width passed to content layout.</param>
    /// <param name="cbHeight">Containing-block height for percentages and min/max clamping.</param>
    /// <param name="forcedBorderHeight">
    /// Explicit border-box height supplied by the caller, for example a stretched
    /// flex item or a table cell stretched to its row.
    /// </param>
    /// <param name="fillHeight">
    /// Fill the containing block's height when the style height is auto and a
    /// definite containing-block height exists.
    /// </param>
    /// <param name="definiteContentHeight">
    /// Known content height used to derive a border-box height, primarily for
    /// table cells and flex items with a definite cross size.
    /// </param>
    /// <returns>The used border-box height in CSS pixels.</returns>
    /// <remarks>
    /// <para>
    /// Height selection order is: the forced or definite height (clamped to
    /// min/max), the style height or fill height, an aspect-ratio derivation, and
    /// finally the content height measured by <see cref="LayoutContent"/>. Auto
    /// height is clamped again after content layout so an empty element still
    /// honours its <c>min-height</c>.
    /// </para>
    /// <para>
    /// After layout the method assigns the baseline, border radii, scroll
    /// metrics and relative/sticky offsets. The returned value is the height of
    /// the final border box, which may differ from the content height when
    /// padding or borders are present.
    /// </para>
    /// </remarks>
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

        // An explicitly supplied or definite height still has to honour
        // min-height/max-height, otherwise a control such as an input whose
        // height is forced by its container can collapse below its floor.
        if (height is { } suppliedHeight)
        {
            height = ClampBorderHeight(box, cbHeight, suppliedHeight);
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

            height = ClampBorderHeight(box, cbHeight, height.Value);
        }

        var startHeight = height ?? 0;
        box.ApplyBorderBox(borderX, borderY, box.BorderBox.Width, startHeight);

        var usedContentHeight = LayoutContent(box, box.ContentBox, height is { } definite
            ? definite - box.Padding.Vertical - box.Border.Vertical
            : float.NaN);

        if (height is null)
        {
            // Auto height: the used height is the content height, but it still
            // has to be clamped by min-height/max-height. Without this, an
            // element whose content collapses (e.g. an empty focused input)
            // ignores its min-height and shrinks to nothing.
            startHeight = usedContentHeight + box.Padding.Vertical + box.Border.Vertical;
            startHeight = ClampBorderHeight(box, cbHeight, startHeight);
        }

        box.ApplyBorderBox(borderX, borderY, box.BorderBox.Width, startHeight);
        box.Baseline = BaselineOf(box);
        ResolveRadius(box);
        UpdateScrollMetrics(box);
        ApplyRelativeOffset(box);

        return box.BorderBox.Height;
    }

    /// <summary>Resolves the box's corner radii against its final border box.</summary>
    /// <param name="box">Laid-out box whose border box is final.</param>
    /// <remarks>
    /// Four auto corners produce <see cref="CornerRadius.Zero"/> without resolving
    /// lengths. Otherwise percentage radii use the border-box width as the
    /// horizontal basis and the border-box height as the vertical basis, as
    /// implemented by <see cref="ComputedStyle.BorderRadius"/> resolution.
    /// </remarks>
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

    /// <summary>Computes the baseline offset used for alignment of this box.</summary>
    /// <param name="box">Laid-out box whose baseline is requested.</param>
    /// <returns>Baseline offset in CSS pixels.</returns>
    /// <remarks>
    /// The first line of the box's own text contributes its baseline relative to
    /// the text origin. Otherwise the bottom of the first in-flow child with a
    /// non-zero height is converted to an offset from this box's border-box top.
    /// An empty box falls back to its full border-box height.
    /// </remarks>
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

    /// <summary>
    /// Translates a relatively or stickily positioned box and its descendants.
    /// </summary>
    /// <param name="box">Box to offset.</param>
    /// <remarks>
    /// <c>left</c> and <c>top</c> win when definite; otherwise the negative of
    /// <c>right</c> or <c>bottom</c> is used. Percentages resolve against the
    /// parent's content box, falling back to the viewport at the root. The
    /// offset does not affect sibling flow positions, only painted geometry.
    /// </remarks>
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

    /// <summary>Computes a box's scrollable overflow and clamps its scroll offsets.</summary>
    /// <param name="box">Box whose scroll metrics are updated after layout.</param>
    /// <remarks>
    /// <para>
    /// The overflow extent starts at the padding box and grows to cover each
    /// child's margin box (or border box when the margin box is degenerate).
    /// Only right and bottom overflow is measured; content above or left of the
    /// origin does not create negative scroll range. The resulting
    /// <see cref="LayoutBox.ScrollWidth"/> and <see cref="LayoutBox.ScrollHeight"/>
    /// are at least the padding-box (client) size.
    /// </para>
    /// <para>
    /// Non-scroll containers keep zero offsets. Scroll containers clamp the
    /// offsets from <see cref="LayoutOptions"/> against the padding-box size, so
    /// a stale offset can never exceed the current range.
    /// </para>
    /// </remarks>
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

        // Scroll metrics are measured from the padding box, matching the
        // client area that scrollbars are sized against. Measuring from the
        // content box would silently drop padding-top/left from the range.
        box.ScrollWidth = MathF.Max(box.PaddingBox.Width, maxRight - box.PaddingBox.X);
        box.ScrollHeight = MathF.Max(box.PaddingBox.Height, maxBottom - box.PaddingBox.Y);
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

    /// <summary>
    /// Lays out every queued absolute or fixed box that has not been processed yet.
    /// </summary>
    /// <param name="viewport">
    /// Layout viewport used for fixed boxes and as the fallback containing block.
    /// </param>
    /// <remarks>
    /// <para>
    /// Fixed boxes use the viewport unless an ancestor has a transform or filter,
    /// which makes that ancestor their containing block. Absolute boxes use the
    /// nearest positioned ancestor's padding box, or the viewport when there is
    /// none. Insets and sizes are resolved by <see cref="LayoutAbsoluteBox"/>.
    /// </para>
    /// <para>
    /// The method is re-entrant because laying out an absolute box can discover
    /// nested absolutes: <see cref="_absoluteLaidOut"/> makes each box run once,
    /// and <see cref="LayoutAbsoluteBox"/> calls this method again after content
    /// layout to drain the additions.
    /// </para>
    /// </remarks>
    private void ProcessAbsoluteBoxes(Rect viewport)
    {
        for (var i = 0; i < _absolutes.Count; i++)
        {
            var box = _absolutes[i];

            if (!_absoluteLaidOut.Add(box))
            {
                continue;
            }

            // A fixed box uses the viewport unless a transformed/filtered
            // ancestor was recorded as its containing block at build time.
            var containingBlock = box.ContainingBlock?.PaddingBox ?? viewport;
            LayoutAbsoluteBox(box, containingBlock);
        }
    }

    /// <summary>Resolves the used width, height and position of one out-of-flow box.</summary>
    /// <param name="box">Absolute or fixed box to lay out.</param>
    /// <param name="containingBlock">
    /// Padding box of the positioned ancestor, or the viewport for fixed boxes
    /// and absolutes without a positioned ancestor.
    /// </param>
    /// <remarks>
    /// <para>
    /// Width and height come from their style when definite or from the sum of
    /// opposite insets when both are present. Width otherwise shrinks to fit,
    /// while height stays content-based. Horizontal position prefers
    /// <c>left</c>, then <c>right</c>, then the static position recorded during
    /// normal flow; vertical position follows the same order for <c>top</c>,
    /// <c>bottom</c> and the static position.
    /// </para>
    /// <para>
    /// After content layout the method drains nested absolutes through
    /// <see cref="ProcessAbsoluteBoxes"/>. Out-of-flow boxes stay in the layout
    /// tree at their absolute coordinates and do not advance normal flow; paint
    /// order is handled by the display-list builder.
    /// </para>
    /// </remarks>
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
            // A bottom-anchored box shrinks to fit when its height is auto. The
            // used height has to be known before the position is computed;
            // otherwise the box starts where its bottom edge belongs and hangs
            // below the containing block (the classic carousel-caption overlap).
            forcedHeight ??= MeasureMaxContentHeight(box, containingBlock.Width);
            y = containingBlock.Bottom - bottom - forcedHeight.Value - box.Margin.Bottom;
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

    /// <summary>Computes and caches a box's max-content border-box width.</summary>
    /// <param name="box">Box to measure.</param>
    /// <returns>Max-content width including padding and border, in CSS pixels.</returns>
    /// <remarks>
    /// <para>
    /// The result is cached on <see cref="LayoutBox.IntrinsicWidth"/> for the
    /// current layout pass. A definite non-percentage style width short-circuits
    /// measurement, and an intrinsic-size provider is preferred over content
    /// measurement. Percentage widths are treated as indefinite because they have
    /// no max-content value independent of the containing block.
    /// </para>
    /// <para>
    /// The value is the content measurement plus resolved padding and border, so
    /// callers can compare it directly with border-box widths.
    /// </para>
    /// </remarks>
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

    /// <summary>Measures the max-content content width of text and child boxes.</summary>
    /// <param name="box">Box whose content is measured.</param>
    /// <param name="context">Length context used for child margins.</param>
    /// <returns>Content width without the box's own padding and border, in CSS pixels.</returns>
    /// <remarks>
    /// The measurement treats inline-level containers, flex rows and grid
    /// containers as one line, so their text and child widths are summed. Block
    /// containers use the widest child instead. Out-of-flow and
    /// <c>display:contents</c> children are skipped; child margins are included
    /// in the contribution.
    /// </remarks>
    private float MeasureContentMaxWidth(LayoutBox box, in LengthContext context)
    {
        var textWidth = 0f;
        var text = box.Children.Count > 0
            && (box.Style.IsFlexContainer || box.Style.IsGridContainer)
            ? null
            : GetNodeText(box.Node);

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
        var flowChildren = 0;

        // A block container measures the widest line formed by its children.
        // Consecutive inline children share one line and therefore add up;
        // a block child contributes its own max-content width. Treating every
        // child independently under-measures mixed content such as Bootstrap's
        // alert (a heading block followed by an inline text/link run), which
        // then wraps text and makes the alert too tall.
        var inlineRun = textWidth;

        for (var i = 0; i < box.Children.Count; i++)
        {
            var child = box.Children[i];

            if (child.IsOutOfFlow || child.IsContents)
            {
                continue;
            }

            var childContext = CreateContext(child.Style);
            var margin = child.Style.Margin.Resolve(in childContext, 0);
            var childWidth = MeasureMaxContentWidth(child)
                + margin.Horizontal;

            sum += childWidth;
            flowChildren++;

            if (inlineFlow)
            {
                max = MathF.Max(max, childWidth);
                continue;
            }

            if (IsInlineLevel(child))
            {
                inlineRun += childWidth;
                max = MathF.Max(max, inlineRun);
            }
            else
            {
                max = MathF.Max(max, inlineRun);
                inlineRun = 0;
                max = MathF.Max(max, childWidth);
            }
        }

        max = MathF.Max(max, inlineRun);

        // An inline-level container puts its text and inline children on one
        // line, so their intrinsic widths add up. A block container's intrinsic
        // width is the widest line instead.
        if (inlineFlow)
        {
            // A single-line flex row also needs its column gaps in the
            // intrinsic width, otherwise the measured width is short by the
            // gaps and a wrapping container (Bootstrap's .btn-toolbar) wraps a
            // child onto the next line during the real layout pass.
            if (style.IsFlexContainer
                && style.FlexDirection is FlexDirection.Row or FlexDirection.RowReverse
                && flowChildren > 1)
            {
                sum += ResolveGap(style.ColumnGap, in context, 0) * (flowChildren - 1);
            }

            return textWidth + sum;
        }

        return max;
    }

    // ------------------------------------------------------------------ text

    /// <summary>
    /// Identifies one shaped text layout: the transformed text, text style,
    /// quantised available width and the wrapping-related style values.
    /// </summary>
    /// <remarks>
    /// <c>Width</c> is stored as quarter-pixel units rounded up, or
    /// <see cref="int.MaxValue"/> for an unbounded width. The <c>white-space</c>,
    /// <c>overflow-wrap</c>, line-clamp and text-overflow values are part of the
    /// key because they change line breaking even when text and style are equal.
    /// The measurer is keyed by identity so swapping it on an existing engine
    /// cannot reuse layouts shaped with the previous metrics.
    /// </remarks>
    private readonly record struct TextLayoutKey(
        string Text,
        TextStyle Style,
        int Width,
        WhiteSpace WhiteSpace,
        OverflowWrap OverflowWrap,
        int LineClamp,
        TextOverflow Overflow,
        ITextMeasurer Measurer);

    /// <summary>Builds the immutable text style used for measurement and painting.</summary>
    /// <param name="style">Computed style providing font and decoration settings.</param>
    /// <param name="color">Optional colour override; uses the style colour when omitted.</param>
    /// <returns>A text style with lengths resolved against the element's font metrics.</returns>
    private TextStyle CreateTextStyle(ComputedStyle style, uint? color = null)
    {
        var context = CreateContext(style);
        var fontSize = context.FontSize;
        return style.ToTextStyle(in context, fontSize, color ?? style.Color);
    }

    /// <summary>Asks the host for a text colour override for one node.</summary>
    /// <param name="node">Node that owns the text.</param>
    /// <param name="text">Transformed text that will be drawn.</param>
    /// <returns>
    /// The host-provided colour, or <see langword="null"/> to use the computed style colour.
    /// </returns>
    private uint? GetTextColor(IgniteSupportedDomNode node, string text)
    {
        return _options.TextColorProvider?.Invoke(node, text);
    }

    /// <summary>Returns a shaped text layout, normally from the two-generation cache.</summary>
    /// <param name="node">Node that owns the text.</param>
    /// <param name="style">Computed style that controls wrapping and decoration.</param>
    /// <param name="text">Raw text before <c>text-transform</c> is applied.</param>
    /// <param name="maxWidth">Available line width in CSS pixels, or infinity for no limit.</param>
    /// <param name="textStyle">
    /// Receives the text style used for shaping and painting.
    /// </param>
    /// <returns>A line layout that can be painted at the caller's origin.</returns>
    /// <remarks>
    /// <para>
    /// The cache key rounds the available width up to the next quarter pixel. A
    /// line that exactly fits must not hit a layout shaped for a narrower width,
    /// which would wrap it again.
    /// </para>
    /// <para>
    /// Cache lookup checks the current generation first and then the previous
    /// one, promoting a previous-generation hit. When the current map reaches
    /// 1024 entries the two maps swap and the new current map is cleared, so
    /// recent layouts survive one rotation while old ones become collectable.
    /// Set <see cref="LayoutOptions.CacheTextLayouts"/> to
    /// <see langword="false"/> for highly dynamic text.
    /// </para>
    /// </remarks>
    private TextLayout GetTextLayout(
        IgniteSupportedDomNode node,
        ComputedStyle style,
        string text,
        float maxWidth,
        out TextStyle textStyle)
    {
        text = ApplyTextTransform(text, style.TextTransform);
        textStyle = CreateTextStyle(style, GetTextColor(node, text));

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
            style.TextOverflow,
            _measurer);

        var cacheText = _options.EnableCaching && _options.CacheTextLayouts;

        if (cacheText)
        {
            if (_textCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (_textCachePrevious.TryGetValue(key, out cached))
            {
                _textCache[key] = cached;
                return cached;
            }
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

        if (cacheText)
        {
            // Two generations give a simple LRU-ish bound: recent layouts stay
            // in the current map, the previous generation survives one rotation
            // and then becomes unreachable. A full clear on a fixed threshold
            // caused a large promotion spike whenever dynamic text filled it.
            if (_textCache.Count >= 1024)
            {
                (_textCache, _textCachePrevious) = (_textCachePrevious, _textCache);
                _textCache.Clear();
            }

            _textCache[key] = layout;
        }

        return layout;
    }

    /// <summary>
    /// Uncached text layout that asks the float context for the available band
    /// on every line. It is only used when text overlaps active floats.
    /// </summary>
    /// <param name="node">Node that owns the text.</param>
    /// <param name="style">Computed style that controls wrapping and decoration.</param>
    /// <param name="text">Raw text before <c>text-transform</c> is applied.</param>
    /// <param name="content">Content box of the containing block.</param>
    /// <param name="startY">Absolute y coordinate of the first line.</param>
    /// <param name="floats">Active floats for the block formatting context.</param>
    /// <returns>A line layout whose line origins already follow the float bands.</returns>
    /// <remarks>
    /// The cache is bypassed because each line's width depends on the vertical
    /// position and on float state that is not part of
    /// <see cref="TextLayoutKey"/>. <see cref="TextLayoutEngine.LayoutWithBands"/>
    /// consults <see cref="FloatContext.BandAt"/> for every candidate line.
    /// </remarks>
    private TextLayout GetTextLayoutInBands(
        IgniteSupportedDomNode node,
        ComputedStyle style,
        string text,
        in Rect content,
        float startY,
        FloatContext floats)
    {
        text = ApplyTextTransform(text, style.TextTransform);
        var textStyle = CreateTextStyle(style, GetTextColor(node, text));
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

    /// <summary>Applies <c>text-transform</c> using invariant casing rules.</summary>
    /// <param name="text">Source text.</param>
    /// <param name="transform">Transformation to apply.</param>
    /// <returns>The transformed text, or the original instance when no transform applies.</returns>
    /// <remarks>
    /// <c>capitalize</c> uppercases the first non-whitespace character after any
    /// whitespace run. Uppercase and lowercase results may allocate a new string;
    /// callers use the transformed value in cache keys and painting.
    /// </remarks>
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

    /// <summary>Selects the formatting context for a box and lays out its contents.</summary>
    /// <param name="box">Box whose content is laid out.</param>
    /// <param name="content">Content box in absolute coordinates.</param>
    /// <param name="definiteContentHeight">
    /// Known content height, or <see cref="float.NaN"/> when the height depends on content.
    /// </param>
    /// <returns>The used content height in CSS pixels, never negative for normal flow.</returns>
    /// <remarks>
    /// <para>
    /// A box that establishes a block formatting context, and the root box,
    /// receives an owned <see cref="FloatContext"/> that is reset for this pass;
    /// other boxes share their parent's context so line boxes and BFC roots can
    /// see the same floats.
    /// </para>
    /// <para>
    /// <c>display:contents</c> always uses block-child layout. Otherwise tables,
    /// flex containers, grid containers and normal block flow are dispatched to
    /// their dedicated implementations.
    /// </para>
    /// </remarks>
    private float LayoutContent(LayoutBox box, Rect content, float definiteContentHeight)
    {
        if (EstablishesBlockFormattingContext(box.Style) || ReferenceEquals(box, _root))
        {
            var context = box.OwnedFloatContext ?? new FloatContext();
            context.Reset();
            box.OwnedFloatContext = context;
            box.FloatContext = context;
        }
        else
        {
            box.FloatContext = box.Parent?.FloatContext ?? new FloatContext();
        }

        // Out-of-flow children need a hypothetical position even when the
        // formatting context never places them. Block and inline flow refine
        // this as they walk their children; flex, grid and table layout skip
        // absolute children entirely, so the container's content origin is the
        // static position they would otherwise lack.
        InitializeOutOfFlowStaticPositions(box, content);

        if (box.IsContents)
        {
            return LayoutBlockChildren(box, content, definiteContentHeight);
        }

        if (box.Style.Display is Display.Table or Display.InlineTable)
        {
            return LayoutTable(box, content, definiteContentHeight);
        }

        // A flex or grid container can own text directly (an element whose
        // only children are text nodes, such as Bootstrap's .accordion-button).
        // There is no anonymous flex item to place, so lay the run out with the
        // block/inline path instead of silently dropping it.
        if (box.Children.Count == 0
            && (box.Style.IsFlexContainer || box.Style.IsGridContainer)
            && !string.IsNullOrEmpty(GetNodeText(box.Node)))
        {
            return LayoutBlockChildren(box, content, definiteContentHeight);
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

    /// <summary>
    /// Seeds the static position of every out-of-flow child from a container's
    /// content box before its formatting context runs.
    /// </summary>
    /// <param name="box">Container whose direct children are inspected.</param>
    /// <param name="content">Content box in absolute CSS pixels.</param>
    /// <remarks>
    /// Block and inline formatting contexts overwrite this with the exact flow
    /// position. Flex, grid and table contexts do not place absolute children at
    /// all, so without the seed an absolute child without insets (the common
    /// Bootstrap dropdown menu) would keep the default (0, 0) and paint at the
    /// viewport origin.
    /// </remarks>
    private static void InitializeOutOfFlowStaticPositions(LayoutBox box, Rect content)
    {
        for (var i = 0; i < box.Children.Count; i++)
        {
            var child = box.Children[i];

            if (child.IsOutOfFlow)
            {
                child.StaticPosition = new Point(content.X, content.Y);
            }
        }
    }

    /// <summary>Returns whether a box participates in an inline formatting context.</summary>
    /// <param name="box">Box to classify.</param>
    /// <returns>
    /// <see langword="true"/> for <c>inline</c>, <c>inline-block</c>,
    /// <c>inline-flex</c>, <c>inline-grid</c> and <c>inline-table</c>.
    /// </returns>
    private static bool IsInlineLevel(LayoutBox box)
    {
        return box.Style.Display is Display.Inline or Display.InlineBlock or Display.InlineFlex
            or Display.InlineGrid or Display.InlineTable;
    }

    /// <summary>
    /// True when the box starts a new block formatting context, which means it
    /// gets its own float context and its border box avoids the parent's floats.
    /// </summary>
    /// <param name="style">Style of the box being classified.</param>
    /// <returns>
    /// <see langword="true"/> for floats, absolutes and fixed boxes,
    /// <c>flow-root</c>, inline-blocks, tables and table cells/captions,
    /// flex and grid containers, and scroll containers.
    /// </returns>
    /// <remarks>
    /// The root box is treated as a BFC root by callers even though its style is
    /// not inspected here. A BFC root encloses its floats and must not overlap
    /// an ancestor's floats.
    /// </remarks>
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

    /// <summary>
    /// Stable <c>order</c> sort. <see cref="List{T}.Sort(Comparison{T})"/> is
    /// unstable, which would let equal-order siblings swap paint and layout
    /// positions between frames.
    /// </summary>
    /// <param name="items">
    /// Flow children to sort in place by <see cref="ComputedStyle.Order"/>.
    /// </param>
    /// <remarks>
    /// Insertion sort with O(n^2) worst-case cost. Flex and grid containers
    /// usually have few children, and the stability guarantee is worth more here
    /// than an unstable O(n log n) sort.
    /// </remarks>
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

}
