---
title: Documentation
status: draft
language: en
created: 2026-09-05
updated: 2026-09-12
---

# MyWealthV2 documentation

This folder is the design source of truth for MyWealthV2.

Write English only. Use the terms in [glossary.md](glossary.md). If a new word appears in a spec, add it to the glossary in the same change.

Coding and commit rules for agents belong in the repository-root `AGENTS.md`, not here.

---

## Current phase

**Phase 1 — platform foundation.** Tenants, identity/session (OpenIddict), four roles, people, currency catalog, Adviser Portal shell.

Do not add Phase-2 ledger tables, routes, or handlers (instruments, accounts, holdings, transactions, net worth) until that phase is opened.

---

## Once, not twice

MyWealthV2 is a learning platform that should still look like the later product.

**Build the final infrastructure now** when both are true: a later phase will use it, **and** keeping today’s design would force a change when that phase opens. One caller, or no product UI yet, is not a reason to ship a throwaway.

**Wait** when later use is not decided, or the later shape is still open. Do not invent hosts, tables, or ports to look complete.

Ship now (certain use + today’s alternative would be rewritten):

- OpenIddict + hosted login on Aspire `identity` — a private `/auth/login`, or OpenIddict inside `webapi`, would be moved or deleted later
- Customer as a login principal — a person-without-identity model would be rewritten
- Full `UserStatus` plus `UserTokens` / `IEmailSender` — a boolean `IsEnabled` would be rewritten for invite
- `Money` in Domain with no balance column yet — the ledger must not invent a second amount type

Wait (use or shape not locked):

- A second OIDC client and Customer Portal — the server shape is already final; registering the client is that slice
- A separate identity SQL database — a database split is not decided
- `IMarketData` / `IFxRate` and ledger tables — Phase 2 will need them, but port and storage shapes are not locked
- Empty Journal / CashLedger tables

Behaviour that ships still belongs only in the current phase.

---

## Read in this order

1. This file — how docs work and what “current” means
2. [Glossary](glossary.md) — shared words
3. [Function plan](function-plan.md) — what ships, who uses it, which phase
4. The Feature Spec **or** portal spec for the slice being built
5. Then only if needed: architecture, domain model, database design, API design, ADRs

Implement **accepted** specs for the current phase only. `draft` and `note` are not build contracts.

---

## What each kind of doc owns

| Doc | Owns | Does not own |
| --- | --- | --- |
| [function-plan.md](function-plan.md) | Scope, roles, phase map | Field-level contracts |
| [glossary.md](glossary.md) | Names | Design arguments |
| [architecture.md](architecture.md) | Host, layers, ports, cross-cutting | Per-slice commands |
| [domain-model.md](domain-model.md) | Accepted aggregates and invariants | Unlocked future shapes |
| [database-design.md](database-design.md) | Accepted tables | Scripts themselves (`database/schema/`) |
| [api-design.md](api-design.md) | HTTP conventions and the resource catalog | Full request/response per action |
| [features/](features/README.md) | One vertical slice: commands, rules, endpoints, tests | Other slices’ APIs |
| [adr/](adr/README.md) | A decision that must not be re-litigated in place | Feature scope |
| [portals/](portals/) | Adviser Portal scope, UI conventions, page construction notes | Backend handlers |

Schema truth is the versioned SQL under `database/schema/`, applied by the schema applicator. Do not treat `EnsureCreated` / `EnsureDeleted` as the normal boot path.

---

## Status values

Use the same `status` on every doc front matter:

| Status | Meaning |
| --- | --- |
| `draft` | In progress. Not implementable. |
| `review` | Ready for feedback. |
| `accepted` | Agreed. Implementation follows this. |
| `deprecated` | Superseded. Leave in place and link to the replacement. |
| `note` | Discussion only. Not a slice contract. |

---

## Conventions

- Prefer one concrete decision over a list of options. Rejected options go in an ADR.
- Keep a mermaid diagram in the one doc that owns the concept. Do not copy the same ER diagram across files.
- When code and an accepted doc disagree, change the code or the doc in the same change.
- Feature Spec **In** scope is only what that slice ships. Later-phase words may appear as out-of-scope names, not as tables to create.
- `api-design.md` is conventions plus a resource catalog. Field rules live in the Feature Spec.
- File names are kebab-case. Specs: `features/<name>.md`. ADRs: `adr/NNNN-short-title.md`. Portal specs: `portals/<name>.md`.
- Do not create empty spec files for phases that have not opened.
