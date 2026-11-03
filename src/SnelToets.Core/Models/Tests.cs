namespace SnelToets.Core.Models;

/// <summary>
/// Practice = feedback (tip/theory) after every question.
/// Exam = no feedback until the end; the student can move back and forth, optional time limit.
/// </summary>
public enum TestMode
{
    Practice = 0,
    Exam = 1
}

/// <summary>A test ("toets") composed by a teacher, a student or an AI.</summary>
public class Test
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public TestMode Mode { get; set; } = TestMode.Exam;
    public int? TimeLimitMinutes { get; set; }
    /// <summary>Minimum percentage to pass. Default 80, like the Hogeschooltaal norm.</summary>
    public int PassPercentage { get; set; } = 80;
    public bool ShuffleQuestions { get; set; } = true;
    public bool ShuffleOptions { get; set; } = true;
    /// <summary>
    /// When set, each attempt draws this many random questions from <see cref="Topics"/>
    /// (plus any fixed <see cref="Questions"/>). When null, exactly the fixed questions are used,
    /// or all questions of the selected topics when there are no fixed questions.
    /// </summary>
    public int? RandomQuestionCount { get; set; }
    public ContentSource Source { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<TestQuestion> Questions { get; set; } = [];
    public List<TestTopic> Topics { get; set; } = [];
}

public class TestQuestion
{
    public int TestId { get; set; }
    public int QuestionId { get; set; }
    public Question? Question { get; set; }
    public int SortOrder { get; set; }
}

public class TestTopic
{
    public int TestId { get; set; }
    public int TopicId { get; set; }
    public Topic? Topic { get; set; }
}
