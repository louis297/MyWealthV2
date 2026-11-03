---
title: Portfolios
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-02
last_updated: 2026-11-03
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../architecture.md
  - ../glossary.md
  - ../adr/0004-money-as-decimal-with-currency.md
  - ../adr/0010-instrument-catalog.md
  - ../adr/0011-ledger-cash-holdings-reversal.md
  - ../adr/0015-idempotency-keys.md
  - ../adr/0019-fx-rate-source-and-refresh.md
  - instruments.md
  - accounts.md
  - holdings.md
  - buy-sell.md
  - fx-rates.md
  - instrument-prices.md
  - posting.md
---

# Portfolios

Model portfolios (target-weight investment schemes) on `webapi`. An Adviser or TenantAdmin defines a portfolio of instruments with target weights and a mandatory base currency. An Adviser assigns one portfolio to an Open Brokerage or Other account. Assignment and any later weight change run a **Full Rebalance**: sell every holding not in the target, then buy/sell to the target weights. All trades are ordinary `Buy` / `Sell` transactions (existing model). Customer cannot assign or rebalance. No new `Account.Type`.

**Status is `accepted`.** Implementation follows this file.

---

## 1. Summary

A TenantAdmin or Adviser creates a **Portfolio** (name, base currency, lines of instrument + target weight) in the current tenant. SystemAdmin does the same for a chosen tenant. An Adviser assigns at most one Active portfolio to an Open Brokerage or Other account of an assigned / same-tenant Customer. Assign runs a synchronous Full Rebalance for that one account. A weight change, and `POST /portfolios/{id}/rebalance`, enqueue an asynchronous Full Rebalance for every Active assignment. Rebalance generates ordinary `Buy` / `Sell` transactions under the existing Account lock. Missing price, missing FX pair, or insufficient cash fails the entire rebalance for that account (no partial writes). While an account has an Active assignment, manual security trades (`Buy` / `Sell` / `StockIn` / `StockOut` / `Split` / `Bonus` / `Scrip` / `OpeningHoldings`, and reverse of those types) are rejected; only rebalance and unassign may change holdings. Cash transfers in and out remain allowed. A later rebalance invests remaining cash (after the configured residual). Customer receives 403 on every write.

---

## 2. Scope

**In**

- Domain: `Portfolio`, `PortfolioLine`, `AccountPortfolioAssignment`
- Application: create / update / disable portfolio; assign / unassign; rebalance (auto on weight change + manual); queries
- Script `database/schema/0023_portfolios.sql` + EF mapping
- Policies: `portfolios.read` / `portfolios.manage` (new); assignment and rebalance reuse `transactions.create` + account visibility
- HTTP: `/portfolios`, `/portfolios/{id}`, `POST /portfolios/{id}/disable`, `/accounts/{id}/portfolio`, `/accounts/{id}/rebalance`, `/portfolios/{id}/rebalance`
- Full Rebalance semantics (sell everything outside the target, then align)
- While assigned: reject manual security-changing transactions; allow cash in/out
- Async rebalance (Hangfire) on weight change and on bulk rebalance
- Reuse `IMarketData` and `IFxRate` (local tables only; no live vendor calls)
- Tests + Development / TestAppHost `TestSeed` (one demo portfolio + one assignment)

**Out**

- Customer self-assign or self-rebalance
- Overlay / partial rebalance (keep holdings outside the portfolio)
- Multiple Active portfolios on one account
- New `Account.Type` for portfolios
- Scheduled / drift-triggered automatic rebalance (only weight-change and explicit manual in this slice)
- Fees, tax lots, performance attribution
- Portal pages: accepted contract is [../adviser-portal/adviser-portal-portfolios.md](../adviser-portal/adviser-portal-portfolios.md)
- Live prices or live FX
- Idempotency key on the rebalance command itself (individual transactions still follow existing idempotency rules)

---

## 3. Stories

1. As an Adviser I create a portfolio with a base currency and target weights so I can offer a model scheme.
2. As an Adviser I assign a portfolio to an Open Brokerage or Other account so the account is fully aligned to that model.
3. As an Adviser I change the weights of a portfolio and every currently assigned account is immediately rebalanced.
4. As an Adviser I manually rebalance an account (or all accounts on a portfolio) when an earlier automatic rebalance failed.
5. As an Adviser I see why a rebalance failed (missing price, missing FX, insufficient cash) without any partial trades having been written.
6. As a TenantAdmin I manage portfolios for the whole firm.
7. As a Customer I receive 403 on every portfolio write.
8. As an Adviser I cannot assign a portfolio to a Bank or Cash account, or to an account I cannot see.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Wrong role → 403. |
| R2 | Path `{id}` and JSON `id` are PublicId. Internal ints never appear. |
| R3 | Customer receives 403 on every portfolio write (create, update, assign, unassign, rebalance). |
| R4 | Only Open accounts of type **Brokerage** or **Other** may be assigned a portfolio. Bank, Cash, Closed → 400. |
| R5 | An account may have at most one Active `AccountPortfolioAssignment`. Assigning a new portfolio first marks the previous Active assignment `Removed`. |
| R6 | Portfolio has a mandatory `BaseCurrency` (enabled catalog code). Immutable after create. |
| R7 | Portfolio lines: each `TargetWeight` > 0 and ≤ 100; sum of weights = 100 (exact, after rounding to a locked precision — see open items). Instrument must be active and belong to the same tenant. |
| R8 | **Full Rebalance** on assign and on weight change: (a) sell every current holding whose instrument is not in the target lines, in full, including dust below the minimum trade; (b) then buy/sell so that each target instrument’s market value in base currency equals `(weight / 100) × V × (1 − residual)`. `V` is cash plus holdings market value, converted into the portfolio base currency. Once rebalance has started, only residual and minimum trade apply to step (b). Drift is not re-checked per line. |
| R9 | All trades are ordinary `Buy` / `Sell` transactions under the existing Account lock. No direct Holding edits. |
| R10 | Valuation uses the current `IMarketData` price and `IFxRate` `quote → base` (identity when they match). The cash leg reuses ordinary Buy/Sell, which converts `quote → account currency`. Missing price or either FX pair → the whole rebalance for that account fails (400 or recorded failure; no transactions written). |
| R11 | Insufficient cash (after planned sells) to fund the net buys → whole rebalance fails. No partial execution. |
| R12 | Weight change on an Active portfolio enqueues / triggers rebalance for every currently Active assignment. Manual rebalance endpoints are also provided. |
| R13 | Automatic rebalance failure does **not** auto-retry. Assignment stays Active; `LastRebalanceStatus = Failed` and a message are stored. Adviser may retry manually. |
| R14 | Drift tolerance, minimum trade size, and cash residual are portfolio-level settings with locked defaults (R21). Drift is used only to decide whether an automatic rebalance should start (deferred in this slice). Once any rebalance has started, individual lines are not skipped for being inside the drift band. Trades below the minimum trade size are skipped. A rebalance that results in zero trades is Success (NoChange). |
| R15 | Unassign marks the assignment `Removed`. No automatic trades on unassign. The account becomes a free-holdings account again. |
| R16 | Cross-tenant or wrong-collection id → 404, not 403. Adviser sees only assigned customers. |
| R17 | SystemAdmin supplies `tenantId` on create/list (same pattern as other Phase-2 resources). |
| R18 | **Assigned-account trade lock.** While an account has an Active `AccountPortfolioAssignment`, any manual security-changing transaction (`Buy`, `Sell`, `StockIn`, `StockOut`, `Split`, `Bonus`, `Scrip`, `OpeningHoldings`) and a reverse of those types is rejected (400). Only the rebalance command and unassign may change holdings. Cash types remain allowed: `TransferIn`, `TransferOut`, `Interest`, `CloseOut`, `Opening`, and reverse of those. |
| R19 | After a cash transfer in, a subsequent rebalance treats all cash above the configured residual as investable and buys to target weights. |
| R20 | Target weights are stored with 2 decimal places. On create/update the last line absorbs any rounding difference so the sum is exactly 100.00. |
| R21 | Drift tolerance default: absolute 1% or relative 5% (whichever is more permissive). Used only for future automatic-trigger decisions; not applied during rebalance execution. Minimum trade size default: 100 in portfolio BaseCurrency. Cash residual default: 1% of total investable value in the portfolio BaseCurrency (cash + holdings market value, all converted; or 0 if explicitly set to 0). These are portfolio-level settings. |
| R22 | Disabling a portfolio unassigns every Active assignment (Status → Removed) and leaves existing holdings untouched. No automatic trades on disable. |
| R23 | Weight-change and bulk rebalance are always asynchronous (Hangfire). The HTTP call returns **202 Accepted** with both a job id and a per-account handle for every targeted assignment. Unchanged lines, or a weight change with no Active assignment, return **204**. Assign and single-account manual rebalance are synchronous and return the result directly. |
| R24 | A rebalance that finds the account already aligned (after residual and minimum-trade filters) is Success (NoChange) and is skipped on later runs of the same trigger. No idempotency key is required on the rebalance command itself. |
| R25 | Assign and weight-change rebalance require only a normal authenticated call. Extra confirmation is a frontend concern (Adviser clicks Confirm); the API does not require a separate confirm flag. |

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `Portfolio` | Aggregate | Tenant-scoped. Name, BaseCurrency (immutable), IsActive, drift/min-trade/residual settings. |
| `PortfolioLine` | Entity | InstrumentId + TargetWeight. Owned by Portfolio. |
| `AccountPortfolioAssignment` | Entity | AccountId, PortfolioId, Status (Active / Removed), AssignedAt, AssignedBy, LastRebalanceAt, LastRebalanceStatus, LastRebalanceMessage. |
| `PortfolioAssigned` / `PortfolioUnassigned` / `PortfolioWeightsChanged` | Event | Raised on the corresponding commands. Weight change is the trigger for auto-rebalance. |

Invariants:

- BaseCurrency immutable after create.
- At most one Active assignment per Account.
- Weights sum to 100 on a valid portfolio.
- Rebalance never writes a partial set of transactions for one account.

Domain events:

- `PortfolioCreated`, `PortfolioWeightsChanged`, `PortfolioAssigned`, `PortfolioUnassigned`

Update [domain-model.md](../domain-model.md) in the same change.

---

## 6. Database

| Table | Change | Index / FK |
| --- | --- | --- |
| `Portfolios` | add | PK, unique PublicId, FK TenantId, FK BaseCurrency → Currencies. Settings columns for drift, minimum trade, and cash residual. |
| `PortfolioLines` | add | PK, FK PortfolioId, FK InstrumentId, unique (PortfolioId, InstrumentId), `SortOrder` (request order; last line absorbs rounding). No TenantId; loaded with the portfolio. |
| `AccountPortfolioAssignments` | add | PK, TenantId, FK AccountId, FK PortfolioId, filtered unique index on AccountId where Status = Active |

Script: `database/schema/0023_portfolios.sql`.

Update [database-design.md](../database-design.md) in the same change. Do not add EF migrations.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreatePortfolio` | Portfolio id | Role, tenant, base currency enabled, weights sum 100, instruments active |
| Command | `UpdatePortfolioWeights` | 204, or 202 when Active assignments exist and lines changed | Same + enqueues rebalance when lines change |
| Command | `DisablePortfolio` | — | Marks inactive and unassigns every Active assignment. No trades. |
| Command | `AssignPortfolio` | Rebalance result | Account type/status, at most one Active, then synchronous Full Rebalance. Same portfolio again → 409. |
| Command | `UnassignPortfolio` | — | Marks Removed; no trades |
| Command | `RebalanceAccount` | Rebalance result | Manual; synchronous Full Rebalance |
| Command | `RebalancePortfolio` | 202 job id + handles | Manual; enqueues every Active assignment |
| Query | `GetPortfolio` / `ListPortfolios` | Portfolio + lines | Isolation |
| Query | `GetAccountPortfolio` | Current assignment + last status | Account visibility |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| POST | `/portfolios` | `portfolios.manage` | 201 `{ "id" }` | 400 validation |
| GET | `/portfolios` | `portfolios.read` | 200 list envelope | |
| GET | `/portfolios/{id}` | `portfolios.read` | 200 | 404 |
| PUT | `/portfolios/{id}` | `portfolios.manage` | 204 when lines are unchanged or no Active assignment; 202 when lines change and an Active assignment exists | 400 / 409 |
| POST | `/portfolios/{id}/disable` | `portfolios.manage` | 204 | 404 |
| POST | `/accounts/{id}/portfolio` | `transactions.create` + visibility | 200 `{ "status": "Success" \| "NoChange" }` | 400 eligibility (nothing written); 400 price / FX / cash (assignment saved as Failed, no transactions); 409 already this portfolio |
| DELETE | `/accounts/{id}/portfolio` | same | 204 | 404 |
| POST | `/accounts/{id}/rebalance` | same | 200 `{ "status": "Success" \| "NoChange" }` | 400 |
| POST | `/portfolios/{id}/rebalance` | same | 202 `{ jobId, accounts }` | 404 |
| GET | `/accounts/{id}/portfolio` | `portfolios.read` or `accounts.read` | 200 | 404 |

List envelope: `{ items, page, pageSize, totalCount }`.

Async rebalance (weight change or `POST /portfolios/{id}/rebalance`) returns:

```json
{
  "jobId": "…",
  "accounts": [
    { "accountId": "…", "handle": "…" }
  ]
}
```

`jobId` identifies the overall job. Each `handle` identifies that account’s rebalance work item (for logs and for `GET /accounts/{id}/portfolio`, which returns `lastRebalanceStatus` and `lastRebalanceMessage`). There is no separate poll route. Assign and `POST /accounts/{id}/rebalance` return 200:

```json
{ "status": "Success" }
```

`status` is `Success` or `NoChange`.

### 8.1 Bodies (sketch)

Create portfolio:

```json
{
  "name": "Balanced Growth",
  "baseCurrency": "NZD",
  "lines": [
    { "instrumentId": "…", "targetWeight": 60.00 },
    { "instrumentId": "…", "targetWeight": 40.00 }
  ]
}
```

Optional settings on create, and on update when sent: `driftAbsolute` (default 1), `driftRelative` (default 5), `minimumTrade` (default 100), `cashResidualPercent` (default 1, and 0 is allowed). Omitted settings on update keep the stored values. SystemAdmin create and list also send `tenantId`. Other roles must not.

Update portfolio adds `rowVersion`. A mismatch is 409. Sending a different `baseCurrency` is 400. Line order is the rounding order.

Assign:

```json
{
  "portfolioId": "…"
}
```

### 8.2 Errors

Ordinary validation in `errors`.  
Cross-tenant / wrong account → 404.  
Rebalance business failures (missing price, missing FX, insufficient cash) → 400 with a stable `code` and a human message. No transactions are written. On assign, the Active assignment is still saved with `LastRebalanceStatus = Failed`. Eligibility failures (`account_not_eligible`, `portfolio_inactive`) write no assignment.

---

## 9. UI

Adviser Portal contract is accepted: [../adviser-portal/adviser-portal-portfolios.md](../adviser-portal/adviser-portal-portfolios.md). Covers list / create / edit / disable, assign / unassign, manual and bulk rebalance, trade lock, and last-rebalance status.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | Weight sum, immutable base currency, single Active assignment invariant |
| Application.FunctionalTests | 401/403; assign on Brokerage succeeds and writes Buy/Sell; assign on Bank → 400; missing price/FX or insufficient cash → no transactions; weight change enqueues rebalance; unassign leaves holdings untouched; Customer 403; while assigned, manual Buy/Sell → 400; cash transfer in then rebalance invests residual cash |
| Infrastructure.IntegrationTests | Other-tenant id → 404 |

---

## 11. Locked in this spec

No new ADR (reuses existing Money, Instrument, Transaction, FX, and price ports).

| Item | Lock |
| --- | --- |
| Rebalance style | Full Rebalance (sell outside holdings, then align) |
| Account types | Brokerage and Other only; no new Account.Type |
| Cardinality | One Active portfolio per account |
| Failure | Whole account rebalance fails; no partial trades; no auto-retry |
| Caller | Adviser / TenantAdmin only; Customer 403 |
| Valuation | Current local price + FX into portfolio BaseCurrency |
| Trades | Ordinary Buy / Sell under Account lock |
| Assigned-account lock | Manual security trades rejected; cash in/out allowed; rebalance invests remaining cash |
| Weight precision | 2 decimal places; last line absorbs rounding to 100.00 |
| Drift / min trade / residual | Drift (1% abs or 5% rel) is for future auto-trigger only; not applied during execution. 100 base-currency min trade; 1% cash residual (portfolio settings). Once started, rebalance aligns to targets subject only to residual and minimum trade. |
| Disable portfolio | Unassign all Active assignments; leave holdings untouched |
| Execution | Weight-change and bulk rebalance always async (202). Assign and single-account manual rebalance are sync (200). |
| Confirmation | Frontend Confirm click only; API has no extra confirm flag |
| Idempotency | No key on the rebalance command; already-aligned accounts are skipped |
| Portal | Accepted: [../adviser-portal/adviser-portal-portfolios.md](../adviser-portal/adviser-portal-portfolios.md) |
| Async 202 body | `jobId` + per-account `handle` array |
| Cash residual base | 1% of total investable value in BaseCurrency (cash + holdings MV) |

### Rebalance result and failure reasons

Success (including NoChange) returns a simple success indicator for that account.  
Failure returns 400 (sync) or a recorded Failed status (async) with a stable `code` and message. No transactions are written.

Possible failure `code` values (to be used consistently):

| code | When |
| --- | --- |
| `missing_price` | `IMarketData` returns empty for any instrument needed (current holding or target line) |
| `missing_fx` | Required FX pair is absent: `quote → base` for valuation, or `quote → account currency` for the Buy/Sell cash leg |
| `insufficient_cash` | After planned sells, remaining cash cannot fund the net buys (above residual) |
| `account_not_eligible` | Account is not Open, or type is not Brokerage/Other, or assignment is not Active |
| `portfolio_inactive` | Target portfolio is disabled |
| `concurrent_modification` | Account lock conflict or row version conflict during the rebalance write (rare) |

The message should include, where relevant, the instrument symbol, the missing pair, or the shortfall amount in both BaseCurrency and Account.Currency.

**Deferred:** no poll route (manual refresh only). Field bindings are the accepted portal contract.

---

## 12. Suggested commits

The repository should build after each commit. Agents split each line into red then green.

1. Domain aggregates + invariants + unit tests.
2. Script `0023_portfolios.sql` + EF mapping.
3. Application commands/queries + policies.
4. HTTP endpoints + functional tests.
5. Auto-rebalance on weight change (Hangfire 202) and synchronous assign / single-account rebalance.
6. TestSeed demo portfolio + assignment.
