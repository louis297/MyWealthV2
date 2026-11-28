---
title: Cost in NZD and the threshold test
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-28
last_updated: 2026-11-28
related:
  - ../roadmaps/fif-roadmap.md
  - ../roadmaps/custodial-roadmap.md
  - ../roadmaps/valuation-roadmap.md
  - ../future-plans.md
  - tax-year-events.md
  - instrument-tax-attributes.md
  - holdings.md
  - buy-sell.md
  - trade-fees.md
  - split.md
  - corporate-actions.md
  - dividends.md
  - in-kind-transfers.md
  - migration-batches.md
  - adjustments.md
  - fx-rates.md
  - market-data-history.md
  - account-valuation.md
  - custody-mode-grouping.md
  - interest-run.md
  - ../domain-model.md
  - ../api-design.md
  - ../architecture.md
  - ../glossary.md
---

# Cost in NZD and the threshold test

Step 3 of [fif-roadmap.md](../roadmaps/fif-roadmap.md) §9. It covers roadmap item **TR3** and writes the decisions **Q9** (the cost a disposal removes) and, for the cost, **Q8** (a holding that arrives without a purchase) and **Q10** (trade fees) as rules. It computes over the tax events of [tax-year-events.md](tax-year-events.md) and adds nothing to the ledger.

An individual whose attributing interests cost NZ$50,000 or less in total at all times in a tax year does not apply the FIF rules for that year. The test looks at what was paid, in NZD, at the time of each purchase, and not at any market value. The ledger keeps cost per Account, in the quote currency, as it is now. This slice works out the cost the test asks for: per tax holding, in NZD, after every tax event, from the Customer's first event on. From it comes the **threshold test** of a tax year: the highest total, when it was reached, and whether it was above the threshold of that year.

It is the one input of the FIF report that is path-dependent, and the only one that reaches back before the tax year (roadmap §1.2). Nothing is stored and no job runs.

New: fields on the item of `GET /users/customers/{customerId}/tax-years/{taxYear}` (`threshold`, a `cost` on each tax holding and on each event); Domain types `FifThreshold`, `TaxCostLedger`, `TaxCost`, `ThresholdTest` and their enums; `CustomerTaxYear.Compute` and the service `CustomerTaxLedger` carry them. No route, script, table, permission, problem code, domain event, `TransactionType`, port, job or setting. No write changes: the `Holdings` projection and its average cost in the quote currency stay as they are. No portal page and no simulator change.

**Status is `accepted` (2026-11-28).** The eighteen points of §11 were confirmed on 2026-11-28, every one as recommended, and are written into the rules. The changes to the docs listed in §12 were made when it was accepted. It is a build contract. Landed 2026-11-28.

Notes from landing:

- R22, R25: the window is the instant the quantity last left 0 before the opening, for a tax holding held at the opening (`TaxCostWalk.HeldSince`), as clarified when the spec was accepted. A tax year missing from the earlier treatments is `NotClassified` and skipped.
- R11, R14: a correction whose `costChange` is 0 needs no rate: there is no figure to convert. `TaxCostLedger.RateKeys` asks for no row for a figure of 0 or in NZD. An arrival with a carried cost of 0 is `NoCarriedCost` before any rate is looked at.
- R13: when one event has several causes, the first that applies is kept, in this order: a rate (`NoRate`, `StaleRate`) or `NoCarriedCost`, then `NegativeQuantity`, then `NegativeCost`. A disposal or a departure of more than is held is `NegativeQuantity`, as its quantity after is below 0.
- R24: an event of a tax holding whose cost is unknown has `cost` `{ change: null, after: null }`. An event that brings the quantity back to 0 from an unknown cost has `after` 0.00 and `change` `null`. A step that made a cost unknown has `change` `null`, and its `total` leaves that tax holding out (R17).
- R24: `cost.fx` on an arrival or a correction is the row that was read, also when it is stale; `null` for a carried cost in NZD and on a correction of the quantity only.
- R24: a zero is written with NZD's places (`0.00`), as every other NZD figure.
- R25: an arrival whose carried cost was not added (the cost was already unknown, or it carried 0) gives no notice `CarriedCost`.
- §5: `ThresholdTest.Compute` also takes NZD's decimal places. `CustomerTaxYear.Compute` takes the earlier treatments as an optional last parameter, keyed by Instrument and tax year. The rate rule of tax-year-events R18 and R21 moved to the internal `TaxFx`, which the tax year read and the walk share.
- §7: `CustomerTaxLedger` reads the earlier treatments from the tax year of each costed Instrument's first event. The rate keys of the year and of the walk are read once each.
- §10: in example 3 through the API, B1 is opened as an External account so that its trades can be dated, and is made Custodial after them; U3 is written with `Transaction.PostTransferIn` and its `acquiredOn`. In example 2 the Instruments are quoted in NZD, so that the NZD figures of the guide need no caller's rate, and `CCC` is classified for 2024 and 2025, because an exemption stands for its own tax year.
- tax-year-events: the case of an arrival and a departure at market value now also lists the notice `CarriedCost`, and its `item` and event field lists gain `threshold` and `cost`.

The tax statements in this spec were read again from Inland Revenue on 2026-11-28 (§14). The Income Tax Act refused automated reads again; §14 says which rules rest on the guide alone. Nothing here is tax advice.

---

## 1. Summary

| | The ledger | The tax year read today | This slice |
| --- | --- | --- | --- |
| Whose cost | One Account's holding | — | One tax holding: the Customer's shares of one Instrument over all their Accounts (roadmap T1) |
| Which currency | The quote currency | An arrival passes its cost on, unconverted | NZD, at the rate of the day of each purchase |
| When | Now | — | At the start of the tax year, after every event of the year, at its end |
| What a sale removes | The Account's average cost | — | The tax holding's average cost in NZD (roadmap Q9) |
| A move between two Accounts | Cost leaves one Account and arrives on the other | One `Move` | Nothing changes |
| A Transaction and its Reversal | Both count | Both left out | Both left out |
| The threshold | — | — | The total over the attributing tax holdings, its highest point in the year, and above or not |

The rule that distinguishes this slice: the cost is what was paid, never what something is worth. No price is read. A cost that cannot be worked out is said, and the test is then **incomplete**; no number is put in its place (roadmap T5).

### 1.1 Worked examples

Roadmap T12 asks for worked examples before the spec. The figures of examples 1 and 3 are made up; example 2 is Inland Revenue's own (S2, Example 1). The tests use them as written. All times are UTC. USD and NZD have 2 decimal places.

**Example 1: the worked example of tax-year-events §1.1, continued.** One Customer, two Accounts (A1 in USD, A2 in NZD), one Instrument `AAA`, `Attributing`. The events and their NZD amounts are those of that spec.

| Tax year | # | Event | Quantity after | Cost change | Tax cost after |
| --- | --- | --- | --- | --- | --- |
| 2025 | — | Opening | 0 | | 0.00 |
| | 1 | `Acquisition` (T1) | 100 | + 8,708.70 | 8,708.70 |
| | 2 | `Acquisition` (T2) | 140 | + 3,647.00 | 12,355.70 |
| | — | Closing | 140 | | 12,355.70 |
| 2026 | — | Opening | 140 | | 12,355.70 |
| | 1 | `Disposal` (T3), 30 of 140 | 110 | − 12,355.70 × 30 ÷ 140 = − 2,647.65 | 9,708.05 |
| | 2 | `Dividend` (T4) | 110 | — | 9,708.05 |
| | 3, 4 | `Reorganisation` (T7, T8) | 220 | — | 9,708.05 |
| | 5 | `Dividend` (T9) | 220 | — | 9,708.05 |
| | 6 | `Acquisition` (T9, reinvested) | 220.8 | + 37.63 | 9,745.68 |
| | 7 | `Move` (T10, T11) | 220.8 | — | 9,745.68 |
| | 8 | `Acquisition` (T12) | 240.8 | + 1,046.65 | 10,792.33 |
| | — | Closing | 240.8 | | 10,792.33 |

Threshold test of tax year 2025: opening 0.00, highest total 12,355.70 after T2, not above 50,000. Of tax year 2026: highest total 12,355.70, at the opening, not above.

The sale price of T3 and the market value of the holding appear nowhere. The reversed purchase T5 (48,125.00 NZD) is not in the walk. As booked, the total would have been 9,708.05 + 48,125.00 = 57,833.05 for two days in June 2025, and the Customer would have been in the FIF rules for a purchase that was entered by mistake.

**Example 2: Inland Revenue's example (S2, Example 1).** "Jax" invests $55,000 in shares of several United States companies on 31 March 2022, sells half in January 2024 and buys exempt Australian shares with the money. On the platform: one NZD External account; `AAA` and `BBB` are `Attributing`, `CCC` is `ExemptAsxListed`.

| `bookedAt` | Transaction | NZD cash legs | Tax cost after |
| --- | --- | --- | --- |
| 30 Mar 2022 22:00 (11:00 on 31 March in Auckland) | `Buy` 200 `AAA` | −29,985.00 and a fee of −15.00 | `AAA` 30,000.00 |
| 30 Mar 2022 22:00 | `Buy` 500 `BBB` | −24,990.00 and a fee of −10.00 | `BBB` 25,000.00 |
| 15 Jan 2024 21:00 | `Sell` 100 `AAA` | | `AAA` 15,000.00 |
| 15 Jan 2024 21:00 | `Sell` 250 `BBB` | | `BBB` 12,500.00 |
| 16 Jan 2024 21:00 | `Buy` 4,000 `CCC` | −32,150.00 | not counted |

| Tax year | Opening total | Highest total | Reached | Closing total | Outcome | Inland Revenue says |
| --- | --- | --- | --- | --- | --- | --- |
| 2022 | 0.00 | 55,000.00 | 30 Mar 2022 22:00, the second `Buy` | 55,000.00 | `Above` | "Jax will be subject to the FIF rules" |
| 2023 | 55,000.00 | 55,000.00 | the opening | 55,000.00 | `Above` | "still subject to the FIF rules" |
| 2024 | 55,000.00 | 55,000.00 | the opening | 27,500.00 | `Above` | "still subject to the FIF rules ... because at a point in time, Jax held attributing interests that cost more than $50,000" |
| 2025 | 27,500.00 | 27,500.00 | the opening | 27,500.00 | `NotAbove` | "the FIF rules do not apply" |

The shares were worth $64,300 when Jax sold; the test does not look at it. The 32,150.00 spent on `CCC` is not counted, because the interest is exempt.

**Example 3: a purchase in a foreign currency crosses the threshold.** One Customer, tax year 2026. B1 is a Custodial account in USD, B2 an External account in NZD. `BBB` and `DDD` are quoted in USD and `Attributing`; `EEE` is `ExemptAsxListed`. Stored rates USD → NZD: 1.6300 for 14 June 2024, 1.6900 for 12 May 2025, 1.7500 for 9 October 2025.

| # | `bookedAt` | Account | Transaction | Cost change in NZD | Total |
| --- | --- | --- | --- | --- | --- |
| — | | | Opening | | 0.00 |
| U1 | 2 Apr 2025 01:00 | B2 | `Buy` 1,000 `EEE` for 30,000.00 NZD | not counted | 0.00 |
| U2 | 12 May 2025 20:00 | B1 | `Buy` 500 `BBB` at 50.00 USD, fee 10.00 USD | 25,000.00 × 1.6900 = 42,250.00, fee 16.90: + 42,266.90 | 42,266.90 |
| U3 | 4 Aug 2025 03:00 | B1 | `StockIn` (in-kind transfer in) 60 `DDD`, cost 2,400.00 USD, `acquiredOn` 14 June 2024 | 2,400.00 × 1.6300 = + 3,912.00 | 46,178.90 |
| U4 | 9 Oct 2025 19:30 | B1 | `Buy` 40 `BBB` at 55.00 USD, fee 10.00 USD | 2,200.00 × 1.7500 = 3,850.00, fee 17.50: + 3,867.50 | 50,046.40 |
| U5 | 20 Nov 2025 20:10 | B1 | `Sell` 270 `BBB` of 540 | − 46,134.40 × 270 ÷ 540 = − 23,067.20 | 26,979.20 |
| — | | | Closing | | 26,979.20 |

Highest total 50,046.40, reached at U4 on 9 October 2025: `Above`, by 46.40. Every attributing tax holding of the Customer is then in the FIF rules for tax year 2026; the first 50,000 is not exempt (S1 p.9), and the sale in November does not undo it.

What the example shows:

- **The rate of the day of each purchase** (R6). At a rate of 1.7000 the last purchase would have cost 3,757.00 and the total would have been 49,935.90. The test is sensitive to the rate, and the answer carries each rate it used.
- **The fee is part of the cost** (R6; S2 ¶8).
- **An arrival brings its cost** (R9): the caller's 2,400.00 USD, at the rate of the stated acquisition date. Its market value on the day it arrived is in the tax year read (tax-year-events R14) and is not used here.
- **An exempt interest is not counted** (R16; S2 ¶7).

---

## 2. Scope

**In**

- Domain: `FifThreshold` (the threshold of a tax year); `TaxCostLedger.Walk` (the cost of each tax holding after each tax event); `TaxCost`, `TaxCostStatus`, `TaxCostUnknownCause`; `ThresholdTest`, `ThresholdOutcome`, `ThresholdIncompleteReason`; `CustomerTaxYear.Compute` carries them; `TaxNotice.CarriedCost`
- Application: `CustomerTaxLedger` loads the rates of the walk and the treatments of earlier tax years; the item of `GetCustomerTaxYear` gains the fields of R24
- HTTP: no new route. The item of `GET /users/customers/{customerId}/tax-years/{taxYear}` gains `threshold`, `holdings[].cost` and `holdings[].events[].cost`
- Tests listed in §10

**Out**

- FIF income under either method, and the quick sale adjustment with its own average cost over the purchases of one year: roadmap TR4. That average is not the cost of this slice
- The report, and what it says to a Customer whose test is `NotAbove`: TR5. The income summary: TR6
- Whether the FIF rules apply to the Customer for another reason: tax residency, a transitional resident, the choice to apply the rules below the threshold and its four-year rule (roadmap Q14, §7). The test says above or not, and nothing else
- Joint holders, trusts and companies. A Customer is one person (ADR 0020)
- The option of half the market value on 1 April 2007 for an interest acquired before 1 January 2000 (R23)
- The deemed sale and purchase at market value when an Instrument becomes or stops being exempt (roadmap §7). The test says that it happened and is incomplete (R22)
- A cost in NZD stated by the caller on an arrival. The cost an arrival carries stays one figure in the quote currency (holdings R10)
- Tax lots. No rule here reads one (roadmap §7)
- Any stored result, any write, ledger replay and period close (roadmap §1.2)
- Any change to the `Holdings` projection, its average cost in the quote currency, or the cost on a security leg
- Unrealised gain in NZD, and cost on a past day for a valuation (valuation-roadmap §7)
- Any portal page

---

## 3. Stories

1. As an Adviser I read, for one of my Customers and one tax year that has ended, whether the cost of their attributing interests was above the threshold at any time, the highest total and when it was reached.
2. As a Customer I read the same for myself.
3. As a reader I see which tax holding has a cost that could not be worked out and where that started, so that I never take "not above" from a partial sum.
4. As a developer I work the test out by hand from `GET /transactions` and `GET /fx-rates`, and get the figures the route returns.
5. As roadmap item TR5 I get the test with every rate it used, and print it.
6. As an Adviser I cannot read the test of another Adviser's Customer, and a firm cannot read another firm's (404).

---

## 4. Rules

### 4.1 Where it is read

| ID | Rule |
| --- | --- |
| R1 | **Part of the tax year read.** The cost and the test are fields of the item of `GET /users/customers/{customerId}/tax-years/{taxYear}`. There is no route of their own. Access, scope, the 401, 403 and 404 cases, and which tax years can be read are those of tax-year-events R1, R4 and R30. No new permission and no new problem code. |
| R2 | **A read only.** tax-year-events R2 holds: nothing is written, no lock is taken, and the Customer's tax profile is not read. An `Overseas` Customer gets the same test as a `NewZealand` one; what a report does with it is roadmap Q14. |

### 4.2 Tax cost

| ID | Rule |
| --- | --- |
| R3 | **Tax cost.** The tax cost of a tax holding at a point in time is what the Customer paid, in NZD, for the shares of it they hold at that point. It is one figure per tax holding, over all the Customer's Accounts (roadmap T1, Q7), with NZD's decimal places. It is never a market value. Sources: S2 ¶8, "what you paid in New Zealand dollars when you acquired the shares, (including any transaction fees) not their market value on any day"; S3 ¶29 and ¶116, the cost "is calculated in New Zealand dollars at the time the shares ... are purchased". |
| R4 | **Which tax holdings have a tax cost.** For tax year `Y`, the tax cost is worked out for a tax holding whose treatment for `Y` is `Attributing`, `AttributingNoFdr` or `NotClassified`, as a value is (tax-year-events R23). For `NotForeign`, `ExemptAsxListed` and `ExemptOther` no rate is read and the status is `NotRequired`. |
| R5 | **The walk.** The tax cost is worked out by walking the tax events of the tax holding from the Customer's first one on, in the order of tax-year-events R8, over every tax year before `End(Y)`. The events are those of `TaxLedger.Build`: a reversed Transaction and its Reversal are not among them (its R7), and a departure and an arrival that its R15 pairs are one `Move`. Before the first event the quantity is 0 and the tax cost is 0.00. The walk keeps the quantity of the tax holding as booked, summed over the Accounts, and its tax cost. |
| R6 | **`Acquisition` (roadmap Q10).** The tax cost rises by the event's `amount`: `principal + fee`, each converted on its own by tax-year-events R18 (a leg of an NZD Account as booked; any other at the stored rate of the UTC date of `bookedAt`). This holds for a `Buy` on either custody mode and for the acquisition of a reinvested dividend, whose fee is 0. Source: S2 ¶8 for the fee. |
| R7 | **`Disposal` (roadmap Q9).** The tax cost falls by the average: `removed = Round(cost × quantity sold ÷ quantity held before, dp, AwayFromZero)`, where `dp` is NZD's `DecimalPlaces`; a disposal of everything held removes the whole cost. This is the release rule of buy-sell R15, applied to the tax holding in NZD. The proceeds and the fee of the sale change nothing here. Sources: S2 Example 1, where selling half of the interests takes the cost from $55,000 to a figure at or below $50,000; S3 ¶92, which allows first in first out or weighted average cost where the cost of the shares sold cannot be traced, for a taxable sale. No source read says which cost a disposal removes for the threshold (§14). |
| R8 | **What changes nothing.** A `Reorganisation` changes the quantity and keeps the tax cost: the new shares take a part of the original cost, so the total is the same (S1 p.23, "an allocation of the original cost"). A `Scrip` is read the same way and keeps its notice (tax-year-events R13). A `Dividend` changes nothing. A `Move` changes nothing: the Customer's shares and what they paid for them are the same on either Account (roadmap T1). |
| R9 | **`Arrival` (roadmap Q8).** The tax cost rises by the cost the arrival carries (`carriedCost`, in the Instrument's quote currency): as it is when the quote currency is NZD, otherwise `Round(carried cost × rate, dp, AwayFromZero)` with the stored rate of the quote currency → NZD of `acquiredOn` when the arrival states one, and of the UTC date of `bookedAt` when it does not (R14). The market value of tax-year-events R14 is not used. A carried cost of 0 with a quantity is read as "not stated": the tax cost becomes unknown with the cause `NoCarriedCost` (R13). The tax holding gets the notice `CarriedCost`: the figure is the caller's, and one rate stands for purchases that may have been made over years. Source: S1 p.23 says the cost of an interest acquired for something other than money is the market value of that thing, so a true cost of zero is rare. |
| R10 | **`Departure`.** The tax cost falls by the average, as R7. What the Customer received for the shares, if anything, is not known and not used. |
| R11 | **`Correction`.** An `Adjustment` of a holding is the caller's two numbers (adjustments R13), and the walk applies them as stated: the quantity changes by `quantityChange`, and the tax cost by `costChange`, signed, converted as R9 at the rate of the UTC date of `bookedAt`. No average is released. A result below 0 makes the tax cost unknown with the cause `NegativeCost`. The tax holding keeps the notice `Correction` (tax-year-events R16). |
| R12 | **Nothing held costs nothing.** When the quantity of the tax holding is 0 after an event, its tax cost is 0.00, whatever the arithmetic left and whether or not it was known before. |
| R13 | **Unknown.** The tax cost becomes unknown at an event when: the figure the event adds has no rate or a stale one (`NoRate`, `StaleRate`; R14); an arrival carries no cost (`NoCarriedCost`); the quantity is below 0 after it, or a disposal or a departure is of more than is held (`NegativeQuantity`); or a correction leaves a cost below 0 (`NegativeCost`). It stays unknown after every later event until R12 applies. An unknown tax cost is `null`, never 0. The tax holding names the first cause and its event, which may be in an earlier tax year. |
| R14 | **Rates.** Every conversion uses the rule of tax-year-events R18 and R21, from the same table: the stored `FxRates` row of the directed pair into NZD with the latest `AsOfDate` on or before the date, not older than 4 calendar days before it. The inverse pair is not used and no source is called. One rule for every amount of every year (roadmap T4). The date is the UTC date of `bookedAt`, or `acquiredOn` for an arrival that states one. |

### 4.3 The threshold test

| ID | Rule |
| --- | --- |
| R15 | **The threshold of a tax year (roadmap T6).** `FifThreshold.For(Y)` is a value per tax year, a constant in Domain that changes only with an amendment to this spec, as `WithholdingRates` does (interest-run R4). It is NZ$50,000 for tax years 2009 to 2026. From tax year 2027 it has **no value yet**: a Bill before Parliament raises it to NZ$100,000 "for the 2026–27 and later income years" (S5), and it was not law in any source read on 2026-11-28. A tax year with no value gives the outcome `Unknown` with the reason `NoThreshold` (R19, R20). Tax year 2027 can first be read on 2 April 2027 (tax-year-events R4), so the row can be added before any read needs it. Sources: S1 p.9, p.10; S2 ¶8; S5 p.48–49. |
| R16 | **Which tax holdings count.** A tax holding counts in the test of tax year `Y` when its treatment for `Y` is `Attributing` or `AttributingNoFdr`. An exempt interest and a New Zealand share are not counted (S2 ¶7). A `NotClassified` tax holding is not counted and makes the test incomplete (R20). The treatment is the one of `Y`, whatever it was when the shares were bought. |
| R17 | **The total.** The total at a point of the year is the sum of the tax costs of the tax holdings that count, where known. It is taken at the opening (`Start(Y)`), after every event of the year on a tax holding that counts, in the order of tax-year-events R8 over all of them, and at the closing (`End(Y)`). |
| R18 | **The highest total.** `peak` is the greatest total of R17, with the first point at which it was reached: the opening, or an event with its `bookedAt`. It is read after every event, not at the end of each day, so a purchase and a sale on one day show (S1 p.9, "on any day in the year"; S2 ¶8, "at any time during the income year"). The point is an instant in UTC; this slice gives no New Zealand date. |
| R19 | **Outcome.** `Above` when the highest total is more than the threshold; `NotAbove` when it is equal to it or less (S1 p.10, "equal to or less than $50,000 at all times in the year") and the test is complete; `Unknown` otherwise. Above the threshold at one point, every attributing tax holding is in the rules for the whole tax year, and the first $50,000 is not exempt (S1 p.9). The outcome says nothing about tax residency or about a choice to apply the rules (R23). |
| R20 | **Incomplete (roadmap T5).** The test is incomplete when a reason of the table below applies, and it lists every one with its tax holding. An incomplete test is never `NotAbove`. It does not make a tax holding incomplete (tax-year-events R27), and it does not change `complete` of the tax year: FIF income can be worked out for a holding whose cost is unknown. The answer is 200. |
| R21 | **A floor.** A tax cost is never below 0, so a total of the known costs is never more than the true total. An incomplete test whose highest total is above the threshold is therefore `Above`: the missing figures can only add to it. Its `peak` is then "at least" this figure, and `complete` `false` says so. |
| R22 | **A treatment that changed while held.** When an Instrument becomes or stops being exempt, the tax rules treat the holder as selling and buying at market value (S1 p.22 note 8), and the cost for the threshold starts again from that value. The platform does not work this out (roadmap §7). For a tax holding that counts in `Y` and is held at the opening: take the instant its quantity last left 0 before the opening, and each tax year from that instant's to `Y − 1`. A tax holding not held at the opening has no earlier year to check: the shares it holds in `Y` were all bought in `Y`. If the Instrument's treatment for one of those years is `NotForeign`, `ExemptAsxListed` or `ExemptOther`, the test is incomplete with the reason `TreatmentChanged`. The tax cost of the walk is still shown and still counts as a floor. A year for which the Instrument is `NotClassified` says nothing and is skipped. |
| R23 | **What the test does not do.** It does not offer half the market value on 1 April 2007 for an interest acquired before 1 January 2000 (S1 p.23): the walk uses the actual cost, or says that it has none. It does not add a share of a joint holding. It does not say whether the Customer chose to apply the rules in this or an earlier year. |

Reasons of R20:

| Reason | When |
| --- | --- |
| `NotClassified` | A tax holding of the year (tax-year-events R23) is `NotClassified` for `Y`: it may be an attributing interest |
| `CostUnknown` | The tax cost of a tax holding that counts is unknown at the opening or after an event of the year (R13) |
| `TreatmentChanged` | R22 |
| `NoThreshold` | `FifThreshold.For(Y)` has no value (R15). It names no tax holding |

### 4.4 The read

| ID | Rule |
| --- | --- |
| R24 | **Fields.** The item gains `threshold`: `{ amount, opening, peak, closing, outcome, complete, incomplete, steps }`. `amount` is the threshold of the year or `null`; `opening` and `closing` are the totals of R17; `peak` is `{ total, at, instrumentId, transactionId }`, the last two `null` when the peak is the opening; `incomplete` is a list of `{ reason, instrumentId }`; `steps` lists each event of the year that changed the total or made a cost unknown, in order, as `{ at, instrumentId, eventSequence, transactionId, kind, change, total }`, so that the peak can be checked from the figures shown. A tax holding gains `cost`: `{ status, opening, closing, unknown }` with `status` `Known`, `Unknown` or `NotRequired`, and `unknown` `{ cause, at, transactionId }` or `null`. An event gains `cost`: `{ change, after, fx }`, where `fx` is the rate row of a carried cost or a correction (`null` otherwise: an acquisition's rate is the event's own `fx`); the whole object is `null` when the status of the tax holding is `NotRequired`. Every figure is in NZD with NZD's decimal places. §8.1 shows each shape. Every caller who may read the Customer sees every field. |
| R25 | **Notice.** `CarriedCost` joins the notices of tax-year-events R28: an arrival that R9 costed is in the walk of the tax holding in this tax year, or in an earlier one after the instant its quantity last left 0 before the opening when it is held at the opening (the window of R22). It does not make anything incomplete. |
| R26 | **Still a ledger read.** tax-year-events R32 holds, and reaches further here: an entry backdated on an External account into any earlier tax year changes the tax cost of every later one (roadmap Q15), and so does a rate row inserted later for the date of an old purchase. |

Policies: none added.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `FifThreshold` | Static (new, `src/Domain/Tax`) | `For(taxYear)` → the threshold in NZD, or none. One row: 50,000 from 2009 to 2026 (R15) |
| `TaxCostLedger` | Static (new) | `Walk(events, costed, rates, decimalPlaces)` → for each event of a costed Instrument, the change and the tax cost after it, or the cause it is unknown; and the tax cost of an Instrument at an instant. R5–R14. It reads no clock and no store |
| `TaxCost` | Value (new) | The cost of one tax holding in one tax year: `Status`, `Opening`, `Closing`, the first unknown point |
| `TaxCostStatus`, `TaxCostUnknownCause` | Enums (new) | `Known = 0`, `Unknown = 1`, `NotRequired = 2`; `NoCarriedCost = 0`, `NoRate = 1`, `StaleRate = 2`, `NegativeQuantity = 3`, `NegativeCost = 4` |
| `ThresholdTest` | Value (new) | The result of R15–R22. `Compute(taxYear, walk, treatments, earlierTreatments, threshold)`: the totals, the peak, the outcome, the reasons and the steps |
| `ThresholdOutcome`, `ThresholdIncompleteReason` | Enums (new) | `NotAbove = 0`, `Above = 1`, `Unknown = 2`; the values of the table of §4.3 |
| `CustomerTaxYear` | Value (existing) | Gains `Threshold`. `Compute` also takes the treatments of earlier tax years and the rates of the walk, calls `TaxCostLedger.Walk` and `ThresholdTest.Compute`, and puts a `TaxCost` on each `TaxHolding` and the cost figures on each `TaxYearEvent` |
| `TaxNotice` | Enum (existing) | Gains `CarriedCost = 7` |
| `TaxLedger`, `TaxEvent`, `TaxYear`, `Transaction`, `Holding`, `Instrument` | Existing | No change |

```text
tax cost              per tax holding, NZD, from the Customer's first event                 (R3, R5)
acquisition           + principal + fee, each at the rate of the UTC date of bookedAt      (R6)
disposal, departure   - Round(cost x quantity out / quantity before, dp); all out -> 0      (R7, R10)
reorganisation, dividend, move      no change                                               (R8)
arrival               + carried cost x rate(quote -> NZD, acquiredOn or the UTC date)      (R9)
correction            quantity and cost change as stated, cost at the rate of the day      (R11)
quantity 0            cost 0.00, known again                                               (R12)
total                 sum over the Attributing and AttributingNoFdr holdings of Y          (R16, R17)
peak                  the greatest total: the opening, or after an event of the year       (R18)
outcome               Above if peak > threshold; NotAbove if complete; else Unknown        (R19, R21)
```

Invariants:

- A tax cost is `≥ 0` or unknown. It is 0.00 whenever the quantity is 0.
- A `Reorganisation`, a `Dividend` and a `Move` never change a tax cost.
- The closing tax cost of tax year `Y` equals the opening tax cost of `Y + 1`, and the closing total equals the opening total when the same tax holdings count in both years.
- The highest total is never below the opening total or the closing total.
- `NotAbove` is given only by a complete test.
- The test never changes `complete` of a tax holding or of the tax year.

Domain events: none. Nothing is an entity and nothing is stored.

[domain-model.md](../domain-model.md) §8.24 gains these types when this spec is accepted.

---

## 6. Database

None. No script.

The read uses the tables of tax-year-events §6 and no other. It reads more rows of `FxRates`: one per currency and date of every acquisition, arrival and correction of a costed tax holding since the Customer's first event, where the tax year read alone needs those of one year.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Service | `CustomerTaxLedger` (existing) | `ReadAsync` also loads: the rate rows of the walk (R14), for the events before `End(Y)` of the Instruments R4 names, a missing pair caught as "no rate"; and for each of those Instruments its treatment for every tax year from its first event to `Y − 1` (`Instrument.FifTreatmentFor`). Both go to `CustomerTaxYear.Compute`. Still the one place a tax year is worked out; TR4 and TR5 call it |
| Query | `GetCustomerTaxYear` (existing) | Maps the fields of R24. No new check |

Where each rule lives:

| Rule | Where |
| --- | --- |
| The threshold of a tax year (R15) | `FifThreshold` |
| The cost of each event and of each point (R5–R14) | `TaxCostLedger.Walk` |
| Which holdings count, the totals, the peak, the outcome, the reasons (R16–R22) | `ThresholdTest.Compute` |
| Which rates and which treatments are read (R4, R14, R22) | `CustomerTaxLedger` |
| Access and `taxYear` (R1) | `GetCustomerTaxYear`, unchanged |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/users/customers/{customerId}/tax-years/{taxYear}` | `accounts.read` | 200 item, with the fields of R24 | 400 / 401 / 403 / 404, unchanged |

### 8.1 Bodies

`GET /users/customers/{customerId}/tax-years/2026` for example 3, the new fields only:

```json
{
  "taxYear": 2026,
  "currency": "NZD",
  "threshold": {
    "amount": 50000.00,
    "opening": 0.00,
    "peak": { "total": 50046.40, "at": "2025-10-09T19:30:00Z", "instrumentId": "<BBB publicId>", "transactionId": "<U4 publicId>" },
    "closing": 26979.20,
    "outcome": "Above",
    "complete": true,
    "incomplete": [],
    "steps": [
      { "at": "2025-05-12T20:00:00Z", "instrumentId": "<BBB publicId>", "eventSequence": 1, "transactionId": "<U2 publicId>", "kind": "Acquisition", "change": 42266.90, "total": 42266.90 },
      { "at": "2025-08-04T03:00:00Z", "instrumentId": "<DDD publicId>", "eventSequence": 1, "transactionId": "<U3 publicId>", "kind": "Arrival", "change": 3912.00, "total": 46178.90 },
      { "at": "2025-10-09T19:30:00Z", "instrumentId": "<BBB publicId>", "eventSequence": 2, "transactionId": "<U4 publicId>", "kind": "Acquisition", "change": 3867.50, "total": 50046.40 },
      { "at": "2025-11-20T20:10:00Z", "instrumentId": "<BBB publicId>", "eventSequence": 3, "transactionId": "<U5 publicId>", "kind": "Disposal", "change": -23067.20, "total": 26979.20 }
    ]
  },
  "holdings": [
    {
      "instrument": { "id": "<BBB publicId>", "symbol": "BBB", "name": "Beta Inc", "quoteCurrency": "USD" },
      "cost": { "status": "Known", "opening": 0.00, "closing": 23067.20, "unknown": null },
      "events": [
        { "sequence": 1, "kind": "Acquisition", "amount": 42266.90, "cost": { "change": 42266.90, "after": 42266.90, "fx": null } },
        { "sequence": 2, "kind": "Acquisition", "amount": 3867.50, "cost": { "change": 3867.50, "after": 46134.40, "fx": null } },
        { "sequence": 3, "kind": "Disposal", "amount": 25431.00, "cost": { "change": -23067.20, "after": 23067.20, "fx": null } }
      ]
    },
    {
      "instrument": { "id": "<DDD publicId>", "symbol": "DDD", "name": "Delta Inc", "quoteCurrency": "USD" },
      "cost": { "status": "Known", "opening": 0.00, "closing": 3912.00, "unknown": null },
      "events": [
        {
          "sequence": 1, "kind": "Arrival", "amount": 4284.00, "status": "MarketValue",
          "carriedCost": { "amount": 2400.00, "currency": "USD", "acquiredOn": "2024-06-14" },
          "cost": { "change": 3912.00, "after": 3912.00, "fx": { "rate": 1.63, "date": "2024-06-14", "source": "Frankfurter" } }
        }
      ],
      "notices": [ "ArrivalAtMarketValue", "CarriedCost" ]
    },
    {
      "instrument": { "id": "<EEE publicId>", "symbol": "EEE", "name": "Echo Ltd", "quoteCurrency": "AUD" },
      "treatment": { "fifTreatment": "ExemptAsxListed", "rowTaxYear": 2026, "note": null, "issuerCountry": "AU" },
      "cost": { "status": "NotRequired", "opening": null, "closing": null, "unknown": null },
      "events": [ { "sequence": 1, "kind": "Acquisition", "amount": 30000.00, "cost": null } ]
    }
  ]
}
```

The `amount` of the `Disposal` and of the `Arrival` are figures of the tax year read (the proceeds, and the market value of the day); they are made up here and no rule of this slice reads them.

A test that is incomplete and still above, and one that cannot say:

```json
{
  "threshold": {
    "amount": 50000.00, "opening": 61200.00,
    "peak": { "total": 61200.00, "at": "2025-03-31T11:00:00Z", "instrumentId": null, "transactionId": null },
    "closing": 61200.00, "outcome": "Above", "complete": false,
    "incomplete": [ { "reason": "NotClassified", "instrumentId": "<publicId>" } ],
    "steps": []
  }
}
```

```json
{
  "threshold": {
    "amount": 50000.00, "opening": 18400.00,
    "peak": { "total": 18400.00, "at": "2025-03-31T11:00:00Z", "instrumentId": null, "transactionId": null },
    "closing": 18400.00, "outcome": "Unknown", "complete": false,
    "incomplete": [ { "reason": "CostUnknown", "instrumentId": "<publicId>" } ],
    "steps": []
  }
}
```

with, on the tax holding it names:

```json
{ "cost": { "status": "Unknown", "opening": null, "closing": null, "unknown": { "cause": "NoCarriedCost", "at": "2023-08-04T00:00:00Z", "transactionId": "<publicId>" } } }
```

`at` is an instant in UTC. The `date` of a rate is a date with no zone ([api-design.md](../api-design.md) §2).

### 8.2 Errors

None added. An incomplete test is not an error: the answer is 200 with `threshold.complete` `false`.

Cross-tenant or wrong-collection id → **404**, not 403, as tax-year-events R1 says. Keep the resource catalog in [api-design.md](../api-design.md) pointing there for the route and here for these fields.

---

## 9. UI

None in this slice. No page shows the test. The entry points are TR5's.

### 9.1 Seeds and hosting

No change to `TestSeed`, the demo seed, AppHost or the VPS. The functional tests write their own Transactions and rates.

What the stored history must cover is wider than for the tax year read (tax-year-events §9.1), and it is not checked by the read:

- A purchase from an NZD Account needs no rate, however old it is.
- A purchase from an Account in another currency, an arrival and a correction of an Instrument quoted in another currency need the rate row of their date. `MarketDataHistory:FloorDate` must be on or before the earliest such date of the Customer, less the 4 days of the age limit; otherwise the tax cost is unknown from that event on (`NoRate`) and the test is incomplete.
- In Development the floor date is 2026-01-01. A Customer who bought foreign shares from a USD Account before 2026 has no complete test there.
- In the demo tenant the catalog is classified from tax year 2027 (demo-tenant-seed), and the threshold has a value up to tax year 2026 (R15). No tax year gives a complete test there until the row for 2027 is added.

---

## 10. Tests

| Project | Where |
| --- | --- |
| Domain.UnitTests | `Tax/FifThresholdTests.cs`, `Tax/TaxCostLedgerTests.cs` (trades, example 1), `Tax/TaxCostLedgerOtherTests.cs` (arrivals, departures, corrections, the unknown causes, the rate keys), `Tax/ThresholdTestTests.cs` (examples 2 and 3), `Tax/ThresholdTestReasonTests.cs`, `Tax/CustomerTaxYearCostTests.cs`, with examples 2 and 3 in `Tax/ThresholdExamples.cs` |
| Application.FunctionalTests | `TaxYears/GetCustomerTaxYearThresholdTests.cs`; `TaxYears/GetCustomerTaxYearAccessTests.cs` (the totals of two tenants); `TaxYears/GetCustomerTaxYearTests.cs` (the new fields and the notice) |

| Project | Assert |
| --- | --- |
| Domain.UnitTests (`Tax/FifThresholdTests`) | 50,000 for 2009, 2025 and 2026; no value for 2027 |
| Domain.UnitTests (`Tax/TaxCostLedgerTests`) | Example 1 to the cent, both years; an acquisition adds principal and fee, from an NZD Account as booked and from a USD Account at the rate of the day; a disposal removes the average with the rounding of R7, and a disposal of everything leaves 0.00; a `Reorganisation`, a `Dividend` and a `Move` change nothing; a reversed purchase is not in the walk; a reinvested dividend adds the reinvested amount; an arrival with `acquiredOn` uses that date's rate, one without uses the date of `bookedAt`, one quoted in NZD is used as it is; a carried cost of 0 is `NoCarriedCost`; a departure removes the average; a correction applies both deltas, and one that leaves a cost below 0 is `NegativeCost`; `NoRate` and `StaleRate`; a disposal of more than is held is `NegativeQuantity`; unknown stays unknown until the quantity is 0, and the cost is then 0.00 and known; the cost is one average over two Accounts |
| Domain.UnitTests (`Tax/ThresholdTestTests`) | Example 2: `Above` in 2022, 2023 and 2024, `NotAbove` in 2025, with each opening, peak and closing; example 3: the peak 50,046.40 at U4, and 49,935.90 and `NotAbove` at a rate of 1.7000; a total of exactly 50,000.00 is `NotAbove` and 50,000.01 is `Above`; an exempt and a `NotForeign` holding are not counted and need no rate; a buy and a sell on one day show in the peak; the peak is the first point of a tie; `NotClassified`, `CostUnknown`, `TreatmentChanged` and `NoThreshold` each make the test incomplete and never `NotAbove`; an incomplete test whose known costs are above is `Above`; `TreatmentChanged` when an earlier year of the holding was exempt, not when the quantity was 0 in between, and not for a `NotClassified` year; a Customer with nothing: 0.00, `NotAbove`, complete; the steps add up to the totals |
| Domain.UnitTests (`Tax/CustomerTaxYear*Tests`) | The closing cost of 2025 equals the opening cost of 2026; `complete` of a tax holding and of the tax year do not change when the test is incomplete; the notice `CarriedCost`, also for an arrival of an earlier year; the existing cases are unchanged |
| Application.FunctionalTests (`TaxYears/GetCustomerTaxYearThresholdTests`) | Example 3 through the API on two Accounts, the fields of §8.1; example 2 over its four tax years; a purchase in an earlier tax year from a USD Account with no stored rate gives `CostUnknown` and `Unknown`; an entry backdated on the External account into an earlier tax year changes the test of the next read; a reversed purchase that would have crossed the threshold leaves `NotAbove` |
| Application.FunctionalTests (`TaxYears/GetCustomerTaxYearAccessTests`) | Unchanged cases, green. Two-tenant fixture: no Transaction or rate of the other Tenant's Customer is in any total, and the other Tenant's ids → 404 |

No Application.UnitTests: there is no new check. No Infrastructure.IntegrationTests: there is no script and no mapping.

---

## 11. Locked in this spec

No new ADR.

| Item | Lock |
| --- | --- |
| Tax cost | Per tax holding, in NZD, from the Customer's first event, over all Accounts; never a market value |
| Acquisition | `principal + fee` in NZD, as the tax year read converts them |
| Disposal and departure | The average cost of the tax holding, rounded as buy-sell R15 |
| Arrival | The carried cost at the rate of `acquiredOn`, else of the UTC date of `bookedAt`; a carried cost of 0 is not stated |
| Reorganisation, dividend, move | No change |
| Correction | Both deltas as stated |
| Unknown | `null`, with its first cause, until the quantity is 0 |
| Threshold | NZ$50,000 for tax years 2009 to 2026; no value from 2027 until amended |
| Test | Attributing tax holdings of the year; the total at the opening and after every event; above means more than |
| Incomplete | The four reasons of §4.3; never `NotAbove`; `Above` when the known costs alone are above; apart from `complete` of the tax year |
| Where | Fields of `GET /users/customers/{customerId}/tax-years/{taxYear}` |
| Routes, scripts, permissions, problem codes, events, ports, jobs, settings | None |

### Points confirmed

Confirmed on 2026-11-28, every one as recommended. Each point states the choice, written into the rules above, and the alternative that was not taken.

1. **The cost and the test are fields of the tax year read** (R1, R24). Recommended: they are worked out from the same events, in one read of the ledger with one `ledgerReadAt`, and roadmap TR3 allows "in the result of the TR2 read or beside it". Alternative: a route of its own (`…/tax-years/{taxYear}/threshold`), which reads the ledger a second time and may show another state of it.
2. **One tax cost per tax holding, over all Accounts** (R3, R7). Recommended: roadmap T1 and Q9 ("the average cost in NZD of that interest"); a move then needs no rule. Alternative: an average per Account, as the ledger keeps it, which makes the cost a sale removes depend on which Account sold.
3. **A disposal removes the average cost** (R7). Recommended: roadmap Q9; it is the ledger's own cost rule, and the one worked example of the sources agrees with it. Alternative: first in first out, which needs lots (roadmap §7). The Act was not read (§14).
4. **An acquisition adds principal and fee** (R6). Recommended: roadmap Q10 and S2 ¶8, which says so for this test in so many words.
5. **An arrival brings the cost it carries, at the rate of `acquiredOn` or of the day it was booked, with a notice** (R9, R25). Recommended: roadmap Q8. The cost: one rate stands for purchases made over years, and a migrated holding takes the rate of the day the batch was booked. Alternative: no cost from an arrival, and every such holding makes the test incomplete until its history is recorded in an External account.
6. **A carried cost of 0 is "not stated"** (R9). Recommended: roadmap TR3, "a holding with no cost in the ledger ... makes the test incomplete and not passed"; the write requires a number, and 0 is what a caller types who does not know it. Alternative: 0 is a cost, and a holding entered without one never counts toward the threshold.
7. **A departure removes the average, and a move changes nothing** (R8, R10). Recommended: roadmap Q8 ("a departure mirrors it") and T1.
8. **A correction applies its two deltas as stated** (R11). Recommended: an adjustment is the caller's number in the ledger too, and its notice is already on the tax holding. Alternative A: any correction makes the tax cost unknown, so a person must look. Alternative B: a correction that lowers the quantity removes the average, and its `costChange` is ignored.
9. **A tax cost only for attributing and not classified tax holdings** (R4). Recommended: as the values of tax-year-events R23; an exempt interest is not in the test, and no rate is read for it. Alternative: a tax cost for every tax holding, which TR6 does not need either.
10. **The total is read after every event, and its point is an instant** (R17, R18). Recommended by the roadmap. A New Zealand date needs the zone data that tax-year-events point 1 chose not to depend on; TR5 decides how the report prints the day. Alternative: the end of each New Zealand day.
11. **The threshold has no value from tax year 2027 until the Bill is law** (R15). Recommended: the figure of a Bill is not a threshold (roadmap T5, T6), no read can ask for tax year 2027 before 2 April 2027, and the row is one line of an amendment. Alternative A: write NZ$100,000 from 2027 now, as the Bill's commentary states it. Alternative B: NZ$50,000 with no end, which is wrong for 2027 if the Bill passes as introduced.
12. **Above means more than the threshold** (R19). Recommended: S1 p.10 and S2 ¶8. S1 p.5 and the Bill's commentary also say "below" and "less than" (see "Found while writing").
13. **An incomplete test does not make a tax holding or the tax year incomplete** (R20). Recommended: roadmap TR4, "TR3 decides whether the result applies, not how it is computed". Alternative: one `complete` for everything.
14. **An incomplete test may still say `Above`** (R21). Recommended: a cost is never negative, so the known costs are a floor, and a Customer with one unclassified holding beside NZ$200,000 of classified ones is above whatever it cost. Alternative: `Unknown` whenever a reason applies.
15. **A treatment that changed while the shares were held makes the test incomplete** (R22). Recommended: the cost should start again from a market value the platform does not work out (roadmap §7), and a wrong cost here decides whether everything else applies. Alternative A: a notice, and the historic cost counts. Alternative B: no mark.
16. **The option for an interest acquired before 2000 is not offered** (R23). Recommended by the roadmap: it is a choice, it needs the market value of 1 April 2007, and no rate or price that old is stored.
17. **The average uses quantities as booked** (R5, R7). Recommended: the fraction of a disposal is the quantity sold over the quantity held, summed over the Accounts as they stand. The cost: between the two postings of one split on two Accounts the sum mixes two bases, and a sale in that window removes a wrong share of the cost (see "Found while writing"). Alternative: comparable quantities over the whole walk, restated to the end of tax year `Y`.
18. **Unknown lasts until nothing is held** (R12, R13). Recommended: nothing later can repair an average that was never known. Alternative: unknown for good, also after a full sale and a new purchase.

### Found while writing

- **The Act was not read.** Section CQ 5 and the sections on cost refused automated reads again (403). Roadmap Q9 asked for the Act; R7 rests on roadmap Q9, on S2 Example 1 and on S3 ¶92, which is about taxable sales and not about the threshold.
- **The guides word the threshold four ways.** S1 p.5: "below $50,000 throughout the year". S1 p.9: "does not exceed the $50,000 threshold at any time in the year". S1 p.10: "equal to or less than $50,000 at all times in the year". S5: "a cost of less than $50,000" and, two paragraphs on, "$50,000 or less". R19 follows the three that agree: exactly $50,000 is not above.
- **The Bill was read from a primary source.** Roadmap §2 and S10 had the NZ$100,000 threshold from press summaries only. Inland Revenue's commentary on the Bill (S5) states it: "The proposed amendment would take effect on 1 April 2026 for the 2026–27 and later income years." Inland Revenue's page showed the Bill at select committee on 12 October 2026. Whether it has passed since was not found.
- **Inland Revenue's example buys on 31 March.** Jax invests "on 31 March 2022" and is in the rules for the 2022 income year, although the shares were held for one day of it. On the platform that day ends at 11:00 UTC on 31 March (tax-year-events R3); example 2 books the purchase at 22:00 UTC on 30 March.
- **A split booked on two days.** A holding on two Accounts is split on each Account at its own time (tax-year-events §1.1). Between the two postings, a sale of 10 shares on the Account not yet split is 10 of a sum that already counts the other Account doubled, so it removes too little cost. The window is the days between the two postings; nothing marks it. Point 17 has the alternative.
- **Rounding drifts by cents.** Each disposal rounds what it removes. After many partial sales the tax cost can differ by a few cents from the cost of the shares left, worked out in one step. The projection in the quote currency behaves the same way (buy-sell R15).
- **The cost of the ledger and the tax cost are two numbers.** `Holdings` keeps an average per Account in the quote currency; this slice keeps one per Customer in NZD. Neither can be worked out from the other, and no page shows the second yet.
- **A change of owner brings the value at the transfer.** in-kind-transfers R25 says the cost of a transfer in is the value at the transfer when ownership changes. R9 takes it as stated. Whether that is the cost for the threshold in every such case (a gift, an inheritance) was not checked against the Act.
- **Roadmap §2 "Cost for the threshold"** cites S1 p.23 for the cost of a split and of an interest bought before 2000. Both are in the glossary of the April 2026 guide on that page, and the same entry adds one more case this slice leaves out: an interest that was exempt under section EX 39 until the 2013 income year may use its market value at the start of that year.

Still open, not this slice:

- FIF income, the quick sale adjustment and its own average cost: TR4
- The report, what it prints for each outcome, and the day of the peak in New Zealand time: TR5
- The threshold of tax year 2027 and later, once the Bill is law (R15)
- The deemed sale and purchase at market value when a treatment changes (R22), and the pre-2000 option (R23)
- An NZD cost or several dated costs stated on an arrival, and tax lots
- A run over every Customer of a Tenant, and whether the rate rows of the walk are then read in one query: TR5
- Ledger replay and period close (roadmap §1.2)

---

## 12. Changes to other docs

Each doc was changed in place when this spec was accepted (2026-11-28).

| Doc | Change |
| --- | --- |
| [tax-year-events.md](tax-year-events.md) | §2 Out: the cost and the threshold test are this spec's, no longer "TR3". R14: the carried cost is converted by R9 here. R28 and its table: the notice `CarriedCost`. R31: the fields of R24 here. §5 and §7: `CustomerTaxYear.Threshold`, the wider read of `CustomerTaxLedger`. "Still open": the first line goes. A History section with its first row |
| [holdings.md](holdings.md), [buy-sell.md](buy-sell.md) | Holdings R10 and buy-sell R15: the cost on a holding stays in the quote currency per Account; the tax cost is a second figure, per Customer in NZD, with the same release rule (R7 here). History rows |
| [in-kind-transfers.md](in-kind-transfers.md) | §2 Out and R25: the cost and `acquiredOn` of a transfer in are read by R9 here; "no rule converts them until TR3" goes. History row |
| [migration-batches.md](migration-batches.md) | §2 Out and "Still open": a migrated holding's cost is converted at the rate of the day it was booked (R9 here); tax lots and acquisition dates stay out |
| [adjustments.md](adjustments.md), [split.md](split.md) | A sentence each: a correction changes the tax cost by its `costChange` (R11); a reorganisation keeps it (R8) |
| [instrument-tax-attributes.md](instrument-tax-attributes.md) | The table of treatments ("In the FIF calculation and the threshold") and "Still open": the threshold reads the treatment of the year (R16), and a treatment that changed while held makes the test incomplete (R22) |
| [market-data-history.md](market-data-history.md) | R2: what the floor date must cover for the tax cost (§9.1 here). History row |
| [api-design.md](../api-design.md) | §7.4: the tax year item carries the tax cost and the threshold test. Change log |
| [domain-model.md](../domain-model.md) | §8.24: `FifThreshold`, `TaxCostLedger`, `TaxCost`, `ThresholdTest` and the invariants of §5. Change log |
| [architecture.md](../architecture.md) | §9: `CustomerTaxLedger` also reads the rates of the cost walk |
| [glossary.md](../glossary.md) | Tax cost, Threshold test and FIF threshold become accepted. "Tax notice" lists the carried cost. "Ledger replay": the cost in NZD of the threshold test is a read |
| [fif-roadmap.md](../roadmaps/fif-roadmap.md) | §6 TR3: the spec line. §8: Q8 (the cost) and Q9 written as rules here. §9 row 3: accepted. §3 "Cost" and "Holdings that arrive without a purchase": what the read now says |
| [custodial-roadmap.md](../roadmaps/custodial-roadmap.md), [valuation-roadmap.md](../roadmaps/valuation-roadmap.md) | "Tax lots and NZD cost": the cost in NZD exists as a read for the threshold test; tax lots stay out |
| [features/README.md](README.md), [future-plans.md](../future-plans.md) §1 step 6 | Status: TR3 accepted |

---

## 13. Suggested commits

Removed when the slice landed (2026-11-28). The commits are in git.

---

## 14. Sources

Read on 2026-11-28. S1 and S2 are the codes of [fif-roadmap.md](../roadmaps/fif-roadmap.md) §11; S3 and S5 are new here.

| # | Source | Used for | Read |
| --- | --- | --- | --- |
| S1 | Inland Revenue, *Guide to foreign investment funds*, IR461, April 2026 edition, the PDF linked from `ird.govt.nz/fif` (page last updated 31 March 2026) | "below $50,000 throughout the year" (p.5); the threshold table, "at any time in the year", "on any day in the year", "the first $50,000 is not exempt", joint ownership (p.9); "equal to or less than $50,000 at all times in the year" (p.10); a change as a deemed disposal and acquisition at market value (p.22 note 8); the cost of an interest from a split or a non-taxable bonus, for a non-monetary cost, and acquired before 1 January 2000 (p.23) | Yes, in full text |
| S2 | Inland Revenue, fact sheet IS 24/10 FS 1, *Income tax – Share investments (whether the FIF rules apply)*, 18 December 2024 (`taxtechnical.ird.govt.nz`) | Exempt interests are not added (¶7); "at any time during the income year", the cost in New Zealand dollars when acquired, "including any transaction fees", not the market value (¶8); joint holdings (¶9); Example 1, Jax, over the 2022 to 2025 income years | Yes, in full text |
| S3 | Inland Revenue, interpretation statement IS 24/10, *Income tax – Share investments*, 18 December 2024 (`taxtechnical.ird.govt.nz`) | The cost "calculated in New Zealand dollars at the time the shares ... are purchased" (¶29); first in first out or weighted average cost where the cost of shares sold cannot be traced, section ED 1(5), for taxable sales (¶92); the threshold and its conversion example (¶115, ¶116); Example 15, the same as S2 Example 1 | The paragraphs named |
| S5 | Inland Revenue, Policy, *Taxation (Annual Rates for 2026–27, FBT Simplification, Foreign Investment Funds, and Remedial Measures) Bill – Bill commentary*, issued 10 September 2026 (`taxpolicy.ird.govt.nz`), "Lift de minimis threshold", p.48–49; and the Bill's page there, "as at 12 October 2026" | The threshold of NZ$100,000 "for the 2026–27 and later income years"; "for the 2025–26 and earlier income years, the relevant total cost would be $50,000 or less"; the Bill's stage, select committee | The section named |
| — | Hon Simon Watts, "Tax system being strengthened", Budget 2026 release, 28 May 2026 (`budget.govt.nz`) | "Budget 2026 also raises the FIF de minimis threshold for overseas investments from $50,000 to $100,000" | Yes |
| — | Income Tax Act 2007, section CQ 5 and the sections on the cost of an attributing interest | The threshold and its cost in the Act's own words; which cost a disposal removes | Not read: `legislation.govt.nz` and NZLII refused automated reads (403) |

What follows from the refusal: R7 (the average cost) follows roadmap Q9 and the two guides; R9 (the cost of an arrival) follows roadmap Q8; R22 follows the guide's one sentence on a change. Each is a point of §11. The threshold itself, the fee in the cost, the NZD cost at the time of purchase and "at any time" are stated by Inland Revenue in the sources read.
