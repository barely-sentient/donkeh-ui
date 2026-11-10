namespace Ignite.Data.Dom
{
    /// <summary>
    /// A rule that applies to a node, together with the data the cascade needs to
    /// order it.
    /// </summary>
    /// <remarks>
    /// <see cref="Specificity"/> packs the three CSS specificity components into
    /// one int, 10 bits each, so ordering never unpacks them into separate lists.
    /// Comparing the packed value compares (ids, classes, types) lexicographically,
    /// which is exactly CSS specificity order.
    /// </remarks>
    public readonly record struct MatchedRule
    {
        public required Rule Rule { get; init; }

        /// <summary>
        /// The selector that matched the node.
        /// </summary>
        public int SelectorId { get; init; }

        /// <summary>
        /// Packed specificity: ids in bits 20-29, classes in bits 10-19, types in
        /// bits 0-9.
        /// </summary>
        public int Specificity { get; init; }

        /// <summary>
        /// The rule's index in <see cref="DataTable.Rules"/>. Rules are stored in
        /// source order, so this is also the cascade order.
        /// </summary>
        public int Order { get; init; }

        public int IdCount => (Specificity >> 20) & 0x3FF;

        public int ClassCount => (Specificity >> 10) & 0x3FF;

        public int TypeCount => Specificity & 0x3FF;
    }
}
