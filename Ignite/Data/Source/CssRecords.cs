namespace Ignite.Data
{
    /// <summary>
    /// A stylesheet import recorded for the host to resolve. The engine does not
    /// fetch or inline imported stylesheets.
    /// </summary>
    public readonly record struct CssImport(
        int UrlId,
        int LayerId,
        int ConditionId,
        int SupportsConditionId);

    /// <summary>
    /// A namespace declaration recorded for the host. The engine stores the
    /// mapping; selectors are not rewritten.
    /// </summary>
    public readonly record struct CssNamespace(
        int PrefixId,
        int UriId);

    /// <summary>
    /// A cascade layer declaration. <see cref="Order"/> is the declaration order
    /// among layers, used by the host for cascade ordering.
    /// </summary>
    public readonly record struct CssLayer(
        int NameId,
        int Order);

    /// <summary>
    /// A scope boundary recorded for the host. The prelude (root/limit selectors)
    /// is stored as interned text.
    /// </summary>
    public readonly record struct CssScope(
        int PreludeId);

    /// <summary>
    /// A single keyframe declaration.
    /// </summary>
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
    public readonly record struct CssDescriptor(
        int AtRuleId,
        int ContextId,
        int PropertyId,
        int ValueId);

    /// <summary>
    /// A margin box inside an @page rule (for example @top-center).
    /// </summary>
    public readonly record struct CssMarginBox(
        int BoxId,
        int PropertyId,
        int ValueId);

    /// <summary>
    /// A charset declaration.
    /// </summary>
    public readonly record struct CssCharset(
        int CharsetId);
}
