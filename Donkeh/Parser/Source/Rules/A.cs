using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'a'.
        /// </summary>
        private static void PropertiesA()
        {
            CssPropertyName("accent-color");
            
            CssPropertyGroup("align", 
                "content", 
                "items", 
                "self", 
                "baseline"
            );
            
            CssPropertyName("all");
            
            CssPropertyName("anchor-scope");
            
            CssPropertyGroup("animation",
                "composition", 
                "delay",
                "direction",
                "duration",
                "fill-mode",
                "iteration-count",
                "name",
                "play-state",
                "range",
                "range-end",
                "range-start",
                "timeline",
                "timing-function",
                "trigger"
            );

            CssPropertyName("app-region");
            CssPropertyName("appearance");

            CssPropertyName("aspect-ratio");
        }

    }
}