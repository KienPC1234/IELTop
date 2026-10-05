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
    int Id, string Role, string Text, IReadOnlyList<StudySource> Sources, DateTime CreatedAt);

public sealed record StudyUnitRow(string Slug, string Title, int SectionCount, int WordCount, int PicturesSkipped);

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

    public StudyService(LessonService lessons, ILlmService llm)
    {
        _lessons = lessons;
        _llm = llm;
    }

    private bool CanUseAi => _llm.IsConfigured;
    private static string AiHint =>
        "Add a language model in Settings to chat with the tutor and build practice sets.";

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
            .Select(u => new StudyUnitRow(u.Slug, u.Title, u.Sections.Count, u.Vocabulary.Count, u.PicturesSkipped))
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

        return rows.Select(m => new StudyMessageRow(
            m.Id, m.Role, m.Text, ParseSources(m.SourcesJson), m.CreatedAt)).ToList();
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

        var result = await _llm.CompleteAsync(messages, ct).ConfigureAwait(false);

        await AppDbContext.InsertAsync(new ChatMessage
        {
            SessionId = sessionId,
            Role = "assistant",
            Text = result.Success ? result.Text.Trim() : DescribeLlmError(result),
            SourcesJson = JsonSerializer.Serialize(sources, Json),
            CreatedAt = DateTime.UtcNow,
        }).ConfigureAwait(false);

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
            builder.AppendLine(Shorten(hit.Body, 1200));
        }
        return builder.ToString();
    }

    /// <summary>
    /// The lesson text a practice set is built from, for one skill. A section is
    /// usable when it is not an answer key and matches the skill, or when it is
    /// an answer key for the same skill: that key is where the original tasks and
    /// their correct answers live, which is what makes a built set follow the
    /// lesson instead of drifting. Vocabulary is added for every skill when the
    /// section text is thin, and used alone when nothing else matched.
    /// </summary>
    public static string BuildPracticeMaterial(
        LessonService lessons, LessonUnit? lesson, string? unitSlug, string skill)
    {
        var material = new StringBuilder();
        if (lesson is not null)
        {
            foreach (var section in lesson.Sections)
            {
                if (section.FlatText.Length == 0) continue;

                bool skillMatch = section.Skill.Equals(skill, StringComparison.OrdinalIgnoreCase)
                    || section.Skill.Equals("Lesson", StringComparison.OrdinalIgnoreCase)
                    || skill == "Vocabulary";

                if (section.IsAnswerKey && !section.Skill.Equals(skill, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!section.IsAnswerKey && !skillMatch)
                    continue;

                material.AppendLine(section.IsAnswerKey
                    ? $"## {section.Title} (tasks and answers)"
                    : $"## {section.Title}");
                material.AppendLine(Shorten(section.FlatText, 1400));
                material.AppendLine();
                if (material.Length > 8000) break;
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
        int sessionId, string unit, string skill, string difficulty, int count, string scope = "All", CancellationToken ct = default)
    {
        count = Math.Clamp(count, 1, 20);

        if (!CanUseAi)
        {
            return PracticeSnapshot(sessionId, 0, scope) with
            {
                StatusMessage = AiHint,
            };
        }

        var unitSlug = string.IsNullOrWhiteSpace(unit) ? null : unit;
        var lesson = unitSlug is null ? null : _lessons.GetUnit(unitSlug);

        var topic = lesson?.Title ?? (string.IsNullOrWhiteSpace(unit) ? "mixed IELTS practice" : unit);
        var material = BuildPracticeMaterial(_lessons, lesson, unitSlug, skill);

        var messages = new List<LlmMessage>
        {
            LlmMessage.System(StudyPrompts.ChatSystem),
            LlmMessage.User(StudyPrompts.BuildPractice(topic, skill, material, count, difficulty)),
        };

        var result = await _llm.CompleteAsync(messages, ct).ConfigureAwait(false);

        // One retry with a blunt reminder: a model that wrapped the JSON in prose
        // usually gets it right the second time, and a lost set is the worst case.
        if (result.Success && !TryParsePractice(result.Text, count, out var retryParsed))
        {
            var retry = messages
                .Concat(new[] { LlmMessage.Assistant(Shorten(result.Text, 800)), LlmMessage.User(
                    "That was not valid question JSON. Reply again with only the JSON object, no prose and no code fences.") })
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
            status = result.Success
                ? "The model reply could not be read as questions. Try again."
                : DescribeLlmError(result);
        }

        return PracticeSnapshot(sessionId, setId, scope) with { StatusMessage = status };
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

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            parsed.Title = Str(root, "title");

            if (!root.TryGetProperty("questions", out var questions)
                || questions.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            // Read each question by hand, so a model that writes options as plain
            // strings, or misses one field, still yields a usable set. A question
            // that cannot be read is skipped; the rest are kept.
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

                if (element.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
                {
                    int index = 0;
                    foreach (var option in options.EnumerateArray())
                    {
                        if (option.ValueKind == JsonValueKind.Object)
                        {
                            var key = Str(option, "key");
                            q.Options.Add(new PracticeOption(key.Length > 0 ? key : Letter(index), Str(option, "text")));
                        }
                        else if (option.ValueKind == JsonValueKind.String)
                        {
                            q.Options.Add(new PracticeOption(Letter(index), option.GetString() ?? string.Empty));
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

    /// <summary>Models sometimes wrap JSON in prose or fences; pull the object out.</summary>
    private static string? ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return text.Substring(start, end - start + 1);
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
}