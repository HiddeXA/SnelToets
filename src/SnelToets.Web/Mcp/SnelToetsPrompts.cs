using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SnelToets.Web.Mcp;

/// <summary>Reusable MCP prompts that teach the AI how to write SnelToets content in the Hogeschooltaal style.</summary>
[McpServerPromptType]
public sealed class SnelToetsPrompts
{
    [McpServerPrompt(Name = "make_practice_set", Title = "Maak een oefenreeks")]
    [Description("Instructions for writing a practice set (theory + questions with tips and distractor feedback) for one topic and saving it in SnelToets.")]
    public static string MakePracticeSet(
        [Description("Subject, e.g. 'Nederlands – taalverzorging'.")] string subject,
        [Description("Topic, e.g. 'Spelling: tussen-s'.")] string topic,
        [Description("Level of the students, e.g. 'hbo jaar 1', '3F', 'havo 4'.")] string level = "hbo jaar 1",
        [Description("Number of questions (default 15).")] int count = 15)
    {
        count = Math.Clamp(count, 1, 60);
        return $"""
            Make a SnelToets practice set for the subject "{subject}", topic "{topic}", for students at level "{level}".

            1. Call list_subjects. Reuse the subject and topic if they exist (compare names case-insensitively); otherwise create them with
               create_subject and create_topic. If the topic exists, call get_topic and avoid repeating existing questions.
            2. Theory (create_topic / update_topic_theory): concise Markdown in the students' language (usually Dutch): the rule in plain words,
               a step-by-step approach ("stappenplan"), 3-6 example sentences with the key part in **bold**, and the common pitfalls.
               Write it in your own words; do not copy textbook or publisher texts.
            3. Write {count} ORIGINAL questions and save them with create_questions (one call, or batches of 10-20):
               - Mix: about 70% multiple-choice (3-4 options), 20% true-false ("Is deze zin goed?" with answer true/false), 10% fill-in.
               - Spread difficulty: roughly a third each of 1 (easy), 2 (medium) and 3 (hard).
               - Put the sentence in "context" (with … for a gap) and the instruction in "prompt", e.g. prompt "Welke vorm is juist?",
                 context "Morgen … hij de auto. (wassen)".
               - Distractors must be plausible: the typical mistakes students actually make, not nonsense.
               - Every wrong option gets a one-sentence "feedback" that explains why it is tempting but wrong.
               - EVERY question gets a "tip" that applies the rule to THIS sentence, step by step, in 1-2 sentences – like Hogeschooltaal.
                 Good: "'Je' kun je hier niet vervangen door 'jij' → geen onderwerp → stam + t: 'Vind je'."
                 Bad: "Let op de regels van de werkwoordspelling." (too generic)
               - Optionally an "explanation" with background, shown also after a correct answer.
               - Give each question a stable "id" (a short slug of the topic plus a number, e.g. "tussen-s-001").
               - Fill-in: ask for one word or a short phrase and list every acceptable variant in "answers" (answers are compared after normalising case and spaces).
            4. Check spelling and grammar of everything twice: this is a language-learning app, errors are unacceptable.
            5. Read the create_questions result. If items failed, fix exactly the reported errors and resend only those items.
            6. Finish with a short summary for the user: how many questions were saved, and the topic link.
               Offer to make a test with create_test (e.g. exam mode, randomQuestionCount 15, 20 minutes, pass percentage 80).
            """;
    }
}
