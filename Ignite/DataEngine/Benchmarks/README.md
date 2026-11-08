# Ignite CSS engine benchmarks

BenchmarkDotNet harness for the parse → build → query pipeline. It generates a
deterministic, realistic stylesheet and scales it to 1M, 4M and 16M characters
(multi-megabyte real-world sizes). A second corpus repeats the same selectors
and conditions to measure de-duplication/interning rather than uniqueness.

The corpus exercises flat rules, selector lists, descendants, `>`/`+`/`~`
combinators, attribute and pseudo selectors, custom properties, nested rules,
`@media`/`@supports`/`@container`, `@layer` and `@keyframes`.

## Running

```powershell
# End-to-end tokenize / build
dotnet run -c Release --project Ignite/DataEngine/Benchmarks -- --filter "*ParseAndBuildBenchmarks*"

# Repeated-selector interning/dedup
dotnet run -c Release --project Ignite/DataEngine/Benchmarks -- --filter "*RepeatedSelectorBenchmarks*"

# Renderer-style lookups and condition matching
dotnet run -c Release --project Ignite/DataEngine/Benchmarks -- --filter "*QueryBenchmarks*"

# One-shot cold/warm report with table shape (no BDN statistics)
dotnet run -c Release --project Ignite/DataEngine/Benchmarks -- --profile
dotnet run -c Release --project Ignite/DataEngine/Benchmarks -- --profile 16000000
```

Results and CSV/HTML output are written to `BenchmarkDotNet.Artifacts/` in the
current working directory. Jobs use 4 warmups and 6 measured iterations; edit
the `[SimpleJob]` attributes in `Benchmarks.cs` for longer runs.

## Corpus shapes

| Target | Chars | Tokens | Selectors | Rules | Conditions |
|-------:|------:|-------:|----------:|------:|-----------:|
| 1M     | 1,000,350 | 313,690 | 19,762 | 37,050 | 3,706 |
| 4M     | 4,000,590 | 1,254,506 | 79,026 | 148,170 | 14,818 |
| 16M    | 16,000,740 | 5,017,516 | 316,066 | 592,620 | 59,263 |

Repeated-selector corpus keeps the same rule counts but collapses to 18
selectors and 4 conditions.

## Measured results

Hardware: AMD Ryzen 5 8400F 4.20 GHz, 12 logical / 6 physical cores, Windows 10
22H2, .NET 10.0.10. Values are BDN means.

### Realistic CSS — parse and build

| Method | Size | Mean | CSS MB/s | Alloc/op |
|--------|-----:|-----:|---------:|---------:|
| Tokenize          |  1M |   6.486 ms | 154.2 | 0 B |
| Build_FromString  |  1M |  14.089 ms |  71.0 | 18.2 MiB |
| Build_FromTokens  |  1M |   5.830 ms | 171.6 | 12.1 MiB |
| Tokenize          |  4M |  28.935 ms | 138.3 | 0 B |
| Build_FromString  |  4M |  56.394 ms |  70.9 | 72.2 MiB |
| Build_FromTokens  |  4M |  26.454 ms | 151.2 | 49.3 MiB |
| Tokenize          | 16M | 117.316 ms | 136.4 | 0 B |
| Build_FromString  | 16M | 248.283 ms |  64.4 | 282.5 MiB |
| Build_FromTokens  | 16M | 108.778 ms | 147.1 | 191.0 MiB |

`Build_FromTokens` is the engine-only path (token list already supplied);
`Build_FromString` is the complete tokenize + build pipeline. Tokenization
itself is allocation free.

### Repeated selectors — interning/dedup path

| Method | Size | Mean | CSS MB/s | Alloc/op |
|--------|-----:|-----:|---------:|---------:|
| Tokenize          |  4M |  22.20 ms | 180.2 | 0 B |
| Build_FromString  |  4M |  42.85 ms |  93.4 | 72.0 MiB |
| Build_FromTokens  |  4M |  17.85 ms | 224.2 | 49.1 MiB |
| Tokenize          | 16M |  92.76 ms | 172.5 | 0 B |
| Build_FromString  | 16M | 175.42 ms |  91.2 | 281.8 MiB |
| Build_FromTokens  | 16M |  68.32 ms | 234.2 | 190.3 MiB |

### Query path (4M realistic table)

| Method | Mean | Alloc/op |
|--------|-----:|---------:|
| RuleScan                       | 126.5 µs | 0 B |
| SelectorLookup                 | 250.8 µs | 0 B |
| ConditionMatch_Cached          | 168.3 µs | 0 B |
| ConditionMatch_AfterInvalidate | 187.1 µs | 0 B |

`SelectorLookup` visits each distinct discriminator once, which is the query
pattern a renderer uses. Every query path is allocation free.

### Cold vs warm full build (`--profile`)

| Corpus | 1M cold | 1M warm | 4M cold | 4M warm | 16M cold | 16M warm |
|--------|--------:|--------:|--------:|--------:|---------:|---------:|
| Realistic | 148 ms | 71 ms | 90 ms | 79 ms | 367 ms | 325 ms |
| Repeated  |  70 ms | 35 ms | 58 ms | 61 ms | 249 ms | 237 ms |

Cold includes one-time type initialisation and process-wide text interning.
A 16M CSS document is fully parsed and query-ready in roughly 0.25–0.37 s on
this machine.
