---
title: Documentation
status: draft
language: en
created: 2026-09-05
updated: 2026-09-20
---

# MyWealthV2 documentation

This folder is the design source of truth for MyWealthV2.

Write English only. Use the terms in [glossary.md](glossary.md). If a new word appears in a spec, add it to the glossary in the same change. Do not add a parallel Chinese draft tree or `v2draft/` pointers.

Coding and commit rules for agents belong in the repository-root `AGENTS.md`, not here.

---

## Current phase

**Phase 2 — ledger domain.** Opened 2026-09-15. One domain, several slices.

Phase 1 platform contracts stay accepted. Do not reopen them. Do not ship a single `Transactions` table that pretends to be both cash and securities.

**Feature map:** [function-plan.md](function-plan.md) §5. First accepted slice: [features/instruments.md](features/instruments.md) (landed `9ea2f2a`; IsActive rename `bbd0f26`). Accounts: [features/accounts.md](features/accounts.md) (`accepted`, not landed). Write Feature Specs one slice at a time. Do not lock columns outside the spec that owns them.

Suggested internal order (tendency, not a locked backlog): instruments → account container → cash ledger → securities / holdings → posting / reversal / Opening → net-worth read model + Dashboard.

### Phase 2 working mode

Phase 1 production code was largely written by the build CLI. Phase 2 is not that loop.

1. **Discuss features** (high aspect first). Do not implement from a chat summary.
2. **Write docs** here (`docs/function-plan.md`, then Feature Specs / ADR expansions). `draft` / `review` are not build contracts. `accepted` is.
3. **CLI writes tests** in the implementation repo from the accepted spec (red).
4. **Human writes most production code.** An agent implements a slice only when asked.

Table and port shape is locked in that slice’s Feature Spec, not in ADR 0010 / 0011 alone. Those ADRs are direction; reopen and expand them when the spec is written.

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
- Account / cash / holdings / posting table shape — ports `IMarketData` / `IFxRate` are locked with instruments; other ledger tables wait for their spec
- Empty Journal / CashLedger / Accounts tables before that spec is `accepted`

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
