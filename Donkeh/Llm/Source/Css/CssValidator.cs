using System.Text.RegularExpressions;
using Donkeh.Data;
using Donkeh.DataEngine;
using Donkeh.LayoutLab;
using Donkeh.Parser;
using Donkeh.Parser.Objects;

namespace Donkeh.Llm.Css;

/// <summary>Validates stylesheets with the shipped tokenizer, data engine and property resolver.</summary>
public static class CssValidator
{
    private static readonly HashSet<string> GlobalKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "inherit", "initial", "unset", "revert", "revert-layer"
    };

    /// <summary>Validates one CSS file.</summary>
    public static CssValidationResult Validate(string file)
    {
        var fullPath = Path.GetFullPath(file);

        if (!File.Exists(fullPath))
        {
            throw new LlmException("CSS0000", $"CSS file not found: {file}");
        }

        var source = File.ReadAllText(fullPath);
        var diagnostics = new List<LlmDiagnostic>();
        List<Token> tokens;

        try
        {
            tokens = Tokenizer.Parse(source);
        }
        catch (Exception exception)
        {
            return new CssValidationResult
            {
                File = file,
                Valid = false,
                Diagnostics =
                [
                    LlmDiagnostics.Error("CSS0001", $"The CSS tokenizer rejected this file: {exception.Message}",
                        new LlmLocation { File = file, Line = 1, Column = 1 })
                ]
            };
        }

        ValidateBalance(source, tokens, file, diagnostics);
        DataTable table;

        try
        {
            table = new Donkeh.DataEngine.DataEngine(MediaConditionCompiler.Instance).Build(tokens);
        }
        catch (Exception exception)
        {
            diagnostics.Add(LlmDiagnostics.Error(
                "CSS0002",
                $"The CSS data engine rejected this file: {exception.Message}",
                new LlmLocation { File = file, Line = 1, Column = 1 }));

            return new CssValidationResult
            {
                File = file,
                Valid = false,
                Diagnostics = diagnostics
            };
        }

        var supported = CssCatalog.Properties().ToDictionary(
            property => property.Name,
            property => property,
            StringComparer.OrdinalIgnoreCase);
        var declarationCursor = 0;
        var unsupportedProperties = 0;
        var unsupportedSelectors = 0;

        foreach (var rule in table.Rules)
        {
            var propertyName = InternTable.GetString(rule.PropertyId);

            if (string.IsNullOrWhiteSpace(propertyName)
                || propertyName.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var location = FindDeclaration(source, propertyName, ref declarationCursor, file);
            var cssProperty = CssPropertyIds.Get(rule.PropertyId);

            if (cssProperty == CssProperty.Unknown)
            {
                unsupportedProperties++;
                diagnostics.Add(LlmDiagnostics.Warning(
                    "CSS1001",
                    $"Unsupported CSS property '{propertyName}'.",
                    location,
                    Closest(propertyName, supported.Keys).ToList()));
                continue;
            }

            if (supported.TryGetValue(propertyName, out var info)
                && string.Equals(info.Status, "unsupported", StringComparison.OrdinalIgnoreCase))
            {
                unsupportedProperties++;
                diagnostics.Add(LlmDiagnostics.Warning(
                    "CSS1002",
                    $"CSS property '{propertyName}' is parsed but not supported by the current engine.",
                    location,
                    info.Notes is null ? null : [info.Notes]));
            }

            var value = InternTable.GetString(rule.ValueId);

            if (value is not null
                && CssCatalog.ValuesFor(propertyName) is { Count: > 0 } values
                && IsSingleKeyword(value, values, out var normalized)
                && !values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.Add(LlmDiagnostics.Warning(
                    "CSS1004",
                    $"Unsupported value '{value}' for '{propertyName}'.",
                    location,
                    [$"Supported values: {string.Join(", ", values)}"]));
            }
        }

        foreach (var record in table.Selectors)
        {
            if (record.IsMatchable)
            {
                continue;
            }

            unsupportedSelectors++;
            var selector = SelectorText(source, table, record);

            diagnostics.Add(LlmDiagnostics.Warning(
                "CSS1003",
                $"Selector '{selector}' uses syntax the Donkeh matcher cannot evaluate.",
                FindSelector(source, selector, file),
                ["Use type, class, id, universal, attribute, supported pseudo-classes and the four standard combinators."]));
        }

        diagnostics = diagnostics
            .GroupBy(diagnostic => (diagnostic.Code, diagnostic.Message, diagnostic.Location?.Line ?? 0,
                diagnostic.Location?.Column ?? 0))
            .Select(group => group.First())
            .OrderBy(diagnostic => diagnostic.Location?.Line ?? 0)
            .ThenBy(diagnostic => diagnostic.Location?.Column ?? 0)
            .ToList();

        return new CssValidationResult
        {
            File = file,
            Valid = diagnostics.All(diagnostic => diagnostic.Severity != "error"),
            Diagnostics = diagnostics,
            Summary = new CssValidationSummary
            {
                RuleCount = table.RuleCount,
                SelectorCount = table.SelectorCount,
                UnsupportedPropertyCount = unsupportedProperties,
                UnsupportedSelectorCount = unsupportedSelectors
            }
        };
    }

    private static void ValidateBalance(
        string source,
        List<Token> tokens,
        string file,
        List<LlmDiagnostic> diagnostics)
    {
        var braces = 0;
        var parentheses = 0;
        var brackets = 0;

        foreach (var token in tokens)
        {
            switch (token.Kind)
            {
                case TokenKind.LeftCurlyBrace: braces++; break;
                case TokenKind.RightCurlyBrace: braces--; break;
                case TokenKind.LeftParenthesis: parentheses++; break;
                case TokenKind.RightParenthesis: parentheses--; break;
                case TokenKind.LeftSquareBracket: brackets++; break;
                case TokenKind.RightSquareBracket: brackets--; break;
            }

            if (braces < 0 || parentheses < 0 || brackets < 0)
            {
                diagnostics.Add(LlmDiagnostics.Error(
                    "CSS0003",
                    "Unmatched closing delimiter.",
                    LineMap.Locate(file, source, token.StartIndex)));
                return;
            }
        }

        if (braces != 0)
        {
            diagnostics.Add(LlmDiagnostics.Error(
                "CSS0004",
                $"Unbalanced curly braces ({braces} unclosed).",
                new LlmLocation { File = file, Line = 1, Column = 1 }));
        }

        if (parentheses != 0)
        {
            diagnostics.Add(LlmDiagnostics.Error(
                "CSS0005",
                $"Unbalanced parentheses ({parentheses} unclosed).",
                new LlmLocation { File = file, Line = 1, Column = 1 }));
        }

        if (brackets != 0)
        {
            diagnostics.Add(LlmDiagnostics.Error(
                "CSS0006",
                $"Unbalanced square brackets ({brackets} unclosed).",
                new LlmLocation { File = file, Line = 1, Column = 1 }));
        }
    }

    private static bool IsSingleKeyword(
        string value,
        IReadOnlyList<string> allowed,
        out string normalized)
    {
        normalized = value.Trim();

        if (normalized.Length == 0
            || normalized.Contains('(')
            || normalized.Contains(' ')
            || normalized.Contains(',')
            || GlobalKeywords.Contains(normalized))
        {
            return false;
        }

        return true;
    }

    private static string SelectorText(string source, DataTable table, SelectorRecord record)
    {
        var id = -1;

        for (var i = 0; i < table.SelectorCount; i++)
        {
            var candidate = table.GetSelector(i);

            if (candidate.TokenOffset == record.TokenOffset
                && candidate.TokenLength == record.TokenLength)
            {
                id = i;
                break;
            }
        }

        if (id < 0)
        {
            return "";
        }

        return SelectorText(source, table, id);
    }

    private static string SelectorText(string source, DataTable table, int id)
    {
        var tokens = table.GetSelectorSpan(id);

        if (tokens.Length == 0)
        {
            return "";
        }

        var start = tokens[0].StartIndex;
        var end = tokens[^1].EndIndex;

        return start >= 0 && end <= source.Length && end > start
            ? source[start..end].Trim()
            : string.Concat(tokens.ToArray().Select(token =>
                InternTable.GetString(token.InternedId) ?? ""));
    }

    private static LlmLocation? FindDeclaration(
        string source,
        string propertyName,
        ref int cursor,
        string file)
    {
        var pattern = $@"(?<![\w-]){Regex.Escape(propertyName)}\s*:";
        var match = Regex.Match(source[cursor..], pattern, RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return null;
        }

        var offset = cursor + match.Index;
        cursor = offset + match.Length;
        return LineMap.Locate(file, source, offset);
    }

    private static LlmLocation? FindSelector(string source, string selector, string file)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        var index = source.IndexOf(selector, StringComparison.Ordinal);
        return index < 0 ? null : LineMap.Locate(file, source, index);
    }

    private static IEnumerable<string> Closest(string value, IEnumerable<string> candidates)
    {
        return candidates
            .Select(candidate => (Candidate: candidate, Distance: Levenshtein(value, candidate)))
            .Where(item => item.Distance <= Math.Max(2, value.Length / 3))
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Candidate, StringComparer.Ordinal)
            .Take(3)
            .Select(item => item.Candidate);
    }

    private static int Levenshtein(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= right.Length; j++)
            {
                var cost = char.ToLowerInvariant(left[i - 1]) == char.ToLowerInvariant(right[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
