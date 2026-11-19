---
title: "ADR 0008: Versioned SQL is the schema source of truth"
status: accepted
language: en
date: 2026-08-31
updated: 2026-11-19
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

## Amendment 2026-11-19: `worker` also applies the scripts

Accepted. Records the code as it is. Do not rewrite the decision above in place; this amendment replaces "the applicator runs **once** (default: `webapi` startup …)" for the `worker`.

- Since `26fb489` (2026-11-09) `src/Worker/Program.cs` runs the same applicator at start-up, before it loads the currency catalog and registers the recurring jobs. Reason: under the Development AppHost the `worker` waits only for the database server, not for `webapi`, and on a first start it could read tables that did not exist yet and exit.
- Two hosts now apply `database/schema/`: `webapi` and `worker`. `identity` still does not. Both start listening only after the applicator has finished.
- The applicator takes no lock. When both hosts start together on an empty database (a first Development start), they can run the same pending script at once, and one of them fails at start-up. A second start finds the scripts applied. On the VPS the compose start order (`webapi` healthy before `worker`) means the `worker` finds nothing pending. See [known-issues/schema-applicator-two-hosts.md](../known-issues/schema-applicator-two-hosts.md).
- The alternative "each host runs the applicator → race" stays rejected as a general rule: a new host does not add an applicator for `MyWealthDbV2`.
