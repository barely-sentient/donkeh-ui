# CssDebug

Standalone inspector for the engine pipeline. Point it at a local file, stdin or
a remote stylesheet and it prints the flattened selector and rule tables that
`DataEngine.Build` produces.

Conditions are intentionally not displayed: they are opaque compiled
`ConditionFunction` delegates supplied by the host. Rules are shown after
selector-list flattening, nesting expansion and adjacent-duplicate removal.

## Usage

```powershell
dotnet run -c Release --project Ignite/DataEngine/Debugger -- <file|url|-> [options]
```

Build once and run the produced executable directly if preferred:

```powershell
dotnet build Ignite/DataEngine/Debugger -c Release
.\Ignite\DataEngine\Debugger\bin\Release\net10.0\CssDebug.exe .\site.css
```

### Sources

| Source | Example |
|--------|---------|
| Local file | `CssDebug .\styles\site.css` |
| Remote URL | `CssDebug https://example.com/site.css` |
| stdin | `Get-Content .\site.css -Raw \| CssDebug -` |

Remote loading follows redirects, sends a user agent and decompresses
gzip/deflate/brotli. The `charset` from the response content type is honoured,
falling back to UTF-8. Source files must fit the tokenizer limit of 16,777,215
characters.

### Options

| Option | Description |
|--------|-------------|
| `--selectors` | Show the interned selector table. |
| `--rules` | Show the flattened rule table. |
| `--summary` | Counts only. |
| `--all` | Print every row (same as `--limit 0`). |
| `--limit <n>` | Rows per section. Default `50`; `0` means unlimited. |
| `--json` | Machine-readable output. |
| `--no-color` | Disable ANSI colours. |
| `-h`, `--help` | Help. |

With no section flags both selectors and rules are shown. Output is limited to
50 rows per section by default so multi-megabyte files stay usable; the summary
always reports the true totals.

## Output

```text
CSS breakdown
  source      C:\styles\site.css
  characters  1,000,350
  tokens      313,690
  selectors   19,762
  rules       37,050
  build time  13.7 ms  (16.97 MB/s)  -- tokenize 6.6 ms + build 7.0 ms
  allocated   17.8 MB
  GC (0/1/2)  0/0/0

Selectors (19,762 total, showing 50)
      id    rules  tokens  discriminator                 selector
       0        1       2  Identifier ".c0000001"         .c0000001
       1        2       5  Identifier ".c0000003"         .c0000002:hover

Rules (37,050 total, showing 50)
      id  selector id  property             value                          selector
       0            0  color                #1a2b3c                        .c0000001
       1            1  color                red                            .c0000002:hover
...
```

Use `--json` for the same data as structured `selectorItems` and `ruleItems`
arrays, including per-selector rule counts and discriminators.
