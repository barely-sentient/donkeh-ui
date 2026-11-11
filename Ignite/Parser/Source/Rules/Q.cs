using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'q'.
        /// </summary>
        private static void PropertiesQ()
        {
            CssPropertyName("quotes");
        }
    }
}