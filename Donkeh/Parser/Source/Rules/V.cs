using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS property names beginning with 'v'.
        /// </summary>
        private static void PropertiesV()
        {
            CssPropertyName("vector-effect");
            CssPropertyName("vertical-align");
            CssPropertyName("view-timeline");
            CssPropertyName("view-timeline-axis");
            CssPropertyName("view-timeline-inset");
            CssPropertyName("view-timeline-name");
            CssPropertyName("view-transition-class");
            CssPropertyName("view-transition-name");
            CssPropertyName("view-transition-scope");
            CssPropertyName("visibility");
        }
    }
}