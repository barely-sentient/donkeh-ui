using Ignite.Data;
using Ignite.Parser.Objects;
using Engine = Ignite.DataEngine.DataEngine;

namespace Ignite.Parser.Tests;

public class DataEngineTests
{
    private static DataTable Build(string css, ConditionCompiler? compiler = null)
    {
        return new Engine(compiler).Build(css);
    }

    [Fact]
    public void KeepsDescendantAndCompoundSelectorsDistinct()
    {
        var table = Build(".a .b { color: red; } .a.b { color: blue; }");

        Assert.Equal(2, table.SelectorCount);
        Assert.NotEqual(table.GetRule(0).SelectorId, table.GetRule(1).SelectorId);

        // Both are four tokens; only the descendant gap differs.
        Assert.Equal(4, table.GetSelectorSpan(table.GetRule(0).SelectorId).Length);
        Assert.Equal(4, table.GetSelectorSpan(table.GetRule(1).SelectorId).Length);
    }

    [Fact]
    public void DeduplicatesIdenticalSelectors()
    {
        var table = Build(".a .b { color: red; } .a .b { color: blue; }");

        Assert.Equal(1, table.SelectorCount);
        Assert.Equal(2, table.RuleCount);
    }

    [Fact]
    public void DeduplicatesWhitespaceAroundExplicitCombinators()
    {
        var table = Build("a > b { color: red; } a>b { color: blue; }");

        Assert.Equal(1, table.SelectorCount);
        Assert.Equal(2, table.RuleCount);
    }

    [Fact]
    public void SharesExecutorAcrossDifferentParents()
    {
        var compilations = 0;
        ConditionCompiler compiler = _ =>
        {
            compilations++;
            return Condition.AlwaysTrue;
        };

        var table = Build(
            "@media (min-width: 1px) { @media (orientation: portrait) { .a { color: red; } } }" +
            "@media (min-width: 2px) { @media (orientation: portrait) { .b { color: blue; } } }",
            compiler);

        Assert.Equal(3, compilations);
        Assert.Equal(4, table.ConditionCount);
    }

    [Fact]
    public void EvaluatesEachConditionOncePerEnvironmentVersion()
    {
        var evaluations = 0;
        ConditionCompiler compiler = _ =>
        {
            return (in RendererContext _) =>
            {
                evaluations++;
                return true;
            };
        };

        var table = Build(
            "@media (min-width: 1px) { .a { color: red; } .b { color: blue; } }",
            compiler);

        var context = new RendererContext();

        Assert.True(table.MatchesCondition(table.GetRule(0).ConditionId, context));
        Assert.True(table.MatchesCondition(table.GetRule(1).ConditionId, context));
        Assert.Equal(1, evaluations);

        context.ViewportWidth = 100;

        Assert.True(table.MatchesCondition(table.GetRule(0).ConditionId, context));
        Assert.Equal(2, evaluations);

        var otherContext = new RendererContext();
        Assert.True(table.MatchesCondition(table.GetRule(0).ConditionId, otherContext));
        Assert.Equal(3, evaluations);
    }

    [Fact]
    public void FailsClosedWhenCompilerThrows()
    {
        var table = Build(
            "@media (min-width: 1px) { .a { color: red; } }",
            _ => throw new InvalidOperationException("unsupported"));

        Assert.False(table.MatchesCondition(table.GetRule(0).ConditionId, new RendererContext()));
    }

    [Fact]
    public void FailsClosedWithoutCompiler()
    {
        var table = Build("@media (min-width: 1px) { .a { color: red; } }");

        Assert.False(table.MatchesCondition(table.GetRule(0).ConditionId, new RendererContext()));
    }

    [Fact]
    public void KeepsConditionsWithDifferentGapsDistinct()
    {
        var compilations = 0;
        ConditionCompiler compiler = _ =>
        {
            compilations++;
            return Condition.AlwaysTrue;
        };

        var table = Build(
            "@supports selector(.a .b) { .x { color: red; } }" +
            "@supports selector(.a.b) { .y { color: blue; } }",
            compiler);

        Assert.Equal(2, compilations);
        Assert.Equal(2, table.ConditionCount);
    }

    [Fact]
    public void NormalisesLexicalKindsForDiscriminatorLookup()
    {
        var table = Build(".filter { color: red; }");

        var selectorId = table.GetRule(0).SelectorId;
        var discriminator = table.GetSelector(selectorId).PrimaryDiscriminator;

        Assert.Equal(TokenKind.Identifier, discriminator.Kind);

        var query = new Token
        {
            Kind = TokenKind.Identifier,
            InternedId = InternTable.Intern("filter")
        };

        var candidates = table.GetSelectorIds(query);
        Assert.Contains(selectorId, candidates.ToArray());
    }

    [Fact]
    public void MalformedNestedSelectorDoesNotLeakToParent()
    {
        var table = Build(".parent { color: red; , { color: blue; } }");

        Assert.Single(table.Rules);
    }

    [Fact]
    public void KeepsLongKeyframesNameAndStopIntact()
    {
        var table = Build(
            "@keyframes progress-bar-stripes { 0% { background-position-x: 1rem; } }");

        var frame = Assert.Single(table.Keyframes);
        Assert.Equal("progress-bar-stripes", InternTable.GetString(frame.NameId));
        Assert.Equal("0%", SelectorText(table, frame.KeyId));
    }

    private static string SelectorText(DataTable table, int selectorId)
    {
        var tokens = table.GetSelectorSpan(selectorId);
        var builder = new System.Text.StringBuilder();

        for (var i = 0; i < tokens.Length; i++)
        {
            builder.Append(InternTable.GetString(tokens[i].InternedId));
        }

        return builder.ToString();
    }
}
