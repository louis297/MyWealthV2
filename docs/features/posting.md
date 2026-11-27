---
title: Posting
status: accepted
phase: 2
language: en
owner: ""
created: 2026-09-25
last_updated: 2026-11-26
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../architecture.md
  - ../adr/0004-money-as-decimal-with-currency.md
  - ../adr/0009-currencies-catalog.md
  - ../adr/0011-ledger-cash-holdings-reversal.md
  - ../adr/0013-roles-authorization-single-user-table.md
  - ../adr/0015-idempotency-keys.md
  - ../fixes/ledger-write-guards.md
  - ../fixes/utc-instants.md
  - accounts.md
  - instruments.md
  - customers.md
  - leg-accounts.md
  - holdings.md
  - buy-sell.md
  - split.md
  - internal-transfers.md
  - fx-conversions.md
  - trade-fees.md
  - custody-modes.md
  - transaction-source.md
  - adjustments.md
  - adjustment-review.md
  - deposit-matching.md
  - withdrawals.md
  - account-closure.md
  - interest-run.md
  - corporate-actions.md
  - orders.md
  - migration-batches.md
  - in-kind-transfers.md
  - dividends.md
---

# Posting

Write model for booked activity on `webapi`. The public name is **Transaction** (a business event). Accounting is **legs** on that same aggregate, assembled in the same command. This spec owns the aggregate, the `/transactions` routes, the cash types and their rules, reverse, the cash balance as a `SUM`, and the rule that an Account closes only at cash zero.

There is **no separate cash-ledger Feature Spec**: cash legs live here. There is **no capture `Transactions` row that is later copied into a `Journals` table.** Other specs add legs, types and details to the same aggregate; §5 names the spec that owns each type.

This file states the rules as they are today. §14 lists what changed here, by date.

**Status is `accepted` and landed.** Script `0013_transactions.sql`, routes `/transactions`, policies `transactions.read` / `transactions.create`. Idempotency: [ADR 0015](../adr/0015-idempotency-keys.md). Direction: [ADR 0011](../adr/0011-ledger-cash-holdings-reversal.md).

---

## 1. Summary

A TenantAdmin or Adviser records a **Transaction** against an **Open** Account of an assigned / same-tenant Customer. SystemAdmin does the same for a chosen tenant. The header is append-only for economic data; the only later write is the single `ReversedByTransactionId` on reverse.

Two **layers**, one **step**:

```text
POST /transactions
  → insert Transaction (Posted) + its legs
  → same transaction: lock every leg account, SUM ≥ 0, Opening / CloseOut rules
  → raise TransactionPosted (no handler required)
```

The header is the business event (`type`, `bookedAt`, `memo`, source). It has no Account: each leg carries the Account it books into ([leg-accounts.md](leg-accounts.md)). A cash leg is the accounting line that the cash `SUM` reads. Application code picks the legs from `type`. The cash types of this spec build exactly one cash leg. `Buy` / `Sell` add a security leg on the **same** header; `Split` / `Bonus` / `Scrip` are the same model with a security leg only.

Write = posted. A Transaction has no status, and no read of the ledger has to leave a row out. Two things come before a Transaction and are not Transactions:

- An **instruction** to an outside party (a withdrawal, an order, an in-kind transfer) is an aggregate of its own with its own lifecycle. The Transaction it leads to is posted in one step, when the outside party confirms.
- Where a second person must approve a write, the approval is on a record of its own (an adjustment request, a migration batch) and comes before the Transaction is posted.

No event handler "approves" a Transaction or copies it into another table.

The cash-book currency is `Account.Currency`. The balance is not stored on `Account`; it is the sum of posted cash legs.

An External account closes with `POST /accounts/{id}/close`, which is refused while cash `SUM ≠ 0` or a holding remains (accounts R16). `CloseOut` brings the book to zero (counterparty off-books). A Closed account refuses every new transaction, including reversals: reopen first. A Custodial account closes through an account closure ([account-closure.md](account-closure.md)).

Which types a person may enter by hand depends on the custody mode of the Account: the manual entry matrix, [custody-modes.md](custody-modes.md) §4.2. On a Custodial account the platform writes the cash types of this spec from its flows.

---

## 2. Scope

**In**

- Domain: `Transaction` aggregate (header) + `TransactionCashLeg`; `TransactionType` enum
- Application: `CreateTransaction`, `ReverseTransaction`, list / get, `GetAccountCashBalance`; the cash-zero guard in `CloseAccount`
- Script `database/schema/0013_transactions.sql` + EF mapping; script `0018_transaction_reversed_by.sql`
- Policies `transactions.read` / `transactions.create` (no update / delete policy)
- `webapi` `/transactions` (root, not under `/users`)
- `GET /accounts/{id}` and `GET /accounts` carry the computed `cashBalance` + `currency`; `GET /accounts/{id}/cash-balance` returns the same `SUM`
- List envelope + isolation tests
- `Idempotency-Key` on create and reverse ([ADR 0015](../adr/0015-idempotency-keys.md))
- Development / TestAppHost `TestSeed` rows (not the schema script, not Production)

**Out — owned by another spec**

- The Account on each leg, and visibility by leg account: [leg-accounts.md](leg-accounts.md)
- Security legs and the holdings projection: [holdings.md](holdings.md), [buy-sell.md](buy-sell.md), [split.md](split.md)
- Cross-account transfer and conversion: [internal-transfers.md](internal-transfers.md), [fx-conversions.md](fx-conversions.md)
- Cash leg roles other than `Principal`: [trade-fees.md](trade-fees.md) (`Fee`), [interest-run.md](interest-run.md) (`Tax`), [dividends.md](dividends.md) (`Reinvestment`)
- The source of a Transaction, and who may reverse by source: [transaction-source.md](transaction-source.md)
- What a person may enter by custody mode, and who sets `bookedAt`: [custody-modes.md](custody-modes.md)
- Each flow that writes on a Custodial account, with the detail it adds to the item: the specs named in §5 and §8.1
- Net worth, the reports and the portal pages

**Out — not in the model**

- A second capture table whose approve step writes this aggregate
- A `Created` / `PendingApproval` status; an event handler that approves
- Draft / unposted rows
- `UPDATE` / `DELETE` of a posted transaction, except the single `ReversedByTransactionId` write on reverse
- Customer ledger-write
- Debit/credit columns; a wide row that is both cash and security
- A stored balance; a `Journals` table
- Property / Credit selectable. The Credit negative-cash exception is written only, not built
- Seeding from the SQL script or in Production

---

## 3. Stories

1. As an Adviser I record an external deposit or withdrawal on an assigned Customer's External Bank account so the cash book is no longer empty.
2. As an Adviser I record interest on that book in the account currency (either sign).
3. As an Adviser I record a cash `Opening` as the initial inbound (`amount > 0`), at most once, and only while the book still has no other transaction.
4. As an Adviser I record a `CloseOut` equal to the current balance so I can close the account.
5. As an Adviser I cannot close an account while cash `SUM ≠ 0` (400).
6. As an Adviser I cannot record a transaction on a Closed account (400). I reopen first.
7. As an Adviser I reverse a posted transaction with a new opposite transaction. The new row points at the original (`originalTransactionId`). The original receives a single write of `reversedByTransactionId`. After that write I do not edit the original again.
8. As a TenantAdmin I do the same for any Customer in my firm.
9. As a SystemAdmin I do the same on Scalar for a chosen tenant.
10. As a Customer I read the transactions of my own accounts. I cannot create or reverse one (403).

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Policy fail → 403. TenantAdmin / Adviser: other-tenant, unassigned Customer, missing id → 404. SystemAdmin: missing id → 404; another tenant's transaction id is allowed. |
| R2 | Path `{id}` and JSON `id` are `PublicId`. Internal ints never appear. Create / reverse return `201 { "id" }` only. |
| R3 | Routes live at `/transactions`. Not under `/users`. Not nested under `/accounts/{id}` as the collection root (account filter is a query). |
| R4 | TenantAdmin / Adviser: tenant = token `TenantId`. Create / list must **omit** `tenantId` (400 if present). Missing current tenant → 404. Current tenant disabled → 403 on every verb, reads included (identity-auth §4.5). SystemAdmin naming a disabled tenant on **create** → 400 `disabled` / `target=tenant`. |
| R4a | SystemAdmin: create and list **require** `tenantId` (tenant PublicId). Omitted → 400. Unknown tenant → 404. Get / reverse use transaction PublicId only and may cross tenants. |
| R5 | A Transaction belongs to the Accounts its legs book into, all in the header's tenant. The header has no `AccountId`; every leg has its own (leg-accounts R2, R3). On `POST /transactions` the body's `accountId` (Account PublicId) is the Account of every leg (leg-accounts R5). Missing / other-tenant / Adviser-unassigned Customer of a leg account → 404 for the whole command (leg-accounts R6). |
| R6 | Create or reverse while a leg account is **Closed** → 400 ordinary `errors` (leg-accounts R8). Reopen the account first. Close / reopen stay on `/accounts`. Directly after this check, a leg account that is **closing** (an account closure `InProgress`) → 400 with `This account is being closed and accepts no new activity.` under the empty `errors` key (account-closure R12). No idempotency record is written. |
| R7 | Create or reverse while the Customer of a leg account is Disabled → 400 `disabled` / `target=user` (leg-accounts R8). List / get of existing rows still work. |
| R8 | A cash amount is a signed book amount in the currency of the leg's Account only (leg-accounts R9). The body must not send a different currency. Store `decimal(18,4)` + `char(3)`. Application uses Domain `Money` for currency equality; direction lives on the signed decimal, not inside `Money`. The caller's `amount` has no more decimal places than the Account currency's `DecimalPlaces`, else 400 `errors.Amount`; `CloseOut` is exempt, because its amount must equal the current `SUM` ([ledger-write-guards](../fixes/ledger-write-guards.md)). |
| R9 | After insert, the cash `SUM` of every leg account the command touched must be `≥ 0` (leg-accounts R10). No Credit account is opened. Violation → 400. Compute in the same command, with every leg account row locked (R22). No persisted balance column. On a Custodial account the test is stricter: every write that lowers cash must leave **available cash** `≥ 0`, the cash `SUM` minus the cash reserved for open withdrawals and live buy orders (withdrawals R36, R37; orders R39). The payout of a withdrawal is the one exception: it releases its own reservation. |
| R10 | Posted header and legs are not `UPDATE`d or `DELETE`d, **except** a single write of `ReversedByTransactionId` on the original when a Reversal is created. After that write the original is immutable again. Economic legs are never updated. Correction is a new transaction with `Type = Reversal` and `originalTransactionId` set. Schema scripts that back-fill a new column on existing rows (`0029`, `0035`) are one-time data writes, not corrections. |
| R11 | A Reversal targets a posted transaction. It has one opposite leg per original leg, each on that leg's Account; R6–R9 apply to every leg account, and there is no partial reverse (leg-accounts R12). Already reversed (`ReversedByTransactionId` is not null) → 400. One reversal per original. Reversal of a Reversal is out (`ReversedByTransactionId` on a Reversal stays null). A Transaction with a security leg can be reversed only while it is the latest security activity on each of its holdings, else 400 ([ledger-write-guards](../fixes/ledger-write-guards.md)). By hand, on a Custodial account only a Transaction with source `Manual` can be reversed (transaction-source R13, rebalances R28), and an `Adjustment` is never reversed on either custody mode (adjustment-review R15); each → 400. A flow reverses its own write through the Domain, not through this route. A Reversal has its own source, not the original's. |
| R12 | `CloseAccount`: if cash `SUM ≠ 0` → 400 ordinary `errors`. `SUM = 0` (including never booked) may close. This is accounts R16, which also requires that no holding remains. Do not bulk-insert on close. Do not write cash inside `Account.Close()`. On an Open Custodial account `POST /accounts/{id}/close` → 400 (account-closure R22); inside the closure flow the same two conditions hold before `Account.Close()`. |
| R13 | Closed accounts stay readable. Net worth excludes them (net-worth R5). Balance APIs still return the `SUM` (expected 0 after a successful close). |
| R14 | List envelope `{ items, page, pageSize, totalCount }`; page default 1, size default 20, max 100. Optional `accountId`, optional `customerId`; each matches a Transaction with at least one leg there (leg-accounts R18). Neither set → tenant scope (Adviser = assigned Customers; Customer = own accounts). A Transaction is returned when the caller can see at least one leg account, with only the legs the caller can see (leg-accounts R7). Sort `bookedAt` then id. |
| R15 | A Customer has `transactions.read`: list and get for the caller's own accounts only; another Customer's transaction or account id → 404. A Customer has no `transactions.create` → 403 on create and reverse. |
| R16 | Type on `POST /transactions`: one of `TransferIn`, `TransferOut`, `Interest`, `CloseOut`, `Opening`, `OpeningHoldings`, `Buy`, `Sell`, `StockIn`, `StockOut`, `Split`, `Bonus`, `Scrip`. `InternalTransfer`, `FxConversion`, `Adjustment`, `Reversal`, `Dividend` and an unknown name → 400 `errors.Type`: the first five are not entered on this route (§5); a hand `Dividend` is `POST /transactions/dividend` ([external-income.md](external-income.md) R10). The matrix then decides by the Account's custody mode (custody-modes R5, R6): on a Custodial account it refuses every type of this route. |
| R17 | `CloseOut`: cash leaves to an off-books counterparty. Signed amount must be `< 0` and equal in magnitude to the current `SUM`, so after this row `SUM = 0`. Not a cross-account transfer. Not a holdings liquidation. Amount `0` not allowed; already zero → just `Close`. On a Custodial account the only `CloseOut` is the remainder write-off that an account closure posts (account-closure R18); this rule holds for it too. |
| R18 | Do not treat a pair of unlinked `TransferOut` + `TransferIn` on two accounts as a transfer. A transfer is one Transaction (§13). |
| R19 | Cash `Opening`: `amount > 0`. At most one per account. Allowed only while no leg of any other Transaction books into the Account (leg-accounts R13). After that, `Opening` → 400. Create Account does not insert a row; a new book's `SUM` is already 0. On a Custodial account an `Opening` is written only by a migration batch, on an account with no Transaction at all, reversed ones included (migration-batches R10, R22). |
| R20 | `TransferIn` / cash `Opening` → `amount > 0`. `TransferOut` / `CloseOut` → `amount < 0`. A hand `Interest` either sign, or with entered tax a `Principal` leg `> 0` and one `Tax` leg `< 0` per tax ([external-income.md](external-income.md) R22); the posting of an interest run has a `Principal` leg `> 0` and, with withholding tax, a `Tax` leg `< 0` (interest-run R28). `Reversal` = arithmetic opposite of each original leg. |
| R21 | `POST /transactions` and `POST /transactions/{id}/reverse` require `Idempotency-Key` (ADR 0015). Missing → 400. Replay same hash → original `201 { id }`. Same key different hash → 409. |
| R22 | Create / reverse do not take Account `rowVersion` from the client. Reverse does **not** take the original transaction `rowVersion`. The server locks every leg account row, in ascending `Accounts.Id` order, while inserting and summing (leg-accounts R11). A flow that posts a Transaction locks the same way. |
| R23 | Optional `memo`: trim; if present, 1–200. Optional `reference`: stored nullable, not unique. |
| R24 | `bookedAt` is an instant with `Z` or an offset. No offset → 400 `errors.BookedAt`. Stored and returned in UTC (`Z`); the request offset is not kept ([utc-instants](../fixes/utc-instants.md)). Later than the server clock plus 5 minutes → 400 `errors.BookedAt` ([ledger-write-guards](../fixes/ledger-write-guards.md)). On an External account the caller must send it. On a Custodial account the server sets it, and a body that sends it → 400 `errors.BookedAt` (custody-modes R10–R12). Audit `Created` is separate. |

Policies: `transactions.read`, `transactions.create`. Grants per role are in the role permission table, [api-design.md](../api-design.md) §3.1.

Adviser scope is **assigned Customers** of the leg accounts (404 if not). Same width as `accounts.*`. Reverse uses `transactions.create`.

`GET /accounts/{id}/cash-balance` uses `accounts.read` (same row the caller can already get).

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `Transaction` | Aggregate | Business-event header. Not an Account. Not a balance. Not a second "journal" copy. Has a source: a kind and an optional id, set when it is built and never sent by a caller ([transaction-source.md](transaction-source.md)). |
| `TransactionCashLeg` | Entity | Accounting line on the cash book of its Account. Has a role: `Principal`, `Fee`, `Tax` or `Reinvestment`. The cash types of this spec build one `Principal` leg. The cash `SUM` and every balance rule add all cash legs, whatever their role. |
| `TransactionType` | Enum | The table below. |
| `TransactionPosted` / `TransactionReversed` | Event | Raised even with no subscriber. Must **not** be the approve / copy path. |

`TransactionType`. "By hand" is the route a person uses where the manual entry matrix allows it (custody-modes §4.2, which also names the flow that writes each type on a Custodial account):

| Name (value) | By hand | Legs | Rules |
| --- | --- | --- | --- |
| `TransferIn` (0) | `POST /transactions` | cash + | Here. External deposit onto the account. On a Custodial account a deposit: [deposit-matching.md](deposit-matching.md) |
| `TransferOut` (1) | `POST /transactions` | cash − | Here. External withdrawal from the account. On a Custodial account a payout: [withdrawals.md](withdrawals.md) |
| `Interest` (2) | `POST /transactions` | cash + or −; on an External account with `taxes` a `Principal` leg `> 0` and `Tax` legs | Here; the tax: [external-income.md](external-income.md). On a Custodial account: [interest-run.md](interest-run.md), which may add a `Tax` leg |
| `CloseOut` (3) | `POST /transactions` | cash − equal to the current `SUM` | Here (R17). Off-books empty before close |
| `Opening` (4) | `POST /transactions` | cash + | Here (R19). Initial inbound. Not holdings |
| `Reversal` (5) | `POST /transactions/{id}/reverse` | the opposite of each original leg | Here (R11) |
| `Dividend` (6) | `POST /transactions/dividend`, External accounts only | cash `Principal`, `Tax` and, with a reinvestment, `Reinvestment` + one security leg; by hand `Principal` and `Tax` only | [dividends.md](dividends.md); by hand [external-income.md](external-income.md) |
| `Buy` (7) / `Sell` (8) | `POST /transactions` | cash + security; may have a `Fee` leg | [buy-sell.md](buy-sell.md), [trade-fees.md](trade-fees.md). On a Custodial account an order fill: [orders.md](orders.md) |
| `OpeningHoldings` (9) | `POST /transactions` | security | [holdings.md](holdings.md) |
| `StockIn` (10) / `StockOut` (11) | `POST /transactions` | security | [buy-sell.md](buy-sell.md). On a Custodial account: [in-kind-transfers.md](in-kind-transfers.md) |
| `Split` (12) / `Bonus` (13) / `Scrip` (14) | `POST /transactions` | security only | [split.md](split.md). On a Custodial account: [corporate-actions.md](corporate-actions.md) |
| `InternalTransfer` (15) | `POST /transactions/transfer` | two cash legs on two Accounts | [internal-transfers.md](internal-transfers.md) |
| `FxConversion` (16) | `POST /transactions/fx` | two cash legs in two currencies | [fx-conversions.md](fx-conversions.md) |
| `Adjustment` (17) | none; the approval of an adjustment request posts it | optional cash, optional security | [adjustments.md](adjustments.md), [adjustment-review.md](adjustment-review.md) |

Invariants:

- `TenantId` is required on the header, and an `AccountId` on every leg. Every leg account is in the header's tenant (application dual check). A Transaction has at least one leg.
- Insert lands **Posted**. There is no status.
- The header is the aggregate root. The cash `SUM` is not stored. The cash of a past day is the same `SUM` over the Transactions with `bookedAt` before that day ended, each counted from its own `bookedAt`, a Reversal too ([account-valuation](account-valuation.md) R3, R5).
- A cash leg's currency equals the currency of its Account (denormalised).
- The original transaction is immutable after insert (audit columns aside), **except** the single `ReversedByTransactionId` write on reverse. After that write it is immutable again.
- `Account.Close()` stays a status flip. Zero-cash is an application guard.
- At most one `Principal` cash leg per `(Transaction, Account)`.

```text
Create(accountId, type, cashAmount, bookedAt, memo?, reference?, source)
  account Open (application)
  type in the cash set
  Opening / CloseOut / sign rules
  cash currency = account currency
  insert header + cash leg on the account
  SUM(account) >= 0
  raise TransactionPosted

Reverse(original, source)
  original.ReversedByTransactionId is null
  original.Type is not Reversal
  every leg account Open
  insert new type = Reversal, originalTransactionId = original
  set original.ReversedByTransactionId = new
  one opposite leg per original leg, on that leg's account
  SUM(each leg account) >= 0
  raise TransactionReversed
```

Keep [domain-model.md](../domain-model.md) in the same change as this file.

---

## 6. Database

Do not add EF migrations. The current columns of every table are in [database-design.md](../database-design.md).

Scripts of this spec:

| Script | What it does |
| --- | --- |
| `0013_transactions.sql` | Creates `Transactions`, `TransactionCashLegs` and the idempotency table of [ADR 0015](../adr/0015-idempotency-keys.md), so this slice cannot boot without it |
| `0018_transaction_reversed_by.sql` | Adds `Transactions.ReversedByTransactionId`: nullable FK → `Transactions` RESTRICT, unique filtered index `WHERE ReversedByTransactionId IS NOT NULL`. No back-fill |

| Table | Shape | Index / FK |
| --- | --- | --- |
| `Transactions` | The header | PK `Id`. Unique `PublicId`. FK `TenantId` → `Tenants` RESTRICT. Optional FK `OriginalTransactionId` → `Transactions` RESTRICT. Optional FK `ReversedByTransactionId` → `Transactions` RESTRICT. Index `(TenantId, BookedAt)`. |
| `TransactionCashLegs` | One row per cash leg | PK `Id`. FK `TransactionId` → `Transactions` RESTRICT. `AccountId`. `Role`. Amount `decimal(18,4)`. Currency `char(3)` FK `Currencies.Code` RESTRICT. Unique filtered `(TransactionId, AccountId)` for `Principal` legs. |

Later scripts of other specs changed the two tables: `0024` moved `AccountId` from the header to the legs ([leg-accounts.md](leg-accounts.md)); `0030` added the cash leg `Role` ([trade-fees.md](trade-fees.md)); `0035` added the source columns on the header ([transaction-source.md](transaction-source.md)). Each detail of §8.1 is a table of the spec that owns it.

Header **and** cash-leg rows use the same audit + concurrency pair as `Accounts` / `Instruments`: `Created`, `CreatedBy`, `LastModified`, `LastModifiedBy`, `RowVersion`. `AuditableEntityInterceptor` fills the four audit fields on both. Posted rows are not updated, so `LastModified*` stay null, **except** the single `ReversedByTransactionId` write, which fills `LastModified` / `LastModifiedBy` and bumps `RowVersion`. Reverse still does **not** take a client `rowVersion` (the server locks the Accounts).

HTTP addresses the header `PublicId` only; the leg `RowVersion` is not a client token.

No `Accounts.Balance`. No `Journals` table. No capture table beside `Transactions`.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreateTransaction` | new PublicId | R1–R10, R16–R24 |
| Command | `ReverseTransaction` | new PublicId | R10–R11, R6–R9, R21–R22 |
| Command | `CloseAccount` (accounts) | 204 | R12: cash `SUM = 0` |
| Query | `GetTransactions` | list envelope | tenant / assignment / R14 |
| Query | `GetTransactionById` | item | 404 rules |
| Query | `GetAccountCashBalance` | `{ accountId, currency, cashBalance }` | `accounts.read` scope |

`cashBalance` on account list/item is the same query, not a second store.

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/transactions` | `transactions.read` | 200 envelope | 400 / 401 / 403 / 404 |
| GET | `/transactions/{id}` | `transactions.read` | 200 item | 401 / 403 / 404 |
| POST | `/transactions` | `transactions.create` | 201 `{ "id" }` | 400 / 401 / 403 / 404 / 409 |
| POST | `/transactions/{id}/reverse` | `transactions.create` | 201 `{ "id" }` | 400 / 401 / 403 / 404 / 409 |
| GET | `/accounts/{id}/cash-balance` | `accounts.read` | 200 | 401 / 403 / 404 |

Create and reverse require `Idempotency-Key`. `POST /transactions/transfer` and `POST /transactions/fx` belong to [internal-transfers.md](internal-transfers.md) and [fx-conversions.md](fx-conversions.md).

### 8.1 Bodies

Create, on an External account:

```json
{
  "accountId": "<account publicId>",
  "type": "TransferIn",
  "amount": 100.00,
  "bookedAt": "2026-09-24T09:00:00+12:00",
  "memo": "Salary",
  "reference": null
}
```

SystemAdmin create adds `tenantId`. TenantAdmin / Adviser must omit it.

Item:

```json
{
  "id": "<transaction publicId>",
  "tenantId": "<tenant publicId>",
  "type": "TransferIn",
  "source": { "kind": "Manual", "id": null },
  "cash": [
    {
      "accountId": "<account publicId>",
      "customerId": "<customer publicId>",
      "role": "Principal",
      "amount": 100.00,
      "currency": "NZD",
      "fee": null,
      "tax": null
    }
  ],
  "security": [],
  "bookedAt": "2026-09-23T21:00:00Z",
  "memo": "Salary",
  "reference": null,
  "originalTransactionId": null,
  "reversedByTransactionId": null,
  "rowVersion": "<base64>",
  "fx": null,
  "adjustment": null,
  "bankReceipt": null,
  "payment": null,
  "interest": null,
  "corporateAction": null,
  "execution": null,
  "migration": null,
  "inKindTransfer": null,
  "dividend": null
}
```

The item has `cash` and `security` arrays, each leg with its own `accountId` / `customerId` ([leg-accounts.md](leg-accounts.md) §8.1). Security-only types return `cash: []`; cash-only types return `security: []`. There is no flat `amount` on the item.

`originalTransactionId` is set on a Reversal (points at the original). `reversedByTransactionId` is set on the original after reverse (points at the Reversal). Both are null on an unreversed original. Both are returned on list and get. Hide the Reverse button when `reversedByTransactionId` is not null or `type` is `Reversal`.

The other fields are `null` except on the Transactions of the spec that owns them:

| Field | On | Owner |
| --- | --- | --- |
| `source` | Every Transaction | [transaction-source.md](transaction-source.md) |
| `cash[].fee` | A `Fee` leg | [trade-fees.md](trade-fees.md) §8.1 |
| `cash[].tax` | A `Tax` leg | [interest-run.md](interest-run.md), [dividends.md](dividends.md) |
| `security[]` | A security leg | [holdings.md](holdings.md), [buy-sell.md](buy-sell.md), [split.md](split.md) |
| `fx` | A Transaction with an FX detail | [fx-conversions.md](fx-conversions.md) R22 |
| `adjustment` | An `Adjustment` | [adjustments.md](adjustments.md) |
| `bankReceipt` | A deposit and its Reversal | [deposit-matching.md](deposit-matching.md) |
| `payment` | A payout and its Reversal | [withdrawals.md](withdrawals.md) |
| `interest` | The posting of an interest run | [interest-run.md](interest-run.md) |
| `corporateAction` | An entitlement posting | [corporate-actions.md](corporate-actions.md) |
| `execution` | An order fill | [orders.md](orders.md) |
| `migration` | A migrated `Opening` or `OpeningHoldings` | [migration-batches.md](migration-batches.md) |
| `inKindTransfer` | The `StockIn` / `StockOut` of an in-kind transfer | [in-kind-transfers.md](in-kind-transfers.md) |
| `dividend` | A `Dividend` | [dividends.md](dividends.md) |

Account item and account list:

```json
{
  "cashBalance": 100.00,
  "currency": "NZD"
}
```

`GET /accounts/{id}/cash-balance` returns `{ "accountId", "currency", "cashBalance" }` with the same numbers.

### 8.2 Errors

Use [api-design.md](../api-design.md) §4.1 only where this spec names `code` / `target`. Ordinary validation stays in `errors` (close-while-cash, post-while-closed, Opening-too-late, CloseOut amount, idempotency mismatch → 409).

Cross-tenant or wrong-collection id → **404**, not 403.

---

## 9. UI

None here. The activity pages are in [docs/adviser-portal/](../adviser-portal/) and [docs/customer-portal/](../customer-portal/). Scalar is enough for SystemAdmin and for local smoke.

---

## 10. Tests

The tests are the executable form of §4. Where they live:

| Project | Where |
| --- | --- |
| Domain.UnitTests | `Entities/TransactionTests.cs`, `Entities/TransactionBookedAtUtcTests.cs` |
| Application.FunctionalTests | Folder `Transactions/`: `CreateTransactionTests`, `TransactionRulesTests`, `ReverseTransactionTests`, `CloseAccountCashTests`, `CashAmountDecimalPlacesTests`, `BookedAtOffsetTests`, `FutureBookedAtTests`. The other files of the folder belong to the specs of §5 |
| Infrastructure.IntegrationTests | `AppliedSchema/TransactionsSchemaTests.cs` |

TestSeed (Development / TestAppHost only): one `TransferIn` on "Demo everyday bank" so Scalar shows a non-zero `SUM`. Functional tests create their own rows.

---

## 11. Locked in this spec

Idempotency: [ADR 0015](../adr/0015-idempotency-keys.md). Direction: [ADR 0011](../adr/0011-ledger-cash-holdings-reversal.md).

| Item | Lock |
| --- | --- |
| Slice packaging | No standalone cash Feature Spec. Cash legs belong here. |
| Public model | **Transaction** = business-event header. **Leg** = accounting line. Two layers, one HTTP write. |
| Not in model | Capture table + approve copy into Journal. `Created` + event-handler auto-approve. Two POST collections (`/journals` and `/transactions`). |
| Events | `TransactionPosted` / `TransactionReversed` are side effects. They must not write the books. |
| Review | A Transaction has no status. Approval is on a record of its own and comes before the Transaction is posted. |
| More legs | A trade, a scrip issue, a transfer: the same `Transaction` with more legs. Not a new write model. |
| Balance storage | `SUM` of posted cash legs. No `Account.Balance`. No projection table. |
| Close | Reject if cash `SUM ≠ 0`. Closed forbids new transactions including reversal. `CloseOut` zeroes the book. `Account.Close()` does not write cash. |
| Legs | Header + `TransactionCashLegs`. Not cash columns on the header. Header **and** cash-leg rows have Created/CreatedBy/LastModified/LastModifiedBy + `RowVersion`. Reverse does not require any `rowVersion`. |
| Mutation | No `UPDATE`/`DELETE` of posted transactions, **except** the single `ReversedByTransactionId` write on reverse. Economic legs are never updated. |
| Line sign | Glossary **Signed book amount**. |

---

## 12. Still open

None.

---

## 13. Cross-account transfer

A transfer is one Transaction with two cash legs on two Accounts, committed atomically: [internal-transfers.md](internal-transfers.md) for one currency (`InternalTransfer`), [fx-conversions.md](fx-conversions.md) for two (`FxConversion`). A cross-currency transfer is never "amount in = amount out" and never uses rate 1.

Two unlinked transactions are not a transfer (R18). Do not seed a pair in TestSeed that looks like one.

---

## 14. History

What changed in this file after it landed. The text of each change is in git (`git log -p -- docs/features/posting.md`).

| Date | With | Change here |
| --- | --- | --- |
| 2026-10-05 | [holdings.md](holdings.md) | R12: close also needs no holding (accounts R16) |
| 2026-10-13 | — | R10, R11, §6: the reverse link in both directions, script `0018` |
| 2026-11-02 | [customer-portal.md](../customer-portal/customer-portal.md) | R15: a Customer reads the transactions of their own accounts |
| 2026-11-10 | [leg-accounts.md](leg-accounts.md) | R5–R9, R11, R14, R19, R22, §5, §6, §8.1: the Account is on each leg |
| 2026-11-14 | [utc-instants](../fixes/utc-instants.md) | R24: `bookedAt` is stored and returned in UTC |
| 2026-11-15 | [trade-fees.md](trade-fees.md) | §5, §8.1: every cash leg has a role. Role grants moved to api-design §3.1 |
| 2026-11-16 | [ledger-write-guards](../fixes/ledger-write-guards.md) | R8 decimal places, R11 latest security activity, R24 no future `bookedAt` |
| 2026-11-17 | [custody-modes.md](custody-modes.md) | R16: the manual entry matrix. R24: the server sets `bookedAt` on a Custodial account |
| 2026-11-19 | [transaction-source.md](transaction-source.md) | §5, §8.1: the source. R11: hand reverse by source on a Custodial account |
| 2026-11-19 | [adjustments.md](adjustments.md) | §5: the type `Adjustment`; `adjustment` on the item |
| 2026-11-20 | [deposit-matching.md](deposit-matching.md) | §5: a `TransferIn` as a deposit; `bankReceipt` on the item |
| 2026-11-20 | [withdrawals.md](withdrawals.md) | §1: instructions. §5: a `TransferOut` as a payout; `payment` on the item. R9: available cash |
| 2026-11-22 | [account-closure.md](account-closure.md) | R6: a closing account. R12, R17: close and `CloseOut` on a Custodial account |
| 2026-11-22 | [interest-run.md](interest-run.md) | §5, R20: `Interest` on a Custodial account, with a `Tax` leg |
| 2026-11-22 | [corporate-actions.md](corporate-actions.md) | §5: `Split` and `Bonus` on a Custodial account |
| 2026-11-23 | [orders.md](orders.md) | §1: an order is an instruction. §5: `Buy` / `Sell` on a Custodial account. R9: reserved cash counts buy orders |
| 2026-11-24 | [migration-batches.md](migration-batches.md) | R19: `Opening` on a Custodial account |
| 2026-11-24 | [in-kind-transfers.md](in-kind-transfers.md) | §1: an in-kind transfer is an instruction. §5: `StockIn` / `StockOut` on a Custodial account |
| 2026-11-24 | [dividends.md](dividends.md) | §5: `Dividend` is active; the `Reinvestment` leg |
| 2026-11-25 | [adjustment-review.md](adjustment-review.md) | §1: no status on a Transaction. R11: an `Adjustment` is never reversed. `POST /transactions/adjustment` removed |
| 2026-11-26 | — | The amendments above were folded into §1–§13. No rule changed |
| 2026-11-26 | [account-valuation.md](account-valuation.md) | §5: the cash of a past day is read by `bookedAt` |
| 2026-11-27 | [external-income.md](external-income.md) | R16: `Dividend` is not entered on `POST /transactions`. R20: a hand `Interest` with tax. §5: `Dividend` by hand on `POST /transactions/dividend`; `Interest` may have `Tax` legs |
