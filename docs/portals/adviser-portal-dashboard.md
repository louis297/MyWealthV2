---
title: Adviser Portal — Dashboard and ledger pages
status: accepted
phase: 2
language: en
created: 2026-10-12
updated: 2026-10-12
related:
  - README.md
  - frontend-conventions.md
  - frontend-implementation-notes.md
  - adviser-portal.md
  - ../function-plan.md
  - ../features/net-worth.md
  - ../features/accounts.md
  - ../features/holdings.md
  - ../features/posting.md
  - ../features/instruments.md
  - ../features/buy-sell.md
  - ../features/split.md
  - ../features/bff-adviser-portal.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
---

# Adviser Portal — Dashboard and ledger pages

Phase 2 portal cut. Consumes the landed net-worth read model and the existing ledger write + read endpoints (posting, buy-sell, split, reverse). Backend contracts live in Feature Specs. This file owns routes, page scope, and acceptance for the Dashboard, ledger pages, and the first write forms.

**Status is `accepted`.** Implementation follows this file. Construction detail lives in [frontend-implementation-notes.md](frontend-implementation-notes.md).

Backend prerequisites (landed):

- Net-worth: `GET /net-worth`, `GET /users/customers/{id}/net-worth` (`af422c2`). BFF prefix `/net-worth` is allowed.
- Accounts, holdings, instruments (Phase 2 slices already landed).
- Transactions: `POST /transactions` (Buy / Sell / StockIn / StockOut / Split / Bonus / Scrip + cash types), `POST /transactions/{id}/reverse`, list / get. Policies `transactions.create` / `transactions.read`. Idempotency-Key required on create and reverse.

Session remains the BFF cookie. All API calls go through `/api`. Do not re-introduce SPA token storage or `/callback`.

---

## Current slice

**In**

- Dashboard as the new default home (replaces `/customers` as the landing page for TenantAdmin / Adviser).
- Per-currency net-worth totals and the three breakdowns from `GET /net-worth`.
- Customer list with net-worth summary (click-through to the customer detail). The detail shows the person and that customer's accounts. Firm breakdowns stay on the dashboard.
- Recent activity widget (reuse `GET /transactions` or the existing list shape).
- Accounts list / detail (read-only first; open / close deferred or minimal).
- Holdings list on an account (`GET /accounts/{id}/holdings`).
- Activity / transactions list (filter by account or customer; reuse existing posting reads).
- Instrument picker on write forms (calls existing `GET /instruments?enabledOnly=true`, optionally with the existing `search` query). Simple select or basic list. No typeahead, no min-character trigger, no dedicated search endpoint.
- Write forms on an Open Brokerage / Other account:
  - Buy / Sell (server price via mocked `IMarketData`; cash + security legs).
  - StockIn / StockOut (client quantity + cost; no cash).
  - Split / Bonus / Scrip (new total quantity; security-only; total cost unchanged).
  - Cash types already accepted (Opening, TransferIn/Out, Interest, CloseOut, etc.) where the account type allows.
- Reverse on a posted transaction (`POST /transactions/{id}/reverse`). Original stays; opposite legs posted. One reverse per original. Idempotency-Key required.
- Account open / close (reuse existing account endpoints; close still requires cash zero and no holdings).

**Out**

- Charts, historical comparison, “vs yesterday”, FX fold.
- Credit / Property as selectable account types.
- Instruments catalog pages (list / create / rename / disable). Firm-wide master data stays on Scalar (SystemAdmin / TenantAdmin) or a later Back Office client. Adviser Portal only consumes the catalog as a picker.
- Typeahead instrument search (input 2–3 characters then backend search + candidates). Reuses the existing list `search` query if needed later; may add a small dedicated endpoint or ranking rules. Defer as an independent small feature.
- SystemAdmin ledger screens (Scalar remains the back-office surface).
- Customer Portal.
- Daily snapshots.
- Lots, realized-gain reports, fees, multi-instrument tickets, cross-account transfer.
- Hand-edited holding quantity or cost outside the accepted write types.

---

## Routes

| Path | Who | Behaviour |
| --- | --- | --- |
| `/` | TenantAdmin, Adviser | Redirect to `/dashboard`. SystemAdmin still does not land on a workspace (C4 from Phase 1). |
| `/dashboard` | TenantAdmin, Adviser | Firm / adviser net-worth. Widgets: per-currency totals, byCustomer, byAccountType, topHoldings, recent activity. |
| `/customers/:id` | Same as Phase 1 | Person fields and that customer's accounts, with New account. No firm net-worth breakdowns. |
| `/customers/:id/accounts/new` | TenantAdmin, Adviser | New account for that customer. `POST /accounts` with `customerId`, name, type, and currency. Omit `tenantId`. |
| `/accounts` | TenantAdmin, Adviser (assigned) | List of accounts the caller can see. |
| `/accounts/:id` | Same | Detail: cash balance, holdings list, recent activity, action buttons (Buy / Sell / StockIn / StockOut / Split / Reverse / Close). |
| `/accounts/:id/holdings` | Same | Holdings projection for the account. |
| `/accounts/:id/transactions/new` | Same | Create form. Type selector drives the body shape from the Feature Specs. Requires `Idempotency-Key`. |
| `/transactions` | Same | Activity list. Optional `accountId` / `customerId` filters. Reverse action on a row. |
| `/transactions/:id` | Same | Read-only detail + Reverse button when the row is not already reversed. |
| `/profile` | Unchanged | |
| `/customers`, `/advisers` | Unchanged | Still available; no longer the default home. |

---

## Session

Unchanged from the BFF cut. Probe is still `GET /api/users/me`. 401 does not refresh in the browser. Sign-out remains `POST /bff/logout`.

---

## Acceptance (D)

| ID | Behaviour |
| --- | --- |
| D1 | TenantAdmin / Adviser open `/` → land on `/dashboard`. SystemAdmin still has no Customers workspace. |
| D2 | Dashboard loads `GET /net-worth` and renders per-currency assets / liabilities / net. Closed accounts do not appear. |
| D3 | Breakdowns render: by customer (name), by account type, top holdings (market value descending). |
| D4 | Customer list on Dashboard (or existing `/customers`) shows a net-worth summary per customer; click navigates to `/customers/:id`. |
| D5 | `/customers/:id` shows the person and that customer's accounts. It does not render by customer, by account type, or top holdings. Unassigned / other-tenant customer id remains 404. |
| D6 | `/accounts` and `/accounts/:id` list and show cash balance + holdings. Adviser sees only assigned customers’ accounts. |
| D7 | `/transactions` shows activity; filters by account or customer work with existing endpoints. |
| D8 | No React password page. All calls go through the BFF `/api` prefix. Customer still receives no session. |
| D9 | Missing / dead session → authorize flow. Wrong-role leftover → `/forbidden`. |
| D10 | Adviser posts a Buy on an Open Brokerage account. Server prices the security leg; cash and quantity update. Missing mock price → form shows the 400. |
| D11 | Adviser posts a Sell that would take quantity or cash negative → 400; original stays. Successful Sell reduces quantity at average cost and increases cash. |
| D12 | Adviser posts StockIn / StockOut (no cash) and Split / Bonus / Scrip (new total quantity, cost unchanged). Wrong body fields → 400. |
| D13 | Adviser reverses a posted Buy / Sell / Split. Original remains; new reverse transaction appears. Already reversed → 400. Idempotency-Key required; replay returns the same id. |
| D14 | Forms omit `tenantId` for TenantAdmin / Adviser. Closed account or Disabled Customer on create / reverse → 400. Bank / Cash refuse security types. |

---

## Tests

Portal tests assert routes, role visibility, and that the Dashboard consumes the net-worth shape. Do not re-prove backend isolation or net-worth aggregation rules here.

---

## Still open

- Exact widget layout and empty states.
- Top-N limits on breakdowns (sort is locked in the Feature Spec; limit is not).
- Daily snapshots and historical charts.
- Multi-instrument tickets, fees, lots, realized-gain views.
- Cross-account transfer UI.
- Instruments management UI (Back Office / Scalar only; not this portal).
- Instrument typeahead search (later independent small feature; existing list `search` is enough for a simple picker now).
