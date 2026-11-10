using Ignite.Data;
using Ignite.Data.Dom;
using Ignite.Parser;
using Engine = Ignite.DataEngine.DataEngine;

namespace Ignite.Parser.Tests;

public class MatchingTests
{
    private static DataTable Build(string css, ConditionCompiler? compiler = null)
    {
        return new Engine(compiler).Build(css);
    }

    private static string Value(MatchedRule match)
    {
        return InternTable.GetString(match.Rule.ValueId) ?? string.Empty;
    }

    [Fact]
    public void MatchesTagIdClassAndUniversal()
    {
        var table = Build(
            "div { color: a; } .btn { color: b; } #main { color: c; } * { color: d; }");

        var node = new IgniteSupportedDomNode { TagName = "div", Id = "main" };
        node.AddClass("btn");

        var matches = table.MatchRules(node);

        Assert.Equal(4, matches.Length);
    }

    [Fact]
    public void OrdersBySpecificityThenSourceOrder()
    {
        var table = Build(
            "* { color: universal; }" +
            "div { color: type; }" +
            ".btn { color: class; }" +
            "#main { color: id; }" +
            ".btn { color: class-later; }");

        var node = new IgniteSupportedDomNode { TagName = "div", Id = "main" };
        node.AddClass("btn");

        var matches = table.MatchRules(node);

        Assert.Equal(5, matches.Length);
        Assert.Equal("universal", Value(matches[0]));
        Assert.Equal("type", Value(matches[1]));
        Assert.Equal("class", Value(matches[2]));
        Assert.Equal("class-later", Value(matches[3]));
        Assert.Equal("id", Value(matches[4]));

        Assert.Equal(0, matches[0].Specificity);
        Assert.Equal(1, matches[1].TypeCount);
        Assert.Equal(1, matches[2].ClassCount);
        Assert.Equal(1, matches[4].IdCount);
    }

    [Fact]
    public void MatchesEveryAttributeOperator()
    {
        var table = Build(
            "[data-role] { color: a; }" +
            "[data-role=admin-en] { color: b; }" +
            "[data-role~=admin-en] { color: c; }" +
            "[data-role|=admin] { color: d; }" +
            "[data-role^=admin] { color: e; }" +
            "[data-role$=en] { color: f; }" +
            "[data-role*=min] { color: g; }");

        var node = new IgniteSupportedDomNode();
        node.SetAttribute("data-role", "admin-en");

        var matches = table.MatchRules(node);

        Assert.Equal(7, matches.Length);
    }

    [Fact]
    public void AttributeOperatorMissesDoNotMatch()
    {
        var table = Build(
            "[data-role%=admin] { color: a; }" +
            "[data-role~=admin] { color: b; }" +
            "[data-role^=admin] { color: c; }" +
            "[data-role$=admin] { color: d; }" +
            "[data-role*=admin] { color: e; }");

        var node = new IgniteSupportedDomNode();
        node.SetAttribute("data-role", "admin-en");

        var matches = table.MatchRules(node);

        // Only ^= and *= match; %= is not a CSS operator.
        Assert.Equal(2, matches.Length);
    }

    [Fact]
    public void HandlesQuotedValuesAndCaseInsensitiveFlag()
    {
        var table = Build(
            "[data-role=\"Admin-En\"] { color: a; }" +
            "[data-role=\"ADMIN-EN\" i] { color: b; }" +
            "[data-role=\"admin-en\" s] { color: c; }");

        var node = new IgniteSupportedDomNode();
        node.SetAttribute("data-role", "admin-en");

        var matches = table.MatchRules(node);

        // The quoted selector without a flag is case sensitive, 'i' folds case,
        // and 's' is the explicit case sensitive flag.
        Assert.Equal(2, matches.Length);
    }

    [Fact]
    public void EmptySubstringValueNeverMatches()
    {
        var table = Build("[data-role^=\"\"] { color: a; } [data-role*=\"\"] { color: b; }");

        var node = new IgniteSupportedDomNode();
        node.SetAttribute("data-role", "anything");

        Assert.Empty(table.MatchRules(node));
    }

    [Fact]
    public void ExcludesSelectorsUsingConstructsThatNeedStateOrGeneratedContent()
    {
        var table = Build(
            ".btn:hover { color: a; }" +
            ".btn::before { color: b; }" +
            ".btn:not(.x) { color: c; }" +
            ".btn:is(.x) { color: d; }" +
            ".btn:has(.x) { color: e; }" +
            ".btn { color: f; }");

        var node = new IgniteSupportedDomNode { TagName = "div" };
        node.AddClass("btn");

        var matches = table.MatchRules(node);

        Assert.Single(matches);
        Assert.Equal("f", Value(matches[0]));
    }

    [Fact]
    public void OnlyReturnsRulesOfMatchingSelectors()
    {
        var table = Build(".a { color: a; margin: 1px; } .b { color: b; padding: 2px; }");

        var node = new IgniteSupportedDomNode();
        node.AddClass("a");

        var matches = table.MatchRules(node);

        Assert.Equal(2, matches.Length);
        Assert.Equal(0, matches[0].Order);
        Assert.Equal(1, matches[1].Order);
    }

    [Fact]
    public void FiltersConditionsWhenContextIsSupplied()
    {
        var table = Build(
            "@media (min-width: 1px) { .a { color: gated; } }",
            Condition.AlwaysFalseCompiler);

        var node = new IgniteSupportedDomNode();
        node.AddClass("a");

        Assert.Single(table.MatchRules(node));
        Assert.Empty(table.MatchRules(node, new RendererContext()));
    }

    [Fact]
    public void SpanOverloadReportsTotalWhenDestinationIsTooSmall()
    {
        var table = Build(".a { color: a; margin: 0; padding: 0; }");

        var node = new IgniteSupportedDomNode();
        node.AddClass("a");

        Span<MatchedRule> destination = stackalloc MatchedRule[1];

        Assert.Equal(3, table.MatchRules(node, null, destination));
        Assert.Equal("a", Value(destination[0]));
    }

    [Fact]
    public void ClassicTagClassAndIdCompoundMatches()
    {
        var table = Build("div.card#hero[data-x] { color: hero; }");

        var node = new IgniteSupportedDomNode { TagName = "div", Id = "hero" };
        node.AddClass("card");
        node.SetAttribute("data-x", "");

        Assert.Single(table.MatchRules(node));
    }

    [Fact]
    public void MatchesDescendantAndChildCombinators()
    {
        var table = Build(
            "div .leaf { color: descendant; }" +
            "div > .leaf { color: child; }" +
            "body > .leaf { color: not-child; }");

        var root = new IgniteSupportedDomNode { TagName = "html" };
        var body = new IgniteSupportedDomNode { TagName = "body" };
        var div = new IgniteSupportedDomNode { TagName = "div" };
        var middle = new IgniteSupportedDomNode { TagName = "section" };
        var leaf = new IgniteSupportedDomNode { TagName = "p" };

        leaf.AddClass("leaf");

        root.Children = [body];
        body.Children = [div];
        div.Children = [middle];
        middle.Children = [leaf];

        var matches = table.MatchRules(leaf);

        // The leaf is a grandchild of div, so only the descendant combinator holds.
        Assert.Single(matches);
        Assert.Equal("descendant", Value(matches[0]));
    }

    [Fact]
    public void MatchesSiblingCombinators()
    {
        var table = Build(
            ".first + .second { color: adjacent; }" +
            ".first ~ .second { color: general; }" +
            ".second + .first { color: backwards; }");

        // Adjacent siblings: both '+' and '~' apply, in source order.
        var root = new IgniteSupportedDomNode { TagName = "div" };
        var first = new IgniteSupportedDomNode { TagName = "span" };
        var second = new IgniteSupportedDomNode { TagName = "span" };

        first.AddClass("first");
        second.AddClass("second");

        root.Children = [first, second];

        var matches = table.MatchRules(second);

        Assert.Equal(2, matches.Length);
        Assert.Equal("adjacent", Value(matches[0]));
        Assert.Equal("general", Value(matches[1]));

        // With an element in between, '+' must not apply, '~' still does.
        var farRoot = new IgniteSupportedDomNode { TagName = "div" };
        var farFirst = new IgniteSupportedDomNode { TagName = "span" };
        var gap = new IgniteSupportedDomNode { TagName = "span" };
        var farSecond = new IgniteSupportedDomNode { TagName = "span" };

        farFirst.AddClass("first");
        farSecond.AddClass("second");

        farRoot.Children = [farFirst, gap, farSecond];

        var farMatches = table.MatchRules(farSecond);

        Assert.Single(farMatches);
        Assert.Equal("general", Value(farMatches[0]));
    }

    [Fact]
    public void BacktracksAcrossMultipleDescendantCompounds()
    {
        var table = Build(".outer .middle .leaf { color: hit; }");

        var root = new IgniteSupportedDomNode { TagName = "html" };
        var outer = new IgniteSupportedDomNode { TagName = "div" };
        var decoy = new IgniteSupportedDomNode { TagName = "div" };
        var middle = new IgniteSupportedDomNode { TagName = "section" };
        var leaf = new IgniteSupportedDomNode { TagName = "p" };

        outer.AddClass("outer");
        decoy.AddClass("middle");
        middle.AddClass("middle");
        leaf.AddClass("leaf");

        root.Children = [outer, decoy];
        outer.Children = [middle];
        middle.Children = [leaf];

        Assert.Single(table.MatchRules(leaf));
    }

    [Fact]
    public void MatchesStructuralPseudoClasses()
    {
        var table = Build(
            ":root { color: root; }" +
            ".b:first-child { color: first; }" +
            ".b:last-child { color: last; }" +
            ".b:nth-child(2) { color: second; }" +
            ".b:nth-last-child(1) { color: last-from-end; }" +
            ".b:only-child { color: only; }" +
            ".b:empty { color: empty; }");

        var root = new IgniteSupportedDomNode { TagName = "html" };
        var first = new IgniteSupportedDomNode { TagName = "div" };
        var second = new IgniteSupportedDomNode { TagName = "div" };

        first.AddClass("b");
        second.AddClass("b");
        root.Children = [first, second];

        Assert.Single(table.MatchRules(root));

        var firstMatches = table.MatchRules(first);
        Assert.Equal(2, firstMatches.Length);
        Assert.Equal("first", Value(firstMatches[0]));
        Assert.Equal("empty", Value(firstMatches[1]));

        var secondMatches = table.MatchRules(second);
        Assert.Equal(4, secondMatches.Length);
        Assert.Contains(secondMatches, match => Value(match) == "second");
        Assert.Contains(secondMatches, match => Value(match) == "last-from-end");
        Assert.Contains(secondMatches, match => Value(match) == "last");
        Assert.Contains(secondMatches, match => Value(match) == "empty");
    }

    [Fact]
    public void MatchesOfTypePseudoClasses()
    {
        var table = Build(
            "p:first-of-type { color: first-of-type; }" +
            "span:last-of-type { color: last-of-type; }" +
            "div:only-of-type { color: only-of-type; }" +
            "p:nth-of-type(2) { color: second-of-type; }");

        var root = new IgniteSupportedDomNode { TagName = "div" };
        var div = new IgniteSupportedDomNode { TagName = "div" };
        var firstParagraph = new IgniteSupportedDomNode { TagName = "p" };
        var span = new IgniteSupportedDomNode { TagName = "span" };
        var secondParagraph = new IgniteSupportedDomNode { TagName = "p" };
        var lastSpan = new IgniteSupportedDomNode { TagName = "span" };

        root.Children = [div, firstParagraph, span, secondParagraph, lastSpan];

        Assert.Contains(table.MatchRules(div), match => Value(match) == "only-of-type");
        Assert.Contains(table.MatchRules(firstParagraph), match => Value(match) == "first-of-type");
        Assert.Contains(table.MatchRules(secondParagraph), match => Value(match) == "second-of-type");
        Assert.Contains(table.MatchRules(lastSpan), match => Value(match) == "last-of-type");
    }

    [Fact]
    public void MatchesAnBMicrosyntax()
    {
        var table = Build(
            "li:nth-child(2n+1) { color: odd; }" +
            "li:nth-child(odd) { color: odd-keyword; }" +
            "li:nth-last-child(-n+2) { color: last-two; }" +
            "li:nth-child(2n) { color: even; }");

        var root = new IgniteSupportedDomNode { TagName = "ul" };
        var items = new IgniteSupportedDomNode[5];

        for (var i = 0; i < items.Length; i++)
        {
            items[i] = new IgniteSupportedDomNode { TagName = "li" };
        }

        root.Children = items;

        // Positions are 1 based; 'odd' and 2n+1 select item 1 and 5.
        var first = table.MatchRules(items[0]);
        Assert.Contains(first, match => Value(match) == "odd");
        Assert.Contains(first, match => Value(match) == "odd-keyword");
        Assert.DoesNotContain(first, match => Value(match) == "even");
        Assert.DoesNotContain(first, match => Value(match) == "last-two");

        var second = table.MatchRules(items[1]);
        Assert.Contains(second, match => Value(match) == "even");
        Assert.DoesNotContain(second, match => Value(match) == "odd");
        Assert.DoesNotContain(second, match => Value(match) == "last-two");

        // Item 4 is even and second from the end; item 5 is odd and last.
        var fourth = table.MatchRules(items[3]);
        Assert.Contains(fourth, match => Value(match) == "even");
        Assert.Contains(fourth, match => Value(match) == "last-two");

        var fifth = table.MatchRules(items[4]);
        Assert.Contains(fifth, match => Value(match) == "odd");
        Assert.Contains(fifth, match => Value(match) == "last-two");
        Assert.DoesNotContain(fifth, match => Value(match) == "even");
    }

    [Fact]
    public void PseudoClassAddsClassSpecificity()
    {
        var table = Build("p { color: type; } p:first-child { color: pseudo; }");

        var root = new IgniteSupportedDomNode { TagName = "div" };
        var paragraph = new IgniteSupportedDomNode { TagName = "p" };
        root.Children = [paragraph];

        var matches = table.MatchRules(paragraph);

        Assert.Equal(2, matches.Length);
        Assert.Equal("type", Value(matches[0]));
        Assert.Equal("pseudo", Value(matches[1]));
        Assert.Equal(1, matches[1].ClassCount);
        Assert.Equal(1, matches[1].TypeCount);
    }
}
