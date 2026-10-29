# QuizTime

A public, verified question bank for .NET quizzes. One pool of questions serves a daily
.NET quiz and the roadmap skill quizzes. No question reaches anyone unverified: each one is
checked by running code where possible, by a blind solver, by an adversarial reviewer, and
against sources that were opened and confirmed.

The full plan, decisions and milestones are in [docs/PLAN.md](docs/PLAN.md).

## Layout

| Path | What it holds |
| --- | --- |
| `roadmaps/<roadmap>.yaml` | Roadmap mirror: the nodes questions attach to |
| `concepts/<domain>.yaml` | Concept registry: one entry per idea; every question tests exactly one |
| `questions/<roadmap>/<node>/<id>.yaml` | One question each: stem, options, answer, explanation, sources, verification |
| `checks/<id>/check.cs` | Runnable check for a question (a .NET 10 file-based app), or `check.sql` |
| `schema/*.schema.json` | JSON Schemas for the three file kinds, plus shared definitions |
| `tools/` | `QuizBank` (.NET 10 console app), its tests, and one wrapper script per tool |
| `private/` | Git-ignored: weekly exports and the name denylist |

Still to come, by milestone: lint, run-checks, link checker and CI (2); the
`write-quiz-batch` skill and its subagents (3); the roadmap app in `app/` (5).

## Validate

Requires the .NET 10 SDK.

```bash
tools/validate
```

It checks every roadmap, concept and question against its schema, then against each other:
unique ids, file paths that match the roadmap, node and id, nodes and concepts that exist,
answers and blind-solve picks that are real option ids, and check files where the
verification method needs one. Errors print as `file:line:column: message`, and the exit
code is 1 if there are any.

## Run a check

A check reproduces the question's snippet and exits 0 only when the marked answer holds:

```bash
dotnet run checks/csharp-001/check.cs
```

## Test the tools

```bash
dotnet test --solution QuizTime.slnx
```
