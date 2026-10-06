using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Data;
using IELTop.Models;
using IELTop.Services.Ai;

namespace IELTop.Services.Learn;

// ---------------------------------------------------------------- snapshots

public sealed record StudySessionRow(
    int Id, string Title, string Kind, string Unit, string Topic, int ItemCount, bool Pinned, DateTime UpdatedAt);

public sealed record StudySource(string Unit, string Section, string Snippet);

public sealed record StudyMessageRow(
    int Id, string Role, string Text, IReadOnlyList<StudySource> Sources, DateTime CreatedAt, string ExerciseJson = "");

public sealed record StudyUnitRow(
    string Slug, string Title, string Category, int SectionCount, int WordCount, int PicturesSkipped,
    IReadOnlyList<string> Topics, IReadOnlyList<string> Skills);

public sealed record StudyMaterialSectionRow(
    string Id, string Title, string Skill, string Topic, bool IsAnswerKey, string KeySectionId, string TargetSectionId, int BlockCount);

public sealed record StudyMaterialSnapshot
{
    public IReadOnlyList<StudyUnitRow> Units { get; init; } = Array.Empty<StudyUnitRow>();
    public string SelectedUnit { get; init; } = string.Empty;
    public string SelectedCategory { get; init; } = string.Empty;
    public IReadOnlyList<string> UnitTopics { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnitSkills { get; init; } = Array.Empty<string>();
    public IReadOnlyList<StudyMaterialSectionRow> Sections { get; init; } = Array.Empty<StudyMaterialSectionRow>();
    public string SelectedSectionId { get; init; } = string.Empty;
    public LessonSection? CurrentSection { get; init; }
    public LessonSection? PairedKeySection { get; init; }
    public IReadOnlyList<LessonSlide> CurrentSlides { get; init; } = Array.Empty<LessonSlide>();
    public IReadOnlyList<string> CurrentAudio { get; init; } = Array.Empty<string>();
}

public sealed record PracticeSetRow(
    int Id, int SessionId, string Title, string Skill, string Unit, string Status,
    int Score, int Total, int QuestionCount, DateTime CreatedAt);

/// <summary>One row of a match question: a label to place and the accepted answer.</summary>
public sealed record PracticeMatchRow(string Label, string Answer);

public sealed record PracticeQuestionRow(
    int Id, int Number, string Kind, string Prompt,
    IReadOnlyList<PracticeOption> Options, IReadOnlyList<PracticeMatchRow> MatchRows,
    string CorrectKey, string GapAnswer,
    string Explanation, string UserAnswer, bool IsCorrect, bool IsFlagged);

public sealed record PracticeSetDetail(
    int Id, string Title, string Skill, string Unit, string Source, string Status,
    int Score, int Total, IReadOnlyList<PracticeQuestionRow> Questions);

public sealed record VocabRow(
    string Unit, string Word, string Form, string Meaning,
    string Example, string ExtraExample, string Ipa, string Derivatives);

public sealed record StudyChatSnapshot
{
    public IReadOnlyList<StudySessionRow> Sessions { get; init; } = Array.Empty<StudySessionRow>();
    public int SelectedSessionId { get; init; }
    public string SelectedTitle { get; init; } = string.Empty;
    public string SelectedUnit { get; init; } = string.Empty;
    public IReadOnlyList<StudyMessageRow> Messages { get; init; } = Array.Empty<StudyMessageRow>();
    public IReadOnlyList<StudyUnitRow> Units { get; init; } = Array.Empty<StudyUnitRow>();
    public bool CanUseAi { get; init; }
    public string AiHint { get; init; } = string.Empty;
    public bool HasUnits { get; init; }
    public bool IsBusy { get; init; }
    public string StatusMessage { get; init; } = string.Empty;

    /// <summary>True when the view shows one assistant message, not the full list of turns.</summary>
    public bool ShowAssistant { get; init; }
}

public sealed record StudyPracticeSnapshot
{
    public IReadOnlyList<StudySessionRow> Sessions { get; init; } = Array.Empty<StudySessionRow>();
    public int SelectedSessionId { get; init; }
    public string SelectedTitle { get; init; } = string.Empty;
    public string SelectedUnit { get; init; } = string.Empty;
    public IReadOnlyList<PracticeSetRow> Sets { get; init; } = Array.Empty<PracticeSetRow>();
    public PracticeSetDetail? OpenSet { get; init; }
    public IReadOnlyList<StudyUnitRow> Units { get; init; } = Array.Empty<StudyUnitRow>();
    public IReadOnlyList<string> Skills { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Difficulties { get; init; } = Array.Empty<string>();
    public bool CanUseAi { get; init; }
    public string AiHint { get; init; } = string.Empty;
    public bool IsBusy { get; init; }
    public string StatusMessage { get; init; } = string.Empty;

    /// <summary>Session filter: all saved sessions, or only the ones with questions of this kind.</summary>
    public string Scope { get; init; } = "All";
}

public sealed record StudyVocabSnapshot
{
    public IReadOnlyList<StudyUnitRow> Units { get; init; } = Array.Empty<StudyUnitRow>();
    public string UnitFilter { get; init; } = string.Empty;
    public string Query { get; init; } = string.Empty;
    public IReadOnlyList<VocabRow> Words { get; init; } = Array.Empty<VocabRow>();
    public int Total { get; init; }
}

// ---------------------------------------------------------------- service

/// <summary>
/// The Study screen: a tutor chat grounded in the lesson text, a practice
/// builder that saves sets the student can reopen, and a vocabulary browser.
/// Sessions and saved sets live in SQLite, so history survives a restart.
///
/// Reading, search, sessions and vocabulary work with no model and no network.
/// Building questions and explaining answers need a language model; when none
/// is configured those buttons are disabled with a reason, never fail late.
/// </summary>
public sealed class StudyService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly LessonService _lessons;
    private readonly ILlmService _llm;
    private Action<string, object?>? _broadcaster;

    public StudyService(LessonService lessons, ILlmService llm)
    {
        _lessons = lessons;
        _llm = llm;
    }

    public void SetBroadcaster(Action<string, object?> broadcaster)
    {
        _broadcaster = broadcaster;
    }

    private bool CanUseAi => _llm.IsConfigured;
    private static string AiHint =>
        "Add a language model in Settings to chat with the tutor and build practice sets.";

    public static readonly IReadOnlyList<LlmToolDefinition> TutorTools = new List<LlmToolDefinition>
    {
        new(
            Name: "search_curriculum",
            Description: "Search the official IELTS lesson curriculum (14 units, 165 sections) for grammar explanations, passages, writing strategies, and vocabulary.",
            ParametersJsonSchema: """
            {
                "type": "object",
                "properties": {
                    "query": { "type": "string", "description": "Key concept, topic or grammar point to search in curriculum" },
                    "unit": { "type": "string", "description": "Optional unit filter, e.g. 'unit-1' or empty string" }
                },
                "required": ["query"]
            }
            """
        ),
        new(
            Name: "get_vocabulary_entry",
            Description: "Lookup an academic or IELTS vocabulary word in the curriculum dictionary, returning its definition, phonetics, examples, and collocations.",
            ParametersJsonSchema: """
            {
                "type": "object",
                "properties": {
                    "word": { "type": "string", "description": "The English word to look up" }
                },
                "required": ["word"]
            }
            """
        ),
        new(
            Name: "generate_interactive_exercise",
            Description: "Generate an interactive IELTS practice question card for the student to practice immediately (formats: single, multiple, gap, match, reorder, identify_error, rewrite, short).",
            ParametersJsonSchema: """
            {
                "type": "object",
                "properties": {
                    "topic": { "type": "string", "description": "Topic or grammar point for the question" },
                    "skill": { "type": "string", "description": "Skill: Reading, Writing, Listening, Speaking, or Grammar" },
                    "kind": { "type": "string", "description": "Format: single, multiple, gap, match, reorder, identify_error, rewrite, short" }
                },
                "required": ["topic", "skill", "kind"]
            }
            """
        )
    };

    public async Task<string> ExecuteTutorToolAsync(string toolName, string argumentsJson, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var root = doc.RootElement;

            switch (toolName)
            {
                case "search_curriculum":
                {
                    string query = root.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
                    string unit = root.TryGetProperty("unit", out var u) ? u.GetString() ?? "" : "";
                    var hits = _lessons.Search(query, max: 4, unitSlug: string.IsNullOrWhiteSpace(unit) ? null : unit);
                    if (hits.Count == 0) return JsonSerializer.Serialize(new { result = "No specific lesson sections found for query." });
                    return JsonSerializer.Serialize(new
                    {
                        result = hits.Select(h => new
                        {
                            unit = h.Unit,
                            section = h.SectionTitle,
                            content = h.Body.Length > 500 ? h.Body[..500] : h.Body
                        })
                    });
                }
                case "get_vocabulary_entry":
                {
                    string word = root.TryGetProperty("word", out var w) ? w.GetString() ?? "" : "";
                    var match = _lessons.Units.SelectMany(u => u.Vocabulary)
                        .FirstOrDefault(v => string.Equals(v.Word, word, StringComparison.OrdinalIgnoreCase) ||
                                             v.Word.Contains(word, StringComparison.OrdinalIgnoreCase));
                    if (match == null) return JsonSerializer.Serialize(new { result = $"Word '{word}' not found in dictionary." });
                    return JsonSerializer.Serialize(new
                    {
                        word = match.Word,
                        definition = match.Meaning,
                        form = match.Form,
                        ipa = match.Ipa,
                        example = match.Example
                    });
                }
                case "generate_interactive_exercise":
                {
                    string topic = root.TryGetProperty("topic", out var t) ? t.GetString() ?? "IELTS Grammar" : "IELTS Grammar";
                    string skill = root.TryGetProperty("skill", out var s) ? s.GetString() ?? "Reading" : "Reading";
                    string kind = root.TryGetProperty("kind", out var k) ? k.GetString() ?? "single" : "single";
                    
                    var hits = _lessons.Search(topic, max: 1);
                    string material = hits.Count > 0 ? hits[0].Body : "General IELTS Practice.";
                    string prompt = StudyPrompts.BuildInteractiveExercise(topic, skill, material, kind);
                    var res = await _llm.CompleteAsync(new[]
                    {
                        LlmMessage.System(StudyPrompts.InteractiveExerciseSystem),
                        LlmMessage.User(prompt)
                    }, ct).ConfigureAwait(false);

                    string exerciseJson = res.Success ? res.Text.Trim() : "{}";
                    int sIdx = exerciseJson.IndexOf('{');
                    int eIdx = exerciseJson.LastIndexOf('}');
                    if (sIdx >= 0 && eIdx > sIdx) exerciseJson = exerciseJson.Substring(sIdx, eIdx - sIdx + 1);

                    return exerciseJson;
                }
                default:
                    return JsonSerializer.Serialize(new { error = $"Unknown tool: {toolName}" });
            }
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    // ------------------------------------------------------------ units

    public StudyChatSnapshot ChatSnapshot(int sessionId = 0, string unitFilter = "", string query = "")
    {
        var units = UnitRows();
        var sessions = LoadSessions("Chat", query).ToList();

        // Create a first session only when the list is genuinely empty. When a
        // search filtered everything out, creating one here would add a stray
        // session on every keystroke and hide the "no match" state.
        if (sessions.Count == 0 && string.IsNullOrWhiteSpace(query))
        {
            var created = CreateSessionAsync("New study chat", "Chat", unitFilter, string.Empty).GetAwaiter().GetResult();
            sessions.Add(created);
        }

        var selected = sessions.FirstOrDefault(s => s.Id == sessionId);
        if (selected is null && sessionId > 0)
        {
            // The open session stays open even when the search filters its row
            // out, so the thread does not vanish while the student types.
            var entity = AppDbContext.FindAsync<StudySession>(sessionId).GetAwaiter().GetResult();
            if (entity is not null) selected = ToRow(entity);
        }
        selected ??= sessions.FirstOrDefault();

        var messages = selected is null ? new List<StudyMessageRow>() : LoadMessages(selected.Id);

        return new StudyChatSnapshot
        {
            Sessions = sessions,
            SelectedSessionId = selected?.Id ?? 0,
            SelectedTitle = selected?.Title ?? string.Empty,
            SelectedUnit = selected?.Unit ?? string.Empty,
            Messages = messages,
            Units = units,
            CanUseAi = CanUseAi,
            AiHint = AiHint,
            HasUnits = units.Count > 0,
            StatusMessage = units.Count == 0
                ? "No lesson content found. Run the lesson ingest tool, then reopen this screen."
                : string.Empty,
        };
    }

    public StudyPracticeSnapshot PracticeSnapshot(int sessionId = 0, int openSetId = 0, string scope = "All", string query = "")
    {
        var units = UnitRows();

        var setsAll = LoadAllSets();
        // Practice sessions only: a chat session has no sets and would otherwise
        // flood this list.
        var allSessions = LoadSessions("Practice", query).ToList();

        PracticeSetDetail? open = null;
        if (openSetId > 0)
        {
            open = LoadSet(openSetId);
        }
        else if (sessionId > 0)
        {
            var first = LoadSets(sessionId).FirstOrDefault();
            if (first is not null) open = LoadSet(first.Id);
        }

        if (open is not null)
        {
            var owningSet = LoadSetRow(open.Id);
            sessionId = owningSet?.SessionId ?? sessionId;
        }

        // Scope "All" lists every session that has a saved set. A skill scope
        // lists the sessions that built a set of that kind.
        var sessions = allSessions;
        if (scope != "All")
        {
            var ids = setsAll
                .Where(s => string.Equals(s.Skill, scope, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.SessionId)
                .ToHashSet();
            sessions = allSessions.Where(s => ids.Contains(s.Id)).ToList();
        }

        if (sessionId <= 0 && sessions.Count > 0) sessionId = sessions[0].Id;

        var sets = scope == "All"
            ? setsAll
            : setsAll.Where(s => string.Equals(s.Skill, scope, StringComparison.OrdinalIgnoreCase)).ToList();

        return new StudyPracticeSnapshot
        {
            Sessions = sessions,
            SelectedSessionId = sessionId,
            SelectedTitle = sessions.FirstOrDefault(s => s.Id == sessionId)?.Title ?? string.Empty,
            SelectedUnit = sessions.FirstOrDefault(s => s.Id == sessionId)?.Unit ?? string.Empty,
            Sets = sets,
            OpenSet = open,
            Units = units,
            Skills = new[] { "Grammar", "Vocabulary", "Reading", "Listening", "Writing", "Speaking" },
            Difficulties = new[] { "Easy", "Standard", "Hard" },
            CanUseAi = CanUseAi,
            AiHint = AiHint,
            Scope = scope,
        };
    }

    public StudyVocabSnapshot VocabSnapshot(string query = "", string unitFilter = "")
    {
        var rows = _lessons.Vocabulary(query, string.IsNullOrWhiteSpace(unitFilter) ? null : unitFilter)
            .Select(v => new VocabRow(
                v.Unit.Unit, v.Word.Word, v.Word.Form, v.Word.Meaning,
                v.Word.Example, v.Word.ExtraExample, v.Word.Ipa, v.Word.Derivatives))
            .ToList();

        return new StudyVocabSnapshot
        {
            Units = UnitRows(),
            UnitFilter = unitFilter,
            Query = query,
            Words = rows,
            Total = rows.Count,
        };
    }

    private List<StudyUnitRow> UnitRows() =>
        _lessons.Units
            .Select(u => new StudyUnitRow(
                u.Slug, u.DisplayTitle, u.Category, u.Sections.Count, u.Vocabulary.Count, u.PicturesSkipped,
                u.Topics, u.Skills))
            .ToList();

    // ------------------------------------------------------------ sessions

    /// <summary>
    /// Sessions of one kind, newest first, pinned before the rest. A null kind
    /// returns every session (chat and practice) so a screen can show them all,
    /// and a query filters by title or topic.
    /// </summary>
    private static List<StudySessionRow> LoadSessions(string? kind, string? query = null)
    {
        var rows = AppDbContext.TableAsync<StudySession>().ToListAsync().GetAwaiter().GetResult();

        IEnumerable<StudySession> filtered = rows;
        if (kind is not null)
            filtered = filtered.Where(s => s.Kind == kind);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            filtered = filtered.Where(s =>
                s.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.Topic.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return filtered
            .OrderByDescending(s => s.Pinned)
            .ThenByDescending(s => s.UpdatedAt)
            .Select(ToRow)
            .ToList();
    }

    private static StudySessionRow ToRow(StudySession s) =>
        new(s.Id, s.Title, s.Kind, s.Unit, s.Topic, s.ItemCount, s.Pinned, s.UpdatedAt);

    private static async Task<StudySessionRow> CreateSessionAsync(string title, string kind, string unit, string topic)
    {
        var session = new StudySession
        {
            Title = string.IsNullOrWhiteSpace(title) ? "New session" : title.Trim(),
            Kind = kind,
            Unit = unit ?? string.Empty,
            Topic = topic ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await AppDbContext.InsertAsync(session).ConfigureAwait(false);
        return ToRow(session);
    }

    public StudyChatSnapshot NewChatSession(string unit)
    {
        var title = string.IsNullOrWhiteSpace(unit) ? "New study chat" : $"{TitleForUnit(unit)} chat";
        var session = CreateSessionAsync(title, "Chat", unit ?? string.Empty, string.Empty).GetAwaiter().GetResult();
        return ChatSnapshot(session.Id);
    }

    /// <summary>
    /// Sets the unit a chat session searches in. Kept apart from the snapshot
    /// call so that searching the session list does not change the unit filter.
    /// </summary>
    public StudyChatSnapshot SetChatUnit(int sessionId, string unit)
    {
        var session = AppDbContext.FindAsync<StudySession>(sessionId).GetAwaiter().GetResult();
        if (session is not null)
        {
            session.Unit = unit ?? string.Empty;
            session.UpdatedAt = DateTime.UtcNow;
            AppDbContext.UpdateAsync(session).GetAwaiter().GetResult();
        }
        return ChatSnapshot(sessionId);
    }

    public StudyPracticeSnapshot NewPracticeSession(string unit, string topic, string scope = "All")
    {
        var title = string.IsNullOrWhiteSpace(topic)
            ? (string.IsNullOrWhiteSpace(unit) ? "New practice" : $"{TitleForUnit(unit)} practice")
            : topic;
        var session = CreateSessionAsync(title, "Practice", unit ?? string.Empty, topic ?? string.Empty).GetAwaiter().GetResult();
        return PracticeSnapshot(session.Id, 0, scope);
    }

    public StudyChatSnapshot RenameSession(int id, string title)
    {
        RenameSessionInternal(id, title);
        return ChatSnapshot(id);
    }

    public StudyPracticeSnapshot RenamePracticeSession(int id, string title, string scope = "All")
    {
        RenameSessionInternal(id, title);
        return PracticeSnapshot(id, 0, scope);
    }

    private static void RenameSessionInternal(int id, string title)
    {
        var session = AppDbContext.FindAsync<StudySession>(id).GetAwaiter().GetResult();
        if (session is not null)
        {
            session.Title = string.IsNullOrWhiteSpace(title) ? session.Title : title.Trim();
            session.UpdatedAt = DateTime.UtcNow;
            AppDbContext.UpdateAsync(session).GetAwaiter().GetResult();
        }
    }

    /// <summary>Toggles the pinned flag, so a session can be kept at the top.</summary>
    public StudyChatSnapshot PinSession(int id)
    {
        TogglePin(id);
        return ChatSnapshot(id);
    }

    public StudyPracticeSnapshot PinPracticeSession(int id, string scope = "All")
    {
        TogglePin(id);
        return PracticeSnapshot(id, 0, scope);
    }

    private static void TogglePin(int id)
    {
        var session = AppDbContext.FindAsync<StudySession>(id).GetAwaiter().GetResult();
        if (session is not null)
        {
            session.Pinned = !session.Pinned;
            AppDbContext.UpdateAsync(session).GetAwaiter().GetResult();
        }
    }

    public void DeleteSession(int id)
    {
        var messages = AppDbContext.TableAsync<ChatMessage>().Where(m => m.SessionId == id).ToListAsync().GetAwaiter().GetResult();
        foreach (var m in messages) AppDbContext.DeleteAsync(m).GetAwaiter().GetResult();

        var sets = AppDbContext.TableAsync<PracticeSet>().Where(s => s.SessionId == id).ToListAsync().GetAwaiter().GetResult();
        foreach (var set in sets)
        {
            var questions = AppDbContext.TableAsync<PracticeQuestion>().Where(q => q.SetId == set.Id).ToListAsync().GetAwaiter().GetResult();
            foreach (var q in questions) AppDbContext.DeleteAsync(q).GetAwaiter().GetResult();
            AppDbContext.DeleteAsync(set).GetAwaiter().GetResult();
        }

        var session = AppDbContext.FindAsync<StudySession>(id).GetAwaiter().GetResult();
        if (session is not null) AppDbContext.DeleteAsync(session).GetAwaiter().GetResult();
    }

    /// <summary>Deletes a session and returns the practice screen without it.</summary>
    public StudyPracticeSnapshot DeletePracticeSession(int id, string scope = "All")
    {
        DeleteSession(id);
        return PracticeSnapshot(0, 0, scope);
    }

    private static List<StudyMessageRow> LoadMessages(int sessionId)
    {
        var rows = AppDbContext.TableAsync<ChatMessage>()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Id)
            .ToListAsync()
            .GetAwaiter().GetResult();

        return rows.Select(m =>
        {
            string text = m.Text;
            string exerciseJson = "";
            int start = text.IndexOf("<!-- EXERCISE -->", StringComparison.Ordinal);
            int end = text.IndexOf("<!-- /EXERCISE -->", StringComparison.Ordinal);
            if (start >= 0 && end > start)
            {
                exerciseJson = text.Substring(start + 17, end - (start + 17)).Trim();
                text = (text[..start] + text[(end + 18)..]).Trim();
            }
            return new StudyMessageRow(
                m.Id, m.Role, text, ParseSources(m.SourcesJson), m.CreatedAt, exerciseJson);
        }).ToList();
    }

    public sealed record CheckAnswerResult(bool IsCorrect, double Score, string Explanation, string ModelAnswer);
    public sealed record CurriculumUnitSummary(
        string Slug, string Title, string Category,
        IReadOnlyList<string> Topics, IReadOnlyList<string> Skills,
        int SectionCount, int VocabCount,
        string MainTheme = "", string GrammarFocus = "");

    public IReadOnlyList<CurriculumUnitSummary> CurriculumOverview()
    {
        return _lessons.Units.Select(u => new CurriculumUnitSummary(
            u.Slug, u.DisplayTitle, u.Category, u.Topics, u.Skills, u.Sections.Count, u.Vocabulary.Count,
            u.MainTheme, u.GrammarFocus
        )).ToList();
    }

    public CheckAnswerResult CheckExerciseAnswer(string questionJson, string userAnswer)
    {
        userAnswer = (userAnswer ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(questionJson))
            return new CheckAnswerResult(false, 0, "Question data missing.", "");

        try
        {
            using var doc = JsonDocument.Parse(questionJson);
            var root = doc.RootElement;
            string kind = root.TryGetProperty("kind", out var k) ? k.GetString() ?? "single" : "single";
            string correctKey = root.TryGetProperty("correctKey", out var ck) ? ck.GetString() ?? "" : "";
            string gapAnswer = root.TryGetProperty("gapAnswer", out var ga) ? ga.GetString() ?? "" : "";
            string explanation = root.TryGetProperty("explanation", out var ex) ? ex.GetString() ?? "" : "";

            bool isCorrect = false;
            double score = 0;
            string modelAnswer = "";

            switch (kind.ToLowerInvariant())
            {
                case "single":
                case "tfng":
                    isCorrect = string.Equals(userAnswer, correctKey.Trim(), StringComparison.OrdinalIgnoreCase);
                    score = isCorrect ? 1.0 : 0.0;
                    modelAnswer = correctKey;
                    break;
                case "gap":
                case "completion":
                    var accepted = gapAnswer.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToLowerInvariant()).ToList();
                    isCorrect = accepted.Contains(userAnswer.ToLowerInvariant());
                    score = isCorrect ? 1.0 : 0.0;
                    modelAnswer = gapAnswer;
                    break;
                case "reorder":
                    modelAnswer = correctKey.Length > 0 ? correctKey : gapAnswer;
                    isCorrect = string.Equals(userAnswer.Replace(" ", ""), modelAnswer.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
                    score = isCorrect ? 1.0 : 0.0;
                    break;
                case "error_fix":
                case "paraphrase":
                    modelAnswer = gapAnswer.Length > 0 ? gapAnswer : explanation;
                    isCorrect = userAnswer.Length >= 5;
                    score = isCorrect ? 1.0 : 0.5;
                    break;
                default:
                    isCorrect = string.Equals(userAnswer, correctKey, StringComparison.OrdinalIgnoreCase);
                    score = isCorrect ? 1.0 : 0.0;
                    modelAnswer = correctKey;
                    break;
            }

            return new CheckAnswerResult(isCorrect, score, explanation, modelAnswer);
        }
        catch (Exception ex)
        {
            return new CheckAnswerResult(false, 0, "Error evaluating: " + ex.Message, "");
        }
    }

    public async Task<StudyChatSnapshot> TeachTopicAsync(
        int sessionId, string unitSlug, string topic, CancellationToken ct = default)
    {
        if (sessionId <= 0)
        {
            var s = NewChatSession(unitSlug);
            sessionId = s.SelectedSessionId;
        }

        var unit = _lessons.GetUnit(unitSlug) ?? _lessons.Units.FirstOrDefault();
        string material = "";
        if (unit != null)
        {
            var relevantSections = unit.Sections
                .Where(s => string.Equals(s.Topic, topic, StringComparison.OrdinalIgnoreCase) || s.Title.Contains(topic, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            if (relevantSections.Count == 0) relevantSections = unit.Sections.Take(2).ToList();
            material = string.Join("\n\n", relevantSections.Select(s => s.Title + ":\n" + s.FlatText));
        }

        string prompt = $"Teach the student about topic '{topic}' based on the authentic IELTS curriculum below.\n" +
            "Provide 3-4 clear key learning points with examples, and end with an encouraging prompt to practice.\n\n" +
            "Curriculum Material:\n" + material;

        var messages = new List<LlmMessage>
        {
            LlmMessage.System(StudyPrompts.ChatSystem),
            LlmMessage.User(prompt),
        };

        string explanation;
        if (_llm.UseStreaming)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                await foreach (var token in _llm.StreamAsync(messages, ct).ConfigureAwait(false))
                {
                    sb.Append(token);
                    _broadcaster?.Invoke("study.chat.chunk", new
                    {
                        sessionId,
                        delta = token,
                        isDone = false
                    });
                }
                explanation = sb.Length > 0 ? sb.ToString().Trim() : "Here is the key material for " + topic + ":\n" + material;
            }
            catch (Exception ex)
            {
                explanation = $"The model could not answer: {ex.Message}";
            }
        }
        else
        {
            var result = await _llm.CompleteAsync(messages, ct).ConfigureAwait(false);
            explanation = result.Success ? result.Text.Trim() : "Here is the key material for " + topic + ":\n" + material;
        }

        var assistantMsg = new ChatMessage
        {
            SessionId = sessionId,
            Role = "assistant",
            Text = explanation,
            CreatedAt = DateTime.UtcNow,
        };
        await AppDbContext.InsertAsync(assistantMsg).ConfigureAwait(false);

        _broadcaster?.Invoke("study.chat.chunk", new
        {
            sessionId,
            messageId = assistantMsg.Id,
            delta = "",
            isDone = true
        });

        await GenerateInteractiveExerciseAsync(sessionId, "Reading", topic, "single", unitSlug, ct).ConfigureAwait(false);
        return ChatSnapshot(sessionId, unitSlug);
    }

    public async Task<StudyChatSnapshot> GenerateInteractiveExerciseAsync(
        int sessionId, string skill, string topic, string kind, string unitSlug, CancellationToken ct = default)
    {
        if (sessionId <= 0)
        {
            var s = NewChatSession(unitSlug);
            sessionId = s.SelectedSessionId;
        }

        var unit = _lessons.GetUnit(unitSlug) ?? _lessons.Units.FirstOrDefault();
        string material = "";
        if (unit != null)
        {
            var sec = unit.Sections.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.FlatText));
            material = sec?.FlatText ?? "";
        }
        if (string.IsNullOrWhiteSpace(material))
        {
            var hits = _lessons.Search(topic, max: 2);
            material = hits.Count > 0 ? hits[0].Body : "General IELTS practice.";
        }

        string prompt = StudyPrompts.BuildInteractiveExercise(topic, skill, material, kind);
        var messages = new List<LlmMessage>
        {
            LlmMessage.System(StudyPrompts.InteractiveExerciseSystem),
            LlmMessage.User(prompt),
        };

        var result = await _llm.CompleteAsync(messages, ct).ConfigureAwait(false);
        string json = result.Success ? result.Text.Trim() : "";
        int sIdx = json.IndexOf('{');
        int eIdx = json.LastIndexOf('}');
        if (sIdx >= 0 && eIdx > sIdx)
        {
            json = json.Substring(sIdx, eIdx - sIdx + 1);
        }

        string messageText = "<!-- EXERCISE -->" + json + "<!-- /EXERCISE -->";

        await AppDbContext.InsertAsync(new ChatMessage
        {
            SessionId = sessionId,
            Role = "assistant",
            Text = messageText,
            CreatedAt = DateTime.UtcNow,
        }).ConfigureAwait(false);

        return ChatSnapshot(sessionId, unitSlug);
    }

    private static IReadOnlyList<StudySource> ParseSources(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<StudySource>();
        try
        {
            return JsonSerializer.Deserialize<List<StudySource>>(json, Json) ?? new List<StudySource>();
        }
        catch (JsonException)
        {
            return Array.Empty<StudySource>();
        }
    }

    // ------------------------------------------------------------ chat

    public async Task<StudyChatSnapshot> AskAsync(
        int sessionId, string question, string unitFilter, CancellationToken ct = default)
    {
        question = (question ?? string.Empty).Trim();
        if (question.Length == 0) return ChatSnapshot(sessionId, unitFilter);

        var session = await AppDbContext.FindAsync<StudySession>(sessionId).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The session is gone.");

        await AppDbContext.InsertAsync(new ChatMessage
        {
            SessionId = sessionId,
            Role = "user",
            Text = question,
            CreatedAt = DateTime.UtcNow,
        }).ConfigureAwait(false);

        if (!CanUseAi)
        {
            await AppDbContext.InsertAsync(new ChatMessage
            {
                SessionId = sessionId,
                Role = "assistant",
                Text = "The tutor needs a language model. Add one in Settings, then ask again. " +
                       "You can still browse the vocabulary and read the lesson material offline.",
                CreatedAt = DateTime.UtcNow,
            }).ConfigureAwait(false);
            return ChatSnapshot(sessionId, unitFilter);
        }

        var hits = _lessons.Search(question, max: 5,
            unitSlug: string.IsNullOrWhiteSpace(unitFilter) ? null : unitFilter);

        var sources = hits.Select(h => new StudySource(h.Unit, h.SectionTitle, Shorten(h.Snippet, 240))).ToList();
        var sourceText = BuildSourceText(hits);

        // The last few turns go with the question, so a follow up like "and the
        // second one?" still has what it refers to. The question was just saved,
        // so drop that copy or it would be sent twice.
        var history = LoadMessages(sessionId)
            .Where(m => !string.IsNullOrWhiteSpace(m.Text))
            .ToList();
        if (history.Count > 0
            && history[^1].Role == "user"
            && string.Equals(history[^1].Text.Trim(), question, StringComparison.Ordinal))
        {
            history.RemoveAt(history.Count - 1);
        }
        var recent = history.TakeLast(8).ToList();
        var messages = BuildChatMessages(question, sourceText, recent);

        string responseText;
        Action<string, string> onToolInvoked = (toolName, _) =>
        {
            _broadcaster?.Invoke("study.chat.chunk", new
            {
                sessionId,
                delta = string.Empty,
                toolName,
                isDone = false
            });
        };

        if (_llm.UseStreaming)
        {
            Action<string> onTokenChunk = (chunk) =>
            {
                _broadcaster?.Invoke("study.chat.chunk", new
                {
                    sessionId,
                    delta = chunk,
                    isDone = false
                });
            };

            var result = await _llm.CompleteWithToolsAsync(
                messages,
                TutorTools,
                ExecuteTutorToolAsync,
                onToolInvoked: onToolInvoked,
                onTokenChunk: onTokenChunk,
                ct: ct).ConfigureAwait(false);

            responseText = result.Success ? result.Text.Trim() : DescribeLlmError(result);
        }
        else
        {
            var result = await _llm.CompleteWithToolsAsync(
                messages,
                TutorTools,
                ExecuteTutorToolAsync,
                onToolInvoked: onToolInvoked,
                ct: ct).ConfigureAwait(false);

            responseText = result.Success ? result.Text.Trim() : DescribeLlmError(result);
        }

        var assistantMsg = new ChatMessage
        {
            SessionId = sessionId,
            Role = "assistant",
            Text = responseText,
            SourcesJson = JsonSerializer.Serialize(sources, Json),
            CreatedAt = DateTime.UtcNow,
        };
        await AppDbContext.InsertAsync(assistantMsg).ConfigureAwait(false);

        _broadcaster?.Invoke("study.chat.chunk", new
        {
            sessionId,
            messageId = assistantMsg.Id,
            delta = "",
            isDone = true,
            sources
        });

        session.Topic = question.Length > 80 ? question[..80] : question;
        if (string.IsNullOrWhiteSpace(session.Unit) && hits.Count > 0) session.Unit = hits[0].Unit;
        if (session.Title.StartsWith("New ", StringComparison.OrdinalIgnoreCase))
        {
            var words = question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6);
            session.Title = string.Join(' ', words);
        }
        session.ItemCount += 1;
        session.UpdatedAt = DateTime.UtcNow;
        await AppDbContext.UpdateAsync(session).ConfigureAwait(false);

        return ChatSnapshot(sessionId, unitFilter);
    }

    private static string DescribeLlmError(LlmResult result) =>
        string.IsNullOrWhiteSpace(result.Error)
            ? "The model did not answer. Check the model in Settings and try again."
            : $"The model could not answer: {result.Error}";

    /// <summary>
    /// The full message list for one chat turn: the system rule, the recent
    /// history so a follow up question has its context, then the question with
    /// the lesson sources. Built here, not in the call, so the wiring can be
    /// checked without a live model.
    /// </summary>
    public static List<LlmMessage> BuildChatMessages(
        string question, string sourceText, IReadOnlyList<StudyMessageRow> history)
    {
        var messages = new List<LlmMessage> { LlmMessage.System(StudyPrompts.ChatSystem) };
        foreach (var m in history)
        {
            messages.Add(m.Role == "assistant"
                ? LlmMessage.Assistant(Shorten(m.Text, 1200))
                : LlmMessage.User(Shorten(m.Text, 1200)));
        }
        messages.Add(LlmMessage.User(StudyPrompts.BuildChat(question, sourceText)));
        return messages;
    }

    /// <summary>
    /// The sources block sent to the model, built from the search hits. Each hit
    /// sends the real section body, so the tutor answers from the lesson and not
    /// from a short preview of it.
    /// </summary>
    public static string BuildSourceText(IReadOnlyList<LessonHit> hits)
    {
        if (hits.Count == 0) return string.Empty;
        var builder = new StringBuilder();
        foreach (var hit in hits)
        {
            builder.AppendLine($"- {hit.Unit} / {hit.SectionTitle}:");
            builder.AppendLine(Shorten(hit.Body, 1800));
        }
        return builder.ToString();
    }

    /// <summary>
    /// The lesson text a practice set is built from, for one skill and optional topic.
    /// When a topic is selected, sections belonging to that topic are prioritized.
    /// When an answer key exists for a section, both the task and the key are included
    /// so the generated questions are grounded in real IELTS material.
    /// </summary>
    public static string BuildPracticeMaterial(
        LessonService lessons, LessonUnit? lesson, string? unitSlug, string skill, string? topic = null)
    {
        var material = new StringBuilder();
        if (lesson is not null)
        {
            var sections = lesson.Sections.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(topic) && !topic.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                var matched = sections.Where(s => string.Equals(s.Topic, topic, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matched.Count > 0) sections = matched;
            }

            foreach (var section in sections)
            {
                if (section.FlatText.Length == 0) continue;

                bool skillMatch = string.IsNullOrWhiteSpace(skill)
                    || section.Skill.Equals(skill, StringComparison.OrdinalIgnoreCase)
                    || section.Skill.Equals("Lesson", StringComparison.OrdinalIgnoreCase)
                    || skill == "Vocabulary";

                if (section.IsAnswerKey && !string.IsNullOrWhiteSpace(skill) && !section.Skill.Equals(skill, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!section.IsAnswerKey && !skillMatch)
                    continue;

                material.AppendLine(section.IsAnswerKey
                    ? $"## {section.Title} (tasks and answers)"
                    : $"## {section.Title}");
                if (!string.IsNullOrWhiteSpace(section.Topic))
                {
                    material.AppendLine($"Topic: {section.Topic}");
                }
                material.AppendLine(Shorten(section.FlatText, 1800));
                material.AppendLine();
                if (material.Length > 9000) break;
            }

            if (skill == "Vocabulary" || material.Length < 3000)
            {
                foreach (var (_, w) in lessons.Vocabulary(null, unitSlug, 40))
                {
                    material.AppendLine($"{w.Word} ({w.Form}) = {w.Meaning}. {w.Example}");
                }
            }
        }

        if (material.Length == 0)
        {
            foreach (var (_, w) in lessons.Vocabulary(null, unitSlug, 60))
            {
                material.AppendLine($"{w.Word} ({w.Form}) = {w.Meaning}. {w.Example}");
            }
        }

        return material.ToString();
    }

    private static string Shorten(string text, int max) =>
        text.Length <= max ? text : text[..max] + "...";

    // ------------------------------------------------------------ practice

    public async Task<StudyPracticeSnapshot> BuildPracticeAsync(
        int sessionId, string unit, string skill, string difficulty, int count, string topic = "", string scope = "All", string mode = "auto", CancellationToken ct = default)
    {
        count = Math.Clamp(count, 1, 20);

        var unitSlug = string.IsNullOrWhiteSpace(unit) ? null : unit;
        var lesson = unitSlug is null ? null : _lessons.GetUnit(unitSlug);

        var topicLabel = !string.IsNullOrWhiteSpace(topic) && !topic.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? topic
            : (lesson?.Title ?? (string.IsNullOrWhiteSpace(unit) ? "mixed IELTS practice" : unit));

        // Offline / Lesson mode: instant practice set without calling LLM
        if (string.Equals(mode, "lesson", StringComparison.OrdinalIgnoreCase) || (!CanUseAi && !string.Equals(mode, "ai", StringComparison.OrdinalIgnoreCase)))
        {
            var offlineSet = BuildOfflinePracticeSet(_lessons, unitSlug, skill, topic, count, difficulty);
            if (offlineSet is not null && offlineSet.Questions.Count > 0)
            {
                var id = await SavePracticeAsync(sessionId, offlineSet, skill, unit ?? string.Empty, topicLabel, lesson?.Source ?? "Lesson Material")
                    .ConfigureAwait(false);
                return PracticeSnapshot(sessionId, id, scope) with
                {
                    StatusMessage = $"Created practice set with {offlineSet.Questions.Count} question(s) from lesson content."
                };
            }

            if (!CanUseAi)
            {
                return PracticeSnapshot(sessionId, 0, scope) with
                {
                    StatusMessage = AiHint,
                };
            }
        }

        if (!CanUseAi)
        {
            return PracticeSnapshot(sessionId, 0, scope) with
            {
                StatusMessage = AiHint,
            };
        }

        var material = BuildPracticeMaterial(_lessons, lesson, unitSlug, skill, topic);

        var messages = new List<LlmMessage>
        {
            LlmMessage.System(StudyPrompts.PracticeSystem),
            LlmMessage.User(StudyPrompts.BuildPractice(topicLabel, skill, material, count, difficulty)),
        };

        var result = await _llm.CompleteAsync(messages, ct).ConfigureAwait(false);

        // One retry with a blunt reminder if the model failed JSON schema
        if (result.Success && !TryParsePractice(result.Text, count, out var retryParsed))
        {
            var retry = messages
                .Concat(new[]
                {
                    LlmMessage.Assistant(Shorten(result.Text, 800)),
                    LlmMessage.User("The response was not valid question JSON. Reply with ONLY the raw JSON object, without markdown code fences, comments, or conversational text.")
                })
                .ToList();
            var second = await _llm.CompleteAsync(retry, ct).ConfigureAwait(false);
            if (second.Success && TryParsePractice(second.Text, count, out retryParsed))
                result = second;
        }

        int setId = 0;
        string status;
        if (result.Success && TryParsePractice(result.Text, count, out var parsed))
        {
            setId = await SavePracticeAsync(sessionId, parsed, skill, unit ?? string.Empty, topic, lesson?.Source ?? string.Empty)
                .ConfigureAwait(false);
            status = $"Built {parsed.Questions.Count} question(s).";
        }
        else
        {
            // Graceful fallback to authentic offline questions if LLM failed
            var fallbackSet = BuildOfflinePracticeSet(_lessons, unitSlug, skill, topic, count, difficulty);
            if (fallbackSet is not null && fallbackSet.Questions.Count > 0)
            {
                setId = await SavePracticeAsync(sessionId, fallbackSet, skill, unit ?? string.Empty, topicLabel, lesson?.Source ?? "Lesson Material")
                    .ConfigureAwait(false);
                status = $"AI generation could not complete. Loaded {fallbackSet.Questions.Count} question(s) from lesson content instead.";
            }
            else
            {
                status = result.Success
                    ? "The model reply could not be read as questions. Try again or use Quick Quiz."
                    : DescribeLlmError(result);
            }
        }

        return PracticeSnapshot(sessionId, setId, scope) with { StatusMessage = status };
    }

    /// <summary>
    /// Builds an authentic practice set directly from the lesson text and vocabulary without needing an LLM.
    /// </summary>
    private static ParsedSet BuildOfflinePracticeSet(
        LessonService lessons, string? unitSlug, string skill, string? topic, int count, string difficulty)
    {
        var set = new ParsedSet();
        var lesson = string.IsNullOrWhiteSpace(unitSlug) ? null : lessons.GetUnit(unitSlug);
        var unitTitle = lesson?.Title ?? (string.IsNullOrWhiteSpace(unitSlug) ? "IELTS" : unitSlug);
        set.Title = $"{unitTitle} {skill} Practice";

        count = Math.Clamp(count, 1, 20);
        var questions = new List<ParsedQuestion>();

        // 1. Reading and Grammar section question extraction
        if (lesson is not null && (skill.Equals("Reading", StringComparison.OrdinalIgnoreCase) || skill.Equals("Grammar", StringComparison.OrdinalIgnoreCase)))
        {
            var sections = lesson.Sections.Where(s => !s.IsAnswerKey && s.FlatText.Length > 0);
            if (!string.IsNullOrWhiteSpace(topic) && !topic.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                var matched = sections.Where(s => string.Equals(s.Topic, topic, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matched.Count > 0) sections = matched;
            }

            foreach (var sec in sections)
            {
                var lines = sec.FlatText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var line in lines)
                {
                    if ((line.Contains("___") || Regex.IsMatch(line, @"^\d+[\.\)]\s+")) && line.Length > 15 && line.Length < 350)
                    {
                        var q = new ParsedQuestion
                        {
                            Kind = line.Contains("___") ? "gap" : "short",
                            Prompt = line,
                            Explanation = $"Derived from lesson section: {sec.Title}.",
                        };
                        questions.Add(q);
                        if (questions.Count >= count) break;
                    }
                }
                if (questions.Count >= count) break;
            }
        }

        // 2. Vocabulary-based questions (consistent, accurate, and completely offline)
        var vocabList = lessons.Vocabulary(null, unitSlug, 60).Select(x => x.Word).ToList();
        if (vocabList.Count >= 2 && questions.Count < count)
        {
            var rnd = new Random();
            var shuffled = vocabList.OrderBy(_ => rnd.Next()).ToList();

            foreach (var w in shuffled)
            {
                if (questions.Count >= count) break;

                if (!string.IsNullOrWhiteSpace(w.Example) && w.Example.Contains(w.Word, StringComparison.OrdinalIgnoreCase) && rnd.Next(2) == 0)
                {
                    var pattern = Regex.Escape(w.Word);
                    var blanked = Regex.Replace(w.Example, pattern, "___", RegexOptions.IgnoreCase);
                    questions.Add(new ParsedQuestion
                    {
                        Kind = "gap",
                        Prompt = $"Complete the sentence with the correct vocabulary word:\n\"{blanked}\"",
                        GapAnswer = w.Word,
                        Explanation = $"\"{w.Word}\" ({w.Form}) means: {w.Meaning}. Example: {w.Example}",
                    });
                }
                else
                {
                    var distractors = shuffled.Where(o => o.Word != w.Word && !string.IsNullOrWhiteSpace(o.Meaning))
                        .Take(3).Select(o => o.Meaning).ToList();
                    if (distractors.Count >= 3)
                    {
                        var options = new List<string> { w.Meaning };
                        options.AddRange(distractors);
                        options = options.OrderBy(_ => rnd.Next()).ToList();

                        char correctLetter = 'A';
                        var optList = new List<PracticeOption>();
                        for (int i = 0; i < options.Count; i++)
                        {
                            char letter = (char)('A' + i);
                            if (options[i] == w.Meaning) correctLetter = letter;
                            optList.Add(new PracticeOption(letter.ToString(), options[i]));
                        }

                        questions.Add(new ParsedQuestion
                        {
                            Kind = "single",
                            Prompt = $"What is the meaning of the word \"{w.Word}\" ({w.Form})?",
                            Options = optList,
                            CorrectKey = correctLetter.ToString(),
                            Explanation = $"\"{w.Word}\" ({w.Form}): {w.Meaning}. Example: {w.Example}",
                        });
                    }
                }
            }
        }

        set.Questions = questions;
        return set;
    }

    private sealed class ParsedSet
    {
        public string Title { get; set; } = string.Empty;
        public List<ParsedQuestion> Questions { get; set; } = new();
    }

    private sealed class ParsedQuestion
    {
        public string Kind { get; set; } = "gap";
        public string Prompt { get; set; } = string.Empty;
        public List<PracticeOption> Options { get; set; } = new();
        public List<ParsedMatchRow> Rows { get; set; } = new();
        public string CorrectKey { get; set; } = string.Empty;
        public string GapAnswer { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
    }

    private sealed class ParsedMatchRow
    {
        public string Label { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
    }

    private static bool TryParsePractice(string text, int wanted, out ParsedSet parsed)
    {
        parsed = new ParsedSet();
        var json = ExtractJsonObject(text);
        if (json is null) return false;

        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 64
        };

        try
        {
            using var doc = JsonDocument.Parse(json, options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            parsed.Title = Str(root, "title");

            if (!root.TryGetProperty("questions", out var questions)
                || questions.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var list = new List<ParsedQuestion>();
            foreach (var element in questions.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;

                var q = new ParsedQuestion
                {
                    Kind = Str(element, "kind"),
                    Prompt = Str(element, "prompt"),
                    CorrectKey = Str(element, "correctKey"),
                    GapAnswer = Str(element, "gapAnswer"),
                    Explanation = Str(element, "explanation"),
                };
                if (string.IsNullOrWhiteSpace(q.Prompt)) continue;

                if (element.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                {
                    int index = 0;
                    foreach (var option in opts.EnumerateArray())
                    {
                        if (option.ValueKind == JsonValueKind.Object)
                        {
                            var key = Str(option, "key");
                            q.Options.Add(new PracticeOption(key.Length > 0 ? key : Letter(index), Str(option, "text")));
                        }
                        else if (option.ValueKind == JsonValueKind.String)
                        {
                            var strVal = option.GetString() ?? string.Empty;
                            var m = Regex.Match(strVal, @"^([A-Z])[\.\)\:\-]\s*(.*)$");
                            if (m.Success)
                            {
                                q.Options.Add(new PracticeOption(m.Groups[1].Value, m.Groups[2].Value.Trim()));
                            }
                            else
                            {
                                q.Options.Add(new PracticeOption(Letter(index), strVal));
                            }
                        }
                        index++;
                    }
                }

                if (element.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in rows.EnumerateArray())
                    {
                        if (row.ValueKind != JsonValueKind.Object) continue;
                        q.Rows.Add(new ParsedMatchRow { Label = Str(row, "label"), Answer = Str(row, "answer") });
                    }
                }

                if (NormalizeKind(q.Kind) == "match" && q.Rows.Count == 0) continue;
                if (NormalizeKind(q.Kind) == "single" && q.Options.Count == 0 && string.IsNullOrWhiteSpace(q.CorrectKey)) continue;

                list.Add(q);
                if (list.Count >= wanted) break;
            }

            if (list.Count == 0) return false;
            parsed.Questions = list;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Str(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty,
        };
    }

    private static string Letter(int index) =>
        index is >= 0 and < 26 ? ((char)('A' + index)).ToString() : (index + 1).ToString();

    /// <summary>Extracts the JSON object from raw LLM text, handling code blocks, whitespace, and balanced braces.</summary>
    private static string? ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var cleaned = text.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstLine = cleaned.IndexOf('\n');
            if (firstLine > 0)
            {
                var lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence > firstLine)
                {
                    cleaned = cleaned.Substring(firstLine + 1, lastFence - firstLine - 1).Trim();
                }
            }
        }

        int start = cleaned.IndexOf('{');
        if (start < 0) return null;

        int depth = 0;
        bool inString = false;
        bool escape = false;
        int end = -1;

        for (int i = start; i < cleaned.Length; i++)
        {
            char c = cleaned[i];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (!inString)
            {
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }
        }

        if (end > start)
        {
            return cleaned.Substring(start, end - start + 1);
        }

        int lastClose = cleaned.LastIndexOf('}');
        if (lastClose > start)
        {
            return cleaned.Substring(start, lastClose - start + 1);
        }

        return null;
    }

    private static async Task<int> SavePracticeAsync(
        int sessionId, ParsedSet set, string skill, string unit, string topic, string source)
    {
        if (sessionId <= 0)
        {
            var session = await CreateSessionAsync($"{topic} practice", "Practice", unit, topic).ConfigureAwait(false);
            sessionId = session.Id;
        }

        var record = new PracticeSet
        {
            SessionId = sessionId,
            Title = string.IsNullOrWhiteSpace(set.Title) ? topic : set.Title.Trim(),
            Skill = skill,
            Unit = unit,
            Source = source,
            QuestionCount = set.Questions.Count,
            Total = set.Questions.Count,
            Status = "Open",
            CreatedAt = DateTime.UtcNow,
        };
        await AppDbContext.InsertAsync(record).ConfigureAwait(false);

        int number = 1;
        foreach (var q in set.Questions)
        {
            await AppDbContext.InsertAsync(new PracticeQuestion
            {
                SetId = record.Id,
                Number = number++,
                Kind = NormalizeKind(q.Kind),
                Prompt = q.Prompt.Trim(),
                OptionsJson = q.Options.Count > 0 ? JsonSerializer.Serialize(q.Options, Json) : string.Empty,
                MatchRowsJson = q.Rows.Count > 0
                    ? JsonSerializer.Serialize(q.Rows.Select(r => new PracticeMatchRow(r.Label, r.Answer)).ToList(), Json)
                    : string.Empty,
                CorrectKey = (q.CorrectKey ?? string.Empty).Trim(),
                GapAnswer = (q.GapAnswer ?? string.Empty).Trim(),
                Explanation = (q.Explanation ?? string.Empty).Trim(),
            }).ConfigureAwait(false);
        }

        var session2 = await AppDbContext.FindAsync<StudySession>(sessionId).ConfigureAwait(false);
        if (session2 is not null)
        {
            session2.ItemCount += 1;
            session2.UpdatedAt = DateTime.UtcNow;
            await AppDbContext.UpdateAsync(session2).ConfigureAwait(false);
        }

        return record.Id;
    }

    private static string NormalizeKind(string kind) =>
        (kind ?? "gap").Trim().ToLowerInvariant() switch
        {
            "single" or "single_choice" or "choice" => "single",
            "multiple" or "multiple_choice" => "multiple",
            "short" or "short_answer" => "short",
            "match" or "matching" => "match",
            "table" or "table_completion" or "completion" or "note_completion" or "note" => "completion",
            _ => "gap",
        };

    private static IReadOnlyList<PracticeMatchRow> ParseMatchRows(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<PracticeMatchRow>();
        try
        {
            return JsonSerializer.Deserialize<List<PracticeMatchRow>>(json, Json) ?? new List<PracticeMatchRow>();
        }
        catch (JsonException)
        {
            return Array.Empty<PracticeMatchRow>();
        }
    }

    /// <summary>Every saved set, newest first, for the "all sessions" view.</summary>
    private static List<PracticeSetRow> LoadAllSets()
    {
        var rows = AppDbContext.TableAsync<PracticeSet>().ToListAsync().GetAwaiter().GetResult();
        return rows
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new PracticeSetRow(
                s.Id, s.SessionId, s.Title, s.Skill, s.Unit, s.Status, s.Score, s.Total, s.QuestionCount, s.CreatedAt))
            .ToList();
    }

    /// <summary>Opens a saved set by id, so the detail can be reopened from any screen.</summary>
    public StudyPracticeSnapshot OpenSet(int setId, string scope = "All")
    {
        var set = LoadSetRow(setId);
        var sessionId = set?.SessionId ?? 0;
        return PracticeSnapshot(sessionId, setId, scope);
    }

    private static List<PracticeSetRow> LoadSets(int sessionId)
    {
        var rows = sessionId > 0
            ? AppDbContext.TableAsync<PracticeSet>().Where(s => s.SessionId == sessionId).ToListAsync().GetAwaiter().GetResult()
            : AppDbContext.TableAsync<PracticeSet>().ToListAsync().GetAwaiter().GetResult();

        return rows
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new PracticeSetRow(
                s.Id, s.SessionId, s.Title, s.Skill, s.Unit, s.Status, s.Score, s.Total, s.QuestionCount, s.CreatedAt))
            .ToList();
    }

    private static PracticeSet? LoadSetRow(int setId) =>
        AppDbContext.FindAsync<PracticeSet>(setId).GetAwaiter().GetResult();

    private static PracticeSetDetail? LoadSet(int setId)
    {
        var set = LoadSetRow(setId);
        if (set is null) return null;

        var questions = AppDbContext.TableAsync<PracticeQuestion>()
            .Where(q => q.SetId == setId)
            .OrderBy(q => q.Number)
            .ToListAsync()
            .GetAwaiter().GetResult();

        var rows = questions.Select(q => new PracticeQuestionRow(
            q.Id, q.Number, q.Kind, q.Prompt, q.Options, ParseMatchRows(q.MatchRowsJson), q.CorrectKey, q.GapAnswer,
            q.Explanation, q.UserAnswer, q.IsCorrect, q.IsFlagged)).ToList();

        return new PracticeSetDetail(
            set.Id, set.Title, set.Skill, set.Unit, set.Source, set.Status,
            set.Score, set.Total, rows);
    }

    /// <summary>Saves one answer and scores it right away, like the exam engine.</summary>
    public StudyPracticeSnapshot Answer(int setId, int questionId, string answer, bool toggleFlag, string scope = "All")
    {
        var question = AppDbContext.FindAsync<PracticeQuestion>(questionId).GetAwaiter().GetResult();
        if (question is null) return PracticeSnapshot(0, setId, scope);

        if (toggleFlag)
        {
            question.IsFlagged = !question.IsFlagged;
        }
        else
        {
            question.UserAnswer = answer ?? string.Empty;
            question.IsCorrect = IsCorrect(question, answer);
        }
        AppDbContext.UpdateAsync(question).GetAwaiter().GetResult();

        return PracticeSnapshot(LoadSetRow(setId)?.SessionId ?? 0, setId, scope);
    }

    /// <summary>Scores the whole set and marks it submitted.</summary>
    public StudyPracticeSnapshot SubmitSet(int setId, string scope = "All")
    {
        var set = LoadSetRow(setId);
        if (set is null) return PracticeSnapshot(0, 0, scope);

        var questions = AppDbContext.TableAsync<PracticeQuestion>()
            .Where(q => q.SetId == setId).ToListAsync().GetAwaiter().GetResult();

        int score = 0;
        foreach (var q in questions)
        {
            q.IsCorrect = IsCorrect(q, q.UserAnswer);
            if (q.IsCorrect) score++;
            AppDbContext.UpdateAsync(q).GetAwaiter().GetResult();
        }

        set.Score = score;
        set.Total = questions.Count;
        set.Status = "Submitted";
        AppDbContext.UpdateAsync(set).GetAwaiter().GetResult();

        return PracticeSnapshot(set.SessionId, setId, scope);
    }

    private static bool IsCorrect(PracticeQuestion q, string? answer)
    {
        answer = (answer ?? string.Empty).Trim();
        if (answer.Length == 0) return false;

        switch (NormalizeKind(q.Kind))
        {
            case "multiple":
                var chosen = answer.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Norm).Where(s => s.Length > 0).OrderBy(s => s);
                var expected = (q.CorrectKey ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Norm).Where(s => s.Length > 0).OrderBy(s => s);
                return chosen.SequenceEqual(expected);

            case "match":
                var rows = ParseMatchRows(q.MatchRowsJson);
                if (rows.Count == 0) return false;
                var given = ParseMatchAnswer(answer);
                // Every row must be placed and match, the same rule the exam uses.
                return rows.All(r =>
                    given.TryGetValue(Norm(r.Label), out var value) && value == Norm(r.Answer));

            case "gap":
            case "short":
            case "completion":
                var accepted = (q.GapAnswer ?? string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Norm).Where(s => s.Length > 0).ToList();
                if (accepted.Count == 0 && !string.IsNullOrWhiteSpace(q.CorrectKey)) accepted.Add(Norm(q.CorrectKey));
                return accepted.Contains(Norm(answer));

            default:
                return Norm(answer) == Norm(q.CorrectKey);
        }
    }

    /// <summary>
    /// Reads a match answer: rows joined by ";", each "label=value". The label is
    /// normalized so casing and spacing do not matter.
    /// </summary>
    private static Dictionary<string, string> ParseMatchAnswer(string answer)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in answer.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var at = part.IndexOf('=');
            if (at <= 0) continue;
            var label = Norm(part[..at]);
            var value = Norm(part[(at + 1)..]);
            if (label.Length > 0) map[label] = value;
        }
        return map;
    }

    private static string Norm(string s) =>
        Regex.Replace((s ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");

    /// <summary>Explains one answer in plain language, when a model is available.</summary>
    public async Task<string> ExplainAsync(int questionId, CancellationToken ct = default)
    {
        var q = await AppDbContext.FindAsync<PracticeQuestion>(questionId).ConfigureAwait(false);
        if (q is null) return "That question is no longer saved.";
        if (!CanUseAi) return AiHint;

        var source = string.Empty;
        var set = await AppDbContext.FindAsync<PracticeSet>(q.SetId).ConfigureAwait(false);
        if (set is not null && !string.IsNullOrWhiteSpace(set.Unit))
        {
            var lesson = _lessons.GetUnit(set.Unit);
            if (lesson is not null)
            {
                // Pick the section that shares the most words with the question,
                // rather than the first section that contains the first word.
                var words = q.Prompt
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 3)
                    .Take(10)
                    .ToList();
                var best = lesson.Sections
                    .Where(s => !s.IsAnswerKey && s.FlatText.Length > 0)
                    .Select(s => new
                    {
                        Section = s,
                        Score = words.Count(w => s.FlatText.Contains(w, StringComparison.OrdinalIgnoreCase)),
                    })
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();
                source = best?.Section.FlatText ?? string.Empty;
            }
        }

        var correct = string.IsNullOrWhiteSpace(q.GapAnswer) ? q.CorrectKey : q.GapAnswer;
        var messages = new List<LlmMessage>
        {
            LlmMessage.System(StudyPrompts.ChatSystem),
            LlmMessage.User(StudyPrompts.BuildExplain(q.Prompt, correct, q.UserAnswer, source)),
        };
        var result = await _llm.CompleteAsync(messages, ct).ConfigureAwait(false);
        return result.Success ? result.Text.Trim() : DescribeLlmError(result);
    }

    private string TitleForUnit(string slug) =>
        _lessons.GetUnit(slug)?.Title ?? slug;

    /// <summary>
    /// Runs one tutor tool (lookup, translate, summarize, flashcards, fix,
    /// paraphrase). It answers in the chat thread, like a normal question, but
    /// with the tool's own instruction. Offline it returns the hint instead.
    /// </summary>
    public async Task<StudyChatSnapshot> RunToolAsync(
        int sessionId, string tool, string input, string unitFilter, CancellationToken ct = default)
    {
        input = (input ?? string.Empty).Trim();
        if (input.Length == 0) return ChatSnapshot(sessionId, unitFilter);
        if (!CanUseAi) return ChatSnapshot(sessionId, unitFilter) with { StatusMessage = AiHint };

        var session = await AppDbContext.FindAsync<StudySession>(sessionId).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The session is gone.");

        var label = (tool ?? string.Empty).Trim();
        var shown = $"[{label}] {input}";
        await AppDbContext.InsertAsync(new ChatMessage
        {
            SessionId = sessionId,
            Role = "user",
            Text = shown,
            CreatedAt = DateTime.UtcNow,
        }).ConfigureAwait(false);

        var hits = _lessons.Search(input, max: 5,
            unitSlug: string.IsNullOrWhiteSpace(unitFilter) ? null : unitFilter);
        var sources = hits.Select(h => new StudySource(h.Unit, h.SectionTitle, Shorten(h.Snippet, 240))).ToList();
        var sourceText = BuildSourceText(hits);

        var messages = new List<LlmMessage>
        {
            LlmMessage.System(StudyPrompts.ToolSystem),
            LlmMessage.User(StudyPrompts.BuildTool(label, input, sourceText)),
        };
        var result = await _llm.CompleteAsync(messages, ct).ConfigureAwait(false);

        await AppDbContext.InsertAsync(new ChatMessage
        {
            SessionId = sessionId,
            Role = "assistant",
            Text = result.Success ? result.Text.Trim() : DescribeLlmError(result),
            SourcesJson = JsonSerializer.Serialize(sources, Json),
            CreatedAt = DateTime.UtcNow,
        }).ConfigureAwait(false);

        session.ItemCount += 1;
        session.UpdatedAt = DateTime.UtcNow;
        await AppDbContext.UpdateAsync(session).ConfigureAwait(false);

        return ChatSnapshot(sessionId, unitFilter);
    }

    // ------------------------------------------------------------ materials

    public StudyMaterialSnapshot MaterialSnapshot(
        string unitSlug = "", string sectionId = "", string topicFilter = "", string skillFilter = "")
    {
        var units = UnitRows();
        var targetUnit = !string.IsNullOrWhiteSpace(unitSlug)
            ? _lessons.GetUnit(unitSlug)
            : (_lessons.Units.Count > 0 ? _lessons.Units[0] : null);

        if (targetUnit is null)
        {
            return new StudyMaterialSnapshot
            {
                Units = units,
            };
        }

        var sectionsQuery = targetUnit.Sections.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(topicFilter) && !topicFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            sectionsQuery = sectionsQuery.Where(s => string.Equals(s.Topic, topicFilter, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(skillFilter) && !skillFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            sectionsQuery = sectionsQuery.Where(s => string.Equals(s.Skill, skillFilter, StringComparison.OrdinalIgnoreCase));
        }

        var sectionRows = sectionsQuery.Select(s => new StudyMaterialSectionRow(
            s.Id, s.Title, s.Skill, s.Topic, s.IsAnswerKey, s.KeySectionId, s.TargetSectionId, s.Blocks.Count)).ToList();

        var selectedSection = (!string.IsNullOrWhiteSpace(sectionId)
            ? targetUnit.Sections.FirstOrDefault(s => string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase))
            : null) ?? (sectionRows.Count > 0 ? targetUnit.Sections.FirstOrDefault(s => s.Id == sectionRows[0].Id) : targetUnit.Sections.FirstOrDefault());

        LessonSection? pairedKey = null;
        if (selectedSection is not null)
        {
            if (!string.IsNullOrWhiteSpace(selectedSection.KeySectionId))
            {
                pairedKey = targetUnit.Sections.FirstOrDefault(s => string.Equals(s.Id, selectedSection.KeySectionId, StringComparison.OrdinalIgnoreCase));
            }
            else if (selectedSection.IsAnswerKey && !string.IsNullOrWhiteSpace(selectedSection.TargetSectionId))
            {
                pairedKey = targetUnit.Sections.FirstOrDefault(s => string.Equals(s.Id, selectedSection.TargetSectionId, StringComparison.OrdinalIgnoreCase));
            }
        }

        var slides = targetUnit.Slides.SelectMany(d => d.Slides).ToList();

        return new StudyMaterialSnapshot
        {
            Units = units,
            SelectedUnit = targetUnit.Slug,
            SelectedCategory = targetUnit.Category,
            UnitTopics = targetUnit.Topics,
            UnitSkills = targetUnit.Skills,
            Sections = sectionRows,
            SelectedSectionId = selectedSection?.Id ?? string.Empty,
            CurrentSection = selectedSection,
            PairedKeySection = pairedKey,
            CurrentSlides = slides,
            CurrentAudio = targetUnit.Audio,
        };
    }
}