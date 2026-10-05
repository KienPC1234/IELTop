using System.Text;

namespace IELTop.Services.Ai;

/// <summary>
/// Prompts for the Study screen: the tutor chat and the practice builder.
/// Kept beside the other prompts so wording is reviewed in one place. Every
/// prompt that asks for data ends with an exact JSON shape, like the rest of
/// the app, and the model is told to stay on the supplied lesson text.
/// </summary>
public static class StudyPrompts
{
    /// <summary>How the chat model should behave: a patient tutor, grounded in the lesson.</summary>
    public const string ChatSystem =
        "You are a patient, precise IELTS tutor for a Vietnamese learner. " +
        "Answer in clear, simple English. Explain the grammar, vocabulary, or strategy the student asks about. " +
        "Use the lesson sources when they are given and say which unit or section a fact comes from. " +
        "If the sources do not cover the question, say so and answer from general IELTS knowledge. " +
        "Keep replies short and practical, with a short example. Never invent official IELTS scores.";

    public static string BuildChat(string question, string sources)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(sources))
        {
            builder.AppendLine("Lesson sources (use these first and name them):");
            builder.AppendLine(sources);
            builder.AppendLine();
        }
        builder.AppendLine("Student question:");
        builder.AppendLine(question);
        builder.AppendLine();
        builder.AppendLine("Answer as the tutor. Plain text, no markdown fences.");
        return builder.ToString();
    }

    /// <summary>
    /// Asks for a practice set as strict JSON. The shape matches PracticeQuestion
    /// so the host can store it without guessing.
    /// </summary>
    public static string BuildPractice(string topic, string skill, string sourceText, int count, string difficulty)
    {
        var clipped = sourceText.Length > 6000 ? sourceText[..6000] + "..." : sourceText;
        var depth = difficulty?.Trim().ToLowerInvariant() switch
        {
            "easy" => "recognition and recall: ask for a fact, a form, or a word that appears in the material.",
            "hard" => "transformation and analysis: ask the student to reword, correct, or compare two items from the material.",
            _ => "application: ask the student to use the material in a short sentence or a normal exam task.",
        };
        return new StringBuilder()
            .AppendLine($"Build {count} IELTS practice questions for this student.")
            .AppendLine($"Skill: {skill}. Topic: {topic}. Difficulty: {depth}")
            .AppendLine("Base every question on the lesson material below. Do not invent facts.")
            .AppendLine("When the material shows the task and its answers, keep the same fact and wording, only vary the question.")
            .AppendLine("Every answer you give must be findable in the material.")
            .AppendLine()
            .AppendLine("Lesson material:")
            .AppendLine(clipped)
            .AppendLine()
            .AppendLine("Kinds allowed:")
            .AppendLine("- gap: one blank, one accepted answer, gapAnswer with | between accepted forms.")
            .AppendLine("- completion: a short note or table with one to three blanks; put the note in prompt and the answers in gapAnswer (| between them).")
            .AppendLine("- single: one correct choice, 3 or 4 options, correctKey is the option key.")
            .AppendLine("- multiple: two or more correct choices, correctKey lists the keys joined by comma.")
            .AppendLine("- short: a written answer of a few words, gapAnswer holds accepted forms.")
            .AppendLine("- match: place 4 to 6 labels into the right answer. Fill \"rows\" with {\"label\",\"answer\"} pairs; leave correctKey and gapAnswer empty.")
            .AppendLine("Every question needs a short explanation of why the answer is right.")
            .AppendLine("Return only JSON with this shape:")
            .AppendLine("{")
            .AppendLine("  \"title\": \"short set title\",")
            .AppendLine("  \"questions\": [")
            .AppendLine("    { \"kind\": \"gap\", \"prompt\": \"The library opens at ___.\", \"options\": [], \"rows\": [], \"correctKey\": \"\", \"gapAnswer\": \"8|8:00|8am\", \"explanation\": \"The clip says eight in the morning.\" },")
            .AppendLine("    { \"kind\": \"single\", \"prompt\": \"What time does the library close on weekdays?\", \"options\": [{\"key\":\"A\",\"text\":\"8 pm\"},{\"key\":\"B\",\"text\":\"10 pm\"}], \"rows\": [], \"correctKey\": \"B\", \"gapAnswer\": \"\", \"explanation\": \"The speaker says it closes at ten at night.\" },")
            .AppendLine("    { \"kind\": \"match\", \"prompt\": \"Match each word with its meaning.\", \"options\": [], \"rows\": [{\"label\":\"arctic\",\"answer\":\"very cold\"},{\"label\":\"drought\",\"answer\":\"a long dry spell\"}], \"correctKey\": \"\", \"gapAnswer\": \"\", \"explanation\": \"Both meanings come from the vocabulary sheet.\" }")
            .AppendLine("  ]")
            .AppendLine("}")
            .ToString();
    }

    /// <summary>How a tutor tool should behave: grounded, short, in the student's language where asked.</summary>
    public const string ToolSystem =
        "You are a precise IELTS tutor tool for a Vietnamese learner. " +
        "Use the given lesson sources when they are present and name the unit or section. " +
        "If the sources do not cover the request, say so, then answer from general IELTS knowledge. " +
        "Be compact and practical. Never invent official IELTS scores.";

    /// <summary>
    /// One tool request: a named tool, the student's input, and the lesson
    /// sources that were found for it. Each tool maps to one instruction so the
    /// wording of every tool is reviewed here.
    /// </summary>
    public static string BuildTool(string tool, string input, string sources)
    {
        var instruction = (tool ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "lookup" => "Explain the word, phrase, or grammar point the student gives: part of speech, meaning, an example, and one common mistake.",
            "translate" => "Translate the student's English into natural Vietnamese, then back translate the key phrase into English.",
            "summarize" => "Summarise the lesson source in five short bullet points the student can revise from.",
            "flashcards" => "Make six flashcards from the lesson source. Each line is Front :: Back, one per line, no extra text.",
            "fix" => "Correct the student's English. Show the corrected sentence, then one line on what was wrong.",
            "paraphrase" => "Rewrite the student's sentence in two different IELTS-friendly ways, then name the words you changed.",
            _ => "Answer the student's request about the lesson.",
        };

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(sources))
        {
            builder.AppendLine("Lesson sources (use these first and name them):");
            builder.AppendLine(sources);
            builder.AppendLine();
        }
        builder.AppendLine(instruction);
        builder.AppendLine();
        builder.AppendLine("Student input:");
        builder.AppendLine(input);
        builder.AppendLine();
        builder.AppendLine("Answer in plain text, no markdown fences.");
        return builder.ToString();
    }

    /// <summary>Asks for a plain-language explanation of one wrong answer.</summary>
    public static string BuildExplain(string prompt, string correct, string chosen, string sourceText)
    {
        var clipped = sourceText.Length > 3000 ? sourceText[..3000] + "..." : sourceText;
        return
            "Lesson material:\n" + clipped + "\n\n" +
            "Question:\n" + prompt + "\n\n" +
            $"Correct answer: {correct}. The student wrote: {chosen}.\n\n" +
            "Reply in exactly two short lines. Line 1: why the correct answer is right, with a short quote. " +
            "Line 2: one tip to get it right next time.";
    }
}
