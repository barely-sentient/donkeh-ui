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
    /// The engine and device are created lazily on the first audio element, so
    /// windows that never use audio pay no cost. All failures are contained:
    /// a missing device, codec or file simply leaves the element silent.
    /// </remarks>
    internal sealed class AudioHost : IDisposable
    {
        private readonly Dictionary<ElementNode, Track> _tracks = [];
        private readonly AudioFormat _format = AudioFormat.DvdHq;
        private MiniAudioEngine? _engine;
        private AudioPlaybackDevice? _device;
        private bool _failed;
        private bool _disposed;

        /// <summary>Gets whether at least one track is currently playing.</summary>
        public bool HasPlaying
        {
            get
            {
                foreach (var track in _tracks.Values)
                {
                    if (track.Player.IsPlaying)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Gets or creates the player for an element and applies the current
        /// declarative options.
        /// </summary>
        public AudioPlayer? Ensure(ElementNode element, in AudioElementOptions options)
        {
            if (_disposed || _failed || string.IsNullOrWhiteSpace(options.Source))
            {
                return null;
            }

            try
            {
                EnsureDevice();
            }
            catch
            {
                _failed = true;
                return null;
            }

            if (_tracks.TryGetValue(element, out var existing))
            {
                if (string.Equals(existing.Source, options.Source, StringComparison.Ordinal))
                {
                    Apply(existing, options);
                    return existing.Player;
                }

                Remove(element);
            }

            try
            {
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

                var track = new Track(options.Source, stream, provider, sound, new AudioPlayer(sound));
                sound.PlaybackEnded += (_, _) => track.Ended = true;
                _tracks[element] = track;
                Apply(track, options);
                return track.Player;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Gets the player already associated with an element, if any.</summary>
        public AudioPlayer? GetPlayer(ElementNode element)
        {
            return _tracks.TryGetValue(element, out var track) ? track.Player : null;
        }

        /// <summary>Gets and clears the natural-end flag for an element.</summary>
        public bool ConsumeEnded(ElementNode element)
        {
            if (!_tracks.TryGetValue(element, out var track) || !track.Ended)
            {
                return false;
            }

            track.Ended = false;
            return true;
        }

        /// <summary>Disposes players for elements that are no longer in the tree.</summary>
        public void Prune(HashSet<ElementNode> live)
        {
            List<ElementNode>? stale = null;

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
                    Remove(element);
                }
            }
        }

        private void EnsureDevice()
        {
            _engine ??= new MiniAudioEngine();
            _device ??= _engine.InitializePlaybackDevice(null, _format, new MiniAudioDeviceConfig());

            if (!_device.IsRunning)
            {
                _device.Start();
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

        private void Remove(ElementNode element)
        {
            if (!_tracks.Remove(element, out var track))
            {
                return;
            }

            _device?.MasterMixer.RemoveComponent(track.Sound);
            track.Dispose();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var track in _tracks.Values)
            {
                _device?.MasterMixer.RemoveComponent(track.Sound);
                track.Dispose();
            }

            _tracks.Clear();
            _device?.Dispose();
            _engine?.Dispose();
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