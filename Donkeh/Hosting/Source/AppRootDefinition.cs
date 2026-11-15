using Donkeh.UILab;
using Donkeh.Windowing;

namespace Donkeh.Hosting;

/// <summary>Describes the discovered application root and its host settings.</summary>
/// <remarks>
/// Hosts use this instead of an application-defined <c>BuildRoot</c> and
/// <c>Settings</c> implementation. <see cref="CreateRoot"/> builds the component
/// node; <see cref="Styles"/> loads the stylesheets declared with
/// <see cref="StylesheetAttribute"/>.
/// </remarks>
public sealed class AppRootDefinition
{
    private readonly Func<Node> _createRoot;

    /// <summary>Creates a definition for an attributed component root.</summary>
    internal AppRootDefinition(Type componentType, WindowSettings settings)
    {
        ComponentType = componentType;
        Settings = settings;
        Styles = Stylesheet.Load();
        _createRoot = () => _.C(componentType);
    }

    /// <summary>Creates a definition for a legacy <c>IDonkehApp</c>.</summary>
    internal AppRootDefinition(Func<Node> createRoot, string styles, WindowSettings settings)
    {
        _createRoot = createRoot;
        Styles = styles;
        Settings = settings;
    }

    /// <summary>Gets the attributed component type, or null for a legacy app.</summary>
    public Type? ComponentType { get; }

    /// <summary>Gets the window and host settings supplied by the attribute.</summary>
    public WindowSettings Settings { get; }

    /// <summary>Gets the merged author stylesheet text.</summary>
    public string Styles { get; }

    /// <summary>Creates the root virtual node for this application.</summary>
    /// <returns>A component node suitable for <see cref="Renderer.Mount"/>.</returns>
    public Node CreateRoot() => _createRoot();
}
