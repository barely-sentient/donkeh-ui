using Donkeh.Data;
using Donkeh.LayoutLab;

namespace Donkeh.LayoutLab.Tests;

public sealed class IconContentTests
{
    [Fact]
    public void FontAwesomeVarContentResolves()
    {
        var table = TestSupport.Build("""
            .icon { --fa: "\e131"; font-family: "Font Awesome 7 Free"; font-weight: 900; }
            .icon::before { content: var(--fa)/""; }
            """);
        var root = TestSupport.Node("div");
        var icon = TestSupport.Node("i", classes: ["icon"]);
        root.Children = [icon];

        var result = TestSupport.Layout(table, root);
        var iconBox = result.Root!.Children[0];
        var pseudo = Assert.Single(iconBox.Children);

        Assert.Equal("\uE131", pseudo.Node.TextContent);
        Assert.Equal("Font Awesome 7 Free", pseudo.Style.FontFamily);
        Assert.Equal(900, pseudo.Style.FontWeight);
    }

    [Fact]
    public void ExpandedFontAwesomeSelectorListResolves()
    {
        // The native host expands simple :is(...) lists before parsing, which is
        // how Font Awesome's ::before rule becomes a matchable selector list.
        var table = TestSupport.Build("""
            .fa-solid,
            .fa {
              --_fa-family: var(--fa-family, var(--fa-style-family, 'Font Awesome 7 Free'));
              display: inline-block;
              font-family: var(--_fa-family);
              font-weight: var(--fa-style, 900);
              line-height: 1;
              width: 1.25em;
            }

            .fa-solid::before,
            .fa::before {
              content: var(--fa)/"";
            }

            .fa-tv { --fa: "\f26c"; }
            """);
        var root = TestSupport.Node("div");
        var icon = TestSupport.Node("i", classes: ["fa-solid", "fa-tv"]);
        root.Children = [icon];

        var result = TestSupport.Layout(table, root);
        var iconBox = result.Root!.Children[0];
        var pseudo = Assert.Single(iconBox.Children);

        Assert.Equal("\uF26C", pseudo.Node.TextContent);
        Assert.Equal("Font Awesome 7 Free", pseudo.Style.FontFamily);
        Assert.Equal(900, pseudo.Style.FontWeight);
    }
}
