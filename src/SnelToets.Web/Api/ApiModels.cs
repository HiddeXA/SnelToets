using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SnelToets.Core.Import;
using SnelToets.Core.Models;

namespace SnelToets.Web.Api;

// Shapes returned by the REST API and the MCP tools. Entities are never serialized directly
// (they contain navigation cycles); these records are compact and camelCase.

public static class SnelToetsJson
{
    /// <summary>camelCase, enums as strings, readable non-ASCII (Dutch quotes and accents are not escaped).</summary>
    public static readonly JsonSerializerOptions Options = Create();

    public static JsonSerializerOptions Create(bool indented = false)
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            // Required by the MCP SDK (it marks the options read-only for schema generation).
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}

public record SubjectSummary(int Id, string Name, string? Description, string? Color, ContentSource Source,
    int QuestionCount, IReadOnlyList<TopicSummary> Topics);

public record TopicSummary(int Id, string Name, int SortOrder, int QuestionCount, bool HasTheory);

public record TopicDetail(int Id, int SubjectId, string SubjectName, string Name, int SortOrder, ContentSource Source,
    string? TheoryMarkdown, int QuestionCount, IReadOnlyList<QuestionSummary>? Questions);

/// <summary>One-line view of a question (for overviews and duplicate checks).</summary>
public record QuestionSummary(int Id, string? ExternalId, string Type, string Prompt, string? Context, int Difficulty, bool HasTip,
    string? CorrectAnswer);

/// <summary>Full view of a question: the import shape plus the database ids.</summary>
public record QuestionView(int Id, int TopicId, string? ExternalId, string Type, string Prompt, string? Context,
    IReadOnlyList<OptionView>? Options, IReadOnlyList<string>? Answers, string? Tip, string? Explanation, int Difficulty,
    ContentSource Source, DateTime UpdatedAt);

public record OptionView(int Id, string Text, bool Correct, string? Feedback);

public record TestSummary(int Id, string Title, string? Description, string Mode, int? TimeLimitMinutes, int PassPercentage,
    int? RandomQuestionCount, int QuestionCount, IReadOnlyList<int> TopicIds, IReadOnlyList<int> FixedQuestionIds,
    ContentSource Source, string StudentUrl);

/// <summary>Body of <c>POST /api/tests</c> and the arguments of the MCP tool <c>create_test</c>.</summary>
public class CreateTestRequest
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>"practice" or "exam" (default).</summary>
    public string? Mode { get; set; }
    public List<int>? TopicIds { get; set; }
    public List<int>? QuestionIds { get; set; }
    public int? RandomQuestionCount { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public int? PassPercentage { get; set; }
    public bool? ShuffleQuestions { get; set; }
    public bool? ShuffleOptions { get; set; }
}

public record CreatedTest(int TestId, string Title, string Mode, int QuestionCount, string StudentUrl, string AdminUrl);

public record BatchItemResult(int Index, string Status, int? QuestionId, string? Prompt,
    IReadOnlyList<string>? Errors, IReadOnlyList<string>? Warnings);

public record BatchResult(int TopicId, string TopicName, int Created, int Updated, int Failed, IReadOnlyList<BatchItemResult> Items)
{
    public string Summary => Failed == 0
        ? $"{Saved(Created + Updated)} opgeslagen ({Created} nieuw, {Updated} bijgewerkt)."
        : $"{Created + Updated} van {Saved(Items.Count)} opgeslagen ({Created} nieuw, {Updated} bijgewerkt); {Failed} afgekeurd – zie 'errors' per item, verbeter die en stuur alleen die opnieuw.";

    private static string Saved(int n) => n == 1 ? "1 vraag" : $"{n} vragen";
}

/// <summary>Maps between the exchange format (<see cref="QuestionDto"/>) and the domain model.</summary>
public static class QuestionMapper
{
    public static string TypeName(QuestionType type) => type switch
    {
        QuestionType.MultipleSelect => QuestionTypeNames.MultipleSelect,
        QuestionType.TrueFalse => QuestionTypeNames.TrueFalse,
        QuestionType.FillIn => QuestionTypeNames.FillIn,
        _ => QuestionTypeNames.MultipleChoice
    };

    public static bool TryParseType(string? name, out QuestionType type)
    {
        // Same lenient rules as the importer: ignore case, '-', '_' and spaces; accept a few Dutch/short aliases.
        var key = new string((name ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        switch (key)
        {
            case "" or "multiplechoice" or "meerkeuze" or "meerkeuzevraag" or "mc" or "choice" or "keuze":
                type = QuestionType.MultipleChoice; return true;
            case "multipleselect" or "meerdere" or "meerdereantwoorden" or "meerkeuzemeerdere" or "ms" or "select" or "checkbox":
                type = QuestionType.MultipleSelect; return true;
            case "truefalse" or "goedfout" or "waaronwaar" or "waarnietwaar" or "juistonjuist" or "tf" or "boolean":
                type = QuestionType.TrueFalse; return true;
            case "fillin" or "invul" or "invullen" or "invulvraag" or "open" or "openvraag" or "text" or "tekst":
                type = QuestionType.FillIn; return true;
            default:
                type = QuestionType.MultipleChoice; return false;
        }
    }

    /// <summary>
    /// Builds a question entity from the exchange DTO. Structural validation is left to the services
    /// (IImportService.Validate / IContentService.CreateQuestionAsync), this only translates.
    /// </summary>
    public static Question ToEntity(QuestionDto dto, int topicId, ContentSource source)
    {
        TryParseType(dto.Type, out var type);
        var q = new Question
        {
            TopicId = topicId,
            Type = type,
            Prompt = dto.Prompt?.Trim() ?? "",
            Context = Blank(dto.Context),
            Tip = Blank(dto.Tip),
            Explanation = Blank(dto.Explanation),
            Difficulty = dto.Difficulty,
            Source = source,
            ExternalId = Blank(dto.Id),
            UpdatedAt = DateTime.UtcNow
        };

        if (type == QuestionType.FillIn)
        {
            q.AcceptedAnswers = (dto.Answers ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList();
            if (q.AcceptedAnswers.Count == 0) // same fallback as the importer: option texts are accepted answers
                q.AcceptedAnswers = (dto.Options ?? []).Where(o => !string.IsNullOrWhiteSpace(o?.Text)).Select(o => o.Text.Trim()).ToList();
        }
        else if (type == QuestionType.TrueFalse && (dto.Options is null || dto.Options.Count == 0) && dto.Answer is bool answer)
        {
            q.Options =
            [
                new AnswerOption { Text = "Goed", IsCorrect = answer, SortOrder = 0 },
                new AnswerOption { Text = "Fout", IsCorrect = !answer, SortOrder = 1 }
            ];
        }
        else
        {
            q.Options = (dto.Options ?? []).Select((o, i) => new AnswerOption
            {
                Text = o.Text?.Trim() ?? "",
                IsCorrect = o.Correct,
                Feedback = Blank(o.Feedback),
                SortOrder = i
            }).ToList();
        }
        return q;
    }

    public static QuestionView ToView(Question q) => new(
        q.Id, q.TopicId, q.ExternalId, TypeName(q.Type), q.Prompt, q.Context,
        q.Type == QuestionType.FillIn ? null : q.Options.OrderBy(o => o.SortOrder).Select(o => new OptionView(o.Id, o.Text, o.IsCorrect, o.Feedback)).ToList(),
        q.Type == QuestionType.FillIn ? q.AcceptedAnswers : null,
        q.Tip, q.Explanation, q.Difficulty, q.Source, q.UpdatedAt);

    public static QuestionSummary ToSummary(Question q) => new(
        q.Id, q.ExternalId, TypeName(q.Type), q.Prompt, q.Context, q.Difficulty, !string.IsNullOrWhiteSpace(q.Tip),
        q.Type == QuestionType.FillIn
            ? string.Join(" | ", q.AcceptedAnswers)
            : string.Join(" | ", q.Options.Where(o => o.IsCorrect).OrderBy(o => o.SortOrder).Select(o => o.Text)));

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
