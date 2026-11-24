using Donkeh.Windowing;

namespace Donkeh.FrameworkTests;

/// <summary>
/// Coverage for the engine-neutral plugin/script layer. No CEF browser is
/// created in these tests.
/// </summary>
public sealed class WebViewPluginTests
{
    [Fact]
    public void PreloadScriptRunsAtDocumentStartAndIsIsolated()
    {
        var script = WebViewScript.Preload("init", "window.value = 1;");

        Assert.Equal(WebViewScriptTiming.DocumentStart, script.RunAt);
        Assert.Contains("(function(){", script.GetInjectedSource());
        Assert.True(script.AppliesTo(new Uri("https://example.com"), true));
    }

    [Fact]
    public void ScriptFilterAndFrameScopeAreHonoured()
    {
        var script = new WebViewScript(
            "scoped",
            "window.value = 1;",
            filter: uri => uri.Host == "example.com");

        Assert.True(script.AppliesTo(new Uri("https://example.com/page"), true));
        Assert.False(script.AppliesTo(new Uri("https://other.com/page"), true));
        Assert.False(script.AppliesTo(new Uri("https://example.com/page"), false));
    }

    [Fact]
    public void DisabledScriptIsNotInjected()
    {
        var script = new WebViewScript("off", "x", enabled: false);

        Assert.False(script.AppliesTo(new Uri("https://example.com"), true));
    }

    [Fact]
    public void NonIsolatedScriptIsInjectedVerbatim()
    {
        var script = new WebViewScript("raw", "window.value = 1;", isolate: false);

        Assert.Equal(script.Source, script.GetInjectedSource());
    }

    [Fact]
    public void PluginContributesScriptsAndLifecycleDefaults()
    {
        var plugin = new CountingPlugin();

        Assert.Equal("counting", plugin.Name);
        Assert.True(plugin.Enabled);
        Assert.Single(plugin.Scripts);
    }

    [Fact]
    public void OptionsExposePluginAndBindingRegistries()
    {
        var options = new WebViewOptions();
        options.AddPlugin(new CountingPlugin());
        options.AddScript(WebViewScript.AfterLoad("after", "x"));
        options.RegisterObject("api", new object());
        options.OnMessage("save", _ => { });

        Assert.Single(options.Plugins);
        Assert.Single(options.Scripts);
        Assert.Single(options.Bindings);
        Assert.True(options.MessageHandlers.ContainsKey("save"));
        Assert.True(options.EnablePluginBridge);
        Assert.Equal("donkeh", options.BridgeGlobalName);
    }

    [Fact]
    public void BindingOptionsDefaultsAreElectronLike()
    {
        var options = new WebViewBindingOptions();

        Assert.True(options.Async);
        Assert.True(options.CamelCaseNames);
    }

    private sealed class CountingPlugin : WebViewPlugin
    {
        public override string Name => "counting";

        public override IReadOnlyList<WebViewScript> Scripts =>
        [
            WebViewScript.Preload("counting.preload", "window.count = 0;")
        ];
    }
}
