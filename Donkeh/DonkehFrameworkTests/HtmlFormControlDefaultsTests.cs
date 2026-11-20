using Donkeh.LayoutLab;
using Donkeh.Testing;
using Donkeh.UILab;
using static Donkeh.UILab._;

namespace Donkeh.FrameworkTests;

/// <summary>
/// Form control defaults. Chrome derives these from native widget appearance;
/// the engine keeps the metrics, typography, borders and surfaces and paints a
/// simple substitute for the widget itself.
/// </summary>
public sealed class HtmlFormControlDefaultsTests
{
    [Fact]
    public void TextInputUsesChromesFieldMetrics()
    {
        var input = RenderedHtml.Element("input");
        var style = input.Style!;
        var box = input.Box!;

        Assert.Equal(Display.InlineBlock, style.Display);
        Assert.Equal(BoxSizing.BorderBox, style.BoxSizing);
        Assert.Equal(177, box.BorderBox.Width, 2);
        Assert.Equal(21, box.BorderBox.Height, 2);
        Assert.Equal(1, box.Padding.Top, 3);
        Assert.Equal(2, box.Padding.Left, 3);
        Assert.Equal(1, box.Border.Top, 3);
        Assert.Equal(BorderStyle.Solid, style.BorderStyleTop);
        Assert.Equal(0xFF767676u, style.BorderColorTop);
        Assert.Equal(CssColor.White, style.BackgroundColor);
        Assert.Equal(WhiteSpace.NoWrap, style.WhiteSpace);
        Assert.Equal("Arial, sans-serif", style.FontFamily);
        Assert.Equal(13.3333f, style.ResolveFontSize(new LengthContext(fontSize: 16)), 2);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("password")]
    [InlineData("search")]
    [InlineData("tel")]
    [InlineData("url")]
    public void TextLikeInputsShareTheTextFieldDefaults(string type)
    {
        var input = RenderedHtml.Element("input", new Props { Type = type });

        Assert.Equal(177, input.Bounds.Width, 2);
        Assert.Equal(21, input.Bounds.Height, 2);
        Assert.Equal(0xFF767676u, input.Style!.BorderColorTop);
    }

    [Fact]
    public void TextareaUsesChromesFieldMetrics()
    {
        var textarea = RenderedHtml.Element("textarea");
        var style = textarea.Style!;
        var box = textarea.Box!;

        Assert.Equal(Display.InlineBlock, style.Display);
        Assert.Equal(BoxSizing.BorderBox, style.BoxSizing);
        Assert.Equal(168, box.BorderBox.Width, 2);
        Assert.Equal(36, box.BorderBox.Height, 2);
        Assert.Equal(2, box.Padding.Top, 3);
        Assert.Equal(1, box.Border.Top, 3);
        Assert.Equal(BorderStyle.Solid, style.BorderStyleTop);
        Assert.Equal(0xFF767676u, style.BorderColorTop);
        Assert.Equal(CssColor.White, style.BackgroundColor);
        Assert.Equal("monospace", style.FontFamily);
        Assert.Equal(WhiteSpace.PreWrap, style.WhiteSpace);
        Assert.Equal(Overflow.Auto, style.OverflowY);
    }

    [Fact]
    public void ButtonUsesChromesButtonSurfaceAndBorder()
    {
        var button = RenderedHtml.TextElement("button");
        var style = button.Style!;
        var box = button.Box!;

        Assert.Equal(Display.InlineBlock, style.Display);
        Assert.Equal(BoxSizing.BorderBox, style.BoxSizing);
        Assert.Equal(21, box.BorderBox.Height, 2);
        Assert.Equal(1, box.Padding.Top, 3);
        Assert.Equal(6, box.Padding.Left, 3);
        Assert.Equal(1, box.Border.Top, 3);
        Assert.Equal(BorderStyle.Solid, style.BorderStyleTop);
        Assert.Equal(0xFF767676u, style.BorderColorTop);
        Assert.Equal(0xFFF0F0F0u, style.BackgroundColor);
        Assert.NotNull(style.BackgroundImages);
        Assert.NotNull(style.BoxShadows);
        Assert.Equal(TextAlign.Center, style.TextAlign);
        Assert.Equal(WhiteSpace.Pre, style.WhiteSpace);
        Assert.Equal(Cursor.Default, style.Cursor);
        Assert.Equal(2, style.BorderRadius.TopLeft.Value, 3);
    }

    [Fact]
    public void ButtonsUseDistinctHoverAndPressedSurfaces()
    {
        var page = RenderedHtml.Page(H("button", new Props { Id = "button" }, Text("Button")));
        var button = page.Find("#button");

        var resting = button.Style!;
        page.Hover("#button");
        var hovered = button.Style!;
        page.MouseDown("#button");
        var pressed = button.Style!;

        Assert.NotEqual(resting.BackgroundColor, hovered.BackgroundColor);
        Assert.NotEqual(hovered.BackgroundColor, pressed.BackgroundColor);
        Assert.Equal(BorderStyle.Solid, pressed.BorderStyleTop);
        Assert.NotNull(pressed.BoxShadows);
    }

    [Fact]
    public void ButtonFocusRingUsesChromesInkRingWithAGap()
    {
        var page = RenderedHtml.Page(
            H("button", new Props { Id = "button" }, Text("Submit")),
            H("input", new Props { Id = "submit", Type = "submit", Value = "Submit" }));

        page.Focus("#button");

        var style = page.Find("#button").Style!;
        Assert.Equal(2, style.OutlineWidth.Resolve(new LengthContext(fontSize: 16)), 3);
        Assert.Equal(BorderStyle.Solid, style.OutlineStyle);
        Assert.Equal(0xFF101010u, style.ResolveOutlineColor());
        Assert.Equal(1, style.OutlineOffset.Resolve(new LengthContext(fontSize: 16)), 3);

        page.Focus("#submit");

        var submit = page.Find("#submit").Style!;
        Assert.Equal(0xFF101010u, submit.ResolveOutlineColor());
        Assert.Equal(1, submit.OutlineOffset.Resolve(new LengthContext(fontSize: 16)), 3);
    }

    [Theory]
    [InlineData("button")]
    [InlineData("submit")]
    [InlineData("reset")]
    public void ButtonInputTypesMatchTheButtonDefaults(string type)
    {
        var button = RenderedHtml.Element("input", new Props { Type = type });
        var style = button.Style!;

        Assert.Equal(0xFFF0F0F0u, style.BackgroundColor);
        Assert.Equal(BorderStyle.Solid, style.BorderStyleTop);
        Assert.Equal(21, button.Bounds.Height, 2);
        Assert.Equal(6, button.Box!.Padding.Left, 3);
    }

    [Fact]
    public void SelectUsesChromesFieldBorderAndNoRadius()
    {
        var select = RenderedHtml.Element("select", null,
            H("option", null, Text("One")),
            H("option", null, Text("Two")));

        var style = select.Style!;
        var box = select.Box!;

        Assert.Equal(Display.InlineBlock, style.Display);
        Assert.Equal(BoxSizing.BorderBox, style.BoxSizing);
        Assert.Equal(19, box.BorderBox.Height, 2);
        Assert.Equal(1, box.Border.Top, 3);
        Assert.Equal(BorderStyle.Solid, style.BorderStyleTop);
        Assert.Equal(0xFF767676u, style.BorderColorTop);
        Assert.Equal(CssColor.White, style.BackgroundColor);
        Assert.Equal(0, style.BorderRadius.TopLeft.Value, 3);
        Assert.Equal(WhiteSpace.Pre, style.WhiteSpace);
    }

    [Theory]
    [InlineData("checkbox", 3, 3, 3, 4)]
    [InlineData("radio", 3, 3, 0, 5)]
    public void CheckableControlsUseChromesThirteenPixelBox(
        string type,
        float top,
        float right,
        float bottom,
        float left)
    {
        var input = RenderedHtml.Element("input", new Props { Type = type });
        var box = input.Box!;

        Assert.Equal(Display.InlineBlock, input.Style!.Display);
        Assert.Equal(13, box.BorderBox.Width, 2);
        Assert.Equal(13, box.BorderBox.Height, 2);
        Assert.Equal(top, box.Margin.Top, 3);
        Assert.Equal(right, box.Margin.Right, 3);
        Assert.Equal(bottom, box.Margin.Bottom, 3);
        Assert.Equal(left, box.Margin.Left, 3);
    }

    [Fact]
    public void PasswordValueIsPaintedAsBullets()
    {
        var input = RenderedHtml.Element("input", new Props
        {
            Type = "password",
            Value = "John212"
        });

        Assert.Equal("•••••••", input.Box!.Text!.Text);
    }

    [Fact]
    public void CheckboxUsesASoftCornerWhileRadioStaysRound()
    {
        var checkbox = RenderedHtml.Element("input", new Props { Type = "checkbox" });
        var radio = RenderedHtml.Element("input", new Props { Type = "radio" });

        Assert.Equal(2, checkbox.Style!.BorderRadius.TopLeft.Value, 3);
        Assert.Equal(LengthUnit.Percent, radio.Style!.BorderRadius.TopLeft.Unit);
        Assert.Equal(50, radio.Style!.BorderRadius.TopLeft.Value, 3);
    }

    [Fact]
    public void ColorInputUsesAPointingCursor()
    {
        var color = RenderedHtml.Element("input", new Props { Type = "color" });

        Assert.Equal(Cursor.Pointer, color.Style!.Cursor);
    }

    [Fact]
    public void FileInputPaintsAChooseFileButtonAndFileName()
    {
        var empty = RenderedHtml.Element("input", new Props { Type = "file" });
        var chosen = RenderedHtml.Element("input", new Props
        {
            Type = "file",
            Value = "photo.png"
        });

        Assert.Equal("No file chosen", empty.Box!.Text!.Text);
        Assert.Equal("photo.png", chosen.Box!.Text!.Text);
        Assert.Equal(88, empty.Box!.Padding.Left, 2);
        Assert.Equal(0, empty.Box!.Border.Top, 3);
    }

    [Fact]
    public void RangeColorAndFileKeepChromesDefaultBoxes()
    {
        var range = RenderedHtml.Element("input", new Props { Type = "range" });
        Assert.Equal(129, range.Bounds.Width, 2);
        Assert.Equal(16, range.Bounds.Height, 2);
        Assert.Equal(2, range.Box!.Margin.Top, 3);
        Assert.Equal(0, range.Box!.Border.Top, 3);

        var color = RenderedHtml.Element("input", new Props { Type = "color" });
        Assert.Equal(50, color.Bounds.Width, 2);
        Assert.Equal(27, color.Bounds.Height, 2);
        Assert.Equal(0xFFF0F0F0u, color.Style!.BackgroundColor);

        var file = RenderedHtml.Element("input", new Props { Type = "file" });
        Assert.Equal(253, file.Bounds.Width, 2);
        Assert.Equal(21, file.Bounds.Height, 2);
        Assert.Equal(0, file.Box!.Border.Top, 3);
        Assert.Equal(CssColor.Transparent, file.Style!.BackgroundColor);
    }

    [Fact]
    public void DisabledButtonUsesChromesTranslucentSurface()
    {
        var button = RenderedHtml.Element("button", new Props { Disabled = true }, Text("Disabled"));
        var style = button.Style!;

        Assert.Equal(0x4CEFEFEFu, style.BackgroundColor);
        Assert.Equal(0x4C767676u, style.BorderColorTop);
        Assert.Equal(0x4C101010u, style.Color);
        Assert.True(style.BackgroundImages is null or { Length: 0 });
        Assert.True(style.BoxShadows is null or { Length: 0 });
    }

    [Fact]
    public void DisabledTextareaUsesAFlatDisabledSurface()
    {
        var textarea = RenderedHtml.Element("textarea", new Props { Disabled = true });
        var style = textarea.Style!;

        Assert.Equal(0x4CEFEFEFu, style.BackgroundColor);
        Assert.Equal(0xFF545454u, style.Color);
        Assert.Equal(Cursor.Default, style.Cursor);
    }

    [Fact]
    public void DisabledSelectIsTranslucent()
    {
        var select = RenderedHtml.Element("select", new Props { Disabled = true });

        Assert.Equal(0.7f, select.Style!.Opacity, 3);
    }

    [Fact]
    public void FocusedControlsUseTheFocusRing()
    {
        var input = RenderedHtml.Element("input");
        input.Focus();

        var style = input.Style!;

        Assert.Equal(2, style.OutlineWidth.Resolve(new LengthContext(fontSize: 16)), 3);
        Assert.Equal(BorderStyle.Solid, style.OutlineStyle);
        Assert.Equal(0xFF0B57D0u, style.OutlineColor);
    }

    [Fact]
    public void LabelsUseTheDefaultCursorButKeepDocumentTypography()
    {
        var label = RenderedHtml.TextElement("label");
        var style = label.Style!;

        Assert.Equal(Cursor.Default, style.Cursor);
        Assert.Contains("Times New Roman", style.FontFamily);
        Assert.Equal(Display.Inline, style.Display);
    }
}
