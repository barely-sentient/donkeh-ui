using System.Reflection;
using System.Runtime.CompilerServices;

namespace Donkeh.UILab
{
    /// <summary>
    /// Resolves image assets declared through <see cref="ImageAttribute"/> from
    /// the embedded resources of loaded assemblies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The referenced image files must be embedded in the assembly at compile
    /// time, for example by listing them as
    /// <c>&lt;EmbeddedResource Include="Assets/logo.png" /&gt;</c> in the project
    /// file. This class supplies the encoded bytes; decoding and caching belong
    /// to the host (for example, the windowing image cache).
    /// </para>
    /// <para>
    /// A source may be a declared path such as <c>Assets/logo.png</c> or its file
    /// name alone, such as <c>logo.png</c>. The optional <c>packed:</c> and
    /// <c>embedded:</c> prefixes force a manifest lookup and are stripped before
    /// matching, which is useful when a file with the same name exists on disk.
    /// Plain paths are also accepted by the manifest lookup; callers that prefer
    /// an on-disk file simply try the file first.
    /// </para>
    /// <para>
    /// Declarations and resource names are discovered through reflection. The
    /// declarations are cached per assembly, but every call still reads the
    /// resource stream, so load an image once and keep the bytes when they are
    /// needed repeatedly. The methods are stateless and thread-safe.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [assembly: Image("Assets/logo.png")]
    ///
    /// if (Image.TryGetBytes("logo.png", out byte[] png))
    /// {
    ///     // Decode or upload the packed image.
    /// }
    ///
    /// byte[] required = Image.Load("packed:Assets/logo.png");
    /// </code>
    /// </example>
    /// <seealso cref="ImageAttribute"/>
    public static class Image
    {
        /// <summary>Prefix that forces a manifest resource lookup.</summary>
        private const string PackedPrefix = "packed:";

        /// <summary>Alias for <see cref="PackedPrefix"/>.</summary>
        private const string EmbeddedPrefix = "embedded:";

        /// <summary>Alias for <see cref="PackedPrefix"/>.</summary>
        private const string AssetPrefix = "asset:";

        /// <summary>
        /// Declared image paths, cached for the lifetime of each assembly. The
        /// attribute metadata of a loaded assembly cannot change, so a cache entry
        /// never has to be invalidated. A weak table keeps collectible assemblies
        /// unloadable.
        /// </summary>
        private static readonly ConditionalWeakTable<Assembly, string[]> DeclaredPaths =
            new();

        /// <summary>
        /// Tries to read an image declared in any assembly loaded into the current
        /// application domain.
        /// </summary>
        /// <param name="source">
        /// The declared path, a file name, or a path prefixed with <c>packed:</c>,
        /// <c>embedded:</c> or <c>asset:</c>.
        /// </param>
        /// <param name="bytes">
        /// Receives the encoded image bytes on success; otherwise an empty array.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when exactly one declared resource matched and
        /// was read; otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// The assembly set comes from <see cref="AppDomain.CurrentDomain"/> and
        /// grows as more assemblies are loaded, so an earlier failed call may
        /// succeed after a plugin or component assembly is loaded. Assemblies are
        /// searched in the order returned by the application domain; the first
        /// match wins when the same path is declared more than once.
        /// </remarks>
        public static bool TryGetBytes(string source, out byte[] bytes) =>
            TryGetBytes(AppDomain.CurrentDomain.GetAssemblies(), source, out bytes);

        /// <summary>
        /// Tries to read an image declared in one assembly.
        /// </summary>
        /// <param name="assembly">The assembly to scan.</param>
        /// <param name="source">
        /// The declared path, a file name, or a prefixed source.
        /// </param>
        /// <param name="bytes">
        /// Receives the encoded image bytes on success; otherwise an empty array.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a declared resource matched and was read;
        /// otherwise <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="assembly"/> is <see langword="null"/>.
        /// </exception>
        public static bool TryGetBytes(Assembly assembly, string source, out byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            return TryGetBytes(new[] { assembly }, source, out bytes);
        }

        /// <summary>
        /// Reads an image declared in any loaded assembly, throwing when it is not
        /// found.
        /// </summary>
        /// <param name="source">The declared path, file name or prefixed source.</param>
        /// <returns>The encoded image bytes.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="source"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="source"/> is empty or whitespace.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no declared embedded resource matches
        /// <paramref name="source"/> or the resource cannot be read.
        /// </exception>
        public static byte[] Load(string source)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            return TryGetBytes(source, out var bytes)
                ? bytes
                : throw new InvalidOperationException(
                    $"Packed image '{source}' was not found in the loaded assemblies.");
        }

        /// <summary>
        /// Reads an image declared in one assembly, throwing when it is not found.
        /// </summary>
        /// <param name="assembly">The assembly to scan.</param>
        /// <param name="source">The declared path, file name or prefixed source.</param>
        /// <returns>The encoded image bytes.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="assembly"/> or <paramref name="source"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="source"/> is empty or whitespace.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no declared embedded resource matches
        /// <paramref name="source"/> or the resource cannot be read.
        /// </exception>
        public static byte[] Load(Assembly assembly, string source)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            return TryGetBytes(assembly, source, out var bytes)
                ? bytes
                : throw new InvalidOperationException(
                    $"Packed image '{source}' was not found in '{assembly.FullName}'.");
        }

        /// <summary>
        /// Searches a sequence of assemblies for a declared image.
        /// </summary>
        /// <param name="assemblies">The assemblies to scan, in search order.</param>
        /// <param name="source">The declared path, file name or prefixed source.</param>
        /// <param name="bytes">
        /// Receives the encoded image bytes on success; otherwise an empty array.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a declared resource matched and was read;
        /// otherwise <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="assemblies"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// Null elements are skipped, as are duplicate assembly references, so the
        /// method is safe to call with a partially populated application-domain
        /// list.
        /// </remarks>
        public static bool TryGetBytes(
            IEnumerable<Assembly> assemblies,
            string source,
            out byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(assemblies);
            bytes = [];

            var requested = StripPrefix(source);

            if (string.IsNullOrWhiteSpace(requested))
            {
                return false;
            }

            foreach (var assembly in assemblies.Distinct())
            {
                if (assembly is null)
                {
                    continue;
                }

                try
                {
                    if (TryReadFromAssembly(assembly, requested, out bytes))
                    {
                        return true;
                    }
                }
                catch
                {
                    // A dynamic or partially loadable assembly must not break
                    // assets that another assembly can still provide.
                }
            }

            bytes = [];
            return false;
        }

        /// <summary>
        /// Determines whether a source carries an explicit packed-asset prefix.
        /// </summary>
        /// <param name="source">The source to inspect.</param>
        /// <returns>
        /// <see langword="true"/> for <c>packed:</c>, <c>embedded:</c> or
        /// <c>asset:</c> prefixes, ignoring case and leading whitespace.
        /// </returns>
        /// <remarks>
        /// Hosts can use this to skip an on-disk probe for a source that is
        /// explicitly meant to be an embedded resource.
        /// </remarks>
        public static bool IsPackedSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            var trimmed = source.TrimStart();

            return trimmed.StartsWith(PackedPrefix, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(EmbeddedPrefix, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Stops the prefix used to force a manifest lookup.
        /// </summary>
        /// <param name="source">The caller-supplied source.</param>
        /// <returns>
        /// The path without the prefix, or the trimmed source when no prefix is
        /// present.
        /// </returns>
        private static string StripPrefix(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            var trimmed = source.Trim();

            if (trimmed.StartsWith(PackedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[PackedPrefix.Length..].Trim();
            }

            if (trimmed.StartsWith(EmbeddedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[EmbeddedPrefix.Length..].Trim();
            }

            if (trimmed.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[AssetPrefix.Length..].Trim();
            }

            return trimmed;
        }

        /// <summary>
        /// Tries to read one declared resource from a single assembly.
        /// </summary>
        /// <param name="assembly">The assembly that owns the resource.</param>
        /// <param name="source">The path or file name to match.</param>
        /// <param name="bytes">Receives the encoded bytes on success.</param>
        /// <returns><see langword="true"/> when a resource was read.</returns>
        private static bool TryReadFromAssembly(
            Assembly assembly,
            string source,
            out byte[] bytes)
        {
            bytes = [];

            string[] declaredPaths;

            try
            {
                declaredPaths = GetDeclaredPaths(assembly);
            }
            catch
            {
                return false;
            }

            foreach (var declared in declaredPaths)
            {
                if (!PathMatches(declared, source))
                {
                    continue;
                }

                if (!TryFindResourceName(assembly, declared, out var resourceName))
                {
                    continue;
                }

                try
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);

                    if (stream is null)
                    {
                        continue;
                    }

                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    bytes = memory.ToArray();
                    return true;
                }
                catch
                {
                    // A resource that cannot be opened is treated as unavailable,
                    // matching the forgiving image pipeline used by the renderer.
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves a declared path to an embedded resource name.
        /// </summary>
        /// <param name="assembly">The assembly that owns the resource.</param>
        /// <param name="declaredPath">The path from an <see cref="ImageAttribute"/>.</param>
        /// <param name="resourceName">Receives the unique manifest resource name.</param>
        /// <returns>
        /// <see langword="true"/> when exactly one resource matched; otherwise
        /// <see langword="false"/> for no match or an ambiguous match.
        /// </returns>
        /// <remarks>
        /// A resource matches when its manifest name equals the normalised path or
        /// ends with <c>.</c> plus that path, ignoring case. If the full path
        /// matches nothing, the file name alone is tried, so
        /// <c>Assets/logo.png</c> can match <c>App.Assets.logo.png</c> even when the
        /// project root is not part of the manifest name.
        /// </remarks>
        private static bool TryFindResourceName(
            Assembly assembly,
            string declaredPath,
            out string resourceName)
        {
            resourceName = string.Empty;

            string[] names;

            try
            {
                names = assembly.GetManifestResourceNames();
            }
            catch
            {
                return false;
            }

            var normalized = NormalizePath(declaredPath);

            var matches = names
                .Where(name => string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("." + normalized, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                var fileName = NormalizePath(FileNameOf(declaredPath));
                matches = names
                    .Where(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (matches.Count != 1)
            {
                return false;
            }

            resourceName = matches[0];
            return true;
        }

        /// <summary>
        /// Collects the image paths declared on an assembly, its types and its
        /// modules.
        /// </summary>
        /// <param name="assembly">The assembly to scan.</param>
        /// <returns>
        /// The declared paths in discovery order: assembly attributes first, then
        /// type attributes in metadata order, then module attributes.
        /// </returns>
        /// <remarks>
        /// Attributes are read through <see cref="CustomAttributeData"/> rather
        /// than by instantiating them, so a partially loadable assembly does not
        /// fail merely because an attribute type cannot be resolved. A
        /// <see cref="ReflectionTypeLoadException"/> degrades to the types that did
        /// load.
        /// </remarks>
        private static string[] GetDeclaredPaths(Assembly assembly) =>
            DeclaredPaths.GetOrAdd(assembly, CollectPaths);

        /// <summary>
        /// Implements <see cref="GetDeclaredPaths"/> for a new assembly.
        /// </summary>
        /// <param name="assembly">The assembly to scan.</param>
        /// <returns>The declared paths.</returns>
        private static string[] CollectPaths(Assembly assembly)
        {
            var paths = new List<string>();

            // Assembly-level declarations.
            try
            {
                foreach (var data in assembly.GetCustomAttributesData())
                {
                    if (IsImageAttribute(data) && TryGetPath(data) is { } assemblyPath)
                    {
                        paths.Add(assemblyPath);
                    }
                }
            }
            catch
            {
                // Metadata that cannot be read simply contributes no declarations.
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
            catch
            {
                // Dynamic assemblies and partially loaded modules are allowed to
                // fail type enumeration; assembly and module attributes still work.
                types = [];
            }

            foreach (var type in types)
            {
                try
                {
                    foreach (var data in type.GetCustomAttributesData())
                    {
                        if (IsImageAttribute(data) && TryGetPath(data) is { } typePath)
                        {
                            paths.Add(typePath);
                        }
                    }
                }
                catch
                {
                    // Skip a type whose metadata cannot be read.
                }
            }

            // Module-level declarations.
            try
            {
                foreach (var module in assembly.GetModules())
                {
                    foreach (var data in module.GetCustomAttributesData())
                    {
                        if (IsImageAttribute(data) && TryGetPath(data) is { } modulePath)
                        {
                            paths.Add(modulePath);
                        }
                    }
                }
            }
            catch
            {
                // A module that cannot be enumerated contributes no declarations.
            }

            return paths.ToArray();
        }

        /// <summary>
        /// Determines whether an attribute is an image declaration.
        /// </summary>
        /// <param name="data">The attribute metadata to inspect.</param>
        /// <returns>
        /// <see langword="true"/> when the attribute type is named
        /// <c>ImageAttribute</c>.
        /// </returns>
        /// <remarks>
        /// The comparison is by simple type name, not identity, so any attribute
        /// with that name — including the copy in <c>Donkeh.Windowing</c> — is
        /// accepted without loading the attribute type.
        /// </remarks>
        private static bool IsImageAttribute(CustomAttributeData data) =>
            string.Equals(data.AttributeType.Name, "ImageAttribute", StringComparison.Ordinal);

        /// <summary>
        /// Extracts the image path from a declaration's metadata.
        /// </summary>
        /// <param name="data">The image attribute metadata.</param>
        /// <returns>
        /// The trimmed path, or <see langword="null"/> when no non-blank path is
        /// present.
        /// </returns>
        /// <remarks>
        /// The first constructor argument is preferred. When it is absent or blank,
        /// a named argument called <c>Path</c> is accepted as a fallback.
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
        /// Determines whether a declared path describes the requested source.
        /// </summary>
        /// <param name="declared">The path from an <see cref="ImageAttribute"/>.</param>
        /// <param name="requested">The source supplied by the caller.</param>
        /// <returns><see langword="true"/> when the paths are equivalent.</returns>
        /// <remarks>
        /// The comparison accepts a full relative path, a suffix of it, or a
        /// matching file name, mirroring the stylesheet loader's fallback so
        /// <c>logo.png</c> can find <c>Assets/logo.png</c>.
        /// </remarks>
        private static bool PathMatches(string declared, string requested)
        {
            var declaredPath = NormalizePath(declared);
            var requestedPath = NormalizePath(requested);

            if (string.Equals(declaredPath, requestedPath, StringComparison.OrdinalIgnoreCase)
                || declaredPath.EndsWith("." + requestedPath, StringComparison.OrdinalIgnoreCase)
                || requestedPath.EndsWith("." + declaredPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // The file-name fallback is only safe when one side is a bare file
            // name. Two different folders must not resolve to each other's
            // resources merely because the file names match.
            var declaredHasDirectory = HasDirectory(declared);
            var requestedHasDirectory = HasDirectory(requested);

            return (!declaredHasDirectory || !requestedHasDirectory)
                && string.Equals(
                    FileNameOf(declared),
                    FileNameOf(requested),
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a path contains a directory component.
        /// </summary>
        /// <param name="path">The path to inspect.</param>
        /// <returns>
        /// <see langword="true"/> when a <c>/</c> or <c>\</c> appears anywhere in
        /// the path.
        /// </returns>
        private static bool HasDirectory(string path) =>
            path.Contains('/') || path.Contains('\\');

        /// <summary>
        /// Returns the file-name component of a path without relying on the host
        /// platform's separator.
        /// </summary>
        /// <param name="path">The path to inspect.</param>
        /// <returns>The text after the last <c>/</c> or <c>\</c>.</returns>
        private static string FileNameOf(string path)
        {
            var separator = path.LastIndexOfAny(['/', '\\']);
            return (separator >= 0 ? path[(separator + 1)..] : path).Trim();
        }

        /// <summary>
        /// Converts a project-style path into the dot-separated form used by
        /// manifest resource names.
        /// </summary>
        /// <param name="path">The path to normalise.</param>
        /// <returns>The trimmed path with separators replaced by dots.</returns>
        /// <remarks>
        /// Leading dots, slashes and backslashes are removed, so
        /// <c>/Assets/logo.png</c> and <c>Assets\logo.png</c> both become
        /// <c>Assets.logo.png</c>.
        /// </remarks>
        private static string NormalizePath(string path)
        {
            var trimmed = path.Trim().TrimStart('.', '/', '\\');
            return trimmed.Replace('\\', '.').Replace('/', '.');
        }
    }
}
