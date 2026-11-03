using System.Text;
using Microsoft.EntityFrameworkCore;
using SnelToets.Core.Import;
using SnelToets.Core.Models;
using SnelToets.Core.Services;

namespace SnelToets.Tests;

public class ImportServiceTests
{
    private static Stream Text(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    private const string Json = """
    {
      // comments are allowed
      "subjects": [{
        "name": "Nederlands 3F",
        "topics": [{
          "name": "Werkwoorden",
          "theory": "# d of t",
          "questions": [
            { "id": "ww-1", "type": "multiple-choice", "prompt": "Hij ... (worden)", "tip": "Stam + t",
              "options": [ { "text": "wordt", "correct": true }, { "text": "word", "feedback": "Vergeet de t niet." } ] },
            { "id": "ww-2", "type": "true-false", "prompt": "'Gebeurd' is een voltooid deelwoord.", "answer": true },
            { "id": "ww-3", "type": "fill-in", "prompt": "Vul in: ik ... (vinden)", "answers": ["vind"], "difficulty": 2 },
            { "id": "ww-4", "type": "multiple-select", "prompt": "Welke zijn goed?",
              "options": [ { "text": "a", "correct": true }, { "text": "b", "correct": true }, { "text": "c" } ] },
          ]
        }]
      }],
      "tests": [{ "title": "Werkwoordtoets", "mode": "exam", "topics": ["Nederlands 3F/Werkwoorden"], "questionIds": ["ww-1", "bestaat-niet"] }]
    }
    """;

    [Fact]
    public async Task Json_parse_and_import()
    {
        using var t = new TestDb();
        var svc = new ImportService(t.Db);
        var preview = await svc.ParseAsync(Text(Json), "inhoud.json");
        Assert.True(preview.IsValid, string.Join("\n", preview.Errors));
        Assert.Equal((1, 1, 4, 1), (preview.SubjectCount, preview.TopicCount, preview.QuestionCount, preview.TestCount));

        var result = await svc.ImportAsync(preview.Document!);
        Assert.Equal((1, 1, 4, 0, 1), (result.SubjectsCreated, result.TopicsCreated, result.QuestionsCreated, result.QuestionsUpdated, result.TestsCreated));
        Assert.Contains(result.Warnings, w => w.Contains("bestaat-niet"));
        var test = await new TestService(t.Db).GetTestAsync(Assert.Single(result.CreatedTestIds));
        Assert.Single(test!.Questions);
        Assert.Single(test.Topics);

        var tf = await t.Db.Questions.Include(q => q.Options).SingleAsync(q => q.ExternalId == "ww-2");
        Assert.Equal(["Goed", "Fout"], tf.Options.OrderBy(o => o.SortOrder).Select(o => o.Text));
        Assert.True(tf.Options.Single(o => o.Text == "Goed").IsCorrect);
        Assert.Equal(ContentSource.Import, tf.Source);
    }

    [Fact]
    public async Task Reimport_updates_by_id_instead_of_duplicating()
    {
        using var t = new TestDb();
        var svc = new ImportService(t.Db);
        var doc = ImportJson.Deserialize(Json);
        doc.Tests.Clear();
        await svc.ImportAsync(doc);
        var optionIds = await t.Db.AnswerOptions.Select(o => o.Id).OrderBy(i => i).ToListAsync();

        var again = ImportJson.Deserialize(Json);
        again.Tests.Clear();
        again.Subjects[0].Name = "nederlands 3f"; // case-insensitive match
        again.Subjects[0].Topics[0].Questions[0].Prompt = "Zij ... (worden)";
        var result = await svc.ImportAsync(again);

        Assert.Equal((0, 0, 0, 4), (result.SubjectsCreated, result.TopicsCreated, result.QuestionsCreated, result.QuestionsUpdated));
        Assert.Equal(4, await t.Db.Questions.CountAsync());
        Assert.Equal("Zij ... (worden)", (await t.Db.Questions.SingleAsync(q => q.ExternalId == "ww-1")).Prompt);
        Assert.Equal(optionIds, await t.Db.AnswerOptions.Select(o => o.Id).OrderBy(i => i).ToListAsync());
    }

    [Fact]
    public async Task Export_then_import_round_trips()
    {
        using var source = new TestDb();
        await new ImportService(source.Db).ImportAsync(ImportJson.Deserialize(Json));
        // A question without external id gets "q{id}" on export.
        var (_, topic, _) = source.Seed(1, subject: "Nederlands 3F", topic: "Spelling");
        var exported = await new ImportService(source.Db).ExportAsync();
        var json = ImportJson.Serialize(exported);

        using var target = new TestDb();
        var svc = new ImportService(target.Db);
        var preview = await svc.ParseAsync(Text(json), "export.json");
        Assert.True(preview.IsValid, string.Join("\n", preview.Errors));
        var result = await svc.ImportAsync(preview.Document!);
        Assert.Equal(5, result.QuestionsCreated);
        Assert.Equal(1, result.TestsCreated);

        var reExported = await svc.ExportAsync();
        Assert.Equal(
            exported.Subjects.SelectMany(s => s.Topics).SelectMany(t => t.Questions).Select(q => (q.Id, q.Prompt, q.Type, q.Options?.Count, q.Answers?.Count)),
            reExported.Subjects.SelectMany(s => s.Topics).SelectMany(t => t.Questions).Select(q => (q.Id, q.Prompt, q.Type, q.Options?.Count, q.Answers?.Count)));
        Assert.Equal(exported.Tests[0].QuestionIds, reExported.Tests[0].QuestionIds);

        // Importing the export back into the source updates instead of duplicating (also for "q{id}").
        var backDoc = ImportJson.Deserialize(json);
        backDoc.Tests.Clear();
        var back = await new ImportService(source.Db).ImportAsync(backDoc);
        Assert.Equal(0, back.QuestionsCreated);
        Assert.Equal(5, back.QuestionsUpdated);
    }

    [Fact]
    public async Task Bare_array_of_subjects_is_accepted()
    {
        using var t = new TestDb();
        var preview = await new ImportService(t.Db).ParseAsync(Text("""
            [{ "name": "Rekenen", "topics": [{ "name": "Breuken", "questions": [
              { "type": "fill-in", "prompt": "1/2 + 1/4 = ?", "answers": ["3/4"] } ] }] }]
            """), "x.json");
        Assert.True(preview.IsValid, string.Join("\n", preview.Errors));
        Assert.Equal(1, preview.QuestionCount);
    }

    [Fact]
    public async Task Invalid_json_and_validation_errors_are_reported_with_location()
    {
        using var t = new TestDb();
        var svc = new ImportService(t.Db);
        var broken = await svc.ParseAsync(Text("{ \"subjects\": [ "), "x.json");
        Assert.False(broken.IsValid);
        Assert.Contains("regel", Assert.Single(broken.Errors));

        var bad = await svc.ParseAsync(Text("""
            { "subjects": [{ "name": "Vak", "topics": [{ "name": "Ond", "questions": [
              { "prompt": "ok", "options": [ { "text": "a", "correct": true }, { "text": "b" } ] },
              { "type": "multiple-choice", "prompt": "geen juist", "options": [ { "text": "a" }, { "text": "b" } ] },
              { "type": "raar", "prompt": "x" }
            ] }] }] }
            """), "x.json");
        Assert.False(bad.IsValid);
        Assert.Equal(2, bad.Errors.Count);
        Assert.StartsWith("Vak 'Vak' › onderwerp 'Ond' › vraag 2:", bad.Errors[0]);
        Assert.StartsWith("Vak 'Vak' › onderwerp 'Ond' › vraag 3:", bad.Errors[1]);
        await Assert.ThrowsAsync<ValidationException>(() => svc.ImportAsync(bad.Document!));
        Assert.Equal(0, await t.Db.Subjects.CountAsync());
    }

    [Fact]
    public async Task Unknown_extension_does_not_throw()
    {
        using var t = new TestDb();
        var preview = await new ImportService(t.Db).ParseAsync(Text("abc"), "x.docx");
        Assert.False(preview.IsValid);
        Assert.Single(preview.Errors);
    }

    [Theory]
    [InlineData(',')]
    [InlineData(';')]
    public async Task Csv_with_comma_or_semicolon(char d)
    {
        var csv = string.Join("\n",
            string.Join(d, "subject", "topic", "type", "prompt", "context", "options", "correct", "tip", "explanation", "difficulty", "id", "theory"),
            string.Join(d, "Nederlands", "Werkwoorden", "multiple-choice", "Hij ... (worden)", "", "wordt|word|wort", "1", "Stam + t", "", "1", "c1", "# Theorie"),
            string.Join(d, "Nederlands", "Werkwoorden", "multiple-select", "Welke zijn goed?", "", "a|b|c", "1|3", "", "", "2", "c2", "Andere theorie"),
            string.Join(d, "Nederlands", "Werkwoorden", "true-false", "Dit klopt.", "", "", "fout", "", "", "", "c3", ""),
            "",
            string.Join(d, "Nederlands", "Spelling", "fill-in", "Vul in", "\"Ik ... het, zei hij\"", "", "vind|vindt", "", "", "3", "c4", ""),
            string.Join(d, "Nederlands", "Spelling", "", "Afgeleid type", "", "x|y", "2", "", "", "", "", ""));
        using var t = new TestDb();
        var svc = new ImportService(t.Db);
        var preview = await svc.ParseAsync(Text(csv), "vragen.csv");
        Assert.True(preview.IsValid, string.Join("\n", preview.Errors));
        Assert.Equal((1, 2, 5), (preview.SubjectCount, preview.TopicCount, preview.QuestionCount));

        var topic = preview.Document!.Subjects[0].Topics[0];
        Assert.Equal("# Theorie", topic.Theory);
        Assert.True(topic.Questions[0].Options![0].Correct);
        Assert.Equal([true, false, true], topic.Questions[1].Options!.Select(o => o.Correct));
        Assert.False(topic.Questions[2].Answer);
        var spelling = preview.Document.Subjects[0].Topics[1];
        Assert.Equal(["vind", "vindt"], spelling.Questions[0].Answers);
        Assert.Equal("Ik ... het, zei hij", spelling.Questions[0].Context);
        Assert.Equal(3, spelling.Questions[0].Difficulty);
        Assert.Equal(QuestionTypeNames.MultipleChoice, spelling.Questions[1].Type);

        var result = await svc.ImportAsync(preview.Document);
        Assert.Equal(5, result.QuestionsCreated);
    }

    [Fact]
    public async Task Csv_errors_have_row_numbers()
    {
        var csv = "subject,topic,type,prompt,options,correct\n" +
                  "Vak,Ond,multiple-choice,Vraag 1,a|b,5\n" +
                  ",Ond,multiple-choice,Vraag 2,a|b,1\n" +
                  "Vak,Ond,multiple-choice,Vraag 3,a|b,1|2\n";
        using var t = new TestDb();
        var preview = await new ImportService(t.Db).ParseAsync(Text(csv), "x.csv");
        Assert.False(preview.IsValid);
        Assert.Contains(preview.Errors, e => e.StartsWith("Rij 2:"));
        Assert.Contains(preview.Errors, e => e.StartsWith("Rij 3:"));
        Assert.Contains(preview.Errors, e => e.Contains("vraag 1") && e.Contains("precies één"));
    }

    [Fact]
    public async Task Csv_missing_required_column_is_an_error()
    {
        using var t = new TestDb();
        var preview = await new ImportService(t.Db).ParseAsync(Text("subject,prompt\nA,B\n"), "x.csv");
        Assert.False(preview.IsValid);
        Assert.Contains(preview.Errors, e => e.Contains("'topic'"));
    }
}
