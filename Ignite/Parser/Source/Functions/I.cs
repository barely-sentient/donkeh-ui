using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// All CSS functions beginning with 'i'
        /// </summary>
        private static void FunctionsI()
        {
            Function("image-set");
            Function("image");
            Function("inset");
            Function("invert");
        }

    }
}