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
    public sealed class DataEngine
    {
        private readonly ConditionCompiler _conditionCompiler;

        /// <param name="conditionCompiler">
        /// The IL generator for conditions. When omitted, every condition is
        /// treated as false so rules guarded by an unsupported condition are not
        /// applied. Pass <see cref="Condition.AlwaysTrueCompiler"/> when the
        /// pipeline is being exercised without an IL generator.
        /// </param>
        public DataEngine(ConditionCompiler? conditionCompiler = null)
        {
            _conditionCompiler = conditionCompiler ?? Condition.AlwaysFalseCompiler;
        }

        public DataTable Build(string source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return Build(Tokenizer.Parse(source));
        }

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
