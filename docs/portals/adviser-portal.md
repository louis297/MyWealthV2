---
title: Adviser Portal
status: review
phase: 1
language: en
created: 2026-09-12
updated: 2026-09-14
related:
  - README.md
  - frontend-conventions.md
  - frontend-implementation-notes.md
  - ../function-plan.md
  - ../features/identity-auth.md
  - ../features/tenants.md
  - ../features/advisers.md
  - ../features/customers.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
  - ../adr/0013-roles-authorization-single-user-table.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
---

# Adviser Portal

Only Phase-1 frontend and only public OIDC client. Aspire resource name and ClientId are both `adviser-portal`.

The portal **does not issue tokens**. Password collection stays on hosted login (`identity`). This file is not a backend Feature Spec. Construction detail lives in [frontend-implementation-notes.md](frontend-implementation-notes.md).

Tenant **management** (create / rename / reporting currency / disable) is not this app. That is `tenants.manage` on Scalar, or a later Back Office client. The shell may **read** the current firm’s name through `GET /tenants/by-code/{code}` (`tenants.read`).

---

## Landed — shell and callback

**In repo master (2026-09-13).** Do not rebuild it.

Vite on Aspire, discovery + PKCE authorize, `/callback`, session probe via `GET /users/me`, 401 refresh-once, OpenIddict redirect upsert including the Aspire dashboard alias. No React password page. SystemAdmin may use the probe (Development seed).

| Path | Behaviour (landed) |
| --- | --- |
| `/` | No access token → authorize. Signed in → session probe. |
| `/callback` | PKCE exchange **once per code**, then the return path. Concurrent mounts must not POST `/connect/token` twice (no OpenIddict `ID2010`). |

---

## Backend prerequisites (not this file)

People HTTP has landed. Two accepted-spec **amendments** must land before pages:

| Step | Spec | Ships |
| --- | --- | --- |
| A | [identity-auth](../features/identity-auth.md) | Client × role allow-list on IdentityHost: after password, before the authorization code. `adviser-portal` allows SystemAdmin, TenantAdmin, Adviser. A Customer with a correct password does **not** get a code. Uniform login failure. Customers hosted-login smoke changes to expect failure on this client. |
| B | [tenants](../features/tenants.md) + [ADR 0013](../adr/0013-roles-authorization-single-user-table.md) | `GET /tenants/by-code/{code}`, policy `tenants.read`. Own-tenant 200 for TenantAdmin / Adviser; other codes 404; Customer 403. |

Do not mix A or B into portal page commits.

Reserved, not registered in Phase 1: client `customer-portal` (Customer) and a Back Office client (SystemAdmin only).

---

## Current slice — pages

Do not start until A and B have landed.

### In

- Role-filtered shell. Default home = `/customers`.
- Profile: `PUT /users/me`, `PUT /users/me/password`.
- Customers: list / detail / new / edit / disable / enable. Visibility follows the Feature Spec (TenantAdmin = firm; Adviser = assigned).
- Advisers: list / detail / new / edit / disable / enable. TenantAdmin only.
- After `/users/me`, load `GET /tenants/by-code/{tenantCode}` for the shell name when the caller has a tenant.
- TenantAdmin customer list joins adviser names from `GET /users/advisers` (no `adviserName` on the customers item).
- Portal `/forbidden` as a second line if a wrong-role token still appears.

### Out

- Rebuilding the landed OIDC cut.
- React password login page.
- Tenant CRUD pages. TenantAdmin pages. Currency pages.
- Dashboard, Accounts, Transactions, Instruments.
- Customer Portal. Back Office UI.

### Routes

| Path | Who | Behaviour |
| --- | --- | --- |
| `/callback` | Protocol | Exchange a given `code` once, then return path. |
| `/` | Allowed roles | Redirect `/customers`. |
| `/customers` | TenantAdmin, Adviser | List. |
| `/customers/new` | Same | Create. |
| `/customers/:id` | Same | Read-only detail. |
| `/customers/:id/edit` | Same | Rename; TenantAdmin may reassign. |
| `/advisers` | TenantAdmin | List. |
| `/advisers/new` | TenantAdmin | Create. |
| `/advisers/:id` | TenantAdmin | Read-only detail. |
| `/advisers/:id/edit` | TenantAdmin | Rename. |
| `/profile` | TenantAdmin, Adviser; SystemAdmin allowed | Name + password. |
| `/session` | Dev aid | Landed probe. Not in the product menu. |
| `/login` | — | Must not be a password page. |

### Client

| Item | Value |
| --- | --- |
| ClientId | `adviser-portal` |
| Type | Public + PKCE. No client secret. Password grant off. |
| Scopes | `openid`, `profile`, `offline_access`, `api` |
| Redirect | `{portalOrigin}/callback` |
| Post-logout | `{portalOrigin}/` |
| Token store | Redux + `sessionStorage` |
| Allow-list (after A) | SystemAdmin, TenantAdmin, Adviser |
| Sign out | Clear store + `sessionStorage`; `POST /connect/revocation` with the refresh token; redirect to discovery `end_session_endpoint` with `client_id` and `post_logout_redirect_uri={origin}/`. While that is in flight, `RequireSession`, `HomePage`, and the 401 handler must **not** call `startAuthorize`. |

### Acceptance (cut C)

Requires A and B in the running hosts. Do not re-test A1–A10 or B1–B11 here except where the page must show the result.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| C1 | TenantAdmin, no session | Open `/customers` | Authorize → hosted login → callback → land on `/customers`. No React password page. |
| C2 | Same session as C1 | Shell | Firm **Name** from `GET /tenants/by-code/{tenantCode}`. Code may appear as secondary. Nav: Customers, Advisers, Profile. |
| C3 | Adviser session | Shell and `/advisers` | Nav has Customers + Profile, no Advisers. `/advisers` and `/advisers/new` render `/forbidden`, not a fake 404. |
| C4 | SystemAdmin session | `/`, `/customers`, `/profile`, `/session` | `/` does not show a Customers workspace. `/customers` and `/advisers` are `/forbidden`. `/profile` and `/session` allowed. No tenant manage screens. |
| C5 | Customer + correct password | Hosted login for this client | Covered by A1. Portal never stores a Customer session. If a leftover token appears, `/forbidden`. |
| C6 | TenantAdmin | Create customer (`name`, `email`, `password`, `adviserId` of an Active adviser) | `POST /users/customers` includes `adviserId`. 201 → `/customers/{id}`. Password is not shown again. |
| C7 | Adviser | Create customer | Body **omits** `adviserId`. New row’s `adviserId` is the caller. |
| C8 | TenantAdmin | Customers list | Each row shows adviser **name** joined from `GET /users/advisers`, not only PublicId. Adviser caller does not request `/users/advisers`. |
| C9 | TenantAdmin | `/customers/{id}` vs `/customers/{id}/edit` | Detail is read-only (disable/enable live here). Edit changes name and may change `adviserId`. Adviser edit has no adviser control. |
| C10 | TenantAdmin | Disable / enable customer with current `rowVersion` | 204. Stale `rowVersion` → page tells the user to reload; it does not replay the old version. |
| C11 | TenantAdmin | Create / rename / disable adviser | Same route split as customers. Disable while assigned customers are Active → ordinary 400 sentence + link to `/customers?adviserId=`. Not painted as `code=disabled`. |
| C12 | TenantAdmin | `PUT /users/me` and `PUT /users/me/password` | Name updates. Password 204 then the app clears tokens and starts authorize again. |
| C13 | Any allowed role | Sign out | Portal session cleared (`sessionStorage` empty). Refresh revoked at `/connect/revocation`. IdentityHost end-session clears the hosted-login cookie (R21). Next visit is hosted `/login`, not a silent authorize. Same authorization `code` is not exchanged twice. |
| C14 | Deep link `/customers/{id}` while signed out | After callback | Returns to that path when it is an in-app path (draft default 13.6). |
| C15 | Cross-tenant or unassigned customer id | Open `/customers/{id}` | “Not found” on that URL. Not 403. Not a bounce to the list before the message. |

Out of this cut: Dashboard, ledger pages, `/currencies` UI, tenant CRUD, a password field that issues tokens.

Suggested commits: [frontend-implementation-notes.md](frontend-implementation-notes.md) §12.

---

## Still out of Phase 1

Dashboard, Accounts, Transactions, Instruments, Customer Portal, Back Office UI, SystemAdmin tenant screens (Scalar).
