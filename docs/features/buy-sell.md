---
title: Buy / Sell / Stock in-out
status: accepted
phase: 2
language: en
owner: ""
created: 2026-10-11
last_updated: 2026-10-11
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
  - holdings.md
---

# Buy / Sell / Stock in-out

Cash + security legs on the existing Transaction aggregate. Public write stays `POST /transactions`. No new table (tables from `0014_holdings.sql`). `0016_transaction_type_stock_in_out.sql` widens the type check so StockIn and StockOut can be stored. Server prices Buy / Sell via the existing mocked `IMarketData` / `IFxRate`. Stock in/out has no cash leg. Amends the type tables in [posting.md](posting.md) and [holdings.md](holdings.md), and the transaction item shape.

Split / scrip / bonus stay out (own spec after this slice). No portal page. No price on the holdings read.

**Status is `accepted`.** Implementation follows this file. Do not implement from ADR 0011 alone.

---

## 1. Summary

A TenantAdmin or Adviser records a `Buy`, `Sell`, `StockIn`, or `StockOut` on an **Open** Brokerage or Other account of an assigned / same-tenant Customer. SystemAdmin does the same on Scalar for a chosen tenant. Customer receives 403. Bank and Cash refuse every security leg.

The header is the existing Transaction. Each of these types has exactly one security leg. `Buy` / `Sell` also have exactly one cash leg (server-priced). `StockIn` / `StockOut` have no cash leg. The `Holdings` projection is upserted in the same Account-locked command. Cost stays average cost in the instrument quote currency.

---

## 2. Scope

**In**

- Domain: types `Buy`, `Sell`, `StockIn`, `StockOut` on the existing `Transaction` aggregate. Exactly one security leg. `Buy` / `Sell` also have exactly one cash leg. Average-cost update on the existing `Holding` projection.
- Application: extend `CreateTransaction` / `ReverseTransaction` for the four types. Reuse mocked `IMarketData.TryGetPrice` and `IFxRate` for `Buy` / `Sell` only.
- No new table or column. Tables and columns from [holdings.md](holdings.md) (`0014_holdings.sql`). Script `0016_transaction_type_stock_in_out.sql` only widens `CK_Transactions_Type` to 0–11.
- No new policy. Writes reuse `transactions.create`. Holdings read stays `accounts.read`.
- Isolation tests + Development / TestAppHost `TestSeed` (one `Buy` and one `StockIn` on an existing demo brokerage).
- Amend the type tables in [posting.md](posting.md) and [holdings.md](holdings.md) in the same accept change.

**Out**

- Split / scrip / bonus (security-only leg, cash omitted or 0, total cost unchanged). Same tables later.
- Dividend (ordinary cash type; no instrument).
- Adviser Portal pages.
- Price or market value on `GET /accounts/{id}/holdings`. Net worth, snapshots, Dashboard.
- Live vendor market data / live FX (ports stay mocked).
- Fees, lots, wash sales, realized-gain reports, short positions.
- Multi-instrument Buy/Sell (one security leg per header this increment).
- Cross-account transfer.
- Property / Credit selectable.
- Hand-edited holding quantity or cost.
- A second POST collection. `UPDATE` / `DELETE` of a posted transaction.
- Customer ledger-write.

---

## 3. Stories

1. As an Adviser I record a `Buy` of an instrument on a Brokerage account so quantity and cash both move without typing a price.
2. As an Adviser I record a `Sell` and the cash increases while the holding quantity falls at average cost.
3. As an Adviser I cannot Buy or Sell on Bank or Cash (400).
4. As an Adviser I cannot pick a disabled instrument (400). Missing mock price → 400. Missing FX pair → 400.
5. As an Adviser I cannot Sell more than the current holding quantity (400). After the command, cash `SUM ≥ 0` and holding quantity `≥ 0`.
6. As an Adviser I reverse a Buy or Sell. The original row stays. The reverse restores cash and quantity.
7. As an Adviser I record a `StockIn` (quantity + cost, no cash) so shares arrive from outside without a buy.
8. As an Adviser I record a `StockOut` so shares leave at average cost without a sell.
9. As a Customer I receive 403.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Policy fail → 403. TenantAdmin / Adviser: other-tenant, unassigned Customer, missing id → 404. SystemAdmin: missing id → 404; another tenant’s id is allowed. |
| R2 | Path and JSON `id` are `PublicId`. Internal ints never appear. Create / reverse return `201 { "id" }` of the Transaction. |
| R3 | Writes stay `POST /transactions` and `POST /transactions/{id}/reverse`. Not under `/users`. |
| R4 | TenantAdmin / Adviser omit `tenantId` (400 if present). SystemAdmin create requires `tenantId`, same as posting and holdings. |
| R5 | Account must be Open, same tenant, and visible to the caller. Closed → 400. Disabled Customer on create / reverse → 400 `disabled` / `target=user`. |
| R6 | Account type must be Brokerage or Other. Bank / Cash → 400. |
| R7 | Instrument must exist, same tenant, and `IsActive`. Disabled or missing → 400. |
| R8 | Type on create in this increment: `Buy`, `Sell`, `StockIn`, `StockOut` (plus the already-accepted cash set and `OpeningHoldings`). Split / scrip / bonus / unknown → 400. |
| R9 | Body `security` is an array of exactly one leg. `Buy` / `Sell` leg fields: `instrumentId`, `quantity` `> 0`. No `cost`, no `price`, no `amount`, no `cash` on that create body → 400 if present. `StockIn` also requires `cost` `≥ 0` (client-supplied, quote currency). `StockOut` is `instrumentId` + `quantity` `> 0` only. |
| R10 | `Buy` / `Sell` only: server calls `IMarketData.TryGetPrice(instrumentId, bookedAt)`. Missing or empty price → 400. Price is not stored on the leg. Ports stay mocked. |
| R11 | `Buy` / `Sell` cost on the security leg is `quantity × price` in the instrument quote currency. Body must not send a currency. `StockIn` cost is the client amount, same quote currency. |
| R12 | `Buy` / `Sell` cash-leg currency is `Account.Currency`. If the quote currency differs, server calls `IFxRate`. Missing pair → 400, never treat as 1. Same-currency FX is 1. Cash amount is server-computed: `Buy` decreases cash (`amount < 0`); `Sell` increases cash (`amount > 0`). `StockIn` / `StockOut` have no cash leg; a cash / amount field on those bodies → 400. |
| R13 | Security-leg quantity is stored signed: `Buy` / `StockIn` `+quantity`; `Sell` / `StockOut` `−quantity`. After insert, quantity `SUM` per `(Account, Instrument)` `≥ 0`. Cash `SUM ≥ 0` on non-Credit accounts. |
| R14 | `Sell` / `StockOut` quantity must not exceed the current holding quantity for that instrument (400). No holding → 400. `Buy` / `StockIn` may be the first security activity for that instrument (creates the holding). |
| R15 | Projection upsert in the same Account-locked command. Callers never PUT a holding. `Buy` / `StockIn` add cost. `Sell` / `StockOut` release cost: `released = Round(oldCost × sellQty / oldQty, 4, MidpointRounding.AwayFromZero)`; remaining = oldCost − released. Selling the full quantity leaves remaining 0. Quantity 0 deletes the projection row. Tests assert projection quantity = `SUM` of security-leg quantities and projection cost matches this rule. |
| R16 | Posted rows are not updated or deleted. Reverse copies the opposite cash amount (if any) and the opposite security quantity and cost. One reversal per original. No client `rowVersion`. Account Open. If the reverse would make cash or quantity `< 0` → 400; original stays. |
| R17 | `Idempotency-Key` required on create and reverse, same as posting and holdings. |
| R18 | `bookedAt` required `datetimeoffset`. Memo optional 1–200. Reference optional, stored, no uniqueness. |
| R19 | Customer has no ledger-write policy → 403. |
| R20 | Close still requires cash `SUM = 0` and no `Holdings` row (already locked in holdings R16). Buy / Sell do not change that guard. |

Policies: no new names. `transactions.create` for the four types and reverse.

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `Transaction` | Aggregate (existing) | `Buy` / `Sell`: one cash leg + one security leg. `StockIn` / `StockOut`: one security leg, no cash. Still one write. |
| `TransactionCashLeg` | Entity (existing) | One per `Buy` / `Sell` header. Amount server-computed. Currency = account currency. Absent on stock in/out. |
| `TransactionSecurityLeg` | Entity (existing) | Exactly one instrument. Quantity signed. Cost in quote currency. |
| `Holding` | Projection (existing) | Per `(Account, Instrument)`. Average cost. Quantity 0 deletes the row. |
| `TransactionType` | Enum | Accept `Buy`, `Sell`, `StockIn`, `StockOut`. |
| `IMarketData` / `IFxRate` | Ports (existing) | Mock only. Used by `Buy` / `Sell` only. |

Invariants:

- `Buy` / `Sell`: client does not supply price, cost, or cash amount. Server prices.
- `StockIn`: client supplies cost `≥ 0` in the quote currency. No price call. No cash.
- `StockOut`: no client cost. Release at average. No cash.
- Price is not persisted on the leg.
- Average cost is the only cost rule. Release uses the rounding in R15.
- Insert lands Posted. No draft.

Domain events: existing `TransactionPosted` / `TransactionReversed`. Do not add a pricing event.

Update [domain-model.md](../domain-model.md) in the same accept change.

---

## 6. Database

No new table and no new column. Tables stay those in [holdings.md](holdings.md) (`0014_holdings.sql`). Do not add EF migrations. Price is not stored. Cash amount and security cost are written by the application from the server price.

`Buy` (7) and `Sell` (8) already fit `CK_Transactions_Type`. `StockIn` (10) and `StockOut` (11) do not. Script `database/schema/0016_transaction_type_stock_in_out.sql` drops and re-adds that check so `Type` is 0–11.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreateTransaction` (extend) | Transaction `PublicId` | Type in the four; account Open + Brokerage/Other; instrument active; `Buy`/`Sell` price + FX present; quantity `> 0`; `Sell`/`StockOut` ≤ holding; `StockIn` cost `≥ 0`; cash `SUM ≥ 0` when a cash leg exists; projection upsert |
| Command | `ReverseTransaction` (extend) | new Transaction `PublicId` | Opposite legs; result `≥ 0`; one reverse |
| Query | `GetAccountHoldings` (existing) | items | Unchanged. Still no market value. |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| POST | `/transactions` | `transactions.create` | 201 `{ "id" }` | 400 / 401 / 403 / 404 / 409 |
| POST | `/transactions/{id}/reverse` | `transactions.create` | 201 `{ "id" }` | 400 / 401 / 403 / 404 / 409 |

Create and reverse require `Idempotency-Key`.

### 8.1 Bodies

`Buy` / `Sell` create (no client price, cost, or cash):

```json
{
  "accountId": "<account publicId>",
  "type": "Buy",
  "bookedAt": "2026-10-11T10:00:00+13:00",
  "memo": null,
  "reference": null,
  "security": [
    {
      "instrumentId": "<instrument publicId>",
      "quantity": 50
    }
  ]
}
```

`StockIn` create (client cost, no cash, no price):

```json
{
  "accountId": "<account publicId>",
  "type": "StockIn",
  "bookedAt": "2026-10-11T10:00:00+13:00",
  "memo": null,
  "reference": null,
  "security": [
    {
      "instrumentId": "<instrument publicId>",
      "quantity": 20,
      "cost": 300.00
    }
  ]
}
```

`StockOut` is `instrumentId` + `quantity` only (same as Sell, no cash). SystemAdmin create adds `tenantId`. TenantAdmin / Adviser omit it.

Transaction **item** always uses objects (Q2 = B). Pure-cash items move off flat `amount` / `currency` onto `cash`. Amend [posting.md](posting.md) in the same accept change. Portal does not consume this shape yet.

`Buy` item:

```json
{
  "id": "<transaction publicId>",
  "accountId": "<account publicId>",
  "type": "Buy",
  "bookedAt": "2026-10-11T10:00:00+13:00",
  "cash": { "amount": -1250.00, "currency": "NZD" },
  "security": [
    {
      "instrumentId": "<instrument publicId>",
      "quantity": 50,
      "cost": 750.00,
      "costCurrency": "USD"
    }
  ]
}
```

Pure-cash item: `cash` present, `security` omitted. `OpeningHoldings` / `StockIn` / `StockOut`: `security` present, `cash` omitted. Do not keep a parallel flat `amount` on the item.

### 8.2 Errors

Ordinary validation stays in `errors`: Bank/Cash, disabled instrument, missing price, missing FX pair, quantity not `> 0`, Sell/StockOut over quantity, cash would go negative, cost/price/cash field present on a Buy/Sell body, cash field on StockIn/StockOut, StockIn cost `< 0`. Disabled Customer uses `code=disabled` / `target=user`. Cross-tenant or wrong-collection id → 404. Idempotency mismatch → 409.

---

## 9. UI

None. Pages live in [docs/portals/](../portals/). Scalar is enough.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | Signs; server cost on Buy; StockIn client cost; average-cost release rounding (R15); Bank/Cash refuse; cannot mutate posted header; result quantity and cash `≥ 0` |
| Application.FunctionalTests | No Bearer → 401 not 302; Customer → 403; happy Buy then Sell; StockIn then StockOut; missing price → 400; missing FX → 400; Sell/StockOut over quantity → 400; Buy that would make cash negative → 400; reverse restores; reverse that would go negative → 400; idempotent replay; hash mismatch 409; disabled instrument → 400 |
| Infrastructure.IntegrationTests | Two-tenant fixture: other tenant’s account or instrument → 404. TestSeed idempotent. Mock price used; same-currency FX = 1; missing cross pair is not 1. |

TestSeed (Development / TestAppHost only): one `Buy` and one `StockIn` on the existing demo brokerage for an existing TestSeed instrument that has a mock price. Not an Opening. Functional tests create their own rows.

---

## 11. Locked in this spec

No new ADR. Decisions 2026-10-11.

| Item | Lock |
| --- | --- |
| Scripts / policy | No new table or policy. Reuse `transactions.create`. Tables from holdings (`0014_holdings.sql`). `0016_transaction_type_stock_in_out.sql` widens `CK_Transactions_Type` to 0–11 so `StockIn` (10) and `StockOut` (11) can be stored. |
| Caller | TenantAdmin; Adviser (assigned); SystemAdmin (Scalar, `tenantId` on create). Customer 403. |
| Types | `Buy`, `Sell`, `StockIn`, `StockOut`. Split / scrip / bonus stay out. |
| Legs | Exactly one security leg. `Buy` / `Sell`: exactly one cash leg. `StockIn` / `StockOut`: no cash leg. |
| Price | `Buy` / `Sell` only. `IMarketData.TryGetPrice(instrumentId, bookedAt)`. Mocked. Missing → 400. Not stored. |
| FX | `Buy` / `Sell` only, when quote ≠ account currency. Missing pair → 400, never 1. |
| StockIn cost | Client-supplied, `≥ 0`, quote currency. No price call. |
| StockOut / Sell release | `released = Round(oldCost × qty / oldQty, 4, MidpointRounding.AwayFromZero)`. Remaining = old − released. Full quantity → remaining 0. |
| First activity | `Buy` / `StockIn` may create the holding. `Sell` / `StockOut` require an existing holding. |
| Item shape | Always optional `cash: { amount, currency }` + optional `security: [...]`. No flat `amount` on the item. Amend posting.md. |
| Reverse | Opposite legs. If result cash or quantity would be `< 0` → 400. Original stays. |
| Close | Unchanged from holdings R16. |
| Portal | None this slice. |

Open questions: none that block review.

---

## 12. Out / later

- **Split / scrip / bonus.** Not this slice. Own Feature Spec after Buy / Sell / StockIn / StockOut is accepted. Same tables, same `POST /transactions`, security-only leg, **total cost unchanged** (average is diluted, cost is not added or released). Do not model them as `StockIn` / `StockOut`. Reserved shape: [ADR 0011](../adr/0011-ledger-cash-holdings-reversal.md), [holdings.md](holdings.md).
- Price or market value on `GET /accounts/{id}/holdings`. Net worth. Dashboard. Daily snapshots.
- Fees, lots, wash sales, realized-gain reports, short positions.
- Live vendor market data / live FX (ports stay mocked).
- Multi-instrument Buy/Sell.
- Cross-account transfer. Property / Credit. Customer ledger-write.
- Client price override on Buy/Sell.

---

## 13. Suggested commits

The repository should build after each commit. Agents split each line into red then green.

1. Domain: four types + average-cost release (R15) + unit tests.
2. Application: extend `CreateTransaction` / `ReverseTransaction`; wire mocked `IMarketData` / `IFxRate` for Buy/Sell; functional tests (Buy, Sell, StockIn, StockOut, missing price, oversell, cash negative, reverse rejected).
3. API: bodies; item shape `cash` + `security`; pure-cash item moves to `cash`; isolation tests.
4. TestSeed one `Buy` and one `StockIn`. Amend posting and holdings type tables and the transaction item shape in the same change that accepts this file.
