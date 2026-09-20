---
title: API design
status: draft
language: en
created: 2026-09-11
updated: 2026-09-21
related:
  - README.md
  - glossary.md
  - function-plan.md
  - architecture.md
  - domain-model.md
  - database-design.md
  - adr/0014-openiddict-authorization-code-pkce.md
---

# API design

This document owns **HTTP conventions and the resource catalog**. Field-level contracts live in the Feature Spec for the slice. Scope and phasing live in [function-plan.md](function-plan.md). Session protocol detail lives in [ADR 0014](adr/0014-openiddict-authorization-code-pkce.md); this file only lists what each HTTP surface exposes.

**Product:** MyWealthV2.

Phase 1 closes the platform base: Adviser Portal + Scalar + the authorization-server hosted login. Phase 2 accepted so far: `/instruments`. Other ledger routes wait for their specs.

---

## 1. Two surfaces

Two processes, two HTTP surfaces. Do not add a custom `POST /auth/login` on `webapi` that issues tokens.

```text
Browser
  ├── redirect / callback / token / revocation / logout  →  identity (src/IdentityHost)
  └── Bearer resource calls                              →  webapi (src/Web)
```

| Host | Aspire name | Role |
| --- | --- | --- |
| `src/IdentityHost` | `identity` | Authorization server: OpenIddict default paths + hosted login |
| `src/Web` | `webapi` | Resource API: Minimal API → MediatR; Scalar; Bearer validation only |

The portal (`adviser-portal`) does not issue tokens. It redirects to `identity`, stores access / refresh after the callback, and calls `webapi` with Bearer.

Phase 1 registers one public OIDC client: `adviser-portal`. IdentityHost allows SystemAdmin, TenantAdmin, and Adviser on that client. A Customer with a correct password does not receive an authorization code for it. Customer Portal is a later second client; it is not registered in Phase 1. Adviser-management resource routes stay 403 for Customer.

---

## 2. Conventions (`webapi`)

Minimal API, TypedResults, endpoints dispatch MediatR only. Paths are kebab-case. JSON properties are camelCase (`System.Text.Json` default). No API versioning. Scalar at `/scalar`.

| Topic | Convention |
| --- | --- |
| Resource id | Paths and JSON use `PublicId` only. Internal `int` keys never appear in routes or JSON. Create response `{ id }` is the PublicId |
| Currency | Three-letter upper-case ISO code, not an enum |
| Login key | Non-SystemAdmin must send tenantCode on the **hosted login** page, not in a resource-API body |
| Tokens | Short-lived JWT access; refresh in the OpenIddict store. The resource API does **not** issue tokens and does **not** expose `/auth/refresh` |
| Concurrency | Writes to Tenant / User accept `rowVersion`; conflict → 409 |
| Customer | May complete the authorization-server flow; management routes → 403 |
| Authorization | Named policies on endpoints; `RolePermissions` mapped in code; assignment scope in the handler (ADR 0013) |
| CORS | Loose in Development; tighten in production without blocking Phase 1 |
| Docs | `[EndpointSummary]` / `[EndpointDescription]` on every action |
| People paths | `/users` is a **namespace prefix**, not a parent resource. People collections hang under it. Do not nest advisers / customers under `/users/{id}`. Tenants, currencies, and ledger routes stay outside this prefix |

Success codes:

| Action | HTTP |
| --- | --- |
| Create | 201 + `{ id }` (PublicId) |
| Update / disable / enable / password change | 204 |
| List / get one | 200 |
| Validation / business rule | 400 |
| Unauthenticated, invalid token, cannot complete login | 401 |
| Authenticated but policy fails | 403 |
| Cross-tenant / cross-adviser / missing | **404** (do not leak existence) |
| `RowVersion` | 409 |
| Unhandled | 500 |

`webapi` returns 401 on auth failure. It must **never** 302 to hosted login. Login-page cookies stay on the `identity` origin.

---

## 3. Authentication and visibility

The login gate is on `identity`: tenant enabled, `UserStatus = Active`, valid Identity link. The resource API then reads JWT claims and applies the dual tenant check.

JWT claims (permissions are not expanded): `sub` = user PublicId, `email`, `role`, `tenant_id` (nullable), `tenant_code` (nullable). No internal ints. Tenant claims are empty for SystemAdmin.

Protocol scopes (not permissions): `openid`, `profile`, `offline_access`, `api`. Role and tenant are claims, not scopes.

| Role | How tokens are obtained | Phase 1 resource scope |
| --- | --- | --- |
| SystemAdmin | Hosted login on `adviser-portal` (and later Back Office), no tenantCode | `/tenants` manage + `GET /tenants/by-code/{code}`, `/users/tenant-admins`, `/users/me`, `/currencies` |
| TenantAdmin | Hosted login on `adviser-portal` + tenantCode | `/users/advisers`, tenant `/users/customers`, `GET /tenants/by-code/{own}`, `/users/me`, `/currencies` |
| Adviser | Hosted login on `adviser-portal` + tenantCode | Assigned `/users/customers`, `GET /tenants/by-code/{own}`, `/users/me`, `/currencies` |
| Customer | Login principal; `adviser-portal` issues no code. Tokens wait for `customer-portal` | `/users/me` and `/currencies` only, on a client that allows Customer. `/users/advisers`, `/users/customers`, `/users/tenant-admins`, `/tenants` including by-code → 403 |

Disable / enable for people: `POST /{id}/disable`, `POST /{id}/enable` (`Disabled` ↔ `Active`). Not a physical delete. Disabling an Adviser requires assigned non-disabled Customers to be handled first. Disabling the last TenantAdmin is allowed in Phase 1 (known gap).

Disabling a tenant does **not** bulk-update `User.Status`. Person status is independent; login checks both. After a tenant is disabled, that Code must not complete login and existing refresh must fail.

Password change, disable, and logout revoke that subject’s OpenIddict tokens. The User aggregate does not hold tokens.

---

## 4. Errors

| Case | HTTP | Who |
| --- | --- | --- |
| Validation / business rule (Adviser still has Customers, duplicate email, illegal reporting currency, …) | 400 | `webapi` |
| Target exists but is disabled and the caller tries to create / attach / post on it (§4.1) | 400 `disabled` | `webapi` |
| Missing / invalid Bearer; calling resources after refresh was revoked | 401 | `webapi` |
| Tenant disabled, Status ≠ Active, bad password, wrong tenant | Authorization does not complete (login failure to the caller; `webapi` returns 401 only if a dead token is presented) | Gate on `identity`; `webapi` only inspects the token |
| Authenticated but policy fails (including Customer on adviser-management routes) | 403 | `webapi` |
| Cross-tenant / cross-adviser / missing | 404 | `webapi` |
| `RowVersion` | 409 | `webapi` |
| Unhandled | 500 | Either host |

Whether login failures distinguish Disabled from bad credentials is **still open** (§9). Until locked, treat them as a uniform failure and do not enumerate the reason. A disabled tenant or Disabled person at **hosted login** is not §4.1: that is a uniform failure on `identity`, not this JSON.

### 4.1 Target is disabled (shared error)

Tenant, User, and later Account can all be disabled. When the caller writes against a target that **exists but is disabled**, use one shape. Do not invent a new code per slice.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Tenant is disabled",
  "status": 400,
  "detail": "Cannot create a TenantAdmin while the tenant is disabled. Enable the tenant first.",
  "code": "disabled",
  "target": "tenant",
  "targetId": "<publicId>"
}
```

| Field | Rule |
| --- | --- |
| HTTP | 400. Unknown target is still 404. Not 409 / 403. |
| `code` | Always `disabled` |
| `target` | `tenant` / `user` / `account`. Phase 1 uses the first two; reserve `account` so Phase 2 does not change the contract |
| `targetId` | PublicId of the disabled target |
| `title` | Fixed per target: `Tenant is disabled` / `User is disabled` / `Account is disabled` |
| `detail` | This action (which command was rejected, what to enable) |
| `type` | Default RFC9110 400. Do not add a custom type URI namespace |

Phase 1 call site:

- `POST /users/tenant-admins` on a disabled tenant → `target=tenant` ([features/tenant-admins.md](features/tenant-admins.md))
- Create or reassign a Customer onto a disabled Adviser → `target=user` (detail in [features/customers.md](features/customers.md))
- Rejecting a disabled catalog currency as a **new** reporting currency stays a plain validation 400 (Currency has no PublicId and is not this lifecycle)

Implement as one Application `TargetDisabled` exception (kind + targetId + detail) mapped once in `webapi`. Do not assemble this JSON in three handlers.

List filters are separate: currencies and people collections use `enabledOnly` (omitted / `false` = all, `true` = enabled / Active only). Do not retrofit landed `GET /tenants?isEnabled=`.

---

## 5. Authorization server (`identity`)

Tables and package rules live in database design / ADR 0014. This section lists the caller-visible surface.

Protocol paths use **OpenIddict defaults. Do not remap.** Portals and `webapi` follow discovery; do not hard-code these strings in business code.

| Surface | Default path | Notes |
| --- | --- | --- |
| Discovery | `GET /.well-known/openid-configuration` | Authoritative endpoint table |
| JWKS | `GET /.well-known/jwks` | `webapi` validates Bearer; asymmetric signing |
| Authorize | `GET /connect/authorize` | Authorization code + PKCE. Unauthenticated callers hit hosted login |
| Token | `POST /connect/token` | Exchange for access / refresh; also used to refresh. No password grant |
| Revocation | `POST /connect/revocation` | Revoke refresh on logout (default name is revocation, not revoke) |
| End session | `GET/POST /connect/logout` | Portal returns to registered `{portalOrigin}/`. IdentityHost passthrough requires the SignOut action in identity-auth R21 |
| Userinfo | `GET /connect/userinfo` | Library default; Phase 1 portal mainly uses access-token claims |
| Hosted login | Password page on `identity` | email + password + tenantCode (SystemAdmin omits tenantCode). **Not** a `/connect` protocol endpoint. Razor Pages at `/login` |

**Phase 1 client**

| Item | Value |
| --- | --- |
| ClientId | `adviser-portal` |
| Type | Public client + PKCE |
| Grant | Authorization code + refresh. Password grant off |
| Redirect | `{portalOrigin}/callback` |
| Post-logout | `{portalOrigin}/` |

**Out (authorization server)**

- Custom `POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout` as a token issuer
- Resource Owner Password Credentials
- The later `customer-portal` client
- A separate identity SQL database
- Invitation, forgot-password, MFA, external IdP

Access lifetime is 15 minutes. Refresh lifetime is 14 days absolute. Hosted login is Razor Pages at `/login`. See identity-auth.

---

## 6. Phase 1 resource catalog (`webapi`)

`/users` is the people-management namespace:

```text
/users/me
/users/tenant-admins
/users/advisers
/users/customers
```

This is not “a User with child collections”. Do not use `/users/{id}/advisers`. Do not expose a catch-all `GET/POST /users` that branches on `role` in the body.

| Resource | Base path | Verbs | Policy |
| --- | --- | --- | --- |
| Current user | `/users/me` | GET, PUT, PUT password | `users.me` |
| Tenant admins | `/users/tenant-admins` | list / get / create / update / disable / enable | `tenant-admins.manage` |
| Advisers | `/users/advisers` | same | `advisers.manage` |
| Customers | `/users/customers` | same | `customers.manage` or `customers.manage-own` |
| Currencies | `/currencies` | GET | Authenticated |
| Tenants | `/tenants` | list / get / create / update / disable / enable | `tenants.manage` |
| Instruments | `/instruments` | list / get / create / update / disable / enable | `instruments.read` / `instruments.create` / `instruments.manage` |
| Accounts | `/accounts` | list / get / create / update / close / reopen | `accounts.read` / `accounts.create` / `accounts.manage` |

Path `{id}` is always PublicId.

Phase 1 has **no** `/auth/*`, `/holdings`, `/transactions`, `/dashboard`, invitation, or forgot-password. `/instruments` is the first Phase-2 ledger route (landed). `/accounts` is the second (accepted; not landed until the accounts slice ships).

---

## 7. Endpoint notes

Field rules belong in each Feature Spec. This section only locks the catalog and cross-slice conventions.

### 7.1 Current user

| Method | Route | Success |
| --- | --- | --- |
| GET | `/users/me` | 200 profile |
| PUT | `/users/me` | 204 |
| PUT | `/users/me/password` | 204 |

- Every authenticated role may call these (including Customer and SystemAdmin).
- Profile PUT updates `name` only (may include `rowVersion`). Email / Role / TenantId / Status cannot change here.
- Password change requires the current password. On success, revoke that subject’s OpenIddict tokens. The caller must run the authorization-code flow again.
- Not a token endpoint. Do not return tokens.

### 7.2 Currencies

`GET /currencies` and `GET /currencies?enabledOnly=true|false`.

| `enabledOnly` | Behaviour |
| --- | --- |
| Omitted or `false` | All rows (including disabled) |
| `true` | Enabled rows only |
| Any other value | 400 |

Item: `{ code, name, decimalPlaces, isActive }`. Sorted by `code`. No “disabled-only” query. No pagination. No write API. No `GET /currencies/{code}`. Column/JSON amendment 2026-09-21: `IsEnabled` → `IsActive`.

Any authenticated caller (default Authorize; no `currencies.read`). Seed: NZD, AUD, USD, EUR, GBP, JPY.

Field rules and `Money` live in [features/currencies.md](features/currencies.md).

### 7.3 Tenants

Manage verbs: SystemAdmin only (`tenants.manage`).

Lookup: `GET /tenants/by-code/{code}` uses `tenants.read` (SystemAdmin, TenantAdmin, Adviser). TenantAdmin / Adviser may read only their own code (else 404). Customer → 403.

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/tenants/by-code/{code}` | `tenants.read` | 200 item |
| GET | `/tenants` | `tenants.manage` | 200 paged list |
| GET | `/tenants/{id}` | `tenants.manage` | 200 |
| POST | `/tenants` | `tenants.manage` | 201 `{ id }` |
| PUT | `/tenants/{id}` | `tenants.manage` | 204 |
| POST | `/tenants/{id}/disable` | `tenants.manage` | 204 |
| POST | `/tenants/{id}/enable` | `tenants.manage` | 204 |

- POST body: `name`, `code`, `reportingCurrency`
- PUT body: `name`, `reportingCurrency`, `rowVersion`
- `code` is immutable after create (login key). Reporting currency may change in this phase (no ledger balances keyed on it yet).
- After disable, that Code must not complete login; existing refresh fails. Person `Status` is not bulk-updated.
- Field rules: [features/tenants.md](features/tenants.md).

### 7.4 People (TenantAdmin / Adviser / Customer)

Create is a dual write: `AspNetUsers` first (`TenantId` projection), then `Users.IdentityUserId`. Responses never include the password. Create returns 201 `{ id }`.

Phase 1 create may set a password and land in `Active` (invitation is not built; that is not a reason to collapse the status machine to a boolean).

| Resource | Caller | Create required | List scope |
| --- | --- | --- | --- |
| `/users/tenant-admins` | SystemAdmin | `tenantId` (PublicId), `name`, `email`, `password` | Optional tenant filter. Field rules: [features/tenant-admins.md](features/tenant-admins.md) |
| `/users/advisers` | TenantAdmin | `name`, `email`, `password` | Current tenant. Field rules: [features/advisers.md](features/advisers.md) |
| `/users/customers` | TenantAdmin; Adviser (assigned) | `name`, `email`, `password`, `adviserId` | TenantAdmin: tenant; Adviser: self only. Field rules: [features/customers.md](features/customers.md) |

Shared actions (`{collection}` = `tenant-admins` / `advisers` / `customers`):

| Method | Route | Success |
| --- | --- | --- |
| GET | `/users/{collection}` | 200 paged; simple search on name / email |
| GET | `/users/{collection}/{id}` | 200 |
| POST | `/users/{collection}` | 201 `{ id }` |
| PUT | `/users/{collection}/{id}` | 204 (`name`; Customer `adviserId` is TenantAdmin only) |
| POST | `/users/{collection}/{id}/disable` | 204 |
| POST | `/users/{collection}/{id}/enable` | 204 |

Rule summary:

- Email is unique inside a tenant. SystemAdmin email is unique globally. The same email may exist in two tenants.
- Role / TenantId / Email cannot change after create.
- Disable Adviser: reject with 400 while any assigned Customer is not Disabled.
- Disable Customer: no ledger guard in Phase 1.
- An Adviser creating a Customer may only set `adviserId` to self; otherwise 400.
- An Adviser reading another Adviser’s Customer → 404. Cross-tenant is always 404.
- Customer calling `/users/advisers`, `/users/customers`, `/users/tenant-admins`, or `/tenants` → 403.
- Disable and password change revoke that person’s OpenIddict tokens.

People lists reuse the tenants envelope `{ items, page, pageSize, totalCount }` (page default 1, size default 20, max 100). Currencies, people collections, and instruments use `enabledOnly` (omit / `false` = all, `true` = enabled / Active only). Each person item includes `status`. Create on a disabled tenant uses §4.1.

### 7.5 Instruments

Field rules: [features/instruments.md](features/instruments.md).

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/instruments` | `instruments.read` | 200 envelope |
| GET | `/instruments/{id}` | `instruments.read` | 200 item |
| POST | `/instruments` | `instruments.create` | 201 `{ id }` |
| PUT | `/instruments/{id}` | `instruments.manage` | 204 |
| POST | `/instruments/{id}/disable` | `instruments.manage` | 204 |
| POST | `/instruments/{id}/enable` | `instruments.manage` | 204 |

- TenantAdmin / Adviser: current tenant. Body / list query must omit `tenantId`.
- SystemAdmin: list and create require tenant PublicId. Get / PUT / disable / enable may cross tenants.
- Customer → 403.
- Item includes `id`, `tenantId`, `symbol`, `name`, `quoteCurrency`, `isActive`, `rowVersion`. No price.
- Create on a disabled tenant → §4.1 `target=tenant`.

### 7.6 Accounts

Field rules: [features/accounts.md](features/accounts.md).

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/accounts` | `accounts.read` | 200 envelope |
| GET | `/accounts/{id}` | `accounts.read` | 200 item |
| POST | `/accounts` | `accounts.create` | 201 `{ id }` |
| PUT | `/accounts/{id}` | `accounts.manage` | 204 |
| POST | `/accounts/{id}/close` | `accounts.manage` | 204 |
| POST | `/accounts/{id}/reopen` | `accounts.manage` | 204 |

- TenantAdmin / Adviser: current tenant. Body / list query must omit `tenantId`. Adviser: assigned Customers only.
- SystemAdmin: list and create require tenant PublicId. Get / PUT / close / reopen may cross tenants.
- Customer → 403.
- Item includes `id`, `tenantId`, `customerId`, `name`, `type`, `currency`, `status`, `isActive`, `rowVersion`. No balance.
- Create on a disabled tenant → §4.1 `target=tenant`. Create or reopen on a Disabled Customer → §4.1 `target=user`.

---

## 8. Explicitly out (until a later phase promotes them)

- `/auth/login`, `/auth/refresh`, `/auth/logout` on `webapi`
- Password grant
- A second OIDC client in Phase 1
- Currency write APIs, per-tenant currency allow-lists, FX
- `/holdings`, `/transactions`, `/dashboard`
- Invitation, forgot-password, self-registration, MFA, external IdP
- Subdomain tenant resolution; Header-based SystemAdmin tenant switching
- Internal `int` ids in routes or JSON
- A catch-all `GET/POST /users`; fake children such as `/users/{id}/advisers`
- Putting `/tenants`, `/currencies`, or ledger routes under `/users`

Further ledger routes wait for their Feature Specs. The session stack and these conventions stay.

---

## 9. Still open in Phase 1

List page size is locked: tenants envelope (page 1 / size 20 / max 100), reused by people lists. `enabledOnly` is locked for currencies and people collections.

Locked in identity-auth: `AspNetUsers.UserName` = Domain `Users.PublicId`; uniform login failure; hosted login is Razor Pages at `/login`; access 15 minutes; refresh 14 days absolute; JWT claims `sub` / `email` / `role` / `tenant_id` / `tenant_code`.

---

## 10. Change log

| Date | Change |
| --- | --- |
| 2026-09-11 | First English draft. Two HTTP surfaces; OpenIddict default protocol paths; no resource-API token issuer; `/users` namespace for people collections; `/users/me` on `webapi`; Customer may complete the authorization server; no ledger routes in Phase 1. |
| 2026-09-12 | identity-auth locks: Razor `/login`; JWT claim names; 15 min access / 14 day absolute refresh; UserName = PublicId. |
| 2026-09-13 | Currencies: `enabledOnly` (omit/`false` = all, `true` = enabled only); item includes `isEnabled`. |
| 2026-09-13 | §4.1 shared disabled error (`code=disabled` + `target`). People lists reuse tenants envelope; `enabledOnly` aligned with currencies. TenantAdmin field rules in [features/tenant-admins.md](features/tenant-admins.md). |
| 2026-09-14 | Adviser field rules in [features/advisers.md](features/advisers.md). Disable-adviser assigned-customer 400 is ordinary validation, not §4.1. |
| 2026-09-14 | Customer field rules in [features/customers.md](features/customers.md). Client allow-list: `adviser-portal` issues no Customer code. `GET /tenants/by-code/{code}` + `tenants.read`. |
| 2026-09-20 | `/instruments` catalog + §7.5. SystemAdmin list/create take tenant PublicId. Field rules in [features/instruments.md](features/instruments.md). Landed `9ea2f2a`. |
| 2026-09-21 | Catalog JSON `isEnabled` → `isActive`. Column rename script `0011`. People list/get JSON adds `isActive`. |
