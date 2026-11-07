using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS properties beginning with 'c'
        /// </summary>
        private static void PropertiesC()
        {
            CssPropertyName("caption-side");
            CssPropertyGroup("caret", "animation", "color", "shape");
            CssPropertyName("clear");
            CssPropertyGroup("clip", "path", "rule");
            CssPropertyGroup("color", "interpolation", "interpolation-filters", "scheme");
            CssPropertyGroup("column",
                "count",
                "fill",
                "gap",
                "height",
                "rule",
                "rule-break",
                "rule-color",
                "rule-inset-cap",
                "rule-inset-cap-end",
                "rule-inset-cap-start",
                "rule-inset-junction",
                "rule-inset-junction-end",
                "rule-inset-junction-start",
                "rule-style",
                "rule-visibility-items",
                "rule-width",
                "span",
                "width",
                "wrap"
            );
            
            CssPropertyName("columns");

            CssPropertyGroup("contain",
                "intrinsic-block-size",
                "intrinsic-height",
                "intrinsic-inline-size",
                "intrinsic-size",
                "intrinsic-width"
            );
            
            CssPropertyGroup("container", "name", "type");
            CssPropertyGroup("content", "visibility");
            
            CssPropertyGroup("corner",
                "block-end-shape",
                "block-start-shape",
                "bottom-left-shape",
                "bottom-right-shape",
                "bottom-shape",
                "end-end-shape",
                "end-start-shape",
                "inline-end-shape",
                "inline-start-shape",
                "left-shape",
                "right-shape",
                "shape",
                "start-end-shape",
                "start-start-shape",
                "top-left-shape",
                "top-right-shape",
                "top-shape"
            );
            
            CssPropertyGroup("counter",
                "increment",
                "reset",
                "set"
            );
            
            CssPropertyName("cursor");

            CssPropertyName("cx");
            CssPropertyName("cy");
        }
    }
}