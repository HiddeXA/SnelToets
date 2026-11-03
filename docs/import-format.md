# Importformaat

SnelToets gebruikt één uitwisselformaat voor alle manieren om inhoud toe te voegen:

- uploaden in het beheer (`/beheer/import`),
- de REST API (`POST /api/import`, exporteren via `GET /api/export`),
- de MCP-tools `import_content` (een heel document) en `create_questions` (een lijst vragen voor één onderwerp).

Een AI-assistent maakt dus precies hetzelfde bestand als een docent zou uploaden. Er zijn twee varianten:
**JSON** (volledig: vakken, onderwerpen met theorie, vragen, feedback per optie en toetsen) en **CSV** (snel vragen
invoeren in een spreadsheet).

Een compleet voorbeeld is [`samples/demo-nederlands.json`](../samples/demo-nederlands.json) (de demo-inhoud);
een CSV-voorbeeld is [`samples/voorbeeld.csv`](../samples/voorbeeld.csv).

## JSON

Veldnamen in camelCase, UTF-8. Commentaar (`//`) en komma's aan het eind van een lijst worden geaccepteerd.

### Document

```json
{
  "version": 1,
  "subjects": [ /* vakken */ ],
  "tests": [ /* toetsen (optioneel) */ ]
}
```

Bij de upload en `POST /api/import` mag je ook alleen één vak-object of een lijst van vakken sturen.

### Vak (`subjects[]`)

| Veld | Type | Verplicht | Betekenis |
| --- | --- | --- | --- |
| `name` | tekst | ja | Naam van het vak. Bestaat er al een vak met deze naam (hoofdletters maken niet uit), dan wordt dat vak aangevuld. |
| `description` | tekst | nee | Korte omschrijving voor studenten. |
| `color` | tekst | nee | Accentkleur als CSS-kleur, bijv. `"#3056d3"`. |
| `topics` | lijst | ja | Onderwerpen. |

### Onderwerp (`topics[]`)

| Veld | Type | Verplicht | Betekenis |
| --- | --- | --- | --- |
| `name` | tekst | ja | Naam van het onderwerp. Wordt binnen het vak op naam gekoppeld aan een bestaand onderwerp. |
| `theory` | Markdown | nee (aanbevolen) | De theorie: regel, stappenplan en voorbeelden. Wordt getoond vóór het oefenen en in de pop-up *Bekijk theorie*. Koppen, lijsten, **vet**, *cursief* en tabellen worden ondersteund; HTML niet. |
| `questions` | lijst | nee | Vragen. |

### Vraag (`questions[]`)

| Veld | Type | Verplicht | Betekenis |
| --- | --- | --- | --- |
| `id` | tekst | nee (aanbevolen) | Vaste sleutel, bijv. `"ww-tt-001"`. Importeer je opnieuw een vraag met hetzelfde `id` in hetzelfde onderwerp, dan wordt de bestaande vraag **bijgewerkt** in plaats van gedupliceerd. |
| `type` | tekst | nee | `multiple-choice` (standaard), `multiple-select`, `true-false` of `fill-in`. |
| `prompt` | Markdown | ja | De vraag of opdracht, bijv. `"Welke vorm is juist?"`. |
| `context` | Markdown | nee | De zin of situatie, bijv. `"Morgen … hij de auto. (wassen)"`. |
| `options` | lijst | bij keuzevragen | Antwoordopties: `{ "text": "…", "correct": true/false, "feedback": "…" }`. `feedback` verschijnt als de student die optie kiest. |
| `answer` | `true`/`false` | nee | Kortere schrijfwijze voor `true-false`: zonder `options` maakt SnelToets de opties **Goed** en **Fout**; `true` betekent dat *Goed* juist is. |
| `answers` | lijst tekst | bij `fill-in` | Alle goede antwoorden. Bij het nakijken tellen hoofdletters, extra spaties en een slotpunt niet mee. |
| `tip` | Markdown | nee (sterk aanbevolen) | Verschijnt direct na een fout antwoord. Pas de regel toe op **deze** vraag, bijv. *"'Je' kun je hier niet vervangen door 'jij' → geen onderwerp → stam + t: 'Vindt je broer'."* |
| `explanation` | Markdown | nee | Extra uitleg die ook na een goed antwoord zichtbaar is. |
| `difficulty` | getal | nee | `1` makkelijk (standaard), `2` gemiddeld, `3` moeilijk. |

Vraagtypen:

| Type | Student doet | Opties / antwoorden |
| --- | --- | --- |
| `multiple-choice` | kiest één optie | ≥ 2 opties, precies 1 juist |
| `multiple-select` | kiest alle juiste opties | ≥ 2 opties, minstens 1 juist; alleen precies de juiste set telt als goed |
| `true-false` | kiest Goed/Fout (of Waar/Niet waar) | precies 2 opties met precies 1 juist, **of** geen opties en `answer` |
| `fill-in` | typt het antwoord | ≥ 1 antwoord in `answers` |

### Toets (`tests[]`)

| Veld | Type | Standaard | Betekenis |
| --- | --- | --- | --- |
| `title` | tekst | – | Titel (verplicht). |
| `description` | tekst | – | Uitleg op de startpagina van de toets. |
| `mode` | tekst | `exam` | `practice`: feedback na elke vraag. `exam`: feedback pas aan het eind, vrij bladeren. |
| `timeLimitMinutes` | getal | – | Tijdslimiet in minuten (weglaten = geen limiet). |
| `passPercentage` | getal | `80` | Percentage goed om te slagen. |
| `randomQuestionCount` | getal | – | Aantal willekeurige vragen per poging uit `topics`. Weglaten = de vaste vragen, of alle vragen van de onderwerpen. |
| `shuffleQuestions` | `true`/`false` | `true` | Vragen in willekeurige volgorde. |
| `topics` | lijst tekst | – | Onderwerpen om uit te trekken, geschreven als `"Vak/Onderwerp"`, bijv. `"Nederlands 3F/Werkwoordspelling"`. |
| `questionIds` | lijst tekst | – | Vaste vragen, via hun `id` uit dit document (of een eerder geïmporteerd `id`). |

Toetsen worden bij elke import **nieuw** aangemaakt (niet bijgewerkt).

### Voorbeeld

```json
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
              "tip": "Onderwerp is 'ik' → alleen de stam: 'Ik vind'."
            },
            {
              "id": "ww-tt-003",
              "type": "fill-in",
              "prompt": "Vul de juiste vorm in van het werkwoord tussen haakjes.",
              "context": "Mijn collega … altijd snel op mijn mails. (antwoorden)",
              "answers": ["antwoordt"],
              "tip": "Onderwerp is 'mijn collega' (enkelvoud) → stam 'antwoord' + t: 'antwoordt'.",
              "difficulty": 2
            },
            {
              "id": "ww-tt-004",
              "type": "multiple-select",
              "prompt": "Welke zinnen zijn goed gespeld? Kies alle juiste zinnen.",
              "options": [
                { "text": "Houd jij je aan de afspraak?", "correct": true },
                { "text": "Wordt je fiets vandaag gerepareerd?", "correct": true },
                { "text": "Vindt je dat ook?", "correct": false, "feedback": "Hier betekent 'je' 'jij': 'Vind je dat ook?'" }
              ],
              "tip": "Kijk per zin naar het onderwerp: 'Houd jij' (geen t), 'Wordt je fiets' (je fiets = hij → t), 'Vind je' (je = jij → geen t).",
              "difficulty": 3
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
```

## CSV

Eén rij per vraag. Scheidingsteken **komma**, puntkomma of tab (wordt herkend aan de kopregel); UTF-8. Zet een
veld tussen dubbele aanhalingstekens als het een scheidingsteken of regeleinde bevat, en verdubbel aanhalingstekens
binnen zo'n veld (`""`).

| Kolom | Nederlandse alias | Verplicht | Inhoud |
| --- | --- | --- | --- |
| `subject` | `vak` | ja | Naam van het vak. |
| `topic` | `onderwerp` | ja | Naam van het onderwerp. |
| `prompt` | `vraag` | ja | De vraag. Een rij met alleen vak, onderwerp en theorie maakt alleen het onderwerp aan. |
| `type` | `soort`, `vraagtype` | nee | `multiple-choice`, `multiple-select`, `true-false` of `fill-in`. Leeg = afgeleid: geen opties → goed/fout (als `correct` goed/fout is) of invulvraag; meerdere juiste nummers → `multiple-select`; anders `multiple-choice`. |
| `context` | `zin` | nee | De zin of situatie. |
| `options` | `opties`, `antwoordopties` | bij keuzevragen | Opties gescheiden door `\|`, bijv. `leest\|lees\|leesd`. |
| `correct` | `juist`, `antwoord`, `answer(s)` | ja | Keuzevraag: nummer(s) van de juiste optie(s), vanaf 1, gescheiden door `\|` (bijv. `1` of `1\|3`). Goed/fout zonder opties: `goed`/`fout` (ook `true`/`false`, `waar`/`onwaar`). Invulvraag: de goede antwoorden, gescheiden door `\|`. |
| `tip` | – | nee (sterk aanbevolen) | Tip na een fout antwoord. |
| `explanation` | `uitleg` | nee | Extra uitleg. |
| `difficulty` | `moeilijkheid`, `niveau` | nee | 1, 2 of 3. |
| `id` | – | nee | Vaste sleutel voor bijwerken bij een volgende import. |
| `theory` | `theorie` | nee | Theorie van het onderwerp; de eerste ingevulde waarde per onderwerp telt. |

Feedback per optie en toetsen kunnen niet in CSV; gebruik daarvoor JSON (of vul het daarna aan in het beheer).
Onbekende kolommen worden genegeerd (met een waarschuwing).

Voorbeeld (zie [`samples/voorbeeld.csv`](../samples/voorbeeld.csv) voor tien rijen):

```csv
subject,topic,id,type,prompt,context,options,correct,tip,explanation,difficulty
Nederlands 3F,Spelling: tussen-s,ts-001,multiple-choice,Welk woord is goed gespeld?,,rijbewijs|rijsbewijs|rij-bewijs,1,Tussen 'rij' en 'bewijs' hoor je geen s → 'rijbewijs'.,,1
Nederlands 3F,Spelling: tussen-s,ts-004,true-false,Is deze zin goed gespeld?,Hij werkt bij de rijksoverheid.,,goed,Je hoort een s tussen 'rijk' en 'overheid' → 'rijksoverheid' is goed.,,1
Nederlands 3F,Werkwoordspelling,ww-003,fill-in,Vul het voltooid deelwoord in.,Ze hebben de hele avond … (dansen),,gedanst,De stam 'dans' eindigt op een s → in 't kofschip → 'gedanst'.,,1
```

## Validatie

Eerst wordt het hele document gecontroleerd. Zijn er **fouten**, dan wordt er niets geïmporteerd en krijg je een
lijst met Nederlandse meldingen met de plaats van de fout (bijv. *"Vak 'Nederlands 3F' › onderwerp 'Tussen-s' ›
vraag 3: Markeer precies één antwoordoptie als juist (nu 2)."*). **Waarschuwingen** blokkeren de import niet.

Fouten:

- Een vak, onderwerp of toets zonder naam/titel; een vraag zonder `prompt`.
- Onbekend vraagtype.
- Keuzevragen (`multiple-choice`, `multiple-select`, `true-false`): minder dan **2 opties**, of een optie zonder tekst.
- `multiple-choice` en `true-false`: niet **precies één** juiste optie.
- `multiple-select`: **geen enkele** juiste optie.
- `true-false`: niet **precies 2** opties en ook geen `answer`.
- `fill-in`: geen enkel antwoord in `answers`.
- `difficulty` niet 1, 2 of 3.
- Hetzelfde `id` twee keer in één onderwerp.
- Toets: onbekende `mode`, `passPercentage` buiten 0–100, `timeLimitMinutes` of `randomQuestionCount` ≤ 0, of geen
  `topics` en geen `questionIds`.
- Het document bevat geen vakken en geen toetsen.

Waarschuwingen:

- Een onderwerp zonder theorie of zonder vragen; een vak zonder onderwerpen.
- Vragen zonder tip (de student krijgt dan alleen een algemene tip).
- Een toets met `randomQuestionCount` maar zonder `topics`, of een onderwerpverwijzing die niet de vorm `Vak/Onderwerp` heeft
  of niet gevonden wordt.

Controleren zonder op te slaan: `POST /api/import?dryRun=true`, of de MCP-tool `import_content` met `dryRun: true`.

## Samenvoegen bij een (nieuwe) import

- **Vakken** en **onderwerpen** worden op **naam** gekoppeld (hoofdletters maken niet uit); ontbrekende worden aangemaakt.
  Een opgegeven `theory` vervangt de theorie van een bestaand onderwerp.
- **Vragen** met een `id` dat in hetzelfde onderwerp al bestaat, worden **bijgewerkt** (tekst, opties, antwoorden,
  tip, uitleg, moeilijkheid). Vragen zonder `id` of met een nieuw `id` worden toegevoegd.
- **Toetsen** worden altijd nieuw aangemaakt.
- De bron van de inhoud wordt vastgelegd: *Import* (upload/REST), *AI* (MCP) of *Seed* (demo).
