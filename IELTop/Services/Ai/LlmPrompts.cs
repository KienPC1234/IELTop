using System.Text;

namespace IELTop.Services.Ai;

/// <summary>
/// Every language model prompt in one place, so prompt wording can be
/// reviewed and edited without touching call logic. Methods take plain
/// values and return the exact user message sent to the model. The system
/// prompt default lives here too; a custom one from Settings overrides it.
/// </summary>
public static class LlmPrompts
{
    public const string DefaultSystemPrompt =
        "You are a strict but fair IELTS examiner and tutor. " +
        "You give practical feedback a learner can act on. " +
        "You never invent official scores. Estimated bands are guidance for practice only. " +
        "Reply using the requested format exactly.";

    public const string TestConnectionPrompt = "Reply with the single word: ok";

    public static string WritingStance(MarkingStrictness strictness) => strictness switch
    {
        MarkingStrictness.Lenient => "Mark like an encouraging examiner. Reward what works, note only clear errors.",
        MarkingStrictness.Strict => "Mark like a very strict examiner. Penalize every error in task response, cohesion, words, and grammar. Do not inflate scores.",
        _ => "Mark like a typical IELTS examiner. Be fair and specific."
    };

    public static string SpeakingStance(MarkingStrictness strictness) => strictness switch
    {
        MarkingStrictness.Lenient => "Be encouraging, reward communication.",
        MarkingStrictness.Strict => "Be very strict. Penalize hesitation, limited words, grammar slips, and unclear sounds.",
        _ => "Be fair like a typical examiner."
    };

    public static bool IsTask1(string taskPrompt) =>
        taskPrompt.Contains("Task 1", StringComparison.OrdinalIgnoreCase)
        || taskPrompt.Contains("letter", StringComparison.OrdinalIgnoreCase)
        || taskPrompt.Contains("chart", StringComparison.OrdinalIgnoreCase)
        || taskPrompt.Contains("graph", StringComparison.OrdinalIgnoreCase)
        || taskPrompt.Contains("diagram", StringComparison.OrdinalIgnoreCase);

    public static string BuildWritingPrompt(
        string taskPrompt, string essay, int minimumWords,
        string strictnessLabel, string stance)
    {
        bool isTask1 = IsTask1(taskPrompt);
        string firstCriterion = isTask1 ? "task_achievement" : "task_response";
        string firstMeaning = isTask1
            ? "Task Achievement: did it cover every bullet or describe every main trend with an overview."
            : "Task Response: did it answer every part, take a clear position, and extend ideas with examples.";
        string firstWhyKey = isTask1 ? "task_achievement_why" : "task_response_why";
        string firstLabel = isTask1 ? "Task Achievement" : "Task Response";
        string firstBands = isTask1
            ? "9: fully satisfies every requirement, clear overview, every detail relevant. "
                + "8: covers every requirement, clear overview, rare irrelevant detail. "
                + "7: covers every requirement, clear overview, minor lapses. "
                + "6: addresses every requirement, overview present but incomplete, some irrelevant detail. "
                + "5: partly addresses, overview missing or unclear, ideas underdeveloped. "
                + "4 or below: task barely attempted, no overview, off topic passages."
            : "9: fully addresses every part, full extended position throughout. "
                + "8: addresses every part, well developed position, rare lapses. "
                + "7: addresses every part, clear position extended with ideas, minor lapses. "
                + "6: addresses every part but development uneven, position clear but conclusions weak. "
                + "5: partly addresses, position unclear, ideas limited and undeveloped. "
                + "4 or below: barely responds, no position, ideas irrelevant or repeated.";

        return new StringBuilder()
            .AppendLine("Task prompt:")
            .AppendLine(taskPrompt)
            .AppendLine()
            .AppendLine($"Minimum words: {minimumWords}")
            .AppendLine($"Marking level: {strictnessLabel}. {stance}")
            .AppendLine()
            .AppendLine("Student essay:")
            .AppendLine(essay)
            .AppendLine()
            .AppendLine("Score like an IELTS examiner against the official band tables below. Match each")
            .AppendLine("criterion to the closest band description, then give the band. Half bands only")
            .AppendLine("(for example 5.5, 6.0, 6.5, never 6.3 or 6.7, never above 9.0).")
            .AppendLine()
            .AppendLine($"{firstLabel} bands: {firstBands}")
            .AppendLine("Coherence and Cohesion bands: 9: full logical flow, paragraphing perfect, linking natural. "
                + "8: logical flow, paragraphing sufficient, rare linking slips. "
                + "7: clear progression, paragraphing logical, some overused linking. "
                + "6: progression visible, paragraphing faulty at times, mechanical linking. "
                + "5: weak organisation, poor paragraphing, linking repetitive or wrong. "
                + "4 or below: no logical organisation, no paragraphing control.")
            .AppendLine("Lexical Resource bands: 9: wide range, natural collocation, rare slips only. "
                + "8: wide range, skilful collocation, rare errors. "
                + "7: sufficient range, some less common words, occasional errors. "
                + "6: adequate range, errors in spelling and word formation but meaning clear. "
                + "5: limited range, frequent errors, meaning sometimes obscured. "
                + "4 or below: minimal words, errors block meaning.")
            .AppendLine("Grammatical Range and Accuracy bands: 9: full range of structures, error free. "
                + "8: wide range, mostly error free sentences. "
                + "7: variety of complex structures, frequent error free sentences. "
                + "6: mix of simple and complex, errors rarely block meaning. "
                + "5: few complex sentences, frequent errors, punctuation weak. "
                + "4 or below: only basic sentences, errors block meaning.")
            .AppendLine()
            .AppendLine("The estimated_band must equal the mean of the four criteria.")
            .AppendLine("If the essay is under the minimum word count, penalize the first criterion.")
            .AppendLine("For each criterion also write one short why line that quotes the essay and names")
            .AppendLine("the matched band description.")
            .AppendLine("Quote short phrases from the essay in strengths and improvements.")
            .AppendLine("The corrected_excerpt must rewrite one weak sentence from the essay, not invent a new topic.")
            .AppendLine("Also give band_low and band_high around the estimate to show examiner variation (usually 0.5 each way).")
            .AppendLine()
            .AppendLine("Return only JSON with this shape, band table first, feedback after:")
            .AppendLine("{")
            .AppendLine("  \"estimated_band\": 6.5,")
            .AppendLine("  \"band_low\": 6.0,")
            .AppendLine("  \"band_high\": 7.0,")
            .AppendLine($"  \"{firstCriterion}\": 6.5,")
            .AppendLine($"  \"{firstWhyKey}\": \"one sentence: matched description plus a short quote\",")
            .AppendLine("  \"coherence\": 6.0,")
            .AppendLine("  \"coherence_why\": \"one sentence: matched description plus a short quote\",")
            .AppendLine("  \"lexical_resource\": 6.5,")
            .AppendLine("  \"lexical_why\": \"one sentence: matched description plus a short quote\",")
            .AppendLine("  \"grammar\": 6.0,")
            .AppendLine("  \"grammar_why\": \"one sentence: matched description plus a short quote\",")
            .AppendLine("  \"summary\": \"two short sentences\",")
            .AppendLine("  \"strengths\": [\"point with a short quote\"],")
            .AppendLine("  \"improvements\": [\"point with a fix and a short quote\"],")
            .AppendLine("  \"corrected_excerpt\": \"rewrite one weak sentence\"")
            .AppendLine("}")
            .AppendLine($"Note: the first criterion key is task_response for Task 2 and task_achievement for Task 1 with why key {firstWhyKey}. Use the matching pair above.")
            .ToString();
    }

    public static string BuildSpeakingPrompt(
        string cue, string transcript, string partInfo, int spokenSeconds,
        int wordCount, string strictnessLabel, string stance)
    {
        var context = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(partInfo)) context.AppendLine($"Part: {partInfo}");
        context.AppendLine($"Transcript words: {wordCount}");
        if (spokenSeconds > 0) context.AppendLine($"Spoke for about {spokenSeconds} seconds");

        return new StringBuilder()
            .AppendLine($"Speaking cue: {cue}")
            .AppendLine()
            .Append(context.ToString())
            .AppendLine()
            .AppendLine("Student transcript (typed from their speech):")
            .AppendLine(transcript.Trim())
            .AppendLine()
            .AppendLine($"Marking level: {strictnessLabel}. {stance}")
            .AppendLine("Score against the official band tables below. Match each criterion to the closest")
            .AppendLine("band description, then give the band. Half bands only (never 6.3, never above 9.0).")
            .AppendLine()
            .AppendLine("Fluency and Coherence bands: 9: fluent with rare repetition, full coherent development. "
                + "8: fluent, rare hesitation, full development. "
                + "7: some hesitation and repetition, ideas developed with lapses. "
                + "6: hesitation and repetition, linking visible but faulty, ideas partly developed. "
                + "5: slow speech, frequent repetition, poor linking, short answers. "
                + "4 or below: long pauses, broken speech, no coherent answer.")
            .AppendLine("Lexical Resource bands: 9: full range with natural idiom and collocation. "
                + "8: wide range, idiom with rare slips. "
                + "7: sufficient range, some less common words, occasional wrong choice. "
                + "6: adequate range, errors in word choice but meaning clear. "
                + "5: limited words, frequent wrong choice, meaning sometimes lost. "
                + "4 or below: only basic words, errors block meaning.")
            .AppendLine("Grammatical Range and Accuracy bands: 9: full range, error free. "
                + "8: wide range, mostly error free. "
                + "7: variety of complex structures, frequent error free sentences. "
                + "6: mix of simple and complex, errors rarely block meaning. "
                + "5: few complex sentences, frequent errors. "
                + "4 or below: only basic structures, errors block meaning.")
            .AppendLine("Pronunciation bands: score from text alone, so stay conservative "
                + "(6.5 or below unless the transcript is flawless). 9: effortless intelligibility, natural stress. "
                + "8: easy to understand, rare lapses. "
                + "7: generally clear, some stress errors. "
                + "6: intelligible with strain, stress errors. "
                + "5: often hard to follow. 4 or below: unintelligible stretches.")
            .AppendLine()
            .AppendLine("estimated_band is the mean of the four. Give band_low and band_high 0.5 each way.")
            .AppendLine("For each criterion also write one short why line that quotes the transcript and names")
            .AppendLine("the matched band description.")
            .AppendLine("Quote short phrases from the transcript in strengths and improvements.")
            .AppendLine("Return only JSON, band table first, feedback after: {\"estimated_band\": 6.5, \"band_low\": 6.0, \"band_high\": 7.0,")
            .AppendLine(" \"fluency\": 6.0, \"fluency_why\": \"one sentence: matched description plus a short quote\",")
            .AppendLine(" \"lexical_resource\": 6.5, \"lexical_why\": \"one sentence: matched description plus a short quote\",")
            .AppendLine(" \"grammar\": 6.0, \"grammar_why\": \"one sentence: matched description plus a short quote\",")
            .AppendLine(" \"pronunciation\": 6.0, \"pronunciation_why\": \"one sentence: matched description plus a short quote\",")
            .AppendLine(" \"summary\": \"two sentences\", \"strengths\": [\"point with a short quote\"], \"improvements\": [\"point with a fix and a short quote\"]}")
            .ToString();
    }

    public static string BuildSuggestTopic(string skill)
    {
        var clean = string.IsNullOrWhiteSpace(skill) ? "Writing" : skill.Trim();
        return $"Suggest one {clean} mock test prompt for IELTS Academic practice. " +
               "Reply with exactly two lines. Line 1: topic. Line 2: the full task or cue " +
               "(for Writing include the word minimum, for Speaking include the bullet points).";
    }

    public static string BuildPickTest(string catalog, string history) =>
        "Parts catalog (paper|partId, skill, type, minutes):\n" + catalog + "\n\n" +
        "Past results (scope and band range):\n" + history + "\n\n" +
        "Build a full test from the catalog below. Listening parts only work " +
        "inside a multi skill test, so include them only with other skills. " +
        "Reply with only JSON: {\"ids\": [\"paper|part\", ...], \"reason\": \"one sentence\"}. " +
        "Order the ids in test order: Listening, Reading, Writing, Speaking. " +
        "Favor the weakest skills from the history. Pick 4 to 8 parts.";

    public static string BuildReviewPaper(string outline) =>
        "Review this IELTS mock test paper for practice use:\n" + outline +
        "\nReply with only JSON: " +
        "{\"score\": 7.5, \"strengths\": [\"point\"], \"fixes\": [\"point\"]}. " +
        "Score 0 to 10 for skill balance, clear instructions, and sane answer keys. " +
        "Keep each list to three short points.";

    public static string BuildDraftPaper(string clippedText, string want) =>
        new StringBuilder()
            .AppendLine("Turn the text below into one IELTop mock test paper.")
            .AppendLine($"Focus skill: {want}. One part is enough unless the text clearly has more.")
            .AppendLine("Keep the material close to the source text. Write clear questions.")
            .AppendLine("Kinds: choice needs 3 to 4 options with correctKey, gap needs gapAnswer with | alternatives, match needs bank plus matchRows.")
            .AppendLine("Writing and Speaking parts have no questions, only material and instructions.")
            .AppendLine("Return only JSON with this shape:")
            .AppendLine("{\"title\": \"...\", \"source\": \"User import, edited before saving.\", \"category\": \"Imported\", \"level\": \"\", \"tags\": [],")
            .AppendLine(" \"parts\": [{\"id\": \"R1\", \"skill\": \"Reading\", \"title\": \"...\", \"topic\": \"...\", \"taskType\": \"Passage 1\",")
            .AppendLine(" \"material\": \"...\", \"instructions\": \"...\", \"audioFile\": \"\", \"minutes\": 12, \"prepSeconds\": 0,")
            .AppendLine(" \"questions\": [{\"number\": 1, \"kind\": \"choice\", \"prompt\": \"...\", \"options\": [{\"key\": \"A\", \"text\": \"...\"}], \"correctKey\": \"A\", \"gapAnswer\": \"\", \"bank\": [], \"matchRows\": [], \"explanation\": \"...\"}]}]}")
            .AppendLine()
            .AppendLine("Source text:")
            .AppendLine(clippedText)
            .ToString();

    public static string BuildReadingExplanation(string passage, string question, string picked, string correctKey)
    {
        var clippedPassage = passage.Length > 4000 ? passage[..4000] + "..." : passage;
        return
            "Passage:\n" + clippedPassage + "\n\n" +
            "Question (with options, bank, or accepted answers):\n" + question + "\n\n" +
            $"The student answered: {picked}. The correct answer is: {correctKey}.\n" +
            "Reply in exactly three short lines:\n" +
            "Line 1: Correct: why the correct answer is right, with a short quote from the passage.\n" +
            "Line 2: Wrong: why the student answer is wrong or what trap they fell into.\n" +
            "Line 3: Tip: one sentence on how to avoid this next time.";
    }

    public static string BuildListeningExplanation(string transcript, string question, string picked, string correctAnswer)
    {
        var clippedScript = transcript.Length > 4000 ? transcript[..4000] + "..." : transcript;
        return
            "Listening transcript (what the student heard once):\n" + clippedScript + "\n\n" +
            "Question (with options or accepted answers):\n" + question + "\n\n" +
            $"The student answered: {picked}. The correct answer is: {correctAnswer}.\n" +
            "Reply in exactly three short lines:\n" +
            "Line 1: Hear: the exact words in the transcript that give the answer, as a short quote.\n" +
            "Line 2: Miss: why the student likely missed it (sound alike word, plural, word limit, paraphrase, distractor).\n" +
            "Line 3: Tip: one sentence on what to listen for next time.";
    }

    public static string BuildCoachSpeaking(string target, string heardPhonemes, string mistakes) =>
        "A student practised this sentence aloud.\n" +
        $"Target: {target}\n" +
        $"Sounds the model heard: {heardPhonemes}\n" +
        $"Detected mistakes: {mistakes}\n\n" +
        "Give three short, specific tips to fix the pronunciation. " +
        "Mention the mouth position when helpful. Keep it under 120 words.";

    /// <summary>
    /// Asks a vision model to read one imported picture: transcribe printed
    /// text exactly, or describe a chart so it works as a Writing Task 1 task.
    /// </summary>
    public static string BuildReadImage(string fileName, string skill) =>
        "Read this picture for an IELTS student. " +
        $"File: {fileName}. Planned skill: {skill}.\n" +
        "If the picture holds printed text, transcribe it exactly, keeping line breaks. " +
        "If it is a chart, graph, table, diagram, or map, describe it in words: the title, " +
        "every axis and unit, every value, and the main trends or stages. " +
        "If it is a photo with no text, describe what it shows in two sentences. " +
        "Plain text only, no markdown fences.";
}
