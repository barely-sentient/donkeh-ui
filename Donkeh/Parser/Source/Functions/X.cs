using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'x'.
        /// </summary>
        private static void FunctionsX()
        {
            Function("xywh");
        }

    }
}