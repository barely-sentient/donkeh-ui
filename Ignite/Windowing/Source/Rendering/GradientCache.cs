using System.Numerics;
using Ignite.LayoutLab;
using Silk.NET.OpenGL;

namespace Ignite.Windowing;

/// <summary>Rasterises gradient specs into small, cached textures.</summary>
internal sealed class GradientCache : IDisposable
{
    private const int Size = 256;

    private readonly Dictionary<long, uint> _textures = [];
    private readonly GL _gl;
    private bool _disposed;

    public GradientCache(GL gl)
    {
        _gl = gl;
    }

    public unsafe uint GetTexture(BackgroundImageSpec spec)
    {
        if (spec.Kind == ImageKind.None)
        {
            return 0;
        }

        var key = KeyOf(spec);

        if (_textures.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var pixels = new byte[Size * Size * 4];
        Rasterise(spec, pixels);

        var texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);

        unsafe
        {
            fixed (byte* data = pixels)
            {
                _gl.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    InternalFormat.Rgba8,
                    Size,
                    Size,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    data);
            }
        }

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        _gl.BindTexture(TextureTarget.Texture2D, 0);

        _textures[key] = texture;
        return texture;
    }

    private static void Rasterise(BackgroundImageSpec spec, byte[] pixels)
    {
        var stops = spec.Stops;

        if (stops is null || stops.Length == 0)
        {
            return;
        }

        var center = new Vector2(
            spec.Center.X is > 0 and <= 1 ? spec.Center.X : 0.5f,
            spec.Center.Y is > 0 and <= 1 ? spec.Center.Y : 0.5f);

        var radius = new Vector2(
            spec.Radius.Width is > 0 and <= 1 ? spec.Radius.Width : 0.5f,
            spec.Radius.Height is > 0 and <= 1 ? spec.Radius.Height : 0.5f);

        var angle = spec.Angle * (MathF.PI / 180f);
        var direction = new Vector2(MathF.Sin(angle), -MathF.Cos(angle));

        for (var y = 0; y < Size; y++)
        {
            var v = y / (float)(Size - 1);

            for (var x = 0; x < Size; x++)
            {
                var u = x / (float)(Size - 1);
                float t;

                if (spec.Kind == ImageKind.RadialGradient)
                {
                    var dx = (u - center.X) / MathF.Max(0.0001f, radius.X);
                    var dy = (v - center.Y) / MathF.Max(0.0001f, radius.Y);
                    t = MathF.Sqrt(dx * dx + dy * dy);
                }
                else if (spec.Kind == ImageKind.ConicGradient)
                {
                    var dx = u - center.X;
                    var dy = v - center.Y;
                    t = (MathF.Atan2(dy, dx) + MathF.PI) / (MathF.PI * 2f);
                }
                else
                {
                    t = 0.5f + (u - center.X) * direction.X + (v - center.Y) * direction.Y;
                }

                t = Math.Clamp(t, 0, 1);
                var color = Sample(stops, t);
                var index = (y * Size + x) * 4;
                pixels[index] = (byte)(color >> 16);
                pixels[index + 1] = (byte)(color >> 8);
                pixels[index + 2] = (byte)color;
                pixels[index + 3] = (byte)(color >> 24);
            }
        }
    }

    private static uint Sample(GradientStop[] stops, float t)
    {
        if (t <= stops[0].Offset)
        {
            return stops[0].Color;
        }

        for (var i = 1; i < stops.Length; i++)
        {
            var right = stops[i];

            if (t > right.Offset)
            {
                continue;
            }

            var left = stops[i - 1];
            var span = right.Offset - left.Offset;
            var amount = span <= 0.00001f ? 1 : (t - left.Offset) / span;
            return Lerp(left.Color, right.Color, amount);
        }

        return stops[^1].Color;
    }

    private static uint Lerp(uint left, uint right, float t)
    {
        t = Math.Clamp(t, 0, 1);
        var a = LerpChannel((int)((left >> 24) & 0xFF), (int)((right >> 24) & 0xFF), t);
        var r = LerpChannel((int)((left >> 16) & 0xFF), (int)((right >> 16) & 0xFF), t);
        var g = LerpChannel((int)((left >> 8) & 0xFF), (int)((right >> 8) & 0xFF), t);
        var b = LerpChannel((int)(left & 0xFF), (int)(right & 0xFF), t);
        return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
    }

    private static int LerpChannel(int left, int right, float t) =>
        (int)MathF.Round(Math.Clamp(left + (right - left) * t, 0f, 255f));

    private static long KeyOf(BackgroundImageSpec spec)
    {
        var hash = new HashCode();
        hash.Add((int)spec.Kind);
        hash.Add(spec.Angle);
        hash.Add(spec.Center);
        hash.Add(spec.Radius);
        hash.Add((int)spec.Repeat);

        if (spec.Stops is not null)
        {
            foreach (var stop in spec.Stops)
            {
                hash.Add(stop.Offset);
                hash.Add(stop.Color);
            }
        }

        return hash.ToHashCode();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var texture in _textures.Values)
        {
            _gl.DeleteTexture(texture);
        }

        _textures.Clear();
    }
}
