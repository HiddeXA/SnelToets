using SnelToets.Core.Models;
using SnelToets.Core.Services;

namespace SnelToets.Tests;

public class TestServiceTests
{
    [Fact]
    public async Task Count_follows_fixed_plus_random_rules()
    {
        using var t = new TestDb();
        var (_, topic, qs) = t.Seed(10);
        var svc = new TestService(t.Db);

        var all = await svc.CreateTestAsync(new Test { Title = "Alles", Topics = [new() { TopicId = topic.Id }] });
        Assert.Equal(10, await svc.CountQuestionsAsync(all.Id));

        var fixedOnly = await svc.CreateTestAsync(new Test
        {
            Title = "Vast", Topics = [new() { TopicId = topic.Id }],
            Questions = [new() { QuestionId = qs[0].Id }, new() { QuestionId = qs[1].Id, SortOrder = 1 }]
        });
        Assert.Equal(2, await svc.CountQuestionsAsync(fixedOnly.Id));

        var mixed = await svc.CreateTestAsync(new Test
        {
            Title = "Mix", RandomQuestionCount = 5, Topics = [new() { TopicId = topic.Id }],
            Questions = [new() { QuestionId = qs[0].Id }]
        });
        Assert.Equal(6, await svc.CountQuestionsAsync(mixed.Id));

        var capped = await svc.CreateTestAsync(new Test
        {
            Title = "Te veel", RandomQuestionCount = 50, Topics = [new() { TopicId = topic.Id }],
            Questions = [new() { QuestionId = qs[0].Id }, new() { QuestionId = qs[1].Id, SortOrder = 1 }]
        });
        Assert.Equal(10, await svc.CountQuestionsAsync(capped.Id));
    }

    [Fact]
    public async Task Update_replaces_question_and_topic_sets()
    {
        using var t = new TestDb();
        var (_, topic, qs) = t.Seed(4);
        var (_, topic2, _) = t.Seed(2, topic: "Spelling");
        var svc = new TestService(t.Db);
        var test = await svc.CreateTestAsync(new Test
        {
            Title = "T", Topics = [new() { TopicId = topic.Id }],
            Questions = [new() { QuestionId = qs[0].Id }, new() { QuestionId = qs[1].Id, SortOrder = 1 }]
        });
        var updated = await svc.UpdateTestAsync(new Test
        {
            Id = test.Id, Title = "T2", Topics = [new() { TopicId = topic2.Id }],
            Questions = [new() { QuestionId = qs[2].Id }, new() { QuestionId = qs[1].Id, SortOrder = 1 }]
        });
        Assert.Equal("T2", updated.Title);
        Assert.Equal([qs[2].Id, qs[1].Id], updated.Questions.OrderBy(q => q.SortOrder).Select(q => q.QuestionId));
        Assert.Equal(topic2.Id, Assert.Single(updated.Topics).TopicId);
        Assert.NotNull(updated.Questions[0].Question);
        Assert.NotEmpty(updated.Questions[0].Question!.Options);
    }
}
