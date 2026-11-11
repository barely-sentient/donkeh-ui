using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
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