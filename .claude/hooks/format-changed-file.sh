#!/usr/bin/env bash
# PostToolUse hook (Edit|Write): fix whitespace in the C# file Claude just changed, so diffs stay
# clean and tools/verify doesn't fail on layout. Whitespace only: style fixes such as removing a
# using that isn't used yet would fight multi-step edits. Never blocks the tool call.
set -uo pipefail

file=$(jq -r '.tool_input.file_path // empty' 2>/dev/null) || exit 0
[[ "$file" == *.cs && -f "$file" ]] || exit 0
command -v dotnet >/dev/null || exit 0

dotnet format whitespace "$(dirname "$file")" --folder --include "$file" >/dev/null 2>&1
exit 0
