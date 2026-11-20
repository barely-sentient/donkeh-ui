# HTML elements demo

A plain HTML document rendered with the framework's Chrome user-agent defaults.
The app adds no author CSS, so the window shows exactly what
`HtmlDefaults.Css` and the unit tests describe.

```powershell
dotnet run --project Donkeh/HtmlElementsDemo/Donkeh.HtmlElementsDemo.csproj
```

The page includes block content, headings, lists, inline text, tables, form
controls, replaced/embedded content, `details` and `dialog[open]`.

Known visual gaps that come from the engine rather than the stylesheet:

- list markers (`list-style-type`) are not painted yet;
- checkboxes, radios and ranges are simple painted boxes instead of native
  widgets;
- `input[type=color]` paints its current value as a swatch and opens the native
  Windows colour chooser on desktop; other platforms currently show the swatch
  only;
- `input[type=file]` paints Chrome's Choose File button and opens the native
  Windows file chooser on desktop;
- `br` is a block with a line height because the engine has no inline
  line-break object.
