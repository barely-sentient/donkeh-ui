using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'v'.
        /// </summary>
        private static void FunctionsV()
        {
            Function("var");
        }

    }
}