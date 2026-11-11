using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'o'.
        /// </summary>
        private static void PropertiesO()
        {
            CssPropertyName("object-fit");
            CssPropertyName("object-position");
            CssPropertyName("object-view-box");
            
            CssPropertyGroup("offset",
                "anchor",
                "distance",
                "path",
                "position",
                "rotate"
            );
            
            CssPropertyName("opacity"); 
            CssPropertyName("order");
            CssPropertyName("orphans");
            
            CssPropertyGroup("outline",
                "color",
                "offset",
                "style",
                "width"
            );
            
            CssPropertyGroup("overflow",
                "anchor",
                "block",
                "clip-margin",
                "inline",
                "wrap",
                "x",
                "y"
            );
            
            CssPropertyName("overscroll-behavior");
            CssPropertyName("overscroll-behavior-block");
            CssPropertyName("overscroll-behavior-inline");
            CssPropertyName("overscroll-behavior-x");
            CssPropertyName("overscroll-behavior-y");
        }
    }
}