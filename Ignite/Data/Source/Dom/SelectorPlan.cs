namespace Ignite.Data.Dom
{
    /// <summary>
    /// How a compound selector relates to the compound on its left, following CSS
    /// combinators.
    /// </summary>
    public enum Combinator : byte
    {
        /// <summary>The first compound of a selector, or no combinator.</summary>
        None,

        /// <summary>Whitespace: the left compound has to match an ancestor.</summary>
        Descendant,

        /// <summary><c>&gt;</c>: the left compound has to match the parent.</summary>
        Child,

        /// <summary><c>+</c>: the left compound has to match the immediately preceding sibling.</summary>
        AdjacentSibling,

        /// <summary><c>~</c>: the left compound has to match any preceding sibling.</summary>
        GeneralSibling
    }

    internal enum PseudoClassKind : byte
    {
        Root,
        Empty,
        FirstChild,
        LastChild,
        OnlyChild,
        FirstOfType,
        LastOfType,
        OnlyOfType,
        NthChild,
        NthLastChild,
        NthOfType,
        NthLastOfType
    }

    /// <summary>
    /// One compound selector plus its slice of the attribute filter and pseudo
    /// class arenas. Stored at selector intern time so matching never re-parses
    /// tokens.
    /// </summary>
    internal struct CompoundRecord
    {
        /// <summary>First token of the compound, relative to the selector's tokens.</summary>
        public int TokenStart;

        /// <summary>Number of tokens in the compound.</summary>
        public int TokenLength;

        public int FilterOffset;
        public int FilterCount;

        public int PseudoOffset;
        public int PseudoCount;

        /// <summary>The combinator that links this compound to the previous one.</summary>
        public Combinator LeadingCombinator;

        /// <summary>False when the compound uses a construct the engine cannot evaluate.</summary>
        public bool IsSupported;
    }

    /// <summary>
    /// A tree-derived pseudo class. <see cref="A"/> and <see cref="B"/> hold the
    /// <c>An+B</c> terms for the <c>:nth-*</c> forms.
    /// </summary>
    internal struct PseudoRecord
    {
        public PseudoClassKind Kind;
        public int A;
        public int B;
    }
}
