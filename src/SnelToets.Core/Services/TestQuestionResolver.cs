using Microsoft.EntityFrameworkCore;
using SnelToets.Core.Data;
using SnelToets.Core.Models;

namespace SnelToets.Core.Services;

/// <summary>Works out which questions an attempt at a <see cref="Test"/> contains.</summary>
internal static class TestQuestionResolver
{
    /// <summary>The fixed questions (in SortOrder), the pool of topic questions that are not fixed, and how many to draw from it.</summary>
    internal sealed record Plan(List<int> FixedIds, List<int> Pool, int DrawCount)
    {
        public int Total => FixedIds.Count + DrawCount;
    }

    public static async Task<Plan> PlanAsync(SnelToetsDbContext db, int testId, CancellationToken ct)
    {
        var fixedIds = await db.TestQuestions.AsNoTracking()
            .Where(tq => tq.TestId == testId)
            .OrderBy(tq => tq.SortOrder).ThenBy(tq => tq.QuestionId)
            .Select(tq => tq.QuestionId)
            .ToListAsync(ct);
        var topicIds = await db.TestTopics.AsNoTracking()
            .Where(tt => tt.TestId == testId).Select(tt => tt.TopicId).ToListAsync(ct);
        var test = await db.Tests.AsNoTracking().FirstOrDefaultAsync(t => t.Id == testId, ct)
                   ?? throw new KeyNotFoundException($"Toets {testId} bestaat niet.");

        var fixedSet = fixedIds.ToHashSet();
        var pool = topicIds.Count == 0
            ? []
            : (await db.Questions.AsNoTracking()
                .Where(q => topicIds.Contains(q.TopicId))
                .OrderBy(q => q.Id)
                .Select(q => q.Id)
                .ToListAsync(ct))
              .Where(id => !fixedSet.Contains(id)).ToList();

        int draw = test.RandomQuestionCount is int n
            ? Math.Min(Math.Max(0, n), pool.Count)
            : fixedIds.Count == 0 ? pool.Count : 0;
        return new Plan(fixedIds, pool, draw);
    }

    /// <summary>Fixed ids first (in order), then a random draw from the pool.</summary>
    public static List<int> Resolve(Plan plan, Random rng)
    {
        var result = new List<int>(plan.FixedIds);
        var pool = plan.Pool.ToArray();
        rng.Shuffle(pool);
        result.AddRange(pool.Take(plan.DrawCount));
        return result;
    }
}
