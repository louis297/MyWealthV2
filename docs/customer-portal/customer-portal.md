---
title: Customer Portal
status: accepted
phase: 3
language: en
created: 2026-11-02
updated: 2026-11-20
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
---

# Customer Portal

Second first-party frontend. Aspire SPA resource and OIDC `ClientId` are both `customer-portal`.

The portal **does not issue tokens** and **does not hold them**. Password collection stays on hosted login (`identity`). Session edge is [bff-customer-portal.md](../features/bff-customer-portal.md) (accepted). This file is not a backend Feature Spec.

Function is intentionally smaller than the Adviser Portal: a Customer signs in, reads their own accounts / holdings / activity / net worth, and lists / generates / downloads their own Customer-scoped reports. No ledger writes. No account open / close. No instrument catalog. No other customers. No schedules.

---

## Backend prerequisites (amendments to existing accepted specs)

These amendments land with the Customer Portal slice. Do not rebuild the resource API; extend RolePermissions and self-scope checks.

| Spec | Amendment |
| --- | --- |
| [identity-auth](../features/identity-auth.md) | Register confidential client `customer-portal`. Allow-list: Customer only. Per-client cookie scheme `customer-portal` (same pattern as the 2026-10-27 amendment). TenantAdmin / Adviser / SystemAdmin receive no code. |
| [accounts](../features/accounts.md) | Grant Customer `accounts.read`. List / get / cash-balance / holdings reads for the caller’s own accounts only. Other customer’s account id → 404. No `accounts.create` / `accounts.manage`. |
| [posting](../features/posting.md) | Grant Customer `transactions.read`. List / get for the caller’s own accounts only. Other customer’s transaction or account id → 404. No `transactions.create`. Amended 2026-11-10 by [leg-accounts](../features/leg-accounts.md) R7: a transaction is visible when any leg books into an own account, and the item lists only the legs on own accounts. |
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
- Charts, historical comparison, FX fold, live vendor prices.
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

Generate uses a hard-coded form for known Customer-scoped reports (first report: Customer Transactions). Selecting a report from the dropdown shows its form and Generate button. History is a separate panel that defaults to all own Customer-scoped executions; an optional report filter adds `reportId`.

Customer Transactions parameters (portal sets `customerId` to self; the field is not editable):

| Field | Required | Notes |
| --- | --- | --- |
| `customerId` | yes | Hidden; value from `/users/me`. |
| `from` | yes | Date. |
| `to` | yes | Date. |
| `includeClosedAccounts` | no | Default false. |

Empty, loading, error, and success states for the list, the form, and the history panel. Poll an execution until `Succeeded` or `Failed`. Show download only when `Succeeded`.

---

## Acceptance

Requires the BFF and the backend amendments above. Do not re-test Adviser Portal cuts here.

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

## Amendment 2026-11-17 (account custody mode) — accepted, landed

Status of this amendment: `accepted` (2026-11-17), together with [account-custody-mode](../features/account-custody-mode.md).

The account item from `GET /accounts` and `GET /accounts/{id}` gains `custodyMode` (`External` / `Custodial`). The Customer already receives it with `accounts.read`; no backend grant changes.

- `/accounts` and `/accounts/:id`: show "Custody" next to "Type".
- The text is a label, not the enum name: `External` → "Held elsewhere", `Custodial` → "Held on platform". A value the portal does not know is shown as received.
- No grouping of accounts by custody mode and no explanatory sentence yet. Both belong to a later read-model slice. (Superseded by the amendment 2026-11-18 below.)

| Id | Given | When | Then |
| --- | --- | --- | --- |
| C13 | Customer with an External account | Accounts list and account page | "Custody" shows "Held elsewhere" next to the account type. |

---

## Amendment 2026-11-18 (grouping by custody mode) — accepted, landed

Status of this amendment: `accepted` (2026-11-18), together with [custody-mode-grouping](../features/custody-mode-grouping.md). It closes the last item of the 2026-11-17 amendment ("No grouping of accounts by custody mode and no explanatory sentence yet"). No backend grant changes and the BFF does not change: `GET /accounts` already takes `custodyMode`, and the net-worth reads gain `breakdowns.byCustodyMode`.

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

## Amendment 2026-11-19 (adjustments) — accepted

Status of this amendment: `accepted` (2026-11-19), together with [adjustments](../features/adjustments.md). No new call; the BFF does not change.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| C18 | Customer with an `Adjustment` on a Custodial account | Account page activity | The row is labelled "Adjustment" and shows its amount like any other row, and its memo on a line under the label (the activity table shows no memo on other rows). The reason is not shown. Its Reversal stays "Reversal", with no memo line. |

---

## Amendment 2026-11-20 (deposit instructions) — accepted

Status of this amendment: `accepted` (2026-11-20), together with [client-money-accounts](../features/client-money-accounts.md). No backend grant changes: the Customer reads deposit instructions with `accounts.read`, own accounts only. The BFF does not change: the route is under `/accounts`.

| Call | When | Notes |
| --- | --- | --- |
| `GET /accounts/{id}/deposit-instructions` | Account page of an Open Custodial account | 200 `{ accountId, currency, reference, bankName, accountName, accountNumber }`; 404 when unavailable. Not called for External or Closed accounts |

| Id | Given | When | Then |
| --- | --- | --- | --- |
| C19 | Customer with an Open Custodial account whose currency has a client money account | Account page | A "How to pay in" section under the cash balance: Bank, Account name, Account number, Reference, and the sentence "Pay in <currency> only, and quote the reference exactly so the payment is matched to this account." |
| C20 | Customer with a Closed Custodial account, or an Open one whose deposit instructions return 404 | Account page | A Closed account shows no section. An Open one shows "Deposit instructions are not available yet. Contact your adviser." An External account shows no section and calls no route. |

---

## Amendment 2026-11-20 (deposits) — accepted

Status of this amendment: `accepted` (2026-11-20), together with [deposit-matching](../features/deposit-matching.md). No new call and no grant change: the deposit is a `TransferIn` on the existing transaction reads, with `bankReceipt` on the item. The BFF does not change.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| C21 | Customer with a deposit (a `TransferIn` with `bankReceipt`) on a Custodial account | Account page activity | The row is labelled "Deposit" instead of the `TransferIn` label and shows "Value date <date>" on a line under it. Payer details are not shown. Its Reversal stays "Reversal". A `TransferIn` without `bankReceipt` keeps today's label. |
---

## Tests

Portal tests assert routes, role visibility (Customer only), and API-client shapes. Do not re-prove backend isolation or the BFF protocol here.

---

## Still open

- Charts and historical net-worth series.
- Report schedules for self.
- Firm name in the shell (`tenants.read` for Customer).
- Instrument detail pages.
- Multi-report parameter forms beyond Customer Transactions.
