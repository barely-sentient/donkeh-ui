using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'd'.
        /// </summary>
        private static void FunctionsD()
        {
            Function("device-cmyk");
            Function("drop-shadow");
            Function("dynamic-range-limit-mix");
        }

    }
}