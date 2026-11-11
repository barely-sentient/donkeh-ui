using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'j'.
        /// </summary>
        private static void PropertiesJ()
        {
            CssPropertyGroup("justify", "content", "items", "self");
        }
    }
}