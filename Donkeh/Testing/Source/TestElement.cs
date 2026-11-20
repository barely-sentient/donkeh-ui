using System.Text;
using Donkeh.Data.Dom;
using Donkeh.LayoutLab;
using Donkeh.UILab;

namespace Donkeh.Testing;

/// <summary>
/// A live element inside a mounted component tree, with query and event helpers.
/// </summary>
/// <remarks>
/// <para>
/// Instances are lightweight wrappers around an <see cref="ElementNode"/>. The
/// node identity survives component re-renders, so a test may keep a
/// <see cref="TestElement"/> across state updates as long as the element itself
/// was not unmounted.
/// </para>
/// <para>
/// Geometry and computed styles come from the harness's most recent layout.
/// Querying them settles pending renders first, so assertions always see the
/// result of the last event instead of a stale frame.
/// </para>
/// </remarks>
public sealed class TestElement
{
    internal TestElement(ComponentTestHarness harness, ElementNode element)
    {
        Harness = harness;
        Element = element;
    }

    /// <summary>Gets the harness that owns the mounted tree.</summary>
    public ComponentTestHarness Harness { get; }

    /// <summary>Gets the live UILab element.</summary>
    public ElementNode Element { get; }

    /// <summary>Gets the lower-case tag name.</summary>
    public string Tag => Element.Tag;

    /// <summary>Gets the element id, or <see langword="null"/> when it has none.</summary>
    public string? Id => Element.Id;

    /// <summary>Gets the element's own text content.</summary>
    /// <remarks>
    /// This mirrors <see cref="IgniteSupportedDomNode.TextContent"/> and does not
    /// include text owned by descendants. Use <see cref="InnerText"/> for the
    /// flattened subtree text.
    /// </remarks>
    public string Text => Element.TextContent ?? string.Empty;

    /// <summary>Gets the concatenated text of this element and all descendants.</summary>
    public string InnerText
    {
        get
        {
            var builder = new StringBuilder();
            AppendText(Element, builder);
            return builder.ToString();
        }
    }

    /// <summary>Gets the live parent element, or <see langword="null"/> at the root.</summary>
    public TestElement? Parent =>
        Element.Parent is ElementNode parent ? new TestElement(Harness, parent) : null;

    /// <summary>Gets the element children in document order.</summary>
    public IReadOnlyList<TestElement> Children
    {
        get
        {
            var children = Element.Children;
            var result = new List<TestElement>(children.Length);

            for (var i = 0; i < children.Length; i++)
            {
                if (children[i] is ElementNode child)
                {
                    result.Add(new TestElement(Harness, child));
                }
            }

            return result;
        }
    }

    /// <summary>Gets whether the element is the current focus target.</summary>
    public bool IsFocused => Element.IsFocused;

    /// <summary>Gets whether the element is currently hovered.</summary>
    public bool IsHovered => Element.IsHovered;

    /// <summary>Gets whether the element is currently pressed.</summary>
    public bool IsActive => Element.IsActive;

    /// <summary>Gets whether the element is checked.</summary>
    public bool IsChecked => Element.IsChecked;

    /// <summary>Gets whether the element produces a visible layout box.</summary>
    /// <remarks>
    /// True when the element has a box in the current layout and neither
    /// <c>display: none</c> nor <c>visibility: hidden</c> applies.
    /// </remarks>
    public bool IsVisible
    {
        get
        {
            Harness.Settle();
            var box = GetBox();
            return box is not null
                && box.Style.Display != Display.None
                && box.Style.Visibility is not (Visibility.Hidden or Visibility.Collapse);
        }
    }

    /// <summary>Gets the border box in window CSS pixels, or an empty rectangle.</summary>
    public Rect Bounds => GetBox()?.BorderBox ?? default;

    /// <summary>Gets the computed style from the latest layout, or <see langword="null"/>.</summary>
    public ComputedStyle? Style
    {
        get
        {
            Harness.Settle();
            return GetBox()?.Style;
        }
    }

    /// <summary>Gets the layout box from the latest layout, or <see langword="null"/>.</summary>
    public LayoutBox? Box
    {
        get
        {
            Harness.Settle();
            return GetBox();
        }
    }

    /// <summary>Gets the value of a form control or an attribute-backed value.</summary>
    /// <remarks>
    /// Text inputs read <c>value</c>; textareas read their text content. Other
    /// elements return the <c>value</c> attribute when present.
    /// </remarks>
    public string Value
    {
        get
        {
            if (Tag == "textarea")
            {
                return Element.TextContent ?? string.Empty;
            }

            return GetAttribute("value") ?? string.Empty;
        }
    }

    /// <summary>Gets a class name check that ignores case and duplicate entries.</summary>
    /// <param name="className">The class to look for.</param>
    /// <returns><see langword="true"/> when the element carries the class.</returns>
    public bool HasClass(string className) => Element.HasClass(className);

    /// <summary>Gets whether an attribute is present.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns><see langword="true"/> when the attribute exists.</returns>
    public bool HasAttribute(string name) => Element.TryGetAttribute(name, out var ignoredValue);

    /// <summary>Gets an attribute value, or <see langword="null"/> when absent.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The attribute value, or <see langword="null"/>.</returns>
    public string? GetAttribute(string name) =>
        Element.TryGetAttribute(name, out var value) ? value : null;

    /// <summary>Finds every descendant matching a CSS selector.</summary>
    /// <param name="selector">A selector such as <c>.card &gt; button</c>.</param>
    /// <returns>Matches in document order, excluding this element.</returns>
    public IReadOnlyList<TestElement> FindAll(string selector)
    {
        Harness.Settle();
        return Harness.FindAllWithin(Element, selector);
    }

    /// <summary>Finds the first descendant matching a CSS selector.</summary>
    /// <param name="selector">A selector such as <c>.card &gt; button</c>.</param>
    /// <returns>The first match, or <see langword="null"/> when none exists.</returns>
    public TestElement? TryFind(string selector)
    {
        var matches = FindAll(selector);
        return matches.Count > 0 ? matches[0] : null;
    }

    /// <summary>Finds the first descendant matching a CSS selector.</summary>
    /// <param name="selector">A selector such as <c>.card &gt; button</c>.</param>
    /// <returns>The first match.</returns>
    /// <exception cref="ComponentQueryException">No descendant matched.</exception>
    public TestElement Find(string selector)
    {
        var matches = FindAll(selector);

        if (matches.Count == 0)
        {
            throw new ComponentQueryException(
                $"No descendant of <{Tag}> matched the selector '{selector}'.");
        }

        return matches[0];
    }

    /// <summary>Clicks this element.</summary>
    /// <returns>The owning harness, for chaining.</returns>
    public ComponentTestHarness Click() => Harness.Click(this);

    /// <summary>Presses the primary mouse button over this element.</summary>
    /// <returns>The owning harness, for chaining.</returns>
    public ComponentTestHarness MouseDown() => Harness.MouseDown(this);

    /// <summary>Releases the primary mouse button over this element.</summary>
    /// <returns>The owning harness, for chaining.</returns>
    public ComponentTestHarness MouseUp() => Harness.MouseUp(this);

    /// <summary>Sends input text to this form control.</summary>
    /// <param name="value">The new control value.</param>
    /// <returns>The owning harness, for chaining.</returns>
    public ComponentTestHarness Input(string value) => Harness.Input(this, value);

    /// <summary>Sends a scroll delta to this element.</summary>
    /// <param name="deltaX">Horizontal delta in CSS pixels.</param>
    /// <param name="deltaY">Vertical delta in CSS pixels.</param>
    /// <returns>The owning harness, for chaining.</returns>
    public ComponentTestHarness Scroll(float deltaX, float deltaY) =>
        Harness.Scroll(this, deltaX, deltaY);

    /// <summary>Moves keyboard focus to this element.</summary>
    /// <returns>The owning harness, for chaining.</returns>
    public ComponentTestHarness Focus() => Harness.Focus(this);

    /// <summary>Removes keyboard focus from this element.</summary>
    /// <returns>The owning harness, for chaining.</returns>
    public ComponentTestHarness Blur() => Harness.Blur(this);

    /// <summary>Returns a short diagnostic representation of the element.</summary>
    public override string ToString()
    {
        var id = Id is null ? string.Empty : $" id=\"{Id}\"";
        var text = Text.Length == 0 ? string.Empty : $" \"{Text}\"";
        return $"<{Tag}{id}>{text}";
    }

    private LayoutBox? GetBox()
    {
        var layout = Harness.Layout;
        return layout is not null && layout.TryGetBox(Element, out var box) ? box : null;
    }

    private static void AppendText(IgniteSupportedDomNode node, StringBuilder builder)
    {
        if (node.TextContent is { Length: > 0 } text)
        {
            builder.Append(text);
        }

        var children = node.Children;

        for (var i = 0; i < children.Length; i++)
        {
            AppendText(children[i], builder);
        }
    }
}
