namespace Ignite.Parser
{
    public static class InternTable
    {
        private static readonly Dictionary<string, int> InternedStrings = [];

        // Ids are allocated densely from zero, so the reverse lookup is just an
        // array index instead of a scan over the dictionary.
        private static readonly List<string> InternedValues = [];

        public static int Intern(string s)
        {
            // if it exists, return out the index without storing it
            // further.
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

        /// <summary>
        /// Resolves an interned id back to its original string, or null if not found.
        /// </summary>
        public static string? GetString(int id)
        {
            return (uint)id < (uint)InternedValues.Count ? InternedValues[id] : null;
        }
    }
}