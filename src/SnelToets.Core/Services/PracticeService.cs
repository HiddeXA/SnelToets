using Microsoft.EntityFrameworkCore;
using SnelToets.Core.Data;
using SnelToets.Core.Models;

namespace SnelToets.Core.Services;

/// <summary>
/// Practice series, start test (placement), test attempts, grading and mastery.
/// The DbContext is scoped per Blazor circuit, so every public method works on a cleared change tracker
/// and reads with AsNoTracking: repeated calls always see the current database state.
/// </summary>
public class PracticeService(SnelToetsDbContext db) : IPracticeService
{
    private readonly SnelToetsDbContext _db = db;
    private static Random Rng => Random.Shared;

    public const string GenericTip = "Bekijk de theorie van dit onderwerp nog eens.";

    // ---------- Starting attempts ----------

    public async Task<Attempt> StartTopicPracticeAsync(Guid learnerId, int topicId, int count = MasteryRules.DefaultSeriesLength, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        var topic = await _db.Topics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == topicId, ct)
                    ?? throw new InvalidOperationException("Dit onderwerp bestaat niet.");
        var candidates = await CandidatesAsync(learnerId, [topicId], ct);
        if (candidates.Count == 0)
            throw new InvalidOperationException($"Er zijn nog geen vragen voor het onderwerp '{topic.Name}'.");

        var mastery = await ScoreAsync(learnerId, topicId, ct);
        var ids = QuestionSelector.Select(candidates[topicId], Math.Max(1, count), mastery, Rng);
        var attempt = new Attempt
        {
            LearnerId = learnerId,
            Kind = AttemptKind.TopicPractice,
            Mode = TestMode.Practice,
            SubjectId = topic.SubjectId,
            TopicId = topicId
        };
        return await SaveNewAttemptAsync(attempt, ids, shuffleOptions: true, ct);
    }

    public async Task<Attempt> StartMixPracticeAsync(Guid learnerId, int subjectId, int count = MasteryRules.DefaultSeriesLength, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        var topicIds = await TopicIdsAsync(subjectId, ct);
        var candidates = await CandidatesAsync(learnerId, topicIds, ct);
        if (candidates.Count == 0)
            throw new InvalidOperationException("Er zijn nog geen vragen voor dit vak.");

        var scores = await ScoresAsync(learnerId, topicIds, ct);
        var allocation = AllocateMix(candidates.ToDictionary(kv => kv.Key, kv => kv.Value.Count),
            scores, Math.Max(1, count), Rng);

        var ids = new List<int>();
        foreach (var (topicId, n) in allocation)
            ids.AddRange(QuestionSelector.Select(candidates[topicId], n, scores.GetValueOrDefault(topicId), Rng));
        var arr = ids.ToArray();
        Rng.Shuffle(arr);

        var attempt = new Attempt
        {
            LearnerId = learnerId,
            Kind = AttemptKind.MixPractice,
            Mode = TestMode.Practice,
            SubjectId = subjectId
        };
        return await SaveNewAttemptAsync(attempt, [.. arr], shuffleOptions: true, ct);
    }

    public async Task<Attempt> StartPlacementAsync(Guid learnerId, int subjectId, int perTopic = 2, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        var topicIds = await TopicIdsAsync(subjectId, ct);
        var candidates = await CandidatesAsync(learnerId, topicIds, ct);
        if (candidates.Count == 0)
            throw new InvalidOperationException("Er zijn nog geen vragen voor dit vak, dus er is geen starttoets.");

        var scores = await ScoresAsync(learnerId, topicIds, ct);
        var ids = new List<int>();
        foreach (var topicId in topicIds.Where(candidates.ContainsKey))
            ids.AddRange(QuestionSelector.Select(candidates[topicId], Math.Max(1, perTopic), scores.GetValueOrDefault(topicId), Rng));

        var attempt = new Attempt
        {
            LearnerId = learnerId,
            Kind = AttemptKind.Placement,
            Mode = TestMode.Practice,
            SubjectId = subjectId
        };
        return await SaveNewAttemptAsync(attempt, ids, shuffleOptions: true, ct);
    }

    public async Task<Attempt> StartTestAsync(Guid learnerId, int testId, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        var test = await _db.Tests.AsNoTracking().FirstOrDefaultAsync(t => t.Id == testId, ct)
                   ?? throw new InvalidOperationException("Deze toets bestaat niet.");
        var plan = await TestQuestionResolver.PlanAsync(_db, testId, ct);
        var ids = TestQuestionResolver.Resolve(plan, Rng);
        if (ids.Count == 0)
            throw new InvalidOperationException($"De toets '{test.Title}' bevat nog geen vragen.");
        if (test.ShuffleQuestions)
        {
            var arr = ids.ToArray();
            Rng.Shuffle(arr);
            ids = [.. arr];
        }

        var subjectIds = await _db.Questions.AsNoTracking().Where(q => ids.Contains(q.Id))
            .Select(q => q.Topic!.SubjectId).Distinct().ToListAsync(ct);
        var now = DateTime.UtcNow;
        var attempt = new Attempt
        {
            LearnerId = learnerId,
            Kind = AttemptKind.Test,
            Mode = test.Mode,
            TestId = test.Id,
            SubjectId = subjectIds.Count == 1 ? subjectIds[0] : null,
            StartedAt = now,
            Deadline = test.TimeLimitMinutes is > 0 ? now.AddMinutes(test.TimeLimitMinutes.Value) : null,
            PassPercentage = test.PassPercentage
        };
        return await SaveNewAttemptAsync(attempt, ids, test.ShuffleOptions, ct);
    }

    // ---------- Reading ----------

    public async Task<Attempt?> GetAttemptAsync(Guid attemptId, CancellationToken ct = default)
    {
        var attempt = await _db.Attempts.AsNoTracking()
            .Include(a => a.Items.OrderBy(i => i.SortOrder))
                .ThenInclude(i => i.Question).ThenInclude(q => q!.Options.OrderBy(o => o.SortOrder))
            .Include(a => a.Items.OrderBy(i => i.SortOrder)).ThenInclude(i => i.Question).ThenInclude(q => q!.Topic)
            .Include(a => a.Test)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is not null) attempt.Items = attempt.Items.OrderBy(i => i.SortOrder).ToList();
        return attempt;
    }

    public Task<List<Attempt>> GetRecentAttemptsAsync(Guid learnerId, int take = 10, CancellationToken ct = default) =>
        _db.Attempts.AsNoTracking()
            .Where(a => a.LearnerId == learnerId)
            .Include(a => a.Test)
            .OrderByDescending(a => a.StartedAt)
            .Take(Math.Max(1, take))
            .ToListAsync(ct);

    public Task<List<TopicMastery>> GetMasteryAsync(Guid learnerId, int? subjectId = null, CancellationToken ct = default)
    {
        var query = _db.TopicMasteries.AsNoTracking().Include(m => m.Topic).Where(m => m.LearnerId == learnerId);
        if (subjectId is int sid) query = query.Where(m => m.Topic!.SubjectId == sid);
        return query.OrderBy(m => m.Topic!.SortOrder).ThenBy(m => m.Topic!.Name).ToListAsync(ct);
    }

    // ---------- Answering ----------

    public async Task<AnswerResult> SubmitAnswerAsync(Guid attemptId, int itemId, AnswerSubmission answer, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(answer);
        _db.ChangeTracker.Clear();
        var attempt = await _db.Attempts.FirstOrDefaultAsync(a => a.Id == attemptId, ct)
                      ?? throw new InvalidOperationException("Deze poging bestaat niet.");
        var item = await _db.AttemptItems
                       .Include(i => i.Question).ThenInclude(q => q!.Options)
                       .FirstOrDefaultAsync(i => i.Id == itemId && i.AttemptId == attemptId, ct)
                   ?? throw new InvalidOperationException("Deze vraag hoort niet bij deze poging.");
        if (attempt.FinishedAt is not null)
            throw new InvalidOperationException("Deze poging is al afgerond.");
        if (attempt.Deadline is DateTime deadline && DateTime.UtcNow > deadline)
        {
            await FinishAsync(attemptId, ct);
            throw new InvalidOperationException("De tijd is om. Je toets is automatisch ingeleverd.");
        }

        var question = item.Question!;
        var exam = attempt.Mode == TestMode.Exam;

        if (!exam && item.AnsweredAt is not null)
        {
            // Practice items count once: return the stored result, do not touch mastery again.
            var current = await ScoreOrNullAsync(attempt.LearnerId, question.TopicId, ct);
            var completeAlready = !await _db.AttemptItems.AnyAsync(i => i.AttemptId == attemptId && i.AnsweredAt == null, ct);
            return BuildResult(item, question, item.IsCorrect == true, ParseIds(item.SelectedOptionIds),
                current, current, completeAlready, revealFeedback: true);
        }

        var isCorrect = AnswerChecker.IsCorrect(question, answer);
        var selected = question.Type == QuestionType.FillIn ? [] : (answer.OptionIds ?? []).Distinct().ToList();
        item.SelectedOptionIds = selected.Count > 0 ? string.Join(',', selected) : null;
        item.TextAnswer = question.Type == QuestionType.FillIn ? answer.Text?.Trim() : null;
        item.IsCorrect = isCorrect;
        item.AnsweredAt = DateTime.UtcNow;

        double? previous = null, next = null;
        if (!exam)
        {
            (previous, next) = await ApplyProgressAsync(attempt.LearnerId, question, isCorrect,
                attempt.Kind == AttemptKind.Placement, ct);
        }
        await _db.SaveChangesAsync(ct);

        var complete = !await _db.AttemptItems.AsNoTracking().AnyAsync(i => i.AttemptId == attemptId && i.AnsweredAt == null, ct);
        var result = BuildResult(item, question, isCorrect, selected, previous, next, complete, revealFeedback: !exam);
        _db.ChangeTracker.Clear();
        return result;
    }

    public async Task<AttemptResult> FinishAsync(Guid attemptId, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        var attempt = await _db.Attempts
                          .Include(a => a.Items).ThenInclude(i => i.Question).ThenInclude(q => q!.Topic)
                          .AsSplitQuery()
                          .FirstOrDefaultAsync(a => a.Id == attemptId, ct)
                      ?? throw new InvalidOperationException("Deze poging bestaat niet.");

        if (attempt.FinishedAt is null)
        {
            if (attempt.Mode == TestMode.Exam)
            {
                foreach (var item in attempt.Items.Where(i => i.AnsweredAt is not null && i.Question is not null).OrderBy(i => i.SortOrder))
                    await ApplyProgressAsync(attempt.LearnerId, item.Question!, item.IsCorrect == true, placement: false, ct);
            }
            attempt.TotalCount = attempt.Items.Count;
            attempt.CorrectCount = attempt.Items.Count(i => i.IsCorrect == true);
            attempt.FinishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        var topicIds = attempt.Items.Where(i => i.Question is not null).Select(i => i.Question!.TopicId).Distinct().ToList();
        var masteries = await _db.TopicMasteries.AsNoTracking()
            .Where(m => m.LearnerId == attempt.LearnerId && topicIds.Contains(m.TopicId))
            .ToDictionaryAsync(m => m.TopicId, m => m.Score, ct);

        var perTopic = attempt.Items
            .Where(i => i.Question is not null)
            .GroupBy(i => i.Question!.TopicId)
            .Select(g => new
            {
                g.Key,
                Topic = g.First().Question!.Topic,
                Result = new TopicResult(g.Key, g.First().Question!.Topic?.Name ?? "", g.Count(i => i.IsCorrect == true), g.Count(),
                    masteries.TryGetValue(g.Key, out var s) ? s : null)
            })
            .OrderBy(x => x.Topic?.SortOrder ?? 0).ThenBy(x => x.Result.TopicName)
            .Select(x => x.Result)
            .ToList();

        var total = attempt.TotalCount;
        var percentage = total == 0 ? 0 : Math.Round(attempt.CorrectCount * 100.0 / total, 1);
        bool? passed = attempt.Kind == AttemptKind.Test ? percentage >= attempt.PassPercentage : null;
        _db.ChangeTracker.Clear();
        return new AttemptResult(attempt.Id, attempt.CorrectCount, total, percentage, passed, perTopic);
    }

    // ---------- Advice & reset ----------

    public async Task<StudyAdvice> GetAdviceAsync(Guid learnerId, int subjectId, CancellationToken ct = default)
    {
        var topics = await _db.Topics.AsNoTracking()
            .Where(t => t.SubjectId == subjectId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new { t.Id, t.Name, t.SortOrder, Count = t.Questions.Count })
            .ToListAsync(ct);
        var topicIds = topics.Select(t => t.Id).ToList();
        var masteries = await _db.TopicMasteries.AsNoTracking()
            .Where(m => m.LearnerId == learnerId && topicIds.Contains(m.TopicId))
            .ToDictionaryAsync(m => m.TopicId, ct);
        var placementDone = await _db.Attempts.AsNoTracking().AnyAsync(a =>
            a.LearnerId == learnerId && a.Kind == AttemptKind.Placement && a.SubjectId == subjectId && a.FinishedAt != null, ct);

        var advice = topics.Select(t =>
        {
            masteries.TryGetValue(t.Id, out var m);
            var score = m?.Score ?? 0;
            var answered = m?.Answered ?? 0;
            var attention = score < MasteryRules.AttentionThreshold && (answered > 0 || (placementDone && t.Count > 0));
            return new TopicAdvice(t.Id, t.Name, score, attention, MasteryRules.IsMastered(score), t.Count, answered);
        }).ToList();

        var recommended = advice
            .Select((a, index) => (a, index))
            .Where(x => x.a.QuestionCount > 0 && !x.a.Mastered)
            .OrderBy(x => x.a.Mastery).ThenBy(x => x.index)
            .Select(x => (TopicAdvice?)x.a)
            .FirstOrDefault();

        string message;
        if (!advice.Any(a => a.QuestionCount > 0))
            message = "Er zijn nog geen vragen voor dit vak.";
        else if (!placementDone)
            message = "Maak eerst de starttoets, dan weet je waar je moet beginnen.";
        else if (recommended is null)
            message = "Gefeliciteerd, je beheerst alle onderwerpen! Houd het vast met een mixoefening of test jezelf met een toets.";
        else
            message = $"Begin met {recommended.Name}: daar valt de meeste winst te halen.";

        return new StudyAdvice(subjectId, advice, recommended?.TopicId, message, placementDone);
    }

    public async Task ResetTopicAsync(Guid learnerId, int topicId, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        await _db.TopicMasteries.Where(m => m.LearnerId == learnerId && m.TopicId == topicId).ExecuteDeleteAsync(ct);
        var questionIds = _db.Questions.Where(q => q.TopicId == topicId).Select(q => q.Id);
        await _db.QuestionStats.Where(s => s.LearnerId == learnerId && questionIds.Contains(s.QuestionId)).ExecuteDeleteAsync(ct);
    }

    // ---------- internals ----------

    /// <summary>
    /// Splits <paramref name="count"/> over the topics: every topic gets one question when possible, the rest goes
    /// by weighted draws where weight = (100 − mastery) + 10, so weak topics get more questions.
    /// </summary>
    internal static Dictionary<int, int> AllocateMix(IReadOnlyDictionary<int, int> available, IReadOnlyDictionary<int, double> scores, int count, Random rng)
    {
        var alloc = available.Keys.ToDictionary(k => k, _ => 0);
        double Weight(int topicId) => 100 - Math.Clamp(scores.GetValueOrDefault(topicId), 0, 100) + 10;
        var total = Math.Min(count, available.Values.Sum());

        // Base: one per topic, weakest first when there are more topics than questions.
        var order = available.Keys.OrderByDescending(k => Weight(k) * (0.75 + rng.NextDouble() * 0.5)).ToList();
        foreach (var topicId in order.Take(total)) alloc[topicId] = 1;

        var assigned = alloc.Values.Sum();
        while (assigned < total)
        {
            var open = alloc.Keys.Where(k => alloc[k] < available[k]).ToList();
            var sum = open.Sum(Weight);
            var r = rng.NextDouble() * sum;
            var chosen = open[^1];
            foreach (var k in open)
            {
                r -= Weight(k);
                if (r <= 0) { chosen = k; break; }
            }
            alloc[chosen]++;
            assigned++;
        }
        return alloc.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private async Task<List<int>> TopicIdsAsync(int subjectId, CancellationToken ct)
    {
        if (!await _db.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            throw new InvalidOperationException("Dit vak bestaat niet.");
        return await _db.Topics.AsNoTracking().Where(t => t.SubjectId == subjectId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name).Select(t => t.Id).ToListAsync(ct);
    }

    /// <summary>Candidates per topic (only topics that have questions).</summary>
    private async Task<Dictionary<int, List<QuestionSelector.Candidate>>> CandidatesAsync(Guid learnerId, List<int> topicIds, CancellationToken ct)
    {
        var questions = await _db.Questions.AsNoTracking()
            .Where(q => topicIds.Contains(q.TopicId))
            .Select(q => new { q.Id, q.TopicId, q.Difficulty })
            .ToListAsync(ct);
        var qids = questions.Select(q => q.Id).ToList();
        var stats = await _db.QuestionStats.AsNoTracking()
            .Where(s => s.LearnerId == learnerId && qids.Contains(s.QuestionId))
            .ToDictionaryAsync(s => s.QuestionId, ct);
        return questions.GroupBy(q => q.TopicId).ToDictionary(
            g => g.Key,
            g => g.Select(q => new QuestionSelector.Candidate(q.Id, q.Difficulty, stats.GetValueOrDefault(q.Id))).ToList());
    }

    private async Task<double> ScoreAsync(Guid learnerId, int topicId, CancellationToken ct) =>
        await ScoreOrNullAsync(learnerId, topicId, ct) ?? 0;

    private Task<double?> ScoreOrNullAsync(Guid learnerId, int topicId, CancellationToken ct) =>
        _db.TopicMasteries.AsNoTracking()
            .Where(m => m.LearnerId == learnerId && m.TopicId == topicId)
            .Select(m => (double?)m.Score)
            .FirstOrDefaultAsync(ct);

    private Task<Dictionary<int, double>> ScoresAsync(Guid learnerId, List<int> topicIds, CancellationToken ct) =>
        _db.TopicMasteries.AsNoTracking()
            .Where(m => m.LearnerId == learnerId && topicIds.Contains(m.TopicId))
            .ToDictionaryAsync(m => m.TopicId, m => m.Score, ct);

    private async Task<Attempt> SaveNewAttemptAsync(Attempt attempt, List<int> questionIds, bool shuffleOptions, CancellationToken ct)
    {
        var questions = await _db.Questions.AsNoTracking()
            .Where(q => questionIds.Contains(q.Id))
            .Include(q => q.Options)
            .ToDictionaryAsync(q => q.Id, ct);

        var sort = 0;
        foreach (var qid in questionIds.Distinct())
        {
            if (!questions.TryGetValue(qid, out var q)) continue;
            attempt.Items.Add(new AttemptItem
            {
                QuestionId = qid,
                SortOrder = sort++,
                OptionOrder = OptionOrder(q, shuffleOptions)
            });
        }
        attempt.TotalCount = attempt.Items.Count;
        _db.Attempts.Add(attempt);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return (await GetAttemptAsync(attempt.Id, ct))!;
    }

    /// <summary>Display order of the options. True/false keeps its natural order (Goed before Fout).</summary>
    private static string? OptionOrder(Question q, bool shuffle)
    {
        if (q.Type == QuestionType.FillIn || q.Options.Count == 0) return null;
        var ids = q.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id).Select(o => o.Id).ToArray();
        if (shuffle && q.Type != QuestionType.TrueFalse) Rng.Shuffle(ids);
        return string.Join(',', ids);
    }

    /// <summary>Updates the question stat and topic mastery (tracked; caller saves). Returns score before/after.</summary>
    private async Task<(double Previous, double Next)> ApplyProgressAsync(Guid learnerId, Question question, bool correct, bool placement, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var stat = await _db.QuestionStats.FindAsync([learnerId, question.Id], ct);
        if (stat is null)
        {
            stat = new QuestionStat { LearnerId = learnerId, QuestionId = question.Id };
            _db.QuestionStats.Add(stat);
        }
        stat.Seen++;
        if (!correct) stat.Wrong++;
        stat.LastCorrect = correct;
        stat.LastSeen = now;

        var mastery = await _db.TopicMasteries.FindAsync([learnerId, question.TopicId], ct);
        if (mastery is null)
        {
            mastery = new TopicMastery { LearnerId = learnerId, TopicId = question.TopicId, Score = 0 };
            _db.TopicMasteries.Add(mastery);
        }
        var previous = mastery.Score;
        var next = placement
            ? MasteryRules.UpdatePlacement(previous, correct, question.Difficulty)
            : MasteryRules.Update(previous, correct, question.Difficulty);
        mastery.Score = Math.Round(next, 2);
        mastery.Answered++;
        if (correct) mastery.Correct++;
        mastery.UpdatedAt = now;
        return (previous, mastery.Score);
    }

    private static AnswerResult BuildResult(AttemptItem item, Question question, bool isCorrect, IReadOnlyCollection<int> selected,
        double? previous, double? next, bool complete, bool revealFeedback)
    {
        if (!revealFeedback)
        {
            // Exam: the answer is stored, but nothing about its correctness is revealed until FinishAsync.
            return new AnswerResult(item.Id, false, null, null, null, [], [], question.TopicId, null, null, complete);
        }

        var feedback = question.Options
            .Where(o => selected.Contains(o.Id) && !string.IsNullOrWhiteSpace(o.Feedback))
            .OrderBy(o => o.SortOrder)
            .Select(o => o.Feedback!.Trim())
            .ToList();
        return new AnswerResult(
            item.Id,
            isCorrect,
            isCorrect ? null : string.IsNullOrWhiteSpace(question.Tip) ? GenericTip : question.Tip,
            question.Explanation,
            feedback.Count > 0 ? string.Join("\n\n", feedback) : null,
            question.Options.Where(o => o.IsCorrect).OrderBy(o => o.SortOrder).Select(o => o.Id).ToList(),
            question.Type == QuestionType.FillIn ? [.. question.AcceptedAnswers] : [],
            question.TopicId,
            previous,
            next,
            complete);
    }

    internal static List<int> ParseIds(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(s => int.TryParse(s, out var v) ? v : 0).Where(v => v > 0).ToList();
}
