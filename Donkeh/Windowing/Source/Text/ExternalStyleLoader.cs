using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Donkeh.Windowing;

/// <summary>
/// Resolves external CSS imports and <c>@font-face</c> sources for the native host.
/// </summary>
/// <remarks>
/// <para>
/// The CSS parser records imports and descriptors but deliberately performs no I/O.
/// This loader runs before the native host compiles author CSS: it fetches
/// <c>@import</c> targets, inlines them, and downloads the first supported font
/// source from each <c>@font-face</c> block. WOFF and WOFF2 bytes are cached on disk
/// and registered with <see cref="FontManager"/> under the declared family, weight
/// and style.
/// </para>
/// <para>
/// HTTP, file and data URIs are supported. Relative URLs resolve against the
/// stylesheet that declared them. Failures are non-fatal: an unreachable import or
/// font keeps the original rule and leaves the fallback font chain intact. The
/// browser host does not use this loader; the browser handles font and CSS URLs
/// itself.
/// </para>
/// </remarks>
internal static class ExternalStyleLoader
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly ConcurrentDictionary<string, string> CssCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> FontPaths = new(StringComparer.Ordinal);

    private static readonly Regex ImportPattern = new(
        """@import\s+(?:url\(\s*)?(?<quote>['"]?)(?<url>[^'")\s;]+)\k<quote>\s*\)?\s*(?<condition>[^;]*);""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FontFacePattern = new(
        """@font-face\s*\{(?<body>[^}]*)\}""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex UrlPattern = new(
        """url\(\s*(?<quote>['"]?)(?<url>[^'")\s]+)\k<quote>\s*\)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex IsSelectorPattern = new(
        """(?<selector>[^{}@]+?):is\((?<inner>[^()]*)\)(?<tail>[^{}]*)\{""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private const int MaxImportDepth = 8;

    /// <summary>Expands imports and registers every usable embedded font declaration.</summary>
    /// <param name="css">The author stylesheet text.</param>
    /// <param name="fonts">The font manager that receives external faces.</param>
    /// <returns>The stylesheet with imports inlined, or the original text on failure.</returns>
    public static string Load(string css, FontManager fonts)
    {
        ArgumentNullException.ThrowIfNull(css);
        ArgumentNullException.ThrowIfNull(fonts);

        if (css.Length == 0)
        {
            return css;
        }

        var baseUri = new Uri(Path.Combine(AppContext.BaseDirectory, "."));
        return Load(css, fonts, baseUri, 0, new HashSet<string>(StringComparer.Ordinal));
    }

    private static string Load(
        string css,
        FontManager fonts,
        Uri baseUri,
        int depth,
        HashSet<string> visiting)
    {
        if (depth > MaxImportDepth)
        {
            return css;
        }

        var expanded = ImportPattern.Replace(css, match => ExpandImport(
            match,
            fonts,
            baseUri,
            depth,
            visiting));

        expanded = ExpandSimpleIs(expanded);
        RegisterFonts(expanded, fonts, baseUri);
        return expanded;
    }

    /// <summary>
    /// Expands simple <c>:is(a, b)</c> selectors into comma-separated selectors.
    /// </summary>
    /// <remarks>
    /// The selector matcher treats logical pseudo-classes as unsupported rather than
    /// risking a wrong match. Icon-font stylesheets often use <c>:is(...)</c> only as
    /// a compact selector list; expanding the simple, parenthesis-free form keeps
    /// those icon rules working without changing the matcher.
    /// </remarks>
    private static string ExpandSimpleIs(string css)
    {
        if (!css.Contains(":is(", StringComparison.OrdinalIgnoreCase))
        {
            return css;
        }

        for (var pass = 0; pass < 8; pass++)
        {
            var changed = false;

            css = IsSelectorPattern.Replace(css, match =>
            {
                var selector = match.Groups["selector"].Value;
                var inner = match.Groups["inner"].Value;
                var alternatives = inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (alternatives.Length == 0)
                {
                    return match.Value;
                }

                changed = true;
                var tail = match.Groups["tail"].Value;
                return string.Join(
                    ", ",
                    alternatives.Select(alternative => selector + alternative + tail)) + "{";
            });

            if (!changed)
            {
                break;
            }
        }

        return css;
    }

    private static string ExpandImport(
        Match match,
        FontManager fonts,
        Uri baseUri,
        int depth,
        HashSet<string> visiting)
    {
        var url = match.Groups["url"].Value.Trim();

        if (url.Length == 0)
        {
            return match.Value;
        }

        var resolved = Resolve(url, baseUri);

        if (resolved is null)
        {
            return match.Value;
        }

        var key = resolved.ToString();

        if (!visiting.Add(key))
        {
            return string.Empty;
        }

        try
        {
            var imported = GetCss(resolved);

            if (imported is null)
            {
                return match.Value;
            }

            return Load(imported, fonts, resolved, depth + 1, visiting);
        }
        finally
        {
            visiting.Remove(key);
        }
    }

    private static void RegisterFonts(string css, FontManager fonts, Uri baseUri)
    {
        foreach (Match face in FontFacePattern.Matches(css))
        {
            var body = face.Groups["body"].Value;
            var family = CleanFamily(Declaration(body, "font-family"));

            if (string.IsNullOrWhiteSpace(family))
            {
                continue;
            }

            var source = ChooseSource(Declaration(body, "src"), baseUri);

            if (source is null)
            {
                continue;
            }

            var path = GetFontPath(source, out var resolved);

            if (path is null || resolved is null)
            {
                Console.WriteLine($"[Donkeh] Could not download external font '{family}' from {source}");
                continue;
            }

            var weight = ParseInt(Declaration(body, "font-weight"), 400);
            var italic = string.Equals(
                Declaration(body, "font-style")?.Trim(),
                "italic",
                StringComparison.OrdinalIgnoreCase);

            try
            {
                fonts.RegisterFont(family, path, weight, italic);
            }
            catch (Exception ex)
            {
                // An unsupported or malformed external font must not break the
                // rest of the stylesheet, but losing an icon font makes the UI
                // look broken, so leave a line in the platform log.
                Console.WriteLine(
                    $"[Donkeh] Could not register external font '{family}' from {source}: {ex.Message}");
            }
        }
    }

    private static string? ChooseSource(string? src, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(src))
        {
            return null;
        }

        string? first = null;

        foreach (Match match in UrlPattern.Matches(src))
        {
            var url = match.Groups["url"].Value.Trim();

            if (url.Length == 0)
            {
                continue;
            }

            var resolved = Resolve(url, baseUri);

            if (resolved is null)
            {
                continue;
            }

            first ??= resolved.ToString();
            var path = resolved.AbsolutePath;

            if (path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            {
                return resolved.ToString();
            }
        }

        return first;
    }

    private static string? GetFontPath(string source, out Uri? resolved)
    {
        resolved = new Uri(source);

        if (FontPaths.TryGetValue(source, out var cached) && File.Exists(cached))
        {
            return cached;
        }

        var bytes = GetBytes(resolved, out var extension);

        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        var path = Path.Combine(CacheDirectory(), hash + extension);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, bytes);
            }

            FontPaths[source] = path;
            return path;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Donkeh] Could not cache external font '{source}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Returns the writable directory used to cache downloaded fonts.</summary>
    /// <returns>An application-data path, or the system temp path as a fallback.</returns>
    /// <remarks>
    /// Android apps cannot write to the process temp directory on every image,
    /// so the local application-data folder is preferred. It is also the right
    /// place for a cache that should survive between launches.
    /// </remarks>
    private static string CacheDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.GetTempPath();
        }

        return Path.Combine(root, "donkeh", "fonts");
    }

    private static string? GetCss(Uri uri)
    {
        var key = uri.ToString();

        if (CssCache.TryGetValue(key, out var cached))
        {
            return cached.Length == 0 ? null : cached;
        }

        try
        {
            var text = uri.IsFile
                ? File.ReadAllText(uri.LocalPath)
                : Http.GetStringAsync(uri).GetAwaiter().GetResult();

            CssCache[key] = text;
            return text;
        }
        catch (Exception ex)
        {
            // A missing stylesheet is usually a missing permission or network.
            // The app still starts with the fallback font chain, so report the
            // cause once instead of failing the whole stylesheet pass.
            Console.WriteLine($"[Donkeh] Could not load external stylesheet '{uri}': {ex.Message}");
            CssCache[key] = string.Empty;
            return null;
        }
    }

    private static byte[]? GetBytes(Uri uri, out string extension)
    {
        extension = Path.GetExtension(uri.AbsolutePath);

        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
        {
            extension = ".font";
        }

        try
        {
            if (string.Equals(uri.Scheme, "data", StringComparison.OrdinalIgnoreCase))
            {
                return DecodeDataUri(uri.OriginalString);
            }

            return uri.IsFile
                ? File.ReadAllBytes(uri.LocalPath)
                : Http.GetByteArrayAsync(uri).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? DecodeDataUri(string source)
    {
        var comma = source.IndexOf(',');
        var header = comma < 0 ? source : source[..comma];
        var payload = comma < 0 ? string.Empty : source[(comma + 1)..];

        if (!header.Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
        }

        return Convert.FromBase64String(payload);
    }

    private static Uri? Resolve(string url, Uri baseUri)
    {
        url = url.Trim();

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            url = "https:" + url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        if (Uri.TryCreate(baseUri, url, out var relative))
        {
            return relative;
        }

        try
        {
            return new Uri(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, url)));
        }
        catch
        {
            return null;
        }
    }

    private static string? Declaration(string body, string name)
    {
        var match = Regex.Match(
            body,
            $@"(?:^|;)\s*{Regex.Escape(name)}\s*:\s*(?<value>[^;]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static string CleanFamily(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().Trim('"', '\'').Trim();
    }

    private static int ParseInt(string? value, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var token = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return int.TryParse(token, out var result) ? result : fallback;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Donkeh/1.0");
        return client;
    }
}
