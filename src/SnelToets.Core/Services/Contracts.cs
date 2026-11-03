using SnelToets.Core.Import;
using SnelToets.Core.Models;

namespace SnelToets.Core.Services;

// Service contracts shared by the web UI, the REST API and the MCP tools.
// Implementations live next to this file and are registered via ServiceCollectionExtensions.AddSnelToetsCore.

public interface IContentService
{
    Task<List<Subject>> GetSubjectsAsync(CancellationToken ct = default);
    /// <summary>Subject with its topics (ordered by SortOrder). No questions.</summary>
    Task<Subject?> GetSubjectAsync(int id, CancellationToken ct = default);
    Task<Subject> CreateSubjectAsync(Subject subject, CancellationToken ct = default);
    Task<Subject> UpdateSubjectAsync(Subject subject, CancellationToken ct = default);
    Task DeleteSubjectAsync(int id, CancellationToken ct = default);

    /// <summary>Topic including its Subject. No questions.</summary>
    Task<Topic?> GetTopicAsync(int id, CancellationToken ct = default);
    Task<List<Topic>> GetTopicsAsync(int subjectId, CancellationToken ct = default);
    Task<Topic> CreateTopicAsync(Topic topic, CancellationToken ct = default);
    Task<Topic> UpdateTopicAsync(Topic topic, CancellationToken ct = default);
    Task DeleteTopicAsync(int id, CancellationToken ct = default);
    /// <summary>Number of questions per topic id for the given subject.</summary>
    Task<Dictionary<int, int>> CountQuestionsPerTopicAsync(int subjectId, CancellationToken ct = default);

    /// <summary>Questions (with options, ordered) filtered by topic and/or free text in prompt/context.</summary>
    Task<List<Question>> GetQuestionsAsync(int? topicId = null, string? search = null, int skip = 0, int take = 200, CancellationToken ct = default);
    Task<Question?> GetQuestionAsync(int id, CancellationToken ct = default);
    /// <summary>Validates (see QuestionValidator) and saves. Throws ValidationException with Dutch messages.</summary>
    Task<Question> CreateQuestionAsync(Question question, CancellationToken ct = default);
    /// <summary>Replaces scalar fields, options and accepted answers.</summary>
    Task<Question> UpdateQuestionAsync(Question question, CancellationToken ct = default);
    Task DeleteQuestionAsync(int id, CancellationToken ct = default);
}

public interface ITestService
{
    Task<List<Test>> GetTestsAsync(CancellationToken ct = default);
    /// <summary>Test with Questions (+Question) and Topics (+Topic).</summary>
    Task<Test?> GetTestAsync(int id, CancellationToken ct = default);
    Task<Test> CreateTestAsync(Test test, CancellationToken ct = default);
    /// <summary>Replaces scalar fields and the fixed question / topic sets.</summary>
    Task<Test> UpdateTestAsync(Test test, CancellationToken ct = default);
    Task DeleteTestAsync(int id, CancellationToken ct = default);
    /// <summary>Number of questions an attempt would contain (fixed + random draw, capped by availability).</summary>
    Task<int> CountQuestionsAsync(int testId, CancellationToken ct = default);
}

public interface IPracticeService
{
    /// <summary>Adaptive series of one topic: prefers questions answered wrongly before or not seen yet.</summary>
    Task<Attempt> StartTopicPracticeAsync(Guid learnerId, int topicId, int count = 15, CancellationToken ct = default);
    /// <summary>Adaptive series across all topics of a subject, weighted towards low-mastery topics.</summary>
    Task<Attempt> StartMixPracticeAsync(Guid learnerId, int subjectId, int count = 15, CancellationToken ct = default);
    /// <summary>Start test: <paramref name="perTopic"/> questions per topic. Sets initial mastery (capped, never "mastered").</summary>
    Task<Attempt> StartPlacementAsync(Guid learnerId, int subjectId, int perTopic = 2, CancellationToken ct = default);
    Task<Attempt> StartTestAsync(Guid learnerId, int testId, CancellationToken ct = default);

    /// <summary>Attempt with Items (ordered) → Question → Options, and Test when applicable.</summary>
    Task<Attempt?> GetAttemptAsync(Guid attemptId, CancellationToken ct = default);
    Task<List<Attempt>> GetRecentAttemptsAsync(Guid learnerId, int take = 10, CancellationToken ct = default);

    /// <summary>
    /// Grades and stores an answer. Practice/Placement: updates mastery right away and the UI shows the result.
    /// Exam: stores the answer (can be changed until finished); the UI must not reveal the result before FinishAsync.
    /// </summary>
    Task<AnswerResult> SubmitAnswerAsync(Guid attemptId, int itemId, AnswerSubmission answer, CancellationToken ct = default);
    /// <summary>Finishes the attempt (idempotent). For exams, mastery is updated here.</summary>
    Task<AttemptResult> FinishAsync(Guid attemptId, CancellationToken ct = default);

    Task<List<TopicMastery>> GetMasteryAsync(Guid learnerId, int? subjectId = null, CancellationToken ct = default);
    /// <summary>"Studieadvies": per topic mastery, which ones need attention and where to start.</summary>
    Task<StudyAdvice> GetAdviceAsync(Guid learnerId, int subjectId, CancellationToken ct = default);
    /// <summary>"Wis je score en begin opnieuw" for one topic.</summary>
    Task ResetTopicAsync(Guid learnerId, int topicId, CancellationToken ct = default);
}

public interface IImportService
{
    /// <summary>Parses a .json or .csv upload into a document and validates it. Never throws for bad input.</summary>
    Task<ImportPreview> ParseAsync(Stream stream, string fileName, CancellationToken ct = default);
    /// <summary>Validates a document (also used for MCP input).</summary>
    ImportPreview Validate(ImportDocument document);
    /// <summary>
    /// Merges the document into the database: subjects and topics match by name (case-insensitive),
    /// questions match by Id within a topic (update) or are created. Tests are always created.
    /// </summary>
    Task<ImportResult> ImportAsync(ImportDocument document, ContentSource source = ContentSource.Import, CancellationToken ct = default);
    /// <summary>Exports content (one subject or everything) in the same format. Tests included when exporting everything.</summary>
    Task<ImportDocument> ExportAsync(int? subjectId = null, CancellationToken ct = default);
}

/// <summary>Seeds a demo subject on first start when the database is empty.</summary>
public interface IDemoSeeder
{
    Task SeedIfEmptyAsync(CancellationToken ct = default);
}

/// <summary>An answer as given by a learner. Choice questions use OptionIds, FillIn uses Text.</summary>
public record AnswerSubmission(IReadOnlyList<int> OptionIds, string? Text)
{
    public static AnswerSubmission Choice(params int[] optionIds) => new(optionIds, null);
    public static AnswerSubmission Typed(string text) => new([], text);
}

public record AnswerResult(
    int ItemId,
    bool IsCorrect,
    /// <summary>Tip for a wrong answer (null when correct).</summary>
    string? Tip,
    string? Explanation,
    /// <summary>Feedback attached to the chosen option(s), if any.</summary>
    string? OptionFeedback,
    IReadOnlyList<int> CorrectOptionIds,
    IReadOnlyList<string> AcceptedAnswers,
    int TopicId,
    /// <summary>Mastery before/after this answer (practice and placement only; null for exams).</summary>
    double? PreviousMastery,
    double? NewMastery,
    /// <summary>True when every item of the attempt has an answer.</summary>
    bool AttemptComplete);

public record AttemptResult(
    Guid AttemptId,
    int Correct,
    int Total,
    double Percentage,
    /// <summary>Only for tests: whether Percentage &gt;= PassPercentage.</summary>
    bool? Passed,
    IReadOnlyList<TopicResult> PerTopic);

public record TopicResult(int TopicId, string TopicName, int Correct, int Total, double? Mastery);

public record StudyAdvice(
    int SubjectId,
    IReadOnlyList<TopicAdvice> Topics,
    int? RecommendedTopicId,
    /// <summary>Dutch advice line, e.g. "Begin met Verleden tijd: hier valt de meeste winst te halen."</summary>
    string Message,
    bool PlacementDone);

public record TopicAdvice(
    int TopicId,
    string Name,
    double Mastery,
    /// <summary>Shown with an exclamation mark in the UI.</summary>
    bool NeedsAttention,
    bool Mastered,
    int QuestionCount,
    int Answered);

public class ValidationException(IReadOnlyList<string> errors)
    : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
