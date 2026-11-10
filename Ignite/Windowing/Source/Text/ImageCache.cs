using System.Collections.Concurrent;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace Ignite.Windowing;

/// <summary>Decoded images and their OpenGL textures, keyed by source string.</summary>
public sealed unsafe class ImageCache : IDisposable
{
    private readonly Dictionary<string, ImageEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte[]> _bytes = new(StringComparer.OrdinalIgnoreCase);
    private GL? _gl;
    private bool _disposed;

    public void Initialize(GL gl) => _gl = gl;

    public bool TryGetInfo(string source, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            var data = LoadBytes(source);

            if (data is null)
            {
                return false;
            }

            using var stream = new MemoryStream(data, writable: false);
            var info = ImageInfo.FromStream(stream);

            if (info is null)
            {
                return false;
            }

            width = info.Value.Width;
            height = info.Value.Height;
            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }

    public uint GetTexture(string source, out int width, out int height, out bool repeat)
    {
        width = 0;
        height = 0;
        repeat = false;

        if (_gl is null)
        {
            return 0;
        }

        if (_entries.TryGetValue(source, out var cached))
        {
            width = cached.Width;
            height = cached.Height;
            return cached.Texture;
        }

        var entry = Load(source, _gl);
        _entries[source] = entry;
        width = entry.Width;
        height = entry.Height;
        return entry.Texture;
    }

    private ImageEntry Load(string source, GL gl)
    {
        var data = LoadBytes(source);

        if (data is null)
        {
            return default;
        }

        ImageResult image;

        try
        {
            image = ImageResult.FromMemory(data, ColorComponents.RedGreenBlueAlpha);
        }
        catch
        {
            return default;
        }

        if (image.Width <= 0 || image.Height <= 0 || image.Data is null)
        {
            return default;
        }

        var texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);

        fixed (byte* pixels = image.Data)
        {
            gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba8,
                (uint)image.Width,
                (uint)image.Height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixels);
        }

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.BindTexture(TextureTarget.Texture2D, 0);

        return new ImageEntry(texture, image.Width, image.Height);
    }

    private byte[]? LoadBytes(string source)
    {
        if (_bytes.TryGetValue(source, out var cached))
        {
            return cached;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = source.IndexOf(',');

            if (comma < 0 || !source[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                var data = Convert.FromBase64String(source[(comma + 1)..]);
                _bytes[source] = data;
                return data;
            }
            catch
            {
                return null;
            }
        }

        try
        {
            var path = source;

            if (!Path.IsPathRooted(path))
            {
                path = Path.GetFullPath(path);
            }

            if (!File.Exists(path))
            {
                return null;
            }

            var data = File.ReadAllBytes(path);
            _bytes[source] = data;
            return data;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_gl is not null)
        {
            foreach (var entry in _entries.Values)
            {
                if (entry.Texture != 0)
                {
                    _gl.DeleteTexture(entry.Texture);
                }
            }
        }

        _entries.Clear();
        _bytes.Clear();
    }

    private readonly record struct ImageEntry(uint Texture, int Width, int Height);
}
