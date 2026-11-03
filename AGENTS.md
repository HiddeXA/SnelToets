# SnelToets – afspraken voor ontwikkelaars en AI-agents

SnelToets is een ASP.NET Core (.NET 10) Blazor-app (Interactive Server) waarmee studenten oefenen voor toetsen,
naar het model van Hogeschooltaal: theorie → oefenreeks van 15 vragen → direct een tip + link naar de theorie bij een fout →
een scorebalk per onderwerp die stijgt bij goede en daalt bij foute antwoorden (beheerst vanaf 80%).

## Opbouw

| Map | Inhoud |
| --- | --- |
| `src/SnelToets.Core/Models` | Domeinmodel (Subject → Topic → Question → AnswerOption, Test, Attempt, TopicMastery, QuestionStat) |
| `src/SnelToets.Core/Data` | `SnelToetsDbContext` (SQLite, `EnsureCreated`) |
| `src/SnelToets.Core/Services` | Contracten in `Contracts.cs`, implementaties per service, `MasteryRules` |
| `src/SnelToets.Core/Import` | Uitwisselformaat (`ImportDocument` e.a.), gebruikt door upload, REST en MCP |
| `src/SnelToets.Core/Text` | `Markdown` → veilige HTML (raw HTML uit) |
| `src/SnelToets.Web/Components/Pages/Learn` | Studentkant |
| `src/SnelToets.Web/Components/Pages/Admin` | Beheer: vakken, onderwerpen, vragen, import/export, toetsbouwer |
| `src/SnelToets.Web/Components/Shared` | Gedeelde componenten (`MarkdownView`, `ScoreBar`, …) |
| `src/SnelToets.Web/Api` | REST API onder `/api` |
| `src/SnelToets.Web/Mcp` | MCP-server op `/mcp` (Streamable HTTP) |
| `tests/SnelToets.Tests` | xUnit-tests (SQLite in-memory) |

## Routes (vast – link hier naar)

Student: `/` dashboard · `/vak/{subjectId}` · `/onderwerp/{topicId}` (theorie + start) · `/oefenen/{attemptId}` (oefenspeler, ook starttoets) ·
`/toetsen` · `/toets/{testId}` (intro) · `/toets/afname/{attemptId}` (toetsspeler) · `/resultaat/{attemptId}`.

Beheer: `/beheer` · `/beheer/vak/{subjectId}` · `/beheer/onderwerp/{topicId}` · `/beheer/vraag/nieuw?topic={topicId}` · `/beheer/vraag/{questionId}` ·
`/beheer/import` · `/beheer/toetsen` · `/beheer/toets/nieuw` · `/beheer/toets/{testId}` · `/beheer/ai` (MCP-koppeling).

## Regels

- UI-teksten in het Nederlands; code, identifiers en commentaar in het Engels.
- Mobiel eerst: alles moet werken op 375 px breed, knoppen minimaal 44 px hoog.
- Gebruik de klassen uit `wwwroot/app.css` (`card`, `btn`, `btn-primary`, `scorebar`, `option`, `feedback`, `field`, `badge`, `modal`, `prose`, …).
  Pagina-specifieke stijl in een `.razor.css` naast de component; wijzig `app.css` alleen als iets echt gedeeld is.
- Gebruik altijd de service-interfaces (`IContentService`, `ITestService`, `IPracticeService`, `IImportService`), nooit de DbContext in componenten.
- De huidige leerling: `@inject LearnerSession Learner` → `Learner.Id`.
- Beheerpagina's: `@inject AdminSession Admin`; toon `<AdminGate>` als `Admin.HasAccess` false is (pincode uit `Beheer:Pincode`).
- Markdown tonen: `<MarkdownView Text="..." />` (of `Inline="true"`).
- Bouwen: `dotnet build`; testen: `dotnet test`.
