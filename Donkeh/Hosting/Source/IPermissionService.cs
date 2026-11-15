namespace Donkeh.Hosting;

/// <summary>Requests device permissions from the active platform host.</summary>
/// <remarks>
/// Implementations must never throw for an unknown or unsupported permission;
/// they return <see langword="false"/> instead. Implementations may cache grants,
/// but <see cref="Host.UsePermissions"/> resets the public cache when the provider
/// changes.
/// </remarks>
public interface IPermissionService
{
    /// <summary>Requests one permission from the platform.</summary>
    /// <param name="permission">The capability being requested.</param>
    /// <param name="cancellationToken">Cancels a pending prompt when the platform supports it.</param>
    /// <returns>
    /// <see langword="true"/> when the permission is granted; otherwise
    /// <see langword="false"/>.
    /// </returns>
    Task<bool> RequestAsync(HostPermission permission, CancellationToken cancellationToken = default);
}
