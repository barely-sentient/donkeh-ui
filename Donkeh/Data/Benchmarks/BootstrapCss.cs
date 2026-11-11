namespace Donkeh.Data.Benchmarks;

/// <summary>
/// Loads the Bootstrap CDN build used by every benchmark. The stylesheet is
/// vendored next to the project and copied to the output, so normal runs never
/// touch the network. When the file is missing (for example from a trimmed
/// output directory) the same CDN URL is downloaded once and cached in the temp
/// directory, keeping runs reproducible across processes.
/// </summary>
internal static class BootstrapCss
{
    public const string Version = "5.3.8";

    public const string CdnUrl =
        "https://cdn.jsdelivr.net/npm/bootstrap@" + Version + "/dist/css/bootstrap.min.css";

    private const string FileName = "bootstrap.min.css";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// Returns the stylesheet text. <c>IGNITE_BOOTSTRAP_CSS</c> can point at a
    /// local file to override the vendored copy, which is useful when comparing
    /// Bootstrap versions.
    /// </summary>
    public static string Load()
    {
        if (Environment.GetEnvironmentVariable("IGNITE_BOOTSTRAP_CSS") is { Length: > 0 } overridePath
            && File.Exists(overridePath))
        {
            return File.ReadAllText(overridePath);
        }

        var local = Path.Combine(AppContext.BaseDirectory, FileName);

        if (File.Exists(local))
        {
            return File.ReadAllText(local);
        }

        var cacheDirectory = Path.Combine(Path.GetTempPath(), "ignite-css-benchmarks");
        var cached = Path.Combine(cacheDirectory, $"bootstrap-{Version}.min.css");

        if (File.Exists(cached))
        {
            return File.ReadAllText(cached);
        }

        try
        {
            Directory.CreateDirectory(cacheDirectory);

            var css = Http.GetStringAsync(CdnUrl).GetAwaiter().GetResult();
            File.WriteAllText(cached, css);
            return css;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not load Bootstrap {Version}. Place '{FileName}' next to the "
                + "benchmark binary, or set IGNITE_BOOTSTRAP_CSS to a local stylesheet. "
                + $"CDN: {CdnUrl}",
                ex);
        }
    }
}
