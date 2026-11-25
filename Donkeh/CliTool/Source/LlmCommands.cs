using System.Text.Json;
using Donkeh.Llm;
using Donkeh.Llm.Capabilities;

namespace Donkeh.Cli;

public static partial class Cli
{
    private static readonly HashSet<string> LlmCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "mcp", "inspect", "component", "csx", "css", "renderer", "docs", "diagnose", "doctor"
    };

    private static readonly HashSet<string> LlmValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "project", "limit", "name", "file", "topic", "section", "renderer", "category",
        "filter", "kind", "package", "query", "transport", "format"
    };

    internal static bool IsLlmCommand(string command) => LlmCommands.Contains(command);

    internal static int RunLlmCommand(string[] args)
    {
        var root = args[0].ToLowerInvariant();
        var rest = args[1..];

        if (root == "doctor")
        {
            return RunLlmCapability("doctor", rest);
        }

        if (rest.Length == 0 || IsHelp(rest[0]))
        {
            PrintLlmHelp(root);
            return 0;
        }

        var subcommand = rest[0].ToLowerInvariant();
        var commandArgs = rest[1..];

        return root switch
        {
            "mcp" => subcommand switch
            {
                "show" => RunLlmCapability("mcp.show", commandArgs),
                "tools" => RunLlmCapability("mcp.tools", commandArgs),
                "capabilities" => RunLlmCapability("mcp.capabilities", commandArgs),
                "invoke" => RunMcpInvoke(commandArgs),
                "start" => RunMcpStart(commandArgs),
                _ => throw new CliException(
                    $"Unknown command 'mcp {subcommand}'. Expected show, tools, capabilities, invoke or start.")
            },
            "inspect" => subcommand switch
            {
                "project" => RunLlmCapability("project.inspect", commandArgs),
                "dependencies" => RunLlmCapability("project.dependencies", commandArgs),
                "components" => RunLlmCapability("project.components", commandArgs),
                "component" => RunLlmCapability("component.inspect", commandArgs),
                "configuration" => RunLlmCapability("project.configuration", commandArgs),
                _ => throw new CliException(
                    $"Unknown command 'inspect {subcommand}'. Expected project, dependencies, components, component or configuration.")
            },
            "component" => subcommand switch
            {
                "search" => RunLlmCapability("component.search", commandArgs),
                "inspect" => RunLlmCapability("component.inspect", commandArgs),
                "scaffold" => RunLlmCapability("component.scaffold", commandArgs),
                _ => throw new CliException(
                    $"Unknown command 'component {subcommand}'. Expected search, inspect or scaffold.")
            },
            "csx" => subcommand switch
            {
                "syntax" => RunLlmCapability("csx.syntax", commandArgs),
                "inspect" => RunLlmCapability("csx.inspect", commandArgs),
                "validate" => RunLlmCapability("csx.validate", commandArgs),
                "compile" => RunLlmCapability("csx.compile", commandArgs),
                _ => throw new CliException(
                    $"Unknown command 'csx {subcommand}'. Expected syntax, inspect, validate or compile.")
            },
            "css" => subcommand switch
            {
                "properties" or "property" => RunLlmCapability("css.properties", commandArgs),
                "selectors" => RunLlmCapability("css.selectors", commandArgs),
                "functions" => RunLlmCapability("css.functions", commandArgs),
                "validate" => RunLlmCapability("css.validate", commandArgs),
                _ => throw new CliException(
                    $"Unknown command 'css {subcommand}'. Expected properties, property, selectors, functions or validate.")
            },
            "renderer" => subcommand switch
            {
                "inspect" => RunLlmCapability("renderer.inspect", commandArgs),
                "capabilities" => RunLlmCapability("renderer.capabilities", commandArgs),
                _ => throw new CliException(
                    $"Unknown command 'renderer {subcommand}'. Expected inspect or capabilities.")
            },
            "docs" => subcommand switch
            {
                "search" => RunLlmCapability("docs.search", commandArgs),
                "get" => RunLlmCapability("docs.get", commandArgs),
                _ => throw new CliException(
                    $"Unknown command 'docs {subcommand}'. Expected search or get.")
            },
            "diagnose" => subcommand switch
            {
                "project" => RunLlmCapability("diagnose.project", commandArgs),
                "component" => RunLlmCapability("diagnose.component", commandArgs),
                _ => throw new CliException(
                    $"Unknown command 'diagnose {subcommand}'. Expected project or component.")
            },
            _ => throw new CliException($"Unknown command '{root}'.")
        };
    }

    private static int RunLlmCapability(string capability, string[] args)
    {
        var parsed = LlmArguments.Parse(args);

        if (parsed.Help)
        {
            PrintCapabilityHelp(capability);
            return 0;
        }

        var service = new CapabilityService(
            Environment.CurrentDirectory,
            Version,
            ScaffoldComponent,
            parsed.Project);

        var result = service.Invoke(capability, parsed.ToInvocation());
        return WriteLlmResult(result, parsed, capability);
    }

    private static int RunMcpStart(string[] args)
    {
        var parsed = LlmArguments.Parse(args);

        if (parsed.Help)
        {
            Console.WriteLine("""
                Usage:
                  donkeh mcp start [--project <path>]

                Starts the Donkeh MCP server on stdio. Compatible agents can then
                discover and invoke the same capabilities available through the CLI.

                The protocol owns stdout; diagnostics and errors are written to stderr.
                """);
            return 0;
        }

        var transport = parsed.Arguments.TryGetValue("transport", out var value)
            ? value as string
            : null;

        if (!string.IsNullOrWhiteSpace(transport)
            && !transport.Equals("stdio", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("Only the stdio MCP transport is supported.");
        }

        return McpServerHost.Run(parsed.Project, Version, ScaffoldComponent);
    }

    private static int RunMcpInvoke(string[] args)
    {
        var parsed = LlmArguments.Parse(args);

        if (parsed.Help)
        {
            Console.WriteLine("""
                Usage:
                  donkeh mcp invoke <capability> [--project <path>] [--arg value]... [--json]

                Invokes one capability, including capabilities registered by packages.
                Use 'donkeh mcp capabilities <name>' to inspect its arguments first.
                """);
            return 0;
        }

        var capability = parsed.Positionals.FirstOrDefault()
            ?? throw new CliException("Expected a capability name. Example: donkeh mcp invoke csx.validate --file Card.cx");

        var service = new CapabilityService(
            Environment.CurrentDirectory,
            Version,
            ScaffoldComponent,
            parsed.Project);
        var invocation = new LlmInvocation
        {
            WorkingDirectory = Environment.CurrentDirectory,
            ProjectPath = parsed.Project,
            Positionals = parsed.Positionals.Skip(1).ToList(),
            Arguments = parsed.Arguments
        };
        var result = service.Invoke(capability, invocation);

        if (parsed.Json)
        {
            Console.WriteLine(LlmJson.Serialize(result));
        }
        else
        {
            Console.WriteLine(LlmJson.SerializeIndented(result));
        }

        return 0;
    }

    private static object ScaffoldComponent(LlmInvocation invocation)
    {
        var name = invocation.Positionals.FirstOrDefault() ?? invocation.String("name")
            ?? throw new LlmException("LLM3006", "A component name is required.");
        bool? csx = invocation.Flag("csx")
            ? true
            : invocation.Flag("csharp") ? false : null;
        var force = invocation.Flag("force");
        var files = ComponentGenerator.Generate(name, invocation.ProjectPath, force, csx);
        var projectFile = ComponentGenerator.FindProject(invocation.ProjectPath);
        var projectDirectory = Path.GetDirectoryName(projectFile) ?? Environment.CurrentDirectory;

        return new
        {
            component = Identifiers.ComponentName(name),
            project = projectFile,
            files = files.Select(file => Path.GetRelativePath(projectDirectory, file).Replace('\\', '/')).ToList(),
            next = new[]
            {
                "donkeh run",
                $"donkeh diagnose component {Identifiers.ComponentName(name)} --json"
            }
        };
    }

    private static int WriteLlmResult(object? result, LlmArguments arguments, string capability)
    {
        if (arguments.Json)
        {
            Console.WriteLine(LlmJson.Serialize(result));
            return 0;
        }

        switch (capability)
        {
            case "mcp.show":
                WriteMcpShow(result);
                break;
            case "mcp.tools":
                WriteMcpTools(result);
                break;
            case "mcp.capabilities":
                WriteMcpCapabilities(result);
                break;
            default:
                Console.WriteLine(LlmJson.SerializeIndented(result));
                break;
        }

        return 0;
    }

    private static void WriteMcpShow(object? result)
    {
        if (result is null)
        {
            return;
        }

        var json = JsonSerializer.SerializeToElement(result, LlmJson.Options);
        var version = json.TryGetProperty("version", out var versionElement) ? versionElement.GetString() : null;
        Console.WriteLine($"Donkeh {version} — LLM capabilities");
        Console.WriteLine();
        Console.WriteLine("Discovery:");
        Console.WriteLine("  donkeh mcp show --json");
        Console.WriteLine("  donkeh mcp tools --json");
        Console.WriteLine("  donkeh mcp capabilities <name> --json");
        Console.WriteLine();

        if (json.TryGetProperty("categories", out var categories))
        {
            foreach (var category in categories.EnumerateArray())
            {
                var name = category.GetProperty("name").GetString();
                var description = category.GetProperty("description").GetString();
                Console.WriteLine($"{name}: {description}");

                if (category.TryGetProperty("capabilities", out var capabilities))
                {
                    foreach (var capability in capabilities.EnumerateArray())
                    {
                        var usage = capability.GetProperty("usage").GetString();
                        var summary = capability.GetProperty("summary").GetString();
                        var mcp = capability.TryGetProperty("mcp", out var mcpElement)
                            && mcpElement.ValueKind == JsonValueKind.String
                            ? $" [mcp:{mcpElement.GetString()}]"
                            : "";
                        Console.WriteLine($"  {usage}{mcp}");
                        Console.WriteLine($"      {summary}");
                    }
                }

                Console.WriteLine();
            }
        }

        Console.WriteLine("Every agent-facing command accepts --json. Run 'donkeh mcp start' for MCP.");
    }

    private static void WriteMcpTools(object? result)
    {
        if (result is null)
        {
            return;
        }

        var json = JsonSerializer.SerializeToElement(result, LlmJson.Options);

        if (!json.TryGetProperty("tools", out var tools))
        {
            return;
        }

        Console.WriteLine($"MCP tools ({tools.GetArrayLength()}):");

        foreach (var tool in tools.EnumerateArray())
        {
            var name = tool.GetProperty("name").GetString();
            var title = tool.GetProperty("title").GetString();
            var required = tool.TryGetProperty("required", out var requiredElement)
                ? string.Join(", ", requiredElement.EnumerateArray().Select(item => item.GetString()))
                : "";
            Console.WriteLine($"  {name}  {title}");
            Console.WriteLine($"      required: {(string.IsNullOrWhiteSpace(required) ? "none" : required)}");
        }
    }

    private static void WriteMcpCapabilities(object? result)
    {
        if (result is null)
        {
            return;
        }

        var json = JsonSerializer.SerializeToElement(result, LlmJson.Options);

        if (json.TryGetProperty("capability", out var single))
        {
            WriteCapability(single);
            return;
        }

        if (!json.TryGetProperty("capabilities", out var capabilities))
        {
            return;
        }

        foreach (var capability in capabilities.EnumerateArray())
        {
            WriteCapability(capability);
            Console.WriteLine();
        }
    }

    private static void WriteCapability(JsonElement capability)
    {
        var name = capability.GetProperty("name").GetString();
        var usage = capability.GetProperty("usage").GetString();
        var summary = capability.GetProperty("summary").GetString();
        var mcp = capability.TryGetProperty("mcpName", out var mcpElement)
            && mcpElement.ValueKind == JsonValueKind.String
            ? mcpElement.GetString()
            : null;
        Console.WriteLine($"{name} — {summary}");
        Console.WriteLine($"  usage:   {usage}");
        Console.WriteLine($"  mcp:     {(mcp is null ? "not exposed" : mcp)}");

        if (capability.TryGetProperty("arguments", out var arguments) && arguments.GetArrayLength() > 0)
        {
            Console.WriteLine("  arguments:");

            foreach (var argument in arguments.EnumerateArray())
            {
                var required = argument.TryGetProperty("required", out var requiredElement)
                    && requiredElement.ValueKind == JsonValueKind.True
                    ? " (required)"
                    : "";
                Console.WriteLine(
                    $"    {argument.GetProperty("name").GetString()}{required}: {argument.GetProperty("description").GetString()}");
            }
        }

        if (capability.TryGetProperty("examples", out var examples) && examples.GetArrayLength() > 0)
        {
            Console.WriteLine("  example: " + examples[0].GetString());
        }
    }

    private static void PrintLlmHelp(string command)
    {
        var text = command switch
        {
            "mcp" => """
                Usage:
                  donkeh mcp show [--json]
                  donkeh mcp tools [--json]
                  donkeh mcp capabilities [<name>] [--json]
                  donkeh mcp invoke <capability> [--project <path>] [--arg value]... [--json]
                  donkeh mcp start [--project <path>]

                Discovery commands explain the LLM-facing functionality. 'mcp invoke'
                calls a capability directly. 'mcp start' hosts the same capabilities
                over the Model Context Protocol on stdio.
                """,
            "inspect" => """
                Usage:
                  donkeh inspect project [--project <path>] [--json]
                  donkeh inspect dependencies [--project <path>] [--json]
                  donkeh inspect components [--query <text>] [--kind <kind>] [--limit <n>] [--json]
                  donkeh inspect component <name> [--properties|--events|--examples] [--json]
                  donkeh inspect configuration [--project <path>] [--json]

                Project intelligence without scanning the whole directory manually.
                """,
            "component" => """
                Usage:
                  donkeh component search <query> [--limit <n>] [--package <id>] [--json]
                  donkeh component inspect <name> [--properties|--events|--examples|--source] [--json]
                  donkeh component scaffold <name> [--csharp] [--force] [--dry-run] [--json]

                Search uses names, descriptions, capabilities and common synonyms.
                Inspection omits implementation source unless --source is supplied.
                """,
            "csx" => """
                Usage:
                  donkeh csx syntax [<category>] [--json]
                  donkeh csx inspect <file> [--summary] [--json]
                  donkeh csx validate <file> [--project <path>] [--json]
                  donkeh csx compile <file> [--generated] [--maps] [--resolved] [--json]

                CSX syntax discovery and validation use the shipped CSX parser and
                transformer. Generated C# and source maps are opt-in.
                """,
            "css" => """
                Usage:
                  donkeh css properties [<name>] [--filter <text>] [--limit <n>] [--json]
                  donkeh css property <name> [--json]
                  donkeh css selectors [<category>] [--json]
                  donkeh css functions [--filter <text>] [--json]
                  donkeh css validate <file> [--json]

                These commands report what the Donkeh CSS engine supports, not what a
                browser supports.
                """,
            "renderer" => """
                Usage:
                  donkeh renderer inspect [--project <path>] [--json]
                  donkeh renderer capabilities [--renderer <desktop|android|web>] [--category <name>] [--json]

                Capabilities come from the renderer implementation documentation and
                reflect desktop, Android and browser differences.
                """,
            "docs" => """
                Usage:
                  donkeh docs search <query> [--limit <n>] [--json]
                  donkeh docs get <topic> [--section <id>] [--json]

                Search returns concise, targeted results. Retrieval supports one section
                at a time to avoid loading an entire documentation set.
                """,
            "diagnose" => """
                Usage:
                  donkeh diagnose project [--project <path>] [--json]
                  donkeh diagnose component <name> [--project <path>] [--json]

                Diagnostics are based on actual files, restore metadata and the shipped
                validators. Speculative warnings are not emitted.
                """,
            _ => "Run 'donkeh --help' for the command overview."
        };

        Console.WriteLine(text);
    }

    private static void PrintCapabilityHelp(string capability)
    {
        var descriptor = BuiltInCapabilities.All().FirstOrDefault(item =>
            item.Name.Equals(capability, StringComparison.OrdinalIgnoreCase));

        if (descriptor is null)
        {
            PrintLlmHelp(capability.Split('.')[0]);
            return;
        }

        Console.WriteLine($"Usage:\n  {descriptor.Usage}\n");
        Console.WriteLine(descriptor.Description ?? descriptor.Summary);
        Console.WriteLine();
        Console.WriteLine($"Output:\n  {descriptor.Output}");

        if (descriptor.Arguments.Count > 0)
        {
            Console.WriteLine("\nArguments:");

            foreach (var argument in descriptor.Arguments)
            {
                var required = argument.Required ? " (required)" : "";
                Console.WriteLine($"  --{argument.Name}{required}  {argument.Description}");
            }
        }
    }

    private sealed class LlmArguments
    {
        private LlmArguments(
            List<string> positionals,
            Dictionary<string, object?> arguments,
            bool json,
            bool help)
        {
            Positionals = positionals;
            Arguments = arguments;
            Json = json;
            Help = help;
        }

        public List<string> Positionals { get; }

        public Dictionary<string, object?> Arguments { get; }

        public bool Json { get; }

        public bool Help { get; }

        public string? Project => Arguments.TryGetValue("project", out var value)
            ? value as string
            : null;

        public LlmInvocation ToInvocation() => new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            ProjectPath = Project,
            Positionals = Positionals,
            Arguments = Arguments
        };

        public static LlmArguments Parse(string[] args)
        {
            var positionals = new List<string>();
            var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var json = false;
            var help = false;

            for (var i = 0; i < args.Length; i++)
            {
                var argument = args[i];

                if (IsHelp(argument))
                {
                    help = true;
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

                var key = Camel(option);

                if (option.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    json = true;
                    arguments[key] = true;
                    continue;
                }

                if (option.Equals("human", StringComparison.OrdinalIgnoreCase))
                {
                    json = false;
                    continue;
                }

                if (LlmValueOptions.Contains(option))
                {
                    if (value is null)
                    {
                        if (i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        {
                            throw new CliException($"Option '--{option}' requires a value.");
                        }

                        value = args[++i];
                    }

                    arguments[key] = option.Equals("limit", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(value, out var limit)
                        ? limit
                        : value;
                    continue;
                }

                if (value is not null)
                {
                    arguments[key] = value;
                }
                else
                {
                    arguments[key] = true;
                }
            }

            return new LlmArguments(positionals, arguments, json, help);
        }

        private static string Camel(string option)
        {
            var parts = option.Split('-', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0
                ? option
                : parts[0] + string.Concat(parts.Skip(1).Select(part =>
                    char.ToUpperInvariant(part[0]) + part[1..]));
        }
    }
}
