using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'g'.
        /// </summary>
        private static void FunctionsG()
        {
            Function("grayscale");
        }

    }
}