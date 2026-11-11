using Donkeh.Parser.Objects;
using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers the punctuation tokens that have a dedicated
        /// <see cref="Objects.TokenKind"/>.
        /// </summary>
        /// <remarks>
        /// The double colon is registered as one two-character rule so the
        /// tokenizer can emit <see cref="Objects.TokenKind.PseudoElementColon"/>
        /// without lookahead.
        /// </remarks>
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