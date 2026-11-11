using Donkeh.Data;
using Donkeh.Data.Dom;

namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Tests for the per-frame configuration object: its defaults, the scroll
/// offset precedence rules, the length/renderer contexts it derives and the
/// signature used to invalidate cached frames.
/// </summary>
public class LayoutOptionsTests
{
    [Fact]
    public void DefaultsDescribeADesktopViewportWithOptimisationOn()
    {
        var options = new LayoutOptions();

        Assert.Equal(1024, options.ViewportWidth);
        Assert.Equal(768, options.ViewportHeight);
        Assert.Equal(Orientation.Horizontal, options.Orientation);
        Assert.Equal(1, options.DevicePixelRatio);
        Assert.Equal(16, options.RootFontSize);
        Assert.Equal(16, options.DefaultFontSize);
        Assert.Null(options.TextMeasurer);

        Assert.True(options.ClipToViewport);
        Assert.True(options.Optimise);
        Assert.True(options.CullOffscreen);
        Assert.True(options.MergeDraws);
        Assert.False(options.CompactConstants);
        Assert.False(options.IncludeFrameMarkers);
        Assert.True(options.EnableCaching);
        Assert.True(options.ApplyScrollOffsets);
        Assert.Equal(512, options.MaxLayoutDepth);

        Assert.Null(options.ScrollOffsets);
        Assert.Null(options.Context);
        Assert.Null(options.TextProvider);
    }

    [Fact]
    public void ScrollOffsetFallsBackToTheNodeWhenNoOverrideExists()
    {
        var options = new LayoutOptions();
        var node = new IgniteSupportedDomNode { ScrollOffsetX = 12, ScrollOffsetY = 34 };

        Assert.Equal(12, options.GetScrollOffsetX(node), 3);
        Assert.Equal(34, options.GetScrollOffsetY(node), 3);
    }

    [Fact]
    public void ScrollOffsetPrefersThePerNodeOverride()
    {
        var node = new IgniteSupportedDomNode { ScrollOffsetX = 12, ScrollOffsetY = 34 };
        var options = new LayoutOptions
        {
            ScrollOffsets = new Dictionary<IgniteSupportedDomNode, Point>
            {
                [node] = new Point(100, 200)
            }
        };

        Assert.Equal(100, options.GetScrollOffsetX(node), 3);
        Assert.Equal(200, options.GetScrollOffsetY(node), 3);
    }

    [Fact]
    public void ScrollOffsetIsIgnoredWhenApplyScrollOffsetsIsFalse()
    {
        var node = new IgniteSupportedDomNode { ScrollOffsetX = 12, ScrollOffsetY = 34 };
        var options = new LayoutOptions
        {
            ApplyScrollOffsets = false,
            ScrollOffsets = new Dictionary<IgniteSupportedDomNode, Point>
            {
                [node] = new Point(100, 200)
            }
        };

        Assert.Equal(0, options.GetScrollOffsetX(node));
        Assert.Equal(0, options.GetScrollOffsetY(node));
    }

    [Fact]
    public void ScrollOffsetForAnUnrelatedNodeUsesTheNodeValues()
    {
        var known = new IgniteSupportedDomNode();
        var other = new IgniteSupportedDomNode { ScrollOffsetX = 7, ScrollOffsetY = 9 };
        var options = new LayoutOptions
        {
            ScrollOffsets = new Dictionary<IgniteSupportedDomNode, Point>
            {
                [known] = new Point(1, 2)
            }
        };

        Assert.Equal(7, options.GetScrollOffsetX(other), 3);
        Assert.Equal(9, options.GetScrollOffsetY(other), 3);
    }

    [Fact]
    public void CreateRootLengthContextCarriesTheViewportAndFontSizes()
    {
        var options = new LayoutOptions
        {
            ViewportWidth = 640,
            ViewportHeight = 480,
            RootFontSize = 20,
            DefaultFontSize = 18
        };

        var context = options.CreateRootLengthContext();

        Assert.Equal(18, context.FontSize, 3);
        Assert.Equal(20, context.RootFontSize, 3);
        Assert.Equal(640, context.ViewportWidth, 3);
        Assert.Equal(480, context.ViewportHeight, 3);
        Assert.Equal(640, context.PercentBasis, 3);
        Assert.Equal(640, context.AvailableWidth, 3);
        Assert.Equal(480, context.AvailableHeight, 3);
    }

    [Fact]
    public void CreateRendererContextCreatesOneWithTheViewportAndOrientation()
    {
        var options = new LayoutOptions
        {
            ViewportWidth = 321.6f,
            ViewportHeight = 123.4f,
            Orientation = Orientation.Vertical
        };

        var context = options.CreateRendererContext();

        Assert.Equal(322, context.ViewportWidth);
        Assert.Equal(123, context.ViewportHeight);
        Assert.Equal(Orientation.Vertical, context.Orientation);
        Assert.True(context.Version > 0);
    }

    [Fact]
    public void CreateRendererContextReusesTheSuppliedContext()
    {
        var supplied = new RendererContext();
        var options = new LayoutOptions { Context = supplied, ViewportWidth = 10, ViewportHeight = 20 };

        var context = options.CreateRendererContext();

        Assert.Same(supplied, context);
        Assert.Equal(10, context.ViewportWidth);
        Assert.Equal(20, context.ViewportHeight);
    }

    [Fact]
    public void CreateRendererContextOnlyBumpsTheVersionWhenSomethingChanges()
    {
        var supplied = new RendererContext
        {
            ViewportWidth = 100,
            ViewportHeight = 200
        };
        var options = new LayoutOptions
        {
            Context = supplied,
            ViewportWidth = 100,
            ViewportHeight = 200
        };

        var version = supplied.Version;
        options.CreateRendererContext();

        Assert.Equal(version, supplied.Version);
    }

    [Fact]
    public void FrameSignatureChangesWithTheViewport()
    {
        var a = new LayoutOptions { ViewportWidth = 100 };
        var b = new LayoutOptions { ViewportWidth = 101 };

        Assert.NotEqual(a.FrameSignature(), b.FrameSignature());
    }

    [Fact]
    public void FrameSignatureChangesWithEveryLayoutRelevantOption()
    {
        var baseline = new LayoutOptions();
        var signature = baseline.FrameSignature();

        Assert.NotEqual(signature, new LayoutOptions { ViewportHeight = 1 }.FrameSignature());
        Assert.NotEqual(signature, new LayoutOptions { Orientation = Orientation.Vertical }.FrameSignature());
        Assert.NotEqual(signature, new LayoutOptions { RootFontSize = 17 }.FrameSignature());
        Assert.NotEqual(signature, new LayoutOptions { DefaultFontSize = 17 }.FrameSignature());
        Assert.NotEqual(signature, new LayoutOptions { DevicePixelRatio = 2 }.FrameSignature());
        Assert.NotEqual(signature, new LayoutOptions { ApplyScrollOffsets = false }.FrameSignature());
    }

    [Fact]
    public void FrameSignatureAccountsForScrollOverrides()
    {
        var node = new IgniteSupportedDomNode();
        var plain = new LayoutOptions();
        var scrolled = new LayoutOptions
        {
            ScrollOffsets = new Dictionary<IgniteSupportedDomNode, Point>
            {
                [node] = new Point(5, 5)
            }
        };

        Assert.NotEqual(plain.FrameSignature(), scrolled.FrameSignature());
    }
}
