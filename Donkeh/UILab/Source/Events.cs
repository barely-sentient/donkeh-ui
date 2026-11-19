namespace Donkeh.UILab
{
    /// <summary>
    /// Base class for every event a host dispatches into a UILab tree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hosts create the most specific event type they can and pass it to the
    /// registered handler. The base class is intentionally useful on its own, so a
    /// host that has no richer payload can dispatch a plain event and add a
    /// subclass later without changing the event map.
    /// </para>
    /// <para>
    /// <see cref="Target"/> is the deepest element the user interacted with.
    /// <see cref="CurrentTarget"/> is the element whose handler is running; it can
    /// be an ancestor when a bubbled event finds its first handler further up the
    /// tree. Pointer coordinates are window CSS pixels in
    /// <see cref="MouseEvent.ClientX"/> and <see cref="MouseEvent.ClientY"/>, and
    /// are also exposed relative to the handling element by
    /// <see cref="MouseEvent.LocalX"/> and <see cref="MouseEvent.LocalY"/> for
    /// pointer events.
    /// </para>
    /// </remarks>
    public class Event
    {
        /// <summary>Creates an event with the given name.</summary>
        /// <param name="type">The normalised event name, such as <c>click</c>.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="type"/> is null or empty.
        /// </exception>
        public Event(string type)
        {
            ArgumentException.ThrowIfNullOrEmpty(type);
            Type = type;
        }

        /// <summary>Gets the normalised event name, such as <c>click</c> or <c>input</c>.</summary>
        public string Type { get; }

        /// <summary>Gets the deepest element involved in the event.</summary>
        public ElementNode? Target { get; internal set; }

        /// <summary>Gets the element whose handler is currently running.</summary>
        public ElementNode? CurrentTarget { get; internal set; }

        /// <summary>Gets whether the handler asked the host to suppress the default action.</summary>
        public bool DefaultPrevented { get; protected set; }

        /// <summary>Gets whether the handler asked the event not to bubble further.</summary>
        public bool PropagationStopped { get; private set; }

        /// <summary>Marks the event as handled.</summary>
        /// <remarks>
        /// Hosts with no default action treat this as advisory, matching the
        /// browser contract. It lets the same handler work on every host.
        /// </remarks>
        public virtual void PreventDefault() => DefaultPrevented = true;

        /// <summary>Stops the event from bubbling to ancestor handlers.</summary>
        /// <remarks>
        /// UILab's built-in dispatch already invokes only the nearest handler, so
        /// this mainly documents intent and leaves room for a richer dispatcher.
        /// </remarks>
        public void StopPropagation() => PropagationStopped = true;

        /// <summary>
        /// Invokes a handler that accepts this event type, if its delegate shape
        /// matches. Used by hosts before falling back to the legacy delegates.
        /// </summary>
        internal virtual bool TryInvoke(Delegate handler)
        {
            if (handler is Action<Event> action)
            {
                action(this);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// An event that carries pointer position and button information.
    /// </summary>
    /// <remarks>
    /// <see cref="ClientX"/> and <see cref="ClientY"/> are in window CSS pixels.
    /// <see cref="LocalX"/> and <see cref="LocalY"/> are relative to
    /// <see cref="Event.CurrentTarget"/>'s painted border box. <see cref="Button"/>
    /// uses the DOM numbering: 0 left, 1 middle, 2 right, 3 back and 4 forward.
    /// </remarks>
    public class MouseEvent : Event
    {
        /// <summary>Creates a mouse event.</summary>
        /// <param name="type">The normalised event name.</param>
        /// <param name="clientX">Pointer X in window CSS pixels.</param>
        /// <param name="clientY">Pointer Y in window CSS pixels.</param>
        /// <param name="localX">Pointer X relative to the handling element.</param>
        /// <param name="localY">Pointer Y relative to the handling element.</param>
        /// <param name="button">The DOM button number.</param>
        /// <param name="modifiers">The modifier keys held during the event.</param>
        public MouseEvent(
            string type,
            float clientX,
            float clientY,
            float localX = 0,
            float localY = 0,
            int button = 0,
            KeyModifiers modifiers = KeyModifiers.None)
            : base(type)
        {
            ClientX = clientX;
            ClientY = clientY;
            LocalX = localX;
            LocalY = localY;
            Button = button;
            Modifiers = modifiers;
        }

        /// <summary>Gets the pointer X in window CSS pixels.</summary>
        public float ClientX { get; }

        /// <summary>Gets the pointer Y in window CSS pixels.</summary>
        public float ClientY { get; }

        /// <summary>Gets the pointer X relative to the handling element's painted border box.</summary>
        public float LocalX { get; internal set; }

        /// <summary>Gets the pointer Y relative to the handling element's painted border box.</summary>
        public float LocalY { get; internal set; }

        /// <summary>Gets the DOM button number: 0 left, 1 middle, 2 right.</summary>
        public int Button { get; }

        /// <summary>Gets the modifier keys held during the event.</summary>
        public KeyModifiers Modifiers { get; }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<MouseEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when an element is clicked.</summary>
    public class ClickEvent : MouseEvent
    {
        /// <summary>Creates a click event.</summary>
        /// <param name="clientX">Pointer X in window CSS pixels.</param>
        /// <param name="clientY">Pointer Y in window CSS pixels.</param>
        /// <param name="localX">Pointer X relative to the handling element.</param>
        /// <param name="localY">Pointer Y relative to the handling element.</param>
        /// <param name="button">The DOM button number.</param>
        /// <param name="modifiers">The modifier keys held during the event.</param>
        public ClickEvent(
            float clientX = 0,
            float clientY = 0,
            float localX = 0,
            float localY = 0,
            int button = 0,
            KeyModifiers modifiers = KeyModifiers.None)
            : base("click", clientX, clientY, localX, localY, button, modifiers)
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<ClickEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when a mouse button is pressed.</summary>
    public class MouseDownEvent : MouseEvent
    {
        /// <summary>Creates a mousedown event.</summary>
        /// <param name="clientX">Pointer X in window CSS pixels.</param>
        /// <param name="clientY">Pointer Y in window CSS pixels.</param>
        /// <param name="localX">Pointer X relative to the handling element.</param>
        /// <param name="localY">Pointer Y relative to the handling element.</param>
        /// <param name="button">The DOM button number.</param>
        /// <param name="modifiers">The modifier keys held during the event.</param>
        public MouseDownEvent(
            float clientX = 0,
            float clientY = 0,
            float localX = 0,
            float localY = 0,
            int button = 0,
            KeyModifiers modifiers = KeyModifiers.None)
            : base("mousedown", clientX, clientY, localX, localY, button, modifiers)
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<MouseDownEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when a mouse button is released.</summary>
    public class MouseUpEvent : MouseEvent
    {
        /// <summary>Creates a mouseup event.</summary>
        /// <param name="clientX">Pointer X in window CSS pixels.</param>
        /// <param name="clientY">Pointer Y in window CSS pixels.</param>
        /// <param name="localX">Pointer X relative to the handling element.</param>
        /// <param name="localY">Pointer Y relative to the handling element.</param>
        /// <param name="button">The DOM button number.</param>
        /// <param name="modifiers">The modifier keys held during the event.</param>
        public MouseUpEvent(
            float clientX = 0,
            float clientY = 0,
            float localX = 0,
            float localY = 0,
            int button = 0,
            KeyModifiers modifiers = KeyModifiers.None)
            : base("mouseup", clientX, clientY, localX, localY, button, modifiers)
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<MouseUpEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when the pointer enters an element.</summary>
    public class MouseOverEvent : MouseEvent
    {
        /// <summary>Creates a mouseover event.</summary>
        /// <param name="clientX">Pointer X in window CSS pixels.</param>
        /// <param name="clientY">Pointer Y in window CSS pixels.</param>
        /// <param name="localX">Pointer X relative to the handling element.</param>
        /// <param name="localY">Pointer Y relative to the handling element.</param>
        /// <param name="modifiers">The modifier keys held during the event.</param>
        public MouseOverEvent(
            float clientX = 0,
            float clientY = 0,
            float localX = 0,
            float localY = 0,
            KeyModifiers modifiers = KeyModifiers.None)
            : base("mouseover", clientX, clientY, localX, localY, 0, modifiers)
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<MouseOverEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when the pointer leaves an element.</summary>
    public class MouseOutEvent : MouseEvent
    {
        /// <summary>Creates a mouseout event.</summary>
        /// <param name="clientX">Pointer X in window CSS pixels.</param>
        /// <param name="clientY">Pointer Y in window CSS pixels.</param>
        /// <param name="localX">Pointer X relative to the handling element.</param>
        /// <param name="localY">Pointer Y relative to the handling element.</param>
        /// <param name="modifiers">The modifier keys held during the event.</param>
        public MouseOutEvent(
            float clientX = 0,
            float clientY = 0,
            float localX = 0,
            float localY = 0,
            KeyModifiers modifiers = KeyModifiers.None)
            : base("mouseout", clientX, clientY, localX, localY, 0, modifiers)
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<MouseOutEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>Base class for pointer events.</summary>
    /// <remarks>
    /// Pointer events carry the same position data as <see cref="MouseEvent"/>.
    /// The class exists separately so richer pointer fields can be added without
    /// changing the mouse handlers.
    /// </remarks>
    public class PointerEvent : MouseEvent
    {
        /// <summary>Creates a pointer event.</summary>
        /// <param name="type">The normalised event name.</param>
        /// <param name="clientX">Pointer X in window CSS pixels.</param>
        /// <param name="clientY">Pointer Y in window CSS pixels.</param>
        /// <param name="localX">Pointer X relative to the handling element.</param>
        /// <param name="localY">Pointer Y relative to the handling element.</param>
        /// <param name="button">The DOM button number.</param>
        /// <param name="modifiers">The modifier keys held during the event.</param>
        public PointerEvent(
            string type,
            float clientX,
            float clientY,
            float localX = 0,
            float localY = 0,
            int button = 0,
            KeyModifiers modifiers = KeyModifiers.None)
            : base(type, clientX, clientY, localX, localY, button, modifiers)
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<PointerEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when a pointer contacts an element.</summary>
    public class PointerDownEvent : PointerEvent
    {
        /// <summary>Creates a pointerdown event.</summary>
        /// <param name="clientX">Pointer X in window CSS pixels.</param>
        /// <param name="clientY">Pointer Y in window CSS pixels.</param>
        /// <param name="localX">Pointer X relative to the handling element.</param>
        /// <param name="localY">Pointer Y relative to the handling element.</param>
        /// <param name="button">The DOM button number.</param>
        /// <param name="modifiers">The modifier keys held during the event.</param>
        public PointerDownEvent(
            float clientX = 0,
            float clientY = 0,
            float localX = 0,
            float localY = 0,
            int button = 0,
            KeyModifiers modifiers = KeyModifiers.None)
            : base("pointerdown", clientX, clientY, localX, localY, button, modifiers)
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<PointerDownEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised while a pointer moves over an element.</summary>
    public class PointerMoveEvent : PointerEvent
    {
        /// <summary>Creates a pointermove event.</summary>
        /// <param name="clientX">Pointer X in window CSS pixels.</param>
        /// <param name="clientY">Pointer Y in window CSS pixels.</param>
        /// <param name="localX">Pointer X relative to the handling element.</param>
        /// <param name="localY">Pointer Y relative to the handling element.</param>
        /// <param name="button">The DOM button number.</param>
        /// <param name="modifiers">The modifier keys held during the event.</param>
        public PointerMoveEvent(
            float clientX = 0,
            float clientY = 0,
            float localX = 0,
            float localY = 0,
            int button = -1,
            KeyModifiers modifiers = KeyModifiers.None)
            : base("pointermove", clientX, clientY, localX, localY, button, modifiers)
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<PointerMoveEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when a pointer contact ends.</summary>
    public class PointerUpEvent : PointerEvent
    {
        /// <summary>Creates a pointerup event.</summary>
        /// <param name="clientX">Pointer X in window CSS pixels.</param>
        /// <param name="clientY">Pointer Y in window CSS pixels.</param>
        /// <param name="localX">Pointer X relative to the handling element.</param>
        /// <param name="localY">Pointer Y relative to the handling element.</param>
        /// <param name="button">The DOM button number.</param>
        /// <param name="modifiers">The modifier keys held during the event.</param>
        public PointerUpEvent(
            float clientX = 0,
            float clientY = 0,
            float localX = 0,
            float localY = 0,
            int button = 0,
            KeyModifiers modifiers = KeyModifiers.None)
            : base("pointerup", clientX, clientY, localX, localY, button, modifiers)
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<PointerUpEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when the scroll position changes.</summary>
    public class ScrollEvent : Event
    {
        /// <summary>Creates a scroll event.</summary>
        /// <param name="deltaX">Horizontal distance in CSS pixels that was applied.</param>
        /// <param name="deltaY">Vertical distance in CSS pixels that was applied.</param>
        /// <param name="modifiers">The modifier keys held during the event.</param>
        public ScrollEvent(
            float deltaX = 0,
            float deltaY = 0,
            KeyModifiers modifiers = KeyModifiers.None)
            : base("scroll")
        {
            DeltaX = deltaX;
            DeltaY = deltaY;
            Modifiers = modifiers;
        }

        /// <summary>Gets the horizontal scroll delta in CSS pixels.</summary>
        public float DeltaX { get; }

        /// <summary>Gets the vertical scroll delta in CSS pixels.</summary>
        public float DeltaY { get; }

        /// <summary>Gets the modifier keys held during the event.</summary>
        public KeyModifiers Modifiers { get; }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<ScrollEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when an editable control's value changes.</summary>
    public class InputEvent : Event
    {
        /// <summary>Creates an input event.</summary>
        /// <param name="value">The control's current text.</param>
        public InputEvent(string value)
            : base("input")
        {
            Value = value ?? string.Empty;
        }

        /// <summary>Gets the control's current text.</summary>
        public string Value { get; }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<InputEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when a control's value is committed.</summary>
    public class ChangeEvent : Event
    {
        /// <summary>Creates a change event.</summary>
        /// <param name="value">The committed value.</param>
        public ChangeEvent(string value)
            : base("change")
        {
            Value = value ?? string.Empty;
        }

        /// <summary>Gets the committed value.</summary>
        public string Value { get; }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<ChangeEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when text is pasted into a control.</summary>
    public class PasteEvent : Event
    {
        /// <summary>Creates a paste event.</summary>
        /// <param name="text">The pasted text.</param>
        public PasteEvent(string text)
            : base("paste")
        {
            Text = text ?? string.Empty;
        }

        /// <summary>Gets the pasted text.</summary>
        public string Text { get; }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<PasteEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when a form is submitted.</summary>
    public class SubmitEvent : Event
    {
        /// <summary>Creates a submit event.</summary>
        public SubmitEvent()
            : base("submit")
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<SubmitEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when a key is pressed.</summary>
    public class KeyDownEvent : Event
    {
        /// <summary>Creates a keydown event.</summary>
        /// <param name="key">The key name, such as <c>Enter</c>.</param>
        /// <param name="modifiers">The modifier keys held during the event.</param>
        /// <param name="chord">
        /// The key name prefixed with its modifiers, for example
        /// <c>Ctrl+Shift+Z</c>.
        /// </param>
        public KeyDownEvent(
            string key,
            KeyModifiers modifiers = KeyModifiers.None,
            string? chord = null)
            : base("keydown")
        {
            Key = key ?? string.Empty;
            Modifiers = modifiers;
            Chord = chord ?? KeyChord.Format(Key, modifiers);
        }

        /// <summary>Gets the key name without modifiers, such as <c>Enter</c>.</summary>
        public string Key { get; }

        /// <summary>Gets the modifier keys held during the event.</summary>
        public KeyModifiers Modifiers { get; }

        /// <summary>Gets the key and modifiers in the same form as <c>OnKeyDown</c>.</summary>
        public string Chord { get; }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<KeyDownEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when an element receives focus.</summary>
    public class FocusEvent : Event
    {
        /// <summary>Creates a focus event.</summary>
        public FocusEvent()
            : base("focus")
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<FocusEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when an element loses focus.</summary>
    public class BlurEvent : Event
    {
        /// <summary>Creates a blur event.</summary>
        public BlurEvent()
            : base("blur")
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<BlurEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>Base class for media playback events.</summary>
    public class MediaEvent : Event
    {
        /// <summary>Creates a media event.</summary>
        /// <param name="type">The normalised event name.</param>
        /// <param name="time">The playback position in seconds, or zero.</param>
        public MediaEvent(string type, float time = 0)
            : base(type)
        {
            Time = time;
        }

        /// <summary>Gets the playback position in seconds.</summary>
        public float Time { get; }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<MediaEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when media starts playing.</summary>
    public class PlayEvent : MediaEvent
    {
        /// <summary>Creates a play event.</summary>
        /// <param name="time">The playback position in seconds.</param>
        public PlayEvent(float time = 0)
            : base("play", time)
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<PlayEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when media is paused.</summary>
    public class PauseEvent : MediaEvent
    {
        /// <summary>Creates a pause event.</summary>
        /// <param name="time">The playback position in seconds.</param>
        public PauseEvent(float time = 0)
            : base("pause", time)
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<PauseEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised when media reaches its end.</summary>
    public class EndedEvent : MediaEvent
    {
        /// <summary>Creates an ended event.</summary>
        /// <param name="time">The final playback position in seconds.</param>
        public EndedEvent(float time = 0)
            : base("ended", time)
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<EndedEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }

    /// <summary>An event raised periodically while media plays.</summary>
    public class TimeUpdateEvent : MediaEvent
    {
        /// <summary>Creates a timeupdate event.</summary>
        /// <param name="time">The current playback position in seconds.</param>
        public TimeUpdateEvent(float time = 0)
            : base("timeupdate", time)
        {
        }

        internal override bool TryInvoke(Delegate handler)
        {
            if (handler is Action<TimeUpdateEvent> action)
            {
                action(this);
                return true;
            }

            return base.TryInvoke(handler);
        }
    }
}
