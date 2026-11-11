using Ignite.Inspector;
using Ignite.UILab;

namespace Ignite.Windowing;

/// <summary>
/// Hosts the inspector in its own native window on a background thread. The
/// panel still reads the real mounted DOM through <see cref="DevToolsState"/>.
/// </summary>
internal sealed class DevToolsWindowHost
{
    private Thread? _thread;
    private volatile bool _closing;
    private volatile bool _open;

    public bool IsOpen => _open;

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

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Close()
    {
        if (!_open)
        {
            return;
        }

        _closing = true;
        _open = false;
    }

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
