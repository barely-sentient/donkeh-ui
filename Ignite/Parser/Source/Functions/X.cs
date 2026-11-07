using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS functions beginning with 'X'
        /// </summary>
        private static void FunctionsX()
        {
            Function("xywh");
        }

    }
}