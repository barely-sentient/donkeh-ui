using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'l'.
        /// </summary>
        private static void PropertiesL()
        {
            CssPropertyName("left");
            CssPropertyName("letter-spacing");
            CssPropertyName("lighting-color");

            CssPropertyGroup("line",
                "break",
                "clamp",
                "height",
                "height-step"
            );
            
            CssPropertyName("link-parameters");

            CssPropertyGroup("list",
                "style",
                "style-image",
                "style-position",
                "style-type"
            );
        }
    }
}