# Donkeh LLM integration

`Donkeh.Llm` is the shared implementation behind the CLI's agent-facing
commands and the MCP server. It provides project/component inspection, CSX and
CSS validation, renderer capability discovery, documentation search,
diagnostics, and a package capability registry.

## Public capability surface

| Capability | CLI | MCP tool |
| --- | --- | --- |
| `mcp.show` | `donkeh mcp show` | — |
| `mcp.tools` | `donkeh mcp tools` | — |
| `mcp.capabilities` | `donkeh mcp capabilities [name]` | — |
| `mcp.invoke` | `donkeh mcp invoke <capability>` | — |
| `project.inspect` | `donkeh inspect project` | `project_inspect` |
| `project.dependencies` | `donkeh inspect dependencies` | `dependencies_inspect` |
| `project.components` | `donkeh inspect components` | `components_list` |
| `project.configuration` | `donkeh inspect configuration` | `configuration_inspect` |
| `component.search` | `donkeh component search` | `component_search` |
| `component.inspect` | `donkeh component inspect` | `component_inspect` |
| `component.scaffold` | `donkeh component scaffold` | `component_scaffold` |
| `csx.syntax` | `donkeh csx syntax` | `csx_syntax` |
| `csx.inspect` | `donkeh csx inspect` | `csx_inspect` |
| `csx.validate` | `donkeh csx validate` | `csx_validate` |
| `csx.compile` | `donkeh csx compile` | `csx_compile` |
| `css.properties` | `donkeh css properties` | `css_properties` |
| `css.selectors` | `donkeh css selectors` | `css_selectors` |
| `css.functions` | `donkeh css functions` | `css_functions` |
| `css.validate` | `donkeh css validate` | `css_validate` |
| `renderer.inspect` | `donkeh renderer inspect` | `renderer_inspect` |
| `renderer.capabilities` | `donkeh renderer capabilities` | `renderer_capabilities` |
| `docs.search` | `donkeh docs search` | `docs_search` |
| `docs.get` | `donkeh docs get` | `docs_get` |
| `diagnose.project` | `donkeh diagnose project` | `diagnose_project` |
| `diagnose.component` | `donkeh diagnose component` | `diagnose_component` |
| `doctor` | `donkeh doctor` | `doctor` |

All CLI commands accept `--json`. MCP results are always structured JSON and
include both `content` text and `structuredContent`.

## Package registration contract

A package contributes capabilities without changing the central CLI. Add a
`donkeh.llm.json` file to the package root (or to the consuming project as
`.donkeh/llm.json`). The CLI discovers it from `project.assets.json` package
roots and from project-local `.donkeh/llm/` folders.

```json
{
  "schemaVersion": 1,
  "package": "Acme.Donkeh.FileExplorer",
  "displayName": "Acme File Explorer",
  "capabilities": [
    {
      "name": "acme.fileexplorer.components",
      "category": "Components",
      "summary": "List file explorer components and view modes.",
      "description": "Optional longer explanation.",
      "usage": "donkeh mcp capabilities acme.fileexplorer.components",
      "output": "Component names and supported view modes.",
      "arguments": [
        {
          "name": "viewMode",
          "type": "string",
          "description": "Optional view-mode filter.",
          "required": false
        }
      ],
      "examples": ["donkeh mcp capabilities acme.fileexplorer.components --json"],
      "tags": ["file", "explorer", "selection"],
      "result": { "components": ["FileExplorer"], "viewModes": ["List", "Grid"] },
      "mcp": true,
      "visibleInShow": true,
      "worksOutsideProject": false
    }
  ],
  "components": [
    {
      "name": "FileExplorer",
      "namespace": "Acme.Donkeh.FileExplorer",
      "kind": "assembly",
      "description": "A file browser with selection support.",
      "propsType": "FileExplorerProps",
      "properties": [
        { "name": "ViewMode", "type": "string", "default": "\"List\"", "description": "List, Grid or Details." }
      ],
      "events": [
        { "name": "OnSelectionChanged", "type": "Action<IReadOnlyList<string>>", "eventArgsType": "IReadOnlyList<string>" }
      ],
      "supportsChildren": false,
      "supportsState": true,
      "examples": ["<FileExplorer ViewMode=\"Grid\" />"],
      "tags": ["file", "explorer", "selection"]
    }
  ],
  "docs": [
    {
      "topic": "file-explorer",
      "title": "File Explorer",
      "summary": "Package documentation for the file explorer.",
      "sections": [
        { "id": "overview", "title": "Overview", "text": "FileExplorer supports List, Grid and Details view modes." }
      ]
    }
  ]
}
```

### Static capabilities

A manifest capability with `result` returns that JSON without executing package
code. This is the recommended way to publish component metadata, documentation,
examples and package inspection data. Names must use the package's first
namespace segment as their first dot-separated segment (for example
`Acme.FileExplorer` may register `acme.fileexplorer.*`) and must not collide
with built-in namespaces (`mcp`, `project`, `component`, `csx`, `css`,
`renderer`, `docs`, `diagnose`, `doctor`). Duplicate names are ignored and
reported as a warning.

### Executable capabilities

A package that needs code supplies a provider implementation and registers it
explicitly:

```json
{
  "schemaVersion": 1,
  "package": "Acme.Donkeh.FileExplorer",
  "provider": {
    "assembly": "llm/Acme.Donkeh.FileExplorer.Llm.dll",
    "type": "Acme.Donkeh.FileExplorer.FileExplorerLlmProvider"
  }
}
```

```csharp
using Donkeh.Llm;

public sealed class FileExplorerLlmProvider : ILlmCapabilityProvider
{
    public string Package => "Acme.Donkeh.FileExplorer";

    public IEnumerable<LlmCapability> GetCapabilities() =>
    [
        new LlmCapability
        {
            Name = "acme.fileexplorer.inspect",
            McpName = "acme_fileexplorer_inspect",
            Category = "Components",
            Summary = "Inspect file explorer configuration.",
            Usage = "donkeh mcp capabilities acme.fileexplorer.inspect",
            Output = "Resolved view modes and selection options."
        }
    ];

    public object? Invoke(string capabilityName, LlmInvocation invocation) =>
        new { viewModes = new[] { "List", "Grid", "Details" } };
}
```

Providers are loaded only when a manifest explicitly declares one, only from a
relative path inside the manifest directory, and only for MCP discovery or when
their capability is invoked. The registry does not execute shell commands,
does not read environment secrets, and does not bypass normal application trust
boundaries. A provider runs with the same permissions as the user running the
CLI, so only install packages you trust.

## Token efficiency rules

- Summary first, targeted flags for detail.
- Component inspection omits implementation source unless `--source` is used.
- HTML element inspection returns common properties by default; use
  `--properties` for the complete Props surface.
- CSX compile returns generated C#, source maps and resolved bindings only when
  requested.
- Validators return stable diagnostic codes and one-based source locations.
- `docs search` returns at most five concise results by default; `docs get`
  supports `--section` to avoid loading a whole page.

## Security boundaries

- Project configuration inspection reads only selected MSBuild properties,
  `csx.json`, package references and restore metadata. It never reads arbitrary
  environment variables or credential files.
- Assembly component discovery uses `System.Reflection.Metadata`; it does not
  load inspected assemblies.
- Provider assemblies are the only third-party code loaded by the LLM
  extension, and only after an explicit manifest declaration.
- Package capability names cannot override built-ins or another package's
  registered MCP tool name.
