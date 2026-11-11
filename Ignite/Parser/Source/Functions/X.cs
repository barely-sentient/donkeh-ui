using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
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