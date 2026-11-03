using SnelToets.Core.Models;

namespace SnelToets.Core.Services;

/// <summary>
/// Adaptive question picking for practice series.
/// Priority buckets: (0) answered wrongly last time, (1) never seen, (2) seen and answered correctly,
/// least recently seen first. Within a bucket questions whose difficulty fits the current mastery win,
/// with some randomness so two series are never identical.
/// </summary>
public static class QuestionSelector
{
    public sealed record Candidate(int QuestionId, int Difficulty, QuestionStat? Stat);

    /// <summary>Difficulty that fits a mastery score: below 40 easy, below 70 medium, otherwise hard.</summary>
    public static int TargetDifficulty(double mastery) => mastery < 40 ? 1 : mastery < 70 ? 2 : 3;

    public static int Bucket(QuestionStat? stat) =>
        stat is null || stat.Seen == 0 ? 1 : !stat.LastCorrect ? 0 : 2;

    /// <summary>Picks up to <paramref name="count"/> distinct questions and returns them in the order to show them.</summary>
    public static List<int> Select(IReadOnlyList<Candidate> candidates, int count, double mastery, Random rng)
    {
        if (count <= 0 || candidates.Count == 0) return [];
        var target = TargetDifficulty(mastery);

        var distinct = candidates.GroupBy(c => c.QuestionId).Select(g => g.First()).ToList();
        // Recency rank (0 = longest ago) for questions that were seen before.
        var seen = distinct.Where(c => c.Stat is { Seen: > 0 }).OrderBy(c => c.Stat!.LastSeen).ToList();
        var recency = seen.Select((c, i) => (c.QuestionId, Rank: seen.Count <= 1 ? 0 : (double)i / (seen.Count - 1)))
                          .ToDictionary(x => x.QuestionId, x => x.Rank);

        var picked = distinct
            .Select(c =>
            {
                var bucket = Bucket(c.Stat);
                var fit = Math.Abs(Math.Clamp(c.Difficulty, 1, 3) - target);
                var key = bucket == 2
                    ? recency[c.QuestionId] * 3 + fit * 0.5 + rng.NextDouble() * 0.75
                    : fit + rng.NextDouble() * 1.5;
                return (c, bucket, key);
            })
            .OrderBy(x => x.bucket).ThenBy(x => x.key)
            .Take(count)
            .Select(x => x.c)
            .ToList();

        return Order(picked, mastery, rng).Select(c => c.QuestionId).ToList();
    }

    /// <summary>Low mastery: roughly easy → hard; otherwise a plain shuffle.</summary>
    public static List<Candidate> Order(List<Candidate> picked, double mastery, Random rng)
    {
        if (mastery < MasteryRules.AttentionThreshold)
            return picked.Select(c => (c, key: c.Difficulty + rng.NextDouble() * 1.2))
                         .OrderBy(x => x.key).Select(x => x.c).ToList();
        var arr = picked.ToArray();
        rng.Shuffle(arr);
        return [.. arr];
    }
}
