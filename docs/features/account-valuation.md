---
title: Account valuation
status: accepted
phase: 3
language: en
owner: ""
created: 2026-11-26
last_updated: 2026-11-26
related:
  - ../valuation-roadmap.md
  - ../future-plans.md
  - market-data-history.md
  - instrument-prices.md
  - fx-rates.md
  - interest-run.md
  - posting.md
  - holdings.md
  - leg-accounts.md
  - accounts.md
  - custody-modes.md
  - corporate-actions.md
  - orders.md
  - fx-conversions.md
  - net-worth.md
  - ../known-issues/eodhd-nz-jp-coverage.md
  - ../domain-model.md
  - ../api-design.md
  - ../architecture.md
  - ../glossary.md
---

# Account valuation

Step 2 of [valuation-roadmap.md](../valuation-roadmap.md) §8. It covers roadmap item **V2** and writes the decisions **Q1** (a day is a UTC date), **Q4** (the age limit of a price or a rate), **Q8** (income not posted does not count) and **Q10** (a spec of its own) as rules. It reads the prices and rates that [market-data-history.md](market-data-history.md) stores.

A **valuation** is a read that values one Account for one day: the cash and the quantity of each holding at the end of that UTC day (the **position of the day**), the price and the FX rate as of that day, and each value in the instrument's quote currency and in the Account's currency. A holding that cannot be valued is listed with the reason, and the valuation says it is **incomplete**. Nothing is stored and no job runs.

New: route `GET /accounts/{id}/valuation?date=`; Domain function `AccountValuation.Compute` with `LedgerDay`, `ValuationRules` and `ValuationLineStatus`; Application services `AccountPositions` and `ValueAccount` and the query `GetAccountValuation`. No script, table, permission, problem code, domain event, `TransactionType`, port, job or setting. No portal page and no simulator change.

**Status is `accepted` (2026-11-26).** The eight points of §11 were confirmed as recommended.

---

## 1. Summary

| | Today | After this slice |
| --- | --- | --- |
| What an Account was worth on a past day | Not available. Net worth is now only | `GET /accounts/{id}/valuation?date=` answers for any day up to today |
| Cash on a past day | Worked out inside the interest run, for Custodial accounts | One rule for every Account: the cash legs booked before the day ended |
| Quantity on a past day | Not available. `Holdings` is the quantity now | The sum of the security legs booked before the day ended |
| A holding with no price | Net worth counts it as 0 | The line is not valued, with a status, and the valuation is incomplete |
| A price or rate that is old | Refused only where an order or an FX conversion needs it | Older than 4 days: the line is not valued |
| A value in the Account's currency | Not available: net worth is per currency | Each line has its market value in the quote currency, the rate used and the value in the Account's currency |
| The ex-date of a split | — | The quantity and the price are on the same side of the split (R18) |

The rule that distinguishes this slice: no invented number. Every figure in a valuation comes from a ledger leg, a stored price or a stored rate, and is shown with its date. What cannot be valued is said, not counted as 0.

### 1.1 Worked example

One Custodial account in NZD over ten days, Monday 2 March to Wednesday 11 March 2026. The figures are made up for the example; the tests use them as written. All times are UTC.

Three instruments: `AAA` quoted in NZD, `BBB` and `CCC` quoted in USD. Both currencies have 2 decimal places.

The ledger. The account was opened by a migration batch posted on Friday 27 February: cash 10,000.00, 1,000 `AAA`, 50 `BBB`, 200 `CCC`.

| # | `bookedAt` | Transaction | Cash legs | Security leg |
| --- | --- | --- | --- | --- |
| T1 | Tue 3 Mar 20:30 | `Buy` (order fill): 500 `AAA` at 2.50 | −1,250.00 and a fee of −15.00 | `AAA` +500 |
| T2 | Wed 4 Mar 02:00 | `InternalTransfer` in, entered by mistake | +2,000.00 | — |
| T3 | Thu 5 Mar 22:10 | `Sell` (order fill): 80 `CCC` at 41.00 USD, converted at 1.7200 | +5,641.60 and a fee of −20.00 | `CCC` −80 |
| T4 | Fri 6 Mar 01:15 | `Reversal` of T2 | −2,000.00 | — |
| T5 | Fri 6 Mar 12:01 | `Split` (entitlement posting): `BBB` 2 for 1, ex-date Thu 5 Mar | — | `BBB` +50 |

T1 was made at 09:30 on Wednesday morning in Auckland. It is in Tuesday's valuation, because a day is a UTC date (R2).

T5 is a hand-entered corporate action. It took effect when the first price dated from the ex-date was stored (corporate-actions R23): the bar of Thursday 5 March arrived with the price job of Friday 12:00, and the entitlement was posted a minute later.

Stored prices (`InstrumentPrices`, by `AsOfDate`). There is no bar on the weekend. `CCC` has no bar after Friday 6 March.

| | Mon 2 | Tue 3 | Wed 4 | Thu 5 | Fri 6 | Mon 9 | Tue 10 | Wed 11 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `AAA` (NZD) | 2.48 | 2.50 | 2.52 | 2.47 | 2.55 | 2.60 | 2.58 | 2.61 |
| `BBB` (USD) | 180.00 | 182.00 | 184.00 | 92.50 | 93.10 | 94.00 | 93.40 | 95.20 |
| `CCC` (USD) | 40.00 | 40.50 | 40.20 | 41.00 | 41.30 | — | — | — |

Stored rates USD → NZD (`FxRates`), one for every calendar day:

| Mon 2 | Tue 3 | Wed 4 | Thu 5 | Fri 6 | Sat 7 | Sun 8 | Mon 9 | Tue 10 | Wed 11 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1.7100 | 1.7150 | 1.7180 | 1.7200 | 1.7250 | 1.7250 | 1.7260 | 1.7300 | 1.7280 | 1.7310 |

The valuation of each day. Each holding cell is quantity × price = market value, then the value in NZD.

| Day | Cash | `AAA` | `BBB` | `CCC` | Total (NZD) | Complete |
| --- | --- | --- | --- | --- | --- | --- |
| Mon 2 | 10,000.00 | 1,000 × 2.48 = 2,480.00 | 50 × 180.00 = 9,000.00 USD → 15,390.00 | 200 × 40.00 = 8,000.00 USD → 13,680.00 | 41,550.00 | yes |
| Tue 3 | 8,735.00 | 1,500 × 2.50 = 3,750.00 | 50 × 182.00 = 9,100.00 USD → 15,606.50 | 200 × 40.50 = 8,100.00 USD → 13,891.50 | 41,983.00 | yes |
| Wed 4 | 10,735.00 | 1,500 × 2.52 = 3,780.00 | 50 × 184.00 = 9,200.00 USD → 15,805.60 | 200 × 40.20 = 8,040.00 USD → 13,812.72 | 44,133.32 | yes |
| Thu 5 | 16,356.60 | 1,500 × 2.47 = 3,705.00 | **100** × 92.50 = 9,250.00 USD → 15,910.00 | 120 × 41.00 = 4,920.00 USD → 8,462.40 | 44,434.00 | yes |
| Fri 6 | 14,356.60 | 1,500 × 2.55 = 3,825.00 | 100 × 93.10 = 9,310.00 USD → 16,059.75 | 120 × 41.30 = 4,956.00 USD → 8,549.10 | 42,790.45 | yes |
| Sat 7 | 14,356.60 | 3,825.00 (Friday's price) | 9,310.00 USD → 16,059.75 | 4,956.00 USD → 8,549.10 | 42,790.45 | yes |
| Sun 8 | 14,356.60 | 3,825.00 (Friday's price) | 9,310.00 USD → 16,069.06 | 4,956.00 USD → 8,554.06 | 42,804.72 | yes |
| Mon 9 | 14,356.60 | 1,500 × 2.60 = 3,900.00 | 100 × 94.00 = 9,400.00 USD → 16,262.00 | 4,956.00 USD → 8,573.88 (price 3 days old) | 43,092.48 | yes |
| Tue 10 | 14,356.60 | 1,500 × 2.58 = 3,870.00 | 100 × 93.40 = 9,340.00 USD → 16,139.52 | 4,956.00 USD → 8,563.97 (price 4 days old) | 42,930.09 | yes |
| Wed 11 | 14,356.60 | 1,500 × 2.61 = 3,915.00 | 100 × 95.20 = 9,520.00 USD → 16,479.12 | not valued: `StalePrice` (5 days old) | 34,750.72 | **no** |

What the example shows:

- **A reversal counts from its own `bookedAt`** (R5). Wednesday and Thursday include the 2,000.00 of T2. Friday does not. Wednesday is not corrected afterwards.
- **A weekend.** Saturday and Sunday use Friday's prices, which are 1 and 2 days old. The rate has a row of its own for each day, so Sunday's value differs from Saturday's by the rate alone.
- **The ex-date of the split** (R18). At the end of Thursday the ledger holds 50 `BBB`, because T5 was booked on Friday. Thursday's price, 92.50, is already the price after the split. 50 × 92.50 = 4,625.00 USD would make the account worth 36,479.00, half of the holding gone for one day. The valuation restates the quantity to 100 and shows both figures.
- **The age limit** (R11). `CCC` is valued at Friday's price up to Tuesday, when the price is 4 days old. On Wednesday it is 5 days old: the line is not valued, the total leaves it out and the valuation is incomplete.
- **Rounding** (R15). Tuesday 10 March, `CCC`: 4,956.00 × 1.7280 = 8,563.968, shown as 8,563.97.

---

## 2. Scope

**In**

- Domain: `LedgerDay` (the end of a UTC day); `AccountPosition`; `AccountValuation.Compute`, a function with no I/O; `ValuationRules` (the age limit); `ValuationLineStatus`
- Application: `AccountPositions` (the position of a day), `ValueAccount` (the position, the prices, the rates and the function), query `GetAccountValuation`
- HTTP: `GET /accounts/{id}/valuation`
- `AccountInterest` takes the end of a day from `LedgerDay`. No change of behaviour
- Tests listed in §10

**Out**

- A stored valuation, a valuation run, a series of days and funds under management per day (roadmap V3)
- Net worth on a past day, a fold into the Tenant's reporting currency, a chart and the Adviser Balances Report for a past date (V4). `GET /net-worth` is unchanged and still counts a missing price as 0 (net-worth R7)
- Cost, average cost and unrealised gain on a past day. They need ledger replay (roadmap §6)
- Income earned but not posted: daily accruals not yet posted, and a dividend between its ex-date and its payment (Q8)
- A valuation of several Accounts in one call (a Customer, a Book, a Tenant)
- A read of the position alone (`GET /accounts/{id}/holdings` for a date)
- A price or a rate entered by a caller, and any call to a source on the read path
- A second price source, and prices for NZX and Tokyo ([known issue](../known-issues/eodhd-nz-jp-coverage.md)): such a holding is not valued
- `TestSeed` history. It comes with V3
- Any portal page

---

## 3. Stories

1. As an Adviser I read what one of my Customer's accounts was worth on a past day, in the account's currency, with the price and the rate each figure used.
2. As a Customer I read the same for my own account.
3. As a reader of a valuation I can see when it is incomplete and which holding could not be valued, so that I never take a partial total for the whole.
4. As a developer I work a valuation out by hand from the ledger, `GET /instruments/{id}/prices` and `GET /fx-rates`, and get the figures the route returns.
5. As the platform I get the same valuation of a Custodial account for a day two or more days back every time I ask, so that roadmap V3 can store it.
6. As an Adviser I cannot read the valuation of another Adviser's Customer, and a firm cannot read another firm's (404).

---

## 4. Rules

### 4.1 Access

| ID | Rule |
| --- | --- |
| R1 | **Access.** `GET /accounts/{id}/valuation` takes `accounts.read` with the Account's scope, as `GET /accounts/{id}/holdings` does (accounts R1 and R18, holdings R19). A TenantAdmin reads any Account of the Tenant, an Adviser those of assigned Customers, a Customer their own, a SystemAdmin any Tenant's. Missing / dead Bearer → 401, never 302. Policy fail → 403. An Account of another Tenant, of an unassigned Customer or of another Customer, or a missing id → 404. `{id}` is the Account `PublicId`. |

### 4.2 The day and the position

| ID | Rule |
| --- | --- |
| R2 | **Valuation day.** A day is a UTC date (roadmap Q1), as everywhere on the platform ([utc-instants](../fixes/utc-instants.md)). Day `d` ends at 00:00 UTC of `d + 1`. A Transaction counts in day `d` when its `bookedAt` is before that instant. The Tenant's local date is not used. |
| R3 | **Cash of the day.** The sum of the cash legs on the Account, of every role, on Transactions booked before the day ended. This is the day balance of interest-run R16, now for every Account of either custody mode. It is not rounded. Reserved cash and unsettled cash count in full: the money is in the cash balance. |
| R4 | **Quantity of the day.** For each Instrument, the sum of the signed quantities of the security legs on the Account, on Transactions booked before the day ended: the **ledger quantity**. An Instrument whose sum is 0 is left out. Reserved quantity and unsettled quantity count in full, because the ledger books a trade when it is filled, not when it settles. `Holdings` is not read: it is the quantity now. |
| R5 | **As booked.** Every Transaction counts from its own `bookedAt`, a Reversal too. `ReversedByTransactionId` is not read. A day between a Transaction and its Reversal shows the original, and stays so. |
| R6 | **What is not in the position.** Only posted legs count. Not counted: a daily accrual not yet posted, a dividend between its ex-date and its payment, an open order, a fill not posted yet, a withdrawal not paid yet, a bank statement line in suspense. So a valuation understates an account that has earned income not yet paid (roadmap Q8). |
| R7 | **Any status.** The read does not look at the Account's status. An Account that is Closed today is valued for a day it was Open. For a day after it was closed the cash is 0 and there is no holding, because a close needs both (accounts R16). |
| R8 | **The past of each custody mode.** On a Custodial account the server sets `bookedAt` (custody-modes R10), so the position of a past day never changes. On an External account the caller sets it (custody-modes R11): a Transaction entered later with an earlier `bookedAt` changes the position of every day from that date on. A valuation of an External account is the ledger as it stands when it is read. Such an entry can also leave the cash or a quantity of a past day below 0. Cash is returned as it is. A quantity below 0 is not valued (R14). |

### 4.3 Price, rate and age

| ID | Rule |
| --- | --- |
| R9 | **Price.** `IMarketData.TryGetPriceDetail(instrument, d)`: the stored row with the latest `AsOfDate` on or before `d` (instrument-prices R6), with its date and source. It is the unadjusted close (market-data-history R7), in the Instrument's quote currency. An inactive Instrument is read like any other. |
| R10 | **Rate.** Only when the Instrument's quote currency is not the Account's currency: `IFxRate.GetRateDetail(quote currency, Account currency, d)`, the stored row of that directed pair with the latest `AsOfDate` on or before `d`, with its date and source. The rate reads "Account currency per 1 unit of the quote currency", the direction of a sell fill (buy-sell R12). The inverse pair is not used. The same currency needs no rate. |
| R11 | **Age limit.** A price or a rate is **fresh** for day `d` when `d` minus its `AsOfDate` is at most 4 calendar days (`ValuationRules.MaxAgeDays`), the limit of orders R10 and fx-conversions R10 (roadmap Q4). One limit for prices and rates. It covers a weekend with a holiday on each side. A longer closure of a market leaves its holdings not valued from the fifth day. |
| R12 | **No source.** The read uses stored rows only and never calls a price or rate source (instrument-prices R9, fx-rates R10). |
| R13 | **When a valuation can still change.** A row dated today or yesterday may be updated (market-data-history R5), and more Transactions may be booked today. So the valuation of today or of yesterday may differ on the next read. For a day two or more days back the rows are final: the valuation of a Custodial account changes only when a missing price or rate row is inserted later, or when a corporate action with an earlier ex-date is posted (R18). The response carries no "final" mark. Storing a valuation is roadmap V3. |

### 4.4 Lines and totals

| ID | Rule |
| --- | --- |
| R14 | **Status of a line.** Each Instrument of R4 is one line. Its status is the first row of the table below that applies. Only a `Valued` line has a `value`. |
| R15 | **Amounts.** `marketValue = Round(quantity × price, dp of the quote currency, AwayFromZero)`. `value = Round(marketValue × rate, dp of the Account's currency, AwayFromZero)`; with the same currency `value = marketValue`. `dp` is the currency's `DecimalPlaces`. Each figure is worked out from the figures the response shows, so a reader can check it by hand: 0.33333333 × 92.50 = 30.83333303 gives a market value of 30.83, and 30.83 × 1.7200 = 53.0276 gives a value of 53.03. |
| R16 | **Totals.** `holdingsValue` is the sum of the `value` of the `Valued` lines. `total = cash + holdingsValue`. `complete` is `true` when every line is `Valued`, and when there is no line. `unvaluedCount` is the number of lines that are not `Valued`. When `complete` is `false`, `holdingsValue` and `total` leave those lines out; they are not the value of the account. No line is given a value of 0. |
| R17 | **No cost.** A line has no cost, average cost or unrealised gain, and no reserved, available, unsettled or transferable quantity. Those are figures of now (holdings §8.1). |

Statuses of R14, in the order they are tested:

| Status | When | `price` | `marketValue` | `fx` | `value` |
| --- | --- | --- | --- | --- | --- |
| `NegativeQuantity` | The quantity of the day is below 0 (R8) | `null` | `null` | `null` | `null` |
| `NoPrice` | No price row on or before `d` | `null` | `null` | `null` | `null` |
| `StalePrice` | The price is not fresh (R11) | the row found | `null` | `null` | `null` |
| `PriceBeforeCorporateAction` | R18 | the row found | `null` | `null` | `null` |
| `NoRate` | Another currency, and no rate row of the pair on or before `d` | the row | set | `null` | `null` |
| `StaleRate` | Another currency, and the rate is not fresh (R11) | the row | set | the row found | `null` |
| `Valued` | None of the above | the row | set | the row, or `null` with the same currency | set |

### 4.5 The ex-date of a corporate action

| ID | Rule |
| --- | --- |
| R18 | **The same basis.** A split, a consolidation or a bonus issue changes the price and the quantity by one factor. The price changes with the source's date; the quantity changes when the platform posts the entitlement, which is later (corporate-actions R23–R26). This rule covers an **entitlement posting** on the Account: a `Split` or `Bonus` Transaction with source `CorporateAction`, not reversed, whose corporate action has origin `Manual`. `E` is the `exDate` of its corporate action detail and the factor is that of corporate-actions R3. The price of the line is on the new basis when its `AsOfDate` is on or after `E`. The ledger quantity is on the new basis when the posting was booked before the day ended. |
| | **Price new, quantity old:** the quantity is restated: `quantity = RoundDown(ledgerQuantity × factor, 8)`, the formula of corporate-actions R28, and the line is valued with it. The line shows `quantity` and `ledgerQuantity`. |
| | **Price old, quantity new:** the line is `PriceBeforeCorporateAction` and is not valued. This happens after "Apply now" (corporate-actions R20), when no price has arrived since the ex-date. |
| | **Both old or both new:** nothing to do; `quantity` equals `ledgerQuantity`. |
| | Several postings on one line are taken in the order of their ex-dates, then of their `bookedAt`. |
| R19 | **What R18 does not cover.** (a) A corporate action from the feed. Its ex-date is the simulator's date, not the platform's, and cannot be compared with an `AsOfDate`; its entitlement is posted in the same job run that reads the first price on the new basis (corporate-actions R8). (b) A `Split`, `Bonus` or `Scrip` entered by hand on an External account. It stores no ex-date; the caller books it at the date it took effect. (c) An Account that held the Instrument at the end of the ex-date and no longer when the corporate action took effect. It has no entitlement posting, and its ex-date is valued with the ledger quantity. (d) A dividend: it changes no quantity. |

### 4.6 The read

| ID | Rule |
| --- | --- |
| R20 | **Route.** `GET /accounts/{id}/valuation?date=yyyy-MM-dd` → 200 with the item of R21. `date` is required and is the only query parameter. Missing, or not a date → 400 `errors.Date`. A date after the platform's UTC date → 400 `errors.Date` (`A valuation can be read only for today or a past day.`). There is no earliest date: a day before the Account's first Transaction answers cash 0, no holding and `complete` `true`; a day before the first stored price of an Instrument answers a `NoPrice` line. Today answers the position so far and the latest prices. |
| R21 | **Item.** `{ accountId, date, currency, cash, holdings, holdingsValue, total, complete, unvaluedCount }`. `currency` is the Account's. A holding is `{ instrumentId, symbol, name, quantity, ledgerQuantity, status, price, marketValue, fx, value }`; `price` is `{ amount, currency, date, source }` and `fx` is `{ rate, date, source }`, each the stored row that was read. `holdings` is sorted by `symbol`, then `instrumentId`. Every caller who may read the Account sees every field. |
| R22 | **A read only.** It writes nothing, takes no lock, raises no domain event and takes no `Idempotency-Key`. |

Policies: none added. `accounts.read` reads the route.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `LedgerDay` | Static rule (new, `src/Domain/Valuations`) | `End(day)`: 00:00 UTC of the next day. `Of(instant)`: the UTC date. The one place that says which day a `bookedAt` belongs to (R2) |
| `AccountPosition` | Value (new) | A day, the cash, and one `(instrument, ledger quantity)` per Instrument with a quantity other than 0 (R3, R4) |
| `ValuationRules` | Static (new) | `MaxAgeDays = 4`; `IsFresh(asOfDate, day)` (R11) |
| `ValuationLineStatus` | Enum (new) | `Valued = 0`, `NoPrice = 1`, `StalePrice = 2`, `NoRate = 3`, `StaleRate = 4`, `PriceBeforeCorporateAction = 5`, `NegativeQuantity = 6` |
| `BasisChange` | Value (new) | One entitlement posting of R18: Instrument, `ExDate`, `BookedAt`, kind, `RatioNew`, `RatioHeld`. `Factor` is corporate-actions R3 |
| `AccountValuation` | Value (new) | The result: day, currency, cash, lines, `HoldingsValue`, `Total`, `Complete`, `UnvaluedCount`. `Compute(position, accountCurrency, decimalPlaces, prices, rates, basisChanges)` holds R11 and R14–R18. It reads no clock and no store |
| `Account`, `Transaction`, `Holding`, `Instrument` | Existing | No change |

```text
one line, day d
  quantity of the day  = sum of security legs booked before End(d)                (R4)
  price                = latest row with AsOfDate <= d                            (R9)
  basis changes        price new, quantity old -> quantity = RoundDown(q x f, 8)  (R18)
                       price old, quantity new -> PriceBeforeCorporateAction
  marketValue          = Round(quantity x price, dp(quote))                       (R15)
  value                = Round(marketValue x rate(quote -> account), dp(account))
total = cash + sum(value of Valued lines);  complete = every line Valued          (R16)
```

Invariants:

- A line has a `value` if and only if its status is `Valued`.
- `total` equals `cash` plus the `value` of every `Valued` line, to the last decimal.
- `quantity` differs from `ledgerQuantity` only on a line R18 restated.
- A price or rate more than 4 days older than the day never produces a value.

Domain events: none. A valuation is not an entity and is not stored.

[domain-model.md](../domain-model.md) §8.22, "Valuation of a day", lists these types.

---

## 6. Database

None. No script.

The position of a day is read from `Transactions` (`BookedAt`), `TransactionCashLegs` and `TransactionSecurityLegs` (`AccountId`). The read for one Account is one sum and one grouped sum. Whether a run over every account of a Tenant needs an index is a question for roadmap V3.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Service | `AccountPositions` (new) | `ReadAsync(accountId, day)` → `AccountPosition` (R3–R5), and the basis changes of the Account's lines (R18). It names the Account and its Tenant itself and does not depend on the tenant query filter, so that a `worker` run can call it later |
| Service | `ValueAccount` (new) | `ForDayAsync(account, day)`: the position, `IMarketData.TryGetPriceDetail` per line, `IFxRate.GetRateDetail` per quote currency, `ICurrencyCatalog` for the decimal places, then `AccountValuation.Compute`. A `FxRateNotFoundException` is caught and passed on as "no rate"; no number takes its place (fx-rates R7). The one place a valuation is worked out; roadmap V3 calls it |
| Query | `GetAccountValuation` (new) | R1, R20–R22: finds the Account as `GetAccountHoldings` does, checks `date`, calls `ValueAccount`, maps the item |
| Service | `AccountInterest` (existing) | Takes the end of a day from `LedgerDay`. Its query and its results do not change |

Where each rule lives:

| Rule | Where |
| --- | --- |
| Which day a `bookedAt` belongs to (R2) | `LedgerDay` |
| Cash and quantity of a day (R3–R5) | `AccountPositions` |
| Age, status, amounts, totals, basis (R11, R14–R18) | `AccountValuation.Compute` |
| Scope and the 404 (R1) | The Account lookup `GetAccountHoldings` already uses |
| `date` (R20) | The validator of `GetAccountValuation`, with the platform's clock |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/accounts/{id}/valuation` | `accounts.read` | 200 item | 400 / 401 / 403 / 404 |

### 8.1 Bodies

`GET /accounts/{id}/valuation?date=2026-03-05` (Thursday of §1.1):

```json
{
  "accountId": "<account publicId>",
  "date": "2026-03-05",
  "currency": "NZD",
  "cash": 16356.60,
  "holdings": [
    {
      "instrumentId": "<instrument publicId>",
      "symbol": "AAA",
      "name": "Alpha Limited",
      "quantity": 1500,
      "ledgerQuantity": 1500,
      "status": "Valued",
      "price": { "amount": 2.47, "currency": "NZD", "date": "2026-03-05", "source": "EODHD" },
      "marketValue": 3705.00,
      "fx": null,
      "value": 3705.00
    },
    {
      "instrumentId": "<instrument publicId>",
      "symbol": "BBB",
      "name": "Bravo Inc",
      "quantity": 100,
      "ledgerQuantity": 50,
      "status": "Valued",
      "price": { "amount": 92.50, "currency": "USD", "date": "2026-03-05", "source": "EODHD" },
      "marketValue": 9250.00,
      "fx": { "rate": 1.72, "date": "2026-03-05", "source": "Frankfurter" },
      "value": 15910.00
    },
    {
      "instrumentId": "<instrument publicId>",
      "symbol": "CCC",
      "name": "Charlie Corp",
      "quantity": 120,
      "ledgerQuantity": 120,
      "status": "Valued",
      "price": { "amount": 41.00, "currency": "USD", "date": "2026-03-05", "source": "EODHD" },
      "marketValue": 4920.00,
      "fx": { "rate": 1.72, "date": "2026-03-05", "source": "Frankfurter" },
      "value": 8462.40
    }
  ],
  "holdingsValue": 28077.40,
  "total": 44434.00,
  "complete": true,
  "unvaluedCount": 0
}
```

`?date=2026-03-11` (Wednesday of §1.1), the `CCC` line and the totals:

```json
{
  "holdings": [
    {
      "symbol": "CCC",
      "quantity": 120,
      "ledgerQuantity": 120,
      "status": "StalePrice",
      "price": { "amount": 41.30, "currency": "USD", "date": "2026-03-06", "source": "EODHD" },
      "marketValue": null,
      "fx": null,
      "value": null
    }
  ],
  "holdingsValue": 20394.12,
  "total": 34750.72,
  "complete": false,
  "unvaluedCount": 1
}
```

`date` is a date with no zone ([api-design.md](../api-design.md) §2). `quantity` and `price.amount` are JSON numbers with up to 8 decimal places; `cash` has up to 4; `marketValue`, `value` and `holdingsValue` are rounded by R15. `source` is the stored `Source` of the row (`EODHD`, `Simulator`, `Frankfurter`).

### 8.2 Errors

All refusals are ordinary validation (400 with `errors`, no problem `code`).

| Case | Response |
| --- | --- |
| No Bearer, or a dead one | 401, never 302 |
| An Account of another Tenant, of an unassigned Customer or of another Customer; a missing id | 404 |
| `date` missing or not a date; a date after today (UTC) | 400 `errors.Date` |

A holding that cannot be valued is not an error: the answer is 200 with `complete` `false`.

Keep the resource catalog in [api-design.md](../api-design.md) pointing here for these rules.

---

## 9. UI

None in this slice. No Adviser Portal, Customer Portal or Back Office page shows a valuation of a day.

`bff-adviser-portal` and `bff-customer-portal` already proxy the prefix `/accounts`, so the route can be reached through both by a signed-in user who may read the Account. No page calls it. The charts and the past-date views are roadmap V4.

### 9.1 Seeds and hosting

No change to `TestSeed`, `DevelopmentSmokeSeeder`, `webapi --seed-demo`, AppHost or the VPS. The functional tests write their own prices and rates.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `LedgerDay`: 23:59:59.9999999 belongs to the day, 00:00:00 to the next; an instant with an offset is taken in UTC. `AccountValuation.Compute`, one case per day of §1.1 with the position, prices and rates of the tables: every line figure, `holdingsValue`, `total`, `complete`. Rounding by R15, with the fraction of that rule and a currency with 0 decimal places. Each status of R14, and their order when two apply. The age limit at 4 and at 5 days, for a price and for a rate. No line → `complete` `true`. R18: price new and quantity old restates (Thursday of §1.1: 50 → 100); a fraction is rounded down to 8 decimals; a consolidation and a bonus use their factors; price old and quantity new → `PriceBeforeCorporateAction`; both old and both new leave the quantity; two postings on one line in ex-date order; a posting of another Instrument changes nothing |
| Application.FunctionalTests | Folder `Valuations/`. No Bearer → 401 not 302. An External account built through the API with the caller's `bookedAt`: the cash and the quantities of §1.1 day by day; a Transaction at 23:59:59 UTC is in the day and one at 00:00:00 is in the next; a `bookedAt` with an offset counts in its UTC day (T1); a Reversal counts from its own `bookedAt` (Wednesday, Thursday and Friday of §1.1); an Instrument sold to 0 is left out; a holding in another currency has `fx` and one in the Account's currency has `fx` `null`; the weekend uses Friday's price and each day's rate; `StalePrice`, `NoPrice`, `NoRate` and `StaleRate` each give `complete` `false` and a total without the line; a day before the first Transaction → cash 0, no holding, `complete` `true`; today is allowed; a Closed account is valued for a day it was Open; an entry with an earlier `bookedAt` changes the valuation of that past day; each 400 of R20. A Custodial account with an entitlement posting of a `Manual` corporate action, its dates moved in the database as the interest tests do: the ex-date is restated, and after "Apply now" with no new price the line is `PriceBeforeCorporateAction`. A Customer reads their own Account; a SystemAdmin reads an Account of any Tenant |
| Application.FunctionalTests — isolation | Two-tenant fixture: a TenantAdmin, an Adviser and a Customer of Tenant A with the id of Tenant B's Account → 404. An Adviser with the Account of an unassigned Customer → 404. A Customer with another Customer's Account → 404 |
| Application.FunctionalTests — interest | The existing `Interest/` tests stay green after `AccountInterest` moves to `LedgerDay` |

No 403 case: every role that reaches `webapi` holds `accounts.read`. No Infrastructure.IntegrationTests: there is no script and no mapping.

---

## 11. Locked in this spec

No new ADR.

| Item | Lock |
| --- | --- |
| Valuation day | A UTC date; a Transaction counts when booked before 00:00 UTC of the next day |
| Position | Cash = the cash legs of every role; quantity = the security legs; as booked; a quantity of 0 left out; any Account status; both custody modes |
| Price and rate | The stored row with the latest date on or before the day; the rate of the directed pair quote currency → Account currency |
| Age limit | 4 calendar days for a price and a rate |
| Not valued | `NoPrice`, `StalePrice`, `NoRate`, `StaleRate`, `PriceBeforeCorporateAction`, `NegativeQuantity`; `complete` `false`; never a value of 0 |
| Amounts | Market value rounded to the quote currency's decimal places; value from the rounded market value, rounded to the Account currency's; cash not rounded |
| Ex-date | The quantity is restated on the days a `Manual` corporate action's price is on the new basis and its entitlement is not booked yet |
| Route | `GET /accounts/{id}/valuation?date=` (`accounts.read`); `date` required, not after today |
| Scripts, permissions, problem codes, events, ports, jobs | None |

### Points to confirm

Each point was confirmed as recommended (2026-11-26) and is written into the rules above as stated here. The alternatives are kept for the record.

1. **A missing or old rate marks the line; it does not fail the read** (R14). Recommended: roadmap Q4 says "older, or none: unpriced and incomplete" for a price or a rate, and one line without a rate should not hide the rest of the account. Roadmap H5 ended with "a missing rate fails"; this spec follows Q4, and H5 was reworded. Alternative: the read answers an error when any rate is missing, as a posting does.
2. **An incomplete valuation still has a total** (R16). Recommended: `total` is the cash plus the lines that could be valued, and `complete` and `unvaluedCount` say that it is partial. A reader sees most of the account, and V3 can store the row with its mark (roadmap V3, "stored with its mark"). The risk is a consumer that reads `total` and not `complete`. Alternative: `holdingsValue` and `total` are `null` when `complete` is `false`.
3. **Rounding line by line, each figure from the figures shown** (R15). Recommended: the market value is rounded to the quote currency's decimal places, the value is the rounded market value times the rate, and the totals are sums of the rounded lines. A person can check every figure with the response alone, and the lines add up to the total exactly. Alternative: carry full precision to the end and round only the total; the lines then do not add up.
4. **The ex-date of a split is valued by restating the quantity** (R18). Recommended. The price job stores a US bar the day after its date, so a hand-entered split is booked one UTC day after its ex-date. Without a rule, every holder's ex-date is valued with the new price and the old quantity: a quarter of the true value on the day of a 4 for 1 split, and that day would be a fee base in roadmap step 4. Alternative A: the line is not valued on such a day (`PriceBeforeCorporateAction` for both directions), which is simpler and leaves every split with an incomplete day. Alternative B: no rule, and the wrong day is a known issue.
5. **A price from before a corporate action the quantity already includes is not used** (R18). Recommended: after "Apply now" no price on the new basis exists, and the old price times the new quantity is wrong by the factor. Alternative: divide the old price by the factor, which invents a price.
6. **Any Account status, and no earliest date** (R7, R20). Recommended: the roadmap asks that an account Closed today is valued for a day it was Open, and the ledger answers every other case by itself (0 and empty). Alternative: 400 for a day before the Account was created, which an External account's past `bookedAt` would break.
7. **An External account is valued from the ledger as it stands, and a negative quantity is not valued** (R8, R14). Recommended: roadmap Q6 values an External account on read, and a backdated entry is how such an account is kept. Alternative: External accounts get no valuation until period close.
8. **`date` is required, today is allowed, and a Customer may read** (R1, R20). Recommended: a caller always says which day it means; today answers the position so far, which V3 never stores; the route shows nothing a Customer cannot already work out from their holdings and transactions. Alternative: default to today when `date` is missing; refuse today; staff only.

### Found while writing, not changed here

- **corporate-actions R23** says that with the first price dated from the ex-date "the quantity and the price a holding is valued at change together". They change within a minute of each other on the clock, but not on the same valuation day when the bar arrives on the next UTC day, which is the usual case for a US instrument. R18 handles it for a valuation. `GET /net-worth` reads now and is not affected.
- **Trades between an ex-date and the posting of the entitlement.** A fill booked in that window is on the new basis in the market and is multiplied again by the entitlement (corporate-actions R25 takes the holding at that moment; the Adviser Portal warns about it, D66). R18 restates such a day with the same factor and inherits the error. It is a gap of the corporate action flow, not of the valuation.
- **A holding of an inactive Instrument goes stale.** `instrument-price-sync` does not fetch an inactive Instrument (instrument-prices R3), and an Instrument may be disabled while it is held (holdings R7). Five days later its line is `StalePrice` and every valuation of the account is incomplete. A fix would be to keep fetching an Instrument that any Account still holds.
- **Net worth and a valuation can disagree today.** `GET /net-worth` counts a holding with no price as 0 and uses a price of any age (net-worth R7). The valuation of the same account for today says incomplete. Roadmap V4 decides whether net worth follows.
- **London shares in pence** (market-data-history §11) is still unchecked. If EODHD quotes them in pence, the valuation of a `GBP` holding is 100 times too high.
- **A corporate action from the feed around midnight UTC.** The simulator's job posts the entitlement and then reads the price. A run that starts before 00:00 and reads the price after it leaves one day with the new quantity and the old price. R19 (a) does not cover it.

Still open, not this slice:

- Storing a valuation, the daily run, the order of that run after `corporate-action-apply`, and `TestSeed` history (roadmap V3)
- Net worth on a past day, the fold into the reporting currency, charts (V4)
- Cost and unrealised gain on a past day (ledger replay)
- Income earned but not posted as part of a value (roadmap step 5)
- NZX and Tokyo prices ([known issue](../known-issues/eodhd-nz-jp-coverage.md))

---

## 12. Suggested commits

The repository should build after each commit. Agents split each behaviour into red then green.

1. `LedgerDay` (R2) + Domain unit tests. Refactor: `AccountInterest` takes the end of a day from it.
2. `AccountValuation.Compute`: valued lines, amounts and totals (R9, R10, R15, R16) + Domain unit tests from §1.1.
3. The age limit and the statuses of a line that is not valued (R11, R14, R16) + Domain unit tests.
4. The same basis on a corporate action (R18, R19) + Domain unit tests.
5. `AccountPositions`, `ValueAccount`, `GetAccountValuation` and `GET /accounts/{id}/valuation` (R1, R3–R8, R12, R20–R22) + functional and isolation tests.
6. The basis changes of a Custodial account in `AccountPositions` (R18) + functional tests.
7. Repo docs: mark this file landed; valuation roadmap §8 step 2.

Do not put a table, a job, a series or a seeded history in these commits.

### Changes to other docs

Made when this spec was accepted (2026-11-26):

[interest-run.md](interest-run.md) (R16: the day balance is the cash of the position of a day, R2 and R3 here; no rule changes), [holdings.md](holdings.md) (§2 Out "snapshots" and a note under R14: the quantity of a past day is read from the security legs), [posting.md](posting.md) (a note that the cash of a past day is read by `bookedAt`), [corporate-actions.md](corporate-actions.md) (R23: the ex-date in a valuation, R18 here), [fx-rates.md](fx-rates.md) (R7: a valuation catches a missing pair and marks the line), [known-issues/eodhd-nz-jp-coverage.md](../known-issues/eodhd-nz-jp-coverage.md) (its effect on a valuation: `NoPrice`, incomplete), [api-design.md](../api-design.md) (the resource row and the change log), [domain-model.md](../domain-model.md) (the valuation of a day), [architecture.md](../architecture.md) (the two services), [glossary.md](../glossary.md) (the review terms become accepted; "Day balance" covers every Account), [features/README.md](README.md) row status, and [valuation-roadmap.md](../valuation-roadmap.md) (H5 reworded by point 1, §5 V2, §8).
