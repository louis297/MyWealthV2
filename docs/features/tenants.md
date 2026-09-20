---
title: Tenants
status: accepted
phase: 1
language: en
owner: ""
created: 2026-09-13
last_updated: 2026-09-14
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../architecture.md
  - ../adr/0005-shared-database-tenantid-isolation.md
  - ../adr/0007-baseentity-primary-key-int.md
  - ../adr/0013-roles-authorization-single-user-table.md
  - currencies.md
  - identity-auth.md
---

# Tenants

Platform Tenant HTTP for SystemAdmin: list / get / create / rename+reporting-currency / disable / enable. The table, `PublicId`, `Code`, `IsEnabled`, audit, `RowVersion`, and `ReportingCurrency` already exist (identity-auth `0005`, currencies `0009`). This slice does not add a script.

Creating a Tenant does **not** create a TenantAdmin or any Identity user. People dual-write is the next slice.

---

## 1. Summary

SystemAdmin onboards a firm with `name`, `code`, and an enabled `reportingCurrency`. Code is the hosted-login key and does not change. Disable leaves person `Status` alone, raises `TenantDisabled`, and revokes every subject in that tenant (handler already in identity-auth). Re-enable restores login for people who are still `Active`. Manage verbs stay SystemAdmin. TenantAdmin / Adviser may **read** their own firm by code (`tenants.read`). There is no portal manage page.

---

## 2. Scope

**In**

- Domain: `Rename`, `Enable`, `TenantCreated`, `TenantEnabled` (Create / `SetReportingCurrency` / `Disable` / `TenantDisabled` already exist)
- Application: `CreateTenant`, `UpdateTenant`, `DisableTenant`, `EnableTenant`, `GetTenants`, `GetTenantById`, `GetTenantByCode`
- `webapi` manage routes under `/tenants` with policy `tenants.manage`
- `GET /tenants/by-code/{code}` with policy `tenants.read` (registered in identity-auth / ADR 0013 amendment 2026-09-14)
- Reporting-currency checks through `ICurrencyCatalog` (no JOIN, no `GET /currencies` from the handler)
- Unique CI `Name` / `Code` at the application layer (indexes already exist)
- List pagination envelope used later by people lists
- Tests: policy 403, missing 404, uniqueness, catalog rules, disable does not rewrite `Users.Status`, two-tenant fixture for later isolation slices

**Out**

- A new SQL script
- Creating a TenantAdmin / any user on Tenant create
- Physical delete
- Changing `Code` after create
- SystemAdmin “enter this tenant” / header switching / subdomain routing
- Quotas, billing, branding, per-tenant currency allow-lists
- `/users/tenant-admins` and later people routes
- An Adviser Portal tenants **manage** page (SystemAdmin uses Scalar / a later Back Office client)
- Opening `tenants.manage` to TenantAdmin or Adviser
- Bulk-updating person `Status` when the tenant flag changes

---

## 3. Stories

1. As a SystemAdmin I create a tenant (`name`, `code`, `reportingCurrency`) so a firm can exist before anyone in it has a login.
2. As a SystemAdmin I list and open tenants (filter, search, page) so I can find a firm in Scalar.
3. As a SystemAdmin I rename a tenant or change its reporting currency without touching Code.
4. As a SystemAdmin I disable a tenant so that Code cannot complete hosted login and existing refresh fails, without rewriting people rows.
5. As a SystemAdmin I re-enable a tenant so Active people in that firm can sign in again.
6. As a TenantAdmin or Adviser I may read **my** firm with `GET /tenants/by-code/{code}` so the Adviser Portal shell can show the Name. Another firm’s code is 404.
7. As a Customer I receive 403 on `tenants.read` and on every `tenants.manage` verb.
8. As a TenantAdmin, Adviser, or Customer I receive 403 on list / get-by-id / create / update / disable / enable.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Manage verbs (list / get-by-id / create / update / disable / enable) require `tenants.manage`. Missing / dead Bearer → 401, never 302. Other authenticated roles → 403. Unknown `PublicId` → 404. |
| R1a | `GET /tenants/by-code/{code}` requires `tenants.read`. Customer → 403. TenantAdmin / Adviser: `{code}` must match the caller’s tenant (CI); any other value or unknown code → **404**. SystemAdmin may read any code; unknown → 404. Anonymous → 401. Path is `/tenants/by-code/{code}` so it does not collide with `GET /tenants/{id}`. |
| R2 | Path `{id}` and JSON `id` are `PublicId`. Internal `int` never appears. Create returns `201 { "id": "<publicId>" }` only. |
| R3 | `Name` required, 1–200, globally unique CI. Trim. Preserve caller casing. |
| R4 | `Code` required, length 2–50, character class `[a-z0-9-]`, globally unique CI. Writes normalise to **lower case**. Compare CI. Immutable after create (no field on PUT). |
| R5 | Create does not create Identity or Domain users. |
| R6 | Create `reportingCurrency` must exist in `ICurrencyCatalog` and be **enabled**. Unknown or disabled → 400. Store the upper-case catalog code. |
| R7 | PUT may change `name` and/or `reportingCurrency`. If `reportingCurrency` is omitted or equals the current value, do **not** call `SetReportingCurrency` (a later-disabled historical code may stay). A **new** code must be enabled. |
| R8 | `IsEnabled` is not on PUT. Disable / enable are `POST …/disable` and `POST …/enable`. |
| R9 | Disable / enable are idempotent: already in the target state → 204, no second domain event. |
| R10 | Disable does **not** UPDATE `Users.Status`. Login already requires an enabled tenant **and** `Status = Active`. |
| R11 | `Disable()` raises `TenantDisabled`. identity-auth `RevokeTokensOnTenantDisabled` revokes every Domain user in that tenant (`sub` = user PublicId). This slice does not rewrite that handler. |
| R12 | `Enable()` raises `TenantEnabled`. No session action in Phase 1. Login works again only for people who are still Active. |
| R13 | PUT / disable / enable require `rowVersion`. Conflict → 409. List / get return `rowVersion` as an opaque string (same encoding as `/users/me`). |
| R14 | No physical delete. FK `Users.TenantId` is RESTRICT. |
| R15 | List query: `page` (1-based, default 1), `pageSize` (default 20, max 100), optional `isEnabled`, optional `search`. `search` matches Name or Code contains (CI) or `PublicId` exact. Sort: `name` ascending, then `code`. Disabled rows stay on the same list (filter if the caller asks). |
| R16 | Tenants is a **platform** resource. It has no `TenantId` column. Dual-check isolation of *tenant-owned rows* starts as a reusable two-tenant fixture here and is asserted on later slices. This slice’s own gate is policy 403, not a 404-for-other-tenant pattern. |
| R17 | Do not seed a demo firm in Development. Tests create tenants. SystemAdmin seed stays identity-auth. |

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `Tenant` | Aggregate | Already mapped. This slice adds `Rename` and `Enable`. |
| `TenantCreated` | Event | Raised once from `Create`. |
| `TenantDisabled` | Event | Already raised from `Disable`. Consumed by identity-auth revocation. |
| `TenantEnabled` | Event | Raised from `Enable` when the flag actually flips. |

Current factory (currencies slice): `Tenant.Create(name, code, Currency reportingCurrency, Guid? publicId = null)` — starts `IsEnabled = true`, stores `reportingCurrency.Code`. Keep that signature. Application normalises name/code **before** `Create`.

Add:

```text
Rename(string name)                 // non-empty; uniqueness is Application
Enable()                            // no-op if already enabled; else flag + TenantEnabled
```

`SetReportingCurrency` / `Disable` stay as implemented: disabled catalog code rejected; second `Disable` is a no-op.

Invariants (domain-model §4.1 — do not reopen):

- Code globally unique CI; `[a-z0-9-]{2,50}`; login key.
- Name globally unique CI.
- New reporting currency ∈ enabled catalog row.
- Disabled Code cannot complete login; existing refresh fails.
- Re-enable does not revive `Disabled` people by itself.
- `RowVersion` conflict → 409.

Keep [domain-model.md](../domain-model.md) in the same change if method names need a line.

---

## 6. Database

No new script. Final contract is [database-design.md](../database-design.md) §6.5.

| Table | Change | Indexes / FK |
| --- | --- | --- |
| `Tenants` | none | Already: PK `Id`; unique CI `PublicId` / `Name` / `Code`; `IX_Tenants_IsEnabled`; `ReportingCurrency` FK → `Currencies.Code` RESTRICT; `CK_Tenants_Code` length 2–50 |

Character class for `Code` is **not** a CHECK today (length only). Keep it in the Application validator. Do not add `0010_…` just to tighten the CHECK.

Uniqueness races: unique indexes + map SqlException 2601/2627 → 400.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreateTenant` | PublicId | R3–R6; load enabled `Currency` from `ICurrencyCatalog`; `Tenant.Create`; raise `TenantCreated` |
| Command | `UpdateTenant` | none | Load by PublicId or 404; `rowVersion`; R3 / R7 |
| Command | `DisableTenant` | none | Load by PublicId or 404; `rowVersion`; `Disable()` |
| Command | `EnableTenant` | none | Load by PublicId or 404; `rowVersion`; `Enable()` |
| Query | `GetTenants` | paged envelope | R15; no tenant-scope filter |
| Query | `GetTenantById` | item | PublicId or 404 |
| Query | `GetTenantByCode` | item | R1a; `{code}` CI; same item shape as get-by-id |

Manage handlers run under `tenants.manage`. `GetTenantByCode` runs under `tenants.read`. Do not inject a tenant filter for SystemAdmin manage verbs.

---

## 8. API

Root path. `{id}` = PublicId.

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/tenants` | `tenants.manage` | 200 envelope | 400 query / 401 / 403 |
| GET | `/tenants/by-code/{code}` | `tenants.read` | 200 item | 401 / 403 / 404 |
| GET | `/tenants/{id}` | `tenants.manage` | 200 item | 401 / 403 / 404 |
| POST | `/tenants` | `tenants.manage` | 201 `{ id }` | 400 / 401 / 403 |
| PUT | `/tenants/{id}` | `tenants.manage` | 204 | 400 / 401 / 403 / 404 / 409 |
| POST | `/tenants/{id}/disable` | `tenants.manage` | 204 | 400 / 401 / 403 / 404 / 409 |
| POST | `/tenants/{id}/enable` | `tenants.manage` | 204 | 400 / 401 / 403 / 404 / 409 |

### 8.1 Item

```json
{
  "id": "<publicId>",
  "name": "North Advisory",
  "code": "north-advisory",
  "reportingCurrency": "NZD",
  "isEnabled": true,
  "rowVersion": "<opaque>",
  "created": "2026-09-13T00:00:00+00:00"
}
```

- `reportingCurrency` is the three-letter code only. Names come from `GET /currencies`.
- `created` is `datetimeoffset`. Do not return `createdBy` / `lastModified*`.

### 8.2 List

`GET /tenants?page=1&pageSize=20&isEnabled=true&search=north`

| Query | Default | Notes |
| --- | --- | --- |
| `page` | 1 | 1-based. `< 1` → 400 |
| `pageSize` | 20 | Max 100. `< 1` or `> 100` → 400 |
| `isEnabled` | omitted = all | Bool only. Other strings → 400 |
| `search` | omitted | Trim; empty = omitted |

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 0
}
```

People slices reuse this envelope. Do not invent a second list shape.

### 8.3 Bodies

POST `/tenants`:

```json
{
  "name": "North Advisory",
  "code": "North-Advisory",
  "reportingCurrency": "nzd"
}
```

Stored: `code = "north-advisory"`, `reportingCurrency = "NZD"`.

PUT `/tenants/{id}`:

```json
{
  "name": "North Advisory Ltd",
  "reportingCurrency": "AUD",
  "rowVersion": "<opaque>"
}
```

`reportingCurrency` may be omitted. `code` in the body is ignored if sent (do not treat it as a rename of Code — 400 if present is also acceptable; pick **400 if `code` is present** so callers do not think it worked).

POST disable / enable:

```json
{
  "rowVersion": "<opaque>"
}
```

Missing `rowVersion` on PUT / disable / enable → 400.

Keep [api-design.md](../api-design.md) §7.3 pointing at this file for field rules.

---

## 9. UI

None — API / Scalar only.

Do not add a tenants **manage** page to the Adviser Portal. SystemAdmin uses Scalar. The portal shell may call `GET /tenants/by-code/{code}` only.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `Rename` rejects blank; `Enable` flips and raises `TenantEnabled` once; second `Enable` / `Disable` is a no-op and raises nothing; `Create` still raises `TenantCreated` |
| Application.FunctionalTests | No Bearer → 401 not 302; TenantAdmin / Adviser / Customer → 403 on every **manage** verb; Customer → 403 on `GET /tenants/by-code/{code}`; TenantAdmin / Adviser → 200 for their own code, 404 for another tenant’s code or an unknown code; SystemAdmin → 200 for any existing code, 404 if missing; SystemAdmin create → 201 `{ id }` and get matches normalised code + upper currency; duplicate name / code (different case) → 400; disabled / unknown currency on create → 400; PUT same historical disabled currency (omit or equal) → 204; PUT new disabled currency → 400; PUT with `code` in body → 400; disable → tenant `isEnabled=false`, users in that tenant stay `Active`, refresh for those subjects fails; enable → 204; bad / stale `rowVersion` → 409; missing PublicId → 404; list default paging; `isEnabled=true` omits disabled; `search` hits name and code |
| Infrastructure.IntegrationTests | No new script. Two-tenant fixture: insert A and B; disable A leaves B enabled. Later slices reuse this fixture for cross-tenant 404 |

`TenantDisabled` revocation is already covered by identity-auth. This slice asserts the event still fires on a real disable HTTP call (refresh for a seeded person in that tenant fails). Creating that person in the test is allowed (Domain `User.Create` + `UserManager`) without shipping `/users/tenant-admins`.

### Acceptance (amendment B)

No new script. Item JSON matches get-by-id (`id`, `name`, `code`, `reportingCurrency`, `isEnabled`; `rowVersion` / `created` may be present but the portal must not PUT through this route).

| Id | Given | When | Then |
| --- | --- | --- | --- |
| B1 | No Bearer | `GET /tenants/by-code/{any}` | 401, never 302. |
| B2 | Customer Bearer | `GET /tenants/by-code/{that customer’s tenant code}` | 403. |
| B3 | TenantAdmin Bearer, tenant `north-advisory` | `GET /tenants/by-code/north-advisory` (any case) | 200. `code` is the stored lower-case value. `name` is the firm Name. |
| B4 | Same TenantAdmin | `GET /tenants/by-code/{other tenant’s code}` | 404. Not 403. |
| B5 | Same TenantAdmin | `GET /tenants/by-code/does-not-exist` | 404. |
| B6 | Adviser Bearer in `north-advisory` | Own code | 200, same item as B3. Other tenant / unknown | 404. |
| B7 | SystemAdmin Bearer | Existing code of any tenant | 200. Unknown code | 404. |
| B8 | TenantAdmin / Adviser / Customer Bearer | `GET /tenants`, `GET /tenants/{id}`, POST/PUT/disable/enable | 403 (manage verbs unchanged). |
| B9 | Route | `GET /tenants/by-code/{code}` vs `GET /tenants/{id}` | A PublicId in `{id}` still hits get-by-id (`tenants.manage`). A code such as `north-advisory` on `/tenants/by-code/…` does not bind as `{id}`. |
| B10 | Policy | Endpoint metadata | `tenants.read`, not `tenants.manage`, not anonymous, not default `[Authorize]` alone. |
| B11 | Two-tenant fixture | Admin of A calls B’s code; admin of B calls A’s code | Both 404. Lists still do not leak the other firm. |

Do not ship `GET /tenants/{code}` without `by-code`. Do not open `tenants.manage` to TenantAdmin.

---

## 11. Locked in this spec

Do not open a new ADR (0005 / 0007 / 0013 already cover isolation, PublicId, and policies). Amendment 2026-09-14 adds `tenants.read` + by-code.

| Item | Lock |
| --- | --- |
| Scripts | None |
| Policy | Manage: `tenants.manage`. Lookup: `tenants.read` |
| Create people | Not this slice |
| Code | Lower-case store; immutable; 400 if PUT body includes `code` |
| Reporting currency | Catalog + enabled on **new** value only |
| Flag | Separate disable / enable; idempotent; no bulk Status rewrite |
| Concurrency | `rowVersion` required on PUT / disable / enable |
| List | Envelope `{ items, page, pageSize, totalCount }`; page 1 / size 20 / max 100 |
| Portal | No page |
| Demo seed firm | No |

Still open, not this slice:

- `/users/tenant-admins` dual-write → tenant-admins
- Login-failure codes when the tenant is disabled (identity-auth § still open; uniform failure until locked)
- Subdomain routing

---

## 12. Suggested commits

The repository should build after each commit.

1. `Rename` / `Enable` / `TenantCreated` / `TenantEnabled` + Domain.UnitTests
2. List envelope type + `GetTenants` / `GetTenantById` + GET functional tests (401 / 403 / 200)
3. `CreateTenant` + POST 201 / uniqueness / catalog 400 tests
4. `UpdateTenant` + PUT 204 / 409 / historical-vs-new currency tests
5. `DisableTenant` / `EnableTenant` + refresh-fail + Status-unchanged tests
6. Two-tenant fixture documented in the test project for later slices

Do not put `/users/tenant-admins` in these commits.
