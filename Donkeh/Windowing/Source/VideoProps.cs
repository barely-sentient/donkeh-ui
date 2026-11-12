using Donkeh.UILab;

namespace Donkeh.Windowing
{
    /// <summary>
    /// Props for a <c>video</c> element. Supports the same declarative
    /// playback options as <see cref="AudioProps"/> plus a typed
    /// <see cref="OnReady"/> callback for the window-side player.
    /// </summary>
    /// <example>
    /// <code>
    /// H("video", new VideoProps
    /// {
    ///     Src = "movie.mp4",
    ///     Controls = true,
    ///     OnTimeUpdate = seconds =&gt; { /* ... */ }
    /// });
    /// </code>
    /// </example>
    public sealed class VideoProps : Props
    {
        /// <summary>
        /// Gets or sets the callback invoked once the window has created the
        /// underlying player. Use it to drive playback from code.
        /// </summary>
        public Action<VideoPlayer>? OnReady
        {
            get => OnVideoReady as Action<VideoPlayer>;
            init => OnVideoReady = value;
        }
    }
}