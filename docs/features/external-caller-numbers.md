---
title: Caller numbers on External accounts
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-18
last_updated: 2026-11-18
related:
  - account-custody-mode.md
  - custody-modes.md
  - tenant-custody-modes.md
  - buy-sell.md
  - trade-fees.md
  - fx-conversions.md
  - cross-currency-trades.md
  - holdings.md
  - leg-accounts.md
  - ../fixes/ledger-write-guards.md
  - ../adr/0020-account-custody-mode.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../adviser-portal/adviser-portal-fees.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
  - ../future-plans.md
---

# Caller numbers on External accounts

Amendment to [buy-sell.md](buy-sell.md), [trade-fees.md](trade-fees.md) and [fx-conversions.md](fx-conversions.md) on `webapi`. It is the fourth slice of the custody modes thread ([future-plans.md](../future-plans.md) §9), after [account-custody-mode.md](account-custody-mode.md), [custody-modes.md](custody-modes.md) and [tenant-custody-modes.md](tenant-custody-modes.md).

An External account records what happened somewhere else, so its numbers come from the caller ([ADR 0020](../adr/0020-account-custody-mode.md), the `External` row of the decision table). Today a `Buy` / `Sell` on an External account is still priced by the server from the stored close price, charged the tenant's trade fee schedule, and converted at the cached reference rate; an `FxConversion` between External accounts is priced at the reference rate. None of these is the number the outside broker or bank used, so the External account does not agree with its statement. This slice makes the caller supply them: the **executed price** and the fee on a `Buy` / `Sell`, the FX rate when the trade settles in another currency, and both amounts of an `FxConversion`.

Custodial accounts do not change. Server pricing, trade fee schedules, the reference rate and `expectedRate` then apply to Custodial accounts only.

No new route, no new policy, no new `TransactionType`, no problem code. One new body field (`fxRate` on `POST /transactions`). Script `0034_entered_trade_fees.sql` drops one CHECK.

**Status is `accepted` (2026-11-18).** Not landed. The amendments in buy-sell, trade-fees, fx-conversions, cross-currency-trades and custody-modes, in the two Adviser Portal page contracts, in domain-model, database-design, api-design and the glossary are in force from the same change.

---

## 1. Summary

| | External account | Custodial account |
| --- | --- | --- |
| `Buy` / `Sell` price | **Caller**: `price` on the security leg, required | Server, from `InstrumentPrices` (unchanged). `price` → 400 |
| `Buy` / `Sell` fee | **Caller**: `fee`, optional; none when omitted. No schedule is read | Server, from the tenant's trade fee schedule (unchanged). `fee` → 400 |
| `Buy` / `Sell` FX rate (quote currency ≠ Account currency) | **Caller**: `fxRate`, required; stored as the FX detail | Server, from `FxRates` (unchanged). `fxRate` → 400 |
| `FxConversion` amounts | **Caller**: both `sellAmount` and `buyAmount`; the rate is derived from them and stored | Caller names one amount; the server computes the other from the reference rate (unchanged) |
| `expectedRate` | Refused | Required (unchanged) |
| `POST /transactions/fx/quote` | Refused | Unchanged |
| `bookedAt` | Caller, required (unchanged) | Server (unchanged) |

With this slice the third invariant of [custody-modes.md](custody-modes.md) §1 holds in both directions: on a Custodial account the server decides the price, the cost, the FX rate and the fee; on an External account the caller does.

A side effect: recording a trade or a conversion on an External account no longer needs a stored price or a stored FX rate. Historical trades can be entered for any date, and for instruments or currency pairs the market data jobs do not cover ([known-issues/eodhd-nz-jp-coverage.md](../known-issues/eodhd-nz-jp-coverage.md)).

---

## 2. Scope

**In**

- Domain: `PostBuy` / `PostSell` take an optional **entered fee** in place of a schedule, and an optional `AppliedFxRate` that is stored as the trade's FX detail. A new factory `PostFxConversionWithAmounts` builds a conversion from both amounts and derives the rate. `TransactionFeeDetail` may have no schedule
- Application: `CreateTransaction` takes the price, fee and FX rate of a `Buy` / `Sell` from the body on an External account and reads no price, rate or schedule. `CreateFxConversion` takes both amounts on an External account and reads no rate. `GetFxConversionQuote` refuses External accounts. The body rules that depend on the custody mode move from the validators to the handlers
- Script `database/schema/0034_entered_trade_fees.sql`: drop `CK_TransactionFeeDetails_BrokerageSchedule`
- HTTP: `security[].price`, `fee` and the new `fxRate` on `POST /transactions` for External `Buy` / `Sell`; both amounts and no `expectedRate` on `POST /transactions/fx` for External Accounts. The transaction item does not change shape
- Adviser Portal: Buy / Sell form inputs on External accounts; the convert form on External accounts takes two amounts and calls no quote; one line on the fees pages
- Tests listed in §10

**Out**

- Any change on a Custodial account
- Changing posted Transactions. External `Buy` / `Sell` and `FxConversion` rows posted before this slice keep their server numbers, their schedule fee details and their reference-rate FX details (R21)
- A settlement amount in place of `fxRate` (§11 item 1)
- A fee in the Account currency, several fees on one trade, or fee kinds other than `Brokerage` ([trade-fees.md](trade-fees.md) §2 Out)
- Checking caller numbers against `InstrumentPrices` or `FxRates`, or a tolerance band around the close price
- The rest of [cross-currency-trades.md](cross-currency-trades.md) (`draft`): the currency rule for manual adds, FX details and staleness on server-priced `Buy` / `Sell`, and cash-leg rounding to the currency's `DecimalPlaces`. This slice limits that draft to Custodial accounts (§4.5); it does not accept it
- A `source` field on the item's `fx` object, or a mark on the item that says a number was entered by the caller. Read-side grouping by custody mode is slice 5
- Customer Portal changes
- `StockIn`, `OpeningHoldings`, cash types, `Split` / `Bonus` / `Scrip`: their numbers already come from the caller
- Portfolio rebalance: only Custodial accounts rebalance ([custody-modes.md](custody-modes.md) R15)

---

## 3. Stories

1. As an Adviser I record a `Buy` on a Customer's External brokerage account with the price and the brokerage on the contract note, so that cash and cost match the outside broker's statement.
2. As an Adviser I record a `Sell` with its price and no fee, and no fee leg is booked even though my firm has a trade fee schedule.
3. As an Adviser I record a `Buy` of a USD instrument in an NZD External account with the FX rate the outside broker used, and I see that rate on the transaction.
4. As an Adviser I record a trade for an instrument and date that have no stored price.
5. As an Adviser I record a currency conversion between two External accounts with the two amounts on the bank statement. I do not ask for a quote and I do not confirm a rate.
6. As an Adviser I reverse such a trade or conversion, and it comes back with the numbers I entered.
7. As an Adviser on a Custodial account, nothing changes: the server prices, charges the schedule's fee and quotes the rate.
8. As a TenantAdmin I know that my trade fee schedules apply only to trades on Custodial accounts.

---

## 4. Rules

### 4.1 Common

| ID | Rule |
| --- | --- |
| R1 | Unchanged access and request rules: posting R1–R4a, buy-sell R1–R5, R17, fx-conversions R1, R2, R6, R7, R19, custody-modes R1. 401 / 403 / 404, PublicId only, `tenantId` rules, Closed account and Disabled Customer, `Idempotency-Key`. An Account the caller cannot see is 404 before any rule of this spec is evaluated. |
| R2 | **Which numbers.** The custody mode of the leg accounts decides where a number comes from. Every leg account of a Transaction has the same custody mode (custody-modes R8), so one mode decides the whole Transaction. External: the rules of §4.2 and §4.4. Custodial: the existing rules, unchanged (R13, R20). |
| R3 | **Order.** Request-shape validation runs first, as today: a field that is present must have the shape of R5, R6, R7 or R15, whatever the custody mode. In the handler, the rules of this spec run after the Account is loaded and locked, after the Closed-account and Disabled-Customer checks, after the manual entry matrix (custody-modes R6) and after `bookedAt` is resolved (custody-modes R10, R11). On `POST /transactions` the instrument is then loaded and checked as today (buy-sell R7), and R5, R6, R7 run in that order. On the conversion routes they run after the same-custody-mode check (custody-modes R8). |

### 4.2 `Buy` / `Sell` on an External account

| ID | Rule |
| --- | --- |
| R4 | **Body.** `security` still holds exactly one leg with `instrumentId` and `quantity` `> 0`. `cost`, `amount`, `currency` and `cash` → 400, as today (buy-sell R9). Three fields depend on the custody mode: `security[0].price`, `fee` and `fxRate`. On every other `TransactionType` of `POST /transactions`, `fee` and `fxRate` → 400 in the validator, and the `price` refusals of `OpeningHoldings`, `StockIn` / `StockOut` and `Split` / `Bonus` / `Scrip` stay, with today's messages. |
| R5 | **Executed price.** `security[0].price` is required: the price per unit at which the trade was filled, in the instrument's quote currency. Shape: `> 0` (`Price must be greater than 0.`), at most 8 decimal places (`Price can have at most 8 decimal places.`; the precision of `InstrumentPrices.Price`). Missing → 400 with `errors["Security[0].Price"]` and the message `Price is required on an External account. Enter the executed price.` The server does not call `IMarketData`: a missing or different stored price does not matter. |
| R6 | **Fee.** `fee` is optional: the brokerage the outside broker charged, in the instrument's quote currency. Shape: `≥ 0` (`Fee cannot be negative.`). It must have no more decimal places than the quote currency's `DecimalPlaces` (else 400 with `errors.Fee` and the message `Fee can have at most {n} decimal places.`, checked after the instrument is loaded). Omitted, `null` or `0` → the trade has no `Fee` leg. The server does not read the tenant's trade fee schedules for an External account. |
| R7 | **FX rate.** When the instrument's quote currency differs from the Account currency, `fxRate` is required: the number of units of Account currency per 1 unit of quote currency that the outside broker used (for a USD instrument in an NZD account, NZD per 1 USD). Shape: `> 0` (`FxRate must be greater than 0.`), at most 8 decimal places (`FxRate can have at most 8 decimal places.`; the precision of `TransactionFxDetails.Rate`). Missing → 400 with `errors.FxRate` and the message `FxRate is required when the instrument currency differs from the account currency.` When the two currencies are the same, `fxRate` → 400 with `errors.FxRate` and the message `FxRate cannot be set when the instrument currency is the account currency.` The server does not call `IFxRate`. |
| R8 | **Amounts.** The legs use the formulas of buy-sell R11, R12, R15 and trade-fees R15, R16 with the caller's numbers in place of the server's: `gross = Round(quantity × price, 4)` in the quote currency; `Buy` principal `−Round(gross × fxRate, 4)`; `Sell` principal `Round(quantity × price × fxRate, 4)`; fee leg `−Round(fee × fxRate, 4)` in the Account currency; `Buy` cost `gross + fee`; `Sell` release unchanged. Same currency → `fxRate` is 1. All rounding is `AwayFromZero`. A fee that rounds to 0 at the leg's rounding books no `Fee` leg and no fee detail (trade-fees implementation note 2026-11-15). |
| R9 | **Fee detail.** A `Fee` leg from R6 has one `TransactionFeeDetails` row as in trade-fees R18: kind `Brokerage`, **no schedule**, basis amount `gross`, fee, quote currency. On the item, `fee.scheduleId` is `null`. A fee detail without a schedule is a fee the caller entered. |
| R10 | **FX detail.** A `Buy` / `Sell` with an `fxRate` gets one `TransactionFxDetails` row in the same unit of work: `BaseCurrency` = the instrument's quote currency, `QuoteCurrency` = the Account currency, `Rate` = `fxRate`, `RateAsOfDate` = the UTC date of `bookedAt` ([utc-instants](../fixes/utc-instants.md)), `RateSource` = `Caller`. The item's `fx` object shows it (fx-conversions R22). A same-currency trade has no FX detail and `fx: null`. This is the shape [cross-currency-trades.md](cross-currency-trades.md) R7 sets for server-priced trades; the only difference is the source. |
| R11 | **Unchanged checks.** Account type Brokerage or Other (buy-sell R6), active instrument (R7), `Sell` quantity not above the holding (R14), cash `SUM ≥ 0` including the fee leg (R13, trade-fees R17), projection upsert (R15), `bookedAt` required and not in the future (R18, posting R24). A currency mismatch between instrument and Account is allowed on both `Buy` and `Sell`, with `fxRate` (§4.5). |
| R12 | **Reverse.** Unchanged (buy-sell R16, trade-fees R19, fx-conversions R17). The Reversal copies the opposite legs, the fee detail with no schedule and the FX detail. Nothing is read again. |
| R13 | **Custodial account.** Unchanged. `security[0].price` → 400 (buy-sell R9, message `Price cannot be set on a buy or sell.`), `fee` → 400 (trade-fees R14, message `Fee cannot be set. Brokerage is added by the server from the trade fee schedule.`), and the new `fxRate` → 400 with `errors.FxRate` and the message `FxRate cannot be set on a Custodial account. The server reads the rate.` The price, the rate and the schedule are read for the server's instant (custody-modes R12). These refusals run in the handler at the same point as R5–R7, after the instrument is loaded, so another tenant's instrument is 404 on both custody modes. |
| R14 | **Idempotency.** `fee` and `fxRate` join `security[].price` in the request hash. A replay with the same body returns the original id. Adding the two fields changes the hash of every `POST /transactions` body, so a retry that crosses the deploy with a key stored before it gets 409; this is accepted (one deploy ships `webapi` and the portal). |

### 4.3 Worked examples (`Buy` / `Sell`)

NZD has 2 decimal places, USD has 2.

- Same currency. NZD External account, NZD instrument. `Buy` 100 at `price` 2.345 with `fee` 15.00: gross `234.5000`; legs `−234.5000 NZD` (Principal) and `−15.0000 NZD` (Fee, `scheduleId: null`); cost `249.5000 NZD`. Then `Sell` 40 at 2.50 with no `fee`: one leg `+100.0000 NZD`; released cost `Round(249.5 × 40 / 100, 4)` = `99.8000`; no Fee leg even if the tenant has a schedule for NZD.
- Two currencies. NZD External account, USD instrument. `Buy` 10 at `price` 180.00 with `fee` 9.95 and `fxRate` 1.69147: gross `1800.0000 USD`; legs `−3044.6460 NZD` (Principal) and `−16.8301 NZD` (Fee; `9.95 × 1.69147 = 16.8301265`); cost `1809.9500 USD`; FX detail USD → NZD `1.69147000`, as of the UTC date of `bookedAt`, source `Caller`.

### 4.4 `FxConversion` on External accounts

| ID | Rule |
| --- | --- |
| R15 | **Body.** On `POST /transactions/fx` between two External Accounts, both `sellAmount` and `buyAmount` are required. Shape: each `> 0` (fx-conversions R3). Each has no more decimal places than its currency's `DecimalPlaces` (fx-conversions R11). One missing → 400 on the missing field (`errors.SellAmount` or `errors.BuyAmount`) with the message `An External conversion needs both sellAmount and buyAmount.` |
| R16 | **No confirmation.** `expectedRate` → 400 with `errors.ExpectedRate` and the message `expectedRate cannot be set on an External conversion. The rate comes from the two amounts.` |
| R17 | **Rate.** `rate = Round(buyAmount ÷ sellAmount, 8, AwayFromZero)`: buy currency per 1 sell currency, the direction of fx-conversions R9. A rate that rounds to 0 or does not fit `decimal(18,8)` → 400 with `errors.BuyAmount`. The server does not call `IFxRate`, and fx-conversions R9, R10 (rate row, staleness) and R12 (`expectedRate`) do not apply. |
| R18 | **Legs and detail.** The legs are `−sellAmount` on the from Account and `+buyAmount` on the to Account, exactly as entered; nothing is rounded. The FX detail (fx-conversions R14) has `BaseCurrency` = sell currency, `QuoteCurrency` = buy currency, `Rate` from R17, `RateAsOfDate` = the UTC date of `bookedAt`, `RateSource` = `Caller`. |
| R19 | **Quote.** `POST /transactions/fx/quote` between two External Accounts → 400 with `errors.FromAccountId` and the message `A quote applies to Custodial accounts only. On an External account enter both amounts.` The check runs after both Accounts are found, after the same-custody-mode check, after the Closed check and after `bookedAt` is resolved (the order of R3), and before any rate is read. So a future `bookedAt` on such a quote still gets `errors.BookedAt` (ledger-write-guards), and a quote between an External and a Custodial account still gets `errors.ToAccountId` (custody-modes R8). |
| R20 | **Unchanged.** fx-conversions R4–R8 (different currencies, different Accounts, same tenant and Customer, Open), R13 (two cash legs), R15 (lock order, from `SUM ≥ 0`), R16 (Opening), R17 (reverse copies the amounts and the FX detail), R19 (idempotency), R21–R27 (reads, portfolios, net worth, report). Between two Custodial Accounts nothing changes: exactly one of `sellAmount` and `buyAmount`, `expectedRate` required, the reference rate and the quote. |

Worked example. Sell `845.74 NZD`, buy `500.00 USD` between two External Accounts: rate `Round(500.00 ÷ 845.74, 8)` = `0.59119824`. Legs `−845.74 NZD`, `+500.00 USD`. FX detail NZD / USD / `0.59119824` / the UTC date of `bookedAt` / `Caller`. JPY (0 decimal places): sell `100000 JPY`, buy `612.40 USD` → rate `0.00612400`; a `sellAmount` of `100000.5` → 400 (R15).

### 4.5 Other specs

| ID | Rule |
| --- | --- |
| R21 | **Existing data.** No Transaction is changed. External `Buy` / `Sell` posted before this slice keep their server price, their schedule fee details and `fx: null`; External conversions keep their reference-rate FX details. Their reversal copies what they have, as today. |
| R22 | **Trade fee schedules.** trade-fees R7 and R14 apply to `Buy` / `Sell` on Custodial accounts only (from `POST /transactions` and from rebalance). The cut-off date of trade-fees R9 is unchanged: it still counts every posted `Buy` / `Sell` of the tenant, External ones included. Since [ledger-write-guards](../fixes/ledger-write-guards.md) no trade can be dated more than 5 minutes ahead, so an External trade cannot push the cut-off date past today. Schedules used by trades posted before this slice stay in effect and unchangeable (trade-fees R12). |
| R23 | **Cross-currency trades (`draft`).** The rules of [cross-currency-trades.md](cross-currency-trades.md) are limited to Custodial accounts: the same-currency rule for manual `Buy`, `StockIn` and `OpeningHoldings` (its R2), automatic FX and staleness on a manual `Sell` (R3, R8), the FX detail of a server-priced trade (R7) and cash rounding (R9). On an External account a `Buy` or `Sell` in another currency is allowed with the caller's `fxRate` (R7), and `StockIn` / `OpeningHoldings` keep today's behaviour. |
| R24 | **Net worth, holdings, reports.** Unchanged. Valuation still uses `InstrumentPrices` and `IFxRate` for both custody modes (net-worth); only the recording of a trade stops depending on them. The Customer Transactions Report shows the entered fee in its Fee column (trade-fees R22). |

Policies: none added. `transactions.create` posts the trade and the conversion.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `Transaction` | Aggregate (existing) | `PostBuy` / `PostSell` take an optional `enteredFee` (quote currency, `≥ 0`) and an optional `AppliedFxRate`. New factory `PostFxConversionWithAmounts`. `Reverse` is unchanged; it already copies the fee and FX details |
| `TransactionFeeDetail` | Entity (existing) | A `Brokerage` detail may have no schedule. The factory already takes `int?` |
| `AppliedFxRate`, `TransactionFxDetail` | VO / entity (existing) | Unchanged. The source string `Caller` marks a rate the caller entered |
| `TradeFeeSchedule`, `CustodyRules` | (existing) | Unchanged |
| Events | | None added |

```text
PostBuy / PostSell(..., price, fxRate, ..., feeSchedule?, quoteDecimalPlaces, enteredFee?, appliedRate?)
  feeSchedule and enteredFee both given → DomainException
  enteredFee < 0 → DomainException
  enteredFee > 0 → Fee leg −Round(enteredFee × fxRate, 4) and fee detail (Brokerage, no schedule, gross, enteredFee)
  appliedRate given:
    base = price currency, quote = account currency, rate = fxRate, else DomainException
    the FX detail is attached
  everything else unchanged

PostFxConversionWithAmounts(tenantId,
    fromAccountId, fromCurrency, fromCustodyMode,
    toAccountId, toCurrency, toCustodyMode,
    Money sell, Money buy, bookedAt, memo, reference, fromCurrentCashSum)
  same checks as PostFxConversion: different Accounts, same custody mode, different currencies,
    sell currency = from currency, buy currency = to currency, amounts > 0, from SUM ≥ 0
  rate = Round(buy ÷ sell, 8, AwayFromZero), > 0
  legs −sell / +buy; FX detail (sell, buy, rate, UTC date of bookedAt, "Caller")
```

Invariants:

- A `Fee` leg's fee detail has a schedule when the server computed the fee, and none when the caller entered it. Exactly one source per fee.
- A `Buy` / `Sell` with an FX detail has base = its security leg's quote currency and quote = its cash legs' currency, and its cash legs were computed with that rate.
- An `FxConversion` built from both amounts has legs equal to them and a rate equal to `Round(buy ÷ sell, 8)`.

Where I3 is held: the factories do not read the custody mode to choose the source of a number. The handlers do (R2), the same split as custody-modes R7 and R13: the test seed and a later simulator build Transactions through the factories directly. The Domain refuses a trade with both a schedule and an entered fee, which no caller can produce.

Recorded in [domain-model.md](../domain-model.md) §8.6.

---

## 6. Database

Script: `database/schema/0034_entered_trade_fees.sql`. Forward only. Do not add EF migrations.

| Table | Change | Index / FK |
| --- | --- | --- |
| `TransactionFeeDetails` | Drop `CK_TransactionFeeDetails_BrokerageSchedule` (`[Kind] <> 0 OR [TradeFeeScheduleId] IS NOT NULL`) | none. The FK to `TradeFeeSchedules` stays; `TradeFeeScheduleId` is already nullable |
| `TransactionFxDetails`, `TransactionCashLegs` and every other table | none | none |

- After the script, a `Brokerage` row with `TradeFeeScheduleId` NULL is a fee the caller entered. Every existing row has a schedule, so the script changes no data.
- No CHECK replaces the dropped one. Which fees may lack a schedule (those on External accounts) depends on the Account, which `TransactionFeeDetails` does not join; Domain and Application hold it.
- `TransactionFxDetails.RateSource` already takes any `nvarchar(50)`. `Caller` needs no change.

Existing data: none changed (R21). Functional tests create their own rows.

Recorded in [database-design.md](../database-design.md) §9, §11.4 and §13.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Command | `CreateTransaction` | Gains `FxRate`. Validator: shape of `security[].price`, `fee` and `fxRate` when present (R5–R7); the `Null` rules for `price` and `fee` on `Buy` / `Sell` leave it; `fee` and `fxRate` stay `Null` for every other type, and so do the `price` rules of the other types (R4). Handler, `Buy` / `Sell`: External → R5–R10 with no `IMarketData`, `IFxRate` or schedule lookup; Custodial → R13, then today's path. The hash gains `fee` and `fxRate` (R14) |
| Command | `CreateFxConversion` | Validator: each amount `> 0` when present, `expectedRate` shape when present; the "exactly one amount" and "expectedRate required" rules leave it. Handler: External → R15–R18 with `PostFxConversionWithAmounts`; Custodial → "exactly one amount" and "expectedRate required" (same messages as today), then today's path |
| Query | `GetFxConversionQuote` | External Accounts → 400 (R19). Its validator does not change |
| Command | `ReverseTransaction` | Unchanged (R12) |
| Command | `RebalanceAccount`, trade fee schedule commands and queries, `TradeFeeSchedule` cut-off date | Unchanged (R22) |
| Query | `GetTransactions` / `GetTransactionById` | Unchanged. `fee.scheduleId` is `null` for an entered fee; `fx` is filled for an External `Buy` / `Sell` with a rate |

Where each rule lives:

| Rule | Where |
| --- | --- |
| Shape of `price`, `fee`, `fxRate`, `sellAmount`, `buyAmount`, `expectedRate` | The two validators; no database read |
| Which of them are required or refused | The handlers, after the Account is loaded (R3). Shared with nothing else, so no helper class is needed |
| Fee decimal places | `CreateTransaction` handler, after the instrument is loaded (it needs the quote currency) |
| Entered fee, FX detail of a trade, conversion rate from two amounts | The Domain factories (§5) |

A refused request stores no idempotency record: every new check throws inside the command's database transaction, before any insert.

---

## 8. API

No new routes. No new policies.

| Method | Route | Change |
| --- | --- | --- |
| POST | `/transactions` | External `Buy` / `Sell`: `security[0].price` required, `fee` optional, `fxRate` required when currencies differ. Custodial: unchanged, plus `fxRate` refused |
| POST | `/transactions/fx` | External: `sellAmount` and `buyAmount` both required, `expectedRate` refused. Custodial: unchanged |
| POST | `/transactions/fx/quote` | External: refused. Custodial: unchanged |
| GET | `/transactions`, `/transactions/{id}` | Item shape unchanged |

### 8.1 Bodies

`Buy` on an External NZD account of a USD instrument:

```json
{
  "accountId": "<external account publicId>",
  "type": "Buy",
  "bookedAt": "2026-11-17T22:30:00Z",
  "memo": "Contract note 48213",
  "reference": "48213",
  "security": [
    { "instrumentId": "<instrument publicId>", "quantity": 10, "price": 180.00 }
  ],
  "fee": 9.95,
  "fxRate": 1.69147
}
```

`Sell` on an External account in the instrument's currency, no fee:

```json
{
  "accountId": "<external account publicId>",
  "type": "Sell",
  "bookedAt": "2026-11-17T22:30:00Z",
  "security": [
    { "instrumentId": "<instrument publicId>", "quantity": 40, "price": 2.50 }
  ]
}
```

`FxConversion` between two External Accounts:

```json
{
  "fromAccountId": "<external NZD account publicId>",
  "toAccountId": "<external USD account publicId>",
  "sellAmount": 845.74,
  "buyAmount": 500.00,
  "bookedAt": "2026-11-17T22:30:00Z",
  "memo": "Bank conversion"
}
```

Item of the `Buy` above (other fields unchanged, [trade-fees.md](trade-fees.md) §8.1):

```json
{
  "type": "Buy",
  "cash": [
    { "accountId": "<account>", "customerId": "<customer>", "role": "Principal", "amount": -3044.6460, "currency": "NZD", "fee": null },
    { "accountId": "<account>", "customerId": "<customer>", "role": "Fee", "amount": -16.8301, "currency": "NZD",
      "fee": { "kind": "Brokerage", "scheduleId": null, "basisAmount": 1800.00, "amount": 9.95, "currency": "USD" } }
  ],
  "security": [
    { "accountId": "<account>", "customerId": "<customer>", "instrumentId": "<instrument>", "quantity": 10, "cost": 1809.9500, "costCurrency": "USD" }
  ],
  "fx": { "baseCurrency": "USD", "quoteCurrency": "NZD", "rate": 1.69147000, "rateAsOfDate": "2026-11-17" }
}
```

Bodies on a Custodial account do not change ([custody-modes.md](custody-modes.md) §8.1).

### 8.2 Errors

All are ordinary validation: 400 with `errors`, no problem `code` ([api-design.md](../api-design.md) §4.2 C1).

| Case | Response |
| --- | --- |
| External `Buy` / `Sell` without `security[0].price` | 400, `errors["Security[0].Price"]` |
| `price` `≤ 0` or with more than 8 decimals (either mode) | 400, `errors["Security[0].Price"]` |
| `fee` `< 0` (either mode), or more decimals than the quote currency (External) | 400, `errors.Fee` |
| `fxRate` missing when currencies differ, present when they match, `≤ 0` or more than 8 decimals (External) | 400, `errors.FxRate` |
| `price`, `fee` or `fxRate` on a Custodial `Buy` / `Sell` | 400, `errors["Security[0].Price"]`, `errors.Fee`, `errors.FxRate` |
| External conversion with one amount | 400, `errors.SellAmount` or `errors.BuyAmount` |
| External conversion with `expectedRate` | 400, `errors.ExpectedRate` |
| External conversion whose rate rounds to 0 or overflows | 400, `errors.BuyAmount` |
| Quote between two External Accounts | 400, `errors.FromAccountId` |

This is a breaking change for two kinds of caller: one that posts an External `Buy` / `Sell` without a price, and one that converts between External Accounts with one amount and `expectedRate`. Both now get 400. `webapi` and the Adviser Portal ship in the same deploy.

Cross-tenant or wrong-collection id → **404**, not 403, before any rule of this spec is evaluated.

Keep the resource catalog in [api-design.md](../api-design.md) §7.7 pointing here for these rules.

---

## 9. UI

Page contracts: [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (D22, D23) and [adviser-portal-fees.md](../adviser-portal/adviser-portal-fees.md) (F20, F21), amended and accepted together with this spec. The Adviser Portal BFF does not change.

**Adviser Portal**

- Buy / Sell form on an **External** account (`/accounts/:id/transactions/new`): a required "Price" input labelled with the instrument's quote currency ("Price per unit (USD)"), an optional "Fee" input in the same currency, and, when the instrument's quote currency differs from the Account currency, a required "FX rate" input labelled "1 USD = ? NZD". The inputs appear once an instrument is picked. The POST carries `security[0].price`, `fee` (left out when blank) and `fxRate`. The form does no cash or cost arithmetic; the posted transaction shows the legs.
- Buy / Sell form on a **Custodial** account: unchanged, no price, fee or rate input, and the help text "Brokerage is added by the server from the trade fee schedule." stays. On an External account that help text is not shown.
- Convert form on an **External** account (`/accounts/:id/convert`): no "I sell" / "I buy" toggle and no quote call. Two required inputs, "Sell (NZD)" and "Buy (USD)", each in its Account's currency. Once both are valid the form shows the implied rate as display text, "1 NZD = 0.5912 USD", rounded to 4 decimals. Review shows "Convert 845.74 NZD from <account> to 500.00 USD in <account> at 1 NZD = 0.5912 USD". Confirm posts both amounts, no `expectedRate`, with one `Idempotency-Key` per form open. "Booked at" stays required (D19). There is no 409 path.
- Convert form on a **Custodial** account: unchanged (live quote, `expectedRate`, re-quote on 409).
- The destination picker of the convert form is unchanged (D20 greys out the other custody mode).
- The account page and the transaction pages already show the rate line under the amount when the item has `fx`. An External `Buy` / `Sell` with a rate shows it the same way. No change.
- Fees pages (`/fees/trade` list and `/fees/trade/:id` detail): one line under the page title, "Schedules apply to trades on Custodial accounts. A trade on an External account carries the fee entered with it." The transaction detail already shows a `Fee` leg without a schedule link when `fee.scheduleId` is `null` (adviser-portal-fees, "Fees on ledger pages"); no change there.

**Customer Portal**

None. The Customer sees the legs and the Fee column as today. A rate line on `Buy` / `Sell` and the grouping by custody mode are later work ([cross-currency-trades.md](cross-currency-trades.md) §9, custody modes slice 5).

### 9.1 Seeds

No seed code changes. The `TestSeed` `Buy` on "Demo brokerage" (External) is built through `PostBuy` with the seeded price and no fee; it stays as it is, like the other posted rows (R21). The optional factory parameters of §5 leave its call unchanged. The Custodial demo account keeps its server-priced trades. The demo seed and the smoke seed post no trade.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `PostBuy` / `PostSell` with an entered fee: the §4.3 numbers (cost = gross + fee, Fee leg in the Account currency, fee detail with no schedule); entered fee 0 → no Fee leg; a fee that converts to 0 → no Fee leg; entered fee `< 0` → throws; a schedule and an entered fee → throws. With an `AppliedFxRate`: FX detail attached; a rate whose base is not the price currency, whose quote is not the Account currency, or whose rate differs from `fxRate` → throws. `PostFxConversionWithAmounts`: legs equal the amounts; rate `0.59119824` for §4.4; `0.00612400` for the JPY example; FX detail as-of date is the UTC date of `bookedAt` and source `Caller`; same currency, same Account, different custody modes, amount `≤ 0`, from `SUM < 0` → throws. `Reverse` copies a fee detail with no schedule and a `Caller` FX detail |
| Application.UnitTests | `ApplicableTradeFeeSchedule.DateOf` is the UTC date of the instant (it replaces the functional case `TradeFeeTradeTests.TheScheduleForTheTradesUtcDateApplies`, which needs a chosen booking instant that a Custodial account cannot have) |
| Application.FunctionalTests — External trades | Same-currency `Buy` with `price` and `fee` → 201 with the §4.3 legs, `fee.scheduleId: null` and `fx: null`; with no `InstrumentPrices` row for the instrument → still 201; with a tenant schedule that applies today and no `fee` → 201 and no Fee leg; `Sell` with `price` → 201, released cost by the average-cost rule; without `price` → 400 `errors["Security[0].Price"]` and nothing written; `price` 0 and with 9 decimals → 400; `fee` with 3 decimals on NZD → 400 `errors.Fee`; `fee` `−1` → 400. Two currencies: `Buy` without `fxRate` → 400 `errors.FxRate`; with `fxRate` → 201, §4.3 legs and cost, item `fx` USD / NZD / `1.69147` / the `bookedAt` date; with no `FxRates` row → still 201; same currency with `fxRate` → 400 `errors.FxRate`. Oversell and cash below 0 → 400 (existing rules). Reverse of each → 201, opposite legs, fee and FX details copied. Replay with the same body → same id; same key with another `price` → 409 |
| Application.FunctionalTests — Custodial trades | `price`, `fee` and `fxRate` on a Custodial `Buy` / `Sell` → 400 on their field and nothing written; a Custodial `Buy` still gets the server price and the schedule's fee |
| Application.FunctionalTests — External conversions | Both amounts → 201, legs equal the amounts, item `fx` rate `0.59119824`, `rateAsOfDate` the `bookedAt` date; no `FxRates` row for the pair → still 201; one amount → 400 on the missing field; `expectedRate` → 400; too many decimals on either amount → 400; insufficient cash → 400; reverse → 201 with the original amounts and FX detail. Quote between two External Accounts → 400 `errors.FromAccountId`; between External and Custodial → 400 `errors.ToAccountId` (existing). Between two Custodial Accounts: both amounts → 400 and no `expectedRate` → 400 (existing messages), the quote → 200 |
| Application.FunctionalTests — two tenants | Another tenant's External account or instrument on an External `Buy` with `price` → 404, not 400; another tenant's External Account on a two-amount conversion → 404 |
| Infrastructure.IntegrationTests | Script `0034` on a scratch database (as for `0031`): an insert into `TransactionFeeDetails` with kind 0 and `TradeFeeScheduleId` NULL is refused before the script and succeeds after it; existing rows are unchanged. The case `Insert_FeeDetail_BrokerageWithoutSchedule_IsRejected` of `TradeFeesSchemaTests` (run on the fully migrated database) becomes an insert that succeeds. EF round-trip: an External `Buy` with an entered fee and a rate reloads with a fee detail without schedule and a `Caller` FX detail |
| Adviser Portal | External account: Buy / Sell form shows Price and Fee after an instrument is picked, and FX rate only when currencies differ; the POST carries `price`, `fee` only when filled, and `fxRate`; a 400 on `Security[0].Price`, `Fee` or `FxRate` is shown at its input. Custodial account: no such inputs and none in the POST. Convert form on an External account: two amount inputs, no toggle, no quote request, implied rate text, the POST carries both amounts and no `expectedRate`. Convert form on a Custodial account: unchanged. Fees pages show the line of §9; the Buy / Sell help text only on a Custodial account |

Where the two-tenant tests live: every new rule runs in a handler after the Account lookup, so the cross-tenant cases are proved over HTTP in Application.FunctionalTests, next to the existing isolation tests of each route.

Assertions this spec replaces (the spec changed; the tests are not weakened): on an External account, "`price` on a `Buy` body → 400", "`fee` on a `Buy` body → 400", "missing price → 400", "missing FX pair → 400", "the schedule for the trade's date gives a Fee leg", and every reference-rate, `expectedRate`, staleness and quote assertion. Each still holds on a Custodial account, where the suite proves it (below). Also replaced: the schema case "a `Brokerage` fee detail without a schedule is rejected" (script `0034` drops that CHECK), "a quote between External Accounts without `bookedAt` → 200" in `FutureBookedAtTests` (now 400 `errors.FromAccountId`, R19), and the boundary-instant case of `TradeFeeTradeTests` (moved to Application.UnitTests).

Fan-out in the functional tests:

- **Moved to Custodial accounts, test-only (§12 commit 1).** The tests that prove server pricing, trade fee schedules and reference-rate conversions open Custodial accounts and stop sending `bookedAt`: the server-priced cases of `BuySellTests` (table price used, missing price, missing FX pair, `price` and `fee` refused), `TradeFeeTradeTests`, `TradeFeeReaderTests` (and the `FeeHttp.TradingAccountAsync` / `FeeHttp.PostTradeAsync` helpers), `FxConversionTests`, `FxConversionExpectedRateTests`, `FxConversionQuoteTests`, `FxConversionReadTests`, `FxConversionReverseTests` and `FxConversionIsolationTests`. A Custodial account is a `Brokerage` account, so the FX world of these suites opens Custodial `Brokerage` accounts in place of the shared External `Bank` accounts of `LegAccountsFixture`. Tests that relied on a chosen `bookedAt` (the rate as of a date, staleness, the schedule for a date) insert their `FxRates`, `InstrumentPrices` and schedule rows relative to today instead; the rate of the FX world is stored as of today, since a rate four days older than the server's date is stale. A Custodial account follows today's rules, so this commit passes before any production change.
- **Stay External, body changes with the rule (§12 commits 3 and 4).** `BuySellTests` (the remaining cases), `TransactionRulesTests`, `ReverseOrderTests` and any other External `Buy` / `Sell` add `price` (and `fxRate` when the currencies differ); the trades are posted by the private `PostSecurityAsync` helpers of `BuySellTests` and `ReverseOrderTests`, which gain a `price` parameter. `FxConversionRulesTests` stays External (a Custodial account refuses the `Opening` it posts) and sends both amounts. `BookedAtOffsetTests` and `FutureBookedAtTests` keep their External conversions and send both amounts with no `expectedRate`; the quote case "without `bookedAt` → 200" of `FutureBookedAtTests` becomes "→ 400 `errors.FromAccountId`" (R19), and its future-`bookedAt` quote case still gets `errors.BookedAt`.
- Unchanged: the Custody suites (they already use Custodial accounts for trades and conversions), the portfolio suites, Domain and integration tests that call the factories directly, and `TestSeed`.

---

## 11. Locked in this spec

No new ADR. [ADR 0020](../adr/0020-account-custody-mode.md) already says the caller supplies an External account's numbers; this spec makes it rules for `Buy`, `Sell` and `FxConversion`.

| Item | Lock |
| --- | --- |
| Source of a number | The custody mode of the leg accounts. External: the caller. Custodial: the server |
| External `Buy` / `Sell` | `price` required, `fee` optional, `fxRate` required when currencies differ. No market data, rate or schedule read |
| External `FxConversion` | Both amounts required, rate derived and rounded to 8 decimals, no `expectedRate`, no quote |
| Formulas | Today's `Buy` / `Sell` formulas and rounding with the caller's numbers |
| Records | Fee detail with no schedule; FX detail with source `Caller` and the `bookedAt` date |
| Custodial accounts | No change |
| Existing rows | No change, no back-fill |
| Script | `0034_entered_trade_fees.sql`: drop `CK_TransactionFeeDetails_BrokerageSchedule` |
| Routes, policies, problem codes, types, events | None added |

Confirmed when this spec was accepted (2026-11-18). The rules above are written with the first option of each:

1. **A cross-currency External trade takes `fxRate`, not a settlement amount** (R7). The rate is stored exactly as entered and the existing formulas apply. The alternative is a required `settlementAmount` (the principal in the Account currency, as on the contract note) with the rate derived from it, as R17 does for a conversion; the cash leg would then match the statement to the cent, but the fee would need its own conversion rule.
2. **The entered fee is in the instrument's quote currency** (R6), like the server fee, so cost stays gross + fee in one currency. The alternative is the Account currency, the amount the broker actually debited; cost would then need the fee converted back.
3. **An omitted fee means no fee** (R6). The schedule is never read on an External account. The alternative is to fall back to the schedule when `fee` is omitted, which keeps a server number on an External account.
4. **The quote route refuses External Accounts** (R19). The alternative is to keep it as a hint for the Adviser; the External convert form would not call it either way.
5. **An External trade with a rate stores an FX detail** (R10), ahead of [cross-currency-trades.md](cross-currency-trades.md) doing the same for server-priced trades. The alternative is to store no detail; the rate would then only be visible as cash ÷ cost.
6. **A cross-currency `Buy` is allowed on an External account** (R23). The cross-currency-trades draft rule that a manual `Buy` must be in the Account currency is limited to Custodial accounts.

Still open, not this slice:

- Read-model and report grouping by custody mode, and a note that External numbers are entered by the Adviser (custody modes slice 5)
- A rate line on `Buy` / `Sell` in the Customer Portal
- The rest of [cross-currency-trades.md](cross-currency-trades.md) for Custodial accounts
- Fees in the Account currency, or several fee kinds, on an External trade

---

## 12. Suggested commits

Only after `accepted`. The repository should build after each commit. Agents split each behaviour into red then green.

1. Test-only: the server-pricing, trade fee and reference-rate conversion suites move to Custodial accounts (§10 fan-out), with the `ApplicableTradeFeeSchedule.DateOf` unit test that replaces the boundary-instant case. No production change. Passes on its own.
2. Script `0034` with its integration test; Domain: entered fee and `AppliedFxRate` on `PostBuy` / `PostSell`, `PostFxConversionWithAmounts` (§5) + unit tests.
3. External `Buy` / `Sell` take `price`, `fee` and `fxRate` (R4–R14, R22) + functional tests, including two-tenant. The External `Buy` / `Sell` bodies in the other suites add `price` here.
4. External conversions take both amounts; the quote refuses External Accounts (R15–R20) + functional tests, including two-tenant. `BookedAtOffsetTests` and `FutureBookedAtTests` change body here.
5. Adviser Portal: Buy / Sell inputs on External accounts (D22) + frontend tests.
6. Adviser Portal: the External convert form (D23) and the fees pages (F20, F21) + frontend tests.
7. Repo docs: mark this file landed. (This file and the amendments in buy-sell, trade-fees, fx-conversions, cross-currency-trades, custody-modes, the two portal page contracts, domain-model, database-design, api-design and the glossary were accepted on 2026-11-18, before commit 1.)

Do not put read-model grouping, the rest of cross-currency-trades, or any change to Custodial accounts in these commits.
