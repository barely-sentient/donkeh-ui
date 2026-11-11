using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'o'.
        /// </summary>
        private static void FunctionsO()
        {
            Function("oklab");
            Function("oklch");
            Function("opacity");
        }

    }
}