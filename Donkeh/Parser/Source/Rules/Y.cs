using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'y'.
        /// </summary>
        private static void PropertiesY()
        {
            CssPropertyName("y");
        }
    }
}