using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
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