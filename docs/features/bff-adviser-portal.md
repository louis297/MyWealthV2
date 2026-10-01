---
title: Adviser Portal BFF
status: accepted
phase: 2
language: en
owner: ""
created: 2026-10-01
last_updated: 2026-10-01
related:
  - ../function-plan.md
  - ../architecture.md
  - ../api-design.md
  - ../glossary.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
  - ../adr/0016-bff-for-first-party-portals.md
  - identity-auth.md
  - ../portals/adviser-portal.md
  - ../portals/frontend-conventions.md
---

# Adviser Portal BFF

Session edge for the Adviser Portal. The SPA stops being the OIDC client. Aspire resource `bff-adviser-portal` (`src/BffAdviserPortal`) runs cookie auth, the OpenID Connect handler against `identity`, and a reverse proxy to `webapi`. Tokens never enter the browser. Pages, fields, and resource HTTP stay in [adviser-portal.md](../portals/adviser-portal.md) and the Feature Specs that own those routes.

This file is a build contract. ADR 0016 is accepted. Do not implement from a chat summary; implement from this file.

Not a ledger slice. Platform session increment during Phase 2. Do not reopen identity-auth R1–R21 except the amendment in §11 (who holds `ClientId` `adviser-portal`).

---

## 1. Summary

The Adviser Portal browser talks only to the BFF origin: login/logout on the BFF, resource calls under `/api/*`. The BFF is the confidential OIDC client `adviser-portal` (secret in Aspire, authorization code + PKCE + refresh). It stores access and refresh tokens server-side, attaches Bearer when proxying to `webapi`, and keeps an HttpOnly session cookie in the browser. `identity` still issues tokens. `webapi` still validates Bearer and never 302s to hosted login.

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
| R7 | On `webapi` 401, the BFF refreshes **once** with the stored refresh token and retries the proxied request. If refresh fails, clear the BFF cookie and return 401 to the SPA. Do not start authorize from an XHR. |
| R8 | Logout order: sign out the BFF cookie → `POST identity /connect/revocation` (`token_type_hint=refresh_token`) → OpenIddict end-session (`/connect/logout` with `client_id` and `post_logout_redirect_uri`). Do not add `/auth/logout`. identity-auth R21 still owns the identity action. |
| R9 | `ClientId` remains `adviser-portal`. Allow-list unchanged: SystemAdmin, TenantAdmin, Adviser. Customer + correct password → no code, uniform failure (identity-auth R4 / R20). |
| R10 | Client is confidential. Secret only in Aspire / host configuration. Authorization code + PKCE stay on. No password grant. |
| R11 | Redirect registered on the client: `{bffOrigin}/signin-oidc`. Post-logout: `{bffOrigin}/`. Development upsert includes the Aspire dashboard alias for the BFF, not the raw Vite origin as the OIDC redirect. |
| R12 | Protocol paths on `identity` stay OpenIddict defaults. Callers follow discovery. Do not remap `/connect/*`. BFF paths in §8 are this host, not the issuer. |
| R13 | Cookie name `__Host-bff-adviser-portal`. HttpOnly, `Path=/`, no `Domain` attribute. `Secure` outside Development. `SameSite=Lax`. No `Expires` / `Max-Age` (browser session). Not readable by JS. This cookie is the only browser credential for `/api` and `/bff/*`. |
| R14 | SPA `fetch` / RTK Query uses `credentials: 'include'` and base URL `/api`. It does not set `Authorization`. Mutating calls send `X-MyWealth-Request: 1` (R18). |
| R15 | Cross-origin browser calls to `webapi` are not part of the product path after this slice. Scalar may keep calling `webapi` directly with Bearer. The BFF does not enable credentialed CORS for a foreign `Origin`. |
| R16 | The BFF does not run the schema applicator and does not reference `Application` handlers. |
| R17 | `/callback` on the SPA is retired. OIDC callback is `/signin-oidc` on the BFF. Deep-link return uses the OIDC handler `state`, still only in-app paths (adviser-portal C14). `returnUrl` must be a relative path: starts with `/`, must not start with `//` or `/\`, must not contain `\`. Redirect URIs registered on the client are exact. The Development Aspire dashboard alias for the BFF origin may be upserted. Do not register a wildcard or arbitrary localhost. |
| R18 | Mutating browser calls (`POST` / `PUT` / `PATCH` / `DELETE` under `/api`, and `POST /bff/logout`) require header `X-MyWealth-Request: 1`. Missing header → **400**, do not proxy. GET does not require it. This is not a second CSRF token. SameSite=Lax stays. |
| R19 | Do not proxy `/connect/*` to the browser. The SPA has no identity origin in env for protocol calls. |
| R20 | Proxy only these `webapi` prefixes: `/users`, `/tenants`, `/currencies`, `/accounts`, `/instruments`, `/transactions`. Any other `/api/*` path → **404**, not forwarded. Do not forward `/scalar`, `/openapi`, `/health`, `/connect`. Destination is Aspire `webapi` only. |
| R21 | On the outbound `webapi` request, drop the browser `Cookie` and `Authorization`. Set `Authorization: Bearer <access>` from the server ticket. Forward `Idempotency-Key`, `Content-Type`, `Accept`. |
| R22 | First slice stores the OIDC ticket in the encrypted authentication cookie (`SaveTokens`). Expose `CookieAuthenticationOptions.SessionStore` so a later amendment can move the ticket to a table without rewriting login or the proxy. No `BffSessions` table in this slice. |
| R23 | BFF responses include `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, and a frame policy of `frame-ancestors 'none'` (or `X-Frame-Options: DENY`). Do not lock `script-src` in this slice. |
| R24 | Access lifetime stays identity-auth (15 minutes). Refresh stays 14 days absolute. This slice requires refresh rotation: a used refresh is revoked when the next one is issued. Stolen-cookie window is the access lifetime, then rotation or R7 fails the copy. |

`RolePermissions` and `/users/me` do not move. The SPA still paints menus from the proxied `GET /api/users/me` body.

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
| Host | OIDC handler `/signin-oidc` | 302 to return path; sets cookie; stores tokens server-side | Single-flight per `code` (no double redeem / OpenIddict `ID2010`) |
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

R20. There is no open `/api/{**path}` catch-all.

Forward: `Idempotency-Key`, `Content-Type`, `Accept`. Drop browser `Cookie` and `Authorization` (R21). Set `Authorization: Bearer <access>` from the server ticket.

### 8.4 Aspire / client

| Item | Value |
| --- | --- |
| Resource | `bff-adviser-portal` |
| Project | `src/BffAdviserPortal` |
| SPA resource | `adviser-portal` (Vite) remains; product origin is the BFF |
| ClientId | `adviser-portal` |
| Type | Confidential + PKCE. Secret in Aspire config. Password grant off |
| Scopes | `openid`, `profile`, `offline_access`, `api` |
| Redirect | `{bffOrigin}/signin-oidc` |
| Post-logout | `{bffOrigin}/` |
| Token store | Encrypted authentication cookie this slice (R22). Not a table. `SessionStore` seam left open |
| Cookie | `__Host-bff-adviser-portal` (R13) |
| Allow-list | SystemAdmin, TenantAdmin, Adviser |

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

Construction notes (`review`): add a BFF cut to [frontend-implementation-notes.md](../portals/frontend-implementation-notes.md) when this spec is accepted. Do not rebuild pages to ship the BFF.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | none |
| BFF / Application.FunctionalTests (new host fixture) | No cookie → `/api/users/me` is 401 not 302; login challenge hits `identity` authorize with `client_id=adviser-portal` and PKCE; callback redeems a given `code` once; response bodies to the test browser have no `access_token` / `refresh_token`; `/api/users/me` after login is 200 and the outbound `webapi` request has Bearer and no browser `Cookie`; 401 from `webapi` → one refresh then retry; failed refresh → 401 and cookie cleared; logout revokes then end-session; Customer hosted-login still issues no code (reuse A1, do not duplicate A2–A10); bad `returnUrl` → 400; mutating `/api` or `POST /bff/logout` without `X-MyWealth-Request` → 400 and is not forwarded; `/api/scalar` and `/api/health` → 404; `Idempotency-Key` forwarded; cookie name is `__Host-bff-adviser-portal` with no `Domain` |
| Infrastructure.IntegrationTests | none (no schema) |
| Adviser Portal tests | `sessionStorage` has no token keys; API client base is `/api` with credentials; mutating calls send `X-MyWealth-Request`; no `/connect/token` calls; `/callback` gone |

`webapi` isolation tests and people/ledger tests do not move.

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
| Library | ASP.NET Cookie + OpenIdConnect + YARP. Not Duende.BFF |
| Cookie | `__Host-bff-adviser-portal`, session, no Domain |
| CSRF | `X-MyWealth-Request: 1` on mutating calls. Not a second token |
| Proxy | Prefix allow-list (R20). Drop browser Cookie and Authorization |
| Ticket store | Encrypted cookie this slice. `SessionStore` seam. No table |

Amendment to [identity-auth.md](identity-auth.md) is written (2026-10-01). Do not rewrite R1–R21 in place.

Companion edits done in the accept change: architecture, function-plan, glossary, adviser-portal client table, frontend-conventions, adr/README, features/README. Repo code is not landed.

Still open, not this slice:

- Production Data Protection key store for a multi-instance BFF (a table does not remove this)
- Exact YARP package version and destination discover name spelling (`webapi` via Aspire service discovery)
- Whether Development still exposes raw Vite on a second port for HMR (product auth origin remains the BFF; if that origin differs, R18 still applies, do not switch the cookie to `SameSite=None`)
- Customer Portal BFF (`bff-customer-portal`) — later slice, own file, not a suffix on this one
- `script-src` CSP after the Vite same-origin path is fixed
- `BffSessions` table + `ITicketStore` — later amendment of this file, not a new feature file

---

## 12. Suggested commits

The repository should build after each commit. Agents split each line into red then green.

1. Empty `src/BffAdviserPortal` + Aspire resource `bff-adviser-portal` + service discovery to `identity` and `webapi` (no auth yet).
2. Cookie + OpenID Connect handler: `/bff/login`, `/signin-oidc`, confidential client upsert, PKCE, tests for single-flight callback and no tokens in the browser-facing body.
3. YARP prefix allow-list + Bearer attach + drop browser Cookie / Authorization + 401 refresh-once; reject missing `X-MyWealth-Request` on mutating calls; forward `Idempotency-Key`.
4. `POST /bff/logout` + revocation + end-session; Customer allow-list smoke through this client.
5. Point Vite / SPA at the BFF origin; delete SPA `/callback`, `sessionStorage` tokens, and Bearer injection; send `X-MyWealth-Request` on mutating calls; fix C1/C13/C14 tests.
6. CORS: drop portal-origin on `webapi` if no browser still calls it; keep Scalar. BFF does not add credentialed CORS.
---
