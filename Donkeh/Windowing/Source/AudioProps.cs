using Donkeh.UILab;

namespace Donkeh.Windowing
{
    /// <summary>
    /// Props for an <c>audio</c> element. In addition to the standard
    /// <see cref="Props"/> attributes it exposes the player lifecycle events and
    /// an optional typed <see cref="OnReady"/> callback.
    /// </summary>
    /// <example>
    /// <code>
    /// H("audio", new AudioProps
    /// {
    ///     Src = "song.mp3",
    ///     Controls = true,
    ///     AutoPlay = true,
    ///     OnTimeUpdate = seconds =&gt; { /* ... */ }
    /// });
    /// </code>
    /// </example>
    public sealed class AudioProps : Props
    {
        /// <summary>
        /// Gets or sets the callback invoked once the window has created the
        /// underlying player. Use it to drive playback from code.
        /// </summary>
        public Action<AudioPlayer>? OnReady
        {
            get => OnAudioReady as Action<AudioPlayer>;
            init => OnAudioReady = value;
        }
    }
}