---
title: Holdings
status: accepted
phase: 2
language: en
owner: ""
created: 2026-09-28
last_updated: 2026-10-05
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
---

# Holdings

Security legs, holdings projection, and Opening of holdings on `webapi`. Public write stays `POST /transactions`. Adds script `0014_holdings.sql`. Amends [posting.md](posting.md) with type `OpeningHoldings` and amends [accounts.md](accounts.md) R16 so close also requires a flat holdings book.

Buy, Sell, split / scrip / bonus, and no-cash stock in/out are the next feature on these same tables. Dividend is an ordinary cash type and is not this file. No portal page.

**Status is `accepted`.** Implementation follows this file. Do not implement from ADR 0011 alone.

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

- Buy / Sell / split / scrip / bonus HTTP. Same tables later. Those types stay 400 here
- No-cash stock in/out. Buy/Sell slice, not tax
- Dividend HTTP. Ordinary cash, no instrument id. Not this file
- DRIP and corporate-action dividend
- Adviser Portal pages
- Net worth, snapshots, Dashboard, price on the holdings read
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
8. As a Customer I receive 403.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Policy fail → 403. TenantAdmin / Adviser: other-tenant, unassigned Customer, missing id → 404. SystemAdmin: missing id → 404; another tenant’s id is allowed. |
| R2 | Path and JSON `id` are `PublicId`. Internal ints never appear. Create / reverse return `201 { "id" }` of the Transaction. |
| R3 | Writes stay `POST /transactions` and `POST /transactions/{id}/reverse`. Holdings read is `GET /accounts/{id}/holdings`. Not under `/users`. |
| R4 | TenantAdmin / Adviser omit `tenantId` (400 if present). SystemAdmin create requires `tenantId`, same as posting. Holdings read uses the account PublicId only. |
| R5 | Account must be Open, same tenant, and visible to the caller. Closed → 400. Disabled Customer on create / reverse → 400 `disabled` / `target=user`. |
| R6 | Account type must be Brokerage or Other. Bank or Cash → 400. Property and Credit are not selectable. |
| R7 | Each security leg stores `InstrumentId` only. Instrument must exist in the same tenant. Disabled instrument on a new leg → 400. Reverse of an existing leg is allowed. Disable instrument is still allowed while holdings exist. |
| R8 | Type on create in this increment: `OpeningHoldings` only. Buy / Sell / Dividend / split / scrip / bonus / unknown → 400. Cash `Opening` rules in posting R19 are unchanged. |
| R9 | `security` is an array of 1..n legs. Each leg: `instrumentId`, `quantity` `> 0`, `cost` `≥ 0`. Duplicate instrument in the array → 400. No `amount` and no `cash` on this type → 400 if present. |
| R10 | Cost currency is the instrument quote currency. Body must not send a currency. A later-disabled catalog currency does not rewrite stored cost currency. |
| R11 | At most one `OpeningHoldings` per `(Account, Instrument)`, including a reversed one until the product says otherwise: a second header that names an instrument already opened → 400. `OpeningHoldings` must be the first security activity for that instrument. |
| R12 | No auto zero row. Create Account does not insert a holding. |
| R13 | After insert, quantity `SUM` per `(Account, Instrument)` `≥ 0`. Stored quantity is `decimal(18,8)`. Cost amount is `decimal(18,4)`. |
| R14 | Projection upsert in the same Account-locked command. Callers never PUT a holding. Quantity 0 deletes the projection row. Tests assert projection quantity = `SUM` of security-leg quantities. |
| R15 | Posted rows are not updated or deleted. Reverse copies the opposite security quantity and cost. One reversal per original. No client `rowVersion`. Account Open. Result must stay `≥ 0`. |
| R16 | `CloseAccount` rejects unless cash `SUM = 0` and no `Holdings` row remains for that account. Amends accounts R16. `Account.Close()` writes neither cash nor holdings. |
| R17 | `Idempotency-Key` required on create and reverse, same as posting. |
| R18 | `bookedAt` required `datetimeoffset`. Memo optional 1–200. Reference optional, stored, no uniqueness. |
| R19 | Customer has no ledger-write policy → 403. Holdings read is `accounts.read`, so a caller who can see the account can see its positions. No `holdings.create`. |
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

`OpeningHoldings` has security legs only. Buy / Sell later add a cash leg on this same header. Split later is security only, total cost unchanged. Those types are not accepted here.

Average cost is the rule the projection uses. This increment only adds cost (Opening). Sell release is specified so the next feature does not add a second cost column: cost out = average × quantity, server-computed.

Buy / Sell, when that feature opens, must not trust client price, cost, or cash amount. Server calls `IMarketData.TryGetPrice(instrumentId, bookedAt)`. Missing price → 400. Cross currency uses `IFxRate`; missing pair → 400, never 1. Price is not stored on the leg. That HTTP is not this increment.

---

## 6. Database

Script: `database/schema/0014_holdings.sql`. Do not back-fill. Do not add EF migrations.

| Table | Change | Index / FK |
| --- | --- | --- |
| `TransactionSecurityLegs` | add | PK `Id`. FK `TransactionId` → `Transactions` RESTRICT. Unique `(TransactionId, InstrumentId)`. FK instrument and tenant consistent with the header. Quantity `decimal(18,8)`. Cost `decimal(18,4)` + `char(3)` quote currency. Audit + `RowVersion`. |
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

Transaction item gains a `security` array when a security leg exists. Landed cash items keep flat `amount` / `currency`. A later Buy sends `"cash": { "amount", "currency?" }` plus `security`. Do not change landed cash JSON in this increment.

### 8.2 Errors

Ordinary validation stays in `errors`: Bank/Cash, disabled instrument, second Opening for the same instrument, cash field present, quantity not `> 0`, cost `< 0`, close while holdings remain. Disabled Customer uses `code=disabled` / `target=user`. Cross-tenant or wrong-collection id → 404.

---

## 9. UI

None. Pages live in [docs/portals/](../portals/). Scalar is enough.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | Quantity sign; Bank/Cash refuse security; cost `≥ 0`; cannot mutate posted header |
| Application.FunctionalTests | 401; Customer 403; OpeningHoldings happy path; second instrument allowed; same instrument 400; disabled instrument 400; Bank 400; cash field 400; reverse; close rejected while quantity remains; holdings read includes `id` |
| Infrastructure.IntegrationTests | Other tenant 404; Adviser unassigned 404; no quantity column on `Accounts`; no free-text symbol; `PublicId` on `Holdings` |

TestSeed (Development / TestAppHost only): one `OpeningHoldings` on Demo brokerage for an existing TestSeed instrument. Not a Buy. Functional tests create their own rows.

---

## 11. Locked

| Item | Lock |
| --- | --- |
| Increment | A1. Opening only. Buy/Sell/split next, same tables |
| Write path | Transaction + legs. Holdings is a projection |
| Legs | Several per header. Unique `(TransactionId, InstrumentId)` |
| Opening | Type `OpeningHoldings`. Many per account. One per `(Account, Instrument)`. No cash leg. Quantity `> 0`. Cost `≥ 0`. First security activity for that instrument |
| Scale | Quantity `decimal(18,8)`. Cost `decimal(18,4)` |
| Shorts | Forbidden |
| Reverse | Allowed, including Opening. Opposite legs |
| Close | Cash `SUM = 0` and no holdings rows. No CloseOut-of-stock |
| Read | `GET /accounts/{id}/holdings`, `accounts.read`, `PublicId` |
| Buy/Sell price | Next feature. Server `IMarketData` only. Not this HTTP |
| Dividend | Ordinary cash. No instrument. Not this file |
| Stock in/out | Buy/Sell slice. Not tax |

Still out of this increment: portal pages, net worth, live FX, lots.

---

## 12. Suggested commits

Only after this file is `accepted`. Tree must build after each commit.

1. `0014_holdings.sql` + EF mapping + integration test that security legs and `Holdings` exist and `Accounts` has no quantity column.
2. Domain security leg + `OpeningHoldings` guards + unit tests.
3. `CreateTransaction` for `OpeningHoldings` + policies reused + functional tests.
4. Reverse of `OpeningHoldings` + projection upsert / delete-at-zero.
5. Close-flat guard + `GET /accounts/{id}/holdings` + TestSeed.
