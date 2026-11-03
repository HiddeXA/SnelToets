namespace SnelToets.Web.Mcp;

/// <summary>Self-service documentation of the exchange format, returned by the MCP tool <c>get_import_format</c>.</summary>
public static class ImportFormat
{
    public const string Documentation = """
        # SnelToets import format (version 1)

        JSON, camelCase. The same format is used by the web upload (/beheer/import), POST /api/import and the MCP tools
        import_content (whole document) and create_questions (an array of question objects for one topic).

        ## Document
        {
          "version": 1,
          "subjects": [ Subject ],
          "tests": [ Test ]                       // optional
        }

        Subject: { "name": string (required, matched case-insensitively with existing subjects), "description"?: string, "color"?: "#3056d3",
                   "topics": [ Topic ] }
        Topic:   { "name": string (required, matched by name within the subject), "theory"?: Markdown string, "questions": [ Question ] }
        Question:
        {
          "id"?: string,             // stable id, e.g. "tussen-s-001". Re-importing the same id in the same topic UPDATES the question.
          "type": "multiple-choice" | "multiple-select" | "true-false" | "fill-in",   // default multiple-choice
          "prompt": string,          // the instruction/question, e.g. "Welke vorm is juist?" (Markdown allowed)
          "context"?: string,        // the sentence, e.g. "Morgen … hij de auto. (wassen)" (Markdown allowed)
          "options"?: [ { "text": string, "correct": bool, "feedback"?: string } ],   // choice questions
          "answer"?: bool,           // true-false shortcut instead of options: true = the statement/sentence is correct ("Goed")
          "answers"?: [ string ],    // fill-in: all accepted answers (compared case-insensitively, spaces normalised)
          "tip": string,             // REQUIRED in practice: shown right after a wrong answer; applies the rule to THIS question
          "explanation"?: string,    // optional background, also shown after a correct answer
          "difficulty": 1 | 2 | 3    // 1 easy, 2 medium, 3 hard (default 1)
        }
        Test:
        {
          "title": string, "description"?: string,
          "mode": "practice" | "exam",            // practice = feedback per question; exam = feedback at the end (default exam)
          "timeLimitMinutes"?: int, "passPercentage": int (default 80),
          "randomQuestionCount"?: int,            // draw this many random questions per attempt from "topics"
          "shuffleQuestions": bool (default true),
          "topics"?: [ "Subject name/Topic name" ],
          "questionIds"?: [ "question id" ]       // fixed questions by their "id"
        }

        ## Validation rules (errors block the import)
        - subject, topic and test names/titles are required; prompt is required.
        - choice questions (multiple-choice, multiple-select, true-false) need at least 2 options, each with text.
        - multiple-choice and true-false: exactly one correct option. multiple-select: at least one correct option.
        - true-false: exactly 2 options, or no options and "answer": true/false.
        - fill-in: at least one accepted answer in "answers".
        - difficulty must be 1, 2 or 3.
        - tests must reference existing (or imported) topics as "Subject/Topic", or question ids from the document.
        A missing tip does not block the import (it may be reported as a warning), but ALWAYS write one.

        ## Writing good questions (Hogeschooltaal style)
        - All content in the students' language (usually Dutch); flawless spelling.
        - Tip = the rule applied to this sentence, 1–2 sentences, e.g.
          "'Je' kun je hier niet vervangen door 'jij' → geen onderwerp → stam + t: 'Vindt je broer'."
        - Distractors are the mistakes students really make; give each wrong option a one-sentence feedback.
        - Mostly multiple-choice (3–4 options), some true-false and fill-in; mix difficulties.

        ## Example
        {
          "version": 1,
          "subjects": [
            {
              "name": "Nederlands 3F",
              "description": "Taalverzorging voor het hbo",
              "topics": [
                {
                  "name": "Werkwoordspelling: tegenwoordige tijd",
                  "theory": "## De regel\nIk → stam (*ik werk*). Jij/je, u, hij, zij, het → stam + t (*hij werkt*). Meervoud → hele werkwoord.\n\nStaat **jij/je** direct achter de persoonsvorm, dan valt de t weg: *Werk jij morgen?*",
                  "questions": [
                    {
                      "id": "ww-tt-001",
                      "type": "multiple-choice",
                      "prompt": "Welke vorm is juist?",
                      "context": "… je broer ook mee? (gaan)",
                      "options": [
                        { "text": "Gaat", "correct": true },
                        { "text": "Ga", "correct": false, "feedback": "'Je' is hier niet het onderwerp: 'je broer' (= hij) is het onderwerp." }
                      ],
                      "tip": "'Je' kun je hier niet vervangen door 'jij' ('jij broer' kan niet) → 'je broer' is het onderwerp (= hij) → stam + t: 'Gaat'.",
                      "difficulty": 2
                    },
                    {
                      "id": "ww-tt-002",
                      "type": "true-false",
                      "prompt": "Is de persoonsvorm in deze zin goed gespeld?",
                      "context": "Ik vindt dit een lastige opdracht.",
                      "answer": false,
                      "tip": "Onderwerp is 'ik' → alleen de stam: 'Ik vind'.",
                      "difficulty": 1
                    },
                    {
                      "id": "ww-tt-003",
                      "type": "fill-in",
                      "prompt": "Vul de juiste vorm in van het werkwoord tussen haakjes.",
                      "context": "Mijn collega … altijd snel op mijn mails. (antwoorden)",
                      "answers": ["antwoordt"],
                      "tip": "Onderwerp is 'mijn collega' (enkelvoud) → stam 'antwoord' + t: 'antwoordt'.",
                      "difficulty": 2
                    }
                  ]
                }
              ]
            }
          ],
          "tests": [
            {
              "title": "Oefentoets werkwoordspelling",
              "mode": "exam",
              "timeLimitMinutes": 20,
              "passPercentage": 80,
              "randomQuestionCount": 15,
              "topics": ["Nederlands 3F/Werkwoordspelling: tegenwoordige tijd"]
            }
          ]
        }
        """;
}
