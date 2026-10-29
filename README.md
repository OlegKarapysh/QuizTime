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

Still to come, by milestone: the `write-quiz-batch` skill and its subagents (3); the roadmap
app in `app/` (5).

Contributing, for people and agents: [AGENTS.md](AGENTS.md), with the style in
[docs/CODE_STYLE.md](docs/CODE_STYLE.md) and the testing strategy in
[docs/TESTING.md](docs/TESTING.md).

## Validate

Requires the .NET 10 SDK.

```bash
tools/validate
```

It checks every roadmap, concept and question against its schema, then against each other:
unique ids, file paths that match the roadmap, node and id, nodes and concepts that exist,
answers and blind-solve picks that are real option ids, and check files where the
verification method needs one. Then the lint rules: option lengths, pinned docs versions,
and the names in `private/denylist.txt` when that file exists. Errors print as
`file:line:column: message`, and the exit code is 1 if there are any.

## Run the checks

A check reproduces the question's snippet and passes only when the marked answer holds.
`tools/run-checks` runs them all, or the ids you pass. Database checks need Docker: it
starts a PostgreSQL 16 container for the run and gives each check its own database.

```bash
tools/run-checks
```

```bash
tools/run-checks --changed-since main
```

One C# check also runs on its own:

```bash
dotnet run checks/csharp-001/check.cs
```

## Check the links

`tools/check-links` opens every source link and checks that the page and the anchor it
points to exist. It needs the network; CI runs it on every PR.

```bash
tools/check-links
```

## Test the tools

```bash
dotnet test --solution QuizTime.slnx
```

## Before you commit

`tools/verify` runs what CI's verify job runs: format check, build and tests, validate, and
actionlint when it's installed (CI always runs it). If you changed questions or checks, also
run `tools/run-checks --changed-since main`. Enable the pre-commit hook once per clone; it
runs `tools/validate` on the staged files of every commit:

```bash
git config core.hooksPath .githooks
```

The full command list is in [AGENTS.md](AGENTS.md#commands).
