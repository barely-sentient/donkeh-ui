using Meziantou.Framework.Fonts;

namespace Donkeh.Windowing;

/// <summary>
/// Expands WOFF 1.0 and WOFF 2.0 font containers into the sfnt bytes expected by
/// <see cref="FontFace"/>.
/// </summary>
/// <remarks>
/// <para>
/// WOFF is a compressed wrapper around TrueType/OpenType tables, and WOFF2 adds
/// Brotli plus table transforms. The decoder is delegated to
/// <c>Meziantou.Framework.Fonts</c>, a fully managed implementation that works on
/// desktop and Android without a native FreeType build. FontManager still feeds
/// the expanded bytes into StbTrueType, so metrics, rasterisation and fallback
/// behaviour are unchanged.
/// </para>
/// <para>
/// The decoder returns the original array for non-WOFF input. A malformed
/// container throws; <see cref="FontManager"/> treats that like any other
/// unreadable optional font and skips the face.
/// </para>
/// </remarks>
internal static class WoffFontDecoder
{
    /// <summary>Maximum temporary memory the font decoder may use for one file.</summary>
    private const long MaxLiveAllocationBytes = 64L * 1024 * 1024;

    /// <summary>Returns whether the bytes start with a WOFF 1.0 or WOFF 2.0 signature.</summary>
    /// <param name="data">The font file bytes.</param>
    /// <returns><see langword="true"/> for <c>wOFF</c> or <c>wOF2</c>.</returns>
    public static bool IsWoff(ReadOnlySpan<byte> data) =>
        data.Length >= 4
        && data[0] == (byte)'w'
        && data[1] == (byte)'O'
        && data[2] == (byte)'F'
        && (data[3] == (byte)'F' || data[3] == (byte)'2');

    /// <summary>Expands WOFF input into plain sfnt bytes.</summary>
    /// <param name="data">The font file bytes.</param>
    /// <returns>
    /// The expanded sfnt bytes for WOFF input, or the original array otherwise.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// The WOFF container contains no usable face.
    /// </exception>
    public static byte[] Expand(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!IsWoff(data))
        {
            return data;
        }

        var environment = new PooledFontEnvironment(
            FontResourceLimits.Default,
            MaxLiveAllocationBytes);
        var faces = FontLoader.Load(data, environment, useEmbeddedBitmaps: false);

        if (faces.Length == 0)
        {
            throw new InvalidDataException("The WOFF font contained no faces.");
        }

        // FontData.Bytes is the expanded sfnt stream. Copy it because
        // stb_truetype needs a plain managed array it can pin.
        return faces[0].Data.Bytes.ToArray();
    }
}
