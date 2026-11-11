using System.Collections.Concurrent;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace Ignite.Windowing;

/// <summary>Decoded images and their OpenGL textures, keyed by source string.</summary>
/// <remarks>
/// <para>
/// A source is either an absolute or relative file path or a base64
/// <c>data:</c> URI. Encoded bytes are cached separately from decoded textures,
/// and both caches are keyed case-insensitively by the original source string.
/// Relative paths are resolved against the process working directory at first
/// use.
/// </para>
/// <para>
/// There is no eviction and no file-change tracking: once a source has been
/// read, the same bytes and texture are returned until <see cref="Dispose"/>.
/// The texture cache and all GL calls are confined to the frame thread, so the
/// class as a whole is not safe for concurrent use even though the encoded-byte
/// dictionary is concurrent. <see cref="Initialize"/> and <see cref="Dispose"/>
/// require a current context.
/// </para>
/// <para>
/// Decoded dimensions are intrinsic image pixels, not CSS pixels. The renderer
/// uses them to apply <c>object-fit</c> and background tiling; this class only
/// decodes, uploads and caches.
/// </para>
/// </remarks>
/// <seealso cref="GLRenderer"/>
/// <seealso cref="FontManager"/>
public sealed unsafe class ImageCache : IDisposable
{
    /// <summary>Decoded texture entries keyed by the original source string.</summary>
    private readonly Dictionary<string, ImageEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Encoded file or base64 bytes keyed by the original source string.</summary>
    private readonly ConcurrentDictionary<string, byte[]> _bytes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The context used for texture creation and deletion.</summary>
    private GL? _gl;

    /// <summary>Whether <see cref="Dispose"/> has already released the textures.</summary>
    private bool _disposed;

    /// <summary>
    /// Captures the OpenGL API used to create and delete image textures.
    /// </summary>
    /// <param name="gl">The GL context wrapper owned by the host window.</param>
    /// <remarks>
    /// Call once after the context exists; <see cref="TryGetInfo"/> works without
    /// it, but <see cref="GetTexture"/> returns zero until it is set. The same
    /// context must still be current when <see cref="Dispose"/> runs.
    /// </remarks>
    public void Initialize(GL gl) => _gl = gl;

    /// <summary>
    /// Reads an image's intrinsic dimensions from its header without creating a
    /// GPU texture.
    /// </summary>
    /// <param name="source">The file path or base64 data URI to inspect.</param>
    /// <param name="width">Receives the intrinsic width in image pixels, or zero.</param>
    /// <param name="height">Receives the intrinsic height in image pixels, or zero.</param>
    /// <returns>
    /// <see langword="true"/> when the encoded bytes were found and a positive
    /// width and height could be decoded; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The encoded bytes are cached but the header parse is repeated on every
    /// call; the decoded pixels and texture are not produced here, so this is
    /// suitable for intrinsic-size queries during layout before the image is
    /// painted.
    /// </para>
    /// <para>
    /// A missing file, an unsupported or corrupt format, a non-base64 data URI and
    /// I/O errors all return <see langword="false"/> with zero dimensions instead
    /// of throwing. The renderer treats such images as absent.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Returns the RGBA texture for a source, decoding and uploading it on first
    /// use.
    /// </summary>
    /// <param name="source">The file path or base64 data URI to load.</param>
    /// <param name="width">Receives the decoded image width in pixels, or zero.</param>
    /// <param name="height">Receives the decoded image height in pixels, or zero.</param>
    /// <param name="repeat">
    /// Always set to <see langword="false"/>. Tiling is decided by the caller from
    /// <c>background-repeat</c>; the parameter is retained for API compatibility.
    /// </param>
    /// <returns>
    /// The OpenGL texture name, or zero when the cache is not initialised or the
    /// source cannot be decoded.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Textures are 8-bit RGBA with linear filtering, clamped to the edge and
    /// without mipmaps. Failures are cached as a zero entry, so a bad source is
    /// not re-read or re-decoded on subsequent frames.
    /// </para>
    /// <para>
    /// Dimensions are intrinsic pixels and are used by <see cref="GLRenderer"/>
    /// for <c>object-fit</c> and background sizing; they are not CSS pixels. A
    /// zero texture tells the renderer to skip the draw entirely.
    /// </para>
    /// <para>
    /// For <c>object-fit</c>, the renderer divides the box by these dimensions to
    /// get per-axis scales, then uses their minimum for <c>contain</c>, their
    /// maximum for <c>cover</c>, exactly 1 for <c>none</c> and
    /// <c>min(1, contain)</c> for <c>scale-down</c>. The scaled image is centred
    /// in the box, and <c>cover</c> crops symmetrically by shrinking the UV
    /// rectangle rather than clipping the quad.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Decodes encoded bytes and uploads them as a new RGBA texture.
    /// </summary>
    /// <param name="source">The source string, used only for byte lookup and diagnostics.</param>
    /// <param name="gl">The context used to create the texture.</param>
    /// <returns>
    /// A populated entry, or <see langword="default"/> when the bytes are missing
    /// or decoding fails.
    /// </returns>
    /// <remarks>
    /// Decoding requests four colour components, so paletted and greyscale images
    /// are expanded to RGBA. Texture creation has no mipmaps and uses linear
    /// min/mag filtering with clamp-to-edge wrapping. Unpack alignment is left at
    /// 4 for tightly packed RGBA rows. The decoded pixel array is not retained
    /// after upload.
    /// </remarks>
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

    /// <summary>
    /// Returns the encoded bytes for a source, reading a file or decoding a
    /// base64 data URI and caching the result.
    /// </summary>
    /// <param name="source">A file path or <c>data:</c> URI.</param>
    /// <returns>The encoded bytes, or <see langword="null"/> when unavailable.</returns>
    /// <remarks>
    /// <para>
    /// Data URIs must contain a comma and the substring <c>;base64</c> before it;
    /// percent-encoded payloads and other URI forms are not supported. The whole
    /// source string, including a potentially large base64 payload, is used as the
    /// cache key and the decoded bytes are also retained, so both representations
    /// stay in memory until <see cref="Dispose"/>.
    /// </para>
    /// <para>
    /// File paths are made absolute with <see cref="Path.GetFullPath(string)"/>
    /// when they are relative, which resolves against the current working
    /// directory rather than the stylesheet or assembly location. Missing files
    /// and all I/O or base64 errors return <see langword="null"/>. Nothing is
    /// cached on failure, so a missing file is probed again on the next call.
    /// </para>
    /// </remarks>
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

    /// <summary>Deletes every cached texture and clears both caches.</summary>
    /// <remarks>
    /// Idempotent. Textures are deleted only when a context was captured; calling
    /// this without <see cref="Initialize"/> has no GPU work to do because none
    /// could have been created. The instance is not meant to be reused afterwards,
    /// and the GL context must still be current.
    /// </remarks>
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

    /// <summary>
    /// A decoded image: its GL texture name and intrinsic pixel dimensions.
    /// </summary>
    /// <param name="Texture">The texture name, or zero for a failed decode.</param>
    /// <param name="Width">The decoded width in image pixels.</param>
    /// <param name="Height">The decoded height in image pixels.</param>
    /// <remarks>
    /// The default value represents an unsuccessful load and is stored in the
    /// cache so failures are not retried.
    /// </remarks>
    private readonly record struct ImageEntry(uint Texture, int Width, int Height);
}
