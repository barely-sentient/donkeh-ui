namespace Donkeh.Llm;

/// <summary>A source position. Line and column are one-based; editor clients expect that.</summary>
public sealed class LlmLocation
{
    /// <summary>File path as supplied by the caller, or a project-relative path.</summary>
    public required string File { get; init; }

    /// <summary>One-based line number, or 0 when the diagnostic is not tied to a line.</summary>
    public int Line { get; init; }

    /// <summary>One-based column number, or 0 when the diagnostic is not tied to a column.</summary>
    public int Column { get; init; }

    /// <summary>Zero-based character offset, when known.</summary>
    public int? Offset { get; init; }
}

/// <summary>A stable, machine-readable diagnostic.</summary>
public sealed class LlmDiagnostic
{
    /// <summary>Stable diagnostic code, for example <c>CSX1004</c>.</summary>
    public required string Code { get; init; }

    /// <summary>error, warning or info.</summary>
    public required string Severity { get; init; }

    /// <summary>Short explanation of the problem.</summary>
    public required string Message { get; init; }

    /// <summary>Source position, when the diagnostic has one.</summary>
    public LlmLocation? Location { get; init; }

    /// <summary>Concrete corrections or next steps.</summary>
    public IReadOnlyList<string>? Suggestions { get; init; }
}

/// <summary>Builds diagnostics with stable codes and consistent severity spelling.</summary>
public static class LlmDiagnostics
{
    /// <summary>Creates an error diagnostic.</summary>
    public static LlmDiagnostic Error(string code, string message, LlmLocation? location = null,
        IReadOnlyList<string>? suggestions = null) =>
        new() { Code = code, Severity = "error", Message = message, Location = location, Suggestions = suggestions };

    /// <summary>Creates a warning diagnostic.</summary>
    public static LlmDiagnostic Warning(string code, string message, LlmLocation? location = null,
        IReadOnlyList<string>? suggestions = null) =>
        new() { Code = code, Severity = "warning", Message = message, Location = location, Suggestions = suggestions };

    /// <summary>Creates an informational diagnostic.</summary>
    public static LlmDiagnostic Info(string code, string message, LlmLocation? location = null,
        IReadOnlyList<string>? suggestions = null) =>
        new() { Code = code, Severity = "info", Message = message, Location = location, Suggestions = suggestions };
}

/// <summary>Maps a character offset in a text file to a one-based line and column.</summary>
public static class LineMap
{
    /// <summary>Creates a location from an offset.</summary>
    public static LlmLocation Locate(string file, string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var line = 1;
        var column = 1;
        var lineStart = 0;

        for (var i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
                lineStart = i + 1;
            }
            else if (text[i] != '\r')
            {
                column++;
            }
        }

        return new LlmLocation { File = file, Line = line, Column = column, Offset = offset };
    }

    /// <summary>Builds an offset lookup for repeated diagnostics.</summary>
    public static int OffsetOf(string text, int line, int column)
    {
        var currentLine = 1;
        var index = 0;

        while (index < text.Length && currentLine < line)
        {
            if (text[index++] == '\n')
            {
                currentLine++;
            }
        }

        return Math.Clamp(index + Math.Max(0, column - 1), 0, text.Length);
    }
}
