using System.Text;
using System.Text.RegularExpressions;

namespace Donkeh.Llm.Components;

/// <summary>
/// A deliberately small C# source scanner used for component discovery and CSX
/// inspection. It does not compile code and it never claims to be a full parser:
/// it masks comments and strings, balances braces and reads declarations that
/// follow the public component conventions.
/// </summary>
internal static class SourceScanner
{
    /// <summary>Returns a copy of <paramref name="source"/> with comments and strings blanked.</summary>
    public static string Mask(string source)
    {
        var buffer = source.ToCharArray();
        var state = State.Code;
        var verbatim = false;

        for (var i = 0; i < buffer.Length; i++)
        {
            var current = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            switch (state)
            {
                case State.Code:
                    if (current == '/' && next == '/')
                    {
                        state = State.LineComment;
                        buffer[i] = ' ';
                        buffer[i + 1] = ' ';
                        i++;
                    }
                    else if (current == '/' && next == '*')
                    {
                        state = State.BlockComment;
                        buffer[i] = ' ';
                        buffer[i + 1] = ' ';
                        i++;
                    }
                    else if (current == '\'')
                    {
                        state = State.Char;
                        buffer[i] = ' ';
                    }
                    else if (current == '"')
                    {
                        var prefix = i > 0 ? source[i - 1] : '\0';
                        verbatim = prefix == '@';
                        state = State.String;
                        buffer[i] = ' ';
                    }
                    else if (current == '@' && next == '"')
                    {
                        verbatim = true;
                        state = State.String;
                        buffer[i] = ' ';
                        buffer[i + 1] = ' ';
                        i++;
                    }

                    break;

                case State.LineComment:
                    if (current == '\n')
                    {
                        state = State.Code;
                    }
                    else
                    {
                        buffer[i] = ' ';
                    }

                    break;

                case State.BlockComment:
                    if (current == '*' && next == '/')
                    {
                        buffer[i] = ' ';
                        buffer[i + 1] = ' ';
                        i++;
                        state = State.Code;
                    }
                    else if (current != '\n' && current != '\r')
                    {
                        buffer[i] = ' ';
                    }

                    break;

                case State.String:
                    if (verbatim && current == '"' && next == '"')
                    {
                        buffer[i] = ' ';
                        buffer[i + 1] = ' ';
                        i++;
                    }
                    else if (!verbatim && current == '\\' && next != '\0')
                    {
                        buffer[i] = ' ';
                        buffer[i + 1] = ' ';
                        i++;
                    }
                    else if (current == '"')
                    {
                        buffer[i] = ' ';
                        state = State.Code;
                    }
                    else if (current != '\n' && current != '\r')
                    {
                        buffer[i] = ' ';
                    }

                    break;

                case State.Char:
                    if (current == '\\' && next != '\0')
                    {
                        buffer[i] = ' ';
                        buffer[i + 1] = ' ';
                        i++;
                    }
                    else if (current == '\'')
                    {
                        buffer[i] = ' ';
                        state = State.Code;
                    }
                    else if (current != '\n' && current != '\r')
                    {
                        buffer[i] = ' ';
                    }

                    break;
            }
        }

        return new string(buffer);
    }

    /// <summary>Finds the matching closing brace for an opening brace.</summary>
    public static int FindBlockEnd(string masked, int openIndex)
    {
        var depth = 0;

        for (var i = openIndex; i < masked.Length; i++)
        {
            if (masked[i] == '{')
            {
                depth++;
            }
            else if (masked[i] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return masked.Length - 1;
    }

    /// <summary>Finds the namespace that contains a source offset.</summary>
    public static string? NamespaceAt(string masked, int offset)
    {
        var matches = Regex.Matches(
            masked[..Math.Min(offset, masked.Length)],
            @"\bnamespace\s+(?<name>[A-Za-z_][\w.]*)",
            RegexOptions.None);

        return matches.Count == 0 ? null : matches[^1].Groups["name"].Value;
    }

    /// <summary>Reads using directives from the whole file.</summary>
    public static List<string> Usings(string masked)
    {
        return Regex.Matches(masked, @"\busing\s+(?:static\s+)?(?<name>[A-Za-z_][\w.]*)\s*;")
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Extracts an XML documentation element that precedes an offset.</summary>
    public static string? DocElement(string source, int offset, string element)
    {
        var start = Math.Max(0, offset - 4000);
        var before = source[start..offset];
        var match = Regex.Matches(
            before,
            $@"///\s*<{element}>(?<text>.*?)</{element}>",
            RegexOptions.Singleline);

        if (match.Count == 0)
        {
            return null;
        }

        return CleanDoc(match[^1].Groups["text"].Value);
    }

    /// <summary>Extracts an XML documentation summary that precedes an offset.</summary>
    public static string? Summary(string source, int offset) => DocElement(source, offset, "summary");

    /// <summary>Strips XML documentation syntax and collapses whitespace.</summary>
    public static string? CleanDoc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = Regex.Replace(value, @"^\s*///\s?", string.Empty, RegexOptions.Multiline);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Splits a PascalCase identifier into searchable words.</summary>
    public static IEnumerable<string> Words(string value)
    {
        foreach (Match match in Regex.Matches(value, @"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+"))
        {
            var word = match.Value;

            if (word.Length > 1)
            {
                yield return word.ToLowerInvariant();
            }
        }
    }

    private enum State
    {
        Code,
        LineComment,
        BlockComment,
        String,
        Char
    }
}
