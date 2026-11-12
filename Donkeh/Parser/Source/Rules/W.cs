using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'w'.
        /// </summary>
        private static void PropertiesW()
        {
            CssPropertyName("white-space");
            CssPropertyName("white-space-collapse");
            CssPropertyName("widows");
            CssPropertyName("width");
            CssPropertyName("will-change");
            CssPropertyName("word-break");
            CssPropertyName("word-spacing");
            CssPropertyName("writing-mode");
            CssPropertyName("-webkit-app-region");
        }
    }
}