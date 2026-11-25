using Donkeh.Llm;

namespace Donkeh.Cli;

/// <summary>Command-line entry point for the Donkeh developer tool.</summary>
public static partial class Cli
{
    private static readonly string Version =
        typeof(Cli).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>Runs the CLI and returns a process exit code.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Zero on success, one on a user-facing failure.</returns>
    public static int Run(string[] args)
    {
        var json = args.Contains("--json", StringComparer.OrdinalIgnoreCase);

        try
        {
            return RunCore(args);
        }
        catch (LlmException exception)
        {
            if (json)
            {
                WriteJsonError(exception.Code, exception.Message, exception.Hint);
            }
            else
            {
                Console.Error.WriteLine($"error: {exception.Message}");

                if (!string.IsNullOrWhiteSpace(exception.Hint))
                {
                    Console.Error.WriteLine($"hint: {exception.Hint}");
                }
            }

            return 1;
        }
        catch (CliException exception)
        {
            if (json)
            {
                WriteJsonError("CLI0001", exception.Message, null);
            }
            else
            {
                Console.Error.WriteLine($"error: {exception.Message}");
            }

            return 1;
        }
        catch (Exception exception)
        {
            if (json)
            {
                WriteJsonError("CLI9999", exception.Message, null);
            }
            else
            {
                Console.Error.WriteLine($"error: {exception.Message}");
            }

            return 1;
        }
    }

    private static void WriteJsonError(string code, string message, string? hint)
    {
        Console.WriteLine(LlmJson.Serialize(new
        {
            error = new
            {
                code,
                message,
                hint
            }
        }));
    }

    private static int RunCore(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        if (IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        if (args[0] is "--version" or "-v")
        {
            Console.WriteLine($"donkeh {Version}");
            return 0;
        }

        if (IsLlmCommand(args[0]))
        {
            return RunLlmCommand(args);
        }

        if (string.Equals(args[0], "upgrade", StringComparison.OrdinalIgnoreCase))
        {
            return RunUpgrade(args[1..]);
        }

        if (string.Equals(args[0], "use-local", StringComparison.OrdinalIgnoreCase))
        {
            return RunUseLocal(args[1..]);
        }

        if (string.Equals(args[0], "use-nuget", StringComparison.OrdinalIgnoreCase))
        {
            return RunUseNuget(args[1..]);
        }

        if (string.Equals(args[0], "enable-feature", StringComparison.OrdinalIgnoreCase))
        {
            return RunEnableFeature(args[1..]);
        }

        if (string.Equals(args[0], "watch", StringComparison.OrdinalIgnoreCase))
        {
            return RunWatch(args[1..]);
        }

        if (string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
        {
            return RunRun(args[1..]);
        }

        if (string.Equals(args[0], "enable-target", StringComparison.OrdinalIgnoreCase))
        {
            return RunEnableTarget(args[1..]);
        }

        if (string.Equals(args[0], "add", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2 || !string.Equals(args[1], "component", StringComparison.OrdinalIgnoreCase))
            {
                throw new CliException("Expected 'component'. Run 'donkeh add --help'.");
            }

            return RunNewComponent(args[2..]);
        }

        if (!string.Equals(args[0], "new", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException($"Unknown command '{args[0]}'. Run 'donkeh --help'.");
        }

        if (args.Length < 2)
        {
            throw new CliException("Expected 'project' or 'component'. Run 'donkeh new --help'.");
        }

        var rest = args[2..];

        return args[1].ToLowerInvariant() switch
        {
            "project" => RunNewProject(rest),
            "component" => RunNewComponent(rest),
            _ when IsHelp(args[1]) => PrintNewHelp(),
            _ => throw new CliException(
                $"Unknown command 'new {args[1]}'. Expected 'project' or 'component'.")
        };
    }

    private static int RunNewProject(string[] args)
    {
        var (csx, filtered) = ParseCsxArguments(args);
        var arguments = ParsedArguments.Parse(filtered, ["name", "framework", "type"]);

        if (arguments.Help)
        {
            PrintProjectHelp();
            return 0;
        }

        if (arguments.Positionals.Count > 1)
        {
            throw new CliException("Expected at most one project directory name.");
        }

        var kind = ParseProjectKind(arguments.Value("type"));
        var target = Environment.CurrentDirectory;
        string? name = arguments.Value("name");
        string? directoryName = null;

        if (arguments.Positionals.Count == 1)
        {
            directoryName = arguments.Positionals[0];
            target = Path.GetFullPath(Path.Combine(target, directoryName));

            if (directoryName is not "." and not "..")
            {
                name ??= directoryName;
            }
        }

        var framework = FrameworkLocator.Find(arguments.Value("framework"));
        var files = ProjectGenerator.Generate(
            target,
            name,
            framework,
            arguments.Force,
            kind,
            csx ?? true);

        Console.WriteLine($"Created Donkeh project in {Path.GetFullPath(target)}");

        if (kind == ProjectKind.Framework)
        {
            Console.WriteLine("  Component library plus test container.");
        }

        if (framework is null)
        {
            Console.WriteLine("  Using published NuGet packages.");
        }

        foreach (var file in files)
        {
            Console.WriteLine($"  {Path.GetFileName(file)}");
        }

        Console.WriteLine();
        Console.WriteLine("Next:");

        if (directoryName is not null and not "." and not "..")
        {
            Console.WriteLine($"  cd {directoryName}");
        }

        Console.WriteLine("  donkeh run");
        return 0;
    }

    private static ProjectKind ParseProjectKind(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "application" or "app" => ProjectKind.Application,
            "framework" or "library" or "lib" => ProjectKind.Framework,
            _ => throw new CliException(
                $"Unknown project type '{value}'. Expected 'application' or 'framework'.")
        };
    }

    private static int RunEnableTarget(string[] args)
    {
        var arguments = ParsedArguments.Parse(args, ["project"]);

        if (arguments.Help)
        {
            PrintEnableTargetHelp();
            return 0;
        }

        if (arguments.Positionals.Count != 1)
        {
            throw new CliException(
                "Expected a target: desktop, android or web. " +
                "Example: donkeh enable-target android");
        }

        var target = TargetEnabler.NormalizeTarget(arguments.Positionals[0]);
        var projectFile = TargetEnabler.Enable(target, arguments.Value("project"));

        Console.WriteLine($"{target} target ready in {projectFile}");
        Console.WriteLine("  restoring packages");
        UpgradeService.RunDotnet(
            ["restore", projectFile],
            Path.GetDirectoryName(projectFile));

        Console.WriteLine();
        Console.WriteLine("Next:");

        switch (target)
        {
            case "android":
                Console.WriteLine("  dotnet build -f net10.0-android --no-restore");
                break;
            case "web":
                Console.WriteLine("  dotnet build -f net10.0-browser --no-restore");
                break;
            default:
                Console.WriteLine("  donkeh run");
                break;
        }

        return 0;
    }

    private static int RunUpgrade(string[] args)
    {
        var arguments = ParsedArguments.Parse(args, ["version", "project"]);

        if (arguments.Help)
        {
            PrintUpgradeHelp();
            return 0;
        }

        if (arguments.Positionals.Count != 1
            || !string.Equals(arguments.Positionals[0], "project", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException(
                "Expected 'project'. Example: donkeh upgrade project --version=1.0.25");
        }

        var version = arguments.Value("version");
        var project = arguments.Value("project");

        UpgradeService.Upgrade(project, version);
        return 0;
    }

    private static int RunUseLocal(string[] args)
    {
        var arguments = ParsedArguments.Parse(args, ["project"]);

        if (arguments.Help)
        {
            PrintUseLocalHelp();
            return 0;
        }

        if (arguments.Positionals.Count != 1)
        {
            throw new CliException(
                "Expected a framework path. Example: donkeh use-local ../donkeh-ui.sln");
        }

        var projectFiles = ProjectModeService.UseLocal(
            arguments.Positionals[0],
            arguments.Value("project"));

        WriteModeTargets("Using local framework in", projectFiles);
        Console.WriteLine();
        Console.WriteLine("Next:");
        Console.WriteLine("  donkeh run");
        return 0;
    }

    private static int RunUseNuget(string[] args)
    {
        var arguments = ParsedArguments.Parse(args, ["version", "project"]);

        if (arguments.Help)
        {
            PrintUseNugetHelp();
            return 0;
        }

        if (arguments.Positionals.Count != 0)
        {
            throw new CliException(
                "Expected no positional arguments. Example: donkeh use-nuget --version 1.0.29");
        }

        var projectFiles = ProjectModeService.UseNuget(
            arguments.Value("version"),
            arguments.Value("project"));

        WriteModeTargets("Using NuGet packages in", projectFiles);
        Console.WriteLine();
        Console.WriteLine("Next:");
        Console.WriteLine("  donkeh run");
        return 0;
    }

    private static void WriteModeTargets(string message, IReadOnlyList<string> projectFiles)
    {
        if (projectFiles.Count == 1)
        {
            Console.WriteLine($"{message} {projectFiles[0]}");
            return;
        }

        Console.WriteLine(message + ":");
        Console.WriteLine($"  {projectFiles[0]}");

        foreach (var projectFile in projectFiles.Skip(1))
        {
            Console.WriteLine($"  and {projectFile}");
        }
    }

    private static int RunEnableFeature(string[] args)
    {
        var arguments = ParsedArguments.Parse(args, ["project"]);

        if (arguments.Help)
        {
            PrintEnableFeatureHelp();
            return 0;
        }

        if (arguments.Positionals.Count != 1)
        {
            throw new CliException(
                "Expected a feature name. Example: donkeh enable-feature hot-reload");
        }

        var feature = arguments.Positionals[0];
        var projectFile = FeatureEnabler.Enable(feature, arguments.Value("project"));

        Console.WriteLine($"Enabled {feature} in {projectFile}");
        Console.WriteLine("  The reference is Debug-only; Release builds do not include it.");
        Console.WriteLine();
        Console.WriteLine("Next:");
        Console.WriteLine("  donkeh watch");
        return 0;
    }

    private static int RunWatch(string[] args)
    {
        var separator = Array.IndexOf(args, "--");
        var commandArgs = separator < 0 ? args : args[..separator];
        var appArguments = separator < 0 ? [] : args[(separator + 1)..];
        var arguments = ParsedArguments.Parse(commandArgs, ["project"]);

        if (arguments.Help)
        {
            PrintWatchHelp();
            return 0;
        }

        if (arguments.Positionals.Count != 0)
        {
            throw new CliException("Expected no positional arguments. Run 'donkeh watch --help'.");
        }

        return WatchService.Run(arguments.Value("project"), appArguments);
    }

    private static int RunRun(string[] args)
    {
        var separator = Array.IndexOf(args, "--");
        var commandArgs = separator < 0 ? args : args[..separator];
        var appArguments = separator < 0 ? [] : args[(separator + 1)..];
        var arguments = ParsedArguments.Parse(commandArgs, ["project"]);

        if (arguments.Help)
        {
            PrintRunHelp();
            return 0;
        }

        if (arguments.Positionals.Count != 0)
        {
            throw new CliException("Expected no positional arguments. Run 'donkeh run --help'.");
        }

        return RunService.Run(arguments.Value("project"), appArguments);
    }

    /// <summary>
    /// Reads the CSX switches shared by project and component generation.
    /// <c>--csx</c> forces CSX, <c>--csharp</c> forces plain C#, and the default
    /// lets the generator follow the project.
    /// </summary>
    private static (bool? Csx, string[] Remaining) ParseCsxArguments(string[] args)
    {
        bool? csx = null;
        var forceCsx = false;
        var forceCsharp = false;
        var remaining = new List<string>();

        foreach (var argument in args)
        {
            if (string.Equals(argument, "--csx", StringComparison.OrdinalIgnoreCase))
            {
                forceCsx = true;
                continue;
            }

            if (string.Equals(argument, "--csharp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "--cs", StringComparison.OrdinalIgnoreCase))
            {
                forceCsharp = true;
                continue;
            }

            remaining.Add(argument);
        }

        if (forceCsx && forceCsharp)
        {
            throw new CliException("Use either --csx or --csharp, not both.");
        }

        if (forceCsx)
        {
            csx = true;
        }
        else if (forceCsharp)
        {
            csx = false;
        }

        return (csx, [.. remaining]);
    }

    private static int RunNewComponent(string[] args)
    {
        var (csx, filtered) = ParseCsxArguments(args);
        var arguments = ParsedArguments.Parse(filtered, ["project"]);

        if (arguments.Help)
        {
            PrintComponentHelp();
            return 0;
        }

        if (arguments.Positionals.Count != 1)
        {
            throw new CliException(
                "Expected a component name. Example: donkeh new component FileList");
        }

        var name = arguments.Positionals[0];
        var files = ComponentGenerator.Generate(
            name,
            arguments.Value("project"),
            arguments.Force,
            csx);

        Console.WriteLine($"Created component {Identifiers.ComponentName(name)}");

        foreach (var file in files)
        {
            Console.WriteLine($"  {file}");
        }

        return 0;
    }

    private static int PrintNewHelp()
    {
        PrintHelp();
        return 0;
    }

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "help";

    private static void PrintHelp()
    {
        Console.WriteLine($"""
            donkeh {Version}

            Usage:
              donkeh new project [<directory>] [--name <name>] [--type application|framework] [--framework <path>] [--csharp] [--force]
              donkeh new component <name> [--project <path>] [--csx|--csharp] [--force]
              donkeh enable-target <desktop|android|web> [--project <path>]
              donkeh enable-feature <hot-reload> [--project <path>]
              donkeh run [--project <path>] [-- <app args>]
              donkeh watch [--project <path>] [-- <app args>]
              donkeh upgrade project [--version <version>] [--project <path>]
              donkeh use-local <framework path> [--project <path>]
              donkeh use-nuget [--version <version>] [--project <path>]

              donkeh mcp show|tools|capabilities [<name>]
              donkeh mcp invoke <capability> [--arg value]...
              donkeh mcp start [--project <path>]
              donkeh inspect project|dependencies|components|configuration [--project <path>]
              donkeh inspect component <name> [--properties|--events|--examples]
              donkeh component search <query> [--limit <n>]
              donkeh component inspect <name> [--properties|--events|--examples|--source]
              donkeh component scaffold <name> [--csharp] [--force] [--dry-run]
              donkeh csx syntax [<category>]
              donkeh csx inspect|validate|compile <file>
              donkeh css properties|selectors|functions
              donkeh css property <name>
              donkeh css validate <file>
              donkeh renderer inspect|capabilities
              donkeh docs search <query>
              donkeh docs get <topic> [--section <id>]
              donkeh diagnose project|component <name>
              donkeh doctor

            Agent-facing commands accept --json.

            Commands:
              new project       Create a minimal CSX app, or a component library with
                                a live test container when --type framework is used.
              new component     Create <Name>.cx (CSX projects) or <Name>.cs and
                                .css, embed the stylesheet, and register
                                [assembly: Stylesheet(...)].
              enable-target     Add desktop, Android or browser support to a project.
              enable-feature    Add an opt-in development feature to a project.
              run               Transform CSX sources when needed and run the project.
                                A framework root runs its test container.
              watch             Run a project with CSS hot reload in Debug.
              upgrade project   Clear NuGet caches, update package versions, restore
                                and update the global donkeh tool.
              use-local         Switch the project to a local framework checkout.
              use-nuget         Switch the project back to published NuGet packages.

            LLM and agent tooling:
              mcp               Discover Donkeh capabilities or start the MCP stdio server.
              inspect           Inspect the project, dependencies, components or configuration.
              component         Search, inspect or scaffold components.
              csx               Discover CSX syntax, inspect, validate or compile CSX files.
              css               Discover supported CSS vocabulary or validate a stylesheet.
              renderer          Inspect the active renderer and its capabilities.
              docs              Search or retrieve Donkeh documentation.
              diagnose          Run project or component diagnostics.
              doctor            Check the Donkeh environment and project health.

            Options:
              -h, --help        Show this help.
              --version         Show the CLI version.
              --csx             Force CSX (.cx) generation.
              --csharp          Force plain C# (.cs) generation.
              --force           Overwrite existing generated files.
            """);
    }

    private static void PrintProjectHelp()
    {
        Console.WriteLine("""
            Usage:
              donkeh new project [<directory>] [--name <name>] [--type application|framework] [--framework <path>] [--csharp] [--force]

            Creates a minimal Donkeh project in the current directory, or in
            <directory> when supplied. The generated app declares [AppRoot] and
            opens a blank window. Outside a framework checkout, the project uses
            the published NuGet packages automatically.

            Projects use CSX (.cx) and csx.json by default. Pass --csharp to
            generate plain C# files instead.

            With --type framework, creates a packable component library in src/
            and a test-container console app that renders every component. The
            library and test container are both generated in the selected mode.

            Options:
              --name <name>        Project name; defaults to the directory name.
              --type <kind>        'application' (default) or 'framework'.
              --framework <path>   Use a local framework checkout instead of NuGet.
              --csharp             Generate plain C# instead of CSX.
              --force              Overwrite existing generated files.
            """);
    }

    private static void PrintComponentHelp()
    {
        Console.WriteLine("""
            Usage:
              donkeh new component <name> [--project <path>] [--csx|--csharp] [--force]

            Creates:
              src/Components/<Name>/<Name>.cx
              src/Components/<Name>/<Name>.css

            In a framework project, the component is added to the library under
            src/ and uses Components/<Name>/ paths. The component follows the
            project: CSX projects get <Name>.cx, plain C# projects get
            <Name>.cs.

            It also adds the CSS as an EmbeddedResource and writes
            [assembly: Stylesheet("<project-relative css path>")] at the top of
            the component file.

            Options:
              --project <path>     Project file or project directory.
              --csx                Force a CSX (.cx) component.
              --csharp             Force a plain C# (.cs) component.
              --force              Overwrite existing component files.
            """);
    }

    private static void PrintEnableTargetHelp()
    {
        Console.WriteLine("""
            Usage:
              donkeh enable-target <desktop|android|web> [--project <path>]

            Adds a platform target to a generated project, wires up the packages,
            framework properties and app identity it needs, then restores the
            project. Desktop is enabled by default.

            Targets:
              desktop              Desktop window host (net10.0).
              android              Android host (net10.0-android).
              web                  Browser host (net10.0-browser).

            Options:
              --project <path>     Project file or project directory.
            """);
    }

    private static void PrintEnableFeatureHelp()
    {
        Console.WriteLine("""
            Usage:
              donkeh enable-feature <hot-reload> [--project <path>]

            Adds an opt-in development feature to a project. The generated reference
            is conditional on the Debug configuration, so Release builds and
            published applications do not restore or copy it.

            Features:
              hot-reload           Watch declared stylesheets and rebuild the CSS
                                   table and layout while the app runs.

            Options:
              --project <path>     Project file, project directory or framework root.
                                   A framework root selects its test-container.
            """);
    }

    private static void PrintWatchHelp()
    {
        Console.WriteLine("""
            Usage:
              donkeh watch [--project <path>] [-- <app args>]

            Runs the project in the Debug configuration through dotnet watch with hot
            reload enabled. Supported C# edits are applied to the running process,
            and CSS edits rebuild the cascade in place. Unsupported C# edits restart
            the app. No rebuild or manual restart is needed for normal edits.

            Hot reload must be enabled first:
              donkeh enable-feature hot-reload

            A framework root selects its test-container project. CSX projects
            should use 'donkeh run' so .cx sources are transformed.

            Options:
              --project <path>     Project file, project directory or framework root.
              -- <app args>        Arguments forwarded to the application.
            """);
    }

    private static void PrintRunHelp()
    {
        Console.WriteLine("""
            Usage:
              donkeh run [--project <path>] [-- <app args>]

            Runs a project. CSX projects are transformed first, then started
            through the CSX tool. Plain C# projects run through dotnet.

            A framework root runs its test-container, which renders the library
            in a live gallery. Referenced CSX projects are transformed first, so
            library changes are visible.

            Options:
              --project <path>     Project file, project directory or framework root.
              -- <app args>        Arguments forwarded to the application.
            """);
    }

    private static void PrintUpgradeHelp()
    {
        Console.WriteLine("""
            Usage:
              donkeh upgrade project [--version <version>] [--project <path>]

            Clears the NuGet caches, updates the Donkeh package version in the
            project, restores from NuGet and updates the global donkeh CLI tool.

            Options:
              --version <version>  Version to install; defaults to the latest stable.
              --project <path>     Project file or project directory.
            """);
    }

    private static void PrintUseLocalHelp()
    {
        Console.WriteLine("""
            Usage:
              donkeh use-local <framework path> [--project <path>]

            Switches a project from NuGet packages to a local framework checkout.
            The path may be the repository root, the Donkeh directory or a
            solution/project file inside the checkout.

            Options:
              --project <path>     Project file or project directory.
            """);
    }

    private static void PrintUseNugetHelp()
    {
        Console.WriteLine("""
            Usage:
              donkeh use-nuget [--version <version>] [--project <path>]

            Switches a project from a local framework checkout to the published
            NuGet packages and restores them.

            Options:
              --version <version>  Version to install; defaults to the latest stable.
              --project <path>     Project file or project directory.
            """);
    }

    /// <summary>Parsed positionals and <c>--option</c> values.</summary>
    private sealed class ParsedArguments
    {
        private readonly Dictionary<string, string?> _options;

        private ParsedArguments(
            List<string> positionals,
            Dictionary<string, string?> options,
            bool force,
            bool help)
        {
            Positionals = positionals;
            _options = options;
            Force = force;
            Help = help;
        }

        public List<string> Positionals { get; }

        public bool Force { get; }

        public bool Help { get; }

        public string? Value(string name) =>
            _options.TryGetValue(name, out var value) ? value : null;

        public static ParsedArguments Parse(string[] args, IReadOnlyList<string> allowed)
        {
            var positionals = new List<string>();
            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var force = false;
            var help = false;

            for (var i = 0; i < args.Length; i++)
            {
                var argument = args[i];

                if (IsHelp(argument))
                {
                    help = true;
                    continue;
                }

                if (string.Equals(argument, "--force", StringComparison.OrdinalIgnoreCase))
                {
                    force = true;
                    continue;
                }

                if (!argument.StartsWith("--", StringComparison.Ordinal))
                {
                    if (argument.StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new CliException($"Unknown option '{argument}'.");
                    }

                    positionals.Add(argument);
                    continue;
                }

                var option = argument[2..];
                string? value = null;
                var equals = option.IndexOf('=');

                if (equals >= 0)
                {
                    value = option[(equals + 1)..];
                    option = option[..equals];
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    value = args[++i];
                }

                if (!allowed.Contains(option, StringComparer.OrdinalIgnoreCase))
                {
                    throw new CliException($"Unknown option '--{option}'.");
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new CliException($"Option '--{option}' requires a value.");
                }

                options[option] = value;
            }

            return new ParsedArguments(positionals, options, force, help);
        }
    }
}
