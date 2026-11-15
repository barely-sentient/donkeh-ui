using System.Reflection;
using Donkeh.Windowing;

namespace Donkeh.Windowing.Android;

/// <summary>
/// Finds the <see cref="IDonkehApp"/> declared with
/// <see cref="DonkehAppAttribute"/> in the application assembly.
/// </summary>
internal static class DonkehAppLocator
{
    /// <summary>Locates and creates the application instance.</summary>
    /// <returns>The application singleton for this process.</returns>
    /// <exception cref="InvalidOperationException">
    /// No assembly in the process carries <see cref="DonkehAppAttribute"/>.
    /// </exception>
    public static IDonkehApp Find()
    {
        var fromEntry = Read(Assembly.GetEntryAssembly());
        if (fromEntry is not null)
        {
            return fromEntry;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var app = Read(assembly);
            if (app is not null)
            {
                return app;
            }
        }

        throw new InvalidOperationException(
            "No Donkeh application was found. Add " +
            "[assembly: DonkehApp(typeof(MyApp))] to the application assembly.");
    }

    /// <summary>Reads and instantiates the attribute from one assembly.</summary>
    /// <param name="assembly">The assembly to inspect, or <see langword="null"/>.</param>
    /// <returns>The app instance, or <see langword="null"/> when the assembly has no attribute.</returns>
    private static IDonkehApp? Read(Assembly? assembly)
    {
        var attribute = assembly?.GetCustomAttribute<DonkehAppAttribute>();
        if (attribute is null)
        {
            return null;
        }

        return Activator.CreateInstance(attribute.AppType) as IDonkehApp
            ?? throw new InvalidOperationException(
                $"'{attribute.AppType.FullName}' must have a public parameterless constructor.");
    }
}
