using System.Numerics;

namespace Donkeh.LayoutLab.Tests;

public class PaintSpecsTests
{
    [Fact]
    public void TextStyleDefaultMatchesTheComputedStyleDefaults()
    {
        var style = TextStyle.Default;

        Assert.Equal("sans-serif", style.FontFamily);
        Assert.Equal(16, style.FontSize);
        Assert.Equal(400, style.FontWeight);
        Assert.False(style.Italic);
        Assert.Equal(0xFF000000u, style.Color);
        Assert.Equal(0, style.LetterSpacing);
        Assert.Equal(1.2f, style.LineHeight);
        Assert.Equal(TextAlign.Start, style.Align);
        Assert.Equal(TextDecoration.None, style.Decoration);
        Assert.Equal(WhiteSpace.Normal, style.WhiteSpace);
        Assert.Equal(Direction.LeftToRight, style.Direction);
    }

    [Fact]
    public void TextCommandComparesByValue()
    {
        var a = new TextCommand("hi", TextStyle.Default);
        var b = new TextCommand("hi", TextStyle.Default);
        var c = new TextCommand("bye", TextStyle.Default);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void BorderSideNoneIsTheEmptySide()
    {
        Assert.Equal(0, BorderSide.None.Width);
        Assert.Equal(BorderStyle.None, BorderSide.None.Style);
        Assert.Equal(0u, BorderSide.None.Color);
    }

    [Fact]
    public void BorderSpecIsEmptyOnlyWhenEverySideIsInvisible()
    {
        var invisible = new BorderSide(0, BorderStyle.Solid, CssColor.Black);
        var visible = new BorderSide(1, BorderStyle.Solid, CssColor.Black);
        var hidden = new BorderSide(1, BorderStyle.Hidden, CssColor.Black);

        Assert.True(new BorderSpec(invisible, invisible, invisible, invisible, CornerRadius.Zero).IsEmpty);
        Assert.True(new BorderSpec(hidden, hidden, hidden, hidden, CornerRadius.Zero).IsEmpty);
        Assert.False(new BorderSpec(visible, invisible, invisible, invisible, CornerRadius.Zero).IsEmpty);
        Assert.False(new BorderSpec(invisible, visible, invisible, invisible, CornerRadius.Zero).IsEmpty);
        Assert.False(new BorderSpec(invisible, invisible, visible, invisible, CornerRadius.Zero).IsEmpty);
        Assert.False(new BorderSpec(invisible, invisible, invisible, visible, CornerRadius.Zero).IsEmpty);
    }

    [Fact]
    public void ShadowAndImageSpecsAreValueRecords()
    {
        var shadow = new ShadowSpec(1, 2, 3, 4, CssColor.Black, true);
        Assert.Equal(new ShadowSpec(1, 2, 3, 4, CssColor.Black, true), shadow);
        Assert.True(shadow.Inset);

        var image = new ImageSpec("logo.png", ObjectFit.Contain, 0.5f, CssColor.White);
        Assert.Equal("logo.png", image.Source);
        Assert.Equal(ObjectFit.Contain, image.Fit);
        Assert.Equal(0.5f, image.Opacity);
        Assert.Equal(CssColor.White, image.Tint);
    }

    [Fact]
    public void FilterAndClipAndMaskNoneSingletonsAreEmpty()
    {
        Assert.True(FilterSpec.None.IsNone);
        Assert.Empty(FilterSpec.None.Functions);

        Assert.True(ClipPathSpec.None.IsNone);
        Assert.Equal(ClipPathKind.None, ClipPathSpec.None.Kind);

        Assert.True(MaskSpec.None.IsNone);
        Assert.Equal(ImageKind.None, MaskSpec.None.Kind);
    }

    [Fact]
    public void FilterFunctionCarriesKindAmountShadowAndUrl()
    {
        var shadow = new ShadowSpec(1, 2, 3, 4, CssColor.Black, false);
        var drop = new FilterFunction(FilterFunctionKind.DropShadow, 0, shadow);
        var url = new FilterFunction(FilterFunctionKind.Url, 0, default, "mask.svg");

        Assert.Equal(FilterFunctionKind.DropShadow, drop.Kind);
        Assert.Equal(shadow, drop.Shadow);
        Assert.Equal("mask.svg", url.Url);
    }

    [Fact]
    public void BackgroundImageSpecDefaultsDescribeAPlainAutoImage()
    {
        var spec = new BackgroundImageSpec { Kind = ImageKind.Url, Url = "a.png" };

        Assert.Equal(ImageKind.Url, spec.Kind);
        Assert.Equal("a.png", spec.Url);
        Assert.Equal(GradientKind.Linear, spec.GradientKind);
        Assert.Equal(BackgroundRepeat.Repeat, spec.Repeat);
        Assert.Equal(BackgroundBox.BorderBox, spec.Clip);
        Assert.Equal(BackgroundBox.BorderBox, spec.Origin);
        Assert.False(spec.Fixed);
        Assert.Empty(spec.Stops);
    }

    [Fact]
    public void TextConstantsAreInternedByContent()
    {
        var pool = new ConstantPool();

        var first = pool.AddText("hello", TextStyle.Default);
        var second = pool.AddText("hello", TextStyle.Default);
        var third = pool.AddText("other", TextStyle.Default);

        Assert.Equal(first, second);
        Assert.NotEqual(first, third);
        Assert.Equal(2, pool.Count);
        Assert.Equal("hello", pool.GetText(first).Text);
    }

    [Fact]
    public void TextStyleDifferencesProduceDifferentConstants()
    {
        var pool = new ConstantPool();
        var bold = TextStyle.Default with { FontWeight = 700 };

        Assert.NotEqual(
            pool.AddText("x", TextStyle.Default),
            pool.AddText("x", bold));
    }

    [Fact]
    public void RadiusAndTransformConstantsAreInternedByValue()
    {
        var pool = new ConstantPool();

        Assert.Equal(pool.AddRadius(CornerRadius.All(4)), pool.AddRadius(CornerRadius.All(4)));
        Assert.NotEqual(pool.AddRadius(CornerRadius.All(4)), pool.AddRadius(CornerRadius.All(5)));

        var matrix = Matrix3x2.CreateTranslation(2, 3);
        Assert.Equal(pool.AddTransform(matrix), pool.AddTransform(matrix));
        Assert.NotEqual(pool.AddTransform(matrix), pool.AddTransform(Matrix3x2.CreateTranslation(4, 5)));
    }

    [Fact]
    public void BorderShadowBackgroundImageAndScrollbarConstantsRoundTrip()
    {
        var pool = new ConstantPool();
        var side = new BorderSide(1, BorderStyle.Solid, CssColor.Black);
        var border = new BorderSpec(side, side, side, side, CornerRadius.Zero);
        var shadow = new ShadowSpec(1, 2, 3, 4, CssColor.Black, false);
        var background = new BackgroundImageSpec { Kind = ImageKind.Url, Url = "b.png" };
        var image = new ImageSpec("img.png", ObjectFit.Cover, 1, CssColor.White);
        var scrollbar = new ScrollbarSpec(true, 1, 2, 3, CssColor.White, CssColor.Black, 10);

        var borderId = pool.AddBorder(border);
        var shadowId = pool.AddShadow(shadow);
        var backgroundId = pool.AddBackground(background);
        var imageId = pool.AddImage(image);
        var scrollbarId = pool.AddScrollbar(scrollbar);

        Assert.Equal(border, pool.GetBorder(borderId));
        Assert.Equal(shadow, pool.GetShadow(shadowId));
        Assert.Same(background, pool.GetBackground(backgroundId));
        Assert.Equal(image, pool.GetImage(imageId));
        Assert.Equal(scrollbar, pool.GetScrollbar(scrollbarId));
        Assert.Equal(border, pool.Get(borderId));
    }

    [Fact]
    public void EqualValueRecordsShareOneConstant()
    {
        var pool = new ConstantPool();
        var side = new BorderSide(1, BorderStyle.Solid, CssColor.Black);
        var border = new BorderSpec(side, side, side, side, CornerRadius.Zero);

        Assert.Equal(pool.AddBorder(border), pool.AddBorder(border));
        Assert.Equal(1, pool.Count);

        var shadow = new ShadowSpec(1, 2, 3, 4, CssColor.Black, false);
        Assert.Equal(pool.AddShadow(shadow), pool.AddShadow(shadow));
        Assert.Equal(2, pool.Count);
    }

    [Fact]
    public void FilterClipPathAndMaskConstantsUseInstanceIdentity()
    {
        var pool = new ConstantPool();
        var filter = new FilterSpec([new FilterFunction(FilterFunctionKind.Blur, 2)]);
        var clip = new ClipPathSpec { Kind = ClipPathKind.Circle };
        var mask = new MaskSpec { Kind = ImageKind.Url, Source = "m.svg" };

        Assert.Equal(pool.AddFilter(filter), pool.AddFilter(filter));
        Assert.Equal(pool.AddClipPath(clip), pool.AddClipPath(clip));
        Assert.Equal(pool.AddMask(mask), pool.AddMask(mask));

        Assert.Same(filter, pool.GetFilter(pool.AddFilter(filter)));
        Assert.Same(clip, pool.GetClipPath(pool.AddClipPath(clip)));
        Assert.Same(mask, pool.GetMask(pool.AddMask(mask)));

        Assert.NotEqual(pool.AddFilter(filter), pool.AddFilter(new FilterSpec([new FilterFunction(FilterFunctionKind.Blur, 2)])));
    }
}
