using Donkeh.Parser.Builder;
using Donkeh.Parser.Compilation;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers every exact rule and compiles them into
        /// <see cref="ExactMatch"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The order of the calls is significant only in that all rules must be
        /// registered before <see cref="MinLengthByFirstChar"/> is computed and
        /// the matcher is compiled. Property groups, pseudo selectors and
        /// functions are all flat exact-match rules.
        /// </para>
        /// <para>
        /// <c>k</c> has no property rules, so <c>PropertiesK</c> does not exist.
        /// The per-letter methods are partial-class helpers split across
        /// <c>Rules/*.cs</c>.
        /// </para>
        /// </remarks>
        static Tokenizer()
        {
            for (var i = 0; i < MinLengthByFirstChar.Length; i++)
            {
                MinLengthByFirstChar[i] = int.MaxValue;
            }

            Punctuation();
            PropertiesA();
            PropertiesB();
            PropertiesC();
            PropertiesD();
            PropertiesE();
            PropertiesF();
            PropertiesG();
            PropertiesH();
            PropertiesI();
            PropertiesJ();
            // No rules with 'k'
            PropertiesL();
            PropertiesM();
            PropertiesO();
            PropertiesP();
            PropertiesQ();
            PropertiesR();
            PropertiesS();
            PropertiesT();
            PropertiesU();
            PropertiesV();
            PropertiesW();
            PropertiesX();
            PropertiesY();
            PropertiesZ();
            PseudoClasses();
            PseudoElements();
            Functions();

            foreach (var pair in RuleBuilder.ExactRules)
            {
                var text = InternTable.GetString(pair.Key);
                if (text is null || text.Length == 0)
                {
                    continue;
                }

                var first = text[0];
                if (text.Length < MinLengthByFirstChar[first])
                {
                    MinLengthByFirstChar[first] = text.Length;
                }
            }

            var compiler = new Compiler();
            compiler.CompileExactRules(RuleBuilder.ExactRules);
            ExactMatch = compiler.CompiledMatcher!;
        }
    }
}