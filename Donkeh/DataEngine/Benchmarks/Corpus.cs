using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Donkeh.Data;
using Donkeh.Parser;
using Donkeh.Parser.Objects;
using Engine = Donkeh.DataEngine.DataEngine;

namespace Donkeh.DataEngine.Benchmarks;

/// <summary>
/// Shape of a generated corpus, derived by building small samples rather than
/// the whole document. Counts are exact as long as every appended block has the
/// same character length, which the generator guarantees by padding ids.
/// </summary>
public readonly record struct CorpusShape(
    int TargetChars,
    int SourceChars,
    int SourceBytes,
    int Tokens,
    int Blocks,
    int Selectors,
    int Rules,
    int Conditions);

/// <summary>
/// Deterministic large-CSS generator. A block exercises flat rules, selector
/// lists, combinators, attribute/pseudo selectors, custom properties, nested
/// rules, media/supports/container conditions, layers and keyframes. Every
/// numeric id is zero padded so each block has exactly the same length and the
/// resulting document scales linearly with the requested size.
/// </summary>
public static class CssCorpus
{
    private const int IdWidth = 7;
    private static readonly ConcurrentDictionary<(int Size, bool Repeated), CorpusShape> Shapes = new();
    private static readonly Engine ShapeEngine = new(Condition.AlwaysTrueCompiler);

    public static string Create(int targetChars)
    {
        var builder = new StringBuilder(targetChars + 4096);
        var id = 0;
        var condition = 0;

        while (builder.Length < targetChars)
        {
            AppendBlock(builder, ref id, ref condition);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Repeats one identical block. Selectors and conditions collapse to a tiny
    /// number of interned records while every declaration still produces rules,
    /// which measures the de-duplication lookup path.
    /// </summary>
    public static string CreateRepeated(int targetChars)
    {
        var sample = new StringBuilder(2048);
        var id = 0;
        var condition = 0;
        AppendBlock(sample, ref id, ref condition);

        var block = sample.ToString();
        var builder = new StringBuilder(targetChars + block.Length);

        while (builder.Length < targetChars)
        {
            builder.Append(block);
        }

        return builder.ToString();
    }

    public static CorpusShape GetShape(int targetChars, bool repeated)
    {
        return Shapes.GetOrAdd((targetChars, repeated), static key => Compute(key.Size, key.Repeated));
    }

    private static CorpusShape Compute(int targetChars, bool repeated)
    {
        var id = 0;
        var condition = 0;

        var first = new StringBuilder(2048);
        AppendBlock(first, ref id, ref condition);
        var block1 = first.ToString();

        var second = new StringBuilder(2048);
        if (repeated)
        {
            // Reset the counters so the second block is byte-for-byte identical.
            id = 0;
            condition = 0;
        }

        AppendBlock(second, ref id, ref condition);
        var block2 = second.ToString();

        var one = ShapeEngine.Build(block1);
        var two = ShapeEngine.Build(block1 + block2);

        var blocks = Math.Max(1, (targetChars + block1.Length - 1) / block1.Length);
        var extraBlocks = blocks - 1;

        var tokensOne = CountTokens(block1);
        var tokensTwo = CountTokens(block1 + block2);

        var sourceChars = blocks * block1.Length;

        return new CorpusShape(
            TargetChars: targetChars,
            SourceChars: sourceChars,
            // The generated corpus is pure ASCII, so chars and UTF-8 bytes match.
            SourceBytes: sourceChars,
            Tokens: tokensOne + (extraBlocks * (tokensTwo - tokensOne)),
            Blocks: blocks,
            Selectors: one.SelectorCount + (extraBlocks * (two.SelectorCount - one.SelectorCount)),
            Rules: one.RuleCount + (extraBlocks * (two.RuleCount - one.RuleCount)),
            Conditions: one.ConditionCount + (extraBlocks * (two.ConditionCount - one.ConditionCount)));
    }

    private static int CountTokens(string css)
    {
        var tokens = new List<Token>(Math.Max(16, css.Length / 2));
        Tokenizer.Parse(css.AsSpan(), tokens);
        return tokens.Count;
    }

    private static void AppendBlock(StringBuilder builder, ref int id, ref int condition)
    {
        builder.Append("/* chunk */\n");

        builder.Append(".c").Append(Next(ref id))
            .Append("{color:#1a2b3c;margin:0 4px;}\n");

        builder.Append(".c").Append(Next(ref id))
            .Append(":hover,.c").Append(Next(ref id))
            .Append(">.c").Append(Next(ref id))
            .Append("{display:flex;gap:8px;padding:1px 2px;}\n");

        builder.Append(".c").Append(Next(ref id))
            .Append(' ').Append(".c").Append(Next(ref id))
            .Append("{background:#fff;border-radius:4px;}\n");

        builder.Append(".c").Append(Next(ref id))
            .Append("{display:grid;grid-template-columns:1fr 1fr;}\n");

        builder.Append(".c").Append(Next(ref id))
            .Append("::before{content:\"x\";color:rgba(1,2,3,.5);}\n");

        builder.Append(".c").Append(Next(ref id))
            .Append("[data-x=\"v").Append(Next(ref id))
            .Append("\"]{color:red;}\n");

        builder.Append(".c").Append(Next(ref id))
            .Append("{--gap:4px;color:var(--gap);}\n");

        builder.Append(".c").Append(Next(ref id))
            .Append("{color:red;&:hover{color:blue;}.c").Append(Next(ref id))
            .Append(" &{color:green;}}\n");

        builder.Append("@media (min-width: ").Append(NextCondition(ref condition))
            .Append("px){.c").Append(Next(ref id))
            .Append("{color:#123456;margin:0;}}\n");

        builder.Append("@supports (display:grid){.c").Append(Next(ref id))
            .Append("{display:grid;gap:1px;}}\n");

        builder.Append("@container (min-width: ").Append(NextCondition(ref condition))
            .Append("px){.c").Append(Next(ref id))
            .Append(">.c").Append(Next(ref id))
            .Append("{color:blue;}}\n");

        builder.Append("@layer base{.c").Append(Next(ref id))
            .Append("{color:#222;}}\n");

        builder.Append(".c").Append(Next(ref id))
            .Append("{color:red;@media (min-width: ").Append(NextCondition(ref condition))
            .Append("px){color:blue;}}\n");

        builder.Append("@keyframes k").Append(Next(ref id))
            .Append("{from{transform:rotate(0deg);}to{transform:rotate(360deg);}}\n");
    }

    private static string Next(ref int value)
    {
        var text = (value++).ToString("D" + IdWidth, CultureInfo.InvariantCulture);
        return text;
    }

    private static string NextCondition(ref int value)
    {
        var text = (value++).ToString("D" + IdWidth, CultureInfo.InvariantCulture);
        return text;
    }
}
