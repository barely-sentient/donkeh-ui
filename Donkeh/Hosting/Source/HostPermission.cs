namespace Donkeh.Hosting;

/// <summary>Identifies a device capability that may require permission.</summary>
public enum HostPermission
{
    /// <summary>Capture images or video from a camera.</summary>
    Camera,

    /// <summary>Capture audio from a microphone.</summary>
    Microphone,

    /// <summary>Read the device's current location.</summary>
    Location,

    /// <summary>Show system notifications.</summary>
    Notifications,

    /// <summary>Read photos or other images from the user's library.</summary>
    Photos,

    /// <summary>Read and write files in app-accessible storage.</summary>
    Storage,

    /// <summary>Read contacts.</summary>
    Contacts,

    /// <summary>Read the calendar.</summary>
    Calendar,

    /// <summary>Connect to nearby Bluetooth devices.</summary>
    Bluetooth,

    /// <summary>Receive motion or activity data.</summary>
    Motion,

    /// <summary>Read from or write to the system clipboard.</summary>
    Clipboard
}
