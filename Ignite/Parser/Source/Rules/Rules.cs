using Ignite.Parser.Builder;
using Ignite.Parser.Compilation;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        static Tokenizer()
        {
            for (int i = 0; i < MinLengthByFirstChar.Length; i++)
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

                char first = text[0];
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