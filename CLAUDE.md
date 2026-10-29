@AGENTS.md

## Claude Code

- Commit with the `small-commits` skill (`.claude/skills/small-commits/`). It overrides the default attribution trailer.
- `.claude/settings.json` holds shared permissions and a PostToolUse hook that fixes whitespace in each `.cs` file after Edit or Write. It only formats; other problems surface in `tools/verify`. Personal overrides go in `.claude/settings.local.json`, which is git-ignored.
- Skills live in `.claude/skills/<name>/SKILL.md`, subagents in `.claude/agents/<name>.md`.
- The blind-solver needs care: a subagent without a `tools:` line inherits every tool. Restrict it in the frontmatter so it has no file, search, shell, web or MCP tools: allow only harmless tools with `tools:`, or block the rest with `disallowedTools:`. Set `model:` to a different model from the writer's. Subagents also load the project's CLAUDE.md and AGENTS.md. That's harmless as long as neither contains answers, so never put question content in them. Before trusting the solver, confirm with a test run that it can't read `questions/`.
- The desktop app creates worktrees under `.claude/worktrees/`. They're git-ignored.
- Use subagents for wide searches across the bank, so the main context keeps conclusions, not file dumps.
