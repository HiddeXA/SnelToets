using System.Text.Json.Serialization;

namespace SnelToets.Core.Import;

// The exchange format for importing/exporting content. The same DTOs are used by the
// web upload, the REST API and the MCP tools, so an AI produces exactly what a teacher could upload.
// JSON uses camelCase property names. See docs/import-format.md.

public class ImportDocument
{
    public int Version { get; set; } = 1;
    public List<SubjectDto> Subjects { get; set; } = [];
    public List<TestDto> Tests { get; set; } = [];
}

public class SubjectDto
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Color { get; set; }
    public List<TopicDto> Topics { get; set; } = [];
}

public class TopicDto
{
    public string Name { get; set; } = "";
    /// <summary>Theory in Markdown.</summary>
    public string? Theory { get; set; }
    public List<QuestionDto> Questions { get; set; } = [];
}

public class QuestionDto
{
    /// <summary>Optional stable id. Re-importing a question with the same id in the same topic updates it.</summary>
    public string? Id { get; set; }
    /// <summary>"multiple-choice" | "multiple-select" | "true-false" | "fill-in". Default multiple-choice.</summary>
    public string Type { get; set; } = QuestionTypeNames.MultipleChoice;
    public string Prompt { get; set; } = "";
    public string? Context { get; set; }
    /// <summary>For choice questions. For true-false, may be omitted when <see cref="Answer"/> is given.</summary>
    public List<OptionDto>? Options { get; set; }
    /// <summary>Accepted answers for fill-in questions.</summary>
    public List<string>? Answers { get; set; }
    /// <summary>Shortcut for true-false: true = the statement is correct ("Goed").</summary>
    public bool? Answer { get; set; }
    public string? Tip { get; set; }
    public string? Explanation { get; set; }
    public int Difficulty { get; set; } = 1;
}

public class OptionDto
{
    public string Text { get; set; } = "";
    public bool Correct { get; set; }
    public string? Feedback { get; set; }
}

public class TestDto
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>"practice" (feedback per question) or "exam" (feedback at the end). Default exam.</summary>
    public string Mode { get; set; } = "exam";
    public int? TimeLimitMinutes { get; set; }
    public int PassPercentage { get; set; } = 80;
    public int? RandomQuestionCount { get; set; }
    public bool ShuffleQuestions { get; set; } = true;
    /// <summary>Topics to draw from, written as "Subject/Topic".</summary>
    public List<string>? Topics { get; set; }
    /// <summary>Fixed questions by their <see cref="QuestionDto.Id"/>.</summary>
    public List<string>? QuestionIds { get; set; }
}

public static class QuestionTypeNames
{
    public const string MultipleChoice = "multiple-choice";
    public const string MultipleSelect = "multiple-select";
    public const string TrueFalse = "true-false";
    public const string FillIn = "fill-in";

    public static readonly string[] All = [MultipleChoice, MultipleSelect, TrueFalse, FillIn];
}

public record ImportPreview(
    ImportDocument? Document,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    int SubjectCount,
    int TopicCount,
    int QuestionCount,
    int TestCount)
{
    [JsonIgnore] public bool IsValid => Document is not null && Errors.Count == 0;
}

public record ImportResult(
    int SubjectsCreated,
    int TopicsCreated,
    int QuestionsCreated,
    int QuestionsUpdated,
    int TestsCreated,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<int> CreatedTestIds);
