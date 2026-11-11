using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'm'.
        /// </summary>
        private static void FunctionsM()
        {
            Function("matrix");
            Function("matrix3d");
            Function("max");
            Function("min");
            Function("minmax");
            Function("mod");
        }

    }
}