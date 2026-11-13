---
title: Identity & Auth
status: accepted
phase: 1
language: en
owner: ""
created: 2026-09-12
last_updated: 2026-11-13
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

The Adviser Portal session is completed by `bff-adviser-portal` (ADR 0016, accepted). This file still owns the issuer, the login gate, policies, and `/users/me`. Who redeems the code is the amendment below, not the Phase 1 public-client sentences.

A person may complete authorization only for a client that allows that `Users.Role`. After the password check succeeds and before IdentityHost issues an authorization code, the host reads `client_id` from the current authorize request and applies the allow-list. `adviser-portal` allows SystemAdmin, TenantAdmin, and Adviser. A Customer with a correct password does **not** receive a code for that client. Failure copy stays uniform (R3). Customer remains a login principal; tokens for that role wait for client `customer-portal`. Adviser-management routes stay 403 for Customer.

---

## 2. Scope

**In**

- Aspire resource `identity` (`src/IdentityHost`): OpenIddict **default protocol paths, do not remap**, plus hosted login
- One Phase-1 client: `adviser-portal` (authorization code + PKCE + refresh; password grant off). Holder is `bff-adviser-portal` (amendment 2026-10-01). Phase 1 landed it as a public browser client.
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
| R18 | Password change, person disable, tenant disable, and logout revoke tokens. Disable / password change use domain events plus `ITokenRevocation`. Logout revokes on `identity` (R21). The User aggregate does not hold tokens. |
| R19 | Identity may keep its `Email` column (framework). Uniqueness lives on Domain `Users`. |
| R20 | Client allow-list (Phase 1 implements the first row only): `adviser-portal` → SystemAdmin, TenantAdmin, Adviser. Reserved, do not register the clients: `customer-portal` → Customer; Back Office (name unlocked) → SystemAdmin. Wrong role → no code, uniform failure. Do not say “no identity”. |
| R21 | End session is OpenIddict `GET/POST /connect/logout`. While `EnableEndSessionEndpointPassthrough` is on, IdentityHost **must** expose a matching action: `SignInManager.SignOutAsync()` then `SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)`. That clears the hosted-login cookie. The portal sends `client_id` and `post_logout_redirect_uri` (registered `{portalOrigin}/`). Revoke the refresh token at `POST /connect/revocation` (`token_type_hint=refresh_token`) before that redirect, or revoke the subject inside the action. Do **not** add `/auth/logout`. Do not remap the path. |

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
| `Tenants` | add; login gate needs `Code` + active flag | Unique CI `Code` / `Name` / `PublicId`. Shipped as `IsEnabled`; `0011` renames to `IsActive`. **No `ReportingCurrency` column or FK** — currencies slice adds those |
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
| End session | `GET/POST /connect/logout` | Identity cookie | 302 to registered `post_logout_redirect_uri` | 400 if that URI is not registered (OpenIddict `ID2052`) |
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
| Redirect | `{portalOrigin}/callback` |
| Post-logout | `{portalOrigin}/` — must be on the OpenIddict client `PostLogoutRedirectUris` |

Keep [api-design.md](../api-design.md) in the same change when the catalog moves.

---

## 9. UI

Adviser Portal:

- Unauthenticated protected route → redirect to `identity` `/connect/authorize` (then `/login`)
- Callback stores access / refresh; later calls use Bearer only
- Logout: portal clears local tokens, revokes refresh at `/connect/revocation`, then discovery end-session. IdentityHost R21 must sign out the hosted-login cookie so the next visit is `/login`, not a silent code. `/callback` redeems a given authorization code **once** (React StrictMode must not produce OpenIddict `ID2010`). |
- **Do not** host a password form that issues tokens
- Profile: `/users/me`; change name; change password (current + new). After password change, run login again

Hosted login on `identity` at `/login`: email, password, tenantCode (optional for SystemAdmin). No forgot-password. Markup is **Razor Pages**; the path is `/login`.

SystemAdmin has no portal. Use Scalar and the same authorization flow. Do not build an admin shell.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | Legal `UserStatus` transitions; SystemAdmin / tenant role shape; login-gate function (disabled tenant, non-Active, missing Identity link → reject) |
| Application.FunctionalTests | Start `identity` and `webapi`. Tenant A’s email must not complete login with tenant B’s code. Refresh fails after password change or disable. Customer + correct password on client `adviser-portal` → **no authorization code** (uniform failure). TenantAdmin / Adviser / SystemAdmin on `adviser-portal` still complete the flow. Do **not** call `/users/advisers` from this slice. Missing Bearer on `/users/me` → 401, never 302. `RolePermissions.Has` covers four roles × seven policies (includes `tenants.read`). `PUT /users/me` cannot change email (400). `rowVersion` conflict → 409. `GET /connect/logout` with `client_id=adviser-portal` and a registered `post_logout_redirect_uri` → 302 to that URI, never 400 when the URI is registered |
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

### Amendment 2026-10-01 (BFF holder + refresh rotation)

Accepted with [bff-adviser-portal.md](bff-adviser-portal.md) and ADR 0016. Landed with the BFF slice (`5e85e9c`): `OpenIddictSeeder` upserts `adviser-portal` as confidential from `Identity:AdviserPortalClientSecret`. Do not rewrite R1–R21 in place. Phase 1 public-client sentences above are historical; this amendment supersedes the holder.

- `adviser-portal` is confidential. Secret in Aspire config. PKCE stays. The BFF exchanges the code at `/connect/token`. The browser does not.
- Redirect `{bffOrigin}/signin-oidc`. Post-logout `{bffOrigin}/`.
- R21 still owns the identity end-session action. The caller of revocation + end-session is the BFF.
- Access lifetime stays 15 minutes. Refresh stays 14 days absolute. Issuing a new refresh revokes the refresh that was presented.
- Allow-list unchanged. SystemAdmin still receives a code. Customer still does not.
- No new table. No new ADR beyond 0016.

### Amendment 2026-10-25 (Back Office client)

Accepted with [backoffice/back-office.md](../backoffice/back-office.md). Do not rewrite R1–R21 or A8 in place. A8’s “Back Office is not registered” sentence is historical.

- Client `back-office` is confidential. Secret `Identity:BackOfficeClientSecret` comes from the Aspire parameter `back-office-client-secret` (not in git). PKCE stays. The Blazor host exchanges the code. The browser does not.
- Allow-list row: `back-office` → SystemAdmin only. TenantAdmin, Adviser, and Customer receive no code. Failure copy stays uniform.
- Redirect `{backOfficeOrigin}/signin-oidc`. Post-logout `{backOfficeOrigin}/`. The origin is `Identity:BackOfficeOrigin`. Do not add it to `Identity:PortalOrigins`; that list stays the `adviser-portal` client.
- `customer-portal` stays unregistered.
- No BFF. The host holds the cookie session and the access and refresh tokens, and calls `webapi` with Bearer. No new table. No new resource route.

### Amendment 2026-10-27 (Per-client identity cookies)

Accepted. Isolates `adviser-portal` and `back-office` on the identity origin so a login on one client does not silently authenticate the other. Do not rewrite R1–R21 or earlier amendments in place. No new table. No protocol-path remap. No second identity host. Clients do not send a special parameter; isolation is enforced by IdentityHost.

**Problem.** IdentityHost previously used one authentication cookie on the `identity` origin. `/connect/authorize` for either client read that cookie. A later login overwrote it, so both clients received the last authenticated subject (silent SSO). `prompt=login` is a client hint and is not the control.

**Decision.** IdentityHost registers a distinct cookie authentication scheme per first-party client. Scheme selection is by `client_id` on the current authorize or logout request.

| ClientId | Cookie scheme (name) | Cookie name (example) |
| --- | --- | --- |
| `adviser-portal` | `adviser-portal` | `.AspNetCore.Identity.AdviserPortal` |
| `back-office` | `back-office` | `.AspNetCore.Identity.BackOffice` |

Exact cookie names may follow the framework default for a named scheme. They must differ. No `Domain` attribute. `Path=/`. HttpOnly. `SameSite=Lax`. `Secure` outside Development.

**Authorize (`/connect/authorize`).** Read `client_id`. Authenticate only against that client’s scheme. If that scheme has no valid cookie, redirect to hosted `/login`. Do not fall back to the other client’s cookie. Do not honour a shared default scheme for these two clients.

**Hosted login (`/login`).** The page receives the client context from the authorize return (the OpenIddict authorize request that was interrupted). After the password check and the client × role allow-list succeed, `SignInAsync` uses the scheme for that `client_id` only. SystemAdmin still omits `tenantCode`. Allow-list is unchanged: `adviser-portal` → SystemAdmin / TenantAdmin / Adviser; `back-office` → SystemAdmin only. Wrong role → no code, uniform failure.

**Logout (R21).** `/connect/logout` receives `client_id`. Sign out only that client’s scheme (`SignInManager.SignOutAsync` against the matching scheme, then OpenIddict SignOut). The other client’s cookie stays. Revocation of the presented refresh is unchanged. Post-logout redirect stays the registered URI for that client.

**What does not change.**

- BFF cookie (`__Host-bff-adviser-portal`) and Back Office host cookie stay on their own origins. Access and refresh stay server-side on those hosts.
- Protocol paths stay OpenIddict defaults. No custom `/auth/login`.
- Refresh store, lifetimes, and revocation events (password change, person disable, tenant disable) stay as identity-auth.
- `customer-portal` stays unregistered until the 2026-11-02 amendment. When registered, it gets its own scheme the same way.

**Acceptance (amendment A14–A17).**

| Id | Given | When | Then |
| --- | --- | --- | --- |
| A14 | Active TenantAdmin logged in on `adviser-portal` (identity cookie for that scheme set) | Browser opens Back Office (client `back-office`) | Back Office does not silently complete. Hosted `/login` is shown. No authorization code until SystemAdmin credentials succeed on the `back-office` scheme. |
| A15 | Seed SystemAdmin logged in on `back-office` | Browser opens Adviser Portal (client `adviser-portal`) | Portal does not silently complete as SystemAdmin. Hosted `/login` is shown for `adviser-portal`. |
| A16 | Both clients have a valid identity cookie for different subjects | Sign out on Adviser Portal (`client_id=adviser-portal`) | Only the `adviser-portal` scheme cookie is cleared. Back Office identity cookie remains. Next Back Office visit does not force login. |
| A17 | Authorize request for `back-office` with a valid `adviser-portal` cookie present and no `back-office` cookie | IdentityHost processes `/connect/authorize` | Does not issue a code from the `adviser-portal` cookie. Redirects to `/login` for `back-office`. |

### Amendment 2026-11-02 (Customer Portal client)

Accepted with [bff-customer-portal.md](bff-customer-portal.md) and [../customer-portal/customer-portal.md](../customer-portal/customer-portal.md). Do not rewrite R1–R21 or earlier amendments in place. No new table. No protocol-path remap. No second identity host.

- Client `customer-portal` is confidential. Secret `Identity:CustomerPortalClientSecret` comes from the Aspire parameter `customer-portal-client-secret` (not in git). PKCE stays. The BFF exchanges the code. The browser does not.
- Allow-list row: `customer-portal` → Customer only. TenantAdmin, Adviser, and SystemAdmin receive no code. Failure copy stays uniform.
- Redirect `{bffOrigin}/signin-oidc`. Post-logout `{bffOrigin}/`. The origin is the `bff-customer-portal` https browser entry.
- Per-client cookie scheme: `customer-portal` (name example `.AspNetCore.Identity.CustomerPortal`). Same rules as the 2026-10-27 amendment. Authorize and hosted login authenticate / sign-in only against this scheme for `client_id=customer-portal`. Logout clears only this scheme.

**Acceptance (amendment A18–A21).**

| Id | Given | When | Then |
| --- | --- | --- | --- |
| A18 | Active Customer + correct password | Hosted login for client `customer-portal` | Authorization code issued. BFF can redeem. |
| A19 | Active TenantAdmin / Adviser / SystemAdmin + correct password | Hosted login for client `customer-portal` | No authorization code. Uniform failure. |
| A20 | Active Customer logged in on `customer-portal` (identity cookie for that scheme set) | Browser opens Adviser Portal (client `adviser-portal`) | Adviser Portal does not silently complete as Customer. Hosted `/login` is shown. |
| A21 | Both `customer-portal` and `adviser-portal` have a valid identity cookie | Sign out on Customer Portal (`client_id=customer-portal`) | Only the `customer-portal` scheme cookie is cleared. Adviser Portal identity cookie remains. |

### Amendment 2026-11-11 (SystemAdmin leaves `adviser-portal`)

Accepted 2026-11-11. Supersedes the `adviser-portal` row in §1, R20, A5, the §10 sentence “TenantAdmin / Adviser / SystemAdmin on `adviser-portal` still complete the flow”, the 2026-10-01 line “SystemAdmin still receives a code”, and the 2026-10-27 allow-list sentence. Do not rewrite those in place.

**Why.** SystemAdmin was on the `adviser-portal` allow-list so it could get an access token before a Back Office existed. Back Office (amendment 2026-10-25) is now the SystemAdmin client. The Adviser Portal has no SystemAdmin screens.

- Allow-list row: `adviser-portal` → TenantAdmin, Adviser. SystemAdmin with a correct password receives no code. Failure copy stays uniform (R3).
- `back-office` → SystemAdmin only (unchanged). SystemAdmin still omits `tenantCode` on hosted login. `customer-portal` → Customer only (unchanged).
- Resource API policies do not change. SystemAdmin keeps every permission in ADR 0013. Back Office and Scalar call the same routes. `webapi` does not read `client_id`.
- The Adviser Portal SPA treats a leftover SystemAdmin session like a leftover Customer session: `/forbidden`, and no `GET /tenants/by-code/{code}` call.
- Known gap: the token endpoint does not re-run the allow-list on a refresh grant. A SystemAdmin refresh token issued on `adviser-portal` before this change keeps working until it is revoked or reaches the 14-day absolute lifetime.
- No new table. No new route. No schema change.

**Acceptance (amendment A22–A24).**

| Id | Given | When | Then |
| --- | --- | --- | --- |
| A22 | Seed SystemAdmin + correct password, no `tenantCode` | Hosted login for client `adviser-portal` (direct or through `bff-adviser-portal`) | No authorization code. Uniform failure. No BFF cookie. |
| A23 | Same person as A22 | Hosted login for client `back-office` | Authorization code issued. Access token works on SystemAdmin routes. |
| A24 | Leftover SystemAdmin session in the Adviser Portal | Open `/`, `/profile`, or `/session` | `/forbidden`. No Profile page. No `by-code` call. |

### Amendment 2026-11-11 (revocation is delivered by the outbox)

Records behaviour that landed with [outbox.md](outbox.md) (`e9b8b81`). Do not rewrite R5, story 5 or §10 in place; this amendment supersedes their timing.

- `UserPasswordChanged`, `UserDisabled` and `TenantDisabled` are written to `OutboxMessages` in the same transaction as the change. The `worker` publishes them after commit, and the revocation handlers run then. The change request returns before any token is revoked.
- Until the event is delivered, an existing refresh token for that subject still works. The token endpoint does not re-check `Users.Status` or the tenant flag on a refresh grant. The window is the outbox poll interval plus retries. A dead-lettered event leaves the refresh token valid until its 14-day absolute lifetime.
- New hosted login is blocked at once: login and `/connect/authorize` check `LoginEligibility` on every request.
- Logout is unchanged and synchronous: the presented refresh token is revoked at `/connect/revocation` (A13).
- Tests: the functional host does not run the outbox worker. Functional tests for password change and disable assert that refresh **still succeeds** right after the call. The revocation handlers and repeat delivery are covered in Infrastructure.IntegrationTests (`RevokeTokensIdempotencyTests`, `RevokeTokensTenantScopeTests`).
- No new table. No new route. No schema change.

### Amendment 2026-11-12 (bootstrap SystemAdmin)

Status of this amendment: `accepted` (2026-11-12). It is a build contract. Landed 2026-11-13: `SystemAdminBootstrap` (`src/Infrastructure/Identity`), called from `src/Web/Program.cs` after the Development seed block, so a first Development start finds the seeded SystemAdmin and logs no warning. Item 6 of [deployment.md](../deployment.md) §13. Do not rewrite R1–R21 or earlier amendments in place.

**Why.** A SystemAdmin exists only through `DevelopmentIdentitySeeder`. A production database starts with none, so nobody can sign in to Back Office to create the first tenant. No route creates a SystemAdmin, and the owner should not write identity rows by hand in SQL.

- **Where.** `webapi`, on start, right after the schema applicator and the currency catalog reload, before it listens. `webapi` already owns the schema and the Development seed; `identity` starts only after `webapi` is healthy (deployment §8).
- **Keys.** `Bootstrap:SystemAdmin:Email` and `Bootstrap:SystemAdmin:Password` (environment `Bootstrap__SystemAdmin__Email`, `Bootstrap__SystemAdmin__Password`). Optional `Bootstrap:SystemAdmin:Name`, default `System Admin`.
- **Any environment.** The bootstrap runs in every environment. In Development the seeded SystemAdmin already exists, so it does nothing (A27).
- **One step, idempotent.** It counts SystemAdmins in any `UserStatus`. When at least one exists, it does nothing and does not read the password. It never changes, re-enables or resets an existing person. Only one `webapi` instance runs, so no lock is needed.
- **Create.** When none exists and both keys are set, it creates one person with role SystemAdmin, no tenant, no adviser, `Status` Active, email confirmed, the given name, email and password. Domain `User` and identity user are written together, the same way the Development seed writes them (dual-write). After that, hosted login for client `back-office` works for that person with no `tenantCode` (unchanged rules).
- **Fail fast.** Start-up fails before `webapi` listens when exactly one of the two keys is set, the email is not a valid address, or the password fails the Identity password options. Nothing is written in that case. The error names the key and lists the password rule codes; it never contains the password.
- **Nothing to do.** When none exists and neither key is set, start-up continues and logs a warning that the database has no SystemAdmin.
- **Logging.** Creation logs the email at Information level. The password never appears in a log, exception or response.
- **After the first sign-in** the owner removes both values from `.env` (deployment §12). Keeping them is harmless (step above) but leaves a secret on the server for no reason.
- No new table. No new route. No schema change. The Development seed is unchanged.

**Acceptance (amendment A25–A31).** Infrastructure.IntegrationTests run the bootstrap against a fresh test database; A26 is a functional test.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| A25 | No SystemAdmin; both keys set, valid | Bootstrap runs | One SystemAdmin with that email and name, Active, no tenant; domain user and identity user both exist and share the PublicId |
| A26 | Person from A25 | Hosted login for `back-office` with that email and password, no `tenantCode` | Authorization code issued; access token works on a SystemAdmin route |
| A27 | One SystemAdmin exists (any status); both keys set with a different email and password | Bootstrap runs | No person created; the existing person's email, password hash and status are unchanged |
| A28 | No SystemAdmin; only `Email` set (and the reverse) | Bootstrap runs | Throws; message names the missing key; no person created |
| A29 | No SystemAdmin; password `short` | Bootstrap runs | Throws; message lists the failed password rules and does not contain `short`; no domain user and no identity user |
| A30 | No SystemAdmin; email `not-an-email` | Bootstrap runs | Throws; no person created |
| A31 | No SystemAdmin; neither key set | Bootstrap runs | No person created; a warning is logged; no exception |

### Amendment 2026-11-13 (`webapi` re-checks person and tenant status)

Accepted 2026-11-13. Supersedes the §4.1 line “dual check: row TenantId ↔ tenant claims” as the only current-user check, the `GetCurrentUser` checks in §7, and the “until then an existing refresh token still works” consequence of the 2026-11-11 outbox amendment for `webapi` resources. Do not rewrite those in place.

**Why.** Revocation waits for the outbox. A late or dead-lettered `UserDisabled` / `TenantDisabled` left a disabled person, or every person of a disabled tenant, able to call `webapi` with refreshed access tokens for up to 14 days. `webapi` already loads the person row and the tenant row on every request, so it can check them there.

- **Status gate.** The current-user load (`CurrentUserAccess.Resolve`) applies `LoginEligibility.CanComplete` to the loaded person and tenant rows on every request, before the dual tenant check. Person `Status` must be `Active`. A tenant person's tenant must be enabled. SystemAdmin has no tenant, so only its `Status` counts.
- **Result.** When the gate fails there is no current user. Every route that needs a permission returns **403**, including `/users/me`. Missing or invalid Bearer stays 401.
- **Disabled tenant.** TenantAdmin, Adviser and Customer of a disabled tenant get 403 on every verb, reads included. The §4.1 `disabled` / `target=tenant` 400 on create is now reached only by a SystemAdmin that names a disabled tenant (`tenantId`). The handler check stays as a backstop.
- **Unchanged.** The token endpoint still does not re-check status on a refresh grant; revocation still waits for the outbox. Hosted login is unchanged. Disabling a tenant still does not bulk-update `User.Status`.
- No new table. No new route. No schema change.

**Acceptance (amendment A32–A35).**

| Id | Given | When | Then |
| --- | --- | --- | --- |
| A32 | TenantAdmin with a valid access token; the person is then disabled | Any `webapi` route, including `GET /users/me` | 403 |
| A33 | Adviser or TenantAdmin with a valid access token; the tenant is then disabled | Get, list, create, PUT, disable / enable, close / reopen, post | 403 |
| A34 | SystemAdmin; a tenant is disabled | Create with that `tenantId` (TenantAdmin, instrument, account, transaction, transfer) | 400 `disabled` / `target=tenant` |
| A35 | Disabled SystemAdmin with a valid access token | Any SystemAdmin route | 403 |

### Amendment 2026-11-13 (identity cookie checks the security stamp)

Status of this amendment: `accepted` (2026-11-13). It is a build contract. Landed 2026-11-13: `IdentityCookieSecurityStamp` (`src/IdentityHost`), set as the validate-principal event in `ClientCookieSchemes.Configure`; A36–A40 are in `PerClientCookieTests`. Supersedes nothing in R1–R21; it closes a gap in R7, story 3 and the §9 line “After password change, run login again”. Do not rewrite those or earlier amendments in place.

**Why.** R7 revokes the subject's tokens after a password change, and the caller must run the authorization-code flow again. But the identity cookie scheme for each client (amendments 2026-10-27, 2026-11-02) is a plain cookie. Nothing compares it with the person's current credentials. `/connect/authorize` re-checks `LoginEligibility` (status and tenant), not the password. So after a password change, any browser that still holds an identity cookie for that person gets a new authorization code silently, and with it a new refresh token. The old password is never asked for. That includes a cookie taken from another device, which is the case a password change is meant to end.

Today's code: IdentityHost registers the user store with `AddIdentityCore` and adds the three client schemes with `AddCookie`. No security-stamp validator runs. Hosted login builds the cookie principal with `SignInManager.CreateUserPrincipalAsync`, so the cookie already carries the security-stamp claim. `UserManager.ChangePasswordAsync` (used by `PUT /users/me/password` on `webapi`) already writes a new `AspNetUsers.SecurityStamp`. Both hosts share `MyWealthDbV2` (R16), so IdentityHost can read the new stamp.

- **Check.** Every time an identity cookie scheme authenticates a request, IdentityHost compares the security-stamp claim in the cookie with `AspNetUsers.SecurityStamp` for that identity user. The check runs on every such request; there is no validation interval. The three schemes (`adviser-portal`, `back-office`, `customer-portal`) all apply it, and any later client scheme does too.
- **Reject.** When the claim is missing, the stamps differ, or the identity user no longer exists, the cookie is rejected and cleared for that scheme only. `/connect/authorize` then treats the request as unauthenticated and redirects to hosted `/login`. No authorization code is issued. The other clients' cookies are untouched; each one fails the same check when it is next used.
- **Keep.** When the stamps match, the cookie stays as it is. The check does not re-issue the cookie or extend its lifetime.
- **What changes the stamp.** Any write through `UserManager` that changes the stamp: today that is the password change. Disable and tenant disable do not change the stamp; `/connect/authorize` already refuses them through `LoginEligibility`. Later flows (reset password, set password from an invite) inherit the check as long as they go through `UserManager`.
- **Deploy.** Cookies issued before this change already carry the claim and stay valid until the stamp changes. No mass sign-out.
- **Implementation note.** Do not wire the framework `SecurityStampValidator` as it is. On failure it signs out Identity's default schemes (`Identity.Application` and others), which IdentityHost does not register, and its default interval is 30 minutes. The check belongs in the client cookie schemes' validate-principal event, set once in `ClientCookieSchemes.Configure`.
- **Unchanged.** Refresh tokens are still revoked through the outbox (amendment 2026-11-11); this amendment does not shorten that window. `webapi` still checks status, not the stamp (amendment 2026-11-13). Hosted login, the allow-list, cookie names, lifetimes and logout (R21) are unchanged.
- No new table. No new route. No schema change. No new configuration key.

**Acceptance (amendment A36–A40).** Application.FunctionalTests, following `PerClientCookieTests`.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| A36 | Active Adviser with a valid `adviser-portal` identity cookie | `PUT /users/me/password` succeeds, then `/connect/authorize` for `adviser-portal` with that cookie | Redirect to hosted `/login`. No authorization code. The `adviser-portal` identity cookie is cleared |
| A37 | Same as A36 | Hosted login with the **new** password, then authorize | Authorization code issued |
| A38 | Active Adviser with a valid `adviser-portal` identity cookie; the password is not changed | `/connect/authorize` for `adviser-portal` twice | Code issued both times without `/login` (silent re-authorization still works) |
| A39 | SystemAdmin with a valid `back-office` identity cookie | Password change, then authorize for `back-office` | Redirect to hosted `/login`. No code |
| A40 | Active Customer with a valid `customer-portal` identity cookie | Password change, then authorize for `customer-portal` | Redirect to hosted `/login`. No code |

### Amendment 2026-11-13 (hosted login lockout and one hash per attempt)

Status of this amendment: `review`. Not a build contract until accepted. Supersedes nothing in R1–R21; it tightens R2 and R3 and closes the lockout gap in [deployment.md](../deployment.md) §11. Do not rewrite those or earlier amendments in place.

**Why.** Hosted login has three gaps that together make password guessing cheap.

1. **No lockout.** `Login.cshtml.cs` checks the password with `UserManager.CheckPasswordAsync`, which does not count failures. Nothing stops attempts on one account spread over many IPs.
2. **Timing tells who exists.** The page returns early, before any password hash, when the tenantCode is unknown, the email is unknown, or the person is not eligible (`LoginEligibility`). Only an eligible person with a wrong password pays for the hash (PBKDF2, tens of milliseconds). The copy is uniform (R3), but the response time is not, so an attacker can list the active tenantCode + email pairs.
3. **The edge limit can be skipped.** Caddy limits `POST` on the exact paths `/login` and `/connect/token`. Caddy's `path` matcher folds case, `//` and `..`, but `/login/`, `/login//` and `/login%2F` do not match an exact path, and ASP.NET routing still serves `/login/` to the page. Those requests only meet the loose global limit (about 300 per minute per IP). Separately, `bff-adviser-portal`, `bff-customer-portal` and `back-office` reach `id.<domain>` through Caddy on the internal network (compose alias), so every code redemption and refresh from one host shares one per-IP key of 10 per minute.

Today's code: `AddIdentityCore` in `src/Infrastructure/DependencyInjection.cs` sets no lockout options. `AspNetUsers` already has `LockoutEnd`, `LockoutEnabled` and `AccessFailedCount` (`0003_identity.sql`). All three OIDC clients are confidential and use authorization code + refresh only; there is no password grant (ADR 0014), so `/connect/token` never checks a person's password.

- **One hash per attempt.** Every `POST /login` runs exactly one password-hash verification, whatever the outcome. When no identity user is resolved (unknown tenantCode, unknown email, missing Identity link), IdentityHost verifies the submitted password against a dummy password hash and then fails. The dummy hash is created once at startup by the registered `IPasswordHasher<ApplicationUser>` from a random password, so its cost matches a real hash.
- **Order of checks.** Resolve tenantCode + email → identity user (R2) → verify the password → lockout → `LoginEligibility` (R3) → client allow-list (R4, R20) → sign in. Eligibility moves after the password check so that a disabled person costs the same as an active one. The response copy for every failure stays `Invalid login.`
- **Lockout.** Five wrong passwords in a row lock the identity user for five minutes. A wrong password counts whether or not the person is eligible. A correct password while not locked resets the count to zero. While locked, the password is still verified (one hash), the attempt fails with the uniform copy, it is not counted, and it does not extend the lockout. When the lockout ends, the count starts at zero.
- **Settings.** `IdentityOptions.Lockout`: `AllowedForNewUsers = true`, `MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 5 minutes`. Set in code next to the password options; no new configuration key. Five minutes keeps a demo account usable after a few typos.
- **Who is locked.** Every role, SystemAdmin included. The person is not told that the account is locked (R3). There is no unlock UI; [deployment.md](../deployment.md) gets a runbook line that clears `LockoutEnd` and `AccessFailedCount` in SQL.
- **Existing rows.** `UserManager.CreateAsync` writes `LockoutEnabled = AllowedForNewUsers`, which already defaults to true. If any existing row has `LockoutEnabled = 0`, a `database/schema/` script sets it to 1. No column changes.
- **Edge rate limit.** The Caddy `sign_in` zone on `id.<domain>` matches `POST` on `/login*` and no longer matches `/connect/token`. Both the `sign_in` zone and the global zone skip the internal network `172.30.0.0/24`, so host-to-host calls through Caddy are not limited per container. [deployment.md](../deployment.md) §11 changes with the Caddyfile, and the subnet becomes one more place listed in [known-issues/deploy-fixed-docker-subnet.md](../known-issues/deploy-fixed-docker-subnet.md).
- **Implementation note.** Do not use `SignInManager.CheckPasswordSignInAsync(..., lockoutOnFailure: true)` as it is. For a locked user it returns before hashing, which brings the timing gap back. Use `UserManager.IsLockedOutAsync`, `CheckPasswordAsync`, `AccessFailedAsync` and `ResetAccessFailedCountAsync` in the order above.
- **Left open (known issues at landing).** Anyone who knows a tenantCode + email can keep that account locked. A failed attempt on an existing identity user writes `AccessFailedCount`, and an unknown tenantCode skips one lookup; both differ by a few milliseconds, far less than a hash. Spread over many IPs, an attacker gets at most five guesses per account every five minutes.
- **Unchanged.** Hosted login copy, the allow-list, identity cookie schemes and the security-stamp check, logout (R21), and `PUT /users/me/password` on `webapi` (an authenticated call; it does not count failures). No new route. No new table. No new configuration key.

**Acceptance (amendment A41–A47).** Application.FunctionalTests. "One hash" is asserted by counting calls on a wrapper around `IPasswordHasher<ApplicationUser>` in the test host, never by wall-clock time. Lockout expiry is simulated by moving `LockoutEnd` into the past in the database, not by waiting.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| A41 | Each of: unknown tenantCode; unknown email; Disabled Adviser; Active Adviser on a disabled tenant; Active Adviser with a wrong password; Active Customer with the correct password on client `adviser-portal`; locked Adviser with the correct password | One `POST /login` | Exactly one password-hash verification. No authorization code. Copy `Invalid login.` |
| A42 | Active Adviser | Five wrong passwords, then the correct password | The sixth attempt issues no code. Copy `Invalid login.` `LockoutEnd` is about five minutes ahead |
| A43 | Locked Adviser | Correct password | No code. `LockoutEnd` and `AccessFailedCount` unchanged |
| A44 | Locked Adviser whose `LockoutEnd` is moved into the past | Correct password | Authorization code issued. `AccessFailedCount` is 0 |
| A45 | Active Adviser | Four wrong passwords, the correct one, then four wrong ones | Not locked. A further correct password issues a code |
| A46 | The same email as an Active Adviser in tenant A and in tenant B | Five wrong passwords with tenant B's code | Tenant A's person is not locked; correct password with tenant A's code issues a code |
| A47 | Unknown email in a known tenant | Five `POST /login` attempts | No `AspNetUsers` row is written |

### Amendment 2026-09-14 (R21 — end session)

Passthrough without an action left `/connect/logout` unhandled. Manual Sign out then bounced through `/callback` and OpenIddict returned `invalid_grant` / `ID2010` (authorization code redeemed twice). No new ADR. No `/auth/logout`.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| A11 | `adviser-portal` client, registered `{origin}/` post-logout URI | `GET /connect/logout?client_id=adviser-portal&post_logout_redirect_uri={origin}/` | 302 to that URI. Not 400. Hosted-login cookie is gone after the action. |
| A12 | Same as A11 but `post_logout_redirect_uri` is an unregistered origin | GET `/connect/logout` | 400 `invalid_request` (OpenIddict `ID2052`). |
| A13 | Active Adviser (or TenantAdmin) with a refresh token | Portal Sign out | Refresh fails at `/connect/token`. Next portal visit shows hosted `/login`, not a silent code. |

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
