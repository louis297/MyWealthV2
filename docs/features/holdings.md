---
title: Holdings
status: accepted
phase: 2
language: en
owner: ""
created: 2026-09-28
last_updated: 2026-11-28
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
  - instruments.md
  - accounts.md
  - posting.md
  - custody-modes.md
  - migration-batches.md
  - in-kind-transfers.md
  - dividends.md
---

# Holdings

Security legs, holdings projection, and Opening of holdings on `webapi`. Public write stays `POST /transactions`. Adds script `0014_holdings.sql`. Amends [posting.md](posting.md) with type `OpeningHoldings` and amends [accounts.md](accounts.md) R16 so close also requires a flat holdings book.

Buy / Sell / StockIn / StockOut are accepted in [buy-sell.md](buy-sell.md). Split / Bonus / Scrip are accepted in [split.md](split.md). §5 lists everything that changes a holding today and the spec that owns it. No portal page.

This file states the rules as they are today. §12 lists what changed here, by date.

**Status is `accepted` and landed** on repo master `676e77a` (PR #1, 2026-10-11). Scripts `0014_holdings.sql` and `0015_transaction_type_opening_holdings.sql` (`CK_Transactions_Type` widened to 0–9; `0016` widens it to 0–11; `0017` widens it to 0–14). `GET /accounts/{id}/holdings` returns `{ "items": [...] }`, not a paged list. TestSeed has one `OpeningHoldings` on the demo brokerage, plus a funding transfer, a Buy, a StockIn, and a Split.

---

## 1. Summary

A TenantAdmin or Adviser records `OpeningHoldings` on an **Open** Brokerage or Other account of an assigned / same-tenant Customer. SystemAdmin does the same on Scalar for a chosen tenant. Customer receives 403. Bank and Cash refuse every security leg.

The header is the existing Transaction. Security legs are the activity. `Holdings` is a projection upserted in the same command, not a second trade book and not a hand edit. Quantity is the `SUM` of signed security-leg quantities. Cost is average cost in the instrument quote currency. This increment only adds cost.

Cash position stays the `SUM` of cash legs. An `OpeningHoldings` header has no cash leg.

---

## 2. Scope

**In**

- Domain: `TransactionSecurityLeg`; `Holding` projection; type `OpeningHoldings`
- Application: extend `CreateTransaction` / `ReverseTransaction` for `OpeningHoldings`; `GetAccountHoldings`; amend `CloseAccount`
- Script `database/schema/0014_holdings.sql` + EF mapping
- No new write policy. Writes reuse `transactions.create`. Read uses `accounts.read`
- `GET /accounts/{id}/holdings`
- Isolation tests + Development / TestAppHost `TestSeed`
- Amend posting type table and accounts R16 in the same accept change

**Out**

- Buy / Sell / StockIn / StockOut. Accepted in [buy-sell.md](buy-sell.md)
- Split / Bonus / Scrip. Accepted in [split.md](split.md). Same tables. Not `StockIn` / `StockOut`
- Dividend HTTP. Ordinary cash, no instrument id. Not this file
- DRIP and corporate-action dividend
- Adviser Portal pages
- Net worth, Dashboard, price on the holdings read
- Snapshots of a past day: the quantity of a past day is read from the security legs by [account-valuation](account-valuation.md) R4; nothing is stored
- Cross-account transfer
- Live vendor market data / live FX
- Property / Credit selectable
- Lots, wash sales, realized-gain reports, fees
- Short positions
- Customer ledger-write

- A second POST collection
- `UPDATE` / `DELETE` of a posted transaction
- Hand-edited holding quantity or cost
- CloseOut-of-stock
- Seeding from the schema script or in Production

---

## 3. Stories

1. As an Adviser I record `OpeningHoldings` on a Brokerage account so quantity and cost exist without a buy.
2. As an Adviser I open another instrument with another `OpeningHoldings`. The same instrument twice → 400.
3. As an Adviser I cannot book a security leg on Bank or Cash (400).
4. As an Adviser I cannot pick a disabled instrument for a new leg (400). Existing legs stay.
5. As an Adviser I list holdings and see quantity and cost, not market value.
6. As an Adviser I reverse `OpeningHoldings`. The original row stays.
7. As an Adviser I cannot close while cash `SUM ≠ 0` or any quantity remains. Close does not liquidate.
8. As a Customer I read the holdings of my own accounts. I receive 403 on every write.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Policy fail → 403. TenantAdmin / Adviser: other-tenant, unassigned Customer, missing id → 404. SystemAdmin: missing id → 404; another tenant’s id is allowed. |
| R2 | Path and JSON `id` are `PublicId`. Internal ints never appear. Create / reverse return `201 { "id" }` of the Transaction. |
| R3 | Writes stay `POST /transactions` and `POST /transactions/{id}/reverse`. Holdings read is `GET /accounts/{id}/holdings`. Not under `/users`. |
| R4 | TenantAdmin / Adviser omit `tenantId` (400 if present). SystemAdmin create requires `tenantId`, same as posting. Holdings read uses the account PublicId only. |
| R5 | Account must be Open, same tenant, and visible to the caller. Closed → 400. Disabled Customer on create / reverse → 400 `disabled` / `target=user`. |
| R6 | The Account of each security leg must be Brokerage or Other (leg-accounts R16). Bank or Cash → 400. Property and Credit are not selectable. A Custodial account is always `Brokerage`. |
| R7 | Each security leg stores `InstrumentId` only. Instrument must exist in the same tenant. Disabled instrument on a new leg → 400. Reverse of an existing leg is allowed. Disable instrument is still allowed while holdings exist. |
| R8 | `OpeningHoldings` is the type of this spec. The other types that change a holding are in §5. On a Custodial account a hand `OpeningHoldings` is refused (400 `errors.Type`, custody-modes R5): there it is written only by a migration batch, on an empty account, with the cost the batch carries in the instrument's quote currency (migration-batches R10, R22). Cash `Opening` rules are posting R19. |
| R9 | `security` is an array of 1..n legs. Each leg: `instrumentId`, `quantity` `> 0`, `cost` `≥ 0`. Duplicate instrument in the array → 400. No `amount` and no `cash` on this type → 400 if present. |
| R10 | Cost currency is the instrument quote currency. Body must not send a currency. A later-disabled catalog currency does not rewrite stored cost currency. |
| R11 | At most one `OpeningHoldings` per `(Account, Instrument)`, including a reversed one until the product says otherwise: a second header that names an instrument already opened → 400. `OpeningHoldings` must be the first security activity for that instrument. |
| R12 | No auto zero row. Create Account does not insert a holding. |
| R13 | After insert, quantity `SUM` per `(Account, Instrument)` `≥ 0`. Stored quantity is `decimal(18,8)`. Cost amount is `decimal(18,4)`. On a Custodial account a sell order and an `Adjustment` that lowers a holding must also leave **available quantity** `≥ 0` (orders R13, R40); a fill, a rebalance and an entitlement posting are not refused by this rule. |
| R14 | Projection upsert in the same command, with every leg account locked in ascending `Accounts.Id` order (leg-accounts R11). The projection key is the security leg's `(AccountId, InstrumentId)` (leg-accounts R15). Callers never PUT a holding. Quantity 0 deletes the projection row. Tests assert projection quantity = `SUM` of security-leg quantities. A platform-side flow that writes a security leg updates the projection in the same Account-locked database transaction. The projection is the quantity now; the quantity of a past day is the sum of the security legs booked before that day ended ([account-valuation](account-valuation.md) R4), not this row. The quantity of a tax holding at an instant is a sum over all the Customer's Accounts, with reversed Transactions left out ([tax-year-events](tax-year-events.md) R22). |
| R15 | Posted rows are not updated or deleted. Reverse copies the opposite security quantity and cost. One reversal per original. No client `rowVersion`. Account Open. Result must stay `≥ 0`. Only the latest security activity on a holding can be reversed: a live later security leg on the same `(Account, Instrument)` → 400 ([ledger-write-guards](../fixes/ledger-write-guards.md)). An adjustment, an order fill, an entitlement posting, an in-kind transfer and a reinvested dividend all count as security activity. By hand, on a Custodial account only a Transaction with source `Manual` is reversed (transaction-source R13). |
| R16 | `CloseAccount` rejects unless cash `SUM = 0` and no `Holdings` row remains for that account. Amends accounts R16. `Account.Close()` writes neither cash nor holdings. |
| R17 | `Idempotency-Key` required on create and reverse, same as posting. |
| R18 | `bookedAt` required instant, as [posting](posting.md) R24. Memo optional 1–200. Reference optional, stored, no uniqueness. |
| R19 | Customer has no ledger-write policy → 403. Holdings read is `accounts.read`, so a caller who can see the account can see its positions: a Customer reads the holdings of their own accounts and gets 404 for another Customer's account. No `holdings.create`. |
| R20 | Account list and item do not embed holdings. |

Policies: no new names. `transactions.create` for Opening and reverse. `accounts.read` for the holdings read.

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `Transaction` | Aggregate (existing) | Grows security legs. Still one write. |
| `TransactionSecurityLeg` | Entity | One instrument on one header. Several per header allowed. |
| `Holding` | Projection | Per `(Account, Instrument)`: quantity + cost. `PublicId`. |
| `TransactionType` | Enum | Add `OpeningHoldings`. Cash `Opening` unchanged. |

`OpeningHoldings` has security legs only. Buy / Sell add a cash leg on this same header ([buy-sell.md](buy-sell.md)). Split / Bonus / Scrip are security only, total cost unchanged, new total quantity ([split.md](split.md)). Those types are not accepted here.

Average cost is the rule the projection uses. This increment only adds cost (Opening). Sell release is specified so the next feature does not add a second cost column: cost out = average × quantity, server-computed.

What changes a holding. The first column is the type of the Transaction; a person enters it where the manual entry matrix allows (custody-modes §4.2), and the platform writes it on a Custodial account:

| Type | On a Custodial account written by | Effect on the holding | Rules |
| --- | --- | --- | --- |
| `OpeningHoldings` | A migration batch | Adds quantity and the cost given | R8–R11; [migration-batches.md](migration-batches.md) |
| `Buy` | An order fill | Adds quantity; adds `gross + fee` to the cost | [buy-sell.md](buy-sell.md), [trade-fees.md](trade-fees.md) R16, [orders.md](orders.md) |
| `Sell`, `StockOut` | An order fill; an in-kind transfer out | Lowers quantity; releases average cost. A fee does not change the release | [buy-sell.md](buy-sell.md), [in-kind-transfers.md](in-kind-transfers.md) |
| `StockIn` | An in-kind transfer in | Adds quantity and the cost given | [buy-sell.md](buy-sell.md), [in-kind-transfers.md](in-kind-transfers.md) |
| `Split`, `Bonus`, `Scrip` | An entitlement posting (`Split` and `Bonus` only): `quantityAfter = RoundDown(quantityBefore × factor, 8)` | Changes quantity; total cost unchanged | [split.md](split.md), [corporate-actions.md](corporate-actions.md) R25–R29 |
| `Adjustment` | The approval of an adjustment request. Custodial accounts only | The signed `quantityChange` and `costChange` as entered (no price, no average-cost release). After it quantity `≥ 0`, cost `≥ 0`, and a zero quantity has zero cost. A disabled instrument is allowed only when the Account already holds it | [adjustments.md](adjustments.md) R10–R13 |
| `Dividend` with a reinvestment | Corporate actions | Adds the reinvested quantity, and the reinvested amount as cost, like a `Buy` with no fee. A holding sold since the ex-date is created again | [dividends.md](dividends.md) R40 |

A `Dividend` with no reinvestment, and every cash type, does not touch a holding.

Buy / Sell, when that feature opens, must not trust client price, cost, or cash amount. Server calls `IMarketData.TryGetPrice(instrumentId, bookedAt)`. Missing price → 400. Cross currency uses `IFxRate`; missing pair → 400, never 1. Price is not stored on the leg. That HTTP is not this increment.

---

## 6. Database

Script: `database/schema/0014_holdings.sql`. Do not back-fill. Do not add EF migrations.

| Table | Change | Index / FK |
| --- | --- | --- |
| `TransactionSecurityLegs` | add | PK `Id`. FK `TransactionId` → `Transactions` RESTRICT. `AccountId` (script `0024`) and `TenantId` (script `0027`) on every leg. Unique `(TransactionId, AccountId, InstrumentId)`. FK instrument and tenant consistent with the header. Quantity `decimal(18,8)`. Cost `decimal(18,4)` + `char(3)` quote currency. Audit + `RowVersion`. |
| `Holdings` | add | PK `Id`. Unique `PublicId`. Unique `(AccountId, InstrumentId)`. Quantity `decimal(18,8)`. Cost + cost currency. Audit + `RowVersion`. |

No quantity or cost column on `Accounts`. No free-text symbol. No price column. Leg `Id` and leg `RowVersion` are not client tokens. HTTP addresses Transaction `PublicId` and Holding `PublicId`.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreateTransaction` | new Transaction PublicId | R1–R14, R17–R19 for `OpeningHoldings` |
| Command | `ReverseTransaction` | new Transaction PublicId | R15, R5, R17 |
| Command | `CloseAccount` (existing) | 204 | add R16 |
| Query | `GetAccountHoldings` | list of positions | `accounts.read`, R1, R20 |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| POST | `/transactions` | `transactions.create` | 201 `{ "id" }` | 400 / 401 / 403 / 404 / 409 |
| POST | `/transactions/{id}/reverse` | `transactions.create` | 201 `{ "id" }` | 400 / 401 / 403 / 404 / 409 |
| GET | `/accounts/{id}/holdings` | `accounts.read` | 200 `{ "items": [ ... ] }` | 401 / 403 / 404 |

Create and reverse require `Idempotency-Key`.

### 8.1 Bodies

```json
{
  "accountId": "<account publicId>",
  "type": "OpeningHoldings",
  "bookedAt": "2026-09-27T09:00:00+12:00",
  "memo": null,
  "reference": null,
  "security": [
    {
      "instrumentId": "<instrument publicId>",
      "quantity": 100,
      "cost": 1500.00
    }
  ]
}
```

SystemAdmin create adds `tenantId`. TenantAdmin / Adviser omit it.

Holdings item:

```json
{
  "id": "<holding publicId>",
  "accountId": "<account publicId>",
  "instrumentId": "<instrument publicId>",
  "symbol": "VTI",
  "name": "Vanguard Total Stock Market ETF",
  "quantity": 100,
  "cost": 1500.00,
  "costCurrency": "USD"
}
```

Transaction item shape is owned by [leg-accounts.md](leg-accounts.md) §8.1: `cash` and `security` arrays, each leg with its own `accountId` / `customerId`. `OpeningHoldings` returns `cash: []`.

The holdings item also carries four computed quantities, none stored on `Holdings`:

| Field | Is | On an External account |
| --- | --- | --- |
| `reservedQuantity` | The open quantity of the Account's live sell orders in the Instrument plus their fills not posted yet, plus the quantity of its open in-kind transfers `Out` | `0` |
| `availableQuantity` | Quantity minus reserved quantity | The quantity |
| `unsettledQuantity` | The quantity of the Account's `Posted` buy fills in the Instrument that are not settled and not broken | `0` |
| `transferableQuantity` | Available quantity minus unsettled quantity, never below 0 | The quantity |

Field rules: orders R40, R41; in-kind-transfers R43, R44.

### 8.2 Errors

Ordinary validation stays in `errors`: Bank/Cash, disabled instrument, second Opening for the same instrument, cash field present, quantity not `> 0`, cost `< 0`, close while holdings remain. Disabled Customer uses `code=disabled` / `target=user`. Cross-tenant or wrong-collection id → 404.

---

## 9. UI

None. Pages live in [docs/adviser-portal/](../adviser-portal/). Scalar is enough.

---

## 10. Tests

The tests are the executable form of §4. Where they live:

| Project | Where |
| --- | --- |
| Domain.UnitTests | `Entities/OpeningHoldingsTests.cs` |
| Application.FunctionalTests | `Transactions/OpeningHoldingsTests.cs`, `Transactions/OpeningHoldingsProjectionTests.cs` |
| Infrastructure.IntegrationTests | `AppliedSchema/HoldingsSchemaTests.cs` |

TestSeed (Development / TestAppHost only): one `OpeningHoldings` on Demo brokerage for an existing TestSeed instrument. Not a Buy. Functional tests create their own rows.

---

## 11. Locked

| Item | Lock |
| --- | --- |
| Increment | A1. Opening only. Buy/Sell/StockIn/StockOut accepted in [buy-sell.md](buy-sell.md). Split / Bonus / Scrip accepted in [split.md](split.md) |
| Write path | Transaction + legs. Holdings is a projection |
| Legs | Several per header. Unique `(TransactionId, InstrumentId)` |
| Opening | Type `OpeningHoldings`. Many per account. One per `(Account, Instrument)`. No cash leg. Quantity `> 0`. Cost `≥ 0`. First security activity for that instrument |
| Scale | Quantity `decimal(18,8)`. Cost `decimal(18,4)` |
| Shorts | Forbidden |
| Reverse | Allowed, including Opening. Opposite legs |
| Close | Cash `SUM = 0` and no holdings rows. No CloseOut-of-stock |
| Read | `GET /accounts/{id}/holdings`, `accounts.read`, `PublicId` |
| Buy/Sell price | Accepted in [buy-sell.md](buy-sell.md). Server `IMarketData` only |
| Dividend | Ordinary cash. No instrument. Not this file |
| Stock in/out | Accepted in [buy-sell.md](buy-sell.md). What a `StockIn` or `StockOut` means for tax (an arrival, a departure or half of a move) is [tax-year-events.md](tax-year-events.md) R14, R15 |

Still out of this increment: portal pages, net worth, live FX, lots.

---

## 12. History

What changed in this file after it was accepted. The text of each change is in git (`git log -p -- docs/features/holdings.md`).

| Date | With | Change here |
| --- | --- | --- |
| 2026-11-02 | [customer-portal.md](../customer-portal/customer-portal.md) | R19: a Customer reads the holdings of their own accounts |
| 2026-11-10 | [leg-accounts.md](leg-accounts.md) | R6, R14, §6: the Account is on each security leg |
| 2026-11-14 | [utc-instants](../fixes/utc-instants.md) | R18 follows posting R24 |
| 2026-11-15 | [trade-fees.md](trade-fees.md) | §5: a `Buy` adds the fee to the cost |
| 2026-11-16 | [ledger-write-guards](../fixes/ledger-write-guards.md) | R15: only the latest security activity is reversed |
| 2026-11-17 | [custody-modes.md](custody-modes.md) | R8: a hand `OpeningHoldings` is refused on a Custodial account |
| 2026-11-19 | [adjustments.md](adjustments.md) | §5: an `Adjustment` changes one holding |
| 2026-11-22 | [corporate-actions.md](corporate-actions.md) | §5, R15: entitlement postings |
| 2026-11-23 | [orders.md](orders.md) | §8.1: reserved and available quantity. R13: available quantity. §5: fills |
| 2026-11-24 | [migration-batches.md](migration-batches.md), [in-kind-transfers.md](in-kind-transfers.md), [dividends.md](dividends.md) | R8: migrated holdings. §8.1: unsettled and transferable quantity. §5: in-kind transfers and reinvested dividends |
| 2026-11-26 | — | The changes above were folded into the body. No rule changed |
| 2026-11-26 | [account-valuation.md](account-valuation.md) | §2 Out: snapshots. R14: the quantity of a past day is read from the security legs |
| 2026-11-28 | [tax-year-events.md](tax-year-events.md) | R14: the quantity of a tax holding at an instant. §11: stock in/out for tax points to the tax year read |
