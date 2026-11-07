using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS properties beginning with 'h'
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