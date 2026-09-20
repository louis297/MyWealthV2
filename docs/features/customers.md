---
title: Customers
status: accepted
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
  - advisers.md
---

# Customers

TenantAdmin / Adviser Customer HTTP: list / get / create (Identity dual-write) / rename / reassign adviser / disable / enable. Tables, `User` factory (including the Customer shape), dual-write, `TargetDisabled`, policies `customers.manage` / `customers.manage-own`, and `UserDisabled` revocation already exist (identity-auth + tenant-admins + advisers). This slice does not add a script or a new policy name.

Unlike advisers: TenantAdmin sees the current tenant; an Adviser sees, creates, and edits only assigned Customers. Disable Customer has no ledger guard in Phase 1. Creating or reassigning onto a disabled Adviser uses api-design §4.1 `target=user` (the call site advisers reserved). The portal Customers page is not this slice (see [adviser-portal](../portals/adviser-portal.md)).

Landed in GitHub master 2026-09-14 (`b16d059`, hosted-login smoke after create).

---

## 1. Summary

A TenantAdmin creates a Customer in **their** tenant (`name`, `email`, `password`, `adviserId`). An Adviser creating a Customer may only assign self (`adviserId` omitted = self; any other id → 400). `tenantId` comes from the current user, not the body. Same-transaction dual write Identity → Domain, lands in `Active`. Email is unique CI inside the tenant. Role / TenantId / Email cannot change. After create, a TenantAdmin may reassign `AdviserId` to another non-disabled Adviser in the same tenant.

Create while the current tenant is disabled → 400 `disabled` / `target=tenant`. Target Adviser Disabled → 400 `disabled` / `target=user`. Cross-tenant, not-assigned, or wrong-collection ids → 404. SystemAdmin / Customer → 403. An Adviser reading another Adviser’s Customer → 404 (not 403; do not leak existence). Every list item includes `status`, `isActive`, and `adviserId`; `enabledOnly` matches currencies (`true` = `IsActive = 1`).

A Customer may complete authorization-server login (tests / reserved). There is no Customer Portal client. `/users/customers` stays 403 for that role. They use `/users/me` and `/currencies` only.

---

## 2. Scope

**In**

- Application: `CreateCustomer`, `UpdateCustomer`, `DisableCustomer`, `EnableCustomer`, `GetCustomers`, `GetCustomerById`
- Reuse the existing dual-write (`IIdentityService.CreateLoginAsync` + `User.Create(Role.Customer, withPassword: true)`) and `TargetDisabledException`
- Domain: add `User.ReassignAdviser(...)` (Customer only) and `CustomerAdviserReassigned`. Leave `Disable()` / `Enable()` / `ChangeName` as they are. This slice does not add a handler for the event and does not add an assignment-history table
- `webapi` `/users/customers`. No new policy name: the endpoint accepts `customers.manage` **or** `customers.manage-own` (already registered). The handler then applies scope
- Same list envelope as tenants / people collections; `enabledOnly`; optional `adviserId=` for TenantAdmin; no `tenantId` query (always current tenant)
- Tests: 403, cross-tenant 404, wrong-collection 404, assigned-scope 404, email uniqueness, disabled-adviser `target=user`, reassign, two-tenant fixture

**Out**

- A new SQL script or policy name
- `/users/advisers`, `/users/tenant-admins`, `GET/POST /users`
- `tenantId` in the body
- Password-less create / `PendingActivation` / invite / admin-set password
- Changing Email / Role / TenantId
- Physical delete
- “Cannot disable the last Customer”
- Ledger / open-Account guard (owned by landed [accounts](accounts.md); this slice does not create Account tables)
- Customer Portal client; Adviser Portal Customers page (portals)
- SystemAdmin managing Customers (Scalar is not that surface)
- A Customer calling this collection (403; profile is `/users/me`)
- An `INotificationHandler<CustomerAdviserReassigned>`; an `AdviserAssignments` history table

---

## 3. Stories

1. As a TenantAdmin I create a Customer in my firm (`name`, `email`, `password`, adviser) so they can complete hosted login (tests / reserved) and the Adviser can manage them.
2. As an Adviser I create a Customer assigned to myself; I cannot assign them to someone else.
3. As a TenantAdmin I list Customers in my firm (default = all, each item has `status` and `adviserId`; `enabledOnly=true` for Active only; filter by adviser; search name / email).
4. As an Adviser my list and get show only assigned Customers; anyone else’s id is 404.
5. As a TenantAdmin I rename a Customer, or reassign them to another non-disabled Adviser in the firm.
6. As an Adviser I rename an assigned Customer; I cannot reassign.
7. As a TenantAdmin or the assigned Adviser I disable a Customer so they cannot complete login and existing refresh fails; [accounts](accounts.md) rejects disable while any account is still active.
8. As a TenantAdmin or the assigned Adviser I re-enable a disabled Customer.
9. As a SystemAdmin or a Customer I receive 403 on every `/users/customers` verb.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Enter the handler with `customers.manage` (TenantAdmin) **or** `customers.manage-own` (Adviser). Missing / dead Bearer → 401, never 302. SystemAdmin / Customer → 403. Authorize with an either-or of the two existing names. Do not register a third policy. |
| R2 | Path `{id}` and JSON `id` are the person’s PublicId. Internal ints never appear. Create returns `201 { "id" }` only. |
| R3 | `Name` required, 1–200, trim, preserve caller casing. |
| R4 | `Email` required, trim, preserve casing, compare CI, must look like an email. Unique inside the tenant (collision with a TenantAdmin or Adviser is also 400). Repeatable across tenants. |
| R5 | Create requires `password` (minimum 8). Lands `Active`, raises `UserActivated` + `UserCreated`. No password-less create. Create does **not** raise `CustomerAdviserReassigned`. |
| R6 | Create does **not** take `tenantId`. Tenant = current user’s TenantId. Missing current tenant (should not happen) → 404. Current tenant `IsActive = false` → 400, `TargetDisabledException("tenant", tenant.PublicId, "Tenant is disabled", "Cannot create a Customer while the tenant is disabled. Enable the tenant first.")`. List / get / PUT / disable / enable do not reject because the tenant is disabled. |
| R7 | Dual-write order matches tenant-admins / advisers: Identity first (`UserName` = person PublicId, `TenantId` projection), then `Users`. Same transaction. Responses never include the password. |
| R8 | Role / TenantId / Email cannot change. PUT updates `name`. A TenantAdmin PUT may also update `adviserId`. Body includes `email` / `tenantId` / `role` / `password` → 400. An Adviser PUT that includes `adviserId` → 400. |
| R9 | Status and `isActive` are not on PUT or create bodies. Disable / enable are `POST …/disable` and `POST …/enable`. |
| R10 | HTTP idempotent: already Disabled + disable, or already Active + enable → 204, no second event. Application inspects Status first; domain throw semantics stay. |
| R11 | Disable goes through existing `User.Disable()`. [accounts](accounts.md) adds an application guard: any `IsActive` account → 400 ordinary `errors`. Closed-only may disable. Raises `UserDisabled`; the identity-auth revocation handler stays. Do not route through `DisableAdviser`. |
| R12 | `Enable()` raises `UserActivated`. Login still requires an enabled tenant and Active. |
| R13 | PUT / disable / enable require `rowVersion`. Conflict 409. Missing 400. Same encoding as `/users/me`. |
| R14 | No physical delete. |
| R15 | **Assignment.** A Customer must have `AdviserId`. The target Adviser is same-tenant, Role=Adviser. Missing / other-tenant / not-an-Adviser → 404. Target Adviser Status=Disabled → 400 §4.1 `code=disabled` / `target=user` / `targetId` = that Adviser’s PublicId. Fixed details: create `"Cannot create a Customer on a disabled adviser. Enable or pick another adviser."`; reassign `"Cannot reassign a Customer to a disabled adviser. Enable or pick another adviser."` Title = `User is disabled`. |
| R16 | **Who may set the adviser.** TenantAdmin create requires `adviserId` (Adviser PublicId). Adviser create: omit `adviserId` = self; if present it must equal the caller’s PublicId, else 400 ordinary validation (not 404 — the caller is assigning, not reading someone else’s row). An Adviser cannot reassign. |
| R17 | **Scope.** TenantAdmin: current tenant, Role=Customer. Adviser: current tenant, Role=Customer, and `AdviserId` = self. Cross-tenant, same-tenant but not assigned, same-tenant but Role is not Customer → GET/PUT/disable/enable 404. Two-tenant fixture required. |
| R18 | List is current tenant, `Role=Customer` only. Every item includes `status`, `isActive`, and `adviserId`. `enabledOnly` omitted/`false` = every Status; `true` = `IsActive = 1`; any other value 400. `page` default 1, `pageSize` default 20 max 100. Optional `search`: Name / Email contains (CI) or person PublicId exact. Sort name then email. No `tenantId=` or `status=` query. Optional `adviserId=` (Adviser PublicId): TenantAdmin filters by that Adviser; unknown / other-tenant adviser → empty list (do not 404; this is a filter). Adviser with `adviserId=`: equal to self → ignore (already scoped); any other value → 400. |
| R19 | Do not seed a demo Customer. Tests create people. |

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `User` | Aggregate | Add `ReassignAdviser`. Leave `Create(Customer, …)` / `ChangeName` / `Disable` / `Enable` as they are. |
| `UserDisabled` / `UserActivated` / `UserCreated` | Event | Existing. Create does not also raise the reassignment event. |
| `CustomerAdviserReassigned` | Event | Once, only when the pointer actually changes. Payload: `CustomerId`, `TenantId`, `PreviousAdviserId`, `NewAdviserId` (domain internal ints). No actor, reason, or as-of date. |

```text
ReassignAdviser(newAdviserId)
  Role is not Customer → DomainException
  newAdviserId empty → DomainException
  same as current AdviserId → no-op, no event
  else write AdviserId and raise CustomerAdviserReassigned
```

Application loads the target Adviser (same tenant, Role=Adviser, not Disabled) and passes a valid internal id. The aggregate does not load the other User. A disabled target is `TargetDisabledException` in Application; it never reaches `ReassignAdviser`.

No subscriber in this slice: do not implement `INotificationHandler<CustomerAdviserReassigned>` and do not create `AdviserAssignments`. The event is a cross-context hook. Audit persistence and as-of reporting wait for a later slice. The actor is already `LastModifiedBy`.

Invariants (domain-model §4.2):

- Customer: `TenantId` set, `AdviserId` set; target same-tenant, Role=Adviser, not Disabled at create / reassign
- Email unique inside the tenant
- Disable Customer: no ledger guard in Phase 1
- A Customer’s `AdviserId` may be reassigned by a TenantAdmin

Same change updates [domain-model.md](../domain-model.md) §4.2 (method name) and §6 (payload of the already-listed event).

---

## 6. Database

No new script. [database-design.md](../database-design.md) §6.6.

Use existing `IX_Users_AdviserId` and `IX_Users_TenantId_Role` for assigned lists and target lookup. Do not add a script for the filter.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreateCustomer` | PublicId | R3–R7, R15–R16; current tenant; disabled tenant → 400 `disabled`/`tenant`; disabled adviser → 400 `disabled`/`user` |
| Command | `UpdateCustomer` | none | R17 load or 404; `rowVersion`; R3 / R8; TenantAdmin `adviserId` change → R15 + `ReassignAdviser` |
| Command | `DisableCustomer` | none | Same load; already Disabled → 204 skip domain; else `Disable()` |
| Command | `EnableCustomer` | none | Same load; already Active → 204 |
| Query | `GetCustomers` | paged envelope | R18; TenantAdmin may pass `adviserId=`; Adviser forced to self |
| Query | `GetCustomerById` | item | R17 else 404 |

Handlers read TenantId and the current person from `IUser` / `ICurrentUser`. Do not trust a tenant in the body. Scope follows policy: `customers.manage` → current tenant; only `customers.manage-own` → assigned.

---

## 8. API

`/users` is a namespace prefix. `{id}` = Customer PublicId.

Authorization: every verb is `customers.manage` **or** `customers.manage-own`. That or is not a new policy name.

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/users/customers` | manage **or** manage-own | 200 envelope | 400 query / 401 / 403 |
| GET | `/users/customers/{id}` | same | 200 item | 401 / 403 / 404 |
| POST | `/users/customers` | same | 201 `{ id }` | 400 (including `disabled`) / 401 / 403 / 404 (unknown adviser) |
| PUT | `/users/customers/{id}` | same | 204 | 400 / 401 / 403 / 404 / 409 |
| POST | `/users/customers/{id}/disable` | same | 204 | 400 / 401 / 403 / 404 / 409 |
| POST | `/users/customers/{id}/enable` | same | 204 | 400 / 401 / 403 / 404 / 409 |

### 8.1 Item

```json
{
  "id": "<publicId>",
  "tenantId": "<tenantPublicId>",
  "adviserId": "<adviserPublicId>",
  "name": "Jordan Lee",
  "email": "jordan@north.example",
  "status": "active",
  "rowVersion": "<opaque>",
  "created": "2026-09-14T00:00:00+00:00"
}
```

Do not return `role`, `identityUserId`, password, or internal ints. `status` matches `/users/me`.

### 8.2 List

`GET /users/customers?page=1&pageSize=20&enabledOnly=true&search=jordan&adviserId=<adviserPublicId>`

| Query | Default | Notes |
| --- | --- | --- |
| `page` | 1 | `< 1` → 400 |
| `pageSize` | 20 | Max 100 |
| `enabledOnly` | omitted / `false` = all | `true` = Active only |
| `search` | omitted | Trim; empty = omitted |
| `adviserId` | omitted | TenantAdmin filter; Adviser see R18 |

Envelope: `{ items, page, pageSize, totalCount }`. Reuse `PagedList<T>`.

### 8.3 Bodies

POST (TenantAdmin):

```json
{
  "name": "Jordan Lee",
  "email": "Jordan@north.example",
  "password": "Passw0rd!",
  "adviserId": "<adviserPublicId>"
}
```

POST (Adviser, `adviserId` optional):

```json
{
  "name": "Jordan Lee",
  "email": "Jordan@north.example",
  "password": "Passw0rd!"
}
```

PUT (TenantAdmin reassign):

```json
{
  "name": "Jordan Lee",
  "adviserId": "<otherAdviserPublicId>",
  "rowVersion": "<opaque>"
}
```

PUT (Adviser / rename only):

```json
{
  "name": "Jordan Lee",
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
  "detail": "Cannot create a Customer while the tenant is disabled. Enable the tenant first.",
  "code": "disabled",
  "target": "tenant",
  "targetId": "<tenantPublicId>"
}
```

Target Adviser disabled (create or reassign), same shape, `target=user`:

```json
{
  "title": "User is disabled",
  "status": 400,
  "detail": "Cannot create a Customer on a disabled adviser. Enable or pick another adviser.",
  "code": "disabled",
  "target": "user",
  "targetId": "<adviserPublicId>"
}
```

This is the first Phase-1 call site for `target=user`. The Adviser still exists; they are Disabled. Do not 404. The advisers “assigned customers still Active” 400 stays an ordinary validation error and does **not** use this shape.

Ordinary validation (missing field, email format, short password, Adviser assigning someone else, Adviser PUT with `adviserId`) stays on the FluentValidation `errors` dictionary. Do not set `code=disabled` on those.

Keep [api-design.md](../api-design.md) §7.4 pointing here for Customer field rules.

---

## 9. UI

None in this slice. The Adviser Portal Customers list / form belongs to the later-pages cut in [portals/adviser-portal.md](../portals/adviser-portal.md). Default home = Customers is a portal contract, not a deliverable of this slice.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `Create(Customer)` sets TenantId and AdviserId, password path → Active + `UserActivated` + `UserCreated`, and does **not** raise `CustomerAdviserReassigned`; `ReassignAdviser` writes the new id and raises the event once (customer + tenant + previous + new); same id → no event; non-Customer `ReassignAdviser` throws; `Disable()` on a Customer matches TenantAdmin |
| Application.FunctionalTests | No Bearer → 401 not 302; SystemAdmin / Customer → 403 on every verb; TenantAdmin create → 201, get `status=active` and the expected `adviserId`; hosted login against client `adviser-portal` with that tenant code + email + password **fails** (allow-list; Identity `CheckPassword` still succeeds); Adviser omit `adviserId` → 201 assigned to self; Adviser passes another `adviserId` → 400; duplicate email in the same tenant → 400; same email in two tenants → 201; `tenantId` in body → 400; create after current tenant disabled → 400 `disabled`/`tenant`; target Adviser Disabled → 400 `disabled`/`user`; unknown `adviserId` → 404; PUT with `email` → 400; Adviser PUT with `adviserId` → 400; TenantAdmin reassign to another Active Adviser in the tenant → 204; reassign to a Disabled Adviser → 400 `disabled`/`user`; GET this collection with an Adviser or TenantAdmin PublicId → 404; TenantAdmin of A GET/PUT/disable of B’s Customer → 404; Adviser B GET of Adviser A’s Customer → 404; disable → 204 and that subject’s refresh fails; enable → 204; stale `rowVersion` → 409; default paging; omitted `enabledOnly` includes disabled; `enabledOnly=true` is Active only; TenantAdmin `adviserId=` filters; Adviser `adviserId=` of someone else → 400 |
| Infrastructure.IntegrationTests | No new script. Two-tenant fixture: A and B each have an Adviser + Customer; A’s list does not include B; A requesting B’s Customer id → 404; two Advisers in one tenant, A does not see B’s assigned row |

Post-create password is verified with Identity `CheckPassword` (or equivalent). Do not invent a token endpoint on `webapi`. Hosted login on client `adviser-portal` for that Customer must **not** issue a code. `/users/me` as that Customer is not asserted on this client. Amendment 2026-09-14 (identity-auth R4 / R20).

---

## 11. Locked in this spec

No new ADR.

| Item | Lock |
| --- | --- |
| Scripts / policy | None; existing `customers.manage` **or** `customers.manage-own` |
| Caller | TenantAdmin: current tenant. Adviser: assigned only |
| Scope | Cross-tenant / not-assigned / wrong collection → 404 |
| Create | No body `tenantId`; password → Active; disabled tenant → 400 `disabled`/`tenant`; disabled adviser → 400 `disabled`/`user` |
| Reassign | TenantAdmin only; domain `ReassignAdviser`; pointer change raises `CustomerAdviserReassigned` |
| Event payload | `CustomerId`, `TenantId`, `PreviousAdviserId`, `NewAdviserId`. No actor / reason / as-of. Create does not raise it. Same adviser does not raise it |
| Event subscribers | None in this slice; no assignment-history table |
| Disable guard | None (ledger in Phase 2) |
| List | Reused envelope; `enabledOnly`; item includes `status` + `adviserId`; TenantAdmin may pass `adviserId=` |
| Portal | No page in this slice |
| Customer login | Login principal remains; `adviser-portal` issues no code; this collection 403 |

Still open, not this slice:

- Portal Customers page
- Admin password reset / invite
- Open-Account guard
- Customer Portal as a second client
- Audit persistence / as-of reporting (later subscribers of this event)

Locked 2026-09-14:

1. First §4.1 `target=user` call site = create or reassign onto a Disabled Adviser. Missing adviser stays 404. The advisers assigned-customer guard stays an ordinary 400.
2. An Adviser naming someone else as `adviserId` is ordinary 400, not 404.
3. Reassign raises `CustomerAdviserReassigned` with the payload above. This slice adds no handler and no assignment-history table.

---

## 12. Suggested commits

The repository should build after each commit.

1. `User.ReassignAdviser` + `CustomerAdviserReassigned` + Domain.UnitTests
2. `CreateCustomer` + dual-write + disabled-tenant 400 + disabled-adviser 400 `disabled`/`user` + uniqueness + Adviser self-only + 401/403
3. `GetCustomers` / `GetCustomerById` + paging + `enabledOnly` + `adviserId=` + cross-tenant 404 + assigned 404 + wrong-collection 404
4. `UpdateCustomer` + PUT 204 / TenantAdmin reassign / Adviser `adviserId` 400 / 409
5. `DisableCustomer` / `EnableCustomer` + HTTP idempotent + refresh fails (no account guard)
6. Hosted-login smoke after create; that Customer gets 403 on this collection and 200 on `/users/me`

Do not put portal pages or ledger tables in these commits.
