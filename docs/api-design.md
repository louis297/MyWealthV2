---
title: API design
status: draft
language: en
created: 2026-09-11
updated: 2026-09-13
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

Phase 1 closes the platform base: Adviser Portal + Scalar + the authorization-server hosted login. Ledger routes do not exist in this phase.

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

Phase 1 registers one public OIDC client: `adviser-portal`. A Customer may obtain tokens from the authorization server (tests / reserved). They cannot enter the Adviser Portal. Adviser-management resource routes stay 403. Customer Portal is a later second client; it is not registered in Phase 1.

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
| SystemAdmin | Hosted login, no tenantCode | `/tenants`, `/users/tenant-admins`, `/users/me`, `/currencies` |
| TenantAdmin | Hosted login + tenantCode | `/users/advisers`, tenant `/users/customers`, `/users/me`, `/currencies` |
| Adviser | Hosted login + tenantCode | Assigned `/users/customers`, `/users/me`, `/currencies` |
| Customer | May complete the authorization server (tests / reserved); no portal client | `/users/me` and `/currencies` only. `/users/advisers`, `/users/customers`, `/users/tenant-admins` → 403 |

Disable / enable for people: `POST /{id}/disable`, `POST /{id}/enable` (`Disabled` ↔ `Active`). Not a physical delete. Disabling an Adviser requires assigned non-disabled Customers to be handled first. Disabling the last TenantAdmin is allowed in Phase 1 (known gap).

Disabling a tenant does **not** bulk-update `User.Status`. Person status is independent; login checks both. After a tenant is disabled, that Code must not complete login and existing refresh must fail.

Password change, disable, and logout revoke that subject’s OpenIddict tokens. The User aggregate does not hold tokens.

---

## 4. Errors

| Case | HTTP | Who |
| --- | --- | --- |
| Validation / business rule (Adviser still has Customers, duplicate email, illegal reporting currency, …) | 400 | `webapi` |
| Missing / invalid Bearer; calling resources after refresh was revoked | 401 | `webapi` |
| Tenant disabled, Status ≠ Active, bad password, wrong tenant | Authorization does not complete (login failure to the caller; `webapi` returns 401 only if a dead token is presented) | Gate on `identity`; `webapi` only inspects the token |
| Authenticated but policy fails (including Customer on adviser-management routes) | 403 | `webapi` |
| Cross-tenant / cross-adviser / missing | 404 | `webapi` |
| `RowVersion` | 409 | `webapi` |
| Unhandled | 500 | Either host |

Whether login failures distinguish Disabled from bad credentials is **still open** (§9). Until locked, treat them as a uniform failure and do not enumerate the reason.

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
| End session | `GET /connect/logout` | Portal returns to its post-logout redirect |
| Userinfo | `GET /connect/userinfo` | Library default; Phase 1 portal mainly uses access-token claims |
| Hosted login | Password page on `identity` | email + password + tenantCode (SystemAdmin omits tenantCode). **Not** a `/connect` protocol endpoint. Razor Pages at `/login` |

**Phase 1 client**

| Item | Value |
| --- | --- |
| ClientId | `adviser-portal` |
| Type | Public client + PKCE |
| Grant | Authorization code + refresh. Password grant off |
| Redirect | Portal callback (local Vite origin + later deployed origin) |

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

Path `{id}` is always PublicId.

Phase 1 has **no** `/auth/*`, `/instruments`, `/accounts`, `/holdings`, `/transactions`, `/dashboard`, invitation, or forgot-password. Do not register ledger policy names in Phase 1.

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

Item: `{ code, name, decimalPlaces, isEnabled }`. Sorted by `code`. No “disabled-only” query. No pagination. No write API. No `GET /currencies/{code}`.

Any authenticated caller (default Authorize; no `currencies.read`). Seed: NZD, AUD, USD, EUR, GBP, JPY.

Field rules and `Money` live in [features/currencies.md](features/currencies.md).

### 7.3 Tenants

SystemAdmin only (`tenants.manage`).

| Method | Route | Success |
| --- | --- | --- |
| GET | `/tenants` | 200 paged list |
| GET | `/tenants/{id}` | 200 |
| POST | `/tenants` | 201 `{ id }` |
| PUT | `/tenants/{id}` | 204 |
| POST | `/tenants/{id}/disable` | 204 |
| POST | `/tenants/{id}/enable` | 204 |

- POST body: `name`, `code`, `reportingCurrency`
- PUT body: `name`, `reportingCurrency`, `rowVersion`
- `code` is immutable after create (login key). Reporting currency may change in this phase (no ledger balances keyed on it yet).
- After disable, that Code must not complete login; existing refresh fails. Person `Status` is not bulk-updated.

### 7.4 People (TenantAdmin / Adviser / Customer)

Create is a dual write: `AspNetUsers` first (`TenantId` projection), then `Users.IdentityUserId`. Responses never include the password. Create returns 201 `{ id }`.

Phase 1 create may set a password and land in `Active` (invitation is not built; that is not a reason to collapse the status machine to a boolean).

| Resource | Caller | Create required | List scope |
| --- | --- | --- | --- |
| `/users/tenant-admins` | SystemAdmin | `tenantId` (PublicId), `name`, `email`, `password` | Optional tenant filter |
| `/users/advisers` | TenantAdmin | `name`, `email`, `password` | Current tenant |
| `/users/customers` | TenantAdmin; Adviser (assigned) | `name`, `email`, `password`, `adviserId` | TenantAdmin: tenant; Adviser: self only |

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

Default list page size is **not locked** (§9).

---

## 8. Explicitly out (until a later phase promotes them)

- `/auth/login`, `/auth/refresh`, `/auth/logout` on `webapi`
- Password grant
- A second OIDC client in Phase 1
- Currency write APIs, per-tenant currency allow-lists, FX
- `/instruments`, `/accounts`, `/holdings`, `/transactions`, `/dashboard`
- Invitation, forgot-password, self-registration, MFA, external IdP
- Subdomain tenant resolution; Header-based SystemAdmin tenant switching
- Internal `int` ids in routes or JSON
- A catch-all `GET/POST /users`; fake children such as `/users/{id}/advisers`
- Putting `/tenants`, `/currencies`, or ledger routes under `/users`

When the Phase-2 ledger opens, add ledger policy names and ledger routes only. The session stack and these conventions stay.

---

## 9. Still open in Phase 1

- Default list page size

Locked in identity-auth: `AspNetUsers.UserName` = Domain `Users.PublicId`; uniform login failure; hosted login is Razor Pages at `/login`; access 15 minutes; refresh 14 days absolute; JWT claims `sub` / `email` / `role` / `tenant_id` / `tenant_code`.

---

## 10. Change log

| Date | Change |
| --- | --- |
| 2026-09-11 | First English draft. Two HTTP surfaces; OpenIddict default protocol paths; no resource-API token issuer; `/users` namespace for people collections; `/users/me` on `webapi`; Customer may complete the authorization server; no ledger routes in Phase 1. |
| 2026-09-12 | identity-auth locks: Razor `/login`; JWT claim names; 15 min access / 14 day absolute refresh; UserName = PublicId. |
| 2026-09-13 | Currencies: `enabledOnly` (omit/`false` = all, `true` = enabled only); item includes `isEnabled`. |
