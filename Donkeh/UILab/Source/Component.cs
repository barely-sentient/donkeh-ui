namespace Donkeh.UILab
{
    /// <summary>
    /// The base class for class components. A component owns its hook state and
    /// is reused across renders by the reconciler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Components must expose a public parameterless constructor. Props are
    /// assigned by the renderer before <see cref="Render"/> is called.
    /// </para>
    /// <para>
    /// One instance exists per mounted <see cref="ComponentNode"/>. The renderer
    /// creates it on mount, reuses it for every later render of the same node and
    /// discards it on unmount, which is why hook state lives exactly as long as the
    /// component stays in the tree. Do not instantiate components yourself: an
    /// instance that does not belong to a <see cref="Renderer"/> cannot render.
    /// </para>
    /// <para>
    /// Hooks may only be called from <see cref="Render"/>, unconditionally and in
    /// the same order on every render. The instance is bound to the renderer's
    /// thread and is not thread-safe.
    /// </para>
    /// </remarks>
    /// <seealso cref="Renderer"/>
    /// <seealso cref="ComponentNode"/>
    public abstract partial class Component
    {
        /// <summary>
        /// The renderer that owns this component, or null before mount and after
        /// unmount. State setters use it to queue a re-render.
        /// </summary>
        internal Renderer? Host;

        /// <summary>
        /// The logical node that owns this instance, or null while unmounted. It
        /// carries the output and the live parent slot.
        /// </summary>
        internal ComponentNode? Node;

        /// <summary>
        /// The context scope captured when the component last rendered. It is read
        /// by <see cref="UseContext{T}"/> and survives until the next render.
        /// </summary>
        internal ContextScope? ContextScope;

        /// <summary>
        /// Hook slots in call order, or null until the first hook is declared. The
        /// list is append-only; a slot is never removed or reordered.
        /// </summary>
        internal List<Hook>? Hooks;

        /// <summary>
        /// The cursor into <see cref="Hooks"/> for the current render. Reset to zero
        /// by <see cref="BeginRender"/> and advanced by every hook call.
        /// </summary>
        internal int HookIndex;

        /// <summary>
        /// True between <see cref="BeginRender"/> and <see cref="EndRender"/>. Hook
        /// calls and state setters outside this window are rejected.
        /// </summary>
        internal bool IsRendering;

        /// <summary>
        /// True when a state update has been applied and a render is owed. The
        /// renderer clears it just before re-rendering.
        /// </summary>
        internal bool NeedsRender;

        /// <summary>
        /// True while the component sits in the renderer's dirty queue. Prevents
        /// duplicate queue entries until the flush dequeues it.
        /// </summary>
        internal bool Queued;

        /// <summary>
        /// True between mount and unmount. Cleared only after
        /// <see cref="OnUnmount"/> and the hook cleanups have run, so those callbacks
        /// still observe a mounted component.
        /// </summary>
        internal bool IsMounted;

        /// <summary>True once the component has been mounted into a renderer.</summary>
        /// <value>
        /// True from mount until unmount. False for a freshly constructed instance.
        /// </value>
        /// <remarks>
        /// This reflects tree membership, not whether a render is in progress. Use
        /// <see cref="Renderer.IsFlushing"/> or the hook window to detect those.
        /// </remarks>
        public bool Mounted => IsMounted;

        /// <summary>Builds the component's virtual subtree.</summary>
        /// <returns>
        /// The logical root of the subtree. Must not be <see langword="null"/>; return
        /// an empty fragment to render nothing.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown by the renderer when this returns <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// This runs with the hook cursor reset, so hooks are called in source order
        /// and must be unconditional. State setters throw while a render is in
        /// progress; schedule updates from effects or event handlers instead.
        /// </para>
        /// <para>
        /// The returned nodes are treated as a description: the renderer diffs them
        /// against the previous output and patches the live tree in place.
        /// </para>
        /// </remarks>
        public abstract Node Render();

        /// <summary>
        /// Called after the component's first commit.
        /// </summary>
        /// <remarks>
        /// Runs after the subtree has been mounted and its refs attached. Layout
        /// effects of the same commit have not run yet, so this is a good place to
        /// start work that does not depend on layout.
        /// </remarks>
        protected virtual void OnMount()
        {
        }

        /// <summary>
        /// Called after the component re-renders.
        /// </summary>
        /// <remarks>
        /// Runs once per committed update, after the new output has been diffed into
        /// the tree. It is not called when <see cref="ShouldRender"/> vetoes a
        /// parent-driven render.
        /// </remarks>
        protected virtual void OnUpdate()
        {
        }

        /// <summary>
        /// Called when the component is removed from the tree.
        /// </summary>
        /// <remarks>
        /// Runs before hook effect cleanups and before
        /// <see cref="IsMounted"/> is cleared. The component's output is still
        /// reachable during this call, but it must not schedule further renders.
        /// </remarks>
        protected virtual void OnUnmount()
        {
        }

        /// <summary>
        /// Return false to skip a render when the parent re-renders.
        /// </summary>
        /// <returns>
        /// True to render, false to keep the previous output. The default is true.
        /// </returns>
        /// <remarks>
        /// This only gates parent-driven renders; a render caused by this component's
        /// own state update is not vetoed. A vetoed render still receives new props
        /// and context scope, so a later state update will see them.
        /// </remarks>
        protected virtual bool ShouldRender() => true;

        /// <summary>
        /// Assigns props supplied by the parent render.
        /// </summary>
        /// <param name="props">The new props, or null to clear them.</param>
        /// <remarks>
        /// The base implementation ignores props because this class has no typed
        /// props. <see cref="Component{TProps}"/> overrides it to assign
        /// <see cref="Component{TProps}.Props"/>.
        /// </remarks>
        internal virtual void SetProps(object? props)
        {
        }

        /// <summary>
        /// Opens the render window and resets the hook cursor.
        /// </summary>
        /// <remarks>
        /// Called by the renderer immediately before <see cref="Render"/>. The
        /// cursor reset is what makes hook order, rather than call site, the identity
        /// of a hook slot.
        /// </remarks>
        internal void BeginRender()
        {
            HookIndex = 0;
            IsRendering = true;
        }

        /// <summary>
        /// Closes the render window.
        /// </summary>
        /// <remarks>
        /// Called from a <c>finally</c> block so a throwing render does not leave the
        /// component stuck in the rendering state.
        /// </remarks>
        internal void EndRender()
        {
            IsRendering = false;
        }

        /// <summary>Invokes the <see cref="OnMount"/> override.</summary>
        internal void InvokeOnMount() => OnMount();

        /// <summary>Invokes the <see cref="OnUpdate"/> override.</summary>
        internal void InvokeOnUpdate() => OnUpdate();

        /// <summary>Invokes the <see cref="OnUnmount"/> override.</summary>
        internal void InvokeOnUnmount() => OnUnmount();

        /// <summary>Invokes the <see cref="ShouldRender"/> override.</summary>
        /// <returns>False when the pending parent-driven render should be skipped.</returns>
        internal bool InvokeShouldRender() => ShouldRender();

        /// <summary>
        /// Flags the component as needing a render and queues it with the renderer.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The <see cref="Queued"/> flag collapses repeated updates in one pass into a
        /// single queue entry. A component without a host (unmounted, or never
        /// mounted) is flagged but not queued.
        /// </para>
        /// <para>
        /// When the host auto-flushes, this may render the component before returning,
        /// so a setter call can run effects synchronously.
        /// </para>
        /// </remarks>
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
    /// <typeparam name="TProps">
    /// The props payload. Use a reference type or a struct that identifies the
    /// component's inputs; value equality is not used by the runtime itself.
    /// </typeparam>
    /// <remarks>
    /// A null props payload is assigned as <c>default</c> rather than rejected, so
    /// a component node may carry no props at all. Passing a value of the wrong
    /// type throws during the diff.
    /// </remarks>
    public abstract class Component<TProps> : Component
    {
        /// <summary>The props supplied by the parent render.</summary>
        /// <value>
        /// The last payload assigned by the renderer, or <c>default</c> when the
        /// component was created without props.
        /// </value>
        /// <remarks>
        /// Replaced on every parent render, before <see cref="Component.ShouldRender"/>
        /// and <see cref="Component.Render"/> run.
        /// </remarks>
        public TProps Props { get; private set; } = default!;

        /// <inheritdoc/>
        /// <exception cref="ArgumentException">
        /// <paramref name="props"/> is neither <typeparamref name="TProps"/> nor null.
        /// </exception>
        /// <remarks>
        /// A null payload assigns <c>default</c> instead of throwing, which lets a
        /// component node carry no props. Any other mismatched type is rejected here,
        /// before the component renders.
        /// </remarks>
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
