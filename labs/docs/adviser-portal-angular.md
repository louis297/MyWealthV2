---
title: Adviser Portal (Angular lab)
status: draft
phase: lab
language: en
created: 2026-09-27
updated: 2026-09-27
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

Ship an Angular 22 (current stable at spec date; pin the stable 22.x the CLI scaffolds) + Angular Material / CDK + NgRx SignalStore app that repeats the cut-C information architecture against the same `webapi` people routes. Session is a **lab bearer panel**: the operator pastes tokens obtained outside this app. This cut does **not** run hosted login, authorization-code + PKCE, or `/callback`.

ClientId `adviser-portal-angular` is reserved and seeded so the name cannot be reused by Customer Portal or Back Office. The seeded client is not used to authorize in this cut.

## 2. Scope

**In**

- Repo folder `labs/adviser-portal-angular/` (standalone Angular application)
- Aspire resource `adviser-portal-angular` in **Development** only (static / `ng serve` origin published for CORS)
- OpenIddict seed row `ClientId = adviser-portal-angular` (public + PKCE, password grant off) plus allow-list map entry matching `adviser-portal` (SystemAdmin, TenantAdmin, Adviser). Redirect URIs may be registered; this cut does not start authorize
- Pages and routes aligned with product cut C: Customers, Advisers, Profile, `/forbidden`
- SignalStore session: pasted access token, optional refresh token, last `GET /users/me`, optional firm name from `GET /tenants/by-code/{tenantCode}`
- Tokens in `sessionStorage` (not `localStorage`)
- Material shell: sidenav + toolbar. Visuals may diverge from the Tailwind product. Information architecture must not
- Lab acceptance L1–L15 below

**Out**

- Replacing or editing the product Vite app
- Hosted login, `/connect/authorize`, `/callback` redeem, end-session from this app
- Password grant; any Angular field that **issues** tokens
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
| HTTP | `provideHttpClient` + interceptor: `Authorization: Bearer`, 401 → try refresh **once** when a refresh token is present, else clear session and return to `/session` |
| Auth protocol this cut | None. Bearer is supplied by the operator. |
| ClientId | `adviser-portal-angular` |
| Aspire resource | `adviser-portal-angular` |
| Token store | SignalStore + `sessionStorage` |

## 5. Session (no hosted login)

Product portal still uses hosted login on `identity`. This lab does not.

1. `/session` is the lab session panel (in the product app this path is a dev probe and is hidden from the menu; here it is the way in).
2. Operator pastes an **access token** issued to an allow-listed role (typically taken from a product `adviser-portal` session or a Development tool). Optional refresh token.
3. App writes tokens to the session store and calls `GET /users/me`.
4. 200 → role-filtered shell. If `tenantCode` is set, load `GET /tenants/by-code/{tenantCode}` for the firm Name.
5. 401 / 403 on the probe → show the panel error; do not keep a half session.
6. Sign out clears the store and `sessionStorage`. Do **not** call `/connect/revocation` or `/connect/logout` in this cut (this app never created the hosted-login cookie).
7. No React- or Angular-hosted password form that talks to `/connect/token`.

A leftover Customer token (should not exist for `adviser-portal`) still lands on `/forbidden`.

OIDC authorize + PKCE + single-flight `/callback` + revoke-then-end-session is an optional later lab cut. It is not this file.

## 6. Routes

Same paths as product cut C so the two apps can be compared.

| Path | Who | Behaviour |
| --- | --- | --- |
| `/session` | Anyone | Bearer panel. After a valid probe, go to the return path or `/`. |
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
| `/callback` | — | Must not redeem codes in this cut. Route may 404 or redirect `/session`. |
| `/login` | — | Must not be a token-issuing password page. |

Deep link while signed out: store the in-app path, send the operator to `/session`, then return to that path after a successful probe (lab stand-in for C14).

## 7. Client and host

| Item | Value |
| --- | --- |
| ClientId | `adviser-portal-angular` |
| Type | Public + PKCE seeded. Password grant off. Unused for authorize in this cut. |
| Scopes (seed only) | `openid`, `profile`, `offline_access`, `api` |
| Redirect (seed only) | `{labOrigin}/callback` |
| Post-logout (seed only) | `{labOrigin}/` |
| Allow-list | SystemAdmin, TenantAdmin, Adviser |
| Lab origin | Aspire-published Development URL for resource `adviser-portal-angular` |

`webapi` CORS in Development includes that origin. Product `adviser-portal` redirects stay as they are.

## 8. HTTP used (already accepted on the server)

No new verbs. Shapes stay in the people Feature Specs.

| Call | Why |
| --- | --- |
| `GET /users/me` | Session probe |
| `PUT /users/me` | Profile name |
| `PUT /users/me/password` | Profile password; 204 then clear lab session (operator must paste a new token) |
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

Do not re-run A1–A10 or B1–B11 except where a page must show the result. Tokens used below are issued **outside** this app to the named role.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| L1 | TenantAdmin access token, no lab session | Open `/customers` | Redirect `/session`. After a successful paste + probe, land on `/customers`. No token-issuing password page. |
| L2 | Same session as L1 | Shell | Firm **Name** from `GET /tenants/by-code/{tenantCode}`. Nav: Customers, Advisers, Profile. |
| L3 | Adviser token | Shell and `/advisers` | Nav has Customers + Profile, no Advisers. `/advisers` and `/advisers/new` render `/forbidden`. |
| L4 | SystemAdmin token | `/`, `/customers`, `/profile`, `/session` | `/` does not show a Customers workspace. `/customers` and `/advisers` are `/forbidden`. `/profile` and `/session` allowed. No tenant manage screens. |
| L5 | Customer token if one is pasted | Any product route | `/forbidden`. Lab never treats Customer as an in-app role. |
| L6 | TenantAdmin | Create customer (`name`, `email`, `password`, `adviserId` of an Active adviser) | `POST /users/customers` includes `adviserId`. 201 → `/customers/{id}`. Password is not shown again. |
| L7 | Adviser | Create customer | Body **omits** `adviserId`. New row’s `adviserId` is the caller. |
| L8 | TenantAdmin | Customers list | Each row shows adviser **name** joined from `GET /users/advisers`. Adviser caller does not request `/users/advisers`. |
| L9 | TenantAdmin | `/customers/{id}` vs `/customers/{id}/edit` | Detail is read-only (disable/enable live here). Edit changes name and may change `adviserId`. Adviser edit has no adviser control. |
| L10 | TenantAdmin | Disable / enable customer with current `rowVersion` | 204. Stale `rowVersion` → reload message; no replay. |
| L11 | TenantAdmin | Create / rename / disable adviser | Same route split as customers. Disable while assigned customers are Active → ordinary 400 sentence + link to `/customers?adviserId=`. |
| L12 | TenantAdmin | `PUT /users/me` and `PUT /users/me/password` | Name updates. Password 204 then the lab clears tokens and returns to `/session`. |
| L13 | Any allowed role | Sign out | Lab `sessionStorage` empty. Identity hosted-login cookie is **not** required to change (this app did not set it). Next visit is `/session`. |
| L14 | Deep link `/customers/{id}` while signed out | After a successful probe | Returns to that path when it is an in-app path. |
| L15 | Cross-tenant or unassigned customer id | Open `/customers/{id}` | “Not found” on that URL. Not 403. Not a bounce to the list before the message. |

Out of this cut: Dashboard, ledger pages, `/currencies` UI, tenant CRUD, authorize / callback / end-session.

## 11. Tests (lab app)

Portal unit / component tests assert routes, role visibility, and API-client shapes. Do not re-prove backend isolation here. Do not add IdentityHost protocol tests for this ClientId beyond “row exists and password grant is off”.

## 12. Later lab cuts (not this file)

- Authorization code + PKCE against `adviser-portal-angular` (would then use hosted login)
- Phase 2 Accounts / Transactions pages (wait for a product portal cut, then decide whether the lab tracks it)
