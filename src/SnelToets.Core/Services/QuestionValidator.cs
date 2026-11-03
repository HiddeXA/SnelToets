using SnelToets.Core.Models;

namespace SnelToets.Core.Services;

/// <summary>Checks a question before it is saved. Returns Dutch messages; empty list = valid.</summary>
public static class QuestionValidator
{
    public static List<string> Validate(Question question)
    {
        var errors = new List<string>();
        if (question is null)
        {
            errors.Add("Er is geen vraag opgegeven.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(question.Prompt))
            errors.Add("De vraagtekst is verplicht.");

        if (!Enum.IsDefined(question.Type))
        {
            errors.Add("Onbekend vraagtype.");
        }
        else if (question.Type == QuestionType.FillIn)
        {
            if (!question.AcceptedAnswers.Any(a => !string.IsNullOrWhiteSpace(a)))
                errors.Add("Een invulvraag heeft minstens één goed antwoord nodig.");
        }
        else
        {
            var options = question.Options;
            if (question.Type == QuestionType.TrueFalse)
            {
                if (options.Count != 2)
                    errors.Add("Een goed/fout-vraag heeft precies twee antwoordopties nodig.");
            }
            else if (options.Count < 2)
            {
                errors.Add("Een meerkeuzevraag heeft minstens twee antwoordopties nodig.");
            }

            if (options.Any(o => string.IsNullOrWhiteSpace(o.Text)))
                errors.Add("Elke antwoordoptie moet een tekst hebben.");

            var correct = options.Count(o => o.IsCorrect);
            if (question.Type == QuestionType.MultipleSelect)
            {
                if (correct < 1)
                    errors.Add("Markeer minstens één antwoordoptie als juist.");
            }
            else if (correct != 1)
            {
                errors.Add(correct == 0
                    ? "Markeer precies één antwoordoptie als juist (nu geen enkele)."
                    : $"Markeer precies één antwoordoptie als juist (nu {correct}).");
            }
        }

        if (question.Difficulty is < 1 or > 3)
            errors.Add("De moeilijkheid moet 1 (makkelijk), 2 (gemiddeld) of 3 (moeilijk) zijn.");

        return errors;
    }
}
