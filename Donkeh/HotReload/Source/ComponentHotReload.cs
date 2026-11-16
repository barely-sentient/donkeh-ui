using System.Diagnostics;
using System.Reflection;
using Donkeh.UILab;
using Donkeh.Windowing;

namespace Donkeh.HotReload;

/// <summary>
/// Watches component source files and asks the window to re-render after a C#
/// hot reload patch.
/// </summary>
/// <remarks>
/// <para>
/// The .NET Hot Reload agent patches method bodies in the running process but
/// never notifies a console or desktop application that it happened. A retained
/// Donkeh tree therefore has no reason to call <see cref="Component.Render"/>
/// again. This watcher treats a stable batch of <c>.cs</c> edits as the signal:
/// because the agent applies a method-body delta shortly after the file save, a
/// short debounce lets the patch land before the window re-renders every mounted
/// component. A second refresh follows shortly afterwards to cover a slower build.
/// </para>
/// <para>
/// The watcher only attaches when it can find project directories for the entry
/// assembly and for assemblies that declare stylesheets. Generated files under
/// <c>bin</c>, <c>obj</c>, <c>.git</c>, <c>.vs</c> and <c>builds</c> are ignored.
/// Unsupported edits make <c>dotnet watch</c> restart the application, so this
/// path is only responsible for supported method-body patches.
/// </para>
/// </remarks>
internal sealed class ComponentHotReload
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan SecondRefreshDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly List<FileSystemWatcher> _watchers = [];
    private long _lastActivity;
    private long _secondRefreshAt;
    private long _lastPoll;

    private ComponentHotReload(IEnumerable<FileSystemWatcher> watchers)
    {
        _watchers.AddRange(watchers);
    }

    /// <summary>
    /// Creates a watcher when at least one component project has source files.
    /// </summary>
    /// <returns>The watcher, or <see langword="null"/> when no source root exists.</returns>
    public static ComponentHotReload? TryCreate()
    {
        var roots = FindProjectRoots();

        if (roots.Count == 0)
        {
            return null;
        }

        var watchers = new List<FileSystemWatcher>();

        foreach (var root in roots)
        {
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    Filter = "*.cs",
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite
                        | NotifyFilters.FileName
                        | NotifyFilters.DirectoryName,
                    InternalBufferSize = 64 * 1024
                };

                watchers.Add(watcher);
            }
            catch (ArgumentException)
            {
                // A stale or inaccessible root simply does not contribute a watcher.
            }
            catch (IOException)
            {
            }
        }

        if (watchers.Count == 0)
        {
            return null;
        }

        var reload = new ComponentHotReload(watchers);

        foreach (var watcher in watchers)
        {
            watcher.Changed += reload.OnFileChanged;
            watcher.Created += reload.OnFileChanged;
            watcher.Deleted += reload.OnFileChanged;
            watcher.Renamed += reload.OnFileRenamed;
            watcher.Error += reload.OnWatcherError;
            watcher.EnableRaisingEvents = true;
        }

        return reload;
    }

    /// <summary>Subscribes the reloader to a window's frame loop.</summary>
    /// <param name="window">The window to refresh.</param>
    public void Attach(DonkehWindow window)
    {
        window.BeforeFrame += OnBeforeFrame;
        window.Closed += OnClosed;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs args)
    {
        if (IsSourceFile(args.FullPath))
        {
            MarkActivity();
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs args)
    {
        if (IsSourceFile(args.FullPath))
        {
            MarkActivity();
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        // A buffer overflow can hide individual events. Treat it as one change;
        // the window refresh is harmless when no code actually changed.
        MarkActivity();
    }

    private void OnBeforeFrame(DonkehWindow window, double delta)
    {
        var now = Stopwatch.GetTimestamp();

        if (_lastPoll != 0 && Stopwatch.GetElapsedTime(_lastPoll, now) < PollInterval)
        {
            return;
        }

        _lastPoll = now;

        var activity = Interlocked.Read(ref _lastActivity);

        if (activity != 0 && Stopwatch.GetElapsedTime(activity, now) >= Debounce)
        {
            Interlocked.Exchange(ref _lastActivity, 0);
            window.RefreshComponents();
            _secondRefreshAt = now + (long)(SecondRefreshDelay.TotalSeconds * Stopwatch.Frequency);
            return;
        }

        if (_secondRefreshAt != 0 && now >= _secondRefreshAt)
        {
            _secondRefreshAt = 0;
            window.RefreshComponents();
        }
    }

    private void OnClosed(DonkehWindow window)
    {
        window.BeforeFrame -= OnBeforeFrame;
        window.Closed -= OnClosed;

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private void MarkActivity() =>
        Interlocked.Exchange(ref _lastActivity, Stopwatch.GetTimestamp());

    private static bool IsSourceFile(string path)
    {
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !ContainsSegment(path, "bin")
            && !ContainsSegment(path, "obj")
            && !ContainsSegment(path, ".git")
            && !ContainsSegment(path, ".vs")
            && !ContainsSegment(path, "builds");
    }

    private static bool ContainsSegment(string path, string segment)
    {
        var marker = Path.DirectorySeparatorChar + segment + Path.DirectorySeparatorChar;
        return path.Contains(marker, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(Path.DirectorySeparatorChar + segment, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> FindProjectRoots()
    {
        var assemblies = new HashSet<Assembly>();
        var entry = Assembly.GetEntryAssembly();

        if (entry is not null)
        {
            assemblies.Add(entry);
        }

        try
        {
            foreach (var source in Donkeh.UILab.Stylesheet.Sources())
            {
                assemblies.Add(source.Assembly);
            }
        }
        catch
        {
            // Broken stylesheet metadata must not prevent component reload.
        }

        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblies)
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(assembly.Location);

            for (var depth = 0; depth < 10 && !string.IsNullOrEmpty(directory); depth++)
            {
                if (HasFile(directory, "*.csproj"))
                {
                    if (seen.Add(directory))
                    {
                        roots.Add(directory);
                    }

                    break;
                }

                directory = Path.GetDirectoryName(directory);
            }
        }

        // Framework projects keep the library under src/ and the executable under
        // test-container/. Watching the solution directory covers both without
        // needing to load the library assembly first.
        foreach (var root in roots.ToArray())
        {
            var parent = Path.GetDirectoryName(root);

            if (parent is not null
                && HasFile(parent, "*.sln")
                && seen.Add(parent))
            {
                roots.Add(parent);
            }
        }

        // A nested watcher would receive the same events twice. Keep only the
        // outermost root when one project directory contains another.
        for (var i = roots.Count - 1; i >= 0; i--)
        {
            var candidate = roots[i] + Path.DirectorySeparatorChar;

            if (roots.Any(other => !string.Equals(other, roots[i], StringComparison.OrdinalIgnoreCase)
                && candidate.StartsWith(other + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                roots.RemoveAt(i);
            }
        }

        return roots;
    }

    private static bool HasFile(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern).Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
