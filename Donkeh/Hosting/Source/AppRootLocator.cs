using System.Reflection;
using Donkeh.UILab;
using Donkeh.Windowing;

namespace Donkeh.Hosting;

/// <summary>
/// Finds the application's <see cref="AppRootAttribute"/> and creates the host
/// definition used by every platform entry point.
/// </summary>
public static class AppRootLocator
{
    /// <summary>Locates the application root.</summary>
    /// <returns>The root component and its settings.</returns>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="AppRootAttribute"/> or legacy <c>DonkehApp</c> declaration was
    /// found, or the attributed type is not a component.
    /// </exception>
    /// <remarks>
    /// The entry assembly is checked first, then every loaded assembly. The legacy
    /// <c>[DonkehApp]</c> contract is still accepted when no app-root attribute is
    /// present, so existing applications keep running during migration.
    /// </remarks>
    public static AppRootDefinition Find()
    {
        var entry = Assembly.GetEntryAssembly();

        if (entry is not null && TryRead(entry) is { } fromEntry)
        {
            return fromEntry;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ReferenceEquals(assembly, entry))
            {
                continue;
            }

            if (TryRead(assembly) is { } fromAssembly)
            {
                return fromAssembly;
            }
        }

        try
        {
            var legacy = DonkehAppLocator.Find();
            return new AppRootDefinition(
                legacy.BuildRoot,
                legacy.Styles(),
                legacy.Settings);
        }
        catch (InvalidOperationException)
        {
        }

        throw new InvalidOperationException(
            "No Donkeh application root was found. Add " +
            "[AppRoot] to the root component, for example " +
            "[AppRoot(Width = 1180, Height = 760)] public sealed class App : Component.");
    }

    /// <summary>Reads one assembly for an attributed root component.</summary>
    private static AppRootDefinition? TryRead(Assembly assembly)
    {
        Type[] types;

        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types
                .Where(static type => type is not null)
                .Cast<Type>()
                .ToArray();
        }

        foreach (var type in types)
        {
            var attribute = type.GetCustomAttribute<AppRootAttribute>(inherit: false);

            if (attribute is null)
            {
                continue;
            }

            if (!typeof(Component).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"[AppRoot] on '{type.FullName}' requires a type derived from " +
                    $"{nameof(Component)}.");
            }

            if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null)
            {
                throw new InvalidOperationException(
                    $"[AppRoot] on '{type.FullName}' requires a public parameterless constructor.");
            }

            return new AppRootDefinition(type, attribute.ToWindowSettings());
        }

        return null;
    }
}
