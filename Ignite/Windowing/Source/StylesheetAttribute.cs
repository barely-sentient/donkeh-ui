namespace Ignite.Windowing
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
    /// attributes are allowed on one target and are merged in declaration order.
    /// Declarations are not inherited, so derived types do not pick up a base
    /// type's sheets.
    /// </para>
    /// <para>
    /// The attribute is metadata only: it does not embed the file or load
    /// anything. The loader scans for attributes whose simple type name is
    /// <c>StylesheetAttribute</c>, so this windowing copy and
    /// <see cref="Ignite.UILab.StylesheetAttribute"/> are interchangeable, but the
    /// file must still be an <c>EmbeddedResource</c> in the same assembly.
    /// Declaring the same path twice appends its rules twice.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [assembly: Stylesheet("Styles/app.css")]
    ///
    /// [Stylesheet("Styles/card.css")]
    /// public sealed class Card { /* ... */ }
    /// </code>
    /// </example>
    /// <seealso cref="Stylesheet"/>
    /// <seealso cref="Ignite.UILab.StylesheetAttribute"/>
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
        /// the separator; stored verbatim otherwise.
        /// </value>
        public string Path { get; }

        /// <summary>
        /// Creates a stylesheet declaration.
        /// </summary>
        /// <param name="path">
        /// The path of an embedded <c>.css</c> file, relative to the project root.
        /// Surrounding whitespace is removed; the rest is stored unchanged.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="path"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="path"/> is empty or whitespace.
        /// </exception>
        /// <remarks>
        /// The constructor performs no file-system or manifest lookup; a bad path
        /// only fails later when <see cref="Stylesheet.Load()"/> resolves it.
        /// </remarks>
        public StylesheetAttribute(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            Path = path.Trim();
        }
    }
}