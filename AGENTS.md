# AGENTS.md

Instructions for coding agents (Claude Code, Codex, Copilot and others) working in this repo. Humans start at [README.md](README.md).

## What this repo is

A public, verified question bank for .NET 10 developers, the tooling that verifies it, and a roadmap quiz app. Questions are written by agents in weekly batches. No question reaches anyone until its marked answer has been proven by running code, a blind solver, an adversarial reviewer and sources that were opened and confirmed.

[docs/PLAN.md](docs/PLAN.md) is the source of truth for what to build. Its "Decisions locked in" table holds requirements, not suggestions: ask Oleh before changing one.

**Status:** milestone 1 (repo skeleton, `tools/validate`) is done. Milestone 2 (lint, run-checks, link checker, the checks CI job) is next. Update this line when a milestone's acceptance check passes.

## Repo map

| Path | What | When |
| --- | --- | --- |
| `docs/PLAN.md` | Plan: schemas, writing rules, gates, milestones | now |
| `docs/CODE_STYLE.md` | Style for C# tools and checks, YAML, scripts, the app | now |
| `docs/TESTING.md` | Testing and verification strategy | now |
| `schema/*.schema.json` | JSON Schemas for roadmaps, concepts and questions, plus shared definitions | now |
| `roadmaps/`, `concepts/`, `questions/` | The bank | now |
| `checks/<id>/check.cs`, `check.sql` | Runnable proof for one question | now |
| `tools/QuizBank/` | The tools: one .NET 10 console app, one command per tool | now |
| `tools/QuizBank.Tests/` | xunit v3 tests for the tools | now |
| `tools/validate`, `tools/verify`, `tools/format` | Wrapper scripts; one per tool, plus the dev scripts | now |
| `.github/workflows/ci.yml` | CI on PRs and `main`: `tools/verify` and actionlint | now |
| `.claude/` | Claude Code settings, hooks and the `small-commits` skill | now |
| Gate 1 lint rules (in `tools/validate`), `tools/run-checks`, `tools/check-links` | More tools | milestone 2 |
| `.claude/skills/write-quiz-batch/`, `.claude/agents/` | Batch skill, blind-solver, adversarial-reviewer | milestone 3 |
| `app/` | Roadmap quiz app (TypeScript) on GitHub Pages | milestone 5 |
| `private/` | Git-ignored: Oleh's weekly exports, name denylist | local only |

## Commands

Needs the .NET 10 SDK, and Docker for database checks. Once per clone, enable the pre-commit hook: `git config core.hooksPath .githooks`. It runs gate 1 on the staged files, not the working tree.

This table is the canonical command list. Other docs show a subset and link here.

| Command | What it does |
| --- | --- |
| `tools/verify` | Everything CI runs: format check, build and tests (warnings are errors), validate, actionlint if installed. Must pass before every commit. |
| `tools/format` | Apply the formatting `tools/verify` checks |
| `tools/validate` | Gate 1 on the real bank: schemas, then cross-references |
| `dotnet test --solution QuizTime.slnx` | All tool tests |
| `dotnet test --project tools/QuizBank.Tests --filter-method "*Unknown_node_is_reported"` | One test |
| `dotnet run checks/<id>/check.cs` | One question's check |

## Hard rules

1. **The repo is public.** No client, project, company or coworker names anywhere: code, questions, commits, PRs, issues. Use neutral names (Shop, Orders, Billing). `private/denylist.txt` enforces this once the gate 1 lint rules exist.
2. **`private/` never leaves the machine.** Don't commit it, copy from it into tracked files, or quote it in commits, PRs, issues or logs. Oleh's answers and ratings stay private.
3. **Only gates verify.** Only passing gates set `status: verified`; only Oleh's 4–5 rating sets `reviewed`. Never write a `verification` result you didn't observe.
4. **Never weaken a check to get green.** That includes tests, validation rules, check assertions, warnings-as-errors and CI steps. Fix the cause, or stop and ask.
5. **Question ids are never reused**, even after a question is retired.
6. **The blind solver can't look anything up.** It gets no file, search, shell or web tools, and never sees the answer, explanation or sources. It runs on a different model from the writer.
7. **Milestones go in order.** If a milestone's acceptance check fails, stop and ask Oleh before starting the next.
8. **Proof means opened and confirmed.** A search snippet, a video or a book is never proof for a question's answer.

## How to work

1. Read the PLAN.md section for the task, plus [docs/CODE_STYLE.md](docs/CODE_STYLE.md) and [docs/TESTING.md](docs/TESTING.md) for the area you touch.
2. Branch from `main`: `<type>/<topic>`, for example `feat/run-checks`, `fix/option-length-rule`, `batch/2026-11-02`.
3. For tools, write the failing test first: break one thing in a `TestRepo` and assert the exact error.
4. Run `tools/verify`, then verify the behaviour for real (see "Verifying your change" in TESTING.md).
5. Commit following [the small-commits skill](.claude/skills/small-commits/SKILL.md): at most 15 files per commit, brief messages, no `Co-Authored-By` or other attribution trailer. Agents other than Claude Code: read that file and follow it.
6. Push and open a PR using the template. CI must be green before merge.

A change is done when:

- `tools/verify` passes locally and in CI.
- New behaviour has tests. A new tool gets a wrapper script in `tools/` and is wired into `tools/verify` and CI in the same PR.
- This file, README.md, CODE_STYLE.md or TESTING.md is updated if commands, layout or rules changed.
- The PR says how the change was verified, and what wasn't verified and why.

Never force-push `main`, rewrite published history, or skip hooks with `--no-verify`. Claude Code's project settings deny force-pushes on every branch; when a rewrite is really needed, the user runs the push. The settings only match the start of a command, so they're a guard rail, not a guarantee. Branch protection on `main` is what actually prevents it.

## Gotchas

- `tools/Directory.Build.props` (net10.0, nullable, warnings as errors) and `tools/Directory.Packages.props` apply to everything under `tools/`. Keep file-based apps (checks, calibration checks) out of `tools/`. There they inherit both and fail with `NU1008`, and inside a project folder such as `tools/QuizBank.Tests/` the project also compiles them (`CS9298`).
- Checks run with `dotnet run checks/<id>/check.cs`. They take `#:sdk` and `#:package Name@1.2.3` directives at the top. The first run restores packages and is slow.
- Database checks use PostgreSQL 16 in Docker, never a local server. The target is 16, and local installs are often newer.
- The bank's YAML is read with YAML 1.2 core rules: `yes` and `no` stay strings, but an unquoted `10.0` becomes a number and fails the schema. Quote versions: `dotnet: "10.0"`.
- The claude.ai daily run downloads the repo tarball from `codeload.github.com` (`github.io` is blocked there), so question files must be usable straight from the repo, without a build step.
- Package versions live in `tools/Directory.Packages.props` (central package management). Project files reference packages without a version.
