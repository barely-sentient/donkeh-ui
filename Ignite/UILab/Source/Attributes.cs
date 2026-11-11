using System.Collections.Concurrent;
using System.Globalization;
using Ignite.Parser;

namespace Ignite.UILab
{
    /// <summary>
    /// An attribute already reduced to two interned ids. Comparing two of these
    /// is two int comparisons, which is what keeps the diff cheap.
    /// </summary>
    /// <param name="NameId">
    /// The id of the attribute name in <see cref="InternTable"/>, usually obtained
    /// through <see cref="NameCache"/>.
    /// </param>
    /// <param name="ValueId">
    /// The id of the attribute value in <see cref="InternTable"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// Instances are stored in <see cref="ElementNode.Attributes"/> in insertion
    /// order, and each name appears at most once. The struct itself enforces no
    /// uniqueness; <see cref="ElementNode.AddStringAttribute"/> replaces an entry
    /// with the same <see cref="NameId"/>.
    /// </para>
    /// <para>
    /// Because both parts are dense ids, equality is the default value equality and
    /// costs no string comparison. Ids are stable for the process lifetime, so a
    /// copied struct stays valid as long as the process runs.
    /// </para>
    /// </remarks>
    /// <seealso cref="NameCache"/>
    internal readonly record struct DomAttribute(int NameId, int ValueId);

    /// <summary>
    /// A single element attribute. It can carry either a string value or an
    /// event handler, which lets <c>H</c> accept both without boxing every
    /// attribute through <see cref="object"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// At most one of <see cref="Value"/> and <see cref="Handler"/> is non-null.
    /// Both are null when the attribute is meant to be omitted (a null string
    /// value or <see langword="false"/>). A string value is interned when the
    /// attribute is applied; a handler is stored in the element's event map under
    /// a normalised event name.
    /// </para>
    /// <para>
    /// The struct is immutable and cheap to copy. Its constructors validate the
    /// name eagerly, so an invalid <see cref="Attr"/> can never reach the tree.
    /// The default value is the exception: <c>default(Attr)</c> has a null
    /// <see cref="Name"/> and must not be applied.
    /// </para>
    /// </remarks>
    /// <seealso cref="ElementNode.WithAttribute(Attr)"/>
    public readonly struct Attr
    {
        /// <summary>The attribute or event name, for example <c>class</c> or <c>click</c>.</summary>
        public string Name { get; }

        /// <summary>
        /// The string value, or null when this attribute carries a handler or is
        /// omitted.
        /// </summary>
        public string? Value { get; }

        /// <summary>
        /// The event handler, or null when this attribute carries a string value
        /// or is omitted.
        /// </summary>
        public Delegate? Handler { get; }

        /// <summary>
        /// Creates an attribute with a string value.
        /// </summary>
        /// <param name="name">
        /// The attribute name, for example <c>class</c>. Leading <c>on</c> is not
        /// stripped here; that normalisation happens only for handlers.
        /// </param>
        /// <param name="value">
        /// The value, or <see langword="null"/> to omit the attribute. An empty
        /// string produces a value-less attribute such as <c>disabled</c>.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="name"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <remarks>
        /// The value is not interned here; it is interned once when
        /// <see cref="ElementNode.ApplyProps"/> or
        /// <see cref="ElementNode.WithAttribute(Attr)"/> applies the attribute.
        /// </remarks>
        public Attr(string name, string? value)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            Name = name;
            Value = value;
            Handler = null;
        }

        /// <summary>
        /// Creates an attribute that carries an event handler.
        /// </summary>
        /// <param name="name">
        /// The event name, for example <c>click</c> or <c>onClick</c>. The engine
        /// normalises it to a lower-case name without the <c>on</c> prefix.
        /// </param>
        /// <param name="handler">The handler to invoke.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="name"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="handler"/> is <see langword="null"/>.
        /// </exception>
        public Attr(string name, Delegate handler)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(handler);
            Name = name;
            Value = null;
            Handler = handler;
        }

        /// <summary>
        /// Creates a boolean presence attribute.
        /// </summary>
        /// <param name="name">The attribute name, for example <c>disabled</c>.</param>
        /// <param name="value">
        /// <see langword="true"/> emits an empty attribute value;
        /// <see langword="false"/> omits the attribute.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="name"/> is <see langword="null"/> or empty.
        /// </exception>
        public Attr(string name, bool value)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            Name = name;

            // Boolean attributes are present when true and omitted when false.
            Value = value ? string.Empty : null;
            Handler = null;
        }

        /// <summary>
        /// Creates an attribute from an integer formatted with the invariant culture.
        /// </summary>
        /// <param name="name">The attribute name.</param>
        /// <param name="value">The value to format.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="name"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <seealso cref="Attr(string, string?)"/>
        public Attr(string name, int value)
            : this(name, value.ToString(CultureInfo.InvariantCulture))
        {
        }

        /// <summary>
        /// Creates an attribute from a long formatted with the invariant culture.
        /// </summary>
        /// <param name="name">The attribute name.</param>
        /// <param name="value">The value to format.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="name"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <seealso cref="Attr(string, string?)"/>
        public Attr(string name, long value)
            : this(name, value.ToString(CultureInfo.InvariantCulture))
        {
        }

        /// <summary>
        /// Creates an attribute from a float formatted with the invariant culture.
        /// </summary>
        /// <param name="name">The attribute name.</param>
        /// <param name="value">The value to format.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="name"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <seealso cref="Attr(string, string?)"/>
        public Attr(string name, float value)
            : this(name, value.ToString(CultureInfo.InvariantCulture))
        {
        }

        /// <summary>
        /// Creates an attribute from a double formatted with the invariant culture.
        /// </summary>
        /// <param name="name">The attribute name.</param>
        /// <param name="value">The value to format.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="name"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <seealso cref="Attr(string, string?)"/>
        public Attr(string name, double value)
            : this(name, value.ToString(CultureInfo.InvariantCulture))
        {
        }

        /// <summary>
        /// Creates a string-valued attribute.
        /// </summary>
        /// <param name="name">The attribute name.</param>
        /// <param name="value">
        /// The attribute value; empty produces a value-less attribute.
        /// </param>
        /// <returns>A new <see cref="Attr"/> carrying <paramref name="value"/>.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="name"/> is <see langword="null"/> or empty.
        /// </exception>
        public static Attr Of(string name, string value) => new(name, value);

        /// <summary>
        /// Creates an event attribute.
        /// </summary>
        /// <param name="name">
        /// The event name, with or without the <c>on</c> prefix.
        /// </param>
        /// <param name="handler">The handler to invoke.</param>
        /// <returns>A new <see cref="Attr"/> carrying <paramref name="handler"/>.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="name"/> is <see langword="null"/> or empty.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="handler"/> is <see langword="null"/>.
        /// </exception>
        public static Attr On(string name, Delegate handler) => new(name, handler);

        /// <summary>
        /// Creates a <c>class</c> attribute.
        /// </summary>
        /// <param name="value">The whitespace-separated class names.</param>
        /// <returns>A new <see cref="Attr"/> named <c>class</c>.</returns>
        public static Attr Class(string value) => new("class", value);

        /// <summary>
        /// Creates an <c>id</c> attribute.
        /// </summary>
        /// <param name="value">The element id.</param>
        /// <returns>A new <see cref="Attr"/> named <c>id</c>.</returns>
        public static Attr Id(string value) => new("id", value);

        /// <summary>
        /// Creates a <c>click</c> event attribute.
        /// </summary>
        /// <param name="handler">The handler to invoke.</param>
        /// <returns>A new <see cref="Attr"/> named <c>click</c>.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="handler"/> is <see langword="null"/>.
        /// </exception>
        public static Attr OnClick(Action handler) => new("click", handler);

        /// <summary>
        /// Creates an <c>input</c> event attribute.
        /// </summary>
        /// <param name="handler">The handler that receives the current text.</param>
        /// <returns>A new <see cref="Attr"/> named <c>input</c>.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="handler"/> is <see langword="null"/>.
        /// </exception>
        public static Attr OnInput(Action<string> handler) => new("input", handler);

        /// <summary>
        /// Creates a <c>change</c> event attribute.
        /// </summary>
        /// <param name="handler">The handler that receives the committed value.</param>
        /// <returns>A new <see cref="Attr"/> named <c>change</c>.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="handler"/> is <see langword="null"/>.
        /// </exception>
        public static Attr OnChange(Action<string> handler) => new("change", handler);

        /// <summary>
        /// Creates a <c>submit</c> event attribute.
        /// </summary>
        /// <param name="handler">The handler to invoke.</param>
        /// <returns>A new <see cref="Attr"/> named <c>submit</c>.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="handler"/> is <see langword="null"/>.
        /// </exception>
        public static Attr OnSubmit(Action handler) => new("submit", handler);
    }

    /// <summary>
    /// Process-wide cache for the interned ids of attribute names, tag names,
    /// class names and event names. <see cref="InternTable"/> already interns,
    /// but it takes a lock; this cache makes the common already-seen path a
    /// lock-free dictionary read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both layers hand out the same ids, so a value from this cache can be mixed
    /// freely with one from <see cref="InternTable"/>. Entries are never removed;
    /// the cache grows with the number of distinct names, not with the number of
    /// lookups, and lives for the lifetime of the process.
    /// </para>
    /// <para>
    /// The class is thread-safe. The first lookup for a name takes the
    /// <see cref="InternTable"/> lock; subsequent lookups do not.
    /// </para>
    /// </remarks>
    /// <seealso cref="DomAttribute"/>
    internal static class NameCache
    {
        /// <summary>
        /// Maps a name to its id. The dictionary is case-sensitive, so
        /// <c>class</c> and <c>Class</c> are different names.
        /// </summary>
        private static readonly ConcurrentDictionary<string, int> Cache =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Returns the interned id for a name, adding it on first sighting.
        /// </summary>
        /// <param name="name">The name to look up. Must not be null.</param>
        /// <returns>A non-negative id that is stable for the process lifetime.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="name"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// The factory passed to <c>GetOrAdd</c> may run more than once under
        /// contention, but it is idempotent because
        /// <see cref="InternTable.Intern(string)"/> returns the same id for the same
        /// text.
        /// </remarks>
        public static int Get(string name)
        {
            if (Cache.TryGetValue(name, out var id))
            {
                return id;
            }

            return Cache.GetOrAdd(name, static value => InternTable.Intern(value));
        }

        /// <summary>
        /// Returns the interned id for a name supplied as a span.
        /// </summary>
        /// <param name="name">The name to look up.</param>
        /// <returns>A non-negative id that is stable for the process lifetime.</returns>
        /// <remarks>
        /// Spans cannot be used as dictionary keys, so the allocating path is
        /// only taken when the text has to be materialised anyway.
        /// </remarks>
        public static int Get(ReadOnlySpan<char> name)
        {
            // Spans cannot be used as dictionary keys, so the allocating path is
            // only taken when the text has to be materialised anyway.
            return Get(name.ToString());
        }
    }
}
