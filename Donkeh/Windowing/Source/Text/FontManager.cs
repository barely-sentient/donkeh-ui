using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Donkeh.LayoutLab;
using Silk.NET.OpenGL;
using StbTrueTypeSharp;

namespace Donkeh.Windowing;

/// <summary>
/// A compact TrueType font pipeline. It supplies both layout metrics and
/// rasterised glyph atlases, so measured text and painted text always agree.
/// </summary>
/// <remarks>
/// <para>
/// Font files are discovered in the operating system's font directories and
/// read once per path; faces are shared by every <see cref="FontKey"/> that
/// resolves to them. Each key caches a <see cref="FaceSet"/> containing a
/// primary face plus a fallback chain, and each face caches glyphs per rounded
/// pixel size in one or more <see cref="FontAtlas"/> textures.
/// </para>
/// <para>
/// The manager is not thread-safe. <see cref="IgniteWindow"/> uses a single
/// instance from its frame thread for both layout
/// (<see cref="Measure(ReadOnlySpan{char}, in TextStyle)"/>) and painting
/// (<see cref="GetGlyph"/>), so measured advances and drawn glyphs come from the
/// same faces. <see cref="Initialize"/> and <see cref="Dispose"/> require a
/// current OpenGL context and must run on the thread that owns it.
/// </para>
/// <para>
/// Caches are never evicted: faces, metric buckets, glyphs and atlases live
/// until <see cref="Dispose"/>. Disposal deletes every atlas texture through the
/// GL API captured by <see cref="Initialize"/>, so it must happen before that
/// context is destroyed.
/// </para>
/// </remarks>
/// <seealso cref="TrueTypeTextMeasurer"/>
/// <seealso cref="ImageCache"/>
public sealed unsafe class FontManager : IDisposable
{
    /// <summary>Cached face sets keyed by normalised font request.</summary>
    private readonly Dictionary<FontKey, FaceSet> _faces = [];

    /// <summary>
    /// Every face ever loaded, in load order. Faces are shared between sets and
    /// disposed exactly once from this list.
    /// </summary>
    private readonly List<FontFace> _allFaces = [];

    /// <summary>The GL API captured by <see cref="Initialize"/>, or null before it runs.</summary>
    private GL? _gl;

    /// <summary>Whether <see cref="Dispose"/> has already released the atlases.</summary>
    private bool _disposed;

    /// <summary>
    /// Captures the OpenGL API used for glyph atlas creation, upload and
    /// deletion.
    /// </summary>
    /// <param name="gl">The GL context wrapper owned by the host window.</param>
    /// <remarks>
    /// Call this once after the context is created and before any painting, for
    /// example from the window's <c>Load</c> callback. Layout-only calls such as
    /// <see cref="Measure(ReadOnlySpan{char}, in TextStyle)"/> work without it,
    /// but <see cref="GetGlyph"/> cannot
    /// rasterise until it is set and the manager must be disposed before the
    /// same context goes away.
    /// </remarks>
    public void Initialize(GL gl)
    {
        _gl = gl;
    }

    /// <summary>Measures a run in CSS pixels.</summary>
    /// <param name="text">The characters to measure; may be empty.</param>
    /// <param name="style">
    /// The resolved style supplying the font family, size, weight, italic flag
    /// and letter spacing.
    /// </param>
    /// <returns>
    /// The single-line advance width, ascent and descent in CSS pixels.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Empty text still returns the primary face's ascent and descent for
    /// <see cref="TextStyle.FontSize"/>, with zero width; the renderer relies on
    /// this to position baselines. Each Unicode scalar value adds its glyph
    /// advance plus <see cref="TextStyle.LetterSpacing"/>, so spacing is added
    /// after the final rune as well.
    /// </para>
    /// <para>
    /// Adjacent runes are kerned only while both resolve to the same face.
    /// Runes the primary face lacks are measured with the first fallback face
    /// that has a glyph (see <see cref="FaceSet.Select"/>), while ascent and
    /// descent always come from the primary face, which keeps line boxes stable.
    /// Weights of 600 and above widen the total by 1 percent as a synthetic bold
    /// adjustment. There is no shaping, bidi reordering or tab expansion.
    /// </para>
    /// <para>
    /// Results are not cached here; <see cref="FontFace"/> caches metric buckets
    /// per quarter CSS pixel and rasterised glyphs per rounded pixel size.
    /// </para>
    /// </remarks>
    public TextMetrics Measure(ReadOnlySpan<char> text, in TextStyle style)
    {
        if (text.IsEmpty)
        {
            var face = ResolveFace(CreateKey(in style));
            var metrics = face.Primary.GetMetricsForCss(style.FontSize);
            return new TextMetrics(0, metrics.Ascent, metrics.Descent);
        }

        var key = CreateKey(in style);
        var set = ResolveFace(key);
        var primary = set.Primary;
        var primaryMetrics = primary.GetMetricsForCss(style.FontSize);
        var width = 0f;
        FontFace? previousFace = null;
        var previousGlyph = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            var face = set.Select(rune);
            var glyph = face.GetGlyphIndex(rune.Value);

            if (previousFace is not null && ReferenceEquals(previousFace, face))
            {
                width += face.GetKerning(previousGlyph, glyph) * face.GetScaleForCss(style.FontSize);
            }

            width += face.GetAdvance(glyph, style.FontSize) + style.LetterSpacing;
            previousFace = face;
            previousGlyph = glyph;
        }

        if (style.FontWeight >= 600)
        {
            width *= 1.01f;
        }

        return new TextMetrics(
            MathF.Max(0, width),
            primaryMetrics.Ascent,
            primaryMetrics.Descent);
    }

    /// <summary>Measures a string with the same rules as the span overload.</summary>
    /// <param name="text">The text to measure; <see langword="null"/> is treated as empty.</param>
    /// <param name="style">The resolved text style.</param>
    /// <returns>The single-line metrics in CSS pixels.</returns>
    /// <remarks>
    /// This is a convenience wrapper for callers that already hold a string;
    /// layout code should prefer the span overload to avoid the extra overload
    /// resolution. See <see cref="Measure(ReadOnlySpan{char}, in TextStyle)"/>
    /// for the fallback, kerning and letter-spacing behaviour.
    /// </remarks>
    public TextMetrics Measure(string text, in TextStyle style) => Measure(text.AsSpan(), in style);

    /// <summary>
    /// Returns the rasterised glyph for a rune together with the atlas texture
    /// that owns it.
    /// </summary>
    /// <param name="key">The resolved font key, normally from <see cref="CreateKey"/>.</param>
    /// <param name="rune">The Unicode scalar value to rasterise.</param>
    /// <param name="fontSize">The font size in CSS pixels.</param>
    /// <param name="dpi">The device-pixel ratio; values below 1 are clamped to 1.</param>
    /// <returns>
    /// The glyph's atlas texture, UV rectangle and CSS-pixel extents, plus the
    /// face and glyph index used for subsequent kerning.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The glyph is rasterised at <c>round(fontSize * dpi)</c> pixels and stored
    /// in an atlas owned by the selected face. Face selection follows the same
    /// fallback chain as <see cref="Measure(ReadOnlySpan{char}, in TextStyle)"/>,
    /// so a missing codepoint is drawn from a fallback font when one contains it
    /// and otherwise from the primary face's <c>.notdef</c> glyph.
    /// </para>
    /// <para>
    /// The returned <see cref="Glyph"/> dimensions and offsets have already been
    /// divided by <paramref name="dpi"/> and are therefore CSS pixels; the UV
    /// coordinates remain normalised texture coordinates. When no GL context has
    /// been supplied, the glyph has zero dimensions but a valid advance. This
    /// method mutates the glyph and atlas caches and is not thread-safe.
    /// </para>
    /// </remarks>
    /// <seealso cref="Kerning"/>
    internal GlyphInfo GetGlyph(FontKey key, Rune rune, float fontSize, float dpi)
    {
        var set = ResolveFace(key);
        var face = set.Select(rune);
        var glyphIndex = face.GetGlyphIndex(rune.Value);
        var glyph = face.GetGlyph(rune.Value, fontSize, MathF.Max(1, dpi), _gl);
        return new GlyphInfo(glyph.Texture, glyph, face, glyphIndex);
    }

    /// <summary>
    /// Returns the kerning adjustment between two glyphs of the same face, in
    /// CSS pixels.
    /// </summary>
    /// <param name="face">The face that owns both glyph indices.</param>
    /// <param name="leftGlyph">The glyph index of the left-hand glyph.</param>
    /// <param name="rightGlyph">The glyph index of the right-hand glyph.</param>
    /// <param name="fontSize">The font size in CSS pixels used to scale font units.</param>
    /// <returns>
    /// The signed advance adjustment to add between the glyphs; zero when the
    /// face defines no kerning pair.
    /// </returns>
    /// <remarks>
    /// Kerning is read from the font's legacy <c>kern</c> table via stb_truetype,
    /// so GPOS-only pairs may report zero. The renderer only calls this while
    /// consecutive runes stay on the same face, because indices are face-local.
    /// </remarks>
    internal float Kerning(FontFace face, int leftGlyph, int rightGlyph, float fontSize) =>
        face.GetKerning(leftGlyph, rightGlyph) * face.GetScaleForCss(fontSize);

    /// <summary>
    /// Normalises a CSS font request into the cache key used by this manager.
    /// </summary>
    /// <param name="style">The resolved text style.</param>
    /// <returns>
    /// A key with a normalised family, a binary weight of 400 or 700, and the
    /// italic flag.
    /// </returns>
    /// <remarks>
    /// Only two weights are distinguished: 600 and above map to 700 and
    /// everything else maps to 400. Variable-font axes are not supported, so
    /// intermediate weights share the nearest available face. Font sizes are not
    /// part of the key because glyphs are cached per pixel size inside the face.
    /// </remarks>
    internal FontKey CreateKey(in TextStyle style)
    {
        var family = NormalizeFamily(style.FontFamily);
        var weight = style.FontWeight >= 600 ? 700 : 400;
        return new FontKey(family, weight, style.Italic);
    }

    /// <summary>Returns the primary face for a key, ignoring the fallback chain.</summary>
    /// <param name="key">The resolved font key.</param>
    /// <returns>The primary face, never <see langword="null"/> for a usable font set.</returns>
    /// <remarks>
    /// This is used where glyph indices must be interpreted against a single
    /// face, for example when estimating intrinsic control sizes. The face may
    /// lack glyphs that the fallback chain would supply during measurement.
    /// </remarks>
    internal FontFace ResolvePrimary(FontKey key) => ResolveFace(key).Primary;

    /// <summary>
    /// Resolves and caches the primary face and fallback chain for a font key.
    /// </summary>
    /// <param name="key">The normalised family, weight and italic request.</param>
    /// <returns>The cached <see cref="FaceSet"/> for the key.</returns>
    /// <remarks>
    /// <para>
    /// The first call performs font discovery, reads the files and builds the
    /// fallback list from <see cref="FallbackFontPaths"/>, skipping any path that
    /// duplicates the primary. If the requested family is not installed, the
    /// first available fallback becomes the primary. The set is then cached for
    /// the lifetime of the manager, so installing fonts while the process runs is
    /// not observed.
    /// </para>
    /// <para>
    /// Fallback faces are shared with other keys through <see cref="LoadFace"/>,
    /// so the same atlas texture can serve several families. No lock is taken;
    /// callers must serialise access.
    /// </para>
    /// </remarks>
    private FaceSet ResolveFace(FontKey key)
    {
        if (_faces.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var paths = FindFontPaths(key);
        var primary = LoadFace(paths.Primary);
        var fallbacks = new List<FontFace>(2);

        foreach (var path in FallbackFontPaths)
        {
            if (paths.Primary is not null && string.Equals(path, paths.Primary, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fallback = LoadFace(path);

            if (fallback is not null && !fallbacks.Contains(fallback))
            {
                fallbacks.Add(fallback);
            }
        }

        if (primary is null)
        {
            primary = fallbacks.Count > 0 ? fallbacks[0] : LoadFace(null);
        }

        var set = new FaceSet(primary!, fallbacks);
        _faces[key] = set;
        return set;
    }

    /// <summary>
    /// Loads a font face from disk, reusing an existing face for the same path.
    /// </summary>
    /// <param name="path">The absolute font file path, or <see langword="null"/>.</param>
    /// <returns>
    /// The cached or newly parsed face, or <see langword="null"/> when the path is
    /// null, the file is missing, or the bytes cannot be parsed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Paths are compared case-insensitively, which is required on Windows and
    /// harmless elsewhere. The whole file is read into memory once and retained
    /// by the face for its lifetime, because stb_truetype keeps pointers into the
    /// managed byte array.
    /// </para>
    /// <para>
    /// All parse failures are swallowed and reported as <see langword="null"/> so
    /// a corrupt optional fallback font never prevents the window from starting.
    /// </para>
    /// </remarks>
    private FontFace? LoadFace(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        foreach (var face in _allFaces)
        {
            if (string.Equals(face.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return face;
            }
        }

        try
        {
            var face = new FontFace(path, File.ReadAllBytes(path));
            _allFaces.Add(face);
            return face;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reduces a CSS <c>font-family</c> list to the single family name used for
    /// file discovery.
    /// </summary>
    /// <param name="family">
    /// The raw CSS family list, for example <c>"Segoe UI", Arial, sans-serif</c>.
    /// </param>
    /// <returns>
    /// A lower-case canonical name for the generic families this engine knows,
    /// or the trimmed first family otherwise.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Only the first comma-separated entry is considered; remaining entries are
    /// not consulted during discovery. Quotes around the name are stripped.
    /// <c>sans-serif</c>, <c>system-ui</c>, <c>ui-sans-serif</c>, Helvetica and
    /// Arial all resolve to <c>segoe ui</c>; <c>serif</c>, Times and Georgia
    /// resolve to <c>times new roman</c>; <c>monospace</c>, Consolas and Courier
    /// resolve to <c>consolas</c>.
    /// </para>
    /// <para>
    /// Blank or <see langword="null"/> input returns <c>sans-serif</c> directly,
    /// without consulting the cache. The literal <c>sans-serif</c> spelling maps
    /// to <c>segoe ui</c>; unknown names keep their original casing and are later
    /// converted into file-name candidates by <see cref="FindFontPaths"/>. Results
    /// for non-blank input are memoised in <see cref="FamilyCache"/>.
    /// </para>
    /// </remarks>
    private static string NormalizeFamily(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return "sans-serif";
        }

        if (FamilyCache.TryGetValue(family, out var cached))
        {
            return cached;
        }

        var comma = family.IndexOf(',');
        var first = comma >= 0 ? family[..comma] : family;
        first = first.Trim().Trim('"', '\'').Trim();

        var normalized = first.ToLowerInvariant() switch
        {
            "" or "sans-serif" or "system-ui" or "ui-sans-serif" or "segoe ui" or "helvetica" or "helvetica neue" or "arial" => "segoe ui",
            "serif" or "ui-serif" or "times" or "times new roman" or "georgia" => "times new roman",
            "monospace" or "ui-monospace" or "consolas" or "courier new" or "courier" => "consolas",
            _ => first
        };

        FamilyCache[family] = normalized;
        return normalized;
    }

    /// <summary>
    /// Process-wide memo of raw family lists to their normalised names.
    /// </summary>
    /// <remarks>
    /// The dictionary is static and never cleared, so it survives
    /// <see cref="Dispose"/> and is shared by every <see cref="FontManager"/>
    /// instance. Keys use ordinal comparison, so differently cased spellings of
    /// the same family are cached separately even though they normalise to the
    /// same value.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, string> FamilyCache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Builds the ordered list of font file candidates for a key and selects the
    /// face that matches its weight and italic request.
    /// </summary>
    /// <param name="key">The normalised font key.</param>
    /// <returns>
    /// The selected primary path plus the regular, bold, italic and bold-italic
    /// paths found for the same family. Each tuple item may be
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Candidate families are tried in a fixed per-family order so the result is
    /// stable across machines: Segoe UI then Arial for <c>segoe ui</c>; Times New
    /// Roman then Georgia for <c>times new roman</c>; Consolas then Courier New
    /// for <c>consolas</c>. Other names are compacted (spaces removed) and tried
    /// with the Windows <c>b</c>/<c>i</c>/<c>bi</c> suffixes first, then the
    /// <c>bd</c> suffix variant, then Segoe UI as a last resort.
    /// </para>
    /// <para>
    /// Within a family, a missing style falls back to an available one: bold to
    /// regular, italic to regular, and bold-italic to bold, italic or regular.
    /// The first family with at least one installed file wins. If nothing is
    /// found, <c>segoeui</c>, <c>arial</c> or the first existing fallback path is
    /// used, and that single path fills all four slots.
    /// </para>
    /// <para>
    /// Results are cached per key in <see cref="_cachedPaths"/>; discovery does
    /// not notice fonts added later. This method touches instance state and is
    /// not thread-safe.
    /// </para>
    /// </remarks>
    private (string? Primary, string? Bold, string? Italic, string? BoldItalic) FindFontPaths(FontKey key)
    {
        var family = key.Family;
        var bold = key.Weight >= 600;
        var italic = key.Italic;

        if (_cachedPaths.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var candidates = new List<(string regular, string bold, string italic, string boldItalic)>();

        switch (family)
        {
            case "segoe ui":
                AddFontFamily(candidates, "segoeui", "segoeuib", "segoeuii", "segoeuiz");
                AddFontFamily(candidates, "arial", "arialbd", "ariali", "arialbi");
                break;
            case "times new roman":
                AddFontFamily(candidates, "times", "timesbd", "timesi", "timesbi");
                AddFontFamily(candidates, "georgia", "georgiab", "georgiai", "georgiaz");
                break;
            case "consolas":
                AddFontFamily(candidates, "consola", "consolab", "consolai", "consolaz");
                AddFontFamily(candidates, "cour", "courbd", "couri", "courbi");
                break;
            default:
            {
                var compact = family.Replace(" ", string.Empty, StringComparison.Ordinal);
                AddFontFamily(candidates, compact, compact + "b", compact + "i", compact + "bi");
                AddFontFamily(candidates, compact, compact + "bd", compact + "i", compact + "bi");
                AddFontFamily(candidates, "segoeui", "segoeuib", "segoeuii", "segoeuiz");
                break;
            }
        }

        foreach (var candidate in candidates)
        {
            var regular = FindInstalledFont(candidate.regular);
            var boldPath = FindInstalledFont(candidate.bold) ?? regular;
            var italicPath = FindInstalledFont(candidate.italic) ?? regular;
            var boldItalicPath = FindInstalledFont(candidate.boldItalic) ?? boldPath ?? italicPath ?? regular;

            if (regular is null && boldPath is null && italicPath is null && boldItalicPath is null)
            {
                continue;
            }

            var selected = bold && italic
                ? boldItalicPath
                : bold
                    ? boldPath
                    : italic
                        ? italicPath
                        : regular;

            if (selected is null)
            {
                continue;
            }

            var result = (selected, boldPath, italicPath, boldItalicPath);
            _cachedPaths[key] = result;
            return result;
        }

        var fallback = FindInstalledFont("segoeui") ?? FindInstalledFont("arial") ?? FirstFallbackFont();
        var value = (fallback, fallback, fallback, fallback);
        _cachedPaths[key] = value;
        return value;
    }

    /// <summary>
    /// Per-instance cache of discovered file paths for each font key.
    /// </summary>
    /// <remarks>
    /// Values are stored as a four-tuple even though only the primary path is
    /// used by <see cref="ResolveFace"/>; the style paths are retained for
    /// diagnostics and future face switching. Cleared by <see cref="Dispose"/>.
    /// </remarks>
    private readonly Dictionary<FontKey, (string? Primary, string? Bold, string? Italic, string? BoldItalic)> _cachedPaths = [];

    /// <summary>Appends one family's four style file-name stems as a discovery candidate.</summary>
    /// <param name="list">The candidate list being built.</param>
    /// <param name="regular">The regular face file-name stem.</param>
    /// <param name="bold">The bold face file-name stem.</param>
    /// <param name="italic">The italic face file-name stem.</param>
    /// <param name="boldItalic">The bold-italic face file-name stem.</param>
    /// <remarks>
    /// Stems are converted to absolute paths later by
    /// <see cref="FindInstalledFont"/>, which appends <c>.ttf</c> when needed.
    /// </remarks>
    private static void AddFontFamily(
        List<(string, string, string, string)> list,
        string regular,
        string bold,
        string italic,
        string boldItalic)
    {
        list.Add((regular, bold, italic, boldItalic));
    }

    /// <summary>
    /// Resolves a font file name or absolute path against the operating system's
    /// font directories.
    /// </summary>
    /// <param name="name">
    /// A file-name stem such as <c>segoeui</c>, a full file name ending in
    /// <c>.ttf</c>, <c>.otf</c> or <c>.ttc</c>, or a rooted path.
    /// </param>
    /// <returns>The first matching absolute path, or <see langword="null"/>.</returns>
    /// <remarks>
    /// A rooted path is returned only when it exists. Other names get a
    /// <c>.ttf</c> suffix unless they already carry a supported extension and are
    /// probed in <see cref="FontDirectories"/> order. There is no recursive scan
    /// and no font-cache service, so only exact file names are found.
    /// </remarks>
    private static string? FindInstalledFont(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (Path.IsPathRooted(name) && File.Exists(name))
        {
            return name;
        }

        var fileName = name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)
                ? name
                : name + ".ttf";

        foreach (var directory in FontDirectories)
        {
            var candidate = Path.Combine(directory, fileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Returns the first fallback font path that exists on disk.</summary>
    /// <returns>An absolute path, or <see langword="null"/> when none exist.</returns>
    /// <remarks>
    /// Used as a last resort when neither the requested family nor the usual
    /// system faces are installed.
    /// </remarks>
    private static string? FirstFallbackFont()
    {
        foreach (var path in FallbackFontPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Directories searched, in order, when resolving a font file name.
    /// </summary>
    /// <remarks>
    /// The Windows font directory is always probed first when it can be
    /// determined. Linux then adds the DejaVu, Liberation and Noto trees followed
    /// by the <c>/usr/share/fonts</c> root; macOS adds the system supplemental,
    /// system and local library directories. The list is built once per process.
    /// </remarks>
    private static readonly string[] FontDirectories = BuildFontDirectories();

    /// <summary>Builds the per-operating-system font directory search order.</summary>
    /// <returns>The directories to probe, most specific first.</returns>
    /// <remarks>
    /// <see cref="Environment.SpecialFolder.Windows"/> is empty on non-Windows
    /// systems, so the Windows entry is simply omitted there. Directories that do
    /// not exist are still listed; callers check each candidate with
    /// <see cref="File.Exists"/>.
    /// </remarks>
    private static string[] BuildFontDirectories()
    {
        var list = new List<string>(6);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        if (!string.IsNullOrEmpty(windows))
        {
            list.Add(Path.Combine(windows, "Fonts"));
        }

        if (OperatingSystem.IsLinux())
        {
            list.Add("/usr/share/fonts/truetype/dejavu");
            list.Add("/usr/share/fonts/truetype/liberation");
            list.Add("/usr/share/fonts/truetype/noto");
            list.Add("/usr/share/fonts");
        }
        else if (OperatingSystem.IsMacOS())
        {
            list.Add("/System/Library/Fonts/Supplemental");
            list.Add("/System/Library/Fonts");
            list.Add("/Library/Fonts");
        }

        return list.ToArray();
    }

    /// <summary>
    /// Font files used to fill gaps when the primary face lacks a codepoint.
    /// </summary>
    /// <remarks>
    /// On Windows the chain is Segoe UI Semilight, Segoe UI Symbol, Arial and
    /// Arial Unicode MS; on Linux it is DejaVu Sans then Noto Sans; on macOS it is
    /// Arial Unicode MS then Arial. Entries are de-duplicated case-insensitively
    /// and may not exist, in which case they are skipped during loading. The first
    /// existing entry also serves as a last-resort primary face.
    /// </remarks>
    private static readonly string[] FallbackFontPaths = BuildFallbackFontPaths();

    /// <summary>Builds the per-operating-system fallback font chain.</summary>
    /// <returns>
    /// Absolute candidate paths in priority order, without duplicates.
    /// </returns>
    /// <remarks>
    /// Symbol and Unicode faces are included before plain Arial so emoji,
    /// arrows and CJK glyphs are found before falling back to <c>.notdef</c>.
    /// Duplicate removal is case-insensitive to match file-system behaviour on
    /// Windows and macOS.
    /// </remarks>
    private static string[] BuildFallbackFontPaths()
    {
        var list = new List<string>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        if (!string.IsNullOrEmpty(windows))
        {
            var fonts = Path.Combine(windows, "Fonts");
            list.Add(Path.Combine(fonts, "segoeuisl.ttf"));
            list.Add(Path.Combine(fonts, "seguisym.ttf"));
            list.Add(Path.Combine(fonts, "arial.ttf"));
            list.Add(Path.Combine(fonts, "arialuni.ttf"));
        }

        if (OperatingSystem.IsLinux())
        {
            list.Add("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");
            list.Add("/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf");
        }
        else if (OperatingSystem.IsMacOS())
        {
            list.Add("/System/Library/Fonts/Supplemental/Arial Unicode.ttf");
            list.Add("/System/Library/Fonts/Supplemental/Arial.ttf");
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Releases every atlas texture and clears all face, path and metric caches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method is idempotent. It deletes textures through the GL API captured
    /// by <see cref="Initialize"/>, so it must be called while that context is
    /// still current and on its owning thread. A manager that was never
    /// initialised has no textures to delete.
    /// </para>
    /// <para>
    /// After disposal the caches are empty and the atlas textures are gone, so
    /// the instance is not intended to be reused. Measurement would rediscover
    /// fonts, but painting would target deleted texture names. The static
    /// <see cref="FamilyCache"/> is intentionally not cleared.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var face in _allFaces)
        {
            face.Dispose(_gl);
        }

        _allFaces.Clear();
        _faces.Clear();
        _cachedPaths.Clear();
    }

    /// <summary>
    /// The result of rasterising one rune: the owning texture, the glyph quad and
    /// the identifiers needed for kerning.
    /// </summary>
    /// <param name="Texture">
    /// The OpenGL atlas texture name, or zero when the glyph has no bitmap (for
    /// example a space or a failed rasterisation).
    /// </param>
    /// <param name="Glyph">The atlas rectangle, CSS-pixel extents and advance.</param>
    /// <param name="Face">The face that supplied the glyph and its kerning data.</param>
    /// <param name="GlyphIndex">
    /// The face-local glyph index, suitable for <see cref="Kerning"/>.
    /// </param>
    /// <remarks>
    /// A zero <paramref name="Texture"/> still carries a valid
    /// <see cref="Glyph.Advance"/>, so callers must advance the pen even when they
    /// skip drawing. Kerning is only valid between glyphs from the same
    /// <paramref name="Face"/>.
    /// </remarks>
    internal readonly record struct GlyphInfo(uint Texture, Glyph Glyph, FontFace Face, int GlyphIndex);
}

/// <summary>
/// The normalised identity of a font request: family name, binary weight and
/// italic flag.
/// </summary>
/// <param name="Family">
/// The normalised family name used for discovery, for example <c>segoe ui</c>
/// or <c>consolas</c>; generic CSS families are mapped to a concrete family.
/// </param>
/// <param name="Weight">Either 400 for regular or 700 for bold.</param>
/// <param name="Italic">Whether an italic face was requested.</param>
/// <remarks>
/// The record is a value type so it can key the face and path dictionaries
/// without allocation. Font size and colour are deliberately excluded; glyphs
/// are keyed by pixel size inside the face, and colour is a paint-time value.
/// </remarks>
internal readonly record struct FontKey(string Family, int Weight, bool Italic);

/// <summary>
/// A primary face plus the ordered fallback chain used for codepoints the
/// primary face does not contain.
/// </summary>
/// <remarks>
/// One instance is cached per <see cref="FontKey"/>. Fallback faces are shared
/// with other sets, so disposing the manager releases their atlases once rather
/// than per set.
/// </remarks>
internal sealed class FaceSet
{
    /// <summary>Creates a face set around an existing primary and fallback list.</summary>
    /// <param name="primary">The face used for metrics and for covered codepoints.</param>
    /// <param name="fallbacks">
    /// Faces tried in order for missing codepoints; may be empty.
    /// </param>
    public FaceSet(FontFace primary, List<FontFace> fallbacks)
    {
        Primary = primary;
        Fallbacks = fallbacks;
    }

    /// <summary>Gets the face that supplies line metrics and default glyphs.</summary>
    public FontFace Primary { get; }

    /// <summary>Gets the fallback faces in priority order.</summary>
    /// <remarks>
    /// The list is mutable and not copied, but callers in this assembly only read
    /// it. It can be empty when no fallback font file could be loaded.
    /// </remarks>
    public List<FontFace> Fallbacks { get; }

    /// <summary>Selects the face that should render a codepoint.</summary>
    /// <param name="rune">The Unicode scalar value to look up.</param>
    /// <returns>
    /// <see cref="Primary"/> when it contains the codepoint, otherwise the first
    /// fallback that contains it, otherwise <see cref="Primary"/> again so the
    /// caller still gets a measurable <c>.notdef</c> glyph.
    /// </returns>
    /// <remarks>
    /// Coverage is tested with stb_truetype's glyph-index lookup, so a codepoint
    /// mapped to glyph 0 counts as missing. The scan is linear in the number of
    /// fallbacks, which is at most four in practice.
    /// </remarks>
    public FontFace Select(Rune rune)
    {
        if (Primary.HasGlyph(rune.Value))
        {
            return Primary;
        }

        foreach (var fallback in Fallbacks)
        {
            if (fallback.HasGlyph(rune.Value))
            {
                return fallback;
            }
        }

        return Primary;
    }
}

/// <summary>
/// One rasterised glyph's atlas location, draw extents and advance, expressed in
/// CSS pixels.
/// </summary>
/// <param name="Texture">The atlas texture that contains the bitmap, or zero.</param>
/// <param name="U0">Normalised left texture coordinate.</param>
/// <param name="V0">Normalised top texture coordinate.</param>
/// <param name="U1">Normalised right texture coordinate.</param>
/// <param name="V1">Normalised bottom texture coordinate.</param>
/// <param name="Width">The bitmap width in CSS pixels, already divided by DPI.</param>
/// <param name="Height">The bitmap height in CSS pixels, already divided by DPI.</param>
/// <param name="OffsetX">
/// Horizontal offset from the pen position to the bitmap's left edge, in CSS
/// pixels. Positive values move right and are applied before rounding.
/// </param>
/// <param name="OffsetY">
/// Vertical offset from the baseline to the bitmap's top edge, in CSS pixels.
/// Values are typically negative because y grows downwards.
/// </param>
/// <param name="Advance">
/// The horizontal pen advance in CSS pixels, before letter spacing and kerning.
/// </param>
/// <remarks>
/// The bitmap is rasterised at <c>round(fontSize * dpi)</c> and then scaled back
/// to CSS units, so the same glyph drawn at different DPI values has the same
/// logical size but a sharper atlas. The UV rectangle covers only the glyph
/// bitmap; the one-texel transparent padding that prevents linear-filter bleed
/// sits outside it.
/// </remarks>
internal readonly record struct Glyph(
    uint Texture,
    float U0,
    float V0,
    float U1,
    float V1,
    float Width,
    float Height,
    float OffsetX,
    float OffsetY,
    float Advance);

/// <summary>
/// The line metrics of a face at one font size, in CSS pixels.
/// </summary>
/// <param name="Ascent">Distance from the baseline to the top of the line.</param>
/// <param name="Descent">
/// Distance from the baseline to the bottom of the line; stored as a positive
/// value although the font table records a negative descent.
/// </param>
/// <param name="Scale">The multiplier from font units to pixels at this size.</param>
/// <remarks>
/// Values are cached in quarter-pixel buckets by
/// <see cref="FontFace.GetMetricsForCss"/>, so callers should not assume they were
/// computed for the exact requested size.
/// </remarks>
internal readonly record struct CssFontMetrics(float Ascent, float Descent, float Scale);

/// <summary>
/// A single parsed TrueType/OpenType face plus its glyph, metric and atlas
/// caches.
/// </summary>
/// <remarks>
/// <para>
/// The original file bytes are retained because stb_truetype keeps unmanaged
/// pointers into them; do not pool or mutate that array. Instances are shared
/// between <see cref="FaceSet"/>s and owned by <see cref="FontManager"/>, which
/// disposes every face once.
/// </para>
/// <para>
/// Caches grow monotonically: metrics are bucketed per quarter CSS pixel and
/// glyphs per rounded device-pixel size. Atlases are allocated lazily, are
/// <see cref="FontAtlas.Size"/> pixels square, and are only freed by
/// <see cref="Dispose"/>. All members require the owning frame thread, and the
/// glyph path additionally requires a current GL context.
/// </para>
/// </remarks>
internal sealed unsafe class FontFace
{
    /// <summary>
    /// The complete font file bytes, retained because stb_truetype points into
    /// them.
    /// </summary>
    private readonly byte[] _data;

    /// <summary>The parsed stb_truetype font handle.</summary>
    private readonly StbTrueType.stbtt_fontinfo _info;

    /// <summary>Cached metrics keyed by quarter-pixel size bucket.</summary>
    private readonly Dictionary<int, CssFontMetrics> _metrics = [];

    /// <summary>Cached glyphs keyed by codepoint and rounded device-pixel size.</summary>
    private readonly Dictionary<long, Glyph> _glyphs = [];

    /// <summary>Atlas pages allocated for this face, in creation order.</summary>
    private readonly List<FontAtlas> _atlases = [];

    /// <summary>Whether the atlas textures have already been deleted.</summary>
    private bool _disposed;

    /// <summary>
    /// Parses a font file and captures its vertical metrics.
    /// </summary>
    /// <param name="path">The absolute path used for diagnostics and de-duplication.</param>
    /// <param name="data">
    /// The complete font file bytes. The array is retained for the lifetime of
    /// the face because stb_truetype reads through pointers into it.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// Thrown when stb_truetype cannot parse the supplied bytes.
    /// </exception>
    /// <remarks>
    /// The font's vertical metrics are read once into <see cref="AscentUnits"/>
    /// and <see cref="DescentUnits"/>; the line gap is read but deliberately
    /// ignored, because CSS line height controls leading in this engine.
    /// <see cref="FontManager"/> catches the parse failure and treats the face as
    /// unavailable.
    /// </remarks>
    public FontFace(string path, byte[] data)
    {
        Path = path;
        _data = data;
        _info = StbTrueType.CreateFont(data, 0);

        if (_info is null)
        {
            throw new InvalidDataException($"The font '{path}' could not be parsed.");
        }

        int ascent;
        int descent;
        int lineGap;
        StbTrueType.stbtt_GetFontVMetrics(_info, &ascent, &descent, &lineGap);
        AscentUnits = ascent;
        DescentUnits = descent;
    }

    /// <summary>Gets the absolute path of the font file this face was read from.</summary>
    /// <remarks>Used to share faces between font sets and to skip duplicate fallbacks.</remarks>
    public string Path { get; }

    /// <summary>
    /// Gets the ascent in font design units, as reported by stb_truetype's font
    /// metrics.
    /// </summary>
    /// <remarks>
    /// Design units are relative to the font's units-per-em; convert with
    /// <see cref="GetScaleForCss"/> before mixing with CSS pixels.
    /// </remarks>
    public int AscentUnits { get; }

    /// <summary>
    /// Gets the descent in font design units. The raw value is typically negative,
    /// matching the font's vertical metrics table.
    /// </summary>
    /// <remarks>
    /// <see cref="GetMetricsForCss"/> negates this to produce the positive CSS
    /// descent used by layout.
    /// </remarks>
    public int DescentUnits { get; }

    /// <summary>Determines whether the face maps a codepoint to a real glyph.</summary>
    /// <param name="codepoint">The Unicode scalar value to test.</param>
    /// <returns>
    /// <see langword="true"/> when stb_truetype returns a non-zero glyph index.
    /// </returns>
    /// <remarks>
    /// Codepoint 0 and unmapped values both report <see langword="false"/>.
    /// Coverage is based on the font's character map only; glyphs reached solely
    /// through substitutions are not considered.
    /// </remarks>
    public bool HasGlyph(int codepoint) => StbTrueType.stbtt_FindGlyphIndex(_info, codepoint) != 0;

    /// <summary>Gets the face-local glyph index for a codepoint.</summary>
    /// <param name="codepoint">The Unicode scalar value to map.</param>
    /// <returns>The glyph index, or 0 when the face does not contain the codepoint.</returns>
    public int GetGlyphIndex(int codepoint) => StbTrueType.stbtt_FindGlyphIndex(_info, codepoint);

    /// <summary>Returns the font-unit to pixel scale for a CSS font size.</summary>
    /// <param name="fontSize">The font size in CSS pixels.</param>
    /// <returns>
    /// The multiplier that converts design units to pixels, computed with
    /// stb_truetype's pixel-height scaling.
    /// </returns>
    /// <remarks>
    /// Sizes below 1 pixel are clamped to 1 so scaling never collapses to zero
    /// or inverts. The scale is recomputed on every call; only metrics and glyphs
    /// are cached.
    /// </remarks>
    public float GetScaleForCss(float fontSize) => StbTrueType.stbtt_ScaleForPixelHeight(_info, MathF.Max(1, fontSize));

    /// <summary>
    /// Returns cached ascent, descent and scale for a CSS font size.
    /// </summary>
    /// <param name="fontSize">The requested font size in CSS pixels.</param>
    /// <returns>The metrics for the nearest quarter-pixel bucket.</returns>
    /// <remarks>
    /// <para>
    /// The size is quantised to 0.25 CSS pixels: the cache key is
    /// <c>round(fontSize * 4)</c> with a minimum of 1, and metrics are computed at
    /// <c>key / 4</c>. This keeps the cache small while remaining visually
    /// indistinguishable for typical text sizes.
    /// </para>
    /// <para>
    /// Ascent is the positive design ascent times the scale, and descent is the
    /// negated design descent times the scale, so both values are positive
    /// distances from the baseline. Scaling clamps to a one-pixel minimum, so
    /// sub-pixel sizes share the one-pixel result.
    /// </para>
    /// </remarks>
    public CssFontMetrics GetMetricsForCss(float fontSize)
    {
        var key = Math.Max(1, (int)MathF.Round(fontSize * 4));
        var value = key * 0.25f;

        if (_metrics.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var scale = GetScaleForCss(value);
        var metrics = new CssFontMetrics(AscentUnits * scale, -DescentUnits * scale, scale);
        _metrics[key] = metrics;
        return metrics;
    }

    /// <summary>Returns the horizontal advance of a glyph in CSS pixels.</summary>
    /// <param name="glyphIndex">The face-local glyph index.</param>
    /// <param name="fontSize">The font size in CSS pixels used to scale the advance.</param>
    /// <returns>
    /// The unscaled pen advance converted from font units; zero for glyph 0 when
    /// the font defines no advance for it.
    /// </returns>
    /// <remarks>
    /// No kerning, letter spacing or synthetic bold adjustment is included; the
    /// caller adds those. The result is not cached, but the underlying metrics
    /// lookup is cheap.
    /// </remarks>
    public float GetAdvance(int glyphIndex, float fontSize)
    {
        int advance;
        int bearing;
        StbTrueType.stbtt_GetGlyphHMetrics(_info, glyphIndex, &advance, &bearing);
        return advance * GetScaleForCss(fontSize);
    }

    /// <summary>Returns the raw kerning adjustment between two glyphs.</summary>
    /// <param name="leftGlyph">The left glyph index within this face.</param>
    /// <param name="rightGlyph">The right glyph index within this face.</param>
    /// <returns>
    /// The signed adjustment in font design units, or zero when the pair is not
    /// kerned.
    /// </returns>
    /// <remarks>
    /// The value must be multiplied by <see cref="GetScaleForCss"/> to obtain CSS
    /// pixels; <see cref="FontManager.Kerning"/> performs that conversion. Only
    /// the legacy <c>kern</c> table is consulted, so OpenType GPOS pairs may be
    /// missed.
    /// </remarks>
    public int GetKerning(int leftGlyph, int rightGlyph) =>
        StbTrueType.stbtt_GetGlyphKernAdvance(_info, leftGlyph, rightGlyph);

    /// <summary>
    /// Rasterises a codepoint at a device pixel size and copies it into an atlas.
    /// </summary>
    /// <param name="codepoint">The Unicode scalar value to rasterise.</param>
    /// <param name="fontSize">The font size in CSS pixels.</param>
    /// <param name="dpi">The device-pixel ratio used to choose the raster size.</param>
    /// <param name="gl">
    /// The GL context used for texture upload, or <see langword="null"/> to return
    /// an untextured glyph.
    /// </param>
    /// <returns>The cached glyph, with CSS-pixel dimensions and normalised UVs.</returns>
    /// <remarks>
    /// <para>
    /// The raster size is <c>max(1, round(fontSize * dpi))</c>, so fractional DPI
    /// values and font sizes that round to the same pixel height share one cache
    /// entry. The key combines the codepoint with that pixel size; entries are
    /// retained until <see cref="Dispose"/>, even if the DPI changes later.
    /// </para>
    /// <para>
    /// Each bitmap is padded by one transparent texel on every side before
    /// upload, and the returned UV rectangle covers only the glyph, not the
    /// padding, so linear filtering cannot bleed in neighbouring glyphs. Widths
    /// and offsets are divided by <paramref name="dpi"/>; the advance is always
    /// computed from the CSS font size.
    /// </para>
    /// <para>
    /// Whitespace, glyphs without a bitmap, oversized glyphs and calls made
    /// without a GL context produce a cached entry with texture zero and the
    /// correct advance, so the pen still moves. Such failures are not retried for
    /// the same codepoint and pixel size.
    /// </para>
    /// </remarks>
    public Glyph GetGlyph(int codepoint, float fontSize, float dpi, GL? gl)
    {
        var pixelSize = Math.Max(1, (int)MathF.Round(fontSize * dpi));
        var key = ((long)codepoint << 20) | (uint)pixelSize;

        if (_glyphs.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var scale = StbTrueType.stbtt_ScaleForPixelHeight(_info, pixelSize);
        int width;
        int height;
        int xOffset;
        int yOffset;
        var bitmap = StbTrueType.stbtt_GetCodepointBitmap(
            _info,
            scale,
            scale,
            codepoint,
            &width,
            &height,
            &xOffset,
            &yOffset);

        var advance = GetAdvance(StbTrueType.stbtt_FindGlyphIndex(_info, codepoint), fontSize);
        Glyph glyph;

        if (bitmap is null || width <= 0 || height <= 0 || gl is null)
        {
            if (bitmap is not null)
            {
                StbTrueType.stbtt_FreeBitmap(bitmap, null);
            }

            glyph = new Glyph(0, 0, 0, 0, 0, 0, 0, 0, 0, advance);
            _glyphs[key] = glyph;
            return glyph;
        }

        const int padding = 1;
        var atlas = FindAtlas(width + padding * 2, height + padding * 2, gl);

        if (atlas is null)
        {
            StbTrueType.stbtt_FreeBitmap(bitmap, null);
            glyph = new Glyph(0, 0, 0, 0, 0, 0, 0, 0, 0, advance);
            _glyphs[key] = glyph;
            return glyph;
        }

        var atlasX = atlas.X + padding;
        var atlasY = atlas.Y + padding;
        atlas.Upload(gl, bitmap, width, height, atlasX, atlasY);
        var invDpi = 1f / dpi;

        glyph = new Glyph(
            atlas.Texture,
            atlasX / (float)FontAtlas.Size,
            atlasY / (float)FontAtlas.Size,
            (atlasX + width) / (float)FontAtlas.Size,
            (atlasY + height) / (float)FontAtlas.Size,
            width * invDpi,
            height * invDpi,
            xOffset * invDpi,
            yOffset * invDpi,
            advance);

        atlas.Advance(width + padding * 2, height + padding * 2);
        StbTrueType.stbtt_FreeBitmap(bitmap, null);
        _glyphs[key] = glyph;
        return glyph;
    }

    /// <summary>
    /// Finds an atlas with room for a padded glyph, creating one when needed.
    /// </summary>
    /// <param name="width">The padded glyph width in device pixels.</param>
    /// <param name="height">The padded glyph height in device pixels.</param>
    /// <param name="gl">The context used to allocate a new atlas.</param>
    /// <returns>
    /// The first atlas whose current row can hold the glyph, a new atlas, or
    /// <see langword="null"/> when the glyph is larger than an atlas.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Existing atlases are scanned in creation order. The allocator is
    /// row-based: a glyph that does not fit the remaining width of any current row
    /// is placed in a newly allocated 1024x1024 texture rather than wrapping the
    /// current page to its next row, so a face can accumulate several atlases even
    /// when earlier pages still have free rows.
    /// </para>
    /// <para>
    /// A new 1024x1024 atlas is allocated when no existing page has room. This
    /// method never returns a disposed atlas and must run on the GL thread.
    /// </para>
    /// </remarks>
    private FontAtlas? FindAtlas(int width, int height, GL gl)
    {
        foreach (var atlas in _atlases)
        {
            if (atlas.CanFit(width, height))
            {
                return atlas;
            }
        }

        if (width > FontAtlas.Size || height > FontAtlas.Size)
        {
            return null;
        }

        var created = new FontAtlas(gl);
        _atlases.Add(created);
        return created;
    }

    /// <summary>
    /// Deletes this face's atlas textures and clears its glyph and metric caches.
    /// </summary>
    /// <param name="gl">
    /// The context that owns the textures, or <see langword="null"/> when only the
    /// managed state should be released.
    /// </param>
    /// <remarks>
    /// The method is idempotent and is normally called by
    /// <see cref="FontManager.Dispose"/>, not directly. Passing <see langword="null"/>
    /// leaks the GPU textures until the context is destroyed, so only do so during
    /// shutdown after the context is already gone. The font bytes are released
    /// with the face; stb_truetype's parsed info is disposed here as well.
    /// </remarks>
    public void Dispose(GL? gl)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var atlas in _atlases)
        {
            atlas.Dispose(gl);
        }

        _atlases.Clear();
        _glyphs.Clear();
        _metrics.Clear();
        _info?.Dispose();
    }
}

/// <summary>
/// One square, single-channel OpenGL texture that packs rasterised glyph alpha
/// masks with a row-based allocator.
/// </summary>
/// <remarks>
/// <para>
/// The texture is <see cref="Size"/> x <see cref="Size"/> at 8 bits per pixel
/// (about 1 MiB), sampled with linear filtering and clamped to the edge. Atlas
/// pages are owned by a <see cref="FontFace"/> and never shrink or evict; new
/// pages are added when the current one cannot fit a glyph.
/// </para>
/// <para>
/// The allocator is a simple shelf packer: glyphs are placed left to right in the
/// current row, separated by one transparent texel of padding. Filling a row
/// closes it, and the next row starts below the tallest glyph in that row. All
/// members require the GL thread and a current context.
/// </para>
/// </remarks>
internal sealed unsafe class FontAtlas
{
    /// <summary>The width and height of every atlas page, in texels.</summary>
    /// <remarks>
    /// A 1024-texel page comfortably holds hundreds of typical Latin glyphs at
    /// UI sizes. Glyphs wider or taller than this cannot be packed and are drawn
    /// as untextured advances.
    /// </remarks>
    public const int Size = 1024;

    /// <summary>The GL texture name owned by this atlas.</summary>
    private readonly uint _texture;

    /// <summary>X cursor of the next free slot in the current row, in texels.</summary>
    private int _x;

    /// <summary>Y offset of the current row's top edge, in texels.</summary>
    private int _y;

    /// <summary>Height of the tallest glyph placed in the current row, in texels.</summary>
    private int _rowHeight;

    /// <summary>Whether the texture has already been deleted.</summary>
    private bool _disposed;

    /// <summary>
    /// Creates an empty atlas texture and configures its sampling state.
    /// </summary>
    /// <param name="gl">The context used to allocate the texture.</param>
    /// <remarks>
    /// The texture is allocated with <c>R8</c> internal format and undefined
    /// contents; callers must upload a glyph before sampling any region. Linear
    /// filtering is used for both minification and magnification, with no mipmaps.
    /// The constructor leaves texture unit 0 unbound and sets unpack alignment to
    /// 1 for later single-byte uploads.
    /// </remarks>
    public FontAtlas(GL gl)
    {
        _texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _texture);
        gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.R8,
            Size,
            Size,
            0,
            PixelFormat.Red,
            PixelType.UnsignedByte,
            null);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>Gets the GL texture name owned by this atlas.</summary>
    /// <remarks>
    /// The name is valid until <see cref="Dispose"/> is called with a non-null
    /// context. It is sampled as an alpha mask, not as colour data.
    /// </remarks>
    public uint Texture => _texture;

    /// <summary>Gets the x cursor of the next free slot in the current row.</summary>
    /// <value>A texel offset in the range 0 to <see cref="Size"/>.</value>
    public int X => _x;

    /// <summary>Gets the y offset of the current row's top edge.</summary>
    /// <value>A texel offset in the range 0 to <see cref="Size"/>.</value>
    public int Y => _y;

    /// <summary>
    /// Determines whether a glyph fits in the unallocated part of the current
    /// row.
    /// </summary>
    /// <param name="width">The padded glyph width in texels.</param>
    /// <param name="height">The padded glyph height in texels.</param>
    /// <returns>
    /// <see langword="true"/> when the glyph fits without wrapping; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// This method does not wrap or search earlier rows: it succeeds only when
    /// <c>x + width</c> and <c>y + height</c> are within the page. A glyph that
    /// would fit on the next row therefore reports failure and causes a new atlas
    /// to be allocated. Widths larger than <see cref="Size"/> always fail.
    /// </remarks>
    public bool CanFit(int width, int height)
    {
        if (width > Size)
        {
            return false;
        }

        if (_x + width > Size)
        {
            return false;
        }

        return _y + height <= Size;
    }

    /// <summary>
    /// Consumes a rectangle in the current row and wraps to the next row when the
    /// cursor reaches the right edge.
    /// </summary>
    /// <param name="width">The padded width to reserve, in texels.</param>
    /// <param name="height">The padded height to reserve, in texels.</param>
    /// <remarks>
    /// The row height becomes the tallest glyph placed since the last wrap, and
    /// <see cref="Y"/> advances by that height on wrap. A width that lands exactly
    /// on <see cref="Size"/> also wraps. No bounds check is performed, so callers
    /// must call <see cref="CanFit"/> first; an oversized height would overflow
    /// the page silently.
    /// </remarks>
    public void Advance(int width, int height)
    {
        if (height > _rowHeight)
        {
            _rowHeight = height;
        }

        _x += width;

        if (_x >= Size)
        {
            _x = 0;
            _y += _rowHeight;
            _rowHeight = 0;
        }
    }

    /// <summary>
    /// Uploads an 8-bit alpha bitmap into the atlas at a texel offset.
    /// </summary>
    /// <param name="gl">The context that owns the texture.</param>
    /// <param name="bitmap">A tightly packed, single-channel alpha bitmap.</param>
    /// <param name="width">The bitmap width in texels.</param>
    /// <param name="height">The bitmap height in texels.</param>
    /// <param name="x">The destination left edge inside the atlas.</param>
    /// <param name="y">The destination top edge inside the atlas.</param>
    /// <remarks>
    /// The update uses <c>glTexSubImage2D</c> with <c>RED</c>/<c>UNSIGNED_BYTE</c>
    /// and unpack alignment 1. The atlas texture is left bound on the active
    /// texture unit; the renderer is expected to rebind before drawing. The bitmap
    /// is not freed and remains owned by the caller.
    /// </remarks>
    public void Upload(GL gl, byte* bitmap, int width, int height, int x, int y)
    {
        gl.BindTexture(TextureTarget.Texture2D, _texture);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        gl.TexSubImage2D(
            TextureTarget.Texture2D,
            0,
            x,
            y,
            (uint)width,
            (uint)height,
            PixelFormat.Red,
            PixelType.UnsignedByte,
            bitmap);
    }

    /// <summary>Deletes the atlas texture, if a context is available.</summary>
    /// <param name="gl">
    /// The owning context, or <see langword="null"/> to release only the managed
    /// wrapper.
    /// </param>
    /// <remarks>
    /// Idempotent. Passing <see langword="null"/> leaves the GPU texture allocated
    /// until the context is destroyed and marks this instance disposed regardless.
    /// </remarks>
    public void Dispose(GL? gl)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        gl?.DeleteTexture(_texture);
    }
}
