namespace Ignite.UILab
{
    /// <summary>
    /// A mutable box that survives re-renders. Assigning <see cref="Value"/> does
    /// not trigger a render.
    /// </summary>
    public sealed class Ref<T>
    {
        public T? Value { get; set; }

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
    public sealed class Context<T>
    {
        public Context(T defaultValue)
        {
            DefaultValue = defaultValue;
        }

        public T DefaultValue { get; }
    }

    /// <summary>
    /// An immutable chain of context values captured at render time. A component
    /// keeps the scope it was rendered under, so a later state-driven render
    /// reads the same values even though the tree is no longer being walked.
    /// </summary>
    internal sealed class ContextScope
    {
        private readonly ContextScope? _parent;
        private readonly object _context;
        private readonly object? _value;

        internal ContextScope(ContextScope? parent, object context, object? value)
        {
            _parent = parent;
            _context = context;
            _value = value;
        }

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

    internal abstract class Hook
    {
    }

    internal sealed class StateHook<T> : Hook
    {
        internal T Value = default!;
        internal Action<T> Setter = null!;
    }

    internal sealed class ReducerHook<TState, TAction> : Hook
    {
        internal TState State = default!;
        internal Func<TState, TAction, TState> Reducer = null!;
        internal Action<TAction> Dispatch = null!;
    }

    internal sealed class EffectHook : Hook
    {
        internal object?[]? Deps;
        internal bool HasDeps;
        internal Action? Effect;
        internal Func<Action?>? EffectFactory;
        internal Action? Cleanup;
        internal bool Pending;
        internal bool IsLayout;
    }

    internal sealed class MemoHook<T> : Hook
    {
        internal T Value = default!;
        internal object?[]? Deps;
        internal bool HasDeps;
    }

    internal sealed class RefHook<T> : Hook
    {
        internal Ref<T> Reference = null!;
    }

    public abstract partial class Component
    {
        /// <summary>
        /// Declares a piece of state. The setter marks the component dirty and,
        /// when the renderer is auto-flushing, schedules a render.
        /// </summary>
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
        protected void Effect(Action effect)
        {
            EffectCore(effect, null, null, hasDeps: false, isLayout: false);
        }

        /// <summary>
        /// Runs an effect when any dependency changes. Pass an empty array to run
        /// it once after mount.
        /// </summary>
        protected void Effect(Action effect, params object?[] deps)
        {
            EffectCore(effect, null, deps, hasDeps: true, isLayout: false);
        }

        /// <summary>
        /// Runs an effect that can return a cleanup action after every render.
        /// </summary>
        protected void Effect(Func<Action?> effect)
        {
            EffectCore(null, effect, null, hasDeps: false, isLayout: false);
        }

        /// <summary>
        /// Runs an effect that can return a cleanup action when dependencies
        /// change.
        /// </summary>
        protected void Effect(Func<Action?> effect, params object?[] deps)
        {
            EffectCore(null, effect, deps, hasDeps: true, isLayout: false);
        }

        /// <summary>
        /// Like <see cref="Effect(Action)"/> but run synchronously during commit,
        /// before passive effects.
        /// </summary>
        protected void LayoutEffect(Action effect)
        {
            EffectCore(effect, null, null, hasDeps: false, isLayout: true);
        }

        protected void LayoutEffect(Action effect, params object?[] deps)
        {
            EffectCore(effect, null, deps, hasDeps: true, isLayout: true);
        }

        protected void LayoutEffect(Func<Action?> effect)
        {
            EffectCore(null, effect, null, hasDeps: false, isLayout: true);
        }

        protected void LayoutEffect(Func<Action?> effect, params object?[] deps)
        {
            EffectCore(null, effect, deps, hasDeps: true, isLayout: true);
        }

        /// <summary>
        /// Recomputes a value only when the dependencies change.
        /// </summary>
        protected T Memo<T>(Func<T> factory)
        {
            return MemoCore(factory, null, hasDeps: false);
        }

        /// <summary>
        /// Recomputes a value when any dependency changes. An empty dependency
        /// array computes the value once.
        /// </summary>
        protected T Memo<T>(Func<T> factory, params object?[] deps)
        {
            return MemoCore(factory, deps, hasDeps: true);
        }

        /// <summary>
        /// Returns the same delegate until the dependencies change.
        /// </summary>
        protected Action Callback(Action callback)
        {
            return Memo(() => callback);
        }

        protected Action Callback(Action callback, params object?[] deps)
        {
            return Memo(() => callback, deps);
        }

        protected Func<T> Callback<T>(Func<T> callback)
        {
            return Memo(() => callback);
        }

        protected Func<T> Callback<T>(Func<T> callback, params object?[] deps)
        {
            return Memo(() => callback, deps);
        }

        /// <summary>
        /// Declares a mutable ref that survives re-renders without causing one.
        /// </summary>
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
        protected T UseContext<T>(Context<T> context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return ContextScope is { } scope && scope.TryFind(context, out var value)
                ? value
                : context.DefaultValue;
        }

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
