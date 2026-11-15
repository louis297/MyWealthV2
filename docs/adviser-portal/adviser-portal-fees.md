---
title: Adviser Portal — Fees
status: accepted
phase: 2
language: en
created: 2026-11-15
updated: 2026-11-15
related:
  - README.md
  - frontend-conventions.md
  - adviser-portal.md
  - adviser-portal-dashboard.md
  - adviser-portal-portfolios.md
  - ../features/trade-fees.md
  - ../features/bff-adviser-portal.md
  - ../api-design.md
---

# Adviser Portal — Fees

Portal cut for trade fees. Consumes [features/trade-fees.md](../features/trade-fees.md) (accepted 2026-11-15; backend landed 2026-11-15, these pages are not). Backend contracts live in the Feature Spec. This file owns routes, page scope, field bindings and behavioural acceptance in the Adviser Portal.

Backend prerequisites (accepted, must land first):

- `GET /fees/trade-schedules`, `GET /fees/trade-schedules/{id}`, `POST /fees/trade-schedules`, `PUT /fees/trade-schedules/{id}`, `DELETE /fees/trade-schedules/{id}` ([trade-fees](../features/trade-fees.md) §8).
- Policies `fees.read` (TenantAdmin, Adviser) and `fees.manage` (TenantAdmin) ([api-design.md](../api-design.md) §3.1).
- Problem codes `fee_schedule_in_effect` and `stale_row_version` on `PUT` ([api-design.md](../api-design.md) §4.2).
- Transaction item: every cash leg carries `role` and `fee` (trade-fees §8.1).
- `POST /fees/trade-schedules/preview` (`fees.read`; trade-fees amendment 2026-11-15, R30–R33).

Session stays the BFF cookie. All calls go through `/api`. The BFF prefix allow-list adds `/fees`: [bff-adviser-portal.md](../features/bff-adviser-portal.md) amendment 2026-11-15.

---

## Current slice

**In**

- Fees page (`/fees`) with one tab, Trade fees: schedules grouped by quote currency, with the state of each row. TenantAdmin and Adviser.
- Create a trade fee schedule. TenantAdmin.
- Edit or delete a pending schedule. TenantAdmin.
- Read-only schedule detail. TenantAdmin and Adviser.
- Fee preview on the create / edit form and on the detail page, computed by `webapi`. TenantAdmin and Adviser.
- Fees on existing ledger pages: transaction detail, transactions list, account detail activity, and a note on the `Buy` / `Sell` form.

**Out**

- Other fee kinds and their tabs (advisory fees are roadmap step 4).
- A fee calculation in TypeScript. The portal never computes a fee; it shows what the preview route returns.
- A quote for a real trade on the `Buy` / `Sell` form (needs price and FX; not in the backend).
- Fees in the rebalance result or on the portfolio pages (trade-fees §2 Out).
- Customer Portal fee display.
- Back Office pages. SystemAdmin uses Scalar.
- Copying a schedule to another currency.

---

## Routes

| Route | Who | Purpose |
| --- | --- | --- |
| `/fees` | TenantAdmin, Adviser | Redirect to `/fees/trade`. |
| `/fees/trade` | TenantAdmin, Adviser | Trade fees tab: schedules grouped by quote currency. New schedule button for TenantAdmin. |
| `/fees/trade/new` | TenantAdmin | Create form. Adviser → `/forbidden`. |
| `/fees/trade/:id` | TenantAdmin, Adviser | Read-only detail. Edit and Delete for TenantAdmin when the row is pending. |
| `/fees/trade/:id/edit` | TenantAdmin | Edit form for a pending row. Adviser → `/forbidden`. A row in effect shows the F9 message instead of the form. |

Nav: a **Fees** item after Portfolios, visible to TenantAdmin and Adviser. Portal routes say `trade`; the API path is `/fees/trade-schedules`.

---

## Session

Unchanged. BFF cookie. Missing / dead session → authorize flow. Wrong role → `/forbidden`.

---

## Field bindings

### Schedule list (`GET /fees/trade-schedules?pageSize=100`)

One table per quote currency, currencies in code order, rows by `effectiveFrom` descending. More than 100 rows: page through the envelope until every row is loaded.

| Column | Source |
| --- | --- |
| Effective from | `effectiveFrom`, shown as the date with "UTC" (not converted to local time) |
| Rates | Bands as text, for example "0.5% to 10,000.00 · 0.3% above" |
| Minimum | `minimum` with the currency |
| Cap | `cap` with the currency, or "None" |
| State | F3 |

### State (computed in the page)

`today` is today's UTC date.

| State | When | Editable |
| --- | --- | --- |
| Applies today | The row with the latest `effectiveFrom` on or before `today` in its currency | No |
| Earlier | Any other row with `effectiveFrom` on or before `today` | No |
| Scheduled | `effectiveFrom` after `today` and `inEffect` false | Yes |
| Scheduled, locked | `effectiveFrom` after `today` and `inEffect` true (a future-dated trade already uses it) | No |

Edit and Delete are offered only when `inEffect` is false. The server decides; the state labels only explain it.

### Create / edit form

| Field | Binding | Rules shown in the form |
| --- | --- | --- |
| Quote currency | `quoteCurrency`, select from `GET /currencies?enabledOnly=true` | Create only. Read-only on edit. |
| Effective from | `effectiveFrom`, date picker | Earliest selectable date is tomorrow (UTC). Help text: F11. |
| Minimum | `minimum` | `≥ 0`. Decimal places of the currency. |
| Cap | `cap` | Optional. Empty sends `null`. `> 0` and `≥` minimum. |
| Bands | `bands[]` of `{ from, ratePercent }` | First row's "From" is fixed at 0 and read-only. Add band / Remove band; 1 to 10 rows; Remove is hidden on the first row. "From" ascending. Rate 0–100 %, up to 4 decimals. |
| — | `rowVersion` | Edit only, from the loaded item. |

The page checks these rules before sending, and still shows any server `errors` against the same fields.

### Fee preview (`POST /fees/trade-schedules/preview`)

A panel below the bands on the create and edit forms, and on the detail page.

| Item | Binding |
| --- | --- |
| Trade amounts | Up to 5 inputs. Prefilled with 1,000, 10,000 and 100,000 in the form's currency; the user may change, add or remove them. Sent as `amounts`. |
| Schedule | Form: the current form values (`quoteCurrency`, `minimum`, `cap`, `bands`). Detail: the loaded row's values. No `effectiveFrom`, no `rowVersion`. |
| Result | One row per amount: amount, fee, and "minimum applied" / "cap applied" from `limitApplied`. Expanding a row shows `bands`: from, rate, slice, band fee, and `banded`. |

Requests start 300 ms after the last change to the form or the amounts. A newer request replaces an older one; a late response from an older request is dropped. While the form fails its own checks, no request is sent and the panel shows the incomplete-form message. A 400 from the preview shows its `errors` under the matching form fields, the same as on submit.

### Fees on ledger pages

| Page | Change |
| --- | --- |
| `/transactions/:id` | Cash legs table gains a Role column. The page has no cash legs table yet (it shows the id, the type and the reverse button), so this cut adds the table: account, role, amount and currency per visible cash leg. A `Fee` leg shows its `fee` object below the amount: "Brokerage 10.00 NZD on 1,250.00 NZD", and a link to `/fees/trade/{fee.scheduleId}`. |
| `/transactions` | The Cash column keeps one entry per visible cash leg and labels a `Fee` leg "fee", for example "-1250.00 NZD, fee -10.00 NZD". |
| `/accounts/:id` activity | The Amount is the sum of the account's cash legs on that row (trade-fees R22). When the row has a `Fee` leg, a second line under the amount reads "incl. fee 10.00 NZD", in the same style as the rate line. |
| `Buy` / `Sell` form | One line of help text: "Brokerage is added by the server from the trade fee schedule." No fee field. |

---

## Acceptance (F)

| ID | Behaviour |
| --- | --- |
| F1 | TenantAdmin and Adviser see Fees in the nav. `/fees` redirects to `/fees/trade`. |
| F2 | `/fees/trade` lists `GET /fees/trade-schedules` grouped by quote currency with the columns above. Empty list shows the empty message. |
| F3 | Each row shows the state from the State table. |
| F4 | TenantAdmin sees New schedule. Adviser does not, and `/fees/trade/new` and `/fees/trade/:id/edit` render `/forbidden` for an Adviser. |
| F5 | Create posts `POST /fees/trade-schedules` without `tenantId`. 201 → `/fees/trade/{id}`. |
| F6 | The band editor keeps the first `from` at 0, allows 1 to 10 bands, and blocks submit while "From" is not ascending. |
| F7 | Detail of a pending row offers Edit and Delete to TenantAdmin. Detail of a row in effect offers neither and says why (message table). Adviser never sees Edit or Delete. |
| F8 | Edit sends `PUT /fees/trade-schedules/{id}` with `rowVersion` and without `quoteCurrency`. 204 → detail. 409 `stale_row_version` → reload message; the form does not resend the old version. |
| F9 | 409 `fee_schedule_in_effect` on edit or delete → the in-effect message, the item is fetched again, and Edit and Delete disappear. The page branches on `code`, not on 409 alone (api-design C8). |
| F10 | Delete asks for confirmation, then sends `DELETE /fees/trade-schedules/{id}`. 204 → back to `/fees/trade`. |
| F11 | The effective-from field explains UTC days: "Dates are UTC days. A schedule from 1 Dec applies to trades booked from 1 Dec 00:00 UTC (1 Dec 13:00 in New Zealand during daylight time)." |
| F12 | A 400 with `errors.EffectiveFrom` (not after the cut-off date, or a duplicate date) shows under the effective-from field. |
| F13 | Transaction detail, transactions list and account activity show fees as in "Fees on ledger pages". A transaction without a `Fee` leg looks as before. |
| F14 | Unknown or other-tenant schedule id → "Not found" on that URL, not 403. |
| F15 | Every call goes through the BFF `/api` prefix. No token storage in the SPA. |
| F16 | The create and edit forms show the preview for the current values. Changing a band, the minimum, the cap or an amount sends one preview request after 300 ms; an older response that arrives late does not overwrite a newer one. |
| F17 | The preview shows each amount's fee, "minimum applied" or "cap applied", and an expandable band breakdown, all from the response. The page does no fee arithmetic. |
| F18 | The detail page shows the preview for the stored row, for TenantAdmin and Adviser. |
| F19 | While the form fails its own checks, no preview request is sent. A preview 400 puts its `errors` under the form fields. |

---

## Tests

Portal tests assert the nav and route role gates, the state labels from fixed `effectiveFrom` / `inEffect` / today inputs, the band editor rules, the endpoints and bodies sent by create / edit / delete / preview, the preview debounce and late-response rule, the handling of both 409 codes, and the fee rendering on the three ledger pages. Do not re-prove the fee calculation, the cut-off date or tenant isolation here.

---

## Messages

| Situation | Message |
| --- | --- |
| No schedules | No trade fee schedules yet. Trades have no brokerage until a schedule starts. |
| Row in effect (detail, F7 / F9) | This schedule may already apply to posted trades, so it can't be changed. Add a new schedule with a later date instead. |
| Stale `rowVersion` | Someone else changed this schedule. Reload to see the latest version. |
| Delete confirmation | Delete this schedule? Trades from {effectiveFrom} will use the schedule before it, or have no brokerage if there is none. |
| "From" not ascending | Each band must start above the one before it. |
| Cap below minimum | The cap must be at least the minimum. |
| Too many bands | A schedule can have at most 10 bands. |
| Preview while the form is incomplete | Complete the bands, minimum and cap to see a preview. |
| Too many preview amounts | You can preview up to 5 amounts. |

---

## Still open

None.

## Locked decisions (2026-11-15)

- **Fee preview** is computed by `webapi` (trade-fees amendment R30–R33), not in TypeScript: one calculation for previews and posted trades, and C# `decimal` instead of JS floating point. Policy `fees.read`, so an Adviser can preview on the read-only detail page.
