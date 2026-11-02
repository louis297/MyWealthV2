---
title: Customer Portal
status: accepted
phase: 3
language: en
created: 2026-11-02
updated: 2026-11-02
related:
  - README.md
  - ../adviser-portal/frontend-conventions.md
  - ../function-plan.md
  - ../features/identity-auth.md
  - ../features/bff-customer-portal.md
  - ../features/accounts.md
  - ../features/holdings.md
  - ../features/posting.md
  - ../features/net-worth.md
  - ../features/reports.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
  - ../adr/0016-bff-for-first-party-portals.md
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
| [posting](../features/posting.md) | Grant Customer `transactions.read`. List / get for the caller’s own accounts only. Other customer’s transaction or account id → 404. No `transactions.create`. |
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
| `/accounts/:id` | Customer | Read-only detail: cash balance, holdings, activity. |
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

## Tests

Portal tests assert routes, role visibility (Customer only), and API-client shapes. Do not re-prove backend isolation or the BFF protocol here.

---

## Still open

- Charts and historical net-worth series.
- Report schedules for self.
- Firm name in the shell (`tenants.read` for Customer).
- Instrument detail pages.
- Multi-report parameter forms beyond Customer Transactions.
