using System.Globalization;
using CsvHelper;

namespace SnelToets.Core.Import;

/// <summary>
/// Reads questions from a CSV file (comma, semicolon or tab separated; detected from the header line).
/// Columns (header names case-insensitive, Dutch aliases in brackets):
/// <c>subject</c> (vak), <c>topic</c> (onderwerp), <c>type</c> (soort), <c>prompt</c> (vraag), <c>context</c>,
/// <c>options</c> (opties; separated by <c>|</c>), <c>correct</c> (juist/antwoord), <c>tip</c>,
/// <c>explanation</c> (uitleg), <c>difficulty</c> (moeilijkheid), <c>id</c>, <c>theory</c> (theorie, optional).
/// <c>correct</c> = 1-based option numbers separated by <c>|</c> for choice questions, accepted answers separated
/// by <c>|</c> for fill-in, or goed/fout/true/false/waar/onwaar for true-false.
/// When <c>type</c> is empty it is inferred: no options → true-false (when correct is goed/fout) or fill-in;
/// several correct numbers → multiple-select; otherwise multiple-choice.
/// </summary>
public static class CsvImportParser
{
    private static readonly Dictionary<string, string> HeaderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["subject"] = "subject", ["vak"] = "subject",
        ["topic"] = "topic", ["onderwerp"] = "topic",
        ["type"] = "type", ["soort"] = "type", ["vraagtype"] = "type",
        ["prompt"] = "prompt", ["vraag"] = "prompt", ["question"] = "prompt",
        ["context"] = "context", ["zin"] = "context",
        ["options"] = "options", ["opties"] = "options", ["antwoordopties"] = "options",
        ["correct"] = "correct", ["juist"] = "correct", ["goed"] = "correct", ["antwoord"] = "correct", ["answer"] = "correct", ["answers"] = "correct",
        ["tip"] = "tip",
        ["explanation"] = "explanation", ["uitleg"] = "explanation",
        ["difficulty"] = "difficulty", ["moeilijkheid"] = "difficulty", ["niveau"] = "difficulty",
        ["id"] = "id",
        ["theory"] = "theory", ["theorie"] = "theory"
    };

    private static readonly string[] TrueWords = ["goed", "true", "waar", "juist"];
    private static readonly string[] FalseWords = ["fout", "false", "onwaar", "niet waar", "onjuist"];

    public sealed record Result(ImportDocument Document, List<string> Errors, List<string> Warnings);

    public static Result Parse(string text)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var document = new ImportDocument();
        text = text.TrimStart('﻿');
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add("Het CSV-bestand is leeg.");
            return new Result(document, errors, warnings);
        }

        var delimiter = DetectDelimiter(text);
        var options = new CsvReaderOptions
        {
            Separator = delimiter,
            HasHeader = true,
            SkipEmptyLines = true,
            TrimWhitespace = true,
            TrackLineNumbers = true,
            FieldCountMismatch = CsvFieldCountHandling.Allow,
            MissingField = CsvMissingFieldHandling.ReturnEmpty,
            MalformedData = CsvMalformedDataHandling.Recover
        };

        try
        {
            using var reader = CsvReader.FromText(text, options);
            var headers = reader.Headers;
            if (headers.Count == 0)
            {
                errors.Add("Het CSV-bestand heeft geen kopregel.");
                return new Result(document, errors, warnings);
            }

            var columns = new Dictionary<string, int>();
            for (var i = 0; i < headers.Count; i++)
            {
                var name = (headers[i] ?? "").Trim().TrimStart('\uFEFF').Trim();
                if (HeaderAliases.TryGetValue(name, out var canonical)) columns.TryAdd(canonical, i);
                else if (name.Length > 0) warnings.Add($"Kolom '{name}' wordt genegeerd (onbekende kolom).");
            }
            foreach (var required in new[] { "subject", "topic", "prompt" })
                if (!columns.ContainsKey(required))
                    errors.Add($"Kolom '{required}' ontbreekt in de kopregel. Verwachte kolommen: subject,topic,type,prompt,context,options,correct,tip,explanation,difficulty,id.");
            if (errors.Count > 0) return new Result(document, errors, warnings);

            var topicsWithTheory = new HashSet<TopicDto>();
            while (reader.Read())
            {
                var row = reader.LineNumber;
                var fieldCount = reader.FieldCount;
                string Get(string column) =>
                    columns.TryGetValue(column, out var idx) && idx < fieldCount ? (reader.GetString(idx) ?? "").Trim() : "";

                var any = false;
                for (var i = 0; i < fieldCount && !any; i++) any = !string.IsNullOrWhiteSpace(reader.GetString(i));
                if (!any) continue;

                var subjectName = Get("subject");
                var topicName = Get("topic");
                if (subjectName.Length == 0) { errors.Add($"Rij {row}: het vak (kolom 'subject') is leeg."); continue; }
                if (topicName.Length == 0) { errors.Add($"Rij {row}: het onderwerp (kolom 'topic') is leeg."); continue; }

                var subject = document.Subjects.FirstOrDefault(s => s.Name.Equals(subjectName, StringComparison.OrdinalIgnoreCase));
                if (subject is null) document.Subjects.Add(subject = new SubjectDto { Name = subjectName });
                var topic = subject.Topics.FirstOrDefault(t => t.Name.Equals(topicName, StringComparison.OrdinalIgnoreCase));
                if (topic is null) subject.Topics.Add(topic = new TopicDto { Name = topicName });

                var theory = Get("theory");
                if (theory.Length > 0 && topicsWithTheory.Add(topic)) topic.Theory = theory;

                var prompt = Get("prompt");
                if (prompt.Length == 0)
                {
                    // A row with only subject/topic/theory just defines the topic.
                    if (Get("options").Length > 0 || Get("correct").Length > 0)
                        errors.Add($"Rij {row}: de vraag (kolom 'prompt') is leeg.");
                    continue;
                }

                var question = ParseQuestion((int)row, Get, errors);
                if (question is not null) topic.Questions.Add(question);
            }
        }
        catch (Exception ex) when (ex is CsvFormatException or IOException or InvalidOperationException or ArgumentException)
        {
            errors.Add($"Het CSV-bestand kon niet worden gelezen: {ex.Message}");
        }

        return new Result(document, errors, warnings);
    }

    private static QuestionDto? ParseQuestion(int row, Func<string, string> get, List<string> errors)
    {
        var options = Split(get("options"));
        var correctRaw = get("correct");
        var correct = Split(correctRaw);
        var typeRaw = get("type");
        var errorCount = errors.Count;

        string type;
        if (typeRaw.Length == 0)
        {
            if (options.Count == 0)
                type = correct.Count == 1 && TryBool(correct[0], out _) ? QuestionTypeNames.TrueFalse : QuestionTypeNames.FillIn;
            else
                type = correct.Count > 1 ? QuestionTypeNames.MultipleSelect : QuestionTypeNames.MultipleChoice;
        }
        else if (QuestionTypes.TryParse(typeRaw, out var parsed))
        {
            type = QuestionTypes.ToName(parsed);
        }
        else
        {
            errors.Add($"Rij {row}: onbekend vraagtype '{typeRaw}'. Kies uit: {string.Join(", ", QuestionTypeNames.All)}.");
            return null;
        }

        var dto = new QuestionDto
        {
            Id = NullIfEmpty(get("id")),
            Type = type,
            Prompt = get("prompt"),
            Context = NullIfEmpty(get("context")),
            Tip = NullIfEmpty(get("tip")),
            Explanation = NullIfEmpty(get("explanation"))
        };

        var difficulty = get("difficulty");
        if (difficulty.Length > 0)
        {
            if (int.TryParse(difficulty, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d)) dto.Difficulty = d;
            else errors.Add($"Rij {row}: moeilijkheid '{difficulty}' is geen getal (gebruik 1, 2 of 3).");
        }

        if (type == QuestionTypeNames.FillIn)
        {
            dto.Answers = correct;
        }
        else if (type == QuestionTypeNames.TrueFalse && options.Count == 0)
        {
            if (correct.Count == 1 && TryBool(correct[0], out var answer)) dto.Answer = answer;
            else errors.Add($"Rij {row}: vul bij een goed/fout-vraag in de kolom 'correct' 'goed' of 'fout' in.");
        }
        else
        {
            var numbers = new HashSet<int>();
            foreach (var part in correct.SelectMany(c => c.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            {
                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= options.Count)
                    numbers.Add(n);
                else if (type == QuestionTypeNames.TrueFalse && TryBool(part, out var b) && options.Count == 2)
                    numbers.Add(b ? 1 : 2);
                else
                    errors.Add($"Rij {row}: '{part}' in de kolom 'correct' is geen geldig optienummer (1 t/m {options.Count}).");
            }
            dto.Options = options.Select((o, i) => new OptionDto { Text = o, Correct = numbers.Contains(i + 1) }).ToList();
        }

        return errors.Count == errorCount ? dto : null;
    }

    internal static bool TryBool(string value, out bool result)
    {
        var v = value.Trim().ToLowerInvariant();
        if (TrueWords.Contains(v)) { result = true; return true; }
        if (FalseWords.Contains(v)) { result = false; return true; }
        result = false;
        return false;
    }

    private static List<string> Split(string value) =>
        value.Length == 0 ? [] : value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    /// <summary>Picks the separator that occurs most often (outside quotes) in the header line.</summary>
    internal static char DetectDelimiter(string text)
    {
        int comma = 0, semicolon = 0, tab = 0;
        var inQuotes = false;
        foreach (var ch in text)
        {
            if (ch == '"') inQuotes = !inQuotes;
            else if (!inQuotes && (ch == '\n' || ch == '\r')) break;
            else if (!inQuotes)
            {
                if (ch == ',') comma++;
                else if (ch == ';') semicolon++;
                else if (ch == '\t') tab++;
            }
        }
        if (tab > comma && tab > semicolon) return '\t';
        return semicolon > comma ? ';' : ',';
    }
}
