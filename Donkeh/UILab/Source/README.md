# Donkeh.UILab

A small, React-shaped view layer for Donkeh. `Node` derives from
`IgniteSupportedDomNode`, so a rendered tree can be passed directly to the
selector matcher and the layout engine. String attributes are interned when the
element is built, and the diff compares ints.

## Elements

```csharp
using Donkeh.UILab;
using static Donkeh.UILab._;

var tree = H("div", new Props { ClassName = "app" },
    H("div", new Props { ClassName = "topbar" }, Text("Ignite")),
    H("button", new Props { ClassName = "counter-btn" }, Text("Count: 0"))
        .OnClick(() => Console.WriteLine("clicked")));
```

`H("div")`, `H("div", [])` and `H("div", child)` all work. Rich attributes use
the fluent form:

```csharp
H("input", new Props
{
    ClassName = "field",
    Placeholder = "Search",
    Disabled = true,
    OnInput = value => Console.WriteLine(value),
});
```

## Components

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

        return H("button", [
            ["class", "counter-btn"],
        ],
            Text($"{Props.Label}: {count}"))
            .OnClick(() => setCount(count + 1));
    }
}
```

Mount it once:

```csharp
var renderer = new Renderer();
var root = renderer.Mount(C<Counter, CounterProps>(new CounterProps()));
```

State updates flush synchronously by default. Set `renderer.AutoFlush = false`
and call `renderer.Flush()` from your frame loop to batch them.

## Hooks

All hooks are protected methods on `Component`:

| Hook | Purpose |
| --- | --- |
| `State(initial)` | `(value, set)` state pair |
| `StateWithUpdate(initial)` | state plus a functional updater |
| `Reducer(initial, reducer)` | state plus a dispatcher |
| `Effect(action, deps)` | run after commit; accepts a cleanup `Func<Action?>` |
| `LayoutEffect(action, deps)` | run during commit, before passive effects |
| `Memo(factory, deps)` | cache a computed value |
| `Callback(action, deps)` | cache a delegate |
| `UseRef(initial)` | mutable `Ref<T>` that survives renders |
| `UseContext(context)` | nearest provider value |

Effects with no dependency array run after every render. Pass `[]` to run once.

## Lifecycle

```csharp
protected override void OnMount() { }
protected override void OnUpdate() { }
protected override void OnUnmount() { }
protected override bool ShouldRender() => true;
```

## Context

```csharp
public static class AppContext
{
    public static readonly Context<string> Theme = new("light");
}

// In a parent component:
return Provider(AppContext.Theme, "dark", C<ThemedPanel>());

// In ThemedPanel.Render():
var theme = UseContext(AppContext.Theme);
return H("div", Text(theme));
```

## Routing

`AppRouter` owns a page path and publishes it through `Router.Path` and
`Router.SetPage`. `AppRoutes` picks the first matching `Route`; `Link` updates the
page without browser navigation. Import `Donkeh.UILab.Routing` for the short
factories.

Routes whose content is expensive to construct can pass a factory instead of
children. The factory runs only on a frame where that route matches, so unmatched
pages do not build their node trees at start-up:

```csharp
Route("/reports", () => H("section", new Props { ClassName = "card" },
    C<ReportsPage>()));
```

```csharp
using Donkeh.UILab;
using static Donkeh.UILab._;
using static Donkeh.UILab.Routing;

var app = AppRouter("/",
    AppRoutes(
        Route("/", H("h1", Text("Home"))),
        Route("/about", H("p", Text("About"))),
        Route("/docs/*", Text("Docs section")),
        Route("*", Text("Not found"))));

renderer.Mount(app);
```

`Route` supports exact paths, trailing `/*` prefixes and a `*` catch-all. Use the
props form when you need a fallback or active-link classes:

```csharp
C<AppRouter>(new AppRouterProps
{
    InitialPath = "/",
    Children =
    [
        C<AppRoutes>(new AppRoutesProps
        {
            Fallback = Text("Not found"),
            Children =
            [
                C<Route>(new RouteProps { Path = "/", Children = [Text("Home")] }),
                C<Route>(new RouteProps { Path = "/about", Children = [Text("About")] })
            ]
        })
    ]
});
```

Navigate from anywhere under the router:

```csharp
public sealed class SaveButton : Component
{
    public override Node Render()
    {
        var setPage = UseContext(Router.SetPage);
        return H("button", Text("Done")).OnClick(() => setPage("/done"));
    }
}
```

Links render an `<a>`, call `Router.SetPage` on click and add the `active` class
while their path is the current section:

```csharp
Link("/about", Text("About"));
```

`AppRouter` does not touch the URL bar or browser history. A host can persist the
path or drive navigation by publishing its own `Router.SetPage` provider.

## Lists, fragments and keys

```csharp
Each(items, item =>
        H("li", new Props { ClassName = "row", Key = item.Id }, Text(item.Name)));
```

`F(...)` creates a fragment. Fragments and text nodes do not match selectors;
fragments generate a `display: contents` box, and text nodes are laid out
inline. Text-only children are folded into the element's `TextContent` so they
do not allocate a text box at all.

## Packed images

`[Image]` declares an embedded image so it can be referenced from the same
string sources as any other asset:

```csharp
using Donkeh.UILab;

[assembly: Image("Assets/logo.png")]

// Resolves to the embedded resource when no file exists at the same path.
H("img", new Props { Src = "Assets/logo.png" });

// Force the embedded lookup, even if a file with that name exists.
H("img", new Props { Src = "packed:Assets/logo.png" });
```

The file must be an `EmbeddedResource` in the assembly that declares it. The
lookup accepts the full relative path or just the file name. `Image.TryGetBytes`
and `Image.Load` expose the encoded bytes directly for hosts that decode images
themselves. The window host uses the same declarations for `src` attributes, CSS
`url(...)` layers and `WindowSettings.Icon`, so a packed asset can be referenced
without a copy step.

## How the diff works

The renderer keeps the old virtual tree and patches the live nodes in place.
An unchanged element keeps its `Revision`, so the layout engine's style and
layout caches stay warm. Only real attribute, text, structure or identity
changes bump revisions. Keyed children are matched by key; unkeyed children are
matched by type and position. Reordered children are invalidated so structural
selectors re-resolve correctly.
