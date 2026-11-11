namespace Ignite.Data
{
    /// <summary>
    /// A stylesheet import recorded for the host to resolve. The engine does not
    /// fetch or inline imported stylesheets.
    /// </summary>
    /// <param name="UrlId">The interned url text (quotes already stripped).</param>
    /// <param name="LayerId">
    /// The cascade layer the import is assigned to, or -1 when the import is not
    /// layered.
    /// </param>
    /// <param name="ConditionId">
    /// The media condition gating the import, or -1 when unconditional. The
    /// condition is a normal table entry, so it is also reachable through
    /// <see cref="DataTable.GetConditionTokens"/>.
    /// </param>
    /// <param name="SupportsConditionId">
    /// The <c>supports()</c> condition gating the import, or -1 when none was
    /// given.
    /// </param>
    /// <remarks>
    /// The record is intentionally just ids: resolving the url and importing the
    /// sheet is the host's responsibility, which keeps network and file access out
    /// of the parser.
    /// </remarks>
    public readonly record struct CssImport(
        int UrlId,
        int LayerId,
        int ConditionId,
        int SupportsConditionId);

    /// <summary>
    /// A namespace declaration recorded for the host. The engine stores the
    /// mapping; selectors are not rewritten.
    /// </summary>
    /// <param name="PrefixId">The interned namespace prefix, or -1 for a default namespace.</param>
    /// <param name="UriId">The interned namespace uri text.</param>
    public readonly record struct CssNamespace(
        int PrefixId,
        int UriId);

    /// <summary>
    /// A cascade layer declaration. <see cref="Order"/> is the declaration order
    /// among layers, used by the host for cascade ordering.
    /// </summary>
    /// <param name="NameId">The interned layer name.</param>
    /// <param name="Order">
    /// The zero-based position of the layer among all declared layers. Lower
    /// values are weaker in the cascade, and unlayered rules always win over
    /// layered ones.
    /// </param>
    /// <remarks>
    /// The order is assigned once when the layer is first seen by
    /// <see cref="DataTable.GetOrAddLayer"/> and never changes, so later imports of
    /// the same name reuse the existing entry.
    /// </remarks>
    public readonly record struct CssLayer(
        int NameId,
        int Order);

    /// <summary>
    /// A scope boundary recorded for the host. The prelude (root/limit selectors)
    /// is stored as interned text.
    /// </summary>
    /// <param name="PreludeId">The interned text of the <c>@scope</c> prelude.</param>
    public readonly record struct CssScope(
        int PreludeId);

    /// <summary>
    /// A single keyframe declaration.
    /// </summary>
    /// <param name="NameId">The interned animation name.</param>
    /// <param name="KeyId">
    /// The selector id of the keyframe selector (for example <c>from</c> or
    /// <c>50%</c>), indexing <see cref="DataTable.Selectors"/>.
    /// </param>
    /// <param name="PropertyId">The interned property name.</param>
    /// <param name="ValueId">The interned declaration value.</param>
    /// <param name="ConditionId">
    /// The condition gating the declaration, or -1 when unconditional.
    /// </param>
    public readonly record struct CssKeyframe(
        int NameId,
        int KeyId,
        int PropertyId,
        int ValueId,
        int ConditionId);

    /// <summary>
    /// A descriptor inside an at-rule such as @font-face, @page, @property,
    /// @counter-style, @font-palette-values or @view-transition.
    /// </summary>
    /// <param name="AtRuleId">
    /// The interned at-rule name including the leading '@', for example
    /// <c>"@font-face"</c>.
    /// </param>
    /// <param name="ContextId">
    /// The interned at-rule prelude, or -1 when the at-rule has none.
    /// </param>
    /// <param name="PropertyId">The interned descriptor name.</param>
    /// <param name="ValueId">The interned descriptor value.</param>
    public readonly record struct CssDescriptor(
        int AtRuleId,
        int ContextId,
        int PropertyId,
        int ValueId);

    /// <summary>
    /// A margin box inside an @page rule (for example @top-center).
    /// </summary>
    /// <param name="BoxId">
    /// The interned margin box name including the leading '@', for example
    /// <c>"@top-center"</c>.
    /// </param>
    /// <param name="PropertyId">The interned property name.</param>
    /// <param name="ValueId">The interned declaration value.</param>
    public readonly record struct CssMarginBox(
        int BoxId,
        int PropertyId,
        int ValueId);

    /// <summary>
    /// A charset declaration.
    /// </summary>
    /// <param name="CharsetId">The interned charset name.</param>
    /// <remarks>
    /// The engine records the declaration for fidelity only; it does not re-decode
    /// the source, which has already been read as text by the time parsing starts.
    /// </remarks>
    public readonly record struct CssCharset(
        int CharsetId);
}
