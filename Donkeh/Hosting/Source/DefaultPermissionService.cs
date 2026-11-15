namespace Donkeh.Hosting;

/// <summary>
/// Permission behaviour used before a platform host installs its own provider.
/// </summary>
/// <remarks>
/// Desktop operating systems do not expose a common runtime permission prompt
/// through .NET, so the default grants the request and lets the device API itself
/// fail if the user has blocked access. Mobile, browser and unknown platforms
/// return <see langword="false"/> until their host registers a real provider.
/// </remarks>
internal sealed class DefaultPermissionService : IPermissionService
{
    /// <summary>Shared stateless instance.</summary>
    public static readonly DefaultPermissionService Instance = new();

    /// <inheritdoc/>
    public Task<bool> RequestAsync(
        HostPermission permission,
        CancellationToken cancellationToken = default)
    {
        var desktop = OperatingSystem.IsWindows()
            || OperatingSystem.IsMacOS()
            || OperatingSystem.IsLinux();

        return Task.FromResult(desktop && !OperatingSystem.IsBrowser());
    }
}
