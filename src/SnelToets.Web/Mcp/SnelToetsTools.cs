using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SnelToets.Core.Import;
using SnelToets.Core.Models;
using SnelToets.Core.Services;
using SnelToets.Web.Api;

namespace SnelToets.Web.Mcp;

/// <summary>
/// MCP tools for SnelToets. Everything created here is marked <see cref="ContentSource.Ai"/>.
/// Results are compact JSON; problems are returned as tool errors with a clear (Dutch) message, never a stack trace.
/// </summary>
[McpServerToolType]
public sealed class SnelToetsTools(
    ContentOperations ops,
    IContentService content,
    IImportService import,
    ILogger<SnelToetsTools> logger)
{
    private const string LanguageRule =
        " Write all content in the students' language (usually Dutch) with flawless spelling.";

    // ---------------- reading ----------------

    [McpServerTool(Name = "list_subjects", Title = "List subjects", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists all subjects with their topics and the number of questions per topic. Call this first to reuse existing subjects/topics and to find topic ids.")]
    public Task<CallToolResult> ListSubjects(CancellationToken ct) =>
        Run(nameof(ListSubjects), async () => Ok(await ops.ListSubjectsAsync(ct)));

    [McpServerTool(Name = "get_topic", Title = "Get topic", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Returns one topic: its theory (Markdown) and a summary of its questions (id, external id, type, prompt, correct answer, whether it has a tip). Use it to avoid duplicate questions and to align new questions with the theory.")]
    public Task<CallToolResult> GetTopic(
        [Description("Topic id (from list_subjects).")] int topicId,
        [Description("Include the question summaries (default true).")] bool includeQuestions = true,
        CancellationToken ct = default) =>
        Run(nameof(GetTopic), async () =>
            await ops.GetTopicAsync(topicId, includeQuestions, ct) is { } t ? Ok(t) : Error($"Onderwerp {topicId} bestaat niet. Gebruik list_subjects voor de juiste id."));

    [McpServerTool(Name = "search_questions", Title = "Search questions", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Searches questions by free text in the prompt/context, optionally within one topic. Returns full questions (options with correct flags and feedback, accepted answers, tip, explanation).")]
    public Task<CallToolResult> SearchQuestions(
        [Description("Text to search for in prompt or context. Leave empty to list the questions of the topic.")] string? query = null,
        [Description("Optional topic id to search in.")] int? topicId = null,
        [Description("Maximum number of results (1-100, default 20).")] int limit = 20,
        CancellationToken ct = default) =>
        Run(nameof(SearchQuestions), async () =>
        {
            var list = await content.GetQuestionsAsync(topicId, string.IsNullOrWhiteSpace(query) ? null : query.Trim(), 0, Math.Clamp(limit, 1, 100), ct);
            return Ok(new { count = list.Count, questions = list.Select(QuestionMapper.ToView) });
        });

    [McpServerTool(Name = "list_tests", Title = "List tests", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists all tests with mode (practice/exam), time limit, pass percentage, question count, topic ids and the student URL.")]
    public Task<CallToolResult> ListTests(CancellationToken ct) =>
        Run(nameof(ListTests), async () => Ok(await ops.ListTestsAsync(ct)));

    [McpServerTool(Name = "get_import_format", Title = "Get import format", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Returns the documentation of the SnelToets JSON exchange format (subjects, topics, questions, tests), the validation rules and a complete example. Read this before using import_content or create_questions.")]
    public CallToolResult GetImportFormat() => Text(ImportFormat.Documentation);

    // ---------------- writing ----------------

    [McpServerTool(Name = "create_subject", Title = "Create subject", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Creates a subject (e.g. 'Nederlands 3F'). If a subject with the same name already exists, it is returned instead (alreadyExisted = true)." + LanguageRule)]
    public Task<CallToolResult> CreateSubject(
        [Description("Name of the subject, e.g. 'Nederlands – taalverzorging'.")] string name,
        [Description("Optional short description shown to students.")] string? description = null,
        [Description("Optional accent colour as CSS colour, e.g. '#3b6cf6'.")] string? color = null,
        CancellationToken ct = default) =>
        Run(nameof(CreateSubject), async () =>
        {
            if (string.IsNullOrWhiteSpace(name)) return Error("Geef het vak een naam.");
            var existing = (await content.GetSubjectsAsync(ct))
                .FirstOrDefault(s => string.Equals(s.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                return Ok(new { subjectId = existing.Id, name = existing.Name, alreadyExisted = true });
            var s = await content.CreateSubjectAsync(new Subject
            {
                Name = name.Trim(),
                Description = Blank(description),
                Color = Blank(color),
                Source = ContentSource.Ai
            }, ct);
            return Ok(new { subjectId = s.Id, name = s.Name, alreadyExisted = false, adminUrl = ops.AbsoluteUrl($"beheer/vak/{s.Id}") });
        });

    [McpServerTool(Name = "create_topic", Title = "Create topic", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Creates a topic inside a subject, with its theory in Markdown. The theory is shown before practising and in the 'Bekijk theorie' pop-up after a mistake, so keep it concise: the rule(s), a step-by-step approach and several example sentences (use headings, lists and **bold**). If the topic already exists in the subject it is returned unchanged (use update_topic_theory to change it)." + LanguageRule)]
    public Task<CallToolResult> CreateTopic(
        [Description("Id of the subject (from list_subjects or create_subject).")] int subjectId,
        [Description("Name of the topic, e.g. 'Werkwoordspelling: tegenwoordige tijd'.")] string name,
        [Description("Theory in Markdown, in the students' language.")] string? theoryMarkdown = null,
        CancellationToken ct = default) =>
        Run(nameof(CreateTopic), async () =>
        {
            if (string.IsNullOrWhiteSpace(name)) return Error("Geef het onderwerp een naam.");
            if (await content.GetSubjectAsync(subjectId, ct) is null) return Error($"Vak {subjectId} bestaat niet. Gebruik list_subjects of create_subject.");
            var topics = await content.GetTopicsAsync(subjectId, ct);
            var existing = topics.FirstOrDefault(t => string.Equals(t.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                return Ok(new { topicId = existing.Id, name = existing.Name, alreadyExisted = true, hint = "Gebruik update_topic_theory om de theorie te wijzigen." });
            var t = await content.CreateTopicAsync(new Topic
            {
                SubjectId = subjectId,
                Name = name.Trim(),
                TheoryMarkdown = Blank(theoryMarkdown),
                SortOrder = topics.Count == 0 ? 0 : topics.Max(x => x.SortOrder) + 1,
                Source = ContentSource.Ai
            }, ct);
            return Ok(new { topicId = t.Id, name = t.Name, alreadyExisted = false, studentUrl = ops.AbsoluteUrl($"onderwerp/{t.Id}") });
        });

    [McpServerTool(Name = "update_topic_theory", Title = "Update topic theory", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Replaces the theory (Markdown) of a topic, and optionally renames it." + LanguageRule)]
    public Task<CallToolResult> UpdateTopicTheory(
        [Description("Topic id.")] int topicId,
        [Description("The complete new theory in Markdown (replaces the old text).")] string theoryMarkdown,
        [Description("Optional new name for the topic.")] string? name = null,
        CancellationToken ct = default) =>
        Run(nameof(UpdateTopicTheory), async () =>
        {
            var t = await content.GetTopicAsync(topicId, ct);
            if (t is null) return Error($"Onderwerp {topicId} bestaat niet.");
            t.TheoryMarkdown = Blank(theoryMarkdown);
            if (!string.IsNullOrWhiteSpace(name)) t.Name = name.Trim();
            var saved = await content.UpdateTopicAsync(t, ct);
            return Ok(new { topicId = saved.Id, name = saved.Name, theoryLength = saved.TheoryMarkdown?.Length ?? 0 });
        });

    [McpServerTool(Name = "create_questions", Title = "Create questions (batch)", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        Adds a batch of questions (best 10-20 per call) to one topic. Each question uses the import format:
        { "id": "stable-id", "type": "multiple-choice|multiple-select|true-false|fill-in", "prompt": "...", "context": "sentence with a gap ...",
          "options": [{ "text": "...", "correct": true|false, "feedback": "why this option is right/wrong" }], "answers": ["for fill-in"],
          "answer": true|false (true-false shortcut instead of options), "tip": "...", "explanation": "...", "difficulty": 1-3 }.
        Rules: multiple-choice and true-false have exactly one correct option; multiple-select at least one; choice questions need >= 2 options;
        true-false has exactly 2 options or 'answer'; fill-in needs >= 1 accepted answer.
        ALWAYS write a 'tip' that applies the rule to THIS question (shown right after a wrong answer, like Hogeschooltaal), e.g.
        "'Je' kun je hier niet vervangen door 'jij' → geen onderwerp → stam + t: 'Vind je'." and give wrong options a short 'feedback'.
        A question whose 'id' already exists in the topic is updated instead of duplicated.
        Partial success: valid questions are saved, invalid ones are reported per item (index, errors) – fix and resend only those.
        """ + LanguageRule)]
    public Task<CallToolResult> CreateQuestions(
        [Description("Topic id to add the questions to.")] int topicId,
        [Description("The questions, in the import format described above.")] List<QuestionDto> questions,
        CancellationToken ct = default) =>
        Run(nameof(CreateQuestions), async () =>
        {
            if (questions is null || questions.Count == 0) return Error("Geen vragen ontvangen: 'questions' is leeg.");
            if (questions.Count > 100) return Error("Stuur maximaal 100 vragen per keer (liefst 10 à 20).");
            var result = await ops.CreateQuestionsAsync(topicId, questions, ContentSource.Ai, ct);
            if (result is null) return Error($"Onderwerp {topicId} bestaat niet. Gebruik list_subjects of create_topic.");
            return Ok(new
            {
                summary = result.Summary,
                result.TopicId,
                result.TopicName,
                result.Created,
                result.Updated,
                result.Failed,
                createdIds = result.Items.Where(i => i.Status == "created").Select(i => i.QuestionId),
                items = result.Items
            });
        });

    [McpServerTool(Name = "import_content", Title = "Import content document", Destructive = false, OpenWorld = false)]
    [Description("""
        Imports a complete SnelToets document at once: { "version": 1, "subjects": [ { "name", "description", "topics": [ { "name", "theory", "questions": [...] } ] } ], "tests": [...] }.
        Subjects and topics are matched by name (case-insensitive) and created when missing; questions with an existing 'id' in the same topic are updated.
        The document is validated first; when it has errors nothing is imported and the errors are returned. Use dryRun=true to only validate.
        Call get_import_format for the full specification and an example.
        """ + LanguageRule)]
    public Task<CallToolResult> ImportContent(
        [Description("The import document as a JSON object (not a string).")] ImportDocument document,
        [Description("Only validate and report what would be imported (default false).")] bool dryRun = false,
        CancellationToken ct = default) =>
        Run(nameof(ImportContent), async () =>
        {
            if (document is null) return Error("Geen document ontvangen.");
            var preview = import.Validate(document);
            var counts = new { subjects = preview.SubjectCount, topics = preview.TopicCount, questions = preview.QuestionCount, tests = preview.TestCount };
            if (!preview.IsValid)
                return Error(SnelToetsJson.Serialize(new
                {
                    message = "Het document bevat fouten; er is niets geïmporteerd. Verbeter de fouten en probeer het opnieuw.",
                    errors = preview.Errors,
                    warnings = preview.Warnings,
                    counts
                }));
            if (dryRun)
                return Ok(new { valid = true, message = "Het document is geldig (proefrun, niets opgeslagen).", counts, warnings = preview.Warnings });

            var result = await import.ImportAsync(preview.Document ?? document, ContentSource.Ai, ct);
            return Ok(new
            {
                message = "Import voltooid.",
                result.SubjectsCreated,
                result.TopicsCreated,
                result.QuestionsCreated,
                result.QuestionsUpdated,
                result.TestsCreated,
                result.Warnings,
                tests = result.CreatedTestIds.Select(id => new { testId = id, studentUrl = ops.AbsoluteUrl($"toets/{id}") })
            });
        });

    [McpServerTool(Name = "create_test", Title = "Create test", Destructive = false, OpenWorld = false)]
    [Description("""
        Composes a test ("toets") from topics and/or fixed questions. mode 'exam' = no feedback until the end, can move back and forth, optional time limit;
        mode 'practice' = tip and theory after every question. With randomQuestionCount each attempt draws that many random questions from topicIds
        (plus any fixed questionIds); without it the fixed questions are used, or all questions of the topics.
        Returns the test id and the student URL to share. Title and description in the students' language.
        """)]
    public Task<CallToolResult> CreateTest(
        [Description("Title of the test, e.g. 'Oefentoets werkwoordspelling'.")] string title,
        [Description("'practice' or 'exam'.")] string mode = "exam",
        [Description("Topic ids to draw questions from.")] List<int>? topicIds = null,
        [Description("Fixed question ids that are always included.")] List<int>? questionIds = null,
        [Description("Number of random questions per attempt drawn from topicIds (e.g. 15). Omit to use all.")] int? randomQuestionCount = null,
        [Description("Optional time limit in minutes (exam mode).")] int? timeLimitMinutes = null,
        [Description("Minimum percentage to pass (default 80).")] int passPercentage = 80,
        [Description("Optional description shown on the intro page of the test.")] string? description = null,
        CancellationToken ct = default) =>
        Run(nameof(CreateTest), async () =>
        {
            var created = await ops.CreateTestAsync(new CreateTestRequest
            {
                Title = title,
                Description = description,
                Mode = mode,
                TopicIds = topicIds,
                QuestionIds = questionIds,
                RandomQuestionCount = randomQuestionCount,
                TimeLimitMinutes = timeLimitMinutes,
                PassPercentage = passPercentage
            }, ContentSource.Ai, ct);
            return Ok(created);
        });

    [McpServerTool(Name = "delete_question", Title = "Delete question", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Deletes one question by its numeric id (from get_topic or search_questions). Learner statistics for the question are removed too.")]
    public Task<CallToolResult> DeleteQuestion(
        [Description("Numeric question id.")] int questionId,
        CancellationToken ct = default) =>
        Run(nameof(DeleteQuestion), async () =>
        {
            var q = await content.GetQuestionAsync(questionId, ct);
            if (q is null) return Error($"Vraag {questionId} bestaat niet.");
            await content.DeleteQuestionAsync(questionId, ct);
            return Ok(new { deleted = questionId, prompt = q.Prompt });
        });

    // ---------------- helpers ----------------

    private async Task<CallToolResult> Run(string tool, Func<Task<CallToolResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ValidationException ex)
        {
            return Error(SnelToetsJson.Serialize(new { message = "De invoer klopt niet.", errors = ex.Errors }));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MCP tool {Tool} failed", tool);
            return Error($"Er ging iets mis bij '{tool}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static CallToolResult Ok(object value) => Text(SnelToetsJson.Serialize(value));

    private static CallToolResult Text(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    private static CallToolResult Error(string message) => new() { Content = [new TextContentBlock { Text = message }], IsError = true };

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
