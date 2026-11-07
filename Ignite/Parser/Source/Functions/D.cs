using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS functions beginning with 'd'
        /// </summary>
        private static void FunctionsD()
        {
            Function("device-cmyk");
            Function("drop-shadow");
            Function("dynamic-range-limit-mix");
        }

    }
}