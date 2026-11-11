using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'f'.
        /// </summary>
        private static void PropertiesF()
        {
            CssPropertyName("field-sizing");
            CssPropertyGroup("fill", "opacity", "rule");
            CssPropertyName("filter");
            
            CssPropertyGroup("flex", 
                "basis",
                "direction",
                "flow",
                "grow",
                "line-count",
                "shrink",
                "wrap"
            );
            
            CssPropertyName("float");
            CssPropertyName("flood-color");
            CssPropertyName("flood-opacity");
            
            CssPropertyGroup("font", 
                "family",
                "feature-settings",
                "kerning",
                "language-override",
                "optical-sizing",
                "palette",
                "size",
                "size-adjust",
                "smooth",
                "stretch",
                "style",
                "synthesis",
                "synthesis-position",
                "synthesis-small-caps",
                "synthesis-style",
                "synthesis-weight",
                "variant",
                "variant-alternates",
                "variant-caps",
                "variant-east-asian",
                "variant-emoji",
                "variant-ligatures",
                "variant-numeric",
                "variant-position",
                "variation-settings",
                "weight",
                "width"
            );
            
            CssPropertyName("forced-color-adjust");
            CssPropertyName("frame-sizing");
        }
    }
}