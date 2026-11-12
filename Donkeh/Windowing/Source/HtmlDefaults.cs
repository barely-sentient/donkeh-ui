using Donkeh.Data.Dom;

namespace Donkeh.Windowing;

/// <summary>
/// The built-in user-agent rules and element knowledge that make a plain HTML
/// tree lay out and paint sensibly without an author stylesheet.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Css"/> is parsed before every author sheet when
/// <see cref="IgniteWindow"/> rebuilds its styles, so ordinary author rules win
/// the cascade by source order even when specificity is equal. The inspector
/// stylesheet is appended after the author sheets.
/// </para>
/// <para>
/// The helper members work from tag names alone and never consult the cascade,
/// computed style or author CSS. They provide the element defaults used while
/// building the DOM and intrinsic sizes, and their tag sets largely mirror the
/// <c>display</c> declarations in <see cref="Css"/>.
/// </para>
/// </remarks>
/// <seealso cref="IgniteWindow"/>
public static class HtmlDefaults
{
    /// <summary>
    /// A compact user-agent stylesheet. It covers the structural HTML5 element
    /// list; exotic media elements get a predictable placeholder box instead of
    /// silently collapsing. Author rules are appended after this sheet, so they
    /// always win the cascade.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sheet starts with <c>box-sizing: border-box</c> for every element and
    /// pseudo-element, then sets the document defaults: block flow, zero margins
    /// and padding, a 15 px Segoe UI stack, 1.45 line height, dark text on white.
    /// </para>
    /// <para>
    /// Section order matters because later rules override earlier ones within the
    /// same specificity: hidden metadata elements, block elements, inline
    /// elements, replaced/media elements, headings, paragraphs and lists, inline
    /// text semantics, links, rules and breaks, form controls, tables, then image,
    /// media, progress and dialog sizing. Media placeholders default to 300 by
    /// 150 px, inputs and selects to a 32 px minimum height, and textareas to
    /// 72 px.
    /// </para>
    /// <para>
    /// The text is an ordinary CSS string parsed by the tokenizer each time the
    /// window rebuilds styles; it is not precompiled.
    /// </para>
    /// </remarks>
    public const string Css = """
        *, *::before, *::after { box-sizing: border-box; }

        html, body {
            display: block;
            margin: 0;
            padding: 0;
            font-family: "Segoe UI", "Helvetica Neue", Arial, sans-serif;
            font-size: 15px;
            line-height: 1.45;
            color: #1f2328;
            background-color: #ffffff;
        }

        head, title, meta, link, style, script, base, template, noscript,
        param, source, track, datalist { display: none; }

        div, p, section, article, aside, header, footer, nav, main, figure,
        figcaption, blockquote, address, dl, dt, dd, ul, ol, li, form,
        fieldset, legend, details, summary, dialog, center, hr, pre,
        h1, h2, h3, h4, h5, h6 {
            display: block;
        }

        span, a, strong, em, b, i, u, s, small, sub, sup, code, kbd, samp,
        var, mark, abbr, cite, q, time, label, output, ruby, rt, rp, bdi, bdo {
            display: inline;
        }

        img, input, button, select, textarea, progress, meter, canvas, video,
        audio, iframe, object, embed, svg {
            display: inline-block;
        }

        h1 { font-size: 2em;    font-weight: 700; margin: 0.67em 0; }
        h2 { font-size: 1.5em;  font-weight: 700; margin: 0.83em 0; }
        h3 { font-size: 1.25em; font-weight: 700; margin: 1em 0; }
        h4 { font-size: 1em;    font-weight: 700; margin: 1.33em 0; }
        h5 { font-size: 0.9em;  font-weight: 700; margin: 1.67em 0; }
        h6 { font-size: 0.8em;  font-weight: 700; margin: 2em 0; }

        p { margin: 0.75em 0; }
        ul, ol { margin: 0.75em 0; padding-left: 1.75em; }
        li { display: block; margin: 0.15em 0; }
        dl { margin: 0.75em 0; }
        dt { font-weight: 700; }
        dd { margin-left: 1.5em; }
        blockquote { margin: 1em 0; padding-left: 1em; border-left-width: 3px; border-left-style: solid; border-left-color: #d0d7de; color: #57606a; }
        pre { margin: 0.75em 0; padding: 12px; overflow-x: auto; background-color: #f6f8fa; border-radius: 6px; font-family: Consolas, "Courier New", monospace; font-size: 13px; }
        code, kbd, samp { font-family: Consolas, "Courier New", monospace; font-size: 0.9em; }
        mark { background-color: #fff8c5; color: inherit; }
        small { font-size: 0.85em; }
        sub, sup { font-size: 0.75em; }
        a { color: #0969da; text-decoration: underline; }
        hr { border: 0; border-top-width: 1px; border-top-style: solid; border-top-color: #d0d7de; margin: 1em 0; height: 0; }
        br { display: block; height: 1.2em; }

        button, input, select, textarea {
            font-family: inherit;
            font-size: 14px;
            color: inherit;
            background-color: #f6f8fa;
            border-width: 1px;
            border-style: solid;
            border-color: #d0d7de;
            border-radius: 6px;
            padding: 6px 10px;
            margin: 0;
        }

        button { cursor: pointer; font-weight: 600; }
        input { min-height: 32px; }
        textarea { width: 120px; min-height: 72px; white-space: pre-wrap; }
        select { min-height: 32px; }
        select option { display: none; }
        input[type="checkbox"], input[type="radio"] { min-height: 0; padding: 0; }
        button:disabled, input:disabled, select:disabled, textarea:disabled { color: #8c959f; background-color: #eff2f5; }

        table { display: table; width: 100%; margin: 0.75em 0; border-collapse: collapse; }
        thead, tbody, tfoot { display: table-row-group; }
        tr { display: table-row; }
        td, th { display: table-cell; padding: 6px 10px; border-width: 1px; border-style: solid; border-color: #d0d7de; vertical-align: top; }
        th { font-weight: 700; background-color: #f6f8fa; }
        caption { display: block; font-weight: 600; }

        img { max-width: 100%; }
        canvas, video, iframe, object, embed { width: 300px; height: 150px; background-color: #f6f8fa; }
        progress, meter { width: 180px; height: 14px; background-color: #eff2f5; border-width: 1px; border-style: solid; border-color: #d0d7de; border-radius: 7px; padding: 0; }
        fieldset { margin: 0 0 1em 0; padding: 0.75em 1em; border-width: 1px; border-style: solid; border-color: #d0d7de; border-radius: 6px; }
        legend { padding: 0 4px; font-weight: 600; }
        details { margin: 0.75em 0; }
        summary { font-weight: 600; }
        dialog { margin: 1em auto; padding: 1em; border-width: 1px; border-style: solid; border-color: #d0d7de; border-radius: 8px; background-color: #ffffff; }
        """;

    /// <summary>
    /// Tags treated as block-level containers by <see cref="DisplayFor"/>.
    /// </summary>
    /// <remarks>
    /// Includes document roots, sectioning and flow content, headings, lists and
    /// the table structure elements. Comparison is case-insensitive. The set
    /// mirrors the <c>display: block</c> and table <c>display</c> declarations in
    /// <see cref="Css"/> and must be kept in sync with it manually.
    /// </remarks>
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "body", "div", "p", "section", "article", "aside", "header",
        "footer", "nav", "main", "figure", "figcaption", "blockquote", "address",
        "dl", "dt", "dd", "ul", "ol", "li", "form", "fieldset", "legend",
        "details", "summary", "dialog", "center", "hr", "pre", "h1", "h2",
        "h3", "h4", "h5", "h6", "table", "thead", "tbody", "tfoot", "tr"
    };

    /// <summary>
    /// Tags treated as inline text content by <see cref="DisplayFor"/>.
    /// </summary>
    /// <remarks>
    /// Covers phrasing content such as links, emphasis, code, quotations and Ruby
    /// annotations. Comparison is case-insensitive, and the set mirrors the
    /// <c>display: inline</c> rule in <see cref="Css"/>.
    /// </remarks>
    private static readonly HashSet<string> InlineTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "span", "a", "strong", "em", "b", "i", "u", "s", "small", "sub", "sup",
        "code", "kbd", "samp", "var", "mark", "abbr", "cite", "q", "time",
        "label", "output", "ruby", "rt", "rp", "bdi", "bdo"
    };

    /// <summary>
    /// Tags suppressed by the user-agent sheet's <c>display: none</c> rule.
    /// </summary>
    /// <remarks>
    /// Document metadata, stylesheet and script carriers, templates and replaced
    /// content sources live here. The set drives <see cref="IsHidden"/> and the
    /// <c>display: none</c> branch of <see cref="DisplayFor"/>; it is matched by
    /// tag name only, so author CSS may still override the visual result.
    /// </remarks>
    private static readonly HashSet<string> HiddenTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "head", "title", "meta", "link", "style", "script", "base", "template",
        "noscript", "param", "source", "track", "datalist"
    };

    /// <summary>True for tags that are hidden by the user-agent sheet.</summary>
    /// <param name="tag">The element tag to test; may be <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the tag is in the metadata/script set;
    /// <see langword="false"/> for <see langword="null"/> or any other tag.
    /// </returns>
    /// <remarks>
    /// Comparison ignores case. This is a tag-name test, not a computed-style
    /// query: it ignores <c>display</c> overrides, <c>hidden</c> attributes and
    /// author rules, so it is only suitable for default element handling.
    /// </remarks>
    public static bool IsHidden(string? tag) => tag is not null && HiddenTags.Contains(tag);

    /// <summary>The default display keyword for a tag.</summary>
    /// <param name="tag">The element tag, or <see langword="null"/> for the root.</param>
    /// <returns>
    /// <c>none</c> for hidden metadata tags, <c>inline</c> for phrasing content,
    /// <c>block</c> for flow and table structure elements and for
    /// <see langword="null"/>, and <c>inline-block</c> for anything unknown
    /// (including form controls and media placeholders).
    /// </returns>
    /// <remarks>
    /// The keyword is the user-agent default only; the resolved style may replace
    /// it. Tag comparison is case-insensitive and unknown custom elements
    /// deliberately default to <c>inline-block</c> so they still get a box.
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

        if (InlineTags.Contains(tag))
        {
            return "inline";
        }

        if (BlockTags.Contains(tag))
        {
            return "block";
        }

        return "inline-block";
    }

    /// <summary>
    /// True for elements that have replaced content and therefore need an
    /// intrinsic size rather than normal flow sizing.
    /// </summary>
    /// <param name="tag">The element tag; may be <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> for <c>img</c>, <c>canvas</c>, <c>video</c>,
    /// <c>audio</c>, <c>iframe</c>, <c>object</c> and <c>embed</c>; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Form controls such as <c>input</c>, <c>select</c> and <c>textarea</c> are
    /// intentionally excluded; they participate in normal box sizing. The check
    /// is ordinal case-sensitive, so callers should pass normalised lower-case
    /// tags.
    /// </remarks>
    public static bool IsReplaced(string? tag) => tag is "img" or "canvas" or "video"
        or "audio" or "iframe" or "object" or "embed";

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
