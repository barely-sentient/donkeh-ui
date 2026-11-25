using System.Text.Json;
using Donkeh.Llm;
using Donkeh.Llm.Capabilities;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Donkeh.Cli;

/// <summary>Hosts the Donkeh capability catalogue over MCP stdio.</summary>
internal static class McpServerHost
{
    /// <summary>Starts the server and blocks until the client disconnects.</summary>
    public static int Run(
        string? projectPath,
        string version,
        Func<LlmInvocation, object?> scaffold)
    {
        try
        {
            return RunAsync(projectPath, version, scaffold).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAsync(
        string? projectPath,
        string version,
        Func<LlmInvocation, object?> scaffold)
    {
        var service = new CapabilityService(Environment.CurrentDirectory, version, scaffold, projectPath);
        var registry = service.ProviderRegistry;
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "donkeh",
                Title = "Donkeh",
                Version = version
            },
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability { ListChanged = false }
            },
            ServerInstructions =
                "Donkeh exposes project, component, CSX, CSS, renderer, documentation and diagnostic capabilities. " +
                "Start with project_inspect or component_search, then use targeted tools to avoid loading unnecessary data.",
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) =>
                {
                    var tools = registry.Capabilities
                        .Where(capability => capability.McpName is not null)
                        .Select(capability => new Tool
                        {
                            Name = capability.McpName!,
                            Title = capability.Summary,
                            Description = capability.Description ?? capability.Summary,
                            InputSchema = CapabilityService.BuildInputSchema(capability)
                        })
                        .ToList();

                    return ValueTask.FromResult(new ListToolsResult { Tools = tools });
                },
                CallToolHandler = (context, _) => ValueTask.FromResult(CallTool(service, registry, context, projectPath))
            }
        };

        var transport = new StdioServerTransport("donkeh");
        await using var server = McpServer.Create(transport, options, loggerFactory: null, serviceProvider: null);
        await server.RunAsync(CancellationToken.None);
        return 0;
    }

    private static CallToolResult CallTool(
        CapabilityService service,
        CapabilityRegistry registry,
        RequestContext<CallToolRequestParams> context,
        string? defaultProjectPath)
    {
        var parameters = context.Params;
        var capability = registry.Capabilities.FirstOrDefault(item =>
            string.Equals(item.McpName, parameters.Name, StringComparison.OrdinalIgnoreCase));

        if (capability is null)
        {
            return Error("MCP4041", $"Unknown MCP tool '{parameters.Name}'.");
        }

        try
        {
            var invocation = CreateInvocation(parameters, capability, defaultProjectPath);
            var result = service.Invoke(capability.Name, invocation);
            var element = JsonSerializer.SerializeToElement(result, LlmJson.Options);

            return new CallToolResult
            {
                StructuredContent = element,
                Content =
                [
                    new TextContentBlock
                    {
                        Text = JsonSerializer.Serialize(element, LlmJson.Options)
                    }
                ],
                IsError = false
            };
        }
        catch (LlmException exception)
        {
            return Error(exception.Code, exception.Message, exception.Hint);
        }
        catch (Exception exception)
        {
            return Error("MCP9999", exception.Message);
        }
    }

    private static LlmInvocation CreateInvocation(
        CallToolRequestParams parameters,
        LlmCapability capability,
        string? defaultProjectPath)
    {
        var arguments = parameters.Arguments is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : parameters.Arguments.ToDictionary(
                pair => pair.Key,
                pair => (object?)pair.Value,
                StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();

        foreach (var argument in capability.Arguments.Where(argument => argument.Required))
        {
            if (parameters.Arguments is not null
                && parameters.Arguments.TryGetValue(argument.Name, out var value))
            {
                var text = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => value.ToString()
                };

                if (!string.IsNullOrWhiteSpace(text))
                {
                    positionals.Add(text);
                }
            }
        }

        var projectPath = parameters.Arguments is not null
            && parameters.Arguments.TryGetValue("project", out var project)
            && project.ValueKind == JsonValueKind.String
            ? project.GetString()
            : defaultProjectPath;

        return new LlmInvocation
        {
            WorkingDirectory = Environment.CurrentDirectory,
            ProjectPath = projectPath,
            Positionals = positionals,
            Arguments = arguments
        };
    }

    private static CallToolResult Error(string code, string message, string? hint = null)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            error = new { code, message, hint }
        }, LlmJson.Options);

        return new CallToolResult
        {
            StructuredContent = payload,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(payload, LlmJson.Options)
                }
            ],
            IsError = true
        };
    }
}
