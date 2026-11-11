using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'f'.
        /// </summary>
        private static void FunctionsF()
        {
            Function("fit-content");
        }

    }
}