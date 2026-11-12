namespace Donkeh.UILab
{
    /// <summary>
    /// Strongly named factories for the standard HTML elements.
    /// </summary>
    /// <remarks>
    /// Each method is a thin overload over <see cref="_.H(string, object?, Node?[])"/>
    /// that fixes the tag name. Props are optional in the sense that callers may
    /// pass <see langword="null"/> for elements without attributes, and media
    /// elements accept their typed props such as <c>CanvasProps</c>,
    /// <c>AudioProps</c> and <c>VideoProps</c> because those derive from
    /// <see cref="Props"/>.
    /// </remarks>
    /// <example>
    /// Add a static import to keep markup terse:
    /// <code>
    /// using static Donkeh.UILab.HtmlHelper;
    ///
    /// var page = Main(new Props { ClassName = "page" },
    ///     H1(new Props(), Text("Hello")),
    ///     P(new Props(), Text("Typed HTML helpers.")));
    /// </code>
    /// </example>
    public static class HtmlHelper
    {
        // ------------------------------------------------------------ document

        /// <summary>Creates a <c>html</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Html(Props props, params Node[] children) => _.H("html", props, children);

        /// <summary>Creates a <c>head</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Head(Props props, params Node[] children) => _.H("head", props, children);

        /// <summary>Creates a <c>title</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Title(Props props, params Node[] children) => _.H("title", props, children);

        /// <summary>Creates a <c>base</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Base(Props props, params Node[] children) => _.H("base", props, children);

        /// <summary>Creates a <c>link</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Link(Props props, params Node[] children) => _.H("link", props, children);

        /// <summary>Creates a <c>meta</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Meta(Props props, params Node[] children) => _.H("meta", props, children);

        /// <summary>Creates a <c>style</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Style(Props props, params Node[] children) => _.H("style", props, children);

        /// <summary>Creates a <c>body</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Body(Props props, params Node[] children) => _.H("body", props, children);

        // -------------------------------------------------------------- sections

        /// <summary>Creates a <c>article</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Article(Props props, params Node[] children) => _.H("article", props, children);

        /// <summary>Creates a <c>section</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Section(Props props, params Node[] children) => _.H("section", props, children);

        /// <summary>Creates a <c>nav</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Nav(Props props, params Node[] children) => _.H("nav", props, children);

        /// <summary>Creates a <c>aside</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Aside(Props props, params Node[] children) => _.H("aside", props, children);

        /// <summary>Creates a <c>h1</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node H1(Props props, params Node[] children) => _.H("h1", props, children);

        /// <summary>Creates a <c>h2</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node H2(Props props, params Node[] children) => _.H("h2", props, children);

        /// <summary>Creates a <c>h3</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node H3(Props props, params Node[] children) => _.H("h3", props, children);

        /// <summary>Creates a <c>h4</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node H4(Props props, params Node[] children) => _.H("h4", props, children);

        /// <summary>Creates a <c>h5</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node H5(Props props, params Node[] children) => _.H("h5", props, children);

        /// <summary>Creates a <c>h6</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node H6(Props props, params Node[] children) => _.H("h6", props, children);

        /// <summary>Creates a <c>hgroup</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Hgroup(Props props, params Node[] children) => _.H("hgroup", props, children);

        /// <summary>Creates a <c>header</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Header(Props props, params Node[] children) => _.H("header", props, children);

        /// <summary>Creates a <c>footer</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Footer(Props props, params Node[] children) => _.H("footer", props, children);

        /// <summary>Creates a <c>address</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Address(Props props, params Node[] children) => _.H("address", props, children);

        /// <summary>Creates a <c>main</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Main(Props props, params Node[] children) => _.H("main", props, children);

        /// <summary>Creates a <c>search</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Search(Props props, params Node[] children) => _.H("search", props, children);

        // -------------------------------------------------------------- grouping

        /// <summary>Creates a <c>div</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Div(Props props, params Node[] children) => _.H("div", props, children);

        /// <summary>Creates a <c>p</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node P(Props props, params Node[] children) => _.H("p", props, children);

        /// <summary>Creates a <c>hr</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Hr(Props props, params Node[] children) => _.H("hr", props, children);

        /// <summary>Creates a <c>pre</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Pre(Props props, params Node[] children) => _.H("pre", props, children);

        /// <summary>Creates a <c>blockquote</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Blockquote(Props props, params Node[] children) => _.H("blockquote", props, children);

        /// <summary>Creates a <c>ol</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Ol(Props props, params Node[] children) => _.H("ol", props, children);

        /// <summary>Creates a <c>ul</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Ul(Props props, params Node[] children) => _.H("ul", props, children);

        /// <summary>Creates a <c>menu</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Menu(Props props, params Node[] children) => _.H("menu", props, children);

        /// <summary>Creates a <c>li</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Li(Props props, params Node[] children) => _.H("li", props, children);

        /// <summary>Creates a <c>dl</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Dl(Props props, params Node[] children) => _.H("dl", props, children);

        /// <summary>Creates a <c>dt</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Dt(Props props, params Node[] children) => _.H("dt", props, children);

        /// <summary>Creates a <c>dd</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Dd(Props props, params Node[] children) => _.H("dd", props, children);

        /// <summary>Creates a <c>figure</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Figure(Props props, params Node[] children) => _.H("figure", props, children);

        /// <summary>Creates a <c>figcaption</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Figcaption(Props props, params Node[] children) => _.H("figcaption", props, children);

        // ------------------------------------------------------- text-level content

        /// <summary>Creates a <c>a</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node A(Props props, params Node[] children) => _.H("a", props, children);

        /// <summary>Creates a <c>em</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Em(Props props, params Node[] children) => _.H("em", props, children);

        /// <summary>Creates a <c>strong</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Strong(Props props, params Node[] children) => _.H("strong", props, children);

        /// <summary>Creates a <c>small</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Small(Props props, params Node[] children) => _.H("small", props, children);

        /// <summary>Creates a <c>s</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node S(Props props, params Node[] children) => _.H("s", props, children);

        /// <summary>Creates a <c>cite</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Cite(Props props, params Node[] children) => _.H("cite", props, children);

        /// <summary>Creates a <c>q</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Q(Props props, params Node[] children) => _.H("q", props, children);

        /// <summary>Creates a <c>dfn</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Dfn(Props props, params Node[] children) => _.H("dfn", props, children);

        /// <summary>Creates a <c>abbr</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Abbr(Props props, params Node[] children) => _.H("abbr", props, children);

        /// <summary>Creates a <c>ruby</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Ruby(Props props, params Node[] children) => _.H("ruby", props, children);

        /// <summary>Creates a <c>rt</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Rt(Props props, params Node[] children) => _.H("rt", props, children);

        /// <summary>Creates a <c>rp</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Rp(Props props, params Node[] children) => _.H("rp", props, children);

        /// <summary>Creates a <c>data</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Data(Props props, params Node[] children) => _.H("data", props, children);

        /// <summary>Creates a <c>time</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Time(Props props, params Node[] children) => _.H("time", props, children);

        /// <summary>Creates a <c>code</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Code(Props props, params Node[] children) => _.H("code", props, children);

        /// <summary>Creates a <c>var</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Var(Props props, params Node[] children) => _.H("var", props, children);

        /// <summary>Creates a <c>samp</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Samp(Props props, params Node[] children) => _.H("samp", props, children);

        /// <summary>Creates a <c>kbd</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Kbd(Props props, params Node[] children) => _.H("kbd", props, children);

        /// <summary>Creates a <c>sub</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Sub(Props props, params Node[] children) => _.H("sub", props, children);

        /// <summary>Creates a <c>sup</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Sup(Props props, params Node[] children) => _.H("sup", props, children);

        /// <summary>Creates a <c>i</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node I(Props props, params Node[] children) => _.H("i", props, children);

        /// <summary>Creates a <c>b</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node B(Props props, params Node[] children) => _.H("b", props, children);

        /// <summary>Creates a <c>u</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node U(Props props, params Node[] children) => _.H("u", props, children);

        /// <summary>Creates a <c>mark</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Mark(Props props, params Node[] children) => _.H("mark", props, children);

        /// <summary>Creates a <c>bdi</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Bdi(Props props, params Node[] children) => _.H("bdi", props, children);

        /// <summary>Creates a <c>bdo</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Bdo(Props props, params Node[] children) => _.H("bdo", props, children);

        /// <summary>Creates a <c>span</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Span(Props props, params Node[] children) => _.H("span", props, children);

        /// <summary>Creates a <c>br</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Br(Props props, params Node[] children) => _.H("br", props, children);

        /// <summary>Creates a <c>wbr</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Wbr(Props props, params Node[] children) => _.H("wbr", props, children);

        /// <summary>Creates a <c>ins</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Ins(Props props, params Node[] children) => _.H("ins", props, children);

        /// <summary>Creates a <c>del</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Del(Props props, params Node[] children) => _.H("del", props, children);

        // ------------------------------------------------------------- embedded

        /// <summary>Creates a <c>picture</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Picture(Props props, params Node[] children) => _.H("picture", props, children);

        /// <summary>Creates a <c>source</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Source(Props props, params Node[] children) => _.H("source", props, children);

        /// <summary>Creates a <c>img</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Img(Props props, params Node[] children) => _.H("img", props, children);

        /// <summary>Creates a <c>iframe</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Iframe(Props props, params Node[] children) => _.H("iframe", props, children);

        /// <summary>Creates a <c>embed</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Embed(Props props, params Node[] children) => _.H("embed", props, children);

        /// <summary>Creates a <c>object</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Object(Props props, params Node[] children) => _.H("object", props, children);

        /// <summary>Creates a <c>param</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Param(Props props, params Node[] children) => _.H("param", props, children);

        /// <summary>Creates a <c>video</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Video(Props props, params Node[] children) => _.H("video", props, children);

        /// <summary>Creates a <c>audio</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Audio(Props props, params Node[] children) => _.H("audio", props, children);

        /// <summary>Creates a <c>track</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Track(Props props, params Node[] children) => _.H("track", props, children);

        /// <summary>Creates a <c>map</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Map(Props props, params Node[] children) => _.H("map", props, children);

        /// <summary>Creates a <c>area</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Area(Props props, params Node[] children) => _.H("area", props, children);

        /// <summary>Creates a <c>canvas</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Canvas(Props props, params Node[] children) => _.H("canvas", props, children);

        // ------------------------------------------------------------- tabular

        /// <summary>Creates a <c>table</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Table(Props props, params Node[] children) => _.H("table", props, children);

        /// <summary>Creates a <c>caption</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Caption(Props props, params Node[] children) => _.H("caption", props, children);

        /// <summary>Creates a <c>colgroup</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Colgroup(Props props, params Node[] children) => _.H("colgroup", props, children);

        /// <summary>Creates a <c>col</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Col(Props props, params Node[] children) => _.H("col", props, children);

        /// <summary>Creates a <c>tbody</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Tbody(Props props, params Node[] children) => _.H("tbody", props, children);

        /// <summary>Creates a <c>thead</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Thead(Props props, params Node[] children) => _.H("thead", props, children);

        /// <summary>Creates a <c>tfoot</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Tfoot(Props props, params Node[] children) => _.H("tfoot", props, children);

        /// <summary>Creates a <c>tr</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Tr(Props props, params Node[] children) => _.H("tr", props, children);

        /// <summary>Creates a <c>td</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Td(Props props, params Node[] children) => _.H("td", props, children);

        /// <summary>Creates a <c>th</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Th(Props props, params Node[] children) => _.H("th", props, children);

        // ---------------------------------------------------------------- forms

        /// <summary>Creates a <c>form</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Form(Props props, params Node[] children) => _.H("form", props, children);

        /// <summary>Creates a <c>label</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Label(Props props, params Node[] children) => _.H("label", props, children);

        /// <summary>Creates a <c>input</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Input(Props props, params Node[] children) => _.H("input", props, children);

        /// <summary>Creates a <c>button</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Button(Props props, params Node[] children) => _.H("button", props, children);

        /// <summary>Creates a <c>select</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Select(Props props, params Node[] children) => _.H("select", props, children);

        /// <summary>Creates a <c>datalist</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Datalist(Props props, params Node[] children) => _.H("datalist", props, children);

        /// <summary>Creates a <c>optgroup</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Optgroup(Props props, params Node[] children) => _.H("optgroup", props, children);

        /// <summary>Creates a <c>option</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Option(Props props, params Node[] children) => _.H("option", props, children);

        /// <summary>Creates a <c>textarea</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Textarea(Props props, params Node[] children) => _.H("textarea", props, children);

        /// <summary>Creates a <c>output</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Output(Props props, params Node[] children) => _.H("output", props, children);

        /// <summary>Creates a <c>progress</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Progress(Props props, params Node[] children) => _.H("progress", props, children);

        /// <summary>Creates a <c>meter</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Meter(Props props, params Node[] children) => _.H("meter", props, children);

        /// <summary>Creates a <c>fieldset</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Fieldset(Props props, params Node[] children) => _.H("fieldset", props, children);

        /// <summary>Creates a <c>legend</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Legend(Props props, params Node[] children) => _.H("legend", props, children);

        // ----------------------------------------------------------- interactive

        /// <summary>Creates a <c>details</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Details(Props props, params Node[] children) => _.H("details", props, children);

        /// <summary>Creates a <c>summary</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Summary(Props props, params Node[] children) => _.H("summary", props, children);

        /// <summary>Creates a <c>dialog</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Dialog(Props props, params Node[] children) => _.H("dialog", props, children);

        // ------------------------------------------------------------ scripting

        /// <summary>Creates a <c>script</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Script(Props props, params Node[] children) => _.H("script", props, children);

        /// <summary>Creates a <c>noscript</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Noscript(Props props, params Node[] children) => _.H("noscript", props, children);

        /// <summary>Creates a <c>template</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Template(Props props, params Node[] children) => _.H("template", props, children);

        /// <summary>Creates a <c>slot</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Slot(Props props, params Node[] children) => _.H("slot", props, children);

        // ------------------------------------------------------------ deprecated

        /// <summary>Creates a <c>acronym</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Acronym(Props props, params Node[] children) => _.H("acronym", props, children);

        /// <summary>Creates a <c>applet</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Applet(Props props, params Node[] children) => _.H("applet", props, children);

        /// <summary>Creates a <c>basefont</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Basefont(Props props, params Node[] children) => _.H("basefont", props, children);

        /// <summary>Creates a <c>bgsound</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Bgsound(Props props, params Node[] children) => _.H("bgsound", props, children);

        /// <summary>Creates a <c>big</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Big(Props props, params Node[] children) => _.H("big", props, children);

        /// <summary>Creates a <c>blink</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Blink(Props props, params Node[] children) => _.H("blink", props, children);

        /// <summary>Creates a <c>center</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Center(Props props, params Node[] children) => _.H("center", props, children);

        /// <summary>Creates a <c>dir</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Dir(Props props, params Node[] children) => _.H("dir", props, children);

        /// <summary>Creates a <c>font</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Font(Props props, params Node[] children) => _.H("font", props, children);

        /// <summary>Creates a <c>frame</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Frame(Props props, params Node[] children) => _.H("frame", props, children);

        /// <summary>Creates a <c>frameset</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Frameset(Props props, params Node[] children) => _.H("frameset", props, children);

        /// <summary>Creates a <c>isindex</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Isindex(Props props, params Node[] children) => _.H("isindex", props, children);

        /// <summary>Creates a <c>keygen</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Keygen(Props props, params Node[] children) => _.H("keygen", props, children);

        /// <summary>Creates a <c>marquee</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Marquee(Props props, params Node[] children) => _.H("marquee", props, children);

        /// <summary>Creates a <c>menuitem</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Menuitem(Props props, params Node[] children) => _.H("menuitem", props, children);

        /// <summary>Creates a <c>nobr</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Nobr(Props props, params Node[] children) => _.H("nobr", props, children);

        /// <summary>Creates a <c>noembed</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Noembed(Props props, params Node[] children) => _.H("noembed", props, children);

        /// <summary>Creates a <c>noframes</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Noframes(Props props, params Node[] children) => _.H("noframes", props, children);

        /// <summary>Creates a <c>plaintext</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Plaintext(Props props, params Node[] children) => _.H("plaintext", props, children);

        /// <summary>Creates a <c>rb</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Rb(Props props, params Node[] children) => _.H("rb", props, children);

        /// <summary>Creates a <c>rtc</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Rtc(Props props, params Node[] children) => _.H("rtc", props, children);

        /// <summary>Creates a <c>spacer</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Spacer(Props props, params Node[] children) => _.H("spacer", props, children);

        /// <summary>Creates a <c>strike</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Strike(Props props, params Node[] children) => _.H("strike", props, children);

        /// <summary>Creates a <c>tt</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Tt(Props props, params Node[] children) => _.H("tt", props, children);

        /// <summary>Creates a <c>xmp</c> element.</summary>
        /// <param name="props">Properties for the element.</param>
        /// <param name="children">Child nodes.</param>
        /// <returns>The created element.</returns>
        public static Node Xmp(Props props, params Node[] children) => _.H("xmp", props, children);
    }
}

