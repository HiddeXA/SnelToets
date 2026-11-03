namespace SnelToets.Core.Models;

/// <summary>Where a piece of content came from.</summary>
public enum ContentSource
{
    Manual = 0,
    Import = 1,
    Ai = 2,
    Seed = 3
}

/// <summary>The kind of question. Determines how an answer is checked.</summary>
public enum QuestionType
{
    /// <summary>Exactly one correct option.</summary>
    MultipleChoice = 0,
    /// <summary>One or more correct options; the student must select exactly the correct set.</summary>
    MultipleSelect = 1,
    /// <summary>Two options ("Goed"/"Fout" or "Waar"/"Niet waar"), one correct. Stored as options.</summary>
    TrueFalse = 2,
    /// <summary>Open text; correct when it matches one of <see cref="Question.AcceptedAnswers"/> (normalised).</summary>
    FillIn = 3
}

/// <summary>A subject ("vak"), e.g. "Nederlands 3F" or "Bedrijfseconomie".</summary>
public class Subject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Optional accent colour as a CSS colour (e.g. "#3b6cf6").</summary>
    public string? Color { get; set; }
    public ContentSource Source { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Topic> Topics { get; set; } = [];
}

/// <summary>A topic ("onderwerp") inside a subject, with its own theory and question bank.</summary>
public class Topic
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Theory shown before practising and in the "Bekijk theorie" pop-up. Markdown.</summary>
    public string? TheoryMarkdown { get; set; }
    public int SortOrder { get; set; }
    public ContentSource Source { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Question> Questions { get; set; } = [];
}

public class Question
{
    public int Id { get; set; }
    public int TopicId { get; set; }
    public Topic? Topic { get; set; }
    public QuestionType Type { get; set; }
    /// <summary>The question itself, e.g. "Welke werkwoordsvorm is juist gespeld?". Markdown allowed.</summary>
    public string Prompt { get; set; } = "";
    /// <summary>Optional context such as the sentence with a gap ("Hij … elke dag."). Markdown allowed.</summary>
    public string? Context { get; set; }
    /// <summary>Options for MultipleChoice / MultipleSelect / TrueFalse.</summary>
    public List<AnswerOption> Options { get; set; } = [];
    /// <summary>Accepted answers for FillIn questions.</summary>
    public List<string> AcceptedAnswers { get; set; } = [];
    /// <summary>Shown automatically after a wrong answer: the rule applied to this question. Markdown.</summary>
    public string? Tip { get; set; }
    /// <summary>Optional longer explanation shown with the result (also after a correct answer). Markdown.</summary>
    public string? Explanation { get; set; }
    /// <summary>1 = easy, 2 = medium, 3 = hard.</summary>
    public int Difficulty { get; set; } = 1;
    public ContentSource Source { get; set; }
    /// <summary>Stable id from an import file or AI, used to update instead of duplicate on re-import.</summary>
    public string? ExternalId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AnswerOption
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
    /// <summary>Optional feedback specific to choosing this option (e.g. why this distractor is wrong).</summary>
    public string? Feedback { get; set; }
    public int SortOrder { get; set; }
}
