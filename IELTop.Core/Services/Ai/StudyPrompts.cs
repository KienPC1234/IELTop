using System;
using System.Globalization;
using System.Text;
using IELTop.Models;

namespace IELTop.Services.Ai;

/// <summary>
/// Prompts for the Study screen: the interactive tutor chat, rich exercise generation,
/// and official 4-criteria IELTS speaking evaluation.
/// </summary>
public static class StudyPrompts
{
    /// <summary>System prompt for the interactive IELTS tutor.</summary>
    public const string ChatSystem =
        "You are an interactive, encouraging, and highly competent personal IELTS tutor. " +
        "You actively teach the student using authentic IELTS lesson materials provided in the context. " +
        "Language adaptation: Respond naturally in the same language the student communicates in (e.g., if the student asks in Vietnamese, explain clearly in Vietnamese while preserving IELTS terminology, target vocabulary, and practice examples in English; if the student asks in English, respond in English). " +
        "Keep explanations concise, well-structured, and practical (maximum 3 to 5 clear points with real examples). " +
        "Format your answer using clean Markdown (headers, bullet lists, bold text, tables). " +
        "Interactive tools: When the student asks to practice, take a quiz, test their understanding, or when completing an explanation of an IELTS concept, call the tool `generate_interactive_exercise` to provide an interactive exercise card. You may also call `search_curriculum` to retrieve authentic lessons and `get_vocabulary_entry` for dictionary definitions. " +
        "Bands mentioned are practice estimates, never official IELTS scores.";

    /// <summary>System prompt for official 4-criteria speaking evaluation.</summary>
    public const string SpeakingFeedbackSystem =
        "You are an expert IELTS Speaking examiner assessing candidate responses strictly according to the " +
        "Official IELTS Speaking Band Descriptors (British Council / IDP / Cambridge). " +
        "Evaluate the candidate across all 4 criteria: Fluency and Coherence (FC), Lexical Resource (LR), " +
        "Grammatical Range and Accuracy (GRA), and Pronunciation (PR). " +
        "Always return ONLY a valid JSON object matching the requested schema with zero markdown conversational filler.";

    public static string BuildSpeakingFeedback(
        string mode, string part, string cue, string transcript,
        double wpm, double speechSeconds, int pauses, double meanPause,
        double clarity, double accuracy, double meanGop)
    {
        var sb = new StringBuilder();
        sb.AppendLine(IeltsBandDescriptors.BuildOfficialSpeakingRubricPrompt());
        sb.AppendLine();
        sb.AppendLine("CANDIDATE ATTEMPT EVIDENCE:");
        sb.AppendLine($"Task / Part: {part} ({mode})");
        sb.AppendLine($"Prompt / Topic: {(string.IsNullOrWhiteSpace(cue) ? "Free practice" : cue)}");
        sb.AppendLine();
        sb.AppendLine("Transcript (Speech recognition text):");
        sb.AppendLine(string.IsNullOrWhiteSpace(transcript) ? "(No speech detected)" : transcript);
        sb.AppendLine();
        sb.AppendLine("Objective Acoustic Measurements (measured directly from audio):");
        sb.AppendLine($"- Pace: {wpm.ToString("0", CultureInfo.InvariantCulture)} words per minute over {speechSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s speaking duration.");
        sb.AppendLine($"- Pauses (silence >= 250ms): {pauses} pause(s), mean pause length {meanPause.ToString("0.0", CultureInfo.InvariantCulture)}s.");
        if (accuracy > 0)
            sb.AppendLine($"- Read-aloud phoneme match: {accuracy.ToString("0", CultureInfo.InvariantCulture)}%, mean GOP score: {meanGop.ToString("0.00", CultureInfo.InvariantCulture)}.");
        else if (clarity > 0)
            sb.AppendLine($"- Voice clarity score: {clarity.ToString("0.00", CultureInfo.InvariantCulture)} (0.0 to 1.0 scale).");
        sb.AppendLine();
        sb.AppendLine("REQUIRED JSON OUTPUT SCHEMA:");
        sb.AppendLine("""
        {
          "overallBand": 6.5,
          "fcBand": 6.0,
          "fcFeedback": "Detailed observation on pace, flow, discourse markers and hesitation.",
          "lrBand": 7.0,
          "lrFeedback": "Detailed observation on vocabulary range, collocations, idiomatic use.",
          "graBand": 6.0,
          "graFeedback": "Detailed observation on sentence structures, clauses, and grammatical accuracy.",
          "prBand": 6.5,
          "prFeedback": "Detailed observation on intelligibility, stress, rhythm and sound clarity.",
          "keyErrors": [
            {"quote": "exact words spoken", "correction": "corrected sentence", "reason": "why this is incorrect"}
          ],
          "upgrades": [
            {"original": "simple phrase", "upgraded": "academic/native phrase (Band 7+)", "note": "usage note"}
          ],
          "drills": [
            "1 concrete exercise for tomorrow (e.g. shadow specific sentence, practice relative clauses)"
          ]
        }
        """);

        return sb.ToString();
    }

    /// <summary>System prompt for generating interactive study questions.</summary>
    public const string InteractiveExerciseSystem =
        "You are an expert IELTS curriculum designer. Generate authentic, interactive practice exercises strictly " +
        "based on the provided lesson text or requested IELTS topic. " +
        "Return ONLY a parseable JSON object matching the specified schema. No markdown fences or intro text.";

    public static string BuildInteractiveExercise(
        string topic, string skill, string sourceText, string kind = "single", string difficulty = "standard")
    {
        var clipped = sourceText.Length > 6000 ? sourceText[..6000] + "..." : sourceText;

        return $$"""
        Generate 1 high-quality IELTS practice question based on the material below.
        Target Skill: {{skill}}
        Topic: {{topic}}
        Difficulty: {{difficulty}}
        Question Kind: {{kind}} (Allowed kinds: single, multiple, gap, completion, tfng, match, reorder, error_fix, paraphrase)

        Lesson material:
        {{clipped}}

        REQUIRED JSON OUTPUT SCHEMA:
        {
          "kind": "{{kind}}",
          "prompt": "Clear instruction or question prompt",
          "contextText": "Optional excerpt or sentence for the question",
          "options": [
            {"key": "A", "text": "Option text"},
            {"key": "B", "text": "Option text"},
            {"key": "C", "text": "Option text"},
            {"key": "D", "text": "Option text"}
          ],
          "matchRows": [
            {"label": "Item 1", "answer": "Match 1"},
            {"label": "Item 2", "answer": "Match 2"}
          ],
          "reorderTokens": ["words", "in", "scrambled", "order"],
          "modelAnswer": "Complete correct target sentence (especially for reorder)",
          "correctKey": "A",
          "gapAnswer": "accepted answer|alternative accepted answer",
          "explanation": "Detailed explanation citing evidence from the material."
        }
        """;
    }

    /// <summary>Prompts the AI to generate a targeted exercise for an audio segment.</summary>
    public static string BuildAudioSegmentDrill(string sentence, string reason, string drillType)
    {
        return $$"""
        A candidate said the following sentence during their IELTS speaking practice:
        Sentence: "{{sentence}}"
        Identified Issue: {{reason}}
        Requested Drill: {{drillType}} (shadowing, grammar_correction, vocabulary_upgrade)

        Return ONLY a JSON object:
        {
          "drillType": "{{drillType}}",
          "targetSentence": "{{sentence}}",
          "explanation": "Brief tip on how to say or formulate this better.",
          "modelAnswer": "The ideal Band 8.0+ spoken version of this sentence.",
          "gapExercise": "Optional fill-in-the-blank test sentence with ___."
        }
        """;
    }

    public static string BuildChat(string question, string sources, string unitMap = "")
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(unitMap))
        {
            sb.AppendLine(unitMap.TrimEnd());
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(sources))
        {
            sb.AppendLine("Authentic lesson curriculum context (ground your explanation on this):");
            sb.AppendLine(sources);
            sb.AppendLine();
        }
        sb.AppendLine("Student query:");
        sb.AppendLine(question);
        sb.AppendLine();
        sb.AppendLine("Answer as the tutor naturally matching the student's query language (e.g., in Vietnamese if the user asked in Vietnamese, in English if asked in English). Keep English quotations and IELTS target vocabulary accurate. Structure your response with clean markdown and bullet points where helpful.");
        sb.AppendLine("When you use a lesson source, name it with its cite key like [unit-5#grammar-1] so the student can open that exact section. When the question fits a unit topic better than the quoted sources, say which unit to read next.");
        return sb.ToString();
    }

    public const string PracticeSystem =
        "You are an expert IELTS test developer. " +
        "Your task is to generate authentic IELTS practice questions strictly based on the provided lesson text. " +
        "You must respond with ONLY a valid, parseable JSON object matching the requested schema. " +
        "Do not include any conversational greeting, markdown commentary, or text before or after the JSON. " +
        "Do not include trailing commas in arrays or objects. Ensure all quotes inside text values are properly escaped.";

    public static string BuildPractice(string topic, string skill, string sourceText, int count, string difficulty)
    {
        var clipped = sourceText.Length > 8000 ? sourceText[..8000] + "..." : sourceText;
        var depth = difficulty?.Trim().ToLowerInvariant() switch
        {
            "easy" => "recognition and recall: direct facts or clear forms from the text.",
            "hard" => "inference, analysis, or transformation: paraphrasing, distinguishing subtle details.",
            _ => "standard exam application: authentic IELTS question style.",
        };

        var isWriting = string.Equals(skill, "Writing", StringComparison.OrdinalIgnoreCase);

        var sb = new StringBuilder()
            .AppendLine($"Generate {count} IELTS practice questions based SOLELY on the lesson text below.")
            .AppendLine($"Skill: {skill}. Topic: {topic}. Difficulty: {depth}")
            .AppendLine("Rules:")
            .AppendLine("1. Grounding: Every question and answer must be strictly supported by the text below.");

        if (isWriting)
        {
            sb.AppendLine("2. Target Skill is IELTS WRITING:");
            sb.AppendLine("   - Use 'short' or 'rewrite' kinds where candidates must practice writing sentences or short paragraphs according to specific task criteria.");
            sb.AppendLine("   - Exercises must include: paraphrasing a prompt using complex grammar, writing an overview for Task 1, writing a clear topic sentence for a Task 2 body paragraph, writing a supporting explanation with cohesion, or upgrading informal sentences to Band 8+ academic register.");
            sb.AppendLine("   - In 'gapAnswer', provide a Band 8.5+ model response sentence or paragraph.");
        }
        else
        {
            sb.AppendLine("2. Allowed question kinds:");
            sb.AppendLine("   - single: Multiple-choice question with 4 options labeled A, B, C, D in options array, and correctKey set to the letter.");
            sb.AppendLine("   - gap: Sentence with ___ (three underscores) for the blank. Put the exact word(s) in gapAnswer. Separate alternative accepted forms with |.");
            sb.AppendLine("   - completion: Short summary or note with ___. Put accepted answers in gapAnswer.");
            sb.AppendLine("   - match: Matching exercise. Fill rows with objects containing label and answer.");
        }

        sb.AppendLine("3. Explanations: Every question must include a concise explanation quoting evidence from the text.")
          .AppendLine("4. Output: Return ONLY raw JSON. No markdown code blocks, no backticks, no comments.")
          .AppendLine()
          .AppendLine("Lesson material:")
          .AppendLine(clipped)
          .AppendLine()
          .AppendLine("Required JSON Schema:")
          .AppendLine("{")
          .AppendLine("  \"title\": \"Descriptive Set Title\",")
          .AppendLine("  \"questions\": [")
          .AppendLine("    {")
          .AppendLine("      \"kind\": \"" + (isWriting ? "short" : "single") + "\",")
          .AppendLine("      \"prompt\": \"" + (isWriting ? "Write a Band 8+ overview sentence summarizing the key trend shown in the data: ..." : "According to the text, why did the author conclude...?") + "\",")
          .AppendLine("      \"options\": [],")
          .AppendLine("      \"rows\": [],")
          .AppendLine("      \"correctKey\": \"" + (isWriting ? "" : "B") + "\",")
          .AppendLine("      \"gapAnswer\": \"" + (isWriting ? "Overall, it is immediately evident that while renewable energy consumption grew markedly, fossil fuel reliance exhibited a consistent downward trajectory." : "") + "\",")
          .AppendLine("      \"explanation\": \"A Band 8+ overview identifies major opposing trends without citing specific figures.\"")
          .AppendLine("    }")
          .AppendLine("  ]")
          .AppendLine("}");

        return sb.ToString();
    }

    public const string ToolSystem =
        "You are an IELTS tutor tool. Provide compact, practical explanations for the student's request.";

    public static string BuildTool(string tool, string input, string sources)
    {
        return $"""
        Tool: {tool}
        Input: {input}
        Sources: {sources}
        Provide a concise, practical response.
        """;
    }

    public static string BuildExplain(string prompt, string correct, string chosen, string sourceText)
    {
        return $"""
        Question: {prompt}
        Correct answer: {correct}. Student wrote: {chosen}.
        Explain in two concise lines why the correct answer is right and one tip.
        """;
    }

    /// <summary>System prompt for grading open-ended IELTS writing responses.</summary>
    public const string EvaluateWritingSystem =
        "You are an expert IELTS Writing Examiner. Evaluate the student's answer accurately, " +
        "remembering that multiple grammatical structures and paraphrases can be valid. " +
        "Return ONLY a JSON object. No preamble, no markdown fences.";

    public static string BuildEvaluateWriting(string prompt, string contextText, string targetAnswer, string studentResponse)
    {
        return $$"""
        You are an expert IELTS Writing Examiner. Evaluate the following student's response for an IELTS writing exercise.
        Exercise Prompt: {{prompt}}
        {{(string.IsNullOrWhiteSpace(contextText) ? "" : $"Context / Source sentence: {contextText}\n")}}
        Model Answer / Target Pattern: {{targetAnswer}}
        Student's Submitted Response: "{{studentResponse}}"

        Instructions:
        1. Check if the student's response accurately answers the prompt with acceptable grammar, vocabulary, and IELTS academic style.
        2. In IELTS Writing, multiple valid paraphrases or grammatical variations exist. Do not mark an answer wrong simply because it uses different words from the model answer, as long as the meaning is accurate and grammar is sound.
        3. Provide specific, constructive feedback and an estimated band level.

        Return ONLY a JSON object:
        {
          "isCorrect": true,
          "score": 1.0,
          "bandEstimate": "Band 7.5",
          "feedback": "Concise feedback on grammar, structure, and vocabulary choice.",
          "improvedVersion": "An enhanced Band 8+ version if applicable"
        }
        """;
    }
}
