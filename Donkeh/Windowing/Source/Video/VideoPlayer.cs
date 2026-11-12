using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace Donkeh.Windowing
{
    /// <summary>
    /// Code-facing control surface for a <c>video</c> element. Wraps a LibVLC
    /// <see cref="MediaPlayer"/> and owns the CPU frame buffer that the window
    /// uploads as a texture.
    /// </summary>
    public sealed unsafe class VideoPlayer : IDisposable
    {
        /// <summary>Width requested from LibVLC's video output callbacks.</summary>
        internal const int FrameWidth = 640;

        /// <summary>Height requested from LibVLC's video output callbacks.</summary>
        internal const int FrameHeight = 360;

        private readonly Media _media;
        private readonly MediaPlayer _player;
        private readonly MediaPlayer.LibVLCVideoLockCb _lockCallback;
        private readonly MediaPlayer.LibVLCVideoUnlockCb _unlockCallback;
        private readonly MediaPlayer.LibVLCVideoDisplayCb _displayCallback;
        private readonly object _frameLock = new();
        private readonly byte[] _frame = new byte[FrameWidth * FrameHeight * 4];
        private IntPtr _buffer;
        private bool _hasFrame;
        private bool _disposed;
        private bool _looping;
        private bool _restartRequested;
        private bool _ended;

        internal VideoPlayer(LibVLC libVlc, string source, bool loop)
        {
            _looping = loop;

            var isNetworked = Uri.TryCreate(source, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

            _media = isNetworked
                ? new Media(libVlc, uri!)
                : new Media(libVlc, source, FromType.FromPath);

            _player = new MediaPlayer(_media)
            {
                EnableKeyInput = false,
                EnableMouseInput = false
            };

            _buffer = Marshal.AllocHGlobal(FrameWidth * FrameHeight * 4);
            _lockCallback = OnLock;
            _unlockCallback = OnUnlock;
            _displayCallback = OnDisplay;

            _player.SetVideoCallbacks(_lockCallback, _unlockCallback, _displayCallback);
            _player.SetVideoFormat("RGBA", FrameWidth, FrameHeight, FrameWidth * 4);
            _player.EndReached += OnEndReached;
        }

        /// <summary>Gets whether playback is currently running.</summary>
        public bool IsPlaying => !_disposed && _player.State == VLCState.Playing;

        /// <summary>Gets the current playback position in seconds.</summary>
        public float Time => Math.Max(0, _player.Time) / 1000f;

        /// <summary>Gets the total duration in seconds.</summary>
        public float Duration => Math.Max(0, _player.Length) / 1000f;

        /// <summary>Gets or sets the volume in the range 0 to 1.</summary>
        public float Volume
        {
            get => Math.Clamp(_player.Volume / 100f, 0f, 1f);
            set => _player.Volume = (int)(Math.Clamp(value, 0f, 1f) * 100);
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
            get => _player.Rate;
            set => _player.SetRate(Math.Max(0.01f, value));
        }

        /// <summary>Gets or sets whether playback restarts at the end.</summary>
        public bool IsLooping
        {
            get => _looping;
            set => _looping = value;
        }

        /// <summary>Starts or resumes playback.</summary>
        public void Play() => _player.Play();

        /// <summary>Pauses playback without resetting the position.</summary>
        public void Pause() => _player.Pause();

        /// <summary>Stops playback and resets the position.</summary>
        public void Stop() => _player.Stop();

        /// <summary>Seeks to a position in seconds.</summary>
        public bool Seek(float seconds)
        {
            _player.Time = (long)(Math.Max(0, seconds) * 1000);
            return true;
        }

        /// <summary>Seeks to an absolute position.</summary>
        public bool Seek(TimeSpan position) => Seek((float)position.TotalSeconds);

        /// <summary>Raised when the source reaches its natural end.</summary>
        public event EventHandler<EventArgs>? PlaybackEnded;

        /// <summary>Copies the newest decoded frame, if one arrived since the last call.</summary>
        internal bool TryCopyFrame(Span<byte> destination, out int width, out int height)
        {
            width = FrameWidth;
            height = FrameHeight;

            lock (_frameLock)
            {
                if (!_hasFrame)
                {
                    return false;
                }

                _hasFrame = false;
                _frame.AsSpan().CopyTo(destination);
                return true;
            }
        }

        /// <summary>Consumes a pending natural-end notification.</summary>
        internal bool ConsumeEnded()
        {
            if (!_ended)
            {
                return false;
            }

            _ended = false;
            return true;
        }

        /// <summary>Consumes a pending loop restart request.</summary>
        internal bool ConsumeRestart()
        {
            if (!_restartRequested)
            {
                return false;
            }

            _restartRequested = false;
            return true;
        }

        /// <summary>Restarts the current media from the beginning.</summary>
        internal void Restart()
        {
            _player.Stop();
            _player.Play();
        }

        private void OnEndReached(object? sender, EventArgs e)
        {
            if (_looping)
            {
                _restartRequested = true;
            }
            else
            {
                _ended = true;
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
            }
        }

        private IntPtr OnLock(IntPtr opaque, IntPtr planes)
        {
            Marshal.WriteIntPtr(planes, 0, _buffer);
            return _buffer;
        }

        private void OnUnlock(IntPtr opaque, IntPtr picture, IntPtr planes)
        {
        }

        private void OnDisplay(IntPtr opaque, IntPtr picture)
        {
            lock (_frameLock)
            {
                Marshal.Copy(_buffer, _frame, 0, _frame.Length);
                _hasFrame = true;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _player.EndReached -= OnEndReached;
            _player.Stop();
            _player.Dispose();
            _media.Dispose();

            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
                _buffer = IntPtr.Zero;
            }
        }
    }
}