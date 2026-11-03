using SnelToets.Core.Models;
using SnelToets.Core.Services;

namespace SnelToets.Tests;

public class QuestionValidatorTests
{
    [Fact]
    public void Valid_multiple_choice_has_no_errors() =>
        Assert.Empty(QuestionValidator.Validate(TestDb.Mc("Welke vorm is juist?")));

    [Fact]
    public void Reports_missing_prompt_and_bad_difficulty()
    {
        var q = TestDb.Mc(" ");
        q.Difficulty = 4;
        var errors = QuestionValidator.Validate(q);
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void Choice_rules()
    {
        var one = new Question { Type = QuestionType.MultipleChoice, Prompt = "?", Options = [new() { Text = "a", IsCorrect = true }] };
        Assert.NotEmpty(QuestionValidator.Validate(one));

        var twoCorrect = TestDb.Mc("?");
        twoCorrect.Options[1].IsCorrect = true;
        Assert.NotEmpty(QuestionValidator.Validate(twoCorrect));
        twoCorrect.Type = QuestionType.MultipleSelect;
        Assert.Empty(QuestionValidator.Validate(twoCorrect));

        var noneCorrect = TestDb.Mc("?");
        noneCorrect.Options[0].IsCorrect = false;
        noneCorrect.Type = QuestionType.MultipleSelect;
        Assert.NotEmpty(QuestionValidator.Validate(noneCorrect));

        var emptyText = TestDb.Mc("?");
        emptyText.Options[2].Text = "";
        Assert.Single(QuestionValidator.Validate(emptyText));
    }

    [Fact]
    public void TrueFalse_needs_exactly_two_options()
    {
        var q = TestDb.Mc("?");
        q.Type = QuestionType.TrueFalse;
        Assert.NotEmpty(QuestionValidator.Validate(q));
        q.Options.RemoveAt(2);
        Assert.Empty(QuestionValidator.Validate(q));
    }

    [Fact]
    public void FillIn_needs_an_accepted_answer()
    {
        var q = new Question { Type = QuestionType.FillIn, Prompt = "?", AcceptedAnswers = [" "] };
        Assert.NotEmpty(QuestionValidator.Validate(q));
        q.AcceptedAnswers = ["wordt"];
        Assert.Empty(QuestionValidator.Validate(q));
    }
}
