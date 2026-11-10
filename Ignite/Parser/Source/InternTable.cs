namespace Ignite.Parser
{
    /// <summary>
    /// Process-wide string interning table. Ids are dense and start at zero.
    /// </summary>
    /// <remarks>
    /// Tokenization can run concurrently, so both the forward and reverse
    /// lookups are synchronised. The lock is only held for the dictionary and
    /// list operations; token text is not copied for strings that are already
    /// interned.
    /// </remarks>
    public static class InternTable
    {
        private static readonly object Sync = new();

        private static readonly Dictionary<string, int> InternedStrings = [];

        // Reuses the string dictionary for allocation-free span lookups. The
        // wrapper holds the dictionary itself, so later inserts stay visible.
        private static readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>>
            AlternateLookup = InternedStrings.GetAlternateLookup<ReadOnlySpan<char>>();

        // Ids are allocated densely from zero, so the reverse lookup is just an
        // array index instead of a scan over the dictionary.
        private static readonly List<string> InternedValues = [];

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
