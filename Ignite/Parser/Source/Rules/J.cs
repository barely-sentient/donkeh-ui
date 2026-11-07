using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS properties beginning with 'J'
        /// </summary>
        private static void PropertiesJ()
        {
            CssPropertyGroup("justify", "content", "items", "self");
        }
    }
}