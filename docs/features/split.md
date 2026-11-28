---
title: Split / Bonus / Scrip
status: accepted
phase: 2
language: en
owner: ""
created: 2026-10-12
last_updated: 2026-11-28
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../architecture.md
  - ../glossary.md
  - ../adr/0011-ledger-cash-holdings-reversal.md
  - instruments.md
  - accounts.md
  - posting.md
  - holdings.md
  - buy-sell.md
  - custody-modes.md
  - dividends.md
---

# Split / Bonus / Scrip

Security-only legs on the existing Transaction aggregate. Public write stays `POST /transactions`. No new table. A small script widens `CK_Transactions_Type` for the new type values. **Total cost is unchanged** — quantity moves, average cost is diluted. Amends the type tables in [posting.md](posting.md), [holdings.md](holdings.md), and [buy-sell.md](buy-sell.md).

Do not model these as `StockIn` / `StockOut`. No cash leg. No portal page. No price call.

On an External account a person enters the three types with the new total quantity. On a Custodial account no person enters them: `Split` and `Bonus` are written by corporate actions from a ratio ([corporate-actions.md](corporate-actions.md)), and `Scrip` is not written at all.

This file states the rules as they are today. §14 lists what changed here, by date.

**Status is `accepted`.** Implementation follows this file. `Split` = 12, `Bonus` = 13, `Scrip` = 14. Script `0017_transaction_type_split.sql` widens `CK_Transactions_Type` to 0–14.

---

## 1. Summary

A TenantAdmin or Adviser records a `Split`, `Bonus`, or `Scrip` on an **Open** Brokerage or Other account of an assigned / same-tenant Customer. SystemAdmin does the same on Scalar for a chosen tenant. Customer receives 403. Bank and Cash refuse every security leg.

The header is the existing Transaction. Exactly one security leg, no cash leg. The holding must already exist. The client sends the **new total quantity**. The server stores the signed delta and leaves the holding's total cost as it was. Average cost falls when quantity rises, and rises when quantity falls (a reverse split). A later corporate-action workflow may accept a ratio and post the resulting quantity here; this slice does not.

This increment treats the three type names as the same book rule. The name is the business label; the legs are identical.

---

## 2. Scope

**In**

- Domain: types `Split`, `Bonus`, `Scrip` on the existing `Transaction` aggregate. Exactly one security leg. No cash leg. Holding total cost unchanged.
- Application: extend `CreateTransaction` / `ReverseTransaction` for the three types.
- Script `database/schema/0017_transaction_type_split.sql` widens `CK_Transactions_Type` past 11. No new table or column.
- No new policy. Writes reuse `transactions.create`. Holdings read stays `accounts.read`.
- Isolation tests + Development / TestAppHost `TestSeed` (one `Split` on an existing demo holding).
- Amend the type tables in [posting.md](posting.md), [holdings.md](holdings.md), and [buy-sell.md](buy-sell.md) in the same accept change.

**Out**

- Cash on these types. A `cash` / `amount` field → 400.
- Client-supplied cost. Cost is not an input.
- `IMarketData` / `IFxRate` on this slice.
- Creating a holding. No existing holding → 400. Not a first-activity type.
- Modelling a split as `StockIn` / `StockOut`.
- Adviser Portal pages.
- Price or market value on the holdings read. Net worth. Dashboard.
- Fees, lots, fractional-share cash-in-lieu.
- Live vendor corporate-action feeds.
- Multi-instrument header.
- Cross-account transfer. Property / Credit. Customer ledger-write.

---

## 3. Stories

1. As an Adviser I record a `Split` by sending the new total quantity so the book doubles (or whatever the new figure is) and total cost stays the same.
2. As an Adviser I record a `Bonus` or `Scrip` by sending the new total quantity — not a ratio.
3. As an Adviser I record a reverse split by sending a smaller new quantity. Total cost stays the same.
4. As an Adviser I cannot book this on Bank or Cash, or on an instrument with no holding (400).
5. As an Adviser I reverse it. Quantity returns. Total cost never moved.
6. As a Customer I receive 403.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Policy fail → 403. TenantAdmin / Adviser: other-tenant, unassigned Customer, missing id → 404. SystemAdmin: missing id → 404; another tenant’s id is allowed. |
| R2 | Path and JSON `id` are `PublicId`. Internal ints never appear. Create / reverse return `201 { "id" }` of the Transaction. |
| R3 | Writes stay `POST /transactions` and `POST /transactions/{id}/reverse`. Not under `/users`. |
| R4 | TenantAdmin / Adviser omit `tenantId` (400 if present). SystemAdmin create requires `tenantId`. |
| R5 | Account must be Open, same tenant, and visible to the caller. Closed → 400. Disabled Customer on create / reverse → 400 `disabled` / `target=user`. |
| R6 | Account type must be Brokerage or Other. Bank / Cash → 400. |
| R7 | Instrument must exist, same tenant, and `IsActive`. Disabled or missing → 400. |
| R8 | The types of this spec are `Split`, `Bonus` and `Scrip`. On an External account a person enters them on `POST /transactions`; the entry has source `Manual` and no corporate action detail. On a Custodial account the hand entry of all three is refused (400 `errors.Type`, custody-modes §4.2.1). There `Split` and `Bonus` are written by corporate actions from a ratio (`Transaction.PostCorporateAction`, source `CorporateAction`): one security leg of `quantityAfter − quantityBefore` with cost `0`, no cash leg, and a corporate action detail (corporate-actions R26, R27); a consolidation is posted as `Split`. `Scrip` is not written on a Custodial account: a dividend taken in shares is a `Dividend` with a reinvestment (a `Reinvestment` cash leg and a security leg with a cost; dividends R40). Unknown → 400. |
| R9 | Body `security` is an array of exactly one leg: `instrumentId` and `quantity` `> 0` (the **new total**). No `ratio`, no `cost`, no `price`, no `cash` / `amount` → 400 if present. |
| R10 | A holding for that `(Account, Instrument)` must already exist. No holding → 400. This type does not create a holding. |
| R11 | Body quantity is the new total, `decimal(18,8)`, `> 0`, and not equal to the current quantity (400 if equal). Delta = new − old. Stored security-leg quantity is the signed delta. A reverse split is a smaller new quantity. |
| R12 | No cash leg. Holding **total cost is unchanged**. Average cost becomes total cost / new quantity. The security leg stores cost `0` in the quote currency (nothing was added or released). This holds for an entitlement posting too: average cost becomes total cost / `quantityAfter` (corporate-actions R29). The tax year read takes a `Split`, `Bonus` or `Scrip` as a reorganisation with no amount; a `Scrip` also gets a notice, because the platform cannot know whether the issue was taxable ([tax-year-events.md](tax-year-events.md) R13). A reorganisation keeps the tax cost of the threshold test ([tax-cost-threshold.md](tax-cost-threshold.md) R8). In FIF income it is neither a purchase nor a sale, and the quick sale is counted on the basis of the year's end ([fif-income.md](fif-income.md) R6, R9). |
| R13 | After insert, quantity `SUM` per `(Account, Instrument)` `> 0` and equals the new quantity. Projection cost equals the cost from before the command. |
| R14 | Posted rows are not updated or deleted. Reverse copies the opposite security quantity and cost `0`. One reversal per original. No client `rowVersion`. Account Open. Reverse that would make quantity `≤ 0` → 400; original stays. Only the latest security activity on a holding can be reversed: a live later security leg on the same `(Account, Instrument)` → 400 ([ledger-write-guards](../fixes/ledger-write-guards.md)). By hand, on a Custodial account only a Transaction with source `Manual` is reversed (transaction-source R13). |
| R15 | `Idempotency-Key` required on create and reverse, same as posting. |
| R16 | `bookedAt` required instant, as [posting](posting.md) R24. Memo optional 1–200. Reference optional, stored, no uniqueness. |
| R17 | Customer has no ledger-write policy → 403. |
| R18 | Close still requires cash `SUM = 0` and no `Holdings` row. These types do not change that guard. |

Policies: no new names. `transactions.create` for the three types and reverse.

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `Transaction` | Aggregate (existing) | One security leg, no cash leg. Still one write. |
| `TransactionSecurityLeg` | Entity (existing) | Exactly one instrument. Quantity = signed delta. Cost `0`. |
| `Holding` | Projection (existing) | Quantity changes. Total cost unchanged. Quantity `0` is not a valid result here. |
| `TransactionType` | Enum | Add `Split` (12), `Bonus` (13), `Scrip` (14). |

Invariants:

- Three names, one book rule this increment.
- Total cost does not move. Do not call average-cost release.
- No price. No FX. No cash.
- Insert lands Posted.

Domain events: existing `TransactionPosted` / `TransactionReversed`.

Update [domain-model.md](../domain-model.md) in the same accept change.

---

## 6. Database

No new table and no new column. Tables stay those in [holdings.md](holdings.md). Do not add EF migrations.

`CK_Transactions_Type` is 0–11 after `0016`. New enum values need a wider check.

Script: `database/schema/0017_transaction_type_split.sql` — drop and re-add the check so `Type` is 0–14 (`Split` 12, `Bonus` 13, `Scrip` 14).

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreateTransaction` (extend) | Transaction `PublicId` | Type in the three; account Open + Brokerage/Other; instrument active; holding exists; new quantity `> 0` and `≠` current; projection quantity updated; cost unchanged |
| Command | `ReverseTransaction` (extend) | new Transaction `PublicId` | Opposite quantity delta; cost still unchanged; result quantity `> 0`; one reverse |
| Query | `GetAccountHoldings` (existing) | items | Unchanged. Still no market value. |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| POST | `/transactions` | `transactions.create` | 201 `{ "id" }` | 400 / 401 / 403 / 404 / 409 |
| POST | `/transactions/{id}/reverse` | `transactions.create` | 201 `{ "id" }` | 400 / 401 / 403 / 404 / 409 |

Create and reverse require `Idempotency-Key`.

### 8.1 Bodies

```json
{
  "accountId": "<account publicId>",
  "type": "Split",
  "bookedAt": "2026-10-12T10:00:00+13:00",
  "memo": null,
  "reference": null,
  "security": [
    {
      "instrumentId": "<instrument publicId>",
      "quantity": 200
    }
  ]
}
```

`quantity` is the **new total**, not a delta. 100 shares and `"quantity": 200` is a 2-for-1. A bonus of 15 on 100 is `"quantity": 115`. A reverse split to half is `"quantity": 50`. `Bonus` and `Scrip` use the same body. SystemAdmin create adds `tenantId`. TenantAdmin / Adviser omit it.

Transaction item (no cash):

```json
{
  "id": "<transaction publicId>",
  "type": "Split",
  "bookedAt": "2026-10-11T21:00:00Z",
  "cash": [],
  "security": [
    {
      "accountId": "<account publicId>",
      "customerId": "<customer publicId>",
      "instrumentId": "<instrument publicId>",
      "quantity": 100,
      "cost": 0.00,
      "costCurrency": "USD"
    }
  ]
}
```

`quantity` on the item is the signed delta. Item shape is [leg-accounts.md](leg-accounts.md) §8.1. These types return `cash: []`.

### 8.2 Errors

Ordinary validation stays in `errors`: Bank/Cash, disabled instrument, no holding, quantity not `> 0`, quantity equal to current, cost/price/cash/ratio field present on the body. Disabled Customer uses `code=disabled` / `target=user`. Cross-tenant or wrong-collection id → 404. Idempotency mismatch → 409.

---

## 9. UI

None. Pages live in [docs/adviser-portal/](../adviser-portal/). Scalar is enough.

---

## 10. Tests

The tests are the executable form of §4. Where they live:

| Project | Where |
| --- | --- |
| Domain.UnitTests | `Entities/SplitTests.cs` |
| Application.FunctionalTests | `Transactions/SplitTests.cs`; entitlement postings in `CorporateActions/` |
| Infrastructure.IntegrationTests | Two-tenant fixture: other tenant's account or instrument → 404 |

TestSeed (Development / TestAppHost only): one `Split` on the existing demo brokerage holding. Not a Buy. Functional tests create their own rows.

---

## 11. Locked in this spec (from earlier slices; not reopened)

No new ADR.

| Item | Lock |
| --- | --- |
| Write path | Same `Transaction` aggregate. Security-only. Not a new POST collection. Not `StockIn` / `StockOut`. |
| Cost | Total cost unchanged. Average is diluted or concentrated. No average-cost release. No client cost. |
| Cash | None. |
| Caller | TenantAdmin; Adviser (assigned); SystemAdmin (Scalar). Customer 403. Same as buy-sell. |
| Portal | None this slice. |
| Item shape | Optional `cash` + optional `security`, already locked in buy-sell. |

---

## 12. Locked in this slice (2026-10-12)

| Item | Lock |
| --- | --- |
| Names | `Split`, `Bonus`, `Scrip`. Same book rule. |
| Input | New **total** quantity, `> 0`, `≠` current, `decimal(18,8)`. Not a ratio. Not a delta. |
| Ratio | Not this slice. A later corporate-action request may take a ratio and post the resulting quantity here. |
| Cost on the leg | `0` in the quote currency. Holding total cost unchanged. |
| Reverse split | Yes. Smaller new quantity. Reject if the new quantity is `≤ 0` or equal to current. |

Open questions: none that block review.

---

## 13. Out / later

- Fractional-share cash-in-lieu.
- A vendor corporate-action feed on External accounts.
- Price / market value on the holdings read. Net worth. Dashboard.
- Fees, lots, realized gain.
- Multi-instrument header.
- A different cost rule per name (for example bonus at a stated cost). That would be a later amendment, not this slice.
- A corporate-action workflow that accepts a ratio and computes the quantity is [corporate-actions.md](corporate-actions.md). It posts the same legs; it is not this spec.

---

## 14. History

What changed in this file after it was accepted. The text of each change is in git (`git log -p -- docs/features/split.md`).

| Date | With | Change here |
| --- | --- | --- |
| 2026-11-10 | [leg-accounts.md](leg-accounts.md) | §8.1: the Account is on the security leg |
| 2026-11-14 | [utc-instants](../fixes/utc-instants.md) | R16 follows posting R24 |
| 2026-11-16 | [ledger-write-guards](../fixes/ledger-write-guards.md) | R14: only the latest security activity is reversed |
| 2026-11-17 | [custody-modes.md](custody-modes.md) | R8: the hand entry is refused on a Custodial account |
| 2026-11-22 | [corporate-actions.md](corporate-actions.md) | R8, R12, §13: `Split` and `Bonus` on a Custodial account are entitlement postings |
| 2026-11-24 | [dividends.md](dividends.md) | R8: `Scrip` is not written on a Custodial account |
| 2026-11-26 | — | The changes above were folded into the body. No rule changed |
| 2026-11-28 | [tax-year-events.md](tax-year-events.md) | R12: a `Scrip` is a reorganisation with a notice in the tax year read |
| 2026-11-28 | [tax-cost-threshold.md](tax-cost-threshold.md) | R12: a reorganisation keeps the tax cost |
| 2026-11-28 | [fif-income.md](fif-income.md) | R12: a reorganisation is neither a purchase nor a sale in FIF income; the quick sale is on the basis of the year's end |
