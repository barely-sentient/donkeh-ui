using System.Collections.Concurrent;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Donkeh.Windowing")]

namespace Donkeh.Hosting;

/// <summary>
/// Platform capabilities, permission requests and frame-loop access available to
/// framework consumers.
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
/// <para>
/// A component's hooks and state setters are bound to the thread that owns the
/// renderer. Work that finishes on another thread must be marshalled back with
/// <see cref="Post"/>, which queues the callback for the next frame. Use
/// <see cref="FrameTick"/> for animation or playback clocks; it runs on the frame
/// thread immediately before the renderer flushes.
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
///
/// Host.Post(() => Console.WriteLine("Runs on the UI thread."));
/// </code>
/// </example>
public static class Host
{
    private static readonly HostPermissions SharedPermissions = new();
    private static readonly ConcurrentQueue<Action> PostedActions = new();
    private static volatile IPermissionService _permissionService = DefaultPermissionService.Instance;
    private static Action<double>? _frameTick;

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

    /// <summary>
    /// Raised once per host frame on the thread that owns the renderer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The argument is the elapsed time since the previous frame, in seconds. The
    /// event runs before the renderer flushes queued updates, so a handler may
    /// change state and have it applied in the same frame.
    /// </para>
    /// <para>
    /// Subscribe and unsubscribe from an effect. Keeping a handler attached for
    /// the lifetime of a process that renders continuously prevents the component
    /// that owns it from being collected.
    /// </para>
    /// </remarks>
    public static event Action<double>? FrameTick
    {
        add => _frameTick += value;
        remove => _frameTick -= value;
    }

    /// <summary>
    /// Queues a callback to run on the frame thread before the next update.
    /// </summary>
    /// <param name="action">The callback. Null is ignored.</param>
    /// <remarks>
    /// Use this to bring work that completed on a worker thread, such as file
    /// I/O or a child process, back into the UI. Callbacks run in the order they
    /// were posted and may safely create or update hook state. An exception thrown
    /// by a callback is swallowed so one failed completion cannot stop the frame.
    /// </remarks>
    public static void Post(Action? action)
    {
        if (action is not null)
        {
            PostedActions.Enqueue(action);
        }
    }

    /// <summary>Runs every callback queued through <see cref="Post"/>.</summary>
    internal static void PumpPostedActions()
    {
        while (PostedActions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[Donkeh] Posted callback failed: {exception.Message}");
            }
        }
    }

    /// <summary>Raises <see cref="FrameTick"/> for the host frame loop.</summary>
    /// <param name="delta">Seconds since the previous frame.</param>
    internal static void RaiseFrameTick(double delta)
    {
        var handlers = _frameTick;

        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<double>)handler)(delta);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[Donkeh] Frame tick failed: {exception.Message}");
            }
        }
    }
}
