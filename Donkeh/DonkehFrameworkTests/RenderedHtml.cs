using Donkeh.Testing;
using Donkeh.UILab;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace Donkeh.FrameworkTests;

/// <summary>
/// Builds a small, real HTML document for the default-style tests. Every page
/// contains <c>html &gt; body</c> so the user-agent document rules and
/// inheritance are exercised exactly as they are in the window.
/// </summary>
internal static class RenderedHtml
{
    /// <summary>Mounts an <c>html &gt; head + body</c> document.</summary>
    public static ComponentTestHarness Page(params Node[] bodyChildren) =>
        ComponentTest.Render(
            Html(new Props(),
                Head(new Props(), Title(new Props(), Text("Defaults"))),
                Body(new Props(), bodyChildren)));

    /// <summary>Mounts one element inside the standard document.</summary>
    public static TestElement Element(
        string tag,
        Props? props = null,
        params Node[] children)
    {
        var page = Page(H(tag, props, children));
        return page.Find(tag);
    }

    /// <summary>Mounts one element with a single text child.</summary>
    public static TestElement TextElement(string tag, Props? props = null) =>
        Element(tag, props, Text("Sample"));
}
