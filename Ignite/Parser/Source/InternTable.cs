namespace Ignite.Parser
{
    /// <summary>
    /// Process-wide string interning table. Ids are dense and start at zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tokenization can run concurrently, so both the forward and reverse
    /// lookups are synchronised. The lock is only held for the dictionary and
    /// list operations; token text is not copied for strings that are already
    /// interned.
    /// </para>
    /// <para>
    /// Ids are assigned in first-seen order and are stable for the lifetime of
    /// the process; entries are never removed. The table therefore grows with
    /// the number of distinct token texts seen, not with the number of tokens.
    /// </para>
    /// <para>
    /// <see cref="Tokenizer.Parse(ReadOnlySpan{char})"/> interns the text of
    /// every emitted token, so the same table also serves as the tokenizer's
    /// text cache.
    /// </para>
    /// </remarks>
    /// <seealso cref="Tokenizer"/>
    /// <seealso cref="Objects.Token.InternedId"/>
    public static class InternTable
    {
        /// <summary>
        /// Guards both the forward dictionary and the dense reverse list.
        /// </summary>
        private static readonly object Sync = new();

        /// <summary>
        /// Maps interned text to its dense id.
        /// </summary>
        private static readonly Dictionary<string, int> InternedStrings = [];

        // Reuses the string dictionary for allocation-free span lookups. The
        // wrapper holds the dictionary itself, so later inserts stay visible.
        private static readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>>
            AlternateLookup = InternedStrings.GetAlternateLookup<ReadOnlySpan<char>>();

        // Ids are allocated densely from zero, so the reverse lookup is just an
        // array index instead of a scan over the dictionary.
        /// <summary>
        /// Reverse lookup from a dense id back to its interned text.
        /// </summary>
        private static readonly List<string> InternedValues = [];

        /// <summary>
        /// Returns the dense id for <paramref name="s"/>, adding it to the table
        /// on first sighting.
        /// </summary>
        /// <param name="s">The text to intern. Must not be <see langword="null"/>.</param>
        /// <returns>
        /// A non-negative id that is stable for the lifetime of the process.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="s"/> is <see langword="null"/>.
        /// </exception>
        public static int Intern(string s)
        {
            ArgumentNullException.ThrowIfNull(s);

            lock (Sync)
            {
                return InternCore(s);
            }
        }

        /// <summary>
        /// Interns a character span, allocating a string only on the first sighting.
        /// This keeps the very common "already interned" path allocation free.
        /// </summary>
        /// <param name="s">The text to intern.</param>
        /// <returns>
        /// A non-negative id that is stable for the lifetime of the process.
        /// </returns>
        public static int Intern(ReadOnlySpan<char> s)
        {
            lock (Sync)
            {
                if (AlternateLookup.TryGetValue(s, out var value))
                {
                    return value;
                }

                return InternCore(s.ToString());
            }
        }

        /// <summary>
        /// Resolves an interned id back to its original string, or null if not found.
        /// </summary>
        /// <param name="id">The id returned by an earlier <c>Intern</c> call.</param>
        /// <returns>
        /// The interned text, or <see langword="null"/> when
        /// <paramref name="id"/> was never handed out.
        /// </returns>
        /// <remarks>
        /// Negative ids and ids beyond the current high-water mark both return
        /// <see langword="null"/> rather than throwing.
        /// </remarks>
        public static string? GetString(int id)
        {
            lock (Sync)
            {
                return (uint)id < (uint)InternedValues.Count ? InternedValues[id] : null;
            }
        }

        /// <summary>
        /// Resolves an interned id back to its original string without throwing
        /// for ids that were never handed out.
        /// </summary>
        /// <param name="id">The id returned by an earlier <c>Intern</c> call.</param>
        /// <param name="value">
        /// Receives the interned text on success, or <see langword="null"/> on
        /// failure.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="id"/> is in range;
        /// otherwise <see langword="false"/>.
        /// </returns>
        public static bool TryGetString(int id, out string? value)
        {
            lock (Sync)
            {
                if ((uint)id < (uint)InternedValues.Count)
                {
                    value = InternedValues[id];
                    return true;
                }

                value = null;
                return false;
            }
        }

        /// <summary>
        /// Interns <paramref name="prefix"/> immediately followed by
        /// <paramref name="s"/>, for example '#' + "username". This yields the same
        /// id the tokenizer hands out for that selector text, and the already
        /// interned path allocates nothing.
        /// </summary>
        /// <param name="prefix">The single character to prepend.</param>
        /// <param name="s">The text that follows the prefix.</param>
        /// <returns>
        /// The id of the concatenated text, which is stable for the lifetime of
        /// the process.
        /// </returns>
        /// <remarks>
        /// Prefixed text shorter than 128 characters is concatenated on the
        /// stack; longer text takes the allocating
        /// <see cref="string.Concat(ReadOnlySpan{char}, ReadOnlySpan{char})"/>
        /// path so stack usage stays bounded.
        /// </remarks>
        public static int Intern(char prefix, ReadOnlySpan<char> s)
        {
            if (s.Length < MaxStackPrefixedLength)
            {
                Span<char> buffer = stackalloc char[s.Length + 1];
                buffer[0] = prefix;
                s.CopyTo(buffer[1..]);

                return Intern(buffer);
            }

            return Intern(string.Concat(prefix.ToString(), s));
        }

        /// <summary>
        /// Looks up the id of <paramref name="s"/> without interning it, so a miss
        /// does not grow the table. Returns false when the text has never been seen.
        /// </summary>
        /// <param name="s">The text to look up.</param>
        /// <param name="id">
        /// Receives the existing id on success, or zero on failure.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="s"/> is already interned;
        /// otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// This is a read-only probe: unlike <see cref="Intern(ReadOnlySpan{char})"/>
        /// it never allocates or inserts.
        /// </remarks>
        public static bool TryGetId(ReadOnlySpan<char> s, out int id)
        {
            lock (Sync)
            {
                return AlternateLookup.TryGetValue(s, out id);
            }
        }

        /// <summary>
        /// Longest prefixed name that is built on the stack. Longer names take the
        /// allocating path, which keeps the stack usage of the hot path bounded.
        /// </summary>
        private const int MaxStackPrefixedLength = 128;

        /// <summary>
        /// Performs the actual insert or lookup. The caller must hold
        /// <see cref="Sync"/>.
        /// </summary>
        /// <param name="s">The text to intern. Never <see langword="null"/>.</param>
        /// <returns>The existing or newly assigned dense id.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the id cannot be added, which indicates the dictionary and
        /// the reverse list have diverged.
        /// </exception>
        private static int InternCore(string s)
        {
            // If it exists, return the index without storing it further.
            if (InternedStrings.TryGetValue(s, out var value))
            {
                return value;
            }

            var id = InternedStrings.Count;
            if (!InternedStrings.TryAdd(s, id))
            {
                throw new InvalidOperationException($"Unable to intern string {s}");
            }

            InternedValues.Add(s);
            return id;
        }
    }
}
