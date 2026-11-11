namespace Ignite.Data.Dom
{
    /// <summary>
    /// A rule that applies to a node, together with the data the cascade needs to
    /// order it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Specificity"/> packs the three CSS specificity components into
    /// one int, 10 bits each, so ordering never unpacks them into separate lists.
    /// Comparing the packed value compares (ids, classes, types) lexicographically,
    /// which is exactly CSS specificity order.
    /// </para>
    /// <para>
    /// Matching returns these values sorted weakest first: layer, then specificity,
    /// then <see cref="Order"/>. Applying them in order and letting later
    /// declarations win reproduces the cascade.
    /// </para>
    /// </remarks>
    public readonly record struct MatchedRule
    {
        /// <summary>
        /// Gets the declaration that matched, including its condition and layer.
        /// </summary>
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

        /// <summary>
        /// Gets the number of id selectors in the matched selector.
        /// </summary>
        /// <value>Bits 20-29 of <see cref="Specificity"/>, 0-1023 (saturated).</value>
        public int IdCount => (Specificity >> 20) & 0x3FF;

        /// <summary>
        /// Gets the number of class, attribute and pseudo class selectors in the
        /// matched selector.
        /// </summary>
        /// <value>Bits 10-19 of <see cref="Specificity"/>, 0-1023 (saturated).</value>
        public int ClassCount => (Specificity >> 10) & 0x3FF;

        /// <summary>
        /// Gets the number of type and pseudo element selectors in the matched
        /// selector.
        /// </summary>
        /// <value>Bits 0-9 of <see cref="Specificity"/>, 0-1023 (saturated).</value>
        public int TypeCount => Specificity & 0x3FF;
    }
}
