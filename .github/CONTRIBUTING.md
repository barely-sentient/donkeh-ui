# Contributing to Donkeh

Thanks for taking the time to contribute. Donkeh is an early-stage project and
is **not stable**: APIs, CSS coverage and rendering behaviour change frequently.
Small, focused, well-tested changes are much easier to review and land than large
rewrites.

This guide covers the practical stuff: building, testing, benchmarking, coding
standards, and how to prepare a bug fix or feature PR.

---

## Before you start

- Read the [README](../README.md), especially the supported CSS surface and
  current limitations.
- Check existing issues and PRs before opening a new one.
- For large changes, open an issue or draft PR first so the direction can be
  agreed before you invest a lot of time.
- Be respectful and constructive in issues, reviews and discussions.

---

## Prerequisites

- **.NET SDK 10.0** (all projects target `net10.0`).
- Windows, Linux or macOS for the engine libraries.
- A desktop with **OpenGL 3.3** if you are working on `Donkeh.Windowing`.
- Optional: Rider or Visual Studio with C# support.

---

## Repository layout and layering

Keep the dependency graph intact:

```text
Donkeh.Parser        no references
Donkeh.Data          ──► Donkeh.Parser
Donkeh.DataEngine    ──► Donkeh.Data, Donkeh.Parser
Donkeh.LayoutLab     ──► Donkeh.Data, Donkeh.DataEngine, Donkeh.Parser
Donkeh.UILab         ──► Donkeh.Data
Donkeh.Inspector     ──► Data, DataEngine, Parser, LayoutLab, UILab
Donkeh.Windowing     ──► Data, DataEngine, Parser, LayoutLab, UILab, Inspector
```

Rules:

- `Donkeh.Parser` must stay dependency-free.
- Do not make `Donkeh.Data` depend on layout or UI code.
- Keep layout and paint host-agnostic: `LayoutLab` emits instructions, it does
  not call GL.
- Do not put Bootstrap-specific or demo-specific workarounds into the engine.
  Fix the generic CSS/layout/matching behaviour instead.

---

## Build and test

```powershell
dotnet restore Donkeh.sln
dotnet build Donkeh.sln -c Release
dotnet test Donkeh.sln -c Release
```

Run a focused subset while iterating:

```powershell
dotnet test Donkeh/LayoutLab/Tests --filter "FullyQualifiedName~FlexLayoutTests"
dotnet test Donkeh/Parser/Tests --filter "FullyQualifiedName~MediaConditionTests"
```

Coverage:

```powershell
dotnet test Donkeh.sln -c Release --collect:"XPlat Code Coverage"
```

The XML documentation build is part of the quality gate:

```powershell
dotnet build Donkeh.sln -c Release -p:GenerateDocumentationFile=true
```

Fix `CS1570`, `CS1572`, `CS1573`, `CS1574` and `CS1734` warnings before opening a
PR.

---

## Coding standards

### General

- Match the style of the surrounding code.
- Keep nullable reference types enabled and avoid `null!` unless unavoidable.
- Prefer small methods and single-responsibility types.
- Do not add `TODO`, `FIXME` or commented-out code without an issue reference.
- Remove debug `Console.WriteLine` calls and temporary instrumentation before
  submitting.

### Documentation

Every public/protected type and member needs XML documentation in the style used
by the existing code:

- `<summary>` for everything.
- `<param>`, `<returns>` and `<exception>` where applicable.
- `<remarks>` for invariants, ownership, lifetime, units and thread-safety.
- `<see cref="..."/>` and `<seealso>` for cross-references.
- Document units explicitly: CSS pixels, device pixels, seconds, degrees, ARGB.

### Performance and allocations

Donkeh is allocation-conscious by design. Hot paths include selector matching,
style resolution, layout, text shaping, paint-list building and GL submission.

- Prefer `ReadOnlySpan<char>`, structs, pooled buffers and reusable scratch
  storage over LINQ, `ToArray()`, `ToList()` and closures in per-node or
  per-frame code.
- Do not add per-frame `new` allocations without a strong reason.
- Cache work keyed by revision, viewport, DPI and interaction state where
  appropriate.
- If a change touches a hot path, run the relevant benchmark in `Release` and
  include before/after numbers in the PR.
- Watch for hidden allocations: boxing, string interpolation in loops, iterator
  methods, `foreach` over interfaces that allocate, and repeated parsing.

### CSS and rendering behaviour

- Support real CSS and generic HTML: no framework-specific hacks in engine code.
- When fixing a rendering bug, fix the cause (selector matching, cascade,
  layout, paint order, hit testing) rather than special-casing a class name.
- Keep style caches correct: if state on one node can restyle a sibling or
  descendant, invalidate or version the cache appropriately.
- Preserve paint order, stacking, clipping and scroll semantics.

---

## Tests

- Add a regression test for every bug fix.
- Add coverage for new behaviour in the most appropriate test project:
  - tokenizer/parser/matcher/compiler → `Donkeh/Parser/Tests`
  - layout/style/paint/text → `Donkeh/LayoutLab/Tests`
  - builder/components/hooks → `Donkeh/UILab/Tests`
- Use the existing `TestSupport` helpers rather than duplicating fixtures.
- Prefer real CSS text through `DataEngine` over hand-built internals.
- Keep tests deterministic; do not depend on wall-clock time, GPU state or
  network access.
- Run the full suite before opening the PR.

---

## Benchmarks

Run benchmarks when changing matching, style resolution, layout, text or paint:

```powershell
dotnet run -c Release --project Donkeh/DataEngine/Benchmarks -- --filter "*"
dotnet run -c Release --project Donkeh/LayoutLab/Benchmarks -- --profile
```

Include:

- method and scene name,
- before/after mean and allocation,
- machine/OS/.NET version if the numbers are meaningful.

Do not commit benchmark result files unless the project already tracks them.

---

## Bug-fix workflow

1. Reproduce the bug with a minimal CSS/DOM fixture.
2. Add a failing test that captures the expected behaviour.
3. Fix the root cause in the lowest layer that owns it.
4. Run the focused test, then the full suite.
5. If the bug affected layout/paint/matching hot paths, benchmark the change.
6. Update the README limitations/supported-surface section if the bug was a
   documented gap.

A good bug-fix PR is small, has a regression test, and explains why the old
behaviour was wrong.

---

## Feature workflow

1. Open an issue or draft PR describing the CSS/API surface and expected
   behaviour.
2. Check whether the feature belongs in Parser, Data, DataEngine, LayoutLab,
   UILab, Windowing or Inspector. Keep it in the lowest layer possible.
3. Add public XML docs and tests.
4. Add benchmarks if the feature is hot-path relevant.
5. Update the README, including limitations if the implementation is partial.
6. Include screenshots or a short recording for visible UI changes.

If a feature is intentionally partial, document exactly what is and is not
implemented rather than leaving it ambiguous.

---

## Pull requests

- Keep PRs focused: one logical change per PR.
- Use the PR template and fill in every relevant section.
- Reference the issue being fixed (`Fixes #123`).
- Make sure CI is green before requesting review.
- Do not mix refactors, formatting-only changes and behaviour changes unless they
  are inseparable.
- Call out breaking changes and migration steps explicitly.
- Be responsive to review comments; push follow-up commits rather than force
  pushing over review history when possible.

### Commit messages

Use a short imperative subject, optionally with a conventional prefix:

```text
fix(flex): include aspect ratio in measured main sizes
feat(grid): support auto-fit track collapsing
docs(readme): document unstable project status
perf(paint): cache SVG intrinsic dimensions
```

Explain the *why* in the body when the change is non-obvious.

---

## Review expectations

Reviewers will check:

- correctness and root-cause fixes,
- test coverage and determinism,
- layering and public API design,
- XML documentation,
- allocation and hot-path impact,
- consistency with existing naming and style,
- README/limitation updates.

Reviews are collaborative. If a suggestion is unclear, ask; if you disagree,
explain the trade-off with evidence (tests, benchmarks, specs).

---

## Packaging and releases

Packaging is tag-driven via `.github/workflows/publish-nuget.yml`. Do not bump
versions or publish manually from a feature branch. Maintainers handle release
tags and NuGet publication.

---

## Getting help

- Open a discussion or issue for design questions.
- For rendering bugs, include:
  - a minimal HTML/CSS fixture,
  - expected vs actual output,
  - platform, .NET version and GPU/OpenGL version if relevant,
  - a screenshot or layout dump where possible.

Thanks for helping make Donkeh better.
