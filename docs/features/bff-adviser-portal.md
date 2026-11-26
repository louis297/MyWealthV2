---
title: Adviser Portal BFF
status: accepted
phase: 2
language: en
owner: ""
created: 2026-10-01
last_updated: 2026-11-26
related:
  - ../function-plan.md
  - ../architecture.md
  - ../api-design.md
  - ../glossary.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
  - ../adr/0016-bff-for-first-party-portals.md
  - identity-auth.md
  - ../adviser-portal/adviser-portal.md
  - ../adviser-portal/frontend-conventions.md
  - production-hosting.md
  - ../deployment.md
  - withdrawals.md
  - interest-run.md
  - trade-settlement.md
  - rebalances.md
  - migration-batches.md
  - in-kind-transfers.md
  - adjustment-review.md
---

# Adviser Portal BFF

Session edge for the Adviser Portal. The SPA stops being the OIDC client. Aspire resource `bff-adviser-portal` (`src/BffAdviserPortal`) runs cookie auth, the OpenID Connect handler against `identity`, and a reverse proxy to `webapi`. Tokens never enter the browser. Pages, fields, and resource HTTP stay in [adviser-portal.md](../adviser-portal/adviser-portal.md) and the Feature Specs that own those routes.

This file is a build contract. ADR 0016 is accepted. Landed on repo master `5e85e9c` (2026-10-01, `78d7eb4`…`5e85e9c`). Do not implement from a chat summary; implement from this file.

Not a ledger slice. Platform session increment during Phase 2. Do not reopen identity-auth R1–R21 except the amendment in §11 (who holds `ClientId` `adviser-portal`).

---

## 1. Summary

The Adviser Portal browser talks only to the BFF origin: login/logout on the BFF, resource calls under `/api/*`. The BFF is the confidential OIDC client `adviser-portal` (secret in Aspire, authorization code + PKCE + refresh). It keeps the access and refresh tokens in the encrypted HttpOnly session cookie it sets in the browser (only the BFF can decrypt it; JavaScript cannot read it; no server-side session store, R22) and attaches Bearer when proxying to `webapi`. `identity` still issues tokens. `webapi` still validates Bearer and never 302s to hosted login.

---

## 2. Scope

**In**

- Application: none (no MediatR use cases on this host)
- Domain: none
- HTTP / host:
  - New Aspire resource `bff-adviser-portal` → project `src/BffAdviserPortal`
  - Cookie authentication + OpenID Connect handler (ASP.NET Core), not Duende.BFF
  - YARP (or the in-stack reverse-proxy equivalent) `/api/{**path}` → `webapi /{path}`
  - BFF login challenge, OIDC callback, logout + revocation + end-session
  - OpenIddict application row `adviser-portal` becomes confidential; redirect and post-logout URIs on the BFF origin
  - Vite product path: browser uses the BFF origin; BFF forwards the SPA and `/api`
- Tests: BFF functional host tests + portal session tests that tokens are absent from JS storage

**Out**

- Hosted login page move; remapping `/connect/*`
- Custom `/auth/login`, `/auth/refresh`, `/auth/logout` that issue or mint tokens
- Password grant; custom `RefreshTokens` table; BFF-owned SQL
- Changing `webapi` policies, claims, `/users/me`, people or ledger routes
- `webapi` accepting the portal cookie
- Shared BFF for Customer Portal / Back Office
- Registering client `customer-portal`
- Duende.BFF
- Rebuilding portal pages cut C
- Data Protection key persistence for multi-instance production (named, not built here)
- Invitation / forgot-password / MFA / external IdP

---

## 3. Stories

1. As a TenantAdmin or Adviser I open the portal with no cookie so the BFF sends me to hosted login and, after callback, my SPA can call `/api/users/me` without seeing a token.
2. As any allowed role I sign out so the BFF cookie is gone, refresh is revoked, the hosted-login cookie is cleared, and the next visit is hosted `/login`.
3. As a Customer with a correct password I still receive no authorization code for `adviser-portal` (identity-auth A). The BFF never establishes a cookie for that person.
4. As the SPA I retry a mutating call with the same `Idempotency-Key`; the BFF forwards the header unchanged.
5. As `webapi` I keep seeing Bearer from the BFF and I still return 401 / 403 / 404 in the existing envelope.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | The browser never receives access or refresh tokens. They must not appear in `sessionStorage`, `localStorage`, Redux, response bodies to the SPA, or HTML bootstrap. Appearance is a failed slice. |
| R2 | Only `identity` issues tokens. The BFF must not mint JWTs and must not host OpenIddict. |
| R3 | `webapi` keeps Bearer + JWKS. Auth failure on `webapi` is 401, never 302 to hosted login (identity-auth R15). |
| R4 | The BFF may 302 the **browser** to `/bff/login` or to `identity` authorize when **its own** session is missing. It must not rewrite `webapi` 403 / 404 / 409 / 400 bodies or status codes when proxying. |
| R5 | Proxy preserves method, path after `/api`, query, JSON body, and product headers (`Idempotency-Key`, `Content-Type`). List envelope, PublicId, and error envelope stay [api-design.md](../api-design.md). |
| R6 | Refresh lives only in the BFF process (tokens saved by the OIDC handler). The SPA must not call `/connect/token` or `/connect/revocation`. |
| R7 | On `webapi` 401, the BFF refreshes **once** with the stored refresh token and retries the proxied request. If refresh fails, clear the BFF cookie and return 401 to the SPA. Do not start authorize from an XHR. At most one refresh grant is in flight per refresh token, and a rotated token is remembered (§4.1). |
| R8 | Logout order: sign out the BFF cookie → `POST identity /connect/revocation` (`token_type_hint=refresh_token`) → OpenIddict end-session (`/connect/logout` with `client_id` and `post_logout_redirect_uri`). Do not add `/auth/logout`. identity-auth R21 still owns the identity action. IdentityHost clears only the `adviser-portal` scheme cookie (identity-auth §4.3). |
| R9 | `ClientId` remains `adviser-portal`. Allow-list: TenantAdmin, Adviser. SystemAdmin was removed by identity-auth §4.2 and receives no code. Customer + correct password → no code, uniform failure (identity-auth R4 / R20). |
| R10 | Client is confidential. Secret only in Aspire / host configuration. Authorization code + PKCE stay on. No password grant. |
| R11 | Redirect registered on the client: `{bffOrigin}/signin-oidc`. Post-logout: `{bffOrigin}/`. `{bffOrigin}` is the BFF https browser entry (the Aspire dashboard link), not a launchSettings port. Development currently shows `https://localhost:7190/`; that port is not the product origin. Upsert a `*.dev.localhost` alias only when that host is itself a browser entry. Do not replace the https entry with the alias. Do not register the BFF http endpoint or the Vite origin. |
| R12 | Protocol paths on `identity` stay OpenIddict defaults. Callers follow discovery. Do not remap `/connect/*`. BFF paths in §8 are this host, not the issuer. |
| R13 | Cookie name `__Host-bff-adviser-portal`. HttpOnly, `Path=/`, no `Domain` attribute. `Secure` outside Development. `SameSite=Lax`. No `Expires` / `Max-Age` (browser session). Not readable by JS. This cookie is the only browser credential for `/api` and `/bff/*`. |
| R14 | SPA `fetch` / RTK Query uses `credentials: 'include'` and base URL `/api`. It does not set `Authorization`. Mutating calls send `X-MyWealth-Request: 1` (R18). |
| R15 | Cross-origin browser calls to `webapi` are not part of the product path after this slice. Scalar may keep calling `webapi` directly with Bearer. The BFF does not enable credentialed CORS for a foreign `Origin`. |
| R16 | The BFF does not run the schema applicator and does not reference `Application` handlers. |
| R17 | `/callback` on the SPA is retired. OIDC callback is `/signin-oidc` on the BFF. Deep-link return uses the OIDC handler `state`, still only in-app paths (adviser-portal C14). `returnUrl` must be a relative path: starts with `/`, must not start with `//` or `/\`, must not contain `\`. Redirect URIs registered on the client are exact. A `*.dev.localhost` alias may be upserted only when that host is a browser entry. Do not register a wildcard, the BFF http endpoint, or the Vite origin. `https://localhost:7190` is the current Development dashboard link, not a locked port. |
| R18 | Mutating browser calls (`POST` / `PUT` / `PATCH` / `DELETE` under `/api`, and `POST /bff/logout`) require header `X-MyWealth-Request: 1`. Missing header → **400**, do not proxy. GET does not require it. This is not a second CSRF token. SameSite=Lax stays. |
| R19 | Do not proxy `/connect/*` to the browser. The SPA has no identity origin in env for protocol calls. |
| R20 | Proxy only the `webapi` prefixes on the allow-list of §8.3: the six of the first slice (`/users`, `/tenants`, `/currencies`, `/accounts`, `/instruments`, `/transactions`) and the prefixes later slices added. Any other `/api/*` path → **404**, not forwarded. Do not forward `/scalar`, `/openapi`, `/health`, `/connect`. Destination is Aspire `webapi` only. A slice that needs a new prefix adds it to the list in `ApiProxy` and to the table in §8.3. |
| R21 | On the outbound `webapi` request, drop the browser `Cookie` and `Authorization`. Set `Authorization: Bearer <access>` from the ticket in the encrypted session cookie (R22). Forward `Idempotency-Key`, `Content-Type`, `Accept`. |
| R22 | First slice stores the OIDC ticket in the encrypted authentication cookie (`SaveTokens`). Expose `CookieAuthenticationOptions.SessionStore` so a later amendment can move the ticket to a table without rewriting login or the proxy. No `BffSessions` table in this slice. |
| R23 | BFF responses include `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, and a frame policy of `frame-ancestors 'none'` (or `X-Frame-Options: DENY`). Do not lock `script-src` in this slice. |
| R24 | Access lifetime stays identity-auth (15 minutes). Refresh stays 14 days absolute. This slice requires refresh rotation: a used refresh is revoked when the next one is issued. Stolen-cookie window is the access lifetime, then rotation or R7 fails the copy. |

`RolePermissions` and `/users/me` do not move. The SPA still paints menus from the proxied `GET /api/users/me` body.

### 4.1 Refresh single-flight

It applies to both BFFs ([bff-customer-portal.md](bff-customer-portal.md) points here) and to Back Office. It tightens R7 and R24.

**Why.** The ticket lives in the browser cookie (R22), so every request the SPA sends before it receives a new cookie carries the same refresh token. IdentityHost rotates refresh tokens with a reuse leeway of zero (identity-auth R5): a second grant for the same refresh token is a replay, fails with `invalid_grant`, and makes OpenIddict revoke the token chain of that authorization, including the refresh token the first grant has just issued. Without the rules below, once the 15-minute access token had expired, any page that loaded two or more queries in parallel signed the person out. The race window is the full round trip of the first request (`webapi` 401, token endpoint, retry), and it also covers a request whose response with the new cookie never reached the browser.

| Id | Rule |
| --- | --- |
| RF1 | **Refresh single-flight.** Within one host process, at most one refresh grant is in flight per refresh token. Every proxied request that needs a refresh with that token waits for that one grant and uses its result. |
| RF2 | **Rotated tokens are remembered.** After a grant succeeds, the host remembers that the presented refresh token was replaced by the result (new access token, new refresh token, access expiry). A later request that presents the replaced refresh token gets that result from memory and does not call the token endpoint. |
| RF3 | **Chain follow.** When the remembered access token has expired, or expires within one minute, the host follows the remembered chain to the newest refresh token and refreshes that one under RF1. It never presents a replaced refresh token again. |
| RF4 | **Failure is not remembered.** A failed grant (non-success status, or no `access_token` or `refresh_token` in the body) is returned to every request that waited for it, then dropped. Each of those requests follows R7: clear the cookie, return 401. The next request with that refresh token calls the token endpoint again. |
| RF5 | **The request that starts the grant cannot cancel it.** The token-endpoint call does not use the request's `RequestAborted`. A waiting request may stop waiting when its own request is aborted; that does not cancel the grant. A grant whose result the browser never receives still lands in memory under RF2. |
| RF6 | **Retention.** Each remembered entry is dropped 30 minutes (two access lifetimes) after it is written. Time comes from the host's `TimeProvider`. A replaced refresh token presented after that goes to the token endpoint and fails as a replay (R7). Memory holds only recent rotations, not every rotation since start. |
| RF7 | **Cookie and retry unchanged.** Every request that uses a refreshed result, whether from the grant or from memory, writes the new ticket to its own response cookie and retries `webapi` once with the new access token. R7 still allows one refresh and one retry per proxied request. |
| RF8 | **Process memory only.** No table, no distributed cache, no new configuration key. Token values are never logged or used as log properties. |

**Left open.** Memory is per process. Two BFF instances behind one origin would not share it, and concurrent requests split across them would race as they do today. Today each BFF runs as one instance (AppHost has no replicas, and `deploy/compose.yml` has one container per BFF). Revisit together with §11 "Production Data Protection key store for a multi-instance BFF" and the session table.

Where it lives: `RefreshTokenExchange` (`src/Shared`), registered as a singleton in each BFF's `BffAuthenticationExtensions` with its own client id, client secret, token endpoint, `HttpClient` and `TimeProvider`, and used by `ApiProxy.TryRefreshAsync`; Back Office delegates to it from `BackOfficeTokenRefresh`. `TryRefreshAsync` keeps the cookie write (RF7) and the `id_token` carry-over. Tests: `RefreshSingleFlightScenarios`, run by `BffRefreshSingleFlightTests` and `CustomerBffRefreshSingleFlightTests`; the Back Office retention case is in `BackOfficeRefreshTests`.

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| — | none | Session protocol is not an aggregate (identity-auth §5). |

Invariants: none new in Domain.

Domain events: none raised by the BFF. Revocation on password change / disable / tenant disable still flows identity-auth R18 (`webapi` → `ITokenRevocation` → OpenIddict store). After that, the next BFF refresh fails (R7).

No update to [domain-model.md](../domain-model.md) in this slice.

---

## 6. Database

| Table | Change | Index / FK |
| --- | --- | --- |
| OpenIddict applications | **update seed / upsert only** | `ClientId = adviser-portal` becomes confidential; secret not in git; redirect URIs in R11 |
| All other tables | none | — |

Script: **none**. No `database/schema/` file. Do not add EF migrations.

Client upsert stays in Development / TestAppHost startup (same place the public client is upserted today), not a versioned SQL script.

No update to [database-design.md](../database-design.md) except a one-line pointer that the `adviser-portal` row is confidential after this slice is accepted.

---

## 7. Application use cases

None on `webapi` / `Application`. BFF composition root only:

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Host | `GET /bff/login` | 302 to `identity` authorize | Anonymous allowed; optional `returnUrl` must be a relative in-app path |
| Host | OIDC handler `/signin-oidc` | 302 to return path; sets the encrypted session cookie that holds the tokens (R22) | Single-flight per `code` (no double redeem / OpenIddict `ID2010`) |
| Host | `POST /bff/logout` | 302 to end-session then `{bffOrigin}/` | Cookie present or already signed out (idempotent); R8 |
| Host | YARP `/api/{**path}` | `webapi` status + body | Cookie session required; attach Bearer; R5–R7 |
| Host | Static / Vite forward | SPA | Anonymous; `/api` and `/bff` and `/signin-oidc` excluded |

---

## 8. API

BFF surface. Resource catalog stays on `webapi`.

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/bff/login` | anonymous | 302 authorize | 400 if `returnUrl` is absolute or off-app |
| GET | `/signin-oidc` | OIDC handler | 302 return path + cookie | Protocol error; no session |
| POST | `/bff/logout` | cookie session (or no-op if already clear) | 302 end-session flow | 405 on GET if only POST is enabled |
| * | `/api/{prefix}/{**path}` | cookie session; mutating methods also R18 | proxied | no cookie → **401** JSON, not a login 302 (XHR); missing `X-MyWealth-Request` on a mutating call → **400**; prefix not in R20 → **404**; `webapi` errors passed through |
| GET | `/signout-callback-oidc` | OIDC handler | 302 `{bffOrigin}/` | — |

Create-on-BFF: none. Do not return `{ "id" }`.

List envelope: not owned here.

### 8.1 Bodies

`GET /bff/login` query:

```text
?returnUrl=/customers
```

`returnUrl` optional. Default `/` (portal still redirects `/` → `/customers`). R17.

`POST /bff/logout`: empty body.

No token JSON:

```json
{ "access_token": "…", "refresh_token": "…" }
```

is forbidden on every BFF response.

### 8.2 Errors

Proxy: use [api-design.md](../api-design.md) exactly as `webapi` emitted it.

BFF-native (no cookie on `/api/*`): **401** with the same error envelope family if the host already has one; do not 302 an XHR to hosted login.

Wrong `returnUrl` → **400**. Missing `X-MyWealth-Request` on a mutating call → **400**. Do not invent a new `code` unless api-design already names it.

Cross-tenant resource ids remain **404** from `webapi`, passed through.

### 8.3 Proxy map

| Browser | BFF → webapi |
| --- | --- |
| `/api/users/me` | `GET /users/me` |
| `/api/users/customers` | `/users/customers` |
| `/api/tenants/by-code/{code}` | `/tenants/by-code/{code}` |
| `/api/currencies` | `/currencies` |
| `/api/accounts` | `/accounts` |
| `/api/instruments` | `/instruments` |
| `/api/transactions` | `/transactions` |

The same mapping, `/api{prefix}` → `{prefix}`, holds for every prefix on the allow-list. `webapi` checks the permission; the BFF does not filter by role, so a caller without the permission gets the 403 from `webapi`. The Customer Portal BFF has its own, shorter list ([bff-customer-portal.md](bff-customer-portal.md)).

| Prefix | Methods | Who has a permission under it | Routes of |
| --- | --- | --- | --- |
| `/users`, `/tenants`, `/currencies`, `/accounts`, `/instruments`, `/transactions` | As the routes | TenantAdmin, Adviser | The Phase 1 and ledger specs |
| `/net-worth` | GET | TenantAdmin, Adviser | [net-worth.md](net-worth.md); `GET /net-worth/funds-under-management` is under the same prefix ([valuation-history.md](valuation-history.md)), no change to the list |
| `/reports` | GET, POST, PUT, DELETE | TenantAdmin, Adviser | [reports.md](reports.md) |
| `/portfolios` | GET, POST, PUT, DELETE | TenantAdmin, Adviser | [portfolios.md](portfolios.md) |
| `/fees` | GET, POST, PUT, DELETE | TenantAdmin, Adviser | [trade-fees.md](trade-fees.md) |
| `/statement-lines` | GET, POST | TenantAdmin | [deposit-matching.md](deposit-matching.md). `GET /bank-connections` is not proxied: only SystemAdmin reads it, in the Back Office |
| `/nominated-bank-accounts`, `/withdrawals` | GET, POST | TenantAdmin, Adviser (verify and approve: TenantAdmin) | [withdrawals.md](withdrawals.md) |
| `/bank-reconciliations` | GET, POST | TenantAdmin | [bank-reconciliation.md](bank-reconciliation.md) |
| `/interest` | GET, POST, PUT, DELETE | TenantAdmin, Adviser (writes and runs: TenantAdmin) | [interest-run.md](interest-run.md) |
| `/corporate-actions` | GET, POST, PUT | TenantAdmin, Adviser (writes: TenantAdmin) | [corporate-actions.md](corporate-actions.md) |
| `/orders` | GET, POST | TenantAdmin, Adviser | [orders.md](orders.md) |
| `/trade-settlements` | GET, POST | TenantAdmin | [trade-settlement.md](trade-settlement.md) |
| `/rebalances` | GET, POST | TenantAdmin, Adviser | [rebalances.md](rebalances.md) |
| `/migration-batches` | GET, POST, PUT | TenantAdmin | [migration-batches.md](migration-batches.md) |
| `/in-kind-transfers` | GET, POST | TenantAdmin, Adviser | [in-kind-transfers.md](in-kind-transfers.md) |
| `/custody-movements`, `/custodian-reconciliations` | GET, POST | TenantAdmin | [in-kind-transfers.md](in-kind-transfers.md) |
| `/adjustment-requests` | GET, POST | TenantAdmin | [adjustment-review.md](adjustment-review.md) |

R20. There is no open `/api/{**path}` catch-all.

Forward: `Idempotency-Key`, `Content-Type`, `Accept`. Drop browser `Cookie` and `Authorization` (R21). Set `Authorization: Bearer <access>` from the ticket in the encrypted session cookie (R22).

### 8.4 Aspire / client

| Item | Value |
| --- | --- |
| Resource | `bff-adviser-portal` |
| Project | `src/BffAdviserPortal` |
| SPA resource | `adviser-portal` (Vite) remains; product origin is the BFF |
| ClientId | `adviser-portal` |
| Type | Confidential + PKCE. Secret in Aspire config. Password grant off |
| Aspire parameter | `adviser-portal-client-secret` (`AddParameter`, `secret: true`). Not in git. Same value to Identity and the BFF |
| Scopes | `openid`, `profile`, `offline_access`, `api` |
| Redirect | `{bffOrigin}/signin-oidc`. Development dashboard link is currently `https://localhost:7190/`; do not lock that port |
| Post-logout | `{bffOrigin}/` |
| Token store | Encrypted authentication cookie this slice (R22). Not a table. `SessionStore` seam left open |
| Cookie | `__Host-bff-adviser-portal` (R13) |
| Allow-list | TenantAdmin, Adviser |

### 8.5 Serving the SPA build

In Development the BFF serves the SPA through `DevelopmentSpaProxy` (`Spa:DevServerUrl`). In production there is no Vite server, and Caddy has one upstream per portal origin, so the BFF serves the built files itself ([deployment.md](../deployment.md) §13 item 4).

| Id | Rule |
| --- | --- |
| P1 | When `Spa:DevServerUrl` is set, the BFF uses `DevelopmentSpaProxy` as today. When it is not set, the BFF serves static files from its `wwwroot` folder. |
| P2 | A `GET` or `HEAD` request for a path that matches a file in `wwwroot` returns that file with its content type. |
| P3 | A `GET` or `HEAD` request that matches no endpoint and no file, whose last path segment has no file extension, and that is not under a reserved prefix, returns `wwwroot/index.html` with status 200 and `text/html`. This serves client routes such as `/accounts/{id}` and deep links after sign-in. |
| P4 | Reserved prefixes never fall back to `index.html`: `/api`, `/bff`, `/signin-oidc`, `/signout-callback-oidc`, `/health`, `/alive`. An unknown path under them returns 404. This is the same list `DevelopmentSpaProxy` uses. |
| P5 | A missing path whose last segment has a file extension (for example `/assets/missing.js`) returns 404, not `index.html`, so a stale script reference never receives HTML. |
| P6 | Methods other than `GET` and `HEAD` on a non-endpoint path return 404 or 405, never `index.html`. |
| P7 | `index.html` (direct or through P3) is sent with `Cache-Control: no-cache`. Files under `/assets/` (Vite hashed output) are sent with `Cache-Control: public, max-age=31536000, immutable`. Other files use the static file defaults. |
| P8 | Static and fallback responses are anonymous. The SPA itself calls `GET /api/users/me` and starts `/bff/login` when it gets 401 (§4 unchanged). |
| P9 | `UseBffSecurityHeaders` applies to static and fallback responses, as to every other response. |
| P10 | If `wwwroot/index.html` does not exist and `Spa:DevServerUrl` is not set, P3 returns 404. The host still starts, so its health check and `/bff/*` keep working. |

**Build.** CI runs `npm ci && npm run build` in `src/AdviserPortal` and copies `dist/` into `src/BffAdviserPortal/wwwroot/` before `dotnet publish` (deployment §4, §9). `src/BffAdviserPortal/wwwroot/` is listed in `.gitignore` and never committed. Local `dotnet run` without a Vite server and without a build therefore serves 404 for client routes (P10).

**Out.** A content-security `script-src` policy (still open in §11). Compression (Caddy does it). Pre-compressed files.

Tests: `Bff/BffSpaStaticFilesTests.cs` and `Bff/BffSpaProxyTests.cs`.

---

## 9. UI

Adviser Portal pages cut C stay. Changes limited to session plumbing:

- Remove SPA OIDC client, `/callback` route, `sessionStorage` token persist, and Bearer header injection.
- Session probe remains `GET /api/users/me` (same DTO). GET needs no `X-MyWealth-Request`.
- Mutating API calls send `X-MyWealth-Request: 1`.
- Unauthenticated document navigation → `GET /bff/login?returnUrl=…`.
- Sign out → `POST /bff/logout` with that header (C13 still true: next visit is hosted `/login`, not a silent authorize).
- `/forbidden` stays a second line for a leftover **wrong-role** session. Customer must not obtain a cookie (R9).
- No React password page.

Construction notes (`review`): add a BFF cut to [frontend-implementation-notes.md](../adviser-portal/frontend-implementation-notes.md) when this spec is accepted. Do not rebuild pages to ship the BFF.

---

## 10. Tests

The tests are the executable form of §4. Where they live:

| Project | Where |
| --- | --- |
| Application.FunctionalTests | Folder `Bff/`: `BffLoginTests`, `BffCallbackTests`, `BffLogoutTests`, `BffAllowListTests`, `BffApiTests`, `BffRequestBodyTests`, `BffCorsTests`, `BffRefreshTests`, `BffRefreshSingleFlightTests`, `BffSpaProxyTests`, `BffSpaStaticFilesTests` |

---

## 11. Locked in this spec

New ADR: [adr/0016-bff-for-first-party-portals.md](../adr/0016-bff-for-first-party-portals.md) (proposed with this draft).

| Item | Lock |
| --- | --- |
| Scripts / policy | No SQL script. No new named policy. |
| Caller | Browser → BFF only. BFF → `identity` (protocol) and `webapi` (Bearer). Scalar → `webapi`. |
| Scope | Adviser Portal session edge only. |
| Project / resource | `src/BffAdviserPortal`, Aspire `bff-adviser-portal` |
| Client | `adviser-portal`, confidential + PKCE |
| Tokens in browser | Forbidden |
| Library | ASP.NET Cookie + OpenIdConnect + hand-rolled `HttpClient` proxy (`ApiProxy`). Not Duende.BFF. Not YARP (landed deviation; see §12) |
| Cookie | `__Host-bff-adviser-portal`, session, no Domain |
| CSRF | `X-MyWealth-Request: 1` on mutating calls. Not a second token |
| Proxy | Prefix allow-list (R20). Drop browser Cookie and Authorization |
| Ticket store | Encrypted cookie this slice. `SessionStore` seam. No table |

Amendment to [identity-auth.md](identity-auth.md) is written (2026-10-01). Do not rewrite R1–R21 in place.

Companion edits done in the accept change: architecture, function-plan, glossary, adviser-portal client table, frontend-conventions, adr/README, features/README. Repo code landed `78d7eb4`…`5e85e9c` (2026-10-01). Head `5e85e9c`.

Development only, not a product rule:

- The dashboard https link is the browser entry. On this machine that is `https://localhost:7190/` from launchSettings. Do not write that port into the redirect contract.
- Ignore `X-Forwarded-Host` in Development so a proxy cannot rewrite that entry to another host. Production must not depend on this.
- `DevelopmentSpaProxy` may catch a Vite connection close (`ResponseEnded`) and return 502 without failing the BFF resource. That catch does not apply to `/api` or login. HMR over this proxy is not supported.

Still open, not this slice:

- Production Data Protection key store for a multi-instance BFF (a table does not remove this)
- Development still runs raw Vite on its own port. Product auth origin is the BFF (`MapFallback` to `Spa:DevServerUrl`). Do not switch the cookie to `SameSite=None` if those origins differ (R18). HMR through the BFF fallback is not a second OIDC client.
- Customer Portal BFF (`bff-customer-portal`) — later slice, own file, not a suffix on this one
- `script-src` CSP after the Vite same-origin path is fixed
- `BffSessions` table + `ITicketStore` — later amendment of this file, not a new feature file

---

## 12. Landed commits

Repo master `5e85e9c` (2026-10-01). Agents must not replay this list.

| Commit | What landed |
| --- | --- |
| `78d7eb4` | Empty `src/BffAdviserPortal` + Aspire resource `bff-adviser-portal` |
| `447fb80` | Cookie + OpenID Connect: `/bff/login`, `/signin-oidc`, confidential upsert, PKCE |
| `76a0f17` | Authorization code redeemed once on the BFF |
| `99f321a` | Prefix allow-list proxy to `webapi` (hand-rolled `HttpClient`, not YARP) |
| `b85b56a` | Proxied 401 refresh-once and refresh rotation |
| `5f229f6` | Revoke refresh token and end-session |
| `eeeb6b0` | Customer and SystemAdmin allow-list smoke |
| `6aae5c7` | Adviser Portal pointed at the BFF (no SPA `/callback`, no token persist, `/api` + credentials, `X-MyWealth-Request` on mutations) |
| `5e85e9c` | Portal CORS removed from `webapi` (`AddCors` / `UseCors` gone). Scalar stays on `webapi` and does not need a portal origin |

Aspire parameter `adviser-portal-client-secret` (`secret: true`, not in git) feeds `Identity:AdviserPortalClientSecret` and `Authentication:ClientSecret`. First AppHost run prompts; user-secrets key is `Parameters:adviser-portal-client-secret`. Portal adaptation record: [adviser-portal-bff-cut.md](../adviser-portal/adviser-portal-bff-cut.md).

Landed deviation: §11 named YARP. The tree uses `ApiProxy` + `IWebApiTransport`. Do not add YARP unless a later amendment says so. Allow-list prefixes in code: `/users`, `/tenants`, `/currencies`, `/accounts`, `/instruments`, `/transactions`, `/reports`, `/portfolios`. Net-worth read model (accepted 2026-10-12) adds prefix `/net-worth` for `GET /net-worth`; the Customer read reuses `/users` (`GET /users/customers/{id}/net-worth`).

---

## 13. History

What changed in this file after it was accepted. The text of each change is in git (`git log -p -- docs/features/bff-adviser-portal.md`).

| Date | With | Change here |
| --- | --- | --- |
| 2026-11-03 – 11-25 | The spec of each prefix (§8.3) | R20, §8.3: prefixes added to the allow-list, one per slice that needed it |
| 2026-11-12 | [deployment.md](../deployment.md) | §8.5: the BFF serves the SPA build (P1–P10) |
| 2026-11-13 | — | §4.1: refresh single-flight (RF1–RF8) |
| 2026-11-26 | — | The changes above were folded into the body. No rule changed |
| 2026-11-26 | [valuation-history.md](valuation-history.md) | §8.3: `/net-worth/funds-under-management` is under the existing prefix. The allow-list does not change |
