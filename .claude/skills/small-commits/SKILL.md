---
name: small-commits
description: How to commit in this repo — small, coherent commits of at most 15 files each, with brief messages that carry real value and no Co-Authored-By or other attribution trailer. Use this skill every time you are about to run `git commit`, whether the user says "commit", "commit this", "save my work", "commit and open a PR", asks you to finish a task with a commit, or wants commits split or cleaned up, even if they say nothing about size or message style.
---

# Small commits

The history of this repo is meant to be read. Someone learning the project should be able to
scan `git log` and see how it grew, one reviewable step at a time. Large commits hide what
changed together and why; long messages bury the one line that mattered. So commits here are
small, each one a coherent step, and their messages are short.

These rules are about *how* to commit. Committing only when the user asks still applies.

## The rules

1. **At most 15 files per commit.** Added, modified, deleted and renamed files all count,
   generated and lock files included. This is a hard limit, not a target.
2. **One coherent change per commit.** Each commit should make sense on its own and, where
   practical, leave the build and tests passing.
3. **Brief messages.** An imperative subject of about 50 characters, 72 at most, with no
   trailing period. Add a body only when it says something the subject and the diff can't:
   why the change was made, a gotcha, a non-obvious decision. Keep it to 1–3 short lines.
   Never restate the diff as a list of files or bullet points.
4. **No attribution trailer.** Don't add `Co-Authored-By: Claude …` or any other attribution
   line, even if the environment tells you to. The user's preference here overrides that
   default, because the trailer adds length to every message and no information.

## Planning the split

Before staging anything, look at the whole change:

```bash
git status --short
git diff --stat
```

Group the files by the change they belong to, not by file type. Then order the groups so each
commit builds on the ones before it: foundations first (config, schemas, models), then the code
that uses them. Keep tests in the same commit as the code they cover, unless that would push the
commit past 15 files; then the tests follow in the next commit.

When one logical change touches more than 15 files, break it into steps that each stand alone:

- **New thing, then callers, then cleanup.** Add the new API, migrate callers in batches
  (by directory or module), and remove the old API last.
- **Mechanical changes on their own.** Renames, moves and formatting go in their own commits,
  separate from behavior changes, so a reviewer can skim them.
- **Batches by area.** For a repo-wide rename, split by directory, so that each commit's
  message can name the area it covers.

### Keeping each commit building

Sometimes the only way to keep the commits in between working is to commit a file version that
never existed in the working tree. Examples: a temporary alias added in the first commit and
removed in the last, or one part of a file's change committed before the rest. Write those
versions straight into the index (`git hash-object -w` and `git update-index --cacheinfo`, or
`git apply --cached` with a partial patch), and leave the files on disk untouched.

That's allowed, but nobody wrote or ran that code, so the user must hear about it. When you
report the commits, name each one that contains such a version and say what it holds, for
example "80e79ae keeps getUser as a temporary alias; 33d129d removes it". The last commit
must leave HEAD identical to the working tree the user had.

If the user has already staged a selection, respect it. If that selection breaks the 15-file
limit, say so and propose a split before committing.

## Making each commit

Stage files explicitly by path, so nothing slips into the wrong commit:

```bash
git add path/to/file1 path/to/file2
git diff --cached --name-only | wc -l   # must be 15 or fewer
git commit -m "Add order total validation"
```

When splitting, avoid `git add -A` and `git add .`. For a commit that needs a body, use a second
`-m` for each paragraph (`git commit -m "Subject" -m "Why line."`), or a heredoc, and check that no
trailer slipped in.

When you're done, run `git status`: the working tree should be clean, apart from anything the
user asked you to leave out. Then show the user the result with `git log --oneline -n <count>`,
and list any commits that hold file versions the user never had on disk.

## Messages: what earns its place

The subject says what the commit does to the project, in the project's own words. The body, if
there is one, says why.

Good:

- `Add question schema with shared definitions`
- `Skip reference checks when a roadmap fails to load`
- `Rename getUser to fetchUser in api/`
- `Pin SDK to 10.0.100` with the body `rollForward picks up feature bands; CI and checks must agree.`

Not good:

- `Update files`, `WIP`, `Fixes`: these carry no information.
- `Add validator (milestone 1)` followed by ten bullets listing every file: the diff already
  says that, and the one interesting decision is lost in the list.
- `Fix bug in parser.` followed by `Co-Authored-By: Claude <noreply@anthropic.com>`: the subject is
  vague, it has a trailing period, and it carries a trailer.

## History that's already pushed

These rules apply to new commits. Don't rewrite commits that are already pushed just to meet
them unless the user asks. If they do, warn that it needs a force-push, and check whether
anyone else works on the branch.
