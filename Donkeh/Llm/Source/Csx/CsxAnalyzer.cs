using System.Text.RegularExpressions;
using CSX.Parser;
using Donkeh.Llm.Components;

namespace Donkeh.Llm.Csx;

/// <summary>Parses a CSX file with the shipped CSX tokenizer and reports its structure.</summary>
public static class CsxAnalyzer
{
    /// <summary>Analyzes a CSX file. The file is read as UTF-8 text.</summary>
    public static CsxInspectionResult Analyze(string file)
    {
        var fullPath = Path.GetFullPath(file);

        if (!File.Exists(fullPath))
        {
            throw new LlmException("CSX0000", $"CSX file not found: {file}");
        }

        var source = File.ReadAllText(fullPath);
        var tokens = Tokenizer.Tokenize(source);
        var elements = new List<CsxElementInfo>();
        var attributes = new List<CsxAttributeInfo>();
        var expressions = new List<CsxExpressionInfo>();
        var diagnostics = new List<LlmDiagnostic>();
        var stack = new Stack<OpenElement>();
        var declared = DeclaredTypes(source);
        var componentNames = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < tokens.Count;)
        {
            var token = tokens[index];

            switch (token.Kind)
            {
                case TokenKind.JsxOpenBracket:
                {
                    var element = ReadElement(source, tokens, ref index, stack.Count, attributes, expressions, diagnostics, file);
                    elements.Add(element.Info);

                    if (element.Info.Kind == "component")
                    {
                        componentNames.Add(element.Info.Name);
                    }

                    if (!element.Info.SelfClosing)
                    {
                        stack.Push(new OpenElement(element.Info, token.StartPosition));
                    }

                    break;
                }

                case TokenKind.JsxCloseTagOpen:
                {
                    var offset = token.StartPosition;
                    index++;
                    var name = "";
                    var kind = "fragment";

                    if (index < tokens.Count && tokens[index].Kind == TokenKind.JsxTagName)
                    {
                        name = tokens[index].Text ?? "";
                        kind = "element";
                        index++;
                    }
                    else if (index < tokens.Count && tokens[index].Kind == TokenKind.JsxComponentName)
                    {
                        name = tokens[index].Text ?? "";
                        kind = "component";
                        index++;
                    }

                    if (index < tokens.Count && tokens[index].Kind == TokenKind.JsxCloseBracket)
                    {
                        index++;
                    }

                    CloseElement(name, kind, offset, stack, diagnostics, source, file);
                    break;
                }

                case TokenKind.OpenBrace when stack.Count > 0:
                {
                    var expression = ReadExpression(source, tokens, ref index, "child", file, diagnostics);
                    if (expression is not null)
                    {
                        expressions.Add(expression);
                    }

                    break;
                }

                default:
                    index++;
                    break;
            }
        }

        foreach (var open in stack)
        {
            diagnostics.Add(LlmDiagnostics.Error(
                "CSX0002",
                $"Element '<{Describe(open.Element)}>' is not closed.",
                LineMap.Locate(file, source, open.Offset),
                [$"Add </{Describe(open.Element)}> before the enclosing element ends."]));
        }

        var referenced = componentNames
            .Where(name => !declared.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        var dependencies = SourceScanner.Usings(SourceScanner.Mask(source))
            .Concat(componentNames)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new CsxInspectionResult
        {
            File = file,
            Valid = diagnostics.All(diagnostic => diagnostic.Severity != "error"),
            Elements = elements,
            Components = componentNames.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            Attributes = attributes,
            Expressions = expressions,
            Dependencies = dependencies,
            ReferencedTypes = referenced,
            Diagnostics = diagnostics,
            Summary = new CsxSummary
            {
                ElementCount = elements.Count(element => element.Kind == "element"),
                ComponentCount = elements.Count(element => element.Kind == "component"),
                AttributeCount = attributes.Count,
                ExpressionCount = expressions.Count,
                DistinctComponentCount = componentNames.Count
            }
        };
    }

    private static ParsedElement ReadElement(
        string source,
        List<Token> tokens,
        ref int index,
        int depth,
        List<CsxAttributeInfo> attributes,
        List<CsxExpressionInfo> expressions,
        List<LlmDiagnostic> diagnostics,
        string file)
    {
        var start = tokens[index].StartPosition;
        index++;
        var name = "";
        var kind = "fragment";
        var elementAttributes = new List<CsxAttributeInfo>();

        if (index < tokens.Count && tokens[index].Kind == TokenKind.JsxTagName)
        {
            name = tokens[index].Text ?? "";
            kind = "element";
            index++;
        }
        else if (index < tokens.Count && tokens[index].Kind == TokenKind.JsxComponentName)
        {
            name = tokens[index].Text ?? "";
            kind = "component";
            index++;
        }

        // Optional generic argument list on a component tag.
        if (index < tokens.Count && tokens[index].Kind == TokenKind.LessThan)
        {
            var genericDepth = 0;

            while (index < tokens.Count)
            {
                if (tokens[index].Kind == TokenKind.LessThan)
                {
                    genericDepth++;
                }
                else if (tokens[index].Kind == TokenKind.GreaterThan)
                {
                    genericDepth--;

                    if (genericDepth <= 0)
                    {
                        index++;
                        break;
                    }
                }

                index++;
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (index < tokens.Count
            && tokens[index].Kind is not (TokenKind.JsxCloseBracket or TokenKind.JsxSelfClose))
        {
            if (tokens[index].Kind != TokenKind.JsxAttributeName)
            {
                index++;
                continue;
            }

            var nameToken = tokens[index];
            var attributeName = nameToken.Text ?? "";
            index++;
            string? value = null;
            var valueKind = "boolean";

            if (index < tokens.Count
                && tokens[index].Kind == TokenKind.Operator
                && tokens[index].Text == "=")
            {
                index++;

                if (index < tokens.Count && tokens[index].Kind == TokenKind.String)
                {
                    value = Unquote(tokens[index].Text ?? "");
                    valueKind = "literal";
                    index++;
                }
                else if (index < tokens.Count && tokens[index].Kind == TokenKind.OpenBrace)
                {
                    var expression = ReadExpression(source, tokens, ref index, "attribute", file, diagnostics);
                    value = expression?.Text;
                    valueKind = "expression";

                    if (expression is not null)
                    {
                        expressions.Add(expression);
                    }
                }
                else if (index < tokens.Count)
                {
                    value = tokens[index].Text ?? "";
                    index++;
                }
            }

            if (!seen.Add(attributeName))
            {
                diagnostics.Add(LlmDiagnostics.Error(
                    "CSX1007",
                    $"Duplicate attribute '{attributeName}' on '<{Describe(name, kind)}>'.",
                    LineMap.Locate(file, source, nameToken.StartPosition)));
            }

            var location = LineMap.Locate(file, source, nameToken.StartPosition);
            var attribute = new CsxAttributeInfo
            {
                Name = attributeName,
                Value = value,
                ValueKind = valueKind,
                Line = location.Line,
                Column = location.Column
            };
            elementAttributes.Add(attribute);
            attributes.Add(attribute);
        }

        var selfClosing = index < tokens.Count && tokens[index].Kind == TokenKind.JsxSelfClose;

        if (index < tokens.Count)
        {
            index++;
        }

        var info = new CsxElementInfo
        {
            Name = name,
            Kind = kind,
            Attributes = elementAttributes,
            Line = LineMap.Locate(file, source, start).Line,
            Column = LineMap.Locate(file, source, start).Column,
            SelfClosing = selfClosing,
            Depth = depth
        };

        return new ParsedElement(info);
    }

    private static CsxExpressionInfo? ReadExpression(
        string source,
        List<Token> tokens,
        ref int index,
        string context,
        string? file,
        List<LlmDiagnostic>? diagnostics)
    {
        var start = tokens[index].StartPosition;
        var depth = 0;

        for (; index < tokens.Count; index++)
        {
            if (tokens[index].Kind == TokenKind.OpenBrace)
            {
                depth++;
            }
            else if (tokens[index].Kind == TokenKind.CloseBrace)
            {
                depth--;

                if (depth == 0)
                {
                    var end = tokens[index].StartPosition;
                    index++;
                    var location = LineMap.Locate(file ?? "", source, start);
                    return new CsxExpressionInfo
                    {
                        Text = source[(start + 1)..Math.Max(start + 1, end)].Trim(),
                        Line = location.Line,
                        Column = location.Column,
                        Context = context
                    };
                }
            }
        }

        diagnostics?.Add(LlmDiagnostics.Error(
            "CSX0004",
            "Unclosed brace expression.",
            LineMap.Locate(file ?? "", source, start)));

        return null;
    }

    private static void CloseElement(
        string name,
        string kind,
        int offset,
        Stack<OpenElement> stack,
        List<LlmDiagnostic> diagnostics,
        string source,
        string file)
    {
        if (stack.Count == 0)
        {
            diagnostics.Add(LlmDiagnostics.Error(
                "CSX0003",
                $"Closing tag '</{Describe(name, kind)}>' has no matching open element.",
                LineMap.Locate(file, source, offset)));
            return;
        }

        var open = stack.Peek();

        if (SameElement(open.Element, name, kind))
        {
            stack.Pop();
            return;
        }

        diagnostics.Add(LlmDiagnostics.Error(
            "CSX0005",
            $"Closing tag '</{Describe(name, kind)}>' does not match open '<{Describe(open.Element)}>'.",
            LineMap.Locate(file, source, offset),
            [$"Close '<{Describe(open.Element)}>' before closing '<{Describe(name, kind)}>'."]));

        // Recover by closing the nearest matching element, if one exists.
        var found = false;

        foreach (var candidate in stack)
        {
            if (SameElement(candidate.Element, name, kind))
            {
                found = true;
                break;
            }
        }

        if (found)
        {
            while (stack.Count > 0 && !SameElement(stack.Peek().Element, name, kind))
            {
                stack.Pop();
            }

            if (stack.Count > 0)
            {
                stack.Pop();
            }
        }
    }

    private static bool SameElement(CsxElementInfo element, string name, string kind)
    {
        if (element.Kind == "fragment" || kind == "fragment")
        {
            return element.Kind == "fragment" && kind == "fragment";
        }

        return element.Kind == kind
            && element.Name.Equals(name, StringComparison.Ordinal);
    }

    private static string Describe(CsxElementInfo element) =>
        element.Kind == "fragment" ? "" : element.Name;

    private static string Describe(string name, string kind) => kind == "fragment" ? "" : name;

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return System.Text.RegularExpressions.Regex.Unescape(value[1..^1]);
        }

        return value;
    }

    private static HashSet<string> DeclaredTypes(string source)
    {
        return Regex.Matches(SourceScanner.Mask(source), @"\bclass\s+(?<name>[A-Za-z_]\w*)")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private sealed record OpenElement(CsxElementInfo Element, int Offset);

    private sealed record ParsedElement(CsxElementInfo Info);
}
