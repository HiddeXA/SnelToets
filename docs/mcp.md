# MCP-server (AI-koppeling)

SnelToets heeft een ingebouwde [Model Context Protocol](https://modelcontextprotocol.io)-server, zodat een
AI-assistent (Claude Desktop, Claude Code of een andere MCP-client) vakken, onderwerpen met theorie, vragen en
toetsen kan aanmaken en de bestaande inhoud kan lezen.

| | |
| --- | --- |
| Endpoint | `https://<jouw-server>/mcp` (lokaal bijv. `http://localhost:5286/mcp`) |
| Transport | Streamable HTTP, **stateless** (geen sessie-id nodig; `POST` met JSON-RPC) |
| Headers | `Content-Type: application/json`, `Accept: application/json, text/event-stream` |
| Authenticatie | `Authorization: Bearer <sleutel>` of `X-Api-Key: <sleutel>` als `Mcp:ApiKey` is ingesteld |
| Uitlegpagina | `/beheer/ai` (adres, status van de sleutel, kant-en-klare configuraties) |
| Implementatie | `src/SnelToets.Web/Mcp/` (pakket `ModelContextProtocol.AspNetCore` 2.3.1) |

## Beveiliging

- Stel in productie altijd een sleutel in: `Mcp:ApiKey` in `appsettings.json` of de omgevingsvariabele
  `Mcp__ApiKey`. Zonder sleutel is `/mcp` open voor iedereen (handig lokaal); bij het starten staat dan een
  waarschuwing in het log.
- Dezelfde sleutel beschermt de schrijvende REST-endpoints (`POST`/`PUT`/`DELETE` onder `/api`).
- Zonder of met een verkeerde sleutel antwoordt de server met `401` (ProblemDetails, Nederlandse melding).
  De vergelijking gebeurt in constante tijd. De sleutel wordt nergens in de app getoond.

## Verbinden

**Claude Code**

```bash
claude mcp add --transport http sneltoets https://<jouw-server>/mcp --header "Authorization: Bearer <sleutel>"
```

**Claude Desktop** – `claude_desktop_config.json` (Instellingen → Developer → Edit Config), via
[`mcp-remote`](https://www.npmjs.com/package/mcp-remote) (Node.js nodig):

```json
{
  "mcpServers": {
    "sneltoets": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "https://<jouw-server>/mcp", "--header", "Authorization:${SNELTOETS_AUTH}"],
      "env": { "SNELTOETS_AUTH": "Bearer <sleutel>" }
    }
  }
}
```

De header staat in een omgevingsvariabele omdat sommige systemen spaties in `args` verkeerd doorgeven. Gebruik je
`http://` naar een andere computer dan localhost, voeg dan `"--allow-http"` toe aan `args`.

**Andere clients / testen met curl**

```bash
curl -X POST https://<jouw-server>/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "Authorization: Bearer <sleutel>" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curl","version":"1"}}}'

curl -X POST https://<jouw-server>/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "Authorization: Bearer <sleutel>" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
```

Het antwoord komt als server-sent event (`event: message` / `data: {…}`).

## Werkwijze voor de AI

De server stuurt bij `initialize` instructies mee. Kort samengevat:

1. Roep eerst `list_subjects` aan en hergebruik bestaande vakken en onderwerpen.
2. Maak een vak (`create_subject`) en onderwerpen met beknopte theorie in Markdown (`create_topic`), of stuur alles
   in één keer met `import_content`.
3. Voeg vragen toe in porties van 10–20 met `create_questions`; verbeter afgekeurde vragen en stuur alleen die opnieuw.
4. Stel eventueel een toets samen met `create_test` en geef de gebruiker de `studentUrl`.

Inhoudsregels die de tools en instructies benadrukken:

- Alle inhoud in de taal van de studenten (meestal Nederlands), foutloos gespeld.
- **Elke vraag krijgt een tip die de regel toepast op die vraag** (zoals Hogeschooltaal), bijvoorbeeld
  *"'Je' kun je hier niet vervangen door 'jij' → geen onderwerp → stam + t: 'Vindt je broer'."*
- Foute opties krijgen een korte `feedback`: waarom is deze afleider verleidelijk maar fout?
- Vooral meerkeuze (3–4 opties), aangevuld met goed/fout- en invulvragen; moeilijkheid 1–3.
- Een vast `id` per vraag (bijv. `tussen-s-001`), zodat een vraag later bijgewerkt kan worden in plaats van gedupliceerd.

Alles wat via MCP wordt aangemaakt, krijgt de bron **AI** (`ContentSource.Ai`), zodat docenten het in het beheer
kunnen herkennen en controleren.

## Tools

Resultaten zijn compacte JSON (camelCase). Bij een probleem geeft de tool een foutresultaat (`isError: true`) met een
duidelijke Nederlandse melding of een JSON-object met `message` en `errors`; nooit een stacktrace.

### Lezen

| Tool | Parameters | Resultaat |
| --- | --- | --- |
| `list_subjects` | – | Alle vakken: `id`, `name`, `description`, `color`, `source`, `questionCount`, `topics[]` (`id`, `name`, `sortOrder`, `questionCount`, `hasTheory`). |
| `get_topic` | `topicId`, `includeQuestions` (standaard `true`) | Onderwerp met `theoryMarkdown`, `questionCount` en een vragenoverzicht (`id`, `externalId`, `type`, `prompt`, `context`, `difficulty`, `hasTip`, `correctAnswer`). |
| `search_questions` | `query?`, `topicId?`, `limit` (1–100, standaard 20) | Volledige vragen (opties met `correct` en `feedback`, `answers`, `tip`, `explanation`). Zoekt in vraagtekst en context. |
| `list_tests` | – | Alle toetsen: `mode`, `timeLimitMinutes`, `passPercentage`, `randomQuestionCount`, `questionCount`, `topicIds`, `fixedQuestionIds`, `studentUrl`. |
| `get_import_format` | – | De specificatie van het importformaat, de validatieregels en een volledig voorbeeld (tekst). |

### Schrijven

| Tool | Parameters | Resultaat |
| --- | --- | --- |
| `create_subject` | `name`, `description?`, `color?` | `{ subjectId, name, alreadyExisted }`. Bestaat het vak al (zelfde naam), dan wordt dat teruggegeven. |
| `create_topic` | `subjectId`, `name`, `theoryMarkdown?` | `{ topicId, name, alreadyExisted, studentUrl }`. Een bestaand onderwerp met dezelfde naam blijft ongewijzigd. |
| `update_topic_theory` | `topicId`, `theoryMarkdown`, `name?` | Vervangt de theorie (en eventueel de naam). |
| `create_questions` | `topicId`, `questions[]` (max. 100) | Per vraag een resultaat; zie hieronder. |
| `import_content` | `document` (ImportDocument als JSON-object), `dryRun` (standaard `false`) | Bij fouten: niets opgeslagen, `errors` en `warnings`. Anders de aantallen aangemaakt/bijgewerkt en de nieuwe toetsen met `studentUrl`. |
| `create_test` | `title`, `mode` (`practice`/`exam`, standaard `exam`), `topicIds?`, `questionIds?`, `randomQuestionCount?`, `timeLimitMinutes?`, `passPercentage` (standaard 80), `description?` | `{ testId, title, mode, questionCount, studentUrl, adminUrl }`. |
| `delete_question` | `questionId` | `{ deleted, prompt }`. |

#### `create_questions`

Elke vraag heeft de vorm van een vraag uit het [importformaat](import-format.md#vraag-questions):

```json
{
  "topicId": 3,
  "questions": [
    {
      "id": "tussen-s-001",
      "type": "multiple-choice",
      "prompt": "Welk woord is goed gespeld?",
      "options": [
        { "text": "stadsbestuur", "correct": true },
        { "text": "stadbestuur", "correct": false, "feedback": "Je hoort een s tussen 'stad' en 'bestuur'." }
      ],
      "tip": "Je hoort een s tussen 'stad' en 'bestuur' → schrijf de s: 'stadsbestuur'.",
      "difficulty": 1
    }
  ]
}
```

Gedeeltelijk succes is mogelijk: goedgekeurde vragen worden opgeslagen, afgekeurde gemeld per positie.

```json
{
  "summary": "1 van 2 vragen opgeslagen (1 nieuw, 0 bijgewerkt); 1 afgekeurd – zie 'errors' per item, verbeter die en stuur alleen die opnieuw.",
  "topicId": 3, "topicName": "Spelling: tussen-s",
  "created": 1, "updated": 0, "failed": 1,
  "createdIds": [77],
  "items": [
    { "index": 0, "status": "created", "questionId": 77, "prompt": "Welk woord is goed gespeld?" },
    { "index": 1, "status": "failed", "prompt": "…", "errors": ["Markeer precies één antwoordoptie als juist (nu 2)."] }
  ]
}
```

`status` is `created`, `updated` (zelfde `id` bestond al in het onderwerp) of `failed`. Een vraag zonder tip wordt
wel opgeslagen, maar krijgt een waarschuwing in `warnings`.

## Prompt

| Prompt | Argumenten | Doel |
| --- | --- | --- |
| `make_practice_set` | `subject`, `topic`, `level` (standaard `hbo jaar 1`), `count` (standaard 15) | Stap-voor-stap-instructie voor een goede oefenreeks: theorie, mix van vraagtypen en moeilijkheid, plausibele afleiders met feedback, een tip per vraag die de regel op die zin toepast, spellingscontrole en opslaan met `create_questions`. |

## Voorbeeldopdracht

> Maak 15 meerkeuzevragen over de tussen-s voor eerstejaars hbo-studenten. Zet bij elke vraag een tip die de regel
> toepast op die ene zin en geef bij elk fout antwoord kort aan waarom het fout is. Zet ze in SnelToets bij het vak
> 'Nederlands – taalverzorging' onder het onderwerp 'Spelling: tussen-s', met een korte theorie-uitleg. Maak daarna
> een oefentoets van 10 willekeurige vragen en geef me de link.

## Zonder MCP

Dezelfde mogelijkheden zijn er via de REST API (zie de README): `POST /api/import` met een document in het
importformaat, `POST /api/tests` en `DELETE /api/questions/{id}`, met dezelfde API-sleutel.
