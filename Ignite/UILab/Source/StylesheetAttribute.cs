namespace Ignite.UILab
{
    /// <summary>
    /// Declares a stylesheet that should be embedded in the final executable at
    /// compile time (as an <c>EmbeddedResource</c>) and merged into the single
    /// stylesheet applied by the engine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Apply this attribute to an assembly, class, struct or module, passing the
    /// path of the <c>.css</c> file relative to the project root. Multiple
    /// attributes are merged in declaration order.
    /// </para>
    /// <para>
    /// The attribute is metadata only; it does not itself embed the file or load
    /// anything. <see cref="Stylesheet.Load()"/> scans these declarations at
    /// runtime and resolves each path against the assembly's manifest resource
    /// names, so the file must actually be an <c>EmbeddedResource</c> in the same
    /// assembly.
    /// </para>
    /// <para>
    /// Declarations are not inherited by derived types and are not deduplicated,
    /// so declaring the same path twice produces the rules twice.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using static Ignite.UILab._;
    ///
    /// [assembly: Stylesheet("Styles/app.css")]
    ///
    /// [Stylesheet("Styles/card.css")]
    /// public sealed class Card : Component
    /// {
    ///     public override Node Render() => H("div", new Props { ClassName = "card" });
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="Stylesheet"/>
    [AttributeUsage(
        AttributeTargets.Assembly |
        AttributeTargets.Class |
        AttributeTargets.Struct |
        AttributeTargets.Module,
        AllowMultiple = true,
        Inherited = false)]
    public sealed class StylesheetAttribute : Attribute
    {
        /// <summary>The path of the stylesheet relative to the project root.</summary>
        /// <value>
        /// The trimmed constructor argument, using either <c>/</c> or <c>\</c> as
        /// the separator.
        /// </value>
        public string Path { get; }

        /// <summary>
        /// Creates a stylesheet declaration.
        /// </summary>
        /// <param name="path">
        /// The path of an embedded <c>.css</c> file, relative to the project root.
        /// It is trimmed but otherwise stored verbatim.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="path"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="path"/> is empty or whitespace.
        /// </exception>
        public StylesheetAttribute(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            Path = path.Trim();
        }
    }
}