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
    public string Topic { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsAnswerKey { get; set; }
    public string KeySectionId { get; set; } = string.Empty;
    public string TargetSectionId { get; set; } = string.Empty;
    public float[]? Embedding { get; set; }
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
    public string Topic { get; set; } = string.Empty;
    public float[]? Embedding { get; set; }
    public IReadOnlyList<LessonSlide> Slides { get; set; } = Array.Empty<LessonSlide>();
}

/// <summary>One unit file, loaded from Assets/Lessons or the user lesson folder.</summary>
public sealed class LessonUnit
{
    public string Unit { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public IReadOnlyList<string> Topics { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Skills { get; set; } = Array.Empty<string>();
    public string Source { get; set; } = string.Empty;
    public IReadOnlyList<string> Files { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Audio { get; set; } = Array.Empty<string>();
    public IReadOnlyList<LessonSection> Sections { get; set; } = Array.Empty<LessonSection>();
    public IReadOnlyList<LessonVocabulary> Vocabulary { get; set; } = Array.Empty<LessonVocabulary>();
    public IReadOnlyList<LessonDeck> Slides { get; set; } = Array.Empty<LessonDeck>();

    /// <summary>
    /// Pictures (charts, photos) the ingest could not read without OCR. Reported
    /// so the app can say what is missing instead of silently lacking it.
    /// </summary>
    public int PicturesSkipped { get; set; }

    /// <summary>
    /// Descriptive title stating clearly what topic and grammar focus the unit covers.
    /// </summary>
    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title) && Title.Contains(':'))
                return Title;

            string theme = MainTheme;
            string grammar = GrammarFocus;

            if (!string.IsNullOrEmpty(theme) && !string.IsNullOrEmpty(grammar))
                return $"{Unit}: {theme} ({grammar})";
            if (!string.IsNullOrEmpty(theme))
                return $"{Unit}: {theme}";
            if (!string.IsNullOrEmpty(Title))
                return Title;
            return Unit;
        }
    }

    /// <summary>
    /// The primary topical theme or reading subject of this unit.
    /// </summary>
    [JsonIgnore]
    public string MainTheme
    {
        get
        {
            var readingSec = Sections.FirstOrDefault(s => s.Skill.Equals("Reading", StringComparison.OrdinalIgnoreCase) && !s.IsAnswerKey);
            if (readingSec != null)
            {
                string t = readingSec.Topic;
                if (string.IsNullOrWhiteSpace(t) || t.Equals("Reading Comprehension", StringComparison.OrdinalIgnoreCase))
                    t = readingSec.Title;
                if (t.StartsWith("Reading:", StringComparison.OrdinalIgnoreCase))
                    t = t[8..].Trim();
                if (!string.IsNullOrWhiteSpace(t))
                    return t;
            }

            var otherTopic = Topics.FirstOrDefault(t =>
                !t.Equals("Grammar", StringComparison.OrdinalIgnoreCase) &&
                !t.Equals("Listening Comprehension", StringComparison.OrdinalIgnoreCase) &&
                !t.Equals("Reading Comprehension", StringComparison.OrdinalIgnoreCase) &&
                !t.Equals("Reading Vocabulary", StringComparison.OrdinalIgnoreCase) &&
                !t.Equals("Speaking Practice", StringComparison.OrdinalIgnoreCase) &&
                !t.Equals("Writing", StringComparison.OrdinalIgnoreCase));

            return otherTopic ?? Title;
        }
    }

    /// <summary>
    /// The specific grammar or language structure focus of this unit.
    /// </summary>
    [JsonIgnore]
    public string GrammarFocus
    {
        get
        {
            var grammarSec = Sections.FirstOrDefault(s => s.Skill.Equals("Grammar", StringComparison.OrdinalIgnoreCase) && !s.IsAnswerKey);
            if (grammarSec != null)
            {
                string g = grammarSec.Title;
                if (string.IsNullOrWhiteSpace(g) || g.Equals("Grammar", StringComparison.OrdinalIgnoreCase))
                    g = grammarSec.Topic;
                if (!string.IsNullOrWhiteSpace(g))
                    return g;
            }
            return string.Empty;
        }
    }
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
    string Body,
    string Topic = "",
    string SectionId = "");

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

    private static string? _shippedDirOverride;
    public static void SetShippedDirForTesting(string? path) => _shippedDirOverride = path;

    private static string ShippedDir
    {
        get
        {
            if (_shippedDirOverride is not null) return _shippedDirOverride;
            var p1 = Path.Combine(AppContext.BaseDirectory, "Assets", "Lessons");
            if (Directory.Exists(p1)) return p1;
            // In unit tests with mock user dir, keep shipped dir isolated so mock data is not contaminated
            if (_userDirOverride is not null) return p1;
            var p2 = Path.Combine(Directory.GetCurrentDirectory(), "Content", "Assets", "Lessons");
            if (Directory.Exists(p2)) return p2;
            var p3 = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Content", "Assets", "Lessons");
            if (Directory.Exists(p3)) return Path.GetFullPath(p3);
            return p1;
        }
    }

    private static string UserDir => _userDirOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IELTop", "content", "Lessons");

    /// <summary>Summary metrics of all ingested lesson knowledge.</summary>
    public (int UnitCount, int SectionCount, int VocabCount, int SlideCount) GetKnowledgeMetrics()
    {
        var units = Units;
        int sections = units.Sum(u => u.Sections.Count);
        int vocabs = units.Sum(u => u.Vocabulary.Count);
        int slides = units.Sum(u => u.Slides.Sum(s => s.Slides.Count));
        return (units.Count, sections, vocabs, slides);
    }

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
        // "unit-1".."unit-10" sort by number
        var parts = slug.Split('-');
        if (parts.Length == 2 && parts[0].Equals("unit", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], out var number))
        {
            return (0, number, slug);
        }
        if (slug.StartsWith("writing-grammar", StringComparison.OrdinalIgnoreCase)) return (1, 1, slug);
        if (slug.StartsWith("writing-task-1", StringComparison.OrdinalIgnoreCase)) return (1, 2, slug);
        if (slug.StartsWith("writing-task-2", StringComparison.OrdinalIgnoreCase)) return (1, 3, slug);
        if (slug.StartsWith("midterm", StringComparison.OrdinalIgnoreCase)) return (2, 0, slug);
        return (3, 0, slug);
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
                if (unit.Topics.Count == 0)
                {
                    unit.Topics = unit.Sections.Select(s => s.Topic).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().OrderBy(t => t).ToList();
                }
                if (unit.Skills.Count == 0)
                {
                    unit.Skills = unit.Sections.Select(s => s.Skill).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                }
                into[unit.Slug] = unit;
            }
            catch (JsonException)
            {
                // A malformed lesson file is skipped so the rest still load.
            }
        }
    }

    public static float CosineSimilarity(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (a.Count == 0 || b.Count == 0 || a.Count != b.Count) return 0f;
        float dot = 0f;
        for (int i = 0; i < a.Count; i++)
        {
            dot += a[i] * b[i];
        }
        return dot;
    }

    public (LessonSection? Section, LessonSection? KeySection, LessonUnit? Unit) GetSection(string unitSlug, string sectionId)
    {
        var unit = GetUnit(unitSlug);
        if (unit is null) return (null, null, null);
        var section = (!string.IsNullOrWhiteSpace(sectionId)
            ? unit.Sections.FirstOrDefault(s => string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase))
            : null) ?? unit.Sections.FirstOrDefault();
        if (section is null) return (null, null, unit);

        LessonSection? keySection = null;
        if (!string.IsNullOrWhiteSpace(section.KeySectionId))
        {
            keySection = unit.Sections.FirstOrDefault(s => string.Equals(s.Id, section.KeySectionId, StringComparison.OrdinalIgnoreCase));
        }
        else if (section.IsAnswerKey && !string.IsNullOrWhiteSpace(section.TargetSectionId))
        {
            keySection = unit.Sections.FirstOrDefault(s => string.Equals(s.Id, section.TargetSectionId, StringComparison.OrdinalIgnoreCase));
        }

        return (section, keySection, unit);
    }

    /// <summary>
    /// Hybrid search over section text, slide decks, and vocabulary. Combines lexical TF-IDF
    /// with pre-computed vector cosine similarity when available.
    /// </summary>
    public IReadOnlyList<LessonHit> Search(
        string query, int max = 6, string? unitSlug = null, string? topic = null, float[]? queryEmbedding = null)
    {
        var terms = Tokenize(query);
        var index = GetIndex();
        var hits = new List<LessonHit>();
        var units = unitSlug is null
            ? Units
            : Units.Where(u => string.Equals(u.Slug, unitSlug, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var unit in units)
        {
            foreach (var section in unit.Sections)
            {
                if (!string.IsNullOrWhiteSpace(topic) && !string.Equals(section.Topic, topic, StringComparison.OrdinalIgnoreCase))
                    continue;

                var text = section.FlatText;
                if (text.Length == 0) continue;

                double lexicalScore = terms.Count > 0 ? index.Score(text, terms) : 0;
                float vectorScore = 0f;
                if (queryEmbedding is not null && section.Embedding is not null && section.Embedding.Length > 0)
                {
                    vectorScore = Math.Max(0f, CosineSimilarity(queryEmbedding, section.Embedding));
                }

                double score = lexicalScore;
                if (vectorScore > 0)
                {
                    score = (lexicalScore * 0.3) + (vectorScore * 1.5);
                }

                if (terms.Any(t => section.Title.Contains(t, StringComparison.OrdinalIgnoreCase))) score += 0.4;
                if (terms.Any(t => section.Topic.Contains(t, StringComparison.OrdinalIgnoreCase))) score += 0.4;

                if (score <= 0) continue;
                hits.Add(new LessonHit(
                    unit.Unit, unit.Slug, section.Title, section.Skill,
                    Snippet(text, terms), (int)Math.Round(score * 1000), Snippet(text, terms, width: 1800),
                    section.Topic, section.Id));
            }

            foreach (var deck in unit.Slides)
            {
                var text = string.Join("\n", deck.Slides.Select(s => s.Text));
                if (string.IsNullOrWhiteSpace(text)) continue;

                double lexicalScore = terms.Count > 0 ? index.Score(text, terms) : 0;
                float vectorScore = 0f;
                if (queryEmbedding is not null && deck.Embedding is not null && deck.Embedding.Length > 0)
                {
                    vectorScore = Math.Max(0f, CosineSimilarity(queryEmbedding, deck.Embedding));
                }

                double score = lexicalScore;
                if (vectorScore > 0)
                {
                    score = (lexicalScore * 0.3) + (vectorScore * 1.5);
                }

                if (score <= 0) continue;
                hits.Add(new LessonHit(
                    unit.Unit, unit.Slug, $"Slides: {deck.File}", "Slides",
                    Snippet(text, terms), (int)Math.Round(score * 1000), Snippet(text, terms, width: 1800),
                    deck.Topic, ""));
            }

            // Vocabulary matches are useful on their own, so they count as a hit.
            if (terms.Count > 0)
            {
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
                        Snippet(text, terms), vocab.Count * 3, Shorten(text, 2000),
                        "Vocabulary", ""));
                }
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
