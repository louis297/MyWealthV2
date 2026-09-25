---
title: "ADR 0015: Idempotency-Key on mutating creates"
status: accepted
phase: 2
language: en
date: 2026-09-25
updated: 2026-09-25
related:
  - ../architecture.md
  - ../api-design.md
  - 0005-shared-database-tenantid-isolation.md
  - 0008-schema-sql-as-source-of-truth.md
  - ../features/posting.md
---

# ADR 0015: Idempotency-Key on mutating creates

Status: accepted (first consumer: posting / `/transactions`)

Cross-cutting HTTP + storage. First consumer is posting create / reverse. Do not invent a second mechanism per Feature Spec.

## Context

A retried `POST` that inserts a cash journal would change `SUM`. Close / reopen / disable / enable are already state-idempotent (already in the target state → 204). Create-style posts are not.

The key must not live only on `Postings`. People creates and later imports will want the same header.

## Decision

1. Clients send `Idempotency-Key` (UUID) on endpoints this ADR lists. The key is a header, not a body field, and is not the resource `PublicId`.
2. Storage is a dedicated table (script when the first consumer is accepted), not a column on each business table.
3. Scope: unique per tenant (`TenantId`). SystemAdmin uses the target tenant of the command. Two callers in the same tenant who reuse a key collide.
4. Same tenant + same key + same request hash → return the **original** status and body. Do not insert a second business row.
5. Same tenant + same key + different hash → **409**.
6. Missing key on a listed endpoint → **400**.
7. Keys persist. Do not expire a ledger key after 24 hours and allow a second insert.
8. First listed endpoints: `POST /transactions`, `POST /transactions/{id}/reverse`.
9. Not listed: GET; `POST …/close` / `reopen` / `disable` / `enable` (state-idempotent). Phase 1 people creates stay unlisted until a later spec opts in.

Request hash covers the business body (and the path / method). Header order and Bearer token are not part of the hash.

## Alternatives considered

| Option | Why not |
| --- | --- |
| Only a unique `reference` on `Transactions` | Import id and retry key are different; reverse has no natural reference |
| Use `PublicId` supplied by the client as the key | Mixes identity with retries; conflicts with server-issued PublicId |
| Per-slice middleware copies | Two tables, two header names |
| Short TTL | A delayed retry after expiry double-posts cash |
| Optional key | Callers that omit it still double-post; the dangerous endpoints must require it |

## Consequences

Posting stores keys in `IdempotencyRecords`, created by `0013_transactions.sql` with the business tables. Later opted-in creates reuse that table. Functional tests cover replay and hash mismatch.

Portal and Scalar callers must send the header. Construction notes should show a generated UUID per user submit, reused only on retry of that submit.

## Links

- Feature specs: [features/posting.md](../features/posting.md)
- Related ADRs: 0005 (tenant scope), 0008 (SQL script)
---
