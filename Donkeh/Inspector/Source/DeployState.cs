using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Donkeh.Inspector;

/// <summary>State of one build target.</summary>
public enum DeployTargetState
{
    /// <summary>No build has run yet.</summary>
    Ready,

    /// <summary>The target is building.</summary>
    Building,

    /// <summary>The last build succeeded.</summary>
    Succeeded,

    /// <summary>The last build failed.</summary>
    Failed
}

/// <summary>Kind of build a target performs.</summary>
/// <remarks>
/// <see cref="Desktop"/> and <see cref="AndroidApk"/> are built by the
/// framework's default commands. <see cref="Custom"/> runs the delegate supplied
/// to <see cref="DeployTarget"/> so applications can add their own outputs without
/// changing the inspector.
/// </remarks>
public enum DeployTargetKind
{
    /// <summary>A framework-dependent desktop build copied to the output folder.</summary>
    Desktop,

    /// <summary>An Android build whose signed APK is copied to the output folder.</summary>
    AndroidApk,

    /// <summary>A browser build whose static site is copied to the output folder.</summary>
    WebAssembly,

    /// <summary>A custom build supplied by the application.</summary>
    Custom
}

/// <summary>Context passed to a custom build delegate.</summary>
public sealed class DeployContext
{
    /// <summary>Creates a build context.</summary>
    internal DeployContext(
        string projectRoot,
        string projectFile,
        string buildsRoot,
        string outputDirectory,
        string configuration,
        Action<string> log)
    {
        ProjectRoot = projectRoot;
        ProjectFile = projectFile;
        BuildsRoot = buildsRoot;
        OutputDirectory = outputDirectory;
        Configuration = configuration;
        Log = log;
    }

    /// <summary>Gets the directory containing the application project.</summary>
    public string ProjectRoot { get; }

    /// <summary>Gets the absolute path of the application project file.</summary>
    public string ProjectFile { get; }

    /// <summary>Gets the absolute path of the build output root.</summary>
    public string BuildsRoot { get; }

    /// <summary>Gets the absolute output directory for the target being built.</summary>
    public string OutputDirectory { get; }

    /// <summary>Gets the configuration requested for the build.</summary>
    public string Configuration { get; }

    /// <summary>Gets a sink that appends a line to the inspector build log.</summary>
    public Action<string> Log { get; }
}

/// <summary>Result returned by a custom build delegate.</summary>
/// <param name="Success">Whether the build completed successfully.</param>
/// <param name="ArtifactPath">
/// Optional path to the produced file or directory, shown in the inspector.
/// </param>
/// <param name="Message">
/// Optional failure or summary message. Shown in the output when non-empty.
/// </param>
public readonly record struct DeployResult(bool Success, string? ArtifactPath = null, string? Message = null);

/// <summary>
/// One entry in the inspector's Deploy tab. Instances are created by
/// <see cref="DeployState"/> for the default desktop and Android targets, or by an
/// application through <see cref="DeployState.AddTarget"/>.
/// </summary>
public sealed class DeployTarget
{
    /// <summary>Guards <see cref="_output"/>.</summary>
    private readonly object _outputGate = new();

    /// <summary>Incremental build output, trimmed to the most recent lines.</summary>
    private readonly StringBuilder _output = new();

    /// <summary>Current state; read by the panel thread.</summary>
    private volatile DeployTargetState _state;

    /// <summary>Backing field for <see cref="LastError"/>.</summary>
    private string? _lastError;

    /// <summary>Backing field for <see cref="ArtifactPath"/>.</summary>
    private string? _artifactPath;

    /// <summary>Creates a build target.</summary>
    /// <param name="id">Stable identifier used by the build buttons.</param>
    /// <param name="name">Display name.</param>
    /// <param name="description">Short description shown under the name.</param>
    /// <param name="framework">Target framework passed to <c>dotnet build</c>.</param>
    /// <param name="outputDirectory">Absolute output directory.</param>
    /// <param name="kind">Default build behavior.</param>
    /// <param name="build">
    /// Optional custom build used when <paramref name="kind"/> is
    /// <see cref="DeployTargetKind.Custom"/> or when the application wants to
    /// override the default command.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/>, <paramref name="name"/> or
    /// <paramref name="framework"/> is empty, or the output directory is empty.
    /// </exception>
    public DeployTarget(
        string id,
        string name,
        string description,
        string framework,
        string outputDirectory,
        DeployTargetKind kind,
        Func<DeployContext, CancellationToken, Task<DeployResult>>? build = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(framework);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Id = id;
        Name = name;
        Description = description;
        Framework = framework;
        OutputDirectory = outputDirectory;
        Kind = kind;
        BuildAction = build;
    }

    /// <summary>Gets the stable target identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the display name.</summary>
    public string Name { get; }

    /// <summary>Gets the description shown under the name.</summary>
    public string Description { get; }

    /// <summary>Gets the target framework passed to <c>dotnet build</c>.</summary>
    public string Framework { get; }

    /// <summary>Gets the absolute output directory.</summary>
    public string OutputDirectory { get; }

    /// <summary>Gets the default build behavior.</summary>
    public DeployTargetKind Kind { get; }

    /// <summary>Gets the optional custom build delegate.</summary>
    public Func<DeployContext, CancellationToken, Task<DeployResult>>? BuildAction { get; }

    /// <summary>Gets the current build state.</summary>
    public DeployTargetState State => _state;

    /// <summary>Gets the failure message from the last build, if any.</summary>
    public string? LastError => _lastError;

    /// <summary>Gets the path of the last produced artifact, if any.</summary>
    public string? ArtifactPath => _artifactPath;

    /// <summary>Gets the duration of the last build.</summary>
    public TimeSpan LastDuration { get; private set; }

    /// <summary>Gets the time the last build completed, if it has run.</summary>
    public DateTimeOffset? LastBuiltAt { get; private set; }

    /// <summary>Gets the captured build output, trimmed to the most recent lines.</summary>
    /// <remarks>
    /// The getter takes a short lock because the build thread appends while the
    /// panel thread renders.
    /// </remarks>
    public string Output
    {
        get
        {
            lock (_outputGate)
            {
                return _output.ToString();
            }
        }
    }

    /// <summary>Resets the target for a new build.</summary>
    internal void BeginBuild()
    {
        _state = DeployTargetState.Building;
        _lastError = null;
        _artifactPath = null;
        LastDuration = TimeSpan.Zero;
        LastBuiltAt = null;

        lock (_outputGate)
        {
            _output.Clear();
        }
    }

    /// <summary>Appends one line of build output.</summary>
    /// <param name="line">The line to append; line endings are removed.</param>
    internal void AppendOutput(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        lock (_outputGate)
        {
            _output.Append(line.TrimEnd('\r', '\n')).Append('\n');

            // Keep the panel cheap: a failed build can emit thousands of lines,
            // and only the tail is useful at a glance.
            const int limit = 16000;
            const int keep = 12000;

            if (_output.Length > limit)
            {
                _output.Remove(0, _output.Length - keep);
            }
        }
    }

    /// <summary>Records the result of a build.</summary>
    /// <param name="success">Whether the build succeeded.</param>
    /// <param name="artifactPath">Optional produced artifact.</param>
    /// <param name="message">Optional message, usually a failure reason.</param>
    /// <param name="duration">Elapsed build time.</param>
    internal void Complete(bool success, string? artifactPath, string? message, TimeSpan duration)
    {
        _state = success ? DeployTargetState.Succeeded : DeployTargetState.Failed;
        _artifactPath = artifactPath;
        _lastError = success ? null : message;
        LastDuration = duration;
        LastBuiltAt = DateTimeOffset.Now;

        if (!string.IsNullOrWhiteSpace(message))
        {
            AppendOutput(message);
        }
    }
}

/// <summary>
/// Builds the application from the inspector's Deploy tab.
/// </summary>
/// <remarks>
/// <para>
/// The service discovers the application project by walking up from the entry
/// assembly until it finds a project file, then creates one target per target
/// framework it understands. Outputs always go to
/// <c>{projectRoot}/builds/{target}</c>. Applications can replace the project or
/// add targets through <see cref="Configure"/> and <see cref="AddTarget"/>.
/// </para>
/// <para>
/// Builds run on a worker thread and never block the frame loop. State changes
/// raise <see cref="Changed"/>; the inspector host forwards that to
/// <see cref="DevToolsState.Refresh"/> so the panel can re-render.
/// </para>
/// </remarks>
public sealed class DeployState : IDisposable
{
    /// <summary>Matches TargetFramework and TargetFrameworks elements.</summary>
    private static readonly Regex FrameworkPattern = new(
        @"<TargetFrameworks?\b[^>]*>(?<list>[^<]+)</TargetFrameworks?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>All known targets, in display order.</summary>
    private readonly List<DeployTarget> _targets = [];

    /// <summary>Non-zero while any build is running.</summary>
    private int _busy;

    /// <summary>Timestamp of the last throttled change notification.</summary>
    private long _lastNotify;

    /// <summary>Creates the service and discovers the current application project.</summary>
    public DeployState()
    {
        Downloads = new DownloadServer(() => Notify(force: true));
        Discover();
    }

    /// <summary>Gets the loopback download server used by the Deploy tab.</summary>
    /// <remarks>
    /// Its root is kept in sync with <see cref="BuildsRoot"/>. Starting and
    /// stopping it raises <see cref="Changed"/> through the same path as builds.
    /// </remarks>
    public DownloadServer Downloads { get; }

    /// <summary>Raised when a target's state or output changes.</summary>
    /// <remarks>
    /// May fire from a build thread. Handlers must be thread-safe; the inspector
    /// uses it only to bump an atomic version counter.
    /// </remarks>
    public event Action? Changed;

    /// <summary>Gets or sets the configuration passed to <c>dotnet build</c>.</summary>
    /// <remarks>Defaults to <c>Release</c>. Change it before starting a build.</remarks>
    public string Configuration { get; set; } = "Release";

    /// <summary>Gets the known build targets in display order.</summary>
    public IReadOnlyList<DeployTarget> Targets => _targets;

    /// <summary>Gets a value indicating whether a build or build-all is running.</summary>
    public bool IsBuilding => Volatile.Read(ref _busy) != 0;

    /// <summary>Gets the most recently started target, if any.</summary>
    public DeployTarget? LastTarget { get; private set; }

    /// <summary>Gets the application project file, or <see langword="null"/> when undiscovered.</summary>
    public string? ProjectFile { get; private set; }

    /// <summary>Gets the directory containing the application project.</summary>
    public string? ProjectRoot { get; private set; }

    /// <summary>Gets the root output directory, normally <c>{projectRoot}/builds</c>.</summary>
    public string? BuildsRoot { get; private set; }

    /// <summary>
    /// Gets the folder that receives screenshots uploaded from the Deploy tab,
    /// normally <c>{projectRoot}/debug</c>.
    /// </summary>
    /// <remarks>
    /// The download server exposes this as its upload directory. Files land here
    /// unchanged so the application and external tooling can read them.
    /// </remarks>
    public string? DebugDirectory { get; private set; }

    /// <summary>
    /// Replaces the discovered project and output root and rebuilds the default
    /// target list.
    /// </summary>
    /// <param name="projectFile">Absolute path to the application project file.</param>
    /// <param name="buildsDirectory">
    /// Optional output root. Defaults to <c>{projectRoot}/builds</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="projectFile"/> is empty or does not exist.
    /// </exception>
    public void Configure(string projectFile, string? buildsDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFile);
        projectFile = Path.GetFullPath(projectFile);

        if (!File.Exists(projectFile))
        {
            throw new ArgumentException($"Project file '{projectFile}' was not found.", nameof(projectFile));
        }

        var projectRoot = Path.GetDirectoryName(projectFile)
            ?? throw new ArgumentException("The project file has no directory.", nameof(projectFile));

        ProjectFile = projectFile;
        ProjectRoot = projectRoot;
        BuildsRoot = string.IsNullOrWhiteSpace(buildsDirectory)
            ? Path.Combine(projectRoot, "builds")
            : Path.GetFullPath(buildsDirectory);

        DebugDirectory = Path.Combine(projectRoot, "debug");

        Downloads.RootDirectory = BuildsRoot;
        Downloads.UploadDirectory = DebugDirectory;
        RebuildTargets();
    }

    /// <summary>Adds a custom target to the Deploy tab.</summary>
    /// <param name="target">The target to add.</param>
    /// <exception cref="ArgumentException">
    /// A target with the same <see cref="DeployTarget.Id"/> already exists.
    /// </exception>
    public void AddTarget(DeployTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_targets.Any(x => string.Equals(x.Id, target.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"A build target with id '{target.Id}' already exists.", nameof(target));
        }

        _targets.Add(target);
        Notify(force: true);
    }

    /// <summary>Starts one target.</summary>
    /// <param name="targetId">The target to build.</param>
    /// <remarks>
    /// Ignores the request while another build is running. The method returns
    /// immediately; progress is reported through <see cref="Changed"/>.
    /// </remarks>
    public void Build(string targetId)
    {
        var target = _targets.FirstOrDefault(x =>
            string.Equals(x.Id, targetId, StringComparison.OrdinalIgnoreCase));

        if (target is null || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await BuildCoreAsync(target).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
                Notify(force: true);
            }
        });
    }

    /// <summary>Starts every target in list order.</summary>
    /// <remarks>
    /// Builds run sequentially because concurrent <c>dotnet build</c> processes
    /// share the same intermediate directories. A failure does not stop the
    /// remaining targets; each row reports its own result.
    /// </remarks>
    public void BuildAll()
    {
        if (_targets.Count == 0 || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var target in _targets.ToArray())
                {
                    await BuildCoreAsync(target).ConfigureAwait(false);
                }
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
                Notify(force: true);
            }
        });
    }

    /// <summary>Discovers the application project for this process.</summary>
    private void Discover()    {
        var projectFile = FindProjectFile();

        if (projectFile is null)
        {
            return;
        }

        try
        {
            Configure(projectFile);
        }
        catch (Exception)
        {
            // An undiscoverable project is a normal state for a published app;
            // the panel shows a hint instead of failing window construction.
            ProjectFile = null;
            ProjectRoot = null;
            BuildsRoot = null;
            DebugDirectory = null;
            _targets.Clear();
        }
    }

    /// <summary>Finds the project file that produced the running application.</summary>
    /// <returns>The absolute project path, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The search starts beside the entry assembly, then at the base directory and
    /// current directory, and walks up until a project file is found. A project
    /// whose name matches the entry assembly wins so a solution folder containing
    /// several projects still resolves the app.
    /// </remarks>
    private static string? FindProjectFile()
    {
        var entry = Assembly.GetEntryAssembly();
        var entryName = entry?.GetName().Name;

        var starts = new[]
        {
            TryGetDirectory(entry?.Location),
            TryGetDirectory(AppContext.BaseDirectory),
            TryGetDirectory(Directory.GetCurrentDirectory())
        };

        foreach (var start in starts)
        {
            if (start is null)
            {
                continue;
            }

            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                FileInfo[] projects;

                try
                {
                    projects = directory.GetFiles("*.csproj");
                }
                catch (Exception)
                {
                    break;
                }

                if (projects.Length == 0)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(entryName))
                {
                    var match = projects.FirstOrDefault(file =>
                        string.Equals(
                            Path.GetFileNameWithoutExtension(file.Name),
                            entryName,
                            StringComparison.OrdinalIgnoreCase));

                    if (match is not null)
                    {
                        return match.FullName;
                    }
                }

                if (projects.Length == 1)
                {
                    return projects[0].FullName;
                }
            }
        }

        return null;
    }

    /// <summary>Returns the containing directory of a file path, or null.</summary>
    /// <param name="path">File or directory path.</param>
    /// <returns>The directory, or <see langword="null"/> when unavailable.</returns>
    private static string? TryGetDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (Directory.Exists(path))
            {
                return path;
            }

            return Path.GetDirectoryName(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Builds the default target list from the project file.</summary>
    private void RebuildTargets()
    {
        _targets.Clear();

        if (ProjectFile is null || ProjectRoot is null || BuildsRoot is null)
        {
            return;
        }

        var (desktop, android, web) = ReadFrameworks(ProjectFile);

        if (desktop is not null)
        {
            _targets.Add(new DeployTarget(
                "desktop",
                "Desktop",
                $"{desktop} · {Configuration}",
                desktop,
                Path.Combine(BuildsRoot, "desktop"),
                DeployTargetKind.Desktop));
        }

        if (android is not null)
        {
            _targets.Add(new DeployTarget(
                "android",
                "Android",
                $"{android} · {Configuration} · APK",
                android,
                Path.Combine(BuildsRoot, "android"),
                DeployTargetKind.AndroidApk));
        }

        if (web is not null)
        {
            _targets.Add(new DeployTarget(
                "wasm",
                "WebAssembly",
                $"{web} · {Configuration} · WASM",
                web,
                Path.Combine(BuildsRoot, "wasm"),
                DeployTargetKind.WebAssembly));
        }

        Notify(force: true);
    }

    /// <summary>Reads the desktop, Android and WebAssembly target frameworks from a project.</summary>
    /// <param name="projectFile">Project file to inspect.</param>
    /// <returns>
    /// The first desktop, Android and browser framework, any of which may be
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Explicit <c>TargetFramework(s)</c> elements are honoured, and the Donkeh
    /// opt-in properties add the matching framework when it is not listed
    /// directly.
    /// </remarks>
    private static (string? Desktop, string? Android, string? Web) ReadFrameworks(string projectFile)
    {
        try
        {
            var text = File.ReadAllText(projectFile);
            var frameworks = new List<string>();

            foreach (Match match in FrameworkPattern.Matches(text))
            {
                foreach (var value in match.Groups["list"].Value.Split(';'))
                {
                    var framework = value.Trim();

                    if (framework.Length > 0
                        && !framework.Contains("$(", StringComparison.Ordinal))
                    {
                        frameworks.Add(framework);
                    }
                }
            }

            AddOptInFramework(text, "DonkehDesktop", "net10.0", frameworks);
            AddOptInFramework(text, "DonkehAndroid", "net10.0-android", frameworks);
            AddOptInFramework(text, "DonkehWeb", "net10.0-browser", frameworks);

            if (frameworks.Count == 0)
            {
                return ("net10.0", null, null);
            }

            var android = frameworks.FirstOrDefault(f =>
                f.Contains("-android", StringComparison.OrdinalIgnoreCase));

            var web = frameworks.FirstOrDefault(f =>
                f.Contains("-browser", StringComparison.OrdinalIgnoreCase));

            var desktop = frameworks.FirstOrDefault(f =>
                !f.Contains("-android", StringComparison.OrdinalIgnoreCase)
                && !f.Contains("-ios", StringComparison.OrdinalIgnoreCase)
                && !f.Contains("-maccatalyst", StringComparison.OrdinalIgnoreCase)
                && !f.Contains("-tvos", StringComparison.OrdinalIgnoreCase)
                && !f.Contains("-browser", StringComparison.OrdinalIgnoreCase));

            return (desktop, android, web);
        }
        catch (Exception)
        {
            return (null, null, null);
        }
    }

    /// <summary>Adds a framework when a Donkeh opt-in property is enabled.</summary>
    /// <param name="projectText">Project file text.</param>
    /// <param name="propertyName">Opt-in property, such as <c>DonkehWeb</c>.</param>
    /// <param name="framework">Framework the property enables.</param>
    /// <param name="frameworks">Collected target frameworks.</param>
    private static void AddOptInFramework(
        string projectText,
        string propertyName,
        string framework,
        List<string> frameworks)
    {
        var enabled = Regex.IsMatch(
            projectText,
            $@"<{propertyName}>\s*true\s*</{propertyName}>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (enabled && !frameworks.Contains(framework, StringComparer.OrdinalIgnoreCase))
        {
            frameworks.Add(framework);
        }
    }

    /// <summary>Runs one target to completion and records its result.</summary>
    /// <param name="target">The target to build.</param>
    /// <returns>A task that completes when the build has finished.</returns>
    private async Task BuildCoreAsync(DeployTarget target)
    {
        LastTarget = target;
        target.BeginBuild();
        Notify(force: true);

        var started = Stopwatch.StartNew();

        try
        {
            Directory.CreateDirectory(target.OutputDirectory);

            var result = target.Kind switch
            {
                DeployTargetKind.Desktop => await RunDesktopAsync(target).ConfigureAwait(false),
                DeployTargetKind.AndroidApk => await RunAndroidAsync(target).ConfigureAwait(false),
                DeployTargetKind.WebAssembly => await RunWebAssemblyAsync(target).ConfigureAwait(false),
                _ => await RunCustomAsync(target).ConfigureAwait(false)
            };

            target.Complete(result.Success, result.ArtifactPath, result.Message, started.Elapsed);
        }
        catch (Exception ex)
        {
            target.Complete(false, null, ex.Message, started.Elapsed);
        }

        Notify(force: true);
    }

    /// <summary>Builds the desktop target into its output directory.</summary>
    /// <param name="target">The target being built.</param>
    /// <returns>The build result.</returns>
    private async Task<DeployResult> RunDesktopAsync(DeployTarget target)
    {
        var exitCode = await RunDotnetAsync(
            target,
            [
                "build",
                ProjectFile!,
                "-c",
                Configuration,
                "-f",
                target.Framework,
                "-o",
                target.OutputDirectory,
                "--nologo"
            ]).ConfigureAwait(false);

        return exitCode == 0
            ? new DeployResult(true, target.OutputDirectory)
            : new DeployResult(false, null, $"dotnet build exited with code {exitCode}.");
    }

    /// <summary>Builds the Android target and copies the signed APK.</summary>
    /// <param name="target">The target being built.</param>
    /// <returns>The build result.</returns>
    private async Task<DeployResult> RunAndroidAsync(DeployTarget target)
    {
        var exitCode = await RunDotnetAsync(
            target,
            [
                "build",
                ProjectFile!,
                "-c",
                Configuration,
                "-f",
                target.Framework,
                "-p:AndroidPackageFormat=apk",
                "-p:EmbedAssembliesIntoApk=true",
                "--nologo"
            ]).ConfigureAwait(false);

        if (exitCode != 0)
        {
            return new DeployResult(false, null, $"dotnet build exited with code {exitCode}.");
        }

        var binDirectory = Path.Combine(ProjectRoot!, "bin", Configuration, target.Framework);

        if (!Directory.Exists(binDirectory))
        {
            return new DeployResult(false, null, $"The APK output directory was not created: {binDirectory}");
        }

        var apk = Directory.GetFiles(binDirectory, "*.apk", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => path.Contains("Signed", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (apk is null)
        {
            return new DeployResult(false, null, $"No APK was produced in {binDirectory}.");
        }

        var destination = Path.Combine(target.OutputDirectory, Path.GetFileName(apk));
        File.Copy(apk, destination, overwrite: true);
        return new DeployResult(true, destination);
    }

    /// <summary>Publishes the WebAssembly target into the project's deploy folder.</summary>
    /// <param name="target">The target being built.</param>
    /// <returns>The build result, whose artifact is the static site root.</returns>
    private async Task<DeployResult> RunWebAssemblyAsync(DeployTarget target)
    {
        var exitCode = await RunDotnetAsync(
            target,
            [
                "publish",
                ProjectFile!,
                "-c",
                Configuration,
                "-f",
                target.Framework,
                "-o",
                target.OutputDirectory,
                "--nologo"
            ]).ConfigureAwait(false);

        if (exitCode != 0)
        {
            return new DeployResult(false, null, $"dotnet publish exited with code {exitCode}.");
        }

        var index = Path.Combine(target.OutputDirectory, "index.html");

        return File.Exists(index)
            ? new DeployResult(true, target.OutputDirectory)
            : new DeployResult(false, null, $"The WebAssembly host page was not produced: {index}");
    }

    /// <summary>Runs a custom target's delegate.</summary>
    /// <param name="target">The target being built.</param>
    /// <returns>The build result.</returns>
    private async Task<DeployResult> RunCustomAsync(DeployTarget target)
    {
        if (target.BuildAction is null)
        {
            return new DeployResult(false, null, "The custom target has no build action.");
        }

        var context = new DeployContext(
            ProjectRoot!,
            ProjectFile!,
            BuildsRoot!,
            target.OutputDirectory,
            Configuration,
            target.AppendOutput);

        return await target.BuildAction(context, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Runs dotnet and streams its output into the target log.</summary>
    /// <param name="target">Target receiving the output.</param>
    /// <param name="arguments">Arguments passed to <c>dotnet</c>.</param>
    /// <returns>The process exit code, or -1 when the process could not start.</returns>
    private async Task<int> RunDotnetAsync(DeployTarget target, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = ProjectRoot!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => OnOutput(target, e.Data);
        process.ErrorDataReceived += (_, e) => OnOutput(target, e.Data);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            target.AppendOutput($"Could not start dotnet: {ex.Message}");
            return -1;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync().ConfigureAwait(false);

        // WaitForExitAsync can complete before the asynchronous output readers
        // have drained; the parameterless overload flushes them.
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>Forwards one process line into the target log.</summary>
    /// <param name="target">Target receiving the output.</param>
    /// <param name="line">Line emitted by the process, or <see langword="null"/>.</param>
    private void OnOutput(DeployTarget target, string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        target.AppendOutput(line);
        Notify();
    }

    /// <summary>Raises <see cref="Changed"/>, optionally bypassing throttling.</summary>
    /// <param name="force">True to notify immediately.</param>
    /// <remarks>
    /// Build output arrives much faster than the panel can usefully redraw; a
    /// 200 ms window keeps a noisy build from monopolising the frame loop.
    /// </remarks>
    private void Notify(bool force = false)
    {
        if (!force)
        {
            var now = Environment.TickCount64;

            if (now - Interlocked.Read(ref _lastNotify) < 200)
            {
                return;
            }

            Interlocked.Exchange(ref _lastNotify, now);
        }
        else
        {
            Interlocked.Exchange(ref _lastNotify, Environment.TickCount64);
        }

        Changed?.Invoke();
    }

    /// <inheritdoc/>
    /// <remarks>Stops the download server. In-flight builds continue and are not cancelled.</remarks>
    public void Dispose() => Downloads.Dispose();
}
