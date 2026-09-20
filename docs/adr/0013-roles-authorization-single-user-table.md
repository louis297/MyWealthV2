---
title: "ADR 0013: Four roles, code-mapped policies, one table per layer"
status: accepted
language: en
date: 2026-09-01
updated: 2026-09-20
related:
  - 0005-shared-database-tenantid-isolation.md
  - 0006-email-password-jwt-authentication.md
  - 0012-user-activation-invite-deferred.md
  - 0014-openiddict-authorization-code-pkce.md
---

# ADR 0013: Four roles, code-mapped policies, one table per layer

Status: accepted

Locked 2026-09-01. Aligned 2026-09-12: Role does not live on Identity.

## Context

Jobs stay the four product-defined roles. Tenants cannot invent roles. The questions are: whether to add roles, how authorization is declared, and how many tables hold a person.

## Decision

1. **The Role enum has four values:** `SystemAdmin`, `TenantAdmin`, `Adviser`, `Customer`. Phase 1 does not add Operations / ReadOnly / Support.
2. **Endpoints use named policies.** Handlers do not scatter role names. `Role → permission` is code (`RolePermissions`). No permission table, no `AspNetRoles`, no permission claims in the JWT.
3. **Scope checks stay in the handler** (this tenant; an Adviser only touches assigned Customers).
4. **All four roles share one Domain `Users` table and one `ApplicationUser` type.** Credentials are one layer; the business person is another. Phase 1 does not split `CustomerProfiles`.
5. Role cannot change after create. A Customer who can obtain a token does not receive adviser-management or ledger policies.
6. **Phase 1 registers:** `tenants.manage`, `tenants.read`, `tenant-admins.manage`, `advisers.manage`, `customers.manage`, `customers.manage-own`, `users.me`. Ledger policy names are registered in Phase 2. Do not hang empty ledger names in Phase 1.

Amendment 2026-09-14: `tenants.read` is the shell / lookup read of a tenant. It is not tenant management. `tenants.manage` stays SystemAdmin-only.

### Two layers

| Layer | Table | Source of truth |
| --- | --- | --- |
| Credentials | `AspNetUsers` / `ApplicationUser` | Password, lockout, SecurityStamp. The only extra business column is nullable `TenantId` (projection, **no FK**). No Role / DisplayName / DomainUserId / Status |
| Business person | `Users` | Name, Email, Role, Status, TenantId, AdviserId, `IdentityUserId` |

Creating any login-capable role: same transaction, `AspNetUsers` first, then `Users` (`IdentityUserId` is a one-way FK). Email truth is Domain `Users`. Turn off Identity `RequireUniqueEmail`.

### `Users` CHECK (conceptual)

- `SystemAdmin` → `TenantId` null, `AdviserId` null
- `TenantAdmin` / `Adviser` → `TenantId` required, `AdviserId` null
- `Customer` → `TenantId` required, `AdviserId` required and a same-tenant Adviser
- All four roles require `IdentityUserId`

### Phase 1 permissions

| Permission | SystemAdmin | TenantAdmin | Adviser | Customer |
| --- | --- | --- | --- | --- |
| `tenants.manage` | ✓ | | | |
| `tenants.read` | ✓ | ✓ | ✓ | |
| `tenant-admins.manage` | ✓ | | | |
| `advisers.manage` | | ✓ | | |
| `customers.manage` | | ✓ | | |
| `customers.manage-own` | | | ✓ | |
| `users.me` | ✓ | ✓ | ✓ | ✓ |

### Phase 2 permissions (instruments)

Ledger names were not registered in Phase 1. The instruments slice adds:

| Permission | SystemAdmin | TenantAdmin | Adviser | Customer |
| --- | --- | --- | --- | --- |
| `instruments.read` | ✓ | ✓ | ✓ | |
| `instruments.create` | ✓ | ✓ | ✓ | |
| `instruments.manage` | ✓ | ✓ | | |

SystemAdmin has no `TenantId`. List and create take the target tenant PublicId. Field rules: [features/instruments.md](../features/instruments.md). Landed in repo 2026-09-20 `9ea2f2a`. Phase 1 people routes do not gain SystemAdmin verbs in this amendment.

### Phase 2 permissions (accounts)

| Permission | SystemAdmin | TenantAdmin | Adviser | Customer |
| --- | --- | --- | --- | --- |
| `accounts.read` | ✓ | ✓ | ✓ | |
| `accounts.create` | ✓ | ✓ | ✓ | |
| `accounts.manage` | ✓ | ✓ | ✓ | |

Adviser account scope is assigned Customers in the handler. Field rules: [features/accounts.md](../features/accounts.md). Landed in the repo 2026-09-21.

The `/users` HTTP namespace (`/users/advisers` and so on) is an API convention. It is not this ADR’s authorization model.

## Alternatives considered

| Option | Why not |
| --- | --- |
| `RequireRole` on each endpoint | Repackaging a permission means touching every endpoint |
| Permission table / tenant-defined roles | No admin UI; two sources of truth |
| Expand permissions into the JWT | Mapping changes force every session to re-login |
| Split Staff / Customer business tables | Phase-1 Customer fields are essentially Name + Email + AdviserId |
| Mirror Role onto Identity | Login already loads `Users`; dual-write would drift |

## Consequences

New slices: endpoints call `.RequireAuthorization("…")`; handlers still enforce tenant and assignment. If Customer profile fields explode later, grow `CustomerProfiles` from `Users`. Do not invent a second login table.
