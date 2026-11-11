using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
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