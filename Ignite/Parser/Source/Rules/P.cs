using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'p'.
        /// </summary>
        private static void PropertiesP()
        {
            CssPropertyGroup("padding",
                "block",
                "block-end",
                "block-start",
                "bottom",
                "inline",
                "inline-end",
                "inline-start",
                "left",
                "right",
                "top"
            );
            
            CssPropertyGroup("page",
                "break-after",
                "break-before",
                "break-inside"
            );
            
            CssPropertyName("paint-order");
            CssPropertyName("path-length");
            CssPropertyName("perspective-origin");
            CssPropertyName("perspective");
            
            CssPropertyName("place-content");
            CssPropertyName("place-items");
            CssPropertyName("place-self");

            CssPropertyName("pointer");
            
            CssPropertyGroup("position",
                "anchor",
                "area",
                "try",
                "try-fallbacks",
                "try-order",
                "visibility"
            );
            
            CssPropertyName("print-color-adjust");
        }
    }
}