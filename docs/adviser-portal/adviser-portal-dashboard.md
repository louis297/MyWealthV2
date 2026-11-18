---
title: Adviser Portal — Dashboard and ledger pages
status: accepted
phase: 2
language: en
created: 2026-10-12
updated: 2026-11-18
related:
  - README.md
  - frontend-conventions.md
  - frontend-implementation-notes.md
  - adviser-portal.md
  - ../function-plan.md
  - ../features/net-worth.md
  - ../features/accounts.md
  - ../features/account-custody-mode.md
  - ../features/holdings.md
  - ../features/posting.md
  - ../features/instruments.md
  - ../features/buy-sell.md
  - ../features/split.md
  - ../features/bff-adviser-portal.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
  - ../features/custody-modes.md
  - ../features/tenant-custody-modes.md
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
  - Buy / Sell (server price via `IMarketData`; cash + security legs).
  - StockIn / StockOut (client quantity + cost; no cash).
  - Split / Bonus / Scrip (new total quantity; security-only; total cost unchanged).
  - Cash types already accepted (Opening, TransferIn/Out, Interest, CloseOut, etc.) where the account type allows.
- Reverse on a posted transaction (`POST /transactions/{id}/reverse`). Original stays (economic legs); opposite legs posted. The original receives `reversedByTransactionId`. One reverse per original. Idempotency-Key required. Hide the Reverse button when `reversedByTransactionId` is not null or `type` is `Reversal`.
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
| `/accounts/:id` | Same | Detail: cash balance, holdings list, recent activity, an Amount column with the cash leg on this account (negative on the source of a transfer, positive on the destination; when the account has a `Principal` and a `Fee` leg, the sum of both, [features/trade-fees.md](../features/trade-fees.md) R22), a rate line "1 NZD = 0.5912 USD (as of 2026-11-09)" under the amount when the item has `fx`, action buttons (TransferIn / TransferOut / Transfer / Convert on an Open account; Buy / Sell / StockIn / StockOut / Split / Reverse / Close). |
| `/accounts/:id/holdings` | Same | Holdings projection for the account. |
| `/accounts/:id/transactions/new` | Same | Create form. Type selector drives the body shape from the Feature Specs. Requires `Idempotency-Key`. |
| `/accounts/:id/transfer` | Same | Internal transfer form ([internal-transfers](../features/internal-transfers.md) §9). Step 1 Customer: defaults to the source Account's Customer; search box debounced 300 ms (`GET /users/customers?enabledOnly=true&search=`). Step 2 Account: `GET /accounts?customerId=&pageSize=100`; Closed, different-currency and source Accounts stay listed, greyed out with the reason. Positive amount, booked at, memo, reference. Review shows "Transfer 500.00 NZD from <account> (<customer>) to <account> (<customer>)" and a warning line when the Customers differ. Confirm posts `POST /transactions/transfer` with one `Idempotency-Key` per form open, then returns to the source account. |
| `/accounts/:id/convert` | Same | FX conversion form ([fx-conversions](../features/fx-conversions.md) §9). One step, no Customer picker: `GET /accounts?customerId=<source Customer>&pageSize=100`; Closed, same-currency and source Accounts stay listed, greyed out with the reason; when no Open Account of another currency exists the form says so. "I sell" / "I buy" toggle chooses which amount is typed; the field shows that currency. Live quote `POST /transactions/fx/quote` debounced 300 ms shows "1 NZD = 0.5912 USD (as of 2026-11-09)" and the computed amount; a quote error is shown and blocks Review. Review shows "Convert 845.74 NZD from <account> to 500.00 USD in <account> at 1 NZD = 0.5912 USD (as of 2026-11-09)". Confirm posts `POST /transactions/fx` with the quoted `expectedRate` and one `Idempotency-Key` per form open. On 409 for `expectedRate` the form shows the message, fetches a new quote and asks for Review again. |
| `/transactions` | Same | Activity list. Optional `accountId` / `customerId` filters. Account and Customer columns list every visible leg account and its Customer; Cash lists each visible cash leg ([leg-accounts](../features/leg-accounts.md) §8.1, amended 2026-11-10). Reverse action on a row, hidden when `reversedByTransactionId` is not null or `type` is `Reversal`. |
| `/transactions/:id` | Same | Read-only detail. Reverse button is hidden when `reversedByTransactionId` is not null or `type` is `Reversal`. |
| `/profile` | Unchanged | |
| `/customers`, `/advisers` | Unchanged | Still available; no longer the default home. |

TransferIn and TransferOut reuse `/accounts/:id/transactions/new`. The route set does not change for external cash in and cash out. An internal transfer has its own page because its body names two Accounts.

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
| D10 | Adviser posts a Buy on an Open Brokerage account. Server prices the security leg; cash and quantity update. Missing price → form shows the 400. |
| D11 | Adviser posts a Sell that would take quantity or cash negative → 400; original stays. Successful Sell reduces quantity at average cost and increases cash. |
| D12 | Adviser posts StockIn / StockOut (no cash) and Split / Bonus / Scrip (new total quantity, cost unchanged). Wrong body fields → 400. |
| D13 | Adviser reverses a posted Buy / Sell / Split. Original remains; new reverse transaction appears. The Reverse button is hidden when `reversedByTransactionId` is not null or `type` is `Reversal`. Already reversed → 400. A security transaction that is not the latest security activity on its holding → 400; the page shows the sentence the same way ([ledger-write-guards](../fixes/ledger-write-guards.md)). Idempotency-Key required; replay returns the same id. |
| D14 | Forms omit `tenantId` for TenantAdmin / Adviser. Closed account or Disabled Customer on create / reverse → 400. Bank / Cash refuse security types. |

---

## Amendment 2026-11-17 (account custody mode) — accepted, landed

Status of this amendment: `accepted` (2026-11-17), together with [account-custody-mode](../features/account-custody-mode.md).

Every account has a custody mode, `External` or `Custodial`. Only `External` can be opened in that slice, and no page behaviour depends on the value.

- `/customers/:id/accounts/new`: `POST /accounts` also sends `custodyMode: "External"`. The form has no custody control; the selector arrives when Custodial accounts can be opened.
- `/accounts`, the accounts table on `/customers/:id`, and `/accounts/:id`: show "Custody" next to "Type". The text is the enum name from the item's `custodyMode`, as for Type.
- No filter control for custody mode. Action buttons and write forms do not read it.

| ID | Behaviour |
| --- | --- |
| D15 | The New account form posts `custodyMode: "External"` together with `customerId`, name, type and currency. The form shows no custody control. |
| D16 | The accounts list, the accounts table on the customer page and the account page show each account's custody mode next to its type. |

---

## Amendment 2026-11-17 (custody modes) — accepted, landed

Status of this amendment: `accepted` (2026-11-17), together with [custody-modes](../features/custody-modes.md).

A Custodial account can be opened, and the write forms follow the account's custody mode. Backend rules: custody-modes §4. D17 replaces D15. D18–D20 extend D10–D14 and the transfer and convert rows of the route table. D16 is unchanged.

| ID | Behaviour |
| --- | --- |
| D17 | The New account form (`/customers/:id/accounts/new`) has a required "Custody" choice with two options: "External — held elsewhere" and "Custodial — held on the platform". Nothing is preselected, and the form cannot be submitted until one is chosen. Choosing Custodial sets Type to Brokerage and disables the Type control; choosing External enables it again. `POST /accounts` carries the chosen `custodyMode`. |
| D18 | The action buttons on `/accounts/:id` and the type selector on `/accounts/:id/transactions/new` offer only the types the manual entry matrix allows for the account's custody mode (custody-modes R5). On a Custodial account: TransferIn, TransferOut, CloseOut, Transfer, Convert, Buy, Sell, Reverse and Close. StockIn, StockOut, Split, Bonus, Scrip, Opening, OpeningHoldings and Interest are not offered. An External account offers what it offers today. The portal keeps its own copy of the list; when the server still refuses a type, the form shows the sentence of the 400. |
| D19 | On a Custodial account the transaction form, the transfer form and the convert form have no "Booked at" input and send no `bookedAt`. The quote request of the convert form sends none either. The forms show "Booked when posted." where the input was. On an External account the input stays and is required. |
| D20 | On `/accounts/:id/transfer` and `/accounts/:id/convert`, an Account whose custody mode differs from the source Account's stays listed, greyed out, with the reason "Different custody mode", like the existing Closed and currency reasons. |

Portfolio controls on the account page: [adviser-portal-portfolios](adviser-portal-portfolios.md) amendment 2026-11-17 (P13).

---

## Amendment 2026-11-18 (tenant custody modes) — accepted, landed

Status of this amendment: `accepted` (2026-11-18), together with [tenant-custody-modes](../features/tenant-custody-modes.md). D21 narrows D17 to the custody modes the firm allows. The BFF does not change.

| ID | Behaviour |
| --- | --- |
| D21 | The New account form offers only the custody modes in the firm's `allowedCustodyModes`, from the tenant item the shell loads with `GET /tenants/by-code/{code}`. Two allowed: D17 as written, nothing preselected. One allowed: that option alone, already selected; if it is Custodial, Type is Brokerage and the Type control is disabled. Until the tenant item has loaded, no custody option is shown and the form cannot be submitted. A 400 with `errors.CustodyMode` (the setting changed after the page loaded) is shown at the Custody choice. |

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
