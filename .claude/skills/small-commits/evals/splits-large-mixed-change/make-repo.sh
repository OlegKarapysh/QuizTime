#!/usr/bin/env bash
# Builds a small TypeScript shop repo with one base commit, then leaves ~29 uncommitted
# files across two features, shared utilities, docs and config.
set -eu
f() { mkdir -p "$(dirname "$1")"; printf '%s\n' "$2" > "$1"; }

git init -q -b main .
git config user.name "Test User"
git config user.email "test@example.com"

f package.json '{ "name": "shop", "version": "0.1.0", "scripts": { "test": "vitest" } }'
f README.md '# Shop

A small order-taking service.'
f .gitignore 'node_modules/'
f src/index.ts 'export const version = "0.1.0";'
git add -A && git commit -q -m "Initial commit"

# shared utilities
f src/shared/money.ts 'export type Money = { cents: number; currency: string };
export const add = (a: Money, b: Money): Money => ({ cents: a.cents + b.cents, currency: a.currency });'
f src/shared/ids.ts 'export const newId = (prefix: string) => `${prefix}_${crypto.randomUUID()}`;'
f src/shared/errors.ts 'export class DomainError extends Error {}'
f src/shared/dates.ts 'export const todayUtc = () => new Date().toISOString().slice(0, 10);'
f tests/shared/money.test.ts 'import { add } from "../../src/shared/money";
test("adds", () => expect(add({ cents: 1, currency: "EUR" }, { cents: 2, currency: "EUR" }).cents).toBe(3));'
f tests/shared/dates.test.ts 'import { todayUtc } from "../../src/shared/dates";
test("iso date", () => expect(todayUtc()).toMatch(/^\d{4}-\d{2}-\d{2}$/));'

# orders feature
f src/orders/model.ts 'import { Money } from "../shared/money";
export type Order = { id: string; lines: { sku: string; qty: number; price: Money }[] };'
f src/orders/repository.ts 'import { Order } from "./model";
const orders = new Map<string, Order>();
export const save = (o: Order) => orders.set(o.id, o);
export const find = (id: string) => orders.get(id);'
f src/orders/validation.ts 'import { Order } from "./model";
import { DomainError } from "../shared/errors";
export const validate = (o: Order) => { if (o.lines.some(l => l.qty <= 0)) throw new DomainError("qty"); };'
f src/orders/service.ts 'import { newId } from "../shared/ids";
import { save } from "./repository";
import { validate } from "./validation";
export const place = (lines: any[]) => { const o = { id: newId("ord"), lines }; validate(o); save(o); return o; };'
f src/orders/index.ts 'export * from "./service";'
f tests/orders/model.test.ts 'test("model compiles", () => expect(true).toBe(true));'
f tests/orders/validation.test.ts 'import { validate } from "../../src/orders/validation";
test("rejects zero qty", () => expect(() => validate({ id: "1", lines: [{ sku: "a", qty: 0, price: { cents: 1, currency: "EUR" } }] })).toThrow());'
f tests/orders/service.test.ts 'import { place } from "../../src/orders/service";
test("places", () => expect(place([]).id).toMatch(/^ord_/));'

# billing feature
f src/billing/tax.ts 'export const vat = (cents: number, rate = 0.2) => Math.round(cents * rate);'
f src/billing/invoice.ts 'import { vat } from "./tax";
export const invoiceTotal = (net: number) => net + vat(net);'
f src/billing/index.ts 'export * from "./invoice";'
f tests/billing/tax.test.ts 'import { vat } from "../../src/billing/tax";
test("vat", () => expect(vat(100)).toBe(20));'
f tests/billing/invoice.test.ts 'import { invoiceTotal } from "../../src/billing/invoice";
test("total", () => expect(invoiceTotal(100)).toBe(120));'

# wiring
f src/index.ts 'export * as orders from "./orders";
export * as billing from "./billing";
export const version = "0.2.0";'

# docs
f docs/orders.md '# Orders

Orders are validated before they are saved.'
f docs/billing.md '# Billing

Invoices add 20% VAT by default.'
f docs/architecture.md '# Architecture

Features live in src/<feature>; shared code in src/shared.'
f README.md '# Shop

A small order-taking service. See docs/ for orders, billing and architecture.'

# config
f package.json '{ "name": "shop", "version": "0.2.0", "scripts": { "test": "vitest", "lint": "eslint src" } }'
f tsconfig.json '{ "compilerOptions": { "strict": true, "target": "ES2022" } }'
f .eslintrc.json '{ "extends": ["eslint:recommended"] }'
f .gitignore 'node_modules/
dist/'
