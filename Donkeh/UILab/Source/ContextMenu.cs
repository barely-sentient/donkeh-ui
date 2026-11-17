namespace Donkeh.UILab
{
    /// <summary>
    /// Describes a right-button context-menu request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Target"/> is the deepest element under the pointer, which is the
    /// node the user actually clicked. <see cref="CurrentTarget"/> is the element
    /// whose <see cref="Props.OnContextMenu"/> handler is running; it may be an
    /// ancestor when the event bubbles out of a child that does not handle it.
    /// </para>
    /// <para>
    /// <see cref="X"/> and <see cref="Y"/> are relative to
    /// <see cref="CurrentTarget"/>'s painted border box, matching the other pointer
    /// events. <see cref="WindowX"/> and <see cref="WindowY"/> are in window CSS
    /// pixels, which is convenient for placing a menu at the pointer.
    /// </para>
    /// </remarks>
    public sealed class ContextMenuEvent
    {
        /// <summary>Creates a context-menu event for a host to dispatch.</summary>
        /// <param name="target">The deepest element under the pointer.</param>
        /// <param name="currentTarget">The element whose handler is being invoked.</param>
        /// <param name="x">Pointer X relative to the current target's painted border box.</param>
        /// <param name="y">Pointer Y relative to the current target's painted border box.</param>
        /// <param name="windowX">Pointer X in window CSS pixels.</param>
        /// <param name="windowY">Pointer Y in window CSS pixels.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="currentTarget"/> is null.
        /// </exception>
        public ContextMenuEvent(
            ElementNode target,
            ElementNode currentTarget,
            float x,
            float y,
            float windowX,
            float windowY)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(currentTarget);

            Target = target;
            CurrentTarget = currentTarget;
            X = x;
            Y = y;
            WindowX = windowX;
            WindowY = windowY;
        }

        /// <summary>Gets the deepest element under the pointer.</summary>
        /// <remarks>
        /// Read <c>ElementNode.Key</c> or an attribute such as <c>data-id</c> to
        /// identify which row or item was clicked.
        /// </remarks>
        public ElementNode Target { get; }

        /// <summary>Gets the element whose context-menu handler is running.</summary>
        public ElementNode CurrentTarget { get; }

        /// <summary>Gets the pointer X relative to the current target's painted border box.</summary>
        public float X { get; }

        /// <summary>Gets the pointer Y relative to the current target's painted border box.</summary>
        public float Y { get; }

        /// <summary>Gets the pointer X in window CSS pixels.</summary>
        public float WindowX { get; }

        /// <summary>Gets the pointer Y in window CSS pixels.</summary>
        public float WindowY { get; }
    }
}
