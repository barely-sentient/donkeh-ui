using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'm'.
        /// </summary>
        private static void PropertiesM()
        {
            CssPropertyGroup("margin",
                "block",
                "block-end",
                "block-start",
                "bottom",
                "inline",
                "inline-end",
                "inline-start",
                "left",
                "right",
                "top",
                "trim"
            );
            
            CssPropertyGroup("marker",
                "end",
                "mid",
                "start"
            );
            
            CssPropertyGroup("mask",
                "border",
                "border-mode",
                "border-outset",
                "border-repeat",
                "border-slice",
                "border-source",
                "border-width",
                "clip",
                "composite",
                "image",
                "mode",
                "origin",
                "position",
                "repeat",
                "size",
                "type"
            );
            
            CssPropertyName("math-depth");
            CssPropertyName("math-shift");
            CssPropertyName("math-style");
            CssPropertyName("max-block-size");
            CssPropertyName("max-height");
            CssPropertyName("max-inline-size");
            CssPropertyName("max-width");
            CssPropertyName("min-block-size");
            CssPropertyName("min-height");
            CssPropertyName("min-inline-size");
            CssPropertyName("min-width");
            CssPropertyName("mix-blend-mode");
        }
    }
}