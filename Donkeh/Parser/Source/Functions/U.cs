using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
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