namespace Ignite.Parser.Objects
{
    /// <summary>
    /// A lexical token produced by the Ignite parser, identifying what kind of
    /// token it is and where it sits in the source text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tokens are created in very large numbers, so this is a value type and is
    /// deliberately compact. Rather than storing <see cref="StartIndex"/> and
    /// <see cref="EndIndex"/> as two separate <see cref="int"/> fields, both are
    /// packed into the single <see cref="Location"/> field, saving 4 bytes per token.
    /// </para>
    /// <para>
    /// The trade-off is limited range: start positions must fit in 24 bits and
    /// token lengths in 8 bits. See <see cref="Location"/> for the exact layout.
    /// </para>
    /// </remarks>
    public struct Token
    {
        /// <summary>
        /// Gets or sets the kind of token (identifier, keyword, punctuation, etc.).
        /// </summary>
        public TokenKind Kind { get; set; }

        /// <summary>
        /// Gets or sets the raw packed position data for this token.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This value is an implementation detail of the packed storage. Prefer
        /// <see cref="StartIndex"/> and <see cref="EndIndex"/>, which encode and
        /// decode it safely.
        /// </para>
        /// <para>The 32 bits are laid out as follows:</para>
        /// <code>
        ///  31          24 23                                  0
        /// +--------------+-------------------------------------+
        /// |    length    |             start index             |
        /// |   (8 bits)   |              (24 bits)              |
        /// +--------------+-------------------------------------+
        /// </code>
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// <b>Bits 0–23</b> hold the start index. Three bytes give a maximum
        /// value of 2^24 − 1 (16,777,215), so the source text can be at most
        /// about 16.7 million characters long.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// <b>Bits 24–31</b> hold the token length, <c>EndIndex - StartIndex</c>,
        /// in the range 0–255. The end index is not stored; it is derived as
        /// <c>start + length</c>.
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// Because the length occupies the top byte, <see cref="Location"/> is
        /// negative whenever the length is 128 or greater (the sign bit is set).
        /// This is harmless, since every read masks the fields out explicitly
        /// (<c>&amp; 0xFF</c> after the shift), but it means the raw value should
        /// not be compared or sorted numerically.
        /// </para>
        /// </remarks>
        public int Location { get; set; }

        /// <summary>
        /// Gets or sets the zero-based index of the token's first character in the
        /// source text.
        /// </summary>
        /// <value>
        /// A value in the range 0 to 16,777,215 (24 bits).
        /// </value>
        /// <remarks>
        /// <para>
        /// <b>Setting this property moves the whole token.</b> The stored length is
        /// preserved, so <see cref="EndIndex"/> shifts by the same amount. To
        /// resize a token, set <see cref="EndIndex"/> afterwards.
        /// </para>
        /// <para>
        /// Values outside the 24-bit range are <b>not</b> validated; the upper bits
        /// are silently discarded by the mask, which would wrap the position. The
        /// lexer is expected to guard against sources larger than 16.7 million
        /// characters before producing tokens.
        /// </para>
        /// </remarks>
        public int StartIndex
        {
            // Mask off the top byte (length) to leave only the low 24 bits.
            get => Location & 0x00FFFFFF;
            set
            {
                // Extract the existing length from the top byte so it survives the write.
                var length = (Location >> 24) & 0xFF;

                // Re-pack: length into bits 24..31, new start (masked to 24 bits) into 0..23.
                Location = (length << 24) | (value & 0x00FFFFFF);
            }
        }

        /// <summary>
        /// Gets or sets the zero-based index one past the token's last character
        /// (an exclusive end), computed as <see cref="StartIndex"/> plus the stored
        /// length.
        /// </summary>
        /// <value>
        /// A value from <see cref="StartIndex"/> to <see cref="StartIndex"/> + 255.
        /// </value>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown by the setter when <paramref name="value"/> is less than
        /// <see cref="StartIndex"/> (negative length), or more than 255 characters
        /// beyond it (the length no longer fits in 8 bits).
        /// </exception>
        /// <remarks>
        /// <para>
        /// Only the length is stored, so reading this property adds the start index
        /// and the length together. Setting it recomputes the length relative to the
        /// current <see cref="StartIndex"/>, so <b>set <see cref="StartIndex"/>
        /// first</b> when assigning both.
        /// </para>
        /// <para>
        /// Tokens longer than 255 characters (long string literals or comments, for
        /// example) cannot be represented and will throw here.
        /// </para>
        /// </remarks>
        public int EndIndex
        {
            // start + length, where length is the top byte.
            get => (Location & 0x00FFFFFF) + ((Location >> 24) & 0xFF);
            set
            {
                var start = Location & 0x00FFFFFF;
                var length = value - start;

                // The length must fit in one unsigned byte and cannot be negative.
                if (length < 0 || length > byte.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "EndIndex must be within 255 characters of StartIndex for the packed storage.");
                }

                // Re-pack: new length into bits 24..31, existing start into 0..23.
                Location = ((length & 0xFF) << 24) | (start & 0x00FFFFFF);
            }
        }
    }
}