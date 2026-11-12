namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Patterns used heavily by Bootstrap 5: cascading custom properties, nested
/// var() substitution and RGB variable composition.
/// </summary>
public sealed class BootstrapCompatibilityTests
{
    [Fact]
    public void LaterCustomPropertiesOverrideEarlierOnes()
    {
        const string css = """
            .btn { --button-bg: #ff0000; color: #ff0000; background-color: var(--button-bg); }
            .btn-primary { --button-bg: #0000ff; }
            """;

        var table = TestSupport.Build(css);
        Assert.Contains(
            table.Rules,
            rule => Donkeh.Parser.InternTable.GetString(rule.PropertyId)?.StartsWith("--", StringComparison.Ordinal) == true);
        Assert.Contains(
            table.Rules,
            rule => Donkeh.Parser.InternTable.GetString(rule.PropertyId) == "color");
        Assert.Contains(
            table.Rules,
            rule => Donkeh.Parser.InternTable.GetString(rule.PropertyId) == "background-color");

        var node = TestSupport.Node("button", classes: ["btn", "btn-primary"]);
        var style = TestSupport.ResolveStyle(css, node);

        Assert.Equal(0xFFFF0000u, style.Color);
        Assert.True(style.CustomProperties!.TryGetValue("--button-bg", out var value));
        Assert.Equal("#0000ff", value.Trim());
        Assert.Equal(0xFF0000FFu, style.BackgroundColor);
    }

    [Fact]
    public void OneAndTwoNumberFlexShorthandsUseAZeroBasis()
    {
        var one = TestSupport.Style("flex: 1");
        var two = TestSupport.Style("flex: 2 0");

        Assert.Equal(1, one.FlexGrow);
        Assert.Equal(0, one.FlexBasis.Value);
        Assert.Equal(LengthUnit.Px, one.FlexBasis.Unit);

        Assert.Equal(2, two.FlexGrow);
        Assert.Equal(0, two.FlexShrink);
        Assert.Equal(0, two.FlexBasis.Value);
    }

    [Fact]
    public void BootstrapTextBackgroundRgbaResolves()
    {
        const string css = """
            :root { --bs-success-rgb: 25,135,84; }
            .text-bg-success { color: #fff !important; background-color: rgba(var(--bs-success-rgb),var(--bs-bg-opacity,1)) !important; }
            """;

        var node = TestSupport.Node("span", classes: "text-bg-success");
        var style = TestSupport.ResolveStyle(css, node);

        Assert.Equal(0xFF198754u, style.BackgroundColor);
    }

    [Fact]
    public void BootstrapRowGuttersResolve()
    {
        const string css = """
            .row { --bs-gutter-x: 1.5rem; display: flex; flex-wrap: wrap; }
            .row > * { padding-left: calc(var(--bs-gutter-x) * .5); padding-right: calc(var(--bs-gutter-x) * .5); }
            """;

        var row = TestSupport.Node("div", classes: "row");
        var column = TestSupport.Node("div");
        row.Children = [column];

        var result = TestSupport.Layout(css, row);
        var box = TestSupport.Box(result, column);

        Assert.Equal(12, box.Padding.Left, 3);
        Assert.Equal(12, box.Padding.Right, 3);
    }

    [Fact]
    public void StructuralPseudoClassesInsideNotAreEvaluated()
    {
        const string css = """
            .item { border-radius: 6px; }
            .group > .item:not(:last-child) { border-top-left-radius: 0; border-bottom-left-radius: 0; }
            .group > .item:last-child { border-top-right-radius: 10px; }
            """;

        var group = TestSupport.Node("div", classes: "group");
        var first = TestSupport.Node("button", classes: "item");
        var last = TestSupport.Node("button", classes: "item");
        group.Children = [first, last];

        var result = TestSupport.Layout(css, group);
        var firstStyle = TestSupport.Box(result, first).Style;
        var lastStyle = TestSupport.Box(result, last).Style;

        // The first item is not the last child, so the negation applies.
        Assert.Equal(0, firstStyle.BorderRadius.TopLeft.Value, 3);

        // The last item must not match :not(:last-child); it keeps the base
        // radius and gains the last-child corner.
        Assert.Equal(6, lastStyle.BorderRadius.TopLeft.Value, 3);
        Assert.Equal(10, lastStyle.BorderRadius.TopRight.Value, 3);
        Assert.Equal(6, lastStyle.BorderRadius.BottomRight.Value, 3);
    }

    [Fact]
    public void SplitButtonKeepsOuterCornerRadius()
    {
        // The Bootstrap group rules that square inner corners explicitly skip
        // the dropdown toggle, so a split button keeps its right-side radius
        // even though the hidden menu is the last child of the group.
        const string css = """
            .btn { border-radius: 6px; }
            .btn-group > .btn:not(:last-child):not(.dropdown-toggle) { border-top-right-radius: 0; border-bottom-right-radius: 0; }
            .btn-group > :not(.btn-check) + .btn { border-top-left-radius: 0; border-bottom-left-radius: 0; }
            """;

        var group = TestSupport.Node("div", classes: "btn-group");
        var primary = TestSupport.Node("button", classes: ["btn", "btn-primary"]);
        var toggle = TestSupport.Node("button", classes: ["btn", "btn-primary", "dropdown-toggle", "dropdown-toggle-split"]);
        var menu = TestSupport.Node("div", classes: "dropdown-menu");
        group.Children = [primary, toggle, menu];

        var result = TestSupport.Layout(css, group);
        var primaryStyle = TestSupport.Box(result, primary).Style;
        var toggleStyle = TestSupport.Box(result, toggle).Style;

        Assert.Equal(0, toggleStyle.BorderRadius.TopLeft.Value, 3);
        Assert.Equal(6, toggleStyle.BorderRadius.TopRight.Value, 3);
        Assert.Equal(6, toggleStyle.BorderRadius.BottomRight.Value, 3);

        Assert.Equal(0, primaryStyle.BorderRadius.TopRight.Value, 3);
    }

    [Fact]
    public void SuppressedSiblingsAreIgnoredByStructuralSelectors()
    {
        // A closed dropdown menu is still in the host tree but generates no box.
        // The split toggle must be treated as the last rendered child so the
        // outer right corner keeps its radius.
        const string css = """
            .btn { border-radius: 6px; }
            .split > .btn { border-radius: 0; }
            .split > .btn:last-child { border-top-right-radius: 6px; border-bottom-right-radius: 6px; }
            .menu { display: none; }
            """;

        var group = TestSupport.Node("div", classes: "split");
        var primary = TestSupport.Node("button", classes: "btn");
        var toggle = TestSupport.Node("button", classes: "btn");
        var menu = TestSupport.Node("div", classes: "menu");
        group.Children = [primary, toggle, menu];

        var result = TestSupport.Layout(css, group);
        var toggleStyle = TestSupport.Box(result, toggle).Style;

        Assert.Equal(6, toggleStyle.BorderRadius.TopRight.Value, 3);
        Assert.Equal(6, toggleStyle.BorderRadius.BottomRight.Value, 3);
        Assert.Equal(0, toggleStyle.BorderRadius.TopLeft.Value, 3);
    }

    [Fact]
    public void DisplayedSiblingsStillCountForLastChild()
    {
        const string css = """
            .item { border-top-right-radius: 6px; }
            .item:last-child { border-top-right-radius: 0; }
            """;

        var group = TestSupport.Node("div");
        var first = TestSupport.Node("button", classes: "item");
        var last = TestSupport.Node("button", classes: "item");
        group.Children = [first, last];

        var result = TestSupport.Layout(css, group);

        Assert.Equal(6, TestSupport.Box(result, first).Style.BorderRadius.TopRight.Value, 3);
        Assert.Equal(0, TestSupport.Box(result, last).Style.BorderRadius.TopRight.Value, 3);
    }

    [Fact]
    public void NestedRgbVariablesResolve()
    {
        const string css = """
            :root { --bs-dark-rgb: 33,37,41; --bs-bg-opacity: 1; }
            .bg-dark { --bs-bg-opacity: 1; background-color: rgba(var(--bs-dark-rgb),var(--bs-bg-opacity)) !important; }
            """;

        var node = TestSupport.Node("nav", classes: "bg-dark");
        var style = TestSupport.ResolveStyle(css, node);

        Assert.Equal(0xFF212529u, style.BackgroundColor);
    }
}
