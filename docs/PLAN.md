# Daily .NET quiz + Brights roadmap quizzes: implementation plan

Oct 29, 2026 · @Oleh

## Goal and context

One public question bank will serve both Oleh's daily .NET quiz and the Brights roadmap skill quizzes. Claude Code writes and verifies questions in weekly batches. Oleh answers and rates 7 a day in claude.ai, and questions he rates 4–5 become the coworker-facing roadmap quizzes.

The bar: no question reaches anyone unverified. Every marked answer is checked by running code where possible, by a blind solver, by an adversarial reviewer, and against sources that were opened and confirmed.

What exists today:

- **Daily quiz page:** a published claude.ai artifact ([link](https://claude.ai/artifact/4brvLXqnEXrgiXN4rsR2xs)) that keeps results in its own database. It shows 6 single-choice questions a day across six areas: C# language features; ASP.NET Core / APIs; EF Core & PostgreSQL; Async, DI & architecture; Security; Observability.
- **Roadmap drafts** from the claude.ai chat "Skill assessment quizzes for role roadmaps" (Oct 16): a JSON Schema for YAML question files, a mirror of 41 of the 96 nodes of the .NET Developer roadmap, and a 9-question pilot for the Middlewares node. Not reviewed yet; no repo.

## Decisions locked in

All of these were settled with Oleh; treat them as requirements, not suggestions.

| Topic | Decision |
| --- | --- |
| Daily size | 7 questions: 5 new + 2 review. Spare review slots become new questions. |
| New-question mix | 3 aimed at Oleh's weak spots, 2 filling roadmap gaps (intermediate and advanced only). If one side has nothing, the other takes the slot. |
| Target versions | .NET 10 (C# 14, ASP.NET Core 10, EF Core 10, Npgsql 10) and PostgreSQL 16. |
| Where questions are written | Claude Code in the roadmap repo, weekly batches. |
| Where Oleh answers | The existing claude.ai quiz page, same URL. |
| Question types | Single-choice; at most one multi-select per daily quiz. No typed predictions. |
| Multi-select scoring | (correct picks − wrong picks) ÷ number of correct options, never below 0. |
| Confidence | Each answer is locked in with a Sure or Not sure tap. |
| Question Ratings | Required before Next: quality 1–5 (a reason is required for 1–2) and value: not useful, useful, very useful. |
| Question Labels | Area and difficulty are shown before answering. Review questions are not marked as reviews. |
| Difficulty target | 70–80% first-try accuracy per area. Above 85% for a week → harder; below 60% → easier. |
| Where the rules live | The question-writing skill and subagents live in the repo. A small daily-picking skill lives on Oleh's claude.ai account. |
| Roadmap scope | Start with the 41 mirrored nodes. |
| Past 53 questions | Stay as history on the quiz page; not imported or audited. |

## Architecture and who builds what

Questions flow one way into a verified pool, and Oleh's results flow back once a week.

&#91;embedded content: quiz system · weekly and daily loops\]

Claude Code builds the weekly lane and the roadmap app. The daily run and the quiz page are built in a claude.ai chat.

## Repo layout

One public repo holds the bank, the checks, the tooling, the skill and the roadmap app. Repo name and owner are still open.

```
<repo>/
├─ docs/PLAN.md                         # this plan
├─ roadmaps/dotnet-developer.yaml       # roadmap mirror, 41 of 96 nodes
├─ concepts/dotnet.yaml                 # concept registry
├─ questions/dotnet-developer/<node-id>/<question-id>.yaml
├─ checks/<question-id>/check.cs        # runnable check (or check.sql)
├─ schema/question.schema.json
├─ schema/concept.schema.json
├─ schema/roadmap.schema.json
├─ tools/                               # validate, lint, run-checks, check-links, pool-stats
├─ app/                                 # roadmap quiz app (GitHub Pages)
├─ .claude/skills/write-quiz-batch/     # SKILL.md + scripts
├─ .claude/agents/blind-solver.md
├─ .claude/agents/adversarial-reviewer.md
├─ .github/workflows/                   # ci.yml, pages.yml, weekly-recheck.yml
└─ private/                             # git-ignored: weekly exports, name denylist
```

Tools are one .NET 10 console app, `tools/QuizBank`, with a wrapper script per tool (`tools/validate`, …), so CI needs only the .NET SDK the checks already use. The roadmap app in `app/` is TypeScript. Shared schema definitions (areas, slugs, dates, versions) live in `schema/common.schema.json`. Checks are C# file-based apps run with `dotnet run check.cs`, or SQL scripts. If the Oct 16 draft files can still be downloaded from that chat, start from them; otherwise recreate them from this plan.

## Question and concept schema

Extend the drafted schema: each question gets a concept, an area, target versions, a two-part explanation, checked sources, a verification record and a status. The drafted `resources` field becomes `sources`.

```yaml
id: middlewares-012                # <node-id>-<3-digit seq>, unique, never reused
roadmap: dotnet-developer
node: middlewares                  # must exist in roadmaps/dotnet-developer.yaml
concept: middleware-order-wraps-later   # must exist in concepts/dotnet.yaml
area: ASP.NET Core / APIs          # one of the six daily-quiz areas
level: advanced                    # beginner | intermediate | advanced
type: single-choice                # single-choice | multi-choice | ordering
targets: { dotnet: "10.0" }        # add efcore / npgsql / postgres when relevant
stem: |
  ...
code:                              # optional
  lang: csharp
  text: |
    ...
options:
  - { id: a, text: "..." }
  - { id: b, text: "..." }
  - { id: c, text: "..." }
  - { id: d, text: "..." }
answer: b                          # a list of ids for multi-choice
explanation:
  takeaway: "..."                  # 30 words max, shown first
  details: |                       # 150 words max, behind "More"
    ...
sources:
  - kind: proof                    # proof | further
    title: "..."
    url: https://learn.microsoft.com/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0#<anchor>
    look_for: "<one line: what the section says>"
    checked: 2026-11-02
verification:
  method: run                      # run | sql | docs
  check: checks/middlewares-012/check.cs
  blind_solve: { model: "<model>", picked: b, agreed: true, date: 2026-11-02 }
  audit: { verdict: pass, date: 2026-11-02 }
status: verified                   # draft | verified | reviewed | quarantined | retired
```

The concept registry holds one entry per idea:

```yaml
- id: operator-binding-static
  idea: "== and other operators are chosen at compile time from the static types"
  area: C# language features
  nodes: [csharp]
```

Rules:

- A concept is one idea that can be tested from at least three different angles. Aim for 10–20 concepts per area. Every question tests exactly one concept.
- Status flow: `draft` → `verified` (all gates passed, merged) → `reviewed` (Oleh rated quality 4–5). Any status → `quarantined` (a failed re-check or a 1–2 rating for a wrong or doubly defensible answer) → fixed back to `verified`, or `retired`.
- The daily quiz page uses single-choice and multi-choice only. Ordering questions are for the roadmap app.
- Old difficulty labels map as: mid-level → intermediate; upper mid-level and senior → advanced.

## Question writing rules

A question tests understanding of one concept through a realistic scenario or code, and nothing about it should give the answer away.

- **No trivia.** Don't ask for namespace names, signatures or flags that people look up instead of knowing.
- **Neutral names only:** Shop, Orders, Billing and similar. Never client or project names, because the repo is public. `private/denylist.txt` lists names the lint must reject.
- **State versions and config** whenever the behavior depends on them.
- **Options:** 4 for single-choice, each a full, plausible answer in the same structure. Within a question, the longest option is at most 1.3× the shortest. Across a batch, the correct option is the longest in at most 35% of questions (chance is 25%).
- **Wrong options** come from real misconceptions, including Oleh's past wrong picks from the export, reworded so old option text isn't recognizable.
- **Multi-choice** only where several answers are genuinely right: 4–5 options, 2–3 correct.
- **Code** is short enough to read in under a minute (about 20 lines). It compiles as shown, or the stem says it's a fragment.
- **Explanation:** a takeaway of 30 words or fewer, then details of 150 words or fewer that also say why the most tempting wrong option is wrong.
- **Difficulty label** is the writer's estimate. Selection uses measured accuracy, not the label.

### Sources

Every question has at least one proof source that directly states what the answer claims.

- **Proof sources:** Microsoft Learn pages pinned to the .NET 10 and ASP.NET Core 10 views, the .NET API reference, the C# language reference and specification, EF Core and Npgsql docs, PostgreSQL 16 docs, OWASP cheat sheets and RFCs.
- **Further reading:** .NET team blog posts, and `dotnet/runtime` or `dotnet/aspnetcore` source and issues when the docs don't cover a behavior.
- **Books** appear only as further reading, at book-and-chapter level, after the chapter is confirmed in the publisher's table of contents. They are never proof, because the text can't be opened to confirm the claim.
- **Every link** goes to the exact section, with a one-line note on what to look for there and the date it was checked. The writer must open the page and confirm it states the claim; a search snippet doesn't count. Videos are never proof.

## Verification pipeline

Each question passes five gates before merge and keeps being re-checked after it. No single gate catches everything, so each covers what the previous one misses.

1. **Schema and lint** (`tools/validate`): schema, unique ids, answer ids exist, node and concept exist, option-length rules, denylist.
2. **Run it** (`tools/run-checks`): each runnable question has `checks/<id>/check.cs`, a .NET 10 file-based app using `#:package` and `#:sdk Microsoft.NET.Sdk.Web` as needed, with PostgreSQL 16 from Docker. The check reproduces the exact snippet and asserts the marked answer: printed output, exception type, HTTP status, or the SQL sent (`ToQueryString()` or EF logging). For "what does this print" questions it also asserts that exactly one option matches the real output. `method: sql` questions run a `.sql` script with expected results. `method: docs` is allowed only when there is no runnable behavior.
3. **Blind solve** (`blind-solver` subagent): it starts with a fresh context and gets only the stem, code and options, never the answer, explanation or sources. It returns its pick, reasoning and confidence. Disagreement blocks the question until it's resolved. Give this subagent no file, search or shell tools, so it can't look the answer up in `questions/`, and run it on a different model from the writer.
4. **Adversarial audit** (`adversarial-reviewer` subagent): it sees everything and tries to break it. Is a second option defensible? Does the answer depend on an unstated version or config? Is any sentence in the explanation false? Does each source say what it's cited for (it opens every link)? Is the setup ambiguous or unrealistic? Does length give the answer away? It returns pass or a fix list.
5. **Fix loop:** failures go back to the writer for at most two rounds. Anything still failing is dropped and listed in the PR.

After merge:

- **CI on every PR** re-runs gates 1 and 2 plus a link check. Merge sets `status: verified`.
- **A weekly CI re-check** runs every check and link. A failure opens an issue and quarantines the question, which catches .NET patch changes and moved docs.
- **Oleh's ratings:** quality 1–2 with "wrong answer" or "more than one defensible answer" quarantines the question. It leaves the pool and his stats until fixed.

No process makes errors impossible. Conceptual questions with no runnable behavior rely on gates 3 and 4 and their sources, so the auditor should treat them with extra suspicion.

## Weekly batch workflow

The `write-quiz-batch` skill runs once a week in Claude Code and ends with one PR of verified questions, about 50 at first.

1. **Load the latest export** from `private/`. If there is none yet, use the bootstrap targets below.
2. **Apply ratings.** Quality 4–5 on a verified question → `reviewed`. Quality 1–2 → `quarantined` with its reason, then fix or retire. Value ratings never go into the repo; they only steer planning.
3. **Plan the batch.**
   - New slots: 35 a week (5 a day). Three a day target weak spots: lowest first-try accuracy and confident misses first, Observability and Security weighted up, Async/DI down, difficulty steered toward the 70–80% target. Two a day fill roadmap gaps: the mirrored nodes with the fewest verified intermediate or advanced questions.
   - Reviews: one unseen question for every concept falling due in the next 7 days.
   - Spare: about 20% extra for questions that fail gates.
   - Value ratings shift topics and formats toward what Oleh found useful.
4. **Write** the questions under the writing rules. Add new concepts to `concepts/dotnet.yaml` when needed.
5. **Run gates 1–5.**
6. **Open a PR** whose description shows counts by area, node and level, dropped questions with reasons, and every blind-solve disagreement with how it was resolved.

Tune the batch size from `tools/pool-stats` once the first few weeks show how many questions Oleh actually uses.

## Bootstrap: first batch targets

The first batch has no export to read, so it targets these first-try results from Oleh's quizzes of Oct 16–28 (53 questions, 74% overall).

| Area | First try | Misses |
| --- | --- | --- |
| Observability | 1 of 3 | `StartActivity` returns null without a listener; `Activity.Current` flows with the async context, not threads |
| Security | 1 of 3 | SQL injection across `FromSql` / `FromSqlRaw` patterns; open redirect with backslash URLs |
| C# language features | 7 of 10 | All on equality: record `with` copies are shallow; `==` on `object`; `==` inside a generic |
| ASP.NET Core / APIs | 8 of 12 | 4 misses Oct 19–22 on the pipeline and routing; 6 correct in a row since |
| EF Core & PostgreSQL | 10 of 12 | Npgsql `DateTime` kind (later correct); `Update` on a detached entity overwrites every column |
| Async, DI & architecture | 12 of 13 | `Task.WhenAll` exception order (later correct) |

Priorities for batch one: `operator-binding-static` (missed twice), then Observability and Security concepts, then the detached-entity `Update` concept. These need new angles, not repeats of the old questions.

## Daily run and quiz page

This side is built in a claude.ai chat, not Claude Code. Claude Code only needs to honour the contract: question files as in the schema, readable from the public repo, and the export format below.

The daily run, at 9am in claude.ai through a small skill on Oleh's account:

1. Download the repo tarball from `codeload.github.com`, which the claude.ai sandbox can reach (`github.io` is blocked there). The pool is every question with status `verified` or `reviewed`, type single- or multi-choice, level intermediate or advanced, that Oleh hasn't answered.
2. Read the quiz page database: answers, ratings and the concept schedule.
3. Pick 7: up to 2 due reviews in queue order, 3 weak-spot questions, 2 roadmap-gap questions. Spare review slots go to new questions; at most one multi-choice.
4. Publish to the quiz page at the same URL, keeping the repo question ids.

The quiz page upgrade:

- Resume an unfinished run; retries are practice and never count. Stats use the first answer ever per question, with past days recalculated from saved answers.
- Lock in each answer with Sure or Not sure; save a timestamp per answer.
- Require both ratings before Next.
- Multi-select with partial credit.
- Show the takeaway first; details and sources under "More".
- Drop quarantined questions from stats.
- Update the concept schedule when an answer is saved.
- Stats: accuracy when Sure vs Not sure, concepts due, learning and mastered, confident misses, review queue size, and progress per roadmap node.
- Weekly export, either from a button on the page or generated in chat, saved by Oleh as `private/export-YYYY-MM-DD.json`.

## Spaced repetition rules

A concept comes back on a 2 → 7 → 21-day ladder and graduates after two Sure-and-correct reviews in a row.

| Event | Next due | Streak |
| --- | --- | --- |
| New question: miss or partial | in 2 days | 0 |
| New question: correct, Not sure | in 7 days | 0 |
| New question: correct, Sure | not scheduled | — |
| Review: correct, Sure | next ladder step (2 → 7, 7 → 21) | +1; at 2 the concept graduates |
| Review: correct, Not sure | same step again | reset to 0 |
| Review: miss or partial | in 2 days | reset to 0 |

- A review is always an unseen question on the concept, never the same question again.
- When more than 2 are due, the queue serves misses first, then Not sure answers, oldest due date first within each group.
- Graduated concepts don't come back; spare review slots go to new questions.

## Weekly export format

One JSON file a week carries Oleh's answers, ratings and concept schedule into the repo's git-ignored `private/` folder, so his results never become public.

```json
{
  "exportedAt": "2026-11-08T09:30:00Z",
  "answers": [
    {
      "qid": "middlewares-012", "concept": "middleware-order-wraps-later",
      "area": "ASP.NET Core / APIs", "node": "middlewares", "level": "advanced",
      "type": "single-choice", "picked": ["c"], "correct": false, "score": 0,
      "confidence": "sure", "answeredAt": "2026-11-03T09:04:12Z", "msSpent": 84000
    }
  ],
  "ratings": [
    {
      "qid": "middlewares-012", "quality": 2, "reasons": ["more-than-one-defensible"],
      "value": "useful", "comment": "", "ratedAt": "2026-11-03T09:05:01Z"
    }
  ],
  "concepts": [
    {
      "concept": "middleware-order-wraps-later", "stepDays": 2, "streak": 0,
      "dueAt": "2026-11-05", "status": "learning"
    }
  ]
}
```

- `confidence`: `sure` | `unsure` | `unknown` (answers from before the upgrade).
- `value`: `not-useful` | `useful` | `very-useful`.
- `reasons`: `wrong-answer` | `more-than-one-defensible` | `ambiguous` | `explanation-unclear` | `trick-question` | `unrealistic`.
- `status`: `due` | `learning` | `graduated`.
- Each export is cumulative: it holds every first answer ever, so the batch planner knows every question Oleh has already seen.

## Roadmap quiz app

A static app in `app/`, deployed to GitHub Pages, lets coworkers self-assess per roadmap node using only `reviewed` questions.

- Shows the roadmap tree (the 41 mirrored nodes). Each node has a quiz of 3–15 questions depending on its depth; a node with fewer than 3 reviewed questions shows "coming soon".
- Results stay in the browser's localStorage only. No backend, accounts or tracking.
- After a quiz, gaps are listed with the questions' sources, so each gap links to its reading.
- Each node has a stable deep link (`#/dotnet-developer/<node-id>`), so the in-house roadmap app can link in later.
- A build step in `pages.yml` converts reviewed YAML questions into JSON for the app.

## CI workflows

Three GitHub Actions workflows, all using the .NET 10 SDK (`actions/setup-dotnet`) and a PostgreSQL 16 service container where checks need a database.

| Workflow | Trigger | What it does |
| --- | --- | --- |
| `ci.yml` | Pull request | Validate and lint; run checks for changed questions (all of them on `main`); check links |
| `pages.yml` | Push to `main` | Build JSON from reviewed questions; deploy the roadmap app |
| `weekly-recheck.yml` | Weekly schedule | Run every check and link; on failure, open an issue listing the questions to quarantine |

CI can't see `private/`, so the denylist check runs locally in the skill and again in the PR author's pre-commit step.

## Milestones and acceptance criteria

Claude Code builds milestones 1–5 in order; each ends with the stated check passing. Stop and ask Oleh before moving past a milestone whose check fails.

1. **Repo skeleton.** Public repo with the layout above, the three schemas (port and extend the drafted one), the 41-node roadmap mirror, and a concept registry seeded with the bootstrap concepts. Done when `tools/validate` passes on one example question.
2. **Tooling and CI.** Validate and lint, the run-checks harness, the link checker, and `ci.yml`. Done when a PR with two example questions (one `run`, one `sql`) goes green.
3. **Skill, subagents and gate calibration.** `SKILL.md`, `blind-solver` and `adversarial-reviewer`. Run the 9-question Middlewares pilot through all gates, then plant three faulty questions: a wrong marked answer, two defensible answers, and a false sentence in an explanation. Done when every planted fault is caught and the report names the gate that caught it.
4. **First weekly batch** from the bootstrap targets, merged through a PR. Done when the pool holds at least 35 new questions plus reviews for the first week.
5. **Roadmap app v1.** Done when a node quiz runs end to end on GitHub Pages, with results in localStorage.

After milestone 4, in a claude.ai chat: the quiz page upgrade, the daily skill, and the first export. Done when one daily quiz is served from the pool and an export round-trips into the next batch.

## Deferred and open items

None of these block milestone 1.

- [ ] Choose the repo name and owner; the daily skill needs both.
- [ ] Decide how beginner-level roadmap questions get reviewed, since Oleh's daily quiz only covers intermediate and advanced.
- [ ] Add the other 55 roadmap nodes, ideally from an export of the in-house app.
- [ ] Schedule the weekly batch once manual runs are stable.
- [ ] After the first week, check whether 7 questions with three taps each fit in 10–15 minutes, using the saved timings.
