using SnelToets.Core.Models;

namespace SnelToets.Web.Components.Admin;

/// <summary>Editable form state for a question. Converted to a fresh (detached) <see cref="Question"/> on save.</summary>
public class QuestionDraft
{
    public int Id { get; set; }
    public int TopicId { get; set; }
    public QuestionType Type { get; set; } = QuestionType.MultipleChoice;
    public string Prompt { get; set; } = "";
    public string? Context { get; set; }
    public List<OptionDraft> Options { get; set; } = [];
    public List<AnswerDraft> Answers { get; set; } = [];
    public string? Tip { get; set; }
    public string? Explanation { get; set; }
    public int Difficulty { get; set; } = 1;
    public ContentSource Source { get; set; } = ContentSource.Manual;
    public string? ExternalId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public static QuestionDraft New(int topicId, QuestionType type = QuestionType.MultipleChoice)
    {
        var d = new QuestionDraft { TopicId = topicId, Options = [new(), new(), new()] };
        d.ChangeType(type);
        return d;
    }

    public static QuestionDraft From(Question q) => new()
    {
        Id = q.Id,
        TopicId = q.TopicId,
        Type = q.Type,
        Prompt = q.Prompt,
        Context = q.Context,
        Options = q.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
            .Select(o => new OptionDraft { Text = o.Text, IsCorrect = o.IsCorrect, Feedback = o.Feedback, ShowFeedback = !string.IsNullOrWhiteSpace(o.Feedback) })
            .ToList(),
        Answers = q.AcceptedAnswers.Select(a => new AnswerDraft { Text = a }).ToList(),
        Tip = q.Tip,
        Explanation = q.Explanation,
        Difficulty = q.Difficulty is >= 1 and <= 3 ? q.Difficulty : 1,
        Source = q.Source,
        ExternalId = q.ExternalId,
        CreatedAt = q.CreatedAt
    };

    /// <summary>Switches the type and adapts options/answers so the form stays consistent.</summary>
    public void ChangeType(QuestionType type)
    {
        var old = Type;
        Type = type;
        switch (type)
        {
            case QuestionType.TrueFalse:
                if (old != QuestionType.TrueFalse || Options.Count != 2)
                {
                    Options =
                    [
                        new OptionDraft { Text = "Goed", IsCorrect = true },
                        new OptionDraft { Text = "Fout" }
                    ];
                }
                break;
            case QuestionType.MultipleChoice:
                if (old == QuestionType.TrueFalse) Options = [new(), new(), new()];
                // Keep only the first correct option.
                var first = true;
                foreach (var o in Options)
                {
                    if (o.IsCorrect && !first) o.IsCorrect = false;
                    if (o.IsCorrect) first = false;
                }
                EnsureOptions(2);
                break;
            case QuestionType.MultipleSelect:
                if (old == QuestionType.TrueFalse) Options = [new(), new(), new()];
                EnsureOptions(2);
                break;
            case QuestionType.FillIn:
                if (Answers.Count == 0) Answers.Add(new AnswerDraft());
                break;
        }
    }

    private void EnsureOptions(int min)
    {
        while (Options.Count < min) Options.Add(new OptionDraft());
    }

    public bool UsesOptions => Type != QuestionType.FillIn;

    public void SetSingleCorrect(OptionDraft option)
    {
        foreach (var o in Options) o.IsCorrect = ReferenceEquals(o, option);
    }

    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Builds a new detached Question. Empty options/answers are dropped.</summary>
    public Question ToQuestion()
    {
        var q = new Question
        {
            Id = Id,
            TopicId = TopicId,
            Type = Type,
            Prompt = Prompt?.Trim() ?? "",
            Context = Clean(Context),
            Tip = Clean(Tip),
            Explanation = Clean(Explanation),
            Difficulty = Difficulty,
            Source = Source,
            ExternalId = ExternalId,
            CreatedAt = CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };
        if (UsesOptions)
        {
            var order = 0;
            foreach (var o in Options.Where(o => !string.IsNullOrWhiteSpace(o.Text)))
                q.Options.Add(new AnswerOption { Text = o.Text.Trim(), IsCorrect = o.IsCorrect, Feedback = Clean(o.Feedback), SortOrder = order++ });
        }
        else
        {
            q.AcceptedAnswers = Answers.Select(a => a.Text?.Trim()).Where(a => !string.IsNullOrEmpty(a)).Distinct().Select(a => a!).ToList();
        }
        return q;
    }

    /// <summary>Quick client-side checks; the service does the authoritative validation.</summary>
    public List<string> LocalErrors()
    {
        var errors = new List<string>();
        if (TopicId <= 0) errors.Add("Kies een onderwerp.");
        if (string.IsNullOrWhiteSpace(Prompt)) errors.Add("Vul de vraag in.");
        if (UsesOptions)
        {
            var filled = Options.Where(o => !string.IsNullOrWhiteSpace(o.Text)).ToList();
            if (filled.Count < 2) errors.Add("Geef minimaal twee antwoordopties.");
            var correct = filled.Count(o => o.IsCorrect);
            if (correct == 0) errors.Add("Markeer het juiste antwoord.");
            else if (Type != QuestionType.MultipleSelect && correct > 1) errors.Add("Bij dit vraagtype is precies één antwoord juist.");
        }
        else if (!Answers.Any(a => !string.IsNullOrWhiteSpace(a.Text)))
        {
            errors.Add("Geef minimaal één goed antwoord.");
        }
        return errors;
    }
}

public class OptionDraft
{
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
    public string? Feedback { get; set; }
    public bool ShowFeedback { get; set; }
}

public class AnswerDraft
{
    public string Text { get; set; } = "";
}
