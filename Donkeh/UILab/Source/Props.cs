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
    public class Props
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
        /// Gets or sets the handler for the <c>mouseover</c> event.
        /// </summary>
        public Action? OnMouseOver { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>mouseout</c> event.
        /// </summary>
        public Action? OnMouseOut { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>paste</c> event. The handler receives
        /// the pasted text.
        /// </summary>
        public Action<string>? OnPaste { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>pointerdown</c> event.
        /// </summary>
        /// <remarks>
        /// The two arguments are the pointer position relative to the element's
        /// painted border box, not the window.
        /// </remarks>
        public Action<float, float>? OnPointerDown { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>pointermove</c> event.
        /// </summary>
        /// <remarks>
        /// The two arguments are the pointer position relative to the element's
        /// painted border box, not the window.
        /// </remarks>
        public Action<float, float>? OnPointerMove { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>pointerup</c> event.
        /// </summary>
        /// <remarks>
        /// The two arguments are the pointer position relative to the element's
        /// painted border box, not the window.
        /// </remarks>
        public Action<float, float>? OnPointerUp { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>input</c> event. The handler receives
        /// the control's current text.
        /// </summary>
        public Action<string>? OnInput { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>change</c> event. The handler receives
        /// the committed value.
        /// </summary>
        public Action<string>? OnChange { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>submit</c> event.
        /// </summary>
        public Action? OnSubmit { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>keydown</c> event. The handler
        /// receives the key name, for example <c>Enter</c>.
        /// </summary>
        public Action<string>? OnKeyDown { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>focus</c> event.
        /// </summary>
        public Action? OnFocus { get; init; }

        /// <summary>
        /// Gets or sets the handler for the <c>blur</c> event.
        /// </summary>
        public Action? OnBlur { get; init; }

        /// <summary>
        /// Handler invoked by the host for a <c>canvas</c> element, receiving a
        /// fresh Silk.NET OpenGL context scoped to that element's bounds.
        /// </summary>
        public Delegate? OnCanvasContext { get; init; }
    }
}
