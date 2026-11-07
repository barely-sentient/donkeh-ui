using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        private static void PropertiesR()
        {
            CssPropertyName("r");
            CssPropertyName("reading-flow");
            CssPropertyName("reading-order");
            CssPropertyName("resize"); 
            CssPropertyName("right");
            CssPropertyName("rotate");
            CssPropertyName("row-gap");
            CssPropertyName("row-rule");
            CssPropertyName("row-rule-break");
            CssPropertyName("row-rule-color");
            CssPropertyName("row-rule-inset-cap");
            CssPropertyName("row-rule-inset-cap-end");
            CssPropertyName("row-rule-inset-cap-start");
            CssPropertyName("row-rule-inset-junction");
            CssPropertyName("row-rule-inset-junction-end");
            CssPropertyName("row-rule-inset-junction-start");
            CssPropertyName("row-rule-style");
            CssPropertyName("row-rule-visibility-items");
            CssPropertyName("row-rule-width");
            CssPropertyName("ruby-align");
            CssPropertyName("ruby-overhang");
            CssPropertyName("ruby-position");
            
            CssPropertyGroup("rule",
                "break",
                "color",
                "inset-cap",
                "inset-junction",
                "overlap",
                "style",
                "visibility-items",
                "width"
            );
            
            CssPropertyName("rx");
            CssPropertyName("ry");
        }
    }
}