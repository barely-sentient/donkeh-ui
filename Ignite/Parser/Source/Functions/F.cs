using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS functions beginning with 'e'
        /// </summary>
        private static void FunctionsF()
        {
            Function("fit-content");
        }

    }
}