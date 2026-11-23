---
title: Adviser Portal — Dashboard and ledger pages
status: accepted
phase: 2
language: en
created: 2026-10-12
updated: 2026-11-23
related:
  - ../features/client-money-accounts.md
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
  - ../features/external-caller-numbers.md
  - ../features/custody-mode-grouping.md
  - ../features/transaction-source.md
  - ../features/deposit-matching.md
  - ../features/withdrawals.md
  - ../features/bank-returns.md
  - ../features/bank-reconciliation.md
  - ../features/account-closure.md
  - ../features/interest-run.md
  - ../features/trade-settlement.md
  - ../features/rebalances.md
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

## Amendment 2026-11-18 (External caller numbers) — accepted, landed

Status of this amendment: `accepted` (2026-11-18), together with [external-caller-numbers](../features/external-caller-numbers.md). D22 replaces "Server prices the security leg" in D10 on External accounts; D23 replaces the convert row for External accounts. On a Custodial account both stay as written. The BFF does not change.

| ID | Behaviour |
| --- | --- |
| D22 | On an External account, the Buy and Sell forms on `/accounts/:id/transactions/new` show, once an instrument is picked, a required "Price per unit (<quote currency>)" input, an optional "Fee (<quote currency>)" input and, only when the instrument's quote currency differs from the Account currency, a required "FX rate" input labelled "1 <quote currency> = ? <Account currency>". The POST carries `security[0].price`, `fee` (left out when blank) and `fxRate`. The form does no cash or cost arithmetic. A 400 on `Security[0].Price`, `Fee` or `FxRate` is shown at its input. On a Custodial account the forms have none of these inputs and send none of these fields. |
| D23 | On an External source account, `/accounts/:id/convert` has no "I sell" / "I buy" toggle and sends no quote request. It has two required inputs, "Sell (<from currency>)" and "Buy (<to currency>)". Once both are valid it shows the implied rate as display text, "1 NZD = 0.5912 USD", rounded to 4 decimals. Review shows "Convert 845.74 NZD from <account> to 500.00 USD in <account> at 1 NZD = 0.5912 USD". Confirm posts `POST /transactions/fx` with `sellAmount`, `buyAmount`, `bookedAt` and no `expectedRate`, with one `Idempotency-Key` per form open; there is no 409 re-quote step. On a Custodial source account the form is unchanged. |

The rate line under the amount on `/accounts/:id` already appears for any item with `fx`; an External `Buy` / `Sell` with a rate shows it without a change.

---

## Amendment 2026-11-18 (grouping by custody mode) — accepted, landed

Status of this amendment: `accepted` (2026-11-18), together with [custody-mode-grouping](../features/custody-mode-grouping.md). D24 and D25 extend D2 and D3; the other sections of the Dashboard do not change. The BFF does not change.

| ID | Behaviour |
| --- | --- |
| D24 | `/dashboard` shows two figures below the per-currency totals and above "By customer": "Funds under management" and "External assets". Each lists, per currency, `assets.total` of the `Custodial` or the `External` entry of `breakdowns.byCustodyMode` from `GET /net-worth`. |
| D25 | A custody mode with no entry in `byCustodyMode` shows "None." under its figure. The figures show no note; the "not checked" note is for the Customer only. |

The Reports page does not change. The Adviser Balances and Customer Transactions PDFs gain a Custody column (custody-mode-grouping R10, R14); that is server output.

---

## Amendment 2026-11-19 (transaction source) — accepted

Status of this amendment: `accepted` (2026-11-19), together with [transaction-source](../features/transaction-source.md). D27 narrows the Reverse conditions of the `/transactions` and `/transactions/:id` rows, D13 and D18. The BFF does not change.

| ID | Behaviour |
| --- | --- |
| D26 | `/transactions/:id` shows a "Source" line under the type, from the item's `source.kind`: "Entered by hand" for `Manual`, "Rebalance" for `Rebalance`, "Test seed" for `Seed`. The source id is not shown. No other page shows the source. |
| D27 | Reverse is offered on `/transactions`, `/transactions/:id` and the recent activity of `/accounts/:id` only when the item is not a `Reversal`, has no `reversedByTransactionId`, and its `source.kind` is `Manual` or `Rebalance`. The portal does not read the custody mode for this rule, so it also hides Reverse on a `Seed` Transaction of an External account (Development data only), which the server would accept. A 400 from reverse shows its sentence as today. |


---

## Amendment 2026-11-19 (adjustments) — accepted

Status of this amendment: `accepted` (2026-11-19), together with [adjustments](../features/adjustments.md). It adds the route `/accounts/:id/adjust`, and D31 narrows the Reverse conditions of D27 for an `Adjustment`. The BFF does not change: the write is under `/transactions`.

Reason labels: `EntryError` "Entry error", `CorporateAction` "Corporate action", `ReconciliationBreak` "Reconciliation", `Other` "Other".

| ID | Behaviour |
| --- | --- |
| D28 | `/accounts/:id` shows an "Adjust" action only to a TenantAdmin, only on an Open Custodial account. It opens `/accounts/:id/adjust`. An Adviser never sees it. |
| D29 | `/accounts/:id/adjust`: Reason (select, required), Memo (required, with the hint "The customer sees this memo"), an optional cash change (signed, in the Account currency), and an optional holding change (instrument picker from the Account's holdings plus `GET /instruments?enabledOnly=true`, signed quantity change, signed cost change in the quote currency). No booked-at input. Review lists the changes as entered ("Cash −0.60 USD; AAPL quantity 0, cost +0.60 USD"); the form does no balance arithmetic. Confirm posts `POST /transactions/adjustment` with one `Idempotency-Key` per form open. A 400 is shown at its field, or as the sentence when it has none. |
| D30 | `/transactions/:id` of an `Adjustment` or its Reversal shows a "Reason" line with the label, under the Source line (D26). `/transactions` shows the type as "Adjustment" like any other type name. |
| D31 | Reverse on an `Adjustment` (D27 conditions plus this one) is offered only to a TenantAdmin. A 403 from reverse shows "Only a firm administrator can reverse an adjustment." |

---

## Amendment 2026-11-20 (client money accounts and deposit references) — accepted

Status of this amendment: `accepted` (2026-11-20), together with [client-money-accounts](../features/client-money-accounts.md). D32 adds deposit instructions to `/accounts/:id`; D33 narrows the currency picker of D17 and D21 for a Custodial account. The BFF does not change: `GET /accounts/{id}/deposit-instructions` is under `/accounts`, and `clientMoneyCurrencies` comes on the tenant item from `GET /tenants/by-code/{code}`.

| ID | Behaviour |
| --- | --- |
| D32 | `/accounts/:id` of a Custodial account shows "Deposit reference" with the item's `depositReference`, Open or Closed. On an Open Custodial account it also shows a "Deposit instructions" panel from `GET /accounts/{id}/deposit-instructions`: Bank, Account name, Account number, Reference, Currency. A 404 shows "No client money account in <currency>. Ask the platform operator to add one." External accounts show neither. |
| D33 | The New account form (`/customers/:id/accounts/new`, D17 and D21): while Custodial is chosen, the currency picker offers only the enabled currencies in the firm's `clientMoneyCurrencies` from the tenant item. When that list is empty, the form shows "Your firm has no client money account, so a Custodial account cannot be opened." and cannot be submitted with Custodial. A 400 with `errors.Currency` (the setting changed after the page loaded) is shown at the currency picker. External keeps every enabled currency. |

---

## Amendment 2026-11-20 (bank receipts and deposits) — accepted

Status of this amendment: `accepted` (2026-11-20), together with [deposit-matching](../features/deposit-matching.md). D34–D37 add the Bank receipts pages for a TenantAdmin; D38 adds the bank receipt to the transaction page; D39 changes D18 and D32. The BFF gains the prefix `/statement-lines` ([bff-adviser-portal.md](../features/bff-adviser-portal.md) amendment 2026-11-20). Calls: `GET /statement-lines`, `GET /statement-lines/{id}`, `POST /statement-lines/{id}/match|ignore|request-return|reopen|unmatch`, `GET /accounts?depositReference=`.

| ID | Behaviour |
| --- | --- |
| D34 | A TenantAdmin sees a nav item "Bank receipts" (`/statement-lines`). An Adviser does not; a 403 on the route shows "Only a firm administrator can see bank receipts." The page lists `GET /statement-lines` with a status filter (default Unmatched; options Unmatched, Matched, Ignored, To be returned, and All, which sends those four statuses): Value date, Received, Amount, Currency, Payer, Reference, Status, Reason. Reason labels: NoReference "No valid reference", UnknownReference "Reference not found", SeveralReferences "More than one reference", CurrencyMismatch "Account in another currency", AccountClosed "Account closed", CustomerDisabled "Customer disabled", AmountPrecision "Amount has too many decimals". `Received` lines (debit lines waiting for B6, and credit lines not yet processed) are not shown. |
| D35 | `/statement-lines/:id` shows every field of the item, the reason, the last action (action, note, person, time) and, on a Matched line, the account (link to `/accounts/:id`), the Customer and the deposit (link to `/transactions/:id`) with "Matched by reference" or "Matched by hand". Actions follow R23: Unmatched → Match, Ignore, Return to payer; Ignored or To be returned → Reopen; Matched → Undo match. |
| D36 | Match opens a form with two ways to pick the account: "Deposit reference" (calls `GET /accounts?depositReference=`) or a Customer picker followed by the Customer's Open Custodial accounts in the line's currency (`GET /accounts?customerId=&custodyMode=Custodial`). An optional note. Review: "Credit 500.00 USD to <account name> of <customer name>." A 400 is shown at its field; a 409 reloads the line with "This receipt changed. Check it and try again." |
| D37 | Ignore, Return to payer and Undo match ask for a required note in a dialog. Undo match warns "This posts a reversal of the deposit on <account name>." Reopen asks for confirmation only. The R30 400 message is shown as returned. |
| D38 | `/transactions/:id` of a deposit or its Reversal shows a "Bank receipt" section: Value date, Reference, Payer, Payer account. The Source line (D26) shows "Bank feed" for `BankFeed`; a TenantAdmin also sees a link "Statement line" to `/statement-lines/:source.id`. Reverse is not offered (D27 already hides it for every source other than `Manual` and `Rebalance`). |
| D39 | D18 changes: on a Custodial account the action buttons and the type selector no longer offer TransferIn. The deposit instructions panel (D32) gains the line "Deposits are credited from the bank statement when the customer pays in with this reference." External accounts unchanged. |
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

---

## Amendment 2026-11-20 (bank accounts and withdrawals) — accepted

Status of this amendment: `accepted` (2026-11-20), together with [withdrawals](../features/withdrawals.md). D40–D43 add nominated bank accounts on the Customer page and the TenantAdmin's checks; D44–D47 add withdrawals and change D18, D19 and D39 on a Custodial account; D48 adds the payment to the transaction page; D49 changes the Bank receipts pages (D34, D35). The BFF gains the prefixes `/nominated-bank-accounts` and `/withdrawals` ([bff-adviser-portal.md](../features/bff-adviser-portal.md) amendment 2026-11-20). Calls: `GET /nominated-bank-accounts`, `POST /nominated-bank-accounts`, `POST /nominated-bank-accounts/{id}/verify` / `reject` / `remove`, `GET /withdrawals`, `GET /withdrawals/{id}`, `POST /withdrawals`, `POST /withdrawals/{id}/approve` / `reject` / `cancel`, and the existing account, transaction and statement line reads.

| ID | Behaviour |
| --- | --- |
| D40 | `/customers/:id` gains a "Bank accounts" section from `GET /nominated-bank-accounts?customerId=`: Bank, Account name, Account number, Currency, Status (Pending "Waiting for check", Verified "Verified", Rejected "Rejected", Removed "Removed"), Added by. Removed and Rejected rows are behind "Show removed". An "Add bank account" button opens D41. Each Pending or Verified row has Remove (D43). |
| D41 | Add form: Bank, Account name, Account number, Currency (enabled currencies). A sentence above it: "Add only an account in the customer's own name. A firm administrator checks it before money can be paid to it." A 400 is shown at its field. Success returns to the Customer page. |
| D42 | A TenantAdmin sees a nav item "Bank account checks" (`/nominated-bank-accounts`), listing Pending rows of the firm: Customer, Bank, Account name, Account number, Currency, Added by, Added. A row opens a dialog with every field and Verify and Reject, each with a required note ("What did you check?" / "Why is it rejected?"). On a row the TenantAdmin added, both buttons are disabled with "Added by you. Another administrator must check it." An Adviser does not see the nav item; a 403 on the route shows "Only a firm administrator can check bank accounts." A 409 reloads with "This bank account changed. Check it and try again." |
| D43 | Remove asks for a required note in a dialog: "Remove this bank account? Withdrawals already approved to it are still paid." |
| D44 | D18 and D39 change: on a Custodial account the action buttons and the type selector no longer offer TransferOut, and offer CloseOut only when `0 < cashBalance <` the minor unit. A "Withdraw" button opens `/accounts/:id/withdraw` on an Open Custodial account. The account header shows "Cash", "Reserved for withdrawals" (only when not 0) and "Available". Open withdrawals of the account are listed under the holdings (D46 columns). On the transfer form (D19), a Custodial account of another Customer stays listed, greyed out, with the reason "Another customer". External accounts unchanged. |
| D45 | `/accounts/:id/withdraw`: To (the Customer's Verified bank accounts in the account's currency; none → "No verified bank account in USD. Add one on the customer page." with a link), Amount (shows "Available 700.00 USD"), Reference (placeholder: the deposit reference), Memo. Review: "Withdraw 300.00 USD from <account> to <bank> <accountName> ****8900. A firm administrator must approve it." Confirm posts `POST /withdrawals` with one `Idempotency-Key` per form open and opens `/withdrawals/:id`. A 400 is shown at its field. |
| D46 | Nav item "Withdrawals" (`/withdrawals`) for TenantAdmin and Adviser: Requested, Customer, Account, Amount, Currency, To, Status, Requested by. Status filter, default "Waiting for approval" (Requested); options Requested, Approved, Sent, Paid, Rejected, Cancelled, Failed, Returned, All. Status labels: Requested "Waiting for approval", Approved "Approved, sending", Sent "Sent to bank", Paid "Paid", Rejected "Rejected", Cancelled "Cancelled", Failed "Bank refused", Returned "Returned by bank". |
| D47 | `/withdrawals/:id` shows every field, the decision, the payment (attempts, last failure label as BO18), the failure or return reason, and links to the payout and the return Reversal (`/transactions/:id`). Actions: a TenantAdmin who did not request it sees Approve (optional note) and Reject (required note) on a Requested one; the requester sees "Waiting for another administrator to approve." in their place; an Adviser sees neither. Cancel (optional note) is offered on Requested, and on Approved while `payment.attempts` is 0. A 400 shows its sentence; a 409 reloads with "This withdrawal changed. Check it and try again." |
| D48 | `/transactions/:id` of a payout or its Reversal shows a "Payment" section: Value date, Reference, Payee, Payee account. The Source line (D26) shows "Withdrawal" for `Payment`, with a link to `/withdrawals/:source.id`. Reverse is not offered (D27). |
| D49 | The Bank receipts pages (D34, D35) gain a Direction filter (Receipts, the default; Payments out; All). New reason label: PaymentMismatch "Amount differs from the payment". New method labels: Payment "Paid by the platform", PaymentReturn "Payment returned". A line with a withdrawal links to `/withdrawals/:id`; a line with a related line links to it. A debit line offers no action. |

---

## Amendment 2026-11-21 (receipt returns, recalls and other debit lines) — accepted

Status of this amendment: `accepted` (2026-11-21), together with [bank-returns](../features/bank-returns.md). D50 changes the Bank receipts pages (D34, D35, D49). The BFF does not change. Calls: the existing statement line reads and actions (`POST /statement-lines/{id}/ignore`, `/reopen` on a debit line too).

| ID | Behaviour |
| --- | --- |
| D50 | The Bank receipts pages (D34, D35, D49) gain status labels ReturnSent "Return sent" and Returned "Returned"; the status filter gains both, and "All" includes them. Reason labels: UnknownPayment "Payment out not recognised", UnknownReturn "Return not recognised", RecallNotCovered "Recalled, but the cash is gone", ReturnRejected "Bank refused the return". Method label: Recall "Recalled by the payer's bank". A debit line offers Ignore (Unmatched) and Reopen (Ignored); a line matched by the platform (Payment, PaymentReturn, Recall) offers no action. On a To be returned line the return payment's status shows under the status ("Waiting to send" with no failure, "Sending failed: <failure label>" with one, labels as BO18), and Reopen is offered only while `returnPayment.attempts` is 0. On a RecallNotCovered line the page says "Correct the account with an adjustment, then ignore this line." with a link to the account of the recalled receipt's deposit when there is one. A match with no Transaction shows no account or transaction link; the related line link (D49) stays. |

---

## Amendment 2026-11-21 (bank reconciliation) — accepted

Status of this amendment: `accepted` (2026-11-21), together with [bank-reconciliation](../features/bank-reconciliation.md). D51 adds the Bank reconciliation pages for a TenantAdmin. The BFF gains the prefix `/bank-reconciliations` ([bff-adviser-portal.md](../features/bff-adviser-portal.md) amendment 2026-11-21). Calls: `GET /bank-reconciliations/overview`, `GET /bank-reconciliations`, `GET /bank-reconciliations/{id}`, `POST /bank-reconciliations`, and the existing statement line reads. The PDF is on the Reports tab ([adviser-portal-reports.md](adviser-portal-reports.md) D52).

| ID | Behaviour |
| --- | --- |
| D51 | A TenantAdmin sees a nav item "Bank reconciliation" (`/bank-reconciliations`); an Adviser does not, and a 403 shows "Only a firm administrator can see bank reconciliations." The page lists the overview: Currency, Bank account (bank name and account number; "Inactive" on an inactive one), Last run, Status (Agreed "Agreed", Breaks "<n> breaks", NoBankBalance "No bank balance yet", no run "Not run yet"), Bank balance, Custodial cash, Difference, Statement read (`balanceAt`; with "Bank feed failing since <time>" when `bankFeed.lastFailedAt` is after `lastSucceededAt`), and "Run now", which posts and opens the new run. A row opens its latest run. `/bank-reconciliations/:id` shows the header (account, ran at, by "Daily run" or the person, status), the two sides and the difference, then the items under "Breaks", "Timing" and "Known differences" with their totals. Kind labels: Unexplained "Not explained", StatementDifference "Bank balance differs from the statement lines", Suspense "In suspense", NotProcessed "Not processed yet", ReturnInProgress "Being returned to the payer", OpeningBalance "Balance before the first statement line", Ignored "Ignored", Trades "Trades not settled through the trust account", FxConversions "FX conversions not settled through the trust account", Adjustments "Adjustments", OtherLedgerCash "Other cash entered on the platform", OtherClientMoneyAccount "Cash from another client money account". A line item shows direction, value date, the status and reason labels of D34, D49 and D50, the note, and links to `/statement-lines/:id` (also a `Received` line, which the Bank receipts list does not show). Under the items: reserved cash and payments in flight, with the sentence "Payments sent to the bank appear on both sides when the statement shows them." An Unexplained item adds "This should not happen. Report it to the platform team." A "History" link lists `GET /bank-reconciliations?clientMoneyAccountId=` with the status filter. |

---

## Amendment 2026-11-22 (account closure) — accepted

Status of this amendment: `accepted` (2026-11-22), together with [account-closure](../features/account-closure.md). D53–D57 replace the plain Close action on a Custodial account with the Close account page and show a closing account. Calls: `POST /accounts/{id}/closures`, the existing account, holdings, portfolio and withdrawal reads. The routes sit under `/accounts`, so the BFF does not change. D18 and D39: the type selector no longer offers CloseOut on a Custodial account.

| ID | Behaviour |
| --- | --- |
| D53 | On an Open Custodial account the Close action opens `/accounts/:id/close` instead of calling `POST /accounts/{id}/close`. External accounts unchanged. The type selector (D18, D39) no longer offers CloseOut on a Custodial account. |
| D54 | `/accounts/:id/close`: a checklist from the account, its holdings, its assignment and `GET /withdrawals?accountId=&status=Requested,Approved,Sent`: "No holdings", "No portfolio", "No open withdrawals", each ticked or with its account-closure R5 / R6 sentence and a link (holdings, the portfolio, the withdrawal). Then "Cash balance", "To pay out" and, when not 0, "Written off" (the remainder). With a payout: To (the Customer's Verified bank accounts in the currency; none → "No verified bank account in USD. Add one on the customer page." with a link). Memo (required). Review: with a payout, "Pay 300.00 USD to ANZ J Smith ****8900 and close the account when the bank has paid. A firm administrator must approve the payment. Until then the account accepts no new activity."; without, "Close the account now." with "0.004 USD is written off." when there is a remainder. Confirm posts `POST /accounts/{id}/closures` with one `Idempotency-Key` per form open and returns to the account page. Confirm is disabled while a check fails. A 400 is shown at its field. |
| D55 | The account page of a closing account (`closure.status` `InProgress`) shows a banner: "Being closed. 300.00 USD is being paid out (<withdrawal status label, D46>). The account accepts no new activity." with a link to `/withdrawals/:closure.withdrawalId`. Every action button is hidden, Withdraw included. A Closed account whose latest closure is `Completed` shows "Closed by account closure on <date>". A `Cancelled` closure shows nothing on the account. |
| D56 | On the withdrawals list (D46) a closure's withdrawal (`accountClosureId` set) shows "Account closure" under To. `/withdrawals/:id` of one (D47) shows "This withdrawal pays out an account closure." Reject and Cancel on it ask in their dialog: "This also stops the account closure. The account stays open." |
| D57 | `/transactions/:id` of a write-off (`source.kind` `AccountClosure`): the Source line (D26) shows "Account closure" with a link to the account page. The type is shown as "Close out (rounding)". Reverse is not offered (D27). |

---

## Amendment 2026-11-22 (interest run) — accepted

Status of this amendment: `accepted` (2026-11-22), together with [interest-run](../features/interest-run.md). Field rules live there. D58 adds the tax details to the customer page; D59 and D60 add the Interest rates and Interest runs pages under the new BFF prefix `/interest` ([bff-adviser-portal](../features/bff-adviser-portal.md) amendment 2026-11-22); D61–D63 show interest postings, accrued interest, the bank's interest line and the new reconciliation kinds. Calls: `GET` / `PUT /users/customers/{id}/tax-profile`, `/interest/rate-schedules` (list, get, create, update, delete), `GET /interest/runs[/{id}]`, `GET /accounts/{id}/interest-accruals`, and the existing account, transaction and statement line reads.

| ID | Behaviour |
| --- | --- |
| D58 | The customer page gains a "Tax details" section from `GET /users/customers/{id}/tax-profile`: Tax residency ("New Zealand" / "Overseas"), IRD number ("Held" / "Not held"), RWT rate (the rate, or "Not chosen"), and the line "Tax withheld from interest: <kind label> <rate> %" with the basis in words (Elected "chosen rate", Default "no rate chosen", NonDeclaration "no IRD number held", NonResident "overseas resident"). Kind labels: Rwt "RWT", Nrwt "NRWT". Never saved → "Not provided" above the same line. Edit opens a form with the three fields; the rate select is disabled unless the residency is New Zealand and the IRD number is held. Save sends the PUT with the `rowVersion` read. A 400 is shown at its field; a 409 reloads with "Someone else changed these details." The form says: "We do not store the IRD number here." |
| D59 | TenantAdmin and Adviser see "Interest rates" under Fees in the nav (`/interest/rates`). The page lists `GET /interest/rate-schedules` grouped by currency: Effective from, Rates (each band as "3 % up to 100,000.00, then 2 %"), State ("In effect" / "Starts <date>"). A TenantAdmin sees New, and Edit and Delete on a pending row; an Adviser sees none, and the form routes render `/forbidden`. The form is the band editor of the fees page (adviser-portal-fees F6) without minimum and cap. 409 `interest_schedule_in_effect` shows "This rate has already been used and cannot be changed. Add a new one from a later date." and reloads. The effective-from field carries the UTC day note of F11, and: "Rates are per year. Interest is worked out on each day's closing balance and paid after the end of each month." |
| D60 | A TenantAdmin sees "Interest runs" (`/interest/runs`) from the Interest rates page; an Adviser does not, and a 403 shows "Only a firm administrator can see interest runs." The page lists `GET /interest/runs`: As of, Trigger (Scheduled "Daily run", Manual "Run by the platform team", AccountClosure "Account closure"), Accounts, Failed (in red when above 0), Interest paid and Tax withheld per currency. A run page shows the same with the posting date. There is no start button: a run is started by the platform team (R32). |
| D61 | `/transactions/:id` of an interest posting shows "Interest <periodFrom> to <periodTo> (<days> days)", Gross, Tax withheld with kind and rate, and Net; the Source line (D26) shows "Interest run". An expandable "Daily interest" table reads `GET /accounts/{id}/interest-accruals?transactionId=`: Day, Balance, Rate, Interest. Reverse is not offered (D27). The transactions list and account activity show the net amount with "incl. tax <amount>" under it, as they show a fee (adviser-portal-fees F13). |
| D62 | The page of a Custodial account shows "Interest accrued, not yet paid: <accruedInterest>" (4 decimals) with "up to <interestAccruedThrough>" when `accruedInterest` is above 0. The Close account page (D54) adds, when it is above 0: "Interest accrued so far is added before the payout. The amount to pay out may be higher than shown." |
| D63 | The Bank receipts list (D34) shows a "Bank interest" tag on a line of kind `Interest` and offers a Kind filter. The bank reconciliation run page (D51) labels the new kinds: BankInterest "Interest paid by the bank", InterestCredited "Interest credited to customers", TaxWithheld "Tax withheld from customers' interest". |

---

## Amendment 2026-11-22 (corporate actions) — accepted

Status of this amendment: `accepted` (2026-11-22), together with [corporate-actions](../features/corporate-actions.md). Field rules live there. D64–D67 add the Corporate actions pages under the new BFF prefix `/corporate-actions` ([bff-adviser-portal](../features/bff-adviser-portal.md) amendment 2026-11-22); D68 shows an entitlement posting on the transaction pages. Calls: `GET /corporate-actions[/{id}]`, `GET /corporate-actions/{id}/entitlements`, `POST /corporate-actions`, `PUT /corporate-actions/{id}`, `POST /corporate-actions/{id}/cancel`, `POST /corporate-actions/{id}/apply`, `GET /instruments`, and the existing transaction reads.

| ID | Behaviour |
| --- | --- |
| D64 | TenantAdmin and Adviser see a nav item "Corporate actions" (`/corporate-actions`). The page lists `GET /corporate-actions` with a status filter (default All): Instrument (symbol and name), Kind (Split "Share split", Consolidation "Share consolidation", Bonus "Bonus issue"), Ratio ("2 for 1"), Ex-date, Source (Feed "Market data", Manual "Entered by hand"), Status (Announced "Announced", Effective "Being applied", Applied "Applied", Cancelled "Cancelled") and Accounts (`posted` of `total`, with "<n> not applied" in amber when above 0). |
| D65 | `/corporate-actions/:id` shows the item, a sentence for the ratio (Split and Consolidation "Every <held> shares become <new>."; Bonus "<new> new shares for every <held> held."), the dates, who entered, applied or cancelled it ("The platform" for `null`), and the entitlements table from `GET /corporate-actions/{id}/entitlements`: Customer, Account, Before, After, Status (Pending "Waiting", Posted "Done", NotApplied with the reason: NoHolding "No longer held", TooSmall "Holding too small to change") and a link to the Transaction. An Adviser sees: "Only your customers' accounts are listed." |
| D66 | A TenantAdmin sees "New corporate action" (`/corporate-actions/new`): Instrument (a search over `GET /instruments`; an instrument with a `marketDataConnection` is listed as disabled with "From market data"), Kind, the two ratio numbers with the D65 sentence as a live preview, Ex-date, Record date, Payment date, Memo. Under the ex-date: "The change is applied when the first price from this date arrives." When the ex-date is before today (UTC): "This date has passed. The change is applied to today's holdings. Check trades booked since then." A 400 is shown at its field. An Adviser sees no button, and the form route renders `/forbidden`. |
| D67 | On a `Manual` corporate action that is `Announced`, a TenantAdmin sees Edit (the D66 form with `rowVersion`), Cancel (a dialog with a required note) and, once the ex-date has come (UTC), "Apply now" with the confirmation "Apply this to every holding now? Use this only when no price arrives for the instrument." A 400 shows its sentence; a 409 reloads with "Someone else changed this corporate action." A `Feed` corporate action offers none of the three. |
| D68 | `/transactions/:id` of an entitlement posting shows "<Kind label> <ratio>", "Ex-date <exDate>", "<quantityBefore> → <quantityAfter> shares" and a link to the corporate action; the Source line (D26) shows "Corporate action". Reverse is not offered (D27). The transactions list and account activity show the kind label and ratio in place of the plain type. The new transaction form of a Custodial account still does not offer `Split`, `Bonus` or `Scrip`. |

---

## Amendment 2026-11-23 (orders and settlement) — accepted

Status of this amendment: `accepted` (2026-11-23), together with [orders](../features/orders.md). Field rules live there. D69 and D70 replace the Buy and Sell entry of a Custodial account (D18, D39) with Place order; D71–D73 add the Orders pages under the new BFF prefix `/orders` ([bff-adviser-portal](../features/bff-adviser-portal.md) amendment 2026-11-23); D74 shows a fill on the transaction pages; D75 changes the Withdraw, transfer, conversion, Close account and Assign controls (D19, D45). Calls: `GET /orders[/{id}]`, `POST /orders`, `POST /orders/preview`, `POST /orders/{id}/cancel`, `GET /instruments`, and the existing account, holding and transaction reads with their new fields.

| ID | Behaviour |
| --- | --- |
| D69 | `/accounts/:id` of an Open Custodial account: the Buy and Sell actions and the `Buy` / `Sell` types of the new transaction form are gone (D18, D39 change), and a "Place order" button opens `/accounts/:id/order`. On an assigned account the button is disabled with "This account follows a portfolio." The header shows "Cash", "Reserved" (withdrawals and orders; only when not 0), "Available", "Not settled" (only when not 0) and "Available to withdraw". The holdings table shows "<n> reserved" under a quantity with a reserved part. The account's open orders are listed under the holdings (D71 columns). An External account is unchanged. |
| D70 | `/accounts/:id/order`: Side (Buy, Sell), Instrument (Buy: a search over `GET /instruments?enabledOnly=true`, an instrument quoted in another currency listed as disabled with "Quoted in NZD"; Sell: the account's holdings with their available quantity), Quantity, Memo. With a valid instrument and quantity the form calls `POST /orders/preview` (debounced ~300 ms) and shows, for a buy, "About 1,000.00 USD at the last price 100.00 (23 Nov), plus about 5.00 USD in fees. At most 1,055.25 USD is reserved until the order is filled. Available 5,000.00 USD." and for a sell "About 1,000.00 USD before fees. 60 available to sell." A preview 400 is shown at its field and blocks Review. Review repeats the sentence with "The broker fills at the market price, not above 105.00." Confirm posts `POST /orders` with one `Idempotency-Key` per form open and opens `/orders/:id`. No price and no booked-at input. |
| D71 | Nav item "Orders" (`/orders`) for TenantAdmin and Adviser: Placed, Customer, Account, Side, Instrument, Quantity, Filled, Status, Placed by. Status filter, default "Open" (Placed, Working, PartlyFilled); options Open, Filled, Cancelled, Rejected, Expired, All. Labels: Placed "Sending to the broker", Working "At the broker", PartlyFilled "Partly filled", Filled "Filled", Cancelled "Cancelled", Rejected "Refused by the broker", Expired "Expired". An order with a cancel request shows "Cancelling" beside its status. An order with a fill not posted shows "Needs attention" in amber; a "Needs attention" filter reads `pendingFill=true`. |
| D72 | `/orders/:id` shows every field: the price limit as "The broker may buy up to 105.00" or "may sell down to 95.00"; what the order reserves now; the send attempts with the last failure (labels as BO18) for a TenantAdmin and Adviser; the reject reason (UnknownInstrument "The broker does not trade this instrument", InvalidOrder "The broker could not read the order", MarketClosed "The market is closed", Refused "Refused"); the cancel request ("by <name>", or "by the platform" for `null`); and the fills table: Trade date, Quantity, Price, Settlement ("Settles 25 Nov", "Settled 25 Nov"), Contract note, Status (Posted "Booked" with a link to the Transaction; Pending "Waiting" with the reason: InsufficientCash "Not enough cash", InsufficientHolding "Not enough shares", AccountClosed "The account is closed", NoFxRate "No exchange rate"). A pending fill shows to a TenantAdmin: "Correct the account with an adjustment. The platform books the fill on its next run." |
| D73 | Cancel on `/orders/:id` and in the list, on an open order with no cancel request: a dialog with an optional note and "If the broker has already filled part of this order, that part stays." A 400 shows its sentence; a 409 reloads with "This order has changed." |
| D74 | `/transactions/:id` of a fill or its Reversal shows a "Trade" section: Price, Trade date, Settlement date with "Settled <date>" or "Not settled yet", Contract note, and a link to the order; the Source line (D26) shows "Order". Reverse is not offered (D27). The transactions list and the account activity show a fill like any `Buy` or `Sell`, with "Settles <date>" under the amount while it is not settled. |
| D75 | The Withdraw form (D45) shows "Available to withdraw 3,144.75 USD" from `withdrawableCash`, with "2,000.00 USD from sales settles later." when `unsettledCash` is above 0. The transfer and conversion forms (D19) show the same figure for a Custodial from account. The Close account page shows the two new refusals as its other blockers. The Assign control shows the 400 of R49 as its sentence. |

---

## Amendment 2026-11-23 (trade settlement and broken trades) — accepted

Status of this amendment: `accepted` (2026-11-23), together with [trade-settlement](../features/trade-settlement.md). Field rules live there.

| ID | Behaviour |
| --- | --- |
| D76 | A TenantAdmin sees a nav item "Trade settlements" (`/trade-settlements`); an Adviser does not. The list: Settled date, Broker reference, Currency, Trades, Net ("Pay broker 1,001.50" or "Receive 2,340.00"), Status, with a status filter, default "Open". Labels: AwaitingPayment "Paying the broker", AwaitingReceipt "Waiting for the broker's payment", Completed "Completed", PaymentFailed "Payment failed", Disputed "Figures differ", SetAside "Set aside". `/trade-settlements/:id` shows the platform's and the broker's figures side by side (a difference in red), the fills with links to their orders, the payments with the labels of D46, the failure ("The bank refused the payment: <reason>", "The bank returned the payment: <reason>", "The broker connection has no bank account in <currency>"), the matched statement line as a link, and the set-aside note. "Pay again" on `PaymentFailed`; "Set aside" with a required note and the sentence "The platform stops waiting for this settlement. Its trades are then shown in the bank reconciliation as not settled through the trust account." |
| D77 | The fills table of `/orders/:id` (D72) shows a broken fill struck through with "Cancelled by the broker <date>: <reason>" and, by outcome: Reversed "Reversed" with a link to the Reversal; NotPosted "Was not booked"; NotApplied "Could not be reversed" with the reason (AccountClosed "The account is closed", LaterActivity "Later activity on this holding", InsufficientCash "The proceeds have been spent") and, for a TenantAdmin, "Correct the account with an adjustment, then mark this resolved." with a "Mark resolved" action that takes a note; ResolvedByHand "Resolved by <name>". "Needs attention" (D71) also marks an order with a NotApplied break. `/transactions/:id` of a broken fill shows "Cancelled by the broker" in the Trade section (D74). The Close account page shows the new refusal. |
| D78 | Bank receipts pages (D34, D50): method label Settlement "Trade settlement" with a link to it for a TenantAdmin; reason label SettlementMismatch "Does not match the trade settlement", with the sentence "Return it to the broker, or ignore it with a note." Bank reconciliation (D51): kind labels UnsettledTrades "Trades waiting for settlement", TradesNeedingAction "Trades that need action" (linking to `/trade-settlements?open=true`), UnbookedFills "Settled trades not booked yet", TradeFees "Trade fees held in the trust account"; Trades becomes "Trades not settled through the trust account"; under the items, "Open trade settlements: <n>, net <amount>". |

---

## Amendment 2026-11-23 (rebalance through orders) — accepted

Status of this amendment: `accepted` (2026-11-23), together with [rebalances](../features/rebalances.md). Field rules live there. The Rebalances pages are in [adviser-portal-portfolios.md](adviser-portal-portfolios.md) (P14–P20). Calls: the existing order and transaction reads, with `rebalanceId` on the order item.

| ID | Behaviour |
| --- | --- |
| D79 | Orders pages (D71–D73): an order with `rebalanceId` shows "Rebalance" with a link to `/rebalances/:id`, and Cancel is not offered on it; the detail page says "This order belongs to a rebalance. Cancel the rebalance to cancel it." The Source line of D26 keeps "Rebalance" for existing Transactions, and Reverse (D27) is offered only for `Manual`. |

- D27: Reverse is offered only when `source.kind` is `Manual`; a `Rebalance` source is no longer hand-reversible on a Custodial account (rebalances R28). The portal still does not read the custody mode, so it also hides Reverse on a `Rebalance` Transaction of an External account, which the server would accept, as it does for `Seed`.
