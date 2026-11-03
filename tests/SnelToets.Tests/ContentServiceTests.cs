using Microsoft.EntityFrameworkCore;
using SnelToets.Core.Models;
using SnelToets.Core.Services;

namespace SnelToets.Tests;

public class ContentServiceTests
{
    [Fact]
    public async Task New_topics_get_increasing_sort_order_and_are_listed_in_order()
    {
        using var t = new TestDb();
        var svc = new ContentService(t.Db);
        var s = await svc.CreateSubjectAsync(new Subject { Name = "Economie" });
        var b = await svc.CreateTopicAsync(new Topic { SubjectId = s.Id, Name = "B" });
        var a = await svc.CreateTopicAsync(new Topic { SubjectId = s.Id, Name = "A" });
        Assert.True(a.SortOrder > b.SortOrder);
        var topics = await svc.GetTopicsAsync(s.Id);
        Assert.Equal(["B", "A"], topics.Select(x => x.Name));
    }

    [Fact]
    public async Task Create_question_validates()
    {
        using var t = new TestDb();
        var (_, topic, _) = t.Seed(0);
        var svc = new ContentService(t.Db);
        var bad = TestDb.Mc("");
        bad.TopicId = topic.Id;
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateQuestionAsync(bad));
        Assert.NotEmpty(ex.Errors);
    }

    [Fact]
    public async Task Update_question_with_fresh_copy_in_same_context_replaces_options()
    {
        using var t = new TestDb();
        var (_, topic, _) = t.Seed(0);
        var svc = new ContentService(t.Db);
        var q = TestDb.Mc("Oud");
        q.TopicId = topic.Id;
        var created = await svc.CreateQuestionAsync(q);
        var loaded = (await svc.GetQuestionAsync(created.Id))!;

        // Fresh, detached copy as the admin UI would send it.
        var copy = new Question
        {
            Id = loaded.Id, TopicId = loaded.TopicId, Type = QuestionType.MultipleChoice, Prompt = "Nieuw", Difficulty = 2,
            Options =
            [
                new AnswerOption { Id = loaded.Options[1].Id, Text = "Nu juist", IsCorrect = true, SortOrder = 0 },
                new AnswerOption { Text = "Nieuwe optie", SortOrder = 1 }
            ]
        };
        var updated = await svc.UpdateQuestionAsync(copy);
        Assert.Equal("Nieuw", updated.Prompt);
        Assert.Equal(2, updated.Options.Count);
        Assert.Equal(loaded.Options[1].Id, updated.Options[0].Id);
        Assert.True(updated.Options[0].IsCorrect);
        Assert.Equal(2, await t.Db.AnswerOptions.CountAsync());

        // A second update in the same context also works.
        copy.Prompt = "Nog nieuwer";
        Assert.Equal("Nog nieuwer", (await svc.UpdateQuestionAsync(copy)).Prompt);
    }

    [Fact]
    public async Task Search_is_case_insensitive_on_prompt_and_context()
    {
        using var t = new TestDb();
        var (_, topic, _) = t.Seed(0);
        var svc = new ContentService(t.Db);
        var q1 = TestDb.Mc("Welke VORM is juist?"); q1.TopicId = topic.Id;
        var q2 = TestDb.Mc("Kies"); q2.TopicId = topic.Id; q2.Context = "Hij vormt een zin.";
        var q3 = TestDb.Mc("Iets anders"); q3.TopicId = topic.Id;
        foreach (var q in new[] { q1, q2, q3 }) await svc.CreateQuestionAsync(q);
        Assert.Equal(2, (await svc.GetQuestionsAsync(search: "vorm")).Count);
    }

    [Fact]
    public async Task Deleting_a_subject_cascades()
    {
        using var t = new TestDb();
        var (s, _, _) = t.Seed(3);
        var svc = new ContentService(t.Db);
        await svc.DeleteSubjectAsync(s.Id);
        Assert.Equal(0, await t.Db.Questions.CountAsync());
        Assert.Equal(0, await t.Db.Topics.CountAsync());
    }
}
