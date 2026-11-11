namespace Donkeh.Data
{
    /// <summary>
    /// A declaration flattened into integer ids. Stored in a flat list so rules
    /// do not allocate individual objects.
    /// </summary>
    /// <remarks>
    /// Every field is an id into another <see cref="DataTable"/> collection (or an
    /// interned string), so a rule is a fixed 20-byte value with no references.
    /// A negative id is the "absent" sentinel: -1 for
    /// <see cref="ConditionId"/> and <see cref="LayerId"/>.
    /// </remarks>
    /// <seealso cref="DataTable.Rules"/>
    public readonly struct Rule
    {
        /// <summary>
        /// Initializes a rule whose ids are all -1, i.e. a rule attached to no
        /// selector.
        /// </summary>
        /// <remarks>
        /// This is mainly the <c>default</c> value and a safe placeholder. Rules
        /// are normally created by <see cref="DataTable.AddRule"/>.
        /// </remarks>
        public Rule()
        {
            SelectorId = -1;
            PropertyId = -1;
            ValueId = -1;
            ConditionId = -1;
            LayerId = -1;
        }

        /// <summary>
        /// Gets the id of the selector this declaration belongs to, indexing
        /// <see cref="DataTable.Selectors"/>.
        /// </summary>
        public required int SelectorId { get; init; }

        /// <summary>
        /// Gets the interned property name, for example the id of <c>"color"</c>.
        /// </summary>
        public required int PropertyId { get; init; }

        /// <summary>
        /// Gets the interned declaration value exactly as written, including any
        /// units, for example the id of <c>"12px"</c>.
        /// </summary>
        public required int ValueId { get; init; }

        /// <summary>
        /// The innermost condition active for this rule, or -1 when unconditional.
        /// Ancestor conditions are reached through <see cref="Condition.ParentId"/>.
        /// </summary>
        public int ConditionId { get; init; } = -1;

        /// <summary>
        /// The cascade layer this rule belongs to, or -1 when unlayered.
        /// </summary>
        /// <remarks>
        /// Unlayered rules are stronger than every layered rule; matching orders
        /// them after all layered rules by treating -1 as <c>int.MaxValue</c>.
        /// </remarks>
        public int LayerId { get; init; } = -1;
    }
}
