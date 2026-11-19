using System.Collections.Generic;

namespace Donkeh.UILab
{
    /// <summary>
    /// Mutable payload shared by every event in one drag operation, modelled on
    /// the browser <c>DataTransfer</c> object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host creates one instance when a drag starts and passes the same object
    /// to <c>dragstart</c>, <c>drag</c>, <c>dragenter</c>, <c>dragover</c>,
    /// <c>drop</c> and <c>dragend</c>. A drag source writes a typed payload in
    /// <c>dragstart</c> and a drop target reads it back.
    /// </para>
    /// <para>
    /// Formats are arbitrary strings. Use a reverse-DNS media type such as
    /// <c>application/x-myapp-item</c> for app-private data, and
    /// <c>text/plain</c> for a readable fallback.
    /// </para>
    /// </remarks>
    public sealed class DataTransfer
    {
        private readonly Dictionary<string, string> _data = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets or sets the operations the source permits, for example
        /// <c>copy</c>, <c>move</c>, <c>link</c> or <c>all</c>.
        /// </summary>
        /// <remarks>The host does not enforce this value; it is descriptive.</remarks>
        public string EffectAllowed { get; set; } = "all";

        /// <summary>
        /// Gets or sets the operation the current target reports, for example
        /// <c>copy</c>, <c>move</c> or <c>none</c>.
        /// </summary>
        /// <remarks>
        /// Set <c>none</c> in <c>dragover</c> to show that the target will not
        /// accept the drop. The host still dispatches <c>drop</c> when a target has
        /// a handler; use <see cref="DragEvent.Cancel"/> in <c>dragstart</c> to
        /// abort entirely.
        /// </remarks>
        public string DropEffect { get; set; } = "move";

        /// <summary>Gets a read-only view of the stored payload entries.</summary>
        public IReadOnlyDictionary<string, string> Items => _data;

        /// <summary>Reports whether a payload exists for a format.</summary>
        /// <param name="format">The format name; matching is case-insensitive.</param>
        /// <returns><see langword="true"/> when data is present.</returns>
        public bool HasData(string format) => _data.ContainsKey(format);

        /// <summary>Reads a payload entry.</summary>
        /// <param name="format">The format name; matching is case-insensitive.</param>
        /// <returns>The stored value, or <see langword="null"/> when absent.</returns>
        public string? GetData(string format) => _data.TryGetValue(format, out var value) ? value : null;

        /// <summary>Stores a payload entry, replacing any previous value.</summary>
        /// <param name="format">The format name; matching is case-insensitive.</param>
        /// <param name="value">The string payload.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="format"/> is null or empty.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="value"/> is null.
        /// </exception>
        public void SetData(string format, string value)
        {
            ArgumentException.ThrowIfNullOrEmpty(format);
            ArgumentNullException.ThrowIfNull(value);
            _data[format] = value;
        }

        /// <summary>Removes every payload entry.</summary>
        public void ClearData() => _data.Clear();
    }

    /// <summary>
    /// One HTML-style drag event delivered to a <c>draggable</c> element or a drop
    /// target.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Events follow the browser order: <c>dragstart</c> on the source when the
    /// pointer moves past a small threshold, <c>drag</c> while it moves,
    /// <c>dragenter</c>/<c>dragover</c>/<c>dragleave</c> as the pointer crosses
    /// targets, <c>drop</c> on release over a target, and <c>dragend</c> on the
    /// source when the interaction finishes.
    /// </para>
    /// <para>
    /// <see cref="MouseEvent.ClientX"/> and <see cref="MouseEvent.ClientY"/> are window CSS pixels.
    /// <see cref="MouseEvent.LocalX"/> and <see cref="MouseEvent.LocalY"/> are relative to the handled
    /// element's painted border box, so a timeline lane can map a drop position to
    /// time without knowing about scrolling.
    /// </para>
    /// </remarks>
    public class DragEvent : MouseEvent
    {
        internal DragEvent(
            string type,
            DataTransfer data,
            float clientX,
            float clientY,
            ElementNode? source)
            : base(type, clientX, clientY)
        {
            Data = data;
            Source = source;
        }

        /// <summary>Gets the payload shared by the whole drag operation.</summary>
        public DataTransfer Data { get; }

        /// <summary>Gets the element that started the drag.</summary>
        public ElementNode? Source { get; }

        /// <summary>
        /// Gets or sets whether the drag should be abandoned.
        /// </summary>
        /// <remarks>
        /// Set this in <c>dragstart</c> to cancel before the drag becomes active.
        /// It is ignored by later events.
        /// </remarks>
        public bool Cancel { get; set; }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<DragEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }
}
