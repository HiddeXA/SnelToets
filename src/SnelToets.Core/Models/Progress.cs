namespace SnelToets.Core.Models;

public enum AttemptKind
{
    /// <summary>A practice series of one topic (default 15 questions), feedback per question.</summary>
    TopicPractice = 0,
    /// <summary>A practice series mixing all topics of a subject, feedback per question.</summary>
    MixPractice = 1,
    /// <summary>An attempt at a <see cref="Test"/>; feedback depends on <see cref="Test.Mode"/>.</summary>
    Test = 2,
    /// <summary>Start test ("activatievragen"): a few questions per topic to set the starting mastery.</summary>
    Placement = 3
}

/// <summary>One run through a set of questions by one learner.</summary>
public class Attempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LearnerId { get; set; }
    public AttemptKind Kind { get; set; }
    /// <summary>Practice = immediate feedback, Exam = feedback at the end.</summary>
    public TestMode Mode { get; set; } = TestMode.Practice;
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int? TestId { get; set; }
    public Test? Test { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    /// <summary>For timed tests: answers after this moment are rejected and the attempt is finished.</summary>
    public DateTime? Deadline { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }
    /// <summary>Copied from the test (or 80 for practice) when the attempt starts.</summary>
    public int PassPercentage { get; set; } = 80;
    public List<AttemptItem> Items { get; set; } = [];
}

/// <summary>One question within an attempt, in the order it is shown.</summary>
public class AttemptItem
{
    public int Id { get; set; }
    public Guid AttemptId { get; set; }
    public int QuestionId { get; set; }
    public Question? Question { get; set; }
    public int SortOrder { get; set; }
    /// <summary>Option ids in display order, comma separated (options may be shuffled per attempt).</summary>
    public string? OptionOrder { get; set; }
    /// <summary>Selected option ids, comma separated (choice questions).</summary>
    public string? SelectedOptionIds { get; set; }
    /// <summary>Typed answer (FillIn).</summary>
    public string? TextAnswer { get; set; }
    public bool? IsCorrect { get; set; }
    public DateTime? AnsweredAt { get; set; }
}

/// <summary>How well a learner masters a topic, 0–100. Rises with correct answers, drops on mistakes.</summary>
public class TopicMastery
{
    public Guid LearnerId { get; set; }
    public int TopicId { get; set; }
    public Topic? Topic { get; set; }
    public double Score { get; set; }
    public int Answered { get; set; }
    public int Correct { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Per-learner history of a question, used to pick questions adaptively.</summary>
public class QuestionStat
{
    public Guid LearnerId { get; set; }
    public int QuestionId { get; set; }
    public int Seen { get; set; }
    public int Wrong { get; set; }
    public bool LastCorrect { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}
