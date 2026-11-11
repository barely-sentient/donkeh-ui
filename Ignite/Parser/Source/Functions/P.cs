using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'p'.
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