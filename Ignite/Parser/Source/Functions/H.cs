using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS functions beginning with 'h'
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