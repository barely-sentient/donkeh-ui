using Ignite.Parser.Objects;
using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        private static void Punctuation()
        {
            // punctuation.
            Exact(';', TokenKind.SemiColon);
            Exact(':', TokenKind.Colon);
            Exact('{', TokenKind.LeftCurlyBrace);
            Exact('}', TokenKind.RightCurlyBrace);
            Exact('[', TokenKind.LeftSquareBracket);
            Exact(']', TokenKind.RightSquareBracket);
            Exact('(', TokenKind.LeftParenthesis);
            Exact(')', TokenKind.RightParenthesis);
            Exact('>', TokenKind.DirectChildBracket);
            Exact("::", TokenKind.PseudoElementColon);
        }
    }
}