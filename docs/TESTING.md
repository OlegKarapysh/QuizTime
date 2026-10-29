# Testing and verification strategy

Two different things have to be correct here, and they're verified in different ways:

- **Code:** the tools, the check harness and the app. Verified by tests, the compiler (warnings are errors) and formatting checks.
- **Content:** each question's marked answer. Verified by the five gates in the PLAN.md section "Verification pipeline", and re-checked every week after merge.

A bug in the code weakens the content without anyone noticing: a validator that misses a rule lets bad questions through. So tool tests are held to a higher bar than usual. Every rule must be shown to fire.

## Where each check runs

| Check | Local | pre-commit | PR CI | `main` CI | Weekly | Available |
| --- | --- | --- | --- | --- | --- | --- |
| `dotnet format` (tools and checks) | `tools/verify` | | ✓ | ✓ | | now |
| Build with warnings as errors, tool tests | `tools/verify` | | ✓ | ✓ | | now |
| Gate 1: schemas and cross-references | `tools/validate` | ✓ | ✓ | ✓ | | now |
| actionlint | `tools/verify` if installed | | ✓ | ✓ | | now |
| Gate 1: lint rules (option lengths, pinned docs, denylist) | `tools/validate` | ✓ | ✓, without denylist | ✓, without denylist | | now |
| Gate 2: run checks | `tools/run-checks` | | changed questions | all | all | now |
| Link check | `tools/check-links` | | changed questions | all | all | now |
| Gates 3–5: blind solve, audit, fix loop | write-quiz-batch skill | | | | | milestone 3 |
| Gate calibration | write-quiz-batch skill | | | | | milestone 3 |

CI's verify job runs `tools/verify`; its checks job runs `tools/run-checks` and `tools/check-links`. Those two stay out of `tools/verify` because they need Docker and the network. PLAN.md puts the lint rules in gate 1, so they belong in `tools/validate` and run wherever it runs. The denylist needs `private/`, which CI can't see, so it only runs locally: in the pre-commit hook and in the batch skill. The hook validates a copy of the staged files, so the `tools/validate` wrapper passes `--denylist` with the real repo root's `private/denylist.txt`. Without that file, validate says so in its summary line. When a tool lands, wire it into `tools/verify` (if it's offline and fast) or the checks job, the hook if it's fast, and CI in the same PR, and update this table.

"Changed questions" means `--changed-since <PR base>`: questions whose YAML file or `checks/<id>/` folder differs from the merge base. A change under `tools/` (except `tools/QuizBank.Tests/`) or to `global.json` can change any result, so it selects every question.

## Testing the tools

xunit v3 on Microsoft.Testing.Platform, in `tools/QuizBank.Tests`. Run all of them with `dotnet test --solution QuizTime.slnx`, or one with `--filter-method "*Test_name"` (see AGENTS.md).

**Break one thing in a throwaway bank.** `TestRepo` writes the real schemas plus a small valid roadmap, concept file, question and check to a temp folder. A test changes one thing with `Replace`, `Move`, `Write` or `Delete`, runs the validator, and asserts the exact message and the line it points to. `TestRepo` is the shared fixture, so new tests don't need fixture files.

- Every rule gets at least one test that makes it fire, and the sample bank must stay clean (`Sample_repo_is_valid`). Write that test first, watch it fail, then implement the rule. A rule with no failing test is unproven.
- Assert the full message and its location, not just "some error". Messages are part of the interface: agents read them and fix files from them.
- Test the commands through `Cli.RunAsync` with string writers: exit codes, the summary line, and usage errors.
- Keep one test against the real repo (`Real_repo_is_valid`), so a bad commit to the bank fails the tests too.
- Test names read as sentences: `Unknown_node_is_reported`, `Bad_usage_exits_with_2`. Group related tests with a `// Questions: references` style comment.

**No network in tests.** The link checker gets a fake `HttpMessageHandler` and a delay function that doesn't sleep (`LinkCheckerTests`). Real link checks run only in the CI jobs meant for them.

**Check harness tests** run checks for real. `CheckRunnerTests` runs C# checks with `dotnet run` (a pass, a FAIL line, a compile error, a crash, no PASS line); they need only the .NET SDK. `DatabaseCheckTests` runs SQL checks and a C# database check against PostgreSQL 16, through `PostgresFixture`, which starts the server the way run-checks does: `QUIZTIME_PG` if set, else a `postgres:16.15` container. Without Docker those tests skip and show why. In CI they must never skip: the verify job sets `QUIZTIME_REQUIRE_DOCKER=1`, and then a missing Docker fails them, so a broken harness can't pass as skipped.

## Verifying questions

The gates, in short. The full rules are in PLAN.md.

1. **Schema and lint** (`tools/validate`).
2. **Run it** (`tools/run-checks`): `checks/<id>/check.cs` or `check.sql` asserts the marked answer.
3. **Blind solve** (`blind-solver` subagent): no tools, a different model from the writer, never sees the answer.
4. **Adversarial audit** (`adversarial-reviewer` subagent): tries to break the question and opens every source.
5. **Fix loop:** at most two rounds. Anything still failing is dropped and listed in the PR.

Rules for agents running the gates:

- Run them in order. A question that fails gate N doesn't go to gate N+1 until it's fixed.
- Record each result in the question's `verification` block from the gate's actual output, with the date. Never record a result you didn't observe.
- **Prove a check can fail.** Before committing a new check, change its marked answer (for a SQL check, put a wrong option's claim in the expected block), confirm `tools/run-checks <id>` prints `FAIL`, then change it back. A check that passes no matter what proves nothing.
- `method: docs` questions have no runnable proof. Gate 4 treats them with extra suspicion, and the PR lists them.

### Gate calibration

The gates are prompts and code, and both drift. Milestone 3 plants three faulty questions: a wrong marked answer, two defensible answers, and a false sentence in an explanation. Keep them in a top-level `calibration/` folder laid out like the bank (`calibration/questions/`, `calibration/checks/`). That keeps them out of the pool in `questions/`, and out of `tools/`, where the test project would compile their checks and `tools/Directory.*.props` would break them as file-based apps.

Re-run calibration whenever `SKILL.md`, a subagent prompt or the model behind a gate changes. Each planted fault must still be caught, and the run report names the gate that caught it. Put the results in the PR.

## Verifying your change

`tools/verify` is necessary, not sufficient. Also do the check for what you changed:

| You changed | Also do |
| --- | --- |
| A tool | Run it on the real bank and read the output. For a new rule, show it firing in its test. |
| A question or check | Run `tools/validate`, `tools/run-checks <id>` and `tools/check-links <id>`, prove the check can fail, then run gates 3–4 through the skill. |
| A schema | Run `tools/validate` on the whole bank, and add tests for the constraint in both directions. |
| A workflow | Push the branch and watch the run with `gh run watch`. A workflow isn't verified until it has run. |
| A skill or subagent prompt | Run gate calibration. |
| The app | Build the JSON, open the page in a browser, take one node quiz end to end, reload, and confirm the result persisted. |
| Docs only | Check that every command and path you mention exists. |

Say in the PR what you ran and what you couldn't run (for example, Docker unavailable). "Should work" isn't verification.

## CI

- **`ci.yml`** (now): on PRs, pushes to `main` and manual runs. The `verify` job runs `tools/verify` on the SDK from `global.json`, with `QUIZTIME_REQUIRE_DOCKER=1`. The `checks` job runs `tools/run-checks` and `tools/check-links`: on PRs with `--changed-since` the PR's base commit, on `main` and manual runs for every question. run-checks starts its own `postgres:16.15` container, so CI and local runs use the same server; the link check runs even when a check failed. A `workflows` job runs actionlint. A new push to a PR cancels the PR's older run; runs on `main` always finish.
- **`weekly-recheck.yml`** comes once run-checks and the link checker exist; PLAN.md doesn't tie it to a milestone. It runs every check and link and, on failure, opens an issue listing the questions to quarantine.
- **Milestone 5** adds `pages.yml`: build JSON from reviewed questions and deploy the app.

## Flaky checks

- A flaky check is a bug. Don't re-run until green. Find the nondeterminism (time, ordering, network) and remove it.
- The link checker retries transient errors (timeouts, 429, 5xx) with backoff before failing. A 404 or a missing anchor fails at once.
