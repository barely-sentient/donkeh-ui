using System.Collections.Concurrent;
using System.Text;

namespace Ignite.UILab
{
    /// <summary>
    /// Mounts a virtual tree, reconciles it in place and schedules re-renders.
    /// </summary>
    /// <remarks>
    /// The live tree is made of <see cref="ElementNode"/>, <see cref="TextNode"/>
    /// and <see cref="FragmentNode"/> instances. A diff mutates those nodes in
    /// place and only bumps their revision when something actually changed, so
    /// unchanged subtrees keep their layout cache entries.
    /// </remarks>
    public sealed class Renderer
    {
        private const int MaxFlushIterations = 64;

        private static readonly ConcurrentDictionary<Type, Func<Component>> Factories =
            new();

        private readonly Queue<Component> _dirty = new();
        private readonly List<Component> _rendered = new();

        private Node? _rootVdom;
        private Node? _rootLive;
        private ComponentNode? _rootComponent;
        private bool _flushing;

        /// <summary>
        /// When true (the default), a state update flushes synchronously. Set it
        /// to false to batch updates and call <see cref="Flush"/> from your frame
        /// loop.
        /// </summary>
        public bool AutoFlush { get; set; } = true;

        /// <summary>The live root node, or null before <see cref="Mount"/>.</summary>
        public Node? Root => _rootLive;

        /// <summary>True while a flush is applying changes and running effects.</summary>
        public bool IsFlushing => _flushing;

        /// <summary>
        /// Mounts a virtual tree and returns its live root. A renderer can only be
        /// mounted once.
        /// </summary>
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

            return _rootLive!;
        }

        /// <summary>
        /// Applies every queued state update and runs the resulting effects.
        /// </summary>
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
                    }

                    CommitEffects();
                    iterations++;

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
            instance.InvokeOnMount();

            return (live, node);
        }

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

        private static void SetOutputSlot(ProviderNode node, Node parentLive, int index)
        {
            node.ParentLive = parentLive;
            node.OutputIndex = index;
        }

        private void UnmountNode(Node node)
        {
            switch (node)
            {
                case ComponentNode component:
                    if (component.Instance is { } instance)
                    {
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

        private void CommitEffects()
        {
            RunEffects(isLayout: true);
            RunEffects(isLayout: false);
        }

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
            var updated = new Ignite.Data.Dom.IgniteSupportedDomNode[children.Length + 1];
            Array.Copy(children, updated, children.Length);
            updated[^1] = newLive;
            parentLive.Children = updated;
        }

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

        private static bool ChildrenEqual(
            Ignite.Data.Dom.IgniteSupportedDomNode[] left,
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
