using System.Reflection;

namespace Donkeh.UILab
{
    /// <summary>
    /// One stylesheet declaration together with its resolved CSS text.
    /// </summary>
    /// <remarks>
    /// Sources are produced by <see cref="Stylesheet.Sources()"/>. They expose the
    /// declaring assembly and the declared project-relative path so development
    /// tooling can map embedded stylesheets back to files on disk. Application code
    /// normally uses <see cref="Stylesheet.Load()"/> instead.
    /// </remarks>
    /// <seealso cref="Stylesheet"/>
    /// <seealso cref="StylesheetAttribute"/>
    public sealed class StylesheetSource
    {
        /// <summary>Creates a source for one declaration.</summary>
        /// <param name="assembly">The assembly that declares and embeds the stylesheet.</param>
        /// <param name="path">The declared project-relative path.</param>
        /// <param name="css">The embedded CSS text.</param>
        internal StylesheetSource(Assembly assembly, string path, string css)
        {
            Assembly = assembly;
            Path = path;
            Css = css;
        }

        /// <summary>Gets the assembly that declares and embeds the stylesheet.</summary>
        public Assembly Assembly { get; }

        /// <summary>
        /// Gets the declared path, for example <c>src/Components/Card/Card.css</c>.
        /// </summary>
        public string Path { get; }

        /// <summary>Gets the CSS text read from the embedded resource.</summary>
        public string Css { get; }
    }
}
