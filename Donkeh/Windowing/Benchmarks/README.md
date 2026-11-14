# Donkeh.Windowing.Benchmarks

A small hidden-window harness for the OpenGL display-list renderer. It builds
synthetic instruction streams, renders them through both submission paths, and
reports CPU build/submit time, GPU completion time, draw calls, indirect
commands and instance counts.

```powershell
dotnet run --project Donkeh/Windowing/Benchmarks -c Release
dotnet run --project Donkeh/Windowing/Benchmarks -c Release -- --count 40000 --iterations 40
```

`--count` overrides the item count for every workload; `--iterations` controls
the measured frames after warm-up. The window is hidden, so the harness needs a
GPU and a working OpenGL driver but no visible desktop session.

## Workloads

| Workload | Shape |
| --- | --- |
| `rects` | 10,000 plain rectangles, no state changes |
| `clipped` | 10,000 rectangles, each inside its own push/pop clip |
| `transformed` | 10,000 rectangles, each with a different translation |
| `text` | 4,000 text runs (64,000 glyph instances in practice) |
| `mixed` | 4,000 rounded rects + text runs + rules |

`Direct` submits the same GPU instance buffer with one
`glDrawArraysInstanced` per batch. `Indirect` uploads one command per batch and
submits with `glMultiDrawArraysIndirect`. Both paths use the same geometry,
shader and per-instance state, so the comparison isolates submission strategy.

## Machine

Windows, NVIDIA GeForce RTX 4080, driver `610.74`, OpenGL 3.3 core context with
`GL_ARB_draw_indirect`, `GL_ARB_multi_draw_indirect`, `GL_ARB_base_instance` and
`GL_ARB_buffer_storage`. Measurements are single-run wall-clock averages over 60
frames and are indicative rather than statistically rigorous.

## Before and after

The "before" column is the previous run-based renderer on the same instruction
streams; "after" is the indirect path. CPU is `Submit + Flush` time and GPU is
`Flush + glFinish`. Draw calls before are the run count produced by the old
texture/clip/transform batching.

| Workload | Instances | Before CPU | Before GPU | Before draws | After CPU | After GPU | After draws |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `rects` | 10,000 | 0.45 ms | 0.58 ms | 1 | 0.41 ms | 0.77 ms | 1 |
| `clipped` | 10,000 | 19.58 ms | 20.85 ms | ~10,000 | 1.29 ms | 0.74 ms | 1 |
| `transformed` | 10,000 | 16.83 ms | 19.96 ms | ~10,000 | 0.39 ms | 0.79 ms | 1 |
| `text` | 64,000 | 20.61 ms | 24.15 ms | 1 | 1.34 ms | 2.74 ms | 1 |
| `mixed` | 56,000 | 18.69 ms | 35.42 ms | ~12,000 | 1.28 ms | 2.44 ms | 1 |

GPU numbers vary by a few tenths of a millisecond between runs and include the
frame clear; the unclipped rectangle workload is already draw-call-light, so its
GPU time is unchanged within noise. The CPU column is the meaningful submission
measurement.

Submission stays small as the scene grows: the clipped workload submits in about
0.01 ms at 1,000 items, 0.04 ms at 10,000 and 0.20 ms at 40,000, while the
remaining CPU time is linear instance construction. Use `--count` to reproduce
those points.

Text improved most after adding the shaped-run cache in `GLRenderer` and the
per-face scale cache in `FontManager`: the same glyph run is no longer looked up,
kerned and measured on every frame.

The important change is scaling: clip and transform changes no longer create a
draw call each, so the clipped and transformed workloads drop from thousands of
runs to one command. Text remains dominated by glyph lookup and atlas upload,
not submission; the indirect path still removes the separate per-run uploads.

The same harness is the place to add future GPU work (compute culling and
command generation) and to compare against new drivers or larger scenes.

## Why compute culling is not enabled yet

A 2D UI frame composites in strict paint order, and the current architecture
already collapses it to one indirect call. Compacting the instance list on the
GPU would require an ordered prefix sum so overlapping translucent layers keep
their order, and would then force the vertex shader to gather instance records
through an SSBO instead of fetching them as instanced attributes. For the scene
sizes here the GPU is not the bottleneck; the remaining CPU cost is glyph
lookup and instance construction, which culling does not remove. The command
buffer is already shaped for GPU-generated commands (`baseInstance` selects the
stream slot, `instanceCount` selects the range), so a compute compaction pass
can be added later without changing the shader interface or the draw path.
