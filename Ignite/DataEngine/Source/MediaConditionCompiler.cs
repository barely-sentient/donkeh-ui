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
    /// comparisons, orientation, reduced motion and media types). Unsupported
    /// syntax fails closed so a rule is never applied against an environment the
    /// engine cannot verify.
    /// </remarks>
    public static class MediaConditionCompiler
    {
        public static readonly ConditionCompiler Instance = Compile;

        private const float InitialFontSize = 16;

        private enum MediaType
        {
            All,
            Screen,
            Print,
            Unknown
        }

        private enum FeatureKind
        {
            Unsupported,
            MinWidth,
            MaxWidth,
            Width,
            MinHeight,
            MaxHeight,
            Height,
            Orientation,
            ReducedMotion
        }

        private readonly struct MediaFeature
        {
            public MediaFeature(FeatureKind kind, float value = 0, bool flag = false)
            {
                Kind = kind;
                Value = value;
                Flag = flag;
            }

            public FeatureKind Kind { get; }
            public float Value { get; }
            public bool Flag { get; }

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

        private readonly struct MediaQuery
        {
            public MediaQuery(bool negated, MediaType type, MediaFeature[] features)
            {
                Negated = negated;
                Type = type;
                Features = features;
            }

            public bool Negated { get; }
            public MediaType Type { get; }
            public MediaFeature[] Features { get; }

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

        private static bool IsName(in Token token, string name)
        {
            return token.Kind == TokenKind.AtKeyword
                && string.Equals(GetText(in token), name, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetText(in Token token)
        {
            return InternTable.GetString(token.InternedId) ?? string.Empty;
        }
    }
}
