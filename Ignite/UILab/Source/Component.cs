namespace Ignite.UILab
{
    /// <summary>
    /// The base class for class components. A component owns its hook state and
    /// is reused across renders by the reconciler.
    /// </summary>
    /// <remarks>
    /// Components must expose a public parameterless constructor. Props are
    /// assigned by the renderer before <see cref="Render"/> is called.
    /// </remarks>
    public abstract partial class Component
    {
        internal Renderer? Host;
        internal ComponentNode? Node;
        internal ContextScope? ContextScope;
        internal List<Hook>? Hooks;
        internal int HookIndex;
        internal bool IsRendering;
        internal bool NeedsRender;
        internal bool Queued;
        internal bool IsMounted;

        /// <summary>True once the component has been mounted into a renderer.</summary>
        public bool Mounted => IsMounted;

        /// <summary>Builds the component's virtual subtree.</summary>
        public abstract Node Render();

        /// <summary>
        /// Called after the component's first commit.
        /// </summary>
        protected virtual void OnMount()
        {
        }

        /// <summary>
        /// Called after the component re-renders.
        /// </summary>
        protected virtual void OnUpdate()
        {
        }

        /// <summary>
        /// Called when the component is removed from the tree.
        /// </summary>
        protected virtual void OnUnmount()
        {
        }

        /// <summary>
        /// Return false to skip a render when the parent re-renders.
        /// </summary>
        protected virtual bool ShouldRender() => true;

        internal virtual void SetProps(object? props)
        {
        }

        internal void BeginRender()
        {
            HookIndex = 0;
            IsRendering = true;
        }

        internal void EndRender()
        {
            IsRendering = false;
        }

        internal void InvokeOnMount() => OnMount();

        internal void InvokeOnUpdate() => OnUpdate();

        internal void InvokeOnUnmount() => OnUnmount();

        internal bool InvokeShouldRender() => ShouldRender();

        internal void MarkDirty()
        {
            NeedsRender = true;

            if (Host is { } host && !Queued)
            {
                Queued = true;
                host.Enqueue(this);
            }
        }
    }

    /// <summary>
    /// A component with typed props. The renderer assigns <see cref="Props"/>
    /// before calling <see cref="Component.Render"/>.
    /// </summary>
    public abstract class Component<TProps> : Component
    {
        /// <summary>The props supplied by the parent render.</summary>
        public TProps Props { get; private set; } = default!;

        internal override void SetProps(object? props)
        {
            if (props is TProps typed)
            {
                Props = typed;
                return;
            }

            if (props is null)
            {
                Props = default!;
                return;
            }

            throw new ArgumentException(
                $"Expected props of type {typeof(TProps).FullName} but received {props.GetType().FullName}.",
                nameof(props));
        }
    }
}
