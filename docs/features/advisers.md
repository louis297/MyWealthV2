---
title: Advisers
status: review
phase: 1
language: en
owner: ""
created: 2026-09-14
last_updated: 2026-09-14
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../architecture.md
  - ../adr/0012-user-activation-invite-deferred.md
  - ../adr/0013-roles-authorization-single-user-table.md
  - identity-auth.md
  - tenants.md
  - tenant-admins.md
---

# Advisers

TenantAdmin Adviser HTTP: list / get / create (Identity dual-write) / rename / disable / enable. Tables, `User` factory, dual-write, `TargetDisabled`, policy `advisers.manage`, and `UserDisabled` revocation already exist (identity-auth + tenant-admins). This slice does not add a script or a new policy name.

Unlike tenant-admins: caller and scope are the current tenant; disable is blocked while assigned Customers are not Disabled. The portal Advisers page is not this slice (later portals work).

Chinese discussion draft: `v2draft/features/advisers.md`.

---

## 1. Summary

A TenantAdmin creates an Adviser in **their** tenant (`name`, `email`, `password`). `tenantId` comes from the current user, not the body. Same-transaction dual write Identity → Domain, lands in `Active`. Email is unique CI inside the tenant. Role / TenantId / Email cannot change. Create while the current tenant is disabled → 400 `disabled` / `target=tenant` (leftover access-token window). Disable while any assigned Customer is not Disabled → 400 ordinary validation, not §4.1. Cross-tenant ids → 404. SystemAdmin / Adviser / Customer → 403. Every list item includes `status`; `enabledOnly` matches currencies.

---

## 2. Scope

**In**

- Application: `CreateAdviser`, `UpdateAdviser`, `DisableAdviser`, `EnableAdviser`, `GetAdvisers`, `GetAdviserById`
- Reuse tenant-admins dual-write (`IIdentityService.CreateLoginAsync` + `User.Create(Role.Adviser, withPassword: true)`) and `TargetDisabledException`
- Domain: add `User.DisableAdviser(bool hasNonDisabledAssignedCustomers)`. The assigned-customer guard lives on the aggregate. `Disable()` stays for TenantAdmin / Customer
- `webapi` `/users/advisers`, policy `advisers.manage` (already registered)
- Same list envelope as tenants / tenant-admins; `enabledOnly`; no `tenantId` query (always current tenant)
- Tests: 403, cross-tenant 404, wrong-collection 404, email uniqueness, assigned-customer guard, two-tenant fixture

**Out**

- A new SQL script or policy name
- `/users/customers`, `/users/tenant-admins`, `GET/POST /users`
- `tenantId` in the body (a TenantAdmin cannot create an Adviser in another firm)
- Password-less create / `PendingActivation` / invite / admin-set password
- Changing Email / Role / TenantId
- Physical delete
- “Cannot disable the last Adviser” (no such gap; the only guard is assigned Customers)
- Shipping Customer HTTP (tests may `User.Create(Customer)` directly)
- Adviser Portal Advisers page (portals)
- SystemAdmin managing Advisers (Scalar is for TenantAdmins)

---

## 3. Stories

1. As a TenantAdmin I create an Adviser in my firm (`name`, `email`, `password`) so they can sign in to the portal.
2. As a TenantAdmin I list Advisers in my firm (default = all, each item has `status`; `enabledOnly=true` for Active only; search name / email).
3. As a TenantAdmin I rename an Adviser without touching email or tenant.
4. As a TenantAdmin I disable an Adviser; if assigned Customers are still not Disabled I must handle them first.
5. As a TenantAdmin I re-enable a disabled Adviser.
6. As a TenantAdmin in another tenant I receive 404 for someone else’s Adviser id.
7. As a SystemAdmin, Adviser, or Customer I receive 403 on every `/users/advisers` verb.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Every verb requires `advisers.manage`. Missing / dead Bearer → 401, never 302. SystemAdmin / Adviser / Customer → 403. |
| R2 | Path `{id}` and JSON `id` are the person’s PublicId. Internal ints never appear. Create returns `201 { "id" }` only. |
| R3 | `Name` required, 1–200, trim, preserve caller casing. |
| R4 | `Email` required, trim, preserve casing, compare CI, must look like an email. Unique inside the tenant (collision with a TenantAdmin or Customer is also 400). Repeatable across tenants. |
| R5 | Create requires `password` (minimum 8). Lands `Active`, raises `UserActivated` + `UserCreated`. No password-less create. |
| R6 | Create does **not** take `tenantId`. Tenant = current user’s TenantId. Missing current tenant (should not happen) → 404. Current tenant `IsEnabled = false` → 400, `TargetDisabledException("tenant", tenant.PublicId, "Tenant is disabled", "Cannot create an Adviser while the tenant is disabled. Enable the tenant first.")`. List / get / PUT / disable / enable do not reject because the tenant is disabled. |
| R7 | Dual-write order matches tenant-admins: Identity first (`UserName` = person PublicId, `TenantId` projection), then `Users`. Same transaction. Responses never include the password. |
| R8 | Role / TenantId / Email cannot change. PUT updates `name` only. Body includes `email` / `tenantId` / `role` / `password` / `adviserId` → 400. |
| R9 | Status is not on PUT. Disable / enable are `POST …/disable` and `POST …/enable`. |
| R10 | HTTP idempotent: already Disabled + disable, or already Active + enable → 204, no second event. Application inspects Status first; domain throw semantics stay. |
| R11 | Disable goes through `User.DisableAdviser(hasNonDisabledAssignedCustomers)`. Application only counts rows `AdviserId = this person AND Role = Customer AND Status != Disabled` and passes the bool. `true` → `DomainException`, HTTP **400** ordinary `errors`, fixed copy: `Reassign or disable assigned customers before disabling this adviser.` Do **not** use `code=disabled` / `target=user` (the Adviser is still Active). `false` (no customers, or all Disabled) → existing `Disable()`, raises `UserDisabled`. Tests may insert Customer rows without the customers slice. |
| R12 | Revocation stays on the existing `UserDisabled` handler. Do not route TenantAdmin / Customer disable through `DisableAdviser`. |
| R13 | `Enable()` raises `UserActivated`. Login still requires an enabled tenant and Active. |
| R14 | PUT / disable / enable require `rowVersion`. Conflict 409. Missing 400. Same encoding as `/users/me`. |
| R15 | No physical delete. |
| R16 | List is current tenant, `Role=Adviser` only. Every item includes `status`. `enabledOnly` omitted/`false` = every Status; `true` = Active only; any other value 400. `page` default 1, `pageSize` default 20 max 100. Optional `search`: Name / Email contains (CI) or person PublicId exact. Sort name then email. No `tenantId=` or `status=` query. |
| R17 | **Cross-tenant 404**: another tenant’s Adviser PublicId → GET/PUT/disable/enable 404, same as missing. Same-tenant PublicId whose Role is not Adviser (TenantAdmin / Customer) → 404. Two-tenant fixture required. |
| R18 | Do not seed a demo Adviser. Tests create people. |

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `User` | Aggregate | Add `DisableAdviser(bool hasNonDisabledAssignedCustomers)`. Leave `Create(Adviser, …)` / `ChangeName` / `Disable` / `Enable` as they are. |
| `UserDisabled` / `UserActivated` | Event | Existing. Guard rejection raises nothing. |

```text
DisableAdviser(hasNonDisabledAssignedCustomers)
  Role is not Adviser → DomainException
  hasNonDisabledAssignedCustomers → DomainException (Status unchanged, no event)
  else → Disable()          // existing machine + UserDisabled
```

Application queries the database for the bool. It does not load Customer entities into the aggregate. Do not reopen the status machine. Same change adds the method name under [domain-model.md](../domain-model.md) §4.2 disable guards.

Invariants (domain-model §4.2):

- Adviser: `TenantId` set, `AdviserId` empty
- Email unique inside the tenant
- Disable Adviser: assigned Customers that are not Disabled must be handled first (`DisableAdviser`)

---

## 6. Database

No new script. [database-design.md](../database-design.md) §6.6.

Use existing `IX_Users_AdviserId` and `IX_Users_TenantId_Role` for the assigned-customer count. Do not add a script for this guard.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreateAdviser` | PublicId | R3–R7; current tenant; disabled tenant → 400 `disabled`/`tenant` |
| Command | `UpdateAdviser` | none | Current tenant + Role=Adviser, else 404; `rowVersion`; R3 / R8 |
| Command | `DisableAdviser` | none | Same load; already Disabled → 204 skip domain; else count assigned non-disabled Customers, call `DisableAdviser(count > 0)`; domain reject → 400 |
| Command | `EnableAdviser` | none | Same load; already Active → 204 |
| Query | `GetAdvisers` | paged envelope | R16; force current tenant |
| Query | `GetAdviserById` | item | Current tenant + Role=Adviser, else 404 |

Handlers use `advisers.manage`. TenantId comes from `IUser` / `ICurrentUser`. Do not trust a tenant in the body.

---

## 8. API

`/users` is a namespace prefix. `{id}` = Adviser PublicId.

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/users/advisers` | `advisers.manage` | 200 envelope | 400 query / 401 / 403 |
| GET | `/users/advisers/{id}` | `advisers.manage` | 200 item | 401 / 403 / 404 |
| POST | `/users/advisers` | `advisers.manage` | 201 `{ id }` | 400 (including `disabled`) / 401 / 403 |
| PUT | `/users/advisers/{id}` | `advisers.manage` | 204 | 400 / 401 / 403 / 404 / 409 |
| POST | `/users/advisers/{id}/disable` | `advisers.manage` | 204 | 400 (including assigned customers) / 401 / 403 / 404 / 409 |
| POST | `/users/advisers/{id}/enable` | `advisers.manage` | 204 | 400 / 401 / 403 / 404 / 409 |

### 8.1 Item

```json
{
  "id": "<publicId>",
  "tenantId": "<tenantPublicId>",
  "name": "Sam Reed",
  "email": "sam@north.example",
  "status": "active",
  "rowVersion": "<opaque>",
  "created": "2026-09-14T00:00:00+00:00"
}
```

Do not return `role`, `identityUserId`, password, or internal ints. `status` matches `/users/me`.

### 8.2 List

`GET /users/advisers?page=1&pageSize=20&enabledOnly=true&search=sam`

| Query | Default | Notes |
| --- | --- | --- |
| `page` | 1 | `< 1` → 400 |
| `pageSize` | 20 | Max 100 |
| `enabledOnly` | omitted / `false` = all | `true` = Active only |
| `search` | omitted | Trim; empty = omitted |

Envelope: `{ items, page, pageSize, totalCount }`. Reuse `PagedList<T>`.

### 8.3 Bodies

POST:

```json
{
  "name": "Sam Reed",
  "email": "Sam@north.example",
  "password": "Passw0rd!"
}
```

PUT:

```json
{
  "name": "Samantha Reed",
  "rowVersion": "<opaque>"
}
```

disable / enable:

```json
{
  "rowVersion": "<opaque>"
}
```

### 8.4 Errors

Create while the current tenant is disabled uses api-design §4.1:

```json
{
  "title": "Tenant is disabled",
  "status": 400,
  "detail": "Cannot create an Adviser while the tenant is disabled. Enable the tenant first.",
  "code": "disabled",
  "target": "tenant",
  "targetId": "<tenantPublicId>"
}
```

Disable blocked by assigned customers: domain reject, HTTP ordinary 400 + `errors`. Do **not** set `code=disabled` or `target=user`. `target=user` is reserved for the customers slice (create a Customer on a disabled Adviser).

Keep [api-design.md](../api-design.md) §7.4 pointing here for Adviser field rules.

---

## 9. UI

None in this slice. The Adviser Portal Advisers list / form belongs to the later-pages cut in [portals/adviser-portal.md](../portals/adviser-portal.md).

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `DisableAdviser(false)` on an Active Adviser flips Status and raises `UserDisabled` once; `DisableAdviser(true)` throws, Status unchanged, no event; non-Adviser `DisableAdviser` throws; `Disable()` on a TenantAdmin is unchanged |
| Application.FunctionalTests | No Bearer → 401 not 302; SystemAdmin / Adviser / Customer → 403 on every verb; TenantAdmin create → 201, get `status=active`, hosted login (that tenant code + email + password) succeeds; duplicate email in the same tenant → 400; same email in two tenants → 201; `tenantId` in body → 400; create after current tenant disabled → 400 `disabled`/`tenant`; PUT with `email` → 400; GET this collection with a TenantAdmin or Customer PublicId → 404; TenantAdmin of A GET/PUT/disable of B’s Adviser → 404; disable while an assigned Customer is Active → 400 and the Adviser stays Active; after those Customers are Disabled, disable → 204 and refresh fails; enable → 204; last Adviser with no customers may be disabled; stale `rowVersion` → 409; default paging; omitted `enabledOnly` includes disabled; `enabledOnly=true` is Active only |
| Infrastructure.IntegrationTests | No new script. Two-tenant fixture: one Adviser in A and one in B; A’s list does not include B; A requesting B’s id → 404 |

Post-create login uses hosted login on `identity`. Do not invent a token endpoint on `webapi`.

---

## 11. Locked in this spec

No new ADR.

| Item | Lock |
| --- | --- |
| Scripts / policy | None; existing `advisers.manage` |
| Caller | TenantAdmin only |
| Scope | Current tenant only; cross-tenant 404 |
| Create | No body `tenantId`; password → Active; disabled tenant → 400 `disabled`/`tenant` |
| Disable guard | Domain `DisableAdviser(bool)`; assigned non-disabled Customer → ordinary 400, not §4.1 |
| List | Reused envelope; `enabledOnly`; item includes `status` |
| Portal | No page in this slice |

Still open, not this slice:

- `/users/customers` (tests insert rows)
- Portal Advisers page
- Admin password reset / invite

Locked 2026-09-14:

1. Assigned-customer guard lives on `User.DisableAdviser(bool)`. Application only counts. `Disable()` stays for other roles.
2. That 400 does **not** share §4.1 `disabled`. `target=user` waits for the customers slice.

---

## 12. Suggested commits

The repository should build after each commit.

1. `User.DisableAdviser(bool)` + Domain.UnitTests
2. `CreateAdviser` + dual-write + disabled-tenant 400 + uniqueness + 401/403
3. `GetAdvisers` / `GetAdviserById` + paging + `enabledOnly` + cross-tenant 404 + wrong-collection 404
4. `UpdateAdviser` + PUT 204 / extra body fields 400 / 409
5. `DisableAdviser` / `EnableAdviser` + assigned-customer 400 + HTTP idempotent + refresh fails
6. Hosted-login smoke after create

Do not put `/users/customers` HTTP in these commits.
