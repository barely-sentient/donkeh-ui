using System.Reflection;

namespace Donkeh.Windowing;

/// <summary>
/// Attaches optional development packages that are present only in Debug builds.
/// </summary>
/// <remarks>
/// <para>
/// The framework must not depend on development tooling at compile time: a Release
/// publish does not restore those packages and must not copy them. The host
/// therefore looks them up by assembly-qualified name at start-up. A missing type
/// is the normal Release case and is ignored.
/// </para>
/// <para>
/// Load failures and attach failures are swallowed so optional tooling can never
/// prevent an application from running.
/// </para>
/// </remarks>
internal static class DevelopmentFeatures
{
    private const string HotReloadType = "Donkeh.HotReload.HotReloadHost, Donkeh.HotReload";

    /// <summary>Attaches CSS hot reload to a window when the development package is present.</summary>
    /// <param name="window">The window that is about to run.</param>
    public static void Attach(DonkehWindow window)
    {
        try
        {
            var type = Type.GetType(HotReloadType, throwOnError: false);

            var attach = type?.GetMethod(
                "Attach",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(DonkehWindow)],
                modifiers: null);

            attach?.Invoke(null, [window]);
        }
        catch
        {
            // Optional development tooling must never break start-up.
        }
    }
}
