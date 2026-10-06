namespace IELTop.Models;

using System.Collections.Generic;

public static class IeltsBandDescriptors
{
    public sealed record CriterionDescriptor(
        string CriterionCode,
        string CriterionName,
        IReadOnlyDictionary<double, string> Bands);

    public static readonly CriterionDescriptor FluencyAndCoherence = new(
        "FC",
        "Fluency and Coherence",
        new Dictionary<double, string>
        {
            [9.0] = "Speaks fluently with only rare repetition or self-correction; any hesitation is content-related rather than to find words or grammar.",
            [8.0] = "Speaks fluently with only occasional repetition or self-correction; hesitation is usually content-related.",
            [7.0] = "Speaks at length without noticeable effort or loss of coherence; uses connectives and discourse markers with some flexibility.",
            [6.0] = "Is willing to speak at length, though may lose coherence at times due to occasional repetition or hesitation.",
            [5.0] = "Usually maintains flow of speech but uses repetition or slow speech; produces simple speech fluently.",
            [4.0] = "Cannot respond without noticeable pauses and speaks slowly with frequent repetition.",
        });

    public static readonly CriterionDescriptor LexicalResourceSpeaking = new(
        "LR",
        "Lexical Resource",
        new Dictionary<double, string>
        {
            [9.0] = "Uses vocabulary with full flexibility and precision in all topics. Uses idiomatic language naturally.",
            [8.0] = "Uses a wide vocabulary resource readily and flexibly to convey precise meaning.",
            [7.0] = "Uses vocabulary resource flexibly to discuss a variety of topics with less common words.",
            [6.0] = "Has a wide enough vocabulary to discuss topics at length and make meaning clear.",
            [5.0] = "Manages to talk about familiar and unfamiliar topics but with limited flexibility.",
            [4.0] = "Is able to talk about familiar topics but conveys basic meaning only.",
        });

    public static readonly CriterionDescriptor GrammaticalRangeAndAccuracySpeaking = new(
        "GRA",
        "Grammatical Range and Accuracy",
        new Dictionary<double, string>
        {
            [9.0] = "Uses a full range of structures naturally and appropriately; consistently accurate.",
            [8.0] = "Uses a wide range of structures flexibly; majority of sentences error-free.",
            [7.0] = "Uses a range of complex structures with some flexibility; frequent error-free sentences.",
            [6.0] = "Uses a mix of simple and complex structures; mistakes rarely cause comprehension problems.",
            [5.0] = "Produces basic sentence forms with reasonable accuracy; complex forms usually contain errors.",
            [4.0] = "Produces basic sentence forms and some correct simple sentences; subordinate structures rare.",
        });

    public static readonly CriterionDescriptor Pronunciation = new(
        "PR",
        "Pronunciation",
        new Dictionary<double, string>
        {
            [9.0] = "Uses a full range of pronunciation features with precision and subtlety; effortless to understand.",
            [8.0] = "Uses a wide range of pronunciation features; sustained flexible use; easy to understand throughout.",
            [7.0] = "Controls features well; generally easy to understand throughout.",
            [6.0] = "Uses a range of pronunciation features with mixed control; individual word slips reduce clarity at times.",
            [5.0] = "Basic speech is intelligible but individual words or sounds are frequently mispronounced.",
            [4.0] = "Uses a limited range of pronunciation features; frequent mispronunciations cause difficulty.",
        });

    public static string BuildOfficialSpeakingRubricPrompt()
    {
        return @"OFFICIAL IELTS SPEAKING BAND DESCRIPTORS (British Council / IDP / Cambridge):
1. Fluency & Coherence (FC):
   - Band 9: Fluent, effortless flow, topic fully developed, rare hesitation (content only).
   - Band 8: Fluent, occasional repetition/correction, topics developed coherently.
   - Band 7: Speaks at length without effort, flexible connectives/discourse markers, some hesitation.
   - Band 6: Willing to speak at length, occasional loss of coherence, mixed connectives.
   - Band 5: Slow speech/repetition to maintain flow, overuses simple connectives, complexity causes pauses.
   - Band 4: Noticeable pauses, slow speech, links basic sentences, frequent breakdowns.

2. Lexical Resource (LR):
   - Band 9: Full flexibility and precision, natural and accurate idiomatic expressions throughout.
   - Band 8: Wide resource, skilful use of less common/idiomatic words, effective paraphrase.
   - Band 7: Flexible vocabulary, uses less common words/collocations with minor slips, effective paraphrase.
   - Band 6: Sufficient vocabulary for length and meaning, generally succeeds in paraphrasing.
   - Band 5: Limited flexibility, basic vocabulary for familiar topics, struggles with unfamiliar.
   - Band 4: Basic vocabulary only, frequent errors in word choice, rare paraphrase.

3. Grammatical Range & Accuracy (GRA):
   - Band 9: Full range of complex structures naturally, consistently accurate except native slips.
   - Band 8: Wide range of structures, majority of sentences error-free.
   - Band 7: Range of complex structures, frequent error-free sentences, minor persistent errors.
   - Band 6: Mix of simple and complex, frequent errors in complex forms but rarely blocks meaning.
   - Band 5: Basic sentence forms accurate, complex structures contain errors and cause confusion.
   - Band 4: Simple sentences only, frequent errors in basic forms, subordinate clauses rare.

4. Pronunciation (PR):
   - Band 9: Full range of features (intonation, rhythm, stress), effortless to understand.
   - Band 8: Wide range of features, sustained flexible use, easy to understand throughout.
   - Band 7: Generally easy to understand, controls features well, minimal L1 accent interference.
   - Band 6: Range of features with mixed control, individual word mispronunciations reduce clarity at times.
   - Band 5: Basic features, frequent mispronunciations of words/sounds reduce intelligibility.
   - Band 4: Limited features, frequent mispronunciations cause listener strain.";
    }
}
