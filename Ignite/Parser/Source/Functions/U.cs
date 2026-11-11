using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'u'.
        /// </summary>
        private static void FunctionsU()
        {
            Function("url");
        }

    }
}