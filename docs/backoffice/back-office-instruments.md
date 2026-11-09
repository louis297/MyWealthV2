---
title: Back Office — Instruments
status: accepted
phase: 2
language: en
created: 2026-11-09
updated: 2026-11-09
related:
  - README.md
  - back-office.md
  - ../features/instruments.md
  - ../features/currencies.md
  - ../adr/0010-instrument-catalog.md
  - ../adr/0013-roles-authorization-single-user-table.md
---

# Back Office — Instruments

Platform-ops page for the tenant instrument catalog. Consumes the landed instruments feature (`docs/features/instruments.md`). Backend contracts live in the Feature Spec. This file owns routes, page scope, field bindings, and behavioural acceptance for the Back Office.

**Status is `accepted`.** Implementation follows this file.

Backend prerequisites (landed):

- `GET /instruments` (SystemAdmin requires `?tenantId=`), `GET /instruments/{id}`, `POST /instruments`, `PUT /instruments/{id}`, `POST /instruments/{id}/disable`, `POST /instruments/{id}/enable`.
- Policies: `instruments.read`, `instruments.create`, `instruments.manage` — SystemAdmin has all three.
- Create and list for SystemAdmin require `tenantId` (tenant PublicId). Get / PUT / disable / enable use the instrument PublicId only and may cross tenants.
- `QuoteCurrency` immutable after create. Symbol unique per tenant (including disabled). No physical delete. `rowVersion` on PUT / disable / enable.
- Adviser Portal has no Instruments management page. Catalog management is Scalar or this host.

Session is the Back Office cookie. Calls go to `webapi` with Bearer. No BFF. No new policy.

---

## Current slice

**In**

- Instruments list for a chosen tenant (`/instruments` with tenant filter).
- Create instrument for the chosen tenant (symbol, name, quoteCurrency).
- Detail + rename (symbol and/or name) with `rowVersion`.
- Disable / enable with `rowVersion`.
- Search and `enabledOnly` on the list.
- Display of `isActive`, `quoteCurrency`, `tenantId`.

**Out**

- Price display or price edit (catalog HTTP does not return a price; `IMarketData` is server-side only).
- Changing `QuoteCurrency` after create.
- Physical delete.
- Cross-tenant ledger pages (Accounts, Transactions, Holdings, Net worth) — separate later slice.
- Adviser / Customer instruments screens.
- Instrument typeahead or picker UI (those stay on firm portals).
- Seeding or editing TestSeed rows from this host.
- Platform-wide instrument catalog (rows remain tenant-scoped).

---

## Routes

| Path | Who | Notes |
| --- | --- | --- |
| `/instruments` | SystemAdmin | List. Requires a tenant selection (query or picker). Links to create / detail. |
| `/instruments/new` | SystemAdmin | Create. Tenant required (pre-filled from list selection when present). |
| `/instruments/{id}` | SystemAdmin | Detail + rename / disable / enable. |

All routes require an authenticated SystemAdmin. Other roles that reach the host are shown `/forbidden`.

Tenant selection on the list is a required filter. The page does not show a cross-tenant union. Unknown tenant → empty list or the API 404 surfaced as an error state. Missing tenant selection shows a prompt to choose a tenant; it does not call `GET /instruments` without `tenantId`.

---

## Pages (field bindings)

Consumes existing endpoints. Field rules and validation live in `features/instruments.md`.

### List (`/instruments`)

- Tenant picker (required). Source: existing `GET /tenants` (or a local cache of tenants already loaded).
- List: `GET /instruments?tenantId={tenantPublicId}` (envelope).
- Query: `enabledOnly` (default all; `true` = active only), optional `search` (Symbol / Name contains CI or PublicId exact), `page` / `pageSize` (default 1 / 20, max 100).
- Columns: Symbol, Name, QuoteCurrency, IsActive, actions (open).
- Sort is server-side (symbol then name). The page does not re-sort.
- Create button navigates to `/instruments/new` with the selected tenant pre-filled.
- Empty state: “No instruments” when the tenant has none (distinct from “choose a tenant”).

### Create (`/instruments/new`)

- Fields: tenant (required, PublicId), symbol, name, quoteCurrency.
- `POST /instruments` with `{ tenantId, symbol, name, quoteCurrency }`.
- Symbol: trim, stored upper case, 1–32, letters / digits / `.` / `-` only. Uniqueness is server-side (including disabled rows).
- Name: 1–200, trim, preserve casing.
- QuoteCurrency: picker of enabled platform currencies (`GET /currencies?enabledOnly=true`). Immutable after create; the field is not shown on edit.
- Success: 201 `{ id }` → navigate to the detail (or back to the list for that tenant).
- Disabled tenant on create → surface the `disabled` / `target=tenant` error. Do not offer create when the selected tenant is known disabled (list of tenants can hide or disable the option).

### Detail (`/instruments/{id}`)

- Load: `GET /instruments/{id}` (SystemAdmin may open any tenant’s id).
- Display: Symbol, Name, QuoteCurrency (read-only), IsActive, TenantId (link to tenant detail when known).
- Rename: `PUT /instruments/{id}` with `{ symbol, name, rowVersion }`. Symbol and/or name only. Other fields in the body are not sent.
- Disable: `POST /instruments/{id}/disable` with `{ rowVersion }` when `isActive` is true.
- Enable: `POST /instruments/{id}/enable` with `{ rowVersion }` when `isActive` is false.
- Already-disabled + disable, or already-enabled + enable, is 204 (idempotent). The UI may hide the irrelevant action.
- Conflict: 409 on stale `rowVersion` → reload the item and show a short conflict message.
- No price column. No “view holdings” action in this slice (ledger inspection is separate).

QuoteCurrency is never editable. Physical delete is never offered. Disable is the only removal path.

---

## Acceptance (BO-I)

| ID | Behaviour |
| --- | --- |
| BO-I1 | `/instruments` without a selected tenant does not call `GET /instruments`. It prompts to choose a tenant. |
| BO-I2 | With a tenant selected, the list calls `GET /instruments?tenantId=` and shows Symbol, Name, QuoteCurrency, IsActive. `enabledOnly=true` omits inactive rows. Search is passed through. |
| BO-I3 | Create sends `POST /instruments` with `tenantId`, `symbol`, `name`, `quoteCurrency`. Success navigates to the new detail or the list. Duplicate symbol or disabled currency surfaces the 400. |
| BO-I4 | Detail loads `GET /instruments/{id}`. Rename sends `PUT` with `symbol` and/or `name` plus `rowVersion` only. QuoteCurrency is displayed and not editable. |
| BO-I5 | Disable and enable send the matching `POST` with `rowVersion`. The opposite action is not offered for the current state. 409 reloads the item. |
| BO-I6 | No physical delete action. No price field. No ledger navigation from this page. |
| BO-I7 | Non-SystemAdmin is shown `/forbidden`. Unauthenticated visit redirects to hosted login. |
| BO-I8 | The page does not call `/instruments` without `tenantId` on list or create. Get / PUT / disable / enable use the instrument id only. |

---

## Tests

Back Office tests assert routes, the tenant-required list, and that the UI client sends the shapes expected by `features/instruments.md`. Do not re-prove backend isolation, uniqueness, or `rowVersion` conflict handling here.

---

## Notes

- Instruments remain tenant-scoped. There is no platform catalog page.
- SystemAdmin already has the three instrument policies. No policy amendment.
- Adviser Portal continues to have no Instruments management page. Firm users create and read via existing firm flows or Scalar; management UI for SystemAdmin is this host.
- Later: optional link from tenant detail to this list pre-filtered to that tenant. Not required for this slice.
