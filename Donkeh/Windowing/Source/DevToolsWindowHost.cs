using Donkeh.Inspector;
using Donkeh.UILab;

namespace Donkeh.Windowing;

/// <summary>
/// Hosts the inspector in its own native window on a background thread. The
/// panel still reads the real mounted DOM through <see cref="DevToolsState"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IgniteWindow"/> creates one host lazily when the inspector is
/// visible and docked to <see cref="DevToolsDock.Window"/>, then calls
/// <see cref="Open"/>/<see cref="Close"/> as that state changes. Each open
/// launches a dedicated background STA thread that owns a second
/// <see cref="IgniteWindow"/> and exits when the window closes; the thread is
/// never joined and this type has no disposal step.
/// </para>
/// <para>
/// The inspector panel is handed the same <see cref="DevToolsState"/> instance,
/// so it sees the live DOM and queues mutations for the main frame thread
/// instead of touching another thread's tree directly. Only the volatile open
/// and closing flags are shared; <see cref="Open"/>, <see cref="Close"/> and
/// <see cref="IsOpen"/> are expected to be called from the main frame thread and
/// are not safe for arbitrary concurrent use.
/// </para>
/// </remarks>
/// <seealso cref="DevToolsState"/>
/// <seealso cref="IgniteWindow"/>
internal sealed class DevToolsWindowHost
{
    /// <summary>The native-window thread, or <see langword="null"/> before opening.</summary>
    private Thread? _thread;

    /// <summary>Set when the host wants the window to shut down.</summary>
    private volatile bool _closing;

    /// <summary>Whether a devtools window is considered open or starting.</summary>
    private volatile bool _open;

    /// <summary>
    /// Gets a value indicating whether a devtools window is open or starting.
    /// </summary>
    /// <remarks>
    /// The flag is set by <see cref="Open"/> before the native window exists and
    /// cleared by <see cref="Close"/>, by a user-initiated window close, or when
    /// the window thread fails. It is therefore a host-level intent flag rather
    /// than proof that a window is currently visible.
    /// </remarks>
    public bool IsOpen => _open;

    /// <summary>
    /// Starts the devtools window on a new background STA thread.
    /// </summary>
    /// <param name="state">
    /// The live inspector state shared with the main window. It must outlive the
    /// devtools window; no copy is made.
    /// </param>
    /// <remarks>
    /// Calling this while already open is a no-op. The thread is a background
    /// thread named <c>Ignite DevTools</c>, so it cannot keep the process alive.
    /// Startup failures are logged to standard error by the thread body and reset
    /// <see cref="IsOpen"/>; this method itself only validates the open flag and
    /// does not wait for the window.
    /// </remarks>
    public void Open(DevToolsState state)
    {
        if (_open)
        {
            return;
        }

        _open = true;
        _closing = false;

        _thread = new Thread(() => RunWindow(state))
        {
            IsBackground = true,
            Name = "Ignite DevTools"
        };

        // STA is a Windows requirement for the native window's message pump;
        // other platforms have no equivalent and reject the call.
        if (OperatingSystem.IsWindows())
        {
            _thread.SetApartmentState(ApartmentState.STA);
        }

        _thread.Start();
    }

    /// <summary>
    /// Requests that the devtools window close.
    /// </summary>
    /// <remarks>
    /// This only raises a flag and returns. The window polls it in its frame
    /// callback and closes the native window on the next frame, so
    /// <see cref="IsOpen"/> is already <see langword="false"/> while the window may
    /// still be visible for a moment. Calling it when nothing is open is a no-op.
    /// The method does not move the inspector back into the main window; that
    /// restoration is performed when the user closes the window themselves.
    /// </remarks>
    public void Close()
    {
        if (!_open)
        {
            return;
        }

        _closing = true;
        _open = false;
    }

    /// <summary>
    /// Runs the devtools window's message loop until it closes.
    /// </summary>
    /// <param name="state">The shared inspector state passed to the panel.</param>
    /// <remarks>
    /// <para>
    /// The window is 1000 by 700 with VSync, 4x MSAA, a resizable border and its
    /// own inspector disabled to avoid recursive devtools windows. Each frame the
    /// panel is ticked and any pending text-input focus is applied.
    /// </para>
    /// <para>
    /// A user-initiated close restores <see cref="DevToolsState.Dock"/> to
    /// <see cref="DevToolsDock.Bottom"/>, makes the panel visible and refreshes it,
    /// which moves the inspector back into the main window. Exceptions are written
    /// to standard error and never escape the thread; either way the open flag is
    /// cleared in <c>finally</c>.
    /// </para>
    /// </remarks>
    private void RunWindow(DevToolsState state)
    {
        try
        {
            var settings = new WindowSettings
            {
                Width = 1000,
                Height = 700,
                Title = "Ignite DevTools",
                VSync = true,
                Samples = 4,
                Resizable = true,
                EnableDevTools = false
            };

            var window = new IgniteWindow(settings)
            {
                SharedDevToolsState = state
            };

            window.BeforeFrame += (host, _) =>
            {
                if (_closing)
                {
                    host.SilkWindow?.Close();
                    return;
                }

                host.UI.FindComponent<InspectorPanel>()?.Tick();
                host.FocusPendingDevToolsInput();
            };

            window.Closed += _ =>
            {
                _open = false;

                if (!_closing)
                {
                    state.Dock = DevToolsDock.Bottom;
                    state.Visible = true;
                    state.Refresh();
                }
            };

            var props = new InspectorPanelProps { State = state, Standalone = true };
            window.Run(
                _.C<InspectorPanel, InspectorPanelProps>(props),
                InspectorStyles.Css + InspectorStyles.HeightClasses);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("[devtools-window] " + exception);
            _open = false;
        }
        finally
        {
            _open = false;
        }
    }
}
