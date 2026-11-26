---
title: Customer Portal BFF
status: accepted
phase: 3
language: en
owner: ""
created: 2026-11-02
last_updated: 2026-11-27
related:
  - ../function-plan.md
  - ../architecture.md
  - ../api-design.md
  - ../glossary.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
  - ../adr/0016-bff-for-first-party-portals.md
  - identity-auth.md
  - ../customer-portal/customer-portal.md
  - ../adviser-portal/frontend-conventions.md
  - bff-adviser-portal.md
  - production-hosting.md
  - ../deployment.md
  - withdrawals.md
  - in-kind-transfers.md
---

# Customer Portal BFF

Session edge for the Customer Portal. The SPA is not an OIDC client. Aspire resource `bff-customer-portal` (`src/BffCustomerPortal`) runs cookie auth, the OpenID Connect handler against `identity`, and a reverse proxy to `webapi`. Tokens never enter the browser. Pages, fields, and resource HTTP stay in [customer-portal.md](../customer-portal/customer-portal.md) and the Feature Specs that own those routes.

This file is a build contract. ADR 0016 already locks the BFF shape for first-party portals: one portal, one BFF, confidential client held by the BFF. Do not reopen that decision. Do not implement from a chat summary; implement from this file.

Not a ledger slice. Platform session increment. Do not reopen identity-auth R1–R21 except the amendment that registers client `customer-portal`.

---

## 1. Summary

The Customer Portal browser talks only to the BFF origin: login/logout on the BFF, resource calls under `/api/*`. The BFF is the confidential OIDC client `customer-portal` (secret in Aspire, authorization code + PKCE + refresh). It keeps the access and refresh tokens in the encrypted HttpOnly session cookie it sets in the browser (only the BFF can decrypt it; JavaScript cannot read it; no server-side session store, R22) and attaches Bearer when proxying to `webapi`. `identity` still issues tokens. `webapi` still validates Bearer and never 302s to hosted login.

The allow-list on IdentityHost is Customer only. A TenantAdmin, Adviser, or SystemAdmin with a correct password receives no authorization code for this client.

---

## 2. Scope

**In**

- Application: none (no MediatR use cases on this host)
- Domain: none
- HTTP / host:
  - New Aspire resource `bff-customer-portal` → project `src/BffCustomerPortal`
  - Cookie authentication + OpenID Connect handler (ASP.NET Core), not Duende.BFF
  - Hand-rolled `HttpClient` proxy (`ApiProxy`) `/api/{**path}` → `webapi /{path}` (same landed pattern as the Adviser Portal BFF; not YARP)
  - BFF login challenge, OIDC callback, logout + revocation + end-session
  - OpenIddict application row `customer-portal` (confidential); redirect and post-logout URIs on the BFF origin
  - Vite product path: browser uses the BFF origin; BFF forwards the SPA and `/api`
- Tests: BFF functional host tests + portal session tests that tokens are absent from JS storage

**Out**

- Hosted login page move; remapping `/connect/*`
- Custom `/auth/login`, `/auth/refresh`, `/auth/logout` that issue or mint tokens
- Password grant; custom `RefreshTokens` table; BFF-owned SQL
- Changing `webapi` policies, claims, `/users/me`, people or ledger routes (policy amendments for self-scope live in the existing Feature Specs; this host does not implement them)
- `webapi` accepting the portal cookie
- Shared BFF for Adviser Portal / Back Office
- Duende.BFF
- Data Protection key persistence for multi-instance production (named, not built here)
- Invitation / forgot-password / MFA / external IdP
- Ledger writes, account open / close, instrument catalog management

---

## 3. Stories

1. As a Customer I open the portal with no cookie so the BFF sends me to hosted login and, after callback, my SPA can call `/api/users/me` without seeing a token.
2. As a Customer I sign out so the BFF cookie is gone, refresh is revoked, the hosted-login cookie for this client is cleared, and the next visit is hosted `/login`.
3. As a TenantAdmin, Adviser, or SystemAdmin with a correct password I still receive no authorization code for `customer-portal`. The BFF never establishes a cookie for that person.
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
| R7 | On `webapi` 401, the BFF refreshes **once** with the stored refresh token and retries the proxied request. If refresh fails, clear the BFF cookie and return 401 to the SPA. Do not start authorize from an XHR. The refresh is single-flight, and a rotated refresh token is remembered: the rules RF1–RF8 of [bff-adviser-portal.md](bff-adviser-portal.md) §4.1 apply to this host unchanged, through the shared `RefreshTokenExchange` with client `customer-portal` in `src/BffCustomerPortal/ApiProxy.cs`. |
| R8 | Logout order: sign out the BFF cookie → `POST identity /connect/revocation` (`token_type_hint=refresh_token`) → OpenIddict end-session (`/connect/logout` with `client_id` and `post_logout_redirect_uri`). Do not add `/auth/logout`. identity-auth R21 still owns the identity action. IdentityHost clears only the `customer-portal` scheme cookie (identity-auth §4.3). |
| R9 | `ClientId` is `customer-portal`. Allow-list: Customer only. TenantAdmin, Adviser, or SystemAdmin + correct password → no code, uniform failure (identity-auth R4, R20). |
| R10 | Client is confidential. Secret only in Aspire / host configuration. Authorization code + PKCE stay on. No password grant. |
| R11 | Redirect registered on the client: `{bffOrigin}/signin-oidc`. Post-logout: `{bffOrigin}/`. `{bffOrigin}` is the BFF https browser entry (the Aspire dashboard link), not a launchSettings port. Do not lock a Development port. Upsert a `*.dev.localhost` alias only when that host is itself a browser entry. Do not replace the https entry with the alias. Do not register the BFF http endpoint or the Vite origin. |
| R12 | Protocol paths on `identity` stay OpenIddict defaults. Callers follow discovery. Do not remap `/connect/*`. BFF paths in §8 are this host, not the issuer. |
| R13 | Cookie name `__Host-bff-customer-portal`. HttpOnly, `Path=/`, no `Domain` attribute. `Secure` outside Development. `SameSite=Lax`. No `Expires` / `Max-Age` (browser session). Not readable by JS. This cookie is the only browser credential for `/api` and `/bff/*`. |
| R14 | SPA `fetch` / RTK Query uses `credentials: 'include'` and base URL `/api`. It does not set `Authorization`. Mutating calls send `X-MyWealth-Request: 1` (R18). |
| R15 | Cross-origin browser calls to `webapi` are not part of the product path after this slice. Scalar may keep calling `webapi` directly with Bearer. The BFF does not enable credentialed CORS for a foreign `Origin`. |
| R16 | The BFF does not run the schema applicator and does not reference `Application` handlers. |
| R17 | There is no SPA `/callback`. OIDC callback is `/signin-oidc` on the BFF. Deep-link return uses the OIDC handler `state`, still only in-app paths. `returnUrl` must be a relative path: starts with `/`, must not start with `//` or `/\`, must not contain `\`, and every character is printable ASCII (U+0020 to U+007E). Browsers drop a tab or line break from a URL, so `/<TAB>/host` would become `//host`. Redirect URIs registered on the client are exact. A `*.dev.localhost` alias may be upserted only when that host is a browser entry. Do not register a wildcard, the BFF http endpoint, or the Vite origin. |
| R18 | Mutating browser calls (`POST` / `PUT` / `PATCH` / `DELETE` under `/api`, and `POST /bff/logout`) require header `X-MyWealth-Request: 1`. Missing header → **400**, do not proxy. GET does not require it. This is not a second CSRF token. SameSite=Lax stays. |
| R19 | Do not proxy `/connect/*` to the browser. The SPA has no identity origin in env for protocol calls. |
| R20 | Proxy only these `webapi` prefixes: `/users`, `/accounts`, `/transactions`, `/net-worth`, `/reports`. Any other `/api/*` path → **404**, not forwarded. Do not forward `/scalar`, `/openapi`, `/health`, `/connect`, `/instruments`, `/tenants`, `/currencies`. Destination is Aspire `webapi` only. Prefixes added by later slices, so the Customer Portal can read the Customer's own data: `/nominated-bank-accounts` and `/withdrawals` ([withdrawals.md](withdrawals.md) C22, C24), `/orders` ([orders.md](orders.md) C32) and `/in-kind-transfers` ([in-kind-transfers.md](in-kind-transfers.md) R29). The portal uses GET only; `webapi` refuses every write for a Customer (403), so the BFF does not filter by role. `/fees`, `/custody-movements` and `/custodian-reconciliations` stay blocked (404). |
| R21 | On the outbound `webapi` request, drop the browser `Cookie` and `Authorization`. Set `Authorization: Bearer <access>` from the ticket in the encrypted session cookie (R22). Forward `Idempotency-Key`, `Content-Type`, `Accept`. |
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
| OpenIddict applications | **update seed / upsert only** | New `ClientId = customer-portal`, confidential; secret not in git; redirect URIs in R11 |
| All other tables | none | — |

Script: **none**. No `database/schema/` file. Do not add EF migrations.

Client upsert stays in Development / TestAppHost startup (same place the other confidential clients are upserted), not a versioned SQL script.

No update to [database-design.md](../database-design.md) except a one-line pointer that the `customer-portal` row is confidential after this slice is accepted.

---

## 7. Application use cases

None on `webapi` / `Application`. BFF composition root only:

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Host | `GET /bff/login` | 302 to `identity` authorize | Anonymous allowed; optional `returnUrl` must be a relative in-app path |
| Host | OIDC handler `/signin-oidc` | 302 to return path; sets the encrypted session cookie that holds the tokens (R22) | Single-flight per `code` (no double redeem / OpenIddict `ID2010`) |
| Host | `POST /bff/logout` | 302 to end-session then `{bffOrigin}/` | Cookie present or already signed out (idempotent); R8 |
| Host | ApiProxy `/api/{**path}` | `webapi` status + body | Cookie session required; attach Bearer; R5–R7 |
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
?returnUrl=/dashboard
```

`returnUrl` optional. Default `/` (portal still redirects `/` → `/dashboard`). R17.

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

Cross-tenant or wrong-collection resource ids remain **404** from `webapi`, passed through.

### 8.3 Proxy map

| Browser | BFF → webapi |
| --- | --- |
| `/api/users/me` | `GET /users/me` |
| `/api/users/me/password` | `PUT /users/me/password` |
| `/api/accounts` | `/accounts` |
| `/api/accounts/{id}` | `/accounts/{id}` |
| `/api/accounts/{id}/holdings` | `/accounts/{id}/holdings` |
| `/api/accounts/{id}/cash-balance` | `/accounts/{id}/cash-balance` |
| `/api/transactions` | `/transactions` |
| `/api/net-worth` | `/net-worth` |
| `/api/users/customers/{id}/net-worth` | `/users/customers/{id}/net-worth` |
| `/api/net-worth/funds-under-management` | `/net-worth/funds-under-management` (self scope, [valuation-history.md](valuation-history.md); under the existing prefix) |
| `/api/accounts/{id}/valuations` | `/accounts/{id}/valuations` ([valuation-run.md](valuation-run.md); the account page chart of [valuation-history.md](valuation-history.md)) |
| `/api/reports` | `/reports` |
| `/api/reports/request` | `/reports/request` |
| `/api/reports/executions` | `/reports/executions` |
| `/api/reports/executions/{id}` | `/reports/executions/{id}` |
| `/api/reports/executions/{id}/download` | `/reports/executions/{id}/download` |

R20. There is no open `/api/{**path}` catch-all. Schedule routes are not in the allow-list for this portal (Customer does not schedule in this slice).

Forward: `Idempotency-Key`, `Content-Type`, `Accept`. Drop browser `Cookie` and `Authorization` (R21). Set `Authorization: Bearer <access>` from the ticket in the encrypted session cookie (R22).

### 8.4 Aspire / client

| Item | Value |
| --- | --- |
| Resource | `bff-customer-portal` |
| Project | `src/BffCustomerPortal` |
| SPA resource | `customer-portal` (Vite) remains; product origin is the BFF |
| ClientId | `customer-portal` |
| Type | Confidential + PKCE. Secret in Aspire config. Password grant off |
| Aspire parameter | `customer-portal-client-secret` (`AddParameter`, `secret: true`). Not in git. Same value to Identity and the BFF |
| Scopes | `openid`, `profile`, `offline_access`, `api` |
| Redirect | `{bffOrigin}/signin-oidc`. Do not lock a Development port |
| Post-logout | `{bffOrigin}/` |
| Token store | Encrypted authentication cookie this slice (R22). Not a table. `SessionStore` seam left open |
| Cookie | `__Host-bff-customer-portal` (R13) |
| Allow-list | Customer only |

### 8.5 Serving the SPA build

The BFF serves the built SPA itself when `Spa:DevServerUrl` is not set. The rules are P1–P10 of [bff-adviser-portal.md](bff-adviser-portal.md) §8.5, with `src/CustomerPortal` built into `src/BffCustomerPortal/wwwroot/`. Both BFFs keep their own copy of the code; sharing it is a refactor, not a rule.

---

## 9. UI

Customer Portal pages stay in [customer-portal.md](../customer-portal/customer-portal.md). Changes limited to session plumbing:

- No SPA OIDC client, no `/callback` route, no `sessionStorage` token persist, no Bearer header injection.
- Session probe remains `GET /api/users/me` (same DTO). GET needs no `X-MyWealth-Request`.
- Mutating API calls send `X-MyWealth-Request: 1`.
- Unauthenticated document navigation → `GET /bff/login?returnUrl=…`.
- Sign out → `POST /bff/logout` with that header (next visit is hosted `/login`, not a silent authorize).
- `/forbidden` stays a second line for a leftover **wrong-role** session. Non-Customer must not obtain a cookie (R9).
- No React password page.

---

## 10. Tests

The tests are the executable form of §4. Where they live:

| Project | Where |
| --- | --- |
| Application.FunctionalTests | Folder `BffCustomer/` |

---

## 11. Locked in this spec

No new ADR. ADR 0016 already covers first-party portal BFFs.

| Item | Lock |
| --- | --- |
| Scripts / policy | No SQL script. No new named policy on this host. |
| Caller | Browser → BFF only. BFF → `identity` (protocol) and `webapi` (Bearer). Scalar → `webapi`. |
| Scope | Customer Portal session edge only. |
| Project / resource | `src/BffCustomerPortal`, Aspire `bff-customer-portal` |
| Client | `customer-portal`, confidential + PKCE |
| Tokens in browser | Forbidden |
| Library | ASP.NET Cookie + OpenIdConnect + hand-rolled `HttpClient` proxy (`ApiProxy`). Not Duende.BFF. Not YARP. |
| Cookie | `__Host-bff-customer-portal`, session, no Domain |
| CSRF | `X-MyWealth-Request: 1` on mutating calls. Not a second token |
| Proxy | Prefix allow-list (R20). Drop browser Cookie and Authorization |
| Ticket store | Encrypted cookie this slice. `SessionStore` seam. No table |

Companion edits on accept: architecture, function-plan, glossary, adviser-portal/README, features/README, identity-auth amendment (client registration + per-client scheme).

Still open, not this slice:

- Production Data Protection key store for a multi-instance BFF
- Development raw Vite on its own port. Product auth origin is the BFF. HMR through the BFF fallback is not a second OIDC client.
- Shared session table across BFFs (`BffSessions` + `ITicketStore`) — later amendment, not a new feature file
- `script-src` CSP after the Vite same-origin path is fixed

---

## 12. History

What changed in this file after it was accepted. The text of each change is in git (`git log -p -- docs/features/bff-customer-portal.md`).

| Date | With | Change here |
| --- | --- | --- |
| 2026-11-12 | [bff-adviser-portal.md](bff-adviser-portal.md) §8.5 | §8.5: the BFF serves the SPA build |
| 2026-11-13 | [bff-adviser-portal.md](bff-adviser-portal.md) §4.1 | R7: refresh single-flight |
| 2026-11-20 – 11-24 | [withdrawals.md](withdrawals.md), [orders.md](orders.md), [in-kind-transfers.md](in-kind-transfers.md) | R20: four prefixes added to the allow-list |
| 2026-11-26 | — | The changes above were folded into the body. No rule changed |
| 2026-11-26 | [valuation-history.md](valuation-history.md) | Route table: the series and the stored valuations, under the existing `/net-worth` and `/accounts` prefixes. R20 does not change |
| 2026-11-27 | — | R17: a return URL with a control character or a character outside ASCII is rejected |
