using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS functions beginning with 'b'
        /// </summary>
        private static void FunctionsB()
        {
            Function("blur");
            Function("brightness");
        }

    }
}