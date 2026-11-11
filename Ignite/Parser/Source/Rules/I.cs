using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'i'.
        /// </summary>
        private static void PropertiesI()
        {
            CssPropertyName("image-orientation");
            CssPropertyName("image-rendering");
            CssPropertyName("image-resolution");
            
            CssPropertyName("initial-letter");
            CssPropertyName("inline-size");
            
            CssPropertyGroup("inset", 
               "block",
               "block-end",
               "block-start",
               "inline",
               "inline-end",
               "inline-start"
            );
            
            CssPropertyName("interactivity");
            
            CssPropertyName("interest-delay");
            CssPropertyName("interest-delay-end");
            CssPropertyName("interest-delay-start");
            CssPropertyName("interpolate-size");
            CssPropertyName("isolation");
        }
    }
}