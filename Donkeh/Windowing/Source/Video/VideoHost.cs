using Donkeh.UILab;
using LibVLCSharp.Shared;

namespace Donkeh.Windowing
{
    /// <summary>Creation options read from a <c>video</c> element's attributes.</summary>
    internal readonly record struct VideoElementOptions(
        string Source,
        bool AutoPlay,
        bool Loop,
        bool Muted,
        float Volume,
        float PlaybackRate);

    /// <summary>
    /// Owns one LibVLC player per <c>video</c> element and uploads decoded
    /// frames into the window's image cache.
    /// </summary>
    /// <remarks>
    /// LibVLC initialization and every player are created on worker threads.
    /// Frame upload stays on the render thread because it needs the OpenGL
    /// context, but all decoding and I/O happens on LibVLC's own threads.
    /// </remarks>
    internal sealed class VideoHost : IDisposable
    {
        private static readonly object InitLock = new();
        private static LibVLC? _sharedLibVlc;
        private static bool _initFailed;

        private readonly object _sync = new();
        private readonly Dictionary<ElementNode, Track> _tracks = [];
        private readonly HashSet<ElementNode> _pending = [];
        private readonly byte[] _frameBuffer = new byte[VideoPlayer.FrameWidth * VideoPlayer.FrameHeight * 4];
        private bool _disposed;

        /// <summary>Gets whether at least one video is currently playing.</summary>
        public bool HasPlaying
        {
            get
            {
                lock (_sync)
                {
                    foreach (var track in _tracks.Values)
                    {
                        if (track.Player.IsPlaying)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Gets or schedules the player for a video element. Returns
        /// <see langword="null"/> while creation is still running on a worker.
        /// </summary>
        public VideoPlayer? Ensure(ElementNode element, in VideoElementOptions options, string textureKey)
        {
            if (_disposed || string.IsNullOrWhiteSpace(options.Source))
            {
                return null;
            }

            lock (_sync)
            {
                if (_tracks.TryGetValue(element, out var existing))
                {
                    if (string.Equals(existing.Options.Source, options.Source, StringComparison.Ordinal))
                    {
                        Apply(existing, options);
                        return existing.Player;
                    }

                    RemoveLocked(element);
                }

                if (_pending.Contains(element))
                {
                    return null;
                }

                _pending.Add(element);
            }

            var captured = options;
            Task.Run(() => CreateTrack(element, captured, textureKey));
            return null;
        }

        /// <summary>Gets the image-cache key that receives an element's frames.</summary>
        public string? GetTextureKey(ElementNode element)
        {
            lock (_sync)
            {
                return _tracks.TryGetValue(element, out var track) ? track.TextureKey : null;
            }
        }

        /// <summary>Gets the player associated with an element, if any.</summary>
        public VideoPlayer? GetPlayer(ElementNode element)
        {
            lock (_sync)
            {
                return _tracks.TryGetValue(element, out var track) ? track.Player : null;
            }
        }

        /// <summary>Uploads any newly decoded frames and services loop/end events.</summary>
        public void UpdateFrames(ImageCache images)
        {
            Track[] tracks;

            lock (_sync)
            {
                if (_tracks.Count == 0)
                {
                    return;
                }

                tracks = [.. _tracks.Values];
            }

            foreach (var track in tracks)
            {
                if (track.Player.ConsumeRestart())
                {
                    Task.Run(track.Player.Restart);
                }

                if (track.Player.TryCopyFrame(_frameBuffer, out var width, out var height))
                {
                    images.UpdateDynamicTexture(track.TextureKey, _frameBuffer, width, height);
                }
            }
        }

        /// <summary>Gets and clears the natural-end flag for an element.</summary>
        public bool ConsumeEnded(ElementNode element)
        {
            lock (_sync)
            {
                return _tracks.TryGetValue(element, out var track) && track.Player.ConsumeEnded();
            }
        }

        /// <summary>Disposes players for elements that are no longer in the tree.</summary>
        public void Prune(HashSet<ElementNode> live)
        {
            List<ElementNode>? stale = null;

            lock (_sync)
            {
                foreach (var element in _tracks.Keys)
                {
                    if (!live.Contains(element))
                    {
                        (stale ??= []).Add(element);
                    }
                }

                if (stale is not null)
                {
                    foreach (var element in stale)
                    {
                        RemoveLocked(element);
                    }
                }
            }
        }

        private void CreateTrack(ElementNode element, VideoElementOptions options, string textureKey)
        {
            Track? track = null;

            try
            {
                var libVlc = GetLibVlc();

                if (libVlc is not null)
                {
                    var player = new VideoPlayer(libVlc, options.Source, options.Loop);
                    track = new Track(textureKey, player);
                    Apply(track, options);
                }
            }
            catch
            {
                track = null;
            }

            lock (_sync)
            {
                var accepted = _pending.Remove(element);

                if (track is null || !accepted || _disposed)
                {
                    if (track is not null)
                    {
                        Task.Run(track.Player.Dispose);
                    }

                    return;
                }

                _tracks[element] = track;
            }
        }

        private static LibVLC? GetLibVlc()
        {
            lock (InitLock)
            {
                if (_initFailed)
                {
                    return null;
                }

                if (_sharedLibVlc is not null)
                {
                    return _sharedLibVlc;
                }

                try
                {
                    Core.Initialize();
                    _sharedLibVlc = new LibVLC("--no-video-title-show", "--quiet");
                    return _sharedLibVlc;
                }
                catch
                {
                    _initFailed = true;
                    return null;
                }
            }
        }

        private static void Apply(Track track, in VideoElementOptions options)
        {
            if (track.HasOptions && track.Options == options)
            {
                return;
            }

            track.Options = options;
            track.HasOptions = true;

            track.Player.Volume = Math.Clamp(options.Volume, 0f, 1f);
            track.Player.IsMuted = options.Muted;
            track.Player.IsLooping = options.Loop;
            track.Player.PlaybackRate = options.PlaybackRate > 0 ? options.PlaybackRate : 1f;

            if (options.AutoPlay && !track.AutoPlayApplied)
            {
                track.AutoPlayApplied = true;
                track.Player.Play();
            }
        }

        private void RemoveLocked(ElementNode element)
        {
            _pending.Remove(element);

            if (_tracks.Remove(element, out var track))
            {
                Task.Run(track.Player.Dispose);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            List<Track>? tracks = null;

            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _pending.Clear();
                tracks = [.. _tracks.Values];
                _tracks.Clear();
            }

            if (tracks is not null)
            {
                Task.Run(() =>
                {
                    foreach (var track in tracks)
                    {
                        track.Player.Dispose();
                    }
                });
            }
        }

        private sealed class Track
        {
            public Track(string textureKey, VideoPlayer player)
            {
                TextureKey = textureKey;
                Player = player;
            }

            public string TextureKey { get; }

            public VideoPlayer Player { get; }

            public VideoElementOptions Options { get; set; }

            public bool HasOptions { get; set; }

            public bool AutoPlayApplied { get; set; }
        }
    }
}