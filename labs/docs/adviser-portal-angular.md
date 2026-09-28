---
title: Adviser Portal (Angular lab)
status: draft
phase: lab
language: en
created: 2026-09-27
updated: 2026-09-28
related:
  - README.md
  - ../../docs/portals/adviser-portal.md
  - ../../docs/features/identity-auth.md
  - ../../docs/features/advisers.md
  - ../../docs/features/customers.md
  - ../../docs/features/tenants.md
  - ../../docs/adr/0003-react-redux-typescript-vite-tailwind-frontend.md
  - ../../docs/adr/0014-openiddict-authorization-code-pkce.md
---

# Adviser Portal (Angular lab)

Long-lived **parallel** of the Phase-1 Adviser Portal pages (cut C). Product UI stays React. This file is a lab contract (`draft`). It is not a product portal spec and must not be read as reopening ADR 0003.

Implementation path in the GitHub repo: `labs/adviser-portal-angular/`. Lab agent rules: [LAB_AGENTS.md](../LAB_AGENTS.md).

## 1. Summary

Ship an Angular 22 (current stable at spec date; pin the stable 22.x the CLI scaffolds) + Angular Material / CDK + NgRx SignalStore app that repeats the cut-C information architecture against the same `webapi` people routes. Session is authorization code + PKCE against `identity`, client `adviser-portal-angular`. This app does not host a password form.

ClientId `adviser-portal-angular` is reserved and seeded so the name cannot be reused by Customer Portal or Back Office. This cut starts authorize, callback redeem, refresh, revocation, and end-session on that client.

## 2. Scope

**In**

- Repo folder `labs/adviser-portal-angular/` (standalone Angular application)
- Aspire resource `adviser-portal-angular` in **Development** only (static / `ng serve` origin published for CORS)
- OpenIddict seed row `ClientId = adviser-portal-angular` (public + PKCE, password grant off) plus allow-list map entry matching `adviser-portal` (SystemAdmin, TenantAdmin, Adviser). Redirect `{labOrigin}/callback`. Post-logout `{labOrigin}/`
- Pages and routes aligned with product cut C: Customers, Advisers, Profile, `/forbidden`
- SignalStore session: access and refresh tokens from the callback, last `GET /users/me`, optional firm name from `GET /tenants/by-code/{tenantCode}`
- Tokens in `sessionStorage` (not `localStorage`)
- Material shell: sidenav + toolbar. Visuals may diverge from the Tailwind product. Information architecture must not
- Lab acceptance L1–L15 below

**Out**

- Replacing or editing the product Vite app
- A password form in this app, or the password grant
- Hard-coded `/connect/*` paths. Use the identity discovery document
- New `webapi` routes, policies, or schema
- ClientIds `customer-portal` and Back Office
- Dashboard, Accounts, Transactions, Instruments, tenant CRUD, currency admin
- NgRx global `Store` / `Effects` / ComponentStore
- Tailwind or a second CSS framework
- Angular Universal / SSR
- NgModule application shell
- Production Aspire wiring; default `aspire run` for product work still starts only the React portal unless a lab task says otherwise
- Promoting this file to product `accepted`

## 3. Isolation

| Area | Rule |
| --- | --- |
| Product `docs/` | SoT. Do not rewrite accepted portal / identity specs for this lab. |
| `labs/` | Lab-only. Non-lab tasks must not touch this tree. |
| Product `adviser-portal` | Do not change routes, token store, or packages. |
| Identity protocol | OpenIddict default paths stay. No remaps. No password grant. |
| HTTP | Same people contracts as product cut C. |
| Packages | Lab has its own `package.json`. Do not hoist into the React app. |

Allowed Development-only host edits (lab task only): CORS origin for the lab, OpenIddict seed row + allow-list key `adviser-portal-angular`, Aspire resource registration. Do not change A1–A10 behaviour for client `adviser-portal`.

## 4. Stack

| Item | Choice |
| --- | --- |
| Angular | Current stable 22.x at generation (`ng new` defaults). Zoneless if that is the CLI default. |
| UI | Angular Material + CDK only |
| State | `@ngrx/signals` SignalStore per feature. No global Store. |
| Routing | `provideRouter`, standalone components |
| HTTP | `provideHttpClient` + interceptor: `Authorization: Bearer`, 401 → try refresh **once** when a refresh token is present, else clear session and start authorize. A sign-out in progress does not start authorize. |
| Auth protocol | Authorization code + PKCE. Discovery document on the identity authority. ClientId `adviser-portal-angular`. |
| ClientId | `adviser-portal-angular` |
| Aspire resource | `adviser-portal-angular` |
| Token store | SignalStore + `sessionStorage` |

## 5. Session

Hosted login stays on `identity`. This app redirects there and redeems the code.

1. An unsigned visit to an in-app path stores that path, shows “Redirecting to sign in…”, and assigns the discovery `authorization_endpoint`.
2. Authorize uses client `adviser-portal-angular`, response type `code`, scope `openid profile offline_access api`, and S256 PKCE. The redirect URI is `{origin}/callback`.
3. `/callback` exchanges the code once at the discovery `token_endpoint` and stores the access token and refresh token. A bad state shows “Sign-in callback was invalid.” and stores nothing.
4. With an access token and no current user, the shell calls `GET /users/me`. 200 → role-filtered shell. If `tenantCode` is set, load `GET /tenants/by-code/{tenantCode}` for the firm Name.
5. 401 on an API call refreshes once when a refresh token is present, using client `adviser-portal-angular`. Otherwise clear the session and start authorize, unless sign-out is already in progress.
6. Sign out posts the refresh token to the discovery `revocation_endpoint`, clears the store, and assigns the discovery `end_session_endpoint` with `post_logout_redirect_uri={origin}/`. A revoke failure still clears and redirects. While that flag is set, do not start authorize.
7. No password form in this app.

A leftover Customer token still lands on `/forbidden`.

PKCE verifier, state, pending flag, and the sign-out flag live in `sessionStorage` under the `adviser-portal-angular.` prefix. The sign-out flag is cleared when the session module loads.

## 6. Routes

Same paths as product cut C so the two apps can be compared.

| Path | Who | Behaviour |
| --- | --- | --- |
| `/session` | Anyone | “Redirecting to sign in…”. Starts authorize. Not a token paste panel. |
| `/` | Allowed roles | Redirect `/customers` for TenantAdmin / Adviser. SystemAdmin: no Customers workspace (same as C4). |
| `/customers` | TenantAdmin, Adviser | List. |
| `/customers/new` | Same | Create. |
| `/customers/:id` | Same | Read-only detail. Disable / enable live here. |
| `/customers/:id/edit` | Same | Rename; TenantAdmin may reassign. |
| `/advisers` | TenantAdmin | List. |
| `/advisers/new` | TenantAdmin | Create. |
| `/advisers/:id` | TenantAdmin | Read-only detail. |
| `/advisers/:id/edit` | TenantAdmin | Rename. |
| `/profile` | TenantAdmin, Adviser; SystemAdmin allowed | Name + password change against `PUT /users/me` and `PUT /users/me/password`. |
| `/forbidden` | Wrong-role leftover token | Second line if a token is present but the route is not allowed. |
| `/callback` | Anyone | Redeem one authorization code, store tokens, then the stored in-app path or `/`. |
| `/login` | — | Same as `/session`. Must not be a token-issuing password page. |

Deep link while signed out: store the in-app path, redirect to identity, then return to that path after callback.

## 7. Client and host

| Item | Value |
| --- | --- |
| ClientId | `adviser-portal-angular` |
| Type | Public + PKCE. Password grant off. |
| Scopes | `openid`, `profile`, `offline_access`, `api` |
| Redirect | `{labOrigin}/callback` |
| Post-logout | `{labOrigin}/` |
| Allow-list | SystemAdmin, TenantAdmin, Adviser |
| Lab origin | Aspire-published Development URL for resource `adviser-portal-angular` |

`webapi` CORS in Development includes that origin. Product `adviser-portal` redirects stay as they are.

## 8. HTTP used (already accepted on the server)

No new verbs. Shapes stay in the people Feature Specs.

| Call | Why |
| --- | --- |
| `GET /users/me` | Session probe |
| `PUT /users/me` | Profile name |
| `PUT /users/me/password` | Profile password; 204 then clear tokens and start authorize |
| `GET /tenants/by-code/{code}` | Shell firm Name (`tenants.read`) |
| `GET/POST /users/customers` | List / create |
| `GET/PUT /users/customers/{id}` | Detail / edit |
| `POST /users/customers/{id}/disable` and `/enable` | `rowVersion` required |
| `GET/POST /users/advisers` | TenantAdmin |
| `GET/PUT /users/advisers/{id}` | TenantAdmin |
| `POST /users/advisers/{id}/disable` and `/enable` | TenantAdmin |

List envelope `{ items, page, pageSize, totalCount }`. Ids in the UI are PublicId values. Cross-tenant or unassigned customer id → “Not found” on that URL, not 403, not a bounce to the list before the message.

TenantAdmin customer list joins adviser names from `GET /users/advisers`. Adviser caller does not request `/users/advisers`. Create-customer: TenantAdmin sends `adviserId`; Adviser omits it.

Stale `rowVersion` → tell the user to reload; do not replay the old version. Disable adviser while assigned customers are Active → ordinary 400 sentence + link to `/customers?adviserId=`.

## 9. Folder layout (lab app)

```text
labs/adviser-portal-angular/
├── src/
│   ├── app/
│   │   ├── app.config.ts
│   │   ├── app.routes.ts
│   │   └── app.ts
│   ├── features/
│   │   ├── session/
│   │   ├── profile/
│   │   ├── customers/
│   │   └── advisers/
│   ├── shared/
│   │   ├── api/
│   │   ├── auth/
│   │   └── ui/
│   └── layouts/
└── package.json
```

One SignalStore per feature folder. Shared HTTP client in `shared/api`. Do not invent a global `pages/` tree.

## 10. Acceptance (this lab cut)

Do not re-run A1–A10 or B1–B11 except where a page must show the result. Hosted login is `identity`. Lab tests stub the discovery document.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| L1 | TenantAdmin, no lab session | Open `/customers` | Redirect to identity authorize. After callback and probe, land on `/customers`. No token-issuing password page and no token paste form. |
| L2 | Same session as L1 | Shell | Firm **Name** from `GET /tenants/by-code/{tenantCode}`. Nav: Customers, Advisers, Profile. |
| L3 | Adviser token | Shell and `/advisers` | Nav has Customers + Profile, no Advisers. `/advisers` and `/advisers/new` render `/forbidden`. |
| L4 | SystemAdmin token | `/`, `/customers`, `/profile`, `/session` | `/` does not show a Customers workspace. `/customers` and `/advisers` are `/forbidden`. `/profile` and `/session` allowed. No tenant manage screens. |
| L5 | Customer token if one is present | Any product route | `/forbidden`. Lab never treats Customer as an in-app role. |
| L6 | TenantAdmin | Create customer (`name`, `email`, `password`, `adviserId` of an Active adviser) | `POST /users/customers` includes `adviserId`. 201 → `/customers/{id}`. Password is not shown again. |
| L7 | Adviser | Create customer | Body **omits** `adviserId`. New row’s `adviserId` is the caller. |
| L8 | TenantAdmin | Customers list | Each row shows adviser **name** joined from `GET /users/advisers`. Adviser caller does not request `/users/advisers`. |
| L9 | TenantAdmin | `/customers/{id}` vs `/customers/{id}/edit` | Detail is read-only (disable/enable live here). Edit changes name and may change `adviserId`. Adviser edit has no adviser control. |
| L10 | TenantAdmin | Disable / enable customer with current `rowVersion` | 204. Stale `rowVersion` → reload message; no replay. |
| L11 | TenantAdmin | Create / rename / disable adviser | Same route split as customers. Disable while assigned customers are Active → ordinary 400 sentence + link to `/customers?adviserId=`. |
| L12 | TenantAdmin | `PUT /users/me` and `PUT /users/me/password` | Name updates. Password 204 then the lab clears tokens and starts authorize again. |
| L13 | Any allowed role | Sign out | Tokens cleared. Refresh token revoked at the discovery revocation endpoint. Browser assigned to the discovery end-session endpoint. Next visit is hosted login. The same authorization code is not exchanged twice. |
| L14 | Deep link `/customers/{id}` while signed out | After callback | Returns to that path when it is an in-app path. |
| L15 | Cross-tenant or unassigned customer id | Open `/customers/{id}` | “Not found” on that URL. Not 403. Not a bounce to the list before the message. |

Out of this cut: Dashboard, ledger pages, `/currencies` UI, tenant CRUD.

## 11. Tests (lab app)

Portal unit / component tests assert routes, role visibility, and API-client shapes. Do not re-prove backend isolation here. Do not add IdentityHost protocol tests for this ClientId beyond “row exists and password grant is off”.

## 12. Later lab cuts (not this file)

- Phase 2 Accounts / Transactions pages (wait for a product portal cut, then decide whether the lab tracks it)
