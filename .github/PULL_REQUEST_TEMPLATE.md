## Summary

<!-- What does this PR change, and why? Keep it short. -->

## Type of change

- [ ] Bug fix
- [ ] New feature
- [ ] Performance / allocation improvement
- [ ] Refactor (no behaviour change)
- [ ] Documentation
- [ ] Tests / benchmarks
- [ ] Breaking change
- [ ] Build / CI / tooling

## Related issue

<!-- e.g. Fixes #123, Closes #456, Part of #789 -->

## What changed

<!--
List the concrete changes. For engine changes, name the layer:
Parser / Data / DataEngine / LayoutLab / UILab / Windowing / Inspector / Demo.
-->

-
-
-

## Why this approach

<!--
Explain the root cause for bug fixes, or the design for features.
Call out alternatives you considered and why you rejected them.
-->

## How it was tested

<!-- Include commands and the specific tests you ran. -->

```powershell
dotnet build Donkeh.sln -c Release
dotnet test Donkeh.sln -c Release
```

- [ ] Added or updated a regression test
- [ ] Ran the full test suite
- [ ] Ran a focused test subset:

```powershell
dotnet test <project> --filter "FullyQualifiedName~<Class>"
```

### For rendering / layout / paint changes

- [ ] Added a minimal fixture that reproduces the issue
- [ ] Compared expected vs actual output
- [ ] Attached a screenshot or layout dump

## Performance impact

<!-- Required for matching, style, layout, text or paint changes. -->

- Benchmark(s) run:
- Before:
- After:
- Allocation impact:
- [ ] No measurable regression
- [ ] Benchmark numbers included above

## Documentation

- [ ] XML documentation added/updated for public/protected members
- [ ] README updated (supported surface, limitations, behaviour)
- [ ] No documentation changes needed

## Breaking changes

<!-- If checked, describe the migration path. -->

- [ ] This PR contains a breaking change

## Checklist

- [ ] The change is focused on one logical concern
- [ ] The code follows the existing style and layering rules
- [ ] `Donkeh.Parser` still has no project references
- [ ] No Bootstrap-specific or demo-specific hacks were added to engine code
- [ ] No new per-frame or per-node allocations were introduced without justification
- [ ] Tests are deterministic (no wall clock, network or GPU dependency)
- [ ] Debug logging / temporary instrumentation was removed
- [ ] XML documentation builds without warnings
- [ ] The README was updated if behaviour or limitations changed
- [ ] CI is green

## Notes for reviewers

<!-- Anything reviewers should focus on, known gaps, follow-up work, etc. -->
