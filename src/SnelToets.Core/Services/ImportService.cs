using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SnelToets.Core.Data;
using SnelToets.Core.Import;
using SnelToets.Core.Models;

namespace SnelToets.Core.Services;

/// <summary>Import (JSON/CSV) and export of content in the exchange format of <see cref="ImportDocument"/>.</summary>
public partial class ImportService(SnelToetsDbContext db) : IImportService
{
    private readonly SnelToetsDbContext _db = db;

    /// <summary>Uploads larger than this are rejected.</summary>
    public const int MaxFileBytes = 10 * 1024 * 1024;

    // ---------- Parse ----------

    public async Task<ImportPreview> ParseAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        string text;
        try
        {
            using var limited = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (limited.Length + read > MaxFileBytes)
                    return Failed($"Het bestand is te groot (maximaal {MaxFileBytes / 1024 / 1024} MB).");
                limited.Write(buffer, 0, read);
            }
            limited.Position = 0;
            using var reader = new StreamReader(limited, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = await reader.ReadToEndAsync(ct);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            return Failed($"Het bestand kon niet worden gelezen: {ex.Message}");
        }

        var extension = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (extension is not (".json" or ".csv" or ".txt"))
            return Failed("Onbekend bestandstype. Upload een .json- of .csv-bestand.");
        if (string.IsNullOrWhiteSpace(text))
            return Failed("Het bestand is leeg.");

        if (extension == ".json")
        {
            ImportDocument document;
            try
            {
                document = ImportJson.Deserialize(text);
            }
            catch (JsonException ex)
            {
                var where = ex.LineNumber is long line ? $" (regel {line + 1}, positie {(ex.BytePositionInLine ?? 0) + 1})" : "";
                return Failed($"Het JSON-bestand is ongeldig{where}: {CleanJsonMessage(ex.Message)}");
            }
            return Validate(document);
        }

        var csv = CsvImportParser.Parse(text);
        var preview = Validate(csv.Document);
        return preview with
        {
            Errors = [.. csv.Errors, .. preview.Errors],
            Warnings = [.. csv.Warnings, .. preview.Warnings]
        };
    }

    private static ImportPreview Failed(string error) => new(null, [error], [], 0, 0, 0, 0);

    private static string CleanJsonMessage(string message)
    {
        var idx = message.IndexOf(" Path:", StringComparison.Ordinal);
        return idx > 0 ? message[..idx] : message;
    }

    // ---------- Validate ----------

    public ImportPreview Validate(ImportDocument document)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (document is null) return Failed("Er is geen document opgegeven.");
        Sanitize(document);

        int topicCount = 0, questionCount = 0;
        for (var si = 0; si < document.Subjects.Count; si++)
        {
            var subject = document.Subjects[si];
            var sLoc = string.IsNullOrWhiteSpace(subject.Name) ? $"Vak {si + 1}" : $"Vak '{subject.Name.Trim()}'";
            if (string.IsNullOrWhiteSpace(subject.Name)) errors.Add($"{sLoc}: de naam van het vak ontbreekt.");
            if (subject.Topics.Count == 0) warnings.Add($"{sLoc}: bevat geen onderwerpen.");

            for (var ti = 0; ti < subject.Topics.Count; ti++)
            {
                var topic = subject.Topics[ti];
                topicCount++;
                var tLoc = string.IsNullOrWhiteSpace(topic.Name) ? $"{sLoc} › onderwerp {ti + 1}" : $"{sLoc} › onderwerp '{topic.Name.Trim()}'";
                if (string.IsNullOrWhiteSpace(topic.Name)) errors.Add($"{tLoc}: de naam van het onderwerp ontbreekt.");
                if (topic.Questions.Count == 0) warnings.Add($"{tLoc}: bevat nog geen vragen.");
                if (string.IsNullOrWhiteSpace(topic.Theory)) warnings.Add($"{tLoc}: er is geen theorie opgegeven.");

                var ids = new HashSet<string>(StringComparer.Ordinal);
                var withoutTip = 0;
                for (var qi = 0; qi < topic.Questions.Count; qi++)
                {
                    var dto = topic.Questions[qi];
                    questionCount++;
                    var loc = $"{tLoc} › vraag {qi + 1}";
                    if (dto is null) { errors.Add($"{loc}: de vraag is leeg."); continue; }
                    if (!string.IsNullOrWhiteSpace(dto.Id) && !ids.Add(dto.Id.Trim()))
                        errors.Add($"{loc}: id '{dto.Id.Trim()}' komt vaker voor in dit onderwerp.");

                    var question = MapQuestion(dto, out var mapError);
                    if (mapError is not null) { errors.Add($"{loc}: {mapError}"); continue; }
                    errors.AddRange(QuestionValidator.Validate(question).Select(e => $"{loc}: {e}"));
                    if (string.IsNullOrWhiteSpace(dto.Tip)) withoutTip++;
                }
                if (withoutTip > 0)
                    warnings.Add(withoutTip == 1
                        ? $"{tLoc}: 1 vraag heeft geen tip; na een fout antwoord verschijnt dan een algemene tip."
                        : $"{tLoc}: {withoutTip} vragen hebben geen tip; na een fout antwoord verschijnt dan een algemene tip.");
            }
        }

        for (var i = 0; i < document.Tests.Count; i++)
        {
            var test = document.Tests[i];
            var loc = string.IsNullOrWhiteSpace(test.Title) ? $"Toets {i + 1}" : $"Toets '{test.Title.Trim()}'";
            if (string.IsNullOrWhiteSpace(test.Title)) errors.Add($"{loc}: de titel ontbreekt.");
            if (!TryParseMode(test.Mode, out _)) errors.Add($"{loc}: onbekende modus '{test.Mode}'. Gebruik 'practice' of 'exam'.");
            if (test.PassPercentage is < 0 or > 100) errors.Add($"{loc}: het slagingspercentage moet tussen 0 en 100 liggen.");
            if (test.TimeLimitMinutes is <= 0) errors.Add($"{loc}: de tijdslimiet moet groter dan 0 minuten zijn (of laat hem weg).");
            if (test.RandomQuestionCount is <= 0) errors.Add($"{loc}: het aantal willekeurige vragen moet groter dan 0 zijn (of laat het weg).");
            var topicRefs = test.Topics ?? [];
            if (topicRefs.Count == 0 && (test.QuestionIds ?? []).Count == 0)
                errors.Add($"{loc}: kies onderwerpen ('topics') en/of vaste vragen ('questionIds').");
            if (test.RandomQuestionCount is > 0 && topicRefs.Count == 0)
                warnings.Add($"{loc}: er is een aantal willekeurige vragen opgegeven maar geen onderwerpen om uit te kiezen.");
            foreach (var r in topicRefs.Where(r => string.IsNullOrWhiteSpace(r) || !r.Contains('/')))
                warnings.Add($"{loc}: onderwerp '{r}' heeft niet de vorm 'Vak/Onderwerp'.");
        }

        if (document.Subjects.Count == 0 && document.Tests.Count == 0)
            errors.Add("Het document bevat geen vakken en geen toetsen.");

        return new ImportPreview(document, errors, warnings, document.Subjects.Count, topicCount, questionCount, document.Tests.Count);
    }

    /// <summary>Maps a DTO to an (unsaved) question. Returns an error for an unknown type.</summary>
    internal static Question MapQuestion(QuestionDto dto, out string? error)
    {
        error = null;
        if (!QuestionTypes.TryParse(dto.Type, out var type))
        {
            error = $"onbekend vraagtype '{dto.Type}'. Kies uit: {string.Join(", ", QuestionTypeNames.All)}.";
            return new Question();
        }

        var q = new Question
        {
            Type = type,
            Prompt = (dto.Prompt ?? "").Trim(),
            Context = ContentService.Blank(dto.Context),
            Tip = ContentService.Blank(dto.Tip),
            Explanation = ContentService.Blank(dto.Explanation),
            Difficulty = dto.Difficulty,
            ExternalId = ContentService.Blank(dto.Id)
        };

        var options = dto.Options ?? [];
        if (type == QuestionType.FillIn)
        {
            var answers = (dto.Answers ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList();
            if (answers.Count == 0)
                answers = options.Where(o => !string.IsNullOrWhiteSpace(o.Text)).Select(o => o.Text.Trim()).ToList();
            q.AcceptedAnswers = answers;
        }
        else if (type == QuestionType.TrueFalse && options.Count == 0 && dto.Answer is bool answer)
        {
            q.Options =
            [
                new AnswerOption { Text = "Goed", IsCorrect = answer, SortOrder = 0 },
                new AnswerOption { Text = "Fout", IsCorrect = !answer, SortOrder = 1 }
            ];
        }
        else
        {
            q.Options = options.Select((o, i) => new AnswerOption
            {
                Text = (o?.Text ?? "").Trim(),
                IsCorrect = o?.Correct ?? false,
                Feedback = ContentService.Blank(o?.Feedback),
                SortOrder = i
            }).ToList();
        }
        return q;
    }

    private static void Sanitize(ImportDocument document)
    {
        document.Subjects ??= [];
        document.Tests ??= [];
        document.Subjects.RemoveAll(s => s is null);
        document.Tests.RemoveAll(t => t is null);
        foreach (var s in document.Subjects)
        {
            s.Name ??= "";
            s.Topics ??= [];
            s.Topics.RemoveAll(t => t is null);
            foreach (var t in s.Topics)
            {
                t.Name ??= "";
                t.Questions ??= [];
            }
        }
        foreach (var t in document.Tests) t.Title ??= "";
    }

    private static bool TryParseMode(string? mode, out TestMode result)
    {
        switch ((mode ?? "").Trim().ToLowerInvariant())
        {
            case "" or "exam" or "examen" or "toets" or "tentamen":
                result = TestMode.Exam; return true;
            case "practice" or "oefenen" or "oefening" or "oefen":
                result = TestMode.Practice; return true;
            default:
                result = TestMode.Exam; return false;
        }
    }

    // ---------- Import ----------

    public async Task<ImportResult> ImportAsync(ImportDocument document, ContentSource source = ContentSource.Import, CancellationToken ct = default)
    {
        var preview = Validate(document);
        if (preview.Errors.Count > 0) throw new ValidationException(preview.Errors);

        var warnings = new List<string>();
        int subjectsCreated = 0, topicsCreated = 0, questionsCreated = 0, questionsUpdated = 0;
        var createdTests = new List<Test>();
        var imported = new Dictionary<string, Question>(StringComparer.Ordinal);
        var now = DateTime.UtcNow;

        _db.ChangeTracker.Clear();
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var subjects = await _db.Subjects.Include(s => s.Topics).ToListAsync(ct);
            foreach (var sDto in document.Subjects)
            {
                var name = sDto.Name.Trim();
                var subject = subjects.FirstOrDefault(s => string.Equals(s.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (subject is null)
                {
                    subject = new Subject { Name = name, Source = source, CreatedAt = now };
                    _db.Subjects.Add(subject);
                    subjects.Add(subject);
                    subjectsCreated++;
                }
                if (!string.IsNullOrWhiteSpace(sDto.Description)) subject.Description = sDto.Description.Trim();
                if (!string.IsNullOrWhiteSpace(sDto.Color)) subject.Color = sDto.Color.Trim();

                foreach (var tDto in sDto.Topics)
                {
                    var topicName = tDto.Name.Trim();
                    var topic = subject.Topics.FirstOrDefault(t => string.Equals(t.Name.Trim(), topicName, StringComparison.OrdinalIgnoreCase));
                    List<Question> existing = [];
                    if (topic is null)
                    {
                        topic = new Topic
                        {
                            Name = topicName,
                            Source = source,
                            CreatedAt = now,
                            SortOrder = subject.Topics.Count == 0 ? 1 : subject.Topics.Max(t => t.SortOrder) + 1
                        };
                        subject.Topics.Add(topic);
                        topicsCreated++;
                    }
                    else if (topic.Id > 0)
                    {
                        existing = await _db.Questions.Include(q => q.Options).Where(q => q.TopicId == topic.Id).ToListAsync(ct);
                    }
                    else
                    {
                        existing = topic.Questions;
                    }
                    if (!string.IsNullOrWhiteSpace(tDto.Theory)) topic.TheoryMarkdown = tDto.Theory.Trim();

                    foreach (var qDto in tDto.Questions)
                    {
                        var mapped = MapQuestion(qDto, out _);
                        var target = FindExisting(existing, mapped.ExternalId);
                        if (target is null)
                        {
                            mapped.Source = source;
                            mapped.CreatedAt = now;
                            mapped.UpdatedAt = now;
                            topic.Questions.Add(mapped);
                            existing.Add(mapped);
                            target = mapped;
                            questionsCreated++;
                        }
                        else
                        {
                            Apply(mapped, target, now);
                            questionsUpdated++;
                        }
                        if (target.ExternalId is not null) imported[target.ExternalId] = target;
                    }
                }
            }
            await _db.SaveChangesAsync(ct);

            if (document.Tests.Count > 0)
            {
                var allTopics = await _db.Topics.Include(t => t.Subject).ToListAsync(ct);
                foreach (var tDto in document.Tests)
                {
                    TryParseMode(tDto.Mode, out var mode);
                    var title = tDto.Title.Trim();
                    var test = new Test
                    {
                        Title = title,
                        Description = ContentService.Blank(tDto.Description),
                        Mode = mode,
                        TimeLimitMinutes = tDto.TimeLimitMinutes,
                        PassPercentage = tDto.PassPercentage,
                        RandomQuestionCount = tDto.RandomQuestionCount,
                        ShuffleQuestions = tDto.ShuffleQuestions,
                        ShuffleOptions = true,
                        Source = source,
                        CreatedAt = now
                    };

                    foreach (var r in tDto.Topics ?? [])
                    {
                        var topic = ResolveTopic(allTopics, r);
                        if (topic is null) warnings.Add($"Toets '{title}': onderwerp '{r}' niet gevonden.");
                        else if (test.Topics.All(t => t.TopicId != topic.Id)) test.Topics.Add(new TestTopic { TopicId = topic.Id });
                    }

                    var sort = 0;
                    foreach (var id in tDto.QuestionIds ?? [])
                    {
                        var questionId = await ResolveQuestionIdAsync(imported, id, ct);
                        if (questionId is null) warnings.Add($"Toets '{title}': vraag met id '{id}' niet gevonden.");
                        else if (test.Questions.All(q => q.QuestionId != questionId))
                            test.Questions.Add(new TestQuestion { QuestionId = questionId.Value, SortOrder = sort++ });
                    }
                    if (test.Topics.Count == 0 && test.Questions.Count == 0)
                        warnings.Add($"Toets '{title}': bevat (nog) geen vragen.");

                    _db.Tests.Add(test);
                    createdTests.Add(test);
                }
                await _db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }

        return new ImportResult(subjectsCreated, topicsCreated, questionsCreated, questionsUpdated, createdTests.Count,
            [.. preview.Warnings, .. warnings], createdTests.Select(t => t.Id).ToList());
    }

    /// <summary>Matches by ExternalId; an exported "q{dbId}" also matches the question with that database id.</summary>
    private static Question? FindExisting(List<Question> existing, string? externalId)
    {
        if (externalId is null) return null;
        var match = existing.FirstOrDefault(q => q.ExternalId == externalId);
        if (match is not null) return match;
        var m = DbIdPattern().Match(externalId);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var dbId))
            return existing.FirstOrDefault(q => q.Id == dbId && q.Id > 0 && q.ExternalId is null);
        return null;
    }

    private static void Apply(Question src, Question dst, DateTime now)
    {
        dst.Type = src.Type;
        dst.Prompt = src.Prompt;
        dst.Context = src.Context;
        dst.Tip = src.Tip;
        dst.Explanation = src.Explanation;
        dst.Difficulty = src.Difficulty;
        dst.ExternalId = src.ExternalId;
        dst.AcceptedAnswers = [.. src.AcceptedAnswers];
        dst.UpdatedAt = now;

        // Keep option ids stable where possible (old attempts refer to them): match by text, then by position.
        var free = dst.Options.OrderBy(o => o.SortOrder).ToList();
        var incoming = new List<AnswerOption>();
        foreach (var o in src.Options)
        {
            var same = free.FirstOrDefault(f => f.Text == o.Text);
            if (same is not null) free.Remove(same);
            incoming.Add(new AnswerOption { Id = same?.Id ?? 0, Text = o.Text, IsCorrect = o.IsCorrect, Feedback = o.Feedback });
        }
        foreach (var o in incoming.Where(o => o.Id == 0))
        {
            if (free.Count == 0) break;
            o.Id = free[0].Id;
            free.RemoveAt(0);
        }
        ReplaceOptionsInMemory(dst, incoming);
    }

    private static void ReplaceOptionsInMemory(Question entity, List<AnswerOption> incoming)
    {
        var byId = entity.Options.Where(o => o.Id > 0).ToDictionary(o => o.Id);
        var result = new List<AnswerOption>();
        for (var i = 0; i < incoming.Count; i++)
        {
            var src = incoming[i];
            if (src.Id > 0 && byId.Remove(src.Id, out var opt))
            {
                opt.Text = src.Text;
                opt.IsCorrect = src.IsCorrect;
                opt.Feedback = src.Feedback;
                opt.SortOrder = i;
                result.Add(opt);
            }
            else
            {
                result.Add(new AnswerOption { Text = src.Text, IsCorrect = src.IsCorrect, Feedback = src.Feedback, SortOrder = i });
            }
        }
        // Options left in byId are removed from the collection; EF deletes them as orphans (required FK).
        entity.Options.RemoveAll(o => o.Id > 0 && byId.ContainsKey(o.Id) || o.Id == 0);
        foreach (var o in result.Where(o => !entity.Options.Contains(o))) entity.Options.Add(o);
        entity.Options.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
    }

    private static Topic? ResolveTopic(List<Topic> topics, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var r = reference.Trim();
        for (var i = r.IndexOf('/'); i >= 0; i = r.IndexOf('/', i + 1))
        {
            var subjectName = r[..i].Trim();
            var topicName = r[(i + 1)..].Trim();
            var match = topics.FirstOrDefault(t =>
                string.Equals(t.Subject?.Name.Trim(), subjectName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.Name.Trim(), topicName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        var byName = topics.Where(t => string.Equals(t.Name.Trim(), r, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    private async Task<int?> ResolveQuestionIdAsync(Dictionary<string, Question> imported, string? id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        id = id.Trim();
        if (imported.TryGetValue(id, out var q) && q.Id > 0) return q.Id;
        var found = await _db.Questions.Where(x => x.ExternalId == id).OrderByDescending(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (found is not null) return found;
        var m = DbIdPattern().Match(id);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var dbId) && await _db.Questions.AnyAsync(x => x.Id == dbId, ct))
            return dbId;
        return null;
    }

    [GeneratedRegex(@"^q(\d+)$")]
    private static partial Regex DbIdPattern();

    // ---------- Export ----------

    public async Task<ImportDocument> ExportAsync(int? subjectId = null, CancellationToken ct = default)
    {
        var query = _db.Subjects.AsNoTracking()
            .Include(s => s.Topics.OrderBy(t => t.SortOrder).ThenBy(t => t.Name))
                .ThenInclude(t => t.Questions.OrderBy(q => q.Id))
                .ThenInclude(q => q.Options.OrderBy(o => o.SortOrder))
            .AsSplitQuery();
        var subjects = subjectId is int sid
            ? await query.Where(s => s.Id == sid).ToListAsync(ct)
            : await query.OrderBy(s => s.Name).ToListAsync(ct);

        var document = new ImportDocument
        {
            Subjects = subjects.Select(s => new SubjectDto
            {
                Name = s.Name,
                Description = s.Description,
                Color = s.Color,
                Topics = s.Topics.Select(t => new TopicDto
                {
                    Name = t.Name,
                    Theory = t.TheoryMarkdown,
                    Questions = t.Questions.Select(ToDto).ToList()
                }).ToList()
            }).ToList()
        };

        if (subjectId is null)
        {
            var tests = await _db.Tests.AsNoTracking()
                .Include(t => t.Questions.OrderBy(q => q.SortOrder)).ThenInclude(tq => tq.Question)
                .Include(t => t.Topics).ThenInclude(tt => tt.Topic).ThenInclude(t => t!.Subject)
                .AsSplitQuery()
                .OrderBy(t => t.Title)
                .ToListAsync(ct);
            document.Tests = tests.Select(t => new TestDto
            {
                Title = t.Title,
                Description = t.Description,
                Mode = t.Mode == TestMode.Practice ? "practice" : "exam",
                TimeLimitMinutes = t.TimeLimitMinutes,
                PassPercentage = t.PassPercentage,
                RandomQuestionCount = t.RandomQuestionCount,
                ShuffleQuestions = t.ShuffleQuestions,
                Topics = t.Topics.Where(tt => tt.Topic is not null).Select(tt => $"{tt.Topic!.Subject?.Name}/{tt.Topic.Name}").ToList(),
                QuestionIds = t.Questions.Where(tq => tq.Question is not null).Select(tq => ExportId(tq.Question!)).ToList()
            }).ToList();
        }
        return document;
    }

    private static string ExportId(Question q) => string.IsNullOrWhiteSpace(q.ExternalId) ? $"q{q.Id}" : q.ExternalId;

    private static QuestionDto ToDto(Question q)
    {
        var dto = new QuestionDto
        {
            Id = ExportId(q),
            Type = QuestionTypes.ToName(q.Type),
            Prompt = q.Prompt,
            Context = q.Context,
            Tip = q.Tip,
            Explanation = q.Explanation,
            Difficulty = q.Difficulty
        };
        if (q.Type == QuestionType.FillIn)
            dto.Answers = [.. q.AcceptedAnswers];
        else
            dto.Options = q.Options.OrderBy(o => o.SortOrder)
                .Select(o => new OptionDto { Text = o.Text, Correct = o.IsCorrect, Feedback = o.Feedback }).ToList();
        return dto;
    }
}
