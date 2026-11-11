using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'd'.
        /// </summary>
        private static void PropertiesD()
        {
            CssPropertyName("d");
            CssPropertyName("direction");
            CssPropertyName("display");
            CssPropertyName("dominant-baseline");
            CssPropertyName("dynamic-range-limit");
        }
    }
}