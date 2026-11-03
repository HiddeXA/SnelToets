using SnelToets.Core.Import;
using SnelToets.Core.Models;
using SnelToets.Core.Services;

namespace SnelToets.Web.Api;

/// <summary>
/// Operations shared by the REST API and the MCP tools, built on the core service interfaces.
/// Validation problems are reported as <see cref="ValidationException"/> with Dutch messages.
/// </summary>
public class ContentOperations(
    IContentService content,
    ITestService tests,
    IImportService import,
    IHttpContextAccessor http,
    IConfiguration config)
{
    public const string PublicBaseUrlKey = "Mcp:PublicBaseUrl";

    // ---------- reading ----------

    public async Task<List<SubjectSummary>> ListSubjectsAsync(CancellationToken ct = default)
    {
        var result = new List<SubjectSummary>();
        foreach (var s in await content.GetSubjectsAsync(ct))
            result.Add(await SummarizeAsync(s, ct));
        return result;
    }

    public async Task<SubjectSummary?> GetSubjectAsync(int id, CancellationToken ct = default)
    {
        var s = await content.GetSubjectAsync(id, ct);
        return s is null ? null : await SummarizeAsync(s, ct);
    }

    private async Task<SubjectSummary> SummarizeAsync(Subject s, CancellationToken ct)
    {
        var topics = await content.GetTopicsAsync(s.Id, ct);
        var counts = await content.CountQuestionsPerTopicAsync(s.Id, ct);
        var topicSummaries = topics.OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .Select(t => new TopicSummary(t.Id, t.Name, t.SortOrder, counts.GetValueOrDefault(t.Id), !string.IsNullOrWhiteSpace(t.TheoryMarkdown)))
            .ToList();
        return new SubjectSummary(s.Id, s.Name, s.Description, s.Color, s.Source, topicSummaries.Sum(t => t.QuestionCount), topicSummaries);
    }

    public async Task<TopicDetail?> GetTopicAsync(int topicId, bool includeQuestions, CancellationToken ct = default)
    {
        var t = await content.GetTopicAsync(topicId, ct);
        if (t is null) return null;
        var questions = await content.GetQuestionsAsync(topicId, null, 0, 1000, ct);
        var subjectName = t.Subject?.Name ?? (await content.GetSubjectAsync(t.SubjectId, ct))?.Name ?? "";
        return new TopicDetail(t.Id, t.SubjectId, subjectName, t.Name, t.SortOrder, t.Source, t.TheoryMarkdown, questions.Count,
            includeQuestions ? questions.Select(QuestionMapper.ToSummary).ToList() : null);
    }

    public async Task<List<TestSummary>> ListTestsAsync(CancellationToken ct = default)
    {
        var result = new List<TestSummary>();
        foreach (var listed in await tests.GetTestsAsync(ct))
        {
            var t = listed.Topics.Count == 0 && listed.Questions.Count == 0
                ? await tests.GetTestAsync(listed.Id, ct) ?? listed
                : listed;
            result.Add(new TestSummary(t.Id, t.Title, t.Description, ModeName(t.Mode), t.TimeLimitMinutes, t.PassPercentage,
                t.RandomQuestionCount, await tests.CountQuestionsAsync(t.Id, ct),
                t.Topics.Select(x => x.TopicId).ToList(),
                t.Questions.OrderBy(x => x.SortOrder).Select(x => x.QuestionId).ToList(),
                t.Source, AbsoluteUrl($"toets/{t.Id}")));
        }
        return result;
    }

    // ---------- writing ----------

    /// <summary>
    /// Creates (or, when an item's <c>id</c> matches an existing question in the topic, updates) questions one by one.
    /// Partial success: every item gets its own status and Dutch error messages. Returns null when the topic does not exist.
    /// </summary>
    public async Task<BatchResult?> CreateQuestionsAsync(int topicId, IReadOnlyList<QuestionDto> questions, ContentSource source, CancellationToken ct = default)
    {
        var topic = await content.GetTopicAsync(topicId, ct);
        if (topic is null) return null;
        var subjectName = topic.Subject?.Name ?? (await content.GetSubjectAsync(topic.SubjectId, ct))?.Name ?? "Vak";

        var existing = (await content.GetQuestionsAsync(topicId, null, 0, 100_000, ct))
            .Where(q => !string.IsNullOrWhiteSpace(q.ExternalId))
            .GroupBy(q => q.ExternalId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var items = new List<BatchItemResult>();
        int created = 0, updated = 0, failed = 0;
        for (var i = 0; i < questions.Count; i++)
        {
            var dto = questions[i] ?? new QuestionDto();
            var label = Shorten(dto.Prompt);
            var errors = new List<string>();
            IReadOnlyList<string> warnings = [];

            if (!QuestionMapper.TryParseType(dto.Type, out _))
                errors.Add($"Onbekend vraagtype '{dto.Type}'. Kies uit: {string.Join(", ", QuestionTypeNames.All)}.");
            else
            {
                var preview = import.Validate(new ImportDocument
                {
                    Subjects = [new SubjectDto { Name = subjectName, Topics = [new TopicDto { Name = topic.Name, Questions = [dto] }] }]
                });
                // Strip the location prefix of the wrapper document ("Vak 'x' › onderwerp 'y' › vraag 1: ").
                errors.AddRange(preview.Errors.Select(StripLocation));
                if (string.IsNullOrWhiteSpace(dto.Tip))
                    warnings = ["Deze vraag heeft geen tip. Schrijf altijd een tip die de regel toepast op deze vraag; die ziet de student direct na een fout antwoord."];
            }

            if (errors.Count == 0)
            {
                try
                {
                    var entity = QuestionMapper.ToEntity(dto, topicId, source);
                    if (entity.ExternalId is { } ext && existing.TryGetValue(ext, out var existingId))
                    {
                        entity.Id = existingId;
                        var saved = await content.UpdateQuestionAsync(entity, ct);
                        items.Add(new BatchItemResult(i, "updated", saved.Id, label, null, NullIfEmpty(warnings)));
                        updated++;
                        continue;
                    }
                    var createdQ = await content.CreateQuestionAsync(entity, ct);
                    if (createdQ.ExternalId is { } newExt) existing[newExt] = createdQ.Id;
                    items.Add(new BatchItemResult(i, "created", createdQ.Id, label, null, NullIfEmpty(warnings)));
                    created++;
                    continue;
                }
                catch (ValidationException ex)
                {
                    errors.AddRange(ex.Errors);
                }
            }

            items.Add(new BatchItemResult(i, "failed", null, label, errors, NullIfEmpty(warnings)));
            failed++;
        }
        return new BatchResult(topicId, topic.Name, created, updated, failed, items);
    }

    public async Task<CreatedTest> CreateTestAsync(CreateTestRequest request, ContentSource source, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var title = request.Title?.Trim() ?? "";
        if (title.Length == 0) errors.Add("Geef de toets een titel.");
        else if (title.Length > 200) errors.Add("De titel mag maximaal 200 tekens lang zijn.");

        if (!TryParseMode(request.Mode, out var mode))
            errors.Add($"Onbekende modus '{request.Mode}'. Kies 'practice' (oefentoets, feedback per vraag) of 'exam' (toets, feedback aan het eind).");

        var topicIds = (request.TopicIds ?? []).Distinct().ToList();
        var questionIds = (request.QuestionIds ?? []).Distinct().ToList();
        if (topicIds.Count == 0 && questionIds.Count == 0)
            errors.Add("Kies minstens één onderwerp (topicIds) of één vraag (questionIds).");

        foreach (var id in topicIds)
            if (await content.GetTopicAsync(id, ct) is null) errors.Add($"Onderwerp {id} bestaat niet.");
        foreach (var id in questionIds)
            if (await content.GetQuestionAsync(id, ct) is null) errors.Add($"Vraag {id} bestaat niet.");

        int? random = request.RandomQuestionCount is > 0 ? request.RandomQuestionCount : null;
        if (request.RandomQuestionCount is < 0) errors.Add("randomQuestionCount moet 1 of hoger zijn.");
        if (random is not null && topicIds.Count == 0)
            errors.Add("Een willekeurige trekking (randomQuestionCount) heeft onderwerpen nodig om uit te trekken (topicIds).");

        var pass = request.PassPercentage ?? 80;
        if (pass is < 1 or > 100) errors.Add("Het slagingspercentage moet tussen 1 en 100 liggen.");

        int? minutes = request.TimeLimitMinutes is > 0 ? request.TimeLimitMinutes : null;
        if (minutes > 600) errors.Add("De tijdslimiet mag maximaal 600 minuten zijn.");

        if (errors.Count > 0) throw new ValidationException(errors);

        var test = new Test
        {
            Title = title,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Mode = mode,
            TimeLimitMinutes = minutes,
            PassPercentage = pass,
            RandomQuestionCount = random,
            ShuffleQuestions = request.ShuffleQuestions ?? true,
            ShuffleOptions = request.ShuffleOptions ?? true,
            Source = source,
            Topics = topicIds.Select(id => new TestTopic { TopicId = id }).ToList(),
            Questions = questionIds.Select((id, i) => new TestQuestion { QuestionId = id, SortOrder = i }).ToList()
        };
        var saved = await tests.CreateTestAsync(test, ct);
        var count = await tests.CountQuestionsAsync(saved.Id, ct);
        return new CreatedTest(saved.Id, saved.Title, ModeName(saved.Mode), count,
            AbsoluteUrl($"toets/{saved.Id}"), AbsoluteUrl($"beheer/toets/{saved.Id}"));
    }

    // ---------- helpers ----------

    public static string ModeName(TestMode mode) => mode == TestMode.Practice ? "practice" : "exam";

    public static bool TryParseMode(string? value, out TestMode mode)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "" or "exam" or "toets" or "examen" or "tentamen": mode = TestMode.Exam; return true;
            case "practice" or "oefen" or "oefenen" or "oefentoets": mode = TestMode.Practice; return true;
            default: mode = TestMode.Exam; return false;
        }
    }

    /// <summary>
    /// Absolute URL for an app-relative path. Uses "Mcp:PublicBaseUrl" when configured (useful behind a proxy),
    /// otherwise the scheme/host of the current request, otherwise the relative path.
    /// </summary>
    public string AbsoluteUrl(string relative)
    {
        relative = relative.TrimStart('/');
        var configured = config[PublicBaseUrlKey];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/') + "/" + relative;
        var req = http.HttpContext?.Request;
        if (req is null || !req.Host.HasValue) return "/" + relative;
        return $"{req.Scheme}://{req.Host}{req.PathBase}/{relative}";
    }

    private static string Shorten(string? s)
    {
        s = (s ?? "").Trim().ReplaceLineEndings(" ");
        return s.Length <= 80 ? s : s[..77] + "…";
    }

    private static string StripLocation(string error)
    {
        var i = error.IndexOf("› vraag 1: ", StringComparison.Ordinal);
        if (i < 0) return error;
        var rest = error[(i + "› vraag 1: ".Length)..];
        return rest.Length > 0 ? char.ToUpperInvariant(rest[0]) + rest[1..] : error;
    }

    private static IReadOnlyList<string>? NullIfEmpty(IReadOnlyList<string> list) => list.Count == 0 ? null : list;
}
