using Microsoft.EntityFrameworkCore;
using SnelToets.Core.Data;
using SnelToets.Core.Models;

namespace SnelToets.Core.Services;

/// <summary>
/// CRUD for subjects, topics and questions.
/// The DbContext lives as long as a Blazor circuit, so reads use AsNoTracking and writes start and end
/// with a clean change tracker: callers always get fresh data and never share tracked instances.
/// </summary>
public class ContentService(SnelToetsDbContext db) : IContentService
{
    private readonly SnelToetsDbContext _db = db;

    // ---------- Subjects ----------

    public Task<List<Subject>> GetSubjectsAsync(CancellationToken ct = default) =>
        _db.Subjects.AsNoTracking()
            .Include(s => s.Topics.OrderBy(t => t.SortOrder).ThenBy(t => t.Name))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public Task<Subject?> GetSubjectAsync(int id, CancellationToken ct = default) =>
        _db.Subjects.AsNoTracking()
            .Include(s => s.Topics.OrderBy(t => t.SortOrder).ThenBy(t => t.Name))
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Subject> CreateSubjectAsync(Subject subject, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        RequireName(subject.Name, "Geef het vak een naam.");
        _db.ChangeTracker.Clear();
        var entity = new Subject
        {
            Name = subject.Name.Trim(),
            Description = Blank(subject.Description),
            Color = Blank(subject.Color),
            Source = subject.Source,
            CreatedAt = DateTime.UtcNow
        };
        _db.Subjects.Add(entity);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return entity;
    }

    public async Task<Subject> UpdateSubjectAsync(Subject subject, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        RequireName(subject.Name, "Geef het vak een naam.");
        _db.ChangeTracker.Clear();
        var entity = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == subject.Id, ct)
                     ?? throw new KeyNotFoundException($"Vak {subject.Id} bestaat niet.");
        entity.Name = subject.Name.Trim();
        entity.Description = Blank(subject.Description);
        entity.Color = Blank(subject.Color);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return entity;
    }

    public async Task DeleteSubjectAsync(int id, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        await _db.Subjects.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
    }

    // ---------- Topics ----------

    public Task<Topic?> GetTopicAsync(int id, CancellationToken ct = default) =>
        _db.Topics.AsNoTracking().Include(t => t.Subject).FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<List<Topic>> GetTopicsAsync(int subjectId, CancellationToken ct = default) =>
        _db.Topics.AsNoTracking()
            .Where(t => t.SubjectId == subjectId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(ct);

    public async Task<Topic> CreateTopicAsync(Topic topic, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(topic);
        RequireName(topic.Name, "Geef het onderwerp een naam.");
        _db.ChangeTracker.Clear();
        if (!await _db.Subjects.AnyAsync(s => s.Id == topic.SubjectId, ct))
            throw new ValidationException(["Het gekozen vak bestaat niet."]);

        var max = await _db.Topics.Where(t => t.SubjectId == topic.SubjectId).MaxAsync(t => (int?)t.SortOrder, ct);
        var entity = new Topic
        {
            SubjectId = topic.SubjectId,
            Name = topic.Name.Trim(),
            TheoryMarkdown = topic.TheoryMarkdown,
            SortOrder = (max ?? 0) + 1,
            Source = topic.Source,
            CreatedAt = DateTime.UtcNow
        };
        _db.Topics.Add(entity);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return entity;
    }

    public async Task<Topic> UpdateTopicAsync(Topic topic, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(topic);
        RequireName(topic.Name, "Geef het onderwerp een naam.");
        _db.ChangeTracker.Clear();
        var entity = await _db.Topics.FirstOrDefaultAsync(t => t.Id == topic.Id, ct)
                     ?? throw new KeyNotFoundException($"Onderwerp {topic.Id} bestaat niet.");
        entity.Name = topic.Name.Trim();
        entity.TheoryMarkdown = topic.TheoryMarkdown;
        entity.SortOrder = topic.SortOrder;
        if (topic.SubjectId > 0 && topic.SubjectId != entity.SubjectId &&
            await _db.Subjects.AnyAsync(s => s.Id == topic.SubjectId, ct))
            entity.SubjectId = topic.SubjectId;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return entity;
    }

    public async Task DeleteTopicAsync(int id, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        await _db.Topics.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task<Dictionary<int, int>> CountQuestionsPerTopicAsync(int subjectId, CancellationToken ct = default)
    {
        var rows = await _db.Topics.AsNoTracking()
            .Where(t => t.SubjectId == subjectId)
            .Select(t => new { t.Id, Count = t.Questions.Count })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.Count);
    }

    // ---------- Questions ----------

    public async Task<List<Question>> GetQuestionsAsync(int? topicId = null, string? search = null, int skip = 0, int take = 200, CancellationToken ct = default)
    {
        var query = _db.Questions.AsNoTracking().AsQueryable();
        if (topicId is int tid) query = query.Where(q => q.TopicId == tid);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(q => q.Prompt.ToLower().Contains(term) ||
                                     (q.Context != null && q.Context.ToLower().Contains(term)));
        }
        return await query
            .Include(q => q.Options.OrderBy(o => o.SortOrder))
            .OrderBy(q => q.TopicId).ThenBy(q => q.Id)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 10_000))
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    public Task<Question?> GetQuestionAsync(int id, CancellationToken ct = default) =>
        _db.Questions.AsNoTracking()
            .Include(q => q.Options.OrderBy(o => o.SortOrder))
            .Include(q => q.Topic)
            .FirstOrDefaultAsync(q => q.Id == id, ct);

    public async Task<Question> CreateQuestionAsync(Question question, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        Normalise(question);
        ThrowIfInvalid(question);
        _db.ChangeTracker.Clear();
        if (!await _db.Topics.AnyAsync(t => t.Id == question.TopicId, ct))
            throw new ValidationException(["Het gekozen onderwerp bestaat niet."]);

        var now = DateTime.UtcNow;
        var entity = new Question
        {
            TopicId = question.TopicId,
            Type = question.Type,
            Prompt = question.Prompt,
            Context = question.Context,
            Tip = question.Tip,
            Explanation = question.Explanation,
            Difficulty = question.Difficulty,
            Source = question.Source,
            ExternalId = question.ExternalId,
            AcceptedAnswers = [.. question.AcceptedAnswers],
            Options = question.Options.Select((o, i) => new AnswerOption
            {
                Text = o.Text,
                IsCorrect = o.IsCorrect,
                Feedback = o.Feedback,
                SortOrder = i
            }).ToList(),
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Questions.Add(entity);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return (await GetQuestionAsync(entity.Id, ct))!;
    }

    public async Task<Question> UpdateQuestionAsync(Question question, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        Normalise(question);
        ThrowIfInvalid(question);
        _db.ChangeTracker.Clear();
        var entity = await _db.Questions.Include(q => q.Options).FirstOrDefaultAsync(q => q.Id == question.Id, ct)
                     ?? throw new KeyNotFoundException($"Vraag {question.Id} bestaat niet.");
        if (question.TopicId != entity.TopicId)
        {
            if (!await _db.Topics.AnyAsync(t => t.Id == question.TopicId, ct))
                throw new ValidationException(["Het gekozen onderwerp bestaat niet."]);
            entity.TopicId = question.TopicId;
        }

        entity.Type = question.Type;
        entity.Prompt = question.Prompt;
        entity.Context = question.Context;
        entity.Tip = question.Tip;
        entity.Explanation = question.Explanation;
        entity.Difficulty = question.Difficulty;
        if (question.ExternalId is not null) entity.ExternalId = Blank(question.ExternalId);
        entity.AcceptedAnswers = [.. question.AcceptedAnswers];
        entity.UpdatedAt = DateTime.UtcNow;
        ReplaceOptions(_db, entity, question.Options);

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return (await GetQuestionAsync(entity.Id, ct))!;
    }

    public async Task DeleteQuestionAsync(int id, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        await _db.Questions.Where(q => q.Id == id).ExecuteDeleteAsync(ct);
    }

    // ---------- helpers ----------

    /// <summary>
    /// Makes the tracked <paramref name="entity"/>'s options equal to <paramref name="incoming"/> (in that order):
    /// options with a known Id are updated, unknown ones added, missing ones removed.
    /// </summary>
    internal static void ReplaceOptions(SnelToetsDbContext db, Question entity, IReadOnlyList<AnswerOption> incoming)
    {
        var existing = entity.Options.ToDictionary(o => o.Id);
        var keep = new HashSet<int>();
        for (var i = 0; i < incoming.Count; i++)
        {
            var src = incoming[i];
            if (src.Id > 0 && existing.TryGetValue(src.Id, out var opt) && keep.Add(src.Id))
            {
                opt.Text = src.Text;
                opt.IsCorrect = src.IsCorrect;
                opt.Feedback = src.Feedback;
                opt.SortOrder = i;
            }
            else
            {
                entity.Options.Add(new AnswerOption
                {
                    Text = src.Text,
                    IsCorrect = src.IsCorrect,
                    Feedback = src.Feedback,
                    SortOrder = i
                });
            }
        }
        foreach (var removed in existing.Values.Where(o => !keep.Contains(o.Id)).ToList())
        {
            entity.Options.Remove(removed);
            db.AnswerOptions.Remove(removed);
        }
    }

    /// <summary>Trims text, orders options by SortOrder and drops the collection that does not apply to the type.</summary>
    internal static void Normalise(Question q)
    {
        q.Prompt = (q.Prompt ?? "").Trim();
        q.Context = Blank(q.Context);
        q.Tip = Blank(q.Tip);
        q.Explanation = Blank(q.Explanation);
        q.Options = (q.Options ?? []).OrderBy(o => o.SortOrder).ToList();
        foreach (var o in q.Options)
        {
            o.Text = (o.Text ?? "").Trim();
            o.Feedback = Blank(o.Feedback);
        }
        q.AcceptedAnswers = (q.AcceptedAnswers ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList();
        if (q.Type == QuestionType.FillIn) q.Options = [];
        else q.AcceptedAnswers = [];
    }

    private static void ThrowIfInvalid(Question q)
    {
        var errors = QuestionValidator.Validate(q);
        if (errors.Count > 0) throw new ValidationException(errors);
    }

    private static void RequireName(string? name, string message)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ValidationException([message]);
    }

    internal static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
