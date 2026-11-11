using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'h'.
        /// </summary>
        private static void PropertiesH()
        {
            CssPropertyName("hanging-punctuation");
            CssPropertyName("height");
            CssPropertyName("hyphenate-character");
            CssPropertyName("hyphenate-limit-chars");
            CssPropertyName("hyphens");
        }

    }
}