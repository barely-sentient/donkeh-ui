using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS functions beginning with 'T'
        /// </summary>
        private static void FunctionsU()
        {
            Function("url");
        }

    }
}