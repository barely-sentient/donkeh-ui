using Donkeh.UILab;

namespace Donkeh.Windowing;

/// <summary>
/// Describes an application that a Donkeh host can start on any platform.
/// </summary>
/// <remarks>
/// <para>
/// A host creates the platform surface, applies mobile-specific defaults and then
/// calls <see cref="BuildRoot"/> and <see cref="Styles"/>. Desktop hosts call it
/// from the application's entry point; the Android host discovers it through
/// <see cref="DonkehAppAttribute"/> and calls it from its activity.
/// </para>
/// <para>
/// One implementation is enough for both targets: the app never creates a window,
/// view or graphics context itself.
/// </para>
/// </remarks>
public interface IDonkehApp
{
    /// <summary>Builds the root node for the application.</summary>
    /// <returns>The mounted tree.</returns>
    Node BuildRoot();

    /// <summary>Gets the author stylesheet for the application.</summary>
    /// <returns>CSS text. An empty string means "use only the user-agent sheet".</returns>
    string Styles();

    /// <summary>Gets the requested window settings.</summary>
    /// <remarks>
    /// Desktop hosts apply these values directly. The Android host keeps the
    /// visual settings but overrides size, frame pacing, MSAA and DPI, because the
    /// activity owns the surface.
    /// </remarks>
    WindowSettings Settings { get; }
}

/// <summary>
/// Marks the assembly's <see cref="IDonkehApp"/> implementation for hosts that
/// cannot call an entry point, such as the Android activity.
/// </summary>
/// <remarks>
/// Apply it once per application assembly:
/// <c>[assembly: DonkehApp(typeof(Program))]</c>. The type must implement
/// <see cref="IDonkehApp"/> and expose a public parameterless constructor.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class DonkehAppAttribute : Attribute
{
    /// <summary>
    /// Creates the attribute for an application type.
    /// </summary>
    /// <param name="appType">
    /// The type that implements <see cref="IDonkehApp"/>. It must have a public
    /// parameterless constructor.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="appType"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="appType"/> does not implement <see cref="IDonkehApp"/>.
    /// </exception>
    public DonkehAppAttribute(Type appType)
    {
        ArgumentNullException.ThrowIfNull(appType);

        if (!typeof(IDonkehApp).IsAssignableFrom(appType))
        {
            throw new ArgumentException(
                $"'{appType.FullName}' must implement {nameof(IDonkehApp)}.",
                nameof(appType));
        }

        AppType = appType;
    }

    /// <summary>Gets the application type.</summary>
    public Type AppType { get; }
}
