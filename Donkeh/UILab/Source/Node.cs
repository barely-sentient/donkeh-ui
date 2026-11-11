using System.Globalization;
using Donkeh.Data.Dom;
using Donkeh.Parser;

namespace Donkeh.UILab
{
    /// <summary>
    /// The base of the virtual tree. Every node is also an
    /// <see cref="IgniteSupportedDomNode"/>, so a rendered tree can be handed
    /// straight to the selector matcher and layout engine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Elements, text nodes and fragments are the live tree. Components and
    /// context providers are logical nodes: they are kept in the virtual tree so
    /// their instances survive re-renders, but their rendered output is what
    /// appears in <see cref="IgniteSupportedDomNode.Children"/>.
    /// </para>
    /// <para>
    /// A node is mutable and reused across renders. The renderer patches it in
    /// place rather than rebuilding it, which is what keeps revision numbers and
    /// layout cache entries valid. Nodes are not thread-safe and belong to the
    /// thread that mounted the owning <see cref="Renderer"/>.
    /// </para>
    /// <para>
    /// <see cref="Key"/> gives a child a stable identity among its siblings. Keys
    /// are scoped to one child list: the same string may be reused in unrelated
    /// parents without conflict.
    /// </para>
    /// </remarks>
    /// <seealso cref="Renderer"/>
    /// <seealso cref="ElementNode"/>
    /// <seealso cref="TextNode"/>
    /// <seealso cref="FragmentNode"/>
    public abstract class Node : IgniteSupportedDomNode
    {
        /// <summary>
        /// The logical children from the last render. This can differ from
        /// <see cref="IgniteSupportedDomNode.Children"/> because component nodes
        /// are replaced by their output in the live tree.
        /// </summary>
        /// <remarks>
        /// The array is replaced, never mutated, by the reconciler. Logical nodes
        /// appear here in the order the parent rendered them; use
        /// <see cref="IgniteSupportedDomNode.Children"/> for the live order.
        /// </remarks>
        internal Node[] VdomChildren = [];

        /// <summary>
        /// A stable identity among siblings. Keyed children are reused across
        /// reorders instead of being matched by position.
        /// </summary>
        /// <value>
        /// A caller-chosen string, or <see langword="null"/> for positional
        /// matching. The value is compared ordinally.
        /// </value>
        /// <remarks>
        /// <para>
        /// Keys must be unique within one child list. With duplicates the first
        /// match wins and the remaining duplicates mount fresh nodes.
        /// </para>
        /// <para>
        /// Changing a node's key makes the reconciler treat it as a different child
        /// even if its kind is unchanged, so component state below it is discarded.
        /// Prefer <see cref="NodeExtensions.Key{T}(T, string)"/> for chaining.
        /// </para>
        /// </remarks>
        public string? Key { get; set; }

        /// <summary>
        /// Converts text to a <see cref="TextNode"/>, mapping null to no node.
        /// </summary>
        /// <param name="text">The text to wrap, or null to render nothing.</param>
        /// <returns>
        /// A new text node, or <see langword="null"/> when <paramref name="text"/> is
        /// null.
        /// </returns>
        /// <remarks>
        /// This is what lets a <c>string</c> child be written directly in a child
        /// list. The node is created eagerly, so conditional text still allocates a
        /// node unless the null branch is taken.
        /// </remarks>
        public static implicit operator Node?(string? text) =>
            text is null ? null : new TextNode(text);

        /// <summary>
        /// Converts an integer to a text node using invariant culture.
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <returns>A new text node containing the formatted value.</returns>
        public static implicit operator Node?(int value) =>
            new TextNode(value.ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// Converts a long integer to a text node using invariant culture.
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <returns>A new text node containing the formatted value.</returns>
        public static implicit operator Node?(long value) =>
            new TextNode(value.ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// Converts a single-precision number to a text node using invariant
        /// culture.
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <returns>A new text node containing the formatted value.</returns>
        public static implicit operator Node?(float value) =>
            new TextNode(value.ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// Converts a double-precision number to a text node using invariant
        /// culture.
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <returns>A new text node containing the formatted value.</returns>
        /// <remarks>
        /// Invariant culture keeps decimal separators stable across machines, which
        /// matters because the rendered text may feed the CSS engine's value parser.
        /// </remarks>
        public static implicit operator Node?(double value) =>
            new TextNode(value.ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// Converts a boolean to the text <c>true</c> or <c>false</c>.
        /// </summary>
        /// <param name="value">The value to render.</param>
        /// <returns>A new text node containing the lowercase literal.</returns>
        public static implicit operator Node?(bool value) =>
            new TextNode(value ? "true" : "false");
    }

    /// <summary>
    /// A concrete element. The node is used both as the virtual description and
    /// as the live DOM node, which is what lets a diff update the tree in place
    /// and preserve layout caches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Elements are created by the builder API, typically
    /// <see cref="_.H(string)"/>, and should not be constructed directly outside
    /// this assembly. Attribute names, class names and attribute values are
    /// interned, so patching compares integers rather than strings.
    /// </para>
    /// <para>
    /// An element owns its bound <see cref="Ref{T}"/>, its event handler map and
    /// its child arrays. Unmounting releases all three so a dead node can be
    /// collected. Elements are not thread-safe.
    /// </para>
    /// </remarks>
    /// <seealso cref="IgniteSupportedDomNode"/>
    public sealed class ElementNode : Node
    {
        /// <summary>Interned id of the <c>id</c> attribute name.</summary>
        internal static readonly int IdAttribute = NameCache.Get("id");

        /// <summary>Interned id of the <c>class</c> attribute name.</summary>
        internal static readonly int ClassAttribute = NameCache.Get("class");

        /// <summary>
        /// Interned class name ids, kept sorted and duplicate-free by
        /// <see cref="AddClassesFromString"/>.
        /// </summary>
        internal int[] ClassIds = [];

        /// <summary>
        /// Attributes other than <c>id</c> and <c>class</c>, as name/value id
        /// pairs. Replaced wholesale by <see cref="PatchFrom"/>.
        /// </summary>
        internal DomAttribute[] Attributes = [];

        /// <summary>
        /// Handlers keyed by interned event name, or <see langword="null"/> when the
        /// element has no handlers. Owned by the live element and replaced on patch.
        /// </summary>
        internal Dictionary<int, Delegate>? Events;

        /// <summary>
        /// The ref bound to this element, or <see langword="null"/>. It is assigned
        /// during mount and cleared on unmount.
        /// </summary>
        internal Ref<ElementNode>? Ref;

        /// <summary>
        /// Creates an element with the given tag name.
        /// </summary>
        /// <param name="tagName">
        /// The tag name, for example <c>div</c>. Must not be null or empty.
        /// </param>
        /// <exception cref="ArgumentException">
        /// <paramref name="tagName"/> is <see langword="null"/> or empty. Whitespace
        /// is accepted and interned as written.
        /// </exception>
        /// <remarks>
        /// The tag name is interned immediately, so the constructor allocates only
        /// for the node itself. Use the builder API rather than calling this
        /// directly.
        /// </remarks>
        internal ElementNode(string tagName)
        {
            ArgumentException.ThrowIfNullOrEmpty(tagName);
            InternedTagName = NameCache.Get(tagName);
        }

        /// <summary>The tag name this element was created with.</summary>
        /// <value>
        /// The original case-preserved tag text, or an empty string when the
        /// interned id is not resolvable.
        /// </value>
        /// <remarks>
        /// The getter resolves the interned id on every call. Cache the result if it
        /// is needed in a hot loop; the renderer compares
        /// <see cref="IgniteSupportedDomNode.InternedTagName"/> instead.
        /// </remarks>
        public string Tag => InternTable.GetString(InternedTagName) ?? string.Empty;

        /// <summary>
        /// Copies a props object onto this element, creating attributes, classes and
        /// event handlers.
        /// </summary>
        /// <param name="props">The props to apply, or <see langword="null"/> to do nothing.</param>
        /// <remarks>
        /// Called by the builder API when an element is created. Only properties that
        /// are set are applied, so an absent prop never clears an existing
        /// attribute. The <c>id</c> and <c>class</c> props route to the dedicated
        /// fields rather than the attribute list.
        /// </remarks>
        internal void ApplyProps(Props? props)
        {
            if (props is null)
            {
                return;
            }

            if (props.Id is { } id)
            {
                AddStringAttribute("id", id);
            }

            if (props.ClassName is { } className)
            {
                AddStringAttribute("class", className);
            }

            if (props.Title is { } title)
            {
                AddStringAttribute("title", title);
            }

            if (props.Href is { } href)
            {
                AddStringAttribute("href", href);
            }

            if (props.Src is { } src)
            {
                AddStringAttribute("src", src);
            }

            if (props.Type is { } type)
            {
                AddStringAttribute("type", type);
            }

            if (props.Value is { } value)
            {
                AddStringAttribute("value", value);
            }

            if (props.Placeholder is { } placeholder)
            {
                AddStringAttribute("placeholder", placeholder);
            }

            if (props.Role is { } role)
            {
                AddStringAttribute("role", role);
            }

            if (props.Alt is { } alt)
            {
                AddStringAttribute("alt", alt);
            }

            if (props.Name is { } name)
            {
                AddStringAttribute("name", name);
            }

            if (props.Target is { } target)
            {
                AddStringAttribute("target", target);
            }

            if (props.Rel is { } rel)
            {
                AddStringAttribute("rel", rel);
            }

            if (props.TabIndex is { } tabIndex)
            {
                AddStringAttribute("tabindex", tabIndex.ToString(CultureInfo.InvariantCulture));
            }

            if (props.Disabled is true)
            {
                AddStringAttribute("disabled", string.Empty);
            }

            if (props.Checked is true)
            {
                AddStringAttribute("checked", string.Empty);
            }

            if (props.Key is { } key)
            {
                Key = key;
            }

            if (props.Attributes is { } attributes)
            {
                foreach (var (attrName, attributeValue) in attributes)
                {
                    AddStringAttribute(attrName, attributeValue);
                }
            }

            if (props.OnClick is { } onClick)
            {
                On("click", onClick);
            }

            if (props.OnMouseOver is { } onMouseOver)
            {
                On("mouseover", onMouseOver);
            }

            if (props.OnMouseOut is { } onMouseOut)
            {
                On("mouseout", onMouseOut);
            }

            if (props.OnPaste is { } onPaste)
            {
                On("paste", onPaste);
            }

            if (props.OnPointerDown is { } onPointerDown)
            {
                On("pointerdown", onPointerDown);
            }

            if (props.OnPointerMove is { } onPointerMove)
            {
                On("pointermove", onPointerMove);
            }

            if (props.OnPointerUp is { } onPointerUp)
            {
                On("pointerup", onPointerUp);
            }

            if (props.OnInput is { } onInput)
            {
                On("input", onInput);
            }

            if (props.OnChange is { } onChange)
            {
                On("change", onChange);
            }

            if (props.OnSubmit is { } onSubmit)
            {
                On("submit", onSubmit);
            }

            if (props.OnKeyDown is { } onKeyDown)
            {
                On("keydown", onKeyDown);
            }

            if (props.OnFocus is { } onFocus)
            {
                On("focus", onFocus);
            }

            if (props.OnBlur is { } onBlur)
            {
                On("blur", onBlur);
            }
        }

        /// <summary>
        /// Adds or replaces an attribute and returns the element for chaining.
        /// </summary>
        /// <param name="name">The attribute name. Must not be null or empty.</param>
        /// <param name="value">The attribute value, or null to remove the attribute.</param>
        /// <returns>This element, so calls can be chained.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="name"/> is null or empty.
        /// </exception>
        /// <remarks>
        /// The <c>id</c> and <c>class</c> names update the dedicated fields; every
        /// other name goes into the attribute list. Assigning null leaves the
        /// element unchanged rather than removing the attribute.
        /// </remarks>
        public ElementNode WithAttribute(string name, string? value)
        {
            AddStringAttribute(name, value);
            return this;
        }

        /// <summary>
        /// Adds an attribute that can carry either a value or an event handler.
        /// </summary>
        /// <param name="attribute">The attribute to apply.</param>
        /// <returns>This element, so calls can be chained.</returns>
        /// <remarks>
        /// When <see cref="Attr.Handler"/> is set the attribute becomes an event
        /// subscription instead of a DOM attribute; the name is normalised by
        /// <see cref="On"/>.
        /// </remarks>
        public ElementNode WithAttribute(Attr attribute)
        {
            if (attribute.Handler is { } handler)
            {
                return On(attribute.Name, handler);
            }

            return WithAttribute(attribute.Name, attribute.Value);
        }

        /// <summary>
        /// Adds one or more classes and returns the element for chaining.
        /// </summary>
        /// <param name="className">
        /// One or more whitespace-separated class names. A null value is ignored.
        /// </param>
        /// <returns>This element, so calls can be chained.</returns>
        /// <remarks>
        /// The string is split on whitespace and each token is interned. Unknown
        /// classes are appended, then the id array is re-sorted so equal class sets
        /// compare equal regardless of order. This means <c>WithClass</c> can only
        /// add classes, never replace the existing set.
        /// </remarks>
        public ElementNode WithClass(string className)
        {
            AddStringAttribute("class", className);
            return this;
        }

        /// <summary>
        /// Sets the id and returns the element for chaining.
        /// </summary>
        /// <param name="id">The id value. A null value is ignored.</param>
        /// <returns>This element, so calls can be chained.</returns>
        public ElementNode WithId(string id)
        {
            AddStringAttribute("id", id);
            return this;
        }

        /// <summary>
        /// Routes a string attribute to the id field, the class id array or the
        /// general attribute list.
        /// </summary>
        /// <param name="name">The attribute name. Must not be null or empty.</param>
        /// <param name="value">The value, or null to leave the element unchanged.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="name"/> is null or empty.
        /// </exception>
        /// <remarks>
        /// Names and values are interned so the later diff can compare ids. The
        /// <c>class</c> path accumulates classes and keeps them sorted; the
        /// remaining names replace any existing attribute with the same name.
        /// </remarks>
        internal void AddStringAttribute(string name, string? value)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("An attribute name cannot be empty.", nameof(name));
            }

            // A null value removes the attribute, matching React.
            if (value is null)
            {
                return;
            }

            var nameId = NameCache.Get(name);

            if (nameId == IdAttribute)
            {
                Id = value;
                return;
            }

            if (nameId == ClassAttribute)
            {
                AddClassesFromString(value);
                return;
            }

            AddDomAttribute(nameId, InternTable.Intern(value));
        }

        /// <summary>
        /// Attaches an event handler. The name may be given as <c>click</c> or
        /// <c>onClick</c>; both normalise to the same interned id.
        /// </summary>
        /// <param name="eventName">The event name, with or without the <c>on</c> prefix.</param>
        /// <param name="handler">The delegate to invoke when the host raises the event.</param>
        /// <returns>This element, so calls can be chained.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="eventName"/> is null or empty.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="handler"/> is null.
        /// </exception>
        /// <remarks>
        /// The handler map is allocated on first use. Registering the same event
        /// twice replaces the previous handler rather than adding a second one, so
        /// there is no multicast support at this layer.
        /// </remarks>
        public ElementNode On(string eventName, Delegate handler)
        {
            ArgumentException.ThrowIfNullOrEmpty(eventName);
            ArgumentNullException.ThrowIfNull(handler);

            Events ??= new Dictionary<int, Delegate>();
            Events[NormalizeEventName(eventName)] = handler;
            return this;
        }

        /// <summary>
        /// Looks up the handler registered for an event. This is the hook a host
        /// uses to route input into the tree.
        /// </summary>
        /// <param name="eventName">The event name, with or without the <c>on</c> prefix.</param>
        /// <param name="handler">
        /// The registered delegate, or <see langword="null"/> when no handler is
        /// registered.
        /// </param>
        /// <returns>True when a handler was found; otherwise false.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="eventName"/> is null or empty.
        /// </exception>
        public bool TryGetEventHandler(string eventName, out Delegate? handler)
        {
            ArgumentException.ThrowIfNullOrEmpty(eventName);

            if (Events is not null
                && Events.TryGetValue(NormalizeEventName(eventName), out var value))
            {
                handler = value;
                return true;
            }

            handler = null;
            return false;
        }

        /// <summary>
        /// Invokes the handler registered for an event. Convenience for hosts;
        /// hot paths should call <see cref="TryGetEventHandler"/> once and invoke
        /// the typed delegate directly.
        /// </summary>
        /// <param name="eventName">The event name, with or without the <c>on</c> prefix.</param>
        /// <param name="args">
        /// The arguments passed to the handler. They are not validated against the
        /// delegate signature.
        /// </param>
        /// <returns>True when a handler was found and invoked; otherwise false.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="eventName"/> is null or empty.
        /// </exception>
        /// <exception cref="System.Reflection.TargetInvocationException">
        /// The handler itself threw; the original exception is its
        /// <see cref="Exception.InnerException"/>.
        /// </exception>
        /// <remarks>
        /// The invocation goes through <see cref="Delegate.DynamicInvoke"/>,
        /// which boxes arguments and allocates an argument array on every call. Route
        /// input by caching the typed delegate when this is on a hot path.
        /// </remarks>
        public bool InvokeEventHandler(string eventName, params object?[] args)
        {
            if (!TryGetEventHandler(eventName, out var handler) || handler is null)
            {
                return false;
            }

            handler.DynamicInvoke(args);
            return true;
        }

        /// <summary>Registers a parameterless click handler.</summary>
        /// <param name="handler">The handler to invoke on click.</param>
        /// <returns>This element, so calls can be chained.</returns>
        public ElementNode OnClick(Action handler) => On("click", handler);

        /// <summary>Registers an input handler that receives the current value.</summary>
        /// <param name="handler">The handler to invoke when the input value changes.</param>
        /// <returns>This element, so calls can be chained.</returns>
        public ElementNode OnInput(Action<string> handler) => On("input", handler);

        /// <summary>Registers a change handler that receives the current value.</summary>
        /// <param name="handler">The handler to invoke when the value is committed.</param>
        /// <returns>This element, so calls can be chained.</returns>
        public ElementNode OnChange(Action<string> handler) => On("change", handler);

        /// <summary>Registers a parameterless submit handler.</summary>
        /// <param name="handler">The handler to invoke on submit.</param>
        /// <returns>This element, so calls can be chained.</returns>
        public ElementNode OnSubmit(Action handler) => On("submit", handler);

        /// <summary>Registers a key-down handler that receives the key name.</summary>
        /// <param name="handler">The handler to invoke when a key is pressed.</param>
        /// <returns>This element, so calls can be chained.</returns>
        public ElementNode OnKeyDown(Action<string> handler) => On("keydown", handler);

        /// <summary>
        /// Binds a ref to this element. The ref is set when the element mounts and
        /// cleared when it unmounts.
        /// </summary>
        /// <param name="reference">The ref to fill with this element.</param>
        /// <returns>This element, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="reference"/> is null.
        /// </exception>
        /// <remarks>
        /// Only one ref can be bound at a time; a later call replaces the previous
        /// one. The ref is not set until the element is actually mounted, so a ref
        /// read before the first commit returns its previous value.
        /// </remarks>
        public ElementNode BindRef(Ref<ElementNode> reference)
        {
            ArgumentNullException.ThrowIfNull(reference);
            Ref = reference;
            return this;
        }

        /// <summary>
        /// Publishes this element to the bound ref, if any.
        /// </summary>
        /// <remarks>
        /// Called by the renderer during mount and whenever a patch swaps the ref.
        /// It overwrites any previous value, including one pointing at another
        /// element.
        /// </remarks>
        internal void AttachRef()
        {
            if (Ref is { } reference)
            {
                reference.Value = this;
            }
        }

        /// <summary>
        /// Clears the bound ref when it still points at this element.
        /// </summary>
        /// <remarks>
        /// The reference check prevents this element from clearing a ref that has
        /// already been rebound to a newer element.
        /// </remarks>
        internal void DetachRef()
        {
            if (Ref is { } reference && ReferenceEquals(reference.Value, this))
            {
                reference.Value = default;
            }
        }

        /// <summary>
        /// Copies the attributes, events and ref of a freshly rendered element
        /// onto this live element. Only actual changes touch the DOM, so an
        /// unchanged element keeps its revision and its layout cache entry.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Attributes are compared by interned name and value id. Attributes present
        /// on the live element but absent from <paramref name="source"/> are removed
        /// first, then changed or new values are written, so the operation is
        /// order-independent and idempotent.
        /// </para>
        /// <para>
        /// The live element ends up sharing the source's attribute and event arrays.
        /// The source element is a throwaway description and is never mounted, so
        /// sharing is safe. Ref changes are applied through
        /// <see cref="DetachRef"/> and <see cref="AttachRef"/>.
        /// </para>
        /// </remarks>
        internal void PatchFrom(ElementNode source)
        {
            if (InternedId != source.InternedId)
            {
                InternedId = source.InternedId;
            }

            if (!ClassIdsEqual(ClassIds, source.ClassIds))
            {
                ClassIds = source.ClassIds;
                SetInternedClassName(ClassIds);
            }

            for (var i = 0; i < Attributes.Length; i++)
            {
                var oldAttribute = Attributes[i];

                if (!ContainsName(source.Attributes, oldAttribute.NameId))
                {
                    RemoveAttribute(oldAttribute.NameId);
                }
            }

            for (var i = 0; i < source.Attributes.Length; i++)
            {
                var newAttribute = source.Attributes[i];
                var oldValue = FindValue(Attributes, newAttribute.NameId, out var found);

                if (!found || oldValue != newAttribute.ValueId)
                {
                    SetAttribute(newAttribute.NameId, newAttribute.ValueId);
                }
            }

            Attributes = source.Attributes;
            Events = source.Events;

            if (!ReferenceEquals(Ref, source.Ref))
            {
                DetachRef();
                Ref = source.Ref;
                AttachRef();
            }
        }

        /// <summary>
        /// Splits a whitespace-separated class string, interns each token and merges
        /// the new ids into the sorted class id array.
        /// </summary>
        /// <param name="value">The class text to merge. Must not be null.</param>
        /// <remarks>
        /// Already-present classes are skipped rather than duplicated, and the array
        /// is re-sorted so <see cref="ClassIdsEqual"/> can compare positionally.
        /// Empty tokens from repeated whitespace are ignored.
        /// </remarks>
        private void AddClassesFromString(string value)
        {
            var start = 0;

            for (var i = 0; i <= value.Length; i++)
            {
                if (i < value.Length && !IsWhitespace(value[i]))
                {
                    continue;
                }

                if (i > start)
                {
                    var classId = InternTable.Intern(value.AsSpan(start, i - start));

                    if (Array.IndexOf(ClassIds, classId) < 0)
                    {
                        Array.Resize(ref ClassIds, ClassIds.Length + 1);
                        ClassIds[^1] = classId;
                    }
                }

                start = i + 1;
            }

            Array.Sort(ClassIds);
            SetInternedClassName(ClassIds);
        }

        /// <summary>
        /// Writes an already interned attribute, replacing any entry with the same
        /// name.
        /// </summary>
        /// <param name="nameId">The interned attribute name.</param>
        /// <param name="valueId">The interned attribute value.</param>
        private void AddDomAttribute(int nameId, int valueId)
        {
            for (var i = 0; i < Attributes.Length; i++)
            {
                if (Attributes[i].NameId == nameId)
                {
                    Attributes[i] = new DomAttribute(nameId, valueId);
                    SetAttribute(nameId, valueId);
                    return;
                }
            }

            Array.Resize(ref Attributes, Attributes.Length + 1);
            Attributes[^1] = new DomAttribute(nameId, valueId);
            SetAttribute(nameId, valueId);
        }

        /// <summary>
        /// Normalises an event name to its interned lowercase form without the
        /// <c>on</c> prefix.
        /// </summary>
        /// <param name="name">The caller-supplied event name.</param>
        /// <returns>The interned id of the normalised name.</returns>
        /// <remarks>
        /// Only a leading <c>on</c> is stripped, and only when something follows it,
        /// so an event literally named <c>on</c> keeps its name. The remainder is
        /// lowercased with the invariant culture before interning, which makes
        /// <c>onClick</c> and <c>click</c> the same key.
        /// </remarks>
        private static int NormalizeEventName(string name)
        {
            if (name.StartsWith("on", StringComparison.Ordinal) && name.Length > 2)
            {
                name = name[2..];
            }

            return NameCache.Get(name.ToLowerInvariant());
        }

        /// <summary>
        /// Compares two class id arrays positionally.
        /// </summary>
        /// <param name="left">The first array.</param>
        /// <param name="right">The second array.</param>
        /// <returns>True when both arrays hold the same ids in the same order.</returns>
        /// <remarks>
        /// Both arrays are kept sorted, so positional equality is set equality.
        /// </remarks>
        private static bool ClassIdsEqual(int[] left, int[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Tests whether an attribute list contains an interned attribute name.
        /// </summary>
        /// <param name="attributes">The attribute list to search.</param>
        /// <param name="nameId">The interned name to find.</param>
        /// <returns>True when an entry with that name exists.</returns>
        private static bool ContainsName(DomAttribute[] attributes, int nameId)
        {
            for (var i = 0; i < attributes.Length; i++)
            {
                if (attributes[i].NameId == nameId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Finds the interned value of the first attribute with the given name.
        /// </summary>
        /// <param name="attributes">The attribute list to search.</param>
        /// <param name="nameId">The interned name to find.</param>
        /// <param name="found">True when the name was present.</param>
        /// <returns>The interned value id, or -1 when the name is absent.</returns>
        private static int FindValue(DomAttribute[] attributes, int nameId, out bool found)
        {
            for (var i = 0; i < attributes.Length; i++)
            {
                if (attributes[i].NameId == nameId)
                {
                    found = true;
                    return attributes[i].ValueId;
                }
            }

            found = false;
            return -1;
        }

        /// <summary>
        /// Reports whether a character separates class tokens.
        /// </summary>
        /// <param name="c">The character to test.</param>
        /// <returns>True for space, tab, newline, carriage return or form feed.</returns>
        private static bool IsWhitespace(char c) =>
            c is ' ' or '\t' or '\n' or '\r' or '\f';
    }

    /// <summary>
    /// A text run. Text nodes do not match selectors and are laid out inline.
    /// When an element only contains text, the renderer folds the text into the
    /// element's <see cref="IgniteSupportedDomNode.TextContent"/> so no extra box
    /// is created at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The text is stored in <see cref="IgniteSupportedDomNode.TextContent"/>, so
    /// assigning <see cref="Text"/> goes through the same revision-aware setter as
    /// an element's folded text. Setting the same string does not bump the
    /// revision.
    /// </para>
    /// <para>
    /// A text node inside an element that also has element children is a real child
    /// in the live tree and is laid out inline; it is only folded when the whole
    /// child list is text.
    /// </para>
    /// </remarks>
    public sealed class TextNode : Node
    {
        /// <summary>
        /// Creates a text node with the given text.
        /// </summary>
        /// <param name="text">
        /// The initial text. A null value leaves the node without text, which reads
        /// as an empty string through <see cref="Text"/>.
        /// </param>
        /// <remarks>
        /// Construction marks the node as a text node so selector matching and
        /// layout treat it as inline content. Prefer the implicit string
        /// conversion on <see cref="Node"/> over calling this directly.
        /// </remarks>
        internal TextNode(string text)
        {
            IsTextNode = true;
            TextContent = text;
        }

        /// <summary>
        /// Gets or sets the text carried by this node.
        /// </summary>
        /// <value>
        /// The current text, never <see langword="null"/>; an unset value reads as
        /// an empty string.
        /// </value>
        /// <remarks>
        /// The setter stores through <see cref="IgniteSupportedDomNode.TextContent"/>
        /// and therefore bumps <see cref="IgniteSupportedDomNode.Revision"/> only
        /// when the string actually changes.
        /// </remarks>
        public string Text
        {
            get => TextContent ?? string.Empty;
            set => TextContent = value;
        }
    }

    /// <summary>
    /// A transparent group of nodes. Fragments do not match selectors and
    /// generate no box, so they behave like a React fragment.
    /// </summary>
    /// <remarks>
    /// A fragment is a live node with children but no tag, id, classes or
    /// attributes. It is used for grouping and as the backing node for context
    /// providers. Because it has no box, text inside a fragment cannot be folded
    /// and always stays in <see cref="TextNode"/> children.
    /// </remarks>
    /// <seealso cref="Node"/>
    public sealed class FragmentNode : Node
    {
        /// <summary>
        /// Creates an empty fragment.
        /// </summary>
        /// <remarks>
        /// Children are assigned by the builder or the renderer through
        /// <see cref="Node.VdomChildren"/>; a bare fragment renders nothing.
        /// </remarks>
        internal FragmentNode()
        {
            IsFragment = true;
        }
    }

    /// <summary>
    /// A logical node that owns a component instance. The instance is reused
    /// across renders, which is what keeps hook state alive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A component node never enters the live tree. The renderer keeps it in the
    /// parent's <see cref="Node.VdomChildren"/> and stores the rendered output in
    /// <see cref="OutputVdom"/>. <see cref="Instance"/> is the component object
    /// whose <see cref="Component.Render"/> produced that output.
    /// </para>
    /// <para>
    /// Identity is the component type plus the node's <see cref="Node.Key"/>. Two
    /// renders of the same type in the same slot reuse one instance; changing the
    /// type or key mounts a new instance and discards the old hook state.
    /// </para>
    /// </remarks>
    /// <seealso cref="Component"/>
    /// <seealso cref="Renderer"/>
    public sealed class ComponentNode : Node
    {
        /// <summary>
        /// Creates a component description for the given type.
        /// </summary>
        /// <param name="componentType">The concrete component type to instantiate on mount.</param>
        /// <param name="props">The props passed to the instance, or null for none.</param>
        /// <param name="key">The sibling key, or null for positional matching.</param>
        internal ComponentNode(Type componentType, object? props, string? key)
        {
            ComponentType = componentType;
            Props = props;
            Key = key;
        }

        /// <summary>The concrete component type instantiated on mount.</summary>
        internal Type ComponentType { get; }

        /// <summary>
        /// The props for the current render. Replaced by the parent's diff and
        /// pushed to the instance before it re-renders.
        /// </summary>
        internal object? Props { get; set; }

        /// <summary>
        /// The mounted component instance, or null while unmounted. Cleared on
        /// unmount so the logical node does not keep the instance alive.
        /// </summary>
        internal Component? Instance { get; set; }

        /// <summary>
        /// The logical output of the instance's last render. This is not the live
        /// node; unwrap it with the renderer's output-chain walk before use.
        /// </summary>
        internal Node? OutputVdom { get; set; }

        /// <summary>
        /// The live parent that owns the output slot, or null when the component is
        /// the root. Used to replace the output in place when it changes kind.
        /// </summary>
        internal Node? ParentLive { get; set; }

        /// <summary>
        /// The index of the component's output inside <see cref="ParentLive"/>'s
        /// children, or -1 when unknown. Refreshed on every child diff.
        /// </summary>
        internal int OutputIndex { get; set; } = -1;
    }

    /// <summary>
    /// A logical node that publishes a context value to the components below it.
    /// It renders as a transparent fragment.
    /// </summary>
    /// <remarks>
    /// The provider is not a component, so republishing a value does not
    /// re-render consumers by itself. Components read the value through
    /// <see cref="Component.UseContext{T}"/> using the scope that was in force when
    /// they rendered.
    /// </remarks>
    /// <seealso cref="Context{T}"/>
    /// <seealso cref="Component.UseContext{T}"/>
    internal sealed class ProviderNode : Node
    {
        /// <summary>
        /// The context object being provided. Identity is by reference, so this must
        /// be the same <see cref="Context{T}"/> instance a consumer reads with.
        /// </summary>
        internal required object Context { get; init; }

        /// <summary>
        /// The value published to descendants for the current render. Mutable so a
        /// diff can update it in place without remounting the fragment.
        /// </summary>
        internal object? Value { get; set; }

        /// <summary>
        /// The logical children rendered under the provider, before the backing
        /// fragment is mounted.
        /// </summary>
        internal Node[] ProviderChildren { get; set; } = [];

        /// <summary>
        /// The transparent fragment that carries the provider's children in the live
        /// tree, or null while the provider is unmounted.
        /// </summary>
        internal FragmentNode? OutputVdom { get; set; }

        /// <summary>
        /// The live parent that owns the fragment's slot, or null at the root.
        /// </summary>
        internal Node? ParentLive { get; set; }

        /// <summary>
        /// The index of the fragment inside <see cref="ParentLive"/>'s children, or
        /// -1 when unknown.
        /// </summary>
        internal int OutputIndex { get; set; } = -1;
    }

    /// <summary>
    /// Fluent helpers for <see cref="Node"/> instances.
    /// </summary>
    public static class NodeExtensions
    {
        /// <summary>Assigns a stable key and returns the same node for chaining.</summary>
        /// <typeparam name="T">The concrete node type, preserved through the call.</typeparam>
        /// <param name="node">The node to key. Must not be null.</param>
        /// <param name="key">The key to assign. Must not be null or empty.</param>
        /// <returns>The same node instance, so the call can stay inside an expression.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="key"/> is null or empty.
        /// </exception>
        /// <remarks>
        /// Unlike the builder overload, this keeps the concrete type so
        /// <see cref="ElementNode"/>-specific calls can follow. Keys only need to be
        /// unique among one parent's children.
        /// </remarks>
        public static T Key<T>(this T node, string key) where T : Node
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentException.ThrowIfNullOrEmpty(key);
            node.Key = key;
            return node;
        }
    }
}
