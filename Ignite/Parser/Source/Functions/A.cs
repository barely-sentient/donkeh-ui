using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    /// <summary>
    /// Tokenizer registrations for CSS function names that begin with the letter
    /// <c>a</c>.
    /// </summary>
    /// <remarks>
    /// The <see cref="Tokenizer"/> type itself is documented on its primary
    /// declaration in <c>Tokenizer.cs</c>; every <c>Functions*.cs</c> file adds
    /// registrations to its static initialization.
    /// </remarks>
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'a'.
        /// </summary>
        private static void FunctionsA()
        {
            Function("abs");
            Function("acos");
            Function("alpha");
            Function("anchor-size");
            Function("asin");
            Function("atan");
            Function("atan2");
            Function("attr");
        }

    }
}