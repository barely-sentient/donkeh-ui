using Donkeh.Data.Dom;

namespace Donkeh.Windowing;

/// <summary>
/// The built-in user-agent rules and element knowledge that make a plain HTML
/// tree lay out and paint sensibly without an author stylesheet.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Css"/> is parsed before every author sheet when
/// <see cref="DonkehWindow"/> rebuilds its styles, so ordinary author rules win
/// the cascade by source order even when specificity is equal. The inspector
/// stylesheet is appended after the author sheets.
/// </para>
/// <para>
/// The sheet mirrors Chromium's <c>html.css</c> user-agent defaults for the
/// light colour scheme, translated to the properties this engine implements.
/// Values that only exist because of a native widget (scrollbars, checkmarks,
/// date pickers and the like) are approximated, and properties the engine does
/// not model are omitted instead of being silently ignored.
/// </para>
/// <para>
/// The helper members work from tag names alone and never consult the cascade,
/// computed style or author CSS. They provide the element defaults used while
/// building the DOM and intrinsic sizes, and their tag sets mirror the
/// <c>display</c> declarations in <see cref="Css"/>.
/// </para>
/// </remarks>
/// <seealso cref="DonkehWindow"/>
public static class HtmlDefaults
{
    /// <summary>
    /// Chrome's user-agent defaults for HTML, limited to the CSS properties the
    /// engine understands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The document starts from Chrome's standard font settings: a 16 px serif
    /// face, black text and an 8 px body margin. Headings, paragraphs, lists,
    /// tables, form controls, embedded content and the remaining HTML5 elements
    /// then receive the same block, inline and table displays as Blink.
    /// </para>
    /// <para>
    /// A few deliberate deviations keep the engine usable: <c>br</c> is a block
    /// with a line height instead of an inline line-break object, list markers
    /// are not painted yet, and native widget chrome is approximated with
    /// borders and a light surface. Everything else is ordinary CSS that author
    /// sheets can override.
    /// </para>
    /// <para>
    /// The text is an ordinary CSS string parsed by the tokenizer each time the
    /// window rebuilds styles; it is not precompiled.
    /// </para>
    /// </remarks>
    public const string Css = """
        /*
         * Chromium user-agent defaults (light colour scheme), translated to the
         * properties this engine implements.
         */

        /* Blink's initial display is inline; the engine starts from block. */
        * { display: inline; }

        /* ---------------------------------------------------------- document */

        html { display: block; }
        body { display: block; margin: 8px; }

        html, body {
            color: #000000;
            font-family: "Times New Roman", Times, serif;
            font-size: 16px;
            line-height: normal;
        }

        /* Browsers paint the system highlight behind selected text and leave the
           glyphs in their normal colour when the highlight is translucent. */
        ::selection { background-color: rgba(0, 117, 255, 0.5); }

        /* --------------------------------------------- hidden and metadata */

        area, base, basefont, datalist, head, link, meta, noembed, noframes,
        noscript, param, rp, script, source, style, template, title, track {
            display: none !important;
        }

        [hidden] { display: none !important; }
        input[type="hidden"] { display: none !important; }

        /* --------------------------------------------------- block content */

        div, p, section, article, aside, header, footer, nav, main, figure,
        figcaption, blockquote, address, dl, dt, dd, ul, ol, menu, dir, li,
        form, fieldset, legend, details, summary, dialog, center, hr, pre,
        plaintext, listing, xmp, h1, h2, h3, h4, h5, h6, hgroup, search, br {
            display: block;
        }

        /* -------------------------------------------------------- headings */

        h1 { font-size: 2em;    font-weight: 700; margin: 0.67em 0; }
        h2 { font-size: 1.5em;  font-weight: 700; margin: 0.83em 0; }
        h3 { font-size: 1.17em; font-weight: 700; margin: 1em 0; }
        h4 { font-size: 1em;    font-weight: 700; margin: 1.33em 0; }
        h5 { font-size: 0.83em; font-weight: 700; margin: 1.67em 0; }
        h6 { font-size: 0.67em; font-weight: 700; margin: 2.33em 0; }

        p { margin: 1em 0; }
        blockquote { margin: 1em 40px; }
        figure { margin: 1em 40px; }
        center { text-align: center; }
        address { font-style: italic; }
        form { margin: 0 0 1em; }

        hr {
            margin: 0.5em 0;
            overflow: hidden;
            border: 1px inset #808080;
        }

        /* ----------------------------------------------------------- lists */

        ul, menu, dir, ol { margin: 1em 0; padding-left: 40px; }
        li { display: list-item; }
        dl { margin: 1em 0; }
        dd { margin-left: 40px; }

        fieldset {
            margin: 0 2px;
            padding: 0.35em 0.75em 0.625em;
            border: 2px groove #f0f0f0;
        }

        legend { padding: 0 2px; }

        /* ---------------------------------------------------------- tables */

        table {
            display: table;
            box-sizing: border-box;
            border-spacing: 2px;
            border-collapse: separate;
            text-indent: 0;
        }

        caption { display: table-caption; text-align: center; }
        thead { display: table-header-group; }
        tbody { display: table-row-group; }
        tfoot { display: table-footer-group; }
        col { display: table-column; }
        colgroup { display: table-column-group; }
        tr { display: table-row; }
        td, th { display: table-cell; padding: 1px; }
        th { font-weight: 700; text-align: center; }

        /* --------------------------------------------------- inline content */

        span, a, strong, b, em, i, u, s, small, sub, sup, code, kbd, samp,
        var, mark, abbr, acronym, cite, q, time, label, output, ruby, rt,
        bdi, bdo, big, strike, del, dfn, tt, nobr, data, ins {
            display: inline;
        }

        a:link { color: #0000ee; text-decoration: underline; cursor: pointer; }
        a:link:active { color: #ff0000; }

        strong, b { font-weight: 700; }
        i, cite, em, var, dfn { font-style: italic; }
        tt, code, kbd, samp { font-family: monospace; font-size: 13px; }

        pre, xmp, plaintext, listing {
            font-family: monospace;
            font-size: 13px;
            white-space: pre;
            margin: 1em 0;
        }

        mark { background-color: #ffff00; color: #000000; }
        big { font-size: 1.2em; }
        small { font-size: 83.33%; }
        u, ins { text-decoration: underline; }
        s, strike, del { text-decoration: line-through; }
        sub { vertical-align: -0.25em; font-size: 83.33%; }
        sup { vertical-align: 0.5em;   font-size: 83.33%; }
        abbr[title], acronym[title] { text-decoration: underline; }

        /* The engine has no inline line-break object, so br keeps a block box. */
        br { height: 1.2em; }

        /* --------------------------------------------------- form controls */

        input, textarea, select, button {
            display: inline-block;
            margin: 0;
            font-family: Arial, sans-serif;
            font-size: 13.3333px;
            line-height: normal;
            color: #000000;
            text-align: start;
            text-indent: 0;
        }

        label { cursor: default; }

        input {
            box-sizing: border-box;
            width: 177px;
            height: 21px;
            padding: 1px 2px;
            border: 1px solid #767676;
            background-color: #ffffff;
            cursor: text;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: clip;
        }

        textarea {
            box-sizing: border-box;
            width: 168px;
            height: 36px;
            padding: 2px;
            border: 1px solid #767676;
            background-color: #ffffff;
            font-family: monospace;
            white-space: pre-wrap;
            overflow: auto;
            cursor: text;
        }

        select {
            box-sizing: border-box;
            width: auto;
            height: 19px;
            padding: 0 18px 0 4px;
            border: 1px solid #767676;
            border-radius: 0;
            background-color: #ffffff;
            white-space: pre;
            overflow: hidden;
            text-overflow: ellipsis;
            cursor: default;
        }

        select option, select optgroup { display: none; }

        input[type="button"], input[type="submit"], input[type="reset"], button {
            box-sizing: border-box;
            width: auto;
            height: 21px;
            padding: 1px 6px;
            border: 1px solid #767676;
            border-radius: 2px;
            background-color: #f0f0f0;
            background-image: linear-gradient(#f5f5f5, #e9e9e9);
            box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.6);
            color: #000000;
            text-align: center;
            white-space: pre;
            cursor: default;
            user-select: none;
        }

        /* Native checkboxes, radios and ranges paint host chrome. The engine
           has no native widget layer, so a simple box stands in for it. */
        input[type="checkbox"], input[type="radio"] {
            box-sizing: border-box;
            width: 13px;
            height: 13px;
            margin: 3px 3px 3px 4px;
            padding: 0;
            border: 1px solid #767676;
            border-radius: 2px;
            background-color: #ffffff;
        }

        input[type="radio"] {
            margin: 3px 3px 0 5px;
            border-radius: 50%;
        }

        input[type="range"] {
            width: 129px;
            height: 16px;
            margin: 2px;
            padding: 0;
            border: 0;
            background-color: #ffffff;
        }

        input[type="color"] {
            width: 50px;
            height: 27px;
            padding: 1px 2px;
            border: 1px solid #000000;
            background-color: #f0f0f0;
            cursor: pointer;
        }

        input[type="file"] {
            width: 253px;
            height: 21px;
            padding: 0 0 0 88px;
            border: 0;
            background-color: transparent;
        }

        button:disabled,
        input[type="button"]:disabled,
        input[type="submit"]:disabled,
        input[type="reset"]:disabled {
            background-color: rgba(239, 239, 239, 0.3);
            background-image: none;
            box-shadow: none;
            border-color: rgba(118, 118, 118, 0.3);
            color: rgba(16, 16, 16, 0.3);
        }

        input:disabled, textarea:disabled {
            background-color: rgba(239, 239, 239, 0.3);
            color: #545454;
            cursor: default;
        }

        select:disabled { opacity: 0.7; }

        button:enabled:hover,
        input[type="button"]:enabled:hover,
        input[type="submit"]:enabled:hover,
        input[type="reset"]:enabled:hover {
            border-color: #5f5f5f;
            background-color: #ededed;
            background-image: linear-gradient(#f0f0f0, #e4e4e4);
        }

        button:enabled:active,
        input[type="button"]:enabled:active,
        input[type="submit"]:enabled:active,
        input[type="reset"]:enabled:active {
            border-color: #5f5f5f;
            background-color: #e5e5e5;
            background-image: none;
            box-shadow: inset 0 1px 2px rgba(0, 0, 0, 0.1);
        }

        /* Chrome paints a dark ink ring around buttons and a blue ring around
           editable fields. Both are two pixels wide; buttons leave a one-pixel
           gap so the page shows through. */
        :focus-visible { outline: 2px solid #101010; outline-offset: 1px; }
        html:focus-visible, body:focus-visible { outline-style: none; }
        input:not([type]):focus-visible,
        input[type="text"]:focus-visible,
        input[type="password"]:focus-visible,
        input[type="email"]:focus-visible,
        input[type="search"]:focus-visible,
        input[type="tel"]:focus-visible,
        input[type="url"]:focus-visible,
        input[type="number"]:focus-visible,
        textarea:focus-visible,
        select:focus-visible {
            outline-color: #0b57d0;
            outline-offset: 0;
        }

        /* ------------------------------------------------ replaced content */

        img, canvas, video { display: inline; overflow: hidden; }
        video { object-fit: contain; }

        audio { display: inline; width: 300px; height: 54px; }
        audio:not([controls]) { display: none !important; }

        iframe, webview {
            display: inline;
            border: 2px inset #767676;
            overflow: hidden;
        }

        object, embed { display: inline; overflow: hidden; }
        svg { display: inline; }
        marquee { display: inline-block; width: 100%; }

        progress, meter {
            display: inline-block;
            box-sizing: border-box;
            width: 10em;
            height: 1em;
            border: 0;
            background-color: transparent;
            vertical-align: -0.2em;
        }

        meter { width: 5em; }

        /* -------------------------------------------------------- ruby etc. */

        ruby, rt { text-indent: 0; display: inline; }
        ruby > rt { font-size: 50%; }

        details { display: block; }
        summary { display: block; }

        /* Chrome centres a non-modal dialog with auto inline margins. The engine
           resolves fit-content absolute widths against both insets instead, so
           the same result is expressed with a percentage offset and transform. */
        dialog {
            position: absolute;
            left: 50%;
            width: fit-content;
            height: fit-content;
            margin: 1em 0;
            padding: 1em;
            border: 3px solid #000000;
            background-color: #ffffff;
            color: #000000;
            transform: translateX(-50%);
        }

        dialog:not([open]) { display: none !important; }
        dialog[open] { display: block; }

        slot { display: contents; }
        """;

    /// <summary>
    /// Tags treated as block-level containers by <see cref="DisplayFor"/>.
    /// </summary>
    /// <remarks>
    /// Includes document roots, sectioning and flow content, headings, lists,
    /// form containers and the legacy presentational blocks. Comparison is
    /// case-insensitive. The set mirrors the <c>display: block</c> declarations
    /// in <see cref="Css"/> and must be kept in sync with it manually.
    /// </remarks>
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "body", "div", "p", "section", "article", "aside", "header",
        "footer", "nav", "main", "search", "figure", "figcaption", "hgroup",
        "blockquote", "address", "dl", "dt", "dd", "ul", "ol", "menu", "dir",
        "form", "fieldset", "legend", "details", "summary", "dialog", "center",
        "hr", "pre", "plaintext", "listing", "xmp", "h1", "h2", "h3", "h4",
        "h5", "h6", "br", "frameset", "frame"
    };

    /// <summary>
    /// Tags treated as inline content by <see cref="DisplayFor"/>.
    /// </summary>
    /// <remarks>
    /// Covers phrasing content, replaced media and inline SVG. Comparison is
    /// case-insensitive, and the set mirrors the inline declarations in
    /// <see cref="Css"/>.
    /// </remarks>
    private static readonly HashSet<string> InlineTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "span", "a", "strong", "em", "b", "i", "u", "s", "small", "sub", "sup",
        "code", "kbd", "samp", "var", "mark", "abbr", "acronym", "cite", "q",
        "time", "label", "output", "ruby", "rt", "bdi", "bdo", "big", "strike",
        "del", "dfn", "tt", "nobr", "data", "ins", "img", "canvas", "video",
        "audio", "iframe", "object", "embed", "svg", "webview"
    };

    /// <summary>
    /// Tags that establish an inline-block formatting context by default.
    /// </summary>
    /// <remarks>
    /// Form controls and the progress/meter widgets use Chrome's
    /// <c>inline-block</c> display. Comparison is case-insensitive.
    /// </remarks>
    private static readonly HashSet<string> InlineBlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "input", "textarea", "select", "button", "progress", "meter", "marquee"
    };

    /// <summary>
    /// Tags suppressed by the user-agent sheet's <c>display: none</c> rules.
    /// </summary>
    /// <remarks>
    /// Document metadata, stylesheet and script carriers, templates and replaced
    /// content sources live here. The set drives <see cref="IsHidden"/> and the
    /// <c>display: none</c> branch of <see cref="DisplayFor"/>.
    /// </remarks>
    private static readonly HashSet<string> HiddenTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "basefont", "datalist", "head", "link", "meta",
        "noembed", "noframes", "noscript", "param", "rp", "script", "source",
        "style", "template", "title", "track"
    };

    /// <summary>Table-internal tags and the display keyword Chrome gives them.</summary>
    private static readonly Dictionary<string, string> TableTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["table"] = "table",
        ["caption"] = "table-caption",
        ["col"] = "table-column",
        ["colgroup"] = "table-column-group",
        ["thead"] = "table-header-group",
        ["tbody"] = "table-row-group",
        ["tfoot"] = "table-footer-group",
        ["tr"] = "table-row",
        ["td"] = "table-cell",
        ["th"] = "table-cell"
    };

    /// <summary>True for tags that are hidden by the user-agent sheet.</summary>
    /// <param name="tag">The element tag to test; may be <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the tag is in the metadata/script set;
    /// <see langword="false"/> for <see langword="null"/> or any other tag.
    /// </returns>
    /// <remarks>
    /// Comparison ignores case. This is a tag-name test, not a computed-style
    /// query: it ignores <c>display</c> overrides, the <c>hidden</c> attribute
    /// and author rules, so it is only suitable for default element handling.
    /// </remarks>
    public static bool IsHidden(string? tag) => tag is not null && HiddenTags.Contains(tag);

    /// <summary>The default display keyword for a tag.</summary>
    /// <param name="tag">The element tag, or <see langword="null"/> for the root.</param>
    /// <returns>
    /// <c>none</c> for hidden metadata tags, the matching table display for
    /// table elements, <c>inline</c> for phrasing and replaced content,
    /// <c>inline-block</c> for form controls, <c>list-item</c> for list items,
    /// and <c>block</c> for flow content and <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// The keyword is the user-agent default only; the resolved style may replace
    /// it. Tag comparison is case-insensitive. Unknown custom elements default to
    /// <c>inline</c>, matching a browser's initial <c>display</c> value.
    /// </remarks>
    public static string DisplayFor(string? tag)
    {
        if (tag is null)
        {
            return "block";
        }

        if (HiddenTags.Contains(tag))
        {
            return "none";
        }

        if (TableTags.TryGetValue(tag, out var tableDisplay))
        {
            return tableDisplay;
        }

        if (tag.Equals("li", StringComparison.OrdinalIgnoreCase))
        {
            return "list-item";
        }

        if (tag.Equals("slot", StringComparison.OrdinalIgnoreCase))
        {
            return "contents";
        }

        if (InlineTags.Contains(tag))
        {
            return "inline";
        }

        if (InlineBlockTags.Contains(tag))
        {
            return "inline-block";
        }

        if (BlockTags.Contains(tag))
        {
            return "block";
        }

        return "inline";
    }

    /// <summary>
    /// True for elements that have replaced content and therefore need an
    /// intrinsic size rather than normal flow sizing.
    /// </summary>
    /// <param name="tag">The element tag; may be <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> for <c>img</c>, <c>canvas</c>, <c>video</c>,
    /// <c>audio</c>, <c>iframe</c>, <c>object</c>, <c>embed</c> and
    /// <c>webview</c>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Form controls such as <c>input</c>, <c>select</c> and <c>textarea</c> are
    /// intentionally excluded; they participate in normal box sizing. The check
    /// is ordinal case-sensitive, so callers should pass normalised lower-case
    /// tags.
    /// </remarks>
    public static bool IsReplaced(string? tag) => tag is "img" or "canvas" or "video"
        or "audio" or "iframe" or "object" or "embed" or "webview";

    /// <summary>
    /// Reads a length attribute such as <c>width="320"</c> on an image. CSS
    /// pixels are used when no unit is supplied.
    /// </summary>
    /// <param name="node">The element to read from; must not be <see langword="null"/>.</param>
    /// <param name="name">The attribute name, for example <c>width</c> or <c>height</c>.</param>
    /// <param name="fallback">
    /// The value returned when the attribute is absent or unparsable.
    /// </param>
    /// <returns>
    /// The numeric prefix of the attribute converted to a float, or
    /// <paramref name="fallback"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The scanner accepts digits plus <c>.</c>, <c>-</c>, <c>+</c> and spaces,
    /// then stops at the first other character. A trailing unit such as
    /// <c>px</c> or <c>%</c> is therefore ignored and the number is treated as CSS
    /// pixels, so percentage attributes are not resolved proportionally here.
    /// </para>
    /// <para>
    /// Parsing uses <see cref="float.TryParse(ReadOnlySpan{char}, out float)"/> and
    /// therefore the current culture; a decimal comma in a comma-decimal locale is
    /// not treated as part of the number because the scanner stops there. The
    /// result is not clamped, so negative and zero values pass through.
    /// </para>
    /// </remarks>
    public static float AttributeLength(IgniteSupportedDomNode node, string name, float fallback)
    {
        if (!node.TryGetAttribute(name, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        text = text.Trim();
        var end = 0;

        while (end < text.Length && (char.IsDigit(text[end]) || text[end] is '.' or '-' or '+' or ' '))
        {
            end++;
        }

        if (end == 0 || !float.TryParse(text.AsSpan(0, end), out var value))
        {
            return fallback;
        }

        return value;
    }
}
