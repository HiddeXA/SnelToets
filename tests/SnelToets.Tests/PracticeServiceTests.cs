using Microsoft.EntityFrameworkCore;
using SnelToets.Core.Models;
using SnelToets.Core.Services;

namespace SnelToets.Tests;

public class PracticeServiceTests
{
    private static readonly Guid Learner = Guid.NewGuid();

    private static int CorrectId(AttemptItem item) => item.Question!.Options.Single(o => o.IsCorrect).Id;
    private static int WrongId(AttemptItem item) => item.Question!.Options.First(o => !o.IsCorrect).Id;

    [Fact]
    public async Task Topic_series_has_no_duplicates_and_uses_all_when_fewer_than_count()
    {
        using var t = new TestDb();
        var (_, topic, _) = t.Seed(6);
        var svc = new PracticeService(t.Db);
        var a = await svc.StartTopicPracticeAsync(Learner, topic.Id, 15);
        Assert.Equal(6, a.Items.Count);
        Assert.Equal(6, a.Items.Select(i => i.QuestionId).Distinct().Count());
        Assert.Equal(AttemptKind.TopicPractice, a.Kind);
        Assert.Equal(TestMode.Practice, a.Mode);
        Assert.All(a.Items, i => Assert.Equal(3, i.OptionOrder!.Split(',').Length));
        Assert.Equal(Enumerable.Range(0, 6), a.Items.Select(i => i.SortOrder));
    }

    [Fact]
    public async Task Topic_series_without_questions_throws()
    {
        using var t = new TestDb();
        var (_, topic, _) = t.Seed(0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PracticeService(t.Db).StartTopicPracticeAsync(Learner, topic.Id));
    }

    [Fact]
    public async Task Topic_series_prefers_questions_answered_wrongly_before()
    {
        using var t = new TestDb();
        var (_, topic, qs) = t.Seed(20);
        var wrong = qs.Take(4).Select(q => q.Id).ToHashSet();
        foreach (var q in qs)
            t.Db.QuestionStats.Add(new QuestionStat { LearnerId = Learner, QuestionId = q.Id, Seen = 1, LastCorrect = !wrong.Contains(q.Id), LastSeen = DateTime.UtcNow });
        // Two never-seen questions come next.
        foreach (var prompt in new[] { "nieuw 1", "nieuw 2" })
        {
            var q = TestDb.Mc(prompt);
            q.TopicId = topic.Id;
            t.Db.Questions.Add(q);
        }
        await t.Db.SaveChangesAsync();
        t.Db.ChangeTracker.Clear();

        var svc = new PracticeService(t.Db);
        for (var run = 0; run < 5; run++)
        {
            var a = await svc.StartTopicPracticeAsync(Learner, topic.Id, 6);
            var ids = a.Items.Select(i => i.QuestionId).ToHashSet();
            Assert.Equal(6, ids.Count);
            Assert.True(wrong.IsSubsetOf(ids));
            Assert.Contains(a.Items, i => i.Question!.Prompt == "nieuw 1");
            Assert.Contains(a.Items, i => i.Question!.Prompt == "nieuw 2");
        }
    }

    [Fact]
    public async Task Practice_submit_updates_mastery_once()
    {
        using var t = new TestDb();
        var (_, topic, _) = t.Seed(3);
        var svc = new PracticeService(t.Db);
        var a = await svc.StartTopicPracticeAsync(Learner, topic.Id);
        var item = a.Items[0];

        var r1 = await svc.SubmitAnswerAsync(a.Id, item.Id, AnswerSubmission.Choice(CorrectId(item)));
        Assert.True(r1.IsCorrect);
        Assert.Null(r1.Tip);
        Assert.Equal(0, r1.PreviousMastery);
        Assert.True(r1.NewMastery > 0);

        var r2 = await svc.SubmitAnswerAsync(a.Id, item.Id, AnswerSubmission.Choice(WrongId(item)));
        Assert.True(r2.IsCorrect); // stored result, not re-graded
        Assert.Equal(r1.NewMastery, r2.NewMastery);

        var m = Assert.Single(await svc.GetMasteryAsync(Learner));
        Assert.Equal(1, m.Answered);
        Assert.Equal(r1.NewMastery, m.Score);
        var stat = await t.Db.QuestionStats.AsNoTracking().SingleAsync();
        Assert.Equal(1, stat.Seen);
    }

    [Fact]
    public async Task Wrong_answer_returns_tip_feedback_and_correct_options()
    {
        using var t = new TestDb();
        var (_, topic, _) = t.Seed(2);
        var svc = new PracticeService(t.Db);
        var a = await svc.StartTopicPracticeAsync(Learner, topic.Id);
        var item = a.Items[0];
        var wrongWithFeedback = item.Question!.Options.Single(o => o.Feedback != null).Id;
        var r = await svc.SubmitAnswerAsync(a.Id, item.Id, AnswerSubmission.Choice(wrongWithFeedback));
        Assert.False(r.IsCorrect);
        Assert.Equal("Let op de regel.", r.Tip);
        Assert.Equal("Dit is de valkuil.", r.OptionFeedback);
        Assert.Equal([CorrectId(item)], r.CorrectOptionIds);
        Assert.False(r.AttemptComplete);

        var last = await svc.SubmitAnswerAsync(a.Id, a.Items[1].Id, AnswerSubmission.Choice(CorrectId(a.Items[1])));
        Assert.True(last.AttemptComplete);

        var fresh = await svc.GetAttemptAsync(a.Id);
        Assert.All(fresh!.Items, i => Assert.NotNull(i.AnsweredAt));
    }

    [Fact]
    public async Task Generic_tip_when_question_has_none()
    {
        using var t = new TestDb();
        var (_, topic, _) = t.Seed(0);
        var q = TestDb.Mc("Zonder tip", tip: null);
        q.TopicId = topic.Id;
        t.Db.Questions.Add(q);
        await t.Db.SaveChangesAsync();
        var svc = new PracticeService(t.Db);
        var a = await svc.StartTopicPracticeAsync(Learner, topic.Id);
        var r = await svc.SubmitAnswerAsync(a.Id, a.Items[0].Id, AnswerSubmission.Choice(WrongId(a.Items[0])));
        Assert.Equal(PracticeService.GenericTip, r.Tip);
    }

    [Fact]
    public async Task Submit_rejects_item_of_other_attempt()
    {
        using var t = new TestDb();
        var (_, topic, _) = t.Seed(2);
        var svc = new PracticeService(t.Db);
        var a = await svc.StartTopicPracticeAsync(Learner, topic.Id);
        var b = await svc.StartTopicPracticeAsync(Learner, topic.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubmitAnswerAsync(a.Id, b.Items[0].Id, AnswerSubmission.Choice(1)));
    }

    private static async Task<(PracticeService Svc, Attempt Attempt)> StartExamAsync(TestDb t, int? timeLimit = null)
    {
        var (_, topic, _) = t.Seed(4);
        var test = await new TestService(t.Db).CreateTestAsync(new Test
        {
            Title = "Eindtoets", Mode = TestMode.Exam, PassPercentage = 50, TimeLimitMinutes = timeLimit,
            Topics = [new() { TopicId = topic.Id }]
        });
        var svc = new PracticeService(t.Db);
        return (svc, await svc.StartTestAsync(Learner, test.Id));
    }

    [Fact]
    public async Task Exam_submit_does_not_touch_mastery_until_finish_and_can_be_changed()
    {
        using var t = new TestDb();
        var (svc, a) = await StartExamAsync(t);
        Assert.Equal(4, a.Items.Count);
        Assert.Equal(TestMode.Exam, a.Mode);
        Assert.Equal(50, a.PassPercentage);

        var item = a.Items[0];
        var r = await svc.SubmitAnswerAsync(a.Id, item.Id, AnswerSubmission.Choice(WrongId(item)));
        Assert.Null(r.NewMastery);
        Assert.Null(r.Tip);
        Assert.Empty(r.CorrectOptionIds);
        await svc.SubmitAnswerAsync(a.Id, item.Id, AnswerSubmission.Choice(CorrectId(item))); // change answer
        await svc.SubmitAnswerAsync(a.Id, a.Items[1].Id, AnswerSubmission.Choice(CorrectId(a.Items[1])));

        Assert.Empty(await svc.GetMasteryAsync(Learner));
        Assert.Equal(0, await t.Db.QuestionStats.CountAsync());

        var result = await svc.FinishAsync(a.Id);
        Assert.Equal(2, result.Correct);
        Assert.Equal(4, result.Total);
        Assert.Equal(50.0, result.Percentage);
        Assert.True(result.Passed);
        var tr = Assert.Single(result.PerTopic);
        Assert.Equal(2, tr.Correct);
        Assert.NotNull(tr.Mastery);

        var m = Assert.Single(await svc.GetMasteryAsync(Learner));
        Assert.Equal(2, m.Answered);
        Assert.Equal(2, await t.Db.QuestionStats.CountAsync());

        // Idempotent; no further changes after finishing.
        var again = await svc.FinishAsync(a.Id);
        Assert.Equal(result.Correct, again.Correct);
        Assert.Equal(2, (await svc.GetMasteryAsync(Learner)).Single().Answered);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubmitAnswerAsync(a.Id, a.Items[2].Id, AnswerSubmission.Choice(CorrectId(a.Items[2]))));
    }

    [Fact]
    public async Task Answer_after_deadline_is_rejected_and_finishes_attempt()
    {
        using var t = new TestDb();
        var (svc, a) = await StartExamAsync(t, timeLimit: 30);
        Assert.NotNull(a.Deadline);
        await t.Db.Attempts.Where(x => x.Id == a.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Deadline, DateTime.UtcNow.AddMinutes(-1)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubmitAnswerAsync(a.Id, a.Items[0].Id, AnswerSubmission.Choice(CorrectId(a.Items[0]))));
        var fresh = await svc.GetAttemptAsync(a.Id);
        Assert.NotNull(fresh!.FinishedAt);
        Assert.Equal(0, fresh.CorrectCount);
    }

    [Fact]
    public async Task Placement_never_exceeds_cap()
    {
        using var t = new TestDb();
        var (subject, _, _) = t.Seed(30, difficulty: 3);
        var svc = new PracticeService(t.Db);
        var a = await svc.StartPlacementAsync(Learner, subject.Id, perTopic: 25);
        Assert.Equal(AttemptKind.Placement, a.Kind);
        Assert.Equal(25, a.Items.Count);
        AnswerResult? last = null;
        foreach (var item in a.Items)
            last = await svc.SubmitAnswerAsync(a.Id, item.Id, AnswerSubmission.Choice(CorrectId(item)));
        Assert.Equal(MasteryRules.PlacementCap, last!.NewMastery);
        await svc.FinishAsync(a.Id);

        var advice = await svc.GetAdviceAsync(Learner, subject.Id);
        Assert.True(advice.PlacementDone);
        Assert.False(advice.Topics.Single().Mastered);
    }

    [Fact]
    public async Task Placement_takes_per_topic_questions_from_every_topic()
    {
        using var t = new TestDb();
        var (subject, _, _) = t.Seed(5, topic: "A");
        t.Seed(5, topic: "B");
        t.Seed(0, topic: "Leeg");
        var a = await new PracticeService(t.Db).StartPlacementAsync(Learner, subject.Id, perTopic: 2);
        Assert.Equal(4, a.Items.Count);
        Assert.Equal(2, a.Items.Select(i => i.Question!.TopicId).Distinct().Count());
    }

    [Fact]
    public async Task Mix_practice_covers_every_topic_and_weights_weak_topics()
    {
        using var t = new TestDb();
        var (subject, strong, _) = t.Seed(20, topic: "Sterk");
        var (_, weak, _) = t.Seed(20, topic: "Zwak");
        var (_, other, _) = t.Seed(20, topic: "Ander");
        t.Db.TopicMasteries.Add(new TopicMastery { LearnerId = Learner, TopicId = strong.Id, Score = 95, Answered = 30 });
        t.Db.TopicMasteries.Add(new TopicMastery { LearnerId = Learner, TopicId = weak.Id, Score = 5, Answered = 30 });
        await t.Db.SaveChangesAsync();
        t.Db.ChangeTracker.Clear();

        var svc = new PracticeService(t.Db);
        int strongTotal = 0, weakTotal = 0;
        for (var run = 0; run < 10; run++)
        {
            var a = await svc.StartMixPracticeAsync(Learner, subject.Id, 15);
            Assert.Equal(15, a.Items.Count);
            Assert.Equal(15, a.Items.Select(i => i.QuestionId).Distinct().Count());
            var perTopic = a.Items.GroupBy(i => i.Question!.TopicId).ToDictionary(g => g.Key, g => g.Count());
            Assert.Equal(3, perTopic.Count);
            strongTotal += perTopic[strong.Id];
            weakTotal += perTopic[weak.Id];
        }
        Assert.True(weakTotal > strongTotal * 2, $"weak {weakTotal} vs strong {strongTotal}");
    }

    [Fact]
    public async Task Advice_recommends_lowest_unmastered_topic()
    {
        using var t = new TestDb();
        var (subject, a, _) = t.Seed(3, topic: "A");
        var (_, b, _) = t.Seed(3, topic: "B");
        var (_, c, _) = t.Seed(3, topic: "C");
        var svc = new PracticeService(t.Db);

        var before = await svc.GetAdviceAsync(Learner, subject.Id);
        Assert.False(before.PlacementDone);
        Assert.Equal("Maak eerst de starttoets, dan weet je waar je moet beginnen.", before.Message);

        t.Db.Attempts.Add(new Attempt { LearnerId = Learner, Kind = AttemptKind.Placement, SubjectId = subject.Id, FinishedAt = DateTime.UtcNow });
        t.Db.TopicMasteries.Add(new TopicMastery { LearnerId = Learner, TopicId = a.Id, Score = 90, Answered = 5 });
        t.Db.TopicMasteries.Add(new TopicMastery { LearnerId = Learner, TopicId = b.Id, Score = 30, Answered = 5 });
        t.Db.TopicMasteries.Add(new TopicMastery { LearnerId = Learner, TopicId = c.Id, Score = 55, Answered = 5 });
        await t.Db.SaveChangesAsync();

        var advice = await svc.GetAdviceAsync(Learner, subject.Id);
        Assert.True(advice.PlacementDone);
        Assert.Equal(b.Id, advice.RecommendedTopicId);
        Assert.Equal("Begin met B: daar valt de meeste winst te halen.", advice.Message);
        Assert.True(advice.Topics.Single(x => x.TopicId == b.Id).NeedsAttention);
        Assert.False(advice.Topics.Single(x => x.TopicId == c.Id).NeedsAttention);
        Assert.True(advice.Topics.Single(x => x.TopicId == a.Id).Mastered);
    }

    [Fact]
    public async Task Reset_topic_removes_mastery_and_stats()
    {
        using var t = new TestDb();
        var (_, topic, _) = t.Seed(2);
        var svc = new PracticeService(t.Db);
        var a = await svc.StartTopicPracticeAsync(Learner, topic.Id);
        await svc.SubmitAnswerAsync(a.Id, a.Items[0].Id, AnswerSubmission.Choice(CorrectId(a.Items[0])));
        await svc.ResetTopicAsync(Learner, topic.Id);
        Assert.Empty(await svc.GetMasteryAsync(Learner));
        Assert.Equal(0, await t.Db.QuestionStats.CountAsync());
    }

    [Fact]
    public async Task Recent_attempts_are_newest_first()
    {
        using var t = new TestDb();
        var (_, topic, _) = t.Seed(2);
        var svc = new PracticeService(t.Db);
        var first = await svc.StartTopicPracticeAsync(Learner, topic.Id);
        await Task.Delay(5);
        var second = await svc.StartTopicPracticeAsync(Learner, topic.Id);
        var recent = await svc.GetRecentAttemptsAsync(Learner);
        Assert.Equal([second.Id, first.Id], recent.Select(r => r.Id));
    }
}
