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

    /// <summary>
    /// New score after one answer. Correct answers move the score towards 100, mistakes pull it down
    /// noticeably, so you only reach the green zone by answering consistently well.
    /// TODO(core agent): implement and unit test.
    /// </summary>
    public static double Update(double score, bool correct, int difficulty) => throw new NotImplementedException();
}
