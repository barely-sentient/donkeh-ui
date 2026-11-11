using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 't'.
        /// </summary>
        private static void FunctionsT()
        {
            Function("tan");
            Function("translate");
            Function("translate3d");
            Function("translateX");
            Function("translateY");
            Function("translateZ");
            Function("type");
        }

    }
}