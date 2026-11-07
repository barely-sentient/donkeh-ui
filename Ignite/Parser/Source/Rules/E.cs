using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS properties beginning with 'e'
        /// </summary>
        private static void PropertiesE()
        {
            CssPropertyName("empty-cells");
        }
    }
}