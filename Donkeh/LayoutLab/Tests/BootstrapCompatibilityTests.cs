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
