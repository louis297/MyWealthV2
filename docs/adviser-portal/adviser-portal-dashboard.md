---
title: Adviser Portal — Dashboard and ledger pages
status: accepted
phase: 2
language: en
created: 2026-10-12
updated: 2026-11-21
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
