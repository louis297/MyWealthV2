---
title: Custody modes
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-17
last_updated: 2026-11-17
related:
  - account-custody-mode.md
  - accounts.md
  - posting.md
  - holdings.md
  - buy-sell.md
  - split.md
  - leg-accounts.md
  - internal-transfers.md
  - fx-conversions.md
  - portfolios.md
  - trade-fees.md
  - ../fixes/ledger-write-guards.md
  - ../adr/0020-account-custody-mode.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../adviser-portal/adviser-portal-portfolios.md
  - ../known-issues/custodial-account-gaps.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
  - ../future-plans.md
---

# Custody modes

Cross-cutting amendment to the ledger on `webapi`: [accounts.md](accounts.md), [posting.md](posting.md), [holdings.md](holdings.md), [buy-sell.md](buy-sell.md), [split.md](split.md), [leg-accounts.md](leg-accounts.md), [internal-transfers.md](internal-transfers.md), [fx-conversions.md](fx-conversions.md), [portfolios.md](portfolios.md) and [trade-fees.md](trade-fees.md). It is the second slice of the custody modes thread ([future-plans.md](../future-plans.md) §9), after [account-custody-mode.md](account-custody-mode.md), which added the attribute with no behaviour.

This slice lets a caller open a **Custodial** account and ships every rule that depends on the custody mode in one change: which account type a Custodial account has, which transaction types a caller may enter on it, the same custody mode on every leg account of a Transaction, who sets `bookedAt`, and which accounts may follow a portfolio. The direction for these rules is [ADR 0020](../adr/0020-account-custody-mode.md) item 5.

No new route, no new policy, no new `TransactionType`, no problem code. Script `0032_remove_external_portfolio_assignments.sql` changes data only.

**Status is `accepted` (2026-11-17) and landed** on branch `feature/custody-modes`: Custodial accounts open as `Brokerage` only, the manual entry matrix (`CustodyRules`), the same custody mode on transfer, conversion and quote, the server-set `bookedAt`, portfolios on Custodial accounts only with script `0032_remove_external_portfolio_assignments.sql`, the Custodial demo account in `TestSeed`, and the Adviser Portal forms. The amendment notes in the specs above, in the two Adviser Portal page contracts, in domain-model, database-design, api-design, the glossary and the known issue landed with the implementation.

---

## 1. Summary

A TenantAdmin or Adviser opens a Custodial account for a Customer, funds it with `TransferIn`, trades it with `Buy` / `Sell`, moves cash with `InternalTransfer` and `FxConversion`, and may assign a portfolio to it. An External account keeps every transaction type it has today.

Three invariants hold from the first Custodial account on:

| # | Invariant | Rules |
| --- | --- | --- |
| I1 | The custody mode of an Account never changes. | account-custody-mode R2, R6 (unchanged) |
| I2 | Every leg account of one Transaction has the same custody mode. | R8, R9 |
| I3 | On a Custodial account the server decides the price, the cost, the FX rate, the fee and the booking time. The caller states what to do and how much. | R10–R13, R18 |

After this slice:

| | External account | Custodial account |
| --- | --- | --- |
| Account type | Bank, Cash, Brokerage, Other | Brokerage only |
| Transaction types a caller may enter | Every accepted type (unchanged) | `TransferIn`, `TransferOut`, `CloseOut`, `Buy`, `Sell`, `InternalTransfer`, `FxConversion`, and reverse |
| `bookedAt` | Sent by the caller, required, not in the future (unchanged) | Set by the server. A body that sends it → 400 |
| Transfer and conversion | Only with another External account | Only with another Custodial account |
| Portfolio assignment and rebalance | Not allowed | Allowed |
| Price, FX rate, trade fee on `Buy` / `Sell` | Server (unchanged; caller numbers are a later slice) | Server |

The rule that distinguishes this slice: all of these rules land together. `Custodial` must not become selectable in a deployed build that lacks any of them.

---

## 2. Scope

**In**

- Domain: `Account.Create` requires `Brokerage` for a Custodial account. `CustodyRules.AllowsManualEntry(custodyMode, transactionType)`, the one place the manual entry matrix lives. `Transaction.PostInternalTransfer` and `Transaction.PostFxConversion` take both Accounts' custody modes and refuse different ones
- Application: `CreateAccount` accepts `Custodial`. `CreateTransaction` applies the manual entry matrix. `CreateInternalTransfer`, `CreateFxConversion` and `GetFxConversionQuote` require the same custody mode on both Accounts. All four take `bookedAt` by custody mode. `AssignPortfolio` and `RebalanceAccount` require a Custodial account
- Script `database/schema/0032_remove_external_portfolio_assignments.sql` (data only; forward only)
- HTTP: `POST /accounts` accepts `custodyMode: "Custodial"`. `bookedAt` on the four transaction bodies depends on the custody mode. No new field, route or policy
- Adviser Portal: custody selector on the New account form; transaction forms, action buttons and account pickers follow the custody mode; the Assign control is offered on Custodial accounts only
- `TestSeed`: one Custodial demo account; the demo portfolio assignment moves to it
- Known gaps recorded in [known-issues](../known-issues/custodial-account-gaps.md)
- Tests listed in §10

**Out**

- A tenant setting for which custody modes may be opened ([future-plans.md](../future-plans.md) §9 slice 3). Every tenant may open both
- Caller-supplied price, fee and rate on External accounts (slice 4). Server pricing and trade fee schedules still apply to both modes
- Grouping by custody mode in net worth, the two reports and the portal dashboards (slice 5). Net worth and reports count a Custodial account like any other account
- Changing the custody mode of an existing account. Moving outside assets onto the platform is a deposit, not a change of mode (ADR 0020 item 2)
- The platform-side flows that later replace hand entry on a Custodial account: deposits and withdrawals matched to a bank, an interest job, corporate action events, in-kind transfer, migration batches, orders, a controlled manual adjustment
- Checking a past `bookedAt` on an External account against the balance on that date (backdating; [ledger-write-guards](../fixes/ledger-write-guards.md) Out of scope)
- A database constraint for I2 or for "an Active assignment is on a Custodial account". Both are held in Domain and Application
- Any change to `Other`, `Property` or `Credit`
- Customer Portal changes. The page contract already shows "Held on platform" for a Custodial account
- A rule matrix or capability list on the account item. The portals keep their own copy of the matrix; the server is the authority

---

## 3. Stories

1. As an Adviser I open a Custodial account for an assigned Customer. It is a Brokerage account in a currency I choose.
2. As an Adviser I record a deposit on a Custodial account with `TransferIn` and a withdrawal with `TransferOut`. I do not choose the booking time.
3. As an Adviser I buy and sell on a Custodial account. The server prices the trade, charges the trade fee and books it at the current time.
4. As an Adviser I cannot type a position, a cost, a balance or interest into a Custodial account (400): no `Opening`, `OpeningHoldings`, `StockIn`, `StockOut`, `Split`, `Bonus`, `Scrip` or `Interest`.
5. As an Adviser I move cash between two Custodial accounts with a transfer or a conversion. I cannot move cash between a Custodial and an External account in one Transaction (400). I record a withdrawal on the Custodial account, and, if the Customer's outside account is also recorded, a deposit there.
6. As an Adviser I assign a portfolio to a Custodial account. I cannot assign one to an External account (400), because the platform does not execute that account's trades.
7. As an Adviser I keep working on External accounts exactly as today, except for portfolios and for transfers to a Custodial account.
8. As a Customer I see a Custodial account, its holdings and its activity like any other account of mine.
9. As a later slice I replace one hand-entered type on Custodial accounts with a platform-side flow by changing one cell of the manual entry matrix.

---

## 4. Rules

### 4.1 Opening a Custodial account

| ID | Rule |
| --- | --- |
| R1 | Unchanged access rules: accounts R1–R6, posting R1–R4a, leg-accounts R6–R8, portfolios R1–R3 and R16. 401 / 403 / 404, PublicId only, `tenantId` rules for TenantAdmin / Adviser / SystemAdmin, Customer and tenant state. A Customer has no write. |
| R2 | `POST /accounts` accepts `custodyMode: "Custodial"`. This supersedes account-custody-mode R4. The value is still required, has no default, and is matched by exact enum name (account-custody-mode R3). |
| R3 | A Custodial account has type `Brokerage`. `Custodial` with `Bank`, `Cash` or `Other` → 400 with `errors.Type` and the message `A Custodial account must be a Brokerage account.` Nothing is written. An External account may have any openable type, as today. |
| R4 | R2 and R3 apply to every caller that can create an account: TenantAdmin, Adviser, and SystemAdmin with `tenantId`. No tenant restricts the value in this slice. Currency (accounts R9), the Disabled-Customer rule (accounts R6), close, reopen and rename are the same for both modes. The custody mode stays immutable, and a PUT body that includes it → 400 (account-custody-mode R6). |

### 4.2 Manual entry matrix

| ID | Rule |
| --- | --- |
| R5 | **Matrix.** A caller may enter a `TransactionType` on an Account only when the matrix below allows it for the Account's custody mode. The matrix covers every create route and reverse. |
| R6 | **Refusal.** A type the matrix refuses on `POST /transactions` → 400 with `errors.Type` and the message in §4.2.1. No Transaction and no idempotency record is written. The check runs in the handler after the Account is loaded and locked and after the Closed-account and Disabled-Customer checks (posting R6, R7). It runs before the portfolio trade lock (portfolios R18) and before every type-specific check (instrument, price, holding, cash). Request-shape validation still runs first, as today. An Account the caller cannot see is still 404. |
| R7 | **Hand entry only.** The matrix is a rule about what a caller may enter. It lives in one Domain function and is called by Application. The `Transaction` factories do not call it: the test seed and later platform-side flows build Transactions directly, the same split as cross-currency-trades R5. Rebalance posts only `Buy` / `Sell`, which the matrix allows. |

| Type | External | Custodial | On a Custodial account, replaced later by |
| --- | --- | --- | --- |
| `TransferIn` / `TransferOut` | allowed | allowed, amount entered by hand (**transitional**) | Deposits matched to a bank statement; withdrawals with review |
| `CloseOut` | allowed | allowed (**transitional**) | An account closure flow |
| `Buy` / `Sell` | allowed | allowed | Orders |
| `InternalTransfer` | allowed, both Accounts External | allowed, both Accounts Custodial | — |
| `FxConversion` | allowed, both Accounts External | allowed, both Accounts Custodial | A conversion with a spread |
| `Reversal` (reverse) | allowed | allowed | Cancel and re-enter |
| `Interest` | allowed | **refused** | An interest job |
| `Opening` / `OpeningHoldings` | allowed | **refused** | Migration batches; in-kind transfer |
| `StockIn` / `StockOut` | allowed | **refused** | In-kind transfer |
| `Split` / `Bonus` / `Scrip` | allowed | **refused** | Corporate action events |
| `Dividend` | reserved, not accepted | reserved, not accepted | — |

The External column is today's behaviour. Nothing in it changes.

`CloseOut` stays allowed on a Custodial account. A `Buy` / `Sell` cash leg is rounded to 4 decimals (buy-sell R12), so a balance can hold a part smaller than the currency's minor unit. `TransferOut` cannot carry that part (posting R8), and `CloseOut` is the only type that can bring such a balance to zero so that the Account can close ([ledger-write-guards](../fixes/ledger-write-guards.md) Decision 5). It is a withdrawal of the whole balance and gives a caller nothing that `TransferOut` does not.

#### 4.2.1 Refusal messages

| Types | `errors.Type` message |
| --- | --- |
| `Interest` | `Interest cannot be entered by hand on a Custodial account.` |
| `Opening`, `OpeningHoldings` | `A Custodial account starts empty. Fund it with TransferIn.` |
| `StockIn`, `StockOut` | `Securities cannot be moved in or out of a Custodial account by hand. Use Buy and Sell.` |
| `Split`, `Bonus`, `Scrip` | `Corporate actions cannot be entered by hand on a Custodial account.` |

### 4.3 Same custody mode on every leg account

| ID | Rule |
| --- | --- |
| R8 | **Transfer and conversion.** On `POST /transactions/transfer`, `POST /transactions/fx` and `POST /transactions/fx/quote`, the from Account and the to Account have the same custody mode. Different modes → 400 with `errors.ToAccountId` and the message `Both accounts must have the same custody mode.` Nothing is written. The check runs after both Accounts are found (an Account the caller cannot see is still 404; internal-transfers R6, fx-conversions R6) and before R10. When this rule and another 400 rule of the same command both refuse (currency, Customer, state), which message the response carries is not fixed. |
| R9 | **Reverse.** No new rule. Every type other than `InternalTransfer` and `FxConversion` books into one Account, and R8 holds for those two, so the leg accounts of every posted Transaction already share one custody mode. A Reversal books into the same Accounts (leg-accounts R12). A Transaction of a type that the matrix refuses on a Custodial account cannot exist there, because Custodial accounts start empty. |

Money that leaves a Custodial account for the Customer's own outside bank account is a `TransferOut` on the Custodial account. If that outside account is recorded as an External account, the Adviser may record a `TransferIn` there. The two Transactions are not linked: they are two events in two different books.

### 4.4 Booking time

| ID | Rule |
| --- | --- |
| R10 | **Custodial: the server sets `bookedAt`.** When the leg accounts are Custodial, a body that includes `bookedAt` → 400 with `errors.BookedAt` and the message `bookedAt cannot be set on a Custodial account. The server sets it.` This holds for any value, including the current time. It applies to `POST /transactions`, `POST /transactions/transfer`, `POST /transactions/fx` and `POST /transactions/fx/quote`. `"bookedAt": null` is the same as leaving the field out. |
| R11 | **External: unchanged.** `bookedAt` stays required on the three create routes, with `Z` or an offset, and not later than the server clock plus 5 minutes (posting R24). Missing → 400 with `errors.BookedAt`, as today. On the quote it stays optional and defaults to now (fx-conversions R20). The "required" check moves from the request validator to the handler, because it now needs the Account. The offset rule ([utc-instants](../fixes/utc-instants.md)) and the future rule are unchanged. |
| R12 | **The server's time.** For a Custodial write the handler reads the clock once from `TimeProvider`, after every leg account is locked (leg-accounts R11). That instant is the stored `BookedAt`, and it is the date for every lookup the write makes: the price (buy-sell R10), the FX rate and its staleness (buy-sell R12, fx-conversions R9, R10) and the trade fee schedule (trade-fees R7). An idempotent replay returns the original id and does not read the clock again. The request hash is unchanged. |
| R13 | **Domain.** The `Transaction` factories keep their `bookedAt` parameter and do not read the custody mode. R10–R12 are request rules in Application, the same split as [ledger-write-guards](../fixes/ledger-write-guards.md) Decision 7. The test seed, and a later simulator, may build dated history on a Custodial account. Rebalance and Reversal already use the server clock. |

Why the server sets the time: a caller who can choose `bookedAt` on a Custodial account can post a withdrawal dated before the deposit that funds it, and can choose the price date of a trade. The ledger cannot yet check a balance as of a date. Taking the choice away closes this on Custodial accounts without that check. The cost is that a deposit entered today cannot be dated yesterday, and that history on a Custodial account can be built only by the seed.

### 4.5 Portfolios

| ID | Rule |
| --- | --- |
| R14 | **Assign.** Only an Open **Custodial** account may be assigned a portfolio. This replaces the type test in portfolios R4 ("Brokerage or Other"); a Custodial account is always `Brokerage` (R3). An External account of any type, or a Closed account → 400 `account_not_eligible` with the message `Only an open Custodial account can be assigned a portfolio.` No assignment is written. |
| R15 | **Rebalance.** `POST /accounts/{id}/rebalance` and the rebalance job require an Open Custodial account with an Active assignment, else `account_not_eligible` with the message `The account is not an open Custodial account with an active portfolio.` (400 on the synchronous call; a recorded failure in the job). This replaces the same type test in `RebalanceAccount`. |
| R16 | **Existing assignments.** After [account-custody-mode](account-custody-mode.md) every existing account is External. Script `0032` sets every Active assignment on an External account to `Removed`. It writes no trade and leaves holdings as they are, the same outcome as an unassign (portfolios R15). After the script, an Active assignment exists only on a Custodial account, and R14 keeps it so. The portfolio-wide commands (`PUT /portfolios/{id}`, `POST /portfolios/{id}/rebalance`, `POST /portfolios/{id}/disable`) act on Active assignments and need no change. |
| R17 | **Trade lock.** Portfolios R18 is unchanged. On a Custodial account the matrix (R5) already refuses `StockIn`, `StockOut`, `Split`, `Bonus`, `Scrip` and `OpeningHoldings`, and R6 runs first, so the lock decides only `Buy`, `Sell` and the reverse of a `Buy` or `Sell`. `TransferIn`, `TransferOut`, `CloseOut`, `InternalTransfer` and `FxConversion` stay allowed on an assigned account. |

### 4.6 What does not change

| ID | Rule |
| --- | --- |
| R18 | **On a Custodial account.** `Buy` / `Sell` are priced by the server (buy-sell R9–R12) and carry the trade fee of the tenant's schedule (trade-fees R7, R14). An `FxConversion` uses the reference rate and `expectedRate` (fx-conversions R9–R12). Cash `SUM ≥ 0`, holding quantity `≥ 0`, average cost, the reverse rules including the latest security activity rule, idempotency, close (cash `SUM = 0` and no holdings), reopen, the Disable-Customer guard, net worth, both reports and the Customer's reads work as on any Brokerage account. |
| R19 | **On an External account.** Every rule of today, with two exceptions: it cannot be assigned a portfolio or rebalanced (R14, R15), and it can transfer and convert only with another External account (R8). `Buy` / `Sell` on an External account are still priced by the server and still carry the trade fee. |
| R20 | **Existing data.** No Account and no Transaction is changed. The only data change is R16. |
| R21 | **Account reads.** The account item and the `custodyMode` filter are unchanged (account-custody-mode R7, R8). `GET /accounts?custodyMode=Custodial` now returns rows. |

Policies: none added. `accounts.create` opens a Custodial account. `transactions.create` posts on it and assigns a portfolio to it.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `Account` | Aggregate (existing) | `Create` refuses `Custodial` with a type other than `Brokerage` (`DomainException`). Supersedes "no rule combines `Type` and `CustodyMode`" in account-custody-mode §5. |
| `CustodyRules` | Static rules (new) | `AllowsManualEntry(CustodyMode, TransactionType)` returns the R5 matrix. Defined for every `TransactionType`; `Dividend` is `false` for both modes. No other code holds a copy of the matrix. |
| `Transaction` | Aggregate (existing) | `PostInternalTransfer` and `PostFxConversion` take the custody mode of the from Account and of the to Account, next to each Account's currency, and refuse different modes (`DomainException`). No other factory changes. No factory calls `CustodyRules`, and none loses its `bookedAt` parameter (R7, R13). |
| `CustodyMode`, `AccountPortfolioAssignment`, events | (existing) | Unchanged. |

Invariants:

- A Custodial account is a `Brokerage` account. An External account may have any type.
- Every leg account of a Transaction has the same custody mode. The Domain enforces it where a Transaction can have two leg accounts (`InternalTransfer`, `FxConversion`). `Reverse` copies each leg's Account and cannot break it.
- An Active `AccountPortfolioAssignment` is on a Custodial account. Held in Application (R14) and by script `0032` (R16), like the existing eligibility test.

```text
Account.Create(tenantId, customerId, name, type, custodyMode, currency)
  custodyMode = Custodial requires type = Brokerage
  everything else as in accounts §5

CustodyRules.AllowsManualEntry(custodyMode, type) -> bool
  External:  every type a create route accepts today, and Reversal
  Custodial: TransferIn, TransferOut, CloseOut, Buy, Sell,
             InternalTransfer, FxConversion, Reversal
```

Application passes facts and the Domain holds the invariant. The 400s of R3 and R8 are raised by Application with their field key before the factory is called; the Domain check is the backstop for callers that do not go through HTTP.

Update [domain-model.md](../domain-model.md) §8.5 and §8.6 when this spec is accepted.

---

## 6. Database

Script: `database/schema/0032_remove_external_portfolio_assignments.sql`. Forward only. Data only: no table, column, index or constraint changes. Do not add EF migrations.

| Table | Change | Index / FK |
| --- | --- | --- |
| `AccountPortfolioAssignments` | Rows with `Status = 0` (Active) whose Account has `CustodyMode = 0` (External) get `Status = 1` (Removed), `LastModified` = the current UTC time and `LastModifiedBy = 'system'` | none |
| `Accounts`, `Holdings`, `Transactions` and every other table | none | none |

- One `UPDATE` joined to `Accounts`. It is atomic and safe to run again: a second run finds no row.
- The filter is the Account's custody mode, not "every Active row". When the script runs in an existing environment no Custodial account exists yet, so it removes every Active assignment there. On a database that already has Custodial accounts it leaves their assignments alone.
- The script raises no `PortfolioUnassigned` event and writes no outbox row. No handler depends on that event.
- Holdings that a rebalance bought stay on the External account. With no Active assignment the trade lock no longer applies, and the Adviser trades them by hand.

Existing data:

- Development databases: the assignment of `Demo Growth` to "Demo brokerage" is removed. `TestSeed` then assigns `Demo Growth` to the new Custodial demo account (§9.1).
- Production: the demo seed creates no accounts and no portfolios ([demo-tenant-seed.md](demo-tenant-seed.md) R9). Before the deploy, count the Active assignments; any that were made by hand will be removed, and the Adviser must be told.
- Functional tests create their own rows and are not affected.

Update [database-design.md](../database-design.md) §9 and §11.6 when this spec is accepted.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Command | `CreateAccount` | Accepts `Custodial`. `Custodial` with a type other than `Brokerage` → 400 `errors.Type` (R3). A validator rule next to the existing `type` and `custodyMode` rules: it needs no database read. |
| Command | `CreateTransaction` | After the Account is loaded: the manual entry matrix (R5, R6), then `bookedAt` by custody mode (R10–R12). The instant from R12 replaces `request.BookedAt` everywhere the handler uses it. |
| Command | `CreateInternalTransfer` | Same custody mode on both Accounts (R8), then `bookedAt` by custody mode. Passes both custody modes to the factory. |
| Command | `CreateFxConversion` | Same as `CreateInternalTransfer`. The rate is read for the instant of R12. |
| Query | `GetFxConversionQuote` | Same custody mode on both Accounts (R8). A body with `bookedAt` for Custodial Accounts → 400 (R10). |
| Command | `ReverseTransaction` | Unchanged (R9). It already uses the server clock. |
| Command | `AssignPortfolio` | Eligibility is "Open and Custodial" (R14). |
| Command | `RebalanceAccount` (and the job) | Eligibility is "Open, Custodial, Active assignment" (R15). The trades it posts are unchanged. |
| Command | `UpdateAccount`, `CloseAccount`, `ReopenAccount`, `UnassignPortfolio`, `UpdatePortfolio`, `RebalancePortfolio`, `DisablePortfolio` | Unchanged. |
| Query | `GetAccounts`, `GetAccountById`, `GetAccountPortfolio`, transaction, holdings, net-worth and report reads | Unchanged. |

Where each rule lives:

| Rule | Where |
| --- | --- |
| Custodial ⇒ Brokerage | `CreateAccountCommandValidator` for the 400; `Account.Create` for the invariant |
| Manual entry matrix | `CustodyRules.AllowsManualEntry` in Domain; called once, by the `CreateTransaction` handler |
| Same custody mode | The transfer, conversion and quote handlers for the 400; the two factories for the invariant |
| `bookedAt` required (External) and refused (Custodial) | One shared helper in `Application/Transactions`, next to `BookedAtRules`, used by the four handlers. It takes the custody mode, the request value and `TimeProvider`, and returns the instant to use. The 400s are `ValidationException` on `BookedAt` |
| `bookedAt` is not in the future | The four validators, as today (`BookedAtRules.NotInTheFuture`). Only the `NotNull` rule leaves the three create validators |
| Portfolio eligibility | `AssignPortfolio` and `RebalanceAccount`, in place of the two "Brokerage or Other" tests |

A refused request stores no idempotency record: every new check throws inside the command's database transaction, before any insert.

---

## 8. API

No new routes. No new policies.

| Method | Route | Change |
| --- | --- | --- |
| POST | `/accounts` | `custodyMode: "Custodial"` is accepted. With it, `type` must be `Brokerage` |
| POST | `/transactions` | The Account's custody mode decides which `type` is accepted and whether `bookedAt` is sent |
| POST | `/transactions/transfer` | Both Accounts have the same custody mode. `bookedAt` by custody mode |
| POST | `/transactions/fx`, `/transactions/fx/quote` | Same |
| POST | `/accounts/{id}/portfolio`, `/accounts/{id}/rebalance` | The Account must be Custodial |
| GET | `/accounts?custodyMode=Custodial` | Returns the caller's Custodial accounts |

### 8.1 Bodies

Open a Custodial account (TenantAdmin / Adviser; SystemAdmin adds `tenantId`):

```json
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Managed portfolio",
  "type": "Brokerage",
  "custodyMode": "Custodial",
  "currency": "NZD"
}
```

Deposit on a Custodial account. There is no `bookedAt`:

```json
{
  "accountId": "<custodial account publicId>",
  "type": "TransferIn",
  "amount": 5000.00,
  "memo": "Initial deposit"
}
```

Buy on a Custodial account:

```json
{
  "accountId": "<custodial account publicId>",
  "type": "Buy",
  "security": [{ "instrumentId": "<instrument publicId>", "quantity": 10 }]
}
```

Transfer between two Custodial accounts:

```json
{
  "fromAccountId": "<custodial account publicId>",
  "toAccountId": "<custodial account publicId>",
  "amount": 500.00
}
```

The transaction item does not change. Its `bookedAt` is the instant the server chose, in UTC. The same bodies on an External account keep `bookedAt`, as today.

### 8.2 Errors

All of these are ordinary validation: 400 with `errors`, no problem `code` ([api-design.md](../api-design.md) §4.2 C1), except the two that reuse the existing `account_not_eligible`.

| Case | Response |
| --- | --- |
| `Custodial` with a type other than `Brokerage` on `POST /accounts` | 400, `errors.Type` |
| A type the matrix refuses on a Custodial account | 400, `errors.Type` |
| Transfer, conversion or quote between a Custodial and an External account | 400, `errors.ToAccountId` |
| `bookedAt` in the body for a Custodial account | 400, `errors.BookedAt` |
| No `bookedAt` in a create body for an External account | 400, `errors.BookedAt` (unchanged) |
| Assign a portfolio to an External account, or rebalance one | 400, `code` `account_not_eligible` (existing code; new message) |

This is a breaking change for three kinds of caller. A caller that assigned a portfolio to an External Brokerage or Other account now gets 400. A caller that sends `bookedAt` for a Custodial account gets 400. A caller that moved cash between two accounts gets 400 when their custody modes differ. `webapi` and the Adviser Portal ship in the same deploy.

Cross-tenant or wrong-collection id → **404**, not 403, before any rule of this spec is evaluated.

Keep the resource catalog in [api-design.md](../api-design.md) §7.6 and §7.7 pointing here for these rules.

---

## 9. UI

Page contracts: [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (D17–D20) and [adviser-portal-portfolios.md](../adviser-portal/adviser-portal-portfolios.md) (P13), amended in the same change that accepts this spec. Neither BFF changes: every route is already proxied.

**Adviser Portal**

- New account form: a required "Custody" choice with two options, "External — held elsewhere" and "Custodial — held on the platform". Nothing is preselected. Choosing Custodial sets Type to Brokerage and disables the Type control.
- Account page and the transaction form: the action buttons and the type selector offer only the types the matrix allows for the account's custody mode. On a Custodial account: TransferIn, TransferOut, CloseOut, Transfer, Convert, Buy, Sell, Reverse, Close.
- On a Custodial account the transaction, transfer and convert forms have no "Booked at" input and send no `bookedAt`. They show "Booked when posted." in its place. The convert form's quote request sends no `bookedAt` either.
- Transfer and convert account pickers: an account with a different custody mode stays listed, greyed out, with the reason "Different custody mode", like the existing Closed and currency reasons.
- The Assign portfolio control is offered on an Open Custodial account only.

**Customer Portal**

None. The account pages already show "Held on platform" for a Custodial account ([customer-portal.md](../customer-portal/customer-portal.md) amendment 2026-11-17). Grouping is a later slice.

### 9.1 Seeds

`TestSeed` (Development and TestAppHost), idempotent as today. Like the four accounts of accounts §9.1, the account is created for every TestSeed firm (`demo` and the Development fixture `demo2`), each on its own demo Customer:

| Name | Type | Custody mode | Currency | Status |
| --- | --- | --- | --- | --- |
| Demo custodial brokerage | Brokerage | Custodial | USD | Open |

- Owned by the firm's demo Customer, next to the four accounts of accounts §9.1, which stay External.
- Transactions, built through the Domain factories with the seed's own dates, priced from the seeded `InstrumentPrices` (as of 2026-01-01) like the existing demo `Buy`. Each is idempotent on its `reference`:

| Booked at | Type | Detail | Reference |
| --- | --- | --- | --- |
| 2026-01-20 10:00 +13:00 | `TransferIn` | `USD 20,000.00` | `TestSeed-DemoCustodial-Funding` |
| 2026-01-20 10:30 +13:00 | `Buy` | `VTI` × 40 at 250 = `USD 10,000.00` | `TestSeed-DemoCustodial-BuyVti` |
| 2026-01-20 11:00 +13:00 | `Buy` | `AAPL` × 20 at 180 = `USD 3,600.00` | `TestSeed-DemoCustodial-BuyAapl` |

- The cash balance after the seed is `USD 6,400.00`. No seeded trade carries a fee: the seeded trade fee schedule starts after the seed date. The `Holdings` rows for `VTI` and `AAPL` are synced like those of "Demo brokerage".
- The `Demo Growth` assignment is created on this account, and no longer on "Demo brokerage". The seed still posts no rebalance trades. `AAPL` is not in `Demo Growth`, so the first rebalance in a demo has something to sell.
- "Demo brokerage" stays External and keeps its hand-entered demo data (`OpeningHoldings`, `StockIn`, `Split`). On a database that existed before this slice, script `0032` has removed its assignment; the seed does not write it again.
- The Development smoke seed (`Smoke Cash`) and the demo seed ([demo-tenant-seed.md](demo-tenant-seed.md)) do not change.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `Account.Create`: Custodial + Brokerage is created; Custodial + Bank / Cash / Other throws; External + each openable type is created. `CustodyRules.AllowsManualEntry`: one case per `(custody mode, type)` pair, equal to the R5 table. `PostInternalTransfer` and `PostFxConversion`: two different custody modes throw; two equal modes build the legs as today |
| Application.FunctionalTests — open | Custodial + Brokerage → 201, and get returns `custodyMode: "Custodial"`; Custodial + Bank, Cash and Other → 400 with `errors.Type` and no account written; Adviser, TenantAdmin and SystemAdmin follow the same rules; PUT with `custodyMode` is still 400; `?custodyMode=Custodial` returns the Custodial account and no External one; two-tenant: the filter returns no account of tenant B |
| Application.FunctionalTests — matrix | On a Custodial account: `TransferIn`, `TransferOut` → 201; `Buy` and `Sell` → 201 with a server price, and with a `Fee` leg when a schedule applies; `CloseOut` of the full balance → 201, then close → 204; `Interest`, `Opening`, `OpeningHoldings`, `StockIn`, `StockOut`, `Split`, `Bonus`, `Scrip` → 400 with `errors.Type`, no Transaction and no idempotency record. Reverse of a `TransferIn` and of a `Buy` → 201. Another tenant's Custodial account id → 404, not 400. Every existing suite still passes on External accounts |
| Application.FunctionalTests — same custody mode | Transfer External → Custodial and Custodial → External → 400 with `errors.ToAccountId`, no Transaction; Custodial → Custodial → 201 and its reverse → 201; the same four cases for `POST /transactions/fx`; the quote between two modes → 400 |
| Application.FunctionalTests — `bookedAt` | Custodial `TransferIn` without `bookedAt` → 201, and the item's `bookedAt` lies between the instants the test took before and after the call; with `bookedAt` (now, in the past, in the future) → 400 with `errors.BookedAt` and nothing written; the same for a transfer and a conversion between Custodial accounts; the quote with `bookedAt` → 400, without → 200; a replay with the same `Idempotency-Key` returns the same id and writes one Transaction. External: no `bookedAt` → 400 and a past `bookedAt` → 201 (existing tests) |
| Application.FunctionalTests — portfolios | Assign to an Open Custodial account → 200 and `Buy` trades; assign to an External Brokerage and to an External Other account → 400 `account_not_eligible`, no assignment row; rebalance of an External account that has an Active assignment (row inserted by the test, as from before the script) → 400 `account_not_eligible`. On an assigned Custodial account: manual `Buy`, `Sell` and the reverse of a rebalance `Buy` → 400 from the trade lock; `TransferIn`, `TransferOut`, a transfer and a conversion → 201 |
| Infrastructure.IntegrationTests | Script `0032` on a scratch database (as for `0024` and `0031`): an Active assignment on an External account becomes Removed with `LastModified` set; a Removed row is not touched; an Active assignment on a Custodial account stays Active; `Holdings` and `Transactions` row counts are unchanged; a second run changes nothing. `TestSeed`: the five demo accounts exist, four External and one Custodial; the Custodial one has its three transactions, its two holdings and the Active `Demo Growth` assignment; "Demo brokerage" has no Active assignment; a second run adds nothing |
| Adviser Portal | New account form: no option preselected, submit is blocked until one is chosen, Custodial fixes Type to Brokerage, and the POST carries the chosen `custodyMode`. Account page and transaction form: a Custodial account offers only the allowed types and no "Booked at" input, and the POST has no `bookedAt`; an External account is unchanged. Transfer and convert pickers grey out an account with a different custody mode. The Assign control is present on a Custodial account and absent on an External one |

Where the two-tenant tests live: every new rule is applied in a handler after the Account lookup, so the cross-tenant cases are proved over HTTP in Application.FunctionalTests, next to the existing isolation tests of each route.

Assertions from the first slice that this spec replaces (the spec changed; the tests are not weakened): "`Custodial` → 400" on `POST /accounts` becomes "Custodial + Brokerage → 201", and "`?custodyMode=Custodial` returns an empty page" becomes "returns the Custodial accounts".

Fan-out in the functional tests:

- `AccountHttp.OpenAccountAsync` gains a `custodyMode` parameter that defaults to `External`.
- Twelve functional test files use the portfolio routes. The ones that assign a portfolio open their accounts as Custodial and fund them with `TransferIn` without `bookedAt`. None of them builds a position with `OpeningHoldings` or `StockIn`. This moves in one test-only commit before the eligibility rule changes (§12, commit 6): a Custodial Brokerage account also passes today's "Brokerage or Other" test, so that commit passes on its own.
- `InternalTransferPortfolioTests` and `FxConversionPortfolioTests` make both Accounts Custodial.
- `TradeLockTests` covers a manual `Buy` and the reverse of a `Buy` on an assigned account. Both are still decided by the trade lock (R17), so its assertions do not change.
- The seed account lists in `TestSeedSmokeTests` and `DevelopmentSeedSecondFirmTests` (functional) and `TestSeedAccountsTests` (integration) gain "Demo custodial brokerage"; `Seed_OpensEveryDemoAccountAsExternal` becomes "four External and one Custodial". These change in §12 commit 5.
- `RebalanceJobTenantScopeTests` (integration) builds an External account with an Active assignment; it opens a Custodial account in §12 commit 6.
- The Domain unit tests and integration tests that call `PostInternalTransfer` or `PostFxConversion` pass the two custody modes from §12 commit 3.
- The Fees tests already insert a schedule that starts today and book trades at the current time ([ledger-write-guards](../fixes/ledger-write-guards.md) B3), so a `Buy` on a Custodial account gets a fee without a future date.

---

## 11. Locked in this spec

No new ADR. [ADR 0020](../adr/0020-account-custody-mode.md) item 5 gave the direction; this spec makes it rules and decides the point that ADR left open (who sets the booking time).

| Item | Lock |
| --- | --- |
| Custodial account type | `Brokerage` only |
| Manual entry matrix | §4.2. One Domain function. Factories do not call it |
| Same custody mode | Every leg account of a Transaction. Refused on transfer, conversion and quote |
| `bookedAt` | Custodial: server clock, refused in the body. External: required, as today |
| Portfolios | Assign and rebalance on Custodial accounts only |
| Existing assignments | Removed by script `0032`. No trades |
| Existing accounts | Stay External. Custodial accounts start empty |
| Server pricing and trade fees | Both modes, unchanged |
| Script | `0032_remove_external_portfolio_assignments.sql`, data only, forward only |
| Routes, policies, problem codes | None added |
| Delivery | All rules of §4 land in one merge |

Confirmed when this spec was accepted (2026-11-17). Each is written into the rules above as stated here:

1. **The server sets `bookedAt` on a Custodial account** (R10–R13). The alternative is to keep the caller's `bookedAt` on both modes and refuse only a future time, as today.
2. **Existing portfolio assignments are removed** (R16), and every existing account stays External. The alternative is to turn the Brokerage accounts that have an Active assignment into Custodial accounts in the script, which keeps the demo data connected but puts hand-entered history into Custodial accounts.
3. **A Custodial account cannot record a split** until corporate action events exist (R5). A holding in an instrument that really splits then shows a wrong market value. Recorded in [known-issues](../known-issues/custodial-account-gaps.md).
4. **`CloseOut` stays allowed on a Custodial account** (R5). The planning notes refused it and pointed to `TransferOut`. A balance below the currency's minor unit, which any `Buy` / `Sell` can leave, would then make the account impossible to close. The alternative is to refuse it and accept that gap until trade cash legs are rounded to the currency ([cross-currency-trades.md](cross-currency-trades.md) R9, `draft`).
5. **Nothing is preselected in the Custody choice of the New account form** (§9). The alternative is to preselect External.

Still open, not this slice:

- The tenant setting for allowed custody modes
- Caller-supplied numbers on External accounts
- Read-model and report grouping by custody mode
- Each platform-side flow in the last column of the matrix
- What happens to `Other`, `Property` and `Credit`

---

## 12. Suggested commits

The repository should build after each commit. Agents split each behaviour into red then green.

`Custodial` becomes selectable in commit 1 and its rules follow in commits 2 to 7. The branch is merged and deployed as a whole; do not merge or deploy a prefix of it.

1. `POST /accounts` accepts `Custodial` with `Brokerage` only (R2–R4): validator rule, `Account.Create` invariant, Domain unit tests, functional tests. `AccountHttp.OpenAccountAsync` gains the `custodyMode` parameter. The two first-slice assertions named in §10 change here.
2. Manual entry matrix (R5–R7): `CustodyRules.AllowsManualEntry` with its unit tests; `CreateTransaction` applies it; functional tests.
3. Same custody mode on transfer, conversion and quote (R8, R9): the two factories take the custody modes; the three handlers; unit and functional tests.
4. `bookedAt` by custody mode (R10–R13): the shared helper, the four handlers, the three validators lose `NotNull`; functional tests.
5. `TestSeed`: the Custodial demo account, its three transactions, and the `Demo Growth` assignment on it (§9.1) + integration tests.
6. Test-only: the portfolio suites open Custodial accounts and fund them without `bookedAt` (§10). No production change. Passes on its own.
7. Portfolios on Custodial accounts only (R14–R17): `AssignPortfolio`, `RebalanceAccount`, script `0032` with its integration test; functional tests.
8. Adviser Portal: Custody choice on the New account form (D17) + frontend tests.
9. Adviser Portal: types, "Booked at" and account pickers by custody mode (D18–D20) + frontend tests.
10. Adviser Portal: Assign control on Custodial accounts only (P13) + frontend tests.
11. Repo docs: mark this file landed (done 2026-11-17). (This file and the amendments in the ledger specs, the portal contracts, domain-model, database-design, api-design, the glossary and the known issue were accepted on 2026-11-17, before commit 1.)

Do not put a tenant setting, caller-supplied prices, or read-model grouping in these commits.
