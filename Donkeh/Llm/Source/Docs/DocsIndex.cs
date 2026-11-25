using System.Text.Json;

namespace Donkeh.Llm.Docs;

/// <summary>One code block in a documentation section.</summary>
public sealed class DocsCodeBlock
{
    /// <summary>Label from the documentation, for example a filename or Terminal.</summary>
    public string? Label { get; init; }

    /// <summary>csharp, css, json, shell, html or text.</summary>
    public required string Language { get; init; }

    /// <summary>Code text.</summary>
    public required string Content { get; init; }
}

/// <summary>One documentation section.</summary>
public sealed class DocsSection
{
    /// <summary>Stable section id.</summary>
    public required string Id { get; init; }

    /// <summary>Section heading.</summary>
    public required string Title { get; init; }

    /// <summary>Plain-text section body without code blocks.</summary>
    public string? Text { get; init; }

    /// <summary>Code blocks in the section.</summary>
    public IReadOnlyList<DocsCodeBlock> Code { get; init; } = [];
}

/// <summary>One documentation topic.</summary>
public sealed class DocsTopic
{
    /// <summary>Stable topic key, normally the page filename.</summary>
    public required string Topic { get; init; }

    /// <summary>Page title.</summary>
    public required string Title { get; init; }

    /// <summary>Lead paragraph.</summary>
    public string? Summary { get; init; }

    /// <summary>Documentation source reference.</summary>
    public string? Reference { get; init; }

    /// <summary>Sections in page order.</summary>
    public IReadOnlyList<DocsSection> Sections { get; init; } = [];

    /// <summary>API signatures extracted from code samples.</summary>
    public IReadOnlyList<string> ApiSignatures { get; init; } = [];

    /// <summary>Minimal examples.</summary>
    public IReadOnlyList<DocsExample> Examples { get; init; } = [];
}

/// <summary>A small documentation example.</summary>
public sealed class DocsExample
{
    /// <summary>Section that contains the example.</summary>
    public string? Section { get; init; }

    /// <summary>Original code-block label.</summary>
    public string? Label { get; init; }

    /// <summary>Example code.</summary>
    public required string Code { get; init; }
}

/// <summary>One ranked documentation search result.</summary>
public sealed class DocsSearchResult
{
    /// <summary>Topic key.</summary>
    public required string Topic { get; init; }

    /// <summary>Page title.</summary>
    public required string Title { get; init; }

    /// <summary>Best matching section, when the match was section-level.</summary>
    public string? Section { get; init; }

    /// <summary>Short relevant explanation.</summary>
    public required string Summary { get; init; }

    /// <summary>Relevant API signatures.</summary>
    public IReadOnlyList<string>? ApiSignatures { get; init; }

    /// <summary>Minimal working example, when available.</summary>
    public string? Example { get; init; }

    /// <summary>Command that retrieves the full topic or section.</summary>
    public required string GetCommand { get; init; }

    /// <summary>Documentation source reference.</summary>
    public string? Reference { get; init; }

    /// <summary>Ranking score; higher is better.</summary>
    public int Score { get; init; }
}

/// <summary>Documentation index loaded from the embedded documentation snapshot.</summary>
public static class DocsIndex
{
    private static List<DocsTopic>? _topics;

    /// <summary>Returns every documentation topic.</summary>
    public static List<DocsTopic> Topics()
    {
        if (_topics is not null)
        {
            return _topics;
        }

        try
        {
            using var stream = typeof(DocsIndex).Assembly
                .GetManifestResourceStream("Donkeh.Llm.Resources.docs.json");

            if (stream is null)
            {
                return _topics = [];
            }

            using var document = JsonDocument.Parse(stream);
            var topics = new List<DocsTopic>();

            foreach (var topic in document.RootElement.GetProperty("topics").EnumerateArray())
            {
                var sections = new List<DocsSection>();

                foreach (var section in topic.GetProperty("sections").EnumerateArray())
                {
                    var code = new List<DocsCodeBlock>();

                    foreach (var block in section.GetProperty("code").EnumerateArray())
                    {
                        code.Add(new DocsCodeBlock
                        {
                            Label = block.GetProperty("label").GetString(),
                            Language = block.GetProperty("language").GetString() ?? "text",
                            Content = block.GetProperty("content").GetString() ?? ""
                        });
                    }

                    var text = section.GetProperty("text").GetString();
                    sections.Add(new DocsSection
                    {
                        Id = section.GetProperty("id").GetString() ?? "",
                        Title = section.GetProperty("title").GetString() ?? "",
                        Text = string.IsNullOrWhiteSpace(text) ? null : text,
                        Code = code
                    });
                }

                var examples = new List<DocsExample>();

                foreach (var example in topic.GetProperty("examples").EnumerateArray())
                {
                    examples.Add(new DocsExample
                    {
                        Section = example.GetProperty("section").GetString(),
                        Label = example.GetProperty("label").GetString(),
                        Code = example.GetProperty("code").GetString() ?? ""
                    });
                }

                topics.Add(new DocsTopic
                {
                    Topic = topic.GetProperty("topic").GetString() ?? "",
                    Title = topic.GetProperty("title").GetString() ?? "",
                    Summary = topic.GetProperty("summary").GetString(),
                    Reference = topic.GetProperty("reference").GetString(),
                    Sections = sections,
                    ApiSignatures = topic.GetProperty("apiSignatures").EnumerateArray()
                        .Select(item => item.GetString() ?? "")
                        .Where(item => item.Length > 0)
                        .ToList(),
                    Examples = examples
                });
            }

            return _topics = topics;
        }
        catch (Exception)
        {
            return _topics = [];
        }
    }

    /// <summary>Finds a topic by key or title.</summary>
    public static DocsTopic? Find(string topic)
    {
        var value = topic.Trim();
        return Topics().FirstOrDefault(candidate =>
                   candidate.Topic.Equals(value, StringComparison.OrdinalIgnoreCase)
                   || candidate.Title.Equals(value, StringComparison.OrdinalIgnoreCase))
               ?? Topics().FirstOrDefault(candidate =>
                   candidate.Topic.Contains(value, StringComparison.OrdinalIgnoreCase)
                   || candidate.Title.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Searches topics and sections with a conceptual query.</summary>
    public static List<DocsSearchResult> Search(string query, int limit = 5) =>
        Search(Topics(), query, limit);

    /// <summary>Searches a supplied topic set, including package-contributed topics.</summary>
    public static List<DocsSearchResult> Search(
        IReadOnlyList<DocsTopic> topics,
        string query,
        int limit = 5)
    {
        var tokens = Tokenize(query);

        if (tokens.Count == 0)
        {
            return [];
        }

        var results = new List<DocsSearchResult>();

        foreach (var topic in topics)
        {
            var topicScore = Score(topic.Title, tokens, 10) + Score(topic.Summary, tokens, 4);

            foreach (var section in topic.Sections)
            {
                var sectionScore = Score(section.Title, tokens, 8)
                    + Score(section.Text, tokens, 2)
                    + section.Code.Sum(item => Score(item.Label, tokens, 2));
                var score = topicScore + sectionScore;

                if (score <= 0)
                {
                    continue;
                }

                var example = section.Code.FirstOrDefault(block =>
                    block.Language == "csharp" && block.Content.Length > 0)?.Content;
                var signatures = topic.ApiSignatures
                    .Where(signature => tokens.Any(token =>
                        signature.Contains(token, StringComparison.OrdinalIgnoreCase)))
                    .Take(4)
                    .ToList();

                results.Add(new DocsSearchResult
                {
                    Topic = topic.Topic,
                    Title = topic.Title,
                    Section = section.Id,
                    Summary = Summarize(section.Text, topic.Summary),
                    ApiSignatures = signatures.Count == 0 ? null : signatures,
                    Example = example,
                    GetCommand = $"donkeh docs get {topic.Topic} --section {section.Id}",
                    Reference = topic.Reference,
                    Score = score
                });
            }
        }

        return results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title, StringComparer.Ordinal)
            .ThenBy(result => result.Section, StringComparer.Ordinal)
            .GroupBy(result => result.Topic, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(Math.Clamp(limit, 1, 20))
            .ToList();
    }

    private static string Summarize(string? text, string? fallback)
    {
        var value = string.IsNullOrWhiteSpace(text) ? fallback : text;

        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var cleaned = string.Join(' ', value.Split(
            ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return cleaned.Length <= 320 ? cleaned : cleaned[..317] + "...";
    }

    private static List<string> Tokenize(string query) =>
        query.Split(
                [' ', ',', '.', ';', ':', '(', ')', '[', ']', '/', '\\', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1)
            .Select(token => token.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int Score(string? haystack, List<string> tokens, int weight)
    {
        if (string.IsNullOrWhiteSpace(haystack))
        {
            return 0;
        }

        var score = 0;

        foreach (var token in tokens)
        {
            var index = haystack.IndexOf(token, StringComparison.OrdinalIgnoreCase);

            if (index < 0)
            {
                continue;
            }

            score += weight;
            score += Math.Min(4, CountOccurrences(haystack, token));
            score += index == 0 ? 2 : 0;
        }

        return score;
    }

    private static int CountOccurrences(string haystack, string token)
    {
        var count = 0;
        var index = 0;

        while ((index = haystack.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
