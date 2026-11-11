# LayoutLab benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org/) harness for the LayoutLab CSS
engine. It compiles real stylesheet text through `DataEngine`, builds
deterministic DOM trees and measures layout, style resolution, text wrapping,
paint-list optimisation and the parsing helpers used while resolving CSS.

Every benchmark reports:

- **Mean / Error / StdDev / P95** - wall-clock time per operation.
- **Gen0 / Gen1 / Gen2** - garbage collections per 1,000 operations.
- **Allocated** - managed bytes allocated per operation.
- **Alloc Ratio** - allocation relative to the baseline method.

## Scenes

`LayoutBenchmarks` and `PaintBenchmarks` share the scenes in `Scenes.cs`. The
same stylesheet covers every scene and is compiled once per process.

| Scene | Approximate shape | Exercises |
|-------|-------------------|-----------|
| `Block` | Sections, headings, paragraphs and cards | Block flow, margin collapsing, borders, padding, wrapping text |
| `Flex` | Header, wrapping card grid, footer | Row/column flex, `flex-grow`, wrapping, alignment |
| `Grid` | Eight-column grid with spans and a hero cell | Track sizing, implicit rows, spans, gaps |
| `Paint` | Cards with shadows, gradients, opacity, transforms, blur, overflow and outlines | Display-list building, state scopes, clipping, culling |
| `Mixed` | Fixed header, flex shell, scrolling sidebar and article list | A realistic composition of block, flex, fixed and scroll layout |

`Nodes` is the requested tree size. The actual count is shown by `--profile`
and is generally within a few nodes of the target.

## Benchmark suites

| Class | What it measures |
|-------|------------------|
| `LayoutBenchmarks` | End-to-end frames: warm caches, cold caches, caching disabled, optimisation on/off, compact constants, and a fresh engine per frame. |
| `StyleBenchmarks` | Rule matching, matching plus style resolution, warm vs fresh value cache, isolated leaf/root resolution. |
| `TextBenchmarks` | Normal wrapping, `overflow-wrap: break-word`, no-wrap, `pre-wrap`, ellipsis, and raw `SimpleTextMeasurer` throughput for ASCII and CJK. |
| `PaintBenchmarks` | Optimiser throughput with culling, merging, viewport clipping and constant compaction toggled independently. |
| `LengthParsingBenchmarks` | Parsing and resolving simple and functional lengths (`calc`, `min`, `max`, `clamp`). |
| `ColorParsingBenchmarks` | Hex, `rgb()`, `rgba()`, `hsl()` and named colours, plus alpha blending. |
| `ValueListParsingBenchmarks` | Top-level comma splitting used by shadows, gradients and image lists. |
| `GridTrackParsingBenchmarks` | Track lists, `repeat()`, `minmax()`, `fr` and named lines. |
| `GridPlacementParsingBenchmarks` | Line numbers, `span`, named lines and the four-part `grid-area` form. |
| `GridAreaParsingBenchmarks` | `grid-template-areas` rectangles. |

## Running

Always benchmark in `Release`; Debug results are not meaningful.

```powershell
# Full suite
dotnet run -c Release --project Donkeh/LayoutLab/Benchmarks

# One suite
dotnet run -c Release --project Donkeh/LayoutLab/Benchmarks -- --filter "*LayoutBenchmarks*"
dotnet run -c Release --project Donkeh/LayoutLab/Benchmarks -- --filter "*StyleBenchmarks*"
dotnet run -c Release --project Donkeh/LayoutLab/Benchmarks -- --filter "*TextBenchmarks*"
dotnet run -c Release --project Donkeh/LayoutLab/Benchmarks -- --filter "*PaintBenchmarks*"
dotnet run -c Release --project Donkeh/LayoutLab/Benchmarks -- --filter "*ParsingBenchmarks*"

# Discover case names
dotnet run -c Release --project Donkeh/LayoutLab/Benchmarks -- --list flat

# Quick smoke run (one iteration per case)
dotnet run -c Release --project Donkeh/LayoutLab/Benchmarks -- --filter "*LayoutBenchmarks*" --job Dry
```

`--filter` accepts wildcards and can target a scene, class or method:

```powershell
# One scene and node count
dotnet run -c Release --project Donkeh/LayoutLab/Benchmarks -- `
    --filter "*LayoutBenchmarks*1000*Block*"

# One method
dotnet run -c Release --project Donkeh/LayoutLab/Benchmarks -- `
    --filter "*LayoutBenchmarks.WarmFrame*"
```

The default job is four warmups and eight measured iterations. Change the
`[SimpleJob]` attributes in the benchmark classes for longer, more stable runs.
CSV, Markdown and HTML reports are written to `BenchmarkDotNet.Artifacts/`
under the current working directory.

## Quick profile mode

For a fast, non-statistical overview, `--profile` measures cold frames, warm
frames and optimiser throughput and prints allocations per operation:

```powershell
dotnet run -c Release --project Donkeh/LayoutLab/Benchmarks -- --profile
dotnet run -c Release --project Donkeh/LayoutLab/Benchmarks -- --profile grid
dotnet run -c Release --project Donkeh/LayoutLab/Benchmarks -- --profile grid 4000
```

Example run at 1,000 nodes (AMD Ryzen 5 8400F, 12 logical processors, Windows 10
22H2, .NET 10.0.10). These are one-shot profile numbers, not BenchmarkDotNet
statistics:

| Scene | Nodes | Cold | Warm | Optimise | Instructions |
|-------|------:|-----:|-----:|---------:|-------------:|
| Block |   997 | 35.9 ms / 5.4 MiB |  4.0 ms / 0.6 MiB | 0.063 ms / 51 KiB | 2,159 -> 56 |
| Flex  | 1,001 | 38.9 ms / 5.8 MiB | 12.4 ms / 1.2 MiB | 0.088 ms / 61 KiB | 2,590 -> 204 |
| Grid  | 1,000 | 20.7 ms / 4.5 MiB |  3.2 ms / 1.0 MiB | 0.100 ms / 71 KiB | 2,998 -> 226 |
| Paint | 1,000 | 19.4 ms / 4.0 MiB |  3.1 ms / 0.7 MiB | 0.071 ms / 75 KiB | 3,204 -> 1,132 |
| Mixed | 1,000 |  6.9 ms / 3.5 MiB |  1.5 ms / 0.6 MiB | 0.033 ms / 34 KiB | 1,446 -> 91 |

The optimiser numbers are much smaller than the full frame because the raw
display list is produced once in setup; only the optimisation pass is timed.

## Reading cold vs warm results

- `WarmFrame` reuses the engine's style and text caches. This is the steady-state
  cost of laying out a frame whose DOM and viewport have not changed.
- `ColdFrame` calls `ClearCaches()` first, which also clears parsed-value caches.
  This is the cost after a global invalidation.
- `NoCacheFrame` sets `EnableCaching = false` and shows the value of the caches.
- `NewEngineFrame` constructs a new `LayoutEngine` for every operation, so its
  allocation column includes engine and cache setup.
- `NoOptimiseFrame` skips the display-list optimiser; `CompactConstantsFrame`
  rebuilds the constant pool with only referenced entries.
