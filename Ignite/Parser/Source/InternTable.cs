namespace Ignite.Parser
{
    public static class InternTable
    {
        private static readonly Dictionary<string, int> InternedStrings = [];

        public static int Intern(string s)
        {
            // if it exists, return out the index without storing it
            // further.
            if (InternedStrings.TryGetValue(s, out var value))
            {
                return value;
            }
            
            return !InternedStrings.TryAdd(s, InternedStrings.Count) ? 
                throw new InvalidOperationException($"Unable to intern string {s}") : 
                InternedStrings[s];
        }
    }
}