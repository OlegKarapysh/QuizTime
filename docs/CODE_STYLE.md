# Code style

`dotnet format` handles layout (`tools/format`, checked by `tools/verify`), and `.editorconfig` records the C# preferences. This guide covers what tools can't decide. Where it describes the code in `tools/QuizBank`, the code is the reference: if they disagree, fix the guide.

## Everywhere

- English, UTF-8, LF line endings, final newline (`.editorconfig`).
- Use the words docs/PLAN.md uses: question, concept, node, area, level, gate, check, source, batch. Don't invent synonyms in code or docs.
- Comments explain why, not what. No commented-out code. A TODO names its milestone: `// TODO(milestone 2): ...`.
- Each new package needs a reason in the PR description.
- Example names are neutral (Shop, Orders, Billing, Customer). The repo is public.

## C# tools (`tools/QuizBank`)

The tools are one .NET 10 console app. Each tool is a command (`quizbank validate`) with a wrapper script in `tools/` (`tools/validate`), so CI needs only the .NET SDK.

**Project setup**

- `tools/Directory.Build.props` sets net10.0, implicit usings, nullable and warnings as errors for every tool project. Don't relax them per project.
- Package versions go in `tools/Directory.Packages.props`. Project files reference packages without a version.
- A new project is added to `QuizTime.slnx`.

**Commands**

- `Cli.Run(args, stdout, stderr)` parses arguments and returns the exit code. It takes writers instead of using `Console`, so tests call it directly.
- Exit codes: `0` passed, `1` found errors, `2` bad usage or environment (unknown command, no repo root, Docker not running).
- Errors go to stdout, one per line, as `file:line:column: message` with the file relative to the repo root and forward slashes (`Diagnostic`). A summary line comes last, like `validate: OK, 1 roadmap (41 nodes), 11 concepts, 1 question`.
- Messages are full sentences that end with a period and quote values in single quotes, and say how to fix the problem when it isn't obvious: `Concept 'x' does not list node 'linq'; add it to the concept in concepts/sample.yaml.`
- Output is deterministic: same input, same lines in the same order.

**Code**

- File-scoped namespaces matching the folder (`QuizBank.Validation`). One main type per file.
- Keep logic apart from I/O. A validator collects `Diagnostic`s and returns a report; only `Cli` writes output.
- `sealed` records for data, `sealed` or `static` classes otherwise. Private fields are `_camelCase`.
- `var` everywhere. Pattern matching (`args is ["validate", .. var options]`) and switch expressions where they read better than `if` chains.
- A single-line `if` may skip braces; anything longer gets them.
- `/// <summary>` on public types and members whose purpose isn't obvious from the name.
- `[GeneratedRegex]` for regular expressions.
- No network in validate or lint. A tool that needs the network (the link checker) takes an `HttpMessageHandler` or similar seam, so tests can fake it.

## Checks (`checks/<id>/check.cs`)

A check proves a question's marked answer by running the question's own code on .NET 10. `checks/csharp-001/check.cs` is the reference.

- A file-based app with top-level statements and no project file. Directives come first: `#:sdk Microsoft.NET.Sdk.Web` when ASP.NET Core is needed, `#:package Name@1.2.3` with an exact version.
- The header comment names the question and the marked answer, and says how the check decides: `// Check for csharp-001 (marked answer: b).`
- Copy the question's code unchanged between markers. Statements go between `// --- snippet statements ---` and `// --- end snippet statements ---`; types go after `// --- snippet types ---` at the end of the file. Harness code stays outside the markers.
- Assert the exact claim: printed output, exception type, HTTP status, or the SQL sent (`ToQueryString()` or EF logging). For "what does this print" questions, capture the output and check that exactly one option matches it and that it is the marked one.
- On success, print `PASS <id>: <what was observed>` to stdout and return 0. On failure, print `FAIL <id>: <what was observed>; <what was expected>` to stderr and return 1.
- Deterministic: no wall-clock time, random values, network calls, or results that depend on thread scheduling, unless the question is about that and the check controls it.
- Database checks read the connection string from `QUIZTIME_PG` and work in their own schema or database, which they drop at the end, so checks can run in parallel.
- Compiler warnings are allowed. A snippet may warn on purpose: CS0252 on `object == string` is often the point of the question.
- Question code follows the same C# layout, so a copy passes `tools/verify`.

## SQL checks (`checks/<id>/check.sql`)

- PostgreSQL 16. Uppercase keywords, snake_case identifiers.
- Self-contained: create what the check needs in its own schema and drop it at the end.
- The expected-result format is defined in milestone 2 together with `tools/run-checks`. Document it here when it exists.

## YAML (questions, concepts, roadmaps)

The schemas and `tools/validate` enforce structure. Beyond that, follow the existing files:

- One question per file: `questions/<roadmap>/<node-id>/<question-id>.yaml`. The file name equals `id`. Ids are lowercase kebab-case; question ids are `<node-id>-<3-digit seq>`.
- Keys follow the order of the PLAN.md example, with a blank line between top-level sections, as in `questions/dotnet-developer/csharp/csharp-001.yaml`.
- Prose goes in block scalars (`|`, or `>-` for a one-paragraph takeaway), wrapped at about 80 characters. URLs and one-line fields such as `look_for` aren't wrapped.
- Quote strings that YAML would otherwise read as something else: versions (`dotnet: "10.0"`), `true`/`false`/`null`, and text containing `: ` or ` #`.
- Dates are ISO and unquoted: `checked: 2026-11-02`.
- Short records use inline maps with spaces inside the braces: `- { id: a, text: "..." }`.
- Code in a question is C# in the style above, about 20 lines at most.

What makes a good question (no trivia, option lengths, explanations, sources) is in the PLAN.md section "Question writing rules". It isn't repeated here.

## Scripts (`tools/<name>`, `.githooks/`)

- Bash, no extension, executable: `#!/usr/bin/env bash` and `set -euo pipefail`. Quote every variable.
- A tool's wrapper finds the repo root from its own path and `exec`s the console app, like `tools/validate`. Logic belongs in C#, not in the script.

## Roadmap app (`app/`, milestone 5)

- TypeScript in strict mode. Its build and lint tools are chosen in milestone 5 and added to `tools/verify` and CI in the same PR.
- No external requests, accounts, analytics or cookies. Results stay in `localStorage`.
- The app reads JSON generated by `pages.yml`. It never parses YAML in the browser.

## GitHub Actions

- Pin actions to a major version (`actions/checkout@v7`) and Docker actions to an exact version. Dependabot proposes bumps.
- Least-privilege `permissions:`, `contents: read` unless a job writes.
- Every job has `timeout-minutes`.
- actionlint runs in CI, and in `tools/verify` when it's installed.

## Markdown

- Sentence-case headings, fenced code blocks with a language, relative links between repo files.
- Docs describe the repo as it is now. Planned things carry their milestone.
