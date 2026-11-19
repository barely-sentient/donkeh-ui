namespace Donkeh.UILab
{
    /// <summary>
    /// Describes a right-button context-menu request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Event.Target"/> is the deepest element under the pointer, which is the
    /// node the user actually clicked. <see cref="Event.CurrentTarget"/> is the element
    /// whose <see cref="Props.OnContextMenu"/> handler is running; it may be an
    /// ancestor when the event bubbles out of a child that does not handle it.
    /// </para>
    /// <para>
    /// <see cref="X"/> and <see cref="Y"/> are relative to
    /// <see cref="Event.CurrentTarget"/>'s painted border box, matching the other pointer
    /// events. <see cref="WindowX"/> and <see cref="WindowY"/> are in window CSS
    /// pixels, which is convenient for placing a menu at the pointer.
    /// </para>
    /// </remarks>
    public class ContextMenuEvent : MouseEvent
    {
        /// <summary>Creates a context-menu event for a host to dispatch.</summary>
        /// <param name="target">The deepest element under the pointer.</param>
        /// <param name="currentTarget">The element whose handler is being invoked.</param>
        /// <param name="x">Pointer X relative to the current target's painted border box.</param>
        /// <param name="y">Pointer Y relative to the current target's painted border box.</param>
        /// <param name="windowX">Pointer X in window CSS pixels.</param>
        /// <param name="windowY">Pointer Y in window CSS pixels.</param>
        /// <param name="modifiers">The modifier keys held during the event.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="currentTarget"/> is null.
        /// </exception>
        public ContextMenuEvent(
            ElementNode target,
            ElementNode currentTarget,
            float x,
            float y,
            float windowX,
            float windowY,
            KeyModifiers modifiers = KeyModifiers.None)
            : base("contextmenu", windowX, windowY, x, y, 0, modifiers)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(currentTarget);

            Target = target;
            CurrentTarget = currentTarget;
        }

        /// <summary>Gets the pointer X relative to the current target's painted border box.</summary>
        public float X => LocalX;

        /// <summary>Gets the pointer Y relative to the current target's painted border box.</summary>
        public float Y => LocalY;

        /// <summary>Gets the pointer X in window CSS pixels.</summary>
        public float WindowX => ClientX;

        /// <summary>Gets the pointer Y in window CSS pixels.</summary>
        public float WindowY => ClientY;

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<ContextMenuEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }
}
