namespace SnelToets.Core.Services;

/// <summary>Shared thresholds for the score bars ("scorebalken").</summary>
public static class MasteryRules
{
    /// <summary>A topic counts as mastered from this score (green bar).</summary>
    public const double MasteredThreshold = 80;
    /// <summary>Below this score a topic gets an exclamation mark in the study advice.</summary>
    public const double AttentionThreshold = 50;
    /// <summary>The start test can never set a score above this: a few questions do not prove mastery.</summary>
    public const double PlacementCap = 60;
    public const int DefaultSeriesLength = 15;

    /// <summary>Minimum number of points a wrong answer costs.</summary>
    public const double MinPenalty = 8;
    /// <summary>Fraction of the current score a wrong answer costs (when larger than <see cref="MinPenalty"/>).</summary>
    public const double PenaltyFraction = 0.25;

    /// <summary>
    /// Fraction of the remaining distance to 100 that a correct answer closes, per difficulty (1, 2, 3).
    /// From 0, consecutive correct answers reach 80 after 14 (easy), 12 (medium) or 10 (hard) questions.
    /// </summary>
    public static double Gain(int difficulty) => Math.Clamp(difficulty, 1, 3) switch
    {
        1 => 0.11,
        2 => 0.13,
        _ => 0.15
    };

    /// <summary>
    /// New score after one answer. Correct answers move the score towards 100, mistakes pull it down
    /// noticeably, so you only reach the green zone by answering consistently well.
    /// <list type="bullet">
    /// <item>correct: <c>score + (100 − score) × gain</c>, gain = 0.11 / 0.13 / 0.15 for difficulty 1 / 2 / 3;</item>
    /// <item>wrong: <c>score − max(8, score × 0.25)</c> (one mistake at 85 drops to 63.75: no longer mastered).</item>
    /// </list>
    /// The result is clamped to 0–100.
    /// </summary>
    public static double Update(double score, bool correct, int difficulty)
    {
        score = Math.Clamp(double.IsNaN(score) ? 0 : score, 0, 100);
        var next = correct
            ? score + (100 - score) * Gain(difficulty)
            : score - Math.Max(MinPenalty, score * PenaltyFraction);
        return Math.Clamp(next, 0, 100);
    }

    /// <summary>
    /// Update used during a start test (placement): correct answers can raise the score at most to
    /// <see cref="PlacementCap"/> (an existing higher score is kept, not raised); mistakes count as usual.
    /// </summary>
    public static double UpdatePlacement(double score, bool correct, int difficulty)
    {
        var next = Update(score, correct, difficulty);
        if (!correct) return next;
        return score >= PlacementCap ? score : Math.Min(next, PlacementCap);
    }

    public static bool IsMastered(double score) => score >= MasteredThreshold;
}
