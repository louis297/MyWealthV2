---
title: Trade fees
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-15
last_updated: 2026-11-15
related:
  - ../future-plans.md
  - ../glossary.md
  - ../api-design.md
  - ../database-design.md
  - ../domain-model.md
  - buy-sell.md
  - posting.md
  - holdings.md
  - leg-accounts.md
  - portfolios.md
  - reports.md
  - fx-conversions.md
  - cross-currency-trades.md
  - currencies.md
---

# Trade fees

Amendment to [buy-sell.md](buy-sell.md), [posting.md](posting.md), [holdings.md](holdings.md), [leg-accounts.md](leg-accounts.md), [portfolios.md](portfolios.md) and [reports.md](reports.md) on `webapi`. It adds a tenant **trade fee schedule** per quote currency (marginal fee bands, a minimum and an optional cap, effective-dated) and a server-computed **trade fee** on every `Buy` / `Sell`, booked as a second cash leg with role `Fee` and fee kind `Brokerage`. New resource `/fees/trade-schedules` under a new `/fees` namespace, new policies `fees.read` / `fees.manage`, script `0030_trade_fees.sql`. Portfolio rebalance becomes fee-aware.

This is step 1 of the business domain roadmap ([future-plans.md](../future-plans.md) §1). Decisions from discussion on 2026-11-15.

**Status is `accepted` (2026-11-15).** Not landed. The amendments to buy-sell, posting, holdings, leg-accounts, portfolios, reports, api-design, database-design, domain-model, the glossary and the portal page contracts are written in those files in the same change.

---

## 1. Summary

A TenantAdmin sets, per quote currency, a trade fee schedule: marginal bands of percentage rates, a minimum fee and an optional cap, effective from a date. When a `Buy` or `Sell` is posted, by hand or by rebalance, the server finds the schedule that applies on the trade's date, computes the fee from the gross amount, and books it as a `Fee` cash leg on the same Account. A `Buy` fee adds to the holding cost; a `Sell` fee only reduces the cash received. A schedule row that may already apply to a posted trade is never changed.

---

## 2. Scope

**In**

- Domain: `CashLegRole` (`Principal`, `Fee`) on `TransactionCashLeg`. `FeeKind` (`Brokerage`). `TradeFeeSchedule` aggregate with `FeeBands` value object and the fee calculation (R8). `TransactionFeeDetail` entity, one per `Fee` leg. `PostBuy` / `PostSell` take the applicable schedule and add the fee leg and detail. `Reverse` copies the role and the fee detail.
- Application: create / update / delete / list / get of trade fee schedules. `CreateTransaction` looks up the applicable schedule for `Buy` / `Sell`. Rebalance planner reads schedules and scales buys (R26–R29).
- HTTP: `/fees/trade-schedules`. Transaction item: `role` on every cash leg, `fee` object on every `Fee` leg.
- Reports: Customer Transactions Report shows the net cash and the fee (R22).
- Policies `fees.read` / `fees.manage`.
- Script `0030_trade_fees.sql`.
- Tests: domain, functional, two-tenant isolation. TestSeed adds one schedule.

**Out**

- A standalone `Fee` transaction type (cancellation fee, withdrawal fee, securities transfer-out fee, advisory fee). Designed with roadmap step 4.
- Fee kinds other than `Brokerage` (FX fee, exchange fee, advisory fee and others). The shape allows them (R18); each needs its own spec.
- Fees on any other type: `TransferIn`, `TransferOut`, `InternalTransfer`, `FxConversion` (no spread, no FX fee), `StockIn`, `StockOut`, `OpeningHoldings`, `Split`, `Bonus`, `Scrip`, cash `Opening`, `Interest`, `CloseOut`.
- Volume-based fee tiers (rate depends on traded volume over a period).
- Client-supplied fee amounts or a per-trade fee override.
- Platform default schedules inherited by tenants.
- Per-Customer, per-Account or per-portfolio rates for trade fees (decided 2026-11-15: brokerage is agreed per firm, not per model portfolio). Which schedule applies to which Account is a roadmap step 4 question for advisory fees.
- GST on fees.
- Realized gain reports. Lots.
- Order lifecycle (placed, filled, cancelled, settled).
- Adviser Portal pages (a later portal spec). Back Office pages: none planned for now; SystemAdmin uses the API (Scalar).
- Showing fees in the rebalance result.

---

## 3. Stories

1. As a TenantAdmin I set the brokerage for NZD trades to 0.5% with a minimum of NZD 10, starting next month, so that trades from then on carry that fee.
2. As a TenantAdmin I set marginal bands (0.5% up to 10,000, 0.3% above) so that large trades are cheaper per dollar, without a cliff at the band edge.
3. As a TenantAdmin I change a rate that has not started yet, or delete it, if I entered it wrongly.
4. As a TenantAdmin I stop charging in a currency by adding a zero schedule from a future date.
5. As an Adviser I see the fee schedules of my tenant but cannot change them.
6. As an Adviser I post a `Buy` and see the fee as its own cash leg, and the holding cost includes it.
7. As an Adviser I reverse a `Buy` booked in error and the fee comes back in full.
8. As an Adviser I assign a portfolio and the rebalance leaves enough cash for its fees instead of failing.
9. As a SystemAdmin I set a tenant's schedules through the API for support.

---

## 4. Rules

### 4.1 Access

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Policy fail → 403. TenantAdmin / Adviser: other-tenant or missing id → 404. SystemAdmin: missing id → 404; another tenant's id is allowed. |
| R2 | Path `{id}` and JSON `id` are `PublicId`. Internal ints never appear. |
| R3 | `fees.read`: TenantAdmin, Adviser, SystemAdmin. `fees.manage`: TenantAdmin, SystemAdmin. Customer has neither → 403 on every verb. Adviser write → 403. |
| R4 | TenantAdmin / Adviser: tenant = token `TenantId`; create and list must omit `tenantId` (400 if present). Current tenant disabled → 403 on every verb ([posting.md](posting.md) R4). SystemAdmin: create and list require `tenantId` (tenant PublicId); omitted → 400; unknown → 404; get / update / delete use the schedule PublicId and may cross tenants ([function-plan.md](../function-plan.md) §5.3). |

### 4.2 Trade fee schedule

| ID | Rule |
| --- | --- |
| R5 | **Shape.** `quoteCurrency`: an enabled catalog code. `effectiveFrom`: a date (no time). `minimum` `≥ 0`. `cap`: null, or `> 0` and `≥ minimum`. `bands`: 1 to 10 rows of `{ from, ratePercent }`; the first `from` is `0`; `from` strictly ascending; the last band has no upper end. `ratePercent` `≥ 0` and `≤ 100`, at most 4 decimals (`0.5` means 0.5 %). `minimum`, `cap` and every `from` have at most the quote currency's `DecimalPlaces` decimals ([currencies.md](currencies.md) R3). Violations → 400 `errors`. |
| R6 | At most one row per `(tenant, quoteCurrency, effectiveFrom)`. Duplicate → 400 `errors.EffectiveFrom`. |
| R7 | **Applicable schedule.** For a trade on an instrument with quote currency `Q` and instant `bookedAt`, the applicable schedule is the tenant's row for `Q` with the latest `effectiveFrom` on or before the UTC date of `bookedAt` ([utc-instants](../fixes/utc-instants.md)). No such row → the trade has no fee. |
| R8 | **Fee.** `gross` = `Round(quantity × price, 4, AwayFromZero)` in the quote currency. Banded amount = sum over bands of `ratePercent / 100 × (min(gross, next band's from) − from)`, counting only bands whose `from` is below `gross`; the last band has no upper end. `fee = Round(min(max(banded, minimum), cap ?? ∞), dp, AwayFromZero)`, where `dp` is the quote currency's `DecimalPlaces`. `fee` is non-decreasing in `gross`. |
| R9 | **Cut-off date.** For `(tenant, Q)`, the cut-off date is the later of today (UTC) and the latest UTC date of `bookedAt` of any `Buy` / `Sell` in the tenant on an instrument quoted in `Q`. Reversals do not count. A row is **in effect** when its `effectiveFrom` is on or before the cut-off date; otherwise it is **pending**. |
| R10 | **Create.** `effectiveFrom` must be after the cut-off date. Otherwise → 400 `errors.EffectiveFrom`. |
| R11 | **Update and delete.** Only a pending row may be updated (`PUT` with `rowVersion`) or deleted. A row in effect → 409 `fee_schedule_in_effect`. An update's new `effectiveFrom` must also be after the cut-off date (R10). `quoteCurrency` is immutable; to change it, delete and create. Update replaces the whole band set. |
| R12 | **Invariant.** No schedule write changes which row applies to a posted `Buy` / `Sell`, or the values of that row. R9–R11 exist to hold this. |
| R13 | **Stop charging.** A row with one band `{ from: 0, ratePercent: 0 }`, `minimum: 0` and no cap. There is no active flag. |

### 4.3 Fee on Buy / Sell

| ID | Rule |
| --- | --- |
| R14 | Every `Buy` / `Sell`, from `POST /transactions` or from rebalance, looks up the applicable schedule (R7). When one exists and `fee > 0`, the Transaction gets one cash leg with role `Fee` on the same Account as the principal cash leg, with a fee detail of kind `Brokerage` (R18). When there is no schedule or `fee = 0`, it gets neither. At most one `Brokerage` fee per Transaction. The create body does not change; a `fee` field in it → 400. |
| R15 | The principal cash leg keeps role `Principal` and today's amount ([buy-sell.md](buy-sell.md) R12). The fee cash leg is `−Round(fee × rate)` in the Account currency, with the same rate and the same rounding as the principal leg: 4 decimals today, the Account currency's `DecimalPlaces` once [cross-currency-trades.md](cross-currency-trades.md) R9 is accepted. Same currency → rate 1. The fee leg is negative on both `Buy` and `Sell`. |
| R16 | **Cost.** `Buy` security-leg cost = `gross + fee` in the quote currency. `Sell` release is unchanged ([buy-sell.md](buy-sell.md) R15); the fee only reduces cash. |
| R17 | **Cash check.** Cash `SUM` after all legs `≥ 0` ([buy-sell.md](buy-sell.md) R13). A `Sell` whose fee exceeds its principal is allowed when the check holds. |
| R18 | **Fee detail.** Every `Fee` cash leg has exactly one `TransactionFeeDetails` row, and no other leg has one: fee kind, the schedule used, the basis amount (`gross` for `Brokerage`), `fee` and the quote currency. At most one fee per `(Transaction, kind)`. A `Principal` leg stays at most one per `(Transaction, Account)`. Written in the same unit of work. Never updated. A Transaction may carry several `Fee` legs of different kinds; this slice only creates `Brokerage`. |
| R19 | **Reverse.** Reverse of a `Buy` / `Sell` copies every cash leg with its role and the opposite amount, and copies each `Fee` leg's fee detail onto the matching new leg. The schedule is not read again; the fee is refunded in full. A broker charge for cancelling a trade is a standalone fee (out of scope). |
| R20 | **Other types** never have a `Fee` leg. The Domain refuses it. |
| R21 | **Existing rows.** Posted cash legs become role `Principal`. Posted `Buy` / `Sell` have no `Fee` leg. No back-fill of fees. |

### 4.4 Readers

| ID | Rule |
| --- | --- |
| R22 | A reader that lists legs shows each cash leg with its `role`. A reader that shows one cash amount per `(Transaction, Account)` shows the sum of that Account's cash legs. Customer Transactions Report: the cash amount column is that sum, and a new Fee column shows the `Fee` legs' sum (empty when none). |
| R23 | Cash balance, net worth, close and the Adviser Balances Report are unchanged: they already sum every cash leg. |

### 4.5 Rebalance

| ID | Rule |
| --- | --- |
| R24 | The portfolio trade lock ([portfolios.md](portfolios.md) R18) is unchanged. |
| R25 | The planner reads, per quote currency of every planned trade, the schedule that applies on the rebalance's UTC date (R7), and computes each trade's fee (R8) and fee cash (R15). |
| R26 | Targets, investable value, the order of sells before buys and the sells themselves are unchanged ([portfolios.md](portfolios.md) R8). Planned cash = cash + each sell's principal − its fee − each buy's principal − its fee. The cash residual absorbs fees first. |
| R27 | **Buy scaling.** If planned cash `< 0`, every planned buy quantity is scaled by one factor `k`: `quantity(k) = Round(k × quantity, 8, AwayFromZero)`, fees recomputed. `k` is the largest value in `[0, 1)` with planned cash `≥ 0`, found by bisection: start with `lo = 0`, `hi = 1`, run 40 steps, keep `lo`. Bisection is valid because planned cash is non-increasing in `k` (R8 and every rounding step are non-decreasing). |
| R28 | After scaling, a buy whose quantity is 0 or whose base-currency value is below the minimum trade size is dropped. Dropping only raises planned cash. |
| R29 | If planned cash is `< 0` even at `k = 0` (sell fees larger than cash plus sell principals), the rebalance fails with `insufficient_cash` ([portfolios.md](portfolios.md) R11). If no trade remains, the result is Success (NoChange). |

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `CashLegRole` | Enum | `Principal` = 0, `Fee` = 1. |
| `TransactionCashLeg` | Entity (existing) | Adds `Role`. `Principal`: at most one per `(Transaction, Account)`. `Fee`: carries one `TransactionFeeDetail`. |
| `FeeKind` | Enum | `Brokerage` = 0. Later kinds are added by their own specs. |
| `TradeFeeSchedule` | Aggregate | Tenant, quote currency (immutable), `EffectiveFrom`, `Minimum`, `Cap`, `FeeBands`. `FeeFor(gross, decimalPlaces)` implements R8. Update and delete take the cut-off date and refuse a row in effect. |
| `FeeBands` | Value object | Ordered `(From, RatePercent)` pairs; R5 shape. `Apply(amount)` returns the banded amount. Written to be reused by advisory fees (roadmap step 4). |
| `TransactionFeeDetail` | Entity | Exactly one per `Fee` cash leg. Kind, schedule id, basis amount, fee, currency. At most one per `(Transaction, kind)`. Copied to a Reversal's matching leg. |
| `Transaction` | Aggregate (existing) | `PostBuy` / `PostSell` take an optional `TradeFeeSchedule` and the quote currency's decimal places. `Reverse` copies roles and the fee detail. |

Invariants:

- A cash leg has a fee detail if and only if its role is `Fee`. In this slice `Fee` legs appear only on `Buy`, `Sell`, or a Reversal of one.
- A fee detail's `fee` converted with the trade's rate (R15) equals the opposite of its leg's amount.
- On a `Buy`, security-leg cost = `Brokerage` basis amount + fee.
- A posted schedule row in effect never changes (R12).

Domain events: existing `TransactionPosted` / `TransactionReversed`. No schedule events.

Update [domain-model.md](../domain-model.md) when this spec is accepted.

---

## 6. Database

| Table | Change | Index / FK |
| --- | --- | --- |
| `TransactionCashLegs` | alter: add `Role int NOT NULL` default 0, CHECK 0–1 | Drop `UQ_TransactionCashLegs_TransactionId_AccountId`; add filtered unique index `(TransactionId, AccountId) WHERE Role = 0`; add UQ `(TenantId, Id)` for the fee detail FK |
| `TradeFeeSchedules` | add: `Id`, `PublicId`, `TenantId`, `QuoteCurrency char(3)`, `EffectiveFrom date`, `MinimumAmount decimal(18,4)`, `CapAmount decimal(18,4) NULL`, audit columns, `RowVersion` | UQ `PublicId`; UQ `(TenantId, QuoteCurrency, EffectiveFrom)`; FK Tenants, Currencies; CHECK minimum `≥ 0`; CHECK cap null or (`> 0` and `≥` minimum) |
| `TradeFeeBands` | add: `Id`, `TenantId`, `TradeFeeScheduleId`, `FromAmount decimal(18,4)`, `RatePercent decimal(7,4)` | UQ `(TradeFeeScheduleId, FromAmount)`; FK `(TenantId, TradeFeeScheduleId)` → schedule, as in `0028_tenant_composite_keys.sql`; CHECK from `≥ 0`; CHECK rate 0–100 |
| `TransactionFeeDetails` | add: `CashLegId` (PK), `TenantId`, `TransactionId`, `Kind int`, `TradeFeeScheduleId NULL`, `BasisAmount decimal(18,4)`, `FeeAmount decimal(18,4)`, `Currency char(3)` | FK `(TenantId, CashLegId)` → TransactionCashLegs; FK `(TenantId, TransactionId)` → Transactions; UQ `(TransactionId, Kind)`; FK schedule (RESTRICT); FK Currencies; CHECK `Kind` 0–0; CHECK `Kind <> 0 OR TradeFeeScheduleId IS NOT NULL`; CHECK basis `> 0`, fee `> 0`. No `PublicId`, no `RowVersion` (part of an immutable Transaction, like `TransactionFxDetails`). Later kinds widen the `Kind` check and add their own schedule column |

Script: `database/schema/0030_trade_fees.sql`. Existing cash legs get role 0 from the default. That the fee detail's `TransactionId` matches its leg's, and that only `Fee` legs have one, is enforced in Domain. Do not add EF migrations. Update [database-design.md](../database-design.md) when this spec is accepted.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreateTradeFeeSchedule` | schedule `PublicId` | R3–R6, R10 |
| Command | `UpdateTradeFeeSchedule` | — | R3–R6, R11; `rowVersion` |
| Command | `DeleteTradeFeeSchedule` | — | R3, R4, R11 |
| Query | `GetTradeFeeSchedules` | list envelope | R3, R4; optional `quoteCurrency` filter |
| Query | `GetTradeFeeSchedule` | item | R3, R4 |
| Command | `CreateTransaction` (extend) | Transaction `PublicId` | `Buy` / `Sell`: R7, R14–R18 |
| Command | `ReverseTransaction` (extend) | new Transaction `PublicId` | R19 |
| Command | rebalance (extend) | rebalance result | R25–R29 |

The cut-off date (R9) is computed in Application from posted `Buy` / `Sell` legs and instruments.

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/fees/trade-schedules` | `fees.read` | 200 list envelope | 400 / 401 / 403 / 404 |
| GET | `/fees/trade-schedules/{id}` | `fees.read` | 200 item | 401 / 403 / 404 |
| POST | `/fees/trade-schedules` | `fees.manage` | 201 `{ "id" }` | 400 / 401 / 403 / 404 |
| PUT | `/fees/trade-schedules/{id}` | `fees.manage` | 204 | 400 / 401 / 403 / 404 / 409 |
| DELETE | `/fees/trade-schedules/{id}` | `fees.manage` | 204 | 401 / 403 / 404 / 409 |

`/fees` is a namespace prefix, like `/users` ([api-design.md](../api-design.md) §2): there is no `GET /fees`. Fee configuration and calculation resources live under it (later `/fees/advisory-schedules`). Fee **transactions** do not: a standalone fee type gets its own `POST /transactions/{action}` route (api-design §7.7). The Adviser Portal BFF allow-list adds the `/fees` prefix with the portal spec.

List: optional `quoteCurrency`; SystemAdmin adds `tenantId`. Sort `quoteCurrency`, then `effectiveFrom`. Envelope `{ items, page, pageSize, totalCount }`; page default 1, size default 20, max 100. No `Idempotency-Key` (configuration, not a ledger write).

`POST /transactions` and `POST /transactions/{id}/reverse` keep their routes and bodies.

### 8.1 Bodies

Schedule create (SystemAdmin adds `tenantId`):

```json
{
  "quoteCurrency": "NZD",
  "effectiveFrom": "2026-12-01",
  "minimum": 10.00,
  "cap": null,
  "bands": [
    { "from": 0, "ratePercent": 0.5 },
    { "from": 10000.00, "ratePercent": 0.3 }
  ]
}
```

Update: the same fields without `quoteCurrency`, plus `rowVersion`.

Schedule item:

```json
{
  "id": "<schedule publicId>",
  "quoteCurrency": "NZD",
  "effectiveFrom": "2026-12-01",
  "minimum": 10.00,
  "cap": null,
  "bands": [
    { "from": 0, "ratePercent": 0.5 },
    { "from": 10000.00, "ratePercent": 0.3 }
  ],
  "inEffect": false,
  "rowVersion": "<base64>"
}
```

`Buy` item (same currency, 50 × 25.00, fee 10.00). Other item fields are unchanged ([leg-accounts.md](leg-accounts.md) §8.1, [fx-conversions.md](fx-conversions.md) `fx`):

```json
{
  "id": "<transaction publicId>",
  "type": "Buy",
  "bookedAt": "2026-12-02T21:00:00Z",
  "cash": [
    {
      "accountId": "<account publicId>",
      "customerId": "<customer publicId>",
      "role": "Principal",
      "amount": -1250.00,
      "currency": "NZD",
      "fee": null
    },
    {
      "accountId": "<account publicId>",
      "customerId": "<customer publicId>",
      "role": "Fee",
      "amount": -10.00,
      "currency": "NZD",
      "fee": {
        "kind": "Brokerage",
        "scheduleId": "<schedule publicId>",
        "basisAmount": 1250.00,
        "amount": 10.00,
        "currency": "NZD"
      }
    }
  ],
  "security": [
    {
      "accountId": "<account publicId>",
      "customerId": "<customer publicId>",
      "instrumentId": "<instrument publicId>",
      "quantity": 50,
      "cost": 1260.00,
      "costCurrency": "NZD"
    }
  ]
}
```

Every cash leg on every type carries `role` and `fee`. `fee` is the leg's fee detail on a `Fee` leg and null on a `Principal` leg. `fee.amount` is in the quote currency; the leg `amount` is in the Account currency.

### 8.2 Errors

Ordinary validation stays in `errors`: R5 shape, R6 duplicate, R10 / R11 `effectiveFrom` not after the cut-off date, `tenantId` present or missing (R4), `fee` field on a create body (R14).

New problem code, added to the registry in [api-design.md](../api-design.md) §4.2 when this spec is accepted:

| `code` | Status | Meaning | Extra fields |
| --- | --- | --- | --- |
| `fee_schedule_in_effect` | 409 | The schedule row may already apply to a posted trade, so it cannot be updated or deleted. Add a new row instead. | — |

A stale `rowVersion` on update → 409 `stale_row_version`. Pages that call `PUT /fees/trade-schedules/{id}` branch on `code` (api-design C8). Cross-tenant or missing id → 404.

Add the `/fees` namespace and `/fees/trade-schedules` to the resource catalog in [api-design.md](../api-design.md) §6 when this spec is accepted.

---

## 9. UI

None in this slice.

- Adviser Portal: a later portal spec under [adviser-portal/](../adviser-portal/). A settings page where TenantAdmin manages schedules and Adviser reads them, and fee legs on the activity rows. Until then, activity pages that show one amount per row show the sum (R22).
- Back Office: none planned for now (decided 2026-11-15). SystemAdmin uses the API through Scalar.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | R8 worked examples: single band; two bands with `gross` below, on and above the edge; minimum binds; cap binds; rounding for `DecimalPlaces` 2 and 0; fee non-decreasing across a band edge. R5 shape refusals. `Buy` cost = gross + fee; `Sell` release unchanged; fee leg negative on both; cash check includes the fee; no fee leg when fee is 0 or no schedule. Reverse copies roles and fee detail. Other types refuse a `Fee` leg. Pending vs in effect for update / delete. Buy scaling: largest feasible `k`; dropped buys below minimum trade; `insufficient_cash` at `k = 0`; no scaling and no change when fees fit in the residual. |
| Application.FunctionalTests | No Bearer → 401 not 302; Customer → 403 on every verb; Adviser write → 403, read 200; TenantAdmin create / update / delete pending; update or delete in effect → 409 `fee_schedule_in_effect`; `effectiveFrom` not after the cut-off date → 400 (today, and after a future-dated `Buy`); duplicate date → 400; stale `rowVersion` → 409 `stale_row_version`; SystemAdmin without `tenantId` → 400. `Buy` / `Sell` with a schedule show the `Fee` leg with its `fee` object and the cost; the schedule that applies is the one for the trade's UTC date; reverse refunds the fee; `fee` field on a create body → 400; a cross-currency `Sell` converts the fee with the principal's rate. Rebalance with fees succeeds where the plain plan would go negative. Customer Transactions Report shows the net cash and the Fee column. |
| Infrastructure.IntegrationTests | Two-tenant fixture: other tenant's schedule id → 404 on get / update / delete; a trade uses only its own tenant's schedules. Script: existing cash legs read as `Principal`; a second `Principal` leg on the same `(Transaction, Account)` is refused; a `Principal` and a `Fee` leg on the same Account are allowed; a second fee of the same kind on one Transaction is refused. TestSeed idempotent. |

TestSeed (Development / TestAppHost only): one schedule for the demo brokerage instruments' quote currency, effective the day after the cut-off date at seed time (so it obeys R10), with a minimum and two bands.

---

## 11. Locked in this spec

No new ADR. Decisions 2026-11-15.

| Item | Lock |
| --- | --- |
| Where the fee lives | A cash leg with role `Fee`, same Account as the principal, with one fee detail per `Fee` leg. Not a separate Transaction, not a net amount plus a side table only. |
| Fee kinds | Coarse leg role (`Principal`, `Fee`, later `Tax`); the kind lives on the fee detail. One fee per `(Transaction, kind)`. Only `Brokerage` in this slice. Each fee kind has its own schedule shape; they share `FeeBands`, not one generic schedule table. |
| Fee source | Server-computed from the tenant schedule. No client amount, no override. |
| Schedule level | Tenant, per quote currency. No platform default. No per-portfolio, per-Account or per-Customer rate. |
| Bands | Marginal. Not whole-amount bands (cliff at the edge, breaks the rebalance bisection). Not volume-based. |
| Effective dating | Append-only in effect; pending rows editable; cut-off date R9; stop charging with a zero row. |
| Cost | `Buy` fee capitalised into cost. `Sell` fee reduces cash only. |
| Currency | Fee computed and rounded in the quote currency, converted with the principal's rate and rounding. |
| Reverse | Full refund, copied, not recomputed. |
| Types | `Buy` and `Sell` only. |
| Rebalance | Fees from the residual first; then every buy scaled by one factor `k` (bisection); buys below minimum trade dropped. |
| Callers | TenantAdmin manages; Adviser reads; SystemAdmin manages through the API; Customer none. |
| Routes | `/fees/trade-schedules` under the `/fees` namespace prefix. Fee transactions stay under `/transactions`. |
| Scripts / policy | `0030_trade_fees.sql`. `fees.read` / `fees.manage`. |

Still open, not this slice:

- Showing planned and posted fees in the rebalance result and on the Adviser Portal.
- A warning when a minimum fee is a large share of the portfolio's minimum trade size (minimum trade 100 with a minimum fee 10 is a 10 % cost).
- Concurrency between a schedule write and a trade posted on a future date at the same moment (R9 reads posted trades; schedule writes do not take the Account lock).
- Whether brokerage is part of the cost base for the FIF CV method, and GST on brokerage. Check against IRD when roadmap step 6 opens. The fee detail keeps gross and fee apart, so either answer can be read from stored data.
- Fees shown on the Customer Portal.

---

## 12. Suggested commits

The repository should build after each commit. Agents split each line into red then green.

1. Domain: `FeeBands` and `TradeFeeSchedule.FeeFor` (R5, R8) with unit tests.
2. Script `0030_trade_fees.sql`; schedule commands and queries; policies; pending vs in effect and the cut-off date (R3–R13); isolation tests.
3. `Buy` / `Sell` fee leg, cost, fee detail and cash check (R14–R18, R20, R21).
4. Reverse copies roles and fee detail (R19).
5. Item `role` and `fee`; Customer Transactions Report Fee column (R22, R23).
6. Fee-aware rebalance and buy scaling (R24–R29).
7. TestSeed schedule. Amend buy-sell, posting, holdings, leg-accounts, portfolios, reports, api-design, database-design and domain-model in the change that accepts this file.

---

## Amendment 2026-11-15 (fee preview) — draft

Status of this amendment: `draft`. The rest of this file stays accepted. It adds a read-only route that computes trade fees for a schedule that is not saved, so the Adviser Portal form can show the fee without repeating R8 in TypeScript ([adviser-portal-fees.md](../adviser-portal/adviser-portal-fees.md)). Same pattern as `POST /transactions/fx/quote`: a POST that computes and writes nothing.

| ID | Rule |
| --- | --- |
| R30 | `POST /fees/trade-schedules/preview`, policy `fees.read` (TenantAdmin, Adviser, SystemAdmin). Customer → 403. Current tenant disabled → 403 (R4). The body has no `tenantId` for any caller (400 if present): the route reads no tenant data. No `Idempotency-Key`. 200 on success. |
| R31 | Body: `quoteCurrency`, `minimum`, `cap`, `bands` with the R5 shape and the same `errors` field names, plus `amounts`: 1 to 5 values, each `> 0` with at most 4 decimals. `effectiveFrom` is not part of the body. Violations → 400 `errors`. |
| R32 | For each amount, the response uses the same Domain calculation as a posted trade (R8, `TradeFeeSchedule.FeeFor`), not a second implementation. Per amount: `amount`, `bands` (only bands whose `from` is below the amount: `from`, `ratePercent`, `slice` = the part of the amount inside the band, `fee` = `ratePercent / 100 × slice`), `banded` (sum of the band fees), `limitApplied` (`"minimum"`, `"cap"` or `null`) and `fee`. `slice`, band `fee` and `banded` are exact, not rounded; only `fee` is rounded (R8). |
| R33 | The route writes nothing, reads no schedule row and no trade, and does not look at the cut-off date. Its result is not a quote for any trade. |

Domain: `FeeFor` returns a `FeeCalculation` value object (band slices, banded amount, limit applied, fee). Posted trades keep only the fee and the basis amount (R18); the preview returns the whole object.

Routing: `{id}` routes under `/fees/trade-schedules` take a GUID only, so `preview` is never read as an id.

Request and response:

```json
{
  "quoteCurrency": "NZD",
  "minimum": 10.00,
  "cap": null,
  "bands": [
    { "from": 0, "ratePercent": 0.5 },
    { "from": 10000.00, "ratePercent": 0.3 }
  ],
  "amounts": [1000.00, 15000.00]
}
```

```json
{
  "currency": "NZD",
  "items": [
    {
      "amount": 1000.00,
      "bands": [{ "from": 0, "ratePercent": 0.5, "slice": 1000.00, "fee": 5.00 }],
      "banded": 5.00,
      "limitApplied": "minimum",
      "fee": 10.00
    },
    {
      "amount": 15000.00,
      "bands": [
        { "from": 0, "ratePercent": 0.5, "slice": 10000.00, "fee": 50.00 },
        { "from": 10000.00, "ratePercent": 0.3, "slice": 5000.00, "fee": 15.00 }
      ],
      "banded": 65.00,
      "limitApplied": null,
      "fee": 65.00
    }
  ]
}
```

Tests:

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `FeeCalculation` for the R8 worked examples: band slices sum to the amount; band fees sum to `banded`; `limitApplied` for minimum, cap and neither; `fee` equals the fee a posted trade gets for the same gross. |
| Application.FunctionalTests | No Bearer → 401 not 302; Customer → 403; Adviser and TenantAdmin → 200; `tenantId` in the body → 400; R5 shape errors → 400 with the same field names as create; `amounts` empty, more than 5, `≤ 0` or with 5 decimals → 400; no row is written. |

Suggested commit: `FeeCalculation` and the preview query with tests (R30–R33), after commit 1 of §12. [api-design.md](../api-design.md) §7.9 lists the route marked as a draft amendment; drop that mark when this amendment is accepted.
