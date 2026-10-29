#!/usr/bin/env bash
# Builds a small repo with one base commit, then leaves a 3-file bug fix uncommitted.
set -eu
f() { mkdir -p "$(dirname "$1")"; printf '%s\n' "$2" > "$1"; }

git init -q -b main .
git config user.name "Test User"
git config user.email "test@example.com"

f src/dates.ts 'export const parseDate = (s: string) => new Date(s + "T00:00:00");'
f tests/dates.test.ts 'import { parseDate } from "../src/dates";
test("parses", () => expect(parseDate("2026-01-02").getDate()).toBe(2));'
f CHANGELOG.md '# Changelog

## Unreleased'
f README.md '# Calendar utils'
git add -A && git commit -q -m "Initial commit"

f src/dates.ts 'export const parseDate = (s: string) => new Date(s + "T00:00:00Z");'
f tests/dates.test.ts 'import { parseDate } from "../src/dates";
test("parses as UTC", () => expect(parseDate("2026-01-02").toISOString()).toBe("2026-01-02T00:00:00.000Z"));'
f CHANGELOG.md '# Changelog

## Unreleased

- parseDate now reads dates as UTC instead of local time.'
