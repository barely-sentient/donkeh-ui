using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'e'.
        /// </summary>
        private static void PropertiesE()
        {
            CssPropertyName("empty-cells");
        }
    }
}