---
title: TenantAdmins
status: accepted
phase: 1
language: en
owner: ""
created: 2026-09-13
last_updated: 2026-09-13
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../architecture.md
  - ../adr/0012-user-activation-invite-deferred.md
  - ../adr/0013-roles-authorization-single-user-table.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
  - identity-auth.md
  - tenants.md
---

# TenantAdmins

SystemAdmin TenantAdmin HTTP: list / get / create (Identity dual-write) / rename / disable / enable. `Users`, `AspNetUsers`, `User.Create` / `Disable` / `Enable` / `ChangeName`, policy `tenant-admins.manage`, and the `UserDisabled` revocation handler already exist (identity-auth + tenants). This slice does not add a script or a new policy name.

Creating a TenantAdmin is the first Phase-1 dual-write that lands in `Active` with a password. No invite. No `PendingActivation` entry. Adviser and Customer dual-write belong to the next two slices.

---

## 1. Summary

SystemAdmin adds a TenantAdmin to an **enabled** tenant (`tenantId`, `name`, `email`, `password`). Same transaction: `AspNetUsers` first (`UserName` = the person’s PublicId, `TenantId` projection), then `Users.IdentityUserId`. Email is unique CI inside the tenant. Role / TenantId / Email cannot change after create. Create on a disabled tenant → 400 shared `disabled` error (`target=tenant`). Disabling the last TenantAdmin is allowed in Phase 1 (known gap). Disable raises `UserDisabled`; the existing identity-auth handler revokes that subject. Every list item includes `status` and `isActive`. TenantAdmin / Adviser / Customer receive 403. There is no portal page.

---

## 2. Scope

**In**

- Domain: add `UserCreated` (once after a successful dual write; current `User.Create` with a password only raises `UserActivated`). `ChangeName` / `Disable` / `Enable` stay as they are
- Application: `CreateTenantAdmin`, `UpdateTenantAdmin`, `DisableTenantAdmin`, `EnableTenantAdmin`, `GetTenantAdmins`, `GetTenantAdminById`
- Dual write in one transaction: `UserManager` creates `ApplicationUser` → `User.Create(Role.TenantAdmin, withPassword: true)` → `Users`
- Shared `TargetDisabled` exception + `webapi` mapping (api-design §4.1). First caller is this slice
- `webapi` routes under `/users/tenant-admins` with policy `tenant-admins.manage` (already registered)
- List envelope reused from tenants; query `enabledOnly` aligned with currencies; optional `tenantId` / `search`
- Tests: policy 403, missing / wrong-collection 404, in-tenant email uniqueness, same email in two tenants, last-admin disable, refresh fails, two-tenant fixture list filter

**Out**

- A new SQL script
- A new policy name (use `tenant-admins.manage`)
- `/users/advisers`, `/users/customers`, a catch-all `GET/POST /users`
- Create without a password / `PendingActivation` / writing `UserTokens` / sending email
- Admin-set or reset password; invite activation
- Changing Email / Role / TenantId
- Physical delete; cascading delete of Identity
- “Cannot disable the last TenantAdmin” (explicitly allowed this phase)
- Bulk-updating other people’s `Status` when this person is disabled
- SystemAdmin “enter this tenant” / header switching
- An Adviser Portal TenantAdmin page (SystemAdmin uses Scalar)
- Making `User.Disable()` / `Enable()` idempotent in the domain (HTTP checks Status first and skips the method)

---

## 3. Stories

1. As a SystemAdmin I create a TenantAdmin on an enabled tenant (`name`, `email`, `password`) so someone in that firm can sign in to the Adviser Portal. If the tenant is disabled I see a distinct error instead of a person who cannot complete login.
2. As a SystemAdmin I list TenantAdmins (default = all, each item has `status`; `enabledOnly=true` for Active only; filter by tenant, search name / email) so I can find a person in Scalar.
3. As a SystemAdmin I rename a TenantAdmin without touching email or tenant.
4. As a SystemAdmin I disable a TenantAdmin so that person cannot complete login and existing refresh fails, even if they are the last TenantAdmin in the firm.
5. As a SystemAdmin I re-enable a TenantAdmin so they can sign in again when the tenant is still enabled and `Status` is `Active`.
6. As a TenantAdmin, Adviser, or Customer I receive 403 on every `/users/tenant-admins` verb so this is a platform surface, not a firm-operator surface.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Every verb requires policy `tenant-admins.manage`. Missing / dead Bearer → 401, never 302. Other authenticated roles → 403. Unknown PublicId, or a PublicId whose `Role != TenantAdmin` → 404 (do not leak people from other collections). |
| R2 | Path `{id}` and JSON `id` / `tenantId` are PublicId. Internal `int` never appears. Create returns `201 { "id": "<publicId>" }` only. |
| R3 | `Name` required, 1–200, trim, preserve caller casing. |
| R4 | `Email` required, trim, preserve caller casing, compare CI. Must look like an email. Unique inside the same `TenantId`. The same email may exist in two tenants. Collision with a SystemAdmin’s global email is allowed (different login key: tenantCode present or not). |
| R5 | Create requires `password`. Identity password options apply (identity-auth: minimum length 8). Lands in `Status = Active` and raises `UserActivated`. No password-less create. |
| R6 | Create requires `tenantId` (tenant PublicId). Unknown tenant → 404. Tenant exists and `IsActive = false` → **400**, shared disabled error ([api-design.md](../api-design.md) §4.1 and §8.4): `code=disabled`, `target=tenant`. Not 404 (the tenant exists). Not 409. List / get / PUT / disable / enable do not reject because the tenant is disabled. Person `Status` and the tenant flag stay independent. |
| R7 | Dual-write order is fixed: `AspNetUsers` first (`UserName` = the person’s PublicId string, `Email` copied from Domain, `TenantId` = tenant internal int), then `Users.IdentityUserId`. Same transaction; an Identity row must not remain if `Users` fails. Responses never include the password. |
| R8 | `Role` / `TenantId` / `Email` cannot change after create. PUT updates `name` only. Body includes `email` / `tenantId` / `role` / `password` → 400. |
| R9 | `Status` and `isActive` are not on PUT or create bodies. Disable / enable are `POST …/disable` and `POST …/enable`. |
| R10 | HTTP-layer idempotent: already Disabled + disable, or already Active + enable → 204, no second domain event. Do not change the throw semantics of `User.Disable()` / `Enable()`. Application inspects Status first. |
| R11 | Disabling the last TenantAdmin in that tenant → 204 (known gap; do not count, do not 400). |
| R12 | `Disable()` raises `UserDisabled`. identity-auth `RevokeTokensOnUserDisabled` revokes by `sub` = person PublicId. This slice does not rewrite that handler. |
| R13 | `Enable()` raises `UserActivated`. No extra session action in Phase 1. Login still requires an enabled tenant and `Status = Active`. |
| R14 | PUT / disable / enable require `rowVersion`. Conflict → 409. Missing → 400. List / get encode `rowVersion` the same way as `/users/me` (Base64). |
| R15 | No physical delete. `Users.TenantId` → Tenants RESTRICT; `Users.IdentityUserId` → AspNetUsers RESTRICT. |
| R16 | List contains `Role = TenantAdmin` only. **Every item includes `status` and `isActive`** (same encoding as `/users/me`). Query aligned with currencies: `enabledOnly` omitted / `false` = every Status; `true` = `IsActive = 1` (excludes `Disabled` and `PendingActivation`); any other string → 400. Also `page` (1-based, default 1), `pageSize` (default 20, max 100), optional `tenantId` (tenant PublicId), optional `search`. `search` matches Name or Email contains (CI) or the person’s PublicId exact. Sort: `name` ascending, then `email`. Do not add a `status=` query parameter. |
| R17 | This collection’s gate is policy 403, not “other tenant → 404”. SystemAdmin has no TenantId and can see TenantAdmins in every firm. Cross-tenant 404 starts on advisers / customers for in-tenant callers. The two-tenant fixture on this slice asserts: unfiltered list includes A and B; `tenantId=A` returns only A. |
| R18 | Do not seed a demo TenantAdmin in Development. Tests create people. The SystemAdmin seed stays identity-auth. |

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `User` | Aggregate | Already mapped. This slice uses `Create(TenantAdmin, withPassword: true)`, `ChangeName`, `Disable`, `Enable`. |
| `UserCreated` | Event | Added here: raised once after a successful dual write. Listed in domain-model §6; not in the tree yet. |
| `UserActivated` | Event | Already raised on password-create and Disabled → Active. |
| `UserDisabled` | Event | Already raised from `Disable()`. identity-auth already subscribes for revocation. |

Keep the `User.Create` signature. Application normalises name / email and already has `identityUserId` and the tenant internal int before calling it.

Invariants (domain-model §4.2 — do not reopen):

- TenantAdmin: `TenantId` set, `AdviserId` empty, `IdentityUserId` set
- Email unique CI inside the tenant
- Role / TenantId / Email immutable; Name may change
- Create with password → Active
- Last TenantAdmin may be disabled
- Disable / password change revoke that subject (application consequence; the aggregate does not hold tokens)

`UserCreated` carries the `User`. Do not model OpenIddict revocation itself as a domain event.

Keep [domain-model.md](../domain-model.md) in the same change if method names need a line.

---

## 6. Database

No new script. Final contract is [database-design.md](../database-design.md) §6.6.

| Table | Change | Indexes / FK |
| --- | --- | --- |
| `Users` | none | Already: PK `Id`; unique `PublicId` / `IdentityUserId`; filtered unique `(TenantId, Email)`; `CK_Users_RoleShape`; `TenantId` → Tenants RESTRICT; `IdentityUserId` → AspNetUsers RESTRICT |
| `AspNetUsers` | none | Already: `TenantId` int null, no FK. Identity `RequireUniqueEmail` is off |

Uniqueness races: filtered unique index + map SqlException 2601/2627 → 400.

`AspNetUsers.UserName` = Domain `Users.PublicId` as a string (identity-auth lock). Do not change it to `{tenantCode}:{email}`.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreateTenantAdmin` | PublicId | R3–R7; load tenant by PublicId or 404; `IsActive = false` → 400 `disabled` / `tenant`; `UserManager.CreateAsync`; `User.Create`; raise `UserCreated` |
| Command | `UpdateTenantAdmin` | none | Load by person PublicId with Role=TenantAdmin or 404; `rowVersion`; R3 / R8 |
| Command | `DisableTenantAdmin` | none | Same load; `rowVersion`; already Disabled → 204, skip domain; else `Disable()` |
| Command | `EnableTenantAdmin` | none | Same load; `rowVersion`; already Active → 204, skip domain; else `Enable()` |
| Query | `GetTenantAdmins` | paged envelope | R16; no caller-tenant filter |
| Query | `GetTenantAdminById` | item | PublicId + Role=TenantAdmin, else 404 |

Handlers run under `tenant-admins.manage`. Do not inject a “current tenant” filter for SystemAdmin.

Do not split the dual write across two `SaveChanges`. Identity and Domain share `MyWealthDbV2`; use one transaction.

---

## 8. API

`/users` is a namespace prefix, not a parent resource. `{id}` = person PublicId.

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/users/tenant-admins` | `tenant-admins.manage` | 200 envelope | 400 query / 401 / 403 |
| GET | `/users/tenant-admins/{id}` | `tenant-admins.manage` | 200 item | 401 / 403 / 404 |
| POST | `/users/tenant-admins` | `tenant-admins.manage` | 201 `{ id }` | 400 (including shared `disabled`) / 401 / 403 / 404 (unknown tenant) |
| PUT | `/users/tenant-admins/{id}` | `tenant-admins.manage` | 204 | 400 / 401 / 403 / 404 / 409 |
| POST | `/users/tenant-admins/{id}/disable` | `tenant-admins.manage` | 204 | 400 / 401 / 403 / 404 / 409 |
| POST | `/users/tenant-admins/{id}/enable` | `tenant-admins.manage` | 204 | 400 / 401 / 403 / 404 / 409 |

### 8.1 Item

```json
{
  "id": "<publicId>",
  "tenantId": "<tenantPublicId>",
  "name": "Alex Chen",
  "email": "alex@north.example",
  "status": "active",
  "rowVersion": "<opaque>",
  "created": "2026-09-13T00:00:00+00:00"
}
```

- Do not return `role` (this collection is always TenantAdmin).
- Do not return `identityUserId`, password, or internal ints.
- `status` matches `/users/me`: camelCase enum (`active` / `disabled` / `pendingActivation`). This slice’s create path never produces `pendingActivation`; `enabledOnly=true` does not return it either.
- `created` is `datetimeoffset`. Do not return `createdBy` / `lastModified*`.

### 8.2 List

`GET /users/tenant-admins?page=1&pageSize=20&tenantId=<tenantPublicId>&enabledOnly=true&search=alex`

| Query | Default | Notes |
| --- | --- | --- |
| `page` | 1 | 1-based. `< 1` → 400 |
| `pageSize` | 20 | Max 100. `< 1` or `> 100` → 400 |
| `tenantId` | omitted = all | Must be a tenant PublicId. Unparseable → 400. Unknown tenant → empty list (200), not 404 |
| `enabledOnly` | omitted / `false` = all | `true` = `Status = Active` only. No disabled-only query. Any other value → 400 |
| `search` | omitted | Trim; empty = omitted |

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 0
}
```

Reuse the existing Application `PagedList<T>` (tenants). Do not invent a second list shape.

### 8.3 Bodies

POST `/users/tenant-admins`:

```json
{
  "tenantId": "<tenantPublicId>",
  "name": "Alex Chen",
  "email": "Alex@north.example",
  "password": "Passw0rd!"
}
```

Stored: Email keeps caller casing (uniqueness is CI); Identity `UserName` = the new person’s PublicId.

PUT `/users/tenant-admins/{id}`:

```json
{
  "name": "Alexandra Chen",
  "rowVersion": "<opaque>"
}
```

POST disable / enable:

```json
{
  "rowVersion": "<opaque>"
}
```

Keep [api-design.md](../api-design.md) §7.4 pointing at this file for TenantAdmin field rules.

### 8.4 Create on a disabled tenant

Shared disabled error from api-design §4.1. This slice’s instance:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Tenant is disabled",
  "status": 400,
  "detail": "Cannot create a TenantAdmin while the tenant is disabled. Enable the tenant first.",
  "code": "disabled",
  "target": "tenant",
  "targetId": "<tenantPublicId>"
}
```

- HTTP **400**, not 404 / 409 / 403.
- `code` + `target` identify the family; `title` is fixed per target; `detail` describes this action.
- Ordinary missing-field / email-format / short-password failures stay on the FluentValidation `errors` dictionary. Do **not** set `code: disabled` on those.
- Application throws one `TargetDisabled` exception; `webapi` maps it once. Advisers / customers / Phase-2 account reuse the same JSON. Do not invent a second shape per slice.

---

## 9. UI

None — API / Scalar only.

Do not add a TenantAdmin page to the Adviser Portal. SystemAdmin is not a portal role. The portal Advisers page belongs to the advisers slice.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `UserCreated` fires once after the factory or application wrapper; `Create(TenantAdmin)` shape: TenantId set, AdviserId empty, password path → Active + `UserActivated`; `ChangeName` rejects blank; a second `Disable()` on an already Disabled user still throws (idempotency lives in Application) |
| Application.FunctionalTests | No Bearer → 401 not 302; TenantAdmin / Adviser / Customer → 403 on every verb; SystemAdmin create → 201 `{ id }`, get has `status=active` and matching name / email / tenantId, hosted login with that tenantCode + email succeeds; duplicate email in the same tenant (different case) → 400; same email in two tenants → two 201s; unknown `tenantId` → 404; create on a disabled tenant → 400 with `code=disabled`, `target=tenant`, `targetId` = that tenant PublicId; GET / PUT / disable / enable of an existing TenantAdmin on that disabled tenant still 200/204; PUT with `email` → 400; GET this collection with an Adviser PublicId → 404; disable → `status=disabled`, that subject’s refresh fails, GET still returns the row; enable → 204; disable when they are the only TenantAdmin → 204; bad / stale `rowVersion` → 409; unknown person PublicId → 404; default paging; `tenantId` filter returns only that tenant; omitted `enabledOnly` includes disabled; `enabledOnly=true` is Active only; `search` hits name and email |
| Infrastructure.IntegrationTests | No new script. Reuse the two-tenant fixture: one TenantAdmin in A and one in B; unfiltered list sees both; `tenantId=A` sees only A |

Login-gate and password-change revocation stay identity-auth. This slice asserts a real HTTP disable still raises `UserDisabled` (refresh fails).

Post-create login uses hosted login on `identity` (tenantCode + email + password). Do not invent a token endpoint on `webapi`.

---

## 11. Locked in this spec

Do not open a new ADR (0012 / 0013 / 0014 already cover invite deferral, policies, and the token issuer).

| Item | Lock |
| --- | --- |
| Scripts | None |
| Policy | Existing `tenant-admins.manage` |
| Dual write | Identity first, then Domain; `UserName` = PublicId |
| Create | Password required → Active; no PendingActivation entry; disabled tenant → 400 `disabled` + `target=tenant` |
| Email | Unique CI inside the tenant; repeatable across tenants |
| Immutable | Role / TenantId / Email |
| Last admin | Disable allowed |
| Flag | Separate disable / enable; HTTP idempotent; domain throws stay |
| Concurrency | `rowVersion` required on PUT / disable / enable |
| List | Same envelope as tenants; item **includes** `status`; `enabledOnly` matches currencies (omit/`false` = all, `true` = Active only) |
| Wrong collection | Other-role PublicId → 404 |
| Portal | No page |
| Demo seed person | No |

Still open, not this slice:

- `/users/advisers`, `/users/customers`
- Admin password reset / invite / forgot-password
- “Cannot disable the last TenantAdmin”
- Login-failure codes when the person or tenant is disabled (identity-auth; uniform failure until locked)

Locked 2026-09-13:

1. List matches currencies: item includes `status`; query uses `enabledOnly`. No disabled-only query.
2. Disabled-target writes use api-design §4.1: `code=disabled` + `target` (this slice: `tenant`). `user` / `account` share the shape; this slice does not implement those call sites.

---

## 12. Suggested commits

The repository should build after each commit.

1. `UserCreated` + Domain.UnitTests (do not change `Disable` / `Enable` throws)
2. `TargetDisabled` exception + Web mapping + dual-write port + `CreateTenantAdmin` + POST 201 / uniqueness / unknown tenant 404 / disabled tenant 400 `disabled`/`tenant` / same email across tenants
3. `GetTenantAdmins` / `GetTenantAdminById` + GET 401 / 403 / 200 / wrong-collection 404 / paging and `enabledOnly`
4. `UpdateTenantAdmin` + PUT 204 / email in body 400 / 409
5. `DisableTenantAdmin` / `EnableTenantAdmin` + last-admin allowed + refresh fails + HTTP idempotent
6. Hosted-login smoke after create (tenantCode + email + password completes authorize)

Do not put `/users/advisers` or `/users/customers` in these commits.
