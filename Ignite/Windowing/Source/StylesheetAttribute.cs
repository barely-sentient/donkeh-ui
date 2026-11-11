namespace Ignite.Windowing
{
    /// <summary>
    /// Declares a stylesheet that should be embedded in the final executable at
    /// compile time (as an <c>EmbeddedResource</c>) and merged into the single
    /// stylesheet applied by the engine.
    /// </summary>
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