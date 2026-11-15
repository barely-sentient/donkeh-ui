using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Donkeh.Hosting;

namespace Donkeh.Windowing.Android;

/// <summary>Android runtime-permission provider used by <see cref="Host.Permissions"/>.</summary>
/// <remarks>
/// The provider requests permissions on the activity's UI thread and completes
/// the caller's task from <c>OnRequestPermissionsResult</c>. Capabilities that do
/// not require a runtime permission, such as clipboard access, resolve
/// immediately. The app must still declare the matching <c>uses-permission</c>
/// entries in its Android manifest.
/// </remarks>
internal sealed class AndroidPermissionService : IPermissionService
{
    private readonly DonkehActivity _activity;
    private readonly object _gate = new();
    private readonly Dictionary<int, PendingRequest> _pending = new();
    private int _nextRequestCode = 0x4A00;

    /// <summary>Creates a provider bound to the running activity.</summary>
    /// <param name="activity">The framework activity that receives results.</param>
    public AndroidPermissionService(DonkehActivity activity)
    {
        _activity = activity;
    }

    /// <inheritdoc/>
    public Task<bool> RequestAsync(
        HostPermission permission,
        CancellationToken cancellationToken = default)
    {
        if (permission == HostPermission.Clipboard)
        {
            return Task.FromResult(true);
        }

        if (permission == HostPermission.Notifications
            && Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
        {
            return Task.FromResult(true);
        }

        var required = GetPermissions(permission);

        if (required.Length == 0)
        {
            return Task.FromResult(false);
        }

        var alreadyGranted = true;

        foreach (var name in required)
        {
            if (_activity.CheckSelfPermission(name) != Permission.Granted)
            {
                alreadyGranted = false;
                break;
            }
        }

        if (alreadyGranted)
        {
            return Task.FromResult(true);
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCode = Interlocked.Increment(ref _nextRequestCode);

        lock (_gate)
        {
            _pending[requestCode] = new PendingRequest(required, completion);
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                lock (_gate)
                {
                    _pending.Remove(requestCode);
                }

                completion.TrySetResult(false);
            });
        }

        try
        {
            _activity.RunOnUiThread(() => _activity.RequestPermissions(required, requestCode));
        }
        catch
        {
            lock (_gate)
            {
                _pending.Remove(requestCode);
            }

            completion.TrySetResult(false);
        }

        return completion.Task;
    }

    /// <summary>Completes a pending request from the activity result callback.</summary>
    /// <param name="requestCode">Request code passed to the activity.</param>
    /// <param name="permissions">Permissions reported by the platform.</param>
    /// <param name="grantResults">Grant results parallel to <paramref name="permissions"/>.</param>
    public void OnRequestPermissionsResult(
        int requestCode,
        string[] permissions,
        Permission[] grantResults)
    {
        PendingRequest? request;

        lock (_gate)
        {
            if (!_pending.Remove(requestCode, out request))
            {
                return;
            }
        }

        var granted = false;

        for (var i = 0; i < permissions.Length && i < grantResults.Length; i++)
        {
            if (grantResults[i] == Permission.Granted
                && Array.IndexOf(request.RequestedPermissions, permissions[i]) >= 0)
            {
                granted = true;
                break;
            }
        }

        request.Completion.TrySetResult(granted);
    }

    /// <summary>Completes every pending request as denied when the activity closes.</summary>
    public void CancelAll()
    {
        lock (_gate)
        {
            foreach (var request in _pending.Values)
            {
                request.Completion.TrySetResult(false);
            }

            _pending.Clear();
        }
    }

    /// <summary>Maps a Donkeh permission to Android manifest permissions.</summary>
    private static string[] GetPermissions(HostPermission permission)
    {
        return permission switch
        {
            HostPermission.Camera => [Manifest.Permission.Camera],
            HostPermission.Microphone => [Manifest.Permission.RecordAudio],
            HostPermission.Location =>
            [
                Manifest.Permission.AccessFineLocation,
                Manifest.Permission.AccessCoarseLocation
            ],
            HostPermission.Notifications => OperatingSystem.IsAndroidVersionAtLeast(33)
                ? [Manifest.Permission.PostNotifications]
                : [],
            HostPermission.Photos => OperatingSystem.IsAndroidVersionAtLeast(33)
                ? [Manifest.Permission.ReadMediaImages]
                : [Manifest.Permission.ReadExternalStorage],
            HostPermission.Storage => OperatingSystem.IsAndroidVersionAtLeast(33)
                ? [Manifest.Permission.ReadMediaImages]
                :
                [
                    Manifest.Permission.ReadExternalStorage,
                    Manifest.Permission.WriteExternalStorage
                ],
            HostPermission.Contacts => [Manifest.Permission.ReadContacts],
            HostPermission.Calendar => [Manifest.Permission.ReadCalendar],
            HostPermission.Bluetooth => OperatingSystem.IsAndroidVersionAtLeast(31)
                ? [Manifest.Permission.BluetoothConnect]
                : [Manifest.Permission.Bluetooth],
            HostPermission.Motion => OperatingSystem.IsAndroidVersionAtLeast(29)
                ? [Manifest.Permission.ActivityRecognition]
                : [],
            _ => []
        };
    }

    /// <summary>One permission request waiting for the activity result.</summary>
    private sealed class PendingRequest
    {
        /// <summary>Creates a pending request.</summary>
        public PendingRequest(string[] requestedPermissions, TaskCompletionSource<bool> completion)
        {
            RequestedPermissions = requestedPermissions;
            Completion = completion;
        }

        /// <summary>Gets the permissions that were requested.</summary>
        public string[] RequestedPermissions { get; }

        /// <summary>Gets the task completed by the activity callback.</summary>
        public TaskCompletionSource<bool> Completion { get; }
    }
}
