namespace Ignite.Data
{
    /// <summary>
    /// A declaration flattened into integer ids. Stored in a flat list so rules
    /// do not allocate individual objects.
    /// </summary>
    public readonly struct Rule
    {
        public Rule()
        {
            SelectorId = -1;
            PropertyId = -1;
            ValueId = -1;
            ConditionId = -1;
        }

        public required int SelectorId { get; init; }

        public required int PropertyId { get; init; }

        public required int ValueId { get; init; }

        /// <summary>
        /// The innermost condition active for this rule, or -1 when unconditional.
        /// Ancestor conditions are reached through <see cref="Condition.ParentId"/>.
        /// </summary>
        public int ConditionId { get; init; } = -1;
    }
}
