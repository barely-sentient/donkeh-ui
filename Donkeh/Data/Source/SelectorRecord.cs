using Donkeh.Parser.Objects;

namespace Donkeh.Data
{
    /// <summary>
    /// A selector stored as a slice of the owning <see cref="DataTable"/>'s flat
    /// token arena. Keeping the tokens in one arena avoids one array allocation
    /// per selector.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The record does not own its tokens or compounds: both are indices into
    /// arenas owned by the <see cref="DataTable"/>. The offsets stay valid for the
    /// lifetime of that table, but a span obtained from the arena is invalidated
    /// when the arena grows, so re-read it through
    /// <see cref="DataTable.GetSelectorSpan"/> instead of caching the span.
    /// </para>
    /// <para>
    /// Selectors are value types with no references, so a record can be compared
    /// and copied cheaply, but identity is the id assigned by
    /// <see cref="DataTable.InternSelector"/>, not the record itself.
    /// </para>
    /// </remarks>
    public readonly struct SelectorRecord
    {
        /// <summary>
        /// Offset of the first selector token in the table's selector arena.
        /// </summary>
        /// <value>A zero-based token index, not a character offset.</value>
        public int TokenOffset { get; init; }

        /// <summary>
        /// Number of tokens in this selector.
        /// </summary>
        public int TokenLength { get; init; }

        /// <summary>
        /// The right-most token, normalised to a semantic selector kind. Used as
        /// a fast candidate filter; it never replaces full selector verification.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Normalisation maps <see cref="TokenKind.Identifier"/>,
        /// <see cref="TokenKind.PropertyName"/> and <see cref="TokenKind.Function"/>
        /// to <see cref="TokenKind.Identifier"/> so class and element names share
        /// one bucket. The interned id is preserved, so a hash token still carries
        /// its leading <c>'#'</c>.
        /// </para>
        /// <para>
        /// Attribute-selector subjects keep <see cref="TokenKind.RightSquareBracket"/>
        /// and are indexed separately by attribute name; structural pseudo class
        /// subjects fall into the small unkeyed list.
        /// </para>
        /// </remarks>
        public required Token PrimaryDiscriminator { get; init; }

        /// <summary>
        /// Packed specificity: ids in bits 20-29, classes in bits 10-19 and types
        /// in bits 0-9. Computed once when the selector is stored.
        /// </summary>
        public int Specificity { get; init; }

        /// <summary>
        /// Offset of the first compound in the owning table's compound arena.
        /// </summary>
        public int CompoundOffset { get; init; }

        /// <summary>
        /// Number of compounds in the selector.
        /// </summary>
        public int CompoundCount { get; init; }

        /// <summary>
        /// True when every construct in the selector can be evaluated by the
        /// engine (type, universal, id, class, attribute, structural pseudo
        /// classes and combinators).
        /// </summary>
        public bool IsMatchable { get; init; }
    }
}
