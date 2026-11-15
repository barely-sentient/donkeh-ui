using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Android.App;
using Android.Content.PM;
using Donkeh.Hosting;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl.Android;

namespace Donkeh.Windowing.Android;

/// <summary>
/// Framework-owned Android activity. It creates the OpenGL ES view, applies
/// mobile defaults and runs the application's <see cref="IDonkehApp"/>.
/// </summary>
/// <remarks>
/// <para>
/// The activity is the launcher for the application; the app assembly only
/// declares <c>[assembly: DonkehApp(typeof(...))]</c>. The view is OpenGL ES 3.0,
/// vsync-limited to 60 Hz and full screen. Window chrome, devtools, MSAA and
/// drag-resize settings do not apply.
/// </para>
/// <para>
/// Rotation is handled by the activity's configuration-change flags, so the same
/// activity instance keeps running and the CSS layout reflows on resize.
/// </para>
/// <para>
/// The SDL callbacks are forwarded explicitly. <see cref="SilkActivity"/> is
/// abstract, so .NET Android does not emit a static JNI registration block for
/// it, and the delegate getters that register <c>n_loadLibraries</c> and friends
/// are private on the binding base. The three handlers below reuse those
/// delegate getters for this concrete activity.
/// </para>
/// </remarks>
[Activity(
    Label = "Donkeh",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen",
    ConfigurationChanges = SilkActivity.ConfigChangesFlags
        | ConfigChanges.UiMode
        | ConfigChanges.Density
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize)]
public class DonkehActivity : SilkActivity
{
    /// <summary>Android permission provider installed for this activity.</summary>
    private AndroidPermissionService? _permissions;

    /// <inheritdoc/>
    public override void LoadLibraries() => base.LoadLibraries();

    /// <inheritdoc/>
    public override void SetOrientationBis(int w, int h, bool resizable, string hint) =>
        base.SetOrientationBis(w, h, resizable, hint);

    /// <inheritdoc/>
    protected override void OnDestroy()
    {
        _permissions?.CancelAll();
        base.OnDestroy();
    }

    /// <summary>Returns the JNI delegate that dispatches <c>loadLibraries</c>.</summary>
    /// <returns>The binding base's cached delegate.</returns>
    private static Delegate GetLoadLibrariesHandler() =>
        GetBaseHandler(typeof(Org.Libsdl.App.SDLActivity), "GetLoadLibrariesHandler");

    /// <summary>Returns the JNI delegate that dispatches <c>setOrientationBis</c>.</summary>
    /// <returns>The binding base's cached delegate.</returns>
    private static Delegate GetSetOrientationBis_IIZLjava_lang_String_Handler() =>
        GetBaseHandler(
            typeof(Org.Libsdl.App.SDLActivity),
            "GetSetOrientationBis_IIZLjava_lang_String_Handler");

    /// <summary>Returns the JNI delegate that dispatches <c>onDestroy</c>.</summary>
    /// <returns>The binding base's cached delegate.</returns>
    private static Delegate GetOnDestroyHandler() =>
        GetBaseHandler(typeof(Activity), "GetOnDestroyHandler");

    /// <summary>Reads a private static handler getter from a binding base.</summary>
    /// <param name="type">The binding type that declares the handler.</param>
    /// <param name="name">The handler method name.</param>
    /// <returns>The delegate returned by the binding.</returns>
    /// <exception cref="MissingMethodException">
    /// The binding no longer exposes the expected handler.
    /// </exception>
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods,
        typeof(Org.Libsdl.App.SDLActivity))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods,
        typeof(Activity))]
    private static Delegate GetBaseHandler(Type type, string name)
    {
        var method = type.GetMethod(
            name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        if (method is null)
        {
            throw new MissingMethodException(type.FullName, name);
        }

        return (Delegate)method.Invoke(null, null)!;
    }

    /// <inheritdoc/>
    protected override void OnRun()
    {
        var app = DonkehAppLocator.Find();
        var density = Resources?.DisplayMetrics?.Density ?? 1f;

        var permissions = new AndroidPermissionService(this);
        _permissions = permissions;
        Host.UsePermissions(permissions);

        var options = ViewOptions.Default with
        {
            API = new GraphicsAPI(
                ContextAPI.OpenGLES,
                ContextProfile.Compatability,
                ContextFlags.Default,
                new APIVersion(3, 0)),
            PreferredDepthBufferBits = 24,
            PreferredStencilBufferBits = 8,
            Samples = 0,
            VSync = true,
            FramesPerSecond = 60
        };

        var view = Silk.NET.Windowing.Window.GetView(options);

        var settings = app.Settings with
        {
            // The activity owns the surface; size and chrome are ignored.
            Width = 0,
            Height = 0,
            VSync = true,
            TargetFramesPerSecond = 60,
            Samples = 0,
            Resizable = false,
            Frameless = false,
            EnableDevTools = false,
            DpiScale = density
        };

        var window = new DonkehWindow(settings);

        window.Loaded += w =>
        {
            var size = w.SilkView;

            // Some SDL builds expose a logical window and a larger drawable. In
            // that case the view already carries the scale and applying density
            // again would shrink the UI.
            if (size is not null && size.Size.X > 0 && size.FramebufferSize.X > size.Size.X * 1.01f)
            {
                w.DpiScale = 0;
            }

            Console.WriteLine(
                $"[Donkeh] density={density} dpi={w.DpiScale} size={size?.Size} " +
                $"framebuffer={size?.FramebufferSize} gl={w.Gl?.GetStringS(StringName.Version)}");
        };

        window.Run(view, app.BuildRoot(), app.Styles());
    }

    /// <inheritdoc/>
    public override void OnRequestPermissionsResult(
        int requestCode,
        string[] permissions,
        Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        _permissions?.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }
}
