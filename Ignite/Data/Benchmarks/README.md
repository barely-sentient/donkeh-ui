# Ignite Data selector benchmarks

BenchmarkDotNet harness for the `DataTable` matcher, driven by the **real
Bootstrap 5.3.8 CDN stylesheet** and a browser-shaped DOM. It complements the
`Ignite/DataEngine/Benchmarks` project, which focuses on parse/build throughput
with generated corpora; this project focuses on selector matching against real
CSS and realistic markup.

## Corpus

* `bootstrap.min.css` — 232,108 chars, vendored from
  `https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/css/bootstrap.min.css`.
  The CDN URL is recorded in `BootstrapCss.CdnUrl`; the file is copied to the
  output so normal runs are reproducible and offline.
* `PageTree` builds `html > head/body`, a navbar, a hero, a footer and **N**
  repeated component sections in between. Each section contains a card, a list
  group, a striped table and a form, with the classes, ids and attributes
  Bootstrap actually targets (`data-bs-toggle`, `aria-*`, `role`, `type`,
  `href`, ...). Text nodes are omitted because the engine matches elements only.

| `Sections` | Elements | Shape |
|-----------:|---------:|-------|
| 8          | 592      | a few components |
| 512        | 35,872   | a large page |
| 4,096      | 286,752  | stress test |

The tree is rebuilt per benchmark case in `[GlobalSetup]`, never inside the
measured method.

## Benchmarks

### `ParseBootstrapBenchmarks`

| Method | What it measures |
|---|---|
| `Tokenize` (baseline) | tokenizer only |
| `Build_FromString` | tokenize + `DataEngine.Build`, the complete pipeline |
| `Build_FromTokens` | `DataEngine.Build` from a pre-tokenized list |

### `MatchBootstrapBenchmarks`

| Method | What it measures |
|---|---|
| `MatchAll_Array` (baseline) | every element using the allocating `MatchRules(node)` overload |
| `MatchAll_Span` | every element using the reusable-buffer span overload |
| `MatchAll_Span_WithConditions` | full pass with a `RendererContext` |
| `MatchAll_Span_ConditionsInvalidated` | full pass after `RendererContext.Invalidate()` |
| `MatchHotLeaf_Span` | one heavily matched `.btn-primary` (51 rules in Bootstrap) |
| `MatchColdLeaf_Span` | a `<title>` element, which should reject nearly the whole table |

Conditions are compiled with `Condition.AlwaysTrueCompiler`, the same setup as
the DataEngine benchmarks: this measures the matcher and the condition-cache
path, not media-query evaluation.

## Running

```powershell
# Parse/build the CDN stylesheet
dotnet run -c Release --project Ignite/Data/Benchmarks -- --filter "*ParseBootstrapBenchmarks*"

# Matching at 8, 512 and 4096 sections
dotnet run -c Release --project Ignite/Data/Benchmarks -- --filter "*MatchBootstrapBenchmarks*"

# One method across all section counts
dotnet run -c Release --project Ignite/Data/Benchmarks -- --filter "*MatchAll_Span*"

# Fast stopwatch sanity check, no BenchmarkDotNet statistics
dotnet run -c Release --project Ignite/Data/Benchmarks -- --smoke 64

# Validate the BenchmarkDotNet wiring without a full run
dotnet run -c Release --project Ignite/Data/Benchmarks -- --filter "*ParseBootstrapBenchmarks*" --job dry
```

Results (CSV/Markdown/HTML) are written to `BenchmarkDotNet.Artifacts/` in the
current working directory. Jobs use 3 warmups and 5 measured iterations; adjust
the `[SimpleJob]` attributes in `ParseBootstrapBenchmarks.cs` and
`MatchBootstrapBenchmarks.cs` for longer runs.

Set `IGNITE_BOOTSTRAP_CSS` to a local file to benchmark a different Bootstrap
build. When the vendored file is missing, `BootstrapCss` downloads the same CDN
URL once and caches it under `%TEMP%/ignite-css-benchmarks`.

> The project file is named `Ignite.Data.Benchmarks.csproj` (not
> `Benchmarks.csproj`) because BenchmarkDotNet locates the generated project by
> assembly name, and the repository already has an `Ignite.DataEngine`
> benchmark project named `Benchmarks`.

## Sample numbers

Approximate, from a Release smoke run (`--smoke`) and a BenchmarkDotNet dry run
on an AMD Ryzen 5 8400F, Windows 10 22H2, .NET 10.0.10. Use a full BDN run for
real statistics.

### Parse (BDN, 3 warmup / 5 iterations)

| Method | Mean | Allocated |
|---|---:|---:|
| `Tokenize` | 1.55 ms | 1.33 MB |
| `Build_FromString` | 2.91 ms | 3.75 MB |
| `Build_FromTokens` | 1.28 ms | 2.42 MB |

### Match (BDN for the hot node; smoke for the full-page passes)

| Method | Sections | Mean | Allocated |
|---|---:|---:|---:|
| `MatchHotLeaf_Span` | 8 | 2.72 µs | 0 B |
| `MatchHotLeaf_Span` | 512 | 2.67 µs | 0 B |
| `MatchHotLeaf_Span` | 4,096 | 2.78 µs | 0 B |

| Sections | Elements | ms/pass | ns/element | Matches/pass |
|---:|---:|---:|---:|---:|
| 8 | 592 | 0.75 | 1,267 | 6,655 |
| 4,096 | 286,752 | 370.9 | 1,294 | 3,068,567 |

The hot `.btn-primary` matches 51 Bootstrap rules. The cold `<title>` node
rejects nearly the whole table in about 0.83 µs (smoke). Per-element cost stays
flat as the document grows, which is the candidate-index design working as
intended.
