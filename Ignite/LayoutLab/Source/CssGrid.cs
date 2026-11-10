namespace Ignite.LayoutLab;

public enum GridTrackKind : byte
{
    Fixed,
    Percent,
    Fr,
    Auto,
    MinContent,
    MaxContent,
    FitContent
}

/// <summary>
/// One grid track. <see cref="Minimum"/> and <see cref="Maximum"/> describe the
/// sizing function; <see cref="Fr"/> is set when the maximum is a flexible
/// length. Auto tracks contribute their item's intrinsic size.
/// </summary>
public readonly record struct GridTrack(
    GridTrackKind Kind,
    CssLength Minimum,
    CssLength Maximum,
    float Fr,
    float FitContentLimit)
{
    public bool IsFlexible => Kind == GridTrackKind.Fr;

    public static GridTrack Auto => new(GridTrackKind.Auto, CssLength.Auto, CssLength.Auto, 0, 0);

    public static GridTrack Fixed(CssLength length)
        => new(GridTrackKind.Fixed, length, length, 0, 0);

    public static GridTrack Fraction(float fr)
        => new(GridTrackKind.Fr, CssLength.Zero, CssLength.Fr(fr), fr, 0);
}

/// <summary>
/// A named area from <c>grid-template-areas</c>. Rows and columns are zero-based
/// track indices with exclusive ends.
/// </summary>
public readonly record struct GridAreaRect(int RowStart, int RowEnd, int ColumnStart, int ColumnEnd)
{
    public int RowSpan => Math.Max(1, RowEnd - RowStart);
    public int ColumnSpan => Math.Max(1, ColumnEnd - ColumnStart);
}

/// <summary>A parsed set of named grid areas.</summary>
public sealed class GridTemplateAreas
{
    public GridTemplateAreas(
        Dictionary<string, GridAreaRect> areas,
        int rowCount,
        int columnCount)
    {
        Areas = areas;
        RowCount = rowCount;
        ColumnCount = columnCount;
    }

    public Dictionary<string, GridAreaRect> Areas { get; }
    public int RowCount { get; }
    public int ColumnCount { get; }

    /// <summary>Swaps rows and columns, for <c>grid-auto-flow: column</c>.</summary>
    public GridTemplateAreas Transpose()
    {
        var transposed = new Dictionary<string, GridAreaRect>(Areas.Count, StringComparer.Ordinal);

        foreach (var pair in Areas)
        {
            var area = pair.Value;
            transposed[pair.Key] = new GridAreaRect(
                area.ColumnStart,
                area.ColumnEnd,
                area.RowStart,
                area.RowEnd);
        }

        return new GridTemplateAreas(transposed, ColumnCount, RowCount);
    }

    public static GridTemplateAreas? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var rows = new List<string[]>();
        var i = 0;

        while (i < value.Length)
        {
            var c = value[i];

            if (c is '"' or '\'')
            {
                var quote = c;
                var start = ++i;

                while (i < value.Length && value[i] != quote)
                {
                    i++;
                }

                var row = value[start..Math.Min(i, value.Length)];
                rows.Add(row.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                i++;
                continue;
            }

            i++;
        }

        if (rows.Count == 0)
        {
            return null;
        }

        var columnCount = 0;

        for (var r = 0; r < rows.Count; r++)
        {
            columnCount = Math.Max(columnCount, rows[r].Length);
        }

        var bounds = new Dictionary<string, (int MinRow, int MaxRow, int MinColumn, int MaxColumn)>(
            StringComparer.Ordinal);

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];

            for (var c = 0; c < row.Length; c++)
            {
                var name = row[c];

                if (name is "." or "none" || name.Length == 0)
                {
                    continue;
                }

                if (bounds.TryGetValue(name, out var existing))
                {
                    bounds[name] = (
                        Math.Min(existing.MinRow, r),
                        Math.Max(existing.MaxRow, r),
                        Math.Min(existing.MinColumn, c),
                        Math.Max(existing.MaxColumn, c));
                }
                else
                {
                    bounds[name] = (r, r, c, c);
                }
            }
        }

        var areas = new Dictionary<string, GridAreaRect>(bounds.Count, StringComparer.Ordinal);

        foreach (var pair in bounds)
        {
            var b = pair.Value;
            areas[pair.Key] = new GridAreaRect(
                b.MinRow,
                b.MaxRow + 1,
                b.MinColumn,
                b.MaxColumn + 1);
        }

        return new GridTemplateAreas(areas, rows.Count, Math.Max(1, columnCount));
    }
}

/// <summary>A parsed <c>grid-template-*</c> or <c>grid-auto-*</c> track list.</summary>
public sealed class GridTrackList
{
    public static readonly GridTrackList Empty = new([]);

    public GridTrackList(GridTrack[] tracks)
        : this(tracks, [], -1, [], [], false)
    {
    }

    internal GridTrackList(
        GridTrack[] tracks,
        string[][] lineNames,
        int autoRepeatIndex,
        GridTrack[] autoRepeatTracks,
        string[][] autoRepeatLineNames,
        bool autoRepeatFit)
    {
        Tracks = tracks;
        LineNames = lineNames;
        AutoRepeatIndex = autoRepeatIndex;
        AutoRepeatTracks = autoRepeatTracks;
        AutoRepeatLineNames = autoRepeatLineNames;
        AutoRepeatFit = autoRepeatFit;
    }

    public GridTrack[] Tracks { get; }

    /// <summary>
    /// Names for each grid line. Index <c>i</c> is the line before track
    /// <c>i</c>; the final entry is the line after the last track. Always has
    /// <c>Count + 1</c> entries.
    /// </summary>
    public string[][] LineNames { get; }

    /// <summary>Index of the placeholder track that an auto-repeat expands, or -1.</summary>
    public int AutoRepeatIndex { get; }

    /// <summary>The track pattern repeated by <c>repeat(auto-fill|auto-fit, ...)</c>.</summary>
    public GridTrack[] AutoRepeatTracks { get; }

    /// <summary>Line names inside the auto-repeat pattern.</summary>
    public string[][] AutoRepeatLineNames { get; }

    public bool AutoRepeatFit { get; }

    public int Count => Tracks.Length;

    public static GridTrackList Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Empty;
        }

        var text = value.Trim();

        if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return Empty;
        }

        if (text.Equals("subgrid", StringComparison.OrdinalIgnoreCase))
        {
            return Empty;
        }

        var parts = CssValueParser.SplitTopLevel(text, ' ');
        var tracks = new List<GridTrack>(Math.Max(1, parts.Count));
        var lineNames = new List<List<string>> { new() };
        var autoRepeatIndex = -1;
        var autoRepeatTracks = Array.Empty<GridTrack>();
        var autoRepeatLineNames = Array.Empty<string[]>();
        var autoRepeatFit = false;

        foreach (var part in parts)
        {
            Append(part, tracks, lineNames, ref autoRepeatIndex, ref autoRepeatTracks,
                ref autoRepeatLineNames, ref autoRepeatFit);
        }

        return tracks.Count == 0
            ? Empty
            : new GridTrackList(
                tracks.ToArray(),
                ToArrays(lineNames),
                autoRepeatIndex,
                autoRepeatTracks,
                autoRepeatLineNames,
                autoRepeatFit);
    }

    private static string[][] ToArrays(List<List<string>> names)
    {
        var result = new string[names.Count][];

        for (var i = 0; i < names.Count; i++)
        {
            result[i] = names[i].ToArray();
        }

        return result;
    }

    private static void Append(
        string part,
        List<GridTrack> tracks,
        List<List<string>> lineNames,
        ref int autoRepeatIndex,
        ref GridTrack[] autoRepeatTracks,
        ref string[][] autoRepeatLineNames,
        ref bool autoRepeatFit)
    {
        var text = part.Trim();

        if (text.Length == 0)
        {
            return;
        }

        if (text[0] == '[')
        {
            AppendLineNames(text, lineNames[^1]);
            return;
        }

        if (TryRepeat(text, tracks, lineNames, ref autoRepeatIndex, ref autoRepeatTracks,
                ref autoRepeatLineNames, ref autoRepeatFit))
        {
            return;
        }

        var track = ParseTrack(text);

        if (track is { } parsed)
        {
            tracks.Add(parsed);
            lineNames.Add([]);
        }
    }

    private static void AppendLineNames(string text, List<string> target)
    {
        var end = text.IndexOf(']');
        var inner = end < 0 ? text[1..] : text[1..end];

        foreach (var name in inner.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            target.Add(name);
        }
    }

    private static bool TryRepeat(
        string text,
        List<GridTrack> tracks,
        List<List<string>> lineNames,
        ref int autoRepeatIndex,
        ref GridTrack[] autoRepeatTracks,
        ref string[][] autoRepeatLineNames,
        ref bool autoRepeatFit)
    {
        if (!text.StartsWith("repeat(", StringComparison.OrdinalIgnoreCase) || !text.EndsWith(')'))
        {
            return false;
        }

        var inner = text[7..^1];
        var comma = FindTopLevelComma(inner);

        if (comma < 0)
        {
            return false;
        }

        var countText = inner[..comma].Trim();
        var tracksText = inner[(comma + 1)..].Trim();
        var autoFill = countText.Equals("auto-fill", StringComparison.OrdinalIgnoreCase);
        var autoFit = countText.Equals("auto-fit", StringComparison.OrdinalIgnoreCase);

        if (autoFill || autoFit)
        {
            // Only one auto-repeat is allowed per track list. The layout pass
            // expands the placeholder to the number of tracks that fit.
            if (autoRepeatIndex >= 0)
            {
                return false;
            }

            var pattern = new List<GridTrack>(2);
            var patternNames = new List<List<string>> { new() };

            foreach (var innerPart in CssValueParser.SplitTopLevel(tracksText, ' '))
            {
                var trimmed = innerPart.Trim();

                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed[0] == '[')
                {
                    AppendLineNames(trimmed, patternNames[^1]);
                    continue;
                }

                var track = ParseTrack(trimmed);

                if (track is { } parsed)
                {
                    pattern.Add(parsed);
                    patternNames.Add([]);
                }
            }

            if (pattern.Count == 0)
            {
                pattern.Add(GridTrack.Auto);
                patternNames.Add([]);
            }

            autoRepeatIndex = tracks.Count;
            autoRepeatTracks = pattern.ToArray();
            autoRepeatLineNames = ToArrays(patternNames);
            autoRepeatFit = autoFit;
            tracks.Add(GridTrack.Auto);
            lineNames.Add([]);
            return true;
        }

        if (!int.TryParse(countText, out var count) || count <= 0 || count > 1024)
        {
            return false;
        }

        var innerParts = CssValueParser.SplitTopLevel(tracksText, ' ');

        for (var i = 0; i < count; i++)
        {
            foreach (var innerPart in innerParts)
            {
                var trimmed = innerPart.Trim();

                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed[0] == '[')
                {
                    AppendLineNames(trimmed, lineNames[^1]);
                    continue;
                }

                var track = ParseTrack(trimmed);

                if (track is { } parsed)
                {
                    tracks.Add(parsed);
                    lineNames.Add([]);
                }
            }
        }

        return true;
    }

    private static GridTrack? ParseTrack(string text)
    {
        if (text.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return GridTrack.Auto;
        }

        if (text.Equals("min-content", StringComparison.OrdinalIgnoreCase))
        {
            return new GridTrack(GridTrackKind.MinContent, CssLength.MinContent, CssLength.MinContent, 0, 0);
        }

        if (text.Equals("max-content", StringComparison.OrdinalIgnoreCase))
        {
            return new GridTrack(GridTrackKind.MaxContent, CssLength.MaxContent, CssLength.MaxContent, 0, 0);
        }

        if (text.StartsWith("minmax(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
        {
            var inner = text[7..^1];
            var comma = FindTopLevelComma(inner);

            if (comma < 0)
            {
                return GridTrack.Auto;
            }

            var minimum = CssValueParser.ParseLength(inner[..comma].Trim());
            var maximum = CssValueParser.ParseLength(inner[(comma + 1)..].Trim());

            if (maximum.IsFr)
            {
                return new GridTrack(GridTrackKind.Fr, minimum, maximum, maximum.Value, 0);
            }

            return new GridTrack(
                maximum.IsPercent ? GridTrackKind.Percent : GridTrackKind.Fixed,
                minimum,
                maximum,
                0,
                0);
        }

        if (text.StartsWith("fit-content(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
        {
            var limit = CssValueParser.ParseLength(text[12..^1].Trim());
            return new GridTrack(GridTrackKind.FitContent, CssLength.Auto, limit, 0, limit.Value);
        }

        var length = CssValueParser.ParseLength(text);

        if (length.IsFr)
        {
            return GridTrack.Fraction(length.Value);
        }

        if (length.IsPercent)
        {
            return new GridTrack(GridTrackKind.Percent, length, length, 0, 0);
        }

        if (length.IsAuto || length.IsIntrinsic)
        {
            return GridTrack.Auto;
        }

        return GridTrack.Fixed(length);
    }

    private static int FindTopLevelComma(string text)
    {
        var depth = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth--;
            else if (text[i] == ',' && depth == 0) return i;
        }

        return -1;
    }
}

public static class GridPlacementParser
{
    private readonly record struct LineSpec(int Line, bool IsSpan, string? Name);

    public static GridPlacement Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GridPlacement.Auto;
        }

        var text = value.Trim();

        if (text.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return GridPlacement.Auto;
        }

        var slash = text.IndexOf('/');

        if (slash < 0)
        {
            var single = ParseLine(text);

            if (single.IsSpan)
            {
                // `span N` alone leaves the opposite side auto and anchors the
                // span to the start side.
                return new GridPlacement(
                    0,
                    0,
                    true,
                    true,
                    Math.Max(1, single.Line),
                    null,
                    single.Name,
                    false);
            }

            if (single.Name is not null)
            {
                return new GridPlacement(0, 0, false, true, 0, single.Name, null);
            }

            if (single.Line == 0)
            {
                return GridPlacement.Auto;
            }

            return new GridPlacement(single.Line, 0, false, true);
        }

        var startText = text[..slash].Trim();
        var endText = text[(slash + 1)..].Trim();

        var start = ParseLine(startText);
        var end = ParseLine(endText);

        if (start.IsSpan)
        {
            var span = Math.Max(1, start.Line);
            var endAuto = end.Line == 0 && end.Name is null;

            // The span belongs to the start side and runs forward, so the end
            // line can keep driving the size (and stay overridable) instead of
            // being baked into a definite start line.
            return new GridPlacement(
                0,
                endAuto ? 0 : end.Line,
                true,
                endAuto,
                span,
                start.Name,
                end.Name,
                false);
        }

        if (end.IsSpan)
        {
            var span = Math.Max(1, end.Line);
            var startAuto = start.Line == 0 && start.Name is null;

            // The span belongs to the end side and runs backwards.
            return new GridPlacement(
                start.Line,
                0,
                startAuto,
                true,
                span,
                start.Name,
                end.Name,
                true);
        }

        return new GridPlacement(
            start.Line,
            end.Line,
            start.Line == 0 && start.Name is null,
            end.Line == 0 && end.Name is null,
            0,
            start.Name,
            end.Name);
    }

    /// <summary>Parses one <c>grid-line</c> value into a line, span or name.</summary>
    public static bool TryParseLine(string value, out int line, out bool isSpan, out string? name)
    {
        var spec = ParseLine(value);
        line = spec.Line;
        isSpan = spec.IsSpan;
        name = spec.Name;
        return line != 0 || isSpan || name is not null;
    }

    private static LineSpec ParseLine(string text)
    {
        text = text.Trim();

        if (text.Length == 0 || text.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return new LineSpec(0, false, null);
        }

        var isSpan = false;

        if (text.StartsWith("span ", StringComparison.OrdinalIgnoreCase))
        {
            isSpan = true;
            text = text[5..].Trim();
        }

        if (int.TryParse(text, out var line))
        {
            return new LineSpec(line, isSpan, null);
        }

        // A non-numeric grid line is a named line/area. The layout pass resolves
        // it against the container's line name map.
        return new LineSpec(isSpan ? 1 : 0, isSpan, text);
    }
}
