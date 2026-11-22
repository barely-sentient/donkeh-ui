#pragma warning disable CS1591

namespace Donkeh.UILab
{
    public partial class Props
    {
        public string? alt
        {
            get => Alt;
            init => Alt = value;
        }

        public Dictionary<string, string>? attributes
        {
            get => Attributes;
            init => Attributes = value;
        }

        public bool? autoplay
        {
            get => Autoplay;
            init => Autoplay = value;
        }

        public string? className
        {
            get => ClassName;
            init => ClassName = value;
        }

        public bool? controls
        {
            get => Controls;
            init => Controls = value;
        }

        public bool? disabled
        {
            get => Disabled;
            init => Disabled = value;
        }

        public bool? draggable
        {
            get => Draggable;
            init => Draggable = value;
        }

        public string? href
        {
            get => Href;
            init => Href = value;
        }

        public string? id
        {
            get => Id;
            init => Id = value;
        }

        public string? key
        {
            get => Key;
            init => Key = value;
        }

        public bool? loop
        {
            get => Loop;
            init => Loop = value;
        }

        public bool? muted
        {
            get => Muted;
            init => Muted = value;
        }

        public string? name
        {
            get => Name;
            init => Name = value;
        }

        public Delegate? onAudioReady
        {
            get => OnAudioReady;
            init => OnAudioReady = value;
        }

        public Action? onBlur
        {
            get => OnBlur;
            init => OnBlur = value;
        }

        public Action<BlurEvent>? onBlurEvent
        {
            get => OnBlurEvent;
            init => OnBlurEvent = value;
        }

        public Delegate? onCanvasContext
        {
            get => OnCanvasContext;
            init => OnCanvasContext = value;
        }

        public Delegate? onCanvasDraw
        {
            get => OnCanvasDraw;
            init => OnCanvasDraw = value;
        }

        public Action<string>? onChange
        {
            get => OnChange;
            init => OnChange = value;
        }

        public Action<ChangeEvent>? onChangeEvent
        {
            get => OnChangeEvent;
            init => OnChangeEvent = value;
        }

        public Action? onClick
        {
            get => OnClick;
            init => OnClick = value;
        }

        public Action<ClickEvent>? onClickEvent
        {
            get => OnClickEvent;
            init => OnClickEvent = value;
        }

        public Action<ContextMenuEvent>? onContextMenu
        {
            get => OnContextMenu;
            init => OnContextMenu = value;
        }

        public Action<DragEvent>? onDrag
        {
            get => OnDrag;
            init => OnDrag = value;
        }

        public Action<DragEvent>? onDragEnd
        {
            get => OnDragEnd;
            init => OnDragEnd = value;
        }

        public Action<DragEvent>? onDragEnter
        {
            get => OnDragEnter;
            init => OnDragEnter = value;
        }

        public Action<DragEvent>? onDragLeave
        {
            get => OnDragLeave;
            init => OnDragLeave = value;
        }

        public Action<DragEvent>? onDragOver
        {
            get => OnDragOver;
            init => OnDragOver = value;
        }

        public Action<DragEvent>? onDragStart
        {
            get => OnDragStart;
            init => OnDragStart = value;
        }

        public Action<DragEvent>? onDrop
        {
            get => OnDrop;
            init => OnDrop = value;
        }

        public Action? onEnded
        {
            get => OnEnded;
            init => OnEnded = value;
        }

        public Action<EndedEvent>? onEndedEvent
        {
            get => OnEndedEvent;
            init => OnEndedEvent = value;
        }

        public Action? onFocus
        {
            get => OnFocus;
            init => OnFocus = value;
        }

        public Action<FocusEvent>? onFocusEvent
        {
            get => OnFocusEvent;
            init => OnFocusEvent = value;
        }

        public Action<string>? onInput
        {
            get => OnInput;
            init => OnInput = value;
        }

        public Action<InputEvent>? onInputEvent
        {
            get => OnInputEvent;
            init => OnInputEvent = value;
        }

        public Action<string>? onKeyDown
        {
            get => OnKeyDown;
            init => OnKeyDown = value;
        }

        public Action<KeyDownEvent>? onKeyDownEvent
        {
            get => OnKeyDownEvent;
            init => OnKeyDownEvent = value;
        }

        public Action? onMouseDown
        {
            get => OnMouseDown;
            init => OnMouseDown = value;
        }

        public Action<MouseDownEvent>? onMouseDownEvent
        {
            get => OnMouseDownEvent;
            init => OnMouseDownEvent = value;
        }

        public Action? onMouseOut
        {
            get => OnMouseOut;
            init => OnMouseOut = value;
        }

        public Action<MouseOutEvent>? onMouseOutEvent
        {
            get => OnMouseOutEvent;
            init => OnMouseOutEvent = value;
        }

        public Action? onMouseOver
        {
            get => OnMouseOver;
            init => OnMouseOver = value;
        }

        public Action<MouseOverEvent>? onMouseOverEvent
        {
            get => OnMouseOverEvent;
            init => OnMouseOverEvent = value;
        }

        public Action? onMouseUp
        {
            get => OnMouseUp;
            init => OnMouseUp = value;
        }

        public Action<MouseUpEvent>? onMouseUpEvent
        {
            get => OnMouseUpEvent;
            init => OnMouseUpEvent = value;
        }

        public Action<string>? onPaste
        {
            get => OnPaste;
            init => OnPaste = value;
        }

        public Action<PasteEvent>? onPasteEvent
        {
            get => OnPasteEvent;
            init => OnPasteEvent = value;
        }

        public Action? onPause
        {
            get => OnPause;
            init => OnPause = value;
        }

        public Action<PauseEvent>? onPauseEvent
        {
            get => OnPauseEvent;
            init => OnPauseEvent = value;
        }

        public Action? onPlay
        {
            get => OnPlay;
            init => OnPlay = value;
        }

        public Action<PlayEvent>? onPlayEvent
        {
            get => OnPlayEvent;
            init => OnPlayEvent = value;
        }

        public Action<float, float>? onPointerDown
        {
            get => OnPointerDown;
            init => OnPointerDown = value;
        }

        public Action<PointerDownEvent>? onPointerDownEvent
        {
            get => OnPointerDownEvent;
            init => OnPointerDownEvent = value;
        }

        public Action<float, float>? onPointerMove
        {
            get => OnPointerMove;
            init => OnPointerMove = value;
        }

        public Action<PointerMoveEvent>? onPointerMoveEvent
        {
            get => OnPointerMoveEvent;
            init => OnPointerMoveEvent = value;
        }

        public Action<float, float>? onPointerUp
        {
            get => OnPointerUp;
            init => OnPointerUp = value;
        }

        public Action<PointerUpEvent>? onPointerUpEvent
        {
            get => OnPointerUpEvent;
            init => OnPointerUpEvent = value;
        }

        public Action<float, float>? onScroll
        {
            get => OnScroll;
            init => OnScroll = value;
        }

        public Action<ScrollEvent>? onScrollEvent
        {
            get => OnScrollEvent;
            init => OnScrollEvent = value;
        }

        public Action? onSubmit
        {
            get => OnSubmit;
            init => OnSubmit = value;
        }

        public Action<SubmitEvent>? onSubmitEvent
        {
            get => OnSubmitEvent;
            init => OnSubmitEvent = value;
        }

        public Action<float>? onTimeUpdate
        {
            get => OnTimeUpdate;
            init => OnTimeUpdate = value;
        }

        public Action<TimeUpdateEvent>? onTimeUpdateEvent
        {
            get => OnTimeUpdateEvent;
            init => OnTimeUpdateEvent = value;
        }

        public Delegate? onVideoReady
        {
            get => OnVideoReady;
            init => OnVideoReady = value;
        }

        public string? placeholder
        {
            get => Placeholder;
            init => Placeholder = value;
        }

        public float? playbackRate
        {
            get => PlaybackRate;
            init => PlaybackRate = value;
        }

        public string? rel
        {
            get => Rel;
            init => Rel = value;
        }

        public string? role
        {
            get => Role;
            init => Role = value;
        }

        public string? src
        {
            get => Src;
            init => Src = value;
        }

        public int? tabIndex
        {
            get => TabIndex;
            init => TabIndex = value;
        }

        public string? target
        {
            get => Target;
            init => Target = value;
        }

        public string? title
        {
            get => Title;
            init => Title = value;
        }

        public string? type
        {
            get => Type;
            init => Type = value;
        }

        public string? value
        {
            get => Value;
            init => Value = value;
        }

        public float? volume
        {
            get => Volume;
            init => Volume = value;
        }

    }
}

