---
title: Fix — Add EF global tenant query filter
status: accepted
language: en
created: 2026-10-21
updated: 2026-10-21
related:
  - ../architecture.md
  - ../adr/0005-shared-database-tenantid-isolation.md
---

# Fix: Add EF global tenant query filter

Status: accepted

## Problem

`docs/architecture.md` §5 lists the tenancy strategy as:

> Shared database + row TenantId + database FK + application dual check + **EF filter** + isolation tests

ADR 0005 already states that a global filter alone is not enough and that handlers must tighten to the current tenant (dual check). The dual-check rule is followed in code.

However, the EF global query filter does not exist. `ApplicationDbContext.OnModelCreating` contains no `HasQueryFilter`. Isolation currently depends entirely on every handler remembering to write:

```csharp
if (currentUser.Role != UserRole.SystemAdmin)
    query = query.Where(row => row.TenantId == currentUser.TenantId);
```

This pattern appears many times across Application handlers. A single omitted check on a new handler leaks cross-tenant data. For financial records this is not an acceptable residual risk. Isolation tests can only cover handlers that already exist.

## Decision

Add an EF Core global query filter on every tenant-scoped entity as a safety net. Keep the existing per-handler dual check.

- Filter expression: `TenantId == currentTenantId` for entities that require a tenant. SystemAdmin (null tenant) is handled by the caller ignoring the filter.
- SystemAdmin paths call `IgnoreQueryFilters()`.
- The filter is configured once in `OnModelCreating` (or a shared configuration class applied from the assembly). Apply only to business entities that carry `TenantId`; exclude OpenIddict and Identity tables.
- Handlers continue to apply the explicit `Where` for non-SystemAdmin users. The filter is the backstop, not the only control.
- Current tenant id is supplied via the DbContext (constructor or scoped accessor). Do not rely on ambient static state.
- Update `architecture.md` §5 and ADR 0005 so the description matches the implementation.

## Out of scope

- SQL Row-Level Security.
- Changing the dual-check pattern in existing handlers.
- Named query filters (EF 10) unless they simplify SystemAdmin handling without extra complexity.
- Soft-delete filters (not currently used).

## Impact

- `Infrastructure` model configuration.
- Possibly a small helper or extension for applying the filter to all tenant-scoped entities.
- Existing isolation tests should continue to pass; add at least one test that queries without the handler-level `Where` and still cannot see other tenants.
- No schema change.
- No change to public API contracts.
