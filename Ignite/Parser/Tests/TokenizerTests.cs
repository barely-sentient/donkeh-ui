using Ignite.Parser.Builder;
using Ignite.Parser.Objects;

namespace Ignite.Parser.Tests;

public class TokenizerTests
{
    private static (TokenKind Kind, string Text)[] Parse(string source)
    {
        var tokens = Tokenizer.Parse(source);
        var result = new (TokenKind, string)[tokens.Count];

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            result[i] = (token.Kind, source.Substring(token.StartIndex, token.EndIndex - token.StartIndex));
        }

        return result;
    }

    [Fact]
    public void ParsesDeclaration()
    {
        Assert.Equal(
            new[]
            {
                (TokenKind.PropertyName, "color"),
                (TokenKind.Colon, ":"),
                (TokenKind.Identifier, "red"),
                (TokenKind.SemiColon, ";")
            },
            Parse("color: red;"));
    }

    [Fact]
    public void DisambiguatesPropertyAndFunctionNames()
    {
        Assert.Equal(
            new[]
            {
                (TokenKind.PropertyName, "color"),
                (TokenKind.Colon, ":"),
                (TokenKind.Identifier, "red")
            },
            Parse("color: red"));

        Assert.Equal(
            new[]
            {
                (TokenKind.Function, "color"),
                (TokenKind.LeftParenthesis, "("),
                (TokenKind.Number, "1"),
                (TokenKind.RightParenthesis, ")")
            },
            Parse("color(1)"));

        Assert.Equal(TokenKind.PropertyName, Parse("rotate: 45deg")[0].Kind);
        Assert.Equal(TokenKind.Function, Parse("rotate(45deg)")[0].Kind);
    }

    [Fact]
    public void ParsesPseudoSelectors()
    {
        Assert.Equal(
            new[]
            {
                (TokenKind.PseudoClass, ":nth-child"),
                (TokenKind.LeftParenthesis, "("),
                (TokenKind.Dimension, "2n"),
                (TokenKind.Plus, "+"),
                (TokenKind.Number, "1"),
                (TokenKind.RightParenthesis, ")")
            },
            Parse(":nth-child(2n+1)"));

        Assert.Equal(
            new[] { (TokenKind.PseudoElement, "::before") },
            Parse("::before"));

        Assert.Equal(
            new[] { (TokenKind.PseudoElementColon, "::"), (TokenKind.Identifier, "unknown") },
            Parse("::unknown"));
    }

    [Fact]
    public void SkipsWhitespaceAndComments()
    {
        Assert.Empty(Parse("   \t\r\n"));

        Assert.Equal(
            new[] { (TokenKind.Identifier, "a") },
            Parse("/* comment */ a"));
    }

    [Fact]
    public void ParsesNumbersDimensionsAndPercentages()
    {
        Assert.Equal(
            new[]
            {
                (TokenKind.Dimension, "-10px"),
                (TokenKind.Percentage, "50%"),
                (TokenKind.Number, "1.5e2"),
                (TokenKind.Delimiter, "-"),
                (TokenKind.Dimension, "1px")
            },
            Parse("-10px 50% 1.5e2 - 1px"));
    }

    [Fact]
    public void RefOverloadConsumesTheSpan()
    {
        var span = "color: red;".AsSpan();
        int count = Tokenizer.Parse(ref span);

        Assert.True(span.IsEmpty);
        Assert.Equal(Tokenizer.Parse("color: red;").Count, count);
    }

    [Fact]
    public void EveryExactRuleRoundTripsThroughTheMatcher()
    {
        foreach (var pair in RuleBuilder.ExactRules)
        {
            string text = InternTable.GetString(pair.Key)!;
            Assert.Equal((int)pair.Value, Tokenizer.ExactMatch(text.AsSpan()));
        }
    }
}
