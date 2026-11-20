# DonkehFrameworkTests

XUnit tests that lock in the framework's core HTML element behaviour and the
Chrome-style user-agent defaults in `Donkeh.Windowing.HtmlDefaults.Css`.

| File | What it covers |
| --- | --- |
| `HtmlDisplayDefaultsTests` | `display` for every HTML element, hidden carriers, `[hidden]`, `dialog[open]`, audio controls |
| `HtmlTypographyDefaultsTests` | document font, heading scale, semantic weight/style, code faces, decorations, links |
| `HtmlBoxDefaultsTests` | resolved margins, padding and borders for flow content, lists, fieldsets and rules |
| `HtmlFormControlDefaultsTests` | input, textarea, select, button, checkbox/radio, range/color/file, disabled and focus states |
| `HtmlTableDefaultsTests` | table display roles, separate borders, cell padding, headers and captions |
| `HtmlReplacedContentTests` | canvas, video, iframe, object/embed, image attributes, progress and meter |
| `HtmlElementBehaviourTests` | block stacking, inline flow, list indentation, click/submit/input events and pseudo classes |
| `HtmlDefaultsHelperTests` | `DisplayFor`, `IsHidden`, `IsReplaced` and `AttributeLength` |
| `HtmlDefaultsStylesheetTests` | every UA declaration is understood by the style resolver |

Run them with:

```powershell
dotnet test Donkeh/DonkehFrameworkTests/DonkehFrameworkTests.csproj
```
