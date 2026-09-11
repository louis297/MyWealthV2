---
title: "ADR 0007: Internal int primary key plus external PublicId"
status: accepted
language: en
date: 2026-08-18
updated: 2026-09-12
related:
  - ../database-design.md
  - ../api-design.md
---

# ADR 0007: Internal int primary key plus external PublicId

Status: accepted

Internal keys are `int` identity. External identifiers are UUID `PublicId` values.

## Context

Business rows need a stable internal key (FK, clustered index, EF) and a stable external contract (API, UI, JWT). A database identity `int` fits the first. A UUID clustered primary key scatters inserts.

“External” means any number that leaves the in-process object graph and appears in HTTP, the frontend, or a JWT. The Adviser Portal counts as external.

## Decision

1. `BaseEntity.Id` remains a database-generated `int` identity. It is the **only primary key and clustered key**. Business foreign keys point at this int.
2. Every resource that is addressed on its own in the API also has `PublicId uniqueidentifier NOT NULL`, unique, **not** the primary key.
3. HTTP paths, JSON, and the JWT subject use **PublicId only**. Handlers translate to int. Cross-tenant or missing → 404.
4. **Phase 1 tables with PublicId are only `Tenants` and `Users`.** Ledger tables add PublicId when that phase opens. Do not reserve empty columns in Phase 1.
5. No PublicId: `Currencies` (natural key `Code`), `SchemaVersions`, `UserTokens` (callers hold the raw token; the store holds the hash), Identity string keys, OpenIddict package keys.
6. People type the tenant **Code** at login, not the tenant UUID.
7. No composite primary keys. No UUID clustered primary keys.

## Alternatives considered

| Option | Why not |
| --- | --- |
| UUID primary key on every table | Wider indexes and more insert fragmentation |
| Expose identity ints in the API | The contract is welded to an internal number |
| Composite `(TenantId, Id)` primary key | Dual check already exists; FK / EF cost is high |
| PublicId columns on ledger tables in Phase 1 | Those tables do not exist yet |

## Consequences

The internal model stays `int`. The external contract can stay stable. Every resource endpoint must translate PublicId → int.
