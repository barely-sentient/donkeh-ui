using Donkeh.LayoutLab;

namespace Donkeh.Testing;

/// <summary>
/// Configures a headless <see cref="ComponentTestHarness"/>.
/// </summary>
/// <remarks>
/// <para>
/// The defaults are deliberately close to a desktop application frame: a
/// 1024 by 768 viewport, the built-in HTML user-agent sheet, and every
/// stylesheet declared through <c>[Stylesheet(...)]</c> in the loaded assemblies.
/// Tests can add their own CSS, change the viewport, or supply a deterministic
/// text measurer.
/// </para>
/// <para>
/// The object is mutable and is read when the harness mounts. Values changed
/// afterwards affect later <see cref="ComponentTestHarness.Settle"/> calls where
/// applicable, but the compiled stylesheet is only rebuilt by
/// <see cref="ComponentTestHarness.RebuildStyles"/>.
/// </para>
/// </remarks>
public sealed class ComponentTestOptions
{
    /// <summary>Gets or sets the viewport width in CSS pixels.</summary>
    public float ViewportWidth { get; set; } = 1024;

    /// <summary>Gets or sets the viewport height in CSS pixels.</summary>
    public float ViewportHeight { get; set; } = 768;

    /// <summary>
    /// Gets or sets whether the built-in HTML user-agent sheet is included.
    /// </summary>
    /// <remarks>
    /// Turn this off only when a test intentionally starts from an empty cascade.
    /// Most component tests should keep it on so form controls, headings and
    /// block elements behave like the real application.
    /// </remarks>
    public bool IncludeDefaultStyles { get; set; } = true;

    /// <summary>
    /// Gets or sets whether stylesheets declared with <c>[Stylesheet(...)]</c>
    /// are loaded from the currently loaded assemblies.
    /// </summary>
    public bool IncludeDeclaredStylesheets { get; set; } = true;

    /// <summary>Gets or sets the root font size in CSS pixels.</summary>
    public float RootFontSize { get; set; } = 16;

    /// <summary>Gets or sets the font size used before the root style resolves.</summary>
    public float DefaultFontSize { get; set; } = 16;

    /// <summary>
    /// Gets or sets the text measurer used by layout, or <see langword="null"/>
    /// for the deterministic <see cref="SimpleTextMeasurer"/>.
    /// </summary>
    /// <remarks>
    /// Tests that assert exact pixel geometry should keep the deterministic
    /// measurer or supply their own fixed-width implementation.
    /// </remarks>
    public ITextMeasurer? TextMeasurer { get; set; }

    /// <summary>Gets or sets whether the display-list optimiser runs.</summary>
    /// <remarks>
    /// Component tests normally only inspect the box tree, so leaving this off
    /// keeps layout cheap. Turn it on when a test asserts optimiser behaviour.
    /// </remarks>
    public bool Optimise { get; set; }

    /// <summary>
    /// Gets the extra stylesheets appended after the user-agent and declared
    /// sheets, in declaration order.
    /// </summary>
    /// <remarks>Later sheets win ties, matching the application window.</remarks>
    public List<string> Stylesheets { get; } = [];
}
