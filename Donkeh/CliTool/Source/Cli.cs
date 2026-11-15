namespace Donkeh.Cli;

/// <summary>Command-line entry point for the Donkeh developer tool.</summary>
public static class Cli
{
    private static readonly string Version =
        typeof(Cli).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>Runs the CLI and returns a process exit code.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Zero on success, one on a user-facing failure.</returns>
    public static int Run(string[] args)
    {
        try
        {
            return RunCore(args);
        }
        catch (CliException exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
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
        var arguments = ParsedArguments.Parse(args, ["name", "framework"]);

        if (arguments.Help)
        {
            PrintProjectHelp();
            return 0;
        }

        if (arguments.Positionals.Count > 1)
        {
            throw new CliException("Expected at most one project directory name.");
        }

        var target = Environment.CurrentDirectory;
        string? name = arguments.Value("name");

        if (arguments.Positionals.Count == 1)
        {
            var directoryName = arguments.Positionals[0];
            target = Path.Combine(target, directoryName);
            name ??= directoryName;
        }

        var framework = FrameworkLocator.Find(arguments.Value("framework"));
        var files = ProjectGenerator.Generate(target, name, framework, arguments.Force);

        Console.WriteLine($"Created Donkeh project in {Path.GetFullPath(target)}");

        foreach (var file in files)
        {
            Console.WriteLine($"  {Path.GetFileName(file)}");
        }

        Console.WriteLine();
        Console.WriteLine("Next:");

        if (arguments.Positionals.Count == 1)
        {
            Console.WriteLine($"  cd {arguments.Positionals[0]}");
        }

        Console.WriteLine("  dotnet run");
        return 0;
    }

    private static int RunNewComponent(string[] args)
    {
        var arguments = ParsedArguments.Parse(args, ["project"]);

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
            arguments.Force);

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
              donkeh new project [<directory>] [--name <name>] [--framework <path>] [--force]
              donkeh new component <name> [--project <path>] [--force]

            Commands:
              new project       Create a minimal app that opens a blank window.
              new component     Create src/Components/<Name>/<Name>.cs and .css, embed
                                the stylesheet, and register [assembly: Stylesheet(...)].

            Options:
              -h, --help        Show this help.
              --version         Show the CLI version.
              --force           Overwrite existing generated files.
            """);
    }

    private static void PrintProjectHelp()
    {
        Console.WriteLine("""
            Usage:
              donkeh new project [<directory>] [--name <name>] [--framework <path>] [--force]

            Creates a minimal Donkeh project in the current directory, or in
            <directory> when supplied. The generated app declares [AppRoot] and
            opens a blank window.

            Options:
              --name <name>        Project name; defaults to the directory name.
              --framework <path>   Repository root or Donkeh directory.
              --force              Overwrite existing generated files.
            """);
    }

    private static void PrintComponentHelp()
    {
        Console.WriteLine("""
            Usage:
              donkeh new component <name> [--project <path>] [--force]

            Creates:
              src/Components/<Name>/<Name>.cs
              src/Components/<Name>/<Name>.css

            It also adds the CSS as an EmbeddedResource and writes
            [assembly: Stylesheet("src/Components/<Name>/<Name>.css")] at the top of
            the component file.

            Options:
              --project <path>     Project file or project directory.
              --force              Overwrite existing component files.
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
