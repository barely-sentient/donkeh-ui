using System.Collections.Concurrent;
using System.Text;

namespace Donkeh.UILab
{
    /// <summary>
    /// Mounts a virtual tree, reconciles it in place and schedules re-renders.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The live tree is made of <see cref="ElementNode"/>, <see cref="TextNode"/>
    /// and <see cref="FragmentNode"/> instances. A diff mutates those nodes in
    /// place and only bumps their revision when something actually changed, so
    /// unchanged subtrees keep their layout cache entries.
    /// </para>
    /// <para>
    /// <see cref="ComponentNode"/> and <see cref="ProviderNode"/> are logical
    /// nodes and never appear in the live tree; they exist only so component
    /// instances and context scopes survive across renders. <see cref="Root"/>
    /// therefore returns the tree a host can hand to layout and selector
    /// matching directly.
    /// </para>
    /// <para>
    /// A renderer is bound to the thread that mounted it. It is not thread-safe
    /// and <see cref="Flush"/> must not be called re-entrantly; nested state
    /// updates are queued and applied by the running flush loop instead.
    /// </para>
    /// </remarks>
    /// <seealso cref="Component"/>
    /// <seealso cref="Mount(Node)"/>
    public sealed class Renderer
    {
        /// <summary>
        /// The maximum number of render/commit passes a single <see cref="Flush"/>
        /// may run before it assumes that an effect updates state unconditionally
        /// and gives up.
        /// </summary>
        private const int MaxFlushIterations = 64;

        /// <summary>
        /// Process-wide cache of component constructors, keyed by component type.
        /// The reflected constructor lookup and the type checks run once per type;
        /// later mounts reuse the compiled delegate.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, Func<Component>> Factories =
            new();

        /// <summary>
        /// Components with a pending state or prop update, in enqueue order. Entries
        /// stay until the flush loop dequeues them.
        /// </summary>
        private readonly Queue<Component> _dirty = new();

        /// <summary>
        /// Components rendered during the current pass. Effects run over this list
        /// after the tree has been applied, then it is cleared for the next pass.
        /// </summary>
        private readonly List<Component> _rendered = new();

        /// <summary>
        /// Every live component instance, used by <see cref="FindComponent{T}"/>
        /// and kept in sync as components mount and unmount.
        /// </summary>
        private readonly List<Component> _mounted = new();

        /// <summary>
        /// The logical root handed to the last mount or render. This is the node
        /// whose <see cref="Node.VdomChildren"/> describes the tree.
        /// </summary>
        private Node? _rootVdom;

        /// <summary>The live root node, i.e. the node the host renders.</summary>
        private Node? _rootLive;

        /// <summary>The root component when the mounted tree's root is a component.</summary>
        private ComponentNode? _rootComponent;

        /// <summary>
        /// True while a mount or flush is applying changes and committing effects.
        /// Guards against re-entrant flushes.
        /// </summary>
        private bool _flushing;

        /// <summary>
        /// When true (the default), a state update flushes synchronously. Set it
        /// to false to batch updates and call <see cref="Flush"/> from your frame
        /// loop.
        /// </summary>
        /// <remarks>
        /// This only governs whether <see cref="Enqueue"/> starts a flush. Calling
        /// <see cref="Flush"/> explicitly always applies queued updates, even when
        /// this is false, and updates queued while a flush is already running are
        /// picked up by that flush rather than by a nested one.
        /// </remarks>
        public bool AutoFlush { get; set; } = true;

        /// <summary>The live root node, or null before <see cref="Mount"/>.</summary>
        /// <value>
        /// The most recent live root, or <see langword="null"/> until the renderer
        /// has been mounted. The reference is replaced when a root component's
        /// output changes kind.
        /// </value>
        /// <remarks>
        /// This node is not a <see cref="ComponentNode"/> or
        /// <see cref="ProviderNode"/>; use <see cref="Donkeh.Data.Dom.IgniteSupportedDomNode"/>'s
        /// members to walk the tree the host renders.
        /// </remarks>
        public Node? Root => _rootLive;

        /// <summary>Finds the first mounted component of the requested type.</summary>
        /// <typeparam name="T">The component type to search for.</typeparam>
        /// <returns>
        /// The first matching instance in mount order, or <see langword="null"/>
        /// when no component of that type is mounted.
        /// </returns>
        /// <remarks>
        /// The search is a linear scan over the live component list. It matches
        /// derived types as well as <typeparamref name="T"/> itself, so pass the
        /// most derived type when several components share a base class.
        /// </remarks>
        public T? FindComponent<T>() where T : Component
        {
            for (var i = 0; i < _mounted.Count; i++)
            {
                if (_mounted[i] is T match)
                {
                    return match;
                }
            }

            return null;
        }

        /// <summary>True while a flush is applying changes and running effects.</summary>
        /// <remarks>
        /// State updates observed while this is true are queued for the current
        /// flush loop. Checking this lets a host avoid re-entrant layout work from
        /// inside layout effects.
        /// </remarks>
        public bool IsFlushing => _flushing;

        /// <summary>
        /// Incremented after a mount or a flush pass that processed at least one
        /// component. Hosts can use it to avoid re-laying out an unchanged frame.
        /// </summary>
        /// <value>
        /// A counter that starts at zero and only increases. Comparing it between
        /// frames is the intended use; the absolute value is meaningless.
        /// </value>
        /// <remarks>
        /// <para>
        /// A flush bumps this once per pass in which at least one dirty component was
        /// processed, even if <see cref="Component.ShouldRender"/> then vetoed the
        /// render. A pass that produces an identical tree therefore still bumps it;
        /// treat the value as a wake-up signal rather than a content hash.
        /// </para>
        /// <para>
        /// <see cref="Mount"/> bumps it once after the initial tree has settled.
        /// </para>
        /// </remarks>
        public int Version { get; private set; }

        /// <summary>
        /// Mounts a virtual tree and returns its live root. A renderer can only be
        /// mounted once.
        /// </summary>
        /// <param name="vdom">
        /// The logical root to mount. It may be an element, a text node, a fragment,
        /// a component node or a context provider.
        /// </param>
        /// <returns>
        /// The live root node. This is the same object as <see cref="Root"/>; it is
        /// never a <see cref="ComponentNode"/> or <see cref="ProviderNode"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="vdom"/> is <see langword="null"/>. Return an empty
        /// fragment instead of null when a render has nothing to show.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The renderer is already mounted, a component type has no public
        /// parameterless constructor, or a component's <see cref="Component.Render"/>
        /// returned <see langword="null"/>.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// The tree contains a node type the renderer does not know.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Mounting walks the tree once, creating component instances and live
        /// nodes. Every mount effect is committed before this method returns, so
        /// effects observe a fully applied tree.
        /// </para>
        /// <para>
        /// A mount effect may call a state setter. Those updates are queued and
        /// flushed before returning so that the caller never sees an intermediate
        /// tree; <see cref="Version"/> is bumped once afterwards for the whole
        /// mount.
        /// </para>
        /// <para>
        /// A renderer cannot be mounted again. Create a new <see cref="Renderer"/>
        /// to build an unrelated tree.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var renderer = new Renderer();
        /// Node root = renderer.Mount(_.H("div", _.C&lt;Counter&gt;()));
        /// // root is now ready to hand to layout and selector matching.
        /// renderer.Flush(); // only needed when AutoFlush is false.
        /// </code>
        /// </example>
        public Node Mount(Node vdom)
        {
            ArgumentNullException.ThrowIfNull(vdom);

            if (_rootVdom is not null)
            {
                throw new InvalidOperationException("The renderer is already mounted.");
            }

            _rootVdom = vdom;
            _rootComponent = vdom as ComponentNode;

            var (live, logical) = MountNode(vdom, null, null);
            _rootVdom = logical;
            _rootLive = live;

            _flushing = true;

            try
            {
                CommitEffects();
            }
            finally
            {
                _flushing = false;
            }

            // A mount effect may have queued a state update. Run it before
            // returning so the first tree the caller sees is fully settled.
            if (_dirty.Count > 0)
            {
                Flush();
            }

            Version++;
            return _rootLive!;
        }

        /// <summary>
        /// Applies every queued state update and runs the resulting effects.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// More than 64 render/commit passes were needed because an effect kept
        /// queuing state updates unconditionally.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Each pass dequeues the dirty components, re-renders those that still need
        /// it, diffs their output into the live tree and then commits effects. An
        /// effect that updates state makes the queue non-empty again, so the loop
        /// repeats until it settles; <see cref="Version"/> is bumped at most once
        /// per pass, and only when at least one dirty component was processed.
        /// </para>
        /// <para>
        /// Calling this while a flush is already running is a no-op: the running
        /// loop owns the queue and will pick up the new work. Calling it before
        /// <see cref="Mount"/> is also a no-op.
        /// </para>
        /// <para>
        /// Effects run in two phases. Layout effects run first, in render order,
        /// then passive effects. Within one component, hooks run in call order.
        /// </para>
        /// </remarks>
        public void Flush()
        {
            if (_flushing || _rootVdom is null)
            {
                return;
            }

            _flushing = true;

            try
            {
                var iterations = 0;
                var changed = false;

                while (true)
                {
                    _rendered.Clear();

                    while (_dirty.Count > 0)
                    {
                        var component = _dirty.Dequeue();
                        component.Queued = false;

                        if (!component.IsMounted || !component.NeedsRender)
                        {
                            continue;
                        }

                        UpdateComponent(component);
                        changed = true;
                    }

                    CommitEffects();
                    iterations++;

                    if (changed)
                    {
                        Version++;
                        changed = false;
                    }

                    if (_dirty.Count == 0)
                    {
                        break;
                    }

                    if (iterations >= MaxFlushIterations)
                    {
                        throw new InvalidOperationException(
                            "The renderer exceeded the maximum number of update passes. An effect is probably updating state unconditionally.");
                    }
                }
            }
            finally
            {
                _flushing = false;
            }
        }

        /// <summary>
        /// Queues a component for a re-render and, when <see cref="AutoFlush"/> is
        /// enabled, flushes it immediately.
        /// </summary>
        /// <param name="component">The component whose state changed.</param>
        /// <remarks>
        /// Components that are not mounted are ignored, which makes a late setter
        /// call after unmount harmless. Duplicate-suppression lives in
        /// <see cref="Component.MarkDirty"/>, which checks
        /// <see cref="Component.Queued"/>; calling this directly bypasses that check.
        /// </remarks>
        internal void Enqueue(Component component)
        {
            if (!component.IsMounted)
            {
                return;
            }

            _dirty.Enqueue(component);

            if (AutoFlush && !_flushing)
            {
                Flush();
            }
        }

        /// <summary>
        /// Re-renders one queued component and writes its new output into the live
        /// tree.
        /// </summary>
        /// <param name="component">The component to update. It must be mounted.</param>
        /// <remarks>
        /// <para>
        /// The component's <see cref="Component.NeedsRender"/> flag is cleared
        /// first, then <see cref="Component.ShouldRender"/> is consulted. When it
        /// returns false the component keeps its previous output and no effect is
        /// committed for it.
        /// </para>
        /// <para>
        /// The first render mounts its output; later renders diff it in place. If
        /// the live root of the output changed identity, the parent's child slot is
        /// rewritten through <see cref="ReplaceSlot"/>. Effects are not run here;
        /// <see cref="Flush"/> commits them after the whole dirty queue has been
        /// processed.
        /// </para>
        /// </remarks>
        private void UpdateComponent(Component component)
        {
            var node = component.Node;

            if (node is null || !component.IsMounted)
            {
                return;
            }

            component.NeedsRender = false;

            if (!component.InvokeShouldRender())
            {
                return;
            }

            var rendered = RenderComponent(component);
            var oldLogical = node.OutputVdom;
            var oldLive = oldLogical is null ? null : GetLive(oldLogical);

            var (live, logical) = oldLogical is null
                ? MountNode(rendered, node.ParentLive, component.ContextScope)
                : DiffNode(oldLogical, rendered, node.ParentLive, component.ContextScope);

            node.OutputVdom = logical;

            if (node.ParentLive is null)
            {
                _rootLive = live;

                if (ReferenceEquals(node, _rootComponent))
                {
                    _rootVdom = node;
                }
            }
            else if (!ReferenceEquals(live, oldLive))
            {
                ReplaceSlot(node.ParentLive, node.OutputIndex, oldLive, live);
            }

            _rendered.Add(component);
            component.InvokeOnUpdate();
        }

        /// <summary>
        /// Builds the live subtree for a logical node and returns both its live
        /// root and the logical node to keep in the parent's virtual children.
        /// </summary>
        /// <param name="vdom">The logical node to materialise.</param>
        /// <param name="liveParent">
        /// The live parent the new nodes are attached to, or <see langword="null"/>
        /// for the root.
        /// </param>
        /// <param name="scope">
        /// The context scope in force above this node, or <see langword="null"/>
        /// outside any provider.
        /// </param>
        /// <returns>
        /// The live root that replaced <paramref name="vdom"/> in the tree, and the
        /// logical node the parent must retain (the component or provider node
        /// itself for logical kinds).
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// <paramref name="vdom"/> is a node type the renderer does not know. Only
        /// the five built-in kinds are supported.
        /// </exception>
        /// <remarks>
        /// Elements recurse into their children and then attach any bound
        /// <see cref="Ref{T}"/>. Fragments recurse without text folding. Components
        /// and providers are mounted through their dedicated helpers and their live
        /// root is the output, not the logical node.
        /// </remarks>
        private (Node Live, Node Logical) MountNode(
            Node vdom,
            Node? liveParent,
            ContextScope? scope)
        {
            switch (vdom)
            {
                case ComponentNode component:
                    return MountComponent(component, liveParent, scope);

                case ProviderNode provider:
                    return MountProvider(provider, liveParent, scope);

                case ElementNode element:
                    ApplyChildren(element, [], element.VdomChildren, scope, allowTextMerge: true);
                    element.AttachRef();
                    return (element, element);

                case FragmentNode fragment:
                    ApplyChildren(fragment, [], fragment.VdomChildren, scope, allowTextMerge: false);
                    return (fragment, fragment);

                case TextNode text:
                    return (text, text);

                default:
                    throw new NotSupportedException(
                        $"Unsupported node type {vdom.GetType().FullName}.");
            }
        }

        /// <summary>
        /// Reconciles a previously mounted logical node with a newly rendered one.
        /// </summary>
        /// <param name="oldLogical">
        /// The logical node currently in the tree, with its live state intact.
        /// </param>
        /// <param name="newLogical">The freshly rendered description to apply.</param>
        /// <param name="liveParent">The live parent that owns the slot.</param>
        /// <param name="scope">The context scope in force above the node.</param>
        /// <returns>
        /// The live root after patching, plus the logical node to keep in the
        /// parent's virtual children.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// <paramref name="newLogical"/> is a node type the renderer does not know.
        /// </exception>
        /// <remarks>
        /// <para>
        /// When <see cref="KindMatches"/> rejects the pair, the old subtree is
        /// unmounted and the new one is mounted from scratch, which discards
        /// component state. Otherwise the old node is reused and only the fields
        /// that actually differ are copied, preserving revision numbers and layout
        /// caches.
        /// </para>
        /// <para>
        /// Text nodes are always reused and their text assigned, even when it is
        /// unchanged; the <see cref="Donkeh.Data.Dom.IgniteSupportedDomNode.TextContent"/> setter
        /// suppresses the revision bump in that case.
        /// </para>
        /// </remarks>
        private (Node Live, Node Logical) DiffNode(
            Node oldLogical,
            Node newLogical,
            Node? liveParent,
            ContextScope? scope)
        {
            if (!KindMatches(oldLogical, newLogical))
            {
                UnmountNode(oldLogical);
                return MountNode(newLogical, liveParent, scope);
            }

            switch (newLogical)
            {
                case ComponentNode newComponent:
                    return DiffComponent((ComponentNode)oldLogical, newComponent, liveParent, scope);

                case ProviderNode newProvider:
                    return DiffProvider((ProviderNode)oldLogical, newProvider, liveParent, scope);

                case ElementNode newElement:
                {
                    var oldElement = (ElementNode)oldLogical;
                    oldElement.PatchFrom(newElement);
                    ApplyChildren(
                        oldElement,
                        oldElement.VdomChildren,
                        newElement.VdomChildren,
                        scope,
                        allowTextMerge: true);

                    return (oldElement, oldElement);
                }

                case FragmentNode newFragment:
                {
                    var oldFragment = (FragmentNode)oldLogical;
                    oldFragment.Key = newFragment.Key;
                    ApplyChildren(
                        oldFragment,
                        oldFragment.VdomChildren,
                        newFragment.VdomChildren,
                        scope,
                        allowTextMerge: false);

                    return (oldFragment, oldFragment);
                }

                case TextNode newText:
                {
                    var oldText = (TextNode)oldLogical;
                    oldText.Text = newText.Text;
                    return (oldText, oldText);
                }

                default:
                    throw new NotSupportedException(
                        $"Unsupported node type {newLogical.GetType().FullName}.");
            }
        }

        /// <summary>
        /// Creates a component instance for a logical node, renders it and mounts
        /// its output.
        /// </summary>
        /// <param name="node">The component node being mounted.</param>
        /// <param name="liveParent">The live parent that will own the output.</param>
        /// <param name="scope">The context scope the component renders under.</param>
        /// <returns>
        /// The live output root and the component node itself, which stays in the
        /// virtual tree so the instance can be reused.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// The component type cannot be constructed or its render returned null.
        /// </exception>
        /// <remarks>
        /// The instance is constructed once per logical node and receives its host,
        /// node, scope and props before the first render. It is added to the mounted
        /// list before <see cref="Component.OnMount"/> runs, so an <c>OnMount</c>
        /// override can call <see cref="FindComponent{T}"/> and see itself.
        /// </remarks>
        private (Node Live, Node Logical) MountComponent(
            ComponentNode node,
            Node? liveParent,
            ContextScope? scope)
        {
            var instance = CreateComponent(node.ComponentType);
            instance.Host = this;
            instance.Node = node;
            instance.ContextScope = scope;
            instance.IsMounted = true;
            instance.SetProps(node.Props);

            node.Instance = instance;
            node.ParentLive = liveParent;

            var rendered = RenderComponent(instance);
            var (live, logical) = MountNode(rendered, liveParent, scope);

            node.OutputVdom = logical;
            _rendered.Add(instance);
            _mounted.Add(instance);
            instance.InvokeOnMount();

            return (live, node);
        }

        /// <summary>
        /// Updates an existing component instance with new props and a possibly new
        /// context scope, then re-renders and diffs its output.
        /// </summary>
        /// <param name="oldNode">The mounted component node that owns the instance.</param>
        /// <param name="newNode">The freshly rendered component node.</param>
        /// <param name="liveParent">The live parent that owns the output slot.</param>
        /// <param name="scope">The context scope in force above the component.</param>
        /// <returns>
        /// The live output root after patching and the logical node to retain. When
        /// <see cref="Component.ShouldRender"/> vetoes the update, the previous
        /// output is returned untouched.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// A component type cannot be constructed or a render returned null.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Props are assigned before <see cref="Component.ShouldRender"/> is called
        /// so that an override can compare old and new props. Context scopes are
        /// captured per render, which is what lets a later state-driven render read
        /// the same values.
        /// </para>
        /// <para>
        /// A component whose instance is missing (for example after the renderer
        /// unmounted it out from under the tree) is mounted from scratch instead of
        /// patched.
        /// </para>
        /// </remarks>
        private (Node Live, Node Logical) DiffComponent(
            ComponentNode oldNode,
            ComponentNode newNode,
            Node? liveParent,
            ContextScope? scope)
        {
            var instance = oldNode.Instance;

            if (instance is null)
            {
                UnmountNode(oldNode);
                return MountComponent(newNode, liveParent, scope);
            }

            instance.SetProps(newNode.Props);
            instance.ContextScope = scope;
            oldNode.Props = newNode.Props;
            oldNode.ParentLive = liveParent;

            if (!instance.InvokeShouldRender())
            {
                if (oldNode.OutputVdom is null)
                {
                    return MountComponent(newNode, liveParent, scope);
                }

                return (GetLive(oldNode.OutputVdom), oldNode);
            }

            var rendered = RenderComponent(instance);
            var oldOutput = oldNode.OutputVdom;

            var (live, logical) = oldOutput is null
                ? MountNode(rendered, liveParent, scope)
                : DiffNode(oldOutput, rendered, liveParent, scope);

            oldNode.OutputVdom = logical;
            instance.NeedsRender = false;
            _rendered.Add(instance);
            instance.InvokeOnUpdate();

            return (live, oldNode);
        }

        /// <summary>
        /// Mounts a context provider as a transparent fragment over a new child
        /// scope.
        /// </summary>
        /// <param name="provider">The provider node to mount.</param>
        /// <param name="liveParent">The live parent that will own the fragment.</param>
        /// <param name="scope">The context scope above the provider.</param>
        /// <returns>
        /// The backing fragment as the live root and the provider node as the
        /// logical node to retain.
        /// </returns>
        /// <remarks>
        /// The backing fragment mirrors the provider's key, but reconciliation is
        /// driven by the provider node itself, which stays in the virtual tree so a
        /// later diff can update the published value in place.
        /// </remarks>
        private (Node Live, Node Logical) MountProvider(
            ProviderNode provider,
            Node? liveParent,
            ContextScope? scope)
        {
            var fragment = new FragmentNode { Key = provider.Key };
            var childScope = new ContextScope(scope, provider.Context, provider.Value);

            ApplyChildren(fragment, [], provider.ProviderChildren, childScope, allowTextMerge: false);

            provider.OutputVdom = fragment;
            provider.ParentLive = liveParent;

            return (fragment, provider);
        }

        /// <summary>
        /// Republishes a context provider's value and diffs its children under a new
        /// child scope.
        /// </summary>
        /// <param name="oldNode">The mounted provider node.</param>
        /// <param name="newNode">The freshly rendered provider node.</param>
        /// <param name="liveParent">The live parent that owns the fragment.</param>
        /// <param name="scope">The context scope above the provider.</param>
        /// <returns>
        /// The existing backing fragment and the provider node to retain.
        /// </returns>
        /// <remarks>
        /// The provider itself is not a component, so changing its value does not
        /// re-render consumers by itself. A new scope object is created for the
        /// children, which means components already rendered below it keep the old
        /// scope until they next render.
        /// </remarks>
        private (Node Live, Node Logical) DiffProvider(
            ProviderNode oldNode,
            ProviderNode newNode,
            Node? liveParent,
            ContextScope? scope)
        {
            oldNode.Value = newNode.Value;
            oldNode.ParentLive = liveParent;

            var fragment = oldNode.OutputVdom;

            if (fragment is null)
            {
                return MountProvider(newNode, liveParent, scope);
            }

            var childScope = new ContextScope(scope, newNode.Context, newNode.Value);
            ApplyChildren(fragment, fragment.VdomChildren, newNode.ProviderChildren, childScope, allowTextMerge: false);

            return (fragment, oldNode);
        }

        /// <summary>
        /// Diffs a parent's child list and writes the resulting live children back
        /// onto the parent.
        /// </summary>
        /// <param name="parent">The live element or fragment that owns the children.</param>
        /// <param name="oldLogical">The logical children from the previous render.</param>
        /// <param name="newLogical">The logical children from the new render.</param>
        /// <param name="scope">The context scope in force for the children.</param>
        /// <param name="allowTextMerge">
        /// True for elements, where an all-text child list is folded into
        /// <see cref="Donkeh.Data.Dom.IgniteSupportedDomNode.TextContent"/> instead of
        /// creating text boxes. Pass false for fragments, which cannot carry text
        /// themselves.
        /// </param>
        /// <remarks>
        /// <para>
        /// Children are first normalised so nulls introduced by conditional
        /// rendering disappear. When every new child is text and merging is
        /// allowed, the live children are dropped and the concatenated text is put
        /// on the element; otherwise any stale <c>TextContent</c> is cleared before
        /// the child diff runs.
        /// </para>
        /// <para>
        /// Assigning <see cref="Donkeh.Data.Dom.IgniteSupportedDomNode.Children"/> bumps the
        /// parent's revision and invalidates its style cache, so the array is only
        /// reassigned when a child actually moved or changed identity. Logical
        /// component and provider nodes then have their output slot indexes
        /// refreshed for future in-place replacements.
        /// </para>
        /// </remarks>
        private void ApplyChildren(
            Node parent,
            Node[] oldLogical,
            Node[] newLogical,
            ContextScope? scope,
            bool allowTextMerge)
        {
            newLogical = _.NormalizeChildren(newLogical);

            if (allowTextMerge
                && parent is ElementNode element
                && AllText(newLogical))
            {
                for (var i = 0; i < oldLogical.Length; i++)
                {
                    UnmountNode(oldLogical[i]);
                }

                element.TextContent = newLogical.Length == 0 ? null : ConcatText(newLogical);

                if (element.Children.Length != 0)
                {
                    element.Children = [];
                }

                element.VdomChildren = newLogical;
                return;
            }

            if (parent is ElementNode elementNode && elementNode.TextContent is not null)
            {
                elementNode.TextContent = null;
            }

            var (live, logical) = DiffChildren(parent, oldLogical, newLogical, scope);

            parent.VdomChildren = logical;

            // Reassigning Children bumps the parent's revision and invalidates
            // its style cache. When every child is the same object in the same
            // order, the old array is still correct.
            if (!ChildrenEqual(parent.Children, live))
            {
                parent.Children = live;
            }

            for (var i = 0; i < logical.Length; i++)
            {
                switch (logical[i])
                {
                    case ComponentNode component:
                        SetOutputSlot(component, parent, i);
                        break;

                    case ProviderNode provider:
                        SetOutputSlot(provider, parent, i);
                        break;
                }
            }
        }

        /// <summary>
        /// Matches old logical children against new ones and returns the live and
        /// logical child arrays for the parent.
        /// </summary>
        /// <param name="parent">The live parent passed down to nested diffs.</param>
        /// <param name="oldLogical">The previous logical children.</param>
        /// <param name="newLogical">The new logical children, already normalised.</param>
        /// <param name="scope">The context scope in force for the children.</param>
        /// <returns>
        /// The live children to assign to the parent, and the logical children to
        /// store as the parent's virtual children.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Keyed children are matched by <see cref="Node.Key"/> regardless of
        /// position, so reordering a keyed list moves nodes instead of remounting
        /// them. Unkeyed children fall back to the first unused positional slot of
        /// the same kind. Old children that are not consumed are unmounted after the
        /// new list has been built.
        /// </para>
        /// <para>
        /// The match is one-to-one: each old index can be consumed once, and a keyed
        /// duplicate falls through to a fresh mount rather than stealing a used
        /// node.
        /// </para>
        /// </remarks>
        private (Node[] Live, Node[] Logical) DiffChildren(
            Node parent,
            Node[] oldLogical,
            Node[] newLogical,
            ContextScope? scope)
        {
            var live = new List<Node>(newLogical.Length);
            var logical = new List<Node>(newLogical.Length);
            var oldUsed = new bool[oldLogical.Length];
            Dictionary<string, int>? keyed = null;

            for (var i = 0; i < oldLogical.Length; i++)
            {
                if (oldLogical[i].Key is { } key)
                {
                    keyed ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    keyed[key] = i;
                }
            }

            for (var i = 0; i < newLogical.Length; i++)
            {
                var newChild = newLogical[i];
                var oldIndex = FindOldChild(oldLogical, oldUsed, keyed, newChild);

                if (oldIndex >= 0)
                {
                    oldUsed[oldIndex] = true;

                    var (childLive, childLogical) = DiffNode(
                        oldLogical[oldIndex],
                        newChild,
                        parent,
                        scope);

                    live.Add(childLive);
                    logical.Add(childLogical);
                }
                else
                {
                    var (childLive, childLogical) = MountNode(newChild, parent, scope);
                    live.Add(childLive);
                    logical.Add(childLogical);
                }
            }

            for (var i = 0; i < oldLogical.Length; i++)
            {
                if (!oldUsed[i])
                {
                    UnmountNode(oldLogical[i]);
                }
            }

            return (live.ToArray(), logical.ToArray());
        }

        /// <summary>
        /// Finds the old child that should be reconciled with a new child.
        /// </summary>
        /// <param name="oldChildren">The previous logical children.</param>
        /// <param name="used">Flags marking old indexes already consumed this pass.</param>
        /// <param name="keyed">
        /// Key-to-index map for keyed old children, or <see langword="null"/> when no
        /// old child has a key.
        /// </param>
        /// <param name="newChild">The child to match.</param>
        /// <returns>
        /// The index of the reusable old child, or -1 when the new child must be
        /// mounted.
        /// </returns>
        /// <remarks>
        /// A keyed child only ever matches its own key, and only if the kinds also
        /// agree. An unkeyed child skips both used slots and keyed old children, so
        /// it cannot accidentally consume a node that a key was meant to own.
        /// </remarks>
        private static int FindOldChild(
            Node[] oldChildren,
            bool[] used,
            Dictionary<string, int>? keyed,
            Node newChild)
        {
            if (newChild.Key is { } key)
            {
                if (keyed is not null
                    && keyed.TryGetValue(key, out var index)
                    && !used[index]
                    && KindMatches(oldChildren[index], newChild))
                {
                    return index;
                }

                return -1;
            }

            for (var i = 0; i < oldChildren.Length; i++)
            {
                if (used[i] || oldChildren[i].Key is not null)
                {
                    continue;
                }

                if (KindMatches(oldChildren[i], newChild))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Decides whether an old node and a new node describe the same kind of
        /// thing and can therefore be diffed in place.
        /// </summary>
        /// <param name="oldNode">The mounted node.</param>
        /// <param name="newNode">The freshly rendered node.</param>
        /// <returns>
        /// True when the pair can be patched; false when the old node must be
        /// unmounted and the new one mounted.
        /// </returns>
        /// <remarks>
        /// Components match on the exact component type, providers on the exact
        /// <see cref="Context{T}"/> instance, and elements on the interned tag name.
        /// Fragments and text nodes always match their own kind. Keys and props are
        /// deliberately not part of this test: they are handled by
        /// <see cref="FindOldChild"/> and the component diff.
        /// </remarks>
        private static bool KindMatches(Node oldNode, Node newNode)
        {
            return (oldNode, newNode) switch
            {
                (ComponentNode left, ComponentNode right) =>
                    left.ComponentType == right.ComponentType,

                (ProviderNode left, ProviderNode right) =>
                    ReferenceEquals(left.Context, right.Context),

                (ElementNode left, ElementNode right) =>
                    left.InternedTagName == right.InternedTagName,

                (FragmentNode, FragmentNode) => true,

                (TextNode, TextNode) => true,

                _ => false
            };
        }

        /// <summary>
        /// Points a component node at the live parent slot its output currently
        /// occupies.
        /// </summary>
        /// <param name="node">The mounted component node.</param>
        /// <param name="parentLive">The live parent that owns the slot.</param>
        /// <param name="index">The child index of the component's output in that parent.</param>
        /// <remarks>
        /// A component whose output is itself a component or provider has no live
        /// node of its own, so the slot is pushed down the output chain until the
        /// first live node is reached. This is what lets a nested component replace
        /// itself in place when its own output changes kind.
        /// </remarks>
        private static void SetOutputSlot(ComponentNode node, Node parentLive, int index)
        {
            node.ParentLive = parentLive;
            node.OutputIndex = index;

            switch (node.OutputVdom)
            {
                case ComponentNode inner:
                    SetOutputSlot(inner, parentLive, index);
                    break;

                case ProviderNode provider:
                    SetOutputSlot(provider, parentLive, index);
                    break;
            }
        }

        /// <summary>
        /// Points a provider node at the live parent slot its fragment occupies.
        /// </summary>
        /// <param name="node">The mounted provider node.</param>
        /// <param name="parentLive">The live parent that owns the slot.</param>
        /// <param name="index">The child index of the provider's fragment.</param>
        /// <remarks>
        /// Unlike <see cref="SetOutputSlot(ComponentNode, Node, int)"/> this does not
        /// recurse, because a provider always renders as a fragment and never wraps
        /// another logical node.
        /// </remarks>
        private static void SetOutputSlot(ProviderNode node, Node parentLive, int index)
        {
            node.ParentLive = parentLive;
            node.OutputIndex = index;
        }

        /// <summary>
        /// Tears down a logical subtree: runs component unmount hooks and effect
        /// cleanups, releases refs, events and parent links, and detaches every live
        /// node.
        /// </summary>
        /// <param name="node">The logical node to remove. It is not reused afterwards.</param>
        /// <remarks>
        /// <para>
        /// For a component, <see cref="Component.OnUnmount"/> runs before its hook
        /// cleanups, then the instance is flagged unmounted and stripped of its host,
        /// node and scope so a later setter call becomes a no-op. Its rendered output
        /// is unmounted recursively.
        /// </para>
        /// <para>
        /// For an element, this clears the child arrays, detaches the bound ref,
        /// drops event handlers and nulls the parent link. Detaching is important:
        /// it prevents a stale ref from keeping an otherwise dead node alive.
        /// </para>
        /// </remarks>
        private void UnmountNode(Node node)
        {
            switch (node)
            {
                case ComponentNode component:
                    if (component.Instance is { } instance)
                    {
                        _mounted.Remove(instance);
                        instance.InvokeOnUnmount();
                        RunEffectCleanups(instance);
                        instance.IsMounted = false;
                        instance.Host = null;
                        instance.Node = null;
                        instance.ContextScope = null;
                    }

                    if (component.OutputVdom is { } output)
                    {
                        UnmountNode(output);
                    }

                    component.OutputVdom = null;
                    component.Instance = null;
                    break;

                case ProviderNode provider:
                    if (provider.OutputVdom is { } providerFragment)
                    {
                        UnmountNode(providerFragment);
                    }

                    provider.OutputVdom = null;
                    break;

                case ElementNode element:
                    for (var i = 0; i < element.VdomChildren.Length; i++)
                    {
                        UnmountNode(element.VdomChildren[i]);
                    }

                    element.VdomChildren = [];
                    element.DetachRef();
                    element.Events = null;

                    if (element.Children.Length != 0)
                    {
                        element.Children = [];
                    }

                    element.Parent = null;
                    break;

                case FragmentNode fragment:
                    for (var i = 0; i < fragment.VdomChildren.Length; i++)
                    {
                        UnmountNode(fragment.VdomChildren[i]);
                    }

                    fragment.VdomChildren = [];

                    if (fragment.Children.Length != 0)
                    {
                        fragment.Children = [];
                    }

                    fragment.Parent = null;
                    break;

                case TextNode text:
                    text.Parent = null;
                    break;
            }
        }

        /// <summary>
        /// Runs and clears every pending effect cleanup a component owns.
        /// </summary>
        /// <param name="component">The component being removed from the tree.</param>
        /// <remarks>
        /// Cleanups run in hook declaration order. The pending flag is cleared before
        /// the cleanup runs, so an effect that was scheduled but never executed has
        /// nothing to clean up. A cleanup that throws aborts the remaining ones.
        /// </remarks>
        private static void RunEffectCleanups(Component component)
        {
            if (component.Hooks is null)
            {
                return;
            }

            for (var i = 0; i < component.Hooks.Count; i++)
            {
                if (component.Hooks[i] is not EffectHook hook)
                {
                    continue;
                }

                hook.Pending = false;

                if (hook.Cleanup is { } cleanup)
                {
                    hook.Cleanup = null;
                    cleanup();
                }
            }
        }

        /// <summary>
        /// Runs every effect scheduled by the current pass, layout effects first and
        /// then passive effects.
        /// </summary>
        /// <remarks>
        /// This runs after the tree has been fully applied, so effects observe the
        /// committed output of every component rendered in the pass, not a partially
        /// patched tree. An effect may queue state updates; the flush loop picks them
        /// up on its next iteration.
        /// </remarks>
        private void CommitEffects()
        {
            RunEffects(isLayout: true);
            RunEffects(isLayout: false);
        }

        /// <summary>
        /// Runs pending effects of one phase for every component rendered this pass.
        /// </summary>
        /// <param name="isLayout">
        /// True to run layout effects, false to run passive effects.
        /// </param>
        /// <remarks>
        /// <para>
        /// Components are visited in render order and hooks in declaration order.
        /// Hooks are skipped when their <see cref="EffectHook.Pending"/> flag is
        /// clear or their phase differs, so a component that did not change its
        /// dependencies runs nothing.
        /// </para>
        /// <para>
        /// A previous cleanup always runs before the new effect body. A factory
        /// effect stores its return value as the next cleanup; a plain action effect
        /// leaves the previous cleanup slot empty.
        /// </para>
        /// </remarks>
        private void RunEffects(bool isLayout)
        {
            for (var i = 0; i < _rendered.Count; i++)
            {
                var component = _rendered[i];

                if (!component.IsMounted || component.Hooks is null)
                {
                    continue;
                }

                for (var h = 0; h < component.Hooks.Count; h++)
                {
                    if (component.Hooks[h] is not EffectHook hook
                        || !hook.Pending
                        || hook.IsLayout != isLayout)
                    {
                        continue;
                    }

                    hook.Pending = false;

                    if (hook.Cleanup is { } cleanup)
                    {
                        hook.Cleanup = null;
                        cleanup();
                    }

                    if (hook.Effect is { } effect)
                    {
                        effect();
                    }
                    else if (hook.EffectFactory is { } factory)
                    {
                        hook.Cleanup = factory();
                    }
                }
            }
        }

        /// <summary>
        /// Walks down component and provider outputs until a live node is reached.
        /// </summary>
        /// <param name="logical">The logical node whose live root is wanted.</param>
        /// <returns>The first live node in the output chain.</returns>
        /// <exception cref="InvalidOperationException">
        /// The output chain is deeper than 64 links and is assumed to be cyclic.
        /// </exception>
        /// <remarks>
        /// Components are allowed to render another component with no element in
        /// between, so a live node may be several logical hops away. The depth limit
        /// is a cycle guard; legitimate chains are nowhere near it.
        /// </remarks>
        private static Node GetLive(Node logical)
        {
            for (var depth = 0; depth < 64; depth++)
            {
                switch (logical)
                {
                    case ComponentNode { OutputVdom: { } componentOutput }:
                        logical = componentOutput;
                        continue;

                    case ProviderNode { OutputVdom: { } providerOutput }:
                        logical = providerOutput;
                        continue;

                    default:
                        return logical;
                }
            }

            throw new InvalidOperationException(
                "The component output chain is cyclic.");
        }

        /// <summary>
        /// Replaces a component's old live output with its new live output inside the
        /// parent's child array.
        /// </summary>
        /// <param name="parentLive">The live parent that owns the child array.</param>
        /// <param name="index">The last known child index of the old output.</param>
        /// <param name="oldLive">The output being replaced, or null if none was live.</param>
        /// <param name="newLive">The output to insert.</param>
        /// <remarks>
        /// <para>
        /// The recorded index is tried first and verified by reference. If the index
        /// is stale, the array is scanned for the old node. This keeps the array
        /// correct when a sibling changed in the same pass.
        /// </para>
        /// <para>
        /// If the old output is no longer present at all, the new output is appended
        /// rather than dropped. That leaves a stray node in the pathological case, but
        /// it keeps the rendered tree reachable instead of silently discarding it.
        /// </para>
        /// <para>
        /// Assignment through the <c>Children</c> setter, rather than in-place
        /// mutation, is what refreshes parent pointers and sibling indexes.
        /// </para>
        /// </remarks>
        private static void ReplaceSlot(
            Node parentLive,
            int index,
            Node? oldLive,
            Node newLive)
        {
            var children = parentLive.Children;

            if ((uint)index < (uint)children.Length
                && ReferenceEquals(children[index], oldLive))
            {
                children[index] = newLive;
                parentLive.Children = children;
                return;
            }

            for (var i = 0; i < children.Length; i++)
            {
                if (ReferenceEquals(children[i], oldLive))
                {
                    children[i] = newLive;
                    parentLive.Children = children;
                    return;
                }
            }

            // The old output is no longer in the parent. Append the new output so
            // it is not lost; this only happens if the parent changed underneath
            // the component.
            var updated = new Donkeh.Data.Dom.IgniteSupportedDomNode[children.Length + 1];
            Array.Copy(children, updated, children.Length);
            updated[^1] = newLive;
            parentLive.Children = updated;
        }

        /// <summary>
        /// Invokes a component's render inside the hook-tracking window and rejects a
        /// null result.
        /// </summary>
        /// <param name="component">The component to render.</param>
        /// <returns>The logical node produced by <see cref="Component.Render"/>.</returns>
        /// <exception cref="InvalidOperationException">
        /// <see cref="Component.Render"/> returned <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <see cref="Component.BeginRender"/> resets the hook cursor to zero and
        /// opens the window in which hooks may be called;
        /// <see cref="Component.EndRender"/> closes it even when the render throws.
        /// Hook calls outside this window are rejected.
        /// </remarks>
        private static Node RenderComponent(Component component)
        {
            component.BeginRender();

            Node rendered;

            try
            {
                rendered = component.Render();
            }
            finally
            {
                component.EndRender();
            }

            if (rendered is null)
            {
                throw new InvalidOperationException(
                    $"{component.GetType().FullName}.Render() returned null. Return a node or an empty fragment instead.");
            }

            return rendered;
        }

        /// <summary>
        /// Instantiates a component type through its cached factory.
        /// </summary>
        /// <param name="type">The concrete component type to construct.</param>
        /// <returns>A new, unmounted component instance.</returns>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="type"/> does not derive from <see cref="Component"/> or has
        /// no public parameterless constructor.
        /// </exception>
        /// <remarks>
        /// The reflection cost is paid once per type. The factory delegates still run
        /// on every mount, so component constructors must be cheap and must not rely
        /// on being called on a particular instance count.
        /// </remarks>
        private static Component CreateComponent(Type type)
        {
            var factory = Factories.GetOrAdd(type, static componentType =>
            {
                if (!typeof(Component).IsAssignableFrom(componentType))
                {
                    throw new InvalidOperationException(
                        $"{componentType.FullName} is not a component.");
                }

                var constructor = componentType.GetConstructor(Type.EmptyTypes)
                    ?? throw new InvalidOperationException(
                        $"{componentType.FullName} needs a public parameterless constructor.");

                return () => (Component)constructor.Invoke(null);
            });

            return factory();
        }

        /// <summary>
        /// Compares the live child array with the newly diffed one by reference.
        /// </summary>
        /// <param name="left">The children currently assigned to the parent.</param>
        /// <param name="right">The children produced by the diff.</param>
        /// <returns>
        /// True when both arrays have the same length and hold the same instances in
        /// the same order.
        /// </returns>
        /// <remarks>
        /// Only identity and order matter; node contents may have changed in place.
        /// This exists purely to decide whether the parent's <c>Children</c> setter
        /// needs to run, since that setter bumps the revision and invalidates style
        /// caches.
        /// </remarks>
        private static bool ChildrenEqual(
            Donkeh.Data.Dom.IgniteSupportedDomNode[] left,
            Node[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (!ReferenceEquals(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Reports whether every child is a <see cref="TextNode"/>, which makes the
        /// list eligible for folding into the parent's text content.
        /// </summary>
        /// <param name="children">The normalised child list to inspect.</param>
        /// <returns>True for an empty list or a list made only of text nodes.</returns>
        /// <remarks>
        /// An empty list counts as all text, which is how an element that used to
        /// have text has its text cleared without creating a child.
        /// </remarks>
        private static bool AllText(Node[] children)
        {
            for (var i = 0; i < children.Length; i++)
            {
                if (children[i] is not TextNode)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Joins the text of adjacent text children into one string.
        /// </summary>
        /// <param name="children">A non-empty list of text nodes.</param>
        /// <returns>
        /// The concatenated text, using the node's string directly when there is only
        /// one.
        /// </returns>
        /// <remarks>
        /// Only called after <see cref="AllText"/> has confirmed the list, so the
        /// casts are safe. The single-child path avoids allocating a builder.
        /// </remarks>
        private static string ConcatText(Node[] children)
        {
            if (children.Length == 1)
            {
                return ((TextNode)children[0]).Text;
            }

            var builder = new StringBuilder();

            for (var i = 0; i < children.Length; i++)
            {
                builder.Append(((TextNode)children[i]).Text);
            }

            return builder.ToString();
        }
    }
}
