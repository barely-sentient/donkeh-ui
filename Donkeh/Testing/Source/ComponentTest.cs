using Donkeh.UILab;

namespace Donkeh.Testing;

/// <summary>
/// Convenience entry points for the headless component test harness.
/// </summary>
/// <remarks>
/// <code>
/// using Donkeh.Testing;
/// using static Donkeh.UILab._;
///
/// var page = ComponentTest.Render(C&lt;Counter&gt;());
/// page.Click("button");
/// Assert.Equal("Count: 1", page.Find("button").Text);
/// </code>
/// </remarks>
public static class ComponentTest
{
    /// <summary>Mounts a virtual tree with the default options.</summary>
    /// <param name="vdom">The tree or component output to mount.</param>
    /// <param name="configure">Optional callback that edits the options.</param>
    /// <returns>A mounted, laid-out harness.</returns>
    public static ComponentTestHarness Render(
        Node vdom,
        Action<ComponentTestOptions>? configure = null)
    {
        var options = new ComponentTestOptions();
        configure?.Invoke(options);
        return new ComponentTestHarness(options).Mount(vdom);
    }

    /// <summary>Mounts a component with a public parameterless constructor.</summary>
    /// <typeparam name="TComponent">The component type.</typeparam>
    /// <param name="configure">Optional callback that edits the options.</param>
    /// <returns>A mounted, laid-out harness.</returns>
    public static ComponentTestHarness Render<TComponent>(
        Action<ComponentTestOptions>? configure = null)
        where TComponent : Component, new()
    {
        return Render(_.C<TComponent>(), configure);
    }

    /// <summary>Mounts a component with a typed props payload.</summary>
    /// <typeparam name="TComponent">The component type.</typeparam>
    /// <typeparam name="TProps">The props payload type.</typeparam>
    /// <param name="props">The props to assign.</param>
    /// <param name="configure">Optional callback that edits the options.</param>
    /// <returns>A mounted, laid-out harness.</returns>
    public static ComponentTestHarness Render<TComponent, TProps>(
        TProps props,
        Action<ComponentTestOptions>? configure = null)
        where TComponent : Component<TProps>, new()
    {
        return Render(_.C<TComponent, TProps>(props), configure);
    }
}
