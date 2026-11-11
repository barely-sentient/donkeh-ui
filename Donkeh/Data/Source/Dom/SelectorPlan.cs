namespace Donkeh.Data.Dom
{
    /// <summary>
    /// How a compound selector relates to the compound on its left, following CSS
    /// combinators.
    /// </summary>
    /// <remarks>
    /// Matching walks the selector right to left, so the combinator stored on a
    /// compound describes the gap to its left. <see cref="None"/> therefore only
    /// appears on the first (left-most) compound.
    /// </remarks>
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

    /// <summary>
    /// The pseudo classes the matcher can evaluate, either from the tree
    /// (structural forms) or from host-maintained interaction state.
    /// </summary>
    /// <remarks>
    /// This is an internal encoding used by <see cref="PseudoRecord"/>; the public
    /// surface only exposes built selectors. Logical functions such as
    /// <c>:not()</c> are not represented here at all, because a selector that uses
    /// them is marked unmatchable instead.
    /// </remarks>
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
        NthLastOfType,

        // Host-driven interaction state.
        Hover,
        Focus,
        FocusVisible,
        FocusWithin,
        Active,
        Checked,
        Disabled,
        Enabled,
        Link,
        AnyLink,
        PlaceholderShown,
        ReadOnly,
        ReadWrite
    }

    /// <summary>
    /// One compound selector plus its slice of the attribute filter and pseudo
    /// class arenas. Stored at selector intern time so matching never re-parses
    /// tokens.
    /// </summary>
    /// <remarks>
    /// <see cref="TokenStart"/> and <see cref="TokenLength"/> are relative to the
    /// owning selector's tokens, while <see cref="FilterOffset"/> and
    /// <see cref="PseudoOffset"/> index the table-wide arenas. The offsets are
    /// rebased when a selector is stored, so they are valid only in the table that
    /// produced the record.
    /// </remarks>
    internal struct CompoundRecord
    {
        /// <summary>First token of the compound, relative to the selector's tokens.</summary>
        public int TokenStart;

        /// <summary>Number of tokens in the compound.</summary>
        public int TokenLength;

        /// <summary>Index of the first attribute filter in the table's filter arena.</summary>
        public int FilterOffset;

        /// <summary>Number of attribute filters in the compound.</summary>
        public int FilterCount;

        /// <summary>Index of the first pseudo class in the table's pseudo arena.</summary>
        public int PseudoOffset;

        /// <summary>Number of pseudo classes in the compound.</summary>
        public int PseudoCount;

        /// <summary>The combinator that links this compound to the previous one.</summary>
        public Combinator LeadingCombinator;

        /// <summary>False when the compound uses a construct the engine cannot evaluate.</summary>
        /// <remarks>
        /// Informational only: matching consults the selector-level
        /// <see cref="SelectorRecord.IsMatchable"/> flag instead. The field is still
        /// written by <see cref="SelectorAnalysis"/> for inspection.
        /// </remarks>
        public bool IsSupported;
    }

    /// <summary>
    /// A tree-derived pseudo class. <see cref="A"/> and <see cref="B"/> hold the
    /// <c>An+B</c> terms for the <c>:nth-*</c> forms.
    /// </summary>
    /// <remarks>
    /// For every other kind <see cref="A"/> and <see cref="B"/> are zero. The matcher
    /// evaluates the kind against the node and its sibling positions; nothing about
    /// the pseudo class is re-parsed at match time.
    /// </remarks>
    internal struct PseudoRecord
    {
        /// <summary>Which pseudo class to evaluate.</summary>
        public PseudoClassKind Kind;

        /// <summary>The <c>A</c> coefficient for the <c>:nth-*</c> forms, or 0.</summary>
        public int A;

        /// <summary>The <c>B</c> offset for the <c>:nth-*</c> forms, or 0.</summary>
        public int B;
    }
}
