using Donkeh.Testing;
using Donkeh.UILab;
using static Donkeh.UILab._;

namespace Donkeh.FrameworkTests;

/// <summary>
/// Behaviour that follows from the default element rules: block stacking,
/// inline flow, document offsets, form interaction and the event contract.
/// </summary>
public sealed class HtmlElementBehaviourTests
{
    [Fact]
    public void BlockElementsStartOnTheirOwnLine()
    {
        var page = RenderedHtml.Page(
            H("div", new Props { Id = "first" }, Text("First")),
            H("div", new Props { Id = "second" }, Text("Second")));

        var first = page.Find("#first");
        var second = page.Find("#second");

        Assert.True(second.Bounds.Y >= first.Bounds.Bottom - 0.01f);
        Assert.Equal(first.Bounds.X, second.Bounds.X, 2);
    }

    [Fact]
    public void InlineElementsShareTheSameLine()
    {
        var page = RenderedHtml.Page(
            H("p", new Props { Id = "line" },
                H("span", new Props { Id = "a" }, Text("Alpha")),
                H("span", new Props { Id = "b" }, Text("Beta"))));

        var first = page.Find("#a");
        var second = page.Find("#b");

        Assert.Equal(first.Bounds.Y, second.Bounds.Y, 2);
        Assert.True(second.Bounds.X > first.Bounds.X);
    }

    [Fact]
    public void DocumentContentStartsAtTheBodyMargin()
    {
        var page = RenderedHtml.Page(
            H("div", new Props { Id = "child" }, Text("Content")));

        var body = page.Find("body");
        var child = page.Find("#child");

        Assert.Equal(body.Box!.ContentBox.X, child.Bounds.X, 2);
        Assert.Equal(body.Box!.ContentBox.Y, child.Bounds.Y, 2);
        Assert.Equal(8, body.Box.BorderBox.X, 2);
        Assert.Equal(8, body.Box.BorderBox.Y, 2);
    }

    [Fact]
    public void ParagraphSpacingPushesTheFollowingBlockDown()
    {
        var page = RenderedHtml.Page(
            H("p", new Props { Id = "paragraph" }, Text("Copy")),
            H("div", new Props { Id = "after" }, Text("After")));

        var paragraph = page.Find("#paragraph");
        var after = page.Find("#after");

        Assert.Equal(16, paragraph.Box!.Margin.Top, 2);
        Assert.True(after.Bounds.Y >= paragraph.Bounds.Bottom + 15.99f);
    }

    [Fact]
    public void HeadingIsTallerThanBodyText()
    {
        var page = RenderedHtml.Page(
            H("h1", new Props { Id = "heading" }, Text("Heading")),
            H("p", new Props { Id = "copy" }, Text("Copy")));

        Assert.True(page.Find("#heading").Bounds.Height > page.Find("#copy").Bounds.Height);
    }

    [Fact]
    public void ListItemsAreIndentedByTheListPadding()
    {
        var page = RenderedHtml.Page(
            H("ul", new Props { Id = "list" },
                H("li", new Props { Id = "item" }, Text("Item"))));

        var list = page.Find("#list");
        var item = page.Find("#item");

        Assert.Equal(list.Bounds.X + 40, item.Bounds.X, 2);
    }

    [Fact]
    public void ClickBubblesFromAnElementToItsHandler()
    {
        var clicks = 0;

        var page = RenderedHtml.Page(
            H("button", new Props
            {
                Id = "action",
                OnClick = () => clicks++
            }, Text("Press")));

        page.Click("#action");

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void AnchorClickReachesItsHandler()
    {
        var clicks = 0;

        var page = RenderedHtml.Page(
            H("a", new Props
            {
                Id = "link",
                Href = "#target",
                OnClick = () => clicks++
            }, Text("Jump")));

        page.Click("#link");

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void CheckedAttributeMatchesTheCheckedPseudoClass()
    {
        var page = ComponentTest.Render(
            H("html", null,
                H("body", null,
                    H("input", new Props { Id = "checked", Type = "checkbox", Checked = true }),
                    H("input", new Props { Id = "unchecked", Type = "checkbox" }))),
            options => options.Stylesheets.Add(
                "input:checked { outline: 1px solid #00ff00; }"));

        Assert.Equal(0xFF00FF00u, page.Find("#checked").Style!.OutlineColor);
        Assert.Equal(0xFF000000u, page.Find("#unchecked").Style!.ResolveOutlineColor());
    }

    [Fact]
    public void DisabledAttributeMatchesTheDisabledPseudoClass()
    {
        var page = ComponentTest.Render(
            H("html", null,
                H("body", null,
                    H("input", new Props { Id = "off", Disabled = true }),
                    H("input", new Props { Id = "on" }))),
            options => options.Stylesheets.Add(
                "input:disabled { outline: 1px solid #ff0000; }"));

        Assert.Equal(0xFFFF0000u, page.Find("#off").Style!.OutlineColor);
        Assert.Equal(0xFF000000u, page.Find("#on").Style!.ResolveOutlineColor());
    }

    [Fact]
    public void InputAcceptsTypedText()
    {
        var page = RenderedHtml.Page(
            H("input", new Props { Id = "field", Value = string.Empty }));

        page.Type("#field", "hello");

        Assert.Equal("hello", page.Find("#field").Value);
    }

    [Fact]
    public void FormReceivesBubbledSubmitEvents()
    {
        var submissions = 0;

        var page = RenderedHtml.Page(
            H("form", new Props
            {
                Id = "form",
                OnSubmit = () => submissions++
            },
                H("button", new Props { Id = "submit" }, Text("Submit"))));

        page.Submit("#form");

        Assert.Equal(1, submissions);
    }

    [Fact]
    public void DetailsAndSummaryAreBlocks()
    {
        var page = RenderedHtml.Page(
            H("details", new Props { Id = "details" },
                H("summary", new Props { Id = "summary" }, Text("More")),
                H("p", null, Text("Body"))));

        Assert.True(page.Find("#details").IsVisible);
        Assert.True(page.Find("#summary").IsVisible);
        Assert.True(page.Find("#summary").Bounds.Y >= page.Find("#details").Bounds.Y);
    }

    [Fact]
    public void OpenDialogRendersInTheDocumentFlow()
    {
        var page = RenderedHtml.Page(
            H("dialog", new Props
            {
                Id = "dialog",
                Attributes = new Dictionary<string, string> { ["open"] = "open" }
            }, Text("Hello")));

        var dialog = page.Find("#dialog");

        Assert.True(dialog.IsVisible);
        Assert.Equal(16, dialog.Box!.Padding.Top, 2);
        Assert.Equal(3, dialog.Box!.Border.Top, 2);
    }
}
