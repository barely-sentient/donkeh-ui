using Ignite.Parser.Objects;

namespace Ignite.Data
{
    /// <summary>
    /// A selector stored as a slice of the owning <see cref="DataTable"/>'s flat
    /// token arena. Keeping the tokens in one arena avoids one array allocation
    /// per selector.
    /// </summary>
    public readonly struct SelectorRecord
    {
        /// <summary>
        /// Offset of the first selector token in the table's selector arena.
        /// </summary>
        public int TokenOffset { get; init; }

        /// <summary>
        /// Number of tokens in this selector.
        /// </summary>
        public int TokenLength { get; init; }

        /// <summary>
        /// The right-most token, normalised to a semantic selector kind. Used as
        /// a fast candidate filter; it never replaces full selector verification.
        /// </summary>
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
