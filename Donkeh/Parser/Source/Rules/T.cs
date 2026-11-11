using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 't'.
        /// </summary>
        private static void PropertiesT()
        {
            CssPropertyName("tab-size");
            CssPropertyName("table-layout");
            CssPropertyName("text-align");
            CssPropertyName("text-align-last");
            CssPropertyName("text-anchor");
            CssPropertyName("text-autospace");
            CssPropertyName("text-box");
            CssPropertyName("text-box-edge");
            CssPropertyName("text-box-trim");
            CssPropertyName("text-combine-upright");
            CssPropertyName("text-decoration");
            CssPropertyName("text-decoration-color");
            CssPropertyName("text-decoration-inset");
            CssPropertyName("text-decoration-line");
            CssPropertyName("text-decoration-skip");
            CssPropertyName("text-decoration-skip-ink");
            CssPropertyName("text-decoration-style");
            CssPropertyName("text-decoration-thickness");
            CssPropertyName("text-emphasis");
            CssPropertyName("text-emphasis-color");
            CssPropertyName("text-emphasis-position");
            CssPropertyName("text-emphasis-style");
            CssPropertyName("text-fit");
            CssPropertyName("text-indent");
            CssPropertyName("text-justify");
            CssPropertyName("text-orientation");
            CssPropertyName("text-overflow");
            CssPropertyName("text-rendering");
            CssPropertyName("text-shadow");
            CssPropertyName("text-size-adjust");
            CssPropertyName("text-spacing-trim");
            CssPropertyName("text-transform");
            CssPropertyName("text-underline-offset");
            CssPropertyName("text-underline-position");
            CssPropertyName("text-wrap");
            CssPropertyName("text-wrap-mode");
            CssPropertyName("text-wrap-style");
            CssPropertyName("timeline-scope");
            CssPropertyName("timeline-trigger");
            CssPropertyName("timeline-trigger-activation-range");
            CssPropertyName("timeline-trigger-activation-range-end");
            CssPropertyName("timeline-trigger-activation-range-start");
            CssPropertyName("timeline-trigger-active-range");
            CssPropertyName("timeline-trigger-active-range-end");
            CssPropertyName("timeline-trigger-active-range-start");
            CssPropertyName("timeline-trigger-name");
            CssPropertyName("timeline-trigger-source");   
            CssPropertyName("top");
            CssPropertyName("touch-action");    
            
            CssPropertyGroup("transform",
                "box",
                "origin",
                "style"
            );
            
            CssPropertyGroup("transition",
                "behavior",
                "delay",
                "duration",
                "property",
                "timing-function"
            );
            
            CssPropertyName("translate");
            CssPropertyName("trigger-scope");
        }
    }
}