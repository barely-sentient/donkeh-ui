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
