using Donkeh.Hosting;

namespace Donkeh.UILab.Tests;

public sealed class HostPermissionsTests
{
    [Fact]
    public async Task GrantedPermissionIsCached()
    {
        var service = new FakePermissionService { Result = true };
        Host.UsePermissions(service);

        try
        {
            Assert.True(await Host.Permissions.Camera());
            Assert.True(await Host.Permissions.Camera());
            Assert.Equal(1, service.Calls);
        }
        finally
        {
            Host.UsePermissions(null);
        }
    }

    [Fact]
    public async Task DeniedPermissionCanBeRetried()
    {
        var service = new FakePermissionService { Result = false };
        Host.UsePermissions(service);

        try
        {
            Assert.False(await Host.Permissions.Microphone());
            service.Result = true;
            Assert.True(await Host.Permissions.Microphone());
            Assert.Equal(2, service.Calls);
        }
        finally
        {
            Host.UsePermissions(null);
        }
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneCall()
    {
        var service = new FakePermissionService
        {
            Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        Host.UsePermissions(service);

        try
        {
            var first = Host.Permissions.Location();
            var second = Host.Permissions.Location();

            Assert.Equal(1, service.Calls);
            service.Gate!.SetResult(true);

            Assert.True(await first);
            Assert.True(await second);
            Assert.Equal(1, service.Calls);
        }
        finally
        {
            Host.UsePermissions(null);
        }
    }

    [Fact]
    public void PlatformFlagsMatchTheRuntime()
    {
        Assert.Equal(OperatingSystem.IsBrowser(), Host.IsWeb);
        Assert.Equal(OperatingSystem.IsAndroid(), Host.IsAndroid);
        Assert.Equal(OperatingSystem.IsIOS(), Host.IsIOS);
        Assert.Equal(OperatingSystem.IsWindows(), Host.IsWindows);
        Assert.Equal(OperatingSystem.IsMacOS(), Host.IsMac);
        Assert.Equal(OperatingSystem.IsLinux(), Host.IsLinux);
        Assert.Equal(Host.IsAndroid || Host.IsIOS, Host.IsMobile);
    }

    private sealed class FakePermissionService : IPermissionService
    {
        public bool Result { get; set; }

        public int Calls { get; private set; }

        public TaskCompletionSource<bool>? Gate { get; set; }

        public Task<bool> RequestAsync(
            HostPermission permission,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Gate is null ? Task.FromResult(Result) : Gate.Task;
        }
    }
}
