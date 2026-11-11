using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'z'.
        /// </summary>
        private static void PropertiesZ()
        {
            CssPropertyName("z-index");
            CssPropertyName("zoom");
        }
    }
}