using System.Globalization;
using Ignite.Data;
using Ignite.Parser;
using Ignite.Parser.Objects;

namespace Ignite.DataEngine
{
    /// <summary>
    /// Compiles CSS media queries into predicates over <see cref="RendererContext"/>.
    /// The window host supplies the viewport on the context, so responsive
    /// breakpoints re-evaluate whenever the window is resized.
    /// </summary>
    /// <remarks>
    /// This is a token-level compiler rather than an IL generator: it supports
    /// the media features browsers and Bootstrap actually use (width/height
    /// comparisons, orientation, reduced motion and media types). Reduced motion
    /// currently reports the browser default, <c>no-preference</c>, because the
    /// host has no preference to query. Unsupported syntax fails closed so a rule
    /// is never applied against an environment the engine cannot verify.
    /// </remarks>
    /// <example>
    /// <code>
    /// var engine = new DataEngine(MediaConditionCompiler.Instance);
    /// var table = engine.Build("@media (min-width: 768px) { .a { color: red; } }");
    /// </code>
    /// </example>
    public static class MediaConditionCompiler
    {
        /// <summary>
        /// The shared compiler delegate, suitable for passing to
        /// <see cref="DataEngine"/> or <see cref="DataTable.GetOrAddCondition"/>.
        /// </summary>
        /// <remarks>
        /// The delegate is stateless and thread-safe. Compiled queries capture only
        /// their parsed feature values, so a single instance can serve any number of
        /// tables.
        /// </remarks>
        public static readonly ConditionCompiler Instance = Compile;

        /// <summary>
        /// The CSS initial font size, in pixels, used as the basis for
        /// <c>em</c> and <c>rem</c> media-query lengths.
        /// </summary>
        private const float InitialFontSize = 16;

        /// <summary>
        /// The media type recognised in a query prelude.
        /// </summary>
        private enum MediaType
        {
            /// <summary>
            /// <c>all</c>, or an omitted media type. Matches every environment.
            /// </summary>
            All,
            /// <summary>
            /// <c>screen</c>. The window host always renders to a screen, so this
            /// matches like <see cref="MediaType.All"/>.
            /// </summary>
            Screen,
            /// <summary>
            /// <c>print</c>. The window host never renders print output, so this
            /// never matches.
            /// </summary>
            Print,
            /// <summary>
            /// A type the parser could not recognise. Unused on success; such a
            /// query is rejected instead.
            /// </summary>
            Unknown
        }

        /// <summary>
        /// The media feature recognised by <see cref="TryParseFeature"/>.
        /// </summary>
        private enum FeatureKind
        {
            /// <summary>
            /// A feature the engine cannot evaluate, such as <c>hover</c> or
            /// <c>prefers-color-scheme</c>. Always evaluates to
            /// <see langword="false"/> so the query fails closed.
            /// </summary>
            Unsupported,
            /// <summary>Viewport width is at least the parsed value.</summary>
            MinWidth,
            /// <summary>Viewport width is at most the parsed value.</summary>
            MaxWidth,
            /// <summary>Viewport width equals the parsed value within 0.01 px.</summary>
            Width,
            /// <summary>Viewport height is at least the parsed value.</summary>
            MinHeight,
            /// <summary>Viewport height is at most the parsed value.</summary>
            MaxHeight,
            /// <summary>Viewport height equals the parsed value within 0.01 px.</summary>
            Height,
            /// <summary>
            /// Viewport orientation; <see cref="MediaFeature.Flag"/> is
            /// <see langword="true"/> for portrait.
            /// </summary>
            Orientation,
            /// <summary>
            /// <c>prefers-reduced-motion</c>; <see cref="MediaFeature.Flag"/> is
            /// <see langword="true"/> for <c>reduce</c>. Since the host has no
            /// preference, <c>reduce</c> is always false and <c>no-preference</c>
            /// is always true.
            /// </summary>
            ReducedMotion
        }

        /// <summary>
        /// A single parsed media feature: its kind plus the numeric value or boolean
        /// flag that kind needs.
        /// </summary>
        private readonly struct MediaFeature
        {
            /// <summary>
            /// Initialises a media feature.
            /// </summary>
            /// <param name="kind">The feature kind.</param>
            /// <param name="value">The length in pixels, when the kind uses one.</param>
            /// <param name="flag">
            /// The boolean operand for <see cref="FeatureKind.Orientation"/> and
            /// <see cref="FeatureKind.ReducedMotion"/>.
            /// </param>
            public MediaFeature(FeatureKind kind, float value = 0, bool flag = false)
            {
                Kind = kind;
                Value = value;
                Flag = flag;
            }

            /// <summary>
            /// Gets the feature kind that determines how the operands are compared.
            /// </summary>
            public FeatureKind Kind { get; }
            /// <summary>
            /// Gets the comparison value in CSS pixels. Unused by orientation and
            /// reduced-motion features.
            /// </summary>
            public float Value { get; }
            /// <summary>
            /// Gets the boolean operand. For orientation, <see langword="true"/>
            /// means portrait; for reduced motion, <see langword="true"/> means
            /// <c>reduce</c>.
            /// </summary>
            public bool Flag { get; }

            /// <summary>
            /// Evaluates the feature against the current renderer environment.
            /// </summary>
            /// <param name="context">The environment snapshot to test.</param>
            /// <returns>
            /// <see langword="true"/> when the feature is satisfied. Unsupported
            /// features always return <see langword="false"/>.
            /// </returns>
            public bool Matches(in RendererContext context)
            {
                return Kind switch
                {
                    FeatureKind.MinWidth => context.ViewportWidth >= Value,
                    FeatureKind.MaxWidth => context.ViewportWidth <= Value,
                    FeatureKind.Width => MathF.Abs(context.ViewportWidth - Value) < 0.01f,
                    FeatureKind.MinHeight => context.ViewportHeight >= Value,
                    FeatureKind.MaxHeight => context.ViewportHeight <= Value,
                    FeatureKind.Height => MathF.Abs(context.ViewportHeight - Value) < 0.01f,
                    FeatureKind.Orientation =>
                        (context.ViewportHeight >= context.ViewportWidth) == Flag,
                    // The host has no reduced-motion preference wired yet, so the
                    // browser default ("no-preference") is the truthful answer.
                    FeatureKind.ReducedMotion => !Flag,
                    _ => false
                };
            }
        }

        /// <summary>
        /// One comma-separated media query: an optional media type, an optional
        /// <c>not</c>, and every feature that must hold.
        /// </summary>
        private readonly struct MediaQuery
        {
            /// <summary>
            /// Initialises a media query.
            /// </summary>
            /// <param name="negated">
            /// Whether a <c>not</c> keyword inverts the whole query result.
            /// </param>
            /// <param name="type">The media type, defaulting to <c>all</c>.</param>
            /// <param name="features">
            /// The features that must all match. May be empty for a bare type.
            /// </param>
            public MediaQuery(bool negated, MediaType type, MediaFeature[] features)
            {
                Negated = negated;
                Type = type;
                Features = features;
            }

            /// <summary>
            /// Gets a value indicating whether <c>not</c> inverts the result of the
            /// type and feature checks.
            /// </summary>
            public bool Negated { get; }
            /// <summary>
            /// Gets the media type. <see cref="MediaType.All"/> and
            /// <see cref="MediaType.Screen"/> are accepted; other values make the
            /// query fail.
            /// </summary>
            public MediaType Type { get; }
            /// <summary>
            /// Gets the features that must all hold for the query to match. The
            /// array is shared by every evaluation of the compiled condition and is
            /// never mutated.
            /// </summary>
            public MediaFeature[] Features { get; }

            /// <summary>
            /// Evaluates the query against the current renderer environment.
            /// </summary>
            /// <param name="context">The environment snapshot to test.</param>
            /// <returns>
            /// <see langword="true"/> when the type and every feature match,
            /// inverted when <see cref="Negated"/> is set.
            /// </returns>
            /// <remarks>
            /// Features are evaluated only when the type already matches, so an
            /// unsupported feature in a <c>print</c> query is never inspected.
            /// </remarks>
            public bool Matches(in RendererContext context)
            {
                var result = Type switch
                {
                    MediaType.All => true,
                    MediaType.Screen => true,
                    _ => false
                };

                if (result)
                {
                    for (var i = 0; i < Features.Length; i++)
                    {
                        if (!Features[i].Matches(in context))
                        {
                            result = false;
                            break;
                        }
                    }
                }

                return Negated ? !result : result;
            }
        }

        /// <summary>
        /// Compiles an <c>@media</c> prelude into a predicate over
        /// <see cref="RendererContext"/>.
        /// </summary>
        /// <param name="tokens">
        /// The condition prelude including the leading <c>@media</c> at-keyword and
        /// ending before the opening brace.
        /// </param>
        /// <returns>
        /// A predicate that evaluates the comma-separated query list, or
        /// <see cref="Condition.AlwaysFalse"/> when the prelude is not
        /// <c>@media</c>, names no queries, or contains a query this compiler
        /// cannot parse.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Queries are separated by top-level commas and combined with logical OR:
        /// the predicate succeeds as soon as one query matches. All other
        /// condition at-rules, including <c>@supports</c> and
        /// <c>@container</c>, fail closed here; a host can supply a different
        /// <see cref="ConditionCompiler"/> for those.
        /// </para>
        /// <para>
        /// Supported features are <c>width</c>, <c>height</c> and their
        /// <c>min-</c>/<c>max-</c> forms, <c>orientation</c>, and
        /// <c>prefers-reduced-motion</c>. Unknown features do not invalidate the
        /// whole prelude: they produce a feature that never matches, so other
        /// comma-separated queries can still succeed.
        /// </para>
        /// <para>
        /// The returned delegate captures only parsed values and is immutable, so
        /// it can be cached and evaluated from multiple threads as long as the
        /// supplied <see cref="RendererContext"/> is not mutated concurrently.
        /// Malformed input is rejected without throwing.
        /// </para>
        /// </remarks>
        public static ConditionFunction Compile(ReadOnlySpan<Token> tokens)
        {
            if (tokens.Length < 2
                || tokens[0].Kind != TokenKind.AtKeyword
                || !IsName(tokens[0], "@media"))
            {
                return Condition.AlwaysFalse;
            }

            var queries = new List<MediaQuery>();

            var index = 1;

            while (index < tokens.Length)
            {
                var start = index;

                while (index < tokens.Length && tokens[index].Kind != TokenKind.Comma)
                {
                    index++;
                }

                if (index > start)
                {
                    if (!TryParseQuery(tokens.Slice(start, index - start), out var query))
                    {
                        return Condition.AlwaysFalse;
                    }

                    queries.Add(query);
                }

                index++;
            }

            if (queries.Count == 0)
            {
                return Condition.AlwaysFalse;
            }

            var compiled = queries.ToArray();

            return (in RendererContext context) =>
            {
                for (var i = 0; i < compiled.Length; i++)
                {
                    if (compiled[i].Matches(in context))
                    {
                        return true;
                    }
                }

                return false;
            };
        }

        /// <summary>
        /// Parses one comma-separated media query.
        /// </summary>
        /// <param name="tokens">The tokens of the query, excluding the comma.</param>
        /// <param name="query">
        /// Receives the parsed query on success; <see langword="default"/> on
        /// failure.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the query uses only supported syntax.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The parser accepts an optional leading media type, the modifiers
        /// <c>not</c> and <c>only</c>, the connector <c>and</c>, and parenthesised
        /// <c>(feature: value)</c> pairs. <c>not</c> may appear anywhere and
        /// negates the entire query; <c>only</c> and <c>and</c> are ignored. Any
        /// other identifier, a second media type, an unbalanced parenthesis, or an
        /// unparsable feature rejects the query.
        /// </para>
        /// <para>
        /// Range syntax such as <c>(width &gt;= 600px)</c> is not supported and
        /// rejects the query, which makes the whole condition fail closed.
        /// </para>
        /// </remarks>
        private static bool TryParseQuery(ReadOnlySpan<Token> tokens, out MediaQuery query)
        {
            query = default;

            var negated = false;
            var type = MediaType.All;
            var hasType = false;
            var features = new List<MediaFeature>();
            var index = 0;

            while (index < tokens.Length)
            {
                var token = tokens[index];

                if (token.Kind == TokenKind.Identifier)
                {
                    var name = GetText(in token);

                    if (string.Equals(name, "not", StringComparison.OrdinalIgnoreCase))
                    {
                        negated = true;
                        index++;
                        continue;
                    }

                    if (string.Equals(name, "only", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "and", StringComparison.OrdinalIgnoreCase))
                    {
                        index++;
                        continue;
                    }

                    if (!hasType && TryParseMediaType(name, out var parsedType))
                    {
                        type = parsedType;
                        hasType = true;
                        index++;
                        continue;
                    }

                    return false;
                }

                if (token.Kind == TokenKind.LeftParenthesis)
                {
                    var close = FindClosingParenthesis(tokens, index);

                    if (close < 0)
                    {
                        return false;
                    }

                    if (!TryParseFeature(tokens[(index + 1)..close], out var feature))
                    {
                        return false;
                    }

                    features.Add(feature);
                    index = close + 1;
                    continue;
                }

                return false;
            }

            query = new MediaQuery(negated, type, features.ToArray());
            return true;
        }

        /// <summary>
        /// Maps a media type name to <see cref="MediaType"/>.
        /// </summary>
        /// <param name="name">The identifier text.</param>
        /// <param name="type">
        /// Receives the parsed type on success; <see cref="MediaType.Unknown"/>
        /// otherwise.
        /// </param>
        /// <returns>
        /// <see langword="true"/> for <c>all</c>, <c>screen</c> and <c>print</c>,
        /// case-insensitively.
        /// </returns>
        private static bool TryParseMediaType(string name, out MediaType type)
        {
            if (string.Equals(name, "all", StringComparison.OrdinalIgnoreCase))
            {
                type = MediaType.All;
                return true;
            }

            if (string.Equals(name, "screen", StringComparison.OrdinalIgnoreCase))
            {
                type = MediaType.Screen;
                return true;
            }

            if (string.Equals(name, "print", StringComparison.OrdinalIgnoreCase))
            {
                type = MediaType.Print;
                return true;
            }

            type = MediaType.Unknown;
            return false;
        }

        /// <summary>
        /// Parses the inside of a feature parenthesis, such as
        /// <c>min-width: 768px</c>.
        /// </summary>
        /// <param name="tokens">
        /// The tokens between the parentheses, excluding the parentheses
        /// themselves.
        /// </param>
        /// <param name="feature">
        /// Receives the parsed feature, including an always-false feature for one
        /// this compiler does not understand.
        /// </param>
        /// <returns>
        /// <see langword="false"/> when the syntax is malformed: no name, no
        /// colon/value, or an unsupported value for a supported feature.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Recognised names are <c>min-width</c>, <c>max-width</c>, <c>width</c>,
        /// <c>min-height</c>, <c>max-height</c>, <c>height</c>,
        /// <c>orientation</c> (<c>portrait</c>/<c>landscape</c>) and
        /// <c>prefers-reduced-motion</c> (<c>reduce</c>/<c>no-preference</c>).
        /// </para>
        /// <para>
        /// Unknown names return <see langword="true"/> with an
        /// <see cref="FeatureKind.Unsupported"/> feature. That keeps the failure
        /// local to the query instead of discarding the entire media query list.
        /// Names are compared with the exact case in the source; CSS media feature
        /// names are normally written in lower case.
        /// </para>
        /// </remarks>
        private static bool TryParseFeature(ReadOnlySpan<Token> tokens, out MediaFeature feature)
        {
            feature = default;

            if (tokens.Length == 0
                || tokens[0].Kind is not (TokenKind.Identifier or TokenKind.PropertyName))
            {
                return false;
            }

            var name = GetText(tokens[0]);
            var valueStart = tokens.Length > 2 && tokens[1].Kind == TokenKind.Colon ? 2 : -1;

            switch (name)
            {
                case "min-width":
                case "max-width":
                case "width":
                case "min-height":
                case "max-height":
                case "height":
                {
                    if (valueStart < 0 || !TryParseLength(tokens[valueStart..], out var value))
                    {
                        return false;
                    }

                    var kind = name switch
                    {
                        "min-width" => FeatureKind.MinWidth,
                        "max-width" => FeatureKind.MaxWidth,
                        "width" => FeatureKind.Width,
                        "min-height" => FeatureKind.MinHeight,
                        "max-height" => FeatureKind.MaxHeight,
                        _ => FeatureKind.Height
                    };

                    feature = new MediaFeature(kind, value);
                    return true;
                }

                case "orientation":
                {
                    if (valueStart < 0)
                    {
                        return false;
                    }

                    var value = GetText(tokens[valueStart]);

                    if (string.Equals(value, "portrait", StringComparison.OrdinalIgnoreCase))
                    {
                        feature = new MediaFeature(FeatureKind.Orientation, flag: true);
                        return true;
                    }

                    if (string.Equals(value, "landscape", StringComparison.OrdinalIgnoreCase))
                    {
                        feature = new MediaFeature(FeatureKind.Orientation, flag: false);
                        return true;
                    }

                    return false;
                }

                case "prefers-reduced-motion":
                {
                    if (valueStart < 0)
                    {
                        return false;
                    }

                    var value = GetText(tokens[valueStart]);

                    if (string.Equals(value, "reduce", StringComparison.OrdinalIgnoreCase))
                    {
                        feature = new MediaFeature(FeatureKind.ReducedMotion, flag: true);
                        return true;
                    }

                    if (string.Equals(value, "no-preference", StringComparison.OrdinalIgnoreCase))
                    {
                        feature = new MediaFeature(FeatureKind.ReducedMotion, flag: false);
                        return true;
                    }

                    return false;
                }

                default:
                    // A feature this environment cannot verify must not make a
                    // rule fire. Returning a false feature keeps the query
                    // conservative without rejecting the whole stylesheet.
                    feature = new MediaFeature(FeatureKind.Unsupported);
                    return true;
            }
        }

        /// <summary>
        /// Parses a single-token CSS length into CSS pixels.
        /// </summary>
        /// <param name="tokens">The tokens of the length value.</param>
        /// <param name="pixels">
        /// Receives the length in pixels on success, or zero on failure.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the value is a finite number followed by a
        /// supported unit, or by no unit at all.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Supported units are <c>px</c>, <c>em</c> and <c>rem</c> (at 16 px),
        /// <c>in</c> (96 px), <c>cm</c>, <c>mm</c>, <c>pt</c> and <c>pc</c>. An
        /// absent unit is treated as pixels.
        /// </para>
        /// <para>
        /// The value must be exactly one token: <c>calc()</c>, <c>min()</c> and
        /// other functional values are not evaluated. Percentages and relative
        /// viewport units such as <c>vw</c> and <c>vh</c> are unsupported, so they
        /// cause the feature, and therefore the whole condition, to be rejected
        /// rather than silently using the wrong scale. Unit names are matched
        /// case-sensitively, and parsing uses the invariant culture, so a decimal
        /// point is never treated as a group separator.
        /// </para>
        /// </remarks>
        private static bool TryParseLength(ReadOnlySpan<Token> tokens, out float pixels)
        {
            pixels = 0;

            if (tokens.Length == 0)
            {
                return false;
            }

            var value = GetText(tokens[0]);

            if (tokens.Length > 1)
            {
                // calc(), min() and other functions are not evaluated here.
                return false;
            }

            var end = 0;

            while (end < value.Length
                && (char.IsDigit(value[end]) || value[end] is '.' or '-' or '+'))
            {
                end++;
            }

            if (end == 0
                || !float.TryParse(
                    value.AsSpan(0, end),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var number))
            {
                return false;
            }

            var unit = value[end..];

            pixels = unit switch
            {
                "" or "px" => number,
                "em" or "rem" => number * InitialFontSize,
                "in" => number * 96,
                "cm" => number * 96 / 2.54f,
                "mm" => number * 96 / 25.4f,
                "pt" => number * 96 / 72,
                "pc" => number * 16,
                _ => float.NaN
            };

            return float.IsFinite(pixels);
        }

        /// <summary>
        /// Finds the parenthesis that closes the one at <paramref name="open"/>,
        /// counting nested pairs.
        /// </summary>
        /// <param name="tokens">The token stream to scan.</param>
        /// <param name="open">Index of the opening parenthesis.</param>
        /// <returns>
        /// The index of the matching closing parenthesis, or -1 when the stream
        /// ends first.
        /// </returns>
        /// <remarks>
        /// The scan is bounded by the supplied span, which for a query is the tokens
        /// between commas; an unbalanced parenthesis therefore cannot consume the
        /// next query.
        /// </remarks>
        private static int FindClosingParenthesis(ReadOnlySpan<Token> tokens, int open)
        {
            var depth = 0;

            for (var i = open; i < tokens.Length; i++)
            {
                if (tokens[i].Kind == TokenKind.LeftParenthesis)
                {
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.RightParenthesis)
                {
                    depth--;

                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Determines whether a token is the specified at-keyword.
        /// </summary>
        /// <param name="token">The token to test, by readonly reference.</param>
        /// <param name="name">The expected name, including <c>@</c>.</param>
        /// <returns>
        /// <see langword="true"/> when the token is an at-keyword with matching
        /// interned text, compared case-insensitively.
        /// </returns>
        private static bool IsName(in Token token, string name)
        {
            return token.Kind == TokenKind.AtKeyword
                && string.Equals(GetText(in token), name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves a token's interned text.
        /// </summary>
        /// <param name="token">The token whose text is needed.</param>
        /// <returns>
        /// The interned text, or <see cref="string.Empty"/> when the token has no
        /// interned text, which makes unknown tokens behave like empty identifiers.
        /// </returns>
        private static string GetText(in Token token)
        {
            return InternTable.GetString(token.InternedId) ?? string.Empty;
        }
    }
}
