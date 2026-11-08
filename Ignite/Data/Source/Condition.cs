using Ignite.Parser.Objects;

namespace Ignite.Data
{
    public delegate bool ConditionFunction(in RendererContext context);

    /// <summary>
    /// Compiles a CSS condition (for example <c>@media screen and (max-width: 768px)</c>)
    /// into an executable <see cref="ConditionFunction"/>.
    /// </summary>
    /// <remarks>
    /// The supplied tokens include the leading at-keyword and run up to, but do not
    /// include, the opening brace. The callback is invoked once per distinct token
    /// sequence; conditions with the same prelude share the compiled executor even
    /// when they are nested under different parents.
    /// </remarks>
    public delegate ConditionFunction ConditionCompiler(ReadOnlySpan<Token> condition);

    /// <summary>
    /// A condition prelude together with its compiled executor and nesting parent.
    /// </summary>
    /// <remarks>
    /// The prelude tokens are not stored per condition. They live once in the
    /// owning <see cref="DataTable"/>'s flat token arena and are reachable through
    /// <see cref="DataTable.GetConditionTokens"/>.
    /// </remarks>
    public readonly struct Condition
    {
        public Condition()
        {
            Executor = AlwaysFalse;
            ParentId = -1;
            ExecutorId = -1;
        }

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
