---
title: Frontend implementation notes
status: review
phase: 1
language: en
created: 2026-09-14
updated: 2026-09-15
related:
  - README.md
  - adviser-portal.md
  - frontend-conventions.md
  - ../function-plan.md
  - ../features/identity-auth.md
  - ../features/tenants.md
  - ../features/advisers.md
  - ../features/customers.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
  - ../adr/0013-roles-authorization-single-user-table.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
---

# Frontend implementation notes

Page-by-page construction notes for the Adviser Portal **pages** cut. Not a backend Feature Spec.

- Inventory, role gate, acceptance → [adviser-portal.md](adviser-portal.md)
- Folder layout, naming, session, API client → [frontend-conventions.md](frontend-conventions.md)
- Fields, HTTP, 400 / 404 / 409 → the Feature Spec for that resource

If this file disagrees with an accepted spec, change this file.

Status is `review`. Locked rows in §13 are build contracts. Unmarked §13 rows stay draft defaults. Pages cut C and amendments A/B have landed; A1–A10 and B1–B11 passed. Acceptance for this cut is in [adviser-portal.md](adviser-portal.md) § Acceptance (cut C).

---

## 0. Two portal cuts

| Cut | Status |
| --- | --- |
| Shell + callback + session probe | **Landed** (2026-09-13). Do not rebuild OIDC / PKCE / probe. |
| Pages: role shell, Profile, Customers, Advisers | **Not started.** People APIs have landed. Identity allow-list and `GET /tenants/by-code/{code}` have not. |

Default home = Customers.

Backend work is **not** this file. It is two amendments on existing slices, then this cut:

```text
A  identity-auth — client × role allow-list
    └── B  tenants — GET /tenants/by-code/{code} + tenants.read
            └── C  this cut — portal pages
```

---

## 1. Already landed — do not touch

`src/AdviserPortal` already has `features/session/*`, a one-line `ShellLayout`, `/` = probe, `/callback`, and `shared/api` with 401 refresh-once.

Locked behaviour:

- Unauthenticated visit → discovery → `/connect/authorize` (PKCE S256 + `offline_access`)
- `/callback` exchanges the code **once**; tokens live in Redux + `sessionStorage` (not `localStorage`)
- Then `GET {webapi}/users/me`
- 401 → refresh **once** at the identity token endpoint; on failure clear session and authorize again
- No React password login page
- ClientId / Aspire name: `adviser-portal`
- Resource ids are PublicId only

This cut adds feature folders and chrome on top of that stack.

---

## 2. In / Out

**In**

- Role-filtered nav + logout (end-session)
- Role gate on the portal (second line; IdentityHost is the first)
- Profile: rename + password change
- Customers: list / read-only detail / create / edit / disable / enable
- Advisers: same minus reassign (TenantAdmin only)
- RTK Query for those REST calls; session stays on the existing slice
- List filters in the URL query string
- Shell firm name from `GET /tenants/by-code/{code}`

**Out**

- Rebuilding OIDC / discovery / PKCE / refresh-once
- React password login page; Customer Portal client; Back Office UI
- Dashboard, Accounts, Holdings, Transactions, Instruments
- Currency admin (`GET /currencies` has no Phase-1 portal consumer)
- SystemAdmin tenant / TenantAdmin CRUD (Scalar)
- Invite, forgot-password, admin reset of someone else’s password, avatars
- Assigned-customer counts, assignment history, account blocks

---

## 3. Folders

No global `pages/` tree.

```text
src/
├── app/                 store, hooks, router, providers
├── features/
│   ├── session/         already there; add logout + role helpers
│   ├── profile/
│   ├── customers/
│   └── advisers/
├── shared/
│   ├── api/             existing fetch wrapper → RTK Query baseQuery
│   ├── components/      pager, status badge, confirm, error bar
│   └── types/
└── layouts/ShellLayout.tsx
```

One feature folder: `*Api.ts`, list / detail / create / edit pages, shared form.

---

## 4. Shared infrastructure

### 4.1 Session

Keep the existing slice: access, refresh, last `/users/me`.

Menus and gates read `/users/me`.role. Do not parse the JWT. Do not call `/users/me/permissions`.

JSON enums are camelCase: `systemAdmin` / `tenantAdmin` / `adviser` / `customer`; `pendingActivation` / `active` / `disabled`.

`/users/me` does **not** include tenant name. After `me`, if `tenantCode` is set, call `GET /tenants/by-code/{tenantCode}` (`tenants.read`) and keep `{ id, name, code, … }` in the store. SystemAdmin skips that call (no tenant). Customer must not receive an `adviser-portal` token after amendment A; if one appears, do not call by-code (403).

### 4.2 API client

One RTK Query `api`. `baseQuery` wraps the existing Bearer + 401 refresh-once client.

Tags: `Me`, `Tenant`, `Customer`, `Adviser`. Writes invalidate the matching list / detail.

Create returns `201 { id }` only. Follow with `GET`. Do not treat the POST body as the item.

### 4.3 List queries

Envelope `{ items, page, pageSize, totalCount }`.

| Query | Default | UI |
| --- | --- | --- |
| `page` | 1 | Pager |
| `pageSize` | 20 (max 100) | Fixed at 20 in this cut |
| `enabledOnly` | omitted = all | Active-only toggle (draft default) |
| `search` | omitted | Name / email contains, or PublicId exact. Debounce ~300ms |
| `adviserId` | Customers + TenantAdmin only | Adviser filter |

Write filters into the URL: `/customers?search=&enabledOnly=true&adviserId=&page=1`.

### 4.4 `rowVersion`

PUT / disable / enable send the current `rowVersion`. On 409, GET again and tell the caller the row changed. Do not replay the stale version.

### 4.5 Password fields

[frontend-conventions.md](frontend-conventions.md) forbids a password field that **issues tokens**. This cut still has password inputs:

- Profile change: `currentPassword` + `newPassword`
- Create Adviser / Customer: initial `password` (required, minimum 8, lands `Active`)

Hosted login on `identity` remains the only place that collects a password for a token.

---

## 5. Routes and gates

| Path | Who | Behaviour |
| --- | --- | --- |
| `/callback` | Protocol | Exchange, then the stored return path |
| `/` | Allowed roles | Redirect to `/customers` |
| `/customers` | TenantAdmin, Adviser | List |
| `/customers/new` | Same | Create |
| `/customers/:id` | Same; Adviser = assigned only | Read-only detail |
| `/customers/:id/edit` | Same | Rename; TenantAdmin may reassign |
| `/advisers` | TenantAdmin | List |
| `/advisers/new` | TenantAdmin | Create |
| `/advisers/:id` | TenantAdmin | Read-only detail |
| `/advisers/:id/edit` | TenantAdmin | Rename |
| `/profile` | TenantAdmin, Adviser (SystemAdmin allowed) | Name + password |
| `/session` | Dev aid | Probe. Not in the product menu |
| `/forbidden` | Authenticated, wrong role | Copy + sign out |

Order:

1. No access token → authorize (existing)
2. Token present, no `me` yet → `GET /users/me`
3. `role === customer` → must not happen after amendment A. If it does, `/forbidden`
4. `role === systemAdmin` → `/customers` and `/advisers` → `/forbidden`; Profile + `/session` stay
5. Adviser hits `/advisers*` → `/forbidden` (API is 403, not 404)
6. Adviser opens another adviser’s `/customers/:id` → page shows the API 404

---

## 6. Shell

- Top bar: product name, firm **Name** from by-code (SystemAdmin: “Platform”), `tenantCode` as secondary, display name, role, sign out
- Side nav:

| Item | TenantAdmin | Adviser | SystemAdmin | Customer |
| --- | --- | --- | --- | --- |
| Customers | ✓ | ✓ | | |
| Advisers | ✓ | | | |
| Profile | ✓ | ✓ | ✓ | |
| Session probe | not in menu | not in menu | URL only | |

Sign out (locked with C13 / identity-auth R21): clear Redux + `sessionStorage`, `POST /connect/revocation` for the refresh token, then discovery end-session (`/connect/logout`) with `client_id` and `post_logout_redirect_uri={portalOrigin}/`. Do not call `startAuthorize` from `RequireSession`, `HomePage`, or the 401 handler while end-session is running. `completeCallback` must be single-flight so React StrictMode cannot redeem the same code twice (`ID2010`). Keep StrictMode enabled.

No tenant switcher. No greyed Phase-2 ledger items.

Tenant **management** (create firm, rename, reporting currency, disable) is not this app. That is `tenants.manage` on Scalar / a later Back Office client.

---

## 7. Profile

- `GET /users/me` — already called
- `PUT /users/me` `{ name, rowVersion }` → 204
- `PUT /users/me/password` `{ currentPassword, newPassword }` → 204 and that subject’s refresh dies

Read-only: email, role, status, tenantCode, id. Editable: name (1–200, trim).

After a successful password change the caller must run authorization code again (identity-auth R7). Draft default: clear the session and authorize immediately.

---

## 8. Customers

Contract: [features/customers.md](../features/customers.md).

### 8.1 List

Columns (draft): Name, Email, Status, Adviser, Created.

The item has `adviserId` only. **Locked (13.9 A):** TenantAdmin loads `GET /users/advisers` and maps `id → name` in the store. Do not add `adviserName` to the customers item.

An Adviser calling `/users/advisers` is 403. That list uses `/users/me`.name (or “Me”).

Filters: search, Active-only, TenantAdmin `adviserId=`. An Adviser does not see the adviser filter. A foreign `adviserId` in the URL is API 400.

Row click → `/customers/:id`. Primary action → `/customers/new`.

### 8.2 Create

| Field | TenantAdmin | Adviser |
| --- | --- | --- |
| name | required | required |
| email | required | required |
| password | required, min 8 | same |
| adviserId | required; Active advisers only | omit; do not render |

201 → `/customers/{id}`. Do not echo the password.

### 8.3 Detail / edit

**Locked (13.12 B).** Detail is read-only. Edit is `/customers/:id/edit`.

Disable / enable stay on the detail page (`{ rowVersion }` only). Phase 1 has no ledger guard. Idempotent 204 is success.

TenantAdmin edit may send `adviserId`. An Adviser PUT that includes `adviserId` is 400 — do not render the control.

### 8.4 Errors

| API | Page |
| --- | --- |
| 400 `disabled` / `target=tenant` | Cannot create while the firm is disabled |
| 400 `disabled` / `target=user` | Cannot attach to a disabled adviser |
| Duplicate email | Field error |
| Cross-tenant / not assigned / wrong collection | Detail 404 |
| 409 | §4.4 |

---

## 9. Advisers

Contract: [features/advisers.md](../features/advisers.md). TenantAdmin only.

List columns (draft): Name, Email, Status, Created. The API has **no** assigned-customer count. Do not scan Customers to invent one.

Create: `name` + `email` + `password`. No `tenantId`. 201 → `/advisers/{id}`.

Detail read-only; `/advisers/:id/edit` is name only.

Disable while assigned customers are not Disabled → ordinary 400 (`Reassign or disable assigned customers before disabling this adviser.`). **Not** §4.1 `code=disabled` / `target=user`. Show that sentence and link to `/customers?adviserId={id}`.

---

## 10. Error map

| HTTP / body | UI |
| --- | --- |
| 401 | Existing refresh-once; then authorize |
| 403 | `/forbidden` or a hidden control the caller deep-linked |
| 404 | Detail “Not found”; unknown adviser filter → empty list |
| 409 | Refetch; ask to resubmit |
| 400 + `code=disabled` + `target=tenant` | Page banner |
| 400 + `code=disabled` + `target=user` | Page banner (create / reassign Customer only) |
| 400 + `errors` | Field errors; adviser disable-guard uses the detail sentence |
| Cross-tenant 404 | Never paint as 403 |

---

## 11. Tests (portal)

Existing Vitest. Minimum for this cut:

- Nav: TenantAdmin sees Advisers; Adviser does not; Customer hits `/forbidden`
- Adviser on `/advisers` is gated
- Create Customer: TenantAdmin body includes `adviserId`; Adviser body omits it
- 409 does not replay a stale `rowVersion`
- Adviser disable failure is ordinary 400, not `code=disabled`

Do not restub the OIDC handshake.

---

## 12. Suggested commits (cut C only)

Repository must build after each commit. Do not mix IdentityHost or `tenants.read` into these.

1. RTK Query on the existing baseQuery; `Me` / `Tenant` tags; router + role gate + `/forbidden`; `ShellLayout` + by-code name + sign out
2. Profile rename + password + re-authorize
3. Customers list (URL filters, pager, adviser-name map)
4. Customers create + detail + edit + disable/enable + 409
5. Advisers list + create + detail + edit + disable-guard copy
6. Probe at `/session`; `/` → `/customers`

---

## 13. Open vs locked

### Locked

| Id | Decision |
| --- | --- |
| 13.5 | IdentityHost allow-list by `client_id` after password, before the code. `adviser-portal`: SystemAdmin, TenantAdmin, Adviser. Reserved rows: `customer-portal` = Customer; Back Office (name unlocked) = SystemAdmin. Uniform login failure; do not say “no identity”. Portal `/forbidden` is a second line. Separate backend amendment A. |
| 13.9 | Client-side join from `GET /users/advisers`. No `adviserName` on the customers item. |
| 13.12 | Read-only detail + `/…/:id/edit`. Disable / enable stay on detail. |
| 13.18 | Firm **Name** on the shell. `GET /tenants/by-code/{code}` + `tenants.read`. Do not put `tenantName` on `/users/me`. TenantAdmin / Adviser: own code only (else 404). Customer 403. SystemAdmin any code. Separate backend amendment B. Path must be `by-code` so it does not collide with `GET /tenants/{id}`. |
| 13.2 | Sign out = clear portal + revoke refresh + IdentityHost end-session (identity-auth R21). `/callback` redeems a given authorization code once. Keep React StrictMode. No `/auth/logout`. |

### Still draft defaults (not accepted)

| Id | Default |
| --- | --- |
| 13.1 | UI copy in English |
| 13.3 | Keep probe at `/session`, out of the menu |
| 13.4 | SystemAdmin: Profile + `/session`; product collections `/forbidden` |
| 13.6 | In-app `returnTo` after callback |
| 13.7 | Password change immediately re-authorizes |
| 13.8 | Active-only toggle only (no Disabled-only; the API cannot do that) |
| 13.10 | Create password: two boxes, no echo after 201 |
| 13.11 | Prev / next pager; pageSize fixed at 20 |
| 13.13 | Confirm disable; do not confirm enable |
| 13.14 | PublicId on detail only |
| 13.15 | `created` as `yyyy-mm-dd` |
| 13.16 | No toast library; 204 has no “Saved” toast |
| 13.17 | Stay on a 404 URL with “Not found” |
| 13.19 | This cut does not call `/currencies` |
| 13.20 | Portal tests = gates + submit shapes |
| 13.21 | No “try hosted login as this person” button |
| 13.22 | Adviser detail links to `/customers?adviserId=` with no count |
| 13.24 | No mobile-collapse acceptance |

---

## 14. Explicitly out

- `/login` password page on this origin
- `/tenants` manage UI and `/users/tenant-admins` pages
- Customer Portal and Back Office apps
- Painting `DisableAdviser` 400 as §4.1
- Reading permissions from the JWT
- `localStorage` for tokens
- Treating a list row as the detail cache after create
