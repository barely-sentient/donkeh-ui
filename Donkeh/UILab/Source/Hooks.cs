namespace Donkeh.UILab
{
    /// <summary>
    /// A mutable box that survives re-renders. Assigning <see cref="Value"/> does
    /// not trigger a render.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ref is the escape hatch for values that must persist across renders without
    /// being inputs to the render. It is created once by <see cref="Component.UseRef{T}"/>
    /// and the same instance is returned on every render, so writing to it never
    /// schedules work. Read it from effects or event handlers rather than during
    /// render when the value feeds layout.
    /// </para>
    /// <para>
    /// Element refs use the same type: <see cref="ElementNode.BindRef"/> points a
    /// <c>Ref&lt;ElementNode&gt;</c> at the mounted element and clears it on unmount.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The boxed value type.</typeparam>
    /// <seealso cref="Component.UseRef{T}"/>
    public sealed class Ref<T>
    {
        /// <summary>Gets or sets the current value.</summary>
        /// <value>
        /// The stored value, or <c>default</c> when it was never assigned. The
        /// property is intentionally nullable because a ref starts empty.
        /// </value>
        /// <remarks>
        /// The setter is a plain field write: it does not bump any revision, notify a
        /// component or schedule a render.
        /// </remarks>
        public T? Value { get; set; }

        /// <summary>
        /// Gets or sets the current value. Alias of <see cref="Value"/> for callers
        /// that prefer the browser-style name.
        /// </summary>
        /// <value>The same storage as <see cref="Value"/>.</value>
        /// <remarks>
        /// The two properties are fully interchangeable; neither has extra semantics.
        /// </remarks>
        public T? Current
        {
            get => Value;
            set => Value = value;
        }
    }

    /// <summary>
    /// A typed context. Providers publish a value and consumers read the nearest
    /// one through <see cref="Component.UseContext{T}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A context is identified by its object identity, not by its type or default
    /// value, so keep one static instance per piece of shared state. The default is
    /// returned whenever no provider is in scope above the consuming component.
    /// </para>
    /// <para>
    /// Context values are captured per render. Republishing a different value does
    /// not re-render existing consumers; they see the new value the next time they
    /// render for some other reason.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The published value type.</typeparam>
    /// <seealso cref="Component.UseContext{T}"/>
    public sealed class Context<T>
    {
        /// <summary>
        /// Creates a context with the value used outside any provider.
        /// </summary>
        /// <param name="defaultValue">
        /// The value returned when no matching provider is in scope.
        /// </param>
        public Context(T defaultValue)
        {
            DefaultValue = defaultValue;
        }

        /// <summary>Gets the value returned when no provider supplies one.</summary>
        /// <value>
        /// The value passed to the constructor. It is fixed for the lifetime of the
        /// context.
        /// </value>
        public T DefaultValue { get; }
    }

    /// <summary>
    /// An immutable chain of context values captured at render time. A component
    /// keeps the scope it was rendered under, so a later state-driven render
    /// reads the same values even though the tree is no longer being walked.
    /// </summary>
    /// <remarks>
    /// Scopes form a singly linked list with the innermost scope at the head.
    /// Lookups walk outward and return the first scope that publishes the exact
    /// context instance, which is what gives nested providers their nearest-wins
    /// behaviour. A scope and its value are never mutated after construction.
    /// </remarks>
    internal sealed class ContextScope
    {
        /// <summary>The enclosing scope, or null at the top of the tree.</summary>
        private readonly ContextScope? _parent;

        /// <summary>
        /// The context object this scope publishes. It is compared by reference.
        /// </summary>
        private readonly object _context;

        /// <summary>The published value, untyped until a consumer reads it.</summary>
        private readonly object? _value;

        /// <summary>
        /// Creates a scope that publishes <paramref name="value"/> for
        /// <paramref name="context"/> inside <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">The enclosing scope, or null at the root.</param>
        /// <param name="context">The context object being published.</param>
        /// <param name="value">The published value, which may be null.</param>
        internal ContextScope(ContextScope? parent, object context, object? value)
        {
            _parent = parent;
            _context = context;
            _value = value;
        }

        /// <summary>
        /// Finds the nearest published value for a context.
        /// </summary>
        /// <typeparam name="T">The context's value type.</typeparam>
        /// <param name="context">The context instance to look for.</param>
        /// <param name="value">
        /// The published value when found; otherwise <c>default</c>.
        /// </param>
        /// <returns>True when a scope in the chain publishes this context.</returns>
        /// <remarks>
        /// A value of the wrong runtime type (or null) is returned as <c>default</c>
        /// rather than throwing, so a mismatched provider silently degrades to the
        /// zero value instead of failing the render.
        /// </remarks>
        internal bool TryFind<T>(Context<T> context, out T value)
        {
            for (var scope = this; scope is not null; scope = scope._parent)
            {
                if (ReferenceEquals(scope._context, context))
                {
                    value = scope._value is T typed ? typed : default!;
                    return true;
                }
            }

            value = default!;
            return false;
        }
    }

    /// <summary>
    /// The base of a hook slot. Slots are stored positionally in
    /// <see cref="Component.Hooks"/> and typed by their concrete subclass; the
    /// runtime type is the hook's identity.
    /// </summary>
    internal abstract class Hook
    {
    }

    /// <summary>
    /// Backing slot for <see cref="Component.State{T}(T)"/>.
    /// </summary>
    /// <typeparam name="T">The state value type.</typeparam>
    internal sealed class StateHook<T> : Hook
    {
        /// <summary>The current value, updated in place by the setter.</summary>
        internal T Value = default!;

        /// <summary>
        /// The setter handed to the caller. It is created once so the same delegate
        /// identity is returned on every render.
        /// </summary>
        internal Action<T> Setter = null!;
    }

    /// <summary>
    /// Backing slot for <see cref="Component.Reducer{TState, TAction}"/>.
    /// </summary>
    /// <typeparam name="TState">The state value type.</typeparam>
    /// <typeparam name="TAction">The dispatched action type.</typeparam>
    internal sealed class ReducerHook<TState, TAction> : Hook
    {
        /// <summary>The current reducer state.</summary>
        internal TState State = default!;

        /// <summary>
        /// The reducer from the latest render. Replaced on every render so a reducer
        /// that closes over props sees current values.
        /// </summary>
        internal Func<TState, TAction, TState> Reducer = null!;

        /// <summary>
        /// The dispatcher handed to the caller, created once per slot.
        /// </summary>
        internal Action<TAction> Dispatch = null!;
    }

    /// <summary>
    /// Backing slot for every <see cref="Component.Effect(Action)"/> and
    /// <see cref="Component.LayoutEffect(Action)"/> overload.
    /// </summary>
    internal sealed class EffectHook : Hook
    {
        /// <summary>
        /// The dependency array from the last run, or null when the effect has no
        /// dependency list.
        /// </summary>
        internal object?[]? Deps;

        /// <summary>
        /// True when <see cref="Deps"/> is meaningful. Without dependencies the effect
        /// runs after every render.
        /// </summary>
        internal bool HasDeps;

        /// <summary>The effect body for the action overloads, or null for factories.</summary>
        internal Action? Effect;

        /// <summary>
        /// The effect body for the factory overloads. Its return value becomes
        /// <see cref="Cleanup"/>.
        /// </summary>
        internal Func<Action?>? EffectFactory;

        /// <summary>
        /// The cleanup returned by the last factory run, or registered by an action
        /// effect through the factory shape. Invoked before the next run and on
        /// unmount.
        /// </summary>
        internal Action? Cleanup;

        /// <summary>
        /// True when the effect must run in the next commit because it is new or its
        /// dependencies changed.
        /// </summary>
        internal bool Pending;

        /// <summary>
        /// True for layout effects, which run before passive effects in a commit.
        /// </summary>
        internal bool IsLayout;
    }

    /// <summary>
    /// Backing slot for <see cref="Component.Memo{T}(Func{T})"/>.
    /// </summary>
    /// <typeparam name="T">The memoised value type.</typeparam>
    internal sealed class MemoHook<T> : Hook
    {
        /// <summary>The value produced by the last factory run.</summary>
        internal T Value = default!;

        /// <summary>The dependency array from the last computation, or null.</summary>
        internal object?[]? Deps;

        /// <summary>True when <see cref="Deps"/> is meaningful.</summary>
        internal bool HasDeps;
    }

    /// <summary>
    /// Backing slot for <see cref="Component.UseRef{T}"/>.
    /// </summary>
    /// <typeparam name="T">The boxed value type.</typeparam>
    internal sealed class RefHook<T> : Hook
    {
        /// <summary>
        /// The box returned to the caller. Created once per slot and never replaced,
        /// so its identity is stable across renders.
        /// </summary>
        internal Ref<T> Reference = null!;
    }

    public abstract partial class Component
    {
        /// <summary>
        /// Declares a piece of state. The setter marks the component dirty and,
        /// when the renderer is auto-flushing, schedules a render.
        /// </summary>
        /// <typeparam name="T">The state value type.</typeparam>
        /// <param name="initial">The value used on the first render.</param>
        /// <returns>
        /// The current value and a setter. The tuple is rebuilt each render but the
        /// setter delegate identity is stable.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed since
        /// the previous render.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The setter compares the new value with the current one using
        /// <c>EqualityComparer&lt;T&gt;.Default</c>; assigning an equal value is a
        /// no-op and does not schedule a render. State is replaced, never merged, so
        /// mutate-and-assign for reference types.
        /// </para>
        /// <para>
        /// The slot is identified by call order, not by name. Calling this
        /// conditionally shifts every later hook and throws on the next render.
        /// </para>
        /// </remarks>
        protected (T Value, Action<T> Set) State<T>(T initial)
        {
            var hook = GetHook<StateHook<T>>(out var created);

            if (created)
            {
                hook.Value = initial;
                hook.Setter = value => SetState(hook, value);
            }

            return (hook.Value, hook.Setter);
        }

        /// <summary>
        /// Declares state with a lazily evaluated initial value. The factory runs
        /// only on the first render.
        /// </summary>
        /// <typeparam name="T">The state value type.</typeparam>
        /// <param name="initial">
        /// Produces the initial value. Invoked once, on the render that creates the
        /// hook slot, and never again.
        /// </param>
        /// <returns>The current value and a setter.</returns>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        /// <remarks>
        /// Use this when the initial value is expensive to build; the delegate is not
        /// called on later renders.
        /// </remarks>
        protected (T Value, Action<T> Set) State<T>(Func<T> initial)
        {
            var hook = GetHook<StateHook<T>>(out var created);

            if (created)
            {
                hook.Value = initial();
                hook.Setter = value => SetState(hook, value);
            }

            return (hook.Value, hook.Setter);
        }

        /// <summary>
        /// Declares state and also returns a functional updater, which is useful
        /// when the next value depends on the current one inside a callback:
        /// <code>
        /// var (count, setCount, updateCount) = StateWithUpdate(0);
        /// updateCount(c => c + 1);
        /// </code>
        /// </summary>
        /// <typeparam name="T">The state value type.</typeparam>
        /// <param name="initial">The value used on the first render.</param>
        /// <returns>
        /// The current value, a direct setter, and an updater that receives the
        /// current value and returns the next one.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        /// <remarks>
        /// The updater reads the value at call time rather than at render time, so it
        /// is safe to call several times in a row within one event handler. It still
        /// compares by equality and skips the render when the result is unchanged.
        /// </remarks>
        protected (T Value, Action<T> Set, Action<Func<T, T>> Update) StateWithUpdate<T>(T initial)
        {
            var hook = GetHook<StateHook<T>>(out var created);

            if (created)
            {
                hook.Value = initial;
                hook.Setter = value => SetState(hook, value);
            }

            return (hook.Value, hook.Setter, updater => SetState(hook, updater(hook.Value)));
        }

        /// <summary>
        /// Declares state with a lazy initial value and a functional updater.
        /// </summary>
        /// <typeparam name="T">The state value type.</typeparam>
        /// <param name="initial">Produces the initial value once, on first render.</param>
        /// <returns>
        /// The current value, a direct setter, and an updater that receives the
        /// current value and returns the next one.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        protected (T Value, Action<T> Set, Action<Func<T, T>> Update) StateWithUpdate<T>(Func<T> initial)
        {
            var hook = GetHook<StateHook<T>>(out var created);

            if (created)
            {
                hook.Value = initial();
                hook.Setter = value => SetState(hook, value);
            }

            return (hook.Value, hook.Setter, updater => SetState(hook, updater(hook.Value)));
        }

        /// <summary>
        /// Declares a reducer and returns its current state plus a dispatcher.
        /// </summary>
        /// <typeparam name="TState">The state value type.</typeparam>
        /// <typeparam name="TAction">The action type the dispatcher accepts.</typeparam>
        /// <param name="initial">The state used on the first render.</param>
        /// <param name="reducer">
        /// Computes the next state from the current state and an action. The latest
        /// delegate is captured on every render, so it may close over fresh props.
        /// </param>
        /// <returns>The current state and a dispatcher for actions.</returns>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        /// <remarks>
        /// The dispatcher runs the reducer synchronously and only schedules a render
        /// when the result differs from the current state by
        /// <c>EqualityComparer&lt;TState&gt;.Default</c>. Because it needs no value
        /// from the render scope, the dispatcher is a stable delegate, but the reducer
        /// is reassigned every render.
        /// </remarks>
        protected (TState State, Action<TAction> Dispatch) Reducer<TState, TAction>(
            TState initial,
            Func<TState, TAction, TState> reducer)
        {
            var hook = GetHook<ReducerHook<TState, TAction>>(out var created);

            if (created)
            {
                hook.State = initial;
                hook.Dispatch = action => DispatchReducer(hook, action);
            }

            hook.Reducer = reducer;
            return (hook.State, hook.Dispatch);
        }

        /// <summary>
        /// Runs an effect after every render.
        /// </summary>
        /// <param name="effect">The effect body. It cannot return a cleanup.</param>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        /// <remarks>
        /// The effect is scheduled during render and runs after the tree has been
        /// applied. Because no dependency list is supplied it runs on every commit,
        /// including the first one. Use the factory overload to register a cleanup.
        /// </remarks>
        protected void Effect(Action effect)
        {
            EffectCore(effect, null, null, hasDeps: false, isLayout: false);
        }

        /// <summary>
        /// Runs an effect when any dependency changes. Pass an empty array to run
        /// it once after mount.
        /// </summary>
        /// <param name="effect">The effect body. It cannot return a cleanup.</param>
        /// <param name="deps">
        /// The dependency values. Elements are compared with
        /// <c>EqualityComparer&lt;object?&gt;.Default</c>; the array is compared by
        /// contents, not identity.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        /// <remarks>
        /// A zero-length list has no elements to differ, so the effect runs exactly
        /// once. Do not mutate the array after the call: it is stored by reference
        /// and the next render compares against its current contents. Null elements
        /// are legal and compare equal to one another.
        /// </remarks>
        protected void Effect(Action effect, params object?[] deps)
        {
            EffectCore(effect, null, deps, hasDeps: true, isLayout: false);
        }

        /// <summary>
        /// Runs an effect that can return a cleanup action after every render.
        /// </summary>
        /// <param name="effect">
        /// The effect body. Its return value is stored and invoked before the next
        /// run and when the component unmounts.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        /// <remarks>
        /// Returning <see langword="null"/> means there is nothing to clean up.
        /// Cleanup runs in the commit phase, never during render.
        /// </remarks>
        protected void Effect(Func<Action?> effect)
        {
            EffectCore(null, effect, null, hasDeps: false, isLayout: false);
        }

        /// <summary>
        /// Runs an effect that can return a cleanup action when dependencies
        /// change.
        /// </summary>
        /// <param name="effect">The effect body, returning an optional cleanup.</param>
        /// <param name="deps">The dependency values, compared element by element.</param>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        /// <remarks>
        /// The previous cleanup, if any, runs before the new body. An empty array
        /// therefore runs the body once and the cleanup once at unmount.
        /// </remarks>
        protected void Effect(Func<Action?> effect, params object?[] deps)
        {
            EffectCore(null, effect, deps, hasDeps: true, isLayout: false);
        }

        /// <summary>
        /// Like <see cref="Effect(Action)"/> but run synchronously during commit,
        /// before passive effects.
        /// </summary>
        /// <param name="effect">The effect body. It cannot return a cleanup.</param>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        /// <remarks>
        /// Layout effects run after the tree is patched but before passive effects,
        /// which makes them suitable for measuring or adjusting the just-applied
        /// output. A layout effect that updates state adds another pass to the
        /// current flush.
        /// </remarks>
        protected void LayoutEffect(Action effect)
        {
            EffectCore(effect, null, null, hasDeps: false, isLayout: true);
        }

        /// <summary>
        /// Runs a layout effect when any dependency changes.
        /// </summary>
        /// <param name="effect">The effect body. It cannot return a cleanup.</param>
        /// <param name="deps">The dependency values, compared element by element.</param>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        protected void LayoutEffect(Action effect, params object?[] deps)
        {
            EffectCore(effect, null, deps, hasDeps: true, isLayout: true);
        }

        /// <summary>
        /// Runs a layout effect that can return a cleanup action after every render.
        /// </summary>
        /// <param name="effect">The effect body, returning an optional cleanup.</param>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        protected void LayoutEffect(Func<Action?> effect)
        {
            EffectCore(null, effect, null, hasDeps: false, isLayout: true);
        }

        /// <summary>
        /// Runs a layout effect that can return a cleanup action when dependencies
        /// change.
        /// </summary>
        /// <param name="effect">The effect body, returning an optional cleanup.</param>
        /// <param name="deps">The dependency values, compared element by element.</param>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        protected void LayoutEffect(Func<Action?> effect, params object?[] deps)
        {
            EffectCore(null, effect, deps, hasDeps: true, isLayout: true);
        }

        /// <summary>
        /// Computes a value on every render and stores it in a hook slot.
        /// </summary>
        /// <typeparam name="T">The memoised value type.</typeparam>
        /// <param name="factory">Produces the value when a recomputation is needed.</param>
        /// <returns>
        /// The cached value, recomputed on every render because this overload has no
        /// dependencies to compare.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        /// <remarks>
        /// Without a dependency list there is no key, so the factory runs each render
        /// and the result is simply stored. Use the overload that takes dependencies
        /// to actually cache a value.
        /// </remarks>
        protected T Memo<T>(Func<T> factory)
        {
            return MemoCore(factory, null, hasDeps: false);
        }

        /// <summary>
        /// Recomputes a value when any dependency changes. An empty dependency
        /// array computes the value once.
        /// </summary>
        /// <typeparam name="T">The memoised value type.</typeparam>
        /// <param name="factory">
        /// Produces the value. Called on the first render and whenever the
        /// dependencies differ.
        /// </param>
        /// <param name="deps">
        /// The dependency values, compared element by element with
        /// <c>EqualityComparer&lt;object?&gt;.Default</c>.
        /// </param>
        /// <returns>The cached or newly computed value.</returns>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        /// <remarks>
        /// The dependency array is stored by reference, so do not mutate it after the
        /// call. The factory is expected to be pure; the runtime does not roll back a
        /// partially computed value if it throws.
        /// </remarks>
        protected T Memo<T>(Func<T> factory, params object?[] deps)
        {
            return MemoCore(factory, deps, hasDeps: true);
        }

        /// <summary>
        /// Returns the callback as a delegate; without dependencies it is recreated on
        /// every render.
        /// </summary>
        /// <param name="callback">The delegate to memoise.</param>
        /// <returns>
        /// A new delegate on every render, because this overload has no dependencies
        /// to compare.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        /// <remarks>
        /// This is shorthand for <see cref="Memo{T}(Func{T})"/> over a constant
        /// closure. Pass dependencies through the other overload if a stable identity
        /// matters to a child component's props comparison.
        /// </remarks>
        protected Action Callback(Action callback)
        {
            return Memo(() => callback);
        }

        /// <summary>
        /// Returns the same delegate until the dependencies change.
        /// </summary>
        /// <param name="callback">The delegate to memoise.</param>
        /// <param name="deps">The dependency values, compared element by element.</param>
        /// <returns>The cached delegate, or a new one when the dependencies differ.</returns>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        protected Action Callback(Action callback, params object?[] deps)
        {
            return Memo(() => callback, deps);
        }

        /// <summary>
        /// Returns the typed callback as a delegate; without dependencies it is
        /// recreated on every render.
        /// </summary>
        /// <typeparam name="T">The delegate's return type.</typeparam>
        /// <param name="callback">The delegate to memoise.</param>
        /// <returns>A new delegate on every render, because no dependencies are given.</returns>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        protected Func<T> Callback<T>(Func<T> callback)
        {
            return Memo(() => callback);
        }

        /// <summary>
        /// Returns the same typed delegate until the dependencies change.
        /// </summary>
        /// <typeparam name="T">The delegate's return type.</typeparam>
        /// <param name="callback">The delegate to memoise.</param>
        /// <param name="deps">The dependency values, compared element by element.</param>
        /// <returns>The cached delegate, or a new one when the dependencies differ.</returns>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        protected Func<T> Callback<T>(Func<T> callback, params object?[] deps)
        {
            return Memo(() => callback, deps);
        }

        /// <summary>
        /// Declares a mutable ref that survives re-renders without causing one.
        /// </summary>
        /// <typeparam name="T">The boxed value type.</typeparam>
        /// <param name="initial">The value stored on the first render only.</param>
        /// <returns>
        /// The same <see cref="Ref{T}"/> instance on every render. Writes to it never
        /// schedule a render.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Called outside <see cref="Render"/>, or the hook call order changed.
        /// </exception>
        /// <remarks>
        /// The initial value is ignored after the first render, so changing it later
        /// has no effect. Use this for timers, subscriptions and element handles, not
        /// for values that should appear in the output.
        /// </remarks>
        protected Ref<T> UseRef<T>(T? initial = default)
        {
            var hook = GetHook<RefHook<T>>(out var created);

            if (created)
            {
                hook.Reference = new Ref<T> { Value = initial };
            }

            return hook.Reference;
        }

        /// <summary>
        /// Reads the nearest provider value, or the context default.
        /// </summary>
        /// <typeparam name="T">The context's value type.</typeparam>
        /// <param name="context">The context to read.</param>
        /// <returns>
        /// The nearest provided value, or <see cref="Context{T}.DefaultValue"/> when
        /// no provider is in scope.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/> is null.
        /// </exception>
        /// <remarks>
        /// The value is resolved from the scope captured when this component last
        /// rendered, not from the live tree. A component that has never rendered has
        /// no scope and gets the default.
        /// </remarks>
        protected T UseContext<T>(Context<T> context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return ContextScope is { } scope && scope.TryFind(context, out var value)
                ? value
                : context.DefaultValue;
        }

        /// <summary>
        /// Returns the hook slot at the current cursor position, creating it when the
        /// cursor is past the end of the list.
        /// </summary>
        /// <typeparam name="T">The concrete hook slot type expected at this position.</typeparam>
        /// <param name="created">
        /// True when a new slot was appended, meaning the caller must initialise it.
        /// </param>
        /// <returns>The existing or newly created slot.</returns>
        /// <exception cref="InvalidOperationException">
        /// Called outside the render window, or the slot at this position holds a
        /// different hook type because the call order changed.
        /// </exception>
        /// <remarks>
        /// <para>
        /// This is the enforcement point for the rules of hooks. The cursor advances on
        /// every call, so the nth hook of a render always maps to the nth slot. A type
        /// mismatch means a hook was added, removed or moved between renders.
        /// </para>
        /// <para>
        /// Slots are append-only: an existing slot is never replaced, which is what
        /// preserves state across renders.
        /// </para>
        /// </remarks>
        private T GetHook<T>(out bool created) where T : Hook, new()
        {
            if (!IsRendering)
            {
                throw new InvalidOperationException(
                    "Hooks can only be called from Render().");
            }

            Hooks ??= new List<Hook>();

            if (HookIndex == Hooks.Count)
            {
                var hook = new T();
                Hooks.Add(hook);
                HookIndex++;
                created = true;
                return hook;
            }

            if (Hooks[HookIndex] is not T existing)
            {
                throw new InvalidOperationException(
                    "The hook order changed between renders. Hooks must be called unconditionally and in the same order.");
            }

            HookIndex++;
            created = false;
            return existing;
        }

        /// <summary>
        /// Applies a state value and marks the component dirty when it differs from
        /// the current one.
        /// </summary>
        /// <typeparam name="T">The state value type.</typeparam>
        /// <param name="hook">The slot to update.</param>
        /// <param name="value">The new value.</param>
        /// <exception cref="InvalidOperationException">
        /// Called while the component is rendering.
        /// </exception>
        /// <remarks>
        /// Equality is checked before the write, so assigning the same value is a
        /// no-op and does not queue work. The guard forbids setters during render
        /// because a render must be a pure function of the captured state.
        /// </remarks>
        private void SetState<T>(StateHook<T> hook, T value)
        {
            if (IsRendering)
            {
                throw new InvalidOperationException(
                    "State cannot be updated while the component is rendering.");
            }

            if (EqualityComparer<T>.Default.Equals(hook.Value, value))
            {
                return;
            }

            hook.Value = value;
            MarkDirty();
        }

        /// <summary>
        /// Runs the reducer and marks the component dirty when the state changes.
        /// </summary>
        /// <typeparam name="TState">The state value type.</typeparam>
        /// <typeparam name="TAction">The action value type.</typeparam>
        /// <param name="hook">The reducer slot to update.</param>
        /// <param name="action">The action dispatched by the caller.</param>
        /// <exception cref="InvalidOperationException">
        /// Called while the component is rendering.
        /// </exception>
        /// <remarks>
        /// The reducer runs synchronously on the calling thread and its result is
        /// compared with the current state by equality. An unchanged result still
        /// counts as a completed dispatch but does not queue a render.
        /// </remarks>
        private void DispatchReducer<TState, TAction>(
            ReducerHook<TState, TAction> hook,
            TAction action)
        {
            if (IsRendering)
            {
                throw new InvalidOperationException(
                    "State cannot be updated while the component is rendering.");
            }

            var next = hook.Reducer(hook.State, action);

            if (EqualityComparer<TState>.Default.Equals(hook.State, next))
            {
                return;
            }

            hook.State = next;
            MarkDirty();
        }

        /// <summary>
        /// Shared implementation of every effect overload.
        /// </summary>
        /// <param name="effect">The action body, or null for factory overloads.</param>
        /// <param name="factory">The factory body, or null for action overloads.</param>
        /// <param name="deps">The dependency array, or null when there is none.</param>
        /// <param name="hasDeps">True when <paramref name="deps"/> should be compared.</param>
        /// <param name="isLayout">True for layout effects, false for passive effects.</param>
        /// <remarks>
        /// The body, factory and phase are updated on every render, so an effect always
        /// runs the latest closure. The pending flag is only set when the slot is new,
        /// has no dependency list, or the dependencies differ. Cleanup stays untouched
        /// until the commit phase runs the effect.
        /// </remarks>
        private void EffectCore(
            Action? effect,
            Func<Action?>? factory,
            object?[]? deps,
            bool hasDeps,
            bool isLayout)
        {
            var hook = GetHook<EffectHook>(out var created);

            hook.Effect = effect;
            hook.EffectFactory = factory;
            hook.IsLayout = isLayout;

            if (created || !hasDeps || !DepsEqual(hook.Deps, deps))
            {
                hook.Pending = true;
                hook.Deps = deps;
                hook.HasDeps = hasDeps;
            }
        }

        /// <summary>
        /// Shared implementation of every memo overload.
        /// </summary>
        /// <typeparam name="T">The memoised value type.</typeparam>
        /// <param name="factory">Produces a value when a recomputation is needed.</param>
        /// <param name="deps">The dependency array, or null when there is none.</param>
        /// <param name="hasDeps">True when <paramref name="deps"/> should be compared.</param>
        /// <returns>The cached or newly computed value.</returns>
        /// <remarks>
        /// A slot with no dependency list recomputes on every render by design. The
        /// computed value is stored before returning, so a factory that throws leaves
        /// the previous value in place.
        /// </remarks>
        private T MemoCore<T>(Func<T> factory, object?[]? deps, bool hasDeps)
        {
            var hook = GetHook<MemoHook<T>>(out var created);

            if (created || !hasDeps || !DepsEqual(hook.Deps, deps))
            {
                hook.Value = factory();
                hook.Deps = deps;
                hook.HasDeps = hasDeps;
            }

            return hook.Value;
        }

        /// <summary>
        /// Compares two dependency arrays element by element.
        /// </summary>
        /// <param name="previous">The stored dependencies, or null.</param>
        /// <param name="current">The new dependencies, or null.</param>
        /// <returns>True when both arrays are non-null, the same length and equal.</returns>
        /// <remarks>
        /// Null on either side counts as a change, which is why creating a slot is also
        /// treated as pending by the callers. Values are compared with
        /// <c>EqualityComparer&lt;object?&gt;.Default</c>, so value types box and
        /// reference types use their own equality.
        /// </remarks>
        private static bool DepsEqual(object?[]? previous, object?[]? current)
        {
            if (previous is null || current is null || previous.Length != current.Length)
            {
                return false;
            }

            for (var i = 0; i < previous.Length; i++)
            {
                if (!EqualityComparer<object?>.Default.Equals(previous[i], current[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
