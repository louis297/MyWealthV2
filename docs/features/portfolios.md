---
title: Portfolios
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-02
last_updated: 2026-11-26
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
  - leg-accounts.md
  - trade-fees.md
  - custody-modes.md
  - adjustments.md
  - corporate-actions.md
  - orders.md
  - transaction-source.md
  - withdrawals.md
  - account-closure.md
  - rebalances.md
  - in-kind-transfers.md
  - dividends.md
---

# Portfolios

Model portfolios (target-weight investment schemes) on `webapi`. An Adviser or TenantAdmin defines a portfolio of instruments with target weights and a mandatory base currency, and assigns one portfolio to an Open Custodial account. Assignment and any later weight change propose a **Full Rebalance**: sell every holding not in the target, then sell overweight target lines before buying underweight ones. A person confirms the proposal, and the platform trades it through orders at the broker. Customer cannot assign or rebalance. No new `Account.Type`.

This spec owns the portfolio, the assignment, the trade lock of an assigned account, and what a Full Rebalance aims at. The run itself — the proposal, the confirmation, the waves of orders — is owned by [rebalances.md](rebalances.md).

This file states the rules as they are today. §12 lists what changed here, by date.

**Status is `accepted` and landed.**

---

## 1. Summary

A TenantAdmin or Adviser creates a **Portfolio** (name, base currency, lines of instrument + target weight) in the current tenant. SystemAdmin does the same for a chosen tenant. An Adviser assigns at most one Active portfolio to an Open Custodial account of an assigned / same-tenant Customer. Assign proposes a Full Rebalance for that one account, synchronously. A weight change, and `POST /portfolios/{id}/rebalance`, enqueue a job that proposes one for every Active assignment. Nothing trades until a person confirms the proposal; then the rebalance places orders, and its trades are order fills. A missing or stale price, a missing FX pair, an inactive instrument or insufficient cash fails the whole plan for that account. While an account has an Active assignment only its rebalance places orders: a person cannot place an order on it or request an in-kind transfer. Cash moves, adjustments and the platform's own postings remain allowed. A later rebalance invests remaining cash (after the configured residual). Customer receives 403 on every write.

---

## 2. Scope

**In**

- Domain: `Portfolio`, `PortfolioLine`, `AccountPortfolioAssignment`
- Application: create / update / disable portfolio; assign / unassign; the requests that propose a rebalance (on assign, on weight change, manual); queries
- Script `database/schema/0023_portfolios.sql` + EF mapping
- Policies: `portfolios.read` / `portfolios.manage`; assignment and rebalance reuse `transactions.create` + account visibility
- HTTP: `/portfolios`, `/portfolios/{id}`, `POST /portfolios/{id}/disable`, `/accounts/{id}/portfolio`, `/accounts/{id}/rebalance`, `/portfolios/{id}/rebalance`
- Full Rebalance semantics (sell everything outside the target, then align)
- The trade lock of an assigned account
- The job (Hangfire) on weight change and on bulk rebalance
- The Adviser's Book on the three portfolio-wide commands
- Reuse `IMarketData` and `IFxRate` (local tables only; no live vendor calls)
- Tests + Development / TestAppHost `TestSeed` (one demo portfolio + one assignment)

**Out — owned by another spec**

- The rebalance as a stored run: proposal, confirm, cancel, waves, orders: [rebalances.md](rebalances.md)
- Which accounts may be assigned by custody mode: [custody-modes.md](custody-modes.md) R14, R15
- The fee estimate in a plan: [trade-fees.md](trade-fees.md) R25–R29
- Portal pages: [../adviser-portal/adviser-portal-portfolios.md](../adviser-portal/adviser-portal-portfolios.md)

**Out — not built**

- Customer self-assign or self-rebalance
- Overlay / partial rebalance (keep holdings outside the portfolio)
- Multiple Active portfolios on one account
- New `Account.Type` for portfolios
- Scheduled / drift-triggered automatic rebalance (only weight-change and explicit manual)
- Tax lots, performance attribution
- Live prices or live FX
- A portfolio owner (§4.1, rejected options)

---

## 3. Stories

1. As an Adviser I create a portfolio with a base currency and target weights so I can offer a model scheme.
2. As an Adviser I assign a portfolio to an Open Custodial account so the account is brought to that model.
3. As an Adviser I change the weights of a portfolio and every currently assigned account gets a rebalance proposal.
4. As an Adviser I manually rebalance an account (or all accounts on a portfolio) when an earlier plan failed.
5. As an Adviser I see why a plan failed (missing or stale price, missing FX, insufficient cash) without any trade having been made.
6. As a TenantAdmin I manage portfolios for the whole firm.
7. As a Customer I receive 403 on every portfolio write.
8. As an Adviser I cannot assign a portfolio to an External account, or to an account I cannot see.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Wrong role → 403. |
| R2 | Path `{id}` and JSON `id` are PublicId. Internal ints never appear. |
| R3 | Customer receives 403 on every portfolio write (create, update, assign, unassign, rebalance). |
| R4 | Only an Open **Custodial** account may be assigned a portfolio. An External account of any type, or a Closed account → 400 `account_not_eligible` (custody-modes R14). The same test applies to a rebalance (custody-modes R15). Three more refusals of assign, each 400 `account_not_eligible` with its own message: a closing account (account-closure R13), an account with a live order (orders R49: `Cancel the account's open orders before assigning a portfolio.`), an account with an open in-kind transfer (in-kind-transfers R24: `Complete or cancel the account's open transfers of securities before assigning a portfolio.`). |
| R5 | An account may have at most one Active `AccountPortfolioAssignment`. Assigning a new portfolio first marks the previous Active assignment `Removed`. Assign on an Account whose rebalance is in progress → 400 `rebalance_in_progress`, nothing written; a `Proposed` rebalance is cancelled with reason `Unassigned` and a new proposal is made for the new portfolio (rebalances R23). |
| R6 | Portfolio has a mandatory `BaseCurrency` (enabled catalog code). Immutable after create. |
| R7 | Portfolio lines: each `TargetWeight` > 0 and ≤ 100; sum of weights = 100 (R20). Instrument must be active and belong to the same tenant. |
| R8 | **Full Rebalance.** The plan of a rebalance, made on assign, on weight change and on request: (a) sell every current holding whose instrument is not in the target lines, in full, including dust below the minimum trade; (b) then sell overweight target lines before buying underweight ones, so that each target instrument's market value in base currency equals `(weight / 100) × V × (1 − residual)`. Sells come before buys so cash is not temporarily negative. `V` is the Account's available cash plus holdings market value, converted into the portfolio base currency. Available cash leaves out the cash reserved for open withdrawals and includes cash from sales that have not settled (withdrawals R35, orders R46). Only residual and minimum trade apply to step (b). Drift is not re-checked per line. It is the plan of each wave of a rebalance (rebalances R7). |
| R9 | A rebalance trades through orders. It is a stored rebalance: a proposal at the stored prices that an Adviser or TenantAdmin confirms, then ordinary orders at the broker in waves, the sells first and the buys when the sells are in the ledger (rebalances R2, R13–R17). Its trades are order fills with source `OrderFill`. No direct Holding edits. |
| R10 | Valuation uses the current `IMarketData` price and `IFxRate` `quote → base` (identity when they match). A missing price or FX pair fails the whole plan for that account (`missing_price`, `missing_fx`). So do an inactive instrument (`instrument_inactive`), a price more than 4 days old (`stale_price`) and a target line quoted in another currency than the Account's (`currency_mismatch`) (rebalances R8). No order is placed. |
| R11 | **Cash.** The plan estimates each trade's fee from the schedule of the UTC date, and fees come out of the cash residual first. When planned cash would be negative, every buy is scaled by one factor and buys below the minimum trade size are dropped (trade-fees R25–R29). The buys are sized so that the cash they reserve at their price limits is not more than the available cash (rebalances R16). The plan fails with `insufficient_cash` only when the sell fees cannot be paid. |
| R12 | Weight change on an Active portfolio enqueues a job that proposes a rebalance for every currently Active assignment. Manual rebalance endpoints are also provided. An Account whose rebalance is in progress gets a follow-up proposal when that one ends (rebalances R12). |
| R13 | A failed plan does **not** auto-retry. The assignment stays Active; the plan is stored as a `Failed` rebalance with its code and message. An Adviser may propose again. |
| R14 | Drift tolerance, minimum trade size, and cash residual are portfolio-level settings with locked defaults (R21). Drift is used only to decide whether an automatic rebalance should start (not built). Once a plan is made, individual lines are not skipped for being inside the drift band. Trades below the minimum trade size are skipped. A plan with zero trades is `NoChange`. |
| R15 | Unassign marks the assignment `Removed`. No trade is made on unassign. In the same database transaction the Account's open rebalance is cancelled; unassign is never refused because of a rebalance (rebalances R22). The account becomes a free-holdings account again. |
| R16 | Cross-tenant or wrong-collection id → 404, not 403. Adviser sees only assigned customers. On the three portfolio-wide commands the Adviser's Book applies (§4.1). |
| R17 | SystemAdmin supplies `tenantId` on create/list (same pattern as other Phase-2 resources). |
| R18 | **Assigned-account trade lock.** While an account has an Active `AccountPortfolioAssignment`, only its rebalance changes its holdings by trading. Refused on such an account: `POST /orders` (400, orders R8); an in-kind transfer request in either direction (in-kind-transfers R24); a hand security-changing transaction (`Buy`, `Sell`, `StockIn`, `StockOut`, `Split`, `Bonus`, `Scrip`, `OpeningHoldings`) and the reverse of one, including a hand reverse of a rebalance trade (400; rebalances R24, R28). The lock applies when any security leg's Account has an Active assignment (leg-accounts R17). Outside the lock, with the assignment staying Active and no rebalance enqueued: the cash types and their reverse; `InternalTransfer` and `FxConversion` with the assigned Account on either side ([fx-conversions.md](fx-conversions.md) R24); an `Adjustment` (adjustments R16); an entitlement posting (corporate-actions R30); a dividend posting (dividends R41). An account with an Active assignment never reinvests a dividend: the portfolio decides what its cash buys (dividends R18). An account closure is refused while the Account has an Active assignment: unassign first (account-closure R5). |
| R19 | After cash arrives, a subsequent rebalance treats all cash above the configured residual as investable and buys to target weights. |
| R20 | Target weights are stored with 2 decimal places. On create/update the last line absorbs any rounding difference so the sum is exactly 100.00. |
| R21 | Drift tolerance default: absolute 1% or relative 5% (whichever is more permissive). Used only for future automatic-trigger decisions; not applied when a plan is made. Minimum trade size default: 100 in portfolio BaseCurrency. Cash residual default: 1% of total investable value in the portfolio BaseCurrency (cash + holdings market value, all converted; or 0 if explicitly set to 0). These are portfolio-level settings. |
| R22 | Disabling a portfolio unassigns every Active assignment (Status → Removed) and leaves existing holdings untouched. No trade is made on disable. The open rebalance of each assigned Account is cancelled in the same database transaction (rebalances R22). |
| R23 | Weight-change and bulk rebalance are always asynchronous (Hangfire). The HTTP call returns **202 Accepted** with both a job id and a per-account handle for every targeted assignment; the handle is the id of the rebalance the job proposes. Unchanged lines, or a weight change with no Active assignment, return **204**. Assign and single-account manual rebalance are synchronous and return the proposal directly. |
| R24 | A plan that finds the account already aligned (after residual and minimum-trade filters) is stored as a `NoChange` rebalance. No idempotency key is required on the rebalance command itself. |
| R25 | Nothing trades without a confirmation: `POST /rebalances/{id}/confirm` is required before any order (rebalances R13). Assign and weight change need no confirm flag of their own. |

### 4.1 The Adviser's Book on portfolio-wide commands

R16 says an Adviser sees only assigned Customers, and every single-account command enforces it. A Portfolio is tenant-scoped and has no owner, and the three portfolio-wide commands expand to every Active assignment of the portfolio in the tenant. Without the rules below an Adviser could act on accounts outside their Book through a portfolio that another Adviser's Customer uses.

An account is in the caller's **Book** when `AccountScope.ManagedAccounts` returns it: for an Adviser, an account of a Customer whose `AdviserId` is that Adviser. This is the same scope as `GET /accounts/{id}`. A portfolio is **Shared** for an Adviser when it has an Active assignment on an account outside the Adviser's Book.

| Id | Rule |
| --- | --- |
| PB1 | **The portfolio stays tenant-scoped.** Every Adviser in the tenant may list, read and assign it (R16, R17 and the assign rules). There is no owner column. |
| PB2 | **Bulk rebalance is filtered.** For an Adviser, `POST /portfolios/{id}/rebalance` enqueues only the Active assignments whose Account is in the Adviser's Book. The 202 `accounts` array lists only those. Assignments outside the Book are not enqueued, not listed, and not counted. When none is in the Book, the call returns 202 with an empty `accounts` array, as it does for a portfolio with no Active assignment. |
| PB3 | **Update is refused on a Shared portfolio.** For an Adviser, `PUT /portfolios/{id}` on a Shared portfolio returns 409 with `code: "portfolio_shared"`. The whole request is refused: name, lines and settings are not saved, `RowVersion` does not change, and nothing is enqueued. This applies whatever the body changes, including a body that changes nothing. |
| PB4 | **Disable is refused on a Shared portfolio.** For an Adviser, `POST /portfolios/{id}/disable` on a Shared portfolio returns 409 with `code: "portfolio_shared"`. The portfolio stays Active and every assignment stays Active. |
| PB5 | **TenantAdmin and SystemAdmin are not limited.** On all three commands they act on every Active assignment in the portfolio's tenant. |
| PB6 | **Order of checks.** Validation (400), then the portfolio lookup (404), then `portfolio_shared` (409), then the `rowVersion` check (409 `stale_row_version`), then the other rules. An Adviser who sends a stale `rowVersion` to a Shared portfolio gets `portfolio_shared`, because a reload would not help. |
| PB7 | **Error shape.** `portfolio_shared` is a problem code ([../api-design.md](../api-design.md) §4.2): a `ProblemException` subclass with `Kind = Conflict`, title `Portfolio is shared`, and detail `This portfolio is assigned to accounts outside your book. Ask a TenantAdmin to change or disable it.` The detail does not name, count or link the other accounts or Advisers (§4.2 C5). Clients tell it apart from `stale_row_version` by `code` (§4.2 C2, C8). |
| PB8 | **Enqueue after a weight change is Book-filtered too.** When an Adviser's `PUT` passes PB3 and lines change, the list of accounts to enqueue is read after the save with the PB2 filter. In the normal case this is every Active assignment, because PB3 passed. It only differs when another Adviser assigned the portfolio between the check and the save (see "Left open"). |

**Why refuse, not filter, on update and disable.** New lines and settings are stored on the portfolio. Every assigned account trades on them at its next rebalance, whether or not this call enqueues it. Disable removes assignments. Filtering would still change other Advisers' accounts. Bulk rebalance stores nothing, so filtering it is enough.

**Why 409, not 403.** The caller has the role and the policy (R1 keeps 403 for a wrong role). The request fails because of the portfolio's current state, which another person can change (a TenantAdmin, or the other Adviser unassigning). That is a conflict.

**Consequences.**

- Once Customers of two Advisers use one portfolio, only a TenantAdmin can change or disable it. A Shared portfolio is a firm-level model, and the firm's models belong to the TenantAdmin (story 6).
- Any Adviser can make a portfolio Shared by assigning it to one of their own accounts. The Advisers who already use it can no longer change it. They can still unassign their own accounts and create a new portfolio. A TenantAdmin can still change it.
- An Adviser's edit page shows only the accounts in the Adviser's Book (the portal reads `/accounts` and `GET /accounts/{id}/portfolio`, which are Book-scoped).

**Rejected options.**

- *Portfolio owner* (`OwnerAdviserId`; null means firm-level and only a TenantAdmin manages it). Cleanest model, but it needs a schema script, a domain change, every portfolio handler and the portal list and forms. Revisit if Advisers need private models that other Advisers cannot assign.
- *Remove `portfolios.manage` from Adviser.* Conflicts with stories 1 and 3 and the accepted portal contract.
- *Filter update and disable to the Book.* See "Why refuse, not filter" above.

**Left open.** The PB3 / PB4 check and the save are not one atomic step. If another Adviser's assign commits between them, the change is saved and applies to that account at its next rebalance. PB8 keeps it out of the immediate enqueue. The window is one request's duration and needs two Advisers acting on the same portfolio at the same moment. Accepted for this phase. Closing it would need the assign to touch the portfolio's `RowVersion`, or a lock on the portfolio row in both commands.

Where it lives: `PortfolioBook` (`src/Application/Portfolios`), used by `RebalancePortfolio`, `UpdatePortfolio` and `DisablePortfolio`; `PortfolioSharedException`.

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `Portfolio` | Aggregate | Tenant-scoped. Name, BaseCurrency (immutable), IsActive, drift/min-trade/residual settings. |
| `PortfolioLine` | Entity | InstrumentId + TargetWeight. Owned by Portfolio. |
| `AccountPortfolioAssignment` | Entity | AccountId, PortfolioId, Status (Active / Removed), AssignedAt, AssignedBy. The outcome of a rebalance is on the `Rebalance`, not here. |
| `PortfolioCreated` / `PortfolioAssigned` / `PortfolioUnassigned` / `PortfolioWeightsChanged` | Event | Raised on the corresponding commands. Weight change is the trigger for the job. |

Invariants:

- BaseCurrency immutable after create.
- At most one Active assignment per Account.
- An Active assignment is on a Custodial account (held by Application).
- Weights sum to 100 on a valid portfolio.

Keep [domain-model.md](../domain-model.md) in the same change as this file.

---

## 6. Database

| Table | Shape | Index / FK |
| --- | --- | --- |
| `Portfolios` | The portfolio and its settings for drift, minimum trade and cash residual | PK, unique PublicId, FK TenantId, FK BaseCurrency → Currencies |
| `PortfolioLines` | One row per target line. `SortOrder` is the request order; the last line absorbs rounding. `TenantId` copied from the Portfolio (`0027`) | PK, FK PortfolioId, FK InstrumentId, unique (PortfolioId, InstrumentId) |
| `AccountPortfolioAssignments` | TenantId, AccountId, PortfolioId, Status, AssignedAt, AssignedBy | PK, FK AccountId, FK PortfolioId, filtered unique index on AccountId where Status = Active; unique `(TenantId, Id)` (`0053`) |

Script: `database/schema/0023_portfolios.sql`. Later scripts: `0032` removed the Active assignments on External accounts (custody-modes R16); `0053` dropped the three `LastRebalance…` columns of `AccountPortfolioAssignments` ([rebalances.md](rebalances.md) §6). Do not add EF migrations. The current columns are in [database-design.md](../database-design.md) §11.6.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreatePortfolio` | Portfolio id | Role, tenant, base currency enabled, weights sum 100, instruments active |
| Command | `UpdatePortfolio` | 204, or 202 when Active assignments exist and lines changed | Same; PB3, PB6; enqueues the job when lines change (PB8) |
| Command | `DisablePortfolio` | — | PB4. Marks inactive, unassigns every Active assignment and cancels their open rebalances. No trades. |
| Command | `AssignPortfolio` | `{ status, rebalanceId }` | R4, R5, then a synchronous proposal. Same portfolio again → 409. |
| Command | `UnassignPortfolio` | — | Marks Removed and cancels the open rebalance; no trades |
| Command | A rebalance of one account (`POST /accounts/{id}/rebalance`) | `{ status, rebalanceId }` | Manual; a synchronous proposal (rebalances R10) |
| Command | `RebalancePortfolio` | 202 job id + handles | Manual; enqueues every Active assignment in the caller's scope (PB2) |
| Query | `GetPortfolio` / `ListPortfolios` | Portfolio + lines | Isolation |
| Query | `GetAccountPortfolio` | Current assignment + its newest rebalance | Account visibility |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| POST | `/portfolios` | `portfolios.manage` | 201 `{ "id" }` | 400 validation |
| GET | `/portfolios` | `portfolios.read` | 200 list envelope | |
| GET | `/portfolios/{id}` | `portfolios.read` | 200 | 404 |
| PUT | `/portfolios/{id}` | `portfolios.manage` | 204 when lines are unchanged or no Active assignment; 202 when lines change and an Active assignment exists | 400 / 409 `stale_row_version` / 409 `portfolio_shared` |
| POST | `/portfolios/{id}/disable` | `portfolios.manage` | 204 | 404 / 409 `portfolio_shared` |
| POST | `/accounts/{id}/portfolio` | `transactions.create` + visibility | 200 `{ "status", "rebalanceId" }` | 400 eligibility (nothing written); 400 with the plan's code (the assignment stays Active and the `Failed` rebalance is stored); 409 `portfolio_already_assigned` |
| DELETE | `/accounts/{id}/portfolio` | same | 204 | 404 |
| POST | `/accounts/{id}/rebalance` | same | 200 `{ "status", "rebalanceId" }` | 400 |
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

`jobId` identifies the overall job. Each `handle` is the id of the rebalance the job proposes for that account (`GET /rebalances/{id}`). Assign and `POST /accounts/{id}/rebalance` return 200:

```json
{ "status": "Proposed", "rebalanceId": "…" }
```

`status` is `Proposed` or `NoChange`. `GET /accounts/{id}/portfolio` carries `rebalance`: the newest rebalance of the assignment as `{ id, status, failure, proposedAt, endedAt }`, or `null` (rebalances R25).

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

Update portfolio adds `rowVersion`. A mismatch is 409 `stale_row_version`. Sending a different `baseCurrency` is 400. Line order is the rounding order.

Assign:

```json
{
  "portfolioId": "…"
}
```

### 8.2 Errors

Ordinary validation in `errors`.  
Cross-tenant / wrong account → 404.  
A plan that fails → 400 with a stable `code` and a human message. No order is placed. On assign, the assignment is still saved Active, and the `Failed` rebalance is stored (rebalances R10). Eligibility failures (`account_not_eligible`, `portfolio_inactive`) write no assignment. Every 409 carries a problem `code` ([../api-design.md](../api-design.md) §4.2).

---

## 9. UI

Adviser Portal contract: [../adviser-portal/adviser-portal-portfolios.md](../adviser-portal/adviser-portal-portfolios.md). Covers list / create / edit / disable, assign / unassign, manual and bulk rebalance, the trade lock, and the rebalance of an account.

---

## 10. Tests

The tests are the executable form of §4. Where they live:

| Project | Where |
| --- | --- |
| Domain.UnitTests | Weight sum, immutable base currency, single Active assignment |
| Application.FunctionalTests | Folder `Portfolios/`: `CreatePortfolioTests`, `UpdatePortfolioTests`, `AssignPortfolioTests`, `GetAccountPortfolioTests`, `RebalanceEnqueueTests`, `TradeLockTests`, `InternalTransferPortfolioTests`, `FxConversionPortfolioTests`; `AdviserBookPortfolioTests` (fixture `AdviserBookWorld`) for §4.1. Folder `Custody/CustodialPortfolioTests` for R4. Folder `Rebalances/` for the run |
| Infrastructure.IntegrationTests | Other-tenant id → 404 |

---

## 11. Locked in this spec

No new ADR (reuses existing Money, Instrument, Transaction, FX, and price ports).

| Item | Lock |
| --- | --- |
| Rebalance style | Full Rebalance (sell outside holdings, then align) |
| Account types | An Open Custodial account; no new Account.Type |
| Cardinality | One Active portfolio per account |
| Failure | The whole plan of an account fails; no auto-retry |
| Caller | Adviser / TenantAdmin only; Customer 403 |
| Valuation | Current local price + FX into portfolio BaseCurrency |
| Trades | Orders at the broker, placed by the rebalance after a person confirmed it |
| Assigned-account lock | Only the rebalance trades; cash in/out allowed; a rebalance invests remaining cash |
| Weight precision | 2 decimal places; last line absorbs rounding to 100.00 |
| Drift / min trade / residual | Drift (1% abs or 5% rel) is for future auto-trigger only; not applied when a plan is made. 100 base-currency min trade; 1% cash residual (portfolio settings). |
| Disable portfolio | Unassign all Active assignments; leave holdings untouched |
| Execution | Weight-change and bulk rebalance always async (202). Assign and single-account manual rebalance are sync (200). |
| Idempotency | No key on the rebalance command |
| Async 202 body | `jobId` + per-account `handle` array |
| Cash residual base | 1% of total investable value in BaseCurrency (cash + holdings MV) |
| Shared portfolio | An Adviser cannot change or disable it; bulk rebalance is filtered to the Book (§4.1) |

### Failure reasons of a plan

A failed plan returns 400 (sync) or is stored as a `Failed` rebalance (job), with a stable `code` and message. No order is placed.

| code | When |
| --- | --- |
| `missing_price` | `IMarketData` returns empty for any instrument needed (current holding or target line) |
| `missing_fx` | A required FX pair is absent |
| `stale_price` | The latest price of an instrument the plan would trade is more than 4 days old (rebalances R8) |
| `instrument_inactive` | An instrument the plan would trade is not active (rebalances R8) |
| `currency_mismatch` | A target line is quoted in another currency than the Account's (rebalances R8) |
| `insufficient_cash` | The sell fees cannot be paid (R11) |
| `account_not_eligible` | The Account is not an Open Custodial account with an Active assignment, or R4 refuses the assign |
| `portfolio_inactive` | Target portfolio is disabled |
| `no_broker_connection` | The Tenant names no configured broker connection (rebalances R6) |
| `rebalance_in_progress` | The Account's rebalance is in progress (rebalances R4) |

The message should include, where relevant, the instrument symbol, the missing pair, or the shortfall amount in both BaseCurrency and Account.Currency. `concurrent_modification` is not used: a stale `rowVersion` on confirm or cancel is 409 `stale_row_version`.

---

## 12. History

What changed in this file after it was accepted. The text of each change is in git (`git log -p -- docs/features/portfolios.md`).

| Date | With | Change here |
| --- | --- | --- |
| 2026-11-10 | [leg-accounts.md](leg-accounts.md) | R18: the lock applies per security leg account |
| 2026-11-11 | [fx-conversions.md](fx-conversions.md) | R18: transfers and conversions on an assigned account |
| 2026-11-13 | [../api-design.md](../api-design.md) §4.2 | A problem `code` on every 409. §4.1: the Adviser's Book (PB1–PB8), `portfolio_shared` |
| 2026-11-15 | [trade-fees.md](trade-fees.md) | R11: fees in the plan, buy scaling |
| 2026-11-17 | [custody-modes.md](custody-modes.md) | R4: Custodial accounts only; script `0032` |
| 2026-11-19 | [transaction-source.md](transaction-source.md), [adjustments.md](adjustments.md) | Rebalance trades got a source and lost their `reference`. R18: an adjustment is outside the lock |
| 2026-11-20 | [withdrawals.md](withdrawals.md) | R8, R11: the plan uses available cash |
| 2026-11-22 | [account-closure.md](account-closure.md), [corporate-actions.md](corporate-actions.md) | R4: a closing account. R18: an entitlement posting is outside the lock |
| 2026-11-23 | [orders.md](orders.md) | R4: an account with a live order. R18: an order is a hand trade |
| 2026-11-23 | [rebalances.md](rebalances.md) | R5, R8–R13, R15, R22–R25, §5–§8, §11: a rebalance is a stored run, confirmed by a person and traded through orders |
| 2026-11-24 | [in-kind-transfers.md](in-kind-transfers.md), [dividends.md](dividends.md) | R4, R18: in-kind transfers and dividends on an assigned account |
| 2026-11-26 | — | The amendments above were folded into §1–§11. No rule changed |
