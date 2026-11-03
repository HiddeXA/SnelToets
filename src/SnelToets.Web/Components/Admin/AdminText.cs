using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SnelToets.Core.Models;

namespace SnelToets.Web.Components.Admin;

/// <summary>Dutch labels and small helpers shared by the management pages.</summary>
public static class AdminText
{
    public static readonly QuestionType[] QuestionTypes =
        [QuestionType.MultipleChoice, QuestionType.MultipleSelect, QuestionType.TrueFalse, QuestionType.FillIn];

    public static string TypeName(QuestionType type) => type switch
    {
        QuestionType.MultipleChoice => "Meerkeuze",
        QuestionType.MultipleSelect => "Meerdere antwoorden",
        QuestionType.TrueFalse => "Goed/Fout",
        QuestionType.FillIn => "Invulvraag",
        _ => type.ToString()
    };

    public static string TypeShort(QuestionType type) => type switch
    {
        QuestionType.MultipleChoice => "MK",
        QuestionType.MultipleSelect => "MA",
        QuestionType.TrueFalse => "G/F",
        QuestionType.FillIn => "Invul",
        _ => "?"
    };

    public static string SourceName(ContentSource source) => source switch
    {
        ContentSource.Manual => "Handmatig",
        ContentSource.Import => "Import",
        ContentSource.Ai => "AI",
        ContentSource.Seed => "Demo",
        _ => source.ToString()
    };

    public static string SourceClass(ContentSource source) => source switch
    {
        ContentSource.Ai => "primary",
        ContentSource.Import => "warn",
        ContentSource.Seed => "good",
        _ => ""
    };

    public static string DifficultyName(int difficulty) => difficulty switch
    {
        <= 1 => "Makkelijk",
        2 => "Gemiddeld",
        _ => "Moeilijk"
    };

    public static string ModeName(TestMode mode) => mode == TestMode.Practice ? "Oefentoets" : "Toets";

    /// <summary>Plain-text snippet of (markdown) text for lists.</summary>
    public static string Snippet(string? text, int max = 110)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = string.Join(' ', text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        s = s.Replace("**", "").Replace("__", "").Replace("`", "");
        return s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>camelCase, indented JSON as used by the import/export format.</summary>
    public static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>File-name friendly version of a name ("Nederlands 3F" → "nederlands-3f").</summary>
    public static string Slug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "export";
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "export" : slug;
    }

    public static string ErrorMessage(Exception ex) => ex switch
    {
        SnelToets.Core.Services.ValidationException v => string.Join(" ", v.Errors),
        _ => "Er ging iets mis: " + ex.Message
    };
}
