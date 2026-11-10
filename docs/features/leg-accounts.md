---
title: Leg accounts
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-10
last_updated: 2026-11-10
related:
  - posting.md
  - holdings.md
  - buy-sell.md
  - split.md
  - net-worth.md
  - portfolios.md
  - reports.md
  - ../customer-portal/customer-portal.md
  - ../adr/0011-ledger-cash-holdings-reversal.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
---

# Leg accounts

Structural amendment to the Transaction aggregate on `webapi`. The Transaction header stops pointing at an Account (and does not point at a Customer). Every cash leg and every security leg carries its own `AccountId`. A Transaction belongs to the Accounts its legs book into.

This is groundwork for cross-account transfer ([posting.md](posting.md) §13), which is the **next** increment and is not specified here. No new `TransactionType`, no new route, no new policy. Every existing type keeps its leg composition, and in this increment every leg of one Transaction still books into the same Account. The transaction item JSON changes shape now (per-leg `accountId`) so the portals change once, not twice.

**Status is `accepted` (2026-11-10) and landed** on branch `feature/leg-accounts`. Script `0024_leg_accounts.sql`. The amendments to [posting.md](posting.md), [holdings.md](holdings.md), [buy-sell.md](buy-sell.md), [split.md](split.md), [net-worth.md](net-worth.md), [portfolios.md](portfolios.md), [reports.md](reports.md), the portal specs, ADR 0011, domain-model, database-design, api-design and the glossary landed with the implementation.

---

## 1. Summary

```text
Before                                After
Transactions (TenantId, AccountId)    Transactions (TenantId)
  └── TransactionCashLegs               └── TransactionCashLegs (AccountId)
  └── TransactionSecurityLegs           └── TransactionSecurityLegs (AccountId)
```

The header is the business event: type, `bookedAt`, memo, reference, reversal links, tenant. Ownership comes from the legs. Every rule that today reads "the transaction's Account" is restated per **leg account** (glossary): every Account that at least one leg of the Transaction books into.

Visibility and authority are derived from leg accounts:

- **Read**: the caller sees a Transaction if they can see at least one of its leg accounts. The item returns only the legs on Accounts the caller can see. Legs on other Accounts are omitted, not masked.
- **Write** (create, reverse): the caller must be able to write to **every** leg account. Otherwise 404.

`TenantId` stays on the header. A Transaction never spans tenants: every leg account is in the header's tenant.

---

## 2. Scope

**In**

- Domain: `Transaction` without `AccountId`; `TransactionCashLeg.AccountId` and `TransactionSecurityLeg.AccountId`; invariant "at least one leg"; leg-account tenant equals header tenant (application dual check)
- Application: restate create / reverse / list / get / cash balance / holdings projection / net worth / reports / portfolio trade lock on leg accounts
- Script `database/schema/0024_leg_accounts.sql` (forward only, back-fills from the header, then drops `Transactions.AccountId`)
- HTTP: transaction item and list item move to per-leg `accountId` / `customerId`; `cash` becomes an array; top-level `accountId` / `customerId` removed
- Leg filtering on read (R7)
- Adviser Portal and Customer Portal consume the new item shape
- Tests listed in §10

**Out**

- Cross-account transfer, the new transfer type and its name, and the create body for it — next increment
- Fees on a transfer — later, designed together with other fee work
- Cross-currency transfer
- Any change to the create body of existing types (top-level `accountId` stays)
- Any change to `TransactionType`, its sign rules, or its leg composition
- `TenantId` on leg rows
- `BookedAt` copied onto leg rows
- A `CustomerId` on the header
- Cross-tenant Transactions (see §13)

---

## 3. Stories

1. As an Adviser I keep booking every existing type exactly as today. The create body is unchanged.
2. As an Adviser I read a transaction and each leg tells me which Account it booked into.
3. As an Adviser I cannot read a transaction none of whose leg accounts belong to my assigned Customers (404).
4. As an Adviser I cannot create or reverse a transaction unless every leg account belongs to my assigned Customers (404).
5. As a Customer I see a transaction that books into one of my accounts, and I see only the legs on my accounts.
6. As a later increment I add a transfer type whose two cash legs book into two Accounts, without another schema change to the header.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Unchanged from posting R1–R4a: 401 / 403 / 404, PublicId only, `/transactions` root, `tenantId` rules for TenantAdmin / Adviser / SystemAdmin. |
| R2 | The header has `TenantId` and no `AccountId` and no `CustomerId`. Every cash leg and every security leg has a required `AccountId`. |
| R3 | A Transaction has at least one leg. Every leg account is in the header's tenant (application dual check, as posting R5 does today for the single Account). |
| R4 | **Leg accounts in this increment.** Every existing type builds its legs on the single `accountId` from the create body. A Reversal builds each opposite leg on the same Account as the original leg. No type in this increment produces legs on two Accounts. |
| R5 | **Create.** The body keeps top-level `accountId` for every existing type. Application builds the legs from `type` (unchanged) and stamps that Account on each leg. Clients never send a legs array. |
| R6 | **Write authority.** Create and reverse require the caller to see every leg account (TenantAdmin: same tenant; Adviser: assigned Customer; SystemAdmin: any). One leg account outside scope → 404 for the whole command. Nothing is written. |
| R7 | **Read visibility.** Get and list return a Transaction if the caller can see at least one leg account. Otherwise get → 404 and list omits it. The item's `cash` and `security` arrays contain only legs on Accounts the caller can see. Legs on other Accounts are omitted; the response gives no count, account id, customer id or name for them. Customer = own accounts only (Customer Portal amendment). |
| R8 | **Account and Customer state.** Posting R6 (Account Closed → 400) and R7 (Customer Disabled → 400 `disabled` / `target=user`) apply to every leg account and its Customer, on create and on reverse. |
| R9 | **Currency.** Posting R8 applies per cash leg: the leg's currency equals the leg account's `Account.Currency`. |
| R10 | **Cash `SUM`.** Cash balance of an Account is `SUM(TransactionCashLegs.Amount WHERE AccountId = account)`. Posting R9 (`SUM ≥ 0` after insert) is checked for every leg account touched by the command. |
| R11 | **Locking.** The command locks every leg account row before inserting and summing, in ascending `Accounts.Id` order. Same rule for create and reverse. Replaces "lock the Account row" in posting R9 / R22, holdings R14, buy-sell R15. |
| R12 | **Reverse.** Replaces posting R11 "same account". The Reversal has one opposite leg per original leg, each on that leg's Account. All of R6, R8, R10, R11 apply to every leg account. If any one fails the whole reverse fails; there is no partial reverse. Already reversed, reversal of a Reversal, single `ReversedByTransactionId` write: unchanged. |
| R13 | **Opening.** Posting R19 "the account has no other transaction" becomes "no leg of any other Transaction books into this Account". |
| R14 | **CloseOut and close.** Posting R12 / R17 read the cash `SUM` from R10. Unchanged otherwise. |
| R15 | **Holdings projection.** Keyed by the security leg's `(AccountId, InstrumentId)`. Projection quantity = `SUM` of security-leg quantities with that key. Cost rules from holdings / buy-sell / split are unchanged. |
| R16 | **Bank / Cash refuse holdings.** Checked per security leg against that leg's Account type. |
| R17 | **Portfolio trade lock.** Portfolios R18 applies when any security leg's Account has an Active assignment. Rebalance still writes ordinary `Buy` / `Sell` on the assigned Account. |
| R18 | **List filters.** `accountId` matches a Transaction with at least one leg on that Account. `customerId` matches a Transaction with at least one leg on an Account of that Customer. Filters are applied after the R7 visibility scope. `totalCount` counts Transactions, not legs. Sort `bookedAt`, then id (unchanged). |
| R19 | **Net worth.** Cash per Account from R10. Holdings from the projection (R15). Unchanged aggregation otherwise. |
| R20 | **Customer Transactions report.** One row per leg account of the Customer: a transaction appears in the account it books into. The closed-account filter is evaluated per leg account. In this increment this produces the same rows as today. |
| R21 | Idempotency (ADR 0015), immutability (posting R10), `bookedAt`, memo, reference: unchanged. |

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `Transaction` | Aggregate | Remove `AccountId`. Factories still take one `accountId` for existing types and stamp it on each leg they build. `Reverse` copies each original leg's Account onto its opposite leg. |
| `TransactionCashLeg` | Entity | Add required `AccountId`. |
| `TransactionSecurityLeg` | Entity | Add required `AccountId`. |
| `TransactionPosted` / `TransactionReversed` | Event | Unchanged. Handlers that need the Account read it from the legs. |

Invariants:

- At least one leg.
- Every leg has an `AccountId`.
- Cash legs: at most one per `(Transaction, Account)`. Security legs: at most one per `(Transaction, Account, Instrument)`.
- Each existing type's leg composition and sign rules are unchanged (posting §5, holdings, buy-sell, split).
- The header is still immutable after insert except the single `ReversedByTransactionId` write.

---

## 6. Database

Script: `database/schema/0024_leg_accounts.sql`. Forward only. Do not edit `0013` / `0014` / `0018`. There is no production data. Development and test databases are migrated by the same script.

| Table | Change | Index / FK |
| --- | --- | --- |
| `TransactionCashLegs` | add `AccountId int NOT NULL` | FK `AccountId` → `Accounts` RESTRICT. Drop `UQ_TransactionCashLegs_TransactionId`. Add unique `(TransactionId, AccountId)`. Index `(AccountId)` INCLUDE `(Amount)`. |
| `TransactionSecurityLegs` | add `AccountId int NOT NULL` | FK `AccountId` → `Accounts` RESTRICT. Drop `UQ_TransactionSecurityLegs_TransactionId_InstrumentId`. Add unique `(TransactionId, AccountId, InstrumentId)`. Index `(AccountId, InstrumentId)` INCLUDE `(Quantity, CostAmount)`. |
| `Transactions` | drop `AccountId` | Drop `FK_Transactions_Accounts_AccountId` and `IX_Transactions_TenantId_AccountId_BookedAt`. Add index `(TenantId, BookedAt)`. |
| `Holdings` | none | Still unique `(AccountId, InstrumentId)`. |

Script order:

1. Add `AccountId` as nullable on both leg tables.
2. Back-fill each leg from its header's `AccountId`. Unlike `0018`, this script back-fills on purpose: without it the column cannot become `NOT NULL`.
3. Make both columns `NOT NULL`. Add FKs, unique constraints and indexes. Drop the old unique constraints.
4. Drop the header index, FK and column. Add the `(TenantId, BookedAt)` index.

No `TenantId` on leg rows: tenant comes from the header and every leg account is checked against it (R3). No `CustomerId` anywhere on the ledger tables.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Command | `CreateTransaction` | Stamp the body `accountId` on every leg. R6, R8–R11, R13, R16, R17 on the leg-account set. |
| Command | `ReverseTransaction` | Leg-account set from the original's legs. R6, R8, R10–R12, R17. |
| Command | `CloseAccount` | Cash `SUM` from R10. |
| Query | `GetTransactions` | Scope and filters via legs (R7, R18). Legs filtered per R7. |
| Query | `GetTransactionById` | 404 unless a leg account is visible. Legs filtered per R7. |
| Query | `GetAccountCashBalance` | R10. |
| Query | Holdings, net worth | R15, R19. |
| Worker | Customer Transactions report | R20. |
| Command | Portfolio rebalance / assign | R17. |

---

## 8. API

No new routes. No new policies. Create body unchanged.

### 8.1 Transaction item (list and get)

Replaces the item shape in [posting.md](posting.md) §8.1, [buy-sell.md](buy-sell.md) §8.1 and [split.md](split.md) §8.1.

```json
{
  "id": "<transaction publicId>",
  "tenantId": "<tenant publicId>",
  "type": "Buy",
  "bookedAt": "2026-10-11T10:00:00+13:00",
  "memo": null,
  "reference": null,
  "originalTransactionId": null,
  "reversedByTransactionId": null,
  "rowVersion": "<base64>",
  "cash": [
    {
      "accountId": "<account publicId>",
      "customerId": "<customer publicId>",
      "amount": -1250.00,
      "currency": "NZD"
    }
  ],
  "security": [
    {
      "accountId": "<account publicId>",
      "customerId": "<customer publicId>",
      "instrumentId": "<instrument publicId>",
      "quantity": 50,
      "cost": 750.00,
      "costCurrency": "USD"
    }
  ]
}
```

- Top-level `accountId` and `customerId` are removed.
- `cash` is always an array (was an optional object). Security-only types return `cash: []`. Cash-only types return `security: []`.
- Both arrays contain only legs the caller can see (R7).
- The Reverse-button rule is unchanged: hide when `reversedByTransactionId` is not null or `type` is `Reversal`.

### 8.2 Errors

Unchanged. A leg account outside the caller's scope → 404, not 403.

---

## 9. UI

Shape change only. No new page.

- Adviser Portal: transactions list, transaction detail, account activity read `accountId` / `customerId` and amounts from the legs. Amount shown for an account row = the cash leg on that account.
- Customer Portal: account activity (`GET /transactions?accountId=`) reads the same shape.
- Create forms are unchanged.

Amend [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) and [customer-portal.md](../customer-portal/customer-portal.md) in the same change that accepts this spec.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | Every factory stamps the given Account on each leg it builds; Reverse copies each leg's Account; a Transaction with no leg cannot be built |
| Application.FunctionalTests | Every existing type still creates and reverses as before; item has per-leg `accountId` / `customerId`, `cash` is an array, no top-level `accountId`; list `accountId` / `customerId` filters still return the same rows; cash balance, holdings, net worth unchanged for the seed data; Adviser unassigned leg account → 404 on get, create and reverse |
| Infrastructure.IntegrationTests | Other tenant's transaction id → 404 (two-tenant fixture); `Transactions` has no `AccountId` column; both leg tables have `AccountId NOT NULL` with FK; `0024` back-fills an existing leg from its header |
| Leg filtering (R7) | Cannot be exercised end to end until a type writes legs on two Accounts. Cover it at the query layer with a hand-inserted two-Account Transaction in the integration fixture: a caller who sees only Account A gets only A's legs and no reference to B; a caller who sees neither gets 404 |

---

## 11. Locked in this spec (discussion, 2026-11-10)

| Item | Lock |
| --- | --- |
| Header ownership | No `AccountId`, no `CustomerId` on the header. Transfers may later span Customers. |
| Tenant | `TenantId` stays on the header. A Transaction never spans tenants. |
| Leg ownership | `AccountId` required on every cash and security leg. |
| Write authority | Caller must see every leg account. Applies to create and reverse. |
| Read visibility | Any visible leg account. Other legs omitted, not masked. |
| Reverse | One Reversal Transaction with an opposite leg per original leg; all-or-nothing. |
| Item JSON | Per-leg `accountId` / `customerId` now (option A), so the API breaks once. |
| Create body | Unchanged in this increment. |
| Script | `0024_leg_accounts.sql`, forward only, back-fills. No production data. |
| `TransferIn` / `TransferOut` | Keep today's meaning: one cash leg, off-books counterparty. |

---

## 12. Still open (next increment, not this spec)

- Name and create body of the cross-account transfer type. Recommendation from discussion: do not reuse `TransferIn` / `TransferOut`, because one header has one `Type` and a linked transfer is both directions.
- Cross-currency transfer.
- In-kind transfer (security legs on two Accounts).
- Fees — deferred, to be designed with other fee work.

---

## 13. Decided for the transfer increment

Recorded here so the next spec does not reopen them:

- A transfer between two Accounts of different Customers in the same tenant is one Transaction with legs on both Accounts.
- A cross-tenant movement is two separate, unlinked Transactions: a `TransferOut` in one tenant and a `TransferIn` in the other. No reference between them. This agrees with posting R18.
- An Adviser creates or reverses a transfer only if both Customers are assigned to them (R6).
- The other side's legs are hidden from a caller who cannot see that Account (R7).
- A transfer has no fee leg until fees are designed.

---

## 14. Suggested commits

Only after `accepted`. Tree must build after each commit. Split each behaviour into red then green.

1. `0024_leg_accounts.sql` + EF mapping (leg `AccountId`, header without `AccountId`) + integration tests for columns, FKs and back-fill. Domain and application move to leg accounts in the same commit so the tree builds; existing functional tests stay green.
2. Write authority on every leg account (R6) + reverse per leg (R12) + lock order (R11) + tests.
3. Read visibility and leg filtering (R7, R18) + integration tests with a hand-inserted two-Account Transaction.
4. Item JSON per-leg shape (§8.1) + functional tests.
5. Adviser Portal and Customer Portal consume the new item shape + frontend tests.
6. Repo docs: this file `accepted`; amend posting, holdings, buy-sell, split, net-worth, portfolios, reports, customer-portal, adviser-portal-dashboard, ADR 0011, domain-model, database-design, api-design, glossary (Transaction, Leg).
