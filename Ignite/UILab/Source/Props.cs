using System.Globalization;

namespace Ignite.UILab
{
    /// <summary>
    /// Strongly typed props for an element. Common HTML attributes and events
    /// are first-class properties; anything else can go through
    /// <see cref="Attributes"/>.
    /// </summary>
    /// <remarks>
    /// Every value is interned when the element is built, so the diff compares
    /// ints even though the caller writes strings.
    /// </remarks>
    public sealed class Props
    {
        public string? Id { get; init; }

        public string? ClassName { get; init; }

        public string? Title { get; init; }

        public string? Href { get; init; }

        public string? Src { get; init; }

        public string? Type { get; init; }

        public string? Value { get; init; }

        public string? Placeholder { get; init; }

        public string? Role { get; init; }

        public string? Alt { get; init; }

        public string? Name { get; init; }

        public string? Target { get; init; }

        public string? Rel { get; init; }

        public int? TabIndex { get; init; }

        public bool? Disabled { get; init; }

        public bool? Checked { get; init; }

        /// <summary>
        /// Arbitrary attributes that do not have a dedicated property. The key is
        /// the attribute name, the value is its string value.
        /// </summary>
        public Dictionary<string, string>? Attributes { get; init; }

        /// <summary>
        /// A stable identity among siblings, used by the reconciler.
        /// </summary>
        public string? Key { get; init; }

        public Action? OnClick { get; init; }

        public Action<string>? OnInput { get; init; }

        public Action<string>? OnChange { get; init; }

        public Action? OnSubmit { get; init; }

        public Action<string>? OnKeyDown { get; init; }

        public Action? OnFocus { get; init; }

        public Action? OnBlur { get; init; }
    }
}
