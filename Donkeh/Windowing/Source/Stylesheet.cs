using System.Reflection;

namespace Donkeh.Windowing
{
    /// <summary>
    /// Loads and merges stylesheets declared through <see cref="StylesheetAttribute"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a thin facade over <see cref="Donkeh.UILab.Stylesheet"/> for code
    /// that already depends on the windowing namespace. The semantics, ordering
    /// and failures are identical: declarations are discovered by attribute name,
    /// resolved against embedded manifest resources and concatenated with a
    /// newline between parts.
    /// </para>
    /// <para>
    /// Every call re-scans attributes and re-reads resources; nothing is cached.
    /// Load once at startup and keep the returned string;
    /// <see cref="DonkehWindow"/> does this when it seeds its author sheets and
    /// parses the text after the user-agent sheet. The methods are stateless and
    /// thread-safe on their own, but the returned text still has to be parsed on
    /// the UI thread.
    /// </para>
    /// </remarks>
    /// <seealso cref="StylesheetAttribute"/>
    /// <seealso cref="Donkeh.UILab.Stylesheet"/>
    public static class Stylesheet
    {
        /// <summary>
        /// Loads and merges every stylesheet declared in the currently loaded
        /// assemblies.
        /// </summary>
        /// <returns>
        /// The concatenated CSS text, or an empty string when nothing is declared.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a declared path matches no embedded resource, matches more
        /// than one, or cannot be opened.
        /// </exception>
        /// <remarks>
        /// The assembly set comes from <see cref="AppDomain.CurrentDomain"/>, so it
        /// grows as more assemblies are loaded. See
        /// <see cref="Donkeh.UILab.Stylesheet.Load()"/> for the discovery order and
        /// performance notes.
        /// </remarks>
        public static string Load() => Donkeh.UILab.Stylesheet.Load();

        /// <summary>
        /// Loads and merges every stylesheet declared in one assembly.
        /// </summary>
        /// <param name="assembly">The assembly to scan.</param>
        /// <returns>The concatenated CSS text, or an empty string.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="assembly"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a declared path matches no embedded resource, matches more
        /// than one, or cannot be opened.
        /// </exception>
        public static string Load(Assembly assembly) => Donkeh.UILab.Stylesheet.Load(assembly);

        /// <summary>
        /// Loads and merges every stylesheet declared in the supplied assemblies.
        /// </summary>
        /// <param name="assemblies">
        /// The assemblies to scan, in merge order. Duplicates are ignored; no
        /// element may be <see langword="null"/>.
        /// </param>
        /// <returns>
        /// The concatenated CSS text, or an empty string when nothing is declared.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="assemblies"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a declared path matches no embedded resource, matches more
        /// than one, or cannot be opened.
        /// </exception>
        /// <remarks>
        /// The same stylesheet declared in two scanned assemblies is appended
        /// twice because merging is by assembly, not by path. Pass a single
        /// assembly when a shared sheet must appear once.
        /// </remarks>
        public static string Load(params Assembly[] assemblies) => Donkeh.UILab.Stylesheet.Load(assemblies);
    }
}