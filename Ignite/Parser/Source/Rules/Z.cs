using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
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