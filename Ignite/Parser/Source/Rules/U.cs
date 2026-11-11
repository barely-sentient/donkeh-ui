using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'u'.
        /// </summary>
        private static void PropertiesU()
        {
            CssPropertyName("unicode-bidi");
            CssPropertyName("user-modify");
            CssPropertyName("user-select");
        }
    }
}