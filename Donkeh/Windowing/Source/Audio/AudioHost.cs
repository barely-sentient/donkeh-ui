using Donkeh.UILab;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Components;
using SoundFlow.Interfaces;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace Donkeh.Windowing
{
    /// <summary>Creation options read from an <c>audio</c> element's attributes.</summary>
    internal readonly record struct AudioElementOptions(
        string Source,
        bool AutoPlay,
        bool Loop,
        bool Muted,
        float Volume,
        float PlaybackRate);

    /// <summary>
    /// Owns the shared SoundFlow playback device and one player per
    /// <c>audio</c> element in a window.
    /// </summary>
    /// <remarks>
    /// The engine, device and every player are created on a worker thread, so
    /// opening a slow file or network stream never stalls the frame loop. The
    /// public surface is thread-safe; state that the UI reads is published only
    /// after construction finishes.
    /// </remarks>
    internal sealed class AudioHost : IDisposable
    {
        private readonly object _sync = new();
        private readonly object _deviceLock = new();
        private readonly Dictionary<ElementNode, Track> _tracks = [];
        private readonly HashSet<ElementNode> _pending = [];
        private readonly AudioFormat _format = AudioFormat.DvdHq;
        private MiniAudioEngine? _engine;
        private AudioPlaybackDevice? _device;
        private bool _disposed;

        /// <summary>Gets whether at least one track is currently playing.</summary>
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
        /// Gets or schedules the player for an element. Returns
        /// <see langword="null"/> while creation is still running on a worker.
        /// </summary>
        public AudioPlayer? Ensure(ElementNode element, in AudioElementOptions options)
        {
            if (_disposed || string.IsNullOrWhiteSpace(options.Source))
            {
                return null;
            }

            lock (_sync)
            {
                if (_tracks.TryGetValue(element, out var existing))
                {
                    if (string.Equals(existing.Source, options.Source, StringComparison.Ordinal))
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
            Task.Run(() => CreateTrack(element, captured));
            return null;
        }

        /// <summary>Gets the player already associated with an element, if any.</summary>
        public AudioPlayer? GetPlayer(ElementNode element)
        {
            lock (_sync)
            {
                return _tracks.TryGetValue(element, out var track) ? track.Player : null;
            }
        }

        /// <summary>Gets and clears the natural-end flag for an element.</summary>
        public bool ConsumeEnded(ElementNode element)
        {
            lock (_sync)
            {
                if (!_tracks.TryGetValue(element, out var track) || !track.Ended)
                {
                    return false;
                }

                track.Ended = false;
                return true;
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

        private void CreateTrack(ElementNode element, AudioElementOptions options)
        {
            Track? track = null;

            try
            {
                EnsureDevice();

                var isNetworked = Uri.TryCreate(options.Source, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

                FileStream? stream = null;
                ISoundDataProvider provider;

                if (isNetworked)
                {
                    provider = new NetworkDataProvider(_engine!, _format, options.Source);
                }
                else
                {
                    stream = File.OpenRead(options.Source);
                    provider = new StreamDataProvider(_engine!, _format, stream);
                }

                var sound = new SoundPlayer(_engine!, _format, provider);
                _device!.MasterMixer.AddComponent(sound);
                track = new Track(options.Source, stream, provider, sound, new AudioPlayer(sound));
                sound.PlaybackEnded += (_, _) => track.Ended = true;
                Apply(track, options);
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
                        Task.Run(track.Dispose);
                    }

                    return;
                }

                _tracks[element] = track;
            }
        }

        private void EnsureDevice()
        {
            lock (_deviceLock)
            {
                _engine ??= new MiniAudioEngine();
                _device ??= _engine.InitializePlaybackDevice(null, _format, new MiniAudioDeviceConfig());

                if (!_device.IsRunning)
                {
                    _device.Start();
                }
            }
        }

        private static void Apply(Track track, in AudioElementOptions options)
        {
            // Only push declarative changes. Re-applying every frame would
            // overwrite volume/rate changes made through AudioPlayer.
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

            if (!_tracks.Remove(element, out var track))
            {
                return;
            }

            var device = _device;
            Task.Run(() =>
            {
                device?.MasterMixer.RemoveComponent(track.Sound);
                track.Dispose();
            });
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

            var device = _device;

            Task.Run(() =>
            {
                if (tracks is not null)
                {
                    foreach (var track in tracks)
                    {
                        device?.MasterMixer.RemoveComponent(track.Sound);
                        track.Dispose();
                    }
                }

                device?.Dispose();
                _engine?.Dispose();
            });
        }

        private sealed class Track : IDisposable
        {
            public Track(string source, FileStream? stream, ISoundDataProvider provider, SoundPlayer sound, AudioPlayer player)
            {
                Source = source;
                Stream = stream;
                Provider = provider;
                Sound = sound;
                Player = player;
            }

            public string Source { get; }

            public FileStream? Stream { get; }

            public ISoundDataProvider Provider { get; }

            public SoundPlayer Sound { get; }

            public AudioPlayer Player { get; }

            public AudioElementOptions Options { get; set; }

            public bool HasOptions { get; set; }

            public bool AutoPlayApplied { get; set; }

            public bool Ended { get; set; }

            public void Dispose()
            {
                Sound.Dispose();
                Provider.Dispose();
                Stream?.Dispose();
            }
        }
    }
}