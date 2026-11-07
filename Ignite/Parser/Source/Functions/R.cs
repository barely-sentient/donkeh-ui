using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS functions beginning with 'p'
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