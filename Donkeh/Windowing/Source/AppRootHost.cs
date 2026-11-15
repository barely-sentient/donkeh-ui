using Donkeh.Hosting;

namespace Donkeh.Windowing;

/// <summary>
/// Desktop entry-point helper generated into the application assembly by the
/// framework build targets.
/// </summary>
/// <remarks>
/// It discovers the <see cref="AppRootAttribute"/>, creates the root component
/// and runs the standard desktop window. Applications never write their own
/// desktop <c>Main</c> or implement <c>IDonkehApp</c>.
/// </remarks>
public static class AppRootHost
{
    /// <summary>Runs the discovered desktop application.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <remarks>
    /// <c>--screenshot &lt;path&gt;</c> is handled by the host: it captures frame
    /// 120 and closes the window, which keeps smoke tests processing-free.
    /// </remarks>
    public static void RunDesktop(string[] args)
    {
        var app = AppRootLocator.Find();
        var window = new DonkehWindow(app.Settings);

        var screenshotIndex = Array.IndexOf(args, "--screenshot");

        if (screenshotIndex >= 0 && screenshotIndex + 1 < args.Length)
        {
            var screenshotPath = args[screenshotIndex + 1];
            window.BeforeFrame += (w, _) =>
            {
                if (w.FrameCount == 120)
                {
                    w.RequestScreenshot(screenshotPath);
                }
            };
        }

        window.Run(app.CreateRoot(), app.Styles);
    }
}
