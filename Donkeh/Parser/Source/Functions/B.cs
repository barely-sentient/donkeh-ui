using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
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