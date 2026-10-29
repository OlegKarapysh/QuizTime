#!/usr/bin/env bash
# Builds a repo where getUser is defined once and called from 19 files, commits it, then
# renames getUser to fetchUser everywhere: one logical change touching 20 files.
set -eu
f() { mkdir -p "$(dirname "$1")"; printf '%s\n' "$2" > "$1"; }

git init -q -b main .
git config user.name "Test User"
git config user.email "test@example.com"

callers="src/api/orders.ts src/api/invoices.ts src/api/profile.ts src/api/admin.ts src/api/search.ts
src/web/header.ts src/web/account.ts src/web/checkout.ts src/web/settings.ts src/web/orders-page.ts
src/jobs/digest.ts src/jobs/cleanup.ts src/jobs/reminders.ts src/jobs/export.ts
tests/api/orders.test.ts tests/api/profile.test.ts tests/web/account.test.ts tests/jobs/digest.test.ts tests/jobs/export.test.ts"

write_all() {
  local name=$1
  f src/api/users.ts "export const ${name} = async (id: string) => ({ id, name: \"Ada\" });"
  for c in $callers; do
    f "$c" "import { ${name} } from \"$(echo "$c" | sed -E 's#[^/]+/#../#g; s#[^/]+$##')src/api/users\";
export const run = async (id: string) => (await ${name}(id)).name;"
  done
}

write_all getUser
f README.md '# Accounts'
git add -A && git commit -q -m "Initial commit"

write_all fetchUser
