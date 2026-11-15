using System.Collections.Concurrent;

namespace Donkeh.Hosting;

/// <summary>
/// Awaitable permission checks exposed through <see cref="Host.Permissions"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each method requests its capability from the platform once. A granted result is
/// cached for the lifetime of the host, while a denied result is not, so an app
/// can retry after the user changes a setting.
/// </para>
/// <para>
/// Concurrent calls for the same permission share one in-flight request.
/// </para>
/// </remarks>
public sealed class HostPermissions
{
    private readonly ConcurrentDictionary<HostPermission, bool> _granted = new();
    private readonly ConcurrentDictionary<HostPermission, Lazy<Task<bool>>> _inflight = new();

    /// <summary>Requests camera access.</summary>
    /// <param name="cancellationToken">Cancels a pending prompt when supported.</param>
    /// <returns><see langword="true"/> when granted.</returns>
    public Task<bool> Camera(CancellationToken cancellationToken = default) =>
        Request(HostPermission.Camera, cancellationToken);

    /// <summary>Requests microphone access.</summary>
    /// <param name="cancellationToken">Cancels a pending prompt when supported.</param>
    /// <returns><see langword="true"/> when granted.</returns>
    public Task<bool> Microphone(CancellationToken cancellationToken = default) =>
        Request(HostPermission.Microphone, cancellationToken);

    /// <summary>Requests location access.</summary>
    /// <param name="cancellationToken">Cancels a pending prompt when supported.</param>
    /// <returns><see langword="true"/> when granted.</returns>
    public Task<bool> Location(CancellationToken cancellationToken = default) =>
        Request(HostPermission.Location, cancellationToken);

    /// <summary>Requests permission to show notifications.</summary>
    /// <param name="cancellationToken">Cancels a pending prompt when supported.</param>
    /// <returns><see langword="true"/> when granted.</returns>
    public Task<bool> Notifications(CancellationToken cancellationToken = default) =>
        Request(HostPermission.Notifications, cancellationToken);

    /// <summary>Requests access to the photo library.</summary>
    /// <param name="cancellationToken">Cancels a pending prompt when supported.</param>
    /// <returns><see langword="true"/> when granted.</returns>
    public Task<bool> Photos(CancellationToken cancellationToken = default) =>
        Request(HostPermission.Photos, cancellationToken);

    /// <summary>Requests file storage access.</summary>
    /// <param name="cancellationToken">Cancels a pending prompt when supported.</param>
    /// <returns><see langword="true"/> when granted.</returns>
    public Task<bool> Storage(CancellationToken cancellationToken = default) =>
        Request(HostPermission.Storage, cancellationToken);

    /// <summary>Requests contacts access.</summary>
    /// <param name="cancellationToken">Cancels a pending prompt when supported.</param>
    /// <returns><see langword="true"/> when granted.</returns>
    public Task<bool> Contacts(CancellationToken cancellationToken = default) =>
        Request(HostPermission.Contacts, cancellationToken);

    /// <summary>Requests calendar access.</summary>
    /// <param name="cancellationToken">Cancels a pending prompt when supported.</param>
    /// <returns><see langword="true"/> when granted.</returns>
    public Task<bool> Calendar(CancellationToken cancellationToken = default) =>
        Request(HostPermission.Calendar, cancellationToken);

    /// <summary>Requests Bluetooth access.</summary>
    /// <param name="cancellationToken">Cancels a pending prompt when supported.</param>
    /// <returns><see langword="true"/> when granted.</returns>
    public Task<bool> Bluetooth(CancellationToken cancellationToken = default) =>
        Request(HostPermission.Bluetooth, cancellationToken);

    /// <summary>Requests motion or activity access.</summary>
    /// <param name="cancellationToken">Cancels a pending prompt when supported.</param>
    /// <returns><see langword="true"/> when granted.</returns>
    public Task<bool> Motion(CancellationToken cancellationToken = default) =>
        Request(HostPermission.Motion, cancellationToken);

    /// <summary>Requests clipboard access.</summary>
    /// <param name="cancellationToken">Cancels a pending prompt when supported.</param>
    /// <returns><see langword="true"/> when granted.</returns>
    public Task<bool> Clipboard(CancellationToken cancellationToken = default) =>
        Request(HostPermission.Clipboard, cancellationToken);

    /// <summary>Requests an arbitrary known permission.</summary>
    /// <param name="permission">The capability being requested.</param>
    /// <param name="cancellationToken">Cancels a pending prompt when supported.</param>
    /// <returns><see langword="true"/> when granted.</returns>
    public Task<bool> Request(HostPermission permission, CancellationToken cancellationToken = default)
    {
        if (_granted.ContainsKey(permission))
        {
            return Task.FromResult(true);
        }

        var lazy = _inflight.GetOrAdd(
            permission,
            static (key, state) => new Lazy<Task<bool>>(
                () => state.Owner.RequestCore(key, state.Token)),
            (Owner: this, Token: cancellationToken));

        return AwaitAndClearAsync(permission, lazy.Value);
    }

    /// <summary>Awaits the shared request and drops it from the in-flight map.</summary>
    private async Task<bool> AwaitAndClearAsync(HostPermission permission, Task<bool> request)
    {
        try
        {
            return await request.ConfigureAwait(false);
        }
        finally
        {
            _inflight.TryRemove(permission, out _);
        }
    }

    /// <summary>Runs one request and updates the caches.</summary>
    private async Task<bool> RequestCore(HostPermission permission, CancellationToken cancellationToken)
    {
        try
        {
            var granted = await Host.PermissionService
                .RequestAsync(permission, cancellationToken)
                .ConfigureAwait(false);

            if (granted)
            {
                _granted[permission] = true;
            }

            return granted;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Clears cached grants after the provider changes.</summary>
    internal void ClearCache()
    {
        _granted.Clear();
        _inflight.Clear();
    }
}
