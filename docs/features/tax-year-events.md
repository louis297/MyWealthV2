---
title: Tax year events in NZD
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-28
last_updated: 2026-11-28
related:
  - ../roadmaps/fif-roadmap.md
  - ../roadmaps/valuation-roadmap.md
  - ../roadmaps/custodial-roadmap.md
  - ../future-plans.md
  - instrument-tax-attributes.md
  - account-valuation.md
  - valuation-run.md
  - market-data-history.md
  - fx-rates.md
  - instrument-prices.md
  - posting.md
  - holdings.md
  - buy-sell.md
  - trade-fees.md
  - external-caller-numbers.md
  - cross-currency-trades.md
  - split.md
  - corporate-actions.md
  - dividends.md
  - external-income.md
  - in-kind-transfers.md
  - migration-batches.md
  - adjustments.md
  - custody-modes.md
  - custody-mode-grouping.md
  - net-worth.md
  - customers.md
  - interest-run.md
  - bff-adviser-portal.md
  - bff-customer-portal.md
  - ../fixes/utc-instants.md
  - ../domain-model.md
  - ../api-design.md
  - ../architecture.md
  - ../glossary.md
---

# Tax year events in NZD

Step 2 of [fif-roadmap.md](../roadmaps/fif-roadmap.md) §9. It covers roadmap item **TR2** and writes the decisions **Q2** (the ordering key), **Q3** (the tax year in New Zealand time), **Q4** (the rate that turns an amount into NZD), **Q5** (the price and rate of the opening and closing value), **Q7** (what one interest is) and, as far as an event needs them, **Q8** (a holding that arrives without a purchase) and **Q10** (trade fees) as rules. It reads the tax attributes of [instrument-tax-attributes.md](instrument-tax-attributes.md) and answers the question its R23 left here.

The ledger is kept per Account, in the Account's currency, by UTC day, as it was booked. The tax rules look at one person, per company, in NZD, by the New Zealand tax year, at what happened. This slice is the read that turns the first into the second. For one Customer and one tax year it answers, per Instrument: the quantity and the market value at the start and at the end of the year, and between them an ordered list of **tax events**, each with a quantity and an NZD amount. Nothing is stored and no job runs.

The slice computes no tax. The cost in NZD and the threshold test (TR3), FIF income (TR4) and the report (TR5) compute over its result.

New: route `GET /users/customers/{customerId}/tax-years/{taxYear}`; Domain types `TaxYear`, `TaxEvent`, `TaxEventKind`, `TaxHolding`, `CustomerTaxYear` and the functions `TaxLedger.Build` and `CustomerTaxYear.Compute`; Application service `CustomerTaxLedger` and query `GetCustomerTaxYear`; `AccountPosition` carries the instant it was cut at, and `ValueAccount` can value a position it is given. No script, table, permission, problem code, domain event, `TransactionType`, port, job or setting. No write changes. No portal page and no simulator change.

**Status is `accepted` (2026-11-28).** The seventeen points of §11 were confirmed on 2026-11-28, every one as recommended, and are written into the rules. The changes to the docs listed in §12 were made when it was accepted. It is a build contract. Landed 2026-11-28.

Notes from landing:

- R30: the messages are `TaxYear must be a whole number.` (also for an empty value or one too large for an integer), `TaxYear must be 2009 or later.` and `Tax year {Y} can be read from 2 April {Y}.`, all at `errors.TaxYear`. They are checked before the Customer is looked up, so a refused `taxYear` is 400 for any `customerId`.
- R31: `sequence` counts the events of one tax holding, 1 for its first event of the year. Holdings are sorted by `instrument.symbol` with an ordinal comparison, then by `instrument.id`.
- R31: `ledger` is `{ currency, principal, fee }` on an acquisition or a disposal and `{ currency, gross, foreignTax, rwt, nrwt, imputationCredit }` on a dividend, each a positive figure in the Account's currency; `null` on every other kind.
- R14: a departure is valued as the quantity that left, so its `amount` is a positive market value while its `quantity` stays negative. An arrival or a departure is valued as a position of one line on the UTC date of its `bookedAt`, with no basis change.
- R18: an NZD leg used as booked is also rounded to NZD's places, which changes no figure and gives every NZD figure NZD's places on the wire.
- R24: a comparable quantity is cut toward zero at 8 decimal places, as account-valuation R18 restates a quantity.
- R15: pairing is in the order of R8 over the departures, as written: with two departures and one arrival, the first departure takes the arrival, even when the second is nearer to it.
- R6, R7: `CustomerTaxLedger` reads no reversed Transaction and no Reversal; `TaxLedger.Build` leaves them out again, so the Domain rule does not depend on the query.
- §10: in the worked example through the API, A1 is opened as an External account so that its trades, split and stock in can be dated, and is made Custodial after them; its two dividends are written with `Transaction.PostDividend`, because a reinvested dividend is never entered by hand.

The tax statements in this spec were read again from Inland Revenue on 2026-11-28 (§14). The Income Tax Act refused automated reads again; §14 says which rules rest on the guide alone. Nothing here is tax advice.

---

## 1. Summary

| | The ledger | This read |
| --- | --- | --- |
| Whose | One Account | One Customer, over all their Accounts of both custody modes (roadmap T1) |
| Of what | A holding per `(Account, Instrument)` | A **tax holding**: one Instrument as the Customer holds it over all their Accounts (roadmap Q7) |
| Which period | A UTC day | A tax year: 1 April to 31 March in New Zealand time, which is 11:00 UTC on 31 March to 11:00 UTC on the next 31 March |
| Which currency | The Account's, and the quote currency for cost | NZD |
| A Transaction and its Reversal | Both count, each from its own `bookedAt` | Both are left out |
| A holding moved between two Accounts of the Customer | A `StockOut` and a `StockIn`, with nothing that links them | One **move**. The Customer's quantity does not change |
| A split | A change of quantity on the day it is booked | A reorganisation, and each earlier quantity of the year also restated to the basis of the year's end |
| What cannot be worked out | — | Said, per tax holding. The result is **incomplete**. No number is put in its place (roadmap T5) |

The rule that distinguishes this slice: it says what happened, in the terms of the tax rules, and nothing about what tax follows. Every figure is a ledger leg, a stored price or a stored rate, shown with its date.

### 1.1 Worked example

Roadmap T12 asks for a worked example before the spec. The figures are made up; the tests use them as written. All times are UTC.

One Customer, a New Zealand resident, with two Accounts: **A1**, Custodial, in USD; **A2**, External, in NZD. One Instrument, `AAA`, quoted in USD, issuer country `US`, FIF treatment `Attributing` from tax year 2025. USD and NZD have 2 decimal places.

Tax year 2025 runs from 31 March 2024 11:00 to 31 March 2025 11:00. Tax year 2026 runs from 31 March 2025 11:00 to 31 March 2026 11:00.

The ledger:

| # | `bookedAt` | Account | Transaction | Cash legs | Security leg |
| --- | --- | --- | --- | --- | --- |
| T1 | 10 Mar 2025 21:00 | A1 | `Buy` (order fill): 100 at 50.00 USD | −5,000.00 and a fee of −5.00 USD | +100 |
| T2 | 31 Mar 2025 09:30 | A2 | `Buy` by hand: 40 at 52.00 USD, the caller's rate 1.7500, fee 4.00 USD | −3,640.00 and a fee of −7.00 NZD | +40 |
| T3 | 31 Mar 2025 20:15 | A1 | `Sell` (order fill): 30 at 53.00 USD | +1,590.00 and a fee of −5.00 USD | −30 |
| T4 | 20 May 2025 02:10 | A1 | `Dividend` (corporate action): 70 shares at 0.50 USD, tax source `Foreign`, foreign tax 15 % | +35.00, tax −5.25 (`ForeignWithholding`) and −6.30 (`Rwt`) USD | — |
| T5 | 3 Jun 2025 04:00 | A2 | `Buy` by hand: 500 at 55.00 USD, the caller's rate 1.7500, entered by mistake | −48,125.00 NZD | +500 |
| T6 | 5 Jun 2025 01:00 | A2 | `Reversal` of T5 | +48,125.00 NZD | −500 |
| T7 | 1 Jul 2025 00:00 | A2 | `Split` by hand, 2 for 1: new total 80 | — | +40 |
| T8 | 2 Jul 2025 12:01 | A1 | `Split` (entitlement posting), 2 for 1, ex-date 1 Jul | — | +70 |
| T9 | 15 Sep 2025 02:30 | A1 | `Dividend` (corporate action) with a reinvestment: 140 shares at 0.25 USD, foreign tax 15 %, 0.8 new shares at 28.00 USD | +35.00, tax −5.25 and −6.30, reinvestment −22.40 USD | +0.8 |
| T10 | 10 Nov 2025 00:00 | A2 | `StockOut` by hand: 80, sent to the firm's custodian | — | −80 |
| T11 | 14 Nov 2025 03:20 | A1 | `StockIn` (in-kind transfer in, `changesBeneficialOwner` `false`): 80 | — | +80 |
| T12 | 2 Feb 2026 21:40 | A1 | `Buy` (order fill): 20 at 30.00 USD | −600.00 and a fee of −5.00 USD | +20 |

Stored prices of `AAA` (`InstrumentPrices`): 53.20 for 31 March 2025; 31.00 for 31 March 2026. Stored rates USD → NZD (`FxRates`): 1.7400 for 10 March 2025, 1.7600 for 31 March 2025, 1.6900 for 20 May 2025, 1.6800 for 15 September 2025, 1.7300 for 2 February 2026, 1.7450 for 31 March 2026.

**Tax year 2025.** Nothing was held at its start.

| # | From | Kind | Quantity | After | NZD |
| --- | --- | --- | --- | --- | --- |
| — | | Opening | | 0 | 0.00 |
| 1 | T1 | `Acquisition` | +100 | 100 | 5,000.00 × 1.7400 = 8,700.00, fee 5.00 × 1.7400 = 8.70, amount 8,708.70 |
| 2 | T2 | `Acquisition` | +40 | 140 | 3,640.00 and fee 7.00 as booked, amount 3,647.00 |
| — | | Closing | | 140 | A1: 100 × 53.20 = 5,320.00 USD × 1.7600 = 9,363.20. A2: 40 × 53.20 = 2,128.00 USD × 1.7600 = 3,745.28. Value 13,108.48 |

**Tax year 2026.**

| # | From | Kind | Quantity | After | Comparable | NZD |
| --- | --- | --- | --- | --- | --- | --- |
| — | | Opening | | 140 | 280 | 13,108.48, the closing value of 2025 |
| 1 | T3 | `Disposal` | −30 | 110 | 220 | 1,590.00 × 1.7600 = 2,798.40, fee 5.00 × 1.7600 = 8.80, amount 2,789.60 |
| 2 | T4 | `Dividend` | 0 | 110 | 220 | gross 35.00 × 1.6900 = 59.15, foreign tax 8.87, RWT 10.65 |
| 3 | T7 | `Reorganisation` | +40 | 150 | 220 | — |
| 4 | T8 | `Reorganisation` | +70 | 220 | 220 | — |
| 5 | T9 | `Dividend` | 0 | 220 | 220 | gross 35.00 × 1.6800 = 58.80, foreign tax 8.82, RWT 10.58 |
| 6 | T9 | `Acquisition` | +0.8 | 220.8 | 220.8 | 22.40 × 1.6800 = 37.63 |
| 7 | T10, T11 | `Move` | 0 | 220.8 | 220.8 | — |
| 8 | T12 | `Acquisition` | +20 | 240.8 | 240.8 | 600.00 × 1.7300 = 1,038.00, fee 5.00 × 1.7300 = 8.65, amount 1,046.65 |
| — | | Closing | | 240.8 | 240.8 | A1: 240.8 × 31.00 = 7,464.80 USD × 1.7450 = 13,026.08 |

The peak quantity of tax year 2026 is 280, at the opening. The tax holding is complete in both years. It carries the notices `Move` and `ExternalFigures`.

What the example shows:

- **The year is cut in New Zealand time** (R3). T2 was booked at 22:30 on 31 March in Auckland and is in tax year 2025. T3 was booked at 09:15 on 1 April in Auckland and is in tax year 2026. Both are on the UTC day 31 March 2025. The stored valuation of A1 for that day has 70 shares, 3,724.00 USD. The opening of tax year 2026 counts 100 shares on A1. Both are correct: they answer different questions.
- **An amount in NZD** (R18). T2 was paid from an NZD Account, so its cash legs are the amount. T1 was paid in USD and is converted at the stored rate of its day. The rate the Transaction itself used (T2's 1.7500) is already in its cash legs and is not read again.
- **A reversal is left out** (R7). T5 and T6 are not in the list. As booked, the Customer held 610 shares for two days, and the year would show a peak that never existed.
- **A split is restated** (R24). The split is booked on two days, because one Account is External and one is Custodial. Each Account's earlier quantities are multiplied by its own factor, so the comparable quantity stays 220 through both postings, and the opening of 140 is compared as 280.
- **A move is not a sale and a purchase** (R15). T10 and T11 are one `Move`. Without the rule the Customer would dispose of 80 shares on 10 November and acquire 80 on 14 November: a holding increased and decreased in the same year, which is what the quick sale adjustment looks for.
- **A reinvested dividend is two events** (R12): the dividend, and an acquisition for the reinvested amount.
- **The closing value of a year is the opening value of the next** (R22): the same instant, the same price row, the same rate row.

---

## 2. Scope

**In**

- Domain: `TaxYear` (the instants of a tax year); `TaxEvent`, `TaxEventKind`, `TaxHolding`, `CustomerTaxYear`; `TaxLedger.Build` and `CustomerTaxYear.Compute`, functions with no I/O; `AccountPosition` gains the instant it was cut at
- Application: service `CustomerTaxLedger`; query `GetCustomerTaxYear`; `ValueAccount.ForPositionAsync`
- HTTP: `GET /users/customers/{customerId}/tax-years/{taxYear}`
- Tests listed in §10

**Out**

- The cost in NZD of what is held, the cost a disposal removes, and the threshold test: roadmap TR3. This read passes on the cost an arrival carries and converts nothing of it (R14)
- FIF income under either method, the peak holding differential, the quick sale adjustment and the matching of a disposal to purchases: TR4. This read gives the quantities they need (R24, R25)
- The report, its PDF, the portal entry points and which years a report may be asked for: TR5
- The income summary, and `Interest` in a tax year: TR6
- Whether the FIF rules apply to the Customer. The read does not look at the tax profile (R2)
- An estimate for a tax year in progress (roadmap Q13; R4)
- Any stored result. A report keeps what it used (roadmap T9); this read is worked out each time
- A write of any kind: no field on a Transaction, no link between a `StockOut` and a `StockIn`, no ledger replay, no period close (roadmap §1.2)
- A security transfer between two Accounts as one Transaction. It stays out of [internal-transfers.md](internal-transfers.md); a move is recognised on read (R15)
- Inland Revenue's own rate tables and the rolling 12-month average (roadmap Q4)
- A second price source, and prices for NZX and Tokyo ([known issue](../known-issues/eodhd-nz-jp-coverage.md))
- A treatment that changes during a tax year, and a holder who becomes or stops being a resident during one (roadmap §7)
- A taxable bonus issue worked out from a `Scrip` (R13)
- Any portal page

---

## 3. Stories

1. As an Adviser I read, for one of my Customers and one tax year that has ended, what they held of each Instrument at the start and at the end, in NZD, and what happened in between.
2. As a Customer I read the same for myself.
3. As a reader I can see which tax holding is incomplete and why, and which figure rests on an assumption, so that I never take a partial answer for the whole.
4. As a developer I work the result out by hand from `GET /transactions`, `GET /instruments/{id}/prices` and `GET /fx-rates`, and get the figures the route returns.
5. As roadmap items TR3 and TR4 I get one ordered list of tax events per tax holding and compute over it with no database.
6. As an Adviser I cannot read the tax year of another Adviser's Customer, and a firm cannot read another firm's (404).

---

## 4. Rules

### 4.1 Access

| ID | Rule |
| --- | --- |
| R1 | **Access.** No new permission. `GET /users/customers/{customerId}/tax-years/{taxYear}` takes `accounts.read` with the Customer's scope, as `GET /users/customers/{id}/net-worth` does (net-worth R1, R3, R9). A TenantAdmin reads any Customer of the Tenant, an Adviser an assigned Customer, a Customer themselves, a SystemAdmin a Customer of any Tenant. Missing / dead Bearer → 401, never 302. Policy fail → 403. A disabled current tenant → 403. A Customer of another Tenant, an unassigned Customer, another Customer, or a missing id → 404. `{customerId}` is the Customer's `PublicId`. The route takes no `tenantId`. |
| R2 | **A read only.** It writes nothing, takes no lock, raises no domain event and takes no `Idempotency-Key`. It does not read the Customer's tax profile: an `Overseas` Customer gets the same answer as a `NewZealand` one, and what a report does with it is roadmap Q14. A Disabled Customer is read like any other. |

### 4.2 The tax year

| ID | Rule |
| --- | --- |
| R3 | **Instants (roadmap Q3).** Tax year `Y` starts at 00:00 on 1 April `Y − 1` in New Zealand and ends at 00:00 on 1 April `Y`. On every 1 April since 2008 New Zealand has been on daylight time, 13 hours ahead of UTC, so `TaxYear.Start(Y)` is 11:00 UTC on 31 March `Y − 1` and `TaxYear.End(Y)` is `TaxYear.Start(Y + 1)`. The two instants are a rule in Domain, the same for every Tenant, and not a setting. A Transaction is in tax year `Y` when `Start(Y) ≤ bookedAt < End(Y)`; `TaxYear.Of(instant)` gives the year. Only this slice and the later items of the roadmap cut time this way. A day stays a UTC date in every other feature (valuation-roadmap H2, [utc-instants](../fixes/utc-instants.md)). Source: S2 ¶17, ¶18 and its example. |
| R4 | **Which tax years.** `taxYear` is an integer from 2009 on, the first tax year whose start falls under the rule of R3: else 400 `errors.TaxYear`, `TaxYear must be 2009 or later.` The tax year must have ended and its last day must be final: 31 March `Y` is on or before the last final day ([valuation-run.md](valuation-run.md) R4), which is so from 2 April `Y` (UTC) on. Else 400 `errors.TaxYear`, `Tax year {Y} can be read from 2 April {Y}.` There is no estimate for a tax year in progress (roadmap Q13). A tax year in which the Customer had nothing answers an empty list and `complete` `true`. |

### 4.3 The walk

| ID | Rule |
| --- | --- |
| R5 | **Whose.** The read covers every Account of the Customer in the Tenant, of both custody modes and of any status, a Closed one too. A **tax holding** is one Instrument as the Customer holds it over all those Accounts (roadmap Q7, T1). Two Instruments of the catalog for one company are two tax holdings (instrument-tax-attributes R14). |
| R6 | **What is read.** Every Transaction with a security leg on one of those Accounts, and every `Dividend` with a cash leg on one of them, booked before `End(Y)`, from the Customer's first one on. A `Dividend` belongs to the Instrument of its dividend detail. The walk starts at the first Transaction because the quantity at the start of the year is a sum over everything before it. |
| R7 | **As it happened.** A Transaction that has been reversed (`ReversedByTransactionId` is set) and every `Reversal` are left out, wherever the two fall, in the same tax year or not. Tax looks at what happened, not at how it was booked. This differs from account-valuation R5, on purpose: a valuation of a day between the two shows the original; the tax year never does. |
| R8 | **Ordering key (roadmap Q2).** Transactions are walked by `bookedAt`, then by the internal `Transactions.Id`, which rises in the order of writing. Two Transactions with the same `bookedAt` are taken in the order they were written. The events of one Transaction follow the order of R9. The internal id is not on the wire: each event of the answer has a `sequence`, 1 for the first event of the year. On an External account the caller sets `bookedAt` (custody-modes R11), so a caller who needs another order of two entries of the same instant gives them different times. A later ledger replay uses the same key. |

### 4.4 Tax events

| ID | Rule |
| --- | --- |
| R9 | **Mapping.** Each Transaction of R6 that R7 does not leave out gives the tax events of the table below, one per security leg, on the tax holding of the leg's Instrument. A type that is not in the table gives none (R17). |
| R10 | **`Acquisition` and `Disposal`.** A `Buy` is an acquisition and a `Sell` is a disposal, on either custody mode and whoever wrote it. `quantity` is the security leg's, signed. The amounts come from the cash legs on the leg's Account: `principal` from the `Principal` leg and `fee` from the `Fee` legs, each turned into NZD by R18 and given as a positive figure. The `amount` of an acquisition is `principal + fee`; of a disposal, `principal − fee` (roadmap Q10). The two parts are given apart, so that TR4 can leave the fee out of one method if section EX 51 says so (§14). Sources: S2 ¶8 and ¶24 for the fee in the cost; S1 p.11 for "expenditure on buying". |
| R11 | **`Dividend`.** One event per `Dividend` Transaction, with `quantity` 0. `shares` is the quantity of the dividend detail. From the legs on the Account, each turned into NZD by R18: `gross` (the `Principal` leg), `foreignTax` (the `Tax` leg of kind `ForeignWithholding`), `rwt` and `nrwt` (the `Tax` legs of those kinds), each `0` when there is no such leg; and `imputationCredit` from the dividend detail, which is in the Account's currency (dividends R39). `amount` is the gross. `taxSource` is the detail's. The dividend is in the tax year of its `bookedAt`, the instant the money reached the account (dividends R37, external-income R15), not of its ex-date or its payment date. |
| R12 | **A reinvested dividend** gives two events, in this order: the `Dividend` of R11, and an `Acquisition` with the security leg's quantity, `principal` from the `Reinvestment` leg, `fee` `0` and `amount` equal to the principal. |
| R13 | **`Reorganisation`.** A `Split`, a `Bonus` or a `Scrip`, entered by hand on an External account or posted as an entitlement on a Custodial one, a consolidation too. `quantity` is the security leg's, signed. It has no amount. A hand `Scrip` is read as the ledger books it, a change of quantity that keeps the total cost (split R12), and the tax holding gets the notice `Scrip`: the platform cannot know whether the issue was a taxable dividend. A person who knows it was records it as an entered dividend and a `Buy` instead. |
| R14 | **`Arrival` and `Departure` (roadmap Q8).** An `OpeningHoldings` or a `StockIn` is an arrival; a `StockOut` is a departure; whoever wrote it (a person, a migration batch, an in-kind transfer), unless R15 makes it half of a move. `quantity` is the security leg's, signed. The `amount` is the market value of that quantity on the UTC date of `bookedAt`, in NZD: the value `AccountValuation.Compute` gives a position of that one line, with the price and the rate of account-valuation R9–R11 and the rounding of its R15, and no basis change. A line that is not `Valued` leaves the event with no amount and its status (R27). The tax holding gets the notice `ArrivalAtMarketValue` or `DepartureAtMarketValue`: the platform cannot know when the holding was acquired or what became of it. An arrival also passes on `carriedCost`: the cost of the security leg and its currency, and `acquiredOn` when the in-kind transfer detail has one. This read converts nothing of it; the cost it brings is TR3's rule. Source: S1 p.22 notes 7 and 8 for market value where no price was paid. |
| R15 | **`Move` (roadmap T1).** A departure and an arrival are one move when all of this holds: the same Instrument; two different Accounts of the Customer; the same quantity; their `bookedAt` at most 30 days apart, in either order; and neither has an in-kind transfer detail with `changesBeneficialOwner` `true`. Pairs are made in the order of R8 over the departures: each takes the arrival nearest in time that is not yet paired, then the earlier by R8. A departure or an arrival with no partner stays what R14 says. A move is one event with `quantity` 0 and no amount, placed at the later of the two `bookedAt`, and both of its legs count from that instant: until then the shares are on the Account they left. The Customer's quantity never dips. The event names both Accounts and both Transactions. The tax holding gets the notice `Move`: nothing in the ledger says that the two entries are one, the rule infers it. |
| R16 | **`Correction`.** An `Adjustment` with a security leg. `quantity` is the leg's `quantityChange`, signed, and may be 0 for a cost-only change. It has no amount: a correction is not a trade. It passes on `carriedCost` with the leg's `costChange`, signed. The tax holding gets the notice `Correction`. The cash part of an adjustment gives nothing. |
| R17 | **What gives no event.** A reversed Transaction and a `Reversal` (R7). Every type with cash legs only: `TransferIn`, `TransferOut`, `Interest`, `Opening`, `CloseOut`, `InternalTransfer`, `FxConversion`, and an `Adjustment` of cash. Cash in a foreign currency is not a FIF (S1 p.4). `Interest` is read by TR6. An open order, a fill not posted yet, an in-kind transfer not completed and a dividend not paid yet are not in the ledger and are not read (account-valuation R6). |

The mapping of R9:

| Ledger | Tax event | Quantity | NZD amount |
| --- | --- | --- | --- |
| `Buy` | `Acquisition` | + | `principal + fee` (R10) |
| `Sell` | `Disposal` | − | `principal − fee` (R10) |
| `Dividend` | `Dividend` | 0 | The gross, with the taxes and the credit (R11) |
| `Dividend` with a reinvestment | `Dividend`, then `Acquisition` | 0, then + | As above, then the reinvested amount (R12) |
| `Split`, `Bonus`, `Scrip` | `Reorganisation` | + or − | None (R13) |
| `OpeningHoldings`, `StockIn` | `Arrival`, or half of a `Move` | + | The market value of the day (R14) |
| `StockOut` | `Departure`, or half of a `Move` | − | The market value of the day (R14) |
| A departure and an arrival that R15 pairs | `Move` | 0 | None |
| `Adjustment` with a security leg | `Correction` | as entered | None (R16) |

### 4.5 An amount in NZD

| ID | Rule |
| --- | --- |
| R18 | **The actual rate of the day (roadmap Q4).** An amount starts from a cash leg, in the currency of the leg's Account. A leg of an NZD Account is used as it is. A leg of an Account in another currency `C` is converted: `Round(amount × rate, dp, AwayFromZero)`, where `rate` is the stored `FxRates` row of the directed pair `C` → NZD with the latest `AsOfDate` on or before the UTC date of the Transaction's `bookedAt` (account-valuation R10), and `dp` is NZD's `DecimalPlaces`. Each figure of an event is converted on its own, and a sum is a sum of the converted figures, so every figure can be checked from the figures shown. The event carries the rate row it used: `{ rate, date, source }`. The inverse pair is not used and no source is called (fx-rates R10). Source: S1 p.13, "the actual rate for the day for each amount derived or incurred or when determining market value". |
| R19 | **What is not used.** The FX detail of the Transaction: it is the rate from the quote currency to the Account's currency, the caller's on an External account, and it is already in the cash legs. Inland Revenue's mid-month and end-of-month tables, and the rolling 12-month average. The cost on a security leg, which is in the quote currency and is the average-cost figure of the holdings projection. |
| R20 | **One rule (roadmap T4).** R18 holds for every amount of every tax holding of every tax year, and the rate of R22 comes from the same table. There is no choice per Customer or per Tenant. |
| R21 | **No rate.** A rate row older than the age limit of account-valuation R11 (4 calendar days before the UTC date of `bookedAt`), or no row, leaves every figure of the event that needs it `null`, with the status `StaleRate` or `NoRate`. No number takes its place (fx-rates R7). The tax holding is incomplete (R27). |

### 4.6 Opening and closing

| ID | Rule |
| --- | --- |
| R22 | **Quantity and value at an instant (roadmap Q5).** The opening of tax year `Y` is taken at `Start(Y)` and the closing at `End(Y)`. The **quantity** of a tax holding at an instant is the sum over the Customer's Accounts of the security legs that count before it: R6 and R7 decide which Transactions, R15 when the legs of a move count. The **value** is worked out by `AccountValuation.Compute`, once per Account that has a quantity at the instant, with that Account's lines as the position, the position cut at the instant, the valuation day 31 March (`Y − 1` for the opening, `Y` for the closing), NZD as the currency, and the basis changes of that Account. So the price is the unadjusted close of the latest source date on or before 31 March, in the market's own dates; the rate is the stored row of the quote currency → NZD as of 31 March; the age limit, the statuses, the rounding and the ex-date rule are those of account-valuation R9–R11 and R14–R18. The value of the tax holding is the sum of its lines' values. The closing of `Y` and the opening of `Y + 1` are the same instant, the same day and the same final rows (R4), so they are equal by construction. Sources: S1 p.11 and S2 ¶17, ¶18 for the value at the start and the end of the income year; S1 p.13 for the rate. |
| R23 | **Which tax holdings are valued, and which are listed.** The opening and the closing value are worked out for a tax holding whose treatment for the year is `Attributing`, `AttributingNoFdr` or `NotClassified` (R26). For `NotForeign`, `ExemptAsxListed` and `ExemptOther` no price and no rate is read: the status is `NotRequired`, and a missing NZX price never makes the result incomplete. A quantity of 0 has the value `0.00` and no line. A tax holding is listed when its quantity at the start is not 0 or it has an event in the year. |

### 4.7 Comparable quantity and peak

| ID | Rule |
| --- | --- |
| R24 | **Comparable quantity.** A reorganisation changes the number of shares and not what they are worth, so a quantity from before it cannot be compared with one from after. Each quantity of the year is therefore also given on the basis of the year's end. A reorganisation leg on an Account has a factor: that Account's quantity after the leg over its quantity before it. The comparable quantity of an Account at a point of the year is its quantity there, multiplied by the factor of each reorganisation leg on that Account between that point and `End(Y)`, multiplied first and divided after, to 8 decimal places. The comparable quantity of the tax holding is the sum over the Accounts. It is worked out per Account because one split is booked on each Account at its own time (§1.1). `comparableQuantity` is given for the opening and after each event. At the closing it equals the quantity. A reorganisation leg on an Account that holds nothing before it leaves no factor: the comparable quantities of the tax holding are `null` and it is incomplete (R27). |
| R25 | **Peak.** `peakQuantity` is the greatest comparable quantity of the year: of the opening and after every event. It is read after every event, not at the end of each day, so a purchase and a sale on one day show (S1 p.15, "the greatest shareholding in the year"). What TR4 does with it is TR4's. |

### 4.8 Classification, incomplete and notices

| ID | Rule |
| --- | --- |
| R26 | **Treatment.** Each tax holding carries `Instrument.FifTreatmentFor(Y)` and nothing else about the rows (instrument-tax-attributes R26), with what a report has to keep (its R28): the treatment, the tax year of the row it came from, the note and the issuer country. No tax holding is left out because of its treatment: an exempt or a New Zealand share is listed with its events, which TR6 reads. |
| R27 | **Incomplete (roadmap T5).** A tax holding is incomplete when a reason of the table below applies, and it lists every one that does. The result is `complete` when no tax holding is incomplete; `incompleteCount` says how many are. A figure that could not be worked out is `null`, never 0, and nothing is left out silently. An incomplete result is not an error: the answer is 200. |
| R28 | **Notices (roadmap T11).** A notice says that a figure is there and rests on an assumption or on somebody else's word. It does not make a tax holding incomplete. A tax holding lists each notice of the table below once. |
| R29 | **A dividend whose tax source disagrees with the issuer country.** This answers instrument-tax-attributes R23. A dividend with `taxSource` `NewZealand` on an Instrument whose issuer country is set and is not `NZ`, or with `Foreign` on one whose issuer country is `NZ`, is given as booked, and the tax holding gets the notice `DividendTaxSourceDisagrees`. Nothing is changed and nothing is refused: the two are statements of two people (dividends R3). An Instrument with no issuer country gets no such notice. |

Reasons of R27:

| Reason | When |
| --- | --- |
| `NotClassified` | The treatment for the year is `NotClassified` (instrument-tax-attributes R27) |
| `OpeningNotValued` | A value is required (R23) and a line of the opening is not `Valued` |
| `ClosingNotValued` | The same for the closing |
| `AmountMissing` | An event of the year has a figure that could not be worked out: `NoRate` or `StaleRate` (R21), or an arrival or a departure that is not `Valued` (R14) |
| `NegativeQuantity` | The quantity of the tax holding is below 0 at the opening or after an event. A backdated entry on an External account can cause it (account-valuation R8) |
| `NoComparableQuantity` | R24 found no factor for a reorganisation leg |

Notices of R28:

| Notice | When |
| --- | --- |
| `ExternalFigures` | A leg of an External account is in the opening quantity or in an event of the year: the figures are the caller's ([custody-mode-grouping](custody-mode-grouping.md)) |
| `ArrivalAtMarketValue`, `DepartureAtMarketValue` | An event of R14 in the year |
| `Move` | An event of R15 in the year |
| `Scrip` | A `Scrip` in the year (R13) |
| `Correction` | An event of R16 in the year |
| `DividendTaxSourceDisagrees` | R29 |

### 4.9 The read

| ID | Rule |
| --- | --- |
| R30 | **Route.** `GET /users/customers/{customerId}/tax-years/{taxYear}` → 200 with the item of R31. It has no query parameter and no paging: the answer is the whole year. `taxYear` that is not an integer → 400 `errors.TaxYear`; the other refusals are R4. |
| R31 | **Item.** `{ customerId, taxYear, from, to, currency, ledgerReadAt, holdings, complete, incompleteCount }`. `from` and `to` are the two instants of R3; `currency` is `NZD`; `ledgerReadAt` is the platform time the ledger was read (roadmap T9). A holding is `{ instrument, treatment, opening, closing, peakQuantity, events, complete, incomplete, notices }`, sorted by `instrument.symbol`, then `instrument.id`. `instrument` is `{ id, symbol, name, quoteCurrency }`. `treatment` is `{ fifTreatment, rowTaxYear, note, issuerCountry }`. `opening` and `closing` are `{ at, valuationDay, quantity, comparableQuantity, value, status, lines }` with `status` `Valued`, `NotValued` or `NotRequired`; a line is `{ accountId, custodyMode, quantity, ledgerQuantity, status, price, marketValue, fx, value }`, the fields of account-valuation R21 with `value` in NZD. An event is `{ sequence, kind, bookedAt, transactionId, accountId, custodyMode, quantity, quantityAfter, comparableQuantityAfter, amount, status, ledger, fx, price, principal, fee, dividend, carriedCost, move }`; a field that its kind does not have is `null`. `status` is `Stated` (NZD legs as booked), `Converted`, `MarketValue`, `None` (the kind has no amount), `NoRate`, `StaleRate`, `NoPrice` or `StalePrice`. `ledger` is the figures in the Account's currency that the NZD figures came from. §8.1 shows each shape. Every caller who may read the Customer sees every field. |
| R32 | **Still a ledger read.** The answer is the ledger as it stands at `ledgerReadAt`. On a Custodial account the server sets `bookedAt`, so a tax year that R4 allows no longer changes there, except when a missing price or rate row is inserted later, or a Transaction of the year is reversed later. On an External account a caller may enter a Transaction with an earlier `bookedAt` at any time, and the next read shows it (roadmap Q15). The read marks nothing as final. |

Policies: none added. `accounts.read` reads the route.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `TaxYear` | Value (new, `src/Domain/Tax`) | A tax year by its name. `Start`, `End`, `Of(instant)`, `OpeningDay` and `ClosingDay` (the two 31 March dates), `FirstReadable = 2009` (R3, R4). The one place that says which tax year a `bookedAt` belongs to |
| `TaxEventKind` | Enum (new) | `Acquisition = 0`, `Disposal = 1`, `Dividend = 2`, `Reorganisation = 3`, `Arrival = 4`, `Departure = 5`, `Move = 6`, `Correction = 7` |
| `LedgerFact` | Value (new) | What the walk needs of one Transaction: its ordering key, `bookedAt`, type, whether it is reversed, its legs with their Account, custody mode, role, amount and currency, and the dividend detail, the in-kind transfer detail and the adjustment detail where it has one. Application fills it; Domain reads no store |
| `TaxEvent` | Value (new) | One event of R9–R16: the kind, the key, the Instrument, the Account, the quantity, the figures in the Account's currency, and for a move its two legs |
| `TaxLedger` | Static (new) | `Build(facts)` → the tax events of the Customer in the order of R8, over every tax year: R7, R9–R17, the pairs of R15. It reads no clock and no store. TR3 walks the same list from its first event |
| `TaxHolding` | Value (new) | One Instrument in one tax year: the treatment, the opening and the closing, the events with their NZD figures and both quantities, the peak, the reasons and the notices |
| `CustomerTaxYear` | Value (new) | The result. `Compute(taxYear, events, treatments, rates, values, decimalPlaces)` holds R18, R21, R23–R29: it slices the events at the two instants, turns the amounts into NZD from the rate rows it is given, and takes the opening and closing lines it is given |
| `TaxIncompleteReason`, `TaxNotice` | Enums (new) | The values of the two tables of §4.8 |
| `AccountPosition` | Value (existing) | Gains `CutAt`: the instant the position was cut at. A position of a day has `LedgerDay.End(day)`, as today. `AccountValuation.Compute` compares the `BookedAt` of a basis change with `CutAt` in place of the end of the day (account-valuation R18). No valuation of a day changes |
| `Transaction`, `Holding`, `Instrument`, `Account` | Existing | No change |

```text
tax year Y            Start(Y) = 31 March (Y-1) 11:00 UTC      End(Y) = Start(Y+1)          (R3)
facts                 not reversed, not a Reversal, ordered by (bookedAt, id)               (R7, R8)
events                one per security leg, and one per Dividend                            (R9)
move                  departure + arrival: same instrument, two accounts, same quantity,
                      at most 30 days apart, no change of owner -> one event at the later   (R15)
NZD                   NZD leg as booked; else Round(leg x rate(C -> NZD, UTC date), dp)     (R18)
opening / closing     per Account: quantity cut at the instant, valued as of 31 March
                      in NZD by AccountValuation.Compute; summed                            (R22)
comparable quantity   per Account: quantity x the factors of its later reorganisations      (R24)
peak                  the greatest comparable quantity, after every event                   (R25)
```

Invariants:

- A reversed Transaction and a Reversal give no event and count in no quantity.
- `quantityAfter` of the last event of a year equals the closing quantity, and the closing quantity of tax year `Y` equals the opening quantity of `Y + 1`.
- The closing value of tax year `Y` equals the opening value of `Y + 1`.
- A `Dividend` and a `Move` never change a quantity.
- An event has an `amount` only when every figure it is made of could be worked out.
- A tax holding is `complete` if and only if it lists no reason.
- The comparable quantity at the closing equals the quantity at the closing.

Domain events: none. Nothing is an entity and nothing is stored.

[domain-model.md](../domain-model.md) gains §8.24, "Tax year of a Customer", with these types when this spec is accepted.

---

## 6. Database

None. No script.

The read uses `Transactions` (`BookedAt`, `ReversedByTransactionId`, `Type`), `TransactionCashLegs` and `TransactionSecurityLegs` (`AccountId`), `TransactionDividendDetails`, `TransactionInKindTransferDetails`, `TransactionCorporateActionDetails`, `Accounts`, `Instruments` with `InstrumentFifTreatments`, `InstrumentPrices` and `FxRates`. It reads the Transactions of one Customer, which the legs' `AccountId` indexes already reach. Whether a run over every Customer of a Tenant needs an index is a question for TR5.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Service | `CustomerTaxLedger` (new) | `ReadAsync(customer, taxYear)`: the Customer's Accounts; the facts of R6; `TaxLedger.Build`; the rate rows the events of the year need (`IFxRate.GetRateDetailAsync`, a missing pair caught as "no rate"); the positions at the two instants and their values through `ValueAccount.ForPositionAsync`; the market value of each arrival and departure of the year the same way; the treatments; then `CustomerTaxYear.Compute`. It names the Customer and the Tenant itself and does not depend on the tenant query filter, so that a `worker` run can call it later. The one place a tax year is worked out; TR3 to TR5 call it |
| Query | `GetCustomerTaxYear` (new) | R1, R4, R30–R32: finds the Customer as `GetCustomerNetWorth` does, checks `taxYear` with the platform's clock, calls `CustomerTaxLedger`, maps the item |
| Service | `ValueAccount` (existing) | Gains `ForPositionAsync(position, currency)`: the prices, the rates into `currency`, the decimal places and the basis changes of the position's Account, then `AccountValuation.Compute`. `ForDayAsync(account, day)` becomes the position of the day and the Account's currency passed to it. No valuation of a day changes |
| Service | `AccountPositions` (existing) | No change. It reads a position as booked (account-valuation R5), which is not the quantity of R22 |

Where each rule lives:

| Rule | Where |
| --- | --- |
| Which tax year a `bookedAt` belongs to (R3) | `TaxYear` |
| Which Transactions, in which order, as which events (R6–R17) | `CustomerTaxLedger` loads the facts; `TaxLedger.Build` decides |
| NZD amounts, comparable quantity, peak, reasons and notices (R18, R21, R23–R29) | `CustomerTaxYear.Compute` |
| Price, rate, age, rounding and the ex-date of a value (R14, R22) | `AccountValuation.Compute`, unchanged apart from `CutAt` |
| Scope and the 404 (R1) | The Customer lookup `GetCustomerNetWorth` already uses |
| `taxYear` (R4) | The validator of `GetCustomerTaxYear`, with the platform's clock |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/users/customers/{customerId}/tax-years/{taxYear}` | `accounts.read` | 200 item | 400 / 401 / 403 / 404 |

### 8.1 Bodies

`GET /users/customers/{customerId}/tax-years/2026` for §1.1, with four of its eight events:

```json
{
  "customerId": "<customer publicId>",
  "taxYear": 2026,
  "from": "2025-03-31T11:00:00Z",
  "to": "2026-03-31T11:00:00Z",
  "currency": "NZD",
  "ledgerReadAt": "2026-11-28T03:12:45Z",
  "holdings": [
    {
      "instrument": { "id": "<instrument publicId>", "symbol": "AAA", "name": "Alpha Inc", "quoteCurrency": "USD" },
      "treatment": { "fifTreatment": "Attributing", "rowTaxYear": 2025, "note": null, "issuerCountry": "US" },
      "opening": {
        "at": "2025-03-31T11:00:00Z",
        "valuationDay": "2025-03-31",
        "quantity": 140,
        "comparableQuantity": 280,
        "value": 13108.48,
        "status": "Valued",
        "lines": [
          {
            "accountId": "<A1 publicId>", "custodyMode": "Custodial",
            "quantity": 100, "ledgerQuantity": 100, "status": "Valued",
            "price": { "amount": 53.20, "currency": "USD", "date": "2025-03-31", "source": "EODHD" },
            "marketValue": 5320.00,
            "fx": { "rate": 1.76, "date": "2025-03-31", "source": "Frankfurter" },
            "value": 9363.20
          },
          {
            "accountId": "<A2 publicId>", "custodyMode": "External",
            "quantity": 40, "ledgerQuantity": 40, "status": "Valued",
            "price": { "amount": 53.20, "currency": "USD", "date": "2025-03-31", "source": "EODHD" },
            "marketValue": 2128.00,
            "fx": { "rate": 1.76, "date": "2025-03-31", "source": "Frankfurter" },
            "value": 3745.28
          }
        ]
      },
      "closing": {
        "at": "2026-03-31T11:00:00Z",
        "valuationDay": "2026-03-31",
        "quantity": 240.8,
        "comparableQuantity": 240.8,
        "value": 13026.08,
        "status": "Valued",
        "lines": [ { "accountId": "<A1 publicId>", "custodyMode": "Custodial", "quantity": 240.8, "ledgerQuantity": 240.8, "status": "Valued", "price": { "amount": 31.00, "currency": "USD", "date": "2026-03-31", "source": "EODHD" }, "marketValue": 7464.80, "fx": { "rate": 1.745, "date": "2026-03-31", "source": "Frankfurter" }, "value": 13026.08 } ]
      },
      "peakQuantity": 280,
      "events": [
        {
          "sequence": 1, "kind": "Disposal", "bookedAt": "2025-03-31T20:15:00Z",
          "transactionId": "<T3 publicId>", "accountId": "<A1 publicId>", "custodyMode": "Custodial",
          "quantity": -30, "quantityAfter": 110, "comparableQuantityAfter": 220,
          "amount": 2789.60, "status": "Converted",
          "ledger": { "currency": "USD", "principal": 1590.00, "fee": 5.00 },
          "fx": { "rate": 1.76, "date": "2025-03-31", "source": "Frankfurter" },
          "price": null, "principal": 2798.40, "fee": 8.80,
          "dividend": null, "carriedCost": null, "move": null
        },
        {
          "sequence": 2, "kind": "Dividend", "bookedAt": "2025-05-20T02:10:00Z",
          "transactionId": "<T4 publicId>", "accountId": "<A1 publicId>", "custodyMode": "Custodial",
          "quantity": 0, "quantityAfter": 110, "comparableQuantityAfter": 220,
          "amount": 59.15, "status": "Converted",
          "ledger": { "currency": "USD", "gross": 35.00, "foreignTax": 5.25, "rwt": 6.30, "nrwt": 0, "imputationCredit": 0 },
          "fx": { "rate": 1.69, "date": "2025-05-20", "source": "Frankfurter" },
          "price": null, "principal": null, "fee": null,
          "dividend": { "shares": 70, "taxSource": "Foreign", "gross": 59.15, "foreignTax": 8.87, "rwt": 10.65, "nrwt": 0, "imputationCredit": 0 },
          "carriedCost": null, "move": null
        },
        {
          "sequence": 3, "kind": "Reorganisation", "bookedAt": "2025-07-01T00:00:00Z",
          "transactionId": "<T7 publicId>", "accountId": "<A2 publicId>", "custodyMode": "External",
          "quantity": 40, "quantityAfter": 150, "comparableQuantityAfter": 220,
          "amount": null, "status": "None",
          "ledger": null, "fx": null, "price": null, "principal": null, "fee": null,
          "dividend": null, "carriedCost": null, "move": null
        },
        {
          "sequence": 7, "kind": "Move", "bookedAt": "2025-11-14T03:20:00Z",
          "transactionId": "<T11 publicId>", "accountId": "<A1 publicId>", "custodyMode": "Custodial",
          "quantity": 0, "quantityAfter": 220.8, "comparableQuantityAfter": 220.8,
          "amount": null, "status": "None",
          "ledger": null, "fx": null, "price": null, "principal": null, "fee": null,
          "dividend": null, "carriedCost": null,
          "move": { "quantity": 80, "fromAccountId": "<A2 publicId>", "fromTransactionId": "<T10 publicId>", "fromBookedAt": "2025-11-10T00:00:00Z", "toAccountId": "<A1 publicId>", "toTransactionId": "<T11 publicId>", "toBookedAt": "2025-11-14T03:20:00Z" }
        }
      ],
      "complete": true,
      "incomplete": [],
      "notices": [ "ExternalFigures", "Move" ]
    }
  ],
  "complete": true,
  "incompleteCount": 0
}
```

An arrival that was not paired, on an NZD External account:

```json
{
  "sequence": 4, "kind": "Arrival", "bookedAt": "2025-08-04T00:00:00Z",
  "transactionId": "<publicId>", "accountId": "<publicId>", "custodyMode": "External",
  "quantity": 25, "quantityAfter": 25, "comparableQuantityAfter": 25,
  "amount": 1310.40, "status": "MarketValue",
  "ledger": null,
  "fx": { "rate": 1.68, "date": "2025-08-04", "source": "Frankfurter" },
  "price": { "amount": 31.20, "currency": "USD", "date": "2025-08-04", "source": "EODHD" },
  "principal": null, "fee": null, "dividend": null,
  "carriedCost": { "amount": 700.00, "currency": "USD", "acquiredOn": null },
  "move": null
}
```

A tax holding that nobody classified, held all year, with no stored price on 31 March 2026:

```json
{
  "treatment": { "fifTreatment": "NotClassified", "rowTaxYear": null, "note": null, "issuerCountry": null },
  "closing": { "at": "2026-03-31T11:00:00Z", "valuationDay": "2026-03-31", "quantity": 12, "comparableQuantity": 12, "value": null, "status": "NotValued", "lines": [ { "status": "NoPrice", "price": null, "marketValue": null, "fx": null, "value": null } ] },
  "complete": false,
  "incomplete": [ "NotClassified", "ClosingNotValued" ],
  "notices": []
}
```

`from`, `to`, `at` and `bookedAt` are instants in UTC. `valuationDay` and the `date` of a price or a rate are dates with no zone ([api-design.md](../api-design.md) §2). A quantity is a JSON number with up to 8 decimal places. An NZD figure has NZD's decimal places. `transactionId` and `accountId` of a `Move` are those of its later leg.

### 8.2 Errors

All refusals are ordinary validation (400 with `errors`, no problem `code`).

| Case | Response |
| --- | --- |
| No Bearer, or a dead one | 401, never 302 |
| A Customer of another Tenant, an unassigned Customer, another Customer; a missing id | 404 |
| `taxYear` not an integer; below 2009; not ended, or its last day not final yet | 400 `errors.TaxYear` (R4, R30) |

A tax holding that is incomplete is not an error: the answer is 200 with `complete` `false`.

Cross-tenant or wrong-collection id → **404**, not 403. Keep the resource catalog in [api-design.md](../api-design.md) pointing here for these rules.

---

## 9. UI

None in this slice. No Adviser Portal, Customer Portal or Back Office page shows a tax year.

`bff-adviser-portal` and `bff-customer-portal` already proxy the prefix `/users`, so the route can be reached through both by a signed-in user who may read the Customer. No page calls it. The entry points are TR5's.

### 9.1 Seeds and hosting

No change to `TestSeed`, the demo seed, AppHost or the VPS. The functional tests write their own Transactions, prices and rates.

What the stored history must cover is not checked by the read. A tax year `Y` can be complete only when `MarketDataHistory:FloorDate` is on or before 27 March `Y − 1`, four days before the day of the opening value, and when the price source serves that far back (market-data-history R1–R3). In Development the floor date is 2026-01-01, so tax year 2026 has no opening price: a Customer who held nothing on 31 March 2025 reads it complete, and any other is incomplete with `OpeningNotValued`. Tax year 2027 is the first with an opening price there, readable from 2 April 2027.

---

## 10. Tests

| Project | Where |
| --- | --- |
| Domain.UnitTests | `Tax/TaxYearTests.cs`, `Tax/TaxLedgerTests.cs`, `Tax/TaxLedgerMoveTests.cs`, `Tax/CustomerTaxYearAmountTests.cs`, `Tax/CustomerTaxYearValueTests.cs`, `Tax/CustomerTaxYearComparableTests.cs`, `Tax/CustomerTaxYearReasonTests.cs`, with the worked example in `Tax/TaxWorkedExample.cs`; `Valuations/AccountValuationBasisTests.cs` for a position cut at an instant |
| Application.UnitTests | `TaxYears/GetCustomerTaxYearQueryValidatorTests.cs`: R4 with a fixed clock (1 April and 2 April) |
| Application.FunctionalTests | `TaxYears/GetCustomerTaxYearTests.cs`, `TaxYears/GetCustomerTaxYearAccessTests.cs` (two tenants), with the worked example in `TaxYears/TaxYearWorld.cs`; `Valuations/` unchanged and green |

| Project | Assert |
| --- | --- |
| Domain.UnitTests (`Tax/TaxYearTests`) | `Start` and `End` of 2026 and 2027 are 11:00 UTC on 31 March; `End(Y)` equals `Start(Y + 1)`; `Of` an instant one tick before, at and one tick after a start; 10:59 and 11:00 UTC on 31 March are two tax years |
| Domain.UnitTests (`Tax/TaxLedgerTests`) | §1.1 gives the events of both years in order; every row of the mapping of R9; a reversed Transaction and its Reversal give nothing, in one year and across a year end; two Transactions with one `bookedAt` in the order written; a reinvested dividend gives two events in order; `OpeningHoldings` with three legs gives three arrivals; a hand `Scrip`; an `Adjustment` of cash gives nothing and one of a holding gives a `Correction` |
| Domain.UnitTests (`Tax/TaxLedgerMoveTests`) | The move of §1.1; the arrival booked before the departure; 30 days and 30 days and one tick; another quantity, the same Account, another Instrument and `changesBeneficialOwner` `true` are not paired; two departures and one arrival pair the nearest; the legs of a move count from the later instant across a year end |
| Domain.UnitTests (`Tax/CustomerTaxYearTests`) | §1.1 to the cent, both years; the closing of 2025 equals the opening of 2026; an NZD leg is used as booked; each figure is converted on its own; `NoRate` and `StaleRate` leave `null` and `AmountMissing`; the comparable quantities and the peak of §1.1; a split booked on two Accounts on two days with a buy between them; `NoComparableQuantity`; `NegativeQuantity`; each reason and each notice; a `NotForeign` holding is `NotRequired` and complete with no price; `DividendTaxSourceDisagrees` both ways and none with no country |
| Domain.UnitTests (`Valuations/AccountValuationBasisTests`) | A position cut at an instant: a posting booked before the instant is on the new basis and one booked after it is not; the existing cases of a day are unchanged |
| Application.FunctionalTests (`TaxYears/GetCustomerTaxYearTests`) | §1.1 through the API on two Accounts, the item of §8.1; a trade at 10:59 and one at 11:00 UTC on 31 March fall in two tax years while both are in one valuation day; an arrival and a departure at market value; an entry backdated on the External account changes the next read; a Closed Account still counts; a Customer with nothing answers an empty list; `taxYear` 2008, a year not ended, 1 April of the year (not final yet) and 2 April |
| Application.FunctionalTests (`TaxYears/GetCustomerTaxYearAccessTests`) | No Bearer → 401 not 302; TenantAdmin, the assigned Adviser, the Customer and a SystemAdmin → 200; an unassigned Adviser → 404; another Customer → 404; a disabled current tenant → 403. Two-tenant fixture: a TenantAdmin and an Adviser of the other Tenant → 404, and no Transaction, Instrument or Account of the other Tenant is in any answer |
| Application.FunctionalTests (`Valuations/`) | Unchanged, green after `ValueAccount.ForDayAsync` calls `ForPositionAsync` |

No 403 case by role: every role that reaches `webapi` holds `accounts.read`. No Infrastructure.IntegrationTests: there is no script and no mapping.

---

## 11. Locked in this spec

No new ADR.

| Item | Lock |
| --- | --- |
| Tax year | 11:00 UTC on 31 March to 11:00 UTC on the next 31 March; from 2009; readable from 2 April of its year |
| Tax holding | One Instrument over every Account of the Customer, both custody modes, any status |
| Walk | Reversed Transactions and Reversals left out; ordered by `bookedAt`, then `Transactions.Id` |
| Events | `Acquisition`, `Disposal`, `Dividend`, `Reorganisation`, `Arrival`, `Departure`, `Move`, `Correction`, by the mapping of R9 |
| Move | Same Instrument, two Accounts, same quantity, at most 30 days apart, no change of owner stated; one event at the later instant |
| NZD | An NZD cash leg as booked; any other leg at the stored rate of the UTC date of `bookedAt`, with the age limit of a valuation; principal and fee apart |
| Opening and closing | The quantity at the instant; the price and the rate as of 31 March; `AccountValuation.Compute` per Account in NZD; only for attributing and not classified holdings |
| Comparable quantity and peak | Per Account, on the basis of the year's end; the peak after every event |
| Incomplete and notices | The two tables of §4.8; `null`, never 0 |
| Route | `GET /users/customers/{customerId}/tax-years/{taxYear}` (`accounts.read`) |
| Scripts, permissions, problem codes, events, ports, jobs, settings | None |

### Points confirmed

Each point states the recommendation, written into the rules above, and the alternative. All seventeen were confirmed as recommended on 2026-11-28.

1. **The tax year is two fixed instants, 11:00 UTC on 31 March, and starts at 2009** (R3, R4). Recommended: roadmap Q3 states the instant; a constant in Domain needs no time zone data on the host, where a missing or old data file fails only at run time; and it has been right on every 1 April since 2008. The cost: tax year 2008 started under the older daylight saving rule, at 12:00 UTC, so it is not offered, and a change of New Zealand's rule needs an amendment here, as a tax rate does. Alternative: look the instant up in the zone `Pacific/Auckland`, which follows a change by itself and depends on the host's zone data.
2. **Only a tax year that has ended and whose 31 March is final can be read** (R4). Recommended: roadmap Q13 and §7 plan no estimate for a year in progress, and the closing value needs final rows. It writes Q13 one item early, because the read needs an answer. Alternative: also answer the year in progress, with the events so far and no closing.
3. **The ordering key is `bookedAt`, then the internal `Transactions.Id`** (R8). Recommended: it is the order the Transactions list already uses (posting R14), it needs no column, and the id rises with every write. Alternative A: `bookedAt`, then `Created`, which can tie. Alternative B: a sequence a caller sets on an External account, which is a write change.
4. **A Transaction and its Reversal are left out wherever they fall** (R7). Recommended, as the roadmap's mapping: an entry made by mistake is not an acquisition and a disposal. The cost: the opening quantity of a tax year can differ from the valuation of the same instant, and a reversal booked after a report was stored changes the next read (R32). Alternative: as booked, like a valuation.
5. **An amount is the cash leg, converted from the Account's currency at the stored rate of the UTC date of `bookedAt`** (R18, R19). Recommended: the cash leg is what was paid or received; roadmap Q4 says an NZD leg is used as it is; and the UTC date is the date every other lookup of the ledger uses for a rate (orders R34, dividends R32), so Q3's "New Zealand time for the tax year only" holds. Alternative A: convert the quote-currency amount (quantity × price, the fee) from the quote currency, which ignores the rate the trade really got on an NZD Account. Alternative B: the rate of the New Zealand date of the event. Alternative C: Inland Revenue's mid-month table, a second rate set (roadmap Q4, not chosen).
6. **Principal and fee are given apart, and `amount` is the roadmap's Q10** (R10). Recommended: S2 ¶8 and ¶24 support the buy fee in the cost; no source read says that a sell fee comes off the proceeds, and section EX 51 could not be read. Alternative: one amount, and a second read of the ledger if TR4 needs the parts.
7. **The opening and closing value are worked out per Account by the valuation function, in NZD, and summed** (R22). Recommended: the roadmap asks for the valuation function, and calling it per Account reuses its age limit, statuses, rounding and ex-date rule with no second implementation; the lines also show which part of a value is the caller's. The cost: the value is a sum of lines rounded one by one, which can differ by a cent from the Customer's whole quantity times the price. Alternative: one line per tax holding, which needs the ex-date rule written a second time for several Accounts.
8. **A value is worked out only for attributing and not classified holdings** (R23). Recommended: an exempt or a New Zealand share is not in the calculation, and a missing NZX price ([known issue](../known-issues/eodhd-nz-jp-coverage.md)) would make almost every result incomplete for nothing. Alternative: value every tax holding and let the reader of the result filter.
9. **An arrival or a departure is given at the market value of the UTC date of its `bookedAt`, with a notice, and its carried cost is passed on unconverted** (R14). Recommended: roadmap Q8 for the value; the conversion of the carried cost needs TR3's choice of date, so it stays with TR3. Alternative: this read also converts the carried cost, at `acquiredOn` or `bookedAt`.
10. **A move is recognised on read, by a rule** (R15). Recommended: the roadmap's mapping and its worked example ask that a move gives nothing, and no Transaction links the two sides today. The rule uses facts only (the same person, Instrument and quantity, close in time, no change of owner stated), it shows the pair it made, and the tax holding says so with a notice. The cost: it is an inference, and a move whose quantity changed on the way (a split in transit) is not paired. Alternative A: no pairing; every departure and arrival is at market value with its notice, and a move can look like a quick sale. Alternative B: a stated link, as a new field on `StockIn` and on a transfer in, which is a write change and a slice of its own.
11. **Thirty days, and both legs of a move count from the later instant** (R15). Recommended: a transfer between brokers takes days to weeks, and counting from the later instant keeps the shares on one Account at every instant, so the Customer's quantity never dips or doubles. Alternative: 7 days; or each leg from its own `bookedAt` with a quantity "in transit".
12. **A hand `Scrip` is a reorganisation with a notice** (R13). Recommended: that is how the ledger books it (no amount, cost unchanged), and no number is invented. Alternative: a dividend and an acquisition at the market value of the day, which states a taxable issue the platform cannot know of.
13. **An `Adjustment` of a holding is a `Correction` with a notice and no amount, and does not make the holding incomplete** (R16). Recommended: roadmap TR2 says "shown and flagged". Alternative: incomplete, so that every corrected holding needs a person's look before a report.
14. **Comparable quantities per Account, and the peak after every event** (R24, R25). Recommended: the roadmap asks for both, and one split is booked per Account at its own time. Alternative A: one factor per tax holding, which is wrong between two postings. Alternative B: leave the restating to TR4.
15. **A dividend's tax source that disagrees with the issuer country is a notice** (R29). Recommended: instrument-tax-attributes R23 keeps the two statements apart; the report is where they meet, and it shows both. Alternative: incomplete; or no mark.
16. **A dividend is in the tax year of its `bookedAt`** (R11). Recommended: the instant the money reached the account. Alternative: the payment date of the detail, which a hand-entered dividend may not have.
17. **The route sits under the Customer, takes `accounts.read`, and a Customer may read their own** (R1, R30). Recommended: it shows nothing a Customer cannot work out from their own Transactions; roadmap Q16 wants the numbers readable as JSON for the portal pages. Alternative: staff only until TR5.

### Found while writing

- **Roadmap Q5 rests on a source not read again.** The roadmap cites S1 p.13 and S8 for "Inland Revenue accepts the closing price of the last day of the previous income year as the opening value". The April 2026 guide does not say it on p.13; it says the opening market value is that of the interests "held at the beginning of the income year" (p.11) and that market value is "generally the share price on a recognised exchange" (p.13). The sentence comes from S8, which was not read again. R22 follows Q5.
- **A daily rate and Inland Revenue's own rates.** S1 p.13 asks for "the actual rate for the day", and says Inland Revenue accepts its mid-month rate as an actual rate. S4 says rates of a central bank may be used in general, and names the FIF rules as an exception where "the methods and rates specified" apply. `FxRates` holds central bank reference rates (ADR 0019). R18 follows roadmap Q4; whether a daily central bank rate is "the actual rate for the day" in the sense of section EX 57 was not checked against the Act.
- **A trade between an ex-date and the posting of its entitlement** is multiplied again by the entitlement (account-valuation "Found while writing"). R24 takes each Account's factor from its ledger quantities and inherits the error for that Account.
- **Shares in transit over a split.** A departure of 100 and an arrival of 200 after a 2 for 1 split are not a move by R15, because the quantities differ. They stay a departure and an arrival, each with its notice.
- **`OpeningHoldings` of an old External account.** An arrival years before the history floor date has no price, so the tax year it falls in is incomplete. Every later tax year is not affected: only its quantity counts there.
- **Roadmap §3 "Tax residency"** says nothing has a date. This read does not need one (R2); TR5 does.

Still open, not this slice:

- The cost in NZD, the cost a disposal removes, the cost an arrival brings and the threshold test: TR3
- FIF income, the quick sale adjustment and what a method does with the fee and the foreign tax: TR4. Sections EX 51, EX 52 and EX 57 are still to be read
- The report, its stored copy of what it used, and what it does for an `Overseas` Customer: TR5
- `Interest` and the income summary: TR6
- A stated link between the two sides of a move, and a security transfer between two Accounts in one Transaction
- A taxable bonus issue, and any non-cash dividend
- A tax year in progress
- Ledger replay and period close (roadmap §1.2). Ledger replay reuses the key of R8

---

## 12. Changes to other docs

Each doc was changed in place when this spec was accepted (2026-11-28).

| Doc | Change |
| --- | --- |
| [account-valuation.md](account-valuation.md) | R18 and §5: a position carries the instant it was cut at, and the ex-date rule compares a posting's `bookedAt` with it; for a position of a day that is the end of the day, as today. §7: `ValueAccount.ForPositionAsync`. A note under R5 that the tax year read leaves reversed Transactions out (R7 here). History row |
| [posting.md](posting.md) | R14: the list's sort, `bookedAt` then id, is the ordering key of the tax year walk (R8 here). A note under §1 that the tax year read leaves a reversed Transaction and its Reversal out. History row |
| [holdings.md](holdings.md) | R14: the quantity of a tax holding at an instant is a sum over the Customer's Accounts (R22 here). §11 "Stock in/out: Not tax" points here. History row |
| [market-data-history.md](market-data-history.md) | R2: what the floor date must cover for a tax year (§9.1 here). History row |
| [trade-fees.md](trade-fees.md) | §11 "Still open": the question on brokerage and the cost base is answered by roadmap Q10 and R10 here; GST stays open. History row |
| [instrument-tax-attributes.md](instrument-tax-attributes.md) | R23 and "Still open": what a tax read does with a dividend whose tax source disagrees is R29 here |
| [in-kind-transfers.md](in-kind-transfers.md) | §2 Out and R25: `changesBeneficialOwner` is read by the move rule (R15 here); `acquiredOn` and the cost are passed on (R14 here) and still converted by no rule until TR3 |
| [split.md](split.md), [adjustments.md](adjustments.md), [migration-batches.md](migration-batches.md) | A sentence each: a `Scrip` is a reorganisation with a notice (R13); an `Adjustment` of a holding is a `Correction` (R16); a migrated `OpeningHoldings` is an arrival, or half of a move (R14, R15) |
| [dividends.md](dividends.md), [external-income.md](external-income.md) | R3 and R11: the tax year read marks a disagreement with the issuer country (R29 here). A dividend is in the tax year of its `bookedAt` (R11 here) |
| [customers.md](customers.md), [net-worth.md](net-worth.md) | Customers: the route under `/users/customers`. Net-worth R3: a second read under the Customer with the same scope |
| [api-design.md](../api-design.md) | §6 catalog, Customers row: `…/{customerId}/tax-years/{taxYear}` GET. §7.4: the route. Change log |
| [domain-model.md](../domain-model.md) | §8.24 "Tax year of a Customer": the types and invariants of §5. §8.22: `AccountPosition.CutAt` |
| [architecture.md](../architecture.md) | The service `CustomerTaxLedger` beside the two valuation services |
| [glossary.md](../glossary.md) | Tax event, Tax holding, Move, Comparable quantity, Tax notice and Incomplete tax holding become accepted. "Tax year" names its instants. "Ledger replay": the ordering key is R8 here |
| [fif-roadmap.md](../roadmaps/fif-roadmap.md) | §6 TR2: the spec line and the settled mapping. §8: Q2, Q3, Q4, Q5, Q7 written as rules here; Q13 written early for the read. §9 row 2: accepted. §3: the rows the read changes |
| [custodial-roadmap.md](../roadmaps/custodial-roadmap.md), [valuation-roadmap.md](../roadmaps/valuation-roadmap.md) | Ledger replay (X2): the ordering key is decided. H3: the tax year read differs, on purpose |
| [features/README.md](README.md), [future-plans.md](../future-plans.md) §1 step 6 | Status: TR2 accepted |

---

## 13. Suggested commits

Removed when the slice landed (2026-11-28). The commits are in git.

---

## 14. Sources

Read on 2026-11-28. Codes are those of [fif-roadmap.md](../roadmaps/fif-roadmap.md) §11.

| # | Source | Used for | Read |
| --- | --- | --- | --- |
| S1 | Inland Revenue, *Guide to foreign investment funds*, IR461, April 2026 edition, the PDF linked from `ird.govt.nz/fif` (page last updated 31 March 2026) | The opening market value "held at the beginning of the income year" and the CV formula with its gains and costs (p.11); market value and currency conversion (p.13); the quick sale adjustment, "increased and decreased in the same year", "the greatest shareholding in the year" (p.15); a share bought during the year has an opening value of zero, acquisitions and disposals not at market value, a change as a deemed disposal and acquisition at market value (p.22 notes 6 to 8); the cost of a share from a split or a non-taxable bonus (p.23) | Yes, in full text |
| S2 | Inland Revenue, fact sheet IS 24/10 FS 1, *Income tax – Share investments (whether the FIF rules apply)*, 18 December 2024 (`taxtechnical.ird.govt.nz`) | Cost "including any transaction fees" (¶8); the value at the start of the income year, 1 April, and at its end, 31 March (¶17, ¶18); the naming of an income year in its example; brokerage "taken into account in the relevant FIF calculation method" (¶24) | Yes, in full text |
| S4 | Inland Revenue, "Overseas currency conversion to NZ dollars" (`ird.govt.nz/overseas-currency`, last updated 4 November 2026) | The three rate tables and their source, the Reserve Bank of New Zealand; "the rate on 31 March can also be used as the rate on 1 April"; rates of other central banks; the FIF rules named as an exception with their own methods and rates | Yes |
| S8 | Inland Revenue Tax Technical, "New tax rules for offshore portfolio investment in shares" | The closing price of the previous year as the opening value (roadmap Q5) | Not read again. See "Found while writing" |
| — | Income Tax Act 2007, sections EX 51, EX 52 and EX 57 | The comparative value method, the fair dividend rate method and the conversion of foreign currency in the Act's own words | Not read: `legislation.govt.nz` and NZLII refused automated reads (403) |

What follows from the refusal: R10 keeps the fee apart from the principal, R18 follows the guide's wording on the actual rate of the day, and R22 follows roadmap Q5. No rule of this slice computes a tax figure; it prepares what TR3 and TR4 compute from, and each of them reads its own sections first.

The dates of New Zealand's daylight time in R3 (daylight time ends on the first Sunday of April, at 03:00, since 2008) are from general knowledge of the Daylight Time Order 2007. The Order was not read for this spec.
