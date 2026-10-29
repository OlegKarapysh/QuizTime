@AGENTS.md

## Claude Code

- Commit with the `small-commits` skill (`.claude/skills/small-commits/`). It overrides the default attribution trailer.
- `.claude/settings.json` holds shared permissions and a PostToolUse hook that fixes whitespace in each `.cs` file after Edit or Write. It only formats; other problems surface in `tools/verify`. Personal overrides go in `.claude/settings.local.json`, which is git-ignored.
- Skills live in `.claude/skills/<name>/SKILL.md`, subagents in `.claude/agents/<name>.md`. A subagent without a `tools:` line inherits every tool, so the blind-solver must restrict its tools explicitly. Confirm with a test run that it can't read `questions/`.
- The desktop app creates worktrees under `.claude/worktrees/`. They're git-ignored.
- Use subagents for wide searches across the bank, so the main context keeps conclusions, not file dumps.
