using SoundFlow.Components;
using SoundFlow.Enums;

namespace Donkeh.Windowing
{
    /// <summary>
    /// Code-facing control surface for an <c>audio</c> element. Wraps the
    /// SoundFlow player created by the window and exposes the usual HTML-style
    /// transport operations.
    /// </summary>
    /// <remarks>
    /// Instances are created by <see cref="IgniteWindow"/> and handed to
    /// <see cref="AudioProps.OnReady"/>. All members are safe to call from the
    /// frame thread; the actual decoding runs on the audio engine's thread.
    /// </remarks>
    public sealed class AudioPlayer
    {
        private readonly SoundPlayer _player;

        internal AudioPlayer(SoundPlayer player)
        {
            _player = player;
        }

        /// <summary>Gets whether playback is currently running.</summary>
        public bool IsPlaying => _player.State == PlaybackState.Playing;

        /// <summary>Gets the current playback position.</summary>
        public float Time => _player.Time;

        /// <summary>Gets the total duration of the loaded source.</summary>
        public float Duration => _player.Duration;

        /// <summary>Gets or sets the volume in the range 0 to 1.</summary>
        public float Volume
        {
            get => _player.Volume;
            set => _player.Volume = Math.Clamp(value, 0f, 1f);
        }

        /// <summary>Gets or sets whether playback should loop at the end.</summary>
        public bool IsLooping
        {
            get => _player.IsLooping;
            set => _player.IsLooping = value;
        }

        /// <summary>Gets or sets whether the player is muted.</summary>
        public bool IsMuted
        {
            get => _player.Mute;
            set => _player.Mute = value;
        }

        /// <summary>Gets or sets the playback rate, where one is normal speed.</summary>
        public float PlaybackRate
        {
            get => _player.PlaybackSpeed;
            set => _player.PlaybackSpeed = Math.Max(0.01f, value);
        }

        /// <summary>Starts or resumes playback.</summary>
        public void Play() => _player.Play();

        /// <summary>Pauses playback without resetting the position.</summary>
        public void Pause() => _player.Pause();

        /// <summary>Stops playback and resets the position.</summary>
        public void Stop() => _player.Stop();

        /// <summary>Seeks to a position in seconds.</summary>
        public bool Seek(float seconds) => _player.Seek(seconds);

        /// <summary>Seeks to an absolute position.</summary>
        public bool Seek(TimeSpan position) => _player.Seek(position, SeekOrigin.Begin);

        /// <summary>Raised when the source reaches its natural end.</summary>
        public event EventHandler<EventArgs>? PlaybackEnded
        {
            add => _player.PlaybackEnded += value;
            remove => _player.PlaybackEnded -= value;
        }
    }
}