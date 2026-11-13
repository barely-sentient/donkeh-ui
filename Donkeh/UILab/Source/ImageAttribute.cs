namespace Donkeh.UILab
{
    /// <summary>
    /// Declares an image that should be embedded in the final executable at
    /// compile time (as an <c>EmbeddedResource</c>) and can be referenced from
    /// image sources.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Apply this attribute to an assembly, class, struct or module, passing the
    /// path of the image file relative to the project root. Multiple attributes
    /// are allowed on one target and are resolved in declaration order.
    /// </para>
    /// <para>
    /// The attribute is metadata only; it does not embed the file or load
    /// anything. <see cref="Image.TryGetBytes(string, out byte[])"/> scans these
    /// declarations at runtime and resolves each path against the assembly's
    /// manifest resource names, so the file must actually be an
    /// <c>EmbeddedResource</c> in the same assembly. A source used by an
    /// <c>img</c> element, a CSS <c>url(...)</c> layer or the window icon is
    /// matched against the declared paths; when no file exists at that path the
    /// embedded bytes are used instead.
    /// </para>
    /// <para>
    /// Declarations are not inherited by derived types and are not
    /// deduplicated, so declaring the same path twice resolves to the same
    /// embedded resource twice.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using static Donkeh.UILab._;
    ///
    /// [assembly: Image("Assets/logo.png")]
    ///
    /// [Image("Assets/card.png")]
    /// public sealed class Card : Component
    /// {
    ///     public override Node Render() =>
    ///         H("img", new Props { Src = "Assets/logo.png" });
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="Image"/>
    [AttributeUsage(
        AttributeTargets.Assembly |
        AttributeTargets.Class |
        AttributeTargets.Struct |
        AttributeTargets.Module,
        AllowMultiple = true,
        Inherited = false)]
    public sealed class ImageAttribute : Attribute
    {
        /// <summary>The path of the image relative to the project root.</summary>
        /// <value>
        /// The trimmed constructor argument, using either <c>/</c> or <c>\</c> as
        /// the separator.
        /// </value>
        public string Path { get; }

        /// <summary>
        /// Creates a packed-image declaration.
        /// </summary>
        /// <param name="path">
        /// The path of an embedded image file, relative to the project root. It is
        /// trimmed but otherwise stored verbatim.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="path"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="path"/> is empty or whitespace.
        /// </exception>
        public ImageAttribute(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            Path = path.Trim();
        }
    }
}
