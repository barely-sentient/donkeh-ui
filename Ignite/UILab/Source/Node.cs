using System.Globalization;
using Ignite.Data.Dom;
using Ignite.Parser;

namespace Ignite.UILab
{
    /// <summary>
    /// The base of the virtual tree. Every node is also an
    /// <see cref="IgniteSupportedDomNode"/>, so a rendered tree can be handed
    /// straight to the selector matcher and layout engine.
    /// </summary>
    /// <remarks>
    /// Elements, text nodes and fragments are the live tree. Components and
    /// context providers are logical nodes: they are kept in the virtual tree so
    /// their instances survive re-renders, but their rendered output is what
    /// appears in <see cref="IgniteSupportedDomNode.Children"/>.
    /// </remarks>
    public abstract class Node : IgniteSupportedDomNode
    {
        /// <summary>
        /// The logical children from the last render. This can differ from
        /// <see cref="IgniteSupportedDomNode.Children"/> because component nodes
        /// are replaced by their output in the live tree.
        /// </summary>
        internal Node[] VdomChildren = [];

        /// <summary>
        /// A stable identity among siblings. Keyed children are reused across
        /// reorders instead of being matched by position.
        /// </summary>
        public string? Key { get; set; }

        public static implicit operator Node?(string? text) =>
            text is null ? null : new TextNode(text);

        public static implicit operator Node?(int value) =>
            new TextNode(value.ToString(CultureInfo.InvariantCulture));

        public static implicit operator Node?(long value) =>
            new TextNode(value.ToString(CultureInfo.InvariantCulture));

        public static implicit operator Node?(float value) =>
            new TextNode(value.ToString(CultureInfo.InvariantCulture));

        public static implicit operator Node?(double value) =>
            new TextNode(value.ToString(CultureInfo.InvariantCulture));

        public static implicit operator Node?(bool value) =>
            new TextNode(value ? "true" : "false");
    }

    /// <summary>
    /// A concrete element. The node is used both as the virtual description and
    /// as the live DOM node, which is what lets a diff update the tree in place
    /// and preserve layout caches.
    /// </summary>
    public sealed class ElementNode : Node
    {
        internal static readonly int IdAttribute = NameCache.Get("id");
        internal static readonly int ClassAttribute = NameCache.Get("class");

        internal int[] ClassIds = [];
        internal DomAttribute[] Attributes = [];
        internal Dictionary<int, Delegate>? Events;
        internal Ref<ElementNode>? Ref;

        internal ElementNode(string tagName)
        {
            ArgumentException.ThrowIfNullOrEmpty(tagName);
            InternedTagName = NameCache.Get(tagName);
        }

        /// <summary>The tag name this element was created with.</summary>
        public string Tag => InternTable.GetString(InternedTagName) ?? string.Empty;

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
        public ElementNode WithAttribute(string name, string? value)
        {
            AddStringAttribute(name, value);
            return this;
        }

        /// <summary>
        /// Adds an attribute that can carry either a value or an event handler.
        /// </summary>
        public ElementNode WithAttribute(Attr attribute)
        {
            if (attribute.Handler is { } handler)
            {
                return On(attribute.Name, handler);
            }

            return WithAttribute(attribute.Name, attribute.Value);
        }

        /// <summary>
        /// Adds or replaces a class and returns the element for chaining.
        /// </summary>
        public ElementNode WithClass(string className)
        {
            AddStringAttribute("class", className);
            return this;
        }

        /// <summary>
        /// Sets the id and returns the element for chaining.
        /// </summary>
        public ElementNode WithId(string id)
        {
            AddStringAttribute("id", id);
            return this;
        }

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
        public bool InvokeEventHandler(string eventName, params object?[] args)
        {
            if (!TryGetEventHandler(eventName, out var handler) || handler is null)
            {
                return false;
            }

            handler.DynamicInvoke(args);
            return true;
        }

        public ElementNode OnClick(Action handler) => On("click", handler);

        public ElementNode OnInput(Action<string> handler) => On("input", handler);

        public ElementNode OnChange(Action<string> handler) => On("change", handler);

        public ElementNode OnSubmit(Action handler) => On("submit", handler);

        public ElementNode OnKeyDown(Action<string> handler) => On("keydown", handler);

        /// <summary>
        /// Binds a ref to this element. The ref is set when the element mounts and
        /// cleared when it unmounts.
        /// </summary>
        public ElementNode BindRef(Ref<ElementNode> reference)
        {
            ArgumentNullException.ThrowIfNull(reference);
            Ref = reference;
            return this;
        }

        internal void AttachRef()
        {
            if (Ref is { } reference)
            {
                reference.Value = this;
            }
        }

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

        private static int NormalizeEventName(string name)
        {
            if (name.StartsWith("on", StringComparison.Ordinal) && name.Length > 2)
            {
                name = name[2..];
            }

            return NameCache.Get(name.ToLowerInvariant());
        }

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

        private static bool IsWhitespace(char c) =>
            c is ' ' or '\t' or '\n' or '\r' or '\f';
    }

    /// <summary>
    /// A text run. Text nodes do not match selectors and are laid out inline.
    /// When an element only contains text, the renderer folds the text into the
    /// element's <see cref="IgniteSupportedDomNode.TextContent"/> so no extra box
    /// is created at all.
    /// </summary>
    public sealed class TextNode : Node
    {
        internal TextNode(string text)
        {
            IsTextNode = true;
            TextContent = text;
        }

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
    public sealed class FragmentNode : Node
    {
        internal FragmentNode()
        {
            IsFragment = true;
        }
    }

    /// <summary>
    /// A logical node that owns a component instance. The instance is reused
    /// across renders, which is what keeps hook state alive.
    /// </summary>
    public sealed class ComponentNode : Node
    {
        internal ComponentNode(Type componentType, object? props, string? key)
        {
            ComponentType = componentType;
            Props = props;
            Key = key;
        }

        internal Type ComponentType { get; }

        internal object? Props { get; set; }

        internal Component? Instance { get; set; }

        internal Node? OutputVdom { get; set; }

        internal Node? ParentLive { get; set; }

        internal int OutputIndex { get; set; } = -1;
    }

    /// <summary>
    /// A logical node that publishes a context value to the components below it.
    /// It renders as a transparent fragment.
    /// </summary>
    internal sealed class ProviderNode : Node
    {
        internal required object Context { get; init; }

        internal object? Value { get; set; }

        internal Node[] ProviderChildren { get; set; } = [];

        internal FragmentNode? OutputVdom { get; set; }

        internal Node? ParentLive { get; set; }

        internal int OutputIndex { get; set; } = -1;
    }

    public static class NodeExtensions
    {
        /// <summary>Assigns a stable key and returns the same node for chaining.</summary>
        public static T Key<T>(this T node, string key) where T : Node
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentException.ThrowIfNullOrEmpty(key);
            node.Key = key;
            return node;
        }
    }
}
