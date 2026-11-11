using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'c'.
        /// </summary>
        private static void FunctionsC()
        {
            Function("calc-size");
            Function("calc");
            Function("circle");
            Function("clamp");
            Function("color-mix");
            Function("color");
            Function("conic-gradient");
            Function("contrast-color");
            Function("contrast");
            Function("cos");
            Function("counter");
            Function("counters");
            Function("cross-fade");
            Function("cubic-bezier");
        }

    }
}