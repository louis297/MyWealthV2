---
title: "ADR 0002: Use SQL Server hosted by Aspire"
status: accepted
language: en
date: 2026-08-18
updated: 2026-09-12
related:
  - 0008-schema-sql-as-source-of-truth.md
  - ../database-design.md
  - ../architecture.md
---

# ADR 0002: Use SQL Server hosted by Aspire

Status: accepted

How schema is applied is ADR 0008.

## Context

Amounts must be exact. The later ledger needs transactions. Local development must start the database with one command under Aspire.

## Decision

- Engine: Microsoft SQL Server. Container resource name: `dbserver` (`RunAsContainer`).
- Database name and Aspire connection-string name: **`MyWealthDbV2`**. Injected into both `identity` and `webapi`.
- Data access is EF Core 10 `UseSqlServer`: **mapping only**. EF does not own the schema.
- Phase 1 does **not** split a separate identity database.

## Alternatives considered

| Option | Why not (Phase 1) |
| --- | --- |
| PostgreSQL | The team is more familiar with SQL Server |
| SQLite | Weak fit for multi-tenant concurrency |
| A separate identity database in Phase 1 | Dual-write and revocation become distributed; a second portal does not require the split |

## Consequences

`decimal`, transactions, and a native Aspire resource. Changing engines later is expensive. Schema evolution follows ADR 0008.
