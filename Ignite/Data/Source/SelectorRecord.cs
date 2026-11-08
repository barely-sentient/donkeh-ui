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
    }
}
