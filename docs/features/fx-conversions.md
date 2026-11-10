---
title: FX conversions
status: draft
phase: 2
language: en
owner: ""
created: 2026-11-10
last_updated: 2026-11-10
related:
  - internal-transfers.md
  - leg-accounts.md
  - posting.md
  - fx-rates.md
  - buy-sell.md
  - portfolios.md
  - cross-currency-trades.md
  - reports.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../customer-portal/customer-portal.md
  - ../adr/0004-money-as-decimal-with-currency.md
  - ../adr/0011-ledger-cash-holdings-reversal.md
  - ../adr/0015-idempotency-keys.md
  - ../adr/0019-fx-rate-source-and-refresh.md
  - ../future-plans.md
  - ../glossary.md
---

# FX conversions

Cash conversion from one currency to another between two Accounts in the same tenant, on `webapi`. One Transaction of the new type `FxConversion` has two cash legs: a negative leg in the sell currency on the from Account and a positive leg in the buy currency on the to Account. The server prices the conversion from the cached reference rate (`IFxRate`, [fx-rates.md](fx-rates.md)) and stores the rate it used in a new table `TransactionFxDetails`. The two Accounts may belong to the same Customer or to two Customers of the same tenant, so this slice is also the cross-currency transfer that [internal-transfers.md](internal-transfers.md) left out.

This slice adds one `TransactionType`, a create route `POST /transactions/fx`, a read-only quote route `POST /transactions/fx/quote`, and script `0026`. No new policy. Read and reverse reuse the existing `/transactions` routes. An Account still has one cash currency (ADR 0011 §6); this slice does not change the Account model ([future-plans.md](../future-plans.md) §4).

`TransactionFxDetails` is designed so that Buy / Sell with automatic FX can store their rate too. That use is a separate amendment to [buy-sell.md](buy-sell.md) and [portfolios.md](portfolios.md): [cross-currency-trades.md](cross-currency-trades.md). This slice does not change Buy / Sell.

---

## 1. Summary

A TenantAdmin or Adviser converts cash from an Open Account in one currency to an Open Account in another currency, in one tenant, in one command. The caller names either the amount to sell or the amount to buy; the server computes the other side from the stored reference rate. The rule that distinguishes this slice: the rate is server-side, the caller confirms it by sending back the quoted rate, and the rate used is stored with the Transaction.

---

## 2. Scope

**In**

- Domain: `TransactionType.FxConversion = 16`; factory that builds two cash legs in two currencies and an FX detail; value object for the applied rate
- Application: `CreateFxConversion` command; `GetFxConversionQuote` query; existing reverse, list, get, cash balance, net worth, reports and portfolio rules applied to the new type; reverse copies the FX detail
- Script `database/schema/0026_fx_conversions.sql`: widens `CK_Transactions_Type` to 0–16 and adds `TransactionFxDetails`
- HTTP: `POST /transactions/fx`, `POST /transactions/fx/quote`; `POST /transactions` rejects `FxConversion`; optional `fx` object on the transaction item
- Adviser Portal: Convert form with live quote (§9). Customer Portal label
- Tests listed in §10

**Out**

- Changing Buy / Sell automatic FX, the manual same-currency rule, or storing the rate on Buy / Sell (next amendment; [future-plans.md](../future-plans.md) §2)
- Multi-currency cash in one Account ([future-plans.md](../future-plans.md) §4)
- Fee or spread on the rate
- A rate entered by the caller
- A stored, time-limited quote (rate lock)
- Cross-tenant conversion
- Customer-initiated conversion (Customer has no ledger write)
- An HTTP read route for `FxRates` (fx-rates R14 stays deferred)
- FX columns in the Customer Transactions report
- Moving existing types to their own create routes ([future-plans.md](../future-plans.md) §3)

---

## 3. Stories

1. As an Adviser I convert NZD from my Customer's NZD Account into their USD Account so they can buy a US instrument.
2. As an Adviser I say "sell 1,000.00 NZD" or "buy 500.00 USD", and the server computes the other amount.
3. As an Adviser I see the rate, its as-of date and both amounts before I confirm, and the conversion uses exactly that rate.
4. As an Adviser I convert from an Account of one assigned Customer into an Account of another assigned Customer in another currency (cross-currency transfer).
5. As an Adviser I reverse a conversion, and both Accounts get the opposite leg at the original rate.
6. As an Adviser I cannot convert when the rate is missing or too old.
7. As a Customer I see a conversion that touches one of my Accounts, with the amount on my Account and the rate.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Unchanged from posting R1–R4a and leg-accounts R1: 401 / 403 / 404, PublicId only, `tenantId` rules for TenantAdmin / Adviser (omit) and SystemAdmin (required on create and quote). Customer → 403. Disabled tenant on create → 400 `disabled` / `target=tenant`. |
| R2 | **Route.** `POST /transactions/fx` creates an `FxConversion`. `POST /transactions` with `type = FxConversion` → 400. One create route per type (api-design §7.7). |
| R3 | **Body.** `fromAccountId` and `toAccountId` (Account PublicIds) are required. Exactly one of `sellAmount` and `buyAmount` is present and `> 0`; both or neither → 400. `expectedRate` is required on create. `bookedAt`, `memo`, `reference` follow posting. The body has no `type`, `accountId`, `currency`, `amount`, `rate` or legs array; any of those present → 400. |
| R4 | **Currencies.** The sell currency is `fromAccount.Currency`. The buy currency is `toAccount.Currency`. They must differ (else 400; same-currency moves use `POST /transactions/transfer`). |
| R5 | **Different Accounts.** `fromAccountId = toAccountId` → 400. (Implied by R4, still checked and reported on its own.) |
| R6 | **Same tenant and scope.** As internal-transfers R6: both Accounts are in the command's tenant; another tenant's Account, a missing Account, or (Adviser) an Account of an unassigned Customer → 404 for the whole command. Nothing is written. |
| R7 | **Customers.** The two Accounts may belong to the same Customer or to two Customers of the tenant. Both Customers must not be Disabled (else 400 `disabled` / `target=user`). |
| R8 | **Account types and state.** Any Phase-2 type (Bank, Cash, Brokerage, Other) on either side. Both Accounts must be Open (else 400). Same rule on reverse. |
| R9 | **Rate.** The server reads `IFxRate.GetRate(sellCurrency, buyCurrency, bookedAt)`: the latest `FxRates` row for that directed pair with `AsOfDate ≤` the date of `bookedAt`, using the same date rule as buy-sell R10. The rate is "buy currency per 1 sell currency". The directed pair is read as stored; the inverse pair is not used. No row → 400. The caller never sends a rate to use. |
| R10 | **Staleness.** If the date of `bookedAt` minus the rate's `AsOfDate` is more than 4 calendar days → 400. The same limit applies to the quote. |
| R11 | **Amounts.** The caller's amount must have no more decimal places than its currency's `DecimalPlaces` (sell currency for `sellAmount`, buy currency for `buyAmount`); else 400. Fixed sell: `buy = Round(sellAmount × rate, buyDecimalPlaces, AwayFromZero)`. Fixed buy: `sell = Round(buyAmount ÷ rate, sellDecimalPlaces, AwayFromZero)`. A computed amount of 0 → 400. |
| R12 | **Expected rate.** `expectedRate` must equal the rate of R9 exactly (`decimal(18,8)`). Different → 409 with the field in `errors`; nothing is written. The client gets a new quote and asks the caller to confirm again. The rate can change between quote and create when the hourly job stores a new row (fx-rates R16). |
| R13 | **Legs.** Application builds exactly two cash legs: `−sell` in the sell currency on the from Account and `+buy` in the buy currency on the to Account. No security leg. Each leg's currency equals its Account's currency (leg-accounts R9). |
| R14 | **FX detail.** Every `FxConversion` has exactly one `TransactionFxDetails` row: `BaseCurrency` = sell currency, `QuoteCurrency` = buy currency, `Rate`, `RateAsOfDate`, `RateSource` (the `FxRates.Source` of the row used). Written in the same unit of work as the header and legs. |
| R15 | **Cash `SUM`.** After insert, the from Account's `SUM ≥ 0` (else 400). Both Account rows are locked in ascending `Accounts.Id` order before the rate is read and the sums are checked (leg-accounts R10, R11). |
| R16 | **Opening.** An `FxConversion` leg counts as another transaction on both Accounts. After it, cash `Opening` on either Account → 400 (internal-transfers R11). |
| R17 | **Reverse.** `POST /transactions/{id}/reverse` on an `FxConversion` creates one `Reversal` with `+sell` on the from Account and `−buy` on the to Account: the original amounts, so the original rate. The current rate is not read and its staleness is not checked. The Reversal gets a copy of the original's FX detail row (same base, quote, rate, as-of date, source). R6, R7, R8 and the `SUM` check apply to both Accounts; if the to Account's `SUM` would go below 0 (the cash was spent), the whole reverse → 400. No partial reverse. |
| R18 | **Converting back.** Converting back at today's rate is a new `FxConversion` in the other direction, not a reverse. |
| R19 | **Idempotency.** `Idempotency-Key` is required on `POST /transactions/fx` (ADR 0015). Missing → 400. Replay with the same hash → original `201 { id }`; same key with a different hash → 409. The request path is part of the hash. A replay returns the original id even if the rate has since changed. |
| R20 | **Quote.** `POST /transactions/fx/quote` takes the create body without `expectedRate`, `memo`, `reference` (present → 400); `bookedAt` is optional and defaults to now. It runs R1, R3–R11 and returns the rate, its as-of date and both amounts. It does not check the cash `SUM`, does not lock, does not write, and takes no `Idempotency-Key`. A quote is not stored and does not hold the rate. |
| R21 | **Read.** Get and list follow leg-accounts R7 and internal-transfers R14: the caller sees the conversion if they can see at least one of its two Accounts, and only the legs on Accounts they can see. The item carries `fx` (R22) for every caller who can see the Transaction. `fx` shows the two currencies and the rate, not the other Account or Customer. |
| R22 | **Item `fx`.** The transaction item has an optional `fx` object `{ baseCurrency, quoteCurrency, rate, rateAsOfDate }` when the Transaction has an FX detail row; otherwise `fx` is `null`. `RateSource` is not on the wire. |
| R23 | **List `type` filter.** `type=FxConversion` works with the existing filter (internal-transfers R15). |
| R24 | **Portfolios.** A conversion is cash-only, so it is allowed on an Account with an Active portfolio assignment, on either side. It does not enqueue or trigger a rebalance (portfolios R19: a later rebalance invests the cash). Amend portfolios R18 so that its list of allowed cash types names `InternalTransfer` and `FxConversion`. |
| R25 | **Net worth.** Cash per Account from the leg `SUM`. The sell currency total goes down and the buy currency total goes up; no fold (net-worth R6). |
| R26 | **Customer Transactions report.** One row per leg account of the Customer (leg-accounts R20). Columns are unchanged in this slice. |
| R27 | Immutability (posting R10), `bookedAt` rules, memo / reference (posting R23), and close (`CloseOut` stays off-books): unchanged. |

### 4.1 Worked example

Rate row NZD → USD = `0.59120000`, as of 2026-11-09. USD has 2 decimal places, NZD has 2.

- Fixed sell `sellAmount = 1000.00`: buy = `Round(591.2, 2)` = `591.20`. Legs `−1000.00 NZD`, `+591.20 USD`.
- Fixed buy `buyAmount = 500.00`: sell = `Round(845.7375…, 2)` = `845.74`. Legs `−845.74 NZD`, `+500.00 USD`.
- Reverse of the first: `+1000.00 NZD`, `−591.20 USD`, FX detail NZD / USD / `0.59120000` / 2026-11-09.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `TransactionType` | Enum | Add `FxConversion = 16`. |
| `AppliedFxRate` | Value object | Base currency, quote currency, rate `> 0`, as-of date, source. Base ≠ quote. |
| `Transaction` | Aggregate | New factory for `FxConversion` taking the from Account, the to Account, an `AppliedFxRate`, the fixed side (sell or buy amount as `Money`) and the decimal places of the computed currency. Computes the other amount with R11 and builds the legs of R13 and the FX detail of R14. `Reverse` copies the FX detail when the original has one. |
| `TransactionFxDetail` | Entity (child of `Transaction`) | 0..1 per Transaction. Immutable after create. |
| `TransactionPosted` / `TransactionReversed` | Event | Unchanged. |

Invariants for `FxConversion`:

- Exactly two cash legs and no security leg, on two different Accounts.
- The two legs have different currencies.
- The from leg is negative in the base currency of the FX detail; the to leg is positive in its quote currency.
- Exactly one FX detail.
- The computed leg equals the R11 formula applied to the fixed leg and the rate.

The Domain does not read `Currencies`; Application passes the decimal places. Update [domain-model.md](../domain-model.md) when this spec is accepted.

---

## 6. Database

Script: `database/schema/0026_fx_conversions.sql`. Forward only.

| Table | Change | Index / FK |
| --- | --- | --- |
| `Transactions` | `CK_Transactions_Type` → `BETWEEN 0 AND 16` | none |
| `TransactionFxDetails` | add | PK `TransactionId`; FK `TransactionId` → `Transactions.Id` RESTRICT; FK `BaseCurrency`, `QuoteCurrency` → `Currencies.Code` RESTRICT; CHECK `BaseCurrency <> QuoteCurrency`; CHECK `Rate > 0` |

`TransactionFxDetails` columns:

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| TransactionId | int | no | PK and FK. One row at most per Transaction |
| BaseCurrency | char(3) | no | Sell currency for `FxConversion` |
| QuoteCurrency | char(3) | no | Buy currency for `FxConversion` |
| Rate | decimal(18,8) | no | Quote per 1 base; same precision as `FxRates.Rate` |
| RateAsOfDate | date | no | `FxRates.AsOfDate` of the row used |
| RateSource | nvarchar(50) | no | `FxRates.Source` of the row used |

No `TenantId` (the header has it), no `PublicId`, no `RowVersion`: the row is part of an immutable Transaction. Leg shape of R13 is enforced in Domain and Application, not by a database check.

Update [database-design.md](../database-design.md) when this spec is accepted. Do not add EF migrations.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreateFxConversion` | `{ id }` | `transactions.create`; idempotency; R3–R15; lock both Accounts by ascending Id; insert header, two legs and FX detail in one unit of work |
| Query | `GetFxConversionQuote` | quote | `transactions.create`; R3–R11; no lock, no write |
| Command | `CreateTransaction` | unchanged | Rejects `FxConversion` (R2) |
| Command | `ReverseTransaction` | unchanged | R17; copies the FX detail |
| Query | `GetTransactions` / `GetTransactionById` | unchanged | Item adds `fx` (R22) |

`CreateFxConversion` is its own command, as `CreateInternalTransfer` is. A missing rate (`FxRateNotFoundException`) becomes a 400 validation error; it is not caught and replaced by 1.

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| POST | `/transactions/fx` | `transactions.create` | 201 `{ id }` | 400 / 401 / 403 / 404 / 409 |
| POST | `/transactions/fx/quote` | `transactions.create` | 200 quote | 400 / 401 / 403 / 404 |
| POST | `/transactions` | `transactions.create` | unchanged | 400 when `type = FxConversion` |

`id` is the Transaction PublicId. `Location` is `/transactions/{id}`. Both routes sit under `/transactions`, so the BFF allow-list needs no change. The quote is a `POST` because it takes a body and the same validation as create; it is safe and writes nothing.

### 8.1 Bodies

Quote request (fixed sell):

```json
{
  "fromAccountId": "<NZD account publicId>",
  "toAccountId": "<USD account publicId>",
  "sellAmount": 1000.00,
  "bookedAt": "2026-11-10T09:00:00+13:00"
}
```

Quote response:

```json
{
  "baseCurrency": "NZD",
  "quoteCurrency": "USD",
  "rate": 0.59120000,
  "rateAsOfDate": "2026-11-09",
  "sell": { "amount": 1000.00, "currency": "NZD" },
  "buy": { "amount": 591.20, "currency": "USD" }
}
```

Create (fixed buy):

```json
{
  "fromAccountId": "<NZD account publicId>",
  "toAccountId": "<USD account publicId>",
  "buyAmount": 500.00,
  "expectedRate": 0.59120000,
  "bookedAt": "2026-11-10T09:00:00+13:00",
  "memo": "USD for US ETF",
  "reference": null
}
```

SystemAdmin adds `tenantId` to quote and create. TenantAdmin / Adviser must omit it.

Item (caller sees both Accounts):

```json
{
  "id": "<transaction publicId>",
  "tenantId": "<tenant publicId>",
  "type": "FxConversion",
  "bookedAt": "2026-11-10T09:00:00+13:00",
  "memo": "USD for US ETF",
  "reference": null,
  "originalTransactionId": null,
  "reversedByTransactionId": null,
  "rowVersion": "<base64>",
  "cash": [
    { "accountId": "<NZD account>", "customerId": "<customer>", "amount": -845.74, "currency": "NZD" },
    { "accountId": "<USD account>", "customerId": "<customer>", "amount": 500.00, "currency": "USD" }
  ],
  "security": [],
  "fx": { "baseCurrency": "NZD", "quoteCurrency": "USD", "rate": 0.59120000, "rateAsOfDate": "2026-11-09" }
}
```

Every other type returns `"fx": null` in this slice. Leg order in `cash` is not a contract; direction comes from the sign.

### 8.2 Errors

Ordinary validation in `errors`: same Account, same currency, both or neither amount, amount `≤ 0`, too many decimal places, computed amount 0, missing rate, stale rate, Closed Account, forbidden body field, insufficient cash on create or reverse, `FxConversion` sent to `POST /transactions`.

`409` with `expectedRate` in `errors` when the rate changed (R12). `409` for an idempotency key reused with a different body (R19).

§4.1 `disabled`: `target=tenant` on a disabled tenant, `target=user` when either Customer is Disabled.

Any Account outside the caller's scope or tenant → **404**, not 403.

---

## 9. UI

Adviser Portal. Amend [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) when this spec is accepted.

- Account detail on an Open Account shows **Convert** beside Transfer.
- **Destination.** Same two-step picker as Transfer (internal-transfers §9): Customer (default the source Account's Customer, searchable), then Account. Here an Account is not selectable when it is Closed, has the **same** currency as the source, or is the source Account; greyed out with the reason.
- **Amount.** A toggle "I sell" / "I buy" chooses which amount the Adviser types; the field shows the matching currency. The form does not ask for a sign.
- **Live quote.** On a valid amount and destination, the form calls `POST /transactions/fx/quote` (debounced ~300 ms) and shows "1 NZD = 0.5912 USD (as of 2026-11-09)" and the computed amount. A quote error (missing or stale rate) is shown on the form and blocks submit.
- **Confirm.** Summary: "Convert 845.74 NZD from <account> (<customer>) to 500.00 USD in <account> (<customer>) at 1 NZD = 0.5912 USD (as of 2026-11-09)". When the two Customers differ, the summary shows the same extra warning line as Transfer.
- **Submit** calls `POST /transactions/fx` with the quoted `expectedRate` and a new `Idempotency-Key` per form open. On 409 for `expectedRate`, the form fetches a new quote and asks the Adviser to confirm again.
- Transaction lists show the leg on the Account being viewed and the rate line.

Customer Portal: read only, no new page. A conversion shows as "Currency exchange" with the amount of the visible leg and the rate line. No counterparty is shown.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | Factory: fixed sell and fixed buy amounts and rounding of §4.1 (including JPY, 0 decimal places); legs and FX detail of R13–R14; rejects same Account, same currency, amount `≤ 0`, computed amount 0, non-positive rate; Reverse produces the opposite legs and copies the FX detail |
| Application.UnitTests | Staleness limit at 4 and 5 days (R10); decimal places check (R11) |
| Application.FunctionalTests | No Bearer → 401 not 302; Customer → 403; happy path fixed sell and fixed buy, same Customer and two Customers (TenantAdmin, Adviser assigned to both); FX detail and item `fx` match the rate row; Adviser unassigned on either side → 404 and nothing written; same Account → 400; same currency → 400; both / neither amount → 400; missing rate → 400; stale rate → 400; `expectedRate` differs → 409 and nothing written; Closed from or to → 400; Disabled Customer on either side → 400 `disabled` / `target=user`; insufficient cash → 400; forbidden body fields → 400; `POST /transactions` with `FxConversion` → 400; missing `Idempotency-Key` → 400, replay → same id, different hash → 409; balances of both Accounts after create and after reverse; reverse uses the original amounts after the rate row changed; reverse after the to Account spent the cash → 400 and nothing written; Opening on either Account after a conversion → 400; allowed on an Account with an Active portfolio assignment and no rebalance enqueued; quote returns the same amounts as create and writes nothing; quote with `memo` → 400; caller who sees one side gets one leg and `fx`; list `type=FxConversion` |
| Infrastructure.IntegrationTests | `CK_Transactions_Type` accepts 16 and rejects 17; `TransactionFxDetails` rejects equal currencies, `Rate ≤ 0`, unknown currency, a second row for one Transaction |
| Application.FunctionalTests (two tenants) | From or to Account in the other tenant → 404 on create and quote, nothing written (TenantAdmin and SystemAdmin); other tenant's conversion id on get / reverse → 404 (`FxConversionIsolationTests`) |
| Frontend | Picker greys out same-currency Accounts; sell / buy toggle; quote debounced and shown; quote error blocks submit; 409 re-quotes and asks to confirm; warning across Customers |

---

## 11. Locked in this spec (discussion, 2026-11-10)

No new ADR. ADR 0011 §6 (one cash currency per Account) is unchanged.

| Item | Lock |
| --- | --- |
| Type name | `FxConversion = 16` |
| Shape | One Transaction, two cash legs on two Accounts of different currencies. Same tenant; same or different Customer. Cross-currency transfer is this type. |
| Route | `POST /transactions/fx` create; `POST /transactions/fx/quote` read-only quote. Read and reverse on existing routes. `POST /transactions` rejects the type. Every new type gets its own create route (api-design §7.7). |
| Amount input | Exactly one of `sellAmount` / `buyAmount`; the server computes the other. Both are stored as legs. |
| Rate | Server-side cached reference rate for the directed pair at `bookedAt`; no caller rate; no fee or spread |
| Staleness | More than 4 calendar days between rate as-of date and `bookedAt` → 400 |
| Rounding | Computed side rounded to its currency's `DecimalPlaces`, `AwayFromZero`; caller amount within its currency's `DecimalPlaces` |
| Confirmation | Required `expectedRate`; mismatch → 409. No stored quote, no rate lock |
| Rate record | `TransactionFxDetails`, 0..1 per Transaction; copied to the Reversal; item `fx` object |
| Reverse | Original amounts (original rate); all-or-nothing; fails if the destination spent the cash |
| Portfolios | Allowed on assigned Accounts; no automatic rebalance; portfolios R18 cash list names `InternalTransfer` and `FxConversion` |
| Script | `0026_fx_conversions.sql` |

Still open, not this slice:

- Buy / Sell amendment: same-currency rule for manual trades that add a holding, manual `Sell` and rebalance with automatic FX, storing their rate in `TransactionFxDetails`, Buy / Sell cash rounding to `DecimalPlaces` ([cross-currency-trades.md](cross-currency-trades.md), draft).
- Fee or spread, and who configures it.
- FX columns in the Customer Transactions report.
- An HTTP read route for `FxRates`.

---

## 12. Suggested commits

Only after `accepted`. The tree must build after each commit. Split each behaviour into red then green.

1. `0026_fx_conversions.sql` + `TransactionType.FxConversion` + integration tests for the type check and `TransactionFxDetails` constraints.
2. Domain `AppliedFxRate`, factory, FX detail, rounding and invariants + unit tests; Reverse copies the FX detail.
3. `CreateFxConversion` + `POST /transactions/fx` happy path (fixed sell, fixed buy), idempotency, validation (R3–R5, R7–R11, R15) + functional tests.
4. `expectedRate` check (R12) + tests.
5. Authority and tenant isolation (R6) + functional two-tenant tests.
6. Quote route (R20) + tests.
7. `POST /transactions` rejects `FxConversion` (R2); Opening after a conversion (R16) + tests.
8. Reverse of a conversion (R17) + tests.
9. Item `fx` (R21, R22) and list filter (R23) + tests.
10. Portfolio interaction (R24) + tests.
11. Adviser Portal Convert form and quote + frontend tests. Customer Portal label.
12. Repo docs: this file `accepted`; amend domain-model, database-design, api-design §7.7 and change log, portfolios R18, adviser-portal-dashboard, customer-portal, features README status.
