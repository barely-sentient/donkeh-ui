using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'b'.
        /// </summary>
        private static void FunctionsB()
        {
            Function("blur");
            Function("brightness");
        }

    }
}