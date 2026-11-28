---
title: Customer Portal
status: accepted
phase: 3
language: en
created: 2026-11-02
updated: 2026-11-28
related:
  - README.md
  - ../adviser-portal/frontend-conventions.md
  - ../function-plan.md
  - ../features/identity-auth.md
  - ../features/bff-customer-portal.md
  - ../features/accounts.md
  - ../features/account-custody-mode.md
  - ../features/custody-mode-grouping.md
  - ../features/holdings.md
  - ../features/posting.md
  - ../features/net-worth.md
  - ../features/reports.md
  - ../features/client-money-accounts.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
  - ../adr/0016-bff-for-first-party-portals.md
  - ../features/deposit-matching.md
  - ../features/withdrawals.md
  - ../features/account-closure.md
  - ../features/interest-run.md
  - ../features/trade-settlement.md
  - ../features/rebalances.md
  - ../features/migration-batches.md
  - ../features/in-kind-transfers.md
  - ../features/dividends.md
  - ../features/valuation-history.md
  - ../features/fif-report.md
---

# Customer Portal

Second first-party frontend. Aspire SPA resource and OIDC `ClientId` are both `customer-portal`.

The portal **does not issue tokens** and **does not hold them**. Password collection stays on hosted login (`identity`). Session edge is [bff-customer-portal.md](../features/bff-customer-portal.md) (accepted). This file is not a backend Feature Spec.

Function is intentionally smaller than the Adviser Portal: a Customer signs in, reads their own accounts / holdings / activity / net worth, and lists / generates / downloads their own Customer-scoped reports. No ledger writes. No account open / close. No instrument catalog. No other customers. No schedules.

---

## Backend prerequisites

These grants landed with the Customer Portal slice; each is now a rule of the spec named. Do not rebuild the resource API.

| Spec | What the Customer Portal needs from it |
| --- | --- |
| [identity-auth](../features/identity-auth.md) | Register confidential client `customer-portal`. Allow-list: Customer only. Per-client cookie scheme `customer-portal` (identity-auth §4.3). TenantAdmin / Adviser / SystemAdmin receive no code. |
| [accounts](../features/accounts.md) | Grant Customer `accounts.read`. List / get / cash-balance / holdings reads for the caller’s own accounts only. Other customer’s account id → 404. No `accounts.create` / `accounts.manage`. |
| [posting](../features/posting.md) | Grant Customer `transactions.read`. List / get for the caller’s own accounts only. Other customer’s transaction or account id → 404. No `transactions.create`. A transaction is visible when any leg books into an own account, and the item lists only the legs on own accounts. |
| [net-worth](../features/net-worth.md) | Customer may call `GET /users/customers/{ownPublicId}/net-worth` (and `GET /net-worth`, which for Customer returns the same self scope). Other customer id → 404. Reuse `accounts.read`. |
| [reports](../features/reports.md) | Customer already has `reports.customer.read`. Grant `reports.customer.create` for **request only**. `POST /reports/request` succeeds only when `parameters.customerId` is the caller (else 404). `POST /reports/schedules` and schedule verbs stay 403 for Customer. Book-scoped routes stay 403. |

Holdings read reuses `accounts.read` (existing rule). Instrument names and symbols already travel on holdings and net-worth responses; Customer does not receive `instruments.read` and the BFF does not proxy `/instruments`.

---

## Current slice

**In**

- Role-filtered shell. Default home = `/dashboard`.
- Profile: `PUT /users/me`, `PUT /users/me/password`.
- Dashboard: own current net worth (per-currency totals, byAccountType, topHoldings). Reuse `GET /users/customers/{id}/net-worth` or `GET /net-worth` (self).
- Accounts: list of own accounts (`GET /accounts`). Detail (`GET /accounts/{id}`) shows cash balance, holdings (`GET /accounts/{id}/holdings`), and activity (`GET /transactions?accountId=`).
- Reports: list Customer-scoped reports granted to the tenant and active (`GET /reports?scope=Customer`). Generate now (`POST /reports/request` with `customerId` = self). History of own executions (`GET /reports/executions?scope=Customer`). Download PDF when status is `Succeeded`. Poll until terminal. No SignalR.
- After `/users/me`, the shell may show the person’s name. Firm name is optional (Customer has no `tenants.read` in this slice).

**Out**

- React password login page.
- Ledger writes (Buy / Sell / StockIn / StockOut / Split / Bonus / Scrip / cash types / Reverse).
- Account open / close / rename.
- Instrument catalog pages or picker.
- Other customers, Advisers, TenantAdmin screens.
- Book-scoped reports.
- Report schedules (create / edit / disable).
- Catalog or tenant-grant management.
- Historical comparison, live vendor prices. The charts and the folded total are [Valuation history](#valuation-history).
- SystemAdmin screens.
- Shared BFF with the Adviser Portal.

---

## Routes

| Path | Who | Behaviour |
| --- | --- | --- |
| `/bff/login` | BFF, not React | Challenge. `returnUrl` is an in-app path. |
| `/` | Customer | Redirect `/dashboard`. |
| `/dashboard` | Customer | Own net worth. Widgets: per-currency totals, byAccountType, topHoldings. |
| `/accounts` | Customer | List of own accounts. |
| `/accounts/:id` | Customer | Read-only detail: cash balance, holdings, activity. Activity shows the amount of the cash leg on this account (the sum of its `Principal` and `Fee` legs when a trade has a fee, [features/trade-fees.md](../features/trade-fees.md) R22). An `InternalTransfer` is labelled "Transfer"; no counterparty is shown ([internal-transfers](../features/internal-transfers.md) §9). An `FxConversion` is labelled "Currency exchange" on each of the Customer's two Accounts, with that Account's leg and the rate line ([fx-conversions](../features/fx-conversions.md) §9). |
| `/reports` | Customer | Customer-scoped reports: list, generate, history, download. |
| `/profile` | Customer | Name + password. |
| `/forbidden` | leftover wrong-role | Second line only. Non-Customer must not obtain a cookie. |
| `/login` | — | Must not be a password page. |

---

## Session

Unchanged from the BFF. Probe is `GET /api/users/me`. 401 does not refresh in the browser. Sign-out remains `POST /bff/logout` with `X-MyWealth-Request: 1`. All resource calls use the shared API client (`credentials: 'include'`, base `/api`).

---

## Client

| Item | Value |
| --- | --- |
| ClientId | `customer-portal` |
| Holder | `bff-customer-portal` (`src/BffCustomerPortal`). Not the browser |
| Type | Confidential + PKCE. Aspire parameter `customer-portal-client-secret`. Password grant off |
| Scopes | `openid`, `profile`, `offline_access`, `api` |
| Redirect | `{bffOrigin}/signin-oidc` |
| Post-logout | `{bffOrigin}/` |
| Token store | Encrypted BFF cookie. Browser: `__Host-bff-customer-portal` only |
| Allow-list | Customer only |
| Sign out | `POST /bff/logout` with `X-MyWealth-Request: 1`. BFF revokes refresh, then identity end-session. SPA must not call `startAuthorize` while that is in flight. |

---

## Instants

Show every instant from the API (`bookedAt`) in browser local time with `formatInstant` (`shared/time/instant.ts`, local `yyyy-MM-dd HH:mm`). Date-only values (`rateAsOfDate`, report `from` / `to`) stay strings. Tests pin `TZ=Pacific/Auckland` in `vite.config.ts`. See [fixes/utc-instants.md](../fixes/utc-instants.md).

## API calls used by the portal

All routes are under `/api`. Policy and self-scope are enforced by the backend.

| Call | When | Notes |
| --- | --- | --- |
| `GET /users/me` | Session probe, shell | Same DTO as Adviser Portal. |
| `PUT /users/me` | Profile name | |
| `PUT /users/me/password` | Profile password | Then clear session and re-authorize. |
| `GET /net-worth` or `GET /users/customers/{id}/net-worth` | Dashboard | Self only. Prefer the customer route with the id from `/users/me`. |
| `GET /accounts` | Accounts list | Own accounts. Optional `enabledOnly`, `type`, `search`. |
| `GET /accounts/{id}` | Account detail | Own account or 404. |
| `GET /accounts/{id}/holdings` | Account detail | Own account or 404. |
| `GET /accounts/{id}/cash-balance` | Account detail | Own account or 404. |
| `GET /transactions?accountId=` | Account detail activity | Own account or 404. |
| `GET /reports?scope=Customer` | Reports page | Granted + active only. |
| `POST /reports/request` | Generate now | Body: `{ reportId, parameters }`. `parameters.customerId` = self. 202 `{ id }`. |
| `GET /reports/executions?scope=Customer` | History | Own executions only. Optional `reportId`. |
| `GET /reports/executions/{id}` | Poll | Own execution or 404. |
| `GET /reports/executions/{id}/download` | Download | Succeeded only. |

---

## Reports page (field bindings)

Generate uses a hard-coded form for known Customer-scoped reports: Customer Transactions and FIF Report. Selecting a report from the dropdown shows its form and Generate button. History is a separate panel that defaults to all own Customer-scoped executions; an optional report filter adds `reportId`.

Customer Transactions parameters (portal sets `customerId` to self; the field is not editable):

| Field | Required | Notes |
| --- | --- | --- |
| `customerId` | yes | Hidden; value from `/users/me`. |
| `from` | yes | Date. |
| `to` | yes | Date. |
| `includeClosedAccounts` | no | Default false. |

FIF Report parameters ([fif-report](../features/fif-report.md) R6, R28):

| Field | Required | Notes |
| --- | --- | --- |
| `customerId` | yes | Hidden; value from `/users/me`. |
| `taxYear` | yes | A select of the tax years that can be reported today (C50). Sent as a number. |

Empty, loading, error, and success states for the list, the form, and the history panel. Poll an execution until `Succeeded` or `Failed`. Show download only when `Succeeded`.

---

## Acceptance

Requires the BFF and the backend prerequisites above. Do not re-test Adviser Portal cuts here.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| C1 | Customer, no session | Open `/dashboard` | Authorize → hosted login → BFF callback → land on `/dashboard`. No React password page. |
| C2 | Same session as C1 | Shell | Nav: Dashboard, Accounts, Reports, Profile. Name from `/users/me`. |
| C3 | TenantAdmin / Adviser / SystemAdmin + correct password | Hosted login for this client | No authorization code. Portal never stores that session. |
| C4 | Customer | Dashboard | Per-currency net worth and the self breakdowns. Closed accounts excluded. |
| C5 | Customer | Accounts list | Only own accounts. Click through to detail shows cash, holdings, activity. |
| C6 | Customer | Open another customer’s account id | “Not found” on that URL. Not 403. |
| C7 | Customer | Reports page | Only Customer-scoped granted + active reports. Generate sets `customerId` to self. History shows only own executions. Download when Succeeded. |
| C8 | Customer | `POST /reports/request` with another customerId | 404 from `webapi`, passed through. |
| C9 | Customer | Attempt a ledger write or schedule | 403 (or BFF 404 if the path is not proxied). No write controls in the UI. |
| C10 | Customer | Sign out | BFF cookie cleared. Refresh revoked. IdentityHost end-session clears only the `customer-portal` scheme cookie. Next visit is hosted `/login`. No token keys in `sessionStorage`. |
| C11 | Deep link `/accounts/{id}` while signed out | After callback | Returns to that path when it is an in-app path. |
| C12 | Customer | Profile password change | 204 then the app clears the session and starts authorize again. |

---

### Account custody mode

Field rules: [account-custody-mode](../features/account-custody-mode.md).

The account item from `GET /accounts` and `GET /accounts/{id}` gains `custodyMode` (`External` / `Custodial`). The Customer already receives it with `accounts.read`; no backend grant changes.

- `/accounts` and `/accounts/:id`: show "Custody" next to "Type".
- The text is a label, not the enum name: `External` → "Held elsewhere", `Custodial` → "Held on platform". A value the portal does not know is shown as received.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| C13 | Customer with an External account | Accounts list and account page | "Custody" shows "Held elsewhere" next to the account type. |

---

### Grouping by custody mode

Field rules: [custody-mode-grouping](../features/custody-mode-grouping.md). No backend grant changes and the BFF does not change: `GET /accounts` already takes `custodyMode`, and the net-worth reads gain `breakdowns.byCustodyMode`.

The note, used word for word wherever it appears (the same sentence is in the Customer Transactions PDF, custody-mode-grouping R15):

> Held elsewhere: recorded by your adviser from outside statements. The platform has not checked these figures.

| Call | When | Notes |
| --- | --- | --- |
| `GET /accounts?custodyMode=Custodial&page=1&pageSize=100` | Accounts list, "Held on platform" group | Replaces the single list request |
| `GET /accounts?custodyMode=External&page=1&pageSize=100` | Accounts list, "Held elsewhere" group | Replaces the single list request |

| Id | Given | When | Then |
| --- | --- | --- | --- |
| C14 | Customer with a Custodial and an External account | Dashboard | Below the per-currency totals and above "By account type", a section "Held on platform and elsewhere" with a "Held on platform" row and a "Held elsewhere" row, each listing `assets.total` per currency from `byCustodyMode`. The note is under the "Held elsewhere" row. |
| C15 | Customer with accounts of one custody mode only | Dashboard | Only that row is shown; the note only with "Held elsewhere". No accounts → the section shows "No balances." |
| C16 | Customer | Accounts list | Two groups, "Held on platform" first, then "Held elsewhere", from the two filtered requests. A group with no accounts is not shown; both empty → "No accounts.". The note is under the "Held elsewhere" heading. The table columns are unchanged (C13). |
| C17 | Customer | Account page | An External account shows the note under the "Custody" field. A Custodial account shows no note. |

---

### Adjustments

Field rules: [adjustments](../features/adjustments.md). No new call; the BFF does not change.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| C18 | Customer with an `Adjustment` on a Custodial account | Account page activity | The row is labelled "Adjustment" and shows its amount like any other row, and its memo on a line under the label (the activity table shows no memo on other rows). The reason is not shown. Its Reversal stays "Reversal", with no memo line. |

---

### Deposit instructions

Field rules: [client-money-accounts](../features/client-money-accounts.md). No backend grant changes: the Customer reads deposit instructions with `accounts.read`, own accounts only. The BFF does not change: the route is under `/accounts`.

| Call | When | Notes |
| --- | --- | --- |
| `GET /accounts/{id}/deposit-instructions` | Account page of an Open Custodial account | 200 `{ accountId, currency, reference, bankName, accountName, accountNumber }`; 404 when unavailable. Not called for External or Closed accounts |

| Id | Given | When | Then |
| --- | --- | --- | --- |
| C19 | Customer with an Open Custodial account whose currency has a client money account | Account page | A "How to pay in" section under the cash balance: Bank, Account name, Account number, Reference, and the sentence "Pay in <currency> only, and quote the reference exactly so the payment is matched to this account." |
| C20 | Customer with a Closed Custodial account, or an Open one whose deposit instructions return 404 | Account page | A Closed account shows no section. An Open one shows "Deposit instructions are not available yet. Contact your adviser." An External account shows no section and calls no route. |

---

### Deposits

Field rules: [deposit-matching](../features/deposit-matching.md). No new call and no grant change: the deposit is a `TransferIn` on the existing transaction reads, with `bankReceipt` on the item. The BFF does not change.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| C21 | Customer with a deposit (a `TransferIn` with `bankReceipt`) on a Custodial account | Account page activity | The row is labelled "Deposit" instead of the `TransferIn` label and shows "Value date <date>" on a line under it. Payer details are not shown. Its Reversal stays "Reversal". A `TransferIn` without `bankReceipt` keeps today's label. |
---

### Bank accounts, available cash and withdrawals

Field rules: [withdrawals](../features/withdrawals.md). New calls: `GET /nominated-bank-accounts` and `GET /withdrawals?accountId=`, both read-only for a Customer (roadmap D6). The BFF gains the two prefixes ([bff-customer-portal.md](../features/bff-customer-portal.md) R20). The account item carries `reservedCash` and `availableCash`; a payout carries `payment`.

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C22 | Customer with nominated bank accounts | Profile page | A "Bank accounts" section lists Pending and Verified rows: Bank, Account name, Account number, Currency, and "Verified" or "Being checked". No add or remove control. "To change your bank accounts, contact your adviser." |
| C23 | Customer with a Custodial account | Account page | Under the cash balance: "Available to withdraw" with `availableCash`, and when `reservedCash` is not 0, "Withdrawals in progress" with it. |
| C24 | Customer with withdrawals on a Custodial account | Account page | A "Withdrawals" section lists open withdrawals and those of the last 90 days: Requested, Amount, To (****8900), Status ("Waiting for approval", "Approved", "Sent to bank", "Paid", "Rejected", "Cancelled", "Bank refused", "Returned by bank"). No action. No memo or note. |
| C25 | Customer with a payout on a Custodial account | Account page activity | The row is labelled "Withdrawal" instead of the `TransferOut` label and shows "Value date <date>" under it. Its Reversal stays "Reversal". A `TransferOut` without `payment` keeps today's label. |

---

### Account closure

Field rules: [account-closure](../features/account-closure.md). The Customer Portal reads the account item's `closure` and the withdrawals it already reads; it calls no closure route and the BFF does not change. The Customer requests no closure (roadmap D6).

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C26 | Customer with a closing Custodial account | Account page | A note above the balance: "This account is being closed. 300.00 USD is being paid to your bank account ****8900." (amount from `closure.payoutAmount`, the number from the withdrawal). A Closed account whose latest closure is `Completed` shows "Closed on <date>." |
| C27 | Customer with a write-off on a Custodial account | Account page activity | The row is labelled "Rounding write-off" instead of the `CloseOut` label. A `CloseOut` with another source keeps today's label. |

---

### Interest run

Field rules: [interest-run](../features/interest-run.md). The Customer Portal reads `GET /users/me/tax-profile`, the account item's `accruedInterest` and `interestAccruedThrough`, and the transaction item's `interest` and leg `tax`. The Customer Portal BFF does not change: it already proxies `/users`, `/accounts` and `/transactions`.

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C28 | A Customer | Profile page | A "Tax details" section from `GET /users/me/tax-profile`: Tax residency, IRD number ("On file" / "Not on file"), RWT rate, and "Tax taken from your interest: <rate> %". Under it: "To change these details, contact your adviser." No form. |
| C29 | A Customer with a Custodial account whose `accruedInterest` is above 0 | Account page | "Interest earned so far this period: <amount>" (the currency's minor unit, rounded down) under the balance, with "Paid after the end of the month." |
| C30 | A Customer with an interest posting | Account page activity | The row is labelled "Interest" with its period, and shows Gross, "Tax withheld (<kind label> <rate> %)" and the net amount. A hand-entered `Interest` on an External account has no period: C49. |

---

### Corporate actions

Field rules: [corporate-actions](../features/corporate-actions.md). The Customer Portal reads the transaction item's `corporateAction`. The Customer Portal BFF does not change: it already proxies `/transactions`.

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C31 | A Customer with an entitlement posting | Account page activity | The row is labelled "Share split 2 for 1", "Share consolidation 1 for 10" or "Bonus issue 1 for 5", with "<quantityBefore> → <quantityAfter> shares" and "Ex-date <exDate>". No cash amount. A hand-entered `Split` on an External account keeps today's row. |

---

### Orders and settlement

Field rules: [orders](../features/orders.md). The Customer Portal reads the Customer's own orders, the transaction item's `execution`, the account item's `unsettledCash` and `withdrawableCash`, and the holding item's `reservedQuantity`. It stays read-only: a Customer places and cancels no order (roadmap D6). The Customer Portal BFF gains the prefix `/orders` ([bff-customer-portal.md](../features/bff-customer-portal.md) R20).

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C32 | A Customer with an open order | Account page | An "Orders" block from `GET /orders?accountId=` lists open orders and those that ended in the last 30 days: "Buy 10 ACME.SIM", the status label of D71 in the Customer's words (Placed and Working "With the broker", PartlyFilled "Partly filled (4 of 10)", Filled "Done", Cancelled "Cancelled", Rejected "Not accepted", Expired "Expired") and the placed date. No action. |
| C33 | A Custodial account with unsettled or reserved cash | Account page cash block | "Cash", "Reserved" (when not 0), "From sales, not settled yet" (when not 0) and "Available to withdraw" from `withdrawableCash`. |
| C34 | A Customer with a fill | Account page activity | The row is labelled "Bought 4 ACME.SIM at 100.12" or "Sold …", with the trade date, "Settles 25 Nov" or "Settled", and the contract note. A trade that is not a fill keeps today's row. |
| C35 | A holding with a reserved quantity | Holdings | "<n> reserved for a sell order" under the quantity. |

---

### Trade settlement and broken trades

Field rules: [trade-settlement](../features/trade-settlement.md).

| ID | Behaviour |
| --- | --- |
| C36 | Customer Portal, account activity: the Reversal of a fill is labelled "Trade cancelled by the broker: Bought 4 ACME.SIM at 100.12", and the fill's own row shows "Cancelled" in place of the settlement text. A broken fill that was never booked shows nothing. |

---

### Rebalance through orders

Field rules: [rebalances](../features/rebalances.md). The Customer Portal BFF does not change: `rebalanceId` comes on the order item under `/orders`.

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C37 | A Customer with an order that has a `rebalanceId` | Account page, Orders block (C32) | The row shows "Rebalance" under the order. No link and no action. The fill rows of C34 are unchanged. |

---

### Migration batches

Field rules: [migration-batches](../features/migration-batches.md). The Customer Portal BFF does not change: `migration` comes on the transaction item it already reads.

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C38 | Customer with a Transaction that has `migration` | Account page activity | An `Opening` row is labelled "Opening balance" and an `OpeningHoldings` row "Holdings transferred in", each with "From <previousProvider>, as at <asOfDate>" under it. A Transaction without `migration` keeps today's labels. |

---

### In-kind transfers

Field rules: [in-kind-transfers](../features/in-kind-transfers.md). The Customer Portal BFF gains the prefix `/in-kind-transfers` (bff-customer-portal R20); `inKindTransfer` comes on the transaction item and the holding fields on the holdings item it already reads.

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C39 | A Customer with an in-kind transfer | Account page | A "Transfers" block from `GET /in-kind-transfers?accountId=` lists open transfers and those that ended in the last 30 days: "100 ACME.SIM from Harbour Brokers" or "… to Harbour Brokers", the status in the Customer's words (Requested "Being checked", Approved and Sent "On its way", Completed "Done", Rejected "Not accepted", Cancelled "Cancelled", Failed and SetAside "Not completed") and the requested date. No action. |
| C40 | A Customer with a Transaction that has `inKindTransfer` | Account page activity | A `StockIn` row is labelled "Shares transferred in" and a `StockOut` row "Shares transferred out", each with "100 ACME.SIM, Harbour Brokers" under it. A Transaction without `inKindTransfer` keeps today's labels. |
| C41 | A holding with a reserved or unsettled quantity | Holdings | "<n> reserved" under the quantity now also covers a transfer out ("reserved for a sale or a transfer"); "<n> not settled yet" when the unsettled quantity is above 0. |

---

### Dividends

Field rules: [dividends](../features/dividends.md). No new BFF prefix: `dividend` on the transaction item and `reinvestDividends` on the account item come with the existing reads.

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C42 | A Customer with a `Dividend` | Account page activity | The row is labelled "Dividend <symbol>", with "<quantity> shares × <amountPerShare>", the gross, one line per tax leg, "Imputation credit <amount>" when above 0, and the net cash amount. A hand-entered dividend: C48. |
| C43 | A reinvested dividend | Account page activity | The row also shows "Reinvested in <reinvestedQuantity> shares at <reinvestmentPrice>", and the holding shows the new quantity. |
| C44 | A Custodial account | Account page | "Dividends are reinvested" or "Dividends are paid in cash", read only, with "Ask your adviser to change this." |

### Valuation history

Field rules: [valuation-history](../features/valuation-history.md). No new BFF prefix: `GET /net-worth/funds-under-management` (self scope) and `GET /accounts/{id}/valuations` are under the existing `/net-worth` and `/accounts` prefixes, and `folded` comes with the net-worth read. The charts are plain SVG with no chart library, with a hollow marker on an incomplete day, the date and total on hover or focus, and a "Show figures" table under each chart.

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C45 | Customer with a Custodial account | Dashboard | Under "Held on platform and elsewhere" (C14), a chart "Held on platform over time" from `GET /net-worth/funds-under-management` (self scope), 90 days, with the choice of 1 year. Above it: "In <reportingCurrency>, at each day's exchange rate. The latest two days are added once their prices are final." An incomplete day's point says "Some holdings could not be valued on this day." No Custodial account in the range → the chart is not shown. External accounts are not in it, and the "Held elsewhere" note (C14) is not repeated. |
| C46 | Customer | Dashboard | Under the per-currency totals, "Total in <folded.currency>: <folded.total> at today's exchange rates", as D108, with "Not counted: <currency>" when a line is not folded. With one currency only, which is the reporting currency, the line is not shown. |
| C47 | Customer with a Custodial account | Account page | The chart of D109, with the Customer's words for an incomplete day ("Some holdings could not be valued on this day.") and "Not valued yet. The first value appears two days after the account was opened." An External account shows no chart. |

---

### Income on External accounts

Field rules: [external-income](../features/external-income.md). No new BFF prefix: a hand-entered dividend and the tax of a hand `Interest` come with the existing transaction reads.

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C48 | A Customer with a hand-entered `Dividend` on an External account | Account page activity | The row of C42, with "<quantity> shares" alone when there is no amount per share. |
| C49 | A Customer with a hand-entered `Interest` that has tax | Account page activity | The row is labelled "Interest" and shows Gross, "Tax withheld (<kind label> <rate> %)" per tax and the net amount, as C30 without a period. Without tax the row is today's. |

### FIF Report

Field rules: [fif-report](../features/fif-report.md) R6, R7, R8, R28 (report `customer-fif`, `7d2f6b48-1a9c-4e35-a7d0-5c8e3b1f9a44`, manual only). No new BFF prefix: the existing `/reports` routes.

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C50 | A Customer, the report granted | Reports page, "FIF Report" selected | The form has one field, "Tax year", a select of every tax year that can be reported today, the latest first and selected, each as "<Y> (1 April <Y − 1> to 31 March <Y>)"; tax year `Y` is offered from 2 April `Y` (UTC), down to 2009. Generate sends `{ reportId, parameters: { customerId: <self>, taxYear: <Y> } }`. No schedule controls. |
| C51 | A Customer whose tax residency is `Overseas` | Generate the FIF Report | The report is in the list. The server's 400 message ("A FIF report is made for a customer whose tax residency is New Zealand.") is shown at the form, not "Report is not available." |

## Tests

Portal tests assert routes, role visibility (Customer only), and API-client shapes. Do not re-prove backend isolation or the BFF protocol here.

---

## Still open

- A net-worth series with External accounts (valuation-history, Out).
- Report schedules for self.
- Firm name in the shell (`tenants.read` for Customer).
- Instrument detail pages.
- A parameter schema for dynamic forms (the Reports page hard-codes two forms).

---

## History

What changed in this file after it was accepted. Each row is a section above. The text of each change is in git (`git log -p -- docs/customer-portal/customer-portal.md`).

| Date | With | Section |
| --- | --- | --- |
| 2026-11-17 | [account-custody-mode](../features/account-custody-mode.md) | Account custody mode |
| 2026-11-18 | [custody-mode-grouping](../features/custody-mode-grouping.md) | Grouping by custody mode |
| 2026-11-19 | [adjustments](../features/adjustments.md) | Adjustments |
| 2026-11-20 | [client-money-accounts](../features/client-money-accounts.md) | Deposit instructions |
| 2026-11-20 | [deposit-matching](../features/deposit-matching.md) | Deposits |
| 2026-11-20 | [withdrawals](../features/withdrawals.md) | Bank accounts, available cash and withdrawals |
| 2026-11-22 | [account-closure](../features/account-closure.md) | Account closure |
| 2026-11-22 | [interest-run](../features/interest-run.md) | Interest run |
| 2026-11-22 | [corporate-actions](../features/corporate-actions.md) | Corporate actions |
| 2026-11-23 | [orders](../features/orders.md) | Orders and settlement |
| 2026-11-23 | [trade-settlement](../features/trade-settlement.md) | Trade settlement and broken trades |
| 2026-11-23 | [rebalances](../features/rebalances.md) | Rebalance through orders |
| 2026-11-24 | [migration-batches](../features/migration-batches.md) | Migration batches |
| 2026-11-24 | [in-kind-transfers](../features/in-kind-transfers.md) | In-kind transfers |
| 2026-11-24 | [dividends](../features/dividends.md) | Dividends |
| 2026-11-26 | [valuation-history](../features/valuation-history.md) | Valuation history |
| 2026-11-26 | — | The amendments became the sections above. No behaviour changed |
| 2026-11-27 | [external-income](../features/external-income.md) | Income on External accounts |
| 2026-11-28 | [fif-report](../features/fif-report.md) | Reports page (field bindings), FIF Report (C50, C51), Still open |
