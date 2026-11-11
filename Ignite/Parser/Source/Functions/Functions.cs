using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers all supported CSS function names by delegating to the
        /// per-letter registration methods.
        /// </summary>
        /// <remarks>
        /// Only letters that actually contain CSS functions have a method;
        /// there are no <c>J</c>, <c>K</c>, <c>N</c>, <c>Q</c>, <c>W</c>,
        /// <c>Y</c> or <c>Z</c> helpers.
        /// </remarks>
        private static void Functions()
        {
            FunctionsA();
            FunctionsB();
            FunctionsC();
            FunctionsD();
            FunctionsE();
            FunctionsF();
            FunctionsG();
            FunctionsH();
            FunctionsI();
            FunctionsL();
            FunctionsM();
            FunctionsO();
            FunctionsP();
            FunctionsR();
            FunctionsS();
            FunctionsT();
            FunctionsU();
            FunctionsV();
            FunctionsX();
        }
    }
}