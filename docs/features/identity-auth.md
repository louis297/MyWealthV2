---
title: Identity & Auth
status: accepted
phase: 1
language: en
owner: ""
created: 2026-09-12
last_updated: 2026-09-14
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../architecture.md
  - ../adr/0012-user-activation-invite-deferred.md
  - ../adr/0013-roles-authorization-single-user-table.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
---

# Identity & Auth

Session protocol, login gate, current user, and authorization policies. Tokens are issued by Aspire resource `identity` (`src/IdentityHost`, ADR 0014). `webapi` is the resource API only. Creating TenantAdmins, Advisers, and Customers belongs to later people slices. This slice owns types, the gate, policies, revocation, and `/users/me`.

---

## 1. Summary

The Adviser Portal redirects to hosted login on `identity` (email + password + tenantCode; SystemAdmin omits tenantCode) and completes authorization code + PKCE + revocable refresh. `webapi` validates Bearer tokens via discovery / JWKS and exposes `/users/me` plus password change. Only `UserStatus = Active` and an enabled tenant (when the person has one) may complete login.

A person may complete authorization only for a client that allows that `Users.Role`. After the password check succeeds and before IdentityHost issues an authorization code, the host reads `client_id` from the current authorize request and applies the allow-list. `adviser-portal` allows SystemAdmin, TenantAdmin, and Adviser. A Customer with a correct password does **not** receive a code for that client. Failure copy stays uniform (R3). Customer remains a login principal; tokens for that role wait for client `customer-portal`. Adviser-management routes stay 403 for Customer.

---

## 2. Scope

**In**

- Aspire resource `identity` (`src/IdentityHost`): OpenIddict **default protocol paths, do not remap**, plus hosted login
- One Phase-1 public client: `adviser-portal` (authorization code + PKCE + refresh; password grant off)
- Asymmetric signing; JWKS; `webapi` validates via discovery. `UseLocalServer()` is not the default validation path
- `GET/PUT /users/me`, `PUT /users/me/password` (resource API; does not issue tokens)
- JWT claims: `sub` = user PublicId, `email`, `role`, `tenant_id`, `tenant_code` (tenant claims empty for SystemAdmin). No internal ints. Permissions are not expanded into the token
- Scopes: `openid`, `profile`, `offline_access`, `api`. Role and tenant are claims, not scopes
- Refresh lives in `OpenIddictTokens`. Logout, password change, person disable, and tenant disable make that subject’s refresh fail
- Login gate: resolve Domain `User` by `tenantCode + email`, then verify the password on that row’s `IdentityUserId`. Turn off Identity `RequireUniqueEmail`
- Full `UserStatus` machine (Phase 1 create may set a password and land in Active; invite transition has no entry)
- Table `UserTokens` (hash only; no write API) + no-op `IEmailSender`
- Register Phase-1 named policies: `tenants.manage`, `tenants.read`, `tenant-admins.manage`, `advisers.manage`, `customers.manage`, `customers.manage-own`, `users.me`
- `PermissionHandler` + `RolePermissions` in code (ADR 0013)
- Current-user port (claims → Domain User + dual tenant check)
- Revocation port: `webapi` handles domain events and writes the shared OpenIddict store
- Dev seed: one SystemAdmin through `UserManager` (never raw hashes in SQL) + the client row (SQL or startup seed)

**Out**

- Custom `POST /auth/login`, `/auth/refresh`, `/auth/logout` as a token issuer
- Password grant; custom `RefreshTokens` table
- Second client `customer-portal`; a separate identity SQL database
- Invitation email, set-password links, forgot-password, MFA, external IdP, self-registration
- `AspNetRoles` / a permission table; permission claims in the JWT
- Ledger policy names
- People create / disable APIs (tenant-admins / advisers / customers)
- `GET /currencies` (next slice; this slice only supplies the authenticated gate)
- A password form on the Adviser Portal that issues tokens

---

## 3. Stories

1. As a TenantAdmin or Adviser I am redirected to `identity` (email + password + tenantCode) so I can call the resource API with access / refresh.
2. As a SystemAdmin I omit tenantCode so I can manage tenants and TenantAdmins from Scalar.
3. As any login-capable role I read and update my display name and change my password; old refresh fails after a password change.
4. As a Customer I remain a login principal (password + Identity). Completing `adviser-portal` hosted login fails even with a correct password. `GET /users/me` as that Customer waits for client `customer-portal`. Adviser-management HTTP stays 403.
5. As the platform, refresh for a subject must fail after that person is disabled, that tenant is disabled, the password changes, or the user logs out.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Non-SystemAdmin must submit a valid **enabled** tenantCode on hosted login. Compare Code case-insensitively. |
| R2 | Resolve `tenantCode + email` → one Domain `Users` row → verify the password on that row’s `IdentityUserId`. Do not `UserManager.FindByEmail` across tenants. Do not log in with PublicId + password. |
| R3 | `Status ≠ Active`, a disabled tenant, or a missing Identity link → authorization does not complete. Until login-failure codes are locked, return a uniform failure and do not enumerate the reason. |
| R4 | A person may obtain tokens only from a client that allows that role. IdentityHost applies the allow-list **after** the password check and **before** it issues an authorization code. `client_id` comes from the current `/connect/authorize` request. Do not invent a parallel `?portal=` query. A Customer receives no adviser-management policies (`RolePermissions`). HTTP 403 on adviser-management routes stays. The portal `/forbidden` page is a second line only. |
| R5 | Access is a short-lived JWT, signed asymmetrically. Refresh is revocable in the OpenIddict store. |
| R6 | All four roles may call `GET /users/me`. `PUT /users/me` updates `name` only (may include `rowVersion`). Email / Role / TenantId / Status / adviserId cannot change here. |
| R7 | Password change requires the current password. On success, revoke that subject’s OpenIddict tokens. The caller must run the authorization-code flow again. Do not return tokens. |
| R8 | The JWT does not expand permissions and does not carry internal ints. Subject / resource `id` are PublicId. |
| R9 | Protocol paths stay on OpenIddict defaults. Callers follow discovery. Business code does not hard-code those strings. |
| R10 | `AspNetUsers` adds one nullable `TenantId` (projection, **no FK**). Do not mirror DomainUserId / Role / DisplayName / Status. **Do not add a PublicId column.** |
| R11 | `Users.IdentityUserId` → `AspNetUsers.Id` is a one-way FK. No reverse database FK. `AspNetUsers.Id` is Identity’s own string primary key. It is **not** PublicId. |
| R12 | There is **one** PublicId: Domain `Users.PublicId`. On dual-write, generate the UUID first and write the **same string** to `Users.PublicId` and `AspNetUsers.UserName`. Two PublicId values are forbidden. |
| R13 | Hosted login lives on `identity` at **`/login`** (GET form / POST submit). Not `/connect/*`, `/account/*`, `/users/login`, or `/auth/login`. |
| R14 | `UserTokens` is created in Phase 1 with no write API and no domain behaviour. `IEmailSender` is a no-op. |
| R15 | `webapi` returns 401 on auth failure. It must **never** 302 to hosted login. Login-page cookies stay on the `identity` origin. |
| R16 | The schema applicator runs once (default: `webapi` startup). `identity` must not run a second applicator. Both hosts share `MyWealthDbV2` and the same Infrastructure mappings. |
| R17 | Phase 1 named policies are the seven names below. `GET /currencies` uses default `.RequireAuthorization()`. Do not register `currencies.read`. |
| R18 | Password change, person disable, tenant disable, and logout revoke tokens. The User aggregate does not hold tokens; domain events plus an application port do. |
| R19 | Identity may keep its `Email` column (framework). Uniqueness lives on Domain `Users`. |
| R20 | Client allow-list (Phase 1 implements the first row only): `adviser-portal` → SystemAdmin, TenantAdmin, Adviser. Reserved, do not register the clients: `customer-portal` → Customer; Back Office (name unlocked) → SystemAdmin. Wrong role → no code, uniform failure. Do not say “no identity”. |

`RolePermissions` (ADR 0013). This slice registers the map. Customer APIs ship later.

| Permission | SystemAdmin | TenantAdmin | Adviser | Customer |
| --- | --- | --- | --- | --- |
| `tenants.manage` | ✓ | | | |
| `tenants.read` | ✓ | ✓ | ✓ | |
| `tenant-admins.manage` | ✓ | | | |
| `advisers.manage` | | ✓ | | |
| `customers.manage` | | ✓ | | |
| `customers.manage-own` | | | ✓ | |
| `users.me` | ✓ | ✓ | ✓ | ✓ |

### 4.1 From token to permission

Permissions are not a list in the JWT and there is no `GET /users/me/permissions`.

```text
Bearer arrives at webapi
  → validate signature / issuer / expiry (JWKS)
  → read claims: sub = Users.PublicId, email, role, tenant_id, tenant_code
  → ICurrentUser loads Domain Users by PublicId
  → dual check: row TenantId ↔ tenant claims (both empty for SystemAdmin)
  → endpoint policy, e.g. advisers.manage
  → PermissionHandler: RolePermissions.Has(role, policy) — else 403
  → handler scope (this tenant / assigned customers) — else 404
```

The portal paints menus from `/users/me`.role. Role cannot change after create. Disable / password change revoke refresh; a short-lived access token dies on its own.

Login on `identity` is not this path:

```text
POST /login  { email, password, tenantCode }
  → Tenant by Code (enabled)
  → Users by TenantId + Email
  → AspNetUsers by Users.IdentityUserId
  → CheckPassword on that row
  → resume /connect/authorize → authorization code
  → portal exchanges code at /connect/token
```

`AspNetUsers.UserName` (the PublicId string) is not a login key.

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `User` | Aggregate | Person; status machine; one-way Identity link. This slice updates own Name and raises password events. It does not create other people |
| `UserRole` / `UserStatus` | Enum | Four roles; PendingActivation / Active / Disabled |
| `ApplicationUser` | Infrastructure | Maps `AspNetUsers`; only extra column is TenantId |
| OpenIddict stores / clients | Infrastructure | Not Domain types |
| `UserToken` | Infrastructure seam | No domain methods in Phase 1 |

Login-gate invariants (domain-model §4.2 / §4.4):

- Non-SystemAdmin: the Code must match an enabled tenant.
- That tenant’s User (or the global SystemAdmin) must exist, `Status = Active`, and `IdentityUserId` must be valid.
- SystemAdmin: empty `TenantId` / `AdviserId`; no tenantCode at login.
- The session protocol is not an aggregate.

Events this slice consumes (people slices raise the write-side events):

- `UserPasswordChanged` → revoke that subject
- `UserDisabled` → revoke that subject
- `TenantDisabled` → revoke every subject in that tenant
- `UserActivated` → no extra session action in Phase 1

Password change in this slice must raise `UserPasswordChanged`.

Keep [domain-model.md](../domain-model.md) in the same change when the model moves.

---

## 6. Database

| Table | Change | Indexes / FK |
| --- | --- | --- |
| `AspNetUsers` and Identity user-store siblings | add (no role tables) | Package defaults; `TenantId` int null, no FK |
| OpenIddict tables (package set) | add | Package defaults. Client row `adviser-portal` |
| `UserTokens` | add | Unique `TokenHash`; no PublicId / RowVersion; `IdentityUserId` / `TenantId` have no FK |
| `Tenants` | add; login gate needs `Code` + `IsEnabled` | Unique CI `Code` / `Name` / `PublicId`. **No `ReportingCurrency` column or FK** — currencies slice adds those |
| `Users` | add so `/users/me` and the SystemAdmin seed have a table | One-way `IdentityUserId` → `AspNetUsers`. No people CRUD in this slice |

This slice does **not** create `Currencies`, `ICurrencyCatalog`, or `GET /currencies`.

Scripts (database-design §9):

- `0001_schema_versions.sql`
- `0003_identity.sql` — user store + `AspNetUsers.TenantId`
- `0004_openiddict.sql` — package tables; optional scope / client seed
- `0005_tenants.sql` — no `ReportingCurrency`
- `0006_users.sql` — FKs to Tenants and AspNetUsers
- `0007_user_tokens.sql` — seam table

Do not add `0002_currencies.sql` here. Currencies + `Tenants.ReportingCurrency` are later forward-only scripts (`0008+`). People-management APIs are not this slice.

Password seed goes through `UserManager` only.

Keep [database-design.md](../database-design.md) in the same change when the tables move.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Query | `GetCurrentUser` | Profile DTO | Valid Bearer; load Domain User; JWT tenant claims match row TenantId (both empty for SystemAdmin) |
| Command | `UpdateCurrentUser` | none | `name` only; `rowVersion` conflict → 409 |
| Command | `ChangeCurrentUserPassword` | none | Current password; new password meets Identity options; raise `UserPasswordChanged`; revoke |
| identity host (not MediatR) | Hosted login POST | Resume authorize | R1–R3; do not leak why login failed |
| identity host | token / refresh / revocation / logout | Protocol | No password grant; revoked refresh fails at the token endpoint |
| Application service | `RolePermissions.Has` | bool | Four roles × six policies |
| Port | `ITokenRevocation.RevokeSubject` | none | OpenIddict store by Identity id / subject |
| Port | `IEmailSender` | none | No-op |
| Port | `IUser` / `ICurrentUser` | Current person | From claims; later handlers use it for scope |

Create-person commands are not this slice.

---

## 8. API

Protocol surface: [api-design.md](../api-design.md) §5. Default paths, do not remap:

| Surface | Path | Auth | Success | Failure |
| --- | --- | --- | --- | --- |
| Discovery | `GET /.well-known/openid-configuration` | Anonymous | 200 | |
| JWKS | `GET /.well-known/jwks` | Anonymous | 200 | |
| Authorize | `GET /connect/authorize` | Unauthenticated → `/login` | 302 / code | Uniform login failure |
| Token | `POST /connect/token` | Client + PKCE | access / refresh | 400; revoked refresh fails |
| Revocation | `POST /connect/revocation` | Protocol | 200 | |
| End session | `GET /connect/logout` | Session | Portal post-logout redirect | |
| Userinfo | `GET /connect/userinfo` | Access | 200 | 401 |
| Hosted login | `GET/POST /login` on `identity` | Public page | Resume `/connect/authorize` | Uniform failure |

Hosted login is **not** a `/connect` endpoint and does not live on `webapi`.

Resource API (`webapi`):

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/users/me` | `users.me` | 200 | 401 |
| PUT | `/users/me` | `users.me` | 204 | 400 / 401 / 409 |
| PUT | `/users/me/password` | `users.me` | 204 | 400 current or new password / 401 |

`GET /users/me` 200:

```json
{
  "id": "<publicId>",
  "name": "",
  "email": "",
  "role": "tenantAdmin",
  "status": "active",
  "tenantId": "<publicId or null>",
  "tenantCode": "<code or null>",
  "adviserId": "<publicId or null>",
  "rowVersion": "<opaque>"
}
```

`role` / `status` are camelCase enum names (`systemAdmin` / `tenantAdmin` / `adviser` / `customer`; `pendingActivation` / `active` / `disabled`). SystemAdmin: `tenantId`, `tenantCode`, and `adviserId` are null. Non-Customer: `adviserId` is null.

`PUT /users/me` body: `{ "name", "rowVersion" }`.

`PUT /users/me/password` body: `{ "currentPassword", "newPassword" }`. 204. No tokens in the body.

Phase 1 client:

| Item | Value |
| --- | --- |
| ClientId | `adviser-portal` |
| Type | Public + PKCE |
| Grant | Authorization code + refresh |
| Redirect | Portal callback (local Vite origin + later deployed origin) |

Keep [api-design.md](../api-design.md) in the same change when the catalog moves.

---

## 9. UI

Adviser Portal:

- Unauthenticated protected route → redirect to `identity` `/connect/authorize` (then `/login`)
- Callback stores access / refresh; later calls use Bearer only
- Logout: end session + revocation; clear local tokens
- **Do not** host a password form that issues tokens
- Profile: `/users/me`; change name; change password (current + new). After password change, run login again

Hosted login on `identity` at `/login`: email, password, tenantCode (optional for SystemAdmin). No forgot-password. Markup is **Razor Pages**; the path is `/login`.

SystemAdmin has no portal. Use Scalar and the same authorization flow. Do not build an admin shell.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | Legal `UserStatus` transitions; SystemAdmin / tenant role shape; login-gate function (disabled tenant, non-Active, missing Identity link → reject) |
| Application.FunctionalTests | Start `identity` and `webapi`. Tenant A’s email must not complete login with tenant B’s code. Refresh fails after password change or disable. Customer + correct password on client `adviser-portal` → **no authorization code** (uniform failure). TenantAdmin / Adviser / SystemAdmin on `adviser-portal` still complete the flow. Do **not** call `/users/advisers` from this slice. Missing Bearer on `/users/me` → 401, never 302. `RolePermissions.Has` covers four roles × seven policies (includes `tenants.read`). `PUT /users/me` cannot change email (400). `rowVersion` conflict → 409 |
| Infrastructure.IntegrationTests | After scripts: Identity + OpenIddict + UserTokens exist; no AspNetRoles; `adviser-portal` is discoverable; UserName / Email lookup does not hit another tenant |

Isolation in this slice is the login resolver: wrong tenantCode + correct email must fail.

---

## 11. Locked in this spec

Do not open a new ADR.

| Item | Lock |
| --- | --- |
| Login failure for Disabled vs bad password | Uniform failure (same hosted-login copy; no dedicated error code) |
| Access lifetime | 15 minutes |
| Refresh lifetime | 14 days **absolute** (not sliding) |
| Default list page size | Not this slice |
| Identity password options | Minimum length 8; other framework defaults |
| Hosted login markup | Razor Pages at `/login` |
| `AspNetUsers.UserName` | The same string as Domain `Users.PublicId` (R12) |
| JWT claim names | `sub` = Users.PublicId; `email`; `role`; `tenant_id`; `tenant_code` (empty for SystemAdmin). No permission claims, no internal ints |

Do not invent a second login protocol.

### Amendment 2026-09-14

Client × role allow-list (R4, R20). `tenants.read` added to the Phase-1 policy map (ADR 0013). Customer hosted-login success against `adviser-portal` is withdrawn; [customers.md](customers.md) smoke follows.

Suggested extra commits (do not mix with portal pages):

1. Allow-list on IdentityHost + uniform failure tests (Customer + `adviser-portal` issues no code; TenantAdmin still does)
2. Register `tenants.read` in `RolePermissions` (endpoint ships in the tenants amendment)

### Acceptance (amendment A)

Gate: password check has already succeeded. Client id is the `client_id` on the current `/connect/authorize` request. Do not add `?portal=`.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| A1 | Active Customer, enabled tenant, correct email + password + tenantCode, client `adviser-portal` | POST hosted `/login` that resumes authorize | No authorization code. Same hosted-login failure copy as a bad password (R3). Response is not 200 with a `code` query. |
| A2 | Same person as A1 | Identity `CheckPassword` (or test helper) on that `AspNetUsers` row | Succeeds. Failure in A1 is the allow-list, not a bad hash. |
| A3 | Active TenantAdmin, same tenant, correct password, client `adviser-portal` | Hosted login + authorize | Code issued. Token exchange works. `GET /users/me` 200, `role=tenantAdmin`. |
| A4 | Active Adviser, same setup as A3 | Hosted login + authorize | Code issued. `GET /users/me` 200, `role=adviser`. |
| A5 | Seed SystemAdmin, empty tenantCode, correct password, client `adviser-portal` | Hosted login + authorize | Code issued. `GET /users/me` 200, `role=systemAdmin`, tenant claims empty. |
| A6 | Active Customer, correct password, client `adviser-portal` | Compare the HTML/text of the login failure with a wrong-password attempt for an Active TenantAdmin | Copy matches. No string such as “no identity”, “wrong role”, or “customer cannot use this app”. |
| A7 | Active Customer after A1 | `GET {webapi}/users/me` with no token obtained from A1 | 401. Do not assert `/users/me` 200 for Customer on this client. |
| A8 | Clients `customer-portal` and Back Office | Discovery / OpenIddict client store | Those ClientId rows are **not** registered in Phase 1. Allow-list table exists in code for `adviser-portal` only; reserved rows are comments or a map with no seed client. |
| A9 | `RolePermissions` | `Has(TenantAdmin, tenants.read)` / `Has(Adviser, tenants.read)` / `Has(SystemAdmin, tenants.read)` / `Has(Customer, tenants.read)` | true, true, true, **false**. `tenants.manage` still SystemAdmin only. |
| A10 | Existing isolation | Tenant A email + tenant B code | Still fails before the allow-list (wrong tenant). Do not weaken R1–R3. |

Customers create smoke ([customers.md](customers.md) §10) must use A1 + A2, not “hosted login succeeds”.

---

## 12. Suggested commits

1. `ApplicationUser` (TenantId projection only) + Identity user store mapping + script `0003`
2. OpenIddict EF stores + script `0004` + `adviser-portal` client seed
3. IdentityHost: default protocol endpoints + `/login` gate (tenant enabled + Active + password)
4. `webapi`: JWKS Bearer validation + current-user port + dual tenant check
5. `/users/me` + password change + `UserPasswordChanged` + revocation port
6. Policy registration + `RolePermissions` + `PermissionHandler`
7. `UserTokens` script `0007` + no-op `IEmailSender`
8. Dual-host functional tests + cross-tenant login isolation
