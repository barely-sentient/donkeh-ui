using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS functions beginning with 'b'
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