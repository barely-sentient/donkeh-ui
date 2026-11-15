using Donkeh.Hosting;
using Microsoft.JSInterop;

namespace Donkeh.Windowing.Browser;

/// <summary>Browser permission provider backed by the embedded DOM bridge.</summary>
/// <remarks>
/// Requests that browsers can only satisfy from a user gesture still require the
/// caller to invoke the permission method from an event handler. Unsupported
/// capabilities return <see langword="false"/>.
/// </remarks>
internal sealed class BrowserPermissionService : IPermissionService
{
    private readonly IJSObjectReference _module;

    /// <summary>Creates a provider for the loaded bridge module.</summary>
    /// <param name="module">The imported DOM bridge.</param>
    public BrowserPermissionService(IJSObjectReference module)
    {
        _module = module;
    }

    /// <inheritdoc/>
    public async Task<bool> RequestAsync(
        HostPermission permission,
        CancellationToken cancellationToken = default)
    {
        var name = permission switch
        {
            HostPermission.Camera => "camera",
            HostPermission.Microphone => "microphone",
            HostPermission.Location => "location",
            HostPermission.Notifications => "notifications",
            HostPermission.Storage => "storage",
            HostPermission.Motion => "motion",
            HostPermission.Clipboard => "clipboard",
            _ => null
        };

        if (name is null)
        {
            return false;
        }

        try
        {
            return await _module
                .InvokeAsync<bool>("requestPermission", cancellationToken, name)
                .ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }
}
