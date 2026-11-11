namespace Donkeh.UILab
{
    /// <summary>
    /// The terse builder API. <c>H</c> creates elements, <c>Text</c> creates text
    /// and <c>C</c> creates components.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Import the class statically to write markup as nested calls:
    /// <c>using static Donkeh.UILab._;</c>. Attribute names and values are
    /// interned when the element is built, so the diff compares ints even though
    /// the caller writes strings.
    /// </para>
    /// <para>
    /// The builders are stateless and safe to call from multiple threads. The
    /// nodes they return are not: a node belongs to the tree that is being built
    /// and must not be mutated while a <see cref="Renderer"/> is walking it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using static Donkeh.UILab._;
    ///
    /// var tree = H("div", new Props { ClassName = "card" },
    ///     H("h1", "Title"),
    ///     Show(isReady, Text("Loaded")));
    /// </code>
    /// </example>
    /// <seealso cref="Node"/>
    /// <seealso cref="Props"/>
    /// <seealso cref="Renderer"/>
    public static class _
    {
        /// <summary>
        /// Creates an element with no children.
        /// </summary>
        /// <param name="tagName">The tag name, for example <c>div</c> or <c>button</c>.</param>
        /// <returns>A new, empty <see cref="ElementNode"/>.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="tagName"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// The tag name is interned through <see cref="NameCache"/>, so repeating a
        /// tag name does not allocate a new string for the node's identity.
        /// </remarks>
        public static ElementNode H(string tagName)
        {
            return new ElementNode(tagName);
        }

        /// <summary>
        /// Creates an element with optional props and children.
        /// </summary>
        /// <param name="tagName">The tag name, for example <c>div</c> or <c>button</c>.</param>
        /// <param name="first">
        /// The optional first argument. It may be a <see cref="Props"/> instance to
        /// apply, a <see cref="Node"/> child, a <see cref="string"/> that becomes a
        /// text child, a sequence of nodes to prepend, or <see langword="null"/>.
        /// </param>
        /// <param name="rest">
        /// Additional children. <see langword="null"/> entries are skipped.
        /// </param>
        /// <returns>
        /// A new <see cref="ElementNode"/> containing the supplied children.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="tagName"/> is null or empty, or when
        /// <paramref name="first"/> is not one of the supported kinds.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The overload exists so markup reads naturally: props come first, then
        /// children. When <paramref name="first"/> is a <see cref="Props"/>, its
        /// values (including <see cref="Props.Key"/>) are applied to the element
        /// and only <paramref name="rest"/> becomes children.
        /// </para>
        /// <para>
        /// Strings are wrapped in a <see cref="TextNode"/>. A sequence is
        /// concatenated with <paramref name="rest"/> before normalisation, so it is
        /// enumerated exactly once. Null children are removed by
        /// <see cref="NormalizeChildren(Node?[])"/>.
        /// </para>
        /// </remarks>
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
        /// Creates a text run. Null renders nothing.
        /// </summary>
        /// <param name="value">
        /// The value to display. It is converted with <see cref="object.ToString"/>.
        /// </param>
        /// <returns>
        /// A <see cref="TextNode"/> for a non-null value; otherwise
        /// <see langword="null"/> so the child is dropped from the tree.
        /// </returns>
        /// <remarks>
        /// Returning null makes the call composable with <see cref="Show"/> and
        /// <see cref="F"/> without an explicit conditional. Formatting uses the
        /// current culture, as a normal <c>ToString()</c> call would.
        /// </remarks>
        public static Node? Text(object? value) =>
            value is null ? null : new TextNode(value.ToString() ?? string.Empty);

        /// <summary>
        /// Creates a text run.
        /// </summary>
        /// <param name="text">The text to display.</param>
        /// <returns>A new <see cref="TextNode"/> containing <paramref name="text"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="text"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// Unlike <see cref="Text(object?)"/>, this overload never returns null and
        /// does not format the value, so it avoids the virtual call and the
        /// nullable return type when the text is already a non-null string.
        /// </remarks>
        public static TextNode Text(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return new TextNode(text);
        }

        /// <summary>
        /// Groups children without adding an element. Fragments are transparent to
        /// selectors and layout.
        /// </summary>
        /// <param name="children">
        /// The children to group. <see langword="null"/> entries are skipped.
        /// </param>
        /// <returns>A new <see cref="FragmentNode"/> holding the non-null children.</returns>
        /// <remarks>
        /// <para>
        /// A fragment generates no box of its own, so its children participate in
        /// the parent's layout as if they had been written inline. This is the
        /// usual way to return several siblings from a component's
        /// <see cref="Component.Render"/> method.
        /// </para>
        /// <para>
        /// When no child is null the input array is stored directly; otherwise a
        /// compacted copy is allocated by
        /// <see cref="NormalizeChildren(Node?[])"/>.
        /// </para>
        /// </remarks>
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
        /// <typeparam name="TComponent">
        /// The component type. It must be concrete and expose a public
        /// parameterless constructor.
        /// </typeparam>
        /// <param name="props">
        /// The props object, or <see langword="null"/>. It is handed to
        /// <see cref="Component.SetProps"/> when the component mounts and whenever
        /// the parent re-renders.
        /// </param>
        /// <returns>A new <see cref="ComponentNode"/> with no key.</returns>
        /// <remarks>
        /// The node holds only the component <see cref="Type"/> and the props; the
        /// instance is created by <see cref="Renderer.Mount"/>. Because the instance
        /// is retained, call <see cref="Key{T}(T, string)"/> (or
        /// <see cref="Node.Key"/>) when components of the same type are reordered,
        /// otherwise they are matched by position.
        /// </remarks>
        /// <seealso cref="C{TComponent,TProps}(TProps)"/>
        public static ComponentNode C<TComponent>(object? props = null)
            where TComponent : Component, new()
        {
            return new ComponentNode(typeof(TComponent), props, null);
        }

        /// <summary>
        /// Creates a typed component node.
        /// </summary>
        /// <typeparam name="TComponent">
        /// The component type. It must derive from
        /// <see cref="Component{TProps}"/> and expose a public parameterless
        /// constructor.
        /// </typeparam>
        /// <typeparam name="TProps">The type of the props object.</typeparam>
        /// <param name="props">The props assigned to the component instance.</param>
        /// <returns>A new <see cref="ComponentNode"/> with no key.</returns>
        /// <remarks>
        /// The generic constraint makes the props type-safe at the call site. A null
        /// <paramref name="props"/> value would reach
        /// <see cref="Component{TProps}.SetProps"/> as null and reset
        /// <see cref="Component{TProps}.Props"/> to its default value, so pass a real
        /// instance unless that is intended.
        /// </remarks>
        /// <seealso cref="C{TComponent}(object?)"/>
        public static ComponentNode C<TComponent, TProps>(TProps props)
            where TComponent : Component<TProps>, new()
        {
            return new ComponentNode(typeof(TComponent), props, null);
        }

        /// <summary>
        /// Publishes <paramref name="value"/> to every component below it.
        /// </summary>
        /// <typeparam name="T">The type of the published value.</typeparam>
        /// <param name="context">
        /// The context to publish. Consumers read it with
        /// <see cref="Component.UseContext{T}"/>.
        /// </param>
        /// <param name="value">The value visible to consumers in the subtree.</param>
        /// <param name="children">
        /// The subtree. <see langword="null"/> entries are skipped.
        /// </param>
        /// <returns>A node that renders its children transparently.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="context"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The provider is a logical node: it creates no box and no selector match,
        /// and its children are spliced into the parent during reconciliation. A
        /// nested provider for the same context shadows the outer one, and a
        /// consumer that finds no provider falls back to
        /// <see cref="Context{T}.DefaultValue"/>.
        /// </para>
        /// <para>
        /// The value is captured per render in an immutable context scope, so a
        /// component that re-renders later still sees the value it was rendered
        /// under. A changed value reaches descendants in the next render, when the
        /// provider builds a new scope around them.
        /// </para>
        /// </remarks>
        /// <seealso cref="Context{T}"/>
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
        /// <param name="condition">When true, <paramref name="node"/> is returned.</param>
        /// <param name="node">The optional node to include.</param>
        /// <returns>
        /// <paramref name="node"/> when <paramref name="condition"/> is true;
        /// otherwise <see langword="null"/>.
        /// </returns>
        /// <remarks>
        /// A null child is discarded by the element and fragment builders, so the
        /// result can be passed directly in a child list. The node is not cloned or
        /// inspected, making this a zero-allocation conditional.
        /// </remarks>
        /// <seealso cref="Each{T}(IEnumerable{T}, Func{T, Node?})"/>
        public static Node? Show(bool condition, Node? node) => condition ? node : null;

        /// <summary>
        /// Projects a sequence into a fragment. The selector runs for each item.
        /// </summary>
        /// <typeparam name="T">The element type of the source sequence.</typeparam>
        /// <param name="items">The items to project. The sequence is enumerated once.</param>
        /// <param name="selector">
        /// Produces a node for an item, or <see langword="null"/> to skip that item.
        /// </param>
        /// <returns>
        /// A <see cref="FragmentNode"/> containing the selector's non-null results in
        /// source order. The fragment may be empty.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="items"/> or <paramref name="selector"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The projection runs eagerly while the tree is built, not during
        /// reconciliation, and the results are collected into a list before the
        /// fragment is created. Null results are dropped so <see cref="Show"/> can be
        /// used inside the selector.
        /// </para>
        /// <para>
        /// Keys are not assigned automatically. For lists that are reordered,
        /// inserted into or removed from, wrap each result in
        /// <see cref="Key{T}(T, string)"/> so the reconciler can move nodes instead of
        /// rebuilding them.
        /// </para>
        /// </remarks>
        /// <seealso cref="F(Node?[])"/>
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
        /// <typeparam name="T">The concrete node type, returned unchanged.</typeparam>
        /// <param name="node">The node to key. It is mutated in place.</param>
        /// <param name="key">The identity, unique among the node's siblings.</param>
        /// <returns>The same <paramref name="node"/> instance, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="node"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="key"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Keys are compared with ordinal string equality inside a single sibling
        /// list; the same key may be reused under different parents. The reconciler
        /// uses the key to find the previous node of the same kind, which preserves
        /// component state and layout across moves. Unkeyed children are matched by
        /// position instead.
        /// </para>
        /// <para>
        /// For elements built through <see cref="Props"/>, setting
        /// <see cref="Props.Key"/> has the same effect. The extension method
        /// <see cref="NodeExtensions.Key{T}"/> is a fluent equivalent.
        /// </para>
        /// </remarks>
        /// <seealso cref="Node.Key"/>
        public static T Key<T>(T node, string key) where T : Node
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentException.ThrowIfNullOrEmpty(key);
            node.Key = key;
            return node;
        }

        /// <summary>
        /// Combines an optional first child with an array of children, dropping
        /// <see langword="null"/> entries.
        /// </summary>
        /// <param name="child">The optional first child.</param>
        /// <param name="children">The remaining children.</param>
        /// <returns>
        /// A compact array containing <paramref name="child"/> (when non-null)
        /// followed by the non-null elements of <paramref name="children"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This is the shared normalisation used by <see cref="H(string, object?, Node?[])"/>
        /// and <see cref="Provider{T}"/>. Dropping nulls is what lets
        /// <see cref="Show"/> and conditionals appear anywhere in a child list.
        /// </para>
        /// <para>
        /// The fast path is taken only when the first child is present and every
        /// remaining child is non-null; then a single copy is made and no null check
        /// is repeated. Otherwise the result is allocated at the exact final size.
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// Drops <see langword="null"/> entries from a child array.
        /// </summary>
        /// <param name="children">The children to normalise.</param>
        /// <returns>
        /// <paramref name="children"/> itself when no entry is null; otherwise a
        /// compacted copy preserving order.
        /// </returns>
        /// <remarks>
        /// The no-null path deliberately returns the caller's array instead of
        /// copying it, because markup builders pass freshly created arrays. The
        /// returned array becomes owned by the node and must not be mutated
        /// afterwards.
        /// </remarks>
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
