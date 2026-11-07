namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        public static void Parse(ref ReadOnlySpan<char> span)
        {
            
        }

        public static void Parse(string source)
        {
            var src = source.AsSpan();
            Parse(ref src);
        }
    }
}