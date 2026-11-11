using System.Runtime.InteropServices;
using Ignite.Data;
using Ignite.Parser;
using Ignite.Parser.Objects;

namespace Ignite.DataEngine
{
    /// <summary>
    /// Turns a token stream produced by <see cref="Tokenizer"/> into a flat
    /// <see cref="DataTable"/> of interned selectors, rules and conditions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine is a single forward pass over the token list: it never rewrites
    /// the source and never keeps a reference to the input after
    /// <see cref="Build(List{Token})"/> returns. Selector and condition token
    /// sequences are copied into the table's flat arenas, while property names,
    /// values and at-rule preludes are interned in <see cref="InternTable"/>.
    /// </para>
    /// <para>
    /// An instance is immutable after construction and may be reused for any
    /// number of stylesheets, including concurrently, provided the supplied
    /// <see cref="ConditionCompiler"/> is thread-safe and each call targets its own
    /// <see cref="DataTable"/>. The table itself is mutable and not thread-safe.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var engine = new DataEngine(MediaConditionCompiler.Instance);
    /// var table = engine.Build("@media (min-width: 768px) { .a { color: red; } }");
    /// </code>
    /// </example>
    public sealed class DataEngine
    {
        /// <summary>
        /// Compiler invoked for every condition prelude. Never <see langword="null"/>;
        /// the constructor substitutes <see cref="Condition.AlwaysFalseCompiler"/>
        /// when none is supplied.
        /// </summary>
        private readonly ConditionCompiler _conditionCompiler;

        /// <summary>
        /// Initialises a new engine that uses <paramref name="conditionCompiler"/>
        /// to turn condition preludes into executable predicates.
        /// </summary>
        /// <param name="conditionCompiler">
        /// The compiler for conditions. When omitted, every condition is
        /// treated as false so rules guarded by an unsupported condition are not
        /// applied. Pass <see cref="Condition.AlwaysTrueCompiler"/> to exercise the
        /// pipeline without condition filtering, or
        /// <see cref="MediaConditionCompiler.Instance"/> to evaluate media queries.
        /// </param>
        public DataEngine(ConditionCompiler? conditionCompiler = null)
        {
            _conditionCompiler = conditionCompiler ?? Condition.AlwaysFalseCompiler;
        }

        /// <summary>
        /// Tokenizes <paramref name="source"/> and builds a new table from it.
        /// </summary>
        /// <param name="source">The CSS text to parse.</param>
        /// <returns>A new table containing the stylesheet's interned data.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="source"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown by <see cref="Tokenizer"/> when the source is longer than
        /// 16,777,215 characters, the maximum addressable by a packed
        /// <see cref="Token"/>.
        /// </exception>
        /// <remarks>
        /// This is the convenience path for a single stylesheet. Use
        /// <see cref="Build(List{Token}, DataTable)"/> to append several
        /// stylesheets into one lookup structure without re-tokenizing.
        /// </remarks>
        public DataTable Build(string source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return Build(Tokenizer.Parse(source));
        }

        /// <summary>
        /// Builds a new table from an existing token list.
        /// </summary>
        /// <param name="tokens">
        /// Tokens produced by <see cref="Tokenizer"/>. The list is only read and
        /// is not retained by the returned table.
        /// </param>
        /// <returns>A new table containing the parsed stylesheet.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="tokens"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// The table is pre-sized from the token count to avoid most growth
        /// allocations; the estimate only affects capacity, not behaviour.
        /// </remarks>
        public DataTable Build(List<Token> tokens)
        {
            ArgumentNullException.ThrowIfNull(tokens);

            // A rough pre-size avoids most of the growth allocations without
            // retaining a per-token buffer.
            var table = new DataTable(Math.Max(16, tokens.Count / 8));
            Build(tokens, table);
            return table;
        }

        /// <summary>
        /// Populates an existing table, allowing callers to append several
        /// stylesheets into one lookup structure.
        /// </summary>
        /// <param name="tokens">
        /// Tokens produced by <see cref="Tokenizer"/>. Read only for the duration
        /// of the call.
        /// </param>
        /// <param name="table">
        /// The table to append to. Existing selectors and conditions are reused
        /// when the new tokens produce an identical key; rules are appended in
        /// source order.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="tokens"/> or <paramref name="table"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Rules from successive calls are appended in call order, so source order
        /// (and therefore cascade precedence at equal specificity and layer) is
        /// preserved across stylesheets.
        /// </para>
        /// <para>
        /// Any span previously obtained from the table, such as one returned by
        /// <see cref="DataTable.GetSelectorSpan"/>, may be invalidated because the
        /// table's arenas can grow during this call.
        /// </para>
        /// </remarks>
        public void Build(List<Token> tokens, DataTable table)
        {
            ArgumentNullException.ThrowIfNull(tokens);
            ArgumentNullException.ThrowIfNull(table);

            var parser = new CssStreamParser(
                CollectionsMarshal.AsSpan(tokens),
                table,
                _conditionCompiler);

            try
            {
                parser.Parse();
            }
            finally
            {
                parser.Dispose();
            }
        }
    }
}
