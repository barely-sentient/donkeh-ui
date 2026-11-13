# Donkeh.Windowing

The native host that ties the whole engine together:

```
UILab tree ──► DataTable selectors ──► LayoutLab boxes ──► 24-byte display list
                                                              │
                                                              ▼
                                                   GLRenderer (Silk.NET / OpenGL 3.3)
```

`IgniteWindow` owns a Silk.NET window, the CSS table, one reusable
`LayoutEngine`, input routing and the OpenGL display-list renderer. A mounted
tree is patched in place by UILab, so unchanged subtrees keep their style and
text caches.

## Quick start

```csharp
using Donkeh.UILab;
using Donkeh.Windowing;
using static Donkeh.UILab._;

var window = new IgniteWindow(WindowSettings.Default with
{
    Width = 1120,
    Height = 760,
    Title = "My app"
});

window.Run(
    H("div", new Props { ClassName = "app" },
        H("h1", Text("Hello")),
        H("p", Text("Painted from the LayoutLab instruction stream."))),
    """
    .app { padding: 32px; }
    """);
```

For a complete application, add a small host project that references
`Donkeh.Windowing` and pass a UILab tree plus a stylesheet to `Run`; see the
root `README.md` for an end-to-end example. The engine itself ships no demo
project, so hosts are free to choose their own entry point and window chrome.

## What is supported

### HTML5 defaults

The built-in user-agent sheet (`HtmlDefaults.Css`) is parsed before author CSS,
so a plain tree already looks like a document. Author rules always win the
cascade.

| Elements | Default behaviour |
| --- | --- |
| `html`, `body`, `div`, `section`, `article`, `header`, `footer`, `nav`, `main`, `aside`, `figure`, `figcaption`, `blockquote`, `address`, `dl`, `dt`, `dd`, `ul`, `ol`, `li`, `form`, `fieldset`, `legend`, `details`, `summary`, `dialog`, `center`, `pre`, headings | block flow with sensible margins/sizes |
| `span`, `a`, `strong`, `em`, `b`, `i`, `u`, `s`, `small`, `sub`, `sup`, `code`, `kbd`, `samp`, `var`, `mark`, `abbr`, `cite`, `q`, `time`, `label`, `output` | inline runs |
| `img`, `input`, `button`, `select`, `textarea`, `progress`, `meter`, `canvas`, `video`, `audio`, `iframe`, `object`, `embed`, `svg` | replaced/inline-block boxes with intrinsic sizing |
| `table`, `thead`, `tbody`, `tfoot`, `tr`, `td`, `th` | block rows with inline-block cells |
| `head`, `title`, `meta`, `link`, `style`, `script`, `base`, `template`, `noscript`, `param`, `source`, `track`, `datalist` | `display: none` |
| `hr` | one-pixel CSS border |
| `br` | block line break |

### Replaced content and controls

- `<img>` uses `src` (file path or base64 data URI), intrinsic/`width`/`height`
  attributes, and `object-fit`.
- `<input>`/`<textarea>` display `value`, then `placeholder`, and are editable
  with the keyboard. `input` events, `change`, `focus` and `blur` are routed to
  UILab handlers.
- Checkboxes, radios, ranges, `progress` and `meter` are painted by
  `IgniteWindow.PaintElementContent` on top of their UA chrome.
- `<a>`, `<button>` and every element with event handlers receive routed
  pointer events.
- `<audio>` plays files through the cross-platform SoundFlow/MiniAudio backend.
  `<video>` decodes through LibVLC and composites decoded frames as textures.
  Both support `controls`, `autoplay`, `loop`, `muted`, `volume` and
  `playbackrate`; `OnPlay`, `OnPause`, `OnEnded`, `OnTimeUpdate` and `OnReady`
  expose playback to host code. Player creation, I/O and decoding run off the
  render thread; only the final frame upload touches the GL context.

### Rendering

`GLRenderer` consumes the instruction opcodes:

| Instruction | OpenGL path |
| --- | --- |
| `DrawRect`, `DrawLine` | batched coloured primitives |
| `DrawRoundedRect` | instanced rounded-box SDF |
| `DrawBorder`, `DrawOutline` | uniform border SDF, per-side fallback |
| `DrawShadow`, `DrawInsetShadow` | blurred rounded-box SDF |
| `DrawEllipse` | ellipse SDF |
| `DrawBackgroundImage` | cached gradient texture, or cached image texture |
| `DrawImage` | cached decoded image with object-fit |
| `DrawText` | TrueType glyph atlas, indexed quad batch |
| `DrawScrollbar` | track + thumb rounded rects |
| `PushClipRect` / `PushClipRoundedRect` | scissor + rounded SDF clip |
| `PushOpacity` | alpha folded into vertex colours |
| `PushTransform` | 2D affine transform in the vertex shader |
| `PushBlendMode` | GL blend factors |

Text and images use a single indexed textured batch. Solid shapes use one
instanced shape batch. Runs are split only when the texture, clip, transform or
blend state changes, so a normal UI frame costs a handful of draw calls.

### Fonts

`FontManager` loads system TrueType fonts (Segoe UI/Arial/Consolas on Windows,
DejaVu/Liberation on Linux, Arial on macOS), caches faces and glyph atlases per
pixel size, and exposes the same metrics to layout and painting. Missing
codepoints fall back through a small system-font list.

### Images, packed assets and icons

`ImageCache` resolves a source in this order: a base64 `data:` URI, an on-disk
file, then an embedded resource declared with
`[assembly: Image("Assets/logo.png")]`. The same source strings work for
`img src`, CSS `background-image: url(...)`, and `WindowSettings.Icon`. A
`packed:` prefix forces the embedded lookup. SVG sources are rasterised whether
they arrive as a data URI, a file or a packed resource.

```csharp
[assembly: Image("Assets/icon.png")]

var window = new IgniteWindow(WindowSettings.Default with
{
    Icon = "packed:Assets/icon.png"
});
```

The icon is applied after the native window is initialized, because Silk.NET's
GLFW backend rejects `SetWindowIcon` before that point.

## Input and pseudo classes

Interaction state is written onto the live DOM node, so it participates in the
normal cascade and style cache:

| Pseudo class | State source |
| --- | --- |
| `:hover` | pointer chain (`IgniteSupportedDomNode.IsHovered`) |
| `:active` | pointer-button chain (`IsActive`) |
| `:focus` | the focused element (`IsFocused`) |
| `:focus-visible` | keyboard focus, and all text fields (`IsFocusVisible`) |
| `:focus-within` | focused element or any focused descendant |
| `:checked` | `IsChecked`, `checked`, or `selected` |
| `:disabled` / `:enabled` | `disabled` on a form control |
| `:link` / `:any-link` | `a`/`area`/`link` with `href` |
| `:placeholder-shown` | empty text control with a placeholder |
| `:read-only` / `:read-write` | text control with/without `readonly` or `disabled` |

Hover and active are applied to the whole ancestor chain, exactly like a
browser, so variant selectors work naturally:

```css
.nav-link:hover { background: #eef3f9; }
.nav-link:focus-visible { outline: 2px solid #0969da; outline-offset: 2px; }
.nav-link:active { background: #dbe8f8; }
.card:focus-within { border-color: #0969da; }
input:focus { border-color: #0969da; }
```

Mouse wheel scrolling walks up to the nearest scroll container and updates its
`ScrollOffsetX/Y`; the next layout pass rebuilds the display list.

### Window scrolling

The application root behaves like a browser document by default: when its height
is `auto` it fills the viewport, and when its overflow is `visible` it scrolls
vertically. Explicit `overflow` and `height` rules still win, so an app that
wants a content-sized or clipped root can opt out.

### Window drag regions

`app-region: drag` (and the Chromium-compatible `-webkit-app-region: drag`)
marks an element and its descendants as a native window-drag region. Pressing
inside it moves the window with the pointer. A closer `app-region: no-drag`
opts a child back out, which is useful for buttons and other controls inside a
custom title bar:

```css
.titlebar { app-region: drag; }
.titlebar button { app-region: no-drag; }
```

### Frameless resizing

Frameless windows have no native resize border, so the host implements one when
`WindowSettings.Resizable` is `true`. A six-CSS-pixel invisible handle sits along
every client edge; corners combine two edges and show the appropriate diagonal
cursor. The resize interaction runs before page hit testing, so it still works
when a drag region or the inspector covers the whole client area. It respects
`MinWidth`, `MinHeight`, `MaxWidth` and `MaxHeight`, and is disabled for
maximized and full-screen windows.

## Performance notes

- Styles are cached per node revision, parent identity, viewport and renderer
  context version.
- Text layout is cached by text, style, width, wrapping and clamp settings.
- A frame only relayouts when `UILab.Renderer.Version`, the viewport, DPR or a
  stylesheet changes.
- Constant pools and instruction buffers are reused by the layout engine.
- The renderer keeps one VAO/VBO per batch kind and reuses index storage.
- The demo reports roughly **3.6 ms warm layout** and **0.7 ms GL render** for
  a full 1120×760 page on a typical desktop.
