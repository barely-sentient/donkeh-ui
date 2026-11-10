using System.Collections.Concurrent;
using System.Globalization;
using Ignite.Parser;

namespace Ignite.UILab
{
    /// <summary>
    /// An attribute already reduced to two interned ids. Comparing two of these
    /// is two int comparisons, which is what keeps the diff cheap.
    /// </summary>
    internal readonly record struct DomAttribute(int NameId, int ValueId);

    /// <summary>
    /// A single element attribute. It can carry either a string value or an
    /// event handler, which lets <c>H</c> accept both without boxing every
    /// attribute through <see cref="object"/>.
    /// </summary>
    public readonly struct Attr
    {
        /// <summary>The attribute or event name, for example <c>class</c> or <c>click</c>.</summary>
        public string Name { get; }

        /// <summary>The string value, or null when this attribute carries a handler.</summary>
        public string? Value { get; }

        /// <summary>The event handler, or null when this attribute carries a string value.</summary>
        public Delegate? Handler { get; }

        public Attr(string name, string? value)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            Name = name;
            Value = value;
            Handler = null;
        }

        public Attr(string name, Delegate handler)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(handler);
            Name = name;
            Value = null;
            Handler = handler;
        }

        public Attr(string name, bool value)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            Name = name;

            // Boolean attributes are present when true and omitted when false.
            Value = value ? string.Empty : null;
            Handler = null;
        }

        public Attr(string name, int value)
            : this(name, value.ToString(CultureInfo.InvariantCulture))
        {
        }

        public Attr(string name, long value)
            : this(name, value.ToString(CultureInfo.InvariantCulture))
        {
        }

        public Attr(string name, float value)
            : this(name, value.ToString(CultureInfo.InvariantCulture))
        {
        }

        public Attr(string name, double value)
            : this(name, value.ToString(CultureInfo.InvariantCulture))
        {
        }

        public static Attr Of(string name, string value) => new(name, value);

        public static Attr On(string name, Delegate handler) => new(name, handler);

        public static Attr Class(string value) => new("class", value);

        public static Attr Id(string value) => new("id", value);

        public static Attr OnClick(Action handler) => new("click", handler);

        public static Attr OnInput(Action<string> handler) => new("input", handler);

        public static Attr OnChange(Action<string> handler) => new("change", handler);

        public static Attr OnSubmit(Action handler) => new("submit", handler);
    }

    /// <summary>
    /// Process-wide cache for the interned ids of attribute names, tag names,
    /// class names and event names. <see cref="InternTable"/> already interns,
    /// but it takes a lock; this cache makes the common already-seen path a
    /// lock-free dictionary read.
    /// </summary>
    internal static class NameCache
    {
        private static readonly ConcurrentDictionary<string, int> Cache =
            new(StringComparer.Ordinal);

        public static int Get(string name)
        {
            if (Cache.TryGetValue(name, out var id))
            {
                return id;
            }

            return Cache.GetOrAdd(name, static value => InternTable.Intern(value));
        }

        public static int Get(ReadOnlySpan<char> name)
        {
            // Spans cannot be used as dictionary keys, so the allocating path is
            // only taken when the text has to be materialised anyway.
            return Get(name.ToString());
        }
    }
}
