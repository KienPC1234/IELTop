using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IELTop.Services.Learn;

/// <summary>One teaching word from the lesson vocabulary sheets.</summary>
public sealed class LessonVocabulary
{
    public string Word { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    public string Meaning { get; set; } = string.Empty;
    public string Example { get; set; } = string.Empty;
    public string ExtraExample { get; set; } = string.Empty;
    public string Ipa { get; set; } = string.Empty;
    public string Derivatives { get; set; } = string.Empty;
}

/// <summary>One content block in a section: running text or a table.</summary>
public sealed class LessonBlock
{
    public string Type { get; set; } = "text";
    public string Text { get; set; } = string.Empty;
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; set; } = Array.Empty<IReadOnlyList<string>>();
}

/// <summary>One homework or handout section (Grammar, Listening, Reading, ...).</summary>
public sealed class LessonSection
{
    public string Id { get; set; } = string.Empty;
    public string Skill { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsAnswerKey { get; set; }
    public IReadOnlyList<LessonBlock> Blocks { get; set; } = Array.Empty<LessonBlock>();

    /// <summary>All block text joined, for search and for the model context.</summary>
    [JsonIgnore]
    public string FlatText
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var block in Blocks)
            {
                if (block.Type == "table")
                {
                    foreach (var row in block.Rows)
                    {
                        builder.AppendLine(string.Join(" | ", row));
                    }
                }
                else if (!string.IsNullOrWhiteSpace(block.Text))
                {
                    builder.AppendLine(block.Text);
                }
            }
            return builder.ToString().Trim();
        }
    }
}

public sealed class LessonSlide
{
    public int Index { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class LessonDeck
{
    public string File { get; set; } = string.Empty;
    public IReadOnlyList<LessonSlide> Slides { get; set; } = Array.Empty<LessonSlide>();
}

/// <summary>One unit file, loaded from Assets/Lessons or the user lesson folder.</summary>
public sealed class LessonUnit
{
    public string Unit { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public IReadOnlyList<string> Files { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Audio { get; set; } = Array.Empty<string>();
    public IReadOnlyList<LessonSection> Sections { get; set; } = Array.Empty<LessonSection>();
    public IReadOnlyList<LessonVocabulary> Vocabulary { get; set; } = Array.Empty<LessonVocabulary>();
    public IReadOnlyList<LessonDeck> Slides { get; set; } = Array.Empty<LessonDeck>();

    /// <summary>File stem, used as the stable id (for example "unit-1").</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Pictures (charts, photos) the ingest could not read without OCR. Reported
    /// so the app can say what is missing instead of silently lacking it.
    /// </summary>
    public int PicturesSkipped { get; set; }
}

/// <summary>
/// One search hit, carrying enough to cite it in a chat answer and to hand the
/// model the real section body. Snippet is the short preview shown as a badge;
/// Body is the whole section text, so the tutor answers from the lesson rather
/// than from a 320 character slice of it.
/// </summary>
public sealed record LessonHit(
    string Unit,
    string Slug,
    string SectionTitle,
    string Skill,
    string Snippet,
    int Score,
    string Body);

/// <summary>
/// Reads the normalized lesson JSON and searches it. The shipped folder is read
/// only; a user folder can add more units without touching the install. Nothing
/// here needs a model or the network: search is plain keyword scoring, so the
/// Study screen works fully offline.
/// </summary>
public sealed class LessonService
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private List<LessonUnit>? _cache;

    private static string? _userDirOverride;

    /// <summary>Points the user lesson folder at a temp path for tests.</summary>
    public static void SetUserDirForTesting(string? path)
    {
        _userDirOverride = path;
    }

    private static string ShippedDir =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "Lessons");

    private static string UserDir => _userDirOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IELTop", "content", "Lessons");

    /// <summary>All units, shipped plus user, newest file wins on the same slug.</summary>
    public IReadOnlyList<LessonUnit> Units
    {
        get
        {
            if (_cache is not null) return _cache;
            var bySlug = new Dictionary<string, LessonUnit>(StringComparer.OrdinalIgnoreCase);
            ReadFolder(ShippedDir, bySlug);
            ReadFolder(UserDir, bySlug);
            _cache = bySlug.Values
                .OrderBy(u => SortKey(u.Slug))
                .ToList();
            return _cache;
        }
    }

    /// <summary>True when at least one unit file was loaded.</summary>
    public bool HasLessons => Units.Count > 0;

    public LessonUnit? GetUnit(string slug) =>
        Units.FirstOrDefault(u => string.Equals(u.Slug, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>Clears the cache so a freshly copied lesson file is picked up.</summary>
    public void Reload()
    {
        _cache = null;
        _index = null;
    }

    private static (int, int, string) SortKey(string slug)
    {
        // "unit-3" sorts before "unit-10"; anything else sorts after by name.
        var parts = slug.Split('-');
        if (parts.Length == 2 && parts[0].Equals("unit", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], out var number))
        {
            return (0, number, slug);
        }
        return (1, 0, slug);
    }

    private void ReadFolder(string folder, Dictionary<string, LessonUnit> into)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                var unit = JsonSerializer.Deserialize<LessonUnit>(File.ReadAllText(file), _options);
                if (unit is null) continue;
                unit.Slug = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(unit.Unit)) unit.Unit = unit.Slug;
                into[unit.Slug] = unit;
            }
            catch (JsonException)
            {
                // A malformed lesson file is skipped so the rest still load.
            }
        }
    }

    /// <summary>
    /// Search over section text, slide decks, and vocabulary. Sections and decks
    /// are ranked by TF-IDF cosine similarity (a local lexical vector index built
    /// from the loaded units), so a document that is really about the query
    /// outranks one that mentions a word once. Returns the best matches with a
    /// short snippet, which the chat uses as sources.
    /// </summary>
    public IReadOnlyList<LessonHit> Search(string query, int max = 6, string? unitSlug = null)
    {
        var terms = Tokenize(query);
        if (terms.Count == 0) return Array.Empty<LessonHit>();

        var index = GetIndex();
        var hits = new List<LessonHit>();
        var units = unitSlug is null
            ? Units
            : Units.Where(u => string.Equals(u.Slug, unitSlug, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var unit in units)
        {
            foreach (var section in unit.Sections)
            {
                var text = section.FlatText;
                if (text.Length == 0) continue;

                double score = index.Score(text, terms);
                if (score <= 0) continue;
                hits.Add(new LessonHit(
                    unit.Unit, unit.Slug, section.Title, section.Skill,
                    Snippet(text, terms), (int)Math.Round(score * 1000), Snippet(text, terms, width: 1600)));
            }

            foreach (var deck in unit.Slides)
            {
                var text = string.Join("\n", deck.Slides.Select(s => s.Text));
                if (string.IsNullOrWhiteSpace(text)) continue;

                double score = index.Score(text, terms);
                if (score <= 0) continue;
                hits.Add(new LessonHit(
                    unit.Unit, unit.Slug, $"Slides: {deck.File}", "Slides",
                    Snippet(text, terms), (int)Math.Round(score * 1000), Snippet(text, terms, width: 1600)));
            }

            // Vocabulary matches are useful on their own, so they count as a hit.
            var vocab = unit.Vocabulary
                .Where(v => terms.Any(t =>
                    v.Word.Contains(t, StringComparison.OrdinalIgnoreCase)
                    || v.Meaning.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .Take(12)
                .ToList();
            if (vocab.Count > 0)
            {
                var text = string.Join("\n", vocab.Select(v =>
                    $"{v.Word} ({v.Form}) = {v.Meaning}. {v.Example}"));
                hits.Add(new LessonHit(
                    unit.Unit, unit.Slug, "Vocabulary", "Vocabulary",
                    Snippet(text, terms), vocab.Count * 3, Shorten(text, 2000)));
            }
        }

        return hits
            .OrderByDescending(h => h.Score)
            .Take(max)
            .ToList();
    }

    /// <summary>Vocabulary across units, filtered by a free text query.</summary>
    public IReadOnlyList<(LessonUnit Unit, LessonVocabulary Word)> Vocabulary(
        string? query = null, string? unitSlug = null, int max = 200)
    {
        var terms = Tokenize(query ?? string.Empty);
        var units = unitSlug is null
            ? Units
            : Units.Where(u => string.Equals(u.Slug, unitSlug, StringComparison.OrdinalIgnoreCase)).ToList();

        var rows = new List<(LessonUnit, LessonVocabulary)>();
        foreach (var unit in units)
        {
            foreach (var word in unit.Vocabulary)
            {
                if (terms.Count > 0 && !terms.Any(t =>
                        word.Word.Contains(t, StringComparison.OrdinalIgnoreCase)
                        || word.Meaning.Contains(t, StringComparison.OrdinalIgnoreCase)
                        || word.Form.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                rows.Add((unit, word));
                if (rows.Count >= max) return rows;
            }
        }
        return rows;
    }

    private TfIdfIndex? _index;

    /// <summary>
    /// The local retrieval index, built once from the loaded units and rebuilt
    /// when the lessons reload. It holds one TF-IDF vector per section and deck,
    /// so ranking is a cosine comparison done fully offline in memory.
    /// </summary>
    private TfIdfIndex GetIndex()
    {
        if (_index is not null) return _index;

        var documents = new List<string>();
        foreach (var unit in Units)
        {
            foreach (var section in unit.Sections)
            {
                if (section.FlatText.Length > 0) documents.Add(section.FlatText);
            }
            foreach (var deck in unit.Slides)
            {
                var text = string.Join("\n", deck.Slides.Select(s => s.Text));
                if (!string.IsNullOrWhiteSpace(text)) documents.Add(text);
            }
        }
        _index = new TfIdfIndex(documents);
        return _index;
    }

    /// <summary>
    /// Term frequency / inverse document frequency with cosine similarity. Each
    /// document is a TF-IDF vector; a query scores by its cosine with that
    /// vector, so rare matching words count more than common ones and a long
    /// document does not win just for being long. Pure C#, no model, no network.
    /// </summary>
    private sealed class TfIdfIndex
    {
        private readonly Dictionary<string, double> _idf;
        private readonly double _docCount;

        public TfIdfIndex(IReadOnlyList<string> documents)
        {
            _docCount = Math.Max(1, documents.Count);
            var docFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var document in documents)
            {
                foreach (var term in Tokenize(document).Distinct())
                {
                    docFrequency[term] = docFrequency.TryGetValue(term, out var count) ? count + 1 : 1;
                }
            }

            _idf = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (term, count) in docFrequency)
            {
                // Smoothed IDF: a term in every document still scores a little.
                _idf[term] = 1.0 + Math.Log(_docCount / count);
            }
        }

        /// <summary>Cosine similarity between the query and one document, 0 to 1.</summary>
        public double Score(string document, List<string> terms)
        {
            var docTerms = Tokenize(document);
            if (docTerms.Count == 0 || terms.Count == 0) return 0;

            var docCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var term in docTerms)
            {
                docCounts[term] = docCounts.TryGetValue(term, out var count) ? count + 1 : 1;
            }

            double dot = 0, docNorm = 0, queryNorm = 0;
            foreach (var term in terms.Distinct())
            {
                if (!_idf.TryGetValue(term, out var idf)) continue;
                double queryWeight = idf;
                queryNorm += queryWeight * queryWeight;

                if (!docCounts.TryGetValue(term, out var count)) continue;
                // Log-scaled term frequency, so stuffing a word does not dominate.
                double docWeight = (1.0 + Math.Log(count)) * idf;
                dot += queryWeight * docWeight;
                docNorm += docWeight * docWeight;
            }

            if (dot <= 0 || docNorm <= 0 || queryNorm <= 0) return 0;
            return dot / (Math.Sqrt(docNorm) * Math.Sqrt(queryNorm));
        }
    }

    private static List<string> Tokenize(string query) =>
        query
            .ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', ',', '.', '?', '!', ';', ':', '"', '\'', '(', ')' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1)
            .Distinct()
            .ToList();

    /// <summary>A window of text around the first matching term.</summary>
    private static string Snippet(string text, List<string> terms, int width = 320)
    {
        var flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        int at = -1;
        foreach (var term in terms)
        {
            at = flat.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) break;
        }
        if (at < 0) at = 0;
        int start = Math.Max(0, at - width / 3);
        int length = Math.Min(width, flat.Length - start);
        var slice = flat.Substring(start, length);
        return (start > 0 ? "..." : string.Empty) + slice + (start + length < flat.Length ? "..." : string.Empty);
    }

    private static string Shorten(string text, int max) =>
        text.Length <= max ? text : text[..max] + "...";
}
