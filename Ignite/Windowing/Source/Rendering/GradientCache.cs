using System.Numerics;
using Ignite.LayoutLab;
using Silk.NET.OpenGL;

namespace Ignite.Windowing;

/// <summary>Rasterises gradient specs into small, cached textures.</summary>
/// <remarks>
/// <para>
/// Only gradient layers are handled here; URL layers are decoded by
/// <see cref="ImageCache"/>. A gradient is rasterised once into a
/// <c>256x256</c> RGBA8 texture on first use and then reused for every draw
/// that produces the same key, which keeps per-frame cost to a dictionary
/// lookup and a texture bind.
/// </para>
/// <para>
/// Textures are owned by this cache and live until <see cref="Dispose"/>; the
/// cache never evicts, so a page that animates a colour stop creates a texture
/// per distinct value. All members must be called on the thread with the
/// current GL context, and <see cref="Dispose"/> must run while that context is
/// alive.
/// </para>
/// </remarks>
internal sealed class GradientCache : IDisposable
{
    /// <summary>Width and height of every cached gradient texture, in texels.</summary>
    private const int Size = 256;

    /// <summary>
    /// Maps a structural hash of the gradient to its GL texture name. Entries
    /// are never removed individually; only <see cref="Dispose"/> clears them.
    /// </summary>
    private readonly Dictionary<long, uint> _textures = [];
    /// <summary>The OpenGL entry point used for texture creation and deletion.</summary>
    private readonly GL _gl;

    /// <summary>Set by <see cref="Dispose"/> so a second call becomes a no-op.</summary>
    private bool _disposed;

    /// <summary>Creates an empty cache; no texture is allocated until the first request.</summary>
    /// <param name="gl">
    /// The Silk.NET OpenGL entry point used for texture creation and deletion.
    /// </param>
    public GradientCache(GL gl)
    {
        _gl = gl;
    }

    /// <summary>
    /// Gets the cached texture for a gradient layer, rasterising and uploading
    /// one if this exact gradient has not been seen before.
    /// </summary>
    /// <param name="spec">
    /// The background layer to resolve. Only its gradient fields are read;
    /// <see cref="BackgroundImageSpec.Url"/> and the box-related fields are
    /// ignored.
    /// </param>
    /// <returns>
    /// The GL texture name, or 0 when <see cref="BackgroundImageSpec.Kind"/> is
    /// <see cref="ImageKind.None"/>. A gradient with no stops still yields a
    /// texture, but it is fully transparent because the pixel buffer is left
    /// zeroed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The lookup key is a 64-bit hash of the kind, angle, centre, radius, repeat
    /// mode and every stop's offset and colour. Only the hash is stored, so a
    /// collision would silently return another gradient's texture rather than
    /// rasterising a new one.
    /// </para>
    /// <para>
    /// On a miss the gradient is rasterised on the CPU into a
    /// <c>Size * Size * 4</c> byte buffer (65,536 pixels) and uploaded with
    /// <c>glTexImage2D</c> as RGBA8, using linear filtering and clamped wrapping
    /// in both axes. The texture is unbound before returning, so the caller's
    /// texture binding is not preserved. The cache retains no reference to the
    /// spec, only the resulting texture name.
    /// </para>
    /// <para>
    /// No kind validation beyond <see cref="ImageKind.None"/> is performed: a URL
    /// layer passed here would take the linear branch and, having no stops,
    /// produce a transparent texture. Callers must route URL layers to
    /// <see cref="ImageCache"/> instead.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Fills the pixel buffer with the gradient sampled on a regular
    /// <c>Size x Size</c> grid.
    /// </summary>
    /// <param name="spec">The gradient layer to rasterise.</param>
    /// <param name="pixels">
    /// Destination buffer of exactly <c>Size * Size * 4</c> bytes, filled as
    /// RGBA, row-major from the first texel. Existing contents are overwritten
    /// only for valid stops; with no stops the buffer is untouched.
    /// </param>
    /// <remarks>
    /// <para>
    /// Texture coordinates run from 0 to 1 across the box. For radial gradients
    /// the distance from the centre is divided by the radius per axis, giving an
    /// ellipse in box space; conic gradients use the angle around the centre
    /// with 0 at the negative X axis; everything else is treated as a linear
    /// gradient whose parameter is the projection of the sample onto the
    /// direction derived from <see cref="BackgroundImageSpec.Angle"/>, offset by
    /// half so the midpoint is the centre.
    /// </para>
    /// <para>
    /// Centres and radii outside the (0, 1] range fall back to 0.5 per axis.
    /// The resulting parameter is clamped to 0..1 before the stops are sampled,
    /// so colours beyond the first and last stop extend as flat bands.
    /// </para>
    /// <para>
    /// This is a per-texel CPU loop, paid once per distinct gradient. Each pixel
    /// stores red, green, blue and alpha from the packed ARGB stop colour.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Samples the stop table at a normalised position and interpolates the
    /// colour.
    /// </summary>
    /// <param name="stops">The gradient stops, assumed non-empty and in offset order.</param>
    /// <param name="t">
    /// The position along the gradient; values before the first stop and after
    /// the last clamp to the end colours.
    /// </param>
    /// <returns>The packed ARGB colour at <paramref name="t"/>.</returns>
    /// <remarks>
    /// Stops are not required to be monotonic, but the search assumes they are:
    /// it returns the colour of the first stop whose offset is not before
    /// <paramref name="t"/>. A zero-length segment jumps straight to the right
    /// stop, which reproduces the CSS hard-stop behaviour.
    /// </remarks>
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

    /// <summary>Linearly interpolates two packed ARGB colours channel by channel.</summary>
    /// <param name="left">Colour at the start of the segment, packed as <c>0xAARRGGBB</c>.</param>
    /// <param name="right">Colour at the end of the segment.</param>
    /// <param name="t">Blend amount, clamped to 0..1.</param>
    /// <returns>The packed interpolated colour.</returns>
    /// <remarks>
    /// Interpolation happens in the stored sRGB byte values rather than linear
    /// light, and without premultiplication, so a fully transparent stop still
    /// contributes its RGB channels to the midpoint colour.
    /// </remarks>
    private static uint Lerp(uint left, uint right, float t)
    {
        t = Math.Clamp(t, 0, 1);
        var a = LerpChannel((int)((left >> 24) & 0xFF), (int)((right >> 24) & 0xFF), t);
        var r = LerpChannel((int)((left >> 16) & 0xFF), (int)((right >> 16) & 0xFF), t);
        var g = LerpChannel((int)((left >> 8) & 0xFF), (int)((right >> 8) & 0xFF), t);
        var b = LerpChannel((int)(left & 0xFF), (int)(right & 0xFF), t);
        return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
    }

    /// <summary>Rounds and clamps one interpolated colour channel to a byte.</summary>
    /// <param name="left">Start channel value, normally 0 to 255.</param>
    /// <param name="right">End channel value, normally 0 to 255.</param>
    /// <param name="t">Blend amount in the range 0 to 1.</param>
    /// <returns>The channel value rounded to the nearest integer and clamped to 0..255.</returns>
    private static int LerpChannel(int left, int right, float t) =>
        (int)MathF.Round(Math.Clamp(left + (right - left) * t, 0f, 255f));

    /// <summary>Computes the cache key for a gradient layer.</summary>
    /// <param name="spec">The layer whose visual parameters are hashed.</param>
    /// <returns>
    /// A 64-bit hash combining kind, angle, centre, radius, repeat mode and
    /// every stop.
    /// </returns>
    /// <remarks>
    /// <see cref="HashCode"/> is seeded per process, so keys are not stable
    /// across runs and must not be persisted. Box-related fields
    /// (<see cref="BackgroundImageSpec.Clip"/>,
    /// <see cref="BackgroundImageSpec.Origin"/> and
    /// <see cref="BackgroundImageSpec.Fixed"/>) are excluded because they do not
    /// affect the texels, as is <see cref="BackgroundImageSpec.Url"/> since URL
    /// layers never reach this cache.
    /// </remarks>
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

    /// <inheritdoc/>
    /// <summary>Deletes every cached gradient texture and empties the cache.</summary>
    /// <remarks>
    /// The call is idempotent thanks to <see cref="_disposed"/>. It must run on
    /// the thread that owns the GL context, and any texture name previously
    /// returned by <see cref="GetTexture"/> becomes invalid; the renderer must
    /// therefore not batch or draw against this cache afterwards.
    /// </remarks>
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
