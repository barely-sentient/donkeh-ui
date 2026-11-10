namespace Ignite.Data.Dom
{
    /// <summary>
    /// The CSS attribute selector operators, as defined by
    /// <c>[attr]</c>, <c>[attr=v]</c>, <c>[attr~=v]</c>, <c>[attr|=v]</c>,
    /// <c>[attr^=v]</c>, <c>[attr$=v]</c> and <c>[attr*=v]</c>.
    /// </summary>
    public enum AttributeOperator : byte
    {
        /// <summary><c>[attr]</c>: the attribute has to be present.</summary>
        Exists,

        /// <summary><c>[attr=v]</c>: the value has to be exactly <c>v</c>.</summary>
        Equals,

        /// <summary><c>[attr~=v]</c>: the value has to contain <c>v</c> as a whitespace separated word.</summary>
        Includes,

        /// <summary><c>[attr|=v]</c>: the value has to be <c>v</c> or start with <c>v-</c>.</summary>
        DashMatch,

        /// <summary><c>[attr^=v]</c>: the value has to start with <c>v</c>.</summary>
        Prefix,

        /// <summary><c>[attr$=v]</c>: the value has to end with <c>v</c>.</summary>
        Suffix,

        /// <summary><c>[attr*=v]</c>: the value has to contain <c>v</c>.</summary>
        Substring
    }

    /// <summary>
    /// An attribute selector parsed once when the selector is stored, so matching
    /// never rescans selector tokens.
    /// </summary>
    /// <param name="NameId">The interned attribute name.</param>
    /// <param name="ValueId">The interned value, or -1 for <see cref="AttributeOperator.Exists"/>.</param>
    /// <param name="Operator">The comparison to perform.</param>
    /// <param name="IgnoreCase">The <c>i</c> flag, making the comparison case insensitive.</param>
    public readonly record struct AttributeFilter(
        int NameId,
        int ValueId,
        AttributeOperator Operator,
        bool IgnoreCase);
}
