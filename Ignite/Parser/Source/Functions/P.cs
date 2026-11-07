using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS functions beginning with 'p'
        /// </summary>
        private static void FunctionsP()
        {
            Function("paint");
            Function("param");
            Function("path");
            Function("perspective");
            Function("polygon");
            Function("pow");
            Function("progress");
        }

    }
}