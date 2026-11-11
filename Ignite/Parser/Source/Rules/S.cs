using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 's'.
        /// </summary>
        private static void PropertiesS()
        {
            CssPropertyName("scale");
            CssPropertyName("scroll-behavior");
            CssPropertyName("scroll-initial-target");
            CssPropertyName("scroll-margin");
            CssPropertyName("scroll-margin-block");
            CssPropertyName("scroll-margin-block-end");
            CssPropertyName("scroll-margin-block-start");
            CssPropertyName("scroll-margin-bottom");
            CssPropertyName("scroll-margin-inline");
            CssPropertyName("scroll-margin-inline-end");
            CssPropertyName("scroll-margin-inline-start");
            CssPropertyName("scroll-margin-left");
            CssPropertyName("scroll-margin-right");
            CssPropertyName("scroll-margin-top");
            CssPropertyName("scroll-marker-group");
            CssPropertyName("scroll-padding");
            CssPropertyName("scroll-padding-block");
            CssPropertyName("scroll-padding-block-end");
            CssPropertyName("scroll-padding-block-start");
            CssPropertyName("scroll-padding-bottom");
            CssPropertyName("scroll-padding-inline");
            CssPropertyName("scroll-padding-inline-end");
            CssPropertyName("scroll-padding-inline-start");
            CssPropertyName("scroll-padding-left");
            CssPropertyName("scroll-padding-right");
            CssPropertyName("scroll-padding-top");
            CssPropertyName("scroll-snap-align");
            CssPropertyName("scroll-snap-stop");
            CssPropertyName("scroll-snap-type");
            CssPropertyName("scroll-target-group");
            CssPropertyName("scroll-timeline");
            CssPropertyName("scroll-timeline-axis");
            CssPropertyName("scroll-timeline-name");
            CssPropertyName("scrollbar-color");
            CssPropertyName("scrollbar-gutter");
            CssPropertyName("scrollbar-width");
            CssPropertyName("shape-image-threshold");
            CssPropertyName("shape-margin");
            CssPropertyName("shape-outside");
            CssPropertyName("shape-rendering");
            CssPropertyName("speak-as");
            CssPropertyName("stop-color");
            CssPropertyName("stop-opacity");
            
            CssPropertyGroup("stroke",
                "dasharray",
                "dashoffset",
                "linecap",
                "linejoin",
                "miterlimit",
                "opacity",
                "width"
            );
        }
    }
}