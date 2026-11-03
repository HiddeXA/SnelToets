using SnelToets.Core.Models;
using SnelToets.Core.Services;

namespace SnelToets.Tests;

public class AnswerCheckerTests
{
    private static Question Choice(QuestionType type, params bool[] correct) => new()
    {
        Type = type,
        Prompt = "?",
        Options = correct.Select((c, i) => new AnswerOption { Id = i + 1, Text = $"o{i + 1}", IsCorrect = c, SortOrder = i }).ToList()
    };

    [Fact]
    public void MultipleChoice_requires_exactly_the_one_correct_option()
    {
        var q = Choice(QuestionType.MultipleChoice, false, true, false);
        Assert.True(AnswerChecker.IsCorrect(q, AnswerSubmission.Choice(2)));
        Assert.False(AnswerChecker.IsCorrect(q, AnswerSubmission.Choice(1)));
        Assert.False(AnswerChecker.IsCorrect(q, AnswerSubmission.Choice(2, 1)));
        Assert.False(AnswerChecker.IsCorrect(q, AnswerSubmission.Choice()));
        Assert.False(AnswerChecker.IsCorrect(q, AnswerSubmission.Choice(99)));
    }

    [Fact]
    public void TrueFalse_works_like_single_choice()
    {
        var q = Choice(QuestionType.TrueFalse, true, false);
        Assert.True(AnswerChecker.IsCorrect(q, AnswerSubmission.Choice(1)));
        Assert.False(AnswerChecker.IsCorrect(q, AnswerSubmission.Choice(2)));
        Assert.False(AnswerChecker.IsCorrect(q, AnswerSubmission.Choice(1, 2)));
    }

    [Fact]
    public void MultipleSelect_requires_the_exact_set()
    {
        var q = Choice(QuestionType.MultipleSelect, true, false, true);
        Assert.True(AnswerChecker.IsCorrect(q, AnswerSubmission.Choice(3, 1)));
        Assert.True(AnswerChecker.IsCorrect(q, AnswerSubmission.Choice(1, 3, 3)));
        Assert.False(AnswerChecker.IsCorrect(q, AnswerSubmission.Choice(1)));
        Assert.False(AnswerChecker.IsCorrect(q, AnswerSubmission.Choice(1, 2, 3)));
        Assert.False(AnswerChecker.IsCorrect(q, AnswerSubmission.Choice()));
    }

    [Theory]
    [InlineData("wordt")]
    [InlineData("  Wordt  ")]
    [InlineData("WORDT.")]
    [InlineData("hij’s")]
    [InlineData("hij  is   klaar")]
    [InlineData("Hij is klaar.")]
    public void FillIn_accepts_normalised_variants(string given)
    {
        var q = new Question { Type = QuestionType.FillIn, Prompt = "?", AcceptedAnswers = ["wordt", "hij's", "hij is klaar"] };
        Assert.True(AnswerChecker.IsCorrect(q, AnswerSubmission.Typed(given)));
    }

    [Theory]
    [InlineData("word")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("wordt..")]
    public void FillIn_rejects_wrong_answers(string given)
    {
        var q = new Question { Type = QuestionType.FillIn, Prompt = "?", AcceptedAnswers = ["wordt"] };
        Assert.False(AnswerChecker.IsCorrect(q, AnswerSubmission.Typed(given)));
    }

    [Fact]
    public void Normalize_handles_quotes_whitespace_case_and_period()
    {
        Assert.Equal("'t is \"mooi\" weer", AnswerChecker.Normalize("  ‘T   is “mooi”\tweer. "));
        Assert.Equal("", AnswerChecker.Normalize(null));
    }
}
