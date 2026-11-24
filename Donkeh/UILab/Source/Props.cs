using System.Globalization;

namespace Donkeh.UILab
{
    /// <summary>
    /// Strongly typed props for an element. Common HTML attributes and events
    /// are first-class properties; anything else can go through
    /// <see cref="Attributes"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a plain data object used only while the virtual tree is built.
    /// <see cref="ElementNode.ApplyProps"/> reads it once and interns every name
    /// and value, so the diff compares ints even though the caller writes strings.
    /// The instance is not retained by the node and may be reused or discarded
    /// immediately afterwards.
    /// </para>
    /// <para>
    /// A null property is omitted. A boolean property is emitted as an empty
    /// attribute when <see langword="true"/> and omitted when
    /// <see langword="false"/> or null, which matches how HTML treats presence
    /// attributes such as <c>disabled</c>.
    /// </para>
    /// <para>
    /// <see cref="Attributes"/> entries are applied after the dedicated
    /// properties, so a non-null entry wins on a name collision. The exception is
    /// <c>class</c>, whose tokens are added to <see cref="ClassName"/> rather than
    /// replacing it, and <c>id</c>, which replaces <see cref="Id"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using static Donkeh.UILab._;
    ///
    /// var button = H("button", new Props
    /// {
    ///     ClassName = "primary",
    ///     Disabled = isBusy,
    ///     OnClick = Save
    /// }, "Save");
    /// </code>
    /// </example>
    /// <seealso cref="Attr"/>
    /// <seealso cref="ElementNode.WithAttribute(Attr)"/>
    public partial class Props
    {
        /// <summary>Gets or sets the <c>id</c> attribute.</summary>
        public string? Id { get; init; }

        /// <summary>
        /// Gets or sets the <c>class</c> attribute as a whitespace-separated list of
        /// class names.
        /// </summary>
        /// <remarks>
        /// The names are split, de-duplicated and sorted when applied, so the same
        /// set of classes always produces the same interned id sequence regardless
        /// of the order written here.
        /// </remarks>
        public string? ClassName { get; init; }

        /// <summary>Gets or sets the <c>title</c> attribute.</summary>
        public string? Title { get; init; }

        /// <summary>Gets or sets the <c>href</c> attribute.</summary>
        public string? Href { get; init; }

        /// <summary>Gets or sets the <c>src</c> attribute.</summary>
        public string? Src { get; init; }

        /// <summary>
        /// Gets or sets the <c>loading-page</c> attribute of a <c>webview</c>.
        /// Shown until the document from <see cref="Src"/> becomes available.
        /// </summary>
        public string? LoadingPage { get; init; }

        /// <summary>
        /// Gets or sets the <c>type</c> attribute, for example the input type or a
        /// button type.
        /// </summary>
        public string? Type { get; init; }

        /// <summary>Gets or sets the <c>value</c> attribute.</summary>
        public string? Value { get; init; }

        /// <summary>Gets or sets the <c>placeholder</c> attribute.</summary>
        public string? Placeholder { get; init; }

        /// <summary>Gets or sets the ARIA <c>role</c> attribute.</summary>
        public string? Role { get; init; }

        /// <summary>Gets or sets the alternative-text <c>alt</c> attribute.</summary>
        public string? Alt { get; init; }

        /// <summary>Gets or sets the form field <c>name</c> attribute.</summary>
        public string? Name { get; init; }

        /// <summary>Gets or sets the link or form <c>target</c> attribute.</summary>
        public string? Target { get; init; }

        /// <summary>Gets or sets the link <c>rel</c> attribute.</summary>
        public string? Rel { get; init; }

        /// <summary>
        /// Gets or sets the <c>tabindex</c> attribute.
        /// </summary>
        /// <value>
        /// A focus order, or <see langword="null"/> to omit the attribute.
        /// </value>
        public int? TabIndex { get; init; }

        /// <summary>
        /// Gets or sets whether the <c>disabled</c> attribute is present.
        /// </summary>
        /// <value>
        /// <see langword="true"/> emits an empty attribute; <see langword="false"/>
        /// or <see langword="null"/> omits it.
        /// </value>
        public bool? Disabled { get; init; }

        /// <summary>
        /// Gets or sets whether the <c>checked</c> attribute is present.
        /// </summary>
        /// <value>
        /// <see langword="true"/> emits an empty attribute; <see langword="false"/>
        /// or <see langword="null"/> omits it.
        /// </value>
        public bool? Checked { get; init; }

        /// <summary>
        /// Gets or sets whether the element can start an HTML-style drag.
        /// </summary>
        /// <value>
        /// <see langword="true"/> emits <c>draggable="true"</c>; false or
        /// <see langword="null"/> leaves the element non-draggable.
        /// </value>
        /// <remarks>
        /// A drag starts on the nearest draggable ancestor of the pressed element
        /// once the pointer moves past a small threshold. The host then dispatches
        /// <see cref="OnDragStart"/>, <see cref="OnDrag"/>,
        /// <see cref="OnDragEnter"/>, <see cref="OnDragOver"/>,
        /// <see cref="OnDragLeave"/>, <see cref="OnDrop"/> and
        /// <see cref="OnDragEnd"/> with a shared <see cref="DataTransfer"/>.
        /// </remarks>
        public bool? Draggable { get; init; }

        /// <summary>
        /// Gets or sets arbitrary attributes that do not have a dedicated property.
        /// The key is the attribute name, the value is its string value.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Entries are applied in dictionary enumeration order after the dedicated
        /// properties. A null value is ignored, so the attribute is omitted unless a
        /// dedicated property already set it. A <c>class</c> entry adds its
        /// whitespace-separated tokens to <see cref="ClassName"/> instead of
        /// replacing them.
        /// </para>
        /// <para>
        /// Names do not need to be valid HTML; the engine does not validate them,
        /// and they are matched case-sensitively by the selector engine. Use
        /// lower-case names for consistency with the built-in attributes.
        /// </para>
        /// </remarks>
        public Dictionary<string, string>? Attributes { get; init; }

        /// <summary>
        /// A stable identity among siblings, used by the reconciler.
        /// </summary>
        /// <remarks>
        /// The key is not an HTML attribute and is never emitted. It is copied onto
        /// <see cref="Node.Key"/> by <see cref="ElementNode.ApplyProps"/> and lets a
        /// reorder reuse the existing node (and its component state and layout)
        /// instead of rebuilding it. Keys only need to be unique within one sibling
        /// list.
        /// </remarks>
        public string? Key { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>click</c> event.
        /// </summary>
        public Action? OnClick { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>mousedown</c> event.
        /// </summary>
        public Action? OnMouseDown { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>mouseup</c> event.
        /// </summary>
        public Action? OnMouseUp { get; init; }

        /// <summary>
        /// Gets or sets a click handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnClick"/> when both are set.
        /// </remarks>
        public Action<ClickEvent>? OnClickEvent { get; init; }

        /// <summary>
        /// Gets or sets a mousedown handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnMouseDown"/> when both are set.
        /// </remarks>
        public Action<MouseDownEvent>? OnMouseDownEvent { get; init; }

        /// <summary>
        /// Gets or sets a mouseup handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnMouseUp"/> when both are set.
        /// </remarks>
        public Action<MouseUpEvent>? OnMouseUpEvent { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>mouseover</c> event.
        /// </summary>
        public Action? OnMouseOver { get; init; }

        /// <summary>
        /// Gets or sets a mouseover handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnMouseOver"/> when both are set.
        /// </remarks>
        public Action<MouseOverEvent>? OnMouseOverEvent { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>mouseout</c> event.
        /// </summary>
        public Action? OnMouseOut { get; init; }

        /// <summary>
        /// Gets or sets a mouseout handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnMouseOut"/> when both are set.
        /// </remarks>
        public Action<MouseOutEvent>? OnMouseOutEvent { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>scroll</c> event.
        /// </summary>
        /// <remarks>
        /// The two arguments are the horizontal and vertical scroll delta in CSS
        /// pixels that was actually applied, positive when scrolling right or down.
        /// The event targets the element under the mouse when the wheel scroll is
        /// applied, and propagates to the nearest ancestor with a handler.
        /// </remarks>
        public Action<float, float>? OnScroll { get; init; }

        /// <summary>
        /// Gets or sets a scroll handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnScroll"/> when both are set.
        /// </remarks>
        public Action<ScrollEvent>? OnScrollEvent { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>paste</c> event. The handler receives
        /// the pasted text.
        /// </summary>
        public Action<string>? OnPaste { get; init; }

        /// <summary>
        /// Gets or sets a paste handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnPaste"/> when both are set.
        /// </remarks>
        public Action<PasteEvent>? OnPasteEvent { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>pointerdown</c> event.
        /// </summary>
        /// <remarks>
        /// The two arguments are the pointer position relative to the element's
        /// painted border box, not the window.
        /// </remarks>
        public Action<float, float>? OnPointerDown { get; init; }

        /// <summary>
        /// Gets or sets a pointerdown handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnPointerDown"/> when both are set.
        /// </remarks>
        public Action<PointerDownEvent>? OnPointerDownEvent { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>pointermove</c> event.
        /// </summary>
        /// <remarks>
        /// The two arguments are the pointer position relative to the element's
        /// painted border box, not the window.
        /// </remarks>
        public Action<float, float>? OnPointerMove { get; init; }

        /// <summary>
        /// Gets or sets a pointermove handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnPointerMove"/> when both are set.
        /// </remarks>
        public Action<PointerMoveEvent>? OnPointerMoveEvent { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>pointerup</c> event.
        /// </summary>
        /// <remarks>
        /// The two arguments are the pointer position relative to the element's
        /// painted border box, not the window.
        /// </remarks>
        public Action<float, float>? OnPointerUp { get; init; }

        /// <summary>
        /// Gets or sets a pointerup handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnPointerUp"/> when both are set.
        /// </remarks>
        public Action<PointerUpEvent>? OnPointerUpEvent { get; init; }

        /// <summary>
        /// Gets or sets the handler for the host's right-button context-menu event.
        /// </summary>
        /// <remarks>
        /// The event carries the deepest element under the pointer
        /// (<see cref="Event.Target"/>), the element that owns the
        /// handler (<see cref="Event.CurrentTarget"/>) and the pointer
        /// position both in window and element-local coordinates. Attach the
        /// handler to a container to treat its children as one context-menu area.
        /// </remarks>
        public Action<ContextMenuEvent>? OnContextMenu { get; init; }

        /// <summary>
        /// Gets or sets the handler for <c>dragstart</c>, dispatched on the
        /// draggable source once a press moves past the drag threshold.
        /// </summary>
        public Action<DragEvent>? OnDragStart { get; init; }

        /// <summary>
        /// Gets or sets the handler for <c>drag</c>, dispatched on the source as
        /// the pointer moves during an active drag.
        /// </summary>
        public Action<DragEvent>? OnDrag { get; init; }

        /// <summary>
        /// Gets or sets the handler for <c>dragend</c>, dispatched on the source
        /// when the drag finishes, whether or not a drop was accepted.
        /// </summary>
        public Action<DragEvent>? OnDragEnd { get; init; }

        /// <summary>
        /// Gets or sets the handler for <c>dragenter</c>, dispatched when the
        /// pointer first enters a potential drop target.
        /// </summary>
        public Action<DragEvent>? OnDragEnter { get; init; }

        /// <summary>
        /// Gets or sets the handler for <c>dragover</c>, dispatched as the pointer
        /// moves within a potential drop target.
        /// </summary>
        public Action<DragEvent>? OnDragOver { get; init; }

        /// <summary>
        /// Gets or sets the handler for <c>dragleave</c>, dispatched when the
        /// pointer leaves a potential drop target.
        /// </summary>
        public Action<DragEvent>? OnDragLeave { get; init; }

        /// <summary>
        /// Gets or sets the handler for <c>drop</c>, dispatched on release over a
        /// target that handles the drag.
        /// </summary>
        public Action<DragEvent>? OnDrop { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>input</c> event. The handler receives
        /// the control's current text.
        /// </summary>
        public Action<string>? OnInput { get; init; }

        /// <summary>
        /// Gets or sets an input handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnInput"/> when both are set.
        /// </remarks>
        public Action<InputEvent>? OnInputEvent { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>change</c> event. The handler receives
        /// the committed value.
        /// </summary>
        public Action<string>? OnChange { get; init; }

        /// <summary>
        /// Gets or sets a change handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnChange"/> when both are set.
        /// </remarks>
        public Action<ChangeEvent>? OnChangeEvent { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>submit</c> event.
        /// </summary>
        public Action? OnSubmit { get; init; }

        /// <summary>
        /// Gets or sets a submit handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnSubmit"/> when both are set.
        /// </remarks>
        public Action<SubmitEvent>? OnSubmitEvent { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>keydown</c> event. The handler
        /// receives the key name, for example <c>Enter</c>, prefixed with the
        /// modifiers that were held, for example <c>Ctrl+Shift+Z</c>. See
        /// <see cref="KeyChord.Format(string, KeyModifiers)"/> for the exact
        /// spelling.
        /// </summary>
        public Action<string>? OnKeyDown { get; init; }

        /// <summary>
        /// Gets or sets a keydown handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnKeyDown"/> when both are set.
        /// </remarks>
        public Action<KeyDownEvent>? OnKeyDownEvent { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>focus</c> event.
        /// </summary>
        public Action? OnFocus { get; init; }

        /// <summary>
        /// Gets or sets a focus handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnFocus"/> when both are set.
        /// </remarks>
        public Action<FocusEvent>? OnFocusEvent { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>blur</c> event.
        /// </summary>
        public Action? OnBlur { get; init; }

        /// <summary>
        /// Gets or sets a blur handler that receives the full event payload.
        /// </summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnBlur"/> when both are set.
        /// </remarks>
        public Action<BlurEvent>? OnBlurEvent { get; init; }

        /// <summary>
        /// Handler invoked by the host for a <c>canvas</c> element, receiving a
        /// fresh Silk.NET OpenGL context scoped to that element's bounds.
        /// </summary>
        public Delegate? OnCanvasContext { get; init; }

        /// <summary>
        /// Handler invoked by the host for a <c>canvas</c> element to record
        /// primitives into the frame's indirect render batch.
        /// </summary>
        public Delegate? OnCanvasDraw { get; init; }

        /// <summary>
        /// Gets or sets whether an <c>audio</c> element paints its built-in
        /// transport controls.
        /// </summary>
        public bool? Controls { get; init; }

        /// <summary>Gets or sets whether an <c>audio</c> element starts playing automatically.</summary>
        public bool? AutoPlay { get; init; }

        /// <summary>Alias for <see cref="AutoPlay"/>.</summary>
        public bool? Autoplay
        {
            get => AutoPlay;
            init => AutoPlay = value;
        }

        /// <summary>Gets or sets whether an <c>audio</c> element loops when it reaches the end.</summary>
        public bool? Loop { get; init; }

        /// <summary>Gets or sets whether an <c>audio</c> element starts muted.</summary>
        public bool? Muted { get; init; }

        /// <summary>
        /// Gets or sets the initial <c>audio</c> volume in the range 0 to 1.
        /// </summary>
        public float? Volume { get; init; }

        /// <summary>
        /// Gets or sets the initial <c>audio</c> playback rate. One is normal speed.
        /// </summary>
        public float? PlaybackRate { get; init; }

        /// <summary>Handler for the <c>play</c> event of an <c>audio</c> element.</summary>
        public Action? OnPlay { get; init; }

        /// <summary>Play handler that receives the full event payload.</summary>
        /// <remarks>This takes precedence over <see cref="OnPlay"/> when both are set.</remarks>
        public Action<PlayEvent>? OnPlayEvent { get; init; }

        /// <summary>Handler for the <c>pause</c> event of an <c>audio</c> element.</summary>
        public Action? OnPause { get; init; }

        /// <summary>Pause handler that receives the full event payload.</summary>
        /// <remarks>This takes precedence over <see cref="OnPause"/> when both are set.</remarks>
        public Action<PauseEvent>? OnPauseEvent { get; init; }

        /// <summary>Handler for the <c>ended</c> event of an <c>audio</c> element.</summary>
        public Action? OnEnded { get; init; }

        /// <summary>Ended handler that receives the full event payload.</summary>
        /// <remarks>This takes precedence over <see cref="OnEnded"/> when both are set.</remarks>
        public Action<EndedEvent>? OnEndedEvent { get; init; }

        /// <summary>
        /// Handler for the <c>timeupdate</c> event of an <c>audio</c> element. The
        /// argument is the current playback position in seconds.
        /// </summary>
        public Action<float>? OnTimeUpdate { get; init; }

        /// <summary>Timeupdate handler that receives the full event payload.</summary>
        /// <remarks>
        /// This takes precedence over <see cref="OnTimeUpdate"/> when both are set.
        /// </remarks>
        public Action<TimeUpdateEvent>? OnTimeUpdateEvent { get; init; }

        /// <summary>
        /// Handler invoked when an <c>audio</c> element's player has been created.
        /// The argument is the window-side <c>AudioPlayer</c> control surface.
        /// </summary>
        public Delegate? OnAudioReady { get; init; }

        /// <summary>
        /// Handler invoked when a <c>video</c> element's player has been created.
        /// The argument is the window-side <c>VideoPlayer</c> control surface.
        /// </summary>
        public Delegate? OnVideoReady { get; init; }

        /// <summary>
        /// Handler invoked when a <c>webview</c> element's browser has been
        /// created. The argument is the window-side <c>WebView</c> control
        /// surface.
        /// </summary>
        public Delegate? OnWebViewReady { get; init; }
    }

    /// <summary>
    /// A minimal props payload for a component that only needs the child nodes
    /// supplied by its parent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class deliberately does not derive from <see cref="Props"/>. A component
    /// that accepts children is usually interested in those children, not in element
    /// attributes. Derive from this class when the component also needs its own
    /// values; derive from <see cref="Props"/> instead when it needs the HTML
    /// attribute surface.
    /// </para>
    /// <para>
    /// Children are ordinary virtual nodes. The component decides where they appear
    /// and what surrounds them. Pass them to a helper, or wrap them in a fragment with
    /// <c>F(Props.Children ?? [])</c> when they should sit directly inside the
    /// component's root element.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public sealed class CardProps : PropsWithChildren
    /// {
    ///     public string Title { get; init; } = "";
    /// }
    ///
    /// public sealed class Card : Component&lt;CardProps&gt;
    /// {
    ///     public override Node Render() =&gt;
    ///         H("section", new Props { ClassName = "card" },
    ///             H("h2", new Props(), Text(Props.Title)),
    ///             H("div", new Props { ClassName = "card-body" },
    ///                 F(Props.Children ?? [])));
    /// }
    ///
    /// C&lt;Card, CardProps&gt;(new CardProps
    /// {
    ///     Title = "Files",
    ///     Children = [Text("No files yet.")]
    /// });
    /// </code>
    /// </example>
    /// <seealso cref="Component{TProps}"/>
    public partial class PropsWithChildren
    {
        /// <summary>
        /// Gets the child nodes supplied by the parent, or <see langword="null"/>
        /// when the component was created without children.
        /// </summary>
        public Node?[]? Children { get; init; }
    }
}
