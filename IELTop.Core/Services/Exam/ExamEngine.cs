using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IELTop.Data;
using IELTop.Models;
using IELTop.Services.Ai;
using IELTop.Services.App;
using IELTop.Services.Storage;

namespace IELTop.Services.Exam;

/// <summary>One row summary for the setup and library lists.</summary>
public sealed record ExamPaperSummary(
    string Title,
    string Skills,
    string Category,
    string Level,
    int Parts,
    int Questions,
    int Minutes,
    bool IsUserPaper);

/// <summary>A skill or task type checkbox.</summary>
public sealed record SetupOption(string Name, bool IsSelected, int PartCount);

/// <summary>Outcome of one engine call, ready to send to the UI.</summary>
public sealed record EngineResult(bool Success, string Message)
{
    public static EngineResult Ok(string message = "") => new(true, message);
    public static EngineResult Fail(string message) => new(false, message);
}

/// <summary>The setup screen state, separate from a running test.</summary>
public sealed class ExamSetup
{
    public IReadOnlyList<ExamPaperSummary> Papers { get; init; } = Array.Empty<ExamPaperSummary>();
    public string SelectedPaperTitle { get; init; } = string.Empty;
    public IReadOnlyList<SetupOption> Skills { get; init; } = Array.Empty<SetupOption>();
    public IReadOnlyList<SetupOption> TaskTypes { get; init; } = Array.Empty<SetupOption>();
    public IReadOnlyList<string> StrictnessOptions { get; init; } = Array.Empty<string>();
    public string Strictness { get; init; } = "Standard";
    public IReadOnlyList<string> BuildModes { get; init; } = Array.Empty<string>();
    public string BuildMode { get; init; } = "Paper order";
    public bool MixAllPapers { get; init; }
    public bool ShuffleParts { get; init; }
    public bool StrictMode { get; init; }
    public bool CanUseAi { get; init; }
    public bool CanStart { get; init; }
    public string SelectedSkillsLabel { get; init; } = string.Empty;
    public string SelectedTaskTypesLabel { get; init; } = string.Empty;
    public string PaperCountLabel { get; init; } = string.Empty;
    public bool HasPapers { get; init; }
    public string MaterialWarning { get; init; } = string.Empty;
    public double Volume { get; init; } = 80;
}

/// <summary>Everything the UI needs: the setup plus the run in progress.</summary>
public sealed class ExamSnapshot
{
    public string Version { get; init; } = "1.0.0";
    public ExamSetup Setup { get; init; } = new();
    public ExamRun Run { get; init; } = new();
}

/// <summary>
/// Drives a mock test: setup, timed parts, answers, offline scoring, and AI
/// marking. It is OS neutral, so the Windows client and the web client score a
/// test exactly the same. It owns no window and no audio device: the host plays
/// audio and records speech when the run asks for it.
/// </summary>
public sealed class ExamEngine : IDisposable
{
    private readonly IExamRepository _repository;
    private readonly IIeltsAiService _ai;
    private readonly ISttService _stt;
    private readonly IGecService _gec;
    private readonly IModelLoadCoordinator _models;
    private readonly IPronunciationService _pronunciation;

    private readonly object _gate = new();
    private System.Threading.Timer? _ticker;
    private CancellationTokenSource? _speakingCts;
    private CancellationTokenSource? _gradingCts;

    private int _listenPrepRemaining;
    private List<ExamRunPart> _parts = new();

    // Setup state that outlives a single run.
    private readonly HashSet<string> _selectedSkills = new(StringComparer.OrdinalIgnoreCase);
    private bool _skillsInitialized;
    private List<SetupOption> _skills = new();
    private List<SetupOption> _taskTypes = new();

    /// <summary>Raised after any state change, so the host can push a fresh snapshot.</summary>
    public event Action? StateChanged;

    /// <summary>Set while the WebView should play the current Listening clip once.</summary>
    public string? ConsumePendingAudioUrl()
    {
        var url = Run.PendingAudioUrl;
        Run.PendingAudioUrl = string.Empty;
        return string.IsNullOrEmpty(url) ? null : url;
    }

    public ExamEngine(
        IExamRepository repository,
        IIeltsAiService ai,
        ISttService stt,
        IGecService gec,
        IModelLoadCoordinator models,
        IPronunciationService pronunciation)
    {
        _repository = repository;
        _ai = ai;
        _stt = stt;
        _gec = gec;
        _models = models;
        _pronunciation = pronunciation;
        Load();
    }

    private string _selectedPaperTitle = string.Empty;
    private string _strictness = "Standard";
    private string _buildMode = "Paper order";

    public ExamSetup Setup { get; private set; } = new();

    public bool MixAllPapers { get; private set; }
    public bool ShuffleParts { get; private set; }
    public bool StrictMode { get; private set; }
    public double Volume { get; private set; } = 80;
    /// <summary>Reading text scale for the exam, held on the run so the UI sees it.</summary>
    public double FontScale
    {
        get => Run.FontScale;
        private set => Run.FontScale = value;
    }
    public bool IsFullscreen { get; private set; }

    public ExamRun Run { get; private set; } = new();

    private ExamPaper? SelectedPaper =>
        _repository.LoadPapers().FirstOrDefault(p =>
            string.Equals(p.Title, _selectedPaperTitle, StringComparison.OrdinalIgnoreCase));

    /// <summary>Loads papers and rebuilds the setup screen. Never throws.</summary>
    public void Load()
    {
        List<ExamPaper> papers;
        try
        {
            papers = _repository.LoadPapers().ToList();
        }
        catch (Exception)
        {
            papers = new List<ExamPaper>();
        }

        if (string.IsNullOrWhiteSpace(_selectedPaperTitle))
            _selectedPaperTitle = papers.FirstOrDefault()?.Title ?? string.Empty;
        else if (!papers.Any(p => string.Equals(p.Title, _selectedPaperTitle, StringComparison.OrdinalIgnoreCase)))
            _selectedPaperTitle = papers.FirstOrDefault()?.Title ?? string.Empty;

        RebuildOptions(papers);
        Run.StatusMessage = papers.Count == 0
            ? $"No test papers found. Add a .json paper under {_repository.ExamsDir}."
            : "Pick a paper and a marking level, tick the skills, then start.";
        Refresh();
    }

    private void RebuildOptions(List<ExamPaper> papers)
    {
        var source = MixAllPapers
            ? papers.SelectMany(p => p.Parts)
            : papers.Where(p => string.Equals(p.Title, _selectedPaperTitle, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(p => p.Parts);

        var counts = source
            .Where(p => !string.IsNullOrWhiteSpace(p.Skill))
            .GroupBy(p => p.Skill, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        if (!_skillsInitialized)
        {
            _skillsInitialized = true;
            _selectedSkills.Clear();
            foreach (var skill in counts.Keys) _selectedSkills.Add(skill);
        }

        _skills = new List<SetupOption>();
        foreach (var skill in new[] { "Listening", "Reading", "Writing", "Speaking" })
        {
            counts.TryGetValue(skill, out int count);
            bool selected = _selectedSkills.Contains(skill) && count > 0;
            _skills.Add(new SetupOption(skill, selected, count));
        }

        var keepTypes = _taskTypes.Where(t => t.IsSelected).Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool firstFill = _taskTypes.Count == 0;
        _taskTypes = new List<SetupOption>();
        foreach (var type in source
            .Where(p => _skills.Any(o => o.IsSelected && string.Equals(o.Name, p.Skill, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.TaskType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
        {
            bool selected = firstFill || keepTypes.Count == 0 || keepTypes.Contains(type);
            _taskTypes.Add(new SetupOption(type, selected, 0));
        }

        var selectedPaper = papers.FirstOrDefault(p =>
            string.Equals(p.Title, _selectedPaperTitle, StringComparison.OrdinalIgnoreCase));

        Setup = new ExamSetup
        {
            Papers = papers.Select(p => new ExamPaperSummary(
                p.Title,
                string.Join(", ", p.Parts.Select(x => x.Skill).Distinct(StringComparer.OrdinalIgnoreCase)),
                p.Category, p.Level, p.Parts.Count,
                p.Parts.Sum(x => x.Questions.Count),
                p.Parts.Sum(x => x.Minutes),
                _repository.IsUserPaper(p.Title))).ToList(),
            SelectedPaperTitle = _selectedPaperTitle,
            Skills = _skills,
            TaskTypes = _taskTypes,
            StrictnessOptions = new[] { "Lenient", "Standard", "Strict" },
            Strictness = _strictness,
            BuildModes = new[] { "Paper order", "Random", "AI pick" },
            BuildMode = _buildMode,
            MixAllPapers = MixAllPapers,
            ShuffleParts = ShuffleParts,
            StrictMode = StrictMode,
            CanUseAi = _ai.IsAvailable,
            CanStart = CanStartSetup(papers),
            SelectedSkillsLabel = SelectedSkillsLabel(),
            SelectedTaskTypesLabel = SelectedTaskTypesLabel(),
            PaperCountLabel = $"{papers.Count} test paper(s) available.",
            HasPapers = papers.Count > 0,
            MaterialWarning = $"No test papers found. Add a .json paper under {_repository.ExamsDir}.",
            Volume = Volume,
        };
        _ = selectedPaper;
    }

    private bool CanStartSetup(List<ExamPaper> papers)
    {
        var picked = _skills.Where(o => o.IsSelected && o.PartCount > 0).Select(o => o.Name).ToList();
        return picked.Count > 0 && picked.Any(s => !string.Equals(s, "Listening", StringComparison.OrdinalIgnoreCase));
    }

    private string SelectedSkillsLabel()
    {
        var picked = _skills.Where(o => o.IsSelected).Select(o => o.Name).ToList();
        if (picked.Count == 0) return "No skill selected. Tick at least one.";
        if (picked.Count == _skills.Count) return "Full test, all four skills.";
        return string.Join(" + ", picked);
    }

    private string SelectedTaskTypesLabel()
    {
        var all = _taskTypes.Count;
        var picked = _taskTypes.Count(o => o.IsSelected);
        if (all == 0) return "No task type filter.";
        if (picked == 0 || picked == all) return "All task types.";
        return $"{picked} of {all} task types.";
    }

    // ----- Setup actions -----

    public void SelectPaper(string title)
    {
        _selectedPaperTitle = title ?? string.Empty;
        var papers = SafePapers();
        RebuildOptions(papers);
        Refresh();
    }

    public void ToggleSkill(string name)
    {
        var list = _skills.ToList();
        int i = list.FindIndex(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return;
        list[i] = list[i] with { IsSelected = !list[i].IsSelected };
        _skills = list;
        _selectedSkills.Clear();
        foreach (var o in _skills.Where(o => o.IsSelected)) _selectedSkills.Add(o.Name);
        RebuildOptions(SafePapers());
        Refresh();
    }

    public void ToggleTaskType(string name)
    {
        var list = _taskTypes.ToList();
        int i = list.FindIndex(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return;
        list[i] = list[i] with { IsSelected = !list[i].IsSelected };
        _taskTypes = list;
        RebuildOptions(SafePapers());
        Refresh();
    }

    public void SetMixAllPapers(bool value) { MixAllPapers = value; RebuildOptions(SafePapers()); Refresh(); }
    public void SetShuffleParts(bool value) { ShuffleParts = value; Refresh(); }
    public void SetStrictMode(bool value) { StrictMode = value; Refresh(); }
    public void SetBuildMode(string value) { _buildMode = value ?? "Paper order"; Refresh(); }
    public void SetStrictness(string value) { _strictness = value ?? "Standard"; Refresh(); }
    public void SetVolume(double value) { Volume = Math.Clamp(value, 0, 100); Refresh(); }
    public void SetFontScale(double value) { FontScale = Math.Clamp(value, 0.9, 1.6); Refresh(); }
    public void SetFullscreen(bool value) { IsFullscreen = value; Refresh(); }

    /// <summary>Counts leaving the test window during strict mode.</summary>
    public void RegisterFocusLost()
    {
        if (Run.Phase is not (ExamPhase.Running or ExamPhase.PartIntro) || !StrictMode) return;
        Run.StrictViolations++;
        Run.StatusMessage = Run.ViolationLabel;
        Refresh();
    }

    private MarkingStrictness StrictnessValue => _strictness switch
    {
        "Lenient" => MarkingStrictness.Lenient,
        "Strict" => MarkingStrictness.Strict,
        _ => MarkingStrictness.Standard,
    };

    private List<ExamPaper> SafePapers()
    {
        try { return _repository.LoadPapers().ToList(); }
        catch (Exception) { return new List<ExamPaper>(); }
    }

    private IEnumerable<(ExamPaper Paper, ExamPart Part)> FilteredParts()
    {
        var papers = SafePapers();
        IEnumerable<ExamPaper> source = MixAllPapers
            ? papers
            : papers.Where(p => string.Equals(p.Title, _selectedPaperTitle, StringComparison.OrdinalIgnoreCase));

        var wantedSkills = _skills.Where(o => o.IsSelected).Select(o => o.Name).ToList();
        var wantedTypes = _taskTypes.Where(o => o.IsSelected).Select(o => o.Name).ToList();
        bool filterTypes = _taskTypes.Count > 0 && wantedTypes.Count > 0 && wantedTypes.Count < _taskTypes.Count;

        foreach (var paper in source)
            foreach (var part in paper.Parts)
            {
                if (wantedSkills.Count > 0 && !wantedSkills.Any(s =>
                    string.Equals(s, part.Skill, StringComparison.OrdinalIgnoreCase))) continue;
                if (filterTypes && !wantedTypes.Any(t =>
                    string.Equals(t, part.TaskType, StringComparison.OrdinalIgnoreCase))) continue;
                yield return (paper, part);
            }
    }

    // ----- Starting a run -----

    public async Task<EngineResult> StartExamAsync(CancellationToken ct)
    {
        ResetRunState();

        var chosen = FilteredParts().ToList();
        if (chosen.Count == 0)
        {
            Run.ResultText = "No parts match this setup. Tick a skill that has parts, or loosen the task types.";
            Refresh();
            return EngineResult.Fail(Run.ResultText);
        }
        if (chosen.All(c => string.Equals(c.Part.Skill, "Listening", StringComparison.OrdinalIgnoreCase)))
        {
            Run.ResultText = "Listening cannot run alone. Tick Reading, Writing, or Speaking too, then start again.";
            Run.StatusMessage = Run.ResultText;
            Refresh();
            return EngineResult.Fail(Run.ResultText);
        }

        string buildNote = string.Empty;
        if (_buildMode == "Random" || ShuffleParts)
        {
            var rng = new Random();
            chosen = chosen.OrderBy(_ => rng.Next()).ToList();
            buildNote = "Random order.";
        }
        else if (_buildMode == "AI pick")
        {
            var picked = await PickBalancedAsync(chosen, ct);
            chosen = picked.Parts;
            buildNote = picked.Note;
        }

        chosen = chosen.OrderBy(c => SkillOrder(c.Part.Skill)).ToList();
        BuildParts(chosen);
        BeginRun(buildNote);
        return EngineResult.Ok();
    }

    /// <summary>Starts a custom test assembled elsewhere, for example the Library basket.</summary>
    public EngineResult StartCustomTest(IEnumerable<(string PaperTitle, string PartId)> picks, string note)
    {
        ResetRunState();

        var fresh = SafePapers();
        var chosen = new List<(ExamPaper Paper, ExamPart Part)>();
        foreach (var (title, id) in picks)
        {
            var paper = fresh.FirstOrDefault(p => string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase));
            var part = paper?.Parts.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (paper is not null && part is not null && !chosen.Any(c => ReferenceEquals(c.Part, part)))
                chosen.Add((paper, part));
        }

        if (chosen.Count == 0)
        {
            Run.ResultText = "The basket is empty. Add parts to it first.";
            Refresh();
            return EngineResult.Fail(Run.ResultText);
        }

        chosen = chosen.OrderBy(c => SkillOrder(c.Part.Skill)).ToList();
        BuildParts(chosen);
        BeginRun(note);
        return EngineResult.Ok();
    }

    private void BuildParts(List<(ExamPaper Paper, ExamPart Part)> chosen)
    {
        bool multiPaper = chosen.Select(c => c.Paper.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        var parts = new List<ExamRunPart>();
        foreach (var (paper, part) in chosen)
            parts.Add(BuildPart(part, MixAllPapers || multiPaper ? paper.Title : string.Empty));
        _parts = parts;
    }

    private static ExamRunPart BuildPart(ExamPart part, string paperName)
    {
        var questions = new List<ExamRunQuestion>();
        foreach (var q in part.Questions)
            questions.Add(BuildQuestion(q));

        return new ExamRunPart
        {
            PaperName = paperName,
            Title = part.Title,
            Skill = part.Skill,
            Topic = part.Topic,
            TaskType = part.TaskType,
            Instructions = part.Instructions,
            Material = part.Material,
            Minutes = part.Minutes,
            PrepSeconds = part.PrepSeconds,
            AudioFileName = part.AudioFile,
            ImageFileName = part.ImageFile,
            Questions = questions,
            RemainingSeconds = Math.Max(60, part.Minutes * 60),
            AudioPlayedOnce = false,
        };
    }

    private static ExamRunQuestion BuildQuestion(ExamQuestion q)
    {
        var prompt = q.Prompt ?? string.Empty;
        int blankAt = prompt.IndexOf("___", StringComparison.Ordinal);
        string before = prompt, after = string.Empty;
        if (blankAt >= 0)
        {
            int blankEnd = blankAt;
            while (blankEnd < prompt.Length && prompt[blankEnd] == '_') blankEnd++;
            before = prompt[..blankAt].TrimEnd();
            after = prompt[blankEnd..].TrimStart();
        }

        return new ExamRunQuestion
        {
            Number = q.Number,
            Prompt = prompt,
            Kind = q.Kind,
            CorrectKey = q.CorrectKey,
            GapAnswer = q.GapAnswer,
            Explanation = q.Explanation,
            GapBefore = before,
            GapAfter = after,
            Options = q.Options.Select(o => new ExamRunOption { Key = o.Key, Text = o.Text }).ToList(),
            Bank = q.Bank.ToList(),
            MatchRows = q.MatchRows.Select(r => new ExamRunMatchRow { Label = r.Label, Answer = r.Answer }).ToList(),
        };
    }

    private void ResetRunState()
    {
        _ticker?.Change(Timeout.Infinite, Timeout.Infinite);
        _speakingCts?.Cancel();
        _gradingCts?.Cancel();
        _parts = new List<ExamRunPart>();
    }

    private void BeginRun(string buildNote)
    {
        Run.AiFeedbackLines = Array.Empty<string>();
        Run.ReviewItems = Array.Empty<ReviewItem>();
        Run.ResultText = string.Empty;
        Run.BandLabel = string.Empty;
        Run.ShowReview = false;
        Run.StrictViolations = 0;
        Run.IsFullscreen = false;
        Run.PendingAudioUrl = string.Empty;
        IsFullscreen = false;
        // Top bar shows the paper or test name, like the real machine test.
        Run.Title = _buildMode == "AI pick" ? "AI built test"
            : MixAllPapers ? "Mixed papers"
            : string.IsNullOrWhiteSpace(_selectedPaperTitle) ? "IELTop test" : _selectedPaperTitle;

        var intro = StrictMode
            ? "Strict mode is on. Full screen, no other apps, finish the test."
            : "Test ready. Press Start part on each part. The clock runs from that press.";
        Run.StatusMessage = string.IsNullOrWhiteSpace(buildNote) ? intro : $"{intro} {buildNote}";
        ShowPartIntro(0);
    }

    private void ShowPartIntro(int index)
    {
        StopTicker();
        _listenPrepRemaining = 0;
        Run.ListeningPrepLabel = string.Empty;
        foreach (var p in _parts) p.IsCurrent = false;
        for (int i = 0; i < _parts.Count; i++)
        {
            _parts[i].Index = i;
            _parts[i].IsPassed = i < index;
            _parts[i].IsCurrent = i == index;
        }

        Run.PartIndex = index;
        Run.Parts = _parts;
        var part = _parts[index];
        part.IsTimerRunning = false;
        Run.Phase = ExamPhase.PartIntro;
        Run.IntroTitle = part.Title;
        Run.IntroSkill = part.Skill;
        Run.IntroDetail = BuildIntroDetail(part);
        Run.IntroHint = part.IsListening
            ? "The clip plays once, after a short reading time, and does not replay."
            : part.IsSpeaking
                ? "Speaking runs without pause. Record once, then finish the part."
                : "Read the instructions, then start when you are ready.";
        Refresh();
    }

    private static string BuildIntroDetail(ExamRunPart part)
    {
        var bits = new List<string> { $"{part.Minutes} minutes" };
        int questions = part.Questions.Count;
        if (questions > 0) bits.Add(questions == 1 ? "1 question" : $"{questions} questions");
        else if (part.IsWriting) bits.Add("one essay task");
        else if (part.IsSpeaking) bits.Add("one spoken part");
        return string.Join(", ", bits);
    }

    /// <summary>Starts the part in view: starts the clock and reveals the body.</summary>
    public EngineResult StartPart()
    {
        if (Run.Phase != ExamPhase.PartIntro) return EngineResult.Fail("The part is not ready.");
        var part = RequireCurrentPart();
        if (part is null) return EngineResult.Fail("No part is open.");

        Run.Phase = ExamPhase.Running;
        Run.StrictMode = StrictMode;
        part.IsTimerRunning = true;
        StartTicker();

        if (part.IsListening && !part.AudioPlayedOnce)
        {
            // Reading time before the clip, like the real test. Skippable.
            _listenPrepRemaining = 15;
            Run.ListeningPrepLabel = "Read the questions. The clip starts in 15 seconds, or press Start now.";
            part.AudioStatus = string.Empty;
            StartTicker();
        }
        Refresh();
        return EngineResult.Ok();
    }

    public void SkipPrep()
    {
        _listenPrepRemaining = 0;
        Run.ListeningPrepLabel = string.Empty;
        StartPendingAudio();
        Refresh();
    }

    private void StartPendingAudio()
    {
        var part = Run.CurrentPart;
        if (part is null || !part.IsListening || part.AudioPlayedOnce) return;
        part.AudioPlayedOnce = true;
        part.NoAudioFallback = false;

        string? path = string.IsNullOrWhiteSpace(part.AudioFileName) ? null : ResolveAudio(part.AudioFileName);
        if (path is null || !File.Exists(path))
        {
            part.AudioStatus = "No audio file for this part. Read the transcript and answer.";
            part.NoAudioFallback = true;
            return;
        }
        // The host plays this once, then calls AudioFinished.
        Run.PendingAudioUrl = path;
        part.AudioStatus = "Playing. The clip plays once, like the real test.";
    }

    /// <summary>Called by the host when the Listening clip finished or failed.</summary>
    public void AudioFinished(bool ok)
    {
        var part = Run.CurrentPart;
        if (part is null) return;
        part.AudioStatus = ok
            ? "Clip finished. It does not replay."
            : "Could not play this clip. Check the file and your speakers.";
        Refresh();
    }

    private void StartTicker()
    {
        _ticker ??= new System.Threading.Timer(_ => OnTick(), null, Timeout.Infinite, Timeout.Infinite);
        _ticker.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void StopTicker() => _ticker?.Change(Timeout.Infinite, Timeout.Infinite);

    private void OnTick()
    {
        lock (_gate)
        {
            if (Run.Phase != ExamPhase.Running) return;
            var part = Run.CurrentPart;
            if (part is null) return;

            if (_listenPrepRemaining > 0)
            {
                _listenPrepRemaining--;
                if (_listenPrepRemaining <= 0)
                {
                    Run.ListeningPrepLabel = string.Empty;
                    StartPendingAudio();
                }
                else
                {
                    Run.ListeningPrepLabel =
                        $"Read the questions. The clip starts in {_listenPrepRemaining} seconds, or press Start now.";
                }
                Refresh();
                return;
            }

            part.Tick();
            if (part.RemainingSeconds > 0) { Refresh(); return; }

            part.IsTimerRunning = false;
            if (Run.PartIndex < _parts.Count - 1) ShowPartIntro(Run.PartIndex + 1);
            else _ = SubmitAsync(confirm: false);
            Refresh();
        }
    }

    // ----- Navigation -----

    public EngineResult GoNextPart()
    {
        if (Run.Phase != ExamPhase.Running) return EngineResult.Fail("The part is not running.");
        if (Run.CurrentPart?.IsSpeaking == true) return EngineResult.Fail("Speaking uses Finish part.");
        if (Run.PartIndex < _parts.Count - 1) ShowPartIntro(Run.PartIndex + 1);
        return EngineResult.Ok();
    }

    public EngineResult SelectPart(int index)
    {
        if (Run.Phase != ExamPhase.Running) return EngineResult.Fail("The test is not running.");
        if (Run.CurrentPart?.IsSpeaking == true) return EngineResult.Fail("Speaking runs to its own end.");
        if (index < 0 || index >= _parts.Count || index == Run.PartIndex) return EngineResult.Ok();
        if (!_parts[index].IsPassed) return EngineResult.Fail("That part is not finished yet.");
        ShowPartIntro(index);
        return EngineResult.Ok();
    }

    public EngineResult GoToQuestion(int questionIndex)
    {
        var part = Run.CurrentPart;
        if (part is null || questionIndex < 0 || questionIndex >= part.Questions.Count) return EngineResult.Ok();
        part.FocusedIndex = questionIndex;
        Refresh();
        return EngineResult.Ok();
    }

    public EngineResult MoveQuestion(int delta)
    {
        var part = Run.CurrentPart;
        if (part is null) return EngineResult.Ok();
        part.FocusedIndex = Math.Clamp(part.FocusedIndex + delta, 0, Math.Max(0, part.Questions.Count - 1));
        Refresh();
        return EngineResult.Ok();
    }

    // ----- In-test tools: clock, text size, contrast, notes, highlights -----

    /// <summary>Hides or shows the clock, so a stressful countdown can be put away.</summary>
    public EngineResult ToggleTimer()
    {
        var part = Run.CurrentPart;
        if (part is null) return EngineResult.Ok();
        part.IsTimerHidden = !part.IsTimerHidden;
        Refresh();
        return EngineResult.Ok();
    }

    /// <summary>Larger or smaller reading text, applied for the whole run.</summary>
    public EngineResult ChangeFontScale(double delta)
    {
        FontScale = Math.Clamp(Math.Round(FontScale + delta, 2), 0.9, 1.6);
        Refresh();
        return EngineResult.Ok();
    }

    /// <summary>Yellow on black for the passage and the essay box.</summary>
    public EngineResult ToggleContrast()
    {
        var part = Run.CurrentPart;
        if (part is null) return EngineResult.Ok();
        part.ContrastOn = !part.ContrastOn;
        Refresh();
        return EngineResult.Ok();
    }

    /// <summary>Opens the notes panel, quoting any text the student selected.</summary>
    public EngineResult OpenNotes(string selectedText)
    {
        var part = Run.CurrentPart;
        if (part is null) return EngineResult.Ok();
        if (!string.IsNullOrWhiteSpace(selectedText)) part.SelectedMaterialWord = selectedText.Trim();
        part.NotesOpen = true;
        Refresh();
        return EngineResult.Ok();
    }

    public EngineResult SetNotes(string text)
    {
        var part = Run.CurrentPart;
        if (part is null) return EngineResult.Ok();
        part.Notes = text ?? string.Empty;
        Refresh();
        return EngineResult.Ok();
    }

    public EngineResult CloseNotes()
    {
        var part = Run.CurrentPart;
        if (part is null) return EngineResult.Ok();
        part.NotesOpen = false;
        Refresh();
        return EngineResult.Ok();
    }

    /// <summary>Marks a passage phrase so it stands out when reading.</summary>
    public EngineResult AddHighlight(string text)
    {
        var part = Run.CurrentPart;
        if (part is null || string.IsNullOrWhiteSpace(text)) return EngineResult.Ok();
        var clean = text.Trim();
        if (clean.Length > 300) clean = clean[..300];
        if (!part.Highlights.Any(h => string.Equals(h, clean, StringComparison.Ordinal)))
            part.Highlights.Add(clean);
        Refresh();
        return EngineResult.Ok();
    }

    public EngineResult ClearHighlights()
    {
        var part = Run.CurrentPart;
        if (part is null) return EngineResult.Ok();
        part.Highlights.Clear();
        Refresh();
        return EngineResult.Ok();
    }

    /// <summary>Turn strict mode off or on from inside the running test.</summary>
    public EngineResult ToggleStrictMode()
    {
        StrictMode = !StrictMode;
        Run.StrictMode = StrictMode;
        Refresh();
        return EngineResult.Ok();
    }

    // ----- Answering -----

    private ExamRunPart? RequireCurrentPart() => Run.CurrentPart;

    public EngineResult SetChoice(int questionIndex, string key)
    {
        var q = FindQuestion(questionIndex);
        if (q is null) return EngineResult.Fail("No such question.");
        q.SelectedKey = key ?? string.Empty;
        foreach (var o in q.Options) o.IsSelected = o.Key == q.SelectedKey;
        Refresh();
        return EngineResult.Ok();
    }

    public EngineResult SetGapAnswer(int questionIndex, string text)
    {
        var q = FindQuestion(questionIndex);
        if (q is null) return EngineResult.Fail("No such question.");
        q.Answer = text ?? string.Empty;
        Refresh();
        return EngineResult.Ok();
    }

    public EngineResult SetMatchRow(int questionIndex, int rowIndex, string value)
    {
        var q = FindQuestion(questionIndex);
        if (q is null || rowIndex < 0 || rowIndex >= q.MatchRows.Count) return EngineResult.Fail("No such row.");
        q.MatchRows[rowIndex].Selected = value ?? string.Empty;
        Refresh();
        return EngineResult.Ok();
    }

    public EngineResult ToggleFlag(int questionIndex)
    {
        var q = FindQuestion(questionIndex);
        if (q is null) return EngineResult.Fail("No such question.");
        q.IsFlagged = !q.IsFlagged;
        Refresh();
        return EngineResult.Ok();
    }

    public EngineResult SetEssay(string text)
    {
        var part = RequireCurrentPart();
        if (part is null) return EngineResult.Fail("No part is open.");
        part.Essay = text ?? string.Empty;
        Refresh();
        return EngineResult.Ok();
    }

    public EngineResult SetTranscript(string text)
    {
        var part = RequireCurrentPart();
        if (part is null) return EngineResult.Fail("No part is open.");
        part.Transcript = text ?? string.Empty;
        Refresh();
        return EngineResult.Ok();
    }

    private ExamRunQuestion? FindQuestion(int index)
    {
        var part = RequireCurrentPart();
        if (part is null || index < 0 || index >= part.Questions.Count) return null;
        return part.Questions[index];
    }

    // ----- Speaking -----

    /// <summary>Tells the engine a recording started. The host captures the audio.</summary>
    public EngineResult BeginSpeakingRecording()
    {
        var part = RequireCurrentPart();
        if (part is null || !part.IsSpeaking || part.IsRecording) return EngineResult.Fail("Recording is not ready.");
        part.IsRecording = true;
        part.AudioStatus = $"Recording for {part.SpeakingSeconds} seconds. Speak now, no pause.";
        Refresh();
        return EngineResult.Ok();
    }

    /// <summary>
    /// Called by the host with the recorded wav path. Runs offline transcription
    /// when a model is installed, otherwise asks the student to type it.
    /// </summary>
    public async Task<EngineResult> CompleteSpeakingRecordingAsync(string wavPath, CancellationToken ct)
    {
        var part = RequireCurrentPart();
        if (part is null || !part.IsSpeaking) return EngineResult.Fail("No speaking part is open.");
        part.IsRecording = false;

        if (string.IsNullOrWhiteSpace(wavPath))
        {
            part.AudioStatus = "No audio was captured. Check your microphone.";
            Refresh();
            return EngineResult.Fail(part.AudioStatus);
        }

        part.SpokenSeconds = part.SpeakingSeconds;
        part.LastRecordingPath = wavPath;
        if (!_stt.IsAvailable())
        {
            part.AudioStatus = "Saved recording. No transcription model, type what you said below so AI marking can work. File kept locally.";
            Refresh();
            return EngineResult.Ok();
        }

        _speakingCts?.Cancel();
        _speakingCts = new CancellationTokenSource();
        try
        {
            if (_models.KeepReady)
            {
                part.AudioStatus = "Getting the transcription model ready.";
                Refresh();
                await _models.PrepareAsync(new[] { "stt-whisper-tiny-en" }, _speakingCts.Token);
            }
            part.AudioStatus = "Transcribing your speech.";
            Refresh();
            var transcript = await _stt.TranscribeAsync(wavPath, _speakingCts.Token);
            if (transcript.Success && !string.IsNullOrWhiteSpace(transcript.Text))
                part.AudioStatus = "Transcript ready. Fix any wrong words below, then continue.";
            else
                part.AudioStatus = "Saved recording. Type what you said below so AI marking can work. File kept locally.";
        }
        catch (OperationCanceledException)
        {
            part.AudioStatus = "Stopped.";
        }
        catch (Exception)
        {
            part.AudioStatus = "Recording failed. Check your microphone and try again.";
        }
        Refresh();
        return EngineResult.Ok();
    }

    public async Task<EngineResult> CheckPronunciationAsync(CancellationToken ct)
    {
        var part = RequireCurrentPart();
        if (part is null || !part.IsSpeaking) return EngineResult.Fail("Open a speaking part first.");

        var r = await _pronunciation.AssessAsync(part.LastRecordingPath, part.Transcript, ct);
        if (!r.Success)
        {
            part.PronunciationSummary = string.Empty;
            part.PronunciationWords = Array.Empty<PronunciationWord>();
            part.AudioStatus = r.Error;
            Refresh();
            return EngineResult.Fail(r.Error);
        }

        part.PronunciationSummary = r.Summary;
        part.PronunciationWords = r.Words;
        Refresh();
        return EngineResult.Ok();
    }

    public EngineResult FinishSpeakingPart()
    {
        var part = RequireCurrentPart();
        if (part is null || !part.IsSpeaking) return EngineResult.Fail("No speaking part is open.");
        if (Run.PartIndex < _parts.Count - 1) ShowPartIntro(Run.PartIndex + 1);
        else _ = SubmitAsync(confirm: false);
        return EngineResult.Ok();
    }

    // ----- Review and submit -----

    public EngineResult ToggleReview()
    {
        Run.ShowReview = !Run.ShowReview;
        if (!Run.ShowReview) { Refresh(); return EngineResult.Ok(); }

        var items = new List<ReviewItem>();
        foreach (var part in _parts)
        {
            if (part.IsWriting)
            {
                items.Add(new ReviewItem($"{part.Title}: {part.WordCountLabel} written, not auto scored.", null));
                continue;
            }
            if (part.IsSpeaking)
            {
                items.Add(new ReviewItem($"{part.Title}: {part.TranscriptWordCount}, not auto scored.", null));
                continue;
            }
            if (part.IsListening)
            {
                var wrong = part.Questions.Where(q => !q.IsCorrect).ToList();
                if (wrong.Count == 0) items.Add(new ReviewItem($"{part.Title}: all correct.", true));
                foreach (var q in wrong)
                    items.Add(new ReviewItem($"{part.Title}, question {q.Number}: {q.ListeningReviewLabel}", false));
                continue;
            }
            foreach (var q in part.Questions)
            {
                items.Add(new ReviewItem($"{part.Title}, question {q.Number}: {q.ReadingReviewLabel}", q.IsCorrect));
                if (q.IsMatch)
                    foreach (var row in q.MatchRows)
                    {
                        var mark = row.IsCorrect ? "correct"
                            : string.IsNullOrWhiteSpace(row.Selected) ? $"no answer, correct is {row.Answer}"
                            : $"you chose {row.Selected.Trim()}, correct is {row.Answer}";
                        items.Add(new ReviewItem($"    {row.Label}: {mark}", row.IsCorrect));
                    }
            }
        }
        Run.ReviewItems = items;
        Refresh();
        return EngineResult.Ok();
    }

    public Task<EngineResult> SubmitAsync(bool confirm)
    {
        if (Run.Phase is ExamPhase.Setup or ExamPhase.Finished)
            return Task.FromResult(EngineResult.Fail("No test is running."));

        StopTicker();
        _speakingCts?.Cancel();
        _gradingCts?.Cancel();
        Run.Phase = ExamPhase.Finished;
        Run.ShowReview = false;
        Run.ReviewItems = Array.Empty<ReviewItem>();

        int correct = _parts.Sum(p => p.CorrectCount);
        int total = _parts.Sum(p => p.ScoredCount);

        bool generalReading = SafePapers().FirstOrDefault(p =>
            string.Equals(p.Title, _selectedPaperTitle, StringComparison.OrdinalIgnoreCase))?.Category
            is "General Training";
        double mid = total == 0 ? 0 : IeltsBanding.RawToBand(correct, total, generalReading);
        var range = IeltsBanding.ToRange(mid, StrictnessValue);
        Run.BandLabel = total == 0 ? string.Empty : range.Label;
        Run.CriteriaHint =
            "Listening and Reading use the official raw to band table. " +
            "Writing and Speaking average four criteria to half bands. " +
            "Bands are ranges, because examiners vary. Practice estimates only.";

        Run.ResultText = total == 0
            ? "This test has no multiple choice questions to score. Use AI marking for Writing and Speaking, or ask a teacher."
            : $"Score {correct} of {total}. Objective band {range.Label} " +
              $"({IeltsBanding.StrictnessLabel(StrictnessValue)}). " +
              "Writing and Speaking need AI marking or a teacher. " +
              "All bands are practice estimates, not official IELTS scores.";
        Run.StatusMessage = Run.ResultText;

        SaveAttempt(correct, total, range);
        Refresh();
        return Task.FromResult(EngineResult.Ok());
    }

    private void SaveAttempt(int correct, int total, BandRange range)
    {
        try
        {
            using var db = new AppDbContext();
            var summary = Run.ResultText.Length > 500 ? Run.ResultText[..500] : Run.ResultText;
            if (Run.StrictViolations > 0)
                summary += $" Focus left {Run.StrictViolations} time(s) during strict mode.";
            var skills = _parts.Select(p => p.Skill).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string scope = skills.Count == 0 ? "Unknown"
                : skills.Count >= 4 ? "Full test"
                : string.Join(" + ", skills);
            db.ExamAttempts.Add(new ExamAttempt
            {
                PaperTitle = _buildMode == "AI pick" ? "AI built test"
                    : MixAllPapers ? "Mixed papers" : _selectedPaperTitle is { Length: > 0 } t ? t : "Unknown",
                Scope = scope,
                Strictness = IeltsBanding.StrictnessLabel(StrictnessValue),
                BandLow = range.Low,
                BandHigh = range.High,
                Correct = correct,
                Total = total,
                Summary = summary,
                Violations = Run.StrictViolations,
                CreatedAt = DateTime.Now,
            });
            db.SaveChanges();
        }
        catch (Exception)
        {
            // History must never block the result screen.
        }
    }

    /// <summary>Clears the result screen so a new test can be set up.</summary>
    public EngineResult BackToSetup()
    {
        Run = new ExamRun { StatusMessage = "Pick a paper and a marking level, tick the skills, then start." };
        _parts = new List<ExamRunPart>();
        Load();
        return EngineResult.Ok();
    }

    /// <summary>Stops a running test without saving it.</summary>
    public EngineResult CancelRunningTest()
    {
        StopTicker();
        _gradingCts?.Cancel();
        _speakingCts?.Cancel();
        _parts = new List<ExamRunPart>();
        Run = new ExamRun { StatusMessage = "Test cancelled. Nothing was saved." };
        Load();
        return EngineResult.Ok();
    }

    // ----- AI marking -----

    public async Task<EngineResult> BeginAiMarkingAsync(CancellationToken ct)
    {
        if (Run.IsGrading) return EngineResult.Fail("Marking is already running.");
        if (!_ai.IsAvailable) return EngineResult.Fail("Add a language model in Settings to get AI bands.");
        if (_parts.Count == 0) return EngineResult.Fail("Start a test first.");

        _gradingCts?.Cancel();
        _gradingCts = new CancellationTokenSource();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _gradingCts.Token);
        var token = linked.Token;
        Run.IsGrading = true;
        Run.LoadingLabel = "Preparing the writing and speaking models.";
        Run.StatusMessage = $"The model is marking ({IeltsBanding.StrictnessLabel(StrictnessValue)}). Press Stop to cancel.";
        Refresh();

        var lines = new List<string>();
        try
        {
            var wanted = new List<string>();
            if (_parts.Any(p => p.IsWriting)) wanted.AddRange(_models.WritingSlots);
            if (_parts.Any(p => p.IsSpeaking)) wanted.AddRange(_models.SpeakingSlots);
            await _models.PrepareAsync(wanted, token);
            Run.LoadingLabel = "Marking with the language model.";
            Refresh();

            foreach (var part in _parts.Where(p => p.IsWriting))
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(part.Essay))
                {
                    lines.Add($"{part.Title}: no essay, skipped.");
                    continue;
                }
                int minimum = LlmPrompts.IsTask1(part.Material + " " + part.Instructions + " " + part.Title) ? 150 : 250;
                var r = await _ai.ReviewWritingAsync(
                    part.Material + "\n" + part.Instructions, part.Essay, minimum, StrictnessValue, token);
                if (!r.Success || r.Feedback is null) { lines.Add($"{part.Title}: {r.Error}"); continue; }
                var f = await ApplyGrammarCheckAsync(part.Title, part.Essay, r.Feedback, lines, token);
                part.AiResult = $"Band {f.BandLabel}. {f.Summary}";
                string firstName = part.Title.Contains("Task 1", StringComparison.OrdinalIgnoreCase)
                    ? "Task Achievement" : "Task Response";
                lines.Add($"{part.Title}: band {f.BandLabel}. {f.Summary}");
                lines.Add($"  Band table: {firstName} {f.TaskResponse:0.0}, Cohesion {f.Coherence:0.0}, " +
                    $"Words {f.LexicalResource:0.0}, Grammar {f.Grammar:0.0}.");
                AddWhy(lines, $"  {firstName}", f.TaskResponseWhy);
                AddWhy(lines, "  Cohesion", f.CoherenceWhy);
                AddWhy(lines, "  Words", f.LexicalWhy);
                AddWhy(lines, "  Grammar", f.GrammarWhy);
                foreach (var s in f.Strengths.Take(2)) lines.Add($"  Good: {s}");
                foreach (var s in f.Improvements.Take(3)) lines.Add($"  Fix: {s}");
                if (!string.IsNullOrWhiteSpace(f.CorrectedExcerpt)) lines.Add($"  Rewrite: {f.CorrectedExcerpt}");
                lines.Add($"  Stats: {WritingStats(part.Essay)}");
            }

            foreach (var part in _parts.Where(p => p.IsSpeaking))
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(part.Transcript))
                {
                    lines.Add($"{part.Title}: no transcript, skipped. Type what you said to get a band.");
                    continue;
                }
                var r = await _ai.AssessSpeakingAsync(
                    part.Material + "\n" + part.Title, part.Transcript,
                    $"{part.Skill} {part.TaskType}", part.SpokenSeconds, StrictnessValue, token);
                if (!r.Success || r.Feedback is null) { lines.Add($"{part.Title}: {r.Error}"); continue; }
                var f = ApplyPaceCap(part, r.Feedback, lines);
                part.AiResult = $"Band {f.BandLabel}. {f.Summary}";
                lines.Add($"{part.Title}: band {f.BandLabel}. {f.Summary}");
                lines.Add($"  Band table: Fluency {f.Fluency:0.0}, Words {f.LexicalResource:0.0}, " +
                    $"Grammar {f.Grammar:0.0}, Sound {f.Pronunciation:0.0}.");
                AddWhy(lines, "  Fluency", f.FluencyWhy);
                AddWhy(lines, "  Words", f.LexicalWhy);
                AddWhy(lines, "  Grammar", f.GrammarWhy);
                AddWhy(lines, "  Sound", f.PronunciationWhy);
                foreach (var s in f.Strengths.Take(2)) lines.Add($"  Good: {s}");
                foreach (var s in f.Improvements.Take(3)) lines.Add($"  Fix: {s}");
            }

            foreach (var part in _parts.Where(p => p.IsListening))
                foreach (var q in part.Questions.Where(q => !q.IsCorrect))
                {
                    token.ThrowIfCancellationRequested();
                    var r = await _ai.ExplainListeningAsync(part.Material, QuestionDetail(q), q.ChosenLabel, q.DisplayAnswer, token);
                    lines.Add(r.Success
                        ? $"{part.Title}, question {q.Number}: {OneLine(r.Text)}"
                        : $"{part.Title}, question {q.Number}: {r.Error}");
                }

            foreach (var part in _parts.Where(p => !p.IsListening && !p.IsWriting && !p.IsSpeaking))
                foreach (var q in part.Questions.Where(q => !q.IsCorrect))
                {
                    token.ThrowIfCancellationRequested();
                    var r = await _ai.ExplainReadingAsync(part.Material, QuestionDetail(q), q.ChosenLabel, q.DisplayAnswer, token);
                    lines.Add(r.Success
                        ? $"{part.Title}, question {q.Number}: {OneLine(r.Text)}"
                        : $"{part.Title}, question {q.Number}: {r.Error}");
                }

            Run.AiFeedbackLines = lines;
            Run.StatusMessage = lines.Count == 0
                ? "Nothing to mark. Write an essay or type a speaking transcript first."
                : "AI marking done. Listening and Reading lines explain each wrong answer. Bands are ranges for practice, not official scores.";
            UpdateLastAttemptWithAi(lines);
        }
        catch (OperationCanceledException)
        {
            Run.StatusMessage = "AI marking stopped.";
        }
        finally
        {
            _models.ReleaseAfterUse(_models.WritingSlots.Concat(_models.SpeakingSlots));
            Run.IsGrading = false;
            Run.LoadingLabel = string.Empty;
            Refresh();
        }
        return EngineResult.Ok();
    }

    public EngineResult CancelGrading()
    {
        _gradingCts?.Cancel();
        Run.StatusMessage = "Stopping AI marking.";
        Refresh();
        return EngineResult.Ok();
    }

    /// <summary>Full AI feedback as one text block, for the copy button.</summary>
    public string FeedbackText() => string.Join("\n", Run.AiFeedbackLines);

    private void UpdateLastAttemptWithAi(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        try
        {
            using var db = new AppDbContext();
            var last = db.ExamAttempts.OrderByDescending(a => a.Id).FirstOrDefault();
            if (last is null) return;
            double writing = _parts.Where(p => p.IsWriting && p.AiResult.Length > 0)
                .Select(p => ParseBand(p.AiResult)).DefaultIfEmpty(0).Max();
            double speaking = _parts.Where(p => p.IsSpeaking && p.AiResult.Length > 0)
                .Select(p => ParseBand(p.AiResult)).DefaultIfEmpty(0).Max();
            if (writing > 0) last.WritingBand = writing;
            if (speaking > 0) last.SpeakingBand = speaking;
            var feedback = string.Join("\n", lines);
            last.AiFeedback = feedback.Length > 8000 ? feedback[..8000] : feedback;
            db.SaveChanges();
        }
        catch (Exception)
        {
            // Result history must never block the feedback screen.
        }
    }

    private async Task<WritingFeedback> ApplyGrammarCheckAsync(
        string title, string essay, WritingFeedback f, List<string> lines, CancellationToken ct)
    {
        if (!_gec.IsAvailable())
        {
            lines.Add($"{title}: no grammar model, grammar band is an AI estimate only.");
            return f;
        }
        Run.StatusMessage = $"Checking grammar in {title}.";
        Refresh();
        GecResult? g;
        try
        {
            g = await _gec.CheckAsync(essay, ct);
        }
        catch (OperationCanceledException) { return f; }
        catch (Exception) { lines.Add($"{title}: grammar check skipped."); return f; }
        if (!g.Success) { lines.Add($"{title}: grammar check skipped."); return f; }

        double cap = g.ErrorsPer100Words switch { > 10 => 4.5, > 6 => 5.5, > 3 => 6.5, > 1 => 7.5, _ => 9.0 };
        double grammar = Math.Min(f.Grammar, cap);
        double overall = IeltsBanding.RoundHalf(
            (f.TaskResponse + f.Coherence + f.LexicalResource + grammar) / 4.0);
        var range = IeltsBanding.ToRange(overall, StrictnessValue);
        lines.Add($"{title}: grammar check found {g.ErrorCount} errors " +
            $"({g.ErrorsPer100Words:0.0} per 100 words), grammar capped at {grammar:0.0}.");
        if (g.Edits.Count > 0)
            lines.Add($"  Example: '{OneLine(g.Edits[0].Original)}' becomes '{OneLine(g.Edits[0].Corrected)}'.");

        return new WritingFeedback
        {
            EstimatedBand = overall,
            BandLow = range.Low,
            BandHigh = range.High,
            TaskResponse = f.TaskResponse,
            TaskAchievement = f.TaskAchievement,
            Coherence = f.Coherence,
            LexicalResource = f.LexicalResource,
            Grammar = grammar,
            TaskResponseWhy = f.TaskResponseWhy,
            TaskAchievementWhy = f.TaskAchievementWhy,
            CoherenceWhy = f.CoherenceWhy,
            LexicalWhy = f.LexicalWhy,
            GrammarWhy = f.GrammarWhy,
            Summary = f.Summary,
            Strengths = f.Strengths,
            Improvements = f.Improvements,
            CorrectedExcerpt = f.CorrectedExcerpt,
        };
    }

    private SpeakingFeedback ApplyPaceCap(ExamRunPart part, SpeakingFeedback f, List<string> lines)
    {
        int words = ExamRunPart.CountWords(part.Transcript);
        double minutes = Math.Max(1, part.SpokenSeconds) / 60.0;
        double wpm = words / minutes;
        lines.Add($"{part.Title}: speech pace about {wpm:0} words per minute.");
        double cap = wpm switch { < 60 => 5.0, < 90 => 6.0, < 110 => 7.0, _ => 9.0 };
        if (f.Fluency <= cap) return f;

        double overall = IeltsBanding.RoundHalf((cap + f.LexicalResource + f.Grammar + f.Pronunciation) / 4.0);
        var range = IeltsBanding.ToRange(overall, StrictnessValue);
        lines.Add($"{part.Title}: slow pace caps fluency at {cap:0.0}.");
        return new SpeakingFeedback
        {
            EstimatedBand = overall,
            BandLow = range.Low,
            BandHigh = range.High,
            Fluency = cap,
            LexicalResource = f.LexicalResource,
            Grammar = f.Grammar,
            Pronunciation = f.Pronunciation,
            FluencyWhy = f.FluencyWhy,
            LexicalWhy = f.LexicalWhy,
            GrammarWhy = f.GrammarWhy,
            PronunciationWhy = f.PronunciationWhy,
            Summary = f.Summary,
            Strengths = f.Strengths,
            Improvements = f.Improvements,
        };
    }

    // ----- Helpers -----

    private static int SkillOrder(string skill) => skill.ToLowerInvariant() switch
    {
        "listening" => 0,
        "reading" => 1,
        "writing" => 2,
        "speaking" => 3,
        _ => 4,
    };

    private async Task<(List<(ExamPaper Paper, ExamPart Part)> Parts, string Note)> PickBalancedAsync(
        List<(ExamPaper Paper, ExamPart Part)> candidates, CancellationToken ct)
    {
        var catalog = candidates.Select(c =>
            $"{c.Paper.Title}|{c.Part.Id}, {c.Part.Skill}, {c.Part.TaskType}, {c.Part.Minutes}min");

        if (_ai.IsAvailable)
        {
            Run.StatusMessage = "AI is building your full test.";
            Refresh();
            try
            {
                var pick = await _ai.PickTestAsync(string.Join("\n", catalog), HistorySummary(), ct);
                if (pick.Success)
                {
                    var ordered = new List<(ExamPaper Paper, ExamPart Part)>();
                    foreach (var id in pick.Ids)
                    {
                        var cut = id.Split('|', 2);
                        if (cut.Length != 2) continue;
                        var found = candidates.FirstOrDefault(c =>
                            string.Equals(c.Paper.Title, cut[0], StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(c.Part.Id, cut[1], StringComparison.OrdinalIgnoreCase));
                        if (found.Part is not null && !ordered.Any(o => ReferenceEquals(o.Part, found.Part)))
                            ordered.Add(found);
                    }
                    if (ordered.Count >= 2 && !IsListeningOnly(ordered))
                        return (ordered, $"AI built this test: {pick.Reason}");
                }
            }
            catch (Exception)
            {
                // A model failure falls through to the balanced set below.
            }
        }

        var balanced = new List<(ExamPaper Paper, ExamPart Part)>();
        foreach (var skill in new[] { "Listening", "Reading", "Writing", "Speaking" })
            balanced.AddRange(candidates
                .Where(c => string.Equals(c.Part.Skill, skill, StringComparison.OrdinalIgnoreCase)
                    && !balanced.Any(o => ReferenceEquals(o.Part, c.Part))).Take(2));
        if (balanced.Count == 0) balanced.AddRange(candidates.Take(8));
        balanced = balanced.Take(8).ToList();
        if (IsListeningOnly(balanced)) balanced = candidates.Take(8).ToList();
        return (balanced, "Balanced set across the four skills.");
    }

    private static bool IsListeningOnly(List<(ExamPaper Paper, ExamPart Part)> parts) =>
        parts.Count > 0 && parts.All(c => string.Equals(c.Part.Skill, "Listening", StringComparison.OrdinalIgnoreCase));

    private static string HistorySummary()
    {
        try
        {
            using var db = new AppDbContext();
            var rows = db.ExamAttempts.GroupBy(a => a.Scope)
                .Select(g => new { Scope = g.Key, Avg = g.Average(a => (a.BandLow + a.BandHigh) / 2.0), Count = g.Count() })
                .ToList();
            if (rows.Count == 0) return "no past tests";
            return string.Join("; ", rows.Select(r =>
                FormattableString.Invariant($"{r.Scope}: {r.Avg:0.0} over {r.Count} test(s)")));
        }
        catch
        {
            return "history unavailable";
        }
    }

    private static double ParseBand(string aiResult)
    {
        var match = Regex.Match(aiResult, @"(\d+(?:\.\d+)?)");
        return match.Success && double.TryParse(match.Groups[1].Value,
            NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static string QuestionDetail(ExamRunQuestion q)
    {
        var sb = new StringBuilder($"Q{q.Number}: {q.Prompt}");
        if (q.ShowOptions && q.Options.Count > 0)
            sb.Append(" Options: " + string.Join("; ", q.Options.Select(o => $"{o.Key}) {o.Text}")));
        if (q.IsGap) sb.Append($" Accepted answers: {q.GapAnswer}");
        if (q.IsMatch)
        {
            if (q.Bank.Count > 0) sb.Append(" Bank: " + string.Join("; ", q.Bank));
            sb.Append(" Rows: " + string.Join("; ", q.MatchRows.Select(r => $"{r.Label} is {r.Answer}")));
        }
        if (!string.IsNullOrWhiteSpace(q.Explanation)) sb.Append($" Note: {q.Explanation}");
        return sb.ToString();
    }

    private static string WritingStats(string essay)
    {
        var words = essay.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "no words";
        int sentences = Math.Max(1, Regex.Matches(essay, @"[.!?]+").Count);
        int distinct = new HashSet<string>(words.Select(w => w.Trim('.', ',', '!', '?', ';', ':').ToLowerInvariant())).Count;
        int longWords = words.Count(w => w.Length >= 7);
        return $"{words.Length} words, {sentences} sentences, {words.Length / (double)sentences:0.0} words per sentence, " +
            $"{distinct} different words, {longWords * 100 / words.Length}% long words.";
    }

    private static void AddWhy(List<string> lines, string label, string why)
    {
        if (string.IsNullOrWhiteSpace(why)) return;
        lines.Add($"{label}: {OneLine(why)}");
    }

    private static string OneLine(string text)
    {
        var clean = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length <= 320 ? clean : clean[..320] + "...";
    }

    private static string ResolveAudio(string file)
    {
        if (Path.IsPathRooted(file) && File.Exists(file)) return file;
        var userPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IELTop", "content", "Audio", file);
        if (File.Exists(userPath)) return userPath;
        return Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", file);
    }

    private void Refresh() => StateChanged?.Invoke();

    public void Dispose()
    {
        _ticker?.Dispose();
        _speakingCts?.Dispose();
        _gradingCts?.Dispose();
    }
}
