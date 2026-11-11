using System.Reflection;

namespace Donkeh.UILab
{
    /// <summary>
    /// Loads and merges stylesheets declared through <see cref="StylesheetAttribute"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The referenced <c>.css</c> files must be embedded in the assembly at
    /// compile time, for example by listing them as
    /// <c>&lt;EmbeddedResource Include="path/to/file.css" /&gt;</c> in the
    /// project file. At runtime every matching embedded resource is read and
    /// concatenated into a single stylesheet.
    /// </para>
    /// <para>
    /// The text is returned as-is: parsing and the cascade are handled by the
    /// CSS engine, not here. Assemblies and declarations are merged in discovery
    /// order (assembly-level attributes, then type-level, then module-level), so
    /// later rules win where the cascade allows it. Nothing is cached; every call
    /// re-enumerates attributes and re-reads the resources.
    /// </para>
    /// <para>
    /// The methods are stateless and thread-safe, but repeated calls from a render
    /// loop are expensive because reflection and stream reads are repeated. Load
    /// once at startup and keep the returned string.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [assembly: Stylesheet("Styles/app.css")]
    ///
    /// string css = Stylesheet.Load();
    /// </code>
    /// </example>
    /// <seealso cref="StylesheetAttribute"/>
    public static class Stylesheet
    {
        /// <summary>
        /// Loads and merges every stylesheet declared in the currently loaded
        /// assemblies.
        /// </summary>
        /// <returns>
        /// The concatenated CSS text, separated by a newline, or an empty string
        /// when nothing is declared.
        /// </returns>
        /// <remarks>
        /// The set of assemblies comes from
        /// <see cref="AppDomain.CurrentDomain"/> and changes as assemblies are
        /// loaded, so a later call may return more rules than an earlier one. The
        /// scanning cost grows with the number of loaded assemblies and types.
        /// </remarks>
        public static string Load() => Load(AppDomain.CurrentDomain.GetAssemblies());

        /// <summary>
        /// Loads and merges every stylesheet declared in <paramref name="assembly"/>.
        /// </summary>
        /// <param name="assembly">The assembly to scan.</param>
        /// <returns>The concatenated CSS text, or an empty string when none is declared.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="assembly"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a declared path matches no embedded resource, matches more
        /// than one, or cannot be opened.
        /// </exception>
        public static string Load(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            return Load(new[] { assembly });
        }

        /// <summary>
        /// Loads and merges every stylesheet declared in the supplied assemblies.
        /// </summary>
        /// <param name="assemblies">
        /// The assemblies to scan. Duplicates are ignored; every element must be
        /// non-null.
        /// </param>
        /// <returns>
        /// The concatenated CSS text in assembly order, with one newline between
        /// parts, or an empty string when nothing is declared.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="assemblies"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a declared path matches no embedded resource, matches more
        /// than one, or cannot be opened.
        /// </exception>
        /// <remarks>
        /// The same stylesheet declared in two scanned assemblies is read and
        /// appended twice; the result is not deduplicated by path. Pass a single
        /// assembly when a shared stylesheet must appear only once.
        /// </remarks>
        public static string Load(params Assembly[] assemblies)
        {
            ArgumentNullException.ThrowIfNull(assemblies);

            var parts = new List<string>();

            foreach (var assembly in assemblies.Distinct())
            {
                foreach (var path in CollectPaths(assembly))
                {
                    parts.Add(ReadResource(assembly, path));
                }
            }

            return string.Join("\n", parts);
        }

        /// <summary>
        /// Collects the declared stylesheet paths from every level of an assembly.
        /// </summary>
        /// <param name="assembly">The assembly to scan.</param>
        /// <returns>
        /// The paths in discovery order: assembly attributes first, then type
        /// attributes in metadata order, then module attributes.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Attributes are read through <see cref="CustomAttributeData"/> rather than
        /// by instantiating them, so a partially loadable assembly does not fail
        /// merely because an attribute type cannot be resolved.
        /// </para>
        /// <para>
        /// <see cref="Assembly.GetTypes"/> can throw
        /// <see cref="ReflectionTypeLoadException"/> when some referenced types are
        /// missing. The exception's successfully loaded types are used instead, so
        /// the scan degrades instead of failing.
        /// </para>
        /// </remarks>
        private static IEnumerable<string> CollectPaths(Assembly assembly)
        {
            var paths = new List<string>();

            // Assembly-level declarations.
            foreach (var data in assembly.GetCustomAttributesData())
            {
                if (IsStylesheetAttribute(data) && TryGetPath(data) is { } assemblyPath)
                {
                    paths.Add(assemblyPath);
                }
            }

            // Type-level declarations.
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                foreach (var data in type.GetCustomAttributesData())
                {
                    if (IsStylesheetAttribute(data) && TryGetPath(data) is { } typePath)
                    {
                        paths.Add(typePath);
                    }
                }
            }

            // Module-level declarations.
            foreach (var module in assembly.GetModules())
            {
                foreach (var data in module.GetCustomAttributesData())
                {
                    if (IsStylesheetAttribute(data) && TryGetPath(data) is { } modulePath)
                    {
                        paths.Add(modulePath);
                    }
                }
            }

            return paths;
        }

        /// <summary>
        /// Determines whether a custom attribute is a stylesheet declaration.
        /// </summary>
        /// <param name="data">The attribute metadata to inspect.</param>
        /// <returns>
        /// <see langword="true"/> when the attribute type is named
        /// <c>StylesheetAttribute</c>.
        /// </returns>
        /// <remarks>
        /// The comparison is by simple type name, not by identity, so any attribute
        /// with that name — including a separate copy in another assembly — is
        /// accepted. This deliberately avoids loading the attribute type.
        /// </remarks>
        private static bool IsStylesheetAttribute(CustomAttributeData data) =>
            string.Equals(data.AttributeType.Name, "StylesheetAttribute", StringComparison.Ordinal);

        /// <summary>
        /// Extracts the stylesheet path from a declaration's metadata.
        /// </summary>
        /// <param name="data">The stylesheet attribute metadata.</param>
        /// <returns>
        /// The trimmed path, or <see langword="null"/> when no non-blank path is
        /// present.
        /// </returns>
        /// <remarks>
        /// The first constructor argument is preferred. When it is absent or blank,
        /// a named argument called <c>Path</c> is accepted as a fallback; the current
        /// <see cref="StylesheetAttribute.Path"/> is get-only, so that branch exists
        /// only for compatibility with declarations that set it as a named argument.
        /// </remarks>
        private static string? TryGetPath(CustomAttributeData data)
        {
            if (data.ConstructorArguments.Count > 0
                && data.ConstructorArguments[0].Value is string constructorPath
                && !string.IsNullOrWhiteSpace(constructorPath))
            {
                return constructorPath.Trim();
            }

            foreach (var named in data.NamedArguments)
            {
                if (string.Equals(named.MemberName, "Path", StringComparison.Ordinal)
                    && named.TypedValue.Value is string namedPath
                    && !string.IsNullOrWhiteSpace(namedPath))
                {
                    return namedPath.Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// Reads the embedded resource that a declared stylesheet path refers to.
        /// </summary>
        /// <param name="assembly">The assembly that contains the resource.</param>
        /// <param name="path">The declared path, for example <c>Styles/app.css</c>.</param>
        /// <returns>The resource text.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no resource matches, when the match is ambiguous, or when the
        /// resource stream cannot be opened.
        /// </exception>
        /// <remarks>
        /// <para>
        /// A resource matches when its manifest name equals the normalised path or
        /// ends with <c>.</c> plus that path, ignoring case. This accepts both
        /// <c>App.Styles.app.css</c> and <c>Styles.app.css</c> for the input
        /// <c>Styles/app.css</c>.
        /// </para>
        /// <para>
        /// If the full path matches nothing, the file name alone is tried as a
        /// fallback, so <c>app.css</c> can match <c>App.Theme.app.css</c>. The
        /// fallback is what makes the ambiguity check important: a file name shared
        /// by two folders raises instead of silently picking one.
        /// </para>
        /// </remarks>
        private static string ReadResource(Assembly assembly, string path)
        {
            var names = assembly.GetManifestResourceNames();
            var normalized = NormalizePath(path);

            var matches = names
                .Where(name => string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("." + normalized, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                var fileName = NormalizePath(Path.GetFileName(path));
                matches = names
                    .Where(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (matches.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Embedded resource for stylesheet '{path}' was not found in '{assembly.FullName}'. " +
                    $"Available resources: {string.Join(", ", names)}");
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple embedded resources match stylesheet '{path}': {string.Join(", ", matches)}");
            }

            using var stream = assembly.GetManifestResourceStream(matches[0])
                ?? throw new InvalidOperationException($"Could not open embedded resource '{matches[0]}'.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Converts a project-style path into the dot-separated form used by
        /// manifest resource names.
        /// </summary>
        /// <param name="path">The path to normalise.</param>
        /// <returns>The trimmed path with separators replaced by dots.</returns>
        /// <remarks>
        /// Leading dots, slashes and backslashes are removed, so <c>/Styles/app.css</c>
        /// and <c>Styles\app.css</c> both become <c>Styles.app.css</c>. No other
        /// validation is performed.
        /// </remarks>
        private static string NormalizePath(string path)
        {
            var trimmed = path.Trim().TrimStart('.', '/', '\\');
            return trimmed.Replace('\\', '.').Replace('/', '.');
        }
    }
}