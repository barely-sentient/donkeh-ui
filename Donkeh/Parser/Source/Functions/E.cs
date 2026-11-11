using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'e'.
        /// </summary>
        private static void FunctionsE()
        {
            Function("element");
            Function("ellipse");
            Function("env");
            Function("exp");
        }

    }
}