namespace Donkeh.Hosting;

/// <summary>
/// Platform capabilities and permission requests available to framework
/// consumers.
/// </summary>
/// <remarks>
/// <para>
/// The platform flags are derived from the runtime and are safe to read on every
/// target. Permission calls are asynchronous because mobile and browser hosts may
/// need to show a system prompt.
/// </para>
/// <para>
/// Hosts replace the permission provider at startup. Desktop defaults to granted;
/// Android and browser hosts install providers that use the platform's real
/// permission flow. A missing or unsupported permission resolves to
/// <see langword="false"/> rather than throwing.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using Donkeh.Hosting;
///
/// if (Host.IsAndroid)
/// {
///     // Android-only setup.
/// }
///
/// if (await Host.Permissions.Camera())
/// {
///     // Use the camera.
/// }
/// </code>
/// </example>
public static class Host
{
    private static readonly HostPermissions SharedPermissions = new();
    private static volatile IPermissionService _permissionService = DefaultPermissionService.Instance;

    /// <summary>Gets whether the app is running on Android.</summary>
    public static bool IsAndroid => OperatingSystem.IsAndroid();

    /// <summary>Gets whether the app is running on iOS.</summary>
    public static bool IsIOS => OperatingSystem.IsIOS();

    /// <summary>Gets whether the app is running on Windows.</summary>
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Gets whether the app is running on macOS.</summary>
    public static bool IsMac => OperatingSystem.IsMacOS();

    /// <summary>Gets whether the app is running on Linux.</summary>
    public static bool IsLinux => OperatingSystem.IsLinux();

    /// <summary>Gets whether the app is running in a browser.</summary>
    public static bool IsWeb => OperatingSystem.IsBrowser();

    /// <summary>Gets whether the app is running on a desktop operating system.</summary>
    public static bool IsDesktop => !IsWeb && (IsWindows || IsMac || IsLinux);

    /// <summary>Gets whether the app is running on a mobile operating system.</summary>
    public static bool IsMobile => IsAndroid || IsIOS;

    /// <summary>Alias for <see cref="IsWeb"/>.</summary>
    public static bool IsBrowser => IsWeb;

    /// <summary>Gets a friendly name for the current platform.</summary>
    public static string PlatformName => IsAndroid
        ? "Android"
        : IsIOS
            ? "iOS"
            : IsWeb
                ? "Web"
                : IsWindows
                    ? "Windows"
                    : IsMac
                        ? "macOS"
                        : IsLinux
                            ? "Linux"
                            : "Unknown";

    /// <summary>Gets the awaitable permission API.</summary>
    public static HostPermissions Permissions => SharedPermissions;

    /// <summary>
    /// Replaces the permission provider used by <see cref="Permissions"/>.
    /// </summary>
    /// <param name="service">The platform permission service, or null to restore the desktop default.</param>
    /// <remarks>
    /// Hosts call this during startup. Replacing the provider clears cached
    /// grants so a new platform session starts from a clean state.
    /// </remarks>
    public static void UsePermissions(IPermissionService? service)
    {
        _permissionService = service ?? DefaultPermissionService.Instance;
        SharedPermissions.ClearCache();
    }

    /// <summary>Gets the current permission provider.</summary>
    internal static IPermissionService PermissionService => _permissionService;
}
