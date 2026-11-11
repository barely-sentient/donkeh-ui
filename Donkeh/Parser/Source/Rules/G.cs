using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'g'.
        /// </summary>
        private static void PropertiesG()
        {
            CssPropertyName("gap");
            CssPropertyGroup("grid",
                "area",
                "auto-columns",
                "auto-flow",
                "auto-rows",
                "column",
                "column-end",
                "column-start",
                "row",
                "row-end",
                "row-start",
                "template",
                "template-areas",
                "template-columns",
                "template-rows"
            );
        }
    }
}