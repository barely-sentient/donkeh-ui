using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ignite.Data;
using Ignite.Parser;
using Ignite.Parser.Objects;
using Engine = Ignite.DataEngine.DataEngine;

namespace Ignite.DataEngine.Debugger;

/// <summary>
/// Debug companion for the CSS engine. Loads a local file, stdin or a remote
/// stylesheet, runs the same tokenize → build pipeline as the engine, and prints
/// the resulting selector/rule breakdown. Conditions are deliberately not
/// displayed: they are opaque compiled delegates supplied by the host.
/// </summary>
internal static class Program
{
    private const int DefaultLimit = 50;

    private static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var options = new Options();
        string? target = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "-h":
                case "--help":
                    PrintHelp();
                    return 0;

                case "--all":
                    options.Limit = 0;
                    break;

                case "--json":
                    options.Json = true;
                    break;

                case "--selectors":
                    options.ShowSelectors = true;
                    break;

                case "--rules":
                    options.ShowRules = true;
                    break;

                case "--summary":
                    options.SummaryOnly = true;
                    break;

                case "--occurrences":
                    options.ShowOccurrences = true;
                    break;

                case "--no-color":
                    options.NoColor = true;
                    break;

                case "--limit":
                    if (++i >= args.Length
                        || !int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit)
                        || limit < 0)
                    {
                        Console.Error.WriteLine("error: --limit requires a non-negative integer.");
                        return 1;
                    }

                    options.Limit = limit;
                    break;

                default:
                    if (arg.StartsWith("--limit=", StringComparison.Ordinal))
                    {
                        if (!int.TryParse(
                                arg.AsSpan("--limit=".Length),
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out var inlineLimit)
                            || inlineLimit < 0)
                        {
                            Console.Error.WriteLine("error: --limit requires a non-negative integer.");
                            return 1;
                        }

                        options.Limit = inlineLimit;
                        break;
                    }

                    if (arg.StartsWith('-') && arg != "-")
                    {
                        Console.Error.WriteLine($"error: unknown option '{arg}'.");
                        PrintHelp();
                        return 1;
                    }

                    if (target is not null)
                    {
                        Console.Error.WriteLine("error: only one source can be inspected at a time.");
                        return 1;
                    }

                    target = arg;
                    break;
            }
        }

        if (target is null)
        {
            PrintHelp();
            return 1;
        }

        if (!options.SummaryOnly && !options.ShowOccurrences && !options.ShowSelectors && !options.ShowRules)
        {
            options.ShowSelectors = true;
            options.ShowRules = true;
        }

        var loaded = await LoadAsync(target);
        var source = loaded.Text;

        // Warm up JIT and the process-wide intern tables so the reported time
        // reflects steady-state throughput rather than one-off initialisation.
        var warmupTokens = new List<Token>(Math.Max(16, source.Length / 2 + 16));
        Tokenizer.Parse(source.AsSpan(), warmupTokens);
        _ = new Engine(Condition.AlwaysFalseCompiler).Build(warmupTokens);

        var tokens = new List<Token>(Math.Max(16, source.Length / 2 + 16));

        var tokenizeAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var tokenizeWatch = Stopwatch.StartNew();
        Tokenizer.Parse(source.AsSpan(), tokens);
        tokenizeWatch.Stop();
        var tokenizeAllocated = GC.GetAllocatedBytesForCurrentThread() - tokenizeAllocatedBefore;

        var buildAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var buildWatch = Stopwatch.StartNew();
        var table = new Engine(Condition.AlwaysFalseCompiler).Build(tokens);
        buildWatch.Stop();
        var buildAllocated = GC.GetAllocatedBytesForCurrentThread() - buildAllocatedBefore;
        var gen0 = GC.CollectionCount(0) - gen0Before;
        var gen1 = GC.CollectionCount(1) - gen1Before;
        var gen2 = GC.CollectionCount(2) - gen2Before;

        var allocated = tokenizeAllocated + buildAllocated;
        var elapsed = tokenizeWatch.Elapsed + buildWatch.Elapsed;

        var selectorText = options.ShowSelectors || options.ShowRules || options.Json || options.ShowOccurrences
            ? new string?[table.SelectorCount]
            : null;

        var ruleCounts = options.ShowSelectors || options.Json || options.ShowOccurrences
            ? CountRulesPerSelector(table)
            : null;

        // Occurrence statistics used for the interning/dedup report.
        var propertyCounts = new Dictionary<int, int>();
        var valueCounts = new Dictionary<int, int>();
        var conditionCounts = new Dictionary<int, int>();
        var declarationBlockCounts = new Dictionary<(int PropertyId, int ValueId), int>();
        var uniqueTuples = new HashSet<(int SelectorId, int PropertyId, int ValueId, int ConditionId)>();

        for (var i = 0; i < table.RuleCount; i++)
        {
            var rule = table.GetRule(i);
            propertyCounts[rule.PropertyId] = propertyCounts.TryGetValue(rule.PropertyId, out var pc) ? pc + 1 : 1;
            valueCounts[rule.ValueId] = valueCounts.TryGetValue(rule.ValueId, out var vc) ? vc + 1 : 1;
            conditionCounts[rule.ConditionId] = conditionCounts.TryGetValue(rule.ConditionId, out var cc) ? cc + 1 : 1;
            var blockKey = (rule.PropertyId, rule.ValueId);
            declarationBlockCounts[blockKey] = declarationBlockCounts.TryGetValue(blockKey, out var dc) ? dc + 1 : 1;
            uniqueTuples.Add((rule.SelectorId, rule.PropertyId, rule.ValueId, rule.ConditionId));
        }

        if (options.Json)
        {
            WriteJson(loaded.Name, loaded.ByteLength, source.Length, tokens.Count, table, elapsed, allocated, gen0, gen1, gen2, tokenizeWatch.Elapsed, tokenizeAllocated, buildWatch.Elapsed, buildAllocated, options, selectorText!, ruleCounts!);
            return 0;
        }

        var color = !options.NoColor && !Console.IsOutputRedirected;

        WriteSummary(loaded.Name, loaded.ByteLength, source.Length, tokens.Count, table, elapsed, allocated, gen0, gen1, gen2, tokenizeWatch.Elapsed, tokenizeAllocated, buildWatch.Elapsed, buildAllocated, propertyCounts.Count, valueCounts.Count, uniqueTuples.Count, declarationBlockCounts.Count, color);

        if (options.ShowOccurrences)
        {
            WriteOccurrences(table, selectorText!, ruleCounts!, propertyCounts, valueCounts, conditionCounts, declarationBlockCounts, options.Limit, color);
        }

        if (options.ShowSelectors)
        {
            WriteSelectors(table, selectorText!, ruleCounts!, options.Limit, color);
        }

        if (options.ShowRules)
        {
            WriteRules(table, selectorText!, options.Limit, color);
        }

        return 0;
    }

    // ----------------------------------------------------------------------
    // Loading
    // ----------------------------------------------------------------------

    private static async Task<LoadedSource> LoadAsync(string target)
    {
        if (target == "-")
        {
            var stdinText = await Console.In.ReadToEndAsync();
            return new LoadedSource("<stdin>", stdinText, Encoding.UTF8.GetByteCount(stdinText));
        }

        if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            {
                return await LoadRemoteAsync(uri);
            }

            if (uri.Scheme == Uri.UriSchemeFile)
            {
                var filePath = uri.LocalPath;
                return new LoadedSource(filePath, await File.ReadAllTextAsync(filePath), new FileInfo(filePath).Length);
            }
        }

        var path = Path.GetFullPath(ExpandHome(Environment.ExpandEnvironmentVariables(target)));
        return new LoadedSource(path, await File.ReadAllTextAsync(path), new FileInfo(path).Length);
    }

    private static string ExpandHome(string path)
    {
        if (path != "~"
            && !path.StartsWith("~/", StringComparison.Ordinal)
            && !path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.Length <= 2 ? home : Path.Combine(home, path[2..]);
    }

    private static async Task<LoadedSource> LoadRemoteAsync(Uri uri)
    {
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.All
        };

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("IgniteCssDebug/1.0");

        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var charset = response.Content.Headers.ContentType?.CharSet;

        var text = Decode(bytes, charset);

        // Strip a UTF-8/UTF-16 byte-order mark if the charset decoder left it.
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        return new LoadedSource(uri.ToString(), text, bytes.LongLength);
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        var encoding = Encoding.UTF8;

        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim().Trim('"', '\''));
            }
            catch (ArgumentException)
            {
                // Unknown charset: UTF-8 is the safest CSS default.
            }
        }

        return encoding.GetString(bytes);
    }

    // ----------------------------------------------------------------------
    // Text report
    // ----------------------------------------------------------------------

    private static void WriteSummary(
        string name,
        long fileSizeBytes,
        int characters,
        int tokens,
        DataTable table,
        TimeSpan elapsed,
        long allocatedBytes,
        int gen0,
        int gen1,
        int gen2,
        TimeSpan tokenizeElapsed,
        long tokenizeAllocatedBytes,
        TimeSpan buildElapsed,
        long buildAllocatedBytes,
        int uniqueProperties,
        int uniqueValues,
        int uniqueTuples,
        int uniqueDeclarationBlocks,
        bool color)
    {
        var throughput = elapsed.TotalMilliseconds > 0
            ? characters / 1_000_000.0 / (elapsed.TotalMilliseconds / 1000.0)
            : 0;

        Console.WriteLine(Header("CSS breakdown", color));
        Console.WriteLine($"  source       {name}");
        Console.WriteLine($"  file size    {FormatBytes(fileSizeBytes)}  ({fileSizeBytes:N0} bytes)");
        Console.WriteLine($"  characters   {characters:N0}");
        Console.WriteLine($"  tokens       {tokens:N0}");
        Console.WriteLine($"  selectors    {table.SelectorCount:N0}");
        Console.WriteLine($"  rules        {table.RuleCount:N0}");
        Console.WriteLine($"  conditions   {table.ConditionCount:N0}");
        Console.WriteLine($"  discriminators {table.DiscriminatorCount:N0}");
        Console.WriteLine($"  tokenize     {tokenizeElapsed.TotalMilliseconds:N1} ms  {FormatBytes(tokenizeAllocatedBytes)}");
        Console.WriteLine($"  build        {buildElapsed.TotalMilliseconds:N1} ms  {FormatBytes(buildAllocatedBytes)}");
        Console.WriteLine($"  total        {elapsed.TotalMilliseconds:N1} ms  ({throughput:N2} MB/s)");
        Console.WriteLine($"  allocated    {FormatBytes(allocatedBytes)}");
        Console.WriteLine($"  GC (0/1/2)   {gen0}/{gen1}/{gen2}");
        Console.WriteLine();

        Console.WriteLine(Header("Interning & dedup", color));
        var avgRulesPerSelector = table.SelectorCount > 0
            ? table.RuleCount / (double)table.SelectorCount
            : 0;
        Console.WriteLine($"  unique selectors     {table.SelectorCount:N0}   (avg {avgRulesPerSelector:N2} rules each)");
        Console.WriteLine($"  unique properties    {uniqueProperties:N0}");
        Console.WriteLine($"  unique values        {uniqueValues:N0}");
        Console.WriteLine($"  unique rule tuples   {uniqueTuples:N0}   of {table.RuleCount:N0} stored");
        Console.WriteLine($"  unique decl blocks   {uniqueDeclarationBlocks:N0}   of {table.RuleCount:N0} stored  ({table.RuleCount - uniqueDeclarationBlocks:N0} reused)");
        Console.WriteLine($"  collapsed adjacent   {table.RuleCount - uniqueTuples:N0}  (distinct-tuple delta)");
        Console.WriteLine($"  discriminators       {table.DiscriminatorCount:N0}   (candidate groups)");
        Console.WriteLine();
    }

    private static void WriteSelectors(
        DataTable table,
        string?[] selectorText,
        int[] ruleCounts,
        int limit,
        bool color)
    {
        var total = table.SelectorCount;
        var shown = Limit(total, limit);

        Console.WriteLine(Header(
            $"Selectors ({total:N0} total{(shown < total ? $", showing {shown:N0}" : string.Empty)})",
            color));
        Console.WriteLine("      id    rules  tokens  discriminator                 selector");

        for (var id = 0; id < shown; id++)
        {
            var record = table.GetSelector(id);
            var text = selectorText[id] ??= SelectorText(table, id);
            var discriminator = DiscriminatorText(record.PrimaryDiscriminator);

            Console.WriteLine(
                $"  {id,7}  {ruleCounts[id],7}  {record.TokenLength,6}  {discriminator,-28}  {text}");
        }

        if (total == 0)
        {
            Console.WriteLine("  (none)");
        }
        else if (shown < total)
        {
            Console.WriteLine($"  ... {total - shown:N0} more (use --all or --limit 0)");
        }

        Console.WriteLine();
    }

    private static void WriteRules(
        DataTable table,
        string?[] selectorText,
        int limit,
        bool color)
    {
        var total = table.RuleCount;
        var shown = Limit(total, limit);

        Console.WriteLine(Header(
            $"Rules ({total:N0} total{(shown < total ? $", showing {shown:N0}" : string.Empty)})",
            color));
        Console.WriteLine("      id  selector id  property             value                          selector");

        for (var id = 0; id < shown; id++)
        {
            var rule = table.GetRule(id);
            var selector = selectorText[rule.SelectorId] ??= SelectorText(table, rule.SelectorId);
            var property = InternTable.GetString(rule.PropertyId) ?? $"<{rule.PropertyId}>";
            var value = InternTable.GetString(rule.ValueId) ?? $"<{rule.ValueId}>";

            Console.WriteLine(
                $"  {id,7}  {rule.SelectorId,8}  {property,-18}  {value,-29}  {selector}");
        }

        if (total == 0)
        {
            Console.WriteLine("  (none)");
        }
        else if (shown < total)
        {
            Console.WriteLine($"  ... {total - shown:N0} more (use --all or --limit 0)");
        }

        Console.WriteLine();
    }

    // ----------------------------------------------------------------------
    // JSON report
    // ----------------------------------------------------------------------

    private static void WriteJson(
        string name,
        long fileSizeBytes,
        int characters,
        int tokens,
        DataTable table,
        TimeSpan elapsed,
        long allocatedBytes,
        int gen0,
        int gen1,
        int gen2,
        TimeSpan tokenizeElapsed,
        long tokenizeAllocatedBytes,
        TimeSpan buildElapsed,
        long buildAllocatedBytes,
        Options options,
        string?[] selectorText,
        int[] ruleCounts)
    {
        using var stdout = Console.OpenStandardOutput();
        using var writer = new Utf8JsonWriter(stdout, new JsonWriterOptions
        {
            Indented = true,
            // Keep selector text readable in the debug output.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        var selectorShown = options.ShowSelectors ? Limit(table.SelectorCount, options.Limit) : 0;
        var ruleShown = options.ShowRules ? Limit(table.RuleCount, options.Limit) : 0;

        writer.WriteStartObject();
        writer.WriteString("source", name);
        writer.WriteNumber("fileSizeBytes", fileSizeBytes);
        writer.WriteNumber("characters", characters);
        writer.WriteNumber("tokens", tokens);
        writer.WriteNumber("selectors", table.SelectorCount);
        writer.WriteNumber("rules", table.RuleCount);
        writer.WriteNumber("buildMilliseconds", Math.Round(elapsed.TotalMilliseconds, 3));
        writer.WriteNumber("tokenizeMilliseconds", Math.Round(tokenizeElapsed.TotalMilliseconds, 3));
        writer.WriteNumber("tokenizeAllocatedBytes", tokenizeAllocatedBytes);
        writer.WriteNumber("buildStageMilliseconds", Math.Round(buildElapsed.TotalMilliseconds, 3));
        writer.WriteNumber("buildStageAllocatedBytes", buildAllocatedBytes);
        writer.WriteNumber("allocatedBytes", allocatedBytes);
        writer.WriteNumber("gen0Collections", gen0);
        writer.WriteNumber("gen1Collections", gen1);
        writer.WriteNumber("gen2Collections", gen2);
        writer.WriteNumber("throughputMegabytesPerSecond", Math.Round(
            elapsed.TotalMilliseconds > 0
                ? characters / 1_000_000.0 / (elapsed.TotalMilliseconds / 1000.0)
                : 0, 3));

        if (options.ShowSelectors)
        {
            writer.WriteNumber("selectorLimit", options.Limit);
            writer.WriteBoolean("selectorsTruncated", selectorShown < table.SelectorCount);
            writer.WriteStartArray("selectorItems");

            for (var id = 0; id < selectorShown; id++)
            {
                var record = table.GetSelector(id);
                var text = selectorText[id] ??= SelectorText(table, id);

                writer.WriteStartObject();
                writer.WriteNumber("id", id);
                writer.WriteString("text", text);
                writer.WriteNumber("ruleCount", ruleCounts[id]);
                writer.WriteNumber("tokenCount", record.TokenLength);
                writer.WriteStartObject("discriminator");
                writer.WriteString("kind", record.PrimaryDiscriminator.Kind.ToString());
                writer.WriteString(
                    "text",
                    InternTable.GetString(record.PrimaryDiscriminator.InternedId) ?? string.Empty);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (options.ShowRules)
        {
            writer.WriteNumber("ruleLimit", options.Limit);
            writer.WriteBoolean("rulesTruncated", ruleShown < table.RuleCount);
            writer.WriteStartArray("ruleItems");

            for (var id = 0; id < ruleShown; id++)
            {
                var rule = table.GetRule(id);
                var selector = selectorText[rule.SelectorId] ??= SelectorText(table, rule.SelectorId);

                writer.WriteStartObject();
                writer.WriteNumber("id", id);
                writer.WriteNumber("selectorId", rule.SelectorId);
                writer.WriteString("selector", selector);
                writer.WriteString("property", InternTable.GetString(rule.PropertyId) ?? string.Empty);
                writer.WriteString("value", InternTable.GetString(rule.ValueId) ?? string.Empty);
                writer.WriteNumber("conditionId", rule.ConditionId);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        writer.Flush();
    }

    // ----------------------------------------------------------------------
    // Occurrence report
    // ----------------------------------------------------------------------

    private static void WriteOccurrences(
        DataTable table,
        string?[] selectorText,
        int[] ruleCounts,
        Dictionary<int, int> propertyCounts,
        Dictionary<int, int> valueCounts,
        Dictionary<int, int> conditionCounts,
        Dictionary<(int PropertyId, int ValueId), int> declarationBlockCounts,
        int limit,
        bool color)
    {
        Console.WriteLine(Header("Symbol occurrences", color));

        // Selectors
        var selectorRows = new List<(int Id, int Count, string Text)>(table.SelectorCount);
        for (var id = 0; id < table.SelectorCount; id++)
        {
            selectorRows.Add((id, ruleCounts[id], selectorText[id] ??= SelectorText(table, id)));
        }

        selectorRows.Sort((a, b) => b.Count.CompareTo(a.Count));
        var shownSelectors = Limit(selectorRows.Count, limit);

        Console.WriteLine($"  Selectors ({selectorRows.Count:N0} unique, showing {shownSelectors:N0})");
        Console.WriteLine("      count  id      selector");
        for (var i = 0; i < shownSelectors; i++)
        {
            var row = selectorRows[i];
            Console.WriteLine($"  {row.Count,8}  {row.Id,6}  {row.Text}");
        }
        if (shownSelectors < selectorRows.Count)
        {
            Console.WriteLine($"  ... {selectorRows.Count - shownSelectors:N0} more (use --all or --limit 0)");
        }
        Console.WriteLine();

        // Properties
        var propertyRows = new List<(string Name, int Count)>(propertyCounts.Count);
        foreach (var pair in propertyCounts)
        {
            propertyRows.Add((InternTable.GetString(pair.Key) ?? $"<{pair.Key}>", pair.Value));
        }

        propertyRows.Sort((a, b) => b.Count.CompareTo(a.Count));
        var shownProperties = Limit(propertyRows.Count, limit);

        Console.WriteLine($"  Properties ({propertyRows.Count:N0} unique, showing {shownProperties:N0})");
        Console.WriteLine("      count  property");
        for (var i = 0; i < shownProperties; i++)
        {
            var row = propertyRows[i];
            Console.WriteLine($"  {row.Count,8}  {row.Name}");
        }
        if (shownProperties < propertyRows.Count)
        {
            Console.WriteLine($"  ... {propertyRows.Count - shownProperties:N0} more (use --all or --limit 0)");
        }
        Console.WriteLine();

        // Values
        var valueRows = new List<(string Value, int Count)>(valueCounts.Count);
        foreach (var pair in valueCounts)
        {
            valueRows.Add((InternTable.GetString(pair.Key) ?? $"<{pair.Key}>", pair.Value));
        }

        valueRows.Sort((a, b) => b.Count.CompareTo(a.Count));
        var shownValues = Limit(valueRows.Count, limit);

        Console.WriteLine($"  Values ({valueRows.Count:N0} unique, showing {shownValues:N0})");
        Console.WriteLine("      count  value");
        for (var i = 0; i < shownValues; i++)
        {
            var row = valueRows[i];
            Console.WriteLine($"  {row.Count,8}  {row.Value}");
        }
        if (shownValues < valueRows.Count)
        {
            Console.WriteLine($"  ... {valueRows.Count - shownValues:N0} more (use --all or --limit 0)");
        }
        Console.WriteLine();

        // Declaration blocks (property: value)
        var blockRows = new List<(string Block, int Count)>(declarationBlockCounts.Count);
        foreach (var pair in declarationBlockCounts)
        {
            var property = InternTable.GetString(pair.Key.PropertyId) ?? $"<{pair.Key.PropertyId}>";
            var value = InternTable.GetString(pair.Key.ValueId) ?? $"<{pair.Key.ValueId}>";
            blockRows.Add(($"{property}: {value}", pair.Value));
        }

        blockRows.Sort((a, b) => b.Count.CompareTo(a.Count));
        var shownBlocks = Limit(blockRows.Count, limit);

        Console.WriteLine($"  Declaration blocks ({blockRows.Count:N0} unique, showing {shownBlocks:N0})");
        Console.WriteLine("      count  declaration");
        for (var i = 0; i < shownBlocks; i++)
        {
            var row = blockRows[i];
            Console.WriteLine($"  {row.Count,8}  {row.Block}");
        }
        if (shownBlocks < blockRows.Count)
        {
            Console.WriteLine($"  ... {blockRows.Count - shownBlocks:N0} more (use --all or --limit 0)");
        }
        Console.WriteLine();

        // Conditions
        if (conditionCounts.Count > 0)
        {
            var conditionRows = new List<(int Id, int Count)>(conditionCounts.Count);
            foreach (var pair in conditionCounts)
            {
                if (pair.Key >= 0)
                {
                    conditionRows.Add((pair.Key, pair.Value));
                }
            }

            conditionRows.Sort((a, b) => b.Count.CompareTo(a.Count));
            var shownConditions = Limit(conditionRows.Count, limit);

            Console.WriteLine($"  Conditions ({conditionRows.Count:N0} unique, showing {shownConditions:N0})");
            Console.WriteLine("      count  id  prelude");
            for (var i = 0; i < shownConditions; i++)
            {
                var row = conditionRows[i];
                var tokens = table.GetConditionTokens(row.Id);
                var preludeBuilder = new StringBuilder();
                for (var j = 0; j < tokens.Length; j++)
                {
                    if (j > 0 && HasGap(tokens, j))
                    {
                        preludeBuilder.Append(' ');
                    }
                    preludeBuilder.Append(InternTable.GetString(tokens[j].InternedId));
                }
                var prelude = preludeBuilder.ToString();
                Console.WriteLine($"  {row.Count,8}  {row.Id,3}  {prelude}");
            }
            if (shownConditions < conditionRows.Count)
            {
                Console.WriteLine($"  ... {conditionRows.Count - shownConditions:N0} more (use --all or --limit 0)");
            }
            Console.WriteLine();
        }
    }

    // ----------------------------------------------------------------------
    // Table helpers
    // ----------------------------------------------------------------------

    private static int[] CountRulesPerSelector(DataTable table)
    {
        var counts = new int[table.SelectorCount];

        for (var i = 0; i < table.RuleCount; i++)
        {
            var selectorId = table.GetRule(i).SelectorId;
            if ((uint)selectorId < (uint)counts.Length)
            {
                counts[selectorId]++;
            }
        }

        return counts;
    }

    /// <summary>
    /// Rebuilds printable selector text from the table's interned token arena.
    /// Gaps that carry meaning become a single space; gaps around explicit
    /// combinators/operators are ignored, mirroring selector identity.
    /// </summary>
    private static string SelectorText(DataTable table, int selectorId)
    {
        var tokens = table.GetSelectorSpan(selectorId);
        var builder = new StringBuilder();

        for (var i = 0; i < tokens.Length; i++)
        {
            if (i > 0 && HasGap(tokens, i))
            {
                builder.Append(' ');
            }

            builder.Append(InternTable.GetString(tokens[i].InternedId));
        }

        return builder.ToString();
    }

    private static bool HasGap(ReadOnlySpan<Token> tokens, int index)
    {
        if (index <= 0)
        {
            return false;
        }

        if (IsSpacingInsensitive(tokens[index].Kind)
            || IsSpacingInsensitive(tokens[index - 1].Kind))
        {
            return false;
        }

        return tokens[index].StartIndex > tokens[index - 1].EndIndex;
    }

    private static bool IsSpacingInsensitive(TokenKind kind)
    {
        return kind is TokenKind.DirectChildBracket
            or TokenKind.Plus
            or TokenKind.Tilde
            or TokenKind.Equals
            or TokenKind.Comma;
    }

    private static string DiscriminatorText(in Token token)
    {
        var text = InternTable.GetString(token.InternedId) ?? string.Empty;
        return $"{token.Kind} \"{text}\"";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:N1} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / 1_048_576.0:N1} MB";
        }

        return $"{bytes / 1_073_741_824.0:N2} GB";
    }

    private static int Limit(int total, int limit)
    {
        return limit <= 0 ? total : Math.Min(total, limit);
    }

    private static string Header(string text, bool color)
    {
        return color ? $"\u001b[1;36m{text}\u001b[0m" : text;
    }

    // ----------------------------------------------------------------------
    // Options / help
    // ----------------------------------------------------------------------

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            CssDebug - inspect how a stylesheet is flattened into selectors and rules.

            Usage:
              CssDebug <file|url|-> [options]

            Source:
              <path>             Local CSS file (environment variables and ~ are expanded).
              <http(s)://url>    Remote stylesheet; follows redirects and decompresses gzip/brotli.
              -                  Read CSS from stdin.

            Options:
              --selectors        Show the interned selector table.
              --rules            Show the flattened rule table.
              --summary          Show only counts (no selector/rule rows).
              --all              Print every row (same as --limit 0).
              --limit <n>        Rows per section (default 50, 0 = unlimited).
              --json             Emit the report as JSON.
              --occurrences      Show per-symbol occurrence counts and dedup stats.
              --no-color         Disable ANSI colours.
              -h, --help         Show this help.

            Notes:
              Rules are shown after selector-list flattening, nesting expansion and
              adjacent-duplicate removal. Conditions are not displayed because they
              are compiled delegates whose contents are owned by the caller.

            Examples:
              CssDebug .\site.css
              CssDebug .\site.css --selectors --limit 200
              CssDebug https://example.com/site.css --rules --all
              Get-Content .\site.css -Raw | CssDebug - --json
            """);
    }

    private sealed class Options
    {
        public int Limit { get; set; } = DefaultLimit;

        public bool ShowSelectors { get; set; }

        public bool ShowRules { get; set; }

        public bool SummaryOnly { get; set; }

        public bool Json { get; set; }

        public bool ShowOccurrences { get; set; }

        public bool NoColor { get; set; }
    }

    private readonly record struct LoadedSource(string Name, string Text, long ByteLength);
}
