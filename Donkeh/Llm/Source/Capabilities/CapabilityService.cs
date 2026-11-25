using System.Text.Json;
using Donkeh.Llm.Components;
using Donkeh.Llm.Csx;
using Donkeh.Llm.Css;
using Donkeh.Llm.Diagnostics;
using Donkeh.Llm.Docs;
using Donkeh.Llm.Extensibility;
using Donkeh.Llm.Project;
using Donkeh.Llm.Renderers;

namespace Donkeh.Llm.Capabilities;

/// <summary>
/// Implements every built-in capability. The CLI and the MCP server call this
/// type, so both interfaces always share the same behaviour.
/// </summary>
public sealed class CapabilityService
{
    private readonly string _workingDirectory;
    private readonly string? _cliVersion;
    private readonly Func<LlmInvocation, object?>? _scaffold;
    private readonly string? _defaultProjectPath;
    private readonly Dictionary<string, CapabilityRegistry> _registries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CapabilityRegistry> _providerRegistries = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> CommonHtmlProperties = new(StringComparer.Ordinal)
    {
        "Id", "ClassName", "Title", "Href", "Src", "Type", "Value", "Placeholder", "Role",
        "Alt", "Name", "Target", "Rel", "TabIndex", "Disabled", "Checked", "Draggable",
        "Attributes", "Key"
    };

    private static readonly HashSet<string> CommonHtmlEvents = new(StringComparer.Ordinal)
    {
        "OnClick", "OnChange", "OnInput", "OnKeyDown", "OnFocus", "OnBlur", "OnSubmit",
        "OnContextMenu", "OnPointerDown", "OnPointerMove", "OnPointerUp", "OnScroll",
        "OnMouseOver", "OnMouseOut", "OnDragStart", "OnDrop"
    };

    /// <summary>Creates a service for one working directory.</summary>
    /// <param name="workingDirectory">Directory the command was launched from.</param>
    /// <param name="cliVersion">CLI version shown in discovery output.</param>
    /// <param name="scaffold">
    /// Optional component scaffolder supplied by the CLI. Keeping it injectable
    /// avoids duplicating the component generator in the LLM library.
    /// </param>
    /// <param name="defaultProjectPath">
    /// Optional project path used by discovery before an invocation supplies one.
    /// The MCP server passes its <c>--project</c> value here.
    /// </param>
    public CapabilityService(
        string workingDirectory,
        string? cliVersion = null,
        Func<LlmInvocation, object?>? scaffold = null,
        string? defaultProjectPath = null)
    {
        _workingDirectory = Path.GetFullPath(workingDirectory);
        _cliVersion = cliVersion;
        _scaffold = scaffold;
        _defaultProjectPath = defaultProjectPath;
    }

    /// <summary>The discovery catalogue. Providers are not loaded by this property.</summary>
    public CapabilityRegistry Registry => RegistryFor(_defaultProjectPath);

    /// <summary>The catalogue including executable package providers.</summary>
    public CapabilityRegistry ProviderRegistry => ProviderRegistryFor(_defaultProjectPath);

    private CapabilityRegistry RegistryFor(string? projectPath)
    {
        var key = projectPath ?? "";

        if (!_registries.TryGetValue(key, out var registry))
        {
            registry = CapabilityRegistry.Build(_workingDirectory, projectPath);
            _registries[key] = registry;
        }

        return registry;
    }

    private CapabilityRegistry ProviderRegistryFor(string? projectPath)
    {
        var key = projectPath ?? "";

        if (!_providerRegistries.TryGetValue(key, out var registry))
        {
            registry = CapabilityRegistry.Build(_workingDirectory, projectPath, includeProviders: true);
            _providerRegistries[key] = registry;
        }

        return registry;
    }

    /// <summary>Invokes a capability by name.</summary>
    public object? Invoke(string name, LlmInvocation invocation)
    {
        switch (name.ToLowerInvariant())
        {
            case "mcp.show": return McpShow();
            case "mcp.tools": return McpTools();
            case "mcp.capabilities": return McpCapabilities(FirstPositional(invocation, "name"));
            case "project.inspect": return InspectProject(invocation);
            case "project.dependencies": return InspectDependencies(invocation);
            case "project.components": return InspectComponents(invocation);
            case "project.configuration": return InspectConfiguration(invocation);
            case "component.search": return ComponentSearch(invocation);
            case "component.inspect": return ComponentInspect(invocation);
            case "component.scaffold": return ComponentScaffold(invocation);
            case "csx.syntax": return CsxSyntax(invocation);
            case "csx.inspect": return CsxInspect(invocation);
            case "csx.validate": return CsxValidate(invocation);
            case "csx.compile": return CsxCompile(invocation);
            case "css.properties": return CssProperties(invocation);
            case "css.selectors": return CssSelectors(invocation);
            case "css.functions": return CssFunctions(invocation);
            case "css.validate": return CssValidate(invocation);
            case "renderer.inspect": return RendererInspect(invocation);
            case "renderer.capabilities": return RendererCapabilities(invocation);
            case "docs.search": return DocsSearch(invocation);
            case "docs.get": return DocsGet(invocation);
            case "diagnose.project": return DiagnoseProject(invocation);
            case "diagnose.component": return DiagnoseComponent(invocation);
            case "doctor": return DoctorRun(invocation);
        }

        var manifest = RegistryFor(invocation.ProjectPath).FindManifest(name, out var declared);

        if (manifest is not null && declared is not null)
        {
            if (declared.Result is { } result)
            {
                return result.Clone();
            }

            return new { capability = declared.Name, package = manifest.Manifest.Package, value = (object?)null };
        }

        var providerRegistry = ProviderRegistryFor(invocation.ProjectPath);
        var provider = providerRegistry.Providers.FirstOrDefault(candidate =>
            SafeProviderCapabilities(candidate).Any(capability =>
                capability.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

        if (provider is not null)
        {
            return provider.Provider.Invoke(name, invocation);
        }

        throw new LlmException(
            "LLM3001",
            $"Unknown capability '{name}'.",
            "Run 'donkeh mcp capabilities' to list available capability names.");
    }

    /// <summary>Builds the concise discovery overview.</summary>
    public object McpShow()
    {
        var capabilities = Registry.Capabilities.Where(capability => capability.VisibleInShow).ToList();
        var categories = capabilities
            .GroupBy(capability => capability.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => CategoryOrder(group.Key))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                name = group.Key,
                description = CategoryDescription(group.Key),
                capabilities = group.Select(capability => new
                {
                    name = capability.Name,
                    summary = capability.Summary,
                    usage = capability.Usage,
                    mcp = capability.McpName,
                    arguments = capability.Arguments
                        .Where(argument => !argument.Name.Equals("json", StringComparison.OrdinalIgnoreCase))
                        .Select(argument => new
                        {
                            name = argument.Name,
                            required = argument.Required
                        }).ToList(),
                    output = capability.Output
                }).ToList()
            })
            .ToList();

        return new
        {
            tool = "donkeh",
            version = _cliVersion,
            summary = "Donkeh exposes project, component, CSX, CSS, renderer, documentation and diagnostic capabilities through the CLI and MCP.",
            start = "donkeh mcp start",
            categories,
            examples = new[]
            {
                "donkeh inspect project --json",
                "donkeh component search \"file selection\" --json",
                "donkeh csx validate ./Components/Card.cx --json",
                "donkeh css properties --json",
                "donkeh docs search \"state management\" --json"
            },
            notes = new[]
            {
                "All agent-facing commands accept --json. JSON is the default for MCP results.",
                "Use 'donkeh mcp capabilities <name>' for the full interface of one capability.",
                "Use 'donkeh mcp tools --json' for MCP input schemas.",
                "Package provider capabilities are included by 'donkeh mcp tools' and 'donkeh mcp capabilities'.",
                "Targeted flags such as --properties, --events and --examples keep responses small."
            },
            registeredPackages = Registry.Manifests
                .Select(manifest => manifest.Manifest.Package)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            providerPackages = Registry.Manifests
                .Where(manifest => manifest.Manifest.Provider is not null)
                .Select(manifest => manifest.Manifest.Package)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            warnings = Registry.Warnings.Count == 0 ? null : Registry.Warnings
        };
    }

    /// <summary>Lists MCP tools and input schemas.</summary>
    public object McpTools()
    {
        var registry = ProviderRegistry;
        var tools = registry.Capabilities
            .Where(capability => capability.McpName is not null)
            .Select(capability => new
            {
                name = capability.McpName,
                capability = capability.Name,
                title = capability.Summary,
                description = capability.Description ?? capability.Summary,
                inputSchema = BuildInputSchema(capability),
                required = capability.Arguments.Where(argument => argument.Required)
                    .Select(argument => argument.Name)
                    .ToList(),
                output = capability.Output,
                category = capability.Category,
                registeredBy = capability.RegisteredBy
            })
            .ToList();

        return new
        {
            count = tools.Count,
            tools,
            warnings = registry.Warnings.Count == 0 ? null : registry.Warnings
        };
    }

    /// <summary>Lists capabilities or inspects one.</summary>
    public object McpCapabilities(string? name)
    {
        var registry = ProviderRegistry;

        if (!string.IsNullOrWhiteSpace(name))
        {
            var capability = registry.Find(name)
                ?? Registry.Find(name);

            if (capability is null)
            {
                throw new LlmException(
                    "LLM3002",
                    $"Capability '{name}' was not found.",
                    ClosestCapability(name));
            }

            return new { capability = FullCapability(capability) };
        }

        return new
        {
            count = registry.Capabilities.Count,
            capabilities = registry.Capabilities.Select(FullCapability).ToList(),
            registeredPackages = registry.Manifests
                .Select(manifest => manifest.Manifest.Package)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            warnings = registry.Warnings.Count == 0 ? null : registry.Warnings
        };
    }

    private object InspectProject(LlmInvocation invocation)
    {
        var project = Project(invocation);

        return new
        {
            project = new
            {
                name = project.Name,
                file = project.ProjectFile,
                directory = project.ProjectDirectory,
                type = project.ProjectType,
                rootNamespace = project.RootNamespace,
                outputType = project.OutputType,
                targetFrameworks = project.TargetFrameworks,
                donkehVersion = project.DonkehVersion,
                usesLocalCheckout = project.UsesLocalCheckout
            },
            versions = project.DonkehPackages.Select(package => new
            {
                id = package.Id,
                requested = package.RequestedVersion,
                resolved = package.ResolvedVersion,
                kind = package.Kind
            }).ToList(),
            csx = new
            {
                present = project.Csx.Present,
                valid = project.Csx.Valid,
                file = project.Csx.File,
                frameworkHtmlClass = project.Csx.FrameworkHtmlClass,
                frameworkHtmlClassMethod = project.Csx.FrameworkHtmlClassMethod,
                frameworkComponentMethod = project.Csx.FrameworkComponentMethod,
                frameworkFragmentMethod = project.Csx.FrameworkFragmentMethod,
                generatedFileOutput = project.Csx.GeneratedFileOutput
            },
            renderer = new
            {
                name = project.Renderer.Name,
                platform = project.Renderer.Platform,
                package = project.Renderer.Package,
                configured = project.Renderer.Configured
            },
            renderers = project.Renderers.Select(renderer => new
            {
                name = renderer.Name,
                platform = renderer.Platform,
                package = renderer.Package,
                configured = renderer.Configured
            }).ToList(),
            features = project.Features,
            settings = project.Settings,
            stylesheets = project.Stylesheets,
            warnings = project.Warnings.Count == 0 ? null : project.Warnings
        };
    }

    private object InspectDependencies(LlmInvocation invocation)
    {
        var project = Project(invocation);
        var donkeh = project.DonkehPackages.ToList();

        return new
        {
            count = donkeh.Count,
            packages = donkeh.Select(package => new
            {
                id = package.Id,
                requestedVersion = package.RequestedVersion,
                resolvedVersion = package.ResolvedVersion,
                kind = package.Kind
            }).ToList(),
            projectReferences = project.ProjectReferences.Select(reference => new
            {
                include = reference.Include,
                name = reference.Name,
                componentLibrary = reference.IsComponentLibrary,
                exists = reference.FullPath is not null
            }).ToList(),
            summary = new
            {
                donkehPackages = donkeh.Count,
                renderers = donkeh.Count(package => package.Kind == "renderer"),
                tooling = donkeh.Count(package => package.Kind == "tooling"),
                otherPackages = project.Packages.Count - donkeh.Count
            },
            registeredPackages = RegistryFor(invocation.ProjectPath).Manifests
                .Select(manifest => manifest.Manifest.Package)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private object InspectComponents(LlmInvocation invocation)
    {
        var project = Project(invocation);
        var components = LoadComponents(project, invocation.ProjectPath);
        var query = invocation.String("query") ?? FirstPositional(invocation);
        var limit = Math.Clamp(invocation.Int("limit", 10), 1, 200);
        var filtered = ComponentCatalog.Search(components, new ComponentQuery
        {
            Text = query,
            Package = invocation.String("package"),
            Kind = invocation.String("kind"),
            Limit = limit
        });

        return new
        {
            count = filtered.Count,
            total = components.Count,
            components = filtered.Select(ComponentSummary).ToList()
        };
    }

    private object InspectConfiguration(LlmInvocation invocation)
    {
        var project = Project(invocation);

        return new
        {
            project = project.ProjectFile,
            csx = new
            {
                present = project.Csx.Present,
                valid = project.Csx.Valid,
                file = project.Csx.File,
                frameworkHtmlClass = project.Csx.FrameworkHtmlClass,
                frameworkHtmlClassMethod = project.Csx.FrameworkHtmlClassMethod,
                frameworkComponentMethod = project.Csx.FrameworkComponentMethod,
                frameworkFragmentMethod = project.Csx.FrameworkFragmentMethod,
                generatedFileOutput = project.Csx.GeneratedFileOutput
            },
            renderer = new
            {
                name = project.Renderer.Name,
                platform = project.Renderer.Platform,
                package = project.Renderer.Package,
                configured = project.Renderer.Configured
            },
            renderers = project.Renderers.Select(renderer => renderer.Name).ToList(),
            settings = project.Settings,
            stylesheets = project.Stylesheets,
            features = project.Features
        };
    }

    private object ComponentSearch(LlmInvocation invocation)
    {
        var project = Project(invocation);
        var query = FirstPositional(invocation, "query")
            ?? throw new LlmException("LLM3003", "A component search query is required.");
        var limit = Math.Clamp(invocation.Int("limit", 10), 1, 100);
        var components = LoadComponents(project, invocation.ProjectPath);
        var results = ComponentCatalog.Search(components, new ComponentQuery
        {
            Text = query,
            Package = invocation.String("package"),
            Kind = invocation.String("kind"),
            Limit = limit
        });

        return new
        {
            query,
            count = results.Count,
            results = results.Select(component => new
            {
                name = component.Name,
                package = component.Package,
                kind = component.Kind,
                description = component.Description,
                tags = component.Tags.Take(8).ToList(),
                properties = component.Properties.Count,
                events = component.Events.Count,
                inspect = $"donkeh component inspect {component.Name} --json"
            }).ToList()
        };
    }

    private object ComponentInspect(LlmInvocation invocation)
    {
        var project = Project(invocation);
        var name = FirstPositional(invocation, "name")
            ?? throw new LlmException("LLM3004", "A component name is required.");
        var components = LoadComponents(project, invocation.ProjectPath);
        var component = components.FirstOrDefault(item =>
                            item.Name.Equals(name, StringComparison.Ordinal))
                        ?? components.FirstOrDefault(item =>
                            item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (component is null)
        {
            var suggestions = ComponentCatalog.Search(components, new ComponentQuery
            {
                Text = name,
                Limit = 5
            });

            throw new LlmException(
                "LLM3005",
                $"Component '{name}' was not found.",
                suggestions.Count == 0
                    ? "Run 'donkeh inspect components --json' to list available components."
                    : "Try: " + string.Join(", ", suggestions.Select(item => item.Name)));
        }

        var onlyProperties = invocation.Flag("properties");
        var onlyEvents = invocation.Flag("events");
        var onlyExamples = invocation.Flag("examples");
        var targeted = onlyProperties || onlyEvents || onlyExamples;

        if (onlyProperties)
        {
            return new
            {
                component = component.Name,
                propsType = component.PropsType,
                properties = component.Properties
            };
        }

        if (onlyEvents)
        {
            return new
            {
                component = component.Name,
                events = component.Events
            };
        }

        if (onlyExamples)
        {
            return new
            {
                component = component.Name,
                examples = component.Examples
            };
        }

        var source = invocation.Flag("source") ? ReadComponentSource(project, component) : null;
        var compactHtml = component.Kind == "html-element";
        var properties = compactHtml
            ? component.Properties.Where(property => CommonHtmlProperties.Contains(property.Name)).ToList()
            : component.Properties;
        var events = compactHtml
            ? component.Events.Where(eventInfo => CommonHtmlEvents.Contains(eventInfo.Name)).ToList()
            : component.Events;

        return new
        {
            component = new
            {
                name = component.Name,
                @namespace = component.Namespace,
                package = component.Package,
                kind = component.Kind,
                baseType = component.BaseType,
                description = component.Description,
                source = component.Source,
                line = component.Line,
                propsType = component.PropsType,
                supportsChildren = component.SupportsChildren,
                supportsState = component.SupportsState,
                lifecycle = component.Lifecycle,
                dependencies = component.Dependencies,
                examples = component.Examples,
                stylesheets = component.Stylesheets,
                propertyCount = component.Properties.Count,
                eventCount = component.Events.Count,
                propertiesComplete = !compactHtml
            },
            properties,
            events,
            source,
            availableProperties = compactHtml
                ? $"donkeh component inspect {component.Name} --properties --json"
                : null
        };
    }

    private object ComponentScaffold(LlmInvocation invocation)
    {
        var name = FirstPositional(invocation, "name")
            ?? throw new LlmException("LLM3006", "A component name is required.");
        var project = Project(invocation);
        var dryRun = invocation.Flag("dryRun");

        if (dryRun)
        {
            var className = Identifiers.ComponentName(name);
            var useCsx = invocation.Flag("csx") || project.Csx.Present;
            var extension = useCsx ? ".cx" : ".cs";
            var directory = Path.Combine(project.ProjectDirectory, "src", "Components", className);
            var files = new[]
            {
                Path.Combine(directory, className + extension),
                Path.Combine(directory, className + ".css")
            };

            return new
            {
                component = className,
                project = project.ProjectFile,
                dryRun = true,
                files = files.Select(file => Path.GetRelativePath(project.ProjectDirectory, file)
                    .Replace('\\', '/')).ToList(),
                next = "Run without --dry-run to create the files."
            };
        }

        if (_scaffold is null)
        {
            throw new LlmException("LLM3007", "Component scaffolding is not available in this host.");
        }

        return _scaffold(invocation)
            ?? throw new LlmException("LLM3007", "The component scaffolder returned no result.");
    }

    private object CsxSyntax(LlmInvocation invocation)
    {
        var category = FirstPositional(invocation, "category");
        var topics = CsxSyntaxCatalog.Get(category);

        if (!string.IsNullOrWhiteSpace(category) && topics.Count == 0)
        {
            throw new LlmException(
                "LLM3008",
                $"Unknown CSX syntax category '{category}'.",
                "Available categories: " + string.Join(", ", CsxSyntaxCatalog.Get().Select(topic => topic.Category)));
        }

        return new
        {
            count = topics.Count,
            topics
        };
    }

    private object CsxInspect(LlmInvocation invocation)
    {
        var file = RequireFile(invocation, "file");
        var result = CsxAnalyzer.Analyze(file);

        if (invocation.Flag("summary"))
        {
            return new
            {
                file = result.File,
                valid = result.Valid,
                summary = result.Summary,
                components = result.Components,
                dependencies = result.Dependencies,
                referencedTypes = result.ReferencedTypes,
                diagnostics = result.Diagnostics
            };
        }

        if (invocation.Flag("full"))
        {
            return result;
        }

        const int preview = 20;

        return new
        {
            file = result.File,
            valid = result.Valid,
            summary = result.Summary,
            components = result.Components,
            dependencies = result.Dependencies,
            referencedTypes = result.ReferencedTypes,
            diagnostics = result.Diagnostics,
            elements = result.Elements.Take(preview).ToList(),
            expressions = result.Expressions.Take(preview).ToList(),
            truncated = result.Elements.Count > preview || result.Expressions.Count > preview,
            fullCommand = $"donkeh csx inspect \"{result.File}\" --full --json"
        };
    }

    private object CsxValidate(LlmInvocation invocation)
    {
        var file = RequireFile(invocation, "file");
        var result = CsxValidator.Validate(file, _workingDirectory, invocation.ProjectPath);

        return new
        {
            file = result.File,
            valid = result.Valid,
            errorCount = result.ErrorCount,
            warningCount = result.WarningCount,
            diagnostics = result.Diagnostics
        };
    }

    private object CsxCompile(LlmInvocation invocation)
    {
        var file = RequireFile(invocation, "file");

        return CsxCompiler.Compile(
            file,
            _workingDirectory,
            invocation.ProjectPath,
            includeGenerated: invocation.Flag("generated"),
            includeSourceMap: invocation.Flag("maps"),
            includeResolved: invocation.Flag("resolved", true));
    }

    private object CssProperties(LlmInvocation invocation)
    {
        var name = FirstPositional(invocation, "name");
        var all = CssCatalog.Properties();

        if (!string.IsNullOrWhiteSpace(name))
        {
            var property = all.FirstOrDefault(item =>
                item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (property is null)
            {
                var suggestions = Closest(name, all.Select(item => item.Name)).ToList();
                throw new LlmException(
                    "LLM3009",
                    $"CSS property '{name}' is not supported by the Donkeh resolver.",
                    suggestions.Count == 0 ? null : "Near matches: " + string.Join(", ", suggestions));
            }

            return new
            {
                property = property.Name,
                supported = property.Supported,
                status = property.Status,
                notes = property.Notes,
                usedBy = property.UsedBy,
                values = property.Values
            };
        }

        var filter = invocation.String("filter");
        var limit = Math.Clamp(invocation.Int("limit", 100), 1, 300);
        var properties = all
            .Where(property => filter is null
                || property.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();

        return new
        {
            count = properties.Count,
            total = all.Count,
            properties
        };
    }

    private object CssSelectors(LlmInvocation invocation)
    {
        var category = FirstPositional(invocation, "category") ?? invocation.String("category");
        var selectors = CssCatalog.Selectors()
            .Where(item => category is null
                || item.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new
        {
            count = selectors.Count,
            selectors
        };
    }

    private object CssFunctions(LlmInvocation invocation)
    {
        var filter = FirstPositional(invocation, "filter") ?? invocation.String("filter");
        var functions = CssCatalog.Functions()
            .Where(item => filter is null
                || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new
        {
            count = functions.Count,
            functions
        };
    }

    private object CssValidate(LlmInvocation invocation)
    {
        var file = RequireFile(invocation, "file");
        return CssValidator.Validate(file);
    }

    private object RendererInspect(LlmInvocation invocation)
    {
        var project = Project(invocation);
        return RendererCatalog.Inspect(project);
    }

    private object RendererCapabilities(LlmInvocation invocation)
    {
        var category = invocation.String("category");
        var rendererName = invocation.String("renderer");
        DonkehProject? project = null;

        if (string.IsNullOrWhiteSpace(rendererName))
        {
            try
            {
                project = Project(invocation);
                rendererName = project.Renderer.Name;
            }
            catch (LlmException)
            {
                // Capability discovery works outside a project.
            }
        }

        if (string.IsNullOrWhiteSpace(rendererName))
        {
            var all = RendererCatalog.All().Select(SetSummary).ToList();

            return new
            {
                active = (string?)null,
                renderers = all
            };
        }

        var renderer = RendererCatalog.Find(rendererName)
            ?? throw new LlmException(
                "LLM3010",
                $"Unknown renderer '{rendererName}'.",
                "Available renderers: " + string.Join(", ", RendererCatalog.All().Select(item => item.Name)));

        return new
        {
            renderer = renderer.Name,
            platform = renderer.Platform,
            package = project?.Renderer.Package ?? renderer.Packages.FirstOrDefault(),
            description = renderer.Description,
            capabilities = category is null
                ? renderer.Capabilities
                : renderer.Capabilities
                    .Where(pair => pair.Key.Equals(category, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(pair => pair.Key, pair => pair.Value)
        };
    }

    private object DocsSearch(LlmInvocation invocation)
    {
        var query = FirstPositional(invocation, "query")
            ?? throw new LlmException("LLM3011", "A documentation search query is required.");
        var limit = Math.Clamp(invocation.Int("limit", 5), 1, 20);
        var results = DocsIndex.Search(AllDocs(invocation.ProjectPath), query, limit);

        return new
        {
            query,
            count = results.Count,
            results
        };
    }

    private object DocsGet(LlmInvocation invocation)
    {
        var topicName = FirstPositional(invocation, "topic")
            ?? throw new LlmException("LLM3012", "A documentation topic is required.");
        var topics = AllDocs(invocation.ProjectPath);
        var topic = topics.FirstOrDefault(candidate =>
                        candidate.Topic.Equals(topicName, StringComparison.OrdinalIgnoreCase)
                        || candidate.Title.Equals(topicName, StringComparison.OrdinalIgnoreCase))
                    ?? topics.FirstOrDefault(candidate =>
                        candidate.Topic.Contains(topicName, StringComparison.OrdinalIgnoreCase)
                        || candidate.Title.Contains(topicName, StringComparison.OrdinalIgnoreCase));

        if (topic is null)
        {
            throw new LlmException(
                "LLM3013",
                $"Documentation topic '{topicName}' was not found.",
                "Available topics: " + string.Join(", ", topics.Select(candidate => candidate.Topic)));
        }

        var section = invocation.String("section");

        if (string.IsNullOrWhiteSpace(section))
        {
            return topic;
        }

        var match = topic.Sections.FirstOrDefault(candidate =>
            candidate.Id.Equals(section, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new LlmException(
                "LLM3014",
                $"Section '{section}' was not found in '{topic.Topic}'.",
                "Available sections: " + string.Join(", ", topic.Sections.Select(candidate => candidate.Id)));
        }

        return new
        {
            topic = topic.Topic,
            title = topic.Title,
            summary = topic.Summary,
            reference = topic.Reference,
            section = match
        };
    }

    private object DiagnoseProject(LlmInvocation invocation) =>
        ProjectDiagnostics.Diagnose(_workingDirectory, invocation.ProjectPath);

    private object DiagnoseComponent(LlmInvocation invocation)
    {
        var name = FirstPositional(invocation, "name")
            ?? throw new LlmException("LLM3015", "A component name is required.");

        return ComponentDiagnostics.Diagnose(name, _workingDirectory, invocation.ProjectPath);
    }

    private object DoctorRun(LlmInvocation invocation) =>
        Donkeh.Llm.Diagnostics.Doctor.Run(_workingDirectory, _cliVersion, invocation.ProjectPath);

    private List<ComponentMetadata> LoadComponents(DonkehProject project, string? projectPath)
    {
        var components = ComponentCatalog.Load(project);
        var names = components.Select(component => component.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contributed = ManifestRegistry.Components(project, _workingDirectory)
            .Where(component => names.Add(component.Name))
            .ToList();

        if (contributed.Count > 0)
        {
            var index = components.FindIndex(component => component.Kind == "html-element");

            if (index < 0)
            {
                index = components.Count;
            }

            components.InsertRange(index, contributed);
        }

        return components;
    }

    private DonkehProject Project(LlmInvocation invocation) =>
        ProjectInspector.Read(invocation.WorkingDirectory, invocation.ProjectPath);

    private static string RequireFile(LlmInvocation invocation, string name)
    {
        return FirstPositional(invocation, name)
            ?? invocation.String(name)
            ?? throw new LlmException("LLM3016", $"A file path is required.");
    }

    private static string? FirstPositional(LlmInvocation invocation, string? argumentName = null)
    {
        if (invocation.Positionals.Count > 0)
        {
            return invocation.Positionals[0];
        }

        return argumentName is null ? null : invocation.String(argumentName);
    }

    private static object ComponentSummary(ComponentMetadata component) => new
    {
        name = component.Name,
        package = component.Package,
        kind = component.Kind,
        description = component.Description,
        source = component.Source,
        line = component.Line,
        propsType = component.PropsType,
        properties = component.Properties.Count,
        events = component.Events.Count,
        supportsChildren = component.SupportsChildren,
        supportsState = component.SupportsState
    };

    private static object SetSummary(RendererCapabilitySet renderer) => new
    {
        name = renderer.Name,
        platform = renderer.Platform,
        packages = renderer.Packages,
        description = renderer.Description,
        capabilities = renderer.Capabilities
    };

    private static string? ReadComponentSource(DonkehProject project, ComponentMetadata component)
    {
        if (component.Source is null
            || component.Kind is not ("class" or "csx"))
        {
            return null;
        }

        var path = Path.IsPathRooted(component.Source)
            ? component.Source
            : Path.GetFullPath(Path.Combine(project.ProjectDirectory, component.Source));

        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static object FullCapability(LlmCapability capability) => new
    {
        name = capability.Name,
        mcpName = capability.McpName,
        category = capability.Category,
        summary = capability.Summary,
        description = capability.Description,
        usage = capability.Usage,
        arguments = capability.Arguments,
        output = capability.Output,
        examples = capability.Examples,
        tags = capability.Tags,
        registeredBy = capability.RegisteredBy,
        providerBacked = capability.IsProviderBacked,
        worksOutsideProject = capability.WorksOutsideProject,
        mcp = capability.McpName is not null
    };

    /// <summary>Builds the MCP JSON input schema for a capability.</summary>
    public static JsonElement BuildInputSchema(LlmCapability capability)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var required = new List<string>();

        foreach (var argument in capability.Arguments.Where(argument => !argument.Name.Equals("json", StringComparison.OrdinalIgnoreCase)))
        {
            properties[argument.Name] = argument.Type switch
            {
                "integer" => new
                {
                    type = "integer",
                    description = argument.Description,
                    @default = argument.Default is null ? null : int.TryParse(argument.Default, out var number) ? number : (object?)null
                },
                "boolean" => new
                {
                    type = "boolean",
                    description = argument.Description,
                    @default = argument.Default is null ? null : bool.TryParse(argument.Default, out var flag) ? flag : (object?)null
                },
                _ => new
                {
                    type = "string",
                    description = argument.Description,
                    @enum = argument.Values
                }
            };

            if (argument.Required)
            {
                required.Add(argument.Name);
            }
        }

        return JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties,
            required,
            additionalProperties = false
        }, LlmJson.Options);
    }

    private static string CategoryDescription(string category) => category switch
    {
        "MCP" => "Discovery and MCP server operations.",
        "Project" => "Project overview, dependencies, components and configuration.",
        "Components" => "Component search, inspection and scaffolding.",
        "CSX" => "CSX syntax, structure, validation and compilation.",
        "CSS" => "Supported properties, selectors, functions and CSS validation.",
        "Renderer" => "Active renderer selection and rendering capabilities.",
        "Documentation" => "Targeted documentation search and retrieval.",
        "Diagnostics" => "Project and component diagnostics plus environment health.",
        _ => "Capabilities contributed by installed packages."
    };

    private static int CategoryOrder(string category) => category switch
    {
        "MCP" => 0,
        "Project" => 1,
        "Components" => 2,
        "CSX" => 3,
        "CSS" => 4,
        "Renderer" => 5,
        "Documentation" => 6,
        "Diagnostics" => 7,
        _ => 8
    };

    private static string? ClosestCapability(string name)
    {
        var candidates = BuiltInCapabilities.All().Select(capability => capability.Name).ToList();
        var matches = Closest(name, candidates).ToList();
        return matches.Count == 0 ? null : "Available capabilities include: " + string.Join(", ", matches);
    }

    private static IEnumerable<string> Closest(string value, IEnumerable<string> candidates)
    {
        return candidates
            .Select(candidate => (Candidate: candidate, Distance: Levenshtein(value, candidate)))
            .Where(item => item.Distance <= Math.Max(1, value.Length / 3))
            .OrderBy(item => item.Distance)
            .Take(3)
            .Select(item => item.Candidate);
    }

    private static int Levenshtein(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= right.Length; j++)
            {
                var cost = char.ToLowerInvariant(left[i - 1]) == char.ToLowerInvariant(right[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private List<DocsTopic> AllDocs(string? projectPath)
    {
        var topics = new List<DocsTopic>(DocsIndex.Topics());

        foreach (var manifest in RegistryFor(projectPath).Manifests)
        {
            foreach (var doc in manifest.Manifest.Docs)
            {
                topics.Add(new DocsTopic
                {
                    Topic = doc.Topic,
                    Title = doc.Title,
                    Summary = doc.Summary,
                    Reference = doc.Reference,
                    Sections = doc.Sections.Select(section => new DocsSection
                    {
                        Id = section.Id,
                        Title = section.Title,
                        Text = section.Text,
                        Code = section.Code
                    }).ToList(),
                    ApiSignatures = doc.ApiSignatures,
                    Examples = doc.Examples
                });
            }
        }

        return topics;
    }

    private static IEnumerable<LlmCapability> SafeProviderCapabilities(LoadedProvider provider)
    {
        try
        {
            return provider.Provider.GetCapabilities() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
