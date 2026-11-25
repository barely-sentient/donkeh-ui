namespace Donkeh.Llm.Capabilities;

/// <summary>Metadata for the built-in CLI and MCP capabilities.</summary>
public static class BuiltInCapabilities
{
    private static readonly LlmCapabilityArgument ProjectArgument = new()
    {
        Name = "project",
        Description = "Project file or directory. Defaults to the nearest project above the working directory.",
        Type = "string"
    };

    private static readonly LlmCapabilityArgument LimitArgument = new()
    {
        Name = "limit",
        Description = "Maximum number of results.",
        Type = "integer",
        Default = "10"
    };

    private static readonly LlmCapabilityArgument PropertyLimitArgument = new()
    {
        Name = "limit",
        Description = "Maximum number of properties to return.",
        Type = "integer",
        Default = "100"
    };

    private static readonly LlmCapabilityArgument DocsLimitArgument = new()
    {
        Name = "limit",
        Description = "Maximum number of documentation results.",
        Type = "integer",
        Default = "5"
    };

    private static readonly LlmCapabilityArgument JsonArgument = new()
    {
        Name = "json",
        Description = "Emit machine-readable JSON. The MCP server always returns structured JSON.",
        Type = "boolean",
        Default = "false"
    };

    /// <summary>Returns the built-in capability descriptors.</summary>
    public static List<LlmCapability> All() =>
    [
        new()
        {
            Name = "mcp.show",
            Category = "MCP",
            Summary = "Explain the LLM functionality Donkeh exposes and how to invoke it.",
            Description = "Primary discovery entry point. Returns capability categories, summaries, invocation syntax and MCP availability without dumping full documentation.",
            Usage = "donkeh mcp show [--json]",
            Arguments = [JsonArgument],
            Output = "Capability categories, concise capability entries, usage examples and notes.",
            Examples = ["donkeh mcp show", "donkeh mcp show --json"],
            Tags = ["discover", "capabilities", "overview", "start"],
            VisibleInShow = false,
            WorksOutsideProject = true
        },
        new()
        {
            Name = "mcp.tools",
            Category = "MCP",
            Summary = "List the tools exposed through MCP with input schemas.",
            Description = "Lists MCP tool names, descriptions, required arguments, input schemas and output descriptions.",
            Usage = "donkeh mcp tools [--json]",
            Arguments = [JsonArgument],
            Output = "Array of MCP tools and their JSON input schemas.",
            Examples = ["donkeh mcp tools --json"],
            Tags = ["mcp", "tools", "schema"],
            VisibleInShow = false,
            WorksOutsideProject = true
        },
        new()
        {
            Name = "mcp.capabilities",
            Category = "MCP",
            Summary = "List capabilities in detail, or inspect one capability's interface.",
            Description = "Returns the full capability catalogue. Pass a capability name to inspect one interface.",
            Usage = "donkeh mcp capabilities [<name>] [--json]",
            Arguments =
            [
                new() { Name = "name", Description = "Capability name, for example csx.validate.", Type = "string" },
                JsonArgument
            ],
            Output = "One or more capability descriptors with arguments, output and examples.",
            Examples = ["donkeh mcp capabilities", "donkeh mcp capabilities csx.validate --json"],
            Tags = ["capabilities", "interface", "schema"],
            VisibleInShow = false,
            WorksOutsideProject = true
        },
        new()
        {
            Name = "mcp.invoke",
            Category = "MCP",
            Summary = "Invoke one capability directly, including package-registered capabilities.",
            Description = "Calls the same implementation used by the CLI and MCP server. Use mcp capabilities first to inspect arguments.",
            Usage = "donkeh mcp invoke <capability> [--project <path>] [--arg value]... [--json]",
            Arguments =
            [
                new() { Name = "capability", Description = "Capability name, for example csx.validate.", Type = "string", Required = true },
                JsonArgument
            ],
            Output = "The capability result in the same shape returned by its dedicated command or MCP tool.",
            Examples = ["donkeh mcp invoke csx.validate --file ./Components/Card.cx --json"],
            Tags = ["invoke", "capability", "package"],
            VisibleInShow = false,
            WorksOutsideProject = true
        },
        new()
        {
            Name = "project.inspect",
            Category = "Project",
            Summary = "Summarize a Donkeh project, its versions, configuration and features.",
            Description = "Reads the project file, csx.json and restore assets. Returns project name, type, frameworks, Donkeh versions, renderer, CSX configuration, settings and detected features.",
            Usage = "donkeh inspect project [--project <path>] [--json]",
            Arguments = [ProjectArgument, JsonArgument],
            Output = "Project summary with configuration and features.",
            Examples = ["donkeh inspect project --json"],
            Tags = ["project", "overview", "configuration", "versions"],
            McpName = "project_inspect"
        },
        new()
        {
            Name = "project.dependencies",
            Category = "Project",
            Summary = "List installed Donkeh packages, renderers and component libraries.",
            Description = "Returns package and project references with requested and resolved versions. Marks renderer, tooling and framework packages.",
            Usage = "donkeh inspect dependencies [--project <path>] [--json]",
            Arguments = [ProjectArgument, JsonArgument],
            Output = "Dependency list plus a compact role summary.",
            Examples = ["donkeh inspect dependencies --json"],
            Tags = ["dependencies", "packages", "versions", "renderer", "component library"],
            McpName = "dependencies_inspect"
        },
        new()
        {
            Name = "project.components",
            Category = "Project",
            Summary = "List components available through the project and its packages.",
            Description = "Returns component names, packages, kinds, short descriptions and source references. Filters keep responses small.",
            Usage = "donkeh inspect components [--query <text>] [--package <id>] [--kind <kind>] [--limit <n>] [--project <path>] [--json]",
            Arguments =
            [
                new() { Name = "query", Description = "Optional conceptual filter.", Type = "string" },
                new() { Name = "package", Description = "Only components from this package.", Type = "string" },
                new() { Name = "kind", Description = "class, csx, html-element or assembly.", Type = "string" },
                LimitArgument,
                ProjectArgument,
                JsonArgument
            ],
            Output = "Component summaries and a count.",
            Examples = ["donkeh inspect components --limit 20 --json"],
            Tags = ["components", "discovery", "catalogue"],
            McpName = "components_list"
        },
        new()
        {
            Name = "project.configuration",
            Category = "Project",
            Summary = "Show Donkeh-specific project configuration without secrets.",
            Description = "Returns CSX, renderer, stylesheet and framework settings. Environment variables and credentials are never read.",
            Usage = "donkeh inspect configuration [--project <path>] [--json]",
            Arguments = [ProjectArgument, JsonArgument],
            Output = "CSX configuration, renderer configuration and selected MSBuild settings.",
            Examples = ["donkeh inspect configuration --json"],
            Tags = ["configuration", "csx", "renderer", "settings"],
            McpName = "configuration_inspect"
        },
        new()
        {
            Name = "component.search",
            Category = "Components",
            Summary = "Search components by name, description, capabilities and synonyms.",
            Description = "Conceptual search. 'file selection' finds file pickers and explorers even when the component name does not contain the exact words.",
            Usage = "donkeh component search <query> [--limit <n>] [--package <id>] [--json]",
            Arguments =
            [
                new() { Name = "query", Description = "Conceptual search text.", Type = "string", Required = true },
                LimitArgument,
                new() { Name = "package", Description = "Only search this package.", Type = "string" },
                new() { Name = "kind", Description = "class, csx, html-element or assembly.", Type = "string" },
                ProjectArgument,
                JsonArgument
            ],
            Output = "Ranked component summaries with inspect commands.",
            Examples = ["donkeh component search \"file selection\" --json", "donkeh component search dialog --limit 5 --json"],
            Tags = ["search", "components", "discovery", "file selection"],
            McpName = "component_search"
        },
        new()
        {
            Name = "component.inspect",
            Category = "Components",
            Summary = "Inspect a component's properties, events, state, lifecycle and examples.",
            Description = "Returns the public API without implementation source unless --source is requested. Use --properties, --events or --examples for targeted responses.",
            Usage = "donkeh component inspect <name> [--properties] [--events] [--examples] [--source] [--project <path>] [--json]",
            Arguments =
            [
                new() { Name = "name", Description = "Component name, for example Button or FileExplorer.", Type = "string", Required = true },
                new() { Name = "properties", Description = "Return only properties.", Type = "boolean" },
                new() { Name = "events", Description = "Return only events.", Type = "boolean" },
                new() { Name = "examples", Description = "Return only examples.", Type = "boolean" },
                new() { Name = "source", Description = "Include implementation source for project components.", Type = "boolean" },
                ProjectArgument,
                JsonArgument
            ],
            Output = "Component metadata: package, props, events, children support, state, lifecycle, dependencies and examples.",
            Examples = ["donkeh component inspect Button --properties --json", "donkeh component inspect FileExplorer --events --json"],
            Tags = ["component", "api", "properties", "events", "examples"],
            McpName = "component_inspect"
        },
        new()
        {
            Name = "component.scaffold",
            Category = "Components",
            Summary = "Generate a component using the project's documented conventions.",
            Description = "Creates the component source, stylesheet and EmbeddedResource entry through the same generator used by 'donkeh new component'.",
            Usage = "donkeh component scaffold <name> [--project <path>] [--csx] [--force] [--dry-run] [--json]",
            Arguments =
            [
                new() { Name = "name", Description = "New component name.", Type = "string", Required = true },
                new() { Name = "csx", Description = "Generate a .cx component even when the project is not CSX-dominant.", Type = "boolean" },
                new() { Name = "force", Description = "Overwrite existing component files.", Type = "boolean" },
                new() { Name = "dryRun", Description = "Report the files that would be created without writing them.", Type = "boolean" },
                ProjectArgument,
                JsonArgument
            ],
            Output = "Created file paths, component name and next steps.",
            Examples = ["donkeh component scaffold UserCard --json"],
            Tags = ["scaffold", "generate", "component", "new"],
            McpName = "component_scaffold"
        },
        new()
        {
            Name = "csx.syntax",
            Category = "CSX",
            Summary = "Explain the CSX syntax and language features actually supported.",
            Description = "Covers components, elements, attributes, expressions, children, fragments, generics, events, conditional rendering, iteration, C# expressions and limitations. There are no CSX directives.",
            Usage = "donkeh csx syntax [<category>] [--json]",
            Arguments =
            [
                new() { Name = "category", Description = "Topic: components, elements, attributes, expressions, children, fragments, generics, events, conditional, iteration, directives, csharp, configuration.", Type = "string" },
                JsonArgument
            ],
            Output = "Syntax topics with minimal examples and limitations.",
            Examples = ["donkeh csx syntax components --json", "donkeh csx syntax events --json"],
            Tags = ["csx", "syntax", "language", "components", "events"],
            McpName = "csx_syntax",
            WorksOutsideProject = true
        },
        new()
        {
            Name = "csx.inspect",
            Category = "CSX",
            Summary = "Inspect a CSX file's components, attributes, expressions and dependencies.",
            Description = "Uses the shipped CSX tokenizer. Returns counters, components and a short element preview by default; --full returns every element, attribute and expression.",
            Usage = "donkeh csx inspect <file> [--full|--summary] [--json]",
            Arguments =
            [
                new() { Name = "file", Description = "Path to a .cx or .csx file.", Type = "string", Required = true },
                new() { Name = "full", Description = "Include every element, attribute and expression.", Type = "boolean" },
                new() { Name = "summary", Description = "Return counters and distinct names only.", Type = "boolean" },
                JsonArgument
            ],
            Output = "Elements, components, attributes, expressions, dependencies and source locations.",
            Examples = ["donkeh csx inspect ./Components/UserCard.cx --json"],
            Tags = ["csx", "inspect", "structure", "parse"],
            McpName = "csx_inspect",
            WorksOutsideProject = true
        },
        new()
        {
            Name = "csx.validate",
            Category = "CSX",
            Summary = "Validate a CSX file with the transformer and component metadata.",
            Description = "Runs the shipped CSX transformer and validates element/component attributes against the project's props types. Diagnostics include stable codes, locations and suggestions.",
            Usage = "donkeh csx validate <file> [--project <path>] [--json]",
            Arguments =
            [
                new() { Name = "file", Description = "Path to a .cx or .csx file.", Type = "string", Required = true },
                ProjectArgument,
                JsonArgument
            ],
            Output = "valid flag and an array of diagnostics with code, severity, message, location and suggestions.",
            Examples = ["donkeh csx validate ./Components/UserCard.cx --json"],
            Tags = ["csx", "validate", "diagnostics", "errors"],
            McpName = "csx_validate",
            WorksOutsideProject = true
        },
        new()
        {
            Name = "csx.compile",
            Category = "CSX",
            Summary = "Transform a CSX file and return compilation status and metadata.",
            Description = "Runs the real CSX transformer. Generated C#, source maps and resolved component/property/event bindings are optional to keep responses small.",
            Usage = "donkeh csx compile <file> [--generated] [--maps] [--resolved] [--project <path>] [--json]",
            Arguments =
            [
                new() { Name = "file", Description = "Path to a .cx or .csx file.", Type = "string", Required = true },
                new() { Name = "generated", Description = "Include generated C#.", Type = "boolean" },
                new() { Name = "maps", Description = "Include source-map entries.", Type = "boolean" },
                new() { Name = "resolved", Description = "Include resolved components, properties and event bindings. Defaults to true.", Type = "boolean", Default = "true" },
                ProjectArgument,
                JsonArgument
            ],
            Output = "success flag, diagnostics, optional generated source, source map and resolved metadata.",
            Examples = ["donkeh csx compile ./Components/UserCard.cx --json", "donkeh csx compile ./Components/UserCard.cx --generated --maps --json"],
            Tags = ["csx", "compile", "transform", "source map"],
            McpName = "csx_compile",
            WorksOutsideProject = true
        },
        new()
        {
            Name = "css.properties",
            Category = "CSS",
            Summary = "List CSS properties the Donkeh resolver actually supports.",
            Description = "Property names come from the LayoutLab resolver, not a browser compatibility table. Pass a property name for value details.",
            Usage = "donkeh css properties [<name>] [--filter <text>] [--limit <n>] [--json]",
            Arguments =
            [
                new() { Name = "name", Description = "Exact property name, for example display.", Type = "string" },
                new() { Name = "filter", Description = "Substring filter over property names.", Type = "string" },
                PropertyLimitArgument,
                JsonArgument
            ],
            Output = "Supported properties with support status, notes, consumers and common values.",
            Examples = ["donkeh css properties --json", "donkeh css property display --json"],
            Tags = ["css", "properties", "display", "values"],
            McpName = "css_properties",
            WorksOutsideProject = true
        },
        new()
        {
            Name = "css.selectors",
            Category = "CSS",
            Summary = "List supported selectors, combinators, pseudo-classes and pseudo-elements.",
            Description = "Marks constructs the Donkeh matcher can evaluate, recognizes but cannot evaluate, or does not support.",
            Usage = "donkeh css selectors [<category>] [--json]",
            Arguments =
            [
                new() { Name = "category", Description = "type, class, id, universal, attribute, combinator, pseudo-class or pseudo-element.", Type = "string" },
                JsonArgument
            ],
            Output = "Selector vocabulary with support status and notes.",
            Examples = ["donkeh css selectors --json", "donkeh css selectors pseudo-class --json"],
            Tags = ["css", "selectors", "pseudo-class", "pseudo-element"],
            McpName = "css_selectors",
            WorksOutsideProject = true
        },
        new()
        {
            Name = "css.functions",
            Category = "CSS",
            Summary = "List CSS functions registered by the tokenizer and their argument shapes.",
            Description = "Returns function names and argument descriptions where the engine documents a shape.",
            Usage = "donkeh css functions [--filter <text>] [--json]",
            Arguments =
            [
                new() { Name = "filter", Description = "Substring filter over function names.", Type = "string" },
                JsonArgument
            ],
            Output = "Function names, argument shapes and notes.",
            Examples = ["donkeh css functions --filter gradient --json"],
            Tags = ["css", "functions", "calc", "gradient"],
            McpName = "css_functions",
            WorksOutsideProject = true
        },
        new()
        {
            Name = "css.validate",
            Category = "CSS",
            Summary = "Validate a stylesheet against the Donkeh CSS implementation.",
            Description = "Tokenizes, builds the real data table, checks property support with the resolver and reports selectors the matcher cannot evaluate.",
            Usage = "donkeh css validate <file> [--json]",
            Arguments =
            [
                new() { Name = "file", Description = "Path to a .css file.", Type = "string", Required = true },
                JsonArgument
            ],
            Output = "valid flag and diagnostics with stable codes and source locations.",
            Examples = ["donkeh css validate ./styles.css --json"],
            Tags = ["css", "validate", "diagnostics", "unsupported"],
            McpName = "css_validate",
            WorksOutsideProject = true
        },
        new()
        {
            Name = "renderer.inspect",
            Category = "Renderer",
            Summary = "Show the active renderer and its project configuration.",
            Description = "Detects the renderer from project properties and package references, then returns platform, package, target frameworks and active capabilities.",
            Usage = "donkeh renderer inspect [--project <path>] [--json]",
            Arguments = [ProjectArgument, JsonArgument],
            Output = "Active renderer, platform, package, available renderers and target frameworks.",
            Examples = ["donkeh renderer inspect --json"],
            Tags = ["renderer", "platform", "desktop", "android", "web"],
            McpName = "renderer_inspect"
        },
        new()
        {
            Name = "renderer.capabilities",
            Category = "Renderer",
            Summary = "Show rendering features and limitations for a renderer.",
            Description = "Returns text, image, clipping, transform, animation, scrolling, input, drawing, media and limitation notes. Defaults to the project's active renderer.",
            Usage = "donkeh renderer capabilities [--renderer <desktop|android|web>] [--category <name>] [--project <path>] [--json]",
            Arguments =
            [
                new() { Name = "renderer", Description = "desktop, android or web. Defaults to the active renderer.", Type = "string", Values = ["desktop", "android", "web"] },
                new() { Name = "category", Description = "Limit to one capability category.", Type = "string" },
                ProjectArgument,
                JsonArgument
            ],
            Output = "Capability status and implementation notes per category.",
            Examples = ["donkeh renderer capabilities --json", "donkeh renderer capabilities --renderer web --category drawPrimitives --json"],
            Tags = ["renderer", "capabilities", "limitations", "canvas", "input"],
            McpName = "renderer_capabilities",
            WorksOutsideProject = true
        },
        new()
        {
            Name = "docs.search",
            Category = "Documentation",
            Summary = "Search Donkeh documentation and return the most relevant sections.",
            Description = "Conceptual search over the shipped documentation snapshot. Results include a short explanation, relevant signatures, a minimal example and a targeted retrieval command.",
            Usage = "donkeh docs search <query> [--limit <n>] [--json]",
            Arguments =
            [
                new() { Name = "query", Description = "Search text, for example 'component state management'.", Type = "string", Required = true },
                DocsLimitArgument,
                JsonArgument
            ],
            Output = "Ranked topics and sections with concise excerpts and retrieval commands.",
            Examples = ["donkeh docs search \"component state management\" --json"],
            Tags = ["docs", "search", "documentation", "state management"],
            McpName = "docs_search",
            WorksOutsideProject = true
        },
        new()
        {
            Name = "docs.get",
            Category = "Documentation",
            Summary = "Retrieve one documentation topic or section.",
            Description = "Returns the requested topic, or only one section when --section is supplied.",
            Usage = "donkeh docs get <topic> [--section <id>] [--json]",
            Arguments =
            [
                new() { Name = "topic", Description = "Topic key or title, for example components or state-management.", Type = "string", Required = true },
                new() { Name = "section", Description = "Section id inside the topic.", Type = "string" },
                JsonArgument
            ],
            Output = "Topic metadata, sections, code blocks and API signatures.",
            Examples = ["donkeh docs get components --json", "donkeh docs get using-hooks --section state --json"],
            Tags = ["docs", "topic", "retrieval", "reference"],
            McpName = "docs_get",
            WorksOutsideProject = true
        },
        new()
        {
            Name = "diagnose.project",
            Category = "Diagnostics",
            Summary = "Find project issues using actual files, restore data and validation.",
            Description = "Checks missing dependencies, version mismatches, configuration, CSX files, CSS files, renderer setup and missing references. Only reports established issues.",
            Usage = "donkeh diagnose project [--project <path>] [--json]",
            Arguments = [ProjectArgument, JsonArgument],
            Output = "Status, counts and structured diagnostics.",
            Examples = ["donkeh diagnose project --json"],
            Tags = ["diagnose", "project", "errors", "validation"],
            McpName = "diagnose_project"
        },
        new()
        {
            Name = "diagnose.component",
            Category = "Diagnostics",
            Summary = "Find implementation issues in one component.",
            Description = "Inspects the component source, props, stylesheets, constructor and CSX validation results. Does not generate speculative warnings.",
            Usage = "donkeh diagnose component <name> [--project <path>] [--json]",
            Arguments =
            [
                new() { Name = "name", Description = "Component name.", Type = "string", Required = true },
                ProjectArgument,
                JsonArgument
            ],
            Output = "Component summary, status and structured diagnostics.",
            Examples = ["donkeh diagnose component UserCard --json"],
            Tags = ["diagnose", "component", "validation", "implementation"],
            McpName = "diagnose_component"
        },
        new()
        {
            Name = "doctor",
            Category = "Diagnostics",
            Summary = "Check the Donkeh environment and project health.",
            Description = "Verifies the runtime, project discovery, packages, restore output, CSX availability, documentation, CSS engine, renderers and MCP hosting.",
            Usage = "donkeh doctor [--project <path>] [--json]",
            Arguments = [ProjectArgument, JsonArgument],
            Output = "Environment summary, health status and individual check results.",
            Examples = ["donkeh doctor --json"],
            Tags = ["doctor", "health", "environment", "project"],
            McpName = "doctor",
            WorksOutsideProject = true
        }
    ];
}
