using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers CSS function names beginning with 'r'.
        /// </summary>
        private static void FunctionsR()
        {
            Function("radial-gradient");
            Function("random");
            Function("ray");
            Function("rect");
            Function("rem");
            Function("repeat");
            Function("repeating-conic-gradient");
            Function("repeating-linear-gradient");
            Function("repeating-radial-gradient");
            Function("rgb");
            Function("rotate");
            Function("rotate3d");
            Function("rotateX");
            Function("rotateY");
            Function("rotateZ");
            Function("round");
        }

    }
}