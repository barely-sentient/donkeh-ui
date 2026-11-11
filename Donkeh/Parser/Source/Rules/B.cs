using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'b'.
        /// </summary>
        private static void PropertiesB()
        {
            CssPropertyName("backdrop-filter");
            CssPropertyName("backface-visibility");

            CssPropertyGroup("background",
                "attachment",
                "blend-mode",
                "clip",
                "color",
                "image",
                "origin",
                "position",
                "position-x",
                "position-y",
                "repeat",
                "repeat-x",
                "repeat-y",
                "size"
            );

            CssPropertyName("baseline-shift");
            CssPropertyName("baseline-source");
            CssPropertyName("block-size");

            CssPropertyGroup("border",
                "block",
                "block-color",
                "block-end",
                "block-end-color",
                "block-end-style",
                "block-end-width",
                "block-start",
                "block-start-color",
                "block-start-style",
                "block-start-width",
                "block-width",
                "bottom",
                "bottom-color",
                "bottom-left-radius",
                "bottom-right-radius",
                "bottom-style",
                "bottom-width",
                "collapse",
                "color",
                "end-end-radius",
                "end-start-radius",
                "image",
                "image-outset",
                "image-repeat",
                "image-slice",
                "image-source",
                "image-width",
                "inline",
                "inline-color",
                "inline-end",
                "inline-end-color",
                "inline-end-style",
                "inline-end-width",
                "inline-start",
                "inline-start-color",
                "inline-start-style",
                "inline-start-width",
                "left",
                "left-color",
                "left-style",
                "left-width",
                "radius",
                "right",
                "right-color",
                "right-style",
                "right-width",
                "shape",
                "spacing",
                "start-end-radius",
                "start-start-radius",
                "style",
                "top",
                "top-color",
                "top-left-radius",
                "top-right-radius",
                "top-style",
                "top-width",
                "width"
            );

            CssPropertyName("bottom");

            CssPropertyGroup("box",
                "align",
                "decoration-break",
                "direction",
                "flex",
                "flex-group",
                "lines",
                "ordinal-group",
                "orient",
                "pack",
                "shadow",
                "sizing"
            );

            CssPropertyName("break-after");
            CssPropertyName("break-before");
            CssPropertyName("break-inside");
        }
    }
}