using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'h'.
        /// </summary>
        private static void FunctionsH()
        {
            Function("hsl");
            Function("hue-rotate");
            Function("hwb");
            Function("hypot");
        }

    }
}