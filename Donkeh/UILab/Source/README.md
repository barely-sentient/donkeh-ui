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

## Lists, fragments and keys

```csharp
Each(items, item =>
        H("li", new Props { ClassName = "row", Key = item.Id }, Text(item.Name)));
```

`F(...)` creates a fragment. Fragments and text nodes do not match selectors;
fragments generate a `display: contents` box, and text nodes are laid out
inline. Text-only children are folded into the element's `TextContent` so they
do not allocate a text box at all.

## How the diff works

The renderer keeps the old virtual tree and patches the live nodes in place.
An unchanged element keeps its `Revision`, so the layout engine's style and
layout caches stay warm. Only real attribute, text, structure or identity
changes bump revisions. Keyed children are matched by key; unkeyed children are
matched by type and position. Reordered children are invalidated so structural
selectors re-resolve correctly.
