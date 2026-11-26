---
title: Adviser Portal — Portfolios
status: accepted
phase: 2
language: en
created: 2026-11-02
updated: 2026-11-26
related:
  - README.md
  - frontend-conventions.md
  - frontend-implementation-notes.md
  - adviser-portal.md
  - adviser-portal-dashboard.md
  - ../features/portfolios.md
  - ../features/accounts.md
  - ../features/holdings.md
  - ../features/bff-adviser-portal.md
  - ../features/custody-modes.md
  - ../features/rebalances.md
---

# Adviser Portal — Portfolios

Phase 2 portal cut for model portfolios. Consumes the landed portfolios feature (`docs/features/portfolios.md`, master @ 4e6f4dc). Backend contracts live in the Feature Spec. This file owns routes, page scope, and behavioural acceptance for the Adviser Portal.

**Status is `accepted`.**

**Amendment 2026-11-13 (Shared portfolio message, accepted, landed):** see the amendment at the end of this file. Adds P12.

**Amendment 2026-11-17 (custody modes, accepted, landed):** see the amendment at the end of this file. Adds P13, which replaces the account test in P5 ([custody-modes](../features/custody-modes.md)).

Backend prerequisites (landed):

- Portfolios: `GET /portfolios`, `GET /portfolios/{id}`, `POST /portfolios`, `PUT /portfolios/{id}`, `POST /portfolios/{id}/disable`, `POST /portfolios/{id}/rebalance`.
- Assignment: `POST /accounts/{id}/portfolio`, `DELETE /accounts/{id}/portfolio` (unassign), `GET /accounts/{id}/portfolio`.
- Manual rebalance: `POST /accounts/{id}/rebalance`.
- Policies: `portfolios.read` / `portfolios.manage`; assignment and rebalance reuse `transactions.create` + account visibility.
- Script `0023_portfolios.sql`. Full Rebalance semantics. Assigned-account trade lock (R18).

Session remains the BFF cookie. All API calls go through `/api`. BFF prefix allow-list must add `/portfolios` (amendment to `bff-adviser-portal.md` when this cut is accepted).

---

## Current slice

**In**

- Portfolios list (`/portfolios`) for TenantAdmin and Adviser.
- Create portfolio (name, base currency, target lines, optional settings).
- Edit portfolio (weights, name, settings). Weight change on an Active portfolio enqueues async rebalance for every currently Active assignment (202).
- Disable portfolio (`POST /portfolios/{id}/disable`). Unassigns every Active assignment; no automatic trades.
- Assign portfolio to an Open Brokerage or Other account from the account detail. Synchronous Full Rebalance.
- Unassign from the account detail. No automatic trades.
- Manual rebalance for a single account (`POST /accounts/{id}/rebalance`) and bulk rebalance from the portfolio detail (`POST /portfolios/{id}/rebalance`).
- Display of last rebalance status and message on assigned accounts.
- Assigned-account trade lock surfaced in the UI: manual security trade buttons disabled or blocked while an Active assignment exists.

**Out**

- Customer Portal pages.
- SystemAdmin portfolios screens (Scalar only).
- Rebalance preview / what-if trade list (no backend query).
- Scheduled or drift-triggered automatic rebalance.
- Partial / overlay rebalance.
- Multiple Active portfolios per account.
- Fees, tax lots, performance attribution.
- Instrument typeahead (existing list search is enough).
- Detailed empty/error state matrices (later).

---

## Routes

| Route | Who | Purpose |
| --- | --- | --- |
| `/portfolios` | TenantAdmin, Adviser | List of portfolios. Create button. |
| `/portfolios/new` | TenantAdmin, Adviser | Create form. |
| `/portfolios/:id` | TenantAdmin, Adviser | Detail + edit. Target weights table. Assigned accounts with last rebalance status. Bulk rebalance. Disable. |
| Account detail (existing) | TenantAdmin, Adviser | Assign / Unassign / Manual rebalance. Current assignment summary. Trade lock indicator. |

No new top-level routes beyond the portfolios list and detail. Assignment and single-account rebalance live on the existing account detail.

---

## Session

Unchanged. BFF cookie. Missing / dead session → authorize flow. Wrong role → `/forbidden`.

---

## Acceptance (P)

| ID | Behaviour |
| --- | --- |
| P1 | TenantAdmin / Adviser open `/portfolios` and see the list from `GET /portfolios`. Adviser sees only portfolios in the current tenant. |
| P2 | Create form posts `POST /portfolios`. Base currency is required and immutable after create. Lines must sum to 100.00. Optional settings (drift, minimum trade, cash residual) accept the Feature Spec defaults when omitted. |
| P3 | Edit form on `/portfolios/:id` loads `GET /portfolios/{id}` and posts `PUT /portfolios/{id}`. The editor can add a line and remove a line while more than one line remains. Changing weights or lines on a portfolio that has Active assignments returns 202 and enqueues rebalance for every assignment. Unchanged weights or no assignments return 204. |
| P4 | Disable button calls `POST /portfolios/{id}/disable`. All Active assignments are removed. Existing holdings are left untouched. The portfolio no longer appears as assignable. |
| P5 | On an Open Custodial account with no Active assignment, the Assign control is present (P13). Selecting a portfolio calls `POST /accounts/{id}/portfolio`; the outcome is shown as P14 says. While an Active assignment exists, the portfolio dropdown and Assign button are hidden. An External account of any type and a Closed account do not offer the control. |
| P6 | Unassign calls the unassign endpoint. The assignment is marked Removed. No trades are written. The portfolio dropdown and Assign button return. Manual security trade buttons become available again. |
| P7 | An assigned account shows its `rebalance` status line with a link (P17, P20), or "No rebalance yet." It offers Rebalance, which calls `POST /accounts/{id}/rebalance` (P14), including after cash arrives or leaves. |
| P8 | Bulk rebalance on the portfolio detail calls `POST /portfolios/{id}/rebalance` and returns 202; the page says "Proposals queued." The 202 lists only accounts in the Adviser's Book. The assigned accounts table shows each account's `rebalance` status with a link (P20). |
| P9 | While an account has an Active assignment, only its rebalance trades it: Place order and Transfer securities are disabled with "This account follows a portfolio." ([adviser-portal-dashboard](adviser-portal-dashboard.md) D69, D86). Transfer, Convert, Withdraw and Adjust stay available. |
| P10 | Cross-tenant or unknown id returns 404. Customer never receives a session on this portal. |
| P11 | All calls go through the BFF `/api` prefix. No SPA token storage. |

---

### Shared portfolio message

Field rules: [portfolios.md](../features/portfolios.md). Backend rules: PB1–PB8 in its §4.1. `problemCode` lives in `src/AdviserPortal/src/shared/api/problem.ts` and is used by `PortfolioDetailPage`; `updatePortfolio` and `disablePortfolio` invalidate the portfolio cache only on success, so a refused call does not reload.

| ID | Behaviour |
| --- | --- |
| P12 | The portfolio detail page branches on the problem `code` of a 409 from `PUT /portfolios/{id}` and `POST /portfolios/{id}/disable` ([../api-design.md](../api-design.md) §4.2 C2, C8), not on the status alone. `stale_row_version`: today's behaviour ("This row changed. Reload and try again." notice and refetch). `portfolio_shared`, or any other code: show the server `detail` as a message, do not refetch, and keep the Adviser's edits (§4.2 C7). |

The edit page lists only the accounts the Adviser can see, so the weight-change confirmation copy holds. The Disable button and the edit form stay visible to an Adviser; the portal does not try to predict PB3 / PB4, because it cannot see accounts outside the Book.

**Implementation note.** Read the code with one shared helper in `src/AdviserPortal/src/shared/api` (for example `problemCode(error): string | undefined`), so the next page that must branch on `code` reuses it. Do not compare `data.code` inline in each page. Other pages keep branching on `status === 409` until their routes gain a second 409 code (§4.2 C8).

**Tests.** `PortfolioDetailPage.test.tsx`: a `portfolio_shared` 409 on save and on disable shows the server detail and no conflict notice, and the form keeps its edits; a `stale_row_version` 409 still shows the conflict notice and refetches; a 409 with an unknown code shows the server detail. A unit test for the shared helper.

---

### Custody modes

Field rules: [custody-modes](../features/custody-modes.md). Backend rules: custody-modes R14–R17 and portfolios R4.

| ID | Behaviour |
| --- | --- |
| P13 | The Assign control is offered on an Open **Custodial** account with no Active assignment. An External account of any type, and a Closed account, do not offer it. If the call still returns 400 `account_not_eligible`, the page shows the server message. |

- An External account that had an Active assignment before script `0032` shows no assignment afterwards. `GET /accounts/{id}/portfolio` answers as for any account without one. Its holdings stay, and its manual trade buttons are available again.

**Tests.** The account page offers the Assign control on a Custodial account and not on an External Brokerage or Other account. The other portfolio tests use a Custodial account.

---

### Rebalance through orders

Field rules: [rebalances](../features/rebalances.md). The BFF proxies `/rebalances` ([bff-adviser-portal.md](../features/bff-adviser-portal.md) §8.3). Calls: `POST /accounts/{id}/rebalance`, `POST /accounts/{id}/portfolio`, `GET /accounts/{id}/portfolio` (`rebalance`), `GET /rebalances`, `GET /rebalances/{id}`, `POST /rebalances/{id}/confirm`, `POST /rebalances/{id}/cancel`, and the existing order reads.

| ID | Behaviour |
| --- | --- |
| P14 | On an assigned account, "Rebalance" (P7) calls `POST /accounts/{id}/rebalance`. `Proposed` opens `/rebalances/:id`; `NoChange` shows "Nothing to trade. The account is in line with its portfolio."; a 400 shows the server message. Assign (P5, P13) does the same after `POST /accounts/{id}/portfolio`. |
| P15 | `/rebalances/:id` of a `Proposed` rebalance: the account, the portfolio, "Proposed <time> by <name>" ("by the platform" for `null`) and the lines as a table: Instrument, Holding, Now %, Target %, Trade ("Sell 40", "Buy 59.4", "—"), About (the estimated gross), Fee. Under it: "Sales are placed first. Purchases are placed when the sales are filled and are worked out again from the cash then, so the quantities bought may differ. The broker fills at the market price." Buttons Confirm and Cancel. An expired proposal shows "This proposal has expired." and offers "Propose again" (P14) in place of Confirm. |
| P16 | Confirm posts `POST /rebalances/{id}/confirm`. A 400 shows its message; a 409 reloads with "This rebalance has changed." After 204 the page shows the status and the orders by wave: Wave, Side, Instrument, Quantity, Filled, Status (labels of D71), each linked to `/orders/:id`. Manual refresh only. |
| P17 | Status labels: Proposed "Waiting for confirmation", Selling "Selling", Buying "Buying", Completed "Completed", Incomplete "Ended early", Cancelled "Cancelled" (with "replaced by a newer proposal" for `Superseded` and "the portfolio was unassigned" for `Unassigned`), NoChange "Nothing to trade", Failed "Could not be planned". `Incomplete` and `Failed` show `failure.message`. A rebalance with a cancel request shows "Cancelling"; one with `followUp` shows "A new proposal follows when this one ends." |
| P18 | Cancel on an open rebalance: a dialog with an optional note and, for one in progress, "Orders the broker has already filled stay." Posts `POST /rebalances/{id}/cancel`. |
| P19 | Nav item "Rebalances" (`/rebalances`) for TenantAdmin and Adviser: Proposed, Customer, Account, Portfolio, Status, Started by (the trigger: Assign "Assignment", Account "Request", Portfolio "Portfolio change", FollowUp "Follow-up"). Status filter, default "Open"; options Open, Waiting for confirmation, Ended early, All. |
| P20 | `/portfolios/:id`: the assigned accounts table (P7, P8) shows each account's `rebalance` status with a link, in place of the last rebalance status. The weight-change confirmation reads "This will propose a rebalance for every account assigned to this portfolio. Nothing is traded until each proposal is confirmed. Continue?" A "Confirm proposals" button lists the `Proposed` rebalances of the portfolio from `GET /rebalances?portfolioId=&status=Proposed`, and after a dialog ("Confirm <n> proposals?") calls confirm for each in turn and shows how many were confirmed and each refusal. The account page (P7) shows the same status line and links. |

**Tests.** As rebalances §10, Adviser Portal row: P14 the three outcomes; P15 the table, the sentence, the expired state; P16 confirm and the orders by wave; P17 labels; P18 cancel; P19 list and filter; P20 the portfolio page, the new confirmation copy and "Confirm proposals".

---

## Tests

Portal tests assert routes, role visibility, that create/edit/assign/unassign/rebalance call the correct endpoints, and that the trade lock is enforced in the UI. Do not re-prove Full Rebalance math or isolation rules here.

---

## Still open

None. Decisions recorded below.

---

## Locked decisions (2026-11-03)

- **Weights table / pie chart**: first version is a simple editable table (instrument + weight %). The edit form has Add line and Remove line. Remove line is hidden when only one line remains. A basic pie chart is optional and can be adjusted later. No advanced layout required in this cut.
- **Assign controls**: while an Active assignment exists, hide the portfolio dropdown and Assign button. They return after Unassign.
- **Account rebalance**: an assigned account shows Rebalance. It calls `POST /accounts/{id}/rebalance` so a later cash transfer can be invested. A `Failed` rebalance is retried with the same button.
- **Confirmation before weight change**: a simple confirmation dialog. Copy: "This will propose a rebalance for every account assigned to this portfolio. Nothing is traded until each proposal is confirmed. Continue?" After a 202 the page says "Proposals queued." Confirm proceeds; Cancel aborts.
- **202 job progress**: manual page refresh only. Same pattern as report generation. No polling or progress bar in this cut.
- **Current vs target on account detail**: show both. Prefer a side-by-side table (Current | Target) on the account holdings section when an Active assignment exists. Stack vertically on narrow screens.
- **BFF allow-list**: `/portfolios` and `/rebalances` ([bff-adviser-portal.md](../features/bff-adviser-portal.md) §8.3).
- **Empty states and validation wording**: see the short list below. Use these exact strings for the first version.

### Empty states and validation messages

| Situation | Message |
| --- | --- |
| Portfolios list empty | No portfolios yet. Create one to get started. |
| Portfolio has no assigned accounts | No accounts are assigned to this portfolio. |
| Weights do not sum to 100.00 | Weights must total 100.00. |
| A weight is ≤ 0 or > 100 | Each weight must be greater than 0 and no more than 100. |
| Instrument missing or inactive | Choose an active instrument. |
| Assign on ineligible account | Only open Custodial accounts can be assigned a portfolio. |
| Rebalance failed (generic) | Rebalance failed. Check the message and try again. |
| No rebalance yet | No rebalance yet. |

Backend error codes (missing price, missing FX, insufficient cash) are shown as the server message. Do not rewrite them.

---

## History

What changed in this file after it was accepted. Each row is a section above. The text of each change is in git (`git log -p -- docs/adviser-portal/adviser-portal-portfolios.md`).

| Date | With | Section |
| --- | --- | --- |
| 2026-11-13 | [portfolios.md](../features/portfolios.md) | Shared portfolio message |
| 2026-11-17 | [custody-modes](../features/custody-modes.md) | Custody modes |
| 2026-11-23 | [rebalances](../features/rebalances.md) | Rebalance through orders |
| 2026-11-26 | — | The amendments became the sections above. No behaviour changed |
