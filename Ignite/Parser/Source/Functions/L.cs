using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'l'.
        /// </summary>
        private static void FunctionsL()
        {
            Function("lab");
            Function("lch");
            Function("light-dark");
            Function("linear-gradient");
            Function("linear");
            Function("log");
        }

    }
}