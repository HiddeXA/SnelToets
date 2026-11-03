using System.Text;
using System.Text.RegularExpressions;
using SnelToets.Core.Models;

namespace SnelToets.Core.Services;

/// <summary>Grades a submitted answer against a question.</summary>
public static partial class AnswerChecker
{
    public static bool IsCorrect(Question question, AnswerSubmission answer)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (answer is null) return false;

        switch (question.Type)
        {
            case QuestionType.MultipleChoice:
            case QuestionType.TrueFalse:
            {
                var selected = (answer.OptionIds ?? []).Distinct().ToList();
                if (selected.Count != 1) return false;
                var option = question.Options.FirstOrDefault(o => o.Id == selected[0]);
                return option is { IsCorrect: true };
            }
            case QuestionType.MultipleSelect:
            {
                var selected = (answer.OptionIds ?? []).ToHashSet();
                if (selected.Count == 0) return false;
                var known = question.Options.Select(o => o.Id).ToHashSet();
                if (!selected.IsSubsetOf(known)) return false;
                var correct = question.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
                return correct.Count > 0 && selected.SetEquals(correct);
            }
            case QuestionType.FillIn:
            {
                if (string.IsNullOrWhiteSpace(answer.Text)) return false;
                var given = Normalize(answer.Text);
                return question.AcceptedAnswers.Any(a => !string.IsNullOrWhiteSpace(a) && Normalize(a) == given);
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Normalises a typed answer: trims, collapses whitespace, lower-cases (invariant), turns curly
    /// quotes/apostrophes into straight ones and strips a single trailing period.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Normalize(NormalizationForm.FormC));
        sb.Replace('’', '\'').Replace('‘', '\'').Replace('‚', '\'').Replace('‛', '\'')
          .Replace('′', '\'').Replace('´', '\'').Replace('`', '\'')
          .Replace('“', '"').Replace('”', '"').Replace('„', '"').Replace('«', '"').Replace('»', '"');
        var s = Whitespace().Replace(sb.ToString(), " ").Trim();
        if (s.EndsWith('.') && !s.EndsWith("..")) s = s[..^1].TrimEnd();
        return s.ToLowerInvariant();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
