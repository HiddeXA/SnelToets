using System.Globalization;
using SnelToets.Core.Models;
using SnelToets.Core.Services;

namespace SnelToets.Web.Components.Shared;

/// <summary>Small helpers shared by the student pages.</summary>
public static class LearnUi
{
    public static List<int> ParseIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        var result = new List<int>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) result.Add(id);
        return result;
    }

    /// <summary>Options in the order stored on the attempt item (shuffled per attempt), with any unknown ones appended.</summary>
    public static List<AnswerOption> OrderedOptions(AttemptItem item)
    {
        var q = item.Question;
        if (q is null) return [];
        var byId = q.Options.ToDictionary(o => o.Id);
        var order = ParseIds(item.OptionOrder).Where(byId.ContainsKey).Distinct().ToList();
        var list = order.Select(id => byId[id]).ToList();
        list.AddRange(q.Options.Where(o => !order.Contains(o.Id)).OrderBy(o => o.SortOrder).ThenBy(o => o.Id));
        return list;
    }

    public static string Letter(int index) => index is >= 0 and < 26 ? ((char)('A' + index)).ToString() : (index + 1).ToString(CultureInfo.InvariantCulture);

    public static string Pct(double value) => Math.Round(Math.Clamp(value, 0, 100)).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>A number formatted for CSS (always a dot as decimal separator).</summary>
    public static string Css(double value) => Math.Clamp(value, 0, 100).ToString("0.##", CultureInfo.InvariantCulture);

    public static string Level(double value) =>
        value >= MasteryRules.MasteredThreshold ? "high" : value >= MasteryRules.AttentionThreshold ? "mid" : "low";

    public static bool IsExam(Attempt a) => a.Kind == AttemptKind.Test && a.Mode == TestMode.Exam;

    /// <summary>Where to continue an unfinished attempt.</summary>
    public static string PlayerHref(Attempt a) => IsExam(a) ? $"toets/afname/{a.Id}" : $"oefenen/{a.Id}";

    public static string KindLabel(Attempt a) => a.Kind switch
    {
        AttemptKind.TopicPractice => "Oefenreeks",
        AttemptKind.MixPractice => "Mixoefening",
        AttemptKind.Placement => "Starttoets",
        AttemptKind.Test => a.Mode == TestMode.Exam ? "Toets" : "Oefentoets",
        _ => "Oefening"
    };

    public static string ModeLabel(TestMode mode) =>
        mode == TestMode.Exam ? "Toets: feedback na afloop" : "Oefentoets met directe feedback";

    public static string QuestionWord(int n) => n == 1 ? "1 vraag" : $"{n} vragen";

    public static string Minutes(int minutes) => minutes == 1 ? "1 minuut" : $"{minutes} minuten";

    public static string DateLabel(DateTime utc)
    {
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        var today = DateTime.Now.Date;
        if (local.Date == today) return "vandaag " + local.ToString("HH:mm", nl);
        if (local.Date == today.AddDays(-1)) return "gisteren " + local.ToString("HH:mm", nl);
        return local.ToString("d MMM yyyy", nl);
    }

    /// <summary>
    /// Message to show for a failed action: the core services throw InvalidOperationException, KeyNotFoundException
    /// and ValidationException with Dutch, user-presentable messages; anything else gets the fallback.
    /// </summary>
    public static string Message(Exception ex, string fallback) => ex switch
    {
        ValidationException v => v.Errors.Count > 0 ? string.Join(" ", v.Errors) : fallback,
        InvalidOperationException or KeyNotFoundException when !string.IsNullOrWhiteSpace(ex.Message) => ex.Message,
        _ => fallback
    };

    public static string Greeting()
    {
        var h = DateTime.Now.Hour;
        return h < 6 ? "Goedenacht" : h < 12 ? "Goedemorgen" : h < 18 ? "Goedemiddag" : "Goedenavond";
    }
}

/// <summary>What the UI needs to show the outcome of one answered item (from <see cref="AnswerResult"/> or rebuilt from stored data).</summary>
public sealed record ItemFeedback(
    bool IsCorrect,
    string? Tip,
    string? Explanation,
    string? OptionFeedback,
    IReadOnlyList<int> CorrectOptionIds,
    IReadOnlyList<string> AcceptedAnswers,
    double? PreviousMastery,
    double? NewMastery)
{
    public static ItemFeedback From(AnswerResult r) =>
        new(r.IsCorrect, r.Tip, r.Explanation, r.OptionFeedback, r.CorrectOptionIds, r.AcceptedAnswers, r.PreviousMastery, r.NewMastery);

    /// <summary>Rebuilds feedback for an item that was answered earlier (resume, results review).</summary>
    public static ItemFeedback FromItem(AttemptItem item)
    {
        var q = item.Question;
        var correct = item.IsCorrect == true;
        if (q is null) return new(correct, null, null, null, [], [], null, null);
        var selected = LearnUi.ParseIds(item.SelectedOptionIds);
        var optionFeedback = string.Join("\n\n", q.Options
            .Where(o => selected.Contains(o.Id) && !string.IsNullOrWhiteSpace(o.Feedback))
            .Select(o => o.Feedback!.Trim()));
        return new(
            correct,
            correct ? null : q.Tip,
            q.Explanation,
            string.IsNullOrWhiteSpace(optionFeedback) ? null : optionFeedback,
            q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList(),
            q.AcceptedAnswers,
            null,
            null);
    }
}
