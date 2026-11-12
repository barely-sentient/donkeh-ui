using Donkeh.Data.Dom;
using static Donkeh.UILab._;
using static Donkeh.UILab.Routing;

namespace Donkeh.UILab.Tests;

public sealed class RouterTests
{
    [Fact]
    public void RouterRendersInitialRouteAndChangesOnLinkClick()
    {
        var renderer = new Renderer();
        renderer.Mount(
            AppRouter("/",
                AppRoutes(
                    Route("/", H("div", Text("Home"), Link("/about", Text("About")))),
                    Route("/about", Text("About")),
                    Route("/docs/*", Text("Docs")))));

        Assert.Contains("Home", CollectText(renderer.Root));

        var link = FindAnchor(renderer.Root, "/about");
        Assert.True(link.TryGetEventHandler("click", out var handler));

        ((Action)handler!)();

        Assert.Contains("About", CollectText(renderer.Root));
    }

    [Fact]
    public void RouterCanSetPageFromContext()
    {
        var renderer = new Renderer();
        renderer.Mount(
            AppRouter("/",
                H("div",
                    C<NavigateButton>(),
                    AppRoutes(
                        Route("/", Text("Home")),
                        Route("/settings", Text("Settings"))))));

        Assert.Contains("Home", CollectText(renderer.Root));

        var button = FindElement(renderer.Root, "button");
        Assert.True(button.TryGetEventHandler("click", out var handler));

        ((Action)handler!)();

        Assert.Contains("Settings", CollectText(renderer.Root));
    }

    [Fact]
    public void RouterFallsBackWhenNothingMatches()
    {
        var renderer = new Renderer();
        renderer.Mount(
            AppRouter("/missing",
                C<AppRoutes, AppRoutesProps>(new AppRoutesProps
                {
                    Children = [Route("/", Text("Home"))],
                    Fallback = Text("Not found")
                })));

        Assert.Contains("Not found", CollectText(renderer.Root));
    }

    [Fact]
    public void LinkMarksItselfActive()
    {
        var renderer = new Renderer();
        renderer.Mount(
            AppRouter("/docs/intro",
                AppRoutes(
                    Route("/docs/*",
                        H("div", C<PathText>(), Link("/docs/intro", Text("Intro")))))));

        Assert.Contains("/docs/intro", CollectText(renderer.Root));

        var link = FindAnchor(renderer.Root, "/docs/intro");
        Assert.True(link.HasClass("active"));
    }

    [Theory]
    [InlineData("/", "/", true)]
    [InlineData("about", "/about", true)]
    [InlineData("/about/", "/about", true)]
    [InlineData("/docs/*", "/docs", true)]
    [InlineData("/docs/*", "/docs/intro", true)]
    [InlineData("/docs/*", "/doc", false)]
    [InlineData("*", "/anything", true)]
    [InlineData("/about", "/", false)]
    public void MatchesRoutes(string route, string current, bool expected)
    {
        Assert.Equal(expected, Router.Matches(route, current));
    }

    [Theory]
    [InlineData("/", "/", true)]
    [InlineData("/docs", "/docs/intro", true)]
    [InlineData("/docs", "/docs", true)]
    [InlineData("/docs", "/other", false)]
    [InlineData("/", "/other", false)]
    public void LinkActiveMatching(string link, string current, bool expected)
    {
        Assert.Equal(expected, Router.IsActive(link, current));
    }

    private static string CollectText(Node? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        Collect(node, builder);
        return builder.ToString();
    }

    private static void Collect(Node node, System.Text.StringBuilder builder)
    {
        builder.Append(node.TextContent);

        for (var i = 0; i < node.Children.Length; i++)
        {
            if (node.Children[i] is Node child)
            {
                Collect(child, builder);
            }
        }
    }

    private static ElementNode FindAnchor(Node? node, string href)
    {
        return FindElement(node, "a", href);
    }

    private static ElementNode FindElement(Node? node, string tag, string? href = null)
    {
        if (node is null)
        {
            return null!;
        }

        if (node is ElementNode element
            && element.Tag == tag
            && (href is null
                || element.TryGetAttribute("href", out var value)
                && value == href))
        {
            return element;
        }

        for (var i = 0; i < node.Children.Length; i++)
        {
            if (node.Children[i] is not Node child)
            {
                continue;
            }

            var found = FindElement(child, tag, href);

            if (found is not null)
            {
                return found;
            }
        }

        return null!;
    }
}

public sealed class NavigateButton : Component
{
    public override Node Render()
    {
        var setPage = UseContext(Router.SetPage);
        return H("button", Text("Go")).OnClick(() => setPage("/settings"));
    }
}

public sealed class PathText : Component
{
    public override Node Render() => Text(UseContext(Router.Path));
}
