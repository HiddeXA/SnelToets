using Microsoft.EntityFrameworkCore;
using SnelToets.Core.Data;
using SnelToets.Core.Models;

namespace SnelToets.Core.Services;

/// <summary>CRUD for tests ("toetsen"). Same change-tracker discipline as <see cref="ContentService"/>.</summary>
public class TestService(SnelToetsDbContext db) : ITestService
{
    private readonly SnelToetsDbContext _db = db;

    public Task<List<Test>> GetTestsAsync(CancellationToken ct = default) =>
        _db.Tests.AsNoTracking()
            .Include(t => t.Topics).ThenInclude(tt => tt.Topic)
            .Include(t => t.Questions.OrderBy(q => q.SortOrder))
            .OrderBy(t => t.Title)
            .AsSplitQuery()
            .ToListAsync(ct);

    public Task<Test?> GetTestAsync(int id, CancellationToken ct = default) =>
        _db.Tests.AsNoTracking()
            .Include(t => t.Questions.OrderBy(q => q.SortOrder)).ThenInclude(tq => tq.Question)
                .ThenInclude(q => q!.Options.OrderBy(o => o.SortOrder))
            .Include(t => t.Topics).ThenInclude(tt => tt.Topic)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Test> CreateTestAsync(Test test, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(test);
        Validate(test);
        _db.ChangeTracker.Clear();
        var entity = new Test { CreatedAt = DateTime.UtcNow, Source = test.Source };
        CopyScalars(test, entity);
        var (questionIds, topicIds) = await ExistingIdsAsync(test, ct);
        entity.Questions = questionIds.Select((qid, i) => new TestQuestion { QuestionId = qid, SortOrder = i }).ToList();
        entity.Topics = topicIds.Select(tid => new TestTopic { TopicId = tid }).ToList();
        _db.Tests.Add(entity);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return (await GetTestAsync(entity.Id, ct))!;
    }

    public async Task<Test> UpdateTestAsync(Test test, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(test);
        Validate(test);
        _db.ChangeTracker.Clear();
        var entity = await _db.Tests.Include(t => t.Questions).Include(t => t.Topics)
                         .AsSplitQuery()
                         .FirstOrDefaultAsync(t => t.Id == test.Id, ct)
                     ?? throw new KeyNotFoundException($"Toets {test.Id} bestaat niet.");
        CopyScalars(test, entity);
        var (questionIds, topicIds) = await ExistingIdsAsync(test, ct);

        var order = questionIds.Select((qid, i) => (qid, i)).ToDictionary(x => x.qid, x => x.i);
        foreach (var tq in entity.Questions.ToList())
        {
            if (order.TryGetValue(tq.QuestionId, out var pos)) tq.SortOrder = pos;
            else { entity.Questions.Remove(tq); _db.TestQuestions.Remove(tq); }
        }
        var present = entity.Questions.Select(q => q.QuestionId).ToHashSet();
        foreach (var (qid, pos) in order.Where(kv => !present.Contains(kv.Key)))
            entity.Questions.Add(new TestQuestion { TestId = entity.Id, QuestionId = qid, SortOrder = pos });

        var wanted = topicIds.ToHashSet();
        foreach (var tt in entity.Topics.Where(t => !wanted.Contains(t.TopicId)).ToList())
        {
            entity.Topics.Remove(tt);
            _db.TestTopics.Remove(tt);
        }
        var presentTopics = entity.Topics.Select(t => t.TopicId).ToHashSet();
        foreach (var tid in topicIds.Where(t => !presentTopics.Contains(t)))
            entity.Topics.Add(new TestTopic { TestId = entity.Id, TopicId = tid });

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return (await GetTestAsync(entity.Id, ct))!;
    }

    public async Task DeleteTestAsync(int id, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        await _db.Tests.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task<int> CountQuestionsAsync(int testId, CancellationToken ct = default)
    {
        if (!await _db.Tests.AnyAsync(t => t.Id == testId, ct)) return 0;
        return (await TestQuestionResolver.PlanAsync(_db, testId, ct)).Total;
    }

    private static void CopyScalars(Test src, Test dst)
    {
        dst.Title = src.Title.Trim();
        dst.Description = ContentService.Blank(src.Description);
        dst.Mode = src.Mode;
        dst.TimeLimitMinutes = src.TimeLimitMinutes is > 0 ? src.TimeLimitMinutes : null;
        dst.PassPercentage = Math.Clamp(src.PassPercentage, 0, 100);
        dst.ShuffleQuestions = src.ShuffleQuestions;
        dst.ShuffleOptions = src.ShuffleOptions;
        dst.RandomQuestionCount = src.RandomQuestionCount is > 0 ? src.RandomQuestionCount : null;
    }

    private static void Validate(Test test)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(test.Title)) errors.Add("Geef de toets een titel.");
        if (test.PassPercentage is < 0 or > 100) errors.Add("Het cijfer om te slagen moet tussen 0 en 100 procent liggen.");
        if (test.TimeLimitMinutes is < 0) errors.Add("De tijdslimiet kan niet negatief zijn.");
        if (test.RandomQuestionCount is < 0) errors.Add("Het aantal willekeurige vragen kan niet negatief zijn.");
        if (errors.Count > 0) throw new ValidationException(errors);
    }

    /// <summary>Distinct, existing question ids (in SortOrder) and topic ids of the incoming test.</summary>
    private async Task<(List<int> QuestionIds, List<int> TopicIds)> ExistingIdsAsync(Test test, CancellationToken ct)
    {
        var qids = (test.Questions ?? []).OrderBy(q => q.SortOrder).Select(q => q.QuestionId).Distinct().ToList();
        var tids = (test.Topics ?? []).Select(t => t.TopicId).Distinct().ToList();
        var existingQ = (await _db.Questions.Where(q => qids.Contains(q.Id)).Select(q => q.Id).ToListAsync(ct)).ToHashSet();
        var existingT = (await _db.Topics.Where(t => tids.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct)).ToHashSet();
        return (qids.Where(existingQ.Contains).ToList(), tids.Where(existingT.Contains).ToList());
    }
}
