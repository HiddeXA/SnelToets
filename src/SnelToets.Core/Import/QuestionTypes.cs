using SnelToets.Core.Models;

namespace SnelToets.Core.Import;

/// <summary>Maps between <see cref="QuestionType"/> and the names used in the exchange format.</summary>
public static class QuestionTypes
{
    public static string ToName(QuestionType type) => type switch
    {
        QuestionType.MultipleSelect => QuestionTypeNames.MultipleSelect,
        QuestionType.TrueFalse => QuestionTypeNames.TrueFalse,
        QuestionType.FillIn => QuestionTypeNames.FillIn,
        _ => QuestionTypeNames.MultipleChoice
    };

    /// <summary>
    /// Lenient parsing: case-insensitive, ignores '-', '_' and spaces, and accepts a few Dutch/short aliases
    /// (meerkeuze, mc, meerdere, ms, goed-fout, waar-onwaar, tf, invul, open). Empty = multiple-choice.
    /// </summary>
    public static bool TryParse(string? name, out QuestionType type)
    {
        type = QuestionType.MultipleChoice;
        if (string.IsNullOrWhiteSpace(name)) return true;
        var key = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        switch (key)
        {
            case "multiplechoice" or "meerkeuze" or "meerkeuzevraag" or "mc" or "choice" or "keuze":
                type = QuestionType.MultipleChoice; return true;
            case "multipleselect" or "meerdere" or "meerdereantwoorden" or "meerkeuzemeerdere" or "ms" or "select" or "checkbox":
                type = QuestionType.MultipleSelect; return true;
            case "truefalse" or "goedfout" or "waaronwaar" or "waarnietwaar" or "juistonjuist" or "tf" or "boolean":
                type = QuestionType.TrueFalse; return true;
            case "fillin" or "invul" or "invullen" or "invulvraag" or "open" or "openvraag" or "text" or "tekst":
                type = QuestionType.FillIn; return true;
            default:
                return false;
        }
    }
}
