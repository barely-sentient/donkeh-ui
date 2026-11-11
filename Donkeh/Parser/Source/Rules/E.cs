using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
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