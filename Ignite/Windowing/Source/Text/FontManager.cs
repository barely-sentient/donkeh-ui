using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Ignite.LayoutLab;
using Silk.NET.OpenGL;
using StbTrueTypeSharp;

namespace Ignite.Windowing;

/// <summary>
/// A compact TrueType font pipeline. It supplies both layout metrics and
/// rasterised glyph atlases, so measured text and painted text always agree.
/// </summary>
public sealed unsafe class FontManager : IDisposable
{
    private readonly Dictionary<FontKey, FaceSet> _faces = [];
    private readonly List<FontFace> _allFaces = [];
    private GL? _gl;
    private bool _disposed;

    public void Initialize(GL gl)
    {
        _gl = gl;
    }

    /// <summary>Measures a run in CSS pixels.</summary>
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

    public TextMetrics Measure(string text, in TextStyle style) => Measure(text.AsSpan(), in style);

    /// <summary>Returns the rasterised glyph and the atlas texture that owns it.</summary>
    internal GlyphInfo GetGlyph(FontKey key, Rune rune, float fontSize, float dpi)
    {
        var set = ResolveFace(key);
        var face = set.Select(rune);
        var glyphIndex = face.GetGlyphIndex(rune.Value);
        var glyph = face.GetGlyph(rune.Value, fontSize, MathF.Max(1, dpi), _gl);
        return new GlyphInfo(glyph.Texture, glyph, face, glyphIndex);
    }

    internal float Kerning(FontFace face, int leftGlyph, int rightGlyph, float fontSize) =>
        face.GetKerning(leftGlyph, rightGlyph) * face.GetScaleForCss(fontSize);

    internal FontKey CreateKey(in TextStyle style)
    {
        var family = NormalizeFamily(style.FontFamily);
        var weight = style.FontWeight >= 600 ? 700 : 400;
        return new FontKey(family, weight, style.Italic);
    }

    internal FontFace ResolvePrimary(FontKey key) => ResolveFace(key).Primary;

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

    private static string NormalizeFamily(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return "sans-serif";
        }

        var comma = family.IndexOf(',');
        var first = comma >= 0 ? family[..comma] : family;
        first = first.Trim().Trim('"', '\'').Trim();

        return first.ToLowerInvariant() switch
        {
            "" or "sans-serif" or "system-ui" or "ui-sans-serif" or "segoe ui" or "helvetica" or "helvetica neue" or "arial" => "segoe ui",
            "serif" or "ui-serif" or "times" or "times new roman" or "georgia" => "times new roman",
            "monospace" or "ui-monospace" or "consolas" or "courier new" or "courier" => "consolas",
            _ => first
        };
    }

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

    private readonly Dictionary<FontKey, (string? Primary, string? Bold, string? Italic, string? BoldItalic)> _cachedPaths = [];

    private static void AddFontFamily(
        List<(string, string, string, string)> list,
        string regular,
        string bold,
        string italic,
        string boldItalic)
    {
        list.Add((regular, bold, italic, boldItalic));
    }

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

    private static readonly string[] FontDirectories = BuildFontDirectories();

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

    private static readonly string[] FallbackFontPaths = BuildFallbackFontPaths();

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var face in _allFaces)
        {
            face.Dispose();
        }

        _allFaces.Clear();
        _faces.Clear();
        _cachedPaths.Clear();
    }

    internal readonly record struct GlyphInfo(uint Texture, Glyph Glyph, FontFace Face, int GlyphIndex);
}

internal readonly record struct FontKey(string Family, int Weight, bool Italic);

internal sealed class FaceSet
{
    public FaceSet(FontFace primary, List<FontFace> fallbacks)
    {
        Primary = primary;
        Fallbacks = fallbacks;
    }

    public FontFace Primary { get; }

    public List<FontFace> Fallbacks { get; }

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

internal readonly record struct CssFontMetrics(float Ascent, float Descent, float Scale);

internal sealed unsafe class FontFace : IDisposable
{
    private readonly byte[] _data;
    private readonly StbTrueType.stbtt_fontinfo _info;
    private readonly Dictionary<int, CssFontMetrics> _metrics = [];
    private readonly Dictionary<long, Glyph> _glyphs = [];
    private readonly List<FontAtlas> _atlases = [];
    private bool _disposed;

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

    public string Path { get; }

    public int AscentUnits { get; }

    public int DescentUnits { get; }

    public bool HasGlyph(int codepoint) => StbTrueType.stbtt_FindGlyphIndex(_info, codepoint) != 0;

    public int GetGlyphIndex(int codepoint) => StbTrueType.stbtt_FindGlyphIndex(_info, codepoint);

    public float GetScaleForCss(float fontSize) => StbTrueType.stbtt_ScaleForPixelHeight(_info, MathF.Max(1, fontSize));

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

    public float GetAdvance(int glyphIndex, float fontSize)
    {
        int advance;
        int bearing;
        StbTrueType.stbtt_GetGlyphHMetrics(_info, glyphIndex, &advance, &bearing);
        return advance * GetScaleForCss(fontSize);
    }

    public int GetKerning(int leftGlyph, int rightGlyph) =>
        StbTrueType.stbtt_GetGlyphKernAdvance(_info, leftGlyph, rightGlyph);

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var atlas in _atlases)
        {
            atlas.Dispose();
        }

        _atlases.Clear();
        _glyphs.Clear();
        _metrics.Clear();
        _info?.Dispose();
    }
}

internal sealed unsafe class FontAtlas : IDisposable
{
    public const int Size = 1024;

    private readonly uint _texture;
    private int _x;
    private int _y;
    private int _rowHeight;
    private bool _disposed;

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

    public uint Texture => _texture;

    public int X => _x;

    public int Y => _y;

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
