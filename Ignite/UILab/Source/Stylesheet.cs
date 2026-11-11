using System.Reflection;

namespace Ignite.UILab
{
    /// <summary>
    /// Loads and merges stylesheets declared through <see cref="StylesheetAttribute"/>.
    /// </summary>
    /// <remarks>
    /// The referenced <c>.css</c> files must be embedded in the assembly at
    /// compile time, for example by listing them as
    /// <c>&lt;EmbeddedResource Include="path/to/file.css" /&gt;</c> in the
    /// project file. At runtime every matching embedded resource is read and
    /// concatenated into a single stylesheet.
    /// </remarks>
    public static class Stylesheet
    {
        /// <summary>
        /// Loads and merges every stylesheet declared in the currently loaded
        /// assemblies.
        /// </summary>
        public static string Load() => Load(AppDomain.CurrentDomain.GetAssemblies());

        /// <summary>
        /// Loads and merges every stylesheet declared in <paramref name="assembly"/>.
        /// </summary>
        public static string Load(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            return Load(new[] { assembly });
        }

        /// <summary>
        /// Loads and merges every stylesheet declared in the supplied assemblies.
        /// </summary>
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

        private static bool IsStylesheetAttribute(CustomAttributeData data) =>
            string.Equals(data.AttributeType.Name, "StylesheetAttribute", StringComparison.Ordinal);

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

        private static string NormalizePath(string path)
        {
            var trimmed = path.Trim().TrimStart('.', '/', '\\');
            return trimmed.Replace('\\', '.').Replace('/', '.');
        }
    }
}