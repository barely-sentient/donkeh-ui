# Donkeh

Build apps for desktop, Android and the browser with C# components and real CSS.

**[Read the documentation →](https://barely-sentient.github.io/donkeh-ui)**

> Donkeh is early software. APIs and CSS coverage are still changing, so expect
> the occasional breaking change.

## What it does for you

- **Component-driven UI in C#.** Write small components with a `Render` method,
  compose them like you would in React, and use hooks for state and effects.
  No JavaScript, no separate front-end build.
- **Actual CSS.** Keep styles in `.css` files next to your components. Selectors,
  the cascade, flexbox, grid and animations work the way you already expect.
- **One codebase, three targets.** The same project runs on desktop, Android and
  WebAssembly. The framework supplies the platform entry points.
- **Scaffolding included.** The `donkeh` CLI creates a working project or a new
  component, wires up its stylesheet, and gets out of your way.
- **Tools that explain themselves.** A built-in inspector shows your component
  hierarchy, styles, performance and animations while the app runs.

## Quick start

```powershell
dotnet tool install --global Donkeh.CliTool
donkeh new project MyApp
cd MyApp
dotnet run
```

Add a component at any time:

```powershell
donkeh new component FileList
```

That creates `src/Components/FileList/FileList.cs` and `FileList.css`, already
registered with the project.

Add another platform when you need it:

```powershell
donkeh enable-target android
donkeh enable-target web
```

Building a reusable component library? Generate a packable library plus a live
gallery:

```powershell
donkeh new project Acme.Components --type framework
cd Acme.Components
dotnet watch --project test-container run
```

## Learn

- [Getting started](https://barely-sentient.github.io/donkeh-ui/getting-started.html)
- [Creating a component](https://barely-sentient.github.io/donkeh-ui/creating-a-component.html)
- [Routing and setPage](https://barely-sentient.github.io/donkeh-ui/routing.html)
- [HTML elements](https://barely-sentient.github.io/donkeh-ui/html-elements.html)
- [CSS support](https://barely-sentient.github.io/donkeh-ui/css-support.html)
- [Using hooks](https://barely-sentient.github.io/donkeh-ui/using-hooks.html)
- [Inspector overview](https://barely-sentient.github.io/donkeh-ui/inspector.html)
- [Host permissions](https://barely-sentient.github.io/donkeh-ui/host-permissions.html)

Everything else lives at
**[barely-sentient.github.io/donkeh-ui](https://barely-sentient.github.io/donkeh-ui)**.
