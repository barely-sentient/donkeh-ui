# Donkeh

> [!WARNING]
> **Donkeh is still in development and is very much not stable.**
> APIs, rendering behaviour and CSS coverage change frequently, several engine
> areas are incomplete or approximated, and the demo is a moving target. Expect
> bugs, regressions and breaking changes. Do not use this for production work
> yet.

Donkeh is a **declarative-first UI framework for .NET 10**, It is not a CSS
engine. It ships one. Around that engine it provides a React-like development
flow (components, hooks, context and keyed reconciliation), built-in devtools
and performance profiling, and an "as close to a browser as possible" experience
with C# in place of JavaScript.

The bundled CSS engine parses real stylesheets, interns them into a flat table,
matches selectors against a DOM-like tree, computes the cascade, lays out boxes
(block, inline, flex, grid, table and floats), and renders a compact display
list with OpenGL 3.3 through Silk.NET.

Donkeh is split into small, single-responsibility assemblies so a host can use
only the layers it needs: the dependency-free CSS parser, selector matcher and
stylesheet compiler, a layout/paint engine, a declarative view layer, a native
window host, and a devtools inspector.

```text
 CSS text ──► Donkeh.Parser.Tokenizer ──► tokens
                                            │
                                            ▼
                                 Donkeh.DataEngine.CssStreamParser
                                            │
                                            ▼
                             Donkeh.Data.DataTable (interned, flat)
                                            │
 DOM tree ──► Donkeh.Data.DataTable.MatchRules
                                            │
                                            ▼
                          Donkeh.LayoutLab.StyleResolver ──► ComputedStyle
                                            │
                                            ▼
                          Donkeh.LayoutLab.LayoutEngine
                          block / inline / flex / grid / table / float / text
                                            │
                                            ▼
                     LayoutBox tree + InstructionBuffer + ConstantPool
                                            │
                                            ▼
                          Donkeh.LayoutLab.Optimiser (cull/merge/compact)
                                            │
                                            ▼
                          Donkeh.Windowing.GLRenderer ──► window
```

`Donkeh.UILab` sits above that pipeline: it produces the DOM-like tree, patches
it in place, and lets a host mount components with hooks. `Donkeh.Windowing`
owns the native window, font/image caches, input routing and the OpenGL
renderer. `Donkeh.Inspector` provides the devtools panel.

---

## Table of contents

- [Design goals](#design-goals)
- [Repository layout](#repository-layout)
- [Modules](#modules)
  - [Donkeh.Parser](#Donkehparser)
  - [Donkeh.Data](#Donkehdata)
  - [Donkeh.DataEngine](#Donkehdataengine)
  - [Donkeh.LayoutLab](#Donkehlayoutlab)
  - [Donkeh.UILab](#Donkehuilab)
  - [Donkeh.Windowing](#Donkehwindowing)
  - [Donkeh.Inspector](#Donkehinspector)
  - [Tooling projects](#tooling-projects)
- [Getting started](#getting-started)
- [End-to-end example](#end-to-end-example)
- [Testing](#testing)
- [Benchmarks](#benchmarks)
- [Documentation standard](#documentation-standard)
- [Performance model](#performance-model)
- [Supported CSS surface and limitations](#supported-css-surface-and-limitations)
- [Packaging and release](#packaging-and-release)
- [Contributing](#contributing)

---

## Design goals

| Goal | How the codebase achieves it |
| --- | --- |
| **Declarative first** | UI is a function of state: components, hooks, context, keyed children and in-place reconciliation (`Donkeh.UILab`) sit on top of the CSS engine rather than the other way around. |
| **A real CSS engine** | A full tokenizer, selector matching with specificity and source order, cascade layers, custom properties, `!important`, shorthand expansion, media/container/supports conditions, keyframes and per-property animations. |
| **Browser-like, C#-native** | Real CSS, form controls, text editing and `:hover`/`:active`/`:focus` state without a JavaScript runtime — the host writes C# instead. |
| **Built-in devtools** | The inspector provides a live DOM tree, style editor, colour picker, animation browser and frame/layout/paint profiling. |
| **Low steady-state cost** | Interned strings and dense integer ids; flat arenas instead of per-rule objects; intrusive hash buckets; reusable instruction and constant buffers; style, text and box caches keyed by revision and viewport. |
| **Pay only for what you use** | Seven independent assemblies with a strictly layered dependency graph. `Donkeh.Parser` has no references at all; a server-side CSS tool can stop after `Donkeh.Data`. |
| **Allocation-conscious hot paths** | `ReadOnlySpan<char>` tokenizer entry points, span-based selector matching, value-type tokens/instructions, pooled buffers, and no per-frame object graph. |
| **Host-agnostic core** | Layout and paint produce data (`LayoutResult`, `InstructionBuffer`), not draw calls. The GL renderer is one consumer; tests consume the same instructions. |
| **First-class documentation** | Every public/protected type and member carries XML documentation with summary, remarks, parameters, return values and exceptions. See [Documentation standard](#documentation-standard). |

---

## Repository layout

```text
.
├── DonkehCSS.sln
├── README.md                        ← this file
├── .github/workflows/publish-nuget.yml
└── Donkeh/
    ├── Directory.Build.props        ← shared NuGet metadata
    ├── Parser/
    │   ├── Source/                  ← Donkeh.Parser
    │   ├── Tests/                   ← tokenizer + matcher + compiler tests
    │   └── Benchmark/               ← tokenizer throughput
    ├── Data/
    │   ├── Source/                  ← Donkeh.Data
    │   └── Benchmarks/              ← Bootstrap 5 matcher benchmarks
    ├── DataEngine/
    │   ├── Source/                  ← Donkeh.DataEngine
    │   ├── Benchmarks/              ← parse/build/query benchmarks
    │   └── Debugger/                ← CssDebug CLI
    ├── LayoutLab/
    │   ├── Source/                  ← Donkeh.LayoutLab
    │   ├── Tests/                   ← layout, style, paint, text tests
    │   └── Benchmarks/              ← frame/style/text/paint benchmarks
    ├── UILab/
    │   ├── Source/                  ← Donkeh.UILab
    │   └── Tests/                   ← builder and component tests
    ├── Windowing/
    │   └── Source/                  ← Donkeh.Windowing
    └── Inspector/
        └── Source/                  ← Donkeh.Inspector
```

### Dependency graph

```text
Donkeh.Parser            no references
  ├─► Donkeh.Data
  │     └─► Donkeh.DataEngine
  ├─► Donkeh.LayoutLab   ──► Donkeh.Data
  └─► Donkeh.UILab       ──► Donkeh.Data

Donkeh.Inspector         ──► Donkeh.Data, Donkeh.DataEngine, Donkeh.Parser,
                             Donkeh.LayoutLab, Donkeh.UILab

Donkeh.Windowing         ──► Donkeh.Data, Donkeh.DataEngine, Donkeh.Parser,
                             Donkeh.LayoutLab, Donkeh.UILab, Donkeh.Inspector
```

A useful way to read this: a CSS-only tool needs `Donkeh.Parser` +
`Donkeh.Data` + `Donkeh.DataEngine`; a headless layout/layout-test harness adds
`Donkeh.LayoutLab`; a desktop application adds `Donkeh.UILab` +
`Donkeh.Windowing`; `Donkeh.Inspector` is optional on top.

All projects target **`net10.0`**, enable nullable reference types and implicit
usings, and are deterministic. `Donkeh.LayoutLab` and `Donkeh.Windowing` allow
unsafe blocks for performance-critical memory access.

---

## Modules

### Donkeh.Parser

**Summary.** A dependency-free CSS tokenizer and an IL-compiled exact-keyword
matcher. It turns CSS text into a compact `List<Token>` and is the only module
with no project references.

**Responsibilities**

- Scan CSS into tokens while preserving source positions.
- Classify property names, functions, pseudo classes/elements, numbers,
  dimensions, percentages, hashes, strings, at-keywords and punctuation.
- Intern every identifier so downstream comparisons are integer comparisons.
- Precompute an exact-match function for all known property/function/pseudo
  names, generated as one small IL method per first character.

**Key types**

| Type | Purpose |
| --- | --- |
| `Tokenizer` | Static entry point: `Parse(string)`, `Parse(ReadOnlySpan<char>)`, `Parse(span, List<Token>)`, and an allocation-free `Parse(ref ReadOnlySpan<char>)`. |
| `Token` | Compact 12-byte value type: `Kind`, `InternedId`, and a packed `Location` (24-bit start index + 8-bit length). |
| `TokenKind` | The lexical categories. |
| `InternTable` | Process-wide string interner used by tokens, rules and the DOM. |
| `RuleBuilder` | Registration API used by the static initializer (`Exact`, `CssPropertyName`, `CssPropertyGroup`, `CssPseudoClass`, `CssPseudoElement`, `Function`). |
| `Compiler` / `ILGeneratorWrapper` | Emits the `Func<ReadOnlySpan<char>, int>` exact matcher at type initialization. |

**Usage**

```csharp
using Donkeh.Parser;
using Donkeh.Parser.Objects;

List<Token> tokens = Tokenizer.Parse(css);

foreach (Token token in tokens)
{
    if (token.Kind == TokenKind.PropertyName)
    {
        string name = InternTable.GetString(token.InternedId)!;
        string text = css[token.StartIndex..token.EndIndex];
    }
}
```

**Remarks**

- Whitespace and comments are skipped, but every token keeps its original start
  and end index so callers can recover the exact source slice.
- `Token.Location` packs a 24-bit start and an 8-bit length into one `Int32`.
  A single token is therefore limited to 255 characters; longer lexemes are
  emitted as multiple tokens. The source limit is 16,777,215 characters.
- The exact matcher is **case-sensitive**; register the canonical CSS spelling.
- The string intern table is process-wide, lock-protected and never evicts.
  This is ideal for stylesheets (bounded vocabulary) and hostile to unbounded
  user-generated strings; do not feed arbitrary text into it.
- `MinLengthByFirstChar` lets the lexer reject short identifiers before calling
  the compiled matcher.

**Tests**

| Project | Class | Tests | Focus |
| --- | --- | --- | --- |
| `Donkeh/Parser/Tests` | `TokenizerTests` | 7 | Declarations, property/function disambiguation, pseudo selectors, whitespace/comments, numbers/dimensions/percentages, span overload, exact-rule round-trip. |

**Benchmarks.** `Donkeh/Parser/Benchmark` measures raw tokenizer throughput.

---

### Donkeh.Data

**Summary.** The stylesheet data model and selector matcher. `DataTable` stores
selectors, declarations and conditions once, as dense integer ids and flat
arenas, and matches a DOM-like node against them without allocating.

**Responsibilities**

- Store interned selector token runs, rules and condition trees.
- Store at-rule records (imports, namespaces, layers, scopes, keyframes,
  descriptors, margin boxes, charsets) for hosts and tooling.
- Match selectors against `DonkehSupportedDomNode` with full cascade ordering
  (specificity, source order, layers, conditions).
- Cache condition evaluation per `(RendererContext, version)`.
- Provide the mutable DOM adapter consumed by layout, UILab and tests.

**Key types**

| Type | Purpose |
| --- | --- |
| `DataTable` | Flat, interned stylesheet: selectors, rules, conditions and at-rule records. |
| `Rule` | A declaration: selector id, property id, value id, condition id, layer id. |
| `SelectorRecord` | A selector's token slice, specificity, flags and plan offsets. |
| `Condition` | A condition prelude, its parent chain and compiled executor. |
| `MatchedRule` | One matched declaration plus its specificity counts, returned by the matcher. |
| `DonkehSupportedDomNode` | The DOM node the matcher, layout engine and UILab share. |
| `RendererContext` | Viewport/orientation snapshot used by media and container queries. |
| `StreamContext` | Parser-side block/selector stack used by `CssStreamParser`. |
| `AttributeFilter`, `SelectorPlan`, `SelectorAnalysis` | Pre-parsed attribute filters, compound plans and structural pseudo records. |

**Usage**

```csharp
using Donkeh.Data;
using Donkeh.Data.Dom;

var node = new DonkehSupportedDomNode { TagName = "div" };
node.Id = "card";
node.AddClass("card");
node.AddClass("card--featured");
node.SetAttribute("data-state", "open");

var table = dataEngine.Build(css);
MatchedRule[] matches = table.MatchRules(node, rendererContext);

// Reuse a caller-owned buffer to avoid allocating per node:
var buffer = new MatchedRule[64];
int total = table.MatchRules(node, rendererContext, buffer);
```

Rules arrive **weakest-first**: apply them in order, then apply `!important`
declarations in a second pass. The matcher already accounts for specificity,
source order and cascade layers, so the resolver does not sort.

**Remarks**

- Selector and rule ids are dense and stable for the lifetime of the table.
- Candidate lookup uses the selector's right-most *discriminator* (tag, id,
  class or attribute), so a node only probes selectors that could match it.
  Selectors whose subject is a structural pseudo class are kept in a small
  unkeyed list and verified explicitly.
- `Condition` results are stamped by `RendererContext.Version`; an unchanged
  environment evaluates each condition once.
- The span-based `MatchRules` overload and the reusable `MatchedRule[]` overload
  exist for render loops. The array-returning overload is for convenience.
- A `DataTable` is not thread-safe for concurrent mutation; matching is
  read-only and safe once construction finishes.

**Tests**

| Project | Class | Tests | Focus |
| --- | --- | --- | --- |
| `Donkeh/Parser/Tests` | `MatchingTests` | 18 | Tag/id/class/universal selectors, specificity and source order, every attribute operator, quoted values and `i` flag, empty substring values, unsupported constructs, condition filtering, span overflow, combinators, backtracking, structural and `of-type` pseudos, `An+B`, pseudo specificity. |
| `Donkeh/LayoutLab/Tests` | `InteractionPseudoTests` | 5 | `:hover`, `:focus`, `:focus-visible`, `:focus-within`, `:checked`, `:disabled`, `:link`, text folding. |

**Benchmarks.** `Donkeh/Data/Benchmarks` runs the matcher against the real
Bootstrap 5.3.8 stylesheet and browser-shaped DOMs from 592 to 286,752 elements.

---

### Donkeh.DataEngine

**Summary.** The CSS compiler. It streams a token list into a `DataTable`,
handles at-rules and nesting, flattens selector lists, de-duplicates selectors
and conditions, and compiles condition preludes into predicates.

**Responsibilities**

- Parse rule lists, declarations, nested rules and selector lists.
- Recognise `@import`, `@namespace`, `@layer`, `@scope`, `@media`, `@supports`,
  `@container`, `@keyframes`, descriptors, margin boxes and `@charset`.
- Recover from malformed input without aborting the whole stylesheet.
- Intern selectors and conditions, including parent-condition chains.
- Compile media queries through an injectable `ConditionCompiler`.

**Key types**

| Type | Purpose |
| --- | --- |
| `DataEngine` | Public facade: `Build(string)`, `Build(List<Token>)`, `Build(tokens, table)`. |
| `CssStreamParser` | Streaming token-to-`DataTable` parser; owns the nested block state. |
| `MediaConditionCompiler` | Token-level compiler for CSS media queries; exposes a stateless `Instance` delegate. |
| `ConditionCompiler` / `ConditionFunction` | The injection point for `@supports`, `@container` and custom conditions. |

**Usage**

```csharp
using Donkeh.DataEngine;

// Media queries enabled; @supports/@container are unsupported and fail closed.
var engine = new DataEngine(MediaConditionCompiler.Instance);
var table = engine.Build(cssText);

// Append a second stylesheet into the same table:
engine.Build(Tokenizer.Parse(secondSheet), table);
```

**Remarks**

- The constructor defaults to `Condition.AlwaysFalseCompiler`: an unsupported
  condition is **not** applied. Pass `Condition.AlwaysTrueCompiler` only for
  tests or pipelines with no condition compiler.
- `Build(List<Token>, DataTable)` appends to an existing table, which lets a host
  merge user-agent, author and inspector stylesheets while keeping one lookup
  structure.
- `MediaConditionCompiler` supports width/height features, media types,
  orientation, comma-separated OR lists and `not`. Unknown features fail closed.
- At-rule records (`Imports`, `Layers`, `Scopes`, `Keyframes`, ...) are exposed
  for tooling; matching currently consults the rule/condition data, not
  `@scope` boundaries or `@import` resolution.
- Parser exceptions are contained where recovery is possible; a failing custom
  `ConditionCompiler` is also treated as `false`.

**Tests**

| Project | Class | Tests | Focus |
| --- | --- | --- | --- |
| `Donkeh/Parser/Tests` | `DataEngineTests` | 11 | Descendant/compound distinction, selector de-duplication, whitespace around combinators, executor sharing, condition caching, compiler failure, condition gaps, lexical normalisation, malformed nesting, long keyframe names/stops. |
| `Donkeh/Parser/Tests` | `MediaConditionTests` | 6 | `min-width`, fractional Bootstrap breakpoints, combined features, media types/orientation, comma OR lists, unsupported features fail closed. |

**Tooling.** `Donkeh/DataEngine/Debugger` (`CssDebug`) prints selectors, rules
and pipeline timings from a file, URL or stdin; `Donkeh/DataEngine/Benchmarks`
measures tokenize/build/query on generated 1M, 4M and 16M corpora.

---

### Donkeh.LayoutLab

**Summary.** The layout and paint engine. It resolves matched CSS into
`ComputedStyle`, builds and positions a `LayoutBox` tree, runs CSS animations,
and emits a compact display list that any renderer can consume.

**Responsibilities**

- Cascade and inheritance: custom properties, `!important`, CSS-wide keywords,
  shorthand expansion, logical properties, animation declarations.
- Value parsing: lengths and `calc()`, colours, transforms, grids, shadows,
  gradients, filters, clip paths, masks, images.
- Box generation and layout: block and inline flow, margin collapsing, floats,
  flexbox, CSS grid, tables, absolute/fixed positioning, scroll containers.
- Text shaping through an injectable `ITextMeasurer` (the deterministic
  `SimpleTextMeasurer` by default; `Donkeh.Windowing` supplies a TrueType one).
- CSS transitions/keyframe animations with per-node retained state.
- Display-list building and optimisation (culling, merging, constant
  compaction).

**Key types**

| Type | Purpose |
| --- | --- |
| `LayoutEngine` | Reusable frame engine. `Layout(root, options)` returns a `LayoutResult`. |
| `LayoutOptions` | Viewport, fonts, DPI, measurer, scroll overrides, provider delegates, optimisation and snapshot settings. |
| `LayoutResult` | Root box, instruction buffer, constant pool, node map, optimiser stats and animation flags. |
| `LayoutBox` | Positioned box with margins, borders, padding, content, scroll and stacking data. |
| `ComputedStyle` | Resolved style for one node, including inherited and custom properties. |
| `StyleResolver` | Applies matched rules to a `ComputedStyle`; partial files cover parsing, keywords and animations. |
| `StyleValueCache` | Per-engine memoisation of parsed declaration values. |
| `CssLength`, `CssColor`, `CssGrid`, `CssTransform`, `CssValueParser` | CSS value model and parsers. |
| `TextLayoutEngine`, `ITextMeasurer`, `TextLayout` | Wrapping, clamping, ellipsis and line metrics. |
| `Instruction`, `InstructionBuffer`, `InstructionOpCode`, `ConstantPool` | The display list (24-byte instructions), its buffer, opcodes and shared constant pool. |
| `Optimiser` | Culls, merges and compacts a display list. |
| `AnimationRuntime` | Retained animation state and interpolation. |
| `ScrollbarLayout` | Track/thumb geometry and scroll mapping. |
| `Geometry` | `Point`, `Size`, `Rect`, `Thickness`, `CornerRadius`. |

**Usage**

```csharp
using Donkeh.LayoutLab;

var engine = new LayoutEngine(table);

var options = new LayoutOptions
{
    ViewportWidth = 1280,
    ViewportHeight = 720,
    DevicePixelRatio = 1.5f,
    RootFontSize = 16,
    TextMeasurer = myMeasurer,
    Optimise = true,
    SnapshotResults = true,     // keep the result valid past the next frame
    AnimationTimeSeconds = clock
};

LayoutResult result = engine.Layout(rootNode, options);

// Consume the display list directly, or hand it to a renderer:
foreach (Instruction instruction in result.Instructions.Span)
{
    // ...
}
```

**How a frame runs**

1. Match rules for each node with `DataTable.MatchRules`.
2. Resolve the cascade into a `ComputedStyle` (cached by node revision, parent
   identity, viewport and renderer-context version).
3. Build or reuse a `LayoutBox`, then lay it out with the algorithm selected by
   `display` (`block`, `inline`, `flex`, `grid`, `table`, floats, absolute and
   fixed).
4. Resolve text through `ITextMeasurer` and the text-layout cache.
5. Advance animations and apply animated values.
6. Build the display list, applying paint order, clips, opacity, transforms,
   filters, masks and scroll offsets.
7. Run the optimiser unless `Optimise` is false.

**Remarks**

- Positions are CSS pixels. `DevicePixelRatio` does not scale the layout tree;
  it participates in cache invalidation and is used by the host for
  rasterisation.
- The layout tree keeps **unscrolled absolute coordinates**. Scrolling is a
  paint-time translation, so changing a scroll offset does not relayout.
- `LayoutResult.Constants` is the engine's pool and is cleared at the start of
  the next layout. A result must be consumed before the next `Layout` call, even
  with `SnapshotResults` enabled (that option copies instructions and the node
  map, not the pool or box tree).
- `ReuseLayoutBoxes` mutates boxes in place; disable it when a previous result
  must remain an immutable snapshot.
- Style and text caches are owned by the engine. Reusing one `LayoutEngine` is
  what makes the warm-frame numbers possible.
- `SimpleTextMeasurer` is deterministic and dependency-free, which makes tests
  and benchmarks reproducible. Production hosts normally use the TrueType
  measurer from `Donkeh.Windowing`.

**Tests**

`Donkeh/LayoutLab/Tests` contains **959 tests** across 26 classes.

| Class | Tests | Covers |
| --- | ---: | --- |
| `StyleResolutionTests` | 216 | Cascade, inheritance, custom properties, `!important`, shorthands, keywords, logical properties. |
| `CssValueParserTests` | 79 | Lengths, numbers, colours, keywords, function values, lists. |
| `ComputedStyleTests` | 68 | Initial values, inheritance lists, copy/clone semantics, defaults for every property group. |
| `CssColorTests` | 63 | Hex, `rgb()`/`rgba()`, `hsl()`, named colours, alpha, blending and clamping. |
| `CssGridParsingTests` | 54 | Track lists, `repeat()`, `minmax()`, `fr`, named lines, `grid-template-areas`, placement syntax. |
| `CssLengthTests` | 43 | Unit conversion, percentages, `calc`/`min`/`max`/`clamp`, definiteness. |
| `TextTests` | 52 | Metrics, wrapping, `break-word`, `pre-wrap`, ellipsis, clamping, ASCII/CJK advances. |
| `BlockLayoutTests` | 47 | Widths, padding/border/content-box, margins and collapsing, auto sizing, percentages, floats. |
| `PaintTests` | 47 | Backgrounds, borders, radii, shadows, outlines, images, gradients, text, clipping, scroll offsets. |
| `FlexLayoutTests` | 41 | Direction, wrapping, grow/shrink, basis, alignment, gaps, nested flex. |
| `OptimiserTests` | 40 | NOP removal, culling, clipping, merging, frame markers, constant compaction. |
| `GridLayoutTests` | 35 | Fixed/percent/fraction/auto tracks, spans, implicit tracks, auto-flow, alignment. |
| `InstructionTests` | 33 | Packed layout, factories, flags, buffer growth and emit helpers. |
| `LayoutBoxTests` | 21 | Tree invariants, paint eligibility, out-of-flow boxes, stacking contexts. |
| `LayoutCachingTests` | 20 | Style reuse and invalidation by revision, class, viewport, font size and context. |
| `GeometryTests` | 18 | Rect/point/size/thickness operations and edge cases. |
| `EngineApiTests` | 16 | Constructor guards, null roots, `display:none`, box lookup, snapshot immutability, stats. |
| `PaintSpecsTests` | 14 | Value equality and defaults for borders, shadows, filters, clip paths, masks and images. |
| `LayoutOptionsTests` | 12 | Defaults, scroll overrides, length/renderer contexts, frame signatures. |
| `CssTransformTests` | 11 | Identity, translate, scale, rotate, skew, matrix and ordering. |
| `AnimationTests` | 6 | Keyframe interpolation, fill modes, paint-only animation, state release. |
| `BootstrapCompatibilityTests` | 5 | Custom-property ordering, flex shorthands, Bootstrap RGBA, gutters, nested variables. |
| `InteractionPseudoTests` | 5 | Interaction-state pseudo classes through the real matcher. |
| `StyleValueCacheTests` | 5 | Cache population, growth and clearing. |
| `KeywordClassTests` | 4 | `CssPropertyIds` classification of keyword properties. |
| `ScrollbarLayoutTests` | 4 | Track/thumb geometry, click and drag mapping. |

**Benchmarks.** `Donkeh/LayoutLab/Benchmarks` measures warm/cold frames, style
resolution, text wrapping, paint optimisation and parsing helpers across
`Block`, `Flex`, `Grid`, `Paint` and `Mixed` scenes. Use `--profile` for a fast
non-statistical report.

---

### Donkeh.UILab

**Summary.** A small React-shaped view layer. It builds a virtual tree, mounts
it into the same DOM abstraction the matcher uses, patches it in place, and
offers components, hooks, context and stylesheet attachment.

**Responsibilities**

- Build virtual nodes (`ElementNode`, `TextNode`, `FragmentNode`,
  `ComponentNode`, `ProviderNode`) and intern string attributes.
- Reconcile old and new trees in place, preserving revisions for unchanged
  subtrees so layout caches stay warm.
- Implement component state, reducers, effects, layout effects, memo,
  callbacks, refs and context.
- Attach CSS inline, from files, or from embedded resources.

**Key types**

| Type | Purpose |
| --- | --- |
| `_` | Static DSL: `H`, `C`, `F`, `Text`, `Show`, `Each`, `Provider`, `Key`. |
| `HtmlHelper` | Typed HTML element factories (`Div`, `Span`, `Canvas`, `Video`, `Audio`, ...). |
| `Props` | Strongly typed attributes and event handlers. |
| `Node` / `ElementNode` / `TextNode` / `FragmentNode` / `ComponentNode` | Virtual and live tree nodes. |
| `Component` / `Component<TProps>` | Lifecycle and typed props. |
| `Renderer` | `Mount`, `Flush`, `FindComponent<T>`, `Version`, `AutoFlush`. |
| `Ref<T>`, `Context<T>` | Mutable refs and context values. |
| `Stylesheet` / `StylesheetAttribute` | CSS loading and embedded-resource declarations. |

**Usage**

```csharp
using Donkeh.UILab;
using static Donkeh.UILab._;

var renderer = new Renderer();
Node root = renderer.Mount(
    H("div", new Props { ClassName = "app" },
        H("h1", Text("Hello")),
        H("button", new Props { ClassName = "primary", OnClick = () => { } },
            Text("Click me"))));

// Later, from a frame loop:
renderer.Flush();
```

Typed HTML helpers are an alternative to raw `H("tag", ...)` calls. `HtmlHelper`
has one method per HTML element, so the tag name is checked at the call site and
discoverable through IntelliSense:

Add `using static Donkeh.UILab.HtmlHelper;` alongside the existing DSL import.
The static import drops the `HtmlHelper.` prefix and makes the helpers read like
ordinary markup:

```csharp
using Donkeh.UILab;
using static Donkeh.UILab._;
using static Donkeh.UILab.HtmlHelper;

Node root = Main(new Props { ClassName = "page" },
    H1(new Props(), Text("Donkeh")),
    P(new Props(), Text("Drawn with typed HTML helpers.")),
    Button(new Props
    {
        ClassName = "primary",
        OnClick = () => { }
    }, Text("Click me")));
```

Media elements keep their typed props, for example
`Canvas(new CanvasProps { OnContext = gl => { ... } })`,
`Audio(new AudioProps { Src = "track.mp3", Controls = true })` and
`Video(new VideoProps { Src = "movie.mp4", Controls = true })`.

Components:

```csharp
public sealed class CounterProps
{
    public string Label { get; init; } = "Counter";
}

public sealed class Counter : Component<CounterProps>
{
    public override Node Render()
    {
        var (count, setCount) = State(0);

        return H("button", new Props { ClassName = "counter" },
                Text($"{Props.Label}: {count}"))
            .OnClick(() => setCount(count + 1));
    }
}

var root = renderer.Mount(C<Counter, CounterProps>(new CounterProps()));
```

Hooks are protected methods on `Component`: `State`, `StateWithUpdate`,
`Reducer`, `Effect`, `LayoutEffect`, `Memo`, `Callback`, `UseRef`,
`UseContext`. Lifecycle overrides are `OnMount`, `OnUpdate`, `OnUnmount` and
`ShouldRender`.

**Remarks**

- Hooks must run in the **same order every render**; the runtime enforces this
  by slot type. Do not call hooks conditionally.
- Effects run after the tree is committed; returned cleanups run before the
  effect re-runs and on unmount. `LayoutEffect` runs during commit.
- State setters compare the old and new value; a no-op update does not bump the
  node revision.
- Keyed children are matched by `Props.Key` within their sibling list. Unkeyed
  children are matched by type and position; reordered children are invalidated
  so structural selectors re-resolve.
- `Renderer.AutoFlush` defaults to `true` (synchronous updates). Set it to
  `false` and call `Flush()` from the frame loop to batch.
- `Renderer.Version` increments when a flush processed dirty components; treat
  it as a "work happened" gate, not a content hash.
- A renderer is mounted once and stays mounted; hosts own its lifetime.

**Tests**

| Project | Class | Tests | Focus |
| --- | --- | --- | --- |
| `Donkeh/UILab/Tests` | `BuilderTests` | 6 | Attribute interning, nested children, props/events, empty attributes, text folding, mixed content, fragments. |
| `Donkeh/UILab/Tests` | `ComponentTests` | 7 | In-place state patching, keyed reorder reuse, effects updating state, context, no-op state, unchanged-child revisions, functional updates. |

---

### Donkeh.Windowing

**Summary.** The native host. `DonkehWindow` owns a Silk.NET window, the CSS
table, a reusable `LayoutEngine`, input routing, text editing, scrolling, form
controls, image/font caches, the OpenGL renderer and optional devtools.

**Responsibilities**

- Create the window and OpenGL 3.3 context; run the frame loop.
- Compile the user-agent sheet plus author CSS into a `DataTable`.
- Mount and patch the UILab tree; relayout only when something changed.
- Route pointer, wheel, keyboard, text and focus events; maintain `:hover`,
  `:active`, `:focus`, `:focus-visible` and `:focus-within` state.
- Implement text inputs/areas, checkboxes, radios, ranges, selects, buttons,
  links, images, progress and meter.
- Render `LayoutLab` instructions with `GLRenderer`.
- Host the inspector in-process or in a separate OS window.

**Key types**

| Type | Purpose |
| --- | --- |
| `DonkehWindow` | Window, stylesheet, layout, input, paint and devtools host. |
| `WindowSettings` | Width, height, title, VSync/frame cap, clear colour, MSAA samples, resizability, fullscreen/frameless/maximized, top-most, transparency, min/max size, icon, on-close callback and devtools dock. |
| `AudioProps` / `AudioPlayer` | Declarative `<audio>` playback with optional controls, media events and a code-facing transport API. |
| `VideoProps` / `VideoPlayer` | Declarative `<video>` playback through LibVLC with optional controls, decoded-frame textures, media events and a transport API. |
| `GLRenderer` | Batched OpenGL consumer of `InstructionBuffer` + `ConstantPool`. |
| `FontManager` | System TrueType discovery, face/atlas caches, metrics. |
| `TrueTypeTextMeasurer` | `ITextMeasurer` adapter over `FontManager`. |
| `ImageCache` | Decoded image cache for file paths and data URIs. |
| `HtmlDefaults` | Built-in user-agent stylesheet and HTML display defaults. |
| `DevToolsWindowHost` | Standalone inspector window on its own STA thread. |
| `Stylesheet` / `StylesheetAttribute` | Window-side stylesheet loading. |

**Usage**

```csharp
using Donkeh.UILab;
using Donkeh.Windowing;
using static Donkeh.UILab._;

var window = new DonkehWindow(WindowSettings.Default with
{
    Width = 1120,
    Height = 760,
    Title = "My app"
});

window.BeforeFrame += (w, dt) => { /* per-frame hook */ };

window.Run(
    H("div", new Props { ClassName = "app" },
        H("h1", Text("Hello")),
        H("p", Text("Painted from the LayoutLab instruction stream."))),
    """
    .app { padding: 32px; }
    """);
```

**Rendering pipeline.** `GLRenderer` consumes the instruction opcodes:

| Instruction | OpenGL path |
| --- | --- |
| `DrawRect`, `DrawLine` | Batched coloured primitives |
| `DrawRoundedRect` | Instanced rounded-box SDF |
| `DrawBorder`, `DrawOutline` | Uniform border SDF, per-side fallback |
| `DrawShadow`, `DrawInsetShadow` | Blurred rounded-box SDF |
| `DrawEllipse` | Ellipse SDF |
| `DrawBackgroundImage` | Cached gradient/texture |
| `DrawImage` | Cached decoded image with `object-fit` |
| `DrawText` | TrueType glyph atlas, indexed quad batch |
| `DrawScrollbar` | Track and thumb rounded rects |
| `PushClipRect`, `PushClipRoundedRect` | Scissor and rounded SDF clip |
| `PushOpacity` | Alpha folded into vertex colours |
| `PushTransform` | 2D affine transform in the vertex shader |
| `PushBlendMode` | GL blend factors |

Text and images share one indexed textured batch; solid shapes use one
instanced shape batch. Runs split only when texture, clip, transform or blend
state changes, so a typical frame uses a handful of draw calls.

**Remarks**

- The UA stylesheet (`HtmlDefaults.Css`) is parsed before author CSS, so plain
  markup already looks like a document. Author rules win the cascade.
- Hover and active state propagate to the ancestor chain, matching browser
  behaviour, so `.nav-link:hover` works naturally.
- Mouse-wheel scrolling walks to the nearest scroll container and updates its
  scroll offset; the next frame reuses the box tree and only rebuilds the
  display list.
- `Run` blocks until the window closes. `Loaded`, `BeforeFrame` and `Closed`
  expose lifecycle hooks on the frame thread.
- Fonts and images are cached per pixel size/face and per source; GPU resources
  are released by `Dispose`.

**Tests.** There are currently **no automated tests** for the window host or
the GL renderer: both require a native window and GPU context. The layout and
instruction layers they consume are covered by `Donkeh/LayoutLab/Tests`.
Manual verification is done with host apps and the inspector. Contributions of
headless GL harnesses (for example an offscreen framebuffer) would be welcome.

---

### Donkeh.Inspector

**Summary.** The devtools panel and its state model. It renders a live DOM tree,
a style editor, colour picker, animation browser and performance view, either
inside an `DonkehWindow` or in a standalone window.

**Responsibilities**

- Track the selected node, hovered node, expando state, dock and active tab.
- Edit inline styles and declared rules without rebuilding the whole stylesheet.
- Parse and preview colours, including alpha and contrast.
- Sample and display frame timing, layout/paint time and GC data.
- Render the panel using the same layout and UILab stack as application UI.

**Key types**

| Type | Purpose |
| --- | --- |
| `DevToolsState` | Observable state: selection, tab, dock, styles, history, performance samples. |
| `InspectorPanel` | The panel component: DOM tree, styles, picker, animations, performance. |
| `InspectorStyles` | CSS and sizing constants for the panel. |
| `ColorPreview` | Colour parsing, formatting, HSV helpers and contrast helpers. |
| `PerformanceStats` | Frame/layout/paint timing samples and rolling history. |
| `DevToolsDock`, `DevToolsTab`, `StyleSheetRange` | Layout and navigation enums/records. |

**Usage.** `DonkehWindow` hosts the inspector automatically when
`WindowSettings` enables it. A standalone panel can share state:

```csharp
window.SharedDevToolsState = new DevToolsState();
```

The inspector is a consumer of public framework APIs; it does not change layout
or matching semantics.

**Remarks**

- The style editor compiles small rule sets and adds them to the shared
  `DataTable`; host stylesheets stay untouched.
- Performance sampling uses a bounded rolling window, so the graph cannot grow
  without limit.
- The panel is ordinary UILab UI, so it is subject to the same layout, caching
  and input rules as application content.

**Tests.** There are currently **no automated tests** for the inspector. Its
colour utility (`ColorPreview`) and state model are pure enough to test and are
a good target for future coverage.

---

### Tooling projects

| Project | Output | Purpose |
| --- | --- | --- |
| `Donkeh/DataEngine/Debugger` | `CssDebug` CLI | Dump selectors, rules, counts and timings from a local file, URL or stdin. Supports `--selectors`, `--rules`, `--summary`, `--json`, `--limit`, `--all`, `--no-color`. |
| `Donkeh/Parser/Benchmark` | BenchmarkDotNet | Tokenizer throughput. |
| `Donkeh/Data/Benchmarks` | BenchmarkDotNet | Real Bootstrap 5.3.8 parse and selector matching across 592–286,752 element DOMs. |
| `Donkeh/DataEngine/Benchmarks` | BenchmarkDotNet | Tokenize/build/query on generated 1M/4M/16M character corpora, plus repeated-selector interning. |
| `Donkeh/LayoutLab/Benchmarks` | BenchmarkDotNet | Frames, style resolution, text, paint optimisation and CSS value parsing. |

---

## Getting started

### Requirements

- .NET SDK **10.0** (the projects target `net10.0`).
- Windows, Linux or macOS for the non-UI engine modules.
- A desktop with OpenGL 3.3 for `Donkeh.Windowing`.

### Build and test

```powershell
dotnet restore DonkehCSS.sln
dotnet build DonkehCSS.sln -c Release
dotnet test DonkehCSS.sln -c Release
```

The solution currently runs **1,014 tests**: 42 parser/data/data-engine tests,
959 layout tests and 13 UILab tests.

Library projects enable `GenerateDocumentationFile`, so the build emits an
`.xml` file beside every assembly and `dotnet pack` includes it in the NuGet
package. This is what makes the XML documentation visible to IntelliSense and
API documentation tools. To force generation for every project (including
tests and benchmarks):

```powershell
dotnet build DonkehCSS.sln -c Release -p:GenerateDocumentationFile=true
```

---

## End-to-end example

The shortest complete program uses the window host:

```csharp
using Donkeh.UILab;
using Donkeh.Windowing;
using static Donkeh.UILab._;

var window = new DonkehWindow(WindowSettings.Default with
{
    Width = 900,
    Height = 640,
    Title = "Donkeh"
});

window.Run(
    H("main", new Props { ClassName = "page" },
        H("h1", Text("Donkeh")),
        H("p", Text("CSS-driven layout and painting."))),
    """
    .page { max-width: 720px; margin: 48px auto; font-family: sans-serif; }
    h1 { font-size: 40px; margin: 0 0 12px; }
    p  { color: #4a5568; line-height: 1.6; }
    """);
```

For a headless pipeline (tests, tools, server-side screenshot generation), stop
at the instruction stream:

```csharp
var tokens = Tokenizer.Parse(css);
var table = new DataEngine(MediaConditionCompiler.Instance).Build(tokens);
var engine = new LayoutEngine(table);

var result = engine.Layout(documentRoot, new LayoutOptions
{
    ViewportWidth = 900,
    ViewportHeight = 640
});

// result.Instructions, result.Constants, result.Root and result.Stats
```

---

## Testing

### Test projects

| Project | Framework | Tests | Modules covered |
| --- | --- | ---: | --- |
| `Donkeh/Parser/Tests` | xUnit | 42 | `Donkeh.Parser`, `Donkeh.Data`, `Donkeh.DataEngine` |
| `Donkeh/LayoutLab/Tests` | xUnit | 959 | `Donkeh.LayoutLab` (plus `Donkeh.Data` / `Donkeh.DataEngine` integration) |
| `Donkeh/UILab/Tests` | xUnit | 13 | `Donkeh.UILab` |

### Module → test matrix

| Module | Direct test classes | Integration coverage |
| --- | --- | --- |
| `Donkeh.Parser` | `TokenizerTests` | Every `DataTable`/`DataEngine` test tokenizes real CSS. |
| `Donkeh.Data` | `MatchingTests`, `InteractionPseudoTests` | `StyleResolutionTests`, `LayoutCachingTests`, `BootstrapCompatibilityTests`. |
| `Donkeh.DataEngine` | `DataEngineTests`, `MediaConditionTests` | Every LayoutLab fixture builds its stylesheet through `DataEngine`. |
| `Donkeh.LayoutLab` | 26 classes, 959 tests | `UILab` tests assert layout-facing node revisions. |
| `Donkeh.UILab` | `BuilderTests`, `ComponentTests` | — |
| `Donkeh.Windowing` | none (requires GPU/window) | Consumes LayoutLab output; manual verification. |
| `Donkeh.Inspector` | none | Manual verification; `ColorPreview` is unit-testable. |

### Running a subset

```powershell
dotnet test Donkeh/Parser/Tests
dotnet test Donkeh/LayoutLab/Tests --filter "FullyQualifiedName~FlexLayoutTests"
dotnet test Donkeh/LayoutLab/Tests --filter "FullyQualifiedName~StyleResolutionTests"
```

### Coverage

```powershell
dotnet test DonkehCSS.sln -c Release --collect:"XPlat Code Coverage"
```

`coverlet.collector` is already referenced by the test projects; results are
written under each test project's `TestResults/` directory.

---

## Benchmarks

> Numbers below are from the benchmark READMEs and were measured on an AMD
> Ryzen 5 8400F, 12 logical cores, Windows 10 22H2, .NET 10.0.10. Treat them as
> order-of-magnitude guidance, not guarantees.

### Parse and build (`Donkeh/DataEngine/Benchmarks`)

| Method | Corpus | Mean | Allocated |
| --- | ---: | ---: | ---: |
| `Tokenize` | 1M | 6.5 ms | 0 B |
| `Build_FromString` | 1M | 14.1 ms | 18.2 MiB |
| `Build_FromTokens` | 1M | 5.8 ms | 12.1 MiB |
| `Tokenize` | 16M | 117.3 ms | 0 B |
| `Build_FromString` | 16M | 248.3 ms | 282.5 MiB |
| `Build_FromTokens` | 16M | 108.8 ms | 191.0 MiB |

Tokenization is allocation-free; the build cost is dominated by the flat arenas
that make subsequent matching cheap and allocation-free.

### Selector matching (`Donkeh/Data/Benchmarks`)

- A hot `.btn-primary` (51 matching Bootstrap rules) matches in roughly
  **2.7 µs**, independent of page size.
- A cold `<title>` rejects nearly the whole table in roughly **0.8 µs**.
- Full-page passes stay near **1.3 µs per element** at 592 and 286,752 elements.

### Layout (`Donkeh/LayoutLab/Benchmarks --profile`)

| Scene | Nodes | Cold | Warm | Instructions after optimise |
| --- | ---: | ---: | ---: | ---: |
| Block | 997 | 35.9 ms / 5.4 MiB | 4.0 ms / 0.6 MiB | 2,159 → 56 |
| Flex | 1,001 | 38.9 ms / 5.8 MiB | 12.4 ms / 1.2 MiB | 2,590 → 204 |
| Grid | 1,000 | 20.7 ms / 4.5 MiB | 3.2 ms / 1.0 MiB | 2,998 → 226 |
| Paint | 1,000 | 19.4 ms / 4.0 MiB | 3.1 ms / 0.7 MiB | 3,204 → 1,132 |
| Mixed | 1,000 | 6.9 ms / 3.5 MiB | 1.5 ms / 0.6 MiB | 1,446 → 91 |

Warm frames reuse style, text and box caches. The optimiser typically removes
65–97% of painted instructions by culling, merging and compaction (the
paint-heavy scene keeps the most draws because overlapping effects cannot be
merged).

---

## Documentation standard

All shipped code follows the same XML documentation rules:

- **Every public and protected type and member** has a `<summary>` (or an
  `<inheritdoc/>` where an override or interface implementation repeats base
  documentation).
- **Non-obvious internal/private members** (algorithms, caches, bit packing,
  IL generation, state machines) are documented too.
- **Methods** document every parameter and return value, plus exceptions that
  are actually thrown. Generic methods document type parameters.
- **Properties** distinguish "Gets" from "Gets or sets", and use `<value>` /
  `<remarks>` for units, ranges, sentinels and invalidation behaviour.
- **Types** add `<remarks>` for invariants, ownership, lifetime, thread-safety,
  allocation and relationships, and `<example>` for the primary entry points.
- **Enums** document every member, including flag values.
- Cross-references use `<see cref="..."/>`, `<paramref>`, `<c>`, `<para>` and
  `<seealso>`; prose is factual rather than promotional.
- Units are named explicitly: CSS pixels, device pixels, seconds/milliseconds,
  degrees, 0–1 alpha, packed ARGB.

The documentation is verified by compiling with
`-p:GenerateDocumentationFile=true`; the source projects build with no XML
warnings (malformed tags, unknown `cref`s or mismatched `<param>` names).

---

## Performance model

Donkeh's steady-state rendering behaviour is built around a few invariants:

1. **Everything is an id.** Strings are interned once; selectors, rules,
   conditions and attributes compare integers.
2. **Nothing is allocated per node per frame.** Matching writes into reusable
   buffers, instructions and constants live in reusable pools, and value types
   dominate the hot path.
3. **Work is skipped, not optimised.** A frame relayouts only if the UILab
   version, viewport, DPI, stylesheet or scroll state changed. Styles are cached
   per node revision and parent identity. Text is cached by content, style and
   width.
4. **Scrolling is paint-only.** Boxes keep unscrolled coordinates; the display
   list applies clamped translations.
5. **Batching is a renderer concern.** Layout emits a flat instruction stream;
   `GLRenderer` splits batches only when texture, clip, transform or blend state
   changes.

When tuning a host, measure in this order: layout warm/cold, text shaping,
paint-list size, then GL draw calls. The benchmarks expose each layer
independently.

---

## Supported CSS surface and limitations

**Selectors.** Type, id, class, universal, attribute operators (`=`, `~=`,
`|=`, `^=`, `$=`, `*=`), quoted values with `i`, descendant/child/adjacent/general
sibling combinators, selector lists, structural pseudo classes
(`:nth-child`, `:first-child`, `:only-child`, `:nth-of-type`, ...), `:not()`,
`:is()`/`:where()` where supported, and interaction states driven by the host
(`:hover`, `:active`, `:focus`, `:focus-visible`, `:focus-within`, `:checked`,
`:disabled`, `:enabled`, `:link`, `:any-link`, `:placeholder-shown`,
`:read-only`, `:read-write`).

**At-rules.** `@media` (width/height/orientation/media types), `@supports`,
`@container`, `@layer`, `@scope`, `@keyframes`, `@import`, `@namespace`,
`@charset`, descriptors and margin boxes. Conditions other than media queries
require a custom `ConditionCompiler`; the default `MediaConditionCompiler`
fails closed for them.

**Layout.** Block and inline flow, margin collapsing, floats and clearance,
flexbox, CSS grid, tables, absolute/fixed positioning, replaced elements,
scroll containers, `box-sizing`, logical properties, percentage and viewport
units.

**Painting.** Background colours/images/gradients, borders, corner radii,
shadows (outer and inset), outlines, transforms, opacity, filters, clip paths,
masks, blend modes, text decoration and images with `object-fit`.

**Animation.** Keyframe animations with delay, iteration, direction and fill
modes; layout-changing and paint-only animations are distinguished so a
paint-only frame can reuse the previous layout.

Current limitations worth knowing:

- **Pre-release quality.** This is an active development build. Expect rough
  edges, missing CSS surface and behaviour changes between commits.
- **Not a browser.** There is no JavaScript, no full HTML parser and no
  network/CORS model. The host supplies the tree, text, images and conditions.
- **Tables** implement `colspan`/`rowspan`, `border-collapse` and
  `border-spacing`; remaining gaps are `caption-side` and full collapsing-border
  corner resolution.
- **Stacking** is approximated by ordering direct children; descendants cannot
  escape an ancestor's paint subtree, and a transformed ancestor is not modelled
  as the containing block for fixed descendants.
- **Grid** implements the common track-sizing path; `auto-fit` collapses only by
  item count, and some intrinsic `minmax()` combinations are simplified.
- **Media queries** support the common width/height/orientation/type features;
  unknown features fail closed. `@supports`/`@container` need a custom compiler.
- **`@scope`** boundaries are recorded but not yet consulted by matching.
- **Text** uses a deterministic approximation by default; production text
  quality and shaping come from the host-provided `ITextMeasurer`.
- **Windowing/Inspector** have no automated tests (GPU/window dependencies).

---

## Packaging and release

`Donkeh/Directory.Build.props` supplies shared NuGet metadata. Package ids are:

| Project | Package id |
| --- | --- |
| `Donkeh.Parser` | `Donkeh.Parser` |
| `Data` | `Donkeh.Data` |
| `DataEngine` | `Donkeh.DataEngine` |
| `LayoutLab` | `Donkeh.LayoutLab` |
| `UILab` | `Donkeh.UILab` |
| `Windowing` | `Donkeh.Windowing` |
| `Inspector` | `Donkeh.Inspector` |

Publishing is tag-driven (`.github/workflows/publish-nuget.yml`): push a tag
such as `v1.2.3` or `v1.2.3-beta.1`; CI restores, builds, tests, packs
`.nupkg` + `.snupkg`, exchanges a GitHub OIDC token for a short-lived NuGet key,
and pushes every package.

Symbol packages, embedded untracked sources and deterministic builds are
enabled by default.

---

## Contributing

1. **Build and test first.**
   ```powershell
   dotnet build DonkehCSS.sln -c Release
   dotnet test DonkehCSS.sln -c Release
   ```
2. **Keep layering intact.** `Donkeh.Parser` must stay dependency-free; do not
   make `Donkeh.Data` depend on layout or UI.
3. **Document new API.** Every public/protected member needs XML docs in the
   style described above. Verify with
   `dotnet build -p:GenerateDocumentationFile=true` and fix all `CS1570`,
   `CS1572`, `CS1573`, `CS1574` and `CS1734` warnings.
4. **Add tests.** Pure value types and parsers belong in the matching test
   project; layout behaviour belongs in `Donkeh/LayoutLab/Tests`. Follow the
   existing `TestSupport` helpers rather than duplicating fixture code.
5. **Measure hot paths.** If a change touches matching, style resolution, layout
   or paint, run the relevant benchmark in `Release` and include before/after
   numbers.
6. **Preserve allocation discipline.** Prefer spans, value types, reusable
   buffers and interned ids in code that runs per node or per frame.

---

*Donkeh is a declarative-first UI framework for .NET, not a WebView. It brings
the parts of the browser that matter ease of use, components and hooks, form
controls, devtools and profiling*
