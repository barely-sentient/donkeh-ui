using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS functions beginning with 'V'
        /// </summary>
        private static void FunctionsV()
        {
            Function("var");
        }

    }
}