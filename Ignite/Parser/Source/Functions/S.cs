using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS functions beginning with 'p'
        /// </summary>
        private static void FunctionsS()
        {
            Function("saturate");
            Function("scale");
            Function("scale3d");
            Function("scaleX");
            Function("scaleY");
            Function("scaleZ");
            Function("sepia");
            Function("shape");
            Function("sibling-count");
            Function("sibling-index");
            Function("sign");
            Function("sin");
            Function("skew");
            Function("skewX");
            Function("skewY");
            Function("sqrt");
            Function("steps");
            Function("superellipse");
            Function("symbols");
        }

    }
}