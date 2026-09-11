---
title: "ADR 0005: Shared database plus row-level TenantId"
status: accepted
language: en
date: 2026-08-18
updated: 2026-09-12
related:
  - ../database-design.md
  - ../architecture.md
  - ../function-plan.md
---

# ADR 0005: Shared database plus row-level TenantId

Status: accepted

Phase-1 isolation: one shared database + row-level `TenantId` + **dual check** + isolation tests.

## Context

A SaaS product must isolate tenants. An EF global query filter plus a JWT claim is not enough: missing the filter once leaks rows across tenants.

## Decision

Phase 1 keeps **one shared database (`MyWealthDbV2`) and row-level `TenantId`**, with:

1. Business tables have a **database FK** `TenantId` → `Tenants` (`Users.TenantId` is null only for SystemAdmin).
2. `AspNetUsers.TenantId` is a projection column, nullable, **with no FK** (avoids a cycle with `Users.IdentityUserId` → `AspNetUsers`).
3. Login carries **tenantCode** (no subdomain routing). The JWT carries tenant PublicId and tenantCode. Repositories / handlers **tighten to the current tenant again**. Do not trust the filter alone.
4. Email is unique **inside a tenant**. SystemAdmin email is unique globally.
5. From the Tenants slice onward, cross-tenant read/write tests are a **CI gate** on every business slice.
6. Phase 1 does not use SQL RLS, database-per-tenant, or schema-per-tenant.

## Alternatives considered

| Option | Why not (Phase 1) |
| --- | --- |
| Schema-per-tenant / database-per-tenant | Too heavy for Aspire + scripts + tests |
| Filter only | Too thin |
| An FK on `AspNetUsers.TenantId` | Cycles with `Users.IdentityUserId` |
| Subdomain routing in Phase 1 | Not locked; `Tenant.Code` is only shape-compatible |

## Consequences

Still one database and one connection string. Arbitrary `TenantId` values cannot be inserted. Login must resolve `tenantCode + email`. Do not `FindByEmail` across tenants.
