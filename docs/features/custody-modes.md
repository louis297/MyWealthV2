---
title: Custody modes
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-17
last_updated: 2026-11-26
related:
  - account-custody-mode.md
  - tenant-custody-modes.md
  - external-caller-numbers.md
  - custody-mode-grouping.md
  - accounts.md
  - posting.md
  - leg-accounts.md
  - internal-transfers.md
  - fx-conversions.md
  - portfolios.md
  - rebalances.md
  - transaction-source.md
  - adjustments.md
  - adjustment-review.md
  - client-money-accounts.md
  - deposit-matching.md
  - withdrawals.md
  - account-closure.md
  - interest-run.md
  - corporate-actions.md
  - dividends.md
  - orders.md
  - migration-batches.md
  - in-kind-transfers.md
  - ../fixes/ledger-write-guards.md
  - ../adr/0020-account-custody-mode.md
  - ../adr/0021-outside-parties-behind-ports.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../adviser-portal/adviser-portal-portfolios.md
  - ../known-issues/custodial-account-gaps.md
---

# Custody modes

The rules on `webapi` that depend on the custody mode of an Account: which account type a Custodial account has, which transaction types a person may enter by hand (the **manual entry matrix**), the same custody mode on every leg account of a Transaction, who sets `bookedAt`, and which accounts may follow a portfolio. The direction is [ADR 0020](../adr/0020-account-custody-mode.md) item 5. It is the second slice of the custody modes thread ([future-plans.md](../future-plans.md) §9), after [account-custody-mode.md](account-custody-mode.md), which added the attribute with no behaviour.

This file states the rules as they are today. Each platform-side flow that writes on a Custodial account has its own spec; this spec owns the matrix and names the flow behind each cell (§4.2). §12 lists what changed here, by date.

**Status is `accepted` (2026-11-17) and landed.** This spec adds no route, no policy, no `TransactionType` and no problem code. Script `0032_remove_external_portfolio_assignments.sql` changes data only.

---

## 1. Summary

A TenantAdmin or Adviser opens a Custodial account for a Customer. From then on the platform writes its Transactions from outside events: a bank statement line, a payment, an order fill, a corporate action, an interest run, an in-kind transfer, a migration batch. A person enters by hand only a cash transfer between two Custodial accounts of one Customer, a conversion between them, the reverse of such a hand entry, and a request for an adjustment that a second person approves. An External account keeps every hand-entered type.

Three invariants hold:

| # | Invariant | Rules |
| --- | --- | --- |
| I1 | The custody mode of an Account never changes. | account-custody-mode R2, R6 |
| I2 | Every leg account of one Transaction has the same custody mode. | R8, R9 |
| I3 | On a Custodial account the server decides the price, the cost, the FX rate, the fee and the booking time. The caller states what to do and how much. On an External account the caller states the numbers. | R10–R13, R18, R19 |

| | External account | Custodial account |
| --- | --- | --- |
| Account type | Bank, Cash, Brokerage, Other | Brokerage only |
| May be opened when | The Tenant allows External accounts | The Tenant allows Custodial accounts and has an active client money account in the currency |
| What a person enters by hand | Every type a create route accepts | `InternalTransfer` (same Customer), `FxConversion`, the reverse of a hand entry, an adjustment request |
| `bookedAt` | Sent by the caller, required, not in the future | Set by the server. A body that sends it → 400 |
| Transfer and conversion | Only with another External account | Only with another Custodial account |
| Portfolio assignment and rebalance | Not allowed | Allowed |
| Price, fee and FX rate | From the caller | From the broker on a fill; the reference rate on a conversion |
| Deposit reference | None | Every account has one |

---

## 2. Scope

**In**

- Domain: `Account.Create` requires `Brokerage` for a Custodial account. `CustodyRules`, the one place the manual entry matrix lives. `Transaction.PostInternalTransfer` and `Transaction.PostFxConversion` take both Accounts' custody modes and refuse different ones
- Application: `CreateAccount` accepts `Custodial`. `CreateTransaction` applies the manual entry matrix. `CreateInternalTransfer`, `CreateFxConversion` and `GetFxConversionQuote` require the same custody mode on both Accounts. All four take `bookedAt` by custody mode. `AssignPortfolio` and a rebalance require a Custodial account
- Script `database/schema/0032_remove_external_portfolio_assignments.sql` (data only; forward only)
- HTTP: `POST /accounts` accepts `custodyMode: "Custodial"`. `bookedAt` on the four transaction bodies depends on the custody mode
- Adviser Portal: the custody selector on the New account form; transaction forms, action buttons and account pickers follow the custody mode; the Assign control is offered on Custodial accounts only
- `TestSeed`: one Custodial demo account, with the demo portfolio assignment

**Out — owned by another spec**

- Which custody modes a Tenant may open: [tenant-custody-modes.md](tenant-custody-modes.md)
- The client money account a Custodial account needs, and its deposit reference: [client-money-accounts.md](client-money-accounts.md)
- The caller's price, fee and rate on an External account: [external-caller-numbers.md](external-caller-numbers.md)
- Grouping by custody mode in net worth, the reports and the portal dashboards: [custody-mode-grouping.md](custody-mode-grouping.md)
- Each flow that writes on a Custodial account: the spec named in the last column of the matrix (§4.2)
- Which source a person may reverse: [transaction-source.md](transaction-source.md)

**Out — not built**

- Changing the custody mode of an existing account. Moving outside assets onto the platform is a migration batch or an in-kind transfer, not a change of mode (ADR 0020 item 2)
- Checking a past `bookedAt` on an External account against the balance on that date (backdating; [ledger-write-guards](../fixes/ledger-write-guards.md) Out of scope)
- A database constraint for I2 or for "an Active assignment is on a Custodial account". Both are held in Domain and Application
- Any change to `Other`, `Property` or `Credit`
- A rule matrix or capability list on the account item. The portals keep their own copy of the matrix; the server is the authority

---

## 3. Stories

1. As an Adviser I open a Custodial account for an assigned Customer. It is a Brokerage account in a currency in which my firm has a client money account.
2. As an Adviser I cannot type a deposit, a withdrawal, a trade, a position, a cost, a balance, interest or a dividend into a Custodial account (400). The platform writes each of them from the flow that owns it.
3. As an Adviser I move cash between two Custodial accounts of one Customer with a transfer or a conversion. I do not choose the booking time. I cannot move cash between a Custodial and an External account in one Transaction (400).
4. As an Adviser I assign a portfolio to a Custodial account. I cannot assign one to an External account (400), because the platform does not execute that account's trades.
5. As an Adviser I keep working on External accounts with every hand-entered type and my own numbers, except for portfolios and for transfers to a Custodial account.
6. As a Customer I see a Custodial account, its holdings and its activity like any other account of mine.

---

## 4. Rules

### 4.1 Opening a Custodial account

| ID | Rule |
| --- | --- |
| R1 | Access rules come from the specs that own them: accounts R1–R6, posting R1–R4a, leg-accounts R6–R8, portfolios R1–R3 and R16. 401 / 403 / 404, PublicId only, `tenantId` rules for TenantAdmin / Adviser / SystemAdmin, Customer and tenant state. A Customer has no write. |
| R2 | `POST /accounts` accepts `custodyMode: "Custodial"`. This supersedes account-custody-mode R4. The value is required, has no default, and is matched by exact enum name (account-custody-mode R3). |
| R3 | A Custodial account has type `Brokerage`. `Custodial` with `Bank`, `Cash` or `Other` → 400 with `errors.Type` and the message `A Custodial account must be a Brokerage account.` Nothing is written. An External account may have any openable type. |
| R4 | R2 and R3 apply to every caller that can create an account: TenantAdmin, Adviser, and SystemAdmin with `tenantId`. Two more checks on open belong to other specs and run in the handler after the validator rules of R2 and R3: the Tenant must allow the custody mode (tenant-custody-modes R7, R8), and a Custodial account needs an active client money account of its Tenant in its currency (client-money-accounts R11, R12). Every Custodial account gets a deposit reference; an External account has none (client-money-accounts R14). The Disabled-Customer rule (accounts R6), close, reopen and rename are the same for both modes. The custody mode stays immutable, and a PUT body that includes it → 400 (account-custody-mode R6). |

### 4.2 Manual entry matrix

| ID | Rule |
| --- | --- |
| R5 | **Matrix.** A person may enter a `TransactionType` on an Account only when the matrix below allows it for the Account's custody mode. The matrix covers every create route and reverse. It applies to every caller, SystemAdmin included. |
| R6 | **Refusal.** A type the matrix refuses on `POST /transactions` → 400 with `errors.Type` and the message in §4.2.1. No Transaction and no idempotency record is written. The check runs in the handler after the Account is loaded and locked and after the Closed-account, closing-account (account-closure R12) and Disabled-Customer checks (posting R6, R7). It runs before the portfolio trade lock (portfolios R18) and before every type-specific check (instrument, price, holding, cash). Request-shape validation still runs first. An Account the caller cannot see is still 404. |
| R7 | **Hand entry only.** The matrix is a rule about what a person may enter. It lives in one Domain class, `CustodyRules`, and is called by Application. The `Transaction` factories do not call it: the test seed and every platform-side flow build Transactions directly through the factory named in the matrix, and record who wrote them as the Transaction's source (transaction-source R2–R6). |

| Type | External, by hand | Custodial, by hand | Written on a Custodial account by |
| --- | --- | --- | --- |
| `TransferIn` | allowed | **refused** | Deposit matching: `Transaction.PostBankDeposit` (deposit-matching R32, R35) |
| `TransferOut` | allowed | **refused** | The payout of a withdrawal: `Transaction.PostPayout` (withdrawals R45) |
| `CloseOut` | allowed | **refused**, for every amount | The remainder write-off of an account closure: `Transaction.PostRemainderWriteOff`, source `AccountClosure` (account-closure R18, R23) |
| `Buy` / `Sell` | allowed | **refused** | An order fill: `Transaction.PostBuy` / `PostSell`, source `OrderFill` (orders R48). A rebalance trades through orders (rebalances R26) |
| `InternalTransfer` | allowed, both Accounts External | allowed, both Accounts Custodial and of the same Customer (withdrawals R47) | Hand entry |
| `FxConversion` | allowed, both Accounts External | allowed, both Accounts Custodial | Hand entry |
| `Interest` | allowed | **refused** | The interest run: `Transaction.PostInterest` (interest-run R29) |
| `Opening` / `OpeningHoldings` | allowed | **refused** | A migration batch: `Transaction.PostMigratedCash` / `PostMigratedHoldings`, source `Migration` (migration-batches R22, R26) |
| `StockIn` / `StockOut` | allowed | **refused** | An in-kind transfer: `Transaction.PostTransferIn` / `PostTransferOut`, source `InKindTransfer` (in-kind-transfers R48) |
| `Split` / `Bonus` | allowed | **refused** | Corporate actions: `Transaction.PostCorporateAction`, source `CorporateAction` (corporate-actions R33) |
| `Scrip` | allowed | **refused** | Nothing. A reinvested dividend is a `Dividend` ([dividends.md](dividends.md)) |
| `Dividend` | not accepted | **refused** | Corporate actions: `Transaction.PostDividend`, source `CorporateAction` (dividends R43) |
| `Adjustment` | **refused** | allowed through an adjustment request that a second person approves, with `transactions.adjust` (adjustment-review R16) | The approval of the request |
| `Reversal` (reverse) | allowed | allowed only for source `Manual`, and never for an `Adjustment` (transaction-source R13, rebalances R28, adjustment-review R15) | Each flow undoes its own writes |

What follows from the matrix:

- On a Custodial account `POST /transactions` refuses every type it accepts. A person writes on a Custodial account only through `POST /transactions/transfer`, `POST /transactions/fx`, `POST /transactions/{id}/reverse` and `POST /adjustment-requests`.
- A Transaction that a person typed on a Custodial account before its cell was refused keeps source `Manual` and can still be reversed by hand, within the reverse rules of its type (withdrawals R45, account-closure R23, orders R48).
- `Dividend` on an External account stays not accepted until income events add it.

Why `CloseOut` exists: a `Buy` / `Sell` cash leg is rounded to 4 decimals (buy-sell R12), so a balance can hold a part smaller than the currency's minor unit. `TransferOut` cannot carry that part (posting R8). On an External account a hand `CloseOut` brings such a balance to zero so that the Account can close ([ledger-write-guards](../fixes/ledger-write-guards.md) Decision 5). On a Custodial account the account closure writes the remainder off (account-closure R18).

#### 4.2.1 Refusal messages

All are ordinary validation, under `errors.Type` on `POST /transactions` unless the row names another key and route.

| Mode | Types | Message |
| --- | --- | --- |
| Custodial | `TransferIn` | `Deposits to a Custodial account come from the bank statement. The customer pays in with the account's deposit instructions.` |
| Custodial | `TransferOut` | `Withdrawals from a Custodial account are paid to the customer's nominated bank account. Request a withdrawal.` |
| Custodial | `CloseOut` | `The remainder of a Custodial account is written off by its account closure. Request an account closure.` |
| Custodial | `Buy`, `Sell` | `Trades on a Custodial account go through the broker. Place an order.` |
| Custodial | `Interest` | `Interest cannot be entered by hand on a Custodial account.` |
| Custodial | `Opening`, `OpeningHoldings` | `Opening balances on a Custodial account come from a migration batch.` |
| Custodial | `StockIn`, `StockOut` | `Securities move in or out of a Custodial account by an in-kind transfer. Request a transfer.` |
| Custodial | `Split`, `Bonus`, `Scrip` | `Corporate actions cannot be entered by hand on a Custodial account.` |
| Custodial | `Dividend` | `Dividends are paid by the platform from the company's announcement.` |
| Custodial | `InternalTransfer` to another Customer's account | `errors.ToAccountId` on `POST /transactions/transfer`: `Cash can be transferred between Custodial accounts of the same customer only.` |
| Custodial | Reverse of a source other than `Manual` | On `POST /transactions/{id}/reverse`: `This transaction was written by the platform and cannot be reversed by hand.` |
| Both | Reverse of an `Adjustment` | On `POST /transactions/{id}/reverse`: `An adjustment cannot be reversed. Request a new adjustment that corrects it.` |
| External | `Adjustment` | `errors.AccountId` on `POST /adjustment-requests`: `An adjustment can be entered only on a Custodial account.` |

### 4.3 Same custody mode on every leg account

| ID | Rule |
| --- | --- |
| R8 | **Transfer and conversion.** On `POST /transactions/transfer`, `POST /transactions/fx` and `POST /transactions/fx/quote`, the from Account and the to Account have the same custody mode. Different modes → 400 with `errors.ToAccountId` and the message `Both accounts must have the same custody mode.` Nothing is written. The check runs after both Accounts are found (an Account the caller cannot see is still 404; internal-transfers R6, fx-conversions R6) and before R10. When this rule and another 400 rule of the same command both refuse (currency, Customer, state), which message the response carries is not fixed. Between two Custodial accounts three more rules belong to other specs: a transfer stays with one Customer (withdrawals R47; a conversion already does, fx-conversions R7), the from side is limited to withdrawable cash (orders R45), and the quote is offered only there (external-caller-numbers R19 refuses a quote between two External accounts). |
| R9 | **Reverse.** No rule of its own. Every type other than `InternalTransfer` and `FxConversion` books into one Account, and R8 holds for those two, so the leg accounts of every posted Transaction share one custody mode. A Reversal books into the same Accounts (leg-accounts R12). A Transaction of a type that the matrix refuses by hand exists on a Custodial account with the source of the flow that wrote it, or with source `Manual` from before its cell was refused. |

Money that leaves a Custodial account for the Customer's own outside bank account is a withdrawal. If that outside account is recorded as an External account, the Adviser may record a `TransferIn` there. The two Transactions are not linked: they are two events in two different books.

### 4.4 Booking time

| ID | Rule |
| --- | --- |
| R10 | **Custodial: the server sets `bookedAt`.** When the leg accounts are Custodial, a body that includes `bookedAt` → 400 with `errors.BookedAt` and the message `bookedAt cannot be set on a Custodial account. The server sets it.` This holds for any value, including the current time. It applies to `POST /transactions`, `POST /transactions/transfer`, `POST /transactions/fx` and `POST /transactions/fx/quote`. `"bookedAt": null` is the same as leaving the field out. On `POST /transactions` the matrix runs first (R6). Every platform-side flow, and the approval of an adjustment request, books at the platform time when it posts (ADR 0021 rule 10, adjustment-review R9). |
| R11 | **External: the caller sets `bookedAt`.** It is required on the three create routes, with `Z` or an offset, and not later than the server clock plus 5 minutes (posting R24). Missing → 400 with `errors.BookedAt`. The "required" check is in the handler, because it needs the Account; the future check is in the validators. The offset rule is [utc-instants](../fixes/utc-instants.md). |
| R12 | **The server's time.** For a Custodial write the handler reads the clock once from `TimeProvider`, after every leg account is locked (leg-accounts R11). That instant is the stored `BookedAt`, and it is the date for every lookup the write makes, such as the FX rate and its staleness on a conversion (fx-conversions R9, R10). An idempotent replay returns the original id and does not read the clock again. The request hash does not include it. |
| R13 | **Domain.** The `Transaction` factories keep their `bookedAt` parameter and do not read the custody mode. R10–R12 are request rules in Application, the same split as [ledger-write-guards](../fixes/ledger-write-guards.md) Decision 7. Only the test seed builds dated history on a Custodial account. |

Why the server sets the time: a caller who can choose `bookedAt` on a Custodial account can post a withdrawal dated before the deposit that funds it, and can choose the price date of a trade. The ledger cannot check a balance as of a date. Taking the choice away closes this on Custodial accounts without that check. The cost is that nothing on a Custodial account can be dated in the past, and that history there can be built only by the seed.

### 4.5 Portfolios

| ID | Rule |
| --- | --- |
| R14 | **Assign.** Only an Open **Custodial** account may be assigned a portfolio. This replaces the type test in portfolios R4 ("Brokerage or Other"); a Custodial account is always `Brokerage` (R3). An External account of any type, or a Closed account → 400 `account_not_eligible` with the message `Only an open Custodial account can be assigned a portfolio.` No assignment is written. Three more refusals of assign belong to other specs: a closing account (account-closure R13: `account_not_eligible`, `Only an open Custodial account that is not being closed can be assigned a portfolio.`), an account with a live order (orders R49: `account_not_eligible`, `Cancel the account's open orders before assigning a portfolio.`) and an account whose rebalance is in progress (rebalances R23: `rebalance_in_progress`). |
| R15 | **Rebalance.** `POST /accounts/{id}/rebalance` and the rebalance job require an Open Custodial account with an Active assignment, else `account_not_eligible` with the message `The account is not an open Custodial account with an active portfolio.` The other conditions of a rebalance (Customer, Tenant, portfolio, broker connection), and when they are checked, are rebalances R6. A rebalance is proposed, confirmed by a person and traded through orders ([rebalances.md](rebalances.md)). |
| R16 | **Existing assignments.** After [account-custody-mode](account-custody-mode.md) every existing account was External. Script `0032` set every Active assignment on an External account to `Removed`. It wrote no trade and left holdings as they were, the same outcome as an unassign (portfolios R15). An Active assignment exists only on a Custodial account, and R14 keeps it so. |
| R17 | **Trade lock.** On a Custodial account with an Active assignment only its rebalance places orders: `POST /orders` is refused (orders R8), and a hand reverse of a `Buy` or `Sell` typed before orders is refused by the lock (rebalances R24). On reverse the source check runs before the trade lock (transaction-source R14). A transfer, a conversion, an adjustment, an entitlement posting and every cash write of a flow stay outside the lock. |

### 4.6 What the two modes share

| ID | Rule |
| --- | --- |
| R18 | **On a Custodial account.** A trade is the fill of an order, priced by the broker, with the fee of its order (orders R32, R33). An `FxConversion` uses the reference rate and `expectedRate` (fx-conversions R9–R12). Cash `SUM ≥ 0`, holding quantity `≥ 0`, average cost, idempotency, reopen, the Disable-Customer guard, net worth, both reports and the Customer's reads work as on any Brokerage account. `POST /accounts/{id}/close` is refused on a Custodial account: it closes through an account closure ([account-closure.md](account-closure.md)). |
| R19 | **On an External account.** Every rule of the ledger specs, with these differences from a Custodial account: it cannot be assigned a portfolio or rebalanced (R14, R15); it transfers and converts only with another External account (R8); the caller states the price, the fee and the rate (external-caller-numbers §4.2, §4.4); it takes no adjustment (§4.2). |
| R20 | **Existing data.** No Account and no Transaction was changed by this spec. The only data change is R16. |
| R21 | **Account reads.** The account item and the `custodyMode` filter are those of account-custody-mode R7, R8. `GET /accounts?custodyMode=Custodial` returns the caller's Custodial accounts. |

Policies: none added. `accounts.create` opens a Custodial account. `transactions.create` posts a transfer or a conversion on it and assigns a portfolio to it.

---

## 5. Domain

| Type | Kind | What it holds |
| --- | --- | --- |
| `Account` | Aggregate | `Create` refuses `Custodial` with a type other than `Brokerage` (`DomainException`). Supersedes "no rule combines `Type` and `CustodyMode`" in account-custody-mode §5. |
| `CustodyRules` | Static rules | The manual entry matrix, defined for every `TransactionType`. No other code holds a copy. |
| `Transaction` | Aggregate | `PostInternalTransfer` and `PostFxConversion` take the custody mode of the from Account and of the to Account, next to each Account's currency, and refuse different modes (`DomainException`). No factory calls `CustodyRules`, and none loses its `bookedAt` parameter (R7, R13). |

```text
Account.Create(tenantId, customerId, name, type, custodyMode, currency)
  custodyMode = Custodial requires type = Brokerage
  everything else as in accounts §5

CustodyRules
  RefusalMessage(custodyMode, type) -> string?        the §4.2.1 message, or null when allowed
  AllowsManualEntry(custodyMode, type) -> bool        RefusalMessage is null
  AllowsHandReverse(custodyMode, sourceKind) -> bool  External: always. Custodial: Manual only
  AllowsCustodialTransfer(custodyMode, fromCustomerId, toCustomerId) -> bool
                                                      External: always. Custodial: same Customer
  CloseOutRefusal(custodyMode, ...) -> string?        External: null. Custodial: the CloseOut message
```

Invariants:

- A Custodial account is a `Brokerage` account. An External account may have any type.
- Every leg account of a Transaction has the same custody mode. The Domain enforces it where a Transaction can have two leg accounts (`InternalTransfer`, `FxConversion`). `Reverse` copies each leg's Account and cannot break it.
- An Active `AccountPortfolioAssignment` is on a Custodial account. Held in Application (R14) and by script `0032` (R16).

Application passes facts and the Domain holds the invariant. The 400s of R3 and R8 are raised by Application with their field key before the factory is called; the Domain check is the backstop for callers that do not go through HTTP.

---

## 6. Database

Script: `database/schema/0032_remove_external_portfolio_assignments.sql`. Forward only. Data only: no table, column, index or constraint. Do not add EF migrations.

| Table | Change | Index / FK |
| --- | --- | --- |
| `AccountPortfolioAssignments` | Rows with `Status = 0` (Active) whose Account has `CustodyMode = 0` (External) get `Status = 1` (Removed), `LastModified` = the current UTC time and `LastModifiedBy = 'system'` | none |

- One `UPDATE` joined to `Accounts`. It is atomic and safe to run again: a second run finds no row.
- The filter is the Account's custody mode, not "every Active row". On a database that already has Custodial accounts it leaves their assignments alone.
- The script raises no `PortfolioUnassigned` event and writes no outbox row.
- Holdings that a rebalance bought stay on the External account, and the Adviser trades them by hand.

---

## 7. Application use cases

| Kind | Name | What this spec puts in it |
| --- | --- | --- |
| Command | `CreateAccount` | Accepts `Custodial`. `Custodial` with a type other than `Brokerage` → 400 `errors.Type` (R3), a validator rule that needs no database read. |
| Command | `CreateTransaction` | After the Account is loaded: the manual entry matrix (R5, R6), then `bookedAt` by custody mode (R10–R12). |
| Command | `CreateInternalTransfer` | Same custody mode on both Accounts (R8), then `bookedAt` by custody mode. Passes both custody modes to the factory. |
| Command | `CreateFxConversion` | Same as `CreateInternalTransfer`. The rate is read for the instant of R12. |
| Query | `GetFxConversionQuote` | Same custody mode on both Accounts (R8). A body with `bookedAt` for Custodial Accounts → 400 (R10). |
| Command | `ReverseTransaction` | No rule from this spec (R9). It uses the server clock. The source check is transaction-source R13. |
| Command | `AssignPortfolio` | Eligibility is "Open and Custodial" (R14). |
| Command | A rebalance (`POST /accounts/{id}/rebalance` and the job) | Eligibility is "Open, Custodial, Active assignment" (R15). |

Where each rule lives:

| Rule | Where |
| --- | --- |
| Custodial ⇒ Brokerage | `CreateAccountCommandValidator` for the 400; `Account.Create` for the invariant |
| Manual entry matrix | `CustodyRules` in Domain; called by the `CreateTransaction` handler, and by the handlers of the specs named in §4.2.1 for their rows |
| Same custody mode | The transfer, conversion and quote handlers for the 400; the two factories for the invariant |
| `bookedAt` required (External) and refused (Custodial) | `BookedAtByCustody` in `Application/Transactions`, used by the four handlers. It takes the custody mode, the request value and `TimeProvider`, and returns the instant to use. The 400s are `ValidationException` on `BookedAt` |
| `bookedAt` is not in the future | The four validators (`BookedAtRules.NotInTheFuture`) |
| Portfolio eligibility | `AssignPortfolio` and the rebalance proposal |

A refused request stores no idempotency record: every check throws inside the command's database transaction, before any insert.

---

## 8. API

No routes and no policies of its own.

| Method | Route | Rule from this spec |
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

Transfer between two Custodial accounts of one Customer. There is no `bookedAt`:

```json
{
  "fromAccountId": "<custodial account publicId>",
  "toAccountId": "<custodial account publicId>",
  "amount": 500.00
}
```

The transaction item does not change. Its `bookedAt` is the instant the server chose, in UTC. The same body between two External accounts carries `bookedAt`.

### 8.2 Errors

All of these are ordinary validation: 400 with `errors`, no problem `code` ([api-design.md](../api-design.md) §4.2 C1), except the two that reuse `account_not_eligible`.

| Case | Response |
| --- | --- |
| `Custodial` with a type other than `Brokerage` on `POST /accounts` | 400, `errors.Type` |
| A type the matrix refuses | 400, `errors.Type` (§4.2.1 for the rows with another key) |
| Transfer, conversion or quote between a Custodial and an External account | 400, `errors.ToAccountId` |
| `bookedAt` in the body for a Custodial account | 400, `errors.BookedAt` |
| No `bookedAt` in a create body for an External account | 400, `errors.BookedAt` |
| Assign a portfolio to an External account, or rebalance one | 400, `code` `account_not_eligible` |

Cross-tenant or wrong-collection id → **404**, not 403, before any rule of this spec is evaluated.

Keep the resource catalog in [api-design.md](../api-design.md) §7.6 and §7.7 pointing here for these rules.

---

## 9. UI

Page contracts: [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (D17–D20) and [adviser-portal-portfolios.md](../adviser-portal/adviser-portal-portfolios.md) (P13). They own what each page offers; the points below are the ones that come from this spec.

**Adviser Portal**

- New account form: a required "Custody" choice with two options, "External — held elsewhere" and "Custodial — held on the platform". Nothing is preselected. Choosing Custodial sets Type to Brokerage and disables the Type control.
- Account page and the transaction form: the action buttons and the type selector offer only what the matrix allows for the account's custody mode.
- On a Custodial account the transfer and convert forms have no "Booked at" input and send no `bookedAt`. They show "Booked when posted." in its place. The convert form's quote request sends no `bookedAt` either.
- Transfer and convert account pickers: an account with a different custody mode stays listed, greyed out, with the reason "Different custody mode", like the Closed and currency reasons.
- The Assign portfolio control is offered on an Open Custodial account only.

**Customer Portal**

Nothing from this spec. The account pages show "Held on platform" for a Custodial account ([customer-portal.md](../customer-portal/customer-portal.md)).

### 9.1 Seeds

`TestSeed` (Development and TestAppHost), idempotent. Like the four accounts of accounts §9.1, the account is created for every TestSeed firm (`demo` and the Development fixture `demo2`), each on its own demo Customer:

| Name | Type | Custody mode | Currency | Status |
| --- | --- | --- | --- | --- |
| Demo custodial brokerage | Brokerage | Custodial | USD | Open |

- Owned by the firm's demo Customer, next to the four accounts of accounts §9.1, which stay External. The firm's client money accounts and the account's deposit reference are client-money-accounts §9.1.
- Transactions, built through the Domain factories with the seed's own dates and source `Seed`, priced from the seeded `InstrumentPrices` (as of 2026-01-01). Each is idempotent on its `reference`. A person cannot reverse them (transaction-source R13):

| Booked at | Type | Detail | Reference |
| --- | --- | --- | --- |
| 2026-01-20 10:00 +13:00 | `TransferIn` | `USD 20,000.00` | `TestSeed-DemoCustodial-Funding` |
| 2026-01-20 10:30 +13:00 | `Buy` | `VTI` × 40 at 250 = `USD 10,000.00` | `TestSeed-DemoCustodial-BuyVti` |
| 2026-01-20 11:00 +13:00 | `Buy` | `AAPL` × 20 at 180 = `USD 3,600.00` | `TestSeed-DemoCustodial-BuyAapl` |

- The cash balance after the seed is `USD 6,400.00`. No seeded trade carries a fee. The `Holdings` rows for `VTI` and `AAPL` are synced like those of "Demo brokerage".
- The `Demo Growth` assignment is on this account. The seed starts no rebalance. `AAPL` is not in `Demo Growth`, so the first rebalance in a demo has something to sell.
- "Demo brokerage" stays External and keeps its hand-entered demo data (`OpeningHoldings`, `StockIn`, `Split`). It has no assignment.
- The Development smoke seed (`Smoke Cash`) and the demo seed ([demo-tenant-seed.md](demo-tenant-seed.md)) create no Custodial account.

---

## 10. Tests

The tests are the executable form of §4. Where they live:

| Project | Where |
| --- | --- |
| Domain.UnitTests | `Custody/CustodyRulesTests.cs`: one case per cell of the matrix. `Entities/AccountTests.cs` and `Entities/FxConversionTests.cs` for the `Account.Create` and two-leg factory invariants |
| Application.FunctionalTests | `Accounts/AccountCustodyModeCreateTests.cs` for open. Folder `Custody/`: the matrix (`CustodialManualEntryTests`, `CustodialTransferInRefusedTests`, `CustodialTransferOutRefusedTests`, `CustodialTradeRefusedTests`), same custody mode and same Customer (`CustodialSameModeTests`, `CustodialTransferSameCustomerTests`), `bookedAt` (`CustodialBookedAtTests`), portfolios (`CustodialPortfolioTests`) |
| Infrastructure.IntegrationTests | `AppliedSchema/RemoveExternalPortfolioAssignmentsSchemaTests.cs` for script `0032`; the seed tests for §9.1 |

Every rule is applied in a handler after the Account lookup, so the cross-tenant cases (another tenant's Custodial account id → 404, not 400) are proved over HTTP in Application.FunctionalTests.

---

## 11. Locked in this spec

No ADR of its own. [ADR 0020](../adr/0020-account-custody-mode.md) item 5 gave the direction; this spec makes it rules and decides the point that ADR left open (who sets the booking time).

Decisions confirmed when this spec was accepted, with the option that was not taken:

1. **The server sets `bookedAt` on a Custodial account** (R10–R13). Not taken: keep the caller's `bookedAt` on both modes and refuse only a future time.
2. **Existing portfolio assignments were removed** (R16), and every existing account stayed External. Not taken: turn the Brokerage accounts that had an Active assignment into Custodial accounts in the script, which would have put hand-entered history into Custodial accounts.
3. **Nothing is preselected in the Custody choice of the New account form** (§9). Not taken: preselect External.

Still open:

- What happens to `Other`, `Property` and `Credit`
- `Dividend` on an External account (income events)
- A conversion on a Custodial account through an outside FX party ([custodial-roadmap.md](../custodial-roadmap.md) O5)

---

## 12. History

What changed in this file after it landed. The text of each change is in git (`git log -p -- docs/features/custody-modes.md`).

| Date | With | Change here |
| --- | --- | --- |
| 2026-11-18 | [tenant-custody-modes.md](tenant-custody-modes.md) | R4: the Tenant must allow the custody mode |
| 2026-11-18 | [external-caller-numbers.md](external-caller-numbers.md) | I3, R19: the caller states the numbers on an External account |
| 2026-11-19 | [transaction-source.md](transaction-source.md) | R5 reverse row: by source on a Custodial account. R7: flows record a source |
| 2026-11-19 | [adjustments.md](adjustments.md) | R5: the row `Adjustment` |
| 2026-11-20 | [client-money-accounts.md](client-money-accounts.md) | R4: a client money account in the currency; the deposit reference |
| 2026-11-20 | [deposit-matching.md](deposit-matching.md) | R5: hand `TransferIn` refused on a Custodial account |
| 2026-11-20 | [withdrawals.md](withdrawals.md) | R5: hand `TransferOut` refused; `CloseOut` only for a remainder; a transfer stays with one Customer |
| 2026-11-22 | [account-closure.md](account-closure.md) | R5: hand `CloseOut` refused for every amount. R14: a closing account |
| 2026-11-22 | [interest-run.md](interest-run.md) | R5: `Interest` is written by the interest run |
| 2026-11-22 | [corporate-actions.md](corporate-actions.md) | R5: `Split` and `Bonus` are written by corporate actions |
| 2026-11-23 | [orders.md](orders.md) | R5: hand `Buy` / `Sell` refused. R17, R18: orders and fills |
| 2026-11-23 | [rebalances.md](rebalances.md) | R15, R17: a rebalance trades through orders. R5 reverse row: `Manual` only |
| 2026-11-24 | [migration-batches.md](migration-batches.md) | R5: `Opening` and `OpeningHoldings` are written by a migration batch |
| 2026-11-24 | [in-kind-transfers.md](in-kind-transfers.md) | R5: `StockIn` and `StockOut` are written by an in-kind transfer |
| 2026-11-24 | [dividends.md](dividends.md) | R5: `Dividend` is written by corporate actions |
| 2026-11-25 | [adjustment-review.md](adjustment-review.md) | R5: an adjustment goes through a request; an `Adjustment` is never reversed |
| 2026-11-26 | — | The amendments above were folded into §1–§11. No rule changed |
