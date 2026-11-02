---
title: Adviser Portal — Portfolios
status: accepted
phase: 2
language: en
created: 2026-11-02
updated: 2026-11-03
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
---

# Adviser Portal — Portfolios

Phase 2 portal cut for model portfolios. Consumes the landed portfolios feature (`docs/features/portfolios.md`, master @ 4e6f4dc). Backend contracts live in the Feature Spec. This file owns routes, page scope, and behavioural acceptance for the Adviser Portal.

**Status is `accepted`.**

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
| P3 | Edit form on `/portfolios/:id` loads `GET /portfolios/{id}` and posts `PUT /portfolios/{id}`. Changing weights on a portfolio that has Active assignments returns 202 and enqueues rebalance for every assignment. Unchanged weights or no assignments return 204. |
| P4 | Disable button calls `POST /portfolios/{id}/disable`. All Active assignments are removed. Existing holdings are left untouched. The portfolio no longer appears as assignable. |
| P5 | On an Open Brokerage or Other account the Assign control is present. Selecting a portfolio calls `POST /accounts/{id}/portfolio`. Success or NoChange is shown. Bank, Cash, or Closed accounts do not offer the control (or the call returns 400). |
| P6 | Unassign calls the unassign endpoint. The assignment is marked Removed. No trades are written. Manual security trade buttons become available again. |
| P7 | Assigned accounts show last rebalance status (Success / NoChange / Failed) and the stored message. Failed status offers a Retry that calls `POST /accounts/{id}/rebalance`. |
| P8 | Bulk rebalance on the portfolio detail calls `POST /portfolios/{id}/rebalance` and returns 202. The page can refresh or poll account status to show per-account results. |
| P9 | While an account has an Active assignment, Buy / Sell / StockIn / StockOut / Split / Bonus / Scrip / OpeningHoldings buttons are disabled or the form is blocked. Cash transfer in/out remains available. |
| P10 | Cross-tenant or unknown id returns 404. Customer never receives a session on this portal. |
| P11 | All calls go through the BFF `/api` prefix. No SPA token storage. |

---

## Tests

Portal tests assert routes, role visibility, that create/edit/assign/unassign/rebalance call the correct endpoints, and that the trade lock is enforced in the UI. Do not re-prove Full Rebalance math or isolation rules here.

---

## Still open

None. Decisions recorded below.

---

## Locked decisions (2026-11-03)

- **Weights table / pie chart**: first version is a simple editable table (instrument + weight %). A basic pie chart is optional and can be adjusted later. No advanced layout required in this cut.
- **Confirmation before weight change**: a simple confirmation dialog. Copy: “This will rebalance every account currently assigned to this portfolio. Continue?” Confirm proceeds; Cancel aborts.
- **202 job progress**: manual page refresh only. Same pattern as report generation. No polling or progress bar in this cut.
- **Current vs target on account detail**: show both. Prefer a side-by-side table (Current | Target) on the account holdings section when an Active assignment exists. Stack vertically on narrow screens.
- **BFF allow-list**: add `/portfolios` (see amendment below).
- **Empty states and validation wording**: see the short list below. Use these exact strings for the first version.

### Empty states and validation messages

| Situation | Message |
| --- | --- |
| Portfolios list empty | No portfolios yet. Create one to get started. |
| Portfolio has no assigned accounts | No accounts are assigned to this portfolio. |
| Weights do not sum to 100.00 | Weights must total 100.00. |
| A weight is ≤ 0 or > 100 | Each weight must be greater than 0 and no more than 100. |
| Instrument missing or inactive | Choose an active instrument. |
| Assign on ineligible account | Only open Brokerage or Other accounts can be assigned a portfolio. |
| Rebalance failed (generic) | Rebalance failed. Check the message and try again. |
| No last rebalance yet | No rebalance has run yet. |

Backend error codes (missing price, missing FX, insufficient cash) are shown as the server message. Do not rewrite them.

