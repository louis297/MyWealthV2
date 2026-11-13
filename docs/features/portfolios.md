---
title: Portfolios
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-02
last_updated: 2026-11-13
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

Model portfolios (target-weight investment schemes) on `webapi`. An Adviser or TenantAdmin defines a portfolio of instruments with target weights and a mandatory base currency. An Adviser assigns one portfolio to an Open Brokerage or Other account. Assignment and any later weight change run a **Full Rebalance**: sell every holding not in the target, then sell overweight target lines before buying underweight ones. All trades are ordinary `Buy` / `Sell` transactions (existing model). Customer cannot assign or rebalance. No new `Account.Type`.

**Status is `accepted`.** Implementation follows this file.


**Amendment 2026-11-10 (leg accounts, accepted, landed):** [leg-accounts.md](leg-accounts.md) R17. The R18 trade lock applies when any security leg's Account has an Active assignment. Rebalance still writes ordinary `Buy` / `Sell` on the assigned Account.

**Amendment 2026-11-13 (Adviser Book on portfolio-wide commands, accepted, landed):** see the amendment at the end of this file. Part 1 puts a problem `code` on every 409 ([../api-design.md](../api-design.md) §4.2). Part 2, for the Adviser role, filters `POST /portfolios/{id}/rebalance` to the Adviser's Book and refuses `PUT /portfolios/{id}` and `POST /portfolios/{id}/disable` on a Shared portfolio (409 `portfolio_shared`).

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
| R8 | **Full Rebalance** on assign and on weight change: (a) sell every current holding whose instrument is not in the target lines, in full, including dust below the minimum trade; (b) then sell overweight target lines before buying underweight ones, so that each target instrument’s market value in base currency equals `(weight / 100) × V × (1 − residual)`. Sells post before buys so cash is not temporarily negative. `V` is cash plus holdings market value, converted into the portfolio base currency. Once rebalance has started, only residual and minimum trade apply to step (b). Drift is not re-checked per line. |
| R9 | All trades are ordinary `Buy` / `Sell` transactions under the existing Account lock. No direct Holding edits. |
| R10 | Valuation uses the current `IMarketData` price and `IFxRate` `quote → base` (identity when they match). The cash leg reuses ordinary Buy/Sell, which converts `quote → account currency`. Missing price or either FX pair → the whole rebalance for that account fails (400 or recorded failure; no transactions written). |
| R11 | Insufficient cash (after planned sells) to fund the net buys → whole rebalance fails. No partial execution. |
| R12 | Weight change on an Active portfolio enqueues / triggers rebalance for every currently Active assignment. Manual rebalance endpoints are also provided. |
| R13 | Automatic rebalance failure does **not** auto-retry. Assignment stays Active; `LastRebalanceStatus = Failed` and a message are stored. Adviser may retry manually. |
| R14 | Drift tolerance, minimum trade size, and cash residual are portfolio-level settings with locked defaults (R21). Drift is used only to decide whether an automatic rebalance should start (deferred in this slice). Once any rebalance has started, individual lines are not skipped for being inside the drift band. Trades below the minimum trade size are skipped. A rebalance that results in zero trades is Success (NoChange). |
| R15 | Unassign marks the assignment `Removed`. No automatic trades on unassign. The account becomes a free-holdings account again. |
| R16 | Cross-tenant or wrong-collection id → 404, not 403. Adviser sees only assigned customers. |
| R17 | SystemAdmin supplies `tenantId` on create/list (same pattern as other Phase-2 resources). |
| R18 | **Assigned-account trade lock.** While an account has an Active `AccountPortfolioAssignment`, any manual security-changing transaction (`Buy`, `Sell`, `StockIn`, `StockOut`, `Split`, `Bonus`, `Scrip`, `OpeningHoldings`) and a reverse of those types is rejected (400). Only the rebalance command and unassign may change holdings. Cash types remain allowed: `TransferIn`, `TransferOut`, `InternalTransfer`, `FxConversion`, `Interest`, `CloseOut`, `Opening`, and reverse of those. `InternalTransfer` and `FxConversion` may have the assigned Account on either side and do not trigger a rebalance (amended 2026-11-11, [fx-conversions.md](fx-conversions.md) R24). |
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

---

## Amendment 2026-11-13 (Adviser Book on portfolio-wide commands) — accepted

Status of this amendment: `accepted` (2026-11-13). It is a build contract. Landed 2026-11-13: `ProblemException` (`src/Application/Common/Exceptions`) mapped in one arm of `ProblemDetailsExceptionHandler`; `PortfolioBook` (`src/Application/Portfolios`) used by `RebalancePortfolio`, `UpdatePortfolio` and `DisablePortfolio`; `PortfolioSharedException`. PC-T1–PC-T5 extend the existing 409 tests plus `AssignPortfolioTests.SamePortfolioAgain_Returns409WithTheProblemCode`; PB-T1–PB-T11 are in `AdviserBookPortfolioTests` (fixture `AdviserBookWorld`). PC-T1–PC-T5, PB-T1, PB-T2, PB-T4–PB-T6 and PB-T9 failed before the change. The rest of this file stays accepted. Do not rewrite R16, R22, R23 or §8 in place. No schema script, no new route, no new policy.

It has two parts. Part 1 is the build contract for [../api-design.md](../api-design.md) §4.2 (problem codes). It lands first, because part 2 adds a second 409 to `PUT /portfolios/{id}` and `POST /portfolios/{id}/disable`, and clients must be able to tell the two apart. Part 2 tightens R16, R22 and R23 and three rows of §8 for the Adviser role only.

### Part 1 — problem codes on every 409

Rules C1–C9 and the registry are in [../api-design.md](../api-design.md) §4.2. Do not copy them here. This part makes today's code follow them.

| Id | Change |
| --- | --- |
| PC1 | Add `ProblemException` in `src/Application/Common/Exceptions` as §4.2 C9 describes. `ProblemDetailsExceptionHandler` maps it in one arm and writes `code`, `title`, `detail` and the listed extensions. |
| PC2 | `ConcurrencyException`, `IdempotencyConflictException`, `ExpectedRateChangedException`, `ReportNotReadyException` and `AssignmentConflictException` become `ProblemException` subclasses with the codes `stale_row_version`, `idempotency_key_reused`, `rate_changed`, `report_not_ready` and `portfolio_already_assigned`. Their own arms leave the handler. Status, `title`, `detail` and `errors.expectedRate` stay as they are today. |
| PC3 | `TargetDisabledException` and `RebalanceException` become `ProblemException` subclasses (`Kind = Rule`). Their JSON does not change. This is a refactor; their existing tests prove it. |
| PC4 | No client change in part 1. Every page that branches on `status === 409` today calls routes that, after part 1, still return one 409 code each, except `POST /transactions/fx`, whose page already tells its two 409s apart by `errors.expectedRate` (§4.2 C8). |

**Tests (part 1).** Application.FunctionalTests. Each asserts the status and `code`, and that the existing `title` / `detail` / `errors` are unchanged. Extend the existing 409 tests where they exist rather than adding parallel ones.

| Id | When | Then |
| --- | --- | --- |
| PC-T1 | `PUT /portfolios/{id}` with a stale `rowVersion` (one route is enough for the shared mapping) | 409, `code` = `stale_row_version` |
| PC-T2 | `POST /transactions` replayed with the same `Idempotency-Key` and a different body | 409, `code` = `idempotency_key_reused` |
| PC-T3 | `POST /transactions/fx` with an `expectedRate` different from the stored rate | 409, `code` = `rate_changed`, `errors.expectedRate` present |
| PC-T4 | Download a report execution that has no result yet | 409, `code` = `report_not_ready` |
| PC-T5 | `POST /accounts/{id}/portfolio` with the portfolio already Active on that account | 409, `code` = `portfolio_already_assigned` |

PC-T1–PC-T5 fail against today's code on the `code` assertion.

### Part 2 — Adviser Book on portfolio-wide commands

**Why.** R16 says an Adviser sees only assigned Customers, and every single-account command (`POST /accounts/{id}/portfolio`, `DELETE /accounts/{id}/portfolio`, `POST /accounts/{id}/rebalance`, `GET /accounts/{id}/portfolio`) enforces it. A Portfolio is tenant-scoped and has no owner, and the three portfolio-wide commands expand to every Active assignment of the portfolio in the tenant. So an Adviser can act on accounts outside their Book through a portfolio that another Adviser's Customer uses:

| Adviser call | Today | Effect on another Adviser's Customer |
| --- | --- | --- |
| `POST /portfolios/{id}/rebalance` | 202 | Enqueues a Full Rebalance of the other account. The body returns that account's PublicId and handle. |
| `PUT /portfolios/{id}` with new lines | 202 | Changes that account's target. Enqueues its rebalance and returns its PublicId. |
| `PUT /portfolios/{id}` with new settings only | 204 | Changes `minimumTrade` / `cashResidualPercent`, which the next rebalance of that account uses. |
| `POST /portfolios/{id}/disable` | 204 | Removes that account's assignment. |

The same Adviser gets 404 from `GET /accounts/{id}` and `POST /accounts/{id}/rebalance` on that account.

Today's code: `src/Application/Portfolios/Commands/RebalancePortfolio/RebalancePortfolio.cs`, `UpdatePortfolio/UpdatePortfolio.cs` and `DisablePortfolio/DisablePortfolio.cs` filter the portfolio by tenant only, then read every Active assignment without `AccountScope.ManagedAccounts`. `RolePermissions` gives Adviser `portfolios.manage` and `transactions.create`.

**Book scope.** In this amendment, an account is in the caller's Book when `AccountScope.ManagedAccounts` returns it: for an Adviser, an account of a Customer whose `AdviserId` is that Adviser. This is the same scope as `GET /accounts/{id}`. No second scope rule.

| Id | Rule |
| --- | --- |
| PB1 | **The portfolio stays tenant-scoped.** Every Adviser in the tenant may list, read and assign it (R16, R17 and the assign rules unchanged). There is no owner column. |
| PB2 | **Bulk rebalance is filtered.** For an Adviser, `POST /portfolios/{id}/rebalance` enqueues only the Active assignments whose Account is in the Adviser's Book. The 202 `accounts` array lists only those. Assignments outside the Book are not enqueued, not listed, and not counted. When none is in the Book, the call returns 202 with an empty `accounts` array, as it does today for a portfolio with no Active assignment. |
| PB3 | **Update is refused on a Shared portfolio.** For an Adviser, `PUT /portfolios/{id}` on a Shared portfolio returns 409 with `code: "portfolio_shared"`. The whole request is refused: name, lines and settings are not saved, `RowVersion` does not change, and nothing is enqueued. This applies whatever the body changes, including a body that changes nothing. |
| PB4 | **Disable is refused on a Shared portfolio.** For an Adviser, `POST /portfolios/{id}/disable` on a Shared portfolio returns 409 with `code: "portfolio_shared"`. The portfolio stays Active and every assignment stays Active. |
| PB5 | **TenantAdmin and SystemAdmin are not limited.** They keep the current behaviour on all three commands: every Active assignment in the portfolio's tenant. |
| PB6 | **Order of checks.** Validation (400), then the portfolio lookup (404), then `portfolio_shared` (409), then the `rowVersion` check (409 `stale_row_version`), then the existing rules. An Adviser who sends a stale `rowVersion` to a Shared portfolio gets `portfolio_shared`, because a reload would not help. |
| PB7 | **Error shape.** `portfolio_shared` is a problem code ([../api-design.md](../api-design.md) §4.2, registry row added by this amendment): a `ProblemException` subclass with `Kind = Conflict`, title `Portfolio is shared`, and detail `This portfolio is assigned to accounts outside your book. Ask a TenantAdmin to change or disable it.` The detail does not name, count or link the other accounts or Advisers (§4.2 C5). Clients tell it apart from `stale_row_version` by `code` (§4.2 C2, C8). |
| PB8 | **Enqueue after a weight change is Book-filtered too.** When an Adviser's `PUT` passes PB3 and lines change, the list of accounts to enqueue is read after the save with the PB2 filter. In the normal case this is every Active assignment, because PB3 passed. It only differs when another Adviser assigned the portfolio between the check and the save (see Left open). |

**Why refuse, not filter, on update and disable.** New lines and settings are stored on the portfolio. Every assigned account trades on them at its next rebalance, whether or not this call enqueues it. Disable removes assignments. Filtering would still change other Advisers' accounts. Bulk rebalance stores nothing, so filtering it is enough.

**Why 409, not 403.** The caller has the role and the policy (R1 keeps 403 for a wrong role). The request fails because of the portfolio's current state, which another person can change (a TenantAdmin, or the other Adviser unassigning). That is a conflict.

**Consequences (accepted).**

- Once Customers of two Advisers use one portfolio, only a TenantAdmin can change or disable it. A Shared portfolio is a firm-level model, and the firm's models belong to the TenantAdmin (story 6).
- Any Adviser can make a portfolio Shared by assigning it to one of their own accounts. The Advisers who already use it can no longer change it. They can still unassign their own accounts and create a new portfolio. A TenantAdmin can still change it.
- An Adviser's edit page shows only the accounts in the Adviser's Book (the portal reads `/accounts` and `GET /accounts/{id}/portfolio`, which are already Book-scoped). The weight-change confirmation copy stays correct, because a Shared portfolio is refused before any rebalance.

**Rejected options.**

- *Portfolio owner* (`OwnerAdviserId`; null means firm-level and only a TenantAdmin manages it). Cleanest model, but it needs a schema script, a domain change, every portfolio handler and the portal list and forms. About two to three times this amendment. Revisit if Advisers need private models that other Advisers cannot assign.
- *Remove `portfolios.manage` from Adviser.* Conflicts with stories 1 and 3 and the accepted portal contract.
- *Filter update and disable to the Book.* See "Why refuse, not filter" above.

**Left open.** The PB3 / PB4 check and the save are not one atomic step. If another Adviser's assign commits between them, the change is saved and applies to that account at its next rebalance. PB8 keeps it out of the immediate enqueue. The window is one request's duration and needs two Advisers acting on the same portfolio at the same moment. Accepted for this phase. Closing it would need the assign to touch the portfolio's `RowVersion`, or a lock on the portfolio row in both commands.

**Tests (part 2).** Application.FunctionalTests. Fixture: one tenant with Advisers A and B, Customer CA (Adviser A) and Customer CB (Adviser B), each with an Open Brokerage account, one active portfolio P. "Shared" means P has Active assignments on both accounts. Assertions on enqueueing read `RecordingRebalanceScheduler`. The existing two-tenant 404 tests stay green.

| Id | Given | When | Then |
| --- | --- | --- | --- |
| PB-T1 | P Shared | Adviser A `POST /portfolios/{P}/rebalance` | 202. `accounts` holds only CA's account. Only CA's account is enqueued |
| PB-T2 | P assigned to CB's account only | Adviser A `POST /portfolios/{P}/rebalance` | 202 with empty `accounts`. Nothing enqueued |
| PB-T3 | P Shared | TenantAdmin `POST /portfolios/{P}/rebalance` | 202. Both accounts listed and enqueued |
| PB-T4 | P Shared | Adviser A `PUT /portfolios/{P}` with new lines | 409, `code` = `portfolio_shared`. `GET /portfolios/{P}` returns the old lines and the old `rowVersion`. Nothing enqueued |
| PB-T5 | P Shared | Adviser A `PUT /portfolios/{P}` with the same lines and a new `minimumTrade` | 409, `code` = `portfolio_shared`. Stored `minimumTrade` unchanged |
| PB-T6 | P Shared | Adviser A `PUT /portfolios/{P}` with a stale `rowVersion` | 409, `code` = `portfolio_shared` (not `stale_row_version`) |
| PB-T7 | P assigned to CA's account only | Adviser A `PUT /portfolios/{P}` with new lines | 202. `accounts` holds CA's account (behaviour unchanged) |
| PB-T8 | P Shared | TenantAdmin `PUT /portfolios/{P}` with new lines | 202. Both accounts listed and enqueued |
| PB-T9 | P Shared | Adviser A `POST /portfolios/{P}/disable` | 409, `code` = `portfolio_shared`. P stays active. Adviser B `GET /accounts/{CB account}/portfolio` returns 200 |
| PB-T10 | P assigned to CA's account only | Adviser A `POST /portfolios/{P}/disable` | 204. P inactive. CA's assignment Removed |
| PB-T11 | P Shared | TenantAdmin `POST /portfolios/{P}/disable` | 204. Both assignments Removed |

PB-T1, PB-T4, PB-T5 and PB-T9 fail against today's code. The others pin behaviour that must not regress.

Portal: [../adviser-portal/adviser-portal-portfolios.md](../adviser-portal/adviser-portal-portfolios.md), amendment 2026-11-13, P12.

**Suggested commits.** The tree builds after each commit. Each behaviour is split into red and green.

1. Problem codes on every 409 (PC1, PC2, PC-T1–PC-T5).
2. Move `TargetDisabledException` and `RebalanceException` onto `ProblemException` (PC3; refactor, existing tests green).
3. Bulk rebalance filtered to the Adviser's Book (PB2, PB-T1–PB-T3).
4. Update refused on a Shared portfolio (PB3, PB6–PB8, PB-T4–PB-T8).
5. Disable refused on a Shared portfolio (PB4, PB-T9–PB-T11).
6. Adviser Portal branches on `code` on the portfolio detail page (P12).
7. Mark this amendment landed here, in the portal spec and in `api-design.md` §4.2.
