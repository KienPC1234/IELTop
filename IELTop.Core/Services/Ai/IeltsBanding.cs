namespace IELTop.Services.Ai;

/// <summary>
/// How strictly the AI examiner marks. Lenient is encouraging,
/// Standard matches a typical examiner, Strict penalizes small errors.
/// </summary>
public enum MarkingStrictness
{
    Lenient = 0,
    Standard = 1,
    Strict = 2
}

/// <summary>
/// A band estimate as a range, because two examiners rarely agree
/// on one exact number. Low and High are half band steps from 0 to 9.
/// </summary>
public sealed record BandRange(double Low, double High)
{
    public string Label => $"{Low:0.0} to {High:0.0}";
    public double Mid => Math.Round((Low + High) / 2.0 * 2.0, MidpointRounding.AwayFromZero) / 2.0;
}

/// <summary>
/// Official IELTS banding rules in one place. Listening and Reading map
/// raw correct counts to bands. Writing and Speaking average four criteria
/// and snap to half bands. All bands here are practice estimates.
/// </summary>
public static class IeltsBanding
{
    public static double RoundHalf(double band)
        => Math.Round(Math.Clamp(band, 0.0, 9.0) * 2.0, MidpointRounding.AwayFromZero) / 2.0;

    /// <summary>
    /// Builds a range around a mid band to show examiner variation.
    /// Strict marking shifts the range down, Lenient shifts it up.
    /// </summary>
    public static BandRange ToRange(double midBand, MarkingStrictness strictness)
    {
        var mid = RoundHalf(midBand);
        double low = mid - 0.5;
        double high = mid + 0.5;
        switch (strictness)
        {
            case MarkingStrictness.Strict:
                low = mid - 1.0;
                high = mid;
                break;
            case MarkingStrictness.Lenient:
                low = mid;
                high = mid + 1.0;
                break;
        }
        return new BandRange(RoundHalf(low), RoundHalf(high));
    }

    /// <summary>
    /// Academic Reading/Listening raw score (out of 40) to band.
    /// General Training Reading uses a slightly harsher table.
    /// </summary>
    public static double RawToBand(int correct, int total, bool generalReading = false)
    {
        if (total <= 0) return 0;
        double ratio = (double)correct / total;
        // 40 question table, scaled to any total by ratio.
        if (generalReading)
        {
            return ratio switch
            {
                >= 0.975 => 9.0,
                >= 0.925 => 8.5,
                >= 0.875 => 8.0,
                >= 0.825 => 7.5,
                >= 0.750 => 7.0,
                >= 0.675 => 6.5,
                >= 0.575 => 6.0,
                >= 0.500 => 5.5,
                >= 0.425 => 5.0,
                >= 0.325 => 4.5,
                >= 0.225 => 4.0,
                >= 0.150 => 3.5,
                _ => 3.0
            };
        }
        return ratio switch
        {
            >= 0.975 => 9.0,
            >= 0.925 => 8.5,
            >= 0.875 => 8.0,
            >= 0.800 => 7.5,
            >= 0.725 => 7.0,
            >= 0.650 => 6.5,
            >= 0.575 => 6.0,
            >= 0.500 => 5.5,
            >= 0.425 => 5.0,
            >= 0.325 => 4.5,
            >= 0.225 => 4.0,
            _ => 3.5
        };
    }

    public static string StrictnessLabel(MarkingStrictness s) => s switch
    {
        MarkingStrictness.Lenient => "Lenient",
        MarkingStrictness.Strict => "Strict",
        _ => "Standard"
    };

    /// <summary>Short public criteria summary shown next to every AI score.</summary>
    public static string WritingCriteriaHint =>
        "Marked on Task Achievement, Coherence and Cohesion, Lexical Resource, Grammatical Range and Accuracy.";

    public static string SpeakingCriteriaHint =>
        "Marked on Fluency and Coherence, Lexical Resource, Grammatical Range and Accuracy, Pronunciation.";

    public static IReadOnlyList<string> WritingBandTable() => new[]
    {
        "9: Full response, natural cohesion, wide precise vocabulary, almost no errors.",
        "8: Full response, well organized, flexible vocabulary, rare errors.",
        "7: Clear position, logical flow, some less common words, some errors.",
        "6: Addresses the task, basic organization, simple but adequate words, visible errors.",
        "5: Partial answer, weak organization, limited words, frequent errors.",
        "4 and below: Off topic or hard to follow, very limited language."
    };

    public static IReadOnlyList<string> SpeakingBandTable() => new[]
    {
        "9: Natural flow, full flexibility, precise words, almost no errors, clear sounds.",
        "8: Smooth with rare slips, wide vocabulary, mostly accurate grammar.",
        "7: Some hesitation, flexible words, some complex sentences, clear accent.",
        "6: Willing to speak at length, simple and some complex language, some errors.",
        "5: Slow with repeats, basic words, frequent errors, partly unclear.",
        "4 and below: Long pauses, very limited language, hard to understand."
    };
}
