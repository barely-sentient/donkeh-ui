using Ignite.Parser;

namespace Ignite.Data.Dom
{
    /// <summary>
    /// A minimal dom node that any framework can populate so the engine can match it
    /// against compiled selectors.
    /// </summary>
    /// <remarks>
    /// Every value is kept as an interned id, which turns matching a selector token
    /// into a single int comparison. The text is interned exactly as the tokenizer
    /// keeps it: an id keeps its leading '#', because hash tokens are emitted from
    /// the '#', while a class name stays plain, because the '.' is a separate
    /// delimiter token. Attribute names and values are interned as-is (surrounding
    /// quotes are stripped), and the <c>id</c> and <c>class</c> attributes are
    /// routed to the dedicated fields instead of the attribute map.
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

        private IgniteSupportedDomNode? _parent;
        private IgniteSupportedDomNode[] _children = [];
        private int _indexInParent = -1;

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

                for (var i = 0; i < _children.Length; i++)
                {
                    if (_children[i] is { } child)
                    {
                        child._parent = this;
                        child._indexInParent = i;
                    }
                }
            }
        }

        /// <summary>
        /// The interned tag name, for example the id of <c>"div"</c>. Compare this
        /// directly with a type selector token.
        /// </summary>
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
        /// Adds a class to the node. The class name may be given with or without the
        /// leading '.'. Adding a class twice is a no-op.
        /// </summary>
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
        public void SetAttribute(int nameId, int valueId)
        {
            _attributes[nameId] = valueId;
            _revision++;
        }

        /// <summary>
        /// Sets an attribute by name. The <c>id</c> and <c>class</c> attributes are
        /// routed to <see cref="Id"/> and <see cref="ClassName"/>.
        /// </summary>
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

        public bool TryGetAttribute(int nameId, out int valueId)
        {
            return _attributes.TryGetValue(nameId, out valueId);
        }

        /// <summary>
        /// Reads an attribute by name. Returns false when the node does not carry it
        /// or when the name was never interned.
        /// </summary>
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
        internal int SiblingIndex
        {
            get
            {
                if (_parent is null)
                {
                    return 0;
                }

                var siblings = _parent._children;

                return (uint)_indexInParent < (uint)siblings.Length
                    && ReferenceEquals(siblings[_indexInParent], this)
                    ? _indexInParent
                    : Array.IndexOf(siblings, this);
            }
        }

        /// <summary>
        /// Number of elements in the sibling list, or 1 for the root.
        /// </summary>
        internal int SiblingCount => _parent?._children.Length ?? 1;

        internal IgniteSupportedDomNode SiblingAt(int index) =>
            _parent is null ? this : _parent._children[index];

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

        private void SetClassFromAttribute(ReadOnlySpan<char> value)
        {
            _className = [];

            var start = 0;

            for (var i = 0; i <= value.Length; i++)
            {
                if (i < value.Length && !IsWhitespace(value[i]))
                {
                    continue;
                }

                if (i > start)
                {
                    AddClass(InternTable.Intern(value[start..i]));
                }

                start = i + 1;
            }
        }

        private static bool IsWhitespace(char c)
        {
            return c is ' ' or '\t' or '\n' or '\r' or '\f';
        }
    }
}
