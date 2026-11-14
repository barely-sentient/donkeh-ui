using Donkeh.Parser;

namespace Donkeh.Data.Dom
{
    /// <summary>
    /// A minimal dom node that any framework can populate so the engine can match it
    /// against compiled selectors.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every value is kept as an interned id, which turns matching a selector token
    /// into a single int comparison. The text is interned exactly as the tokenizer
    /// keeps it: an id keeps its leading '#', because hash tokens are emitted from
    /// the '#', while a class name stays plain, because the '.' is a separate
    /// delimiter token. Attribute names and values are interned as-is (surrounding
    /// quotes are stripped), and the <c>id</c> and <c>class</c> attributes are
    /// routed to the dedicated fields instead of the attribute map.
    /// </para>
    /// <para>
    /// The node is a mutable tree. Assigning <see cref="Children"/> rewrites each
    /// child's parent pointer and sibling indices and bumps the child's
    /// <see cref="Revision"/> when it moved, so structural selectors and style
    /// caches see the change. Mutating the array returned by
    /// <see cref="Children"/> in place is not tracked; assign a new array instead.
    /// </para>
    /// <para>
    /// Nodes are not thread-safe and are expected to be created and mutated on the
    /// UI thread. The intern table they use is shared process-wide and grows
    /// monotonically.
    /// </para>
    /// </remarks>
    public class IgniteSupportedDomNode
    {
        private static readonly int IdAttribute = InternTable.Intern("id");
        private static readonly int ClassAttribute = InternTable.Intern("class");

        private int _tagName = -1;
        private int _id = -1;
        private int[] _className = [];
        private int _revision;
        private string? _textContent;

        private readonly Dictionary<int, int> _attributes = [];

        private bool _isHovered;
        private bool _isFocused;
        private bool _isFocusVisible;
        private bool _isActive;
        private bool _isChecked;

        private IgniteSupportedDomNode? _parent;
        private IgniteSupportedDomNode[] _children = [];
        private int _indexInParent = -1;
        private int _elementIndexInParent = -1;
        private int _elementChildCount;
        private int _suppressedChildCount;
        private bool _isSuppressed;

        /// <summary>
        /// True when this node is a framework text node. Text nodes do not match
        /// selectors and are laid out as inline content.
        /// </summary>
        public bool IsTextNode { get; set; }

        /// <summary>
        /// True when this node is a transparent fragment. Fragments do not match
        /// selectors and generate no box of their own.
        /// </summary>
        public bool IsFragment { get; set; }

        /// <summary>
        /// True when the node generated no box in the current frame because its
        /// computed <c>display</c> is <c>none</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The host tree often keeps declarative scaffolding that a browser would
        /// not mount at all, such as a closed dropdown menu that is styled
        /// <c>display: none</c> but still sits in the parent's child list. A
        /// browser built from the mounted markup would not have that sibling, so
        /// structural selectors (<c>:last-child</c>, <c>:nth-child</c>, the sibling
        /// combinators) are evaluated over the rendered children instead.
        /// </para>
        /// <para>
        /// The layout engine sets this flag after resolving styles and before it
        /// builds boxes, so matching and layout see the same sibling set. It only
        /// applies to elements; text nodes and fragments are already excluded.
        /// </para>
        /// </remarks>
        public bool IsSuppressed
        {
            get => _isSuppressed;
            set
            {
                if (_isSuppressed == value)
                {
                    return;
                }

                _isSuppressed = value;

                if (_parent is not null)
                {
                    _parent._suppressedChildCount += value ? 1 : -1;
                    _parent._revision++;
                }
            }
        }

        /// <summary>
        /// Optional text carried by the element. The engine has no text nodes,
        /// so the host puts the element's text here for layout and painting.
        /// </summary>
        public string? TextContent
        {
            get => _textContent;
            set
            {
                if (!string.Equals(_textContent, value, StringComparison.Ordinal))
                {
                    _textContent = value;
                    _revision++;
                }
            }
        }

        /// <summary>
        /// Incremented whenever the element's identity, classes, attributes or
        /// text change. Layout caches use it to reject stale subtrees cheaply.
        /// </summary>
        public int Revision => _revision;

        /// <summary>Marks the node as changed when the host mutates state the setters cannot see.</summary>
        public void Invalidate() => _revision++;

        /// <summary>
        /// True while the pointer is over this element or one of its descendants.
        /// The host normally applies hover to the whole ancestor chain so
        /// <c>:hover</c> matches the same way it does in a browser.
        /// </summary>
        public bool IsHovered
        {
            get => _isHovered;
            set => SetState(ref _isHovered, value);
        }

        /// <summary>True while this element owns keyboard focus.</summary>
        public bool IsFocused
        {
            get => _isFocused;
            set => SetState(ref _isFocused, value);
        }

        /// <summary>
        /// True when focus should be visibly indicated. Keyboard focus sets this
        /// on all elements; pointer focus sets it for text fields.
        /// </summary>
        public bool IsFocusVisible
        {
            get => _isFocusVisible;
            set => SetState(ref _isFocusVisible, value);
        }

        /// <summary>True while the pointer button is held on this element or an ancestor.</summary>
        public bool IsActive
        {
            get => _isActive;
            set => SetState(ref _isActive, value);
        }

        /// <summary>
        /// Host-maintained checked state for checkboxes, radios and selectable
        /// options. Attribute-based <c>checked</c> is also honoured by the matcher.
        /// </summary>
        public bool IsChecked
        {
            get => _isChecked;
            set => SetState(ref _isChecked, value);
        }

        /// <summary>Clears every interaction state without touching attributes or text.</summary>
        public void ClearInteractionState()
        {
            IsHovered = false;
            IsFocused = false;
            IsFocusVisible = false;
            IsActive = false;
            IsChecked = false;
        }

        private void SetState(ref bool field, bool value)
        {
            if (field == value)
            {
                return;
            }

            field = value;
            _revision++;
        }

        /// <summary>
        /// Horizontal scroll position in CSS pixels for this element when it is
        /// a scroll container. LayoutLab clamps it to the scrollable range.
        /// </summary>
        public float ScrollOffsetX { get; set; }

        /// <summary>
        /// Vertical scroll position in CSS pixels for this element when it is a
        /// scroll container. LayoutLab clamps it to the scrollable range.
        /// </summary>
        public float ScrollOffsetY { get; set; }

        /// <summary>
        /// The parent element, or null for the root. Assigning
        /// <see cref="Children"/> sets this automatically.
        /// </summary>
        public IgniteSupportedDomNode? Parent
        {
            get => _parent;
            set
            {
                if (ReferenceEquals(_parent, value))
                {
                    return;
                }

                _parent = value;
                _indexInParent = -1;
            }
        }

        /// <summary>
        /// The element children. Assigning the array also sets each child's
        /// <see cref="Parent"/> and sibling index, which the child and sibling
        /// combinators rely on. Mutating the array in place afterwards is not
        /// tracked; assign a new array instead.
        /// </summary>
        public IgniteSupportedDomNode[] Children
        {
            get => _children;
            set
            {
                _children = value ?? [];
                _revision++;

                var elementIndex = 0;

                for (var i = 0; i < _children.Length; i++)
                {
                    if (_children[i] is { } child)
                    {
                        var previousParent = child._parent;
                        var previousIndex = child._indexInParent;
                        var previousElementIndex = child._elementIndexInParent;

                        child._parent = this;
                        child._indexInParent = i;

                        // Text nodes and fragments are not elements, so they do
                        // not participate in :nth-child or sibling combinators.
                        if (child.IsTextNode || child.IsFragment)
                        {
                            child._elementIndexInParent = -1;
                        }
                        else
                        {
                            child._elementIndexInParent = elementIndex++;
                        }

                        // A moved or reparented child can match different
                        // structural and sibling selectors, so its cached style
                        // has to be rejected.
                        if (!ReferenceEquals(previousParent, this)
                            || previousIndex != child._indexInParent
                            || previousElementIndex != child._elementIndexInParent)
                        {
                            child._revision++;
                        }
                    }
                }

                _elementChildCount = elementIndex;
                _suppressedChildCount = 0;

                for (var i = 0; i < _children.Length; i++)
                {
                    if (_children[i] is { IsSuppressed: true, IsTextNode: false, IsFragment: false })
                    {
                        _suppressedChildCount++;
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the interned tag name, for example the id of <c>"div"</c>.
        /// Compare this directly with a type selector token.
        /// </summary>
        /// <value>
        /// An interned id, or -1 when the node has no tag name. The setter bumps
        /// <see cref="Revision"/> only when the id actually changes.
        /// </value>
        public int InternedTagName
        {
            get => _tagName;
            set
            {
                if (_tagName != value)
                {
                    _tagName = value;
                    _revision++;
                }
            }
        }

        /// <summary>
        /// Gets or sets the tag name as text. Assigning <see langword="null"/> clears
        /// the tag name and stores -1.
        /// </summary>
        /// <value>The interned text, or <see langword="null"/> when no tag is set.</value>
        public string? TagName
        {
            get => InternTable.TryGetString(_tagName, out var tagName) ? tagName : null;
            set
            {
                var id = value is null ? -1 : InternTable.Intern(value);

                if (_tagName != id)
                {
                    _tagName = id;
                    _revision++;
                }
            }
        }

        /// <summary>
        /// The interned id selector text, including the leading '#'. Compare this
        /// directly with a hash selector token.
        /// </summary>
        public int InternedId
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    _revision++;
                }
            }
        }

        /// <summary>
        /// Gets or sets the id as text, for example <c>"main"</c> or <c>"#main"</c>.
        /// A leading '#' is accepted and added when missing, because hash selector
        /// tokens include it.
        /// </summary>
        /// <value>
        /// The interned text including the leading '#', or <see langword="null"/>
        /// when the id is empty.
        /// </value>
        /// <remarks>
        /// Reading resolves the interned id back to text, so prefer
        /// <see cref="InternedId"/> on match paths.
        /// </remarks>
        public string? Id
        {
            get => InternTable.TryGetString(_id, out var id) ? id : null;
            set
            {
                var id = string.IsNullOrEmpty(value)
                    ? -1
                    : value[0] == '#'
                        ? InternTable.Intern(value)
                        : InternTable.Intern('#', value);

                if (_id != id)
                {
                    _id = id;
                    _revision++;
                }
            }
        }

        /// <summary>
        /// The interned class names, without a leading '.'. Compare these directly
        /// with identifier selector tokens.
        /// </summary>
        public ReadOnlySpan<int> InternedClassName => _className;

        /// <summary>
        /// Every class name on the node. Replacing this replaces the whole class
        /// list; class names may be given with or without a leading '.'.
        /// </summary>
        public string[] ClassName
        {
            get
            {
                var names = new string[_className.Length];

                for (var i = 0; i < names.Length; i++)
                {
                    names[i] = InternTable.GetString(_className[i]) ?? string.Empty;
                }

                return names;
            }
            set
            {
                _className = [];

                if (value is null)
                {
                    _revision++;
                    return;
                }

                foreach (var name in value)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        AddClass(name);
                    }
                }

                _revision++;
            }
        }

        /// <summary>
        /// Replaces the whole class list from already interned ids. This is the
        /// fast path for frameworks that cache class ids between renders.
        /// </summary>
        /// <param name="classIds">The interned class ids, without any delimiter.</param>
        /// <remarks>
        /// The span is copied, so the caller may reuse its buffer immediately.
        /// </remarks>
        public void SetInternedClassName(ReadOnlySpan<int> classIds)
        {
            _className = classIds.ToArray();
            _revision++;
        }

        /// <summary>
        /// Adds a class to the node. The class name may be given with or without the
        /// leading '.'. Adding a class twice is a no-op.
        /// </summary>
        /// <param name="className">
        /// The class name to add, with or without a leading '.'.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="className"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// The name is interned, so adding a class grows the intern table on first
        /// use. <see cref="Revision"/> is bumped only when the class was not already
        /// present.
        /// </remarks>
        public void AddClass(string className)
        {
            ArgumentException.ThrowIfNullOrEmpty(className);

            var id = className[0] == '.'
                ? InternTable.Intern(className.AsSpan(1))
                : InternTable.Intern(className);

            AddClass(id);
        }

        /// <summary>
        /// Removes a class from the node and returns whether it was present.
        /// </summary>
        /// <param name="className">
        /// The class to remove, with or without a leading '.'.
        /// </param>
        /// <returns><see langword="true"/> when the class existed and was removed.</returns>
        /// <remarks>
        /// The name is not interned, so a miss cannot grow the intern table.
        /// </remarks>
        public bool RemoveClass(string className)
        {
            if (string.IsNullOrEmpty(className) || !TryGetInternedClass(className, out var id))
            {
                return false;
            }

            var index = IndexOfClass(id);
            if (index < 0)
            {
                return false;
            }

            Array.Copy(_className, index + 1, _className, index, _className.Length - index - 1);
            Array.Resize(ref _className, _className.Length - 1);
            _revision++;

            return true;
        }

        /// <summary>
        /// True when the node carries <paramref name="className"/>. This never
        /// interns the name, so a miss cannot grow the intern table.
        /// </summary>
        /// <param name="className">
        /// The class to test, with or without a leading '.'.
        /// </param>
        /// <returns><see langword="true"/> when the class is present.</returns>
        public bool HasClass(string className)
        {
            return !string.IsNullOrEmpty(className)
                && TryGetInternedClass(className, out var id)
                && IndexOfClass(id) >= 0;
        }

        /// <summary>
        /// Number of attributes stored besides <c>id</c> and <c>class</c>.
        /// </summary>
        public int AttributeCount => _attributes.Count;

        /// <summary>
        /// Sets an attribute from already interned ids. The fastest path, and the
        /// one to use when a framework caches ids.
        /// </summary>
        /// <param name="nameId">The interned attribute name.</param>
        /// <param name="valueId">The interned attribute value.</param>
        /// <remarks>
        /// No validation is performed, so an id that was never interned produces a
        /// value that the matcher cannot see. Unlike the string overload this does
        /// not special-case <c>id</c> or <c>class</c>.
        /// </remarks>
        public void SetAttribute(int nameId, int valueId)
        {
            _attributes[nameId] = valueId;
            _revision++;
        }

        /// <summary>
        /// Sets an attribute by name. The <c>id</c> and <c>class</c> attributes are
        /// routed to <see cref="Id"/> and <see cref="ClassName"/>.
        /// </summary>
        /// <param name="name">The attribute name; must not be empty.</param>
        /// <param name="value">The attribute value.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="name"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="value"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// A value wrapped in matching single or double quotes is stored unquoted,
        /// so it interns to the same id as a normalised selector value.
        /// </remarks>
        public void SetAttribute(string name, string value)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(value);

            var nameId = InternTable.Intern(name);

            if (nameId == IdAttribute)
            {
                Id = value;
                return;
            }

            if (nameId == ClassAttribute)
            {
                SetClassFromAttribute(value);
                return;
            }

            _attributes[nameId] = InternAttributeValue(value);
            _revision++;
        }

        /// <summary>
        /// Gets or sets a specific attribute by interned name.
        /// </summary>
        /// <param name="nameId">The interned attribute name.</param>
        /// <param name="valueId">
        /// When this method returns <see langword="true"/>, receives the interned
        /// value; otherwise -1.
        /// </param>
        /// <returns><see langword="true"/> when the attribute is present.</returns>
        /// <remarks>
        /// The <see cref="SetAttribute(string,string)"/> overload routes <c>id</c>
        /// and <c>class</c> to <see cref="Id"/> and <see cref="ClassName"/> rather
        /// than the attribute map; the interned overload stores whatever ids it is
        /// given.
        /// </remarks>
        public bool TryGetAttribute(int nameId, out int valueId)
        {
            return _attributes.TryGetValue(nameId, out valueId);
        }

        /// <summary>
        /// Reads an attribute by name. Returns false when the node does not carry it
        /// or when the name was never interned.
        /// </summary>
        /// <param name="name">The attribute name to look up.</param>
        /// <param name="value">
        /// Receives the attribute text on success; otherwise <see langword="null"/>.
        /// </param>
        /// <returns><see langword="true"/> when the attribute is present.</returns>
        public bool TryGetAttribute(string name, out string? value)
        {
            if (!string.IsNullOrEmpty(name)
                && InternTable.TryGetId(name, out var nameId)
                && _attributes.TryGetValue(nameId, out var valueId))
            {
                value = InternTable.GetString(valueId);
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Removes an attribute by interned name and returns whether it was present.
        /// </summary>
        /// <param name="nameId">The interned attribute name.</param>
        /// <returns><see langword="true"/> when the attribute existed and was removed.</returns>
        /// <remarks>
        /// Removing an attribute bumps <see cref="Revision"/>, because attribute
        /// selectors may stop matching.
        /// </remarks>
        public bool RemoveAttribute(int nameId)
        {
            if (_attributes.Remove(nameId))
            {
                _revision++;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Removes an attribute by name and returns whether it was present.
        /// </summary>
        /// <param name="name">The attribute name to remove.</param>
        /// <returns><see langword="true"/> when the attribute existed and was removed.</returns>
        public bool RemoveAttribute(string name)
        {
            if (!string.IsNullOrEmpty(name)
                && InternTable.TryGetId(name, out var nameId)
                && _attributes.Remove(nameId))
            {
                _revision++;
                return true;
            }

            return false;
        }

        private void AddClass(int id)
        {
            if (IndexOfClass(id) >= 0)
            {
                return;
            }

            var length = _className.Length;
            Array.Resize(ref _className, length + 1);
            _className[length] = id;
            _revision++;
        }

        private int IndexOfClass(int id)
        {
            return Array.IndexOf(_className, id);
        }

        /// <summary>
        /// Class lookup by interned id, used by the matcher to avoid resolving
        /// strings.
        /// </summary>
        internal bool HasInternedClass(int id)
        {
            return IndexOfClass(id) >= 0;
        }

        /// <summary>
        /// The interned attribute names on the node, used by the matcher to reach
        /// selectors whose subject is an attribute selector.
        /// </summary>
        internal Dictionary<int, int>.KeyCollection InternedAttributeNames => _attributes.Keys;

        /// <summary>
        /// Position among the siblings, or 0 for the root. Validated against the
        /// parent's array so a hand-edited tree still resolves correctly.
        /// </summary>
        /// <remarks>
        /// Siblings with <see cref="IsSuppressed"/> set generate no box and are
        /// skipped, so structural selectors see the same children the layout does.
        /// </remarks>
        internal int SiblingIndex
        {
            get
            {
                if (_parent is null)
                {
                    return 0;
                }

                var siblings = _parent._children;

                if (_parent._suppressedChildCount == 0
                    && (uint)_indexInParent < (uint)siblings.Length
                    && ReferenceEquals(siblings[_indexInParent], this))
                {
                    return _elementIndexInParent >= 0 ? _elementIndexInParent : 0;
                }

                if (_parent._suppressedChildCount > 0)
                {
                    var suppressedIndex = 0;

                    for (var i = 0; i < siblings.Length; i++)
                    {
                        var sibling = siblings[i];

                        if (sibling is null || sibling.IsTextNode || sibling.IsFragment)
                        {
                            continue;
                        }

                        if (ReferenceEquals(sibling, this))
                        {
                            return suppressedIndex;
                        }

                        if (!sibling.IsSuppressed)
                        {
                            suppressedIndex++;
                        }
                    }
                }

                return FindElementIndex(siblings, this);
            }
        }

        /// <summary>
        /// Number of elements in the sibling list, or 1 for the root. Text nodes
        /// and fragments are not elements and do not count, nor do suppressed
        /// elements that generated no box.
        /// </summary>
        internal int SiblingCount => _parent is null
            ? 1
            : Math.Max(0, _parent._elementChildCount - _parent._suppressedChildCount);

        /// <summary>
        /// Returns the element at an element-relative sibling index, or this node
        /// when the index is out of range.
        /// </summary>
        /// <param name="index">A zero-based index among element siblings.</param>
        /// <returns>The sibling element, or this node as a fallback.</returns>
        /// <remarks>
        /// Text nodes and fragments are skipped. The fallback keeps callers total
        /// when a hand-edited tree has stale sibling counts; it never returns null.
        /// </remarks>
        internal IgniteSupportedDomNode SiblingAt(int index)
        {
            if (_parent is null)
            {
                return this;
            }

            var siblings = _parent._children;
            var elementIndex = 0;

            for (var i = 0; i < siblings.Length; i++)
            {
                var sibling = siblings[i];

                if (sibling is null || sibling.IsTextNode || sibling.IsFragment || sibling.IsSuppressed)
                {
                    continue;
                }

                if (elementIndex == index)
                {
                    return sibling;
                }

                elementIndex++;
            }

            return this;
        }

        private static int FindElementIndex(IgniteSupportedDomNode[] siblings, IgniteSupportedDomNode node)
        {
            var elementIndex = 0;

            for (var i = 0; i < siblings.Length; i++)
            {
                var sibling = siblings[i];

                if (sibling is null || sibling.IsTextNode || sibling.IsFragment || sibling.IsSuppressed)
                {
                    continue;
                }

                if (ReferenceEquals(sibling, node))
                {
                    return elementIndex;
                }

                elementIndex++;
            }

            return 0;
        }

        /// <summary>
        /// Gets the raw child array, including text nodes and fragments, for the
        /// matcher's descendant walk.
        /// </summary>
        /// <remarks>
        /// The span is a live view over the array; it is invalidated when
        /// <see cref="Children"/> is assigned.
        /// </remarks>
        internal ReadOnlySpan<IgniteSupportedDomNode> ChildNodes => _children;

        private static bool TryGetInternedClass(string className, out int id)
        {
            return className[0] == '.'
                ? InternTable.TryGetId(className.AsSpan(1), out id)
                : InternTable.TryGetId(className, out id);
        }

        /// <summary>
        /// Interns an attribute value. Selector values are normalised to their
        /// unquoted text when the selector is stored, so a quoted value from a
        /// framework has to be unquoted the same way to produce the same id.
        /// </summary>
        private static int InternAttributeValue(string value)
        {
            if (value.Length >= 2)
            {
                var quote = value[0];

                if ((quote == '"' || quote == '\'') && value[^1] == quote)
                {
                    return InternTable.Intern(value.AsSpan(1, value.Length - 2));
                }
            }

            return InternTable.Intern(value);
        }

        /// <summary>
        /// Replaces the class list from a whitespace-separated <c>class</c>
        /// attribute value.
        /// </summary>
        /// <param name="value">
        /// The raw attribute value, for example <c>"btn btn-primary"</c>.
        /// </param>
        /// <remarks>
        /// Runs of CSS whitespace separate names; empty runs are ignored. Each name
        /// is interned, so this method grows the intern table and bumps
        /// <see cref="Revision"/> once per added class.
        /// </remarks>
        private void SetClassFromAttribute(ReadOnlySpan<char> value)
        {
            if (value.IsEmpty)
            {
                if (_className.Length != 0)
                {
                    _className = [];
                    _revision++;
                }

                return;
            }

            // A class list separated by whitespace has at most (length + 1) / 2
            // names, so one exact-size allocation replaces a resize per token.
            var buffer = new int[(value.Length + 1) / 2];
            var count = 0;
            var start = 0;

            for (var i = 0; i <= value.Length; i++)
            {
                if (i < value.Length && !IsWhitespace(value[i]))
                {
                    continue;
                }

                if (i > start)
                {
                    var id = InternTable.Intern(value[start..i]);

                    if (Array.IndexOf(buffer, id, 0, count) < 0)
                    {
                        buffer[count++] = id;
                    }
                }

                start = i + 1;
            }

            if (count == 0)
            {
                if (_className.Length != 0)
                {
                    _className = [];
                    _revision++;
                }

                return;
            }

            Array.Sort(buffer, 0, count);

            if (count != buffer.Length)
            {
                Array.Resize(ref buffer, count);
            }

            _className = buffer;
            _revision++;
        }

        private static bool IsWhitespace(char c)
        {
            return c is ' ' or '\t' or '\n' or '\r' or '\f';
        }
    }
}
