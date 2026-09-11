---
title: "ADR 0008: Versioned SQL is the schema source of truth"
status: accepted
language: en
date: 2026-08-31
updated: 2026-09-12
related:
  - ../database-design.md
  - ../architecture.md
  - 0002-use-mssql-with-aspire.md
---

# ADR 0008: Versioned SQL is the schema source of truth

Status: accepted

## Context

EF code-first plus daily `EnsureDeleted` / `EnsureCreated` has no auditable history. Constraints drift. Phase 1 also has to land Identity and OpenIddict tables.

## Decision

- `database/schema/NNNN_*.sql` is the only schema source of truth, including Identity user tables and OpenIddict package tables.
- `SchemaVersions` records applied scripts.
- Startup applies scripts that are not yet recorded. The default path **does not drop the database**. Local reset is an explicit command: drop → scripts → seed.
- The applicator runs **once** (default: `webapi` startup, or an explicit apply step). `identity` must not run a second applicator concurrently.
- Domain entities are hand-written. Do not scaffold into Domain. EF Fluent API maps only. **Do not generate EF migrations.**
- Identity password seed goes through `UserManager`. OpenIddict clients may be inserted in SQL or in a startup seed.
- Phase 1 script order: `0001` SchemaVersions → `0002` Currencies → `0003` Identity → `0004` OpenIddict → `0005` Tenants → `0006` Users → `0007` UserTokens. No ledger tables, no custom RefreshTokens table, no AspNetRoles.

## Alternatives considered

| Option | Why not |
| --- | --- |
| Keep `EnsureCreated` | No evolution history |
| EF migrations as the source of truth | Weaker “database is truth” than versioned SQL |
| Scaffold Domain from the database | Breaks Clean Architecture |
| Each host runs the applicator | Race |

## Consequences

Column changes are SQL scripts. CI must apply the full script set on an empty database and run mapping / FK tests against it.
