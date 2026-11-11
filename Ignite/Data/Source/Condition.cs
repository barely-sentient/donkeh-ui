using Ignite.Parser.Objects;

namespace Ignite.Data
{
    /// <summary>
    /// A compiled condition predicate, evaluated against the current
    /// <see cref="RendererContext"/>.
    /// </summary>
    /// <param name="context">
    /// The environment snapshot the condition is tested against. It is passed by
    /// <see langword="readonly"/> reference so a condition cannot mutate the
    /// version counters used for caching.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the condition is satisfied and the gated rules
    /// may apply; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Implementations must be pure: the engine caches the result per
    /// <see cref="RendererContext.Version"/> and assumes the same context produces
    /// the same answer. The delegate is shared by every
    /// <see cref="Condition"/> that has the same prelude, so it must not capture
    /// state that differs between those conditions.
    /// </remarks>
    public delegate bool ConditionFunction(in RendererContext context);

    /// <summary>
    /// Compiles a CSS condition (for example <c>@media screen and (max-width: 768px)</c>)
    /// into an executable <see cref="ConditionFunction"/>.
    /// </summary>
    /// <param name="condition">
    /// The condition prelude tokens, including the leading at-keyword and running up
    /// to, but not including, the opening brace.
    /// </param>
    /// <returns>
    /// The compiled predicate. Returning <see langword="null"/> is treated as
    /// <see cref="Condition.AlwaysFalse"/>.
    /// </returns>
    /// <remarks>
    /// The callback is invoked once per distinct token sequence; conditions with the
    /// same prelude share the compiled executor even when they are nested under
    /// different parents. Exceptions other than <see cref="OutOfMemoryException"/>
    /// are caught and also fail closed.
    /// </remarks>
    public delegate ConditionFunction ConditionCompiler(ReadOnlySpan<Token> condition);

    /// <summary>
    /// A condition prelude together with its compiled executor and nesting parent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The prelude tokens are not stored per condition. They live once in the
    /// owning <see cref="DataTable"/>'s flat token arena and are reachable through
    /// <see cref="DataTable.GetConditionTokens"/>.
    /// </para>
    /// <para>
    /// Conditions form a tree. Each condition points at its enclosing condition
    /// through <see cref="ParentId"/>; evaluation walks that chain and stops at
    /// the first failure, so a nested rule only applies when every enclosing
    /// condition also holds. Callers always pass an already stored parent id, so
    /// the chain cannot cycle.
    /// </para>
    /// <para>
    /// Instances are values created by the table and exposed read-only through
    /// <see cref="DataTable.Conditions"/> and <see cref="DataTable.GetCondition"/>.
    /// Use <see cref="DataTable.GetOrAddCondition"/> rather than constructing the
    /// struct directly, because a <c>default</c> value leaves
    /// <see cref="Executor"/> <see langword="null"/>.
    /// </para>
    /// </remarks>
    /// <seealso cref="ConditionFunction"/>
    /// <seealso cref="ConditionCompiler"/>
    public readonly struct Condition
    {
        /// <summary>
        /// Initializes a placeholder condition that never matches and has no parent.
        /// </summary>
        /// <remarks>
        /// The explicit parameterless constructor is a safe placeholder only: it
        /// sets <see cref="Executor"/> to <see cref="AlwaysFalse"/> and both ids to
        /// -1. Real conditions are created by
        /// <see cref="DataTable.GetOrAddCondition"/>. Note that
        /// <c>default(Condition)</c> bypasses this constructor and therefore has a
        /// <see langword="null"/> executor.
        /// </remarks>
        public Condition()
        {
            Executor = AlwaysFalse;
            ParentId = -1;
            ExecutorId = -1;
        }

        /// <summary>
        /// Gets the compiled predicate for this condition's prelude.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the same delegate instance for every condition that shares a
        /// prelude, even when their parent chains differ; <see cref="ExecutorId"/>
        /// identifies that shared slot. Because the delegate is shared, it must
        /// depend only on the environment and on the prelude text.
        /// </para>
        /// <para>
        /// A compiler that throws, or returns <see langword="null"/>, is replaced
        /// by <see cref="AlwaysFalse"/> so a broken condition fails closed.
        /// </para>
        /// </remarks>
        public required ConditionFunction Executor { get; init; }

        /// <summary>
        /// The condition this one is nested inside, or -1 when it is top level.
        /// Evaluation walks this chain, so every ancestor must succeed.
        /// </summary>
        public int ParentId { get; init; } = -1;

        /// <summary>
        /// The shared executor id. Different parent chains with the same prelude
        /// point at the same executor.
        /// </summary>
        public int ExecutorId { get; init; }

        /// <summary>
        /// A condition that always evaluates to true.
        /// </summary>
        public static readonly ConditionFunction AlwaysTrue = static (in RendererContext _) => true;

        /// <summary>
        /// A condition that always evaluates to false. Used when no compiler is
        /// supplied or when a compiler cannot handle a prelude, so unsupported
        /// conditions fail closed rather than applying styles incorrectly.
        /// </summary>
        public static readonly ConditionFunction AlwaysFalse = static (in RendererContext _) => false;

        /// <summary>
        /// Compiler that treats every condition as true. Useful for exercising
        /// the rest of the pipeline without an IL generator.
        /// </summary>
        public static readonly ConditionCompiler AlwaysTrueCompiler = static _ => AlwaysTrue;

        /// <summary>
        /// Compiler that treats every condition as false. This is the default
        /// when no compiler is supplied, so gated rules are not applied by
        /// accident.
        /// </summary>
        public static readonly ConditionCompiler AlwaysFalseCompiler = static _ => AlwaysFalse;
    }
}
