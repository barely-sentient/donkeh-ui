namespace Ignite.UILab
{
    /// <summary>
    /// The terse builder API. <c>H</c> creates elements, <c>Text</c> creates text
    /// and <c>C</c> creates components.
    /// </summary>
    /// <remarks>
    /// Attribute names and values are interned when the element is built, so the
    /// diff compares ints even though the caller writes strings.
    /// </remarks>
    public static class _
    {
        /// <summary>
        /// Creates an element with no children.
        /// </summary>
        public static ElementNode H(string tagName)
        {
            return new ElementNode(tagName);
        }

        /// <summary>
        /// Creates an element without attributes.
        /// </summary>
        public static ElementNode H(string tagName, object? first = null, params Node?[] rest)
        {
            var element = new ElementNode(tagName);

            if (first is Props props)
            {
                element.ApplyProps(props);
                element.VdomChildren = NormalizeChildren(rest);
            }
            else if (first is string text)
            {
                var children = new Node?[rest.Length + 1];
                children[0] = new TextNode(text);
                Array.Copy(rest, 0, children, 1, rest.Length);
                element.VdomChildren = NormalizeChildren(children);
            }
            else if (first is Node node)
            {
                var children = new Node?[rest.Length + 1];
                children[0] = node;
                Array.Copy(rest, 0, children, 1, rest.Length);
                element.VdomChildren = NormalizeChildren(children);
            }
            else if (first is IEnumerable<Node?> nodes)
            {
                var children = nodes.Concat(rest).ToArray();
                element.VdomChildren = NormalizeChildren(children);
            }
            else if (first is null)
            {
                element.VdomChildren = NormalizeChildren(rest);
            }
            else
            {
                throw new ArgumentException(
                    "The second argument must be a Props, a child node, a string, " +
                    "or a sequence of children.",
                    nameof(first));
            }

            return element;
        }

        /// <summary>
        /// Creates an element. The second argument may be a <see cref="Props"/>,
        /// a child node, a string, a sequence of children, or null.
        /// </summary>

        /// <summary>
        /// Creates a text run. Null renders nothing.
        /// </summary>
        public static Node? Text(object? value) =>
            value is null ? null : new TextNode(value.ToString() ?? string.Empty);

        /// <summary>
        /// Creates a text run.
        /// </summary>
        public static TextNode Text(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return new TextNode(text);
        }

        /// <summary>
        /// Groups children without adding an element. Fragments are transparent to
        /// selectors and layout.
        /// </summary>
        public static FragmentNode F(params Node?[] children)
        {
            var fragment = new FragmentNode();
            fragment.VdomChildren = NormalizeChildren(children);
            return fragment;
        }

        /// <summary>
        /// Creates a component node. The renderer instantiates
        /// <typeparamref name="TComponent"/> on mount and reuses that instance
        /// across renders, so hook state survives.
        /// </summary>
        public static ComponentNode C<TComponent>(object? props = null)
            where TComponent : Component, new()
        {
            return new ComponentNode(typeof(TComponent), props, null);
        }

        /// <summary>
        /// Creates a typed component node.
        /// </summary>
        public static ComponentNode C<TComponent, TProps>(TProps props)
            where TComponent : Component<TProps>, new()
        {
            return new ComponentNode(typeof(TComponent), props, null);
        }

        /// <summary>
        /// Publishes <paramref name="value"/> to every component below it.
        /// </summary>
        public static Node Provider<T>(Context<T> context, T value, params Node?[] children)
        {
            ArgumentNullException.ThrowIfNull(context);

            return new ProviderNode
            {
                Context = context,
                Value = value,
                ProviderChildren = NormalizeChildren(children)
            };
        }

        /// <summary>
        /// Renders <paramref name="node"/> only when the condition is true.
        /// </summary>
        public static Node? Show(bool condition, Node? node) => condition ? node : null;

        /// <summary>
        /// Projects a sequence into a fragment. The selector runs for each item.
        /// </summary>
        public static FragmentNode Each<T>(IEnumerable<T> items, Func<T, Node?> selector)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(selector);

            var children = new List<Node>();

            foreach (var item in items)
            {
                var node = selector(item);

                if (node is not null)
                {
                    children.Add(node);
                }
            }

            var fragment = new FragmentNode();
            fragment.VdomChildren = children.ToArray();
            return fragment;
        }

        /// <summary>
        /// Assigns a stable key to a node.
        /// </summary>
        public static T Key<T>(T node, string key) where T : Node
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentException.ThrowIfNullOrEmpty(key);
            node.Key = key;
            return node;
        }

        internal static Node[] NormalizeChildren(Node? child, Node?[] children)
        {
            if (children.Length == 0)
            {
                return child is null ? [] : [child];
            }

            var count = child is null ? 0 : 1;

            for (var i = 0; i < children.Length; i++)
            {
                if (children[i] is not null)
                {
                    count++;
                }
            }

            if (count == children.Length + 1)
            {
                var all = new Node[children.Length + 1];
                all[0] = child!;
                Array.Copy(children!, 0, all, 1, children.Length);
                return all;
            }

            var result = new Node[count];
            var index = 0;

            if (child is not null)
            {
                result[index++] = child;
            }

            for (var i = 0; i < children.Length; i++)
            {
                if (children[i] is { } value)
                {
                    result[index++] = value;
                }
            }

            return result;
        }

        internal static Node[] NormalizeChildren(Node?[] children)
        {
            var count = 0;

            for (var i = 0; i < children.Length; i++)
            {
                if (children[i] is not null)
                {
                    count++;
                }
            }

            if (count == children.Length)
            {
                return children!;
            }

            var result = new Node[count];
            var index = 0;

            for (var i = 0; i < children.Length; i++)
            {
                if (children[i] is { } child)
                {
                    result[index++] = child;
                }
            }

            return result;
        }
    }
}
