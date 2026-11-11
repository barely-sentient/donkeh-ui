namespace Ignite.UILab
{
    /// <summary>
    /// Declares a stylesheet that should be embedded in the final executable at
    /// compile time (as an <c>EmbeddedResource</c>) and merged into the single
    /// stylesheet applied by the engine.
    /// </summary>
    /// <remarks>
    /// Apply this attribute to an assembly, type or module, passing the path of
    /// the <c>.css</c> file relative to the project root. Multiple attributes
    /// are merged in declaration order.
    /// </remarks>
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
        public string Path { get; }

        public StylesheetAttribute(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            Path = path.Trim();
        }
    }
}