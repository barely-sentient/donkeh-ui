using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS functions beginning with 'm'
        /// </summary>
        private static void FunctionsO()
        {
            Function("oklab");
            Function("oklch");
            Function("opacity");
        }

    }
}