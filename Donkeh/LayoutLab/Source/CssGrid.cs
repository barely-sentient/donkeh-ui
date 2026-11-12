namespace Donkeh.LayoutLab;

/// <summary>Classifies a grid track's sizing function.</summary>
public enum GridTrackKind : byte
{
    /// <summary>A fixed length: the track's minimum and maximum are equal.</summary>
    Fixed,
    /// <summary>A percentage of the grid container's content-box size.</summary>
    Percent,
    /// <summary>A flexible <c>fr</c> track; free space is distributed by the fraction.</summary>
    Fr,
    /// <summary>An <c>auto</c> track, sized from its items' contributions and the free space.</summary>
    Auto,
    /// <summary>A <c>min-content</c> track.</summary>
    MinContent,
    /// <summary>A <c>max-content</c> track.</summary>
    MaxContent,
    /// <summary>
    /// A <c>fit-content(limit)</c> track, clamped by
    /// <see cref="GridTrack.FitContentLimit"/>.
    /// </summary>
    FitContent
}

/// <summary>
/// One grid track. <see cref="Minimum"/> and <see cref="Maximum"/> describe the
/// sizing function; <see cref="Fr"/> is set when the maximum is a flexible
/// length. Auto tracks contribute their item's intrinsic size.
/// </summary>
/// <param name="Kind">The track's sizing function.</param>
/// <param name="Minimum">Lower sizing function of the track.</param>
/// <param name="Maximum">Upper sizing function of the track.</param>
/// <param name="Fr">
/// Flexible fraction when <paramref name="Kind"/> is
/// <see cref="GridTrackKind.Fr"/>; otherwise zero.
/// </param>
/// <param name="FitContentLimit">
/// Limit in CSS pixels when the track came from <c>fit-content()</c>; otherwise zero.
/// </param>
/// <remarks>
/// <para>
/// This is a flattened representation of a CSS <c>&lt;track-size&gt;</c>:
/// <c>minmax(a, b)</c> keeps both bounds, while a single length sets both
/// bounds to the same value. For fixed-length tracks the length is still stored
/// unresolved in <see cref="Minimum"/> and <see cref="Maximum"/> so font- and
/// viewport-relative units resolve against the final grid context.
/// </para>
/// <para>
/// The fraction value is duplicated in <see cref="Fr"/> for fast access during
/// free-space distribution; it must stay in sync with <see cref="Maximum"/>.
/// </para>
/// </remarks>
public readonly record struct GridTrack(
    GridTrackKind Kind,
    CssLength Minimum,
    CssLength Maximum,
    float Fr,
    float FitContentLimit)
{
    /// <summary>Gets a value indicating whether the track distributes free space as a fraction.</summary>
    public bool IsFlexible => Kind == GridTrackKind.Fr;

    /// <summary>An <c>auto</c> track that takes its size from its items.</summary>
    public static GridTrack Auto => new(GridTrackKind.Auto, CssLength.Auto, CssLength.Auto, 0, 0);

    /// <summary>Creates a fixed-length track.</summary>
    /// <param name="length">Length used as both the minimum and maximum.</param>
    /// <returns>A <see cref="GridTrackKind.Fixed"/> track.</returns>
    public static GridTrack Fixed(CssLength length)
        => new(GridTrackKind.Fixed, length, length, 0, 0);

    /// <summary>Creates a flexible track from a fraction.</summary>
    /// <param name="fr">Fraction of the free space, where <c>1</c> means <c>1fr</c>.</param>
    /// <returns>A <see cref="GridTrackKind.Fr"/> track with a zero minimum.</returns>
    public static GridTrack Fraction(float fr)
        => new(GridTrackKind.Fr, CssLength.Zero, CssLength.Fr(fr), fr, 0);
}

/// <summary>
/// A named area from <c>grid-template-areas</c>. Rows and columns are zero-based
/// track indices with exclusive ends.
/// </summary>
/// <param name="RowStart">First row index, inclusive.</param>
/// <param name="RowEnd">Row index one past the last row, exclusive.</param>
/// <param name="ColumnStart">First column index, inclusive.</param>
/// <param name="ColumnEnd">Column index one past the last column, exclusive.</param>
/// <remarks>
/// A named area is rectangular, so the parser derives these bounds from the
/// minimum and maximum row and column where the name appears. Use
/// <see cref="RowSpan"/> and <see cref="ColumnSpan"/> for the track counts.
/// </remarks>
public readonly record struct GridAreaRect(int RowStart, int RowEnd, int ColumnStart, int ColumnEnd)
{
    /// <summary>Gets the number of rows covered, at least one.</summary>
    public int RowSpan => Math.Max(1, RowEnd - RowStart);

    /// <summary>Gets the number of columns covered, at least one.</summary>
    public int ColumnSpan => Math.Max(1, ColumnEnd - ColumnStart);
}

/// <summary>A parsed set of named grid areas.</summary>
/// <remarks>
/// Produced by <see cref="Parse(string?)"/> from the quoted row strings of
/// <c>grid-template-areas</c>. The dictionaries use ordinal comparison, so area
/// names are case sensitive as required by CSS custom identifiers. The
/// <see cref="Areas"/> dictionary is mutable and is exposed by reference;
/// callers should treat it as read-only after construction.
/// </remarks>
public sealed class GridTemplateAreas
{
    /// <summary>Initialises a named-area set from pre-computed bounds.</summary>
    /// <param name="areas">Map from area name to its rectangular bounds.</param>
    /// <param name="rowCount">Number of rows declared by the template.</param>
    /// <param name="columnCount">Number of columns declared by the template.</param>
    public GridTemplateAreas(
        Dictionary<string, GridAreaRect> areas,
        int rowCount,
        int columnCount)
    {
        Areas = areas;
        RowCount = rowCount;
        ColumnCount = columnCount;
    }

    /// <summary>Gets the area bounds keyed by area name.</summary>
    public Dictionary<string, GridAreaRect> Areas { get; }

    /// <summary>Gets the number of rows in the template.</summary>
    public int RowCount { get; }

    /// <summary>Gets the number of columns in the template.</summary>
    public int ColumnCount { get; }

    /// <summary>Swaps rows and columns, for <c>grid-auto-flow: column</c>.</summary>
    /// <returns>A new set with row and column bounds exchanged and counts swapped.</returns>
    /// <remarks>Area names are preserved; the <see cref="Areas"/> dictionary is newly allocated.</remarks>
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

    /// <summary>Parses the value of <c>grid-template-areas</c>.</summary>
    /// <param name="value">Quoted row strings, for example <c>"a a" "b c"</c>.</param>
    /// <returns>
    /// A parsed template, or <see langword="null"/> when the value is empty or
    /// contains no quoted rows.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Each quoted string is one row and is split on whitespace. A cell made
    /// entirely of dots (for example <c>.</c> or <c>...</c>) is a null cell and
    /// does not create an area. The keyword <c>none</c> is only special as the
    /// whole-property value; inside a quoted row it is an area name. When a name
    /// occurs in several cells its bounding box is the union of those cells.
    /// </para>
    /// <para>
    /// Rows may be ragged: the column count is the widest row and missing
    /// trailing cells are simply absent. Unlike CSS, the parser does not
    /// require an area's cells to form a rectangle; it takes the bounding box
    /// instead. Duplicate names therefore silently widen the area rather than
    /// failing.
    /// </para>
    /// </remarks>
    public static GridTemplateAreas? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
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

                // A run of one or more dots is a null cell. The keyword none is
                // only special as the whole-property value; inside a quoted row
                // it is an ordinary area name.
                if (name.Length == 0 || IsNullCell(name))
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

    /// <summary>Returns whether a cell token is made entirely of dots.</summary>
    private static bool IsNullCell(string name)
    {
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] != '.')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>A parsed <c>grid-template-*</c> or <c>grid-auto-*</c> track list.</summary>
/// <remarks>
/// <para>
/// The list stores already-expanded tracks plus the metadata needed to expand
/// <c>repeat(auto-fill, ...)</c> and <c>repeat(auto-fit, ...)</c> during
/// layout, when the available size is known. There are no mutators, but the
/// exposed arrays are shared by reference and should be treated as read-only.
/// </para>
/// <para>
/// Parsed lists expose <see cref="LineNames"/> with exactly
/// <see cref="Count"/> + 1 entries; index <c>i</c> names the line before track
/// <c>i</c> and the final entry names the line after the last track. The
/// public constructor synthesises an empty name list for each line.
/// </para>
/// </remarks>
public sealed class GridTrackList
{
    /// <summary>An empty track list, used for <c>none</c> and unsupported values.</summary>
    public static readonly GridTrackList Empty = new([]);

    /// <summary>Initialises a track list with no auto-repeat placeholder.</summary>
    /// <param name="tracks">Expanded track sizes.</param>
    /// <remarks>
    /// No line names are provided, so <see cref="LineNames"/> contains
    /// <c>tracks.Length + 1</c> empty slots. Use <see cref="Parse(string?)"/>
    /// for a list with named lines.
    /// </remarks>
    public GridTrackList(GridTrack[] tracks)
        : this(tracks, CreateEmptyLineNames(tracks.Length), -1, [], [], false)
    {
    }

    /// <summary>Creates the <c>Count + 1</c> empty name slots required by the invariant.</summary>
    private static string[][] CreateEmptyLineNames(int trackCount)
    {
        var names = new string[trackCount + 1][];

        for (var i = 0; i < names.Length; i++)
        {
            names[i] = [];
        }

        return names;
    }

    /// <summary>Initialises a track list including auto-repeat metadata.</summary>
    /// <param name="tracks">
    /// Expanded tracks; the auto-repeat slot contains a placeholder
    /// <see cref="GridTrack.Auto"/>.
    /// </param>
    /// <param name="lineNames">Names for each line, with <c>tracks.Length + 1</c> entries.</param>
    /// <param name="autoRepeatIndex">
    /// Index of the placeholder track, or -1 when there is no auto-repeat.
    /// </param>
    /// <param name="autoRepeatTracks">Track pattern that the placeholder expands to.</param>
    /// <param name="autoRepeatLineNames">Line names inside the repeated pattern.</param>
    /// <param name="autoRepeatFit">
    /// <see langword="true"/> for <c>auto-fit</c>, <see langword="false"/> for
    /// <c>auto-fill</c>.
    /// </param>
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

    /// <summary>Gets the expanded track sizes in order.</summary>
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

    /// <summary>
    /// Gets a value indicating whether the auto-repeat uses <c>auto-fit</c>
    /// rather than <c>auto-fill</c>.
    /// </summary>
    /// <remarks>
    /// With <c>auto-fit</c>, tracks that receive no items collapse to zero and
    /// their space is absorbed by the flexible tracks; with <c>auto-fill</c>
    /// the empty tracks keep their size.
    /// </remarks>
    public bool AutoRepeatFit { get; }

    /// <summary>Gets the number of explicit tracks, excluding auto-repeat expansion.</summary>
    public int Count => Tracks.Length;

    /// <summary>
    /// Parses a <c>grid-template-columns</c>, <c>grid-template-rows</c> or
    /// <c>grid-auto-*</c> value.
    /// </summary>
    /// <param name="value">Track list text such as <c>100px 1fr [main] auto</c>.</param>
    /// <returns>
    /// The parsed list, or <see cref="Empty"/> when the value is empty,
    /// <c>none</c> or <c>subgrid</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Supports fixed lengths, percentages, <c>fr</c>, <c>auto</c>,
    /// <c>min-content</c>, <c>max-content</c>, <c>minmax()</c>,
    /// <c>fit-content()</c>, bracketed line names, and both counted and
    /// automatic <c>repeat()</c>. Splitting is done at the top level only, so
    /// commas and spaces inside a function do not split the list.
    /// </para>
    /// <para>
    /// A counted <c>repeat()</c> is expanded inline; a count outside 1–1024 is
    /// ignored. Only the first <c>auto-fill</c>/<c>auto-fit</c> repeat is
    /// honoured, because CSS permits at most one; later or malformed repeats are
    /// invalid and contribute no track. <c>subgrid</c> is accepted as an empty
    /// list rather than implemented.
    /// </para>
    /// </remarks>
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

    /// <summary>Converts the per-line name lists into jagged arrays.</summary>
    /// <param name="names">Accumulated names, one list per grid line.</param>
    /// <returns>A jagged array with one entry per line.</returns>
    private static string[][] ToArrays(List<List<string>> names)
    {
        var result = new string[names.Count][];

        for (var i = 0; i < names.Count; i++)
        {
            result[i] = names[i].ToArray();
        }

        return result;
    }

    /// <summary>Appends one top-level token: a line-name block, a repeat, or a track.</summary>
    /// <param name="part">Token text.</param>
    /// <param name="tracks">Accumulated track list.</param>
    /// <param name="lineNames">Accumulated line names.</param>
    /// <param name="autoRepeatIndex">Auto-repeat placeholder index, updated in place.</param>
    /// <param name="autoRepeatTracks">Auto-repeat pattern, updated in place.</param>
    /// <param name="autoRepeatLineNames">Auto-repeat line names, updated in place.</param>
    /// <param name="autoRepeatFit">Auto-fit flag, updated in place.</param>
    /// <remarks>
    /// A bracketed token always attaches to the current (last) line, which is
    /// why a line name may appear before or after the track it names in the
    /// source while sharing the same slot.
    /// </remarks>
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

    /// <summary>Adds the names inside a bracketed token to a line.</summary>
    /// <param name="text">Token beginning with <c>[</c>.</param>
    /// <param name="target">Name list for the line being built.</param>
    /// <remarks>
    /// A missing closing bracket is tolerated: the remainder of the token is
    /// treated as names. Names are split on whitespace; empty names are dropped.
    /// </remarks>
    private static void AppendLineNames(string text, List<string> target)
    {
        var end = text.IndexOf(']');
        var inner = end < 0 ? text[1..] : text[1..end];

        foreach (var name in inner.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            target.Add(name);
        }
    }

    /// <summary>Attempts to parse and expand a <c>repeat()</c> token.</summary>
    /// <param name="text">Token text.</param>
    /// <param name="tracks">Accumulated track list, appended to on success.</param>
    /// <param name="lineNames">Accumulated line names, appended to on success.</param>
    /// <param name="autoRepeatIndex">Set to the placeholder index for an auto-repeat.</param>
    /// <param name="autoRepeatTracks">Set to the repeated pattern for an auto-repeat.</param>
    /// <param name="autoRepeatLineNames">Set to the pattern's line names.</param>
    /// <param name="autoRepeatFit">Set to <see langword="true"/> for <c>auto-fit</c>.</param>
    /// <returns><see langword="true"/> when the token was a recognised repeat.</returns>
    /// <remarks>
    /// <para>
    /// For <c>auto-fill</c>/<c>auto-fit</c>, a single placeholder
    /// <see cref="GridTrack.Auto"/> track is inserted and the real pattern is
    /// stored for layout to expand once the container size is known. A second
    /// auto-repeat in the same list is rejected by returning
    /// <see langword="false"/>.
    /// </para>
    /// <para>
    /// For a counted repeat, the pattern is expanded <c>count</c> times in
    /// place. Counts outside 1–1024, or a missing comma, are rejected. A
    /// comma-less or malformed <c>repeat()</c> therefore falls through to
    /// <see cref="ParseTrack(string)"/>; an unknown function resolves to a zero
    /// expression, producing a zero-valued fixed track.
    /// </para>
    /// </remarks>
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

    /// <summary>Parses a single track-sizing token.</summary>
    /// <param name="text">One token from the track list, without line names.</param>
    /// <returns>The parsed track, or <see langword="null"/> when the token is empty.</returns>
    /// <remarks>
    /// <para>
    /// <c>auto</c>, <c>min-content</c> and <c>max-content</c> map to their
    /// dedicated kinds. <c>minmax(a, b)</c> keeps both bounds and chooses the
    /// kind from the maximum: a flexible maximum makes an
    /// <see cref="GridTrackKind.Fr"/> track, a percentage maximum makes a
    /// <see cref="GridTrackKind.Percent"/> track, and anything else is fixed.
    /// </para>
    /// <para>
    /// <c>fit-content(limit)</c> stores the limit both as the maximum and in
    /// <see cref="GridTrack.FitContentLimit"/>. Unknown or intrinsic single
    /// values collapse to <see cref="GridTrack.Auto"/>, so an unrecognised
    /// function becomes a zero-valued fixed track only if it reached this far
    /// as a literal length.
    /// </para>
    /// </remarks>
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

            // Infer the kind from the maximum's unit so an intrinsic maximum
            // does not decay into a fixed track and collapse to the minimum.
            var kind = maximum.Unit switch
            {
                LengthUnit.Percent => GridTrackKind.Percent,
                LengthUnit.MinContent => GridTrackKind.MinContent,
                LengthUnit.MaxContent => GridTrackKind.MaxContent,
                LengthUnit.Auto => GridTrackKind.Auto,
                _ => GridTrackKind.Fixed
            };

            return new GridTrack(kind, minimum, maximum, 0, 0);
        }

        if (text.StartsWith("fit-content(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
        {
            var limit = CssValueParser.ParseLength(text[12..^1].Trim());
            return new GridTrack(GridTrackKind.FitContent, CssLength.Auto, limit, 0, limit.Value);
        }

        // An unrecognised function (a malformed or second repeat(), for example)
        // is invalid rather than a zero-length fixed track.
        if (text.Contains('(') && text.EndsWith(')'))
        {
            return null;
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

    /// <summary>Finds the first comma that is not nested inside parentheses.</summary>
    /// <param name="text">Function argument text.</param>
    /// <returns>The zero-based index of the separator, or -1 when there is none.</returns>
    /// <remarks>
    /// Only parentheses are tracked, which is sufficient for track syntax;
    /// quoted strings are not expected inside <c>minmax()</c> or <c>repeat()</c>.
    /// </remarks>
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

/// <summary>
/// Parses the <c>grid-row</c>, <c>grid-column</c> and <c>grid-area</c>
/// placement syntaxes into a <see cref="GridPlacement"/>.
/// </summary>
/// <remarks>
/// The parser is intentionally context-free: named lines and named areas are
/// stored by name and resolved later against the container's template, because
/// the same value can mean different lines in different containers. Line
/// numbers are 1-based in CSS and are kept that way here; zero means auto.
/// </remarks>
/// <seealso cref="GridPlacement"/>
public static class GridPlacementParser
{
    /// <summary>An intermediate parse result for one side of a slash-separated placement.</summary>
    /// <param name="Line">A 1-based line number, or 0 for auto.</param>
    /// <param name="IsSpan">Whether the side used the <c>span</c> keyword.</param>
    /// <param name="Name">The named line or area, when the value was not numeric.</param>
    private readonly record struct LineSpec(int Line, bool IsSpan, string? Name);

    /// <summary>Parses a grid placement value.</summary>
    /// <param name="value">
    /// A <c>grid-line</c> such as <c>2</c>, <c>-1</c>, <c>span 2</c>,
    /// <c>header-start</c>, or a pair separated by <c>/</c>.
    /// </param>
    /// <returns>
    /// The resolved placement description; <see cref="GridPlacement.Auto"/>
    /// for empty or <c>auto</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// With a single value, a span anchors to the start side and leaves the end
    /// auto; a name becomes the start name; a bare line number makes the end
    /// auto. With a slash, each side is parsed independently and the span
    /// stays on the side that declared it, so the opposite side remains
    /// overridable by a later longhand.
    /// </para>
    /// <para>
    /// A leading <c>span</c> applies to the side it appears on; <c>span</c>
    /// without a number counts as one track. Negative line numbers count
    /// backwards from the end of the explicit grid, as in CSS.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var placement = GridPlacementParser.Parse("2 / span 3");
    /// // Start == 2, SpanCount == 3, SpanOnEnd == true
    /// </code>
    /// </example>
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
    /// <param name="value">A single grid-line value, without a slash.</param>
    /// <param name="line">Receives the 1-based line number, or 0 for auto.</param>
    /// <param name="isSpan">Receives whether the value used <c>span</c>.</param>
    /// <param name="name">
    /// Receives the named line or area, or <see langword="null"/> for numeric
    /// values.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value denotes anything other than
    /// <c>auto</c> or an empty string.
    /// </returns>
    /// <remarks>
    /// A lone <c>0</c> is reported as unsuccessful because line zero means
    /// auto in CSS; this lets callers distinguish an explicit auto from a
    /// parse failure.
    /// </remarks>
    public static bool TryParseLine(string value, out int line, out bool isSpan, out string? name)
    {
        var spec = ParseLine(value);
        line = spec.Line;
        isSpan = spec.IsSpan;
        name = spec.Name;
        return line != 0 || isSpan || name is not null;
    }

    /// <summary>Parses one side of a placement value.</summary>
    /// <param name="text">Trimmed text for one side.</param>
    /// <returns>The line number, span flag and optional name.</returns>
    /// <remarks>
    /// Accepts an optional leading <c>span</c>, then either an integer or a
    /// custom identifier. A named line with <c>span</c> but no number is
    /// normalised to a span of one track.
    /// </remarks>
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
