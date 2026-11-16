using System.Diagnostics;
using System.Reflection;
using Donkeh.UILab;
using Donkeh.Windowing;

namespace Donkeh.HotReload;

/// <summary>
/// Watches the source files behind declared stylesheets and rebuilds the native
/// window cascade when one changes.
/// </summary>
/// <remarks>
/// <para>
/// Stylesheets are embedded at compile time, so the running window normally keeps
/// the CSS from the last build. This loader maps every
/// <see cref="Donkeh.UILab.StylesheetAttribute"/> declaration back to a file on disk, prefers
/// the file text over the embedded copy, and polls the files from the window's
/// frame loop. A change calls <see cref="DonkehWindow.SetStylesheet"/>, which
/// rebuilds the complete <c>DataTable</c>, style caches and layout engines, so the
/// next frame reflects the edited cascade.
/// </para>
/// <para>
/// Only files that can be found are watched. A published application has no source
/// files beside its assemblies, so creating the reloader yields
/// <see langword="null"/> and nothing is attached. Relative declarations are
/// resolved against optional <c>DonkehProjectDir</c> assembly metadata, then by
/// walking up from the declaring assembly and the app base directory, and finally
/// against the current working directory.
/// </para>
/// <para>
/// Polling runs on the frame thread. Changes are read only after they have been
/// stable for one poll interval, which avoids loading a partially written file.
/// </para>
/// </remarks>
internal sealed class CssHotReload
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(120);

    private readonly Entry[] _entries;
    private readonly string _embeddedCss;
    private long _lastPoll;
    private FileStamp?[] _applied;
    private FileStamp?[]? _pending;

    private CssHotReload(Entry[] entries)
    {
        _entries = entries;
        _embeddedCss = Join(entries);
        Css = _embeddedCss;
        _applied = Capture();
    }

    /// <summary>Gets the current merged CSS, preferring file contents.</summary>
    public string Css { get; private set; }

    /// <summary>
    /// Creates a reloader when at least one declared stylesheet resolves to a file.
    /// </summary>
    /// <returns>The reloader, or <see langword="null"/> when no source file exists.</returns>
    public static CssHotReload? TryCreate()
    {
        IReadOnlyList<StylesheetSource> sources;

        try
        {
            sources = Donkeh.UILab.Stylesheet.Sources();
        }
        catch
        {
            // A broken declaration must not stop the application from starting.
            return null;
        }

        if (sources.Count == 0)
        {
            return null;
        }

        var entries = new List<Entry>(sources.Count);
        var found = false;

        foreach (var source in sources)
        {
            var file = ResolveSourceFile(source.Assembly, source.Path);

            if (file is not null)
            {
                found = true;
            }

            entries.Add(new Entry(source.Path, file, source.Css));
        }

        if (!found)
        {
            return null;
        }

        var reload = new CssHotReload(entries.ToArray());
        reload.Refresh();
        return reload;
    }

    /// <summary>Subscribes to the window and applies any on-disk CSS at load time.</summary>
    /// <param name="window">The window to watch.</param>
    public void Attach(DonkehWindow window)
    {
        window.Loaded += OnLoaded;
        window.BeforeFrame += OnBeforeFrame;
        window.Closed += OnClosed;
    }

    private void OnLoaded(DonkehWindow window)
    {
        // The startup sheet came from the embedded resources. Replace it when the
        // source file had unsaved changes at launch.
        if (!string.Equals(Css, _embeddedCss, StringComparison.Ordinal))
        {
            window.SetStylesheet(Css);
        }
    }

    private void OnBeforeFrame(DonkehWindow window, double delta)
    {
        if (Poll())
        {
            window.SetStylesheet(Css);
        }
    }

    private void OnClosed(DonkehWindow window)
    {
        window.Loaded -= OnLoaded;
        window.BeforeFrame -= OnBeforeFrame;
        window.Closed -= OnClosed;
        _pending = null;
    }

    /// <summary>Checks the files and reloads them when a stable change is seen.</summary>
    /// <returns><see langword="true"/> when <see cref="Css"/> changed.</returns>
    private bool Poll()
    {
        var now = Stopwatch.GetTimestamp();

        if (_lastPoll != 0 && Stopwatch.GetElapsedTime(_lastPoll, now) < PollInterval)
        {
            return false;
        }

        _lastPoll = now;

        var current = Capture();

        if (StampsEqual(current, _applied))
        {
            _pending = null;
            return false;
        }

        // Wait for one more poll so an editor that truncates and rewrites a file
        // is not read halfway through the write.
        if (_pending is null || !StampsEqual(current, _pending))
        {
            _pending = current;
            return false;
        }

        _pending = null;
        return Refresh();
    }

    /// <summary>Reads every resolved file and rebuilds the merged CSS.</summary>
    private bool Refresh()
    {
        var parts = new string[_entries.Length];
        var changed = false;
        var readFailed = false;

        for (var i = 0; i < _entries.Length; i++)
        {
            var entry = _entries[i];

            if (entry.File is not { } file)
            {
                parts[i] = entry.Css;
                continue;
            }

            if (!File.Exists(file))
            {
                // Keep the last good text while the file is temporarily gone.
                parts[i] = entry.Css;
                continue;
            }

            try
            {
                var text = File.ReadAllText(file);

                if (!string.Equals(text, entry.Css, StringComparison.Ordinal))
                {
                    changed = true;
                    entry.Css = text;
                }
            }
            catch (IOException)
            {
                readFailed = true;
            }
            catch (UnauthorizedAccessException)
            {
                readFailed = true;
            }

            parts[i] = entry.Css;
        }

        if (!readFailed)
        {
            _applied = Capture();
        }

        if (!changed)
        {
            return false;
        }

        Css = string.Join("\n", parts);
        return true;
    }

    private FileStamp?[] Capture()
    {
        var stamps = new FileStamp?[_entries.Length];

        for (var i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].File is { } file)
            {
                stamps[i] = ReadStamp(file);
            }
        }

        return stamps;
    }

    private static FileStamp? ReadStamp(string file)
    {
        try
        {
            var info = new FileInfo(file);
            return info.Exists ? new FileStamp(info.LastWriteTimeUtc.Ticks, info.Length) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool StampsEqual(FileStamp?[] left, FileStamp?[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Finds the on-disk file for one declaration.</summary>
    private static string? ResolveSourceFile(Assembly assembly, string path)
    {
        var relative = path.Replace('\\', '/').TrimStart('/');

        if (relative.Length == 0)
        {
            return null;
        }

        var native = relative.Replace('/', Path.DirectorySeparatorChar);

        foreach (var root in CandidateRoots(assembly))
        {
            try
            {
                var candidate = Path.GetFullPath(Path.Combine(root, native));

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // An invalid declaration simply cannot resolve.
            }
            catch (NotSupportedException)
            {
            }
            catch (PathTooLongException)
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateRoots(Assembly assembly)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (metadata.Value is { Length: > 0 } value
                && (string.Equals(metadata.Key, "DonkehProjectDir", StringComparison.Ordinal)
                    || string.Equals(metadata.Key, "DonkehSourceRoot", StringComparison.Ordinal))
                && seen.Add(value))
            {
                yield return value;
            }
        }

        var location = assembly.Location;

        if (!string.IsNullOrEmpty(location)
            && Path.GetDirectoryName(location) is { Length: > 0 } assemblyDirectory)
        {
            foreach (var root in WalkUp(assemblyDirectory))
            {
                if (seen.Add(root))
                {
                    yield return root;
                }
            }
        }

        foreach (var root in WalkUp(AppContext.BaseDirectory))
        {
            if (seen.Add(root))
            {
                yield return root;
            }
        }

        if (seen.Add(Environment.CurrentDirectory))
        {
            yield return Environment.CurrentDirectory;
        }
    }

    private static IEnumerable<string> WalkUp(string directory)
    {
        var current = directory;

        for (var depth = 0; depth < 10 && !string.IsNullOrEmpty(current); depth++)
        {
            yield return current;
            current = Path.GetDirectoryName(current);
        }
    }

    private static string Join(Entry[] entries)
    {
        var parts = new string[entries.Length];

        for (var i = 0; i < entries.Length; i++)
        {
            parts[i] = entries[i].Css;
        }

        return string.Join("\n", parts);
    }

    private sealed class Entry
    {
        public Entry(string path, string? file, string css)
        {
            Path = path;
            File = file;
            Css = css;
        }

        public string Path { get; }

        public string? File { get; }

        public string Css { get; set; }
    }

    private readonly record struct FileStamp(long Ticks, long Length);
}
