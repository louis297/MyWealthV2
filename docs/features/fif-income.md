---
title: "FIF income: FDR, the quick sale adjustment and CV"
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-28
last_updated: 2026-11-28
related:
  - ../roadmaps/fif-roadmap.md
  - ../future-plans.md
  - tax-year-events.md
  - tax-cost-threshold.md
  - instrument-tax-attributes.md
  - account-valuation.md
  - buy-sell.md
  - trade-fees.md
  - split.md
  - corporate-actions.md
  - dividends.md
  - external-income.md
  - in-kind-transfers.md
  - migration-batches.md
  - adjustments.md
  - custody-mode-grouping.md
  - interest-run.md
  - market-data-history.md
  - ../domain-model.md
  - ../api-design.md
  - ../glossary.md
---

# FIF income: FDR, the quick sale adjustment and CV

Step 4 of [fif-roadmap.md](../roadmaps/fif-roadmap.md) §9. It covers roadmap item **TR4** and writes the decisions **Q11** (the actual gain of a quick sale) and **Q12** (an interest where FDR is not allowed) as rules, and, for the two methods, **Q8** (a holding that arrives without a purchase) and **Q10** (trade fees). It computes over the tax year of [tax-year-events.md](tax-year-events.md) and adds nothing to the ledger.

The FIF rules tax an individual on an income figure worked out from market values, not on the dividends received. An individual with listed ordinary shares works the figure out under one of two methods, and may choose each year. The **fair dividend rate (FDR)** method takes 5 % of the market value held at the start of the tax year, and adds a **quick sale adjustment** for shares bought and sold within the year. The **comparative value (CV)** method takes the change in market value over the year, with what was received and less what was spent. This slice is the calculation: per attributing tax holding and in total, the FDR income with its quick sale adjustment, the CV income, and which total is lower.

It is a Domain function with no I/O (roadmap T7). It reads no price, no rate and no Transaction: every input is a figure the tax year read already gives. Nothing is stored and no job runs. The platform computes both methods and chooses neither (roadmap T8).

New: fields on the item of `GET /users/customers/{customerId}/tax-years/{taxYear}` (`fifIncome` on the item and on each tax holding); Domain types `FairDividendRate`, `FifIncome`, `TaxHoldingIncome`, `QuickSale` and their enums; `CustomerTaxYear.Compute` carries them. No route, script, table, permission, problem code, domain event, `TransactionType`, port, job or setting. No write changes. `CustomerTaxLedger` reads nothing more than it does today. No portal page and no simulator change.

**Status is `accepted` (2026-11-28).** The eighteen points of §11 were confirmed on 2026-11-28, every one as recommended, and are written into the rules. The changes to the docs listed in §12 were made when it was accepted. It is a build contract. Sections EX 51, EX 52 and EX 54 and the calculator were still not checked when it was accepted; they stay open (§11). Landed 2026-11-28.

Notes from landing:

- §5: `FifIncome.Compute(taxYear, holdings, decimalPlaces)` returns the totals and `Holdings`, the income of each tax holding in the order it was given. `CustomerTaxYear.Compute` puts each on `TaxHolding.Income`; a `TaxHolding` built outside `Compute` has none. The per-holding figures are the records `FifFdr`, `QuickSale` (with `QuickSaleMatch`, `QuickSaleSale`, `QuickSaleDividendShare`) and `FifCv`; the totals `FifFdrTotals` and `FifCvTotals`; a reason of R23 `FifIncomeIncomplete`.
- R7: `FairDividendRate.For` throws for a tax year before 2009, which cannot be read (tax-year-events R4).
- R11: the average cost is rounded to 8 places away from zero.
- R13: a purchase whose comparable change is not above 0 puts nothing on the stack.
- R14: the proceeds are rounded per sale, and a dividend share per dividend; `dividendShares` lists only shares above 0.
- R23: `incomplete` follows the order of the tax holdings in Domain (by Instrument), not the order of `holdings` in the answer (by symbol).
- R26: an NZD figure of 0 is written with NZD's places (`0.00`), as every other NZD figure.
- §10: in example 3 through the API, the Instruments are quoted in NZD on one NZD External account; the trade prices have up to three places, so that each principal is the figure of §1.1; the dividends go through `POST /transactions/dividend` with 15 % foreign tax; the first purchase of `QQQ` is in tax year 2025. The reversed purchase and the backdated entry each use an Instrument of their own; the backdated entry is in tax year 2025 and changes the FDR amount of 2026. S1 p.14's total of 15,000 is asserted in `FifIncomeTotalsTests`.
- tax-year-events: the field list of the item in its tests gains `fifIncome`.

The tax statements in this spec were read again from Inland Revenue on 2026-11-28 (§14). The Income Tax Act refused automated reads again, so sections EX 51, EX 52 and EX 54, which roadmap Q11 asked for, were not read; §14 says which rules rest on the guide and the commentaries alone. Nothing here is tax advice.

---

## 1. Summary

| | The tax year read today | This slice |
| --- | --- | --- |
| The opening and the closing value | Given, per tax holding, in NZD | The base of FDR, and the two ends of CV |
| Purchases, sales and dividends | Tax events, each with an NZD amount | Costs and gains of CV; the purchases and sales of a quick sale |
| The peak quantity | Given, on the basis of the year's end | The peak holding differential |
| A quick sale | — | Sales matched to the purchases of the same tax year, last bought first sold |
| FIF income | — | Per attributing tax holding and in total, under FDR and under CV |
| Which method | — | Both are shown, and which total is lower. No method is chosen |
| A tax holding that is incomplete | Said, with its reasons | Not computed. The totals are then not given: no partial sum takes their place (roadmap T5) |

The rule that distinguishes this slice: it adds no fact. Every figure is arithmetic over figures the tax year read shows with their prices, rates and dates, so a reader can work each one out again from the answer itself.

### 1.1 Worked examples

Roadmap T12 asks for worked examples before the spec. Example 1 continues the worked example of tax-year-events. Examples 2 and 3 are made up. The published examples of Inland Revenue follow them. The tests use all of them as written. NZD has 2 decimal places.

**Example 1: the worked example of tax-year-events §1.1, continued.** One Customer, two Accounts, one Instrument `AAA`, `Attributing`. Every figure below is one that spec gives.

| | Tax year 2025 | Tax year 2026 |
| --- | --- | --- |
| Opening value | 0.00 | 13,108.48 |
| Closing value | 13,108.48 | 13,026.08 |
| Purchases | 8,708.70 + 3,647.00 = 12,355.70 | 37.63 (reinvested) + 1,046.65 = 1,084.28 |
| Sales | — | 2,789.60 |
| Dividends, gross | — | 59.15 + 58.80 = 117.95 |
| Comparable quantity: opening, peak, closing | 0, 140, 140 | 280, 280, 240.8 |
| FDR amount | 0.05 × 0.00 = 0.00 | 0.05 × 13,108.48 = 655.42 |
| Peak holding differential | lesser of 140 − 0 and 140 − 140: 0 | lesser of 280 − 280 and 280 − 240.8: 0 |
| Quick sale adjustment | 0.00 | 0.00 |
| **FDR income** | **0.00** | **655.42** |
| **CV income** | (13,108.48 + 0.00) − (0.00 + 12,355.70) = **752.78** | (13,026.08 + 2,789.60 + 117.95) − (13,108.48 + 1,084.28) = **1,740.87** |
| Lower | FDR | FDR |

What the example shows:

- **A share bought during the year has no opening value** (R8). In tax year 2025 the Customer pays 12,355.70 for shares and FDR gives no income, because nothing was held at the start (S1 p.22 note 6).
- **A sale is not a quick sale by itself** (R10). In tax year 2026 the Customer sells 30 shares and later buys 20.8. The sale came first, so it follows no purchase of the year, and the holding never rose above what it was at the start.
- **The dividend is a gain under CV at its gross** (R17): 59.15, of which 8.87 of foreign tax and 10.65 of RWT never reached the Account. Under FDR it is not added (R8).
- **A reinvested dividend is a gain and a cost** (R6): the dividend of 58.80 and the purchase of 37.63.
- **The split and the move change nothing** (R6). The quantities are compared on the basis of the year's end, and the 80 shares moved between the two Accounts are neither sold nor bought.

**Example 2: a quick sale across a split.** One Customer, tax year 2026, one NZD External account. `QQQ` is `Attributing` and, as in tax-cost-threshold example 2, quoted in NZD, so that every figure is the one booked. The Customer held 1,000 shares at the start of the year, bought in an earlier tax year. Stored prices: 20.00 for 31 March 2025; 10.50 for 31 March 2026, after a 2 for 1 split.

| # | `bookedAt` | Tax event | Quantity | Comparable | After | NZD |
| --- | --- | --- | --- | --- | --- | --- |
| — | | Opening | 1,000 | 2,000 | 2,000 | value 20,000.00 |
| 1 | 10 Apr 2025 01:00 | `Acquisition` | +400 | +800 | 2,800 | 8,790.00 and a fee of 10.00: 8,800.00 |
| 2 | 20 May 2025 01:00 | `Dividend`, 1,400 shares at 0.50 | 0 | 0 | 2,800 | gross 700.00, foreign tax 105.00 |
| 3 | 15 Jun 2025 01:00 | `Acquisition` | +200 | +400 | 3,200 | 4,990.00 and a fee of 10.00: 5,000.00 |
| 4 | 1 Jul 2025 00:00 | `Reorganisation`, 2 for 1 | +1,600 | 0 | 3,200 | — |
| 5 | 10 Aug 2025 01:00 | `Disposal` | −1,000 | −1,000 | 2,200 | 13,015.00 less a fee of 15.00: 13,000.00 |
| 6 | 5 Sep 2025 01:00 | `Dividend`, 2,200 shares at 0.30 | 0 | 0 | 2,200 | gross 660.00, foreign tax 99.00 |
| 7 | 3 Nov 2025 01:00 | `Disposal` | −500 | −500 | 1,700 | 6,010.00 less a fee of 10.00: 6,000.00 |
| 8 | 12 Feb 2026 01:00 | `Acquisition` | +500 | +500 | 2,200 | 4,890.00 and a fee of 10.00: 4,900.00 |
| — | | Closing | 2,200 | 2,200 | 2,200 | value 2,200 × 10.50 = 23,100.00 |

The peak holding method amount (R10–R12):

```text
peak holding differential   lesser of 3,200 - 2,000 = 1,200 and 3,200 - 2,200 = 1,000        1,000
shares acquired             800 + 400 + 500                                                  1,700
cost of the acquisitions    8,800.00 + 5,000.00 + 4,900.00                               18,700.00
average cost                18,700.00 / 1,700                                                11.00
amount                      0.05 x 1,000 x 18,700.00 / 1,700                                550.00
```

The quick sale gain amount (R13, R14). Each sale takes the shares bought most recently in the year first:

| Sale | Takes | Matched | Of the proceeds |
| --- | --- | --- | --- |
| 5: 1,000 | 400 of purchase 3, then 600 of purchase 1 | 1,000 of 1,000 | 13,000.00 |
| 7: 500 | the last 200 of purchase 1; the other 300 were held at the start | 200 of 500 | 6,000.00 × 200 ÷ 500 = 2,400.00 |

```text
proceeds of the matched shares   13,000.00 + 2,400.00                                    15,400.00
dividend 2 (20 May)              800 matched shares held of 2,800:  700.00 x 800 / 2,800     200.00
dividend 6 (5 Sep)               200 matched shares held of 2,200:  660.00 x 200 / 2,200      60.00
cost of the matched shares       1,200 x 18,700.00 / 1,700                               13,200.00
quick sale gain amount           15,400.00 + 260.00 - 13,200.00                           2,460.00
```

| | |
| --- | --- |
| FDR amount | 0.05 × 20,000.00 = 1,000.00 |
| Quick sale adjustment, were `QQQ` the only tax holding | lesser of 550.00 and 2,460.00: 550.00 |
| FDR income | 1,550.00 |
| CV income | (23,100.00 + 13,000.00 + 6,000.00 + 700.00 + 660.00) − (20,000.00 + 18,700.00) = 4,760.00 |

What the example shows:

- **Shares are counted on the basis of the year's end** (R9). The 400 shares bought in April are 800 after the split, and the 1,000 sold in August are compared with them like with like. As booked, the Customer would have bought 600 shares and sold 1,000 of them.
- **The average cost is over every purchase of the year** (R11), the one of February too, which came after both sales.
- **The two amounts count different shares** (R10, R13). The peak holding method counts 1,000: the holding rose by 1,200 and ended 1,000 below its peak. The gain counts the 1,200 shares that were bought in the year and sold in it.
- **A sale is matched only as far as it follows purchases of the year** (R13). 300 of the 500 shares sold in November were held at the start of the year. They are not a quick sale, and their part of the proceeds is not in the gain.
- **A dividend counts for the matched shares that were held when it was paid** (R14). The shares bought in June earned no part of the May dividend.
- **Last bought, first sold decides which shares those are** (R13). Had the August sale been of 300 shares and the only one, it would take 300 of the June purchase, and no dividend would count. First bought, first sold would take the April purchase, and count 700.00 × 300 ÷ 2,800 = 75.00.

**Example 3: two tax holdings, and one that is not computed.** The same Customer also holds `RRR`, `Attributing`, quoted in NZD: nothing at the start; 300 bought for 9,000.00; 100 sold for 900.00 after the price fell; 200 held at the end, worth 1,600.00.

| | `QQQ` | `RRR` | Total |
| --- | --- | --- | --- |
| Opening value | 20,000.00 | 0.00 | 20,000.00 |
| FDR amount | 1,000.00 | 0.00 | 1,000.00 |
| Peak holding method amount | 550.00 | 0.05 × 100 × 30.00 = 150.00 | 700.00 |
| Quick sale gain amount | 2,460.00 | 900.00 − 100 × 30.00 = −2,100.00 | 360.00 |
| Quick sale adjustment | | | lesser of 700.00 and 360.00: **360.00** |
| **FDR income** | | | 1,000.00 + 360.00 = **1,360.00** |
| CV amount | 4,760.00 | (1,600.00 + 900.00) − (0.00 + 9,000.00) = −6,500.00 | −1,740.00 |
| **CV income** | | | not below zero: **0.00** |
| Lower | | | CV |

What the example shows:

- **The quick sale adjustment is the lesser of two totals** (R20), not a total of lessers. The loss on the quick sale of `RRR` comes off the gain on `QQQ`. Taken per tax holding, the adjustment would be 550.00 + 0.00.
- **The CV total is not below zero** (R21). The loss of 1,740.00 gives an income of 0.00 and cannot be used elsewhere (S1 p.14).
- **A third tax holding, `SSS`, `AttributingNoFdr`**, would be listed as not computed with the reason `FdrNotAllowed` (R3). The totals above would stay as they are, and `complete` would be `false`: the totals cover the tax holdings for which the Customer may choose between the two methods, and the income of `SSS` is not among them (R23).

**Published examples.** Each gives these results through the rules of §4. The sources are in §14.

| Source | Case | Result |
| --- | --- | --- |
| S1 p.14 | Three companies, each worth $100,000 at the start, and $102,000, $110,000 and $80,000 at the end. No dividends, no change in the number of shares | FDR $5,000 each, total $15,000. CV $2,000, $10,000 and −$20,000: −$8,000, "reduced to 0" |
| S1 p.15 | 10,000 shares worth $200,000 at the start. 5,000 bought for $110,000 on 1 October, 4,000 sold for $100,000 on 1 December, 2,000 bought for $44,000 on 23 December | Peak holding differential 2,000. Average cost $154,000 ÷ 7,000 = $22. Peak holding method amount $2,200. Actual gain $100,000 − 4,000 × $22 = $12,000. Quick sale adjustment $2,200. FIF income $12,200 |
| S2 Example 1 | "Jax" buys shares for $55,000 on 31 March 2022, worth $59,125 on 31 March 2023. No dividends, no sales | 2022 income year: "no income will arise as the opening value on 1 April 2021 was zero and there were no quick sales". 2023 income year: FDR $2,750, CV $4,125 |
| S8 Examples 1 to 3 | $100,000 at the start. Example 1: $115,000 at the end and a dividend of $10,000. Example 2: $102,000 and $1,000. Example 3: $75,000 and $10,000 | Example 1: CV $25,000, FDR $5,000. Example 2: CV $3,000, below FDR. Example 3: a loss of $15,000, no income |
| S8 Example 4 | Co.A: 10,000 shares worth $30,000 at the start; 1,000 bought for $4,000 and 4,000 for $20,000; a dividend of $1,000 on 15,000 shares; 3,000 sold for $15,000; worth $48,000 at the end. Co.B: worth $50,000 at the start and $55,000 at the end, a dividend of $2,000 | Average cost $4.80. Peak holding method amount $720. Dividend share 3,000 ÷ 15,000 × $1,000 = $200. Quick sale gain ($15,000 + $200) − $14,400 = $800. Quick sale adjustment $720. FDR $4,000 + $720 = $4,720. CV $17,000 |
| S8 Example 5 | Co.A: 20,000 shares worth $120,000; 5,000 bought for $30,000; a dividend of $2,000 on 25,000 shares; 2,000 sold for $14,000. Co.B: 10,000 shares worth $80,000; 5,000 bought for $40,000; 3,000 sold for $18,000 | Peak holding method amounts $600 and $1,200: $1,800. Quick sale gains $2,160 and −$6,000: −$3,840, taken as zero. Quick sale adjustment $0. FDR $10,000 |

---

## 2. Scope

**In**

- Domain: `FairDividendRate` (the rate of a tax year); `FifIncome.Compute` (the income of each tax holding and the totals); `FifIncome`, `TaxHoldingIncome`, `QuickSale`, `FifIncomeStatus`, `FifIncomeReason`, `FifLowerMethod`; `CustomerTaxYear.Compute` carries them
- Application: the item of `GetCustomerTaxYear` gains the fields of R26. `CustomerTaxLedger` does not change
- HTTP: no new route. The item of `GET /users/customers/{customerId}/tax-years/{taxYear}` gains `fifIncome` and `holdings[].fifIncome`
- Tests listed in §10

**Out**

- The choice of a method. Both are computed and both are shown (roadmap T8)
- Whether the FIF rules apply to the Customer: the threshold test ([tax-cost-threshold.md](tax-cost-threshold.md)), tax residency, a transitional resident, the choice to apply the rules below the threshold (roadmap Q14). The income is computed whatever the test says (R2)
- The CV income of a tax holding for which FDR is not allowed, with its loss allowed (roadmap Q12; R3)
- The other methods: cost, deemed rate of return, revenue account, attributable FIF income (roadmap §7). The periodic FDR method of unit valuers
- Tax payable, the foreign tax credit allowed, the limit on an imputation credit, a refund of RWT, the IR3 and IR1261 forms (roadmap §7). The foreign tax, the RWT and the credits of each dividend are in the tax year read as booked; the report lists them per tax holding and issuer country beside the income ([fif-report.md](fif-report.md) R22)
- Foreign income tax the Customer pays in another country on their own, a fee rebate, interest on money borrowed to buy shares, and any other figure that is not in the ledger (R18)
- The deemed sale and purchase at market value when a treatment or the Customer's residence changes (roadmap §7), and when a person changes method from one year to the next
- The anti-avoidance rules for purchases and sales that are not at market value (S1 p.22 note 7)
- The report, its PDF and what it prints for each case: [fif-report.md](fif-report.md). The income summary for dividends and interest under the ordinary rules: [income-summary.md](income-summary.md)
- Any stored result, any write, ledger replay and period close (roadmap §1.2)
- Tax lots. The matching of R13 is within one tax year and keeps nothing
- Any portal page

---

## 3. Stories

1. As an Adviser I read, for one of my Customers and one tax year that has ended, the FIF income under the fair dividend rate method and under the comparative value method, per tax holding and in total, and which total is lower.
2. As a Customer I read the same for myself.
3. As a reader I see how a quick sale adjustment was worked out: which sales were matched to which purchases of the year, at which average cost, and with which share of which dividend.
4. As a reader I see which tax holding was not computed and why, so that I never take a total that leaves something out for my FIF income.
5. As a developer I work every figure out by hand from the opening and closing values and the events of the same answer.
6. As roadmap item TR5 I get the figures of both methods and print them, with the prices and rates the tax year read already carries.
7. As an Adviser I cannot read the income of another Adviser's Customer, and a firm cannot read another firm's (404).

---

## 4. Rules

### 4.1 Where it is read

| ID | Rule |
| --- | --- |
| R1 | **Part of the tax year read.** FIF income is a field of the item of `GET /users/customers/{customerId}/tax-years/{taxYear}` and of each of its tax holdings. There is no route of its own. Access, scope, the 401, 403 and 404 cases, and which tax years can be read are those of tax-year-events R1, R4 and R30. No new permission and no new problem code. |
| R2 | **A read only, and whatever the threshold test says.** tax-year-events R2 holds: nothing is written, no lock is taken, and the Customer's tax profile is not read. The income is computed for every tax year that can be read, whether `threshold.outcome` is `Above`, `NotAbove` or `Unknown`: the test decides whether the result applies, not how it is computed (roadmap TR3, TR4), and a person below the threshold may choose to apply the rules (S2 ¶8). An `Overseas` Customer gets the same answer as a `NewZealand` one; what the report does with it is [fif-report.md](fif-report.md) R8: an `Overseas` Customer gets no report. |

### 4.2 Which tax holdings, and what is read

| ID | Rule |
| --- | --- |
| R3 | **By treatment (roadmap Q12).** The income of a tax holding of tax year `Y` has a status. `Attributing`: it is computed under both methods, unless R4 applies. `AttributingNoFdr`: `NotComputed` with the reason `FdrNotAllowed`. The fair dividend rate method cannot be used for it; it must use the comparative value method on its own, where a loss can be claimed, or the deemed rate of return (S1 p.16), and the platform does neither. `NotClassified`: `NotComputed` with the reason `NotClassified`; it may be an attributing interest. `NotForeign`, `ExemptAsxListed` and `ExemptOther`: `NotRequired`; the holding is not in the FIF rules. The treatment is the one of `Y` (tax-year-events R26). |
| R4 | **An incomplete tax holding is not computed (roadmap T5).** An `Attributing` tax holding that is incomplete for a reason of tax-year-events R27 (`OpeningNotValued`, `ClosingNotValued`, `AmountMissing`, `NegativeQuantity`, `NoComparableQuantity`) is `NotComputed` with the reason `HoldingIncomplete`. The tax holding's own `incomplete` says which figure is missing. No figure of either method is given for it, also not one that could be worked out without the missing figure: a tax holding is computed whole or not at all. |
| R5 | **What is read.** Only figures of the tax holding in the tax year read: the opening and the closing value (tax-year-events R22), the events of the year with their NZD amounts (its R10–R14, R18), the comparable quantities and the peak (its R24, R25). No price, no rate and no Transaction is read again, and the tax cost of [tax-cost-threshold.md](tax-cost-threshold.md) is not used: the average cost of R11 is another figure. A notice on the tax holding (`ExternalFigures`, `ArrivalAtMarketValue`, `Move`, `Scrip`, `Correction` and the others) changes nothing here: the figure it marks is used as the read gives it, and the notice stays where it is. |
| R6 | **What each tax event is.** The table below says what an event of the year is for the two methods. A **purchase** adds shares bought in the year and its cost; a **sale** removes shares and brings its proceeds. Trade fees are part of both (roadmap Q10): the cost of an `Acquisition` is its `amount`, `principal + fee`, and the proceeds of a `Disposal` are its `amount`, `principal − fee`. An `Arrival` is a purchase and a `Departure` is a sale, each at its `amount`, the market value of the day (roadmap Q8; tax-year-events R14). Sources: S1 p.11, "expenditure on buying attributing interests" and "amounts received from ... disposing"; S2 ¶24, brokerage is "taken into account in the relevant FIF calculation method"; S8, the average cost is the expenditure incurred "in acquiring or increasing" the shareholding. |

The events of R6:

| Tax event | Quick sale (FDR) | CV |
| --- | --- | --- |
| `Acquisition` | A purchase: its shares, and its `amount` as cost | A cost |
| `Arrival` | A purchase at market value | A cost |
| `Disposal` | A sale: its shares, and its `amount` as proceeds | A gain |
| `Departure` | A sale at market value | A gain |
| `Dividend` | A share of it belongs to the matched shares held when it was paid (R14) | A gain (R17) |
| `Reorganisation`, also a `Scrip` | Neither. The shares of the year are counted on the basis of the year's end (R9) | Nothing |
| `Move` | Neither. The Customer's shares are the same (roadmap T1) | Nothing |
| `Correction` | Neither. The quantity is as the read gives it after the correction; a correction is not a trade (tax-year-events R16) | Nothing |

### 4.3 The fair dividend rate method

| ID | Rule |
| --- | --- |
| R7 | **The rate (roadmap T6).** `FairDividendRate.For(Y)` is a value per tax year, a constant in Domain that changes only with an amendment to this spec, as `FifThreshold` does (tax-cost-threshold R15). It is 0.05 for tax year 2009 and every later one. No change to it was found in any source read. The same rate is used in R8 and in R12. Sources: S1 p.11, p.14, p.15; S2 ¶17. |
| R8 | **FDR amount of a tax holding.** `Round(rate × opening value, dp, AwayFromZero)`, where the opening value is `opening.value` of the tax holding (tax-year-events R22) and `dp` is NZD's `DecimalPlaces`. Shares bought during the year are not in the opening value, so they give no amount, and shares sold during the year are in it in full (S1 p.22 note 6). Dividends, sales and the closing value add nothing: "Dividends and capital gains are not usually taxed separately" (S1 p.14). The amount is never below zero. Sources: S1 p.11, "0.05 multiplied by opening market value"; S8, the closing price of the last day of the year before is accepted as the opening value. |

### 4.4 The quick sale adjustment

The fair dividend rate method looks at one day. Shares bought after the start of a tax year and sold before its end would be held with no income. The quick sale adjustment taxes them: the lesser of an amount worked out from the peak of the holding, and the gain actually made (S1 p.15). R9 to R15 work the two amounts out per tax holding; R20 takes the lesser over all of them.

| ID | Rule |
| --- | --- |
| R9 | **Shares on the basis of the year's end.** Every quantity of R10 to R14 is a comparable quantity (tax-year-events R24). The shares of an event are the change of the tax holding's comparable quantity at it: its `comparableQuantityAfter` less that of the event before it, or of the opening for the first event. A split between a purchase and a sale then needs no rule of its own: the shares bought and the shares sold are counted on one basis, and the average cost is a cost per share of that basis. Sources: S9, the amounts take share reorganisations "between the date the shares were purchased and when they were sold" into account (sections EX 52(13) and EX 54). |
| R10 | **Peak holding differential.** The lesser of: the peak less the quantity at the opening; and the peak less the quantity at the closing. The peak is `peakQuantity` of the tax holding, the greatest comparable quantity at the opening and after every event (tax-year-events R25), so a purchase and a sale on one day show. It is 0 when the holding never rose above its opening, or never fell below its peak afterwards: a holding that was only increased, or only decreased, has no quick sale. Source: S1 p.15. |
| R11 | **Average cost.** The cost of every purchase of the tax year (R6) over the shares of every purchase of the tax year (R9): the purchases before a sale and those after it, on every Account. It is one figure per tax holding and tax year. It is not the tax cost, which reaches back to the Customer's first event, and not the cost of the shares a sale is matched to. It is used multiplied first and divided after (R12, R14) and is given for the reader to 8 decimal places. With no purchase in the year there is no average cost: the figure is `null`, and the two amounts are 0.00. Sources: S1 p.15, "calculated across all purchases for that share and class in the income year"; S8, the expenditure of the year in acquiring the shares "divided by the total number of shares acquired in the foreign company during the income year". |
| R12 | **Peak holding method amount.** `Round(rate × peak holding differential × cost of the purchases ÷ shares purchased, dp, AwayFromZero)`. Never below zero. Source: S1 p.15, "5% × the peak holding differential × average cost". |
| R13 | **Matching, last bought first sold (roadmap Q11).** The events of the tax holding are walked in their order (tax-year-events R8). A purchase puts its shares on top of the shares bought in the year so far. A sale takes shares from the top down, the most recently bought first, until it has its quantity or none is left; the shares it takes are **matched**, and the rest of the sale is of shares held at the start of the year. A sale before the first purchase of the year matches nothing, and a purchase after the last sale is never matched. Each run of shares taken from one purchase by one sale is a **match**: the purchase, the sale and the quantity. The walk starts empty at the opening and keeps nothing after the closing. Sources: S1 p.15, "The actual gain is calculated for each disposal to the extent that it follows acquisitions made earlier in the income year"; S8, "a last-in-first-out (LIFO) method applies to determine whether shares" were bought in the same year; S7, the "LIFO (last-in-first-out) approach used for the purposes of calculating quick sale gains". |
| R14 | **Quick sale gain amount.** `proceeds + dividends − cost`, where: **proceeds** is the sum over the sales of `Round(sale's proceeds × shares matched ÷ shares sold, dp)`; **dividends** is the sum over the `Dividend` events of `Round(dividend × matched shares held ÷ shares held, dp)`, with the dividend figure of R17, the shares held being the tax holding's comparable quantity at the dividend, and the matched shares held being those of the matches whose purchase is before the dividend and whose sale is after it; **cost** is `Round(shares matched × cost of the purchases ÷ shares purchased, dp)`, the average cost of R11 and not what the matched shares themselves cost. A dividend booked while nothing is held gives no share. The amount may be below zero: a quick sale at a loss. Inland Revenue's guide calls it the actual gain. Sources: S1 p.15 and its example; S8 Example 4 for the dividend, 3,000 ÷ 15,000 of it; S7, "the average cost basis is used for both the peak holding adjustment and quick sale gains components". |
| R15 | **What is not a quick sale.** A `Move`, a `Reorganisation` and a `Correction` are neither a purchase nor a sale (R6). A reversed Transaction and its Reversal are not in the read (tax-year-events R7), so a purchase entered by mistake and taken back is none. A purchase on one Account and a sale on another are matched: the tax holding is one (roadmap T1). A sale and a purchase in two tax years are never matched, however close. |

### 4.5 The comparative value method

| ID | Rule |
| --- | --- |
| R16 | **CV amount of a tax holding.** `(closing value + gains) − (opening value + costs)`. The closing and the opening value are those of the tax holding (tax-year-events R22). **Gains** are the proceeds of every sale of the year and the dividend figure of every `Dividend` of the year (R17). **Costs** are the cost of every purchase of the year (R6). Each figure is in NZD with NZD's places already, so nothing is rounded here. The amount may be below zero. Sources: S1 p.11, "(Closing market value plus gains) minus (opening market value plus costs)"; S2 ¶18. |
| R17 | **The dividend.** A dividend counts at `gross + imputationCredit` of the event (tax-year-events R11). The gross is what was paid before any tax was taken from it, so the foreign tax withheld and the RWT or NRWT are in it, and no tax is added a second time. The imputation credit is attached to the dividend and is on no cash leg (dividends R39). A reinvested dividend counts in full, and its reinvested amount is a cost (R6). The dividend is in the tax year of its `bookedAt`. Sources: S1 p.11, gains are "amounts received from holding (includes dividends) or disposing of the attributing interest and foreign withholding tax or other credits"; S7, the gains of this method are grossed up by trans-Tasman imputation credits. |
| R18 | **What is not in it.** Costs do not include foreign income tax the Customer is liable to pay in another country (S1 p.11): the platform knows only the tax withheld from a dividend, which is part of the gross. Interest on money borrowed to buy the shares and other costs of holding are not costs of the method (S8). A fee rebate is not read (S1 p.14). Each would be a figure invented here (roadmap T5). |

### 4.6 The totals

| ID | Rule |
| --- | --- |
| R19 | **Which tax holdings.** The totals are over the tax holdings whose income is computed (R3, R4): the interests for which the Customer may choose between the two methods. For those a person uses one method for all of them in a year (S1 p.14; S2 ¶16), so the two totals are the two answers, and no mix of them is one. |
| R20 | **FDR income (roadmap TR4).** `sum of the FDR amounts + quick sale adjustment`. The quick sale adjustment is the lesser of two totals: the sum of the peak holding method amounts; and the sum of the quick sale gain amounts, taken as 0.00 when it is below zero. It is taken once over all the tax holdings, not per tax holding: a loss on the quick sale of one comes off the gain on another. It is never below zero and never above the sum of the peak holding method amounts. No loss can arise under this method (S1 p.19). Sources: S1 p.15, "the lesser of the peak holding method amount and the actual gain"; S8, the peak holding adjustment "is the total of the amounts (a pooled approach) calculated for each foreign company", and the gains are "the greater of zero and the total amount for all shares bought and sold in foreign companies during the year"; S8 Example 5; S9, section EX 52(7). |
| R21 | **CV income.** The sum of the CV amounts, taken as 0.00 when it is below zero. The sum itself is also given. A loss on one tax holding comes off the income of another; a loss in total is not income and cannot be used elsewhere. Sources: S1 p.14, "the total result cannot be less than zero. You cannot claim a FIF loss from these investments"; S6, "If there is an overall loss across all of these holdings the person is considered to have zero income" (section EX 51(8)). |
| R22 | **Lower.** `lower` is `Fdr` when the FDR income is the lower of the two totals, `Cv` when the CV income is, and `Equal` when they are the same. It is a comparison of two figures. It is not a choice and not advice: the choice is the taxpayer's, and it depends on holdings the platform may not see (roadmap T8). |
| R23 | **Incomplete (roadmap T5).** The result is incomplete when a tax holding is `NotComputed`, and it lists each with its reason. When a reason is `NotClassified` or `HoldingIncomplete`, the totals of R20 to R22 are `null`: a tax holding that belongs in them is missing, and a sum without it is neither a floor nor an estimate, because its quick sale gain and its CV amount may be below zero. When every reason is `FdrNotAllowed`, the totals are given: they cover the tax holdings of R19, the tax holding named is not one of them, and its income is to be worked out by the reader and added. `complete` is `false` in both cases. An incomplete result does not change `complete` of a tax holding, of the tax year or of the threshold test. The answer is 200. A tax year with no computed tax holding and no reason has every total 0.00, `Equal` and `complete` `true`. |
| R24 | **Rounding.** Every figure of a tax holding is rounded where R8, R12 and R14 say, to NZD's decimal places, away from zero. A total is a sum of the rounded figures of the tax holdings, so every total can be checked from the figures shown (tax-year-events R18). No source read states a rounding rule; the published examples are in whole dollars and agree either way. |
| R25 | **What the result does not say.** It does not say that the Customer has FIF income: that needs the threshold test, the Customer's residence and their choices (R2). It does not choose a method. It does not say what tax is payable or which credit is allowed. It covers what the ledger knows: a holding the Customer has elsewhere counts only if it is recorded in an External account (roadmap §4). |

Reasons of R23:

| Reason | When | The totals |
| --- | --- | --- |
| `NotClassified` | The treatment of the tax holding for `Y` is `NotClassified` | `null` |
| `HoldingIncomplete` | An `Attributing` tax holding is incomplete (R4) | `null` |
| `FdrNotAllowed` | The treatment is `AttributingNoFdr` (R3) | Given, without this tax holding |

### 4.7 The read

| ID | Rule |
| --- | --- |
| R26 | **Fields.** The item gains `fifIncome`: `{ rate, fdr, cv, lower, complete, incomplete }`. `fdr` is `{ openingValue, amount, peakHoldingAmount, quickSaleGainAmount, quickSaleAdjustment, income }`: the sums over the computed tax holdings, the adjustment of R20 and the FDR income; `quickSaleGainAmount` is the sum as it is, also below zero. `cv` is `{ amount, income }`: the sum of R21 and the CV income. `lower` is `Fdr`, `Cv` or `Equal`. `incomplete` is a list of `{ reason, instrumentId }`. When R23 gives no totals, `fdr`, `cv` and `lower` are `null`. A tax holding gains `fifIncome`: `{ status, reason, fdr, quickSale, cv }` with `status` `Computed`, `NotComputed` or `NotRequired`; `reason` is `null` unless the status is `NotComputed`; the other three are `null` unless it is `Computed`. `fdr` is `{ openingValue, amount }`. `quickSale` is `{ openingQuantity, peakQuantity, closingQuantity, peakHoldingDifferential, purchasedQuantity, purchaseCost, averageCost, peakHoldingAmount, matchedQuantity, proceeds, dividends, cost, gainAmount, matches, sales, dividendShares }`; a match is `{ purchaseSequence, saleSequence, quantity }`; a sale is `{ sequence, quantity, matchedQuantity, amount, proceeds }`; a dividend share is `{ sequence, amount, heldQuantity, matchedQuantity, share }`. Each `sequence` is that of the event in `events`; every quantity is a comparable quantity. `sales` lists every sale of the year and `dividendShares` every dividend with a share above 0. `cv` is `{ openingValue, closingValue, sales, dividends, purchases, amount }`. Every amount is in NZD with NZD's decimal places. §8.1 shows each shape. Every caller who may read the Customer sees every field. |
| R27 | **Still a ledger read.** tax-year-events R32 holds: an entry backdated on an External account into the tax year, a Transaction of the year reversed later, or a price or rate row inserted later changes the next answer (roadmap Q15). A backdated entry in an earlier tax year changes the opening quantity and so the FDR amount. Nothing is marked as final. |

Policies: none added.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `FairDividendRate` | Static (new, `src/Domain/Tax`) | `For(taxYear)` → the rate. One row: 0.05 from 2009 on (R7) |
| `FifIncome` | Value (new) | The result of R19–R25: the totals of both methods, `Lower`, the reasons. `Compute(taxYear, holdings, decimalPlaces)` works out the income of each tax holding and the totals from the tax holdings of the year. It reads no clock and no store |
| `TaxHoldingIncome` | Value (new) | The income of one tax holding: `Status`, `Reason`, the FDR amount, the `QuickSale`, the CV figures (R3–R18) |
| `QuickSale` | Value (new) | The quick sale figures of one tax holding: the differential, the purchases of the year and their average cost, the peak holding method amount, the matches, and the gain amount with its three parts (R9–R14) |
| `FifIncomeStatus`, `FifIncomeReason`, `FifLowerMethod` | Enums (new) | `Computed = 0`, `NotComputed = 1`, `NotRequired = 2`; `NotClassified = 0`, `HoldingIncomplete = 1`, `FdrNotAllowed = 2`; `Fdr = 0`, `Cv = 1`, `Equal = 2` |
| `CustomerTaxYear` | Value (existing) | Gains `FifIncome`. `Compute` calls `FifIncome.Compute` after the tax holdings are built, with its own parameters unchanged |
| `TaxHolding` | Value (existing) | Gains `Income` |
| `TaxLedger`, `TaxEvent`, `TaxCostLedger`, `ThresholdTest`, `TaxNotice`, `TaxIncompleteReason` | Existing | No change |

```text
FDR amount            Round(rate x opening value)                                    per holding   (R8)
differential          lesser of peak - opening and peak - closing, comparable        per holding   (R10)
average cost          cost of the year's purchases / shares of the year's purchases  per holding   (R11)
peak holding amount   Round(rate x differential x cost / shares)                     per holding   (R12)
matching              a sale takes the year's purchases, last bought first           per holding   (R13)
gain amount           matched proceeds + dividend shares - matched shares x average  per holding   (R14)
CV amount             (closing + sales + dividends) - (opening + purchases)          per holding   (R16)
quick sale adjustment lesser of sum(peak holding amounts) and max(0, sum(gains))     in total      (R20)
FDR income            sum(FDR amounts) + quick sale adjustment                       in total      (R20)
CV income             max(0, sum(CV amounts))                                        in total      (R21)
```

Invariants:

- The FDR amount and the peak holding method amount of a tax holding are `≥ 0`. The FDR income and the CV income are `≥ 0`.
- The quick sale adjustment is `≥ 0` and never above the sum of the peak holding method amounts.
- The shares matched of a tax holding are never more than the shares purchased in the year, and never more than the shares sold in it.
- A tax holding with no purchase in the year, or with no sale in it, has both quick sale amounts 0.00.
- A `Move`, a `Reorganisation` and a `Correction` change no figure of either method other than through the quantities the read gives.
- A total is given only when no tax holding is `NotClassified` or `HoldingIncomplete`.
- The result never changes `complete` of a tax holding, of the tax year or of the threshold test.

Domain events: none. Nothing is an entity and nothing is stored.

[domain-model.md](../domain-model.md) §8.24 holds these types.

---

## 6. Database

None. No script. The read uses the tables of tax-year-events §6 and reads no row more than it does today.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Service | `CustomerTaxLedger` (existing) | No change. It loads nothing for this slice: `CustomerTaxYear.Compute` works the income out from the tax holdings it builds |
| Query | `GetCustomerTaxYear` (existing) | Maps the fields of R26, with the `PublicId` of each Instrument. No new check |

Where each rule lives:

| Rule | Where |
| --- | --- |
| The rate of a tax year (R7) | `FairDividendRate` |
| Which tax holdings are computed, the two methods per tax holding, the matching (R3–R18) | `FifIncome.Compute` |
| The totals, the lesser, the reasons (R19–R25) | `FifIncome.Compute` |
| Access and `taxYear` (R1) | `GetCustomerTaxYear`, unchanged |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/users/customers/{customerId}/tax-years/{taxYear}` | `accounts.read` | 200 item, with the fields of R26 | 400 / 401 / 403 / 404, unchanged |

### 8.1 Bodies

`GET /users/customers/{customerId}/tax-years/2026` for example 3, the new fields only:

```json
{
  "taxYear": 2026,
  "currency": "NZD",
  "fifIncome": {
    "rate": 0.05,
    "fdr": {
      "openingValue": 20000.00,
      "amount": 1000.00,
      "peakHoldingAmount": 700.00,
      "quickSaleGainAmount": 360.00,
      "quickSaleAdjustment": 360.00,
      "income": 1360.00
    },
    "cv": { "amount": -1740.00, "income": 0.00 },
    "lower": "Cv",
    "complete": true,
    "incomplete": []
  },
  "holdings": [
    {
      "instrument": { "id": "<QQQ publicId>", "symbol": "QQQ", "name": "Quebec Inc", "quoteCurrency": "NZD" },
      "fifIncome": {
        "status": "Computed",
        "reason": null,
        "fdr": { "openingValue": 20000.00, "amount": 1000.00 },
        "quickSale": {
          "openingQuantity": 2000, "peakQuantity": 3200, "closingQuantity": 2200,
          "peakHoldingDifferential": 1000,
          "purchasedQuantity": 1700, "purchaseCost": 18700.00, "averageCost": 11,
          "peakHoldingAmount": 550.00,
          "matchedQuantity": 1200, "proceeds": 15400.00, "dividends": 260.00, "cost": 13200.00,
          "gainAmount": 2460.00,
          "matches": [
            { "purchaseSequence": 3, "saleSequence": 5, "quantity": 400 },
            { "purchaseSequence": 1, "saleSequence": 5, "quantity": 600 },
            { "purchaseSequence": 1, "saleSequence": 7, "quantity": 200 }
          ],
          "sales": [
            { "sequence": 5, "quantity": 1000, "matchedQuantity": 1000, "amount": 13000.00, "proceeds": 13000.00 },
            { "sequence": 7, "quantity": 500, "matchedQuantity": 200, "amount": 6000.00, "proceeds": 2400.00 }
          ],
          "dividendShares": [
            { "sequence": 2, "amount": 700.00, "heldQuantity": 2800, "matchedQuantity": 800, "share": 200.00 },
            { "sequence": 6, "amount": 660.00, "heldQuantity": 2200, "matchedQuantity": 200, "share": 60.00 }
          ]
        },
        "cv": { "openingValue": 20000.00, "closingValue": 23100.00, "sales": 19000.00, "dividends": 1360.00, "purchases": 18700.00, "amount": 4760.00 }
      }
    },
    {
      "instrument": { "id": "<RRR publicId>", "symbol": "RRR", "name": "Romeo Inc", "quoteCurrency": "NZD" },
      "fifIncome": {
        "status": "Computed",
        "reason": null,
        "fdr": { "openingValue": 0.00, "amount": 0.00 },
        "quickSale": {
          "openingQuantity": 0, "peakQuantity": 300, "closingQuantity": 200,
          "peakHoldingDifferential": 100,
          "purchasedQuantity": 300, "purchaseCost": 9000.00, "averageCost": 30,
          "peakHoldingAmount": 150.00,
          "matchedQuantity": 100, "proceeds": 900.00, "dividends": 0.00, "cost": 3000.00,
          "gainAmount": -2100.00,
          "matches": [ { "purchaseSequence": 1, "saleSequence": 2, "quantity": 100 } ],
          "sales": [ { "sequence": 2, "quantity": 100, "matchedQuantity": 100, "amount": 900.00, "proceeds": 900.00 } ],
          "dividendShares": []
        },
        "cv": { "openingValue": 0.00, "closingValue": 1600.00, "sales": 900.00, "dividends": 0.00, "purchases": 9000.00, "amount": -6500.00 }
      }
    }
  ]
}
```

A tax holding for which FDR is not allowed, beside the two above. The totals stay:

```json
{
  "fifIncome": {
    "rate": 0.05,
    "fdr": { "openingValue": 20000.00, "amount": 1000.00, "peakHoldingAmount": 700.00, "quickSaleGainAmount": 360.00, "quickSaleAdjustment": 360.00, "income": 1360.00 },
    "cv": { "amount": -1740.00, "income": 0.00 },
    "lower": "Cv",
    "complete": false,
    "incomplete": [ { "reason": "FdrNotAllowed", "instrumentId": "<SSS publicId>" } ]
  }
}
```

with, on the tax holding it names:

```json
{ "fifIncome": { "status": "NotComputed", "reason": "FdrNotAllowed", "fdr": null, "quickSale": null, "cv": null } }
```

A tax holding with no closing price. No total is given:

```json
{
  "fifIncome": {
    "rate": 0.05,
    "fdr": null,
    "cv": null,
    "lower": null,
    "complete": false,
    "incomplete": [ { "reason": "HoldingIncomplete", "instrumentId": "<publicId>" } ]
  }
}
```

A New Zealand or an exempt share:

```json
{ "fifIncome": { "status": "NotRequired", "reason": null, "fdr": null, "quickSale": null, "cv": null } }
```

A quantity is a JSON number with up to 8 decimal places, and so is `averageCost`. An NZD figure has NZD's decimal places.

### 8.2 Errors

None added. An incomplete result is not an error: the answer is 200 with `fifIncome.complete` `false`.

Cross-tenant or wrong-collection id → **404**, not 403, as tax-year-events R1 says. Keep the resource catalog in [api-design.md](../api-design.md) pointing there for the route and here for these fields.

---

## 9. UI

None in this slice. No page shows FIF income. The report that prints it is [fif-report.md](fif-report.md): for an incomplete result and for a holding where FDR is not allowed, its R17, R20 and §4.7.

### 9.1 Seeds and hosting

No change to `TestSeed`, the demo seed, AppHost or the VPS. The functional tests write their own Transactions, prices and rates.

What the stored history must cover is what the tax year read needs (tax-year-events §9.1), and nothing more:

- A tax holding is computed only when its opening and its closing are valued. In Development the floor date is 2026-01-01, so tax year 2026 has no opening price: an attributing tax holding held on 31 March 2025 is `HoldingIncomplete` there, and the totals are `null`. Tax year 2027 is the first with both prices, readable from 2 April 2027.
- In the demo tenant the catalog is classified from tax year 2027 (demo-tenant-seed). An earlier tax year lists every tax holding as `NotClassified`.
- The rate has a value for every tax year that can be read (R7). Unlike the threshold, no row has to be added before tax year 2027 is read.

---

## 10. Tests

| Project | Where |
| --- | --- |
| Domain.UnitTests | `Tax/FairDividendRateTests.cs`, `Tax/FifIncomeFdrTests.cs`, `Tax/QuickSalePeakTests.cs`, `Tax/QuickSaleGainTests.cs`, `Tax/FifIncomeCvTests.cs`, `Tax/FifIncomeTotalsTests.cs`, `Tax/CustomerTaxYearIncomeTests.cs`, with the tax holdings of the examples in `Tax/FifExamples.cs` |
| Application.FunctionalTests | `TaxYears/GetCustomerTaxYearIncomeTests.cs`; `TaxYears/GetCustomerTaxYearAccessTests.cs` (the totals of two tenants); `TaxYears/GetCustomerTaxYearTests.cs` (the field list of the item) |

| Project | Assert |
| --- | --- |
| Domain.UnitTests (`Tax/FairDividendRateTests`) | 0.05 for 2009, 2026 and 2027 |
| Domain.UnitTests (`Tax/FifIncomeFdrTests`) | The FDR amount is the rate times the opening value, rounded away from zero; a tax holding bought during the year gives 0.00; one sold in full during the year gives its whole amount; dividends and sales add nothing; S1 p.14: 5,000 each and 15,000; S2 Example 1: 0.00 for 2022 and 2,750.00 for 2023; example 1 of §1.1: 0.00 and 655.42 |
| Domain.UnitTests (`Tax/QuickSalePeakTests`) | S1 p.15: differential 2,000, average cost 22, amount 2,200.00; S8 Example 4: 720.00; S8 Example 5: 600.00 and 1,200.00; a holding only increased, and one only decreased, has a differential of 0; a sale and then a purchase that does not pass the opening has 0; a purchase and a sale on one day show; example 2: the shares bought before the split count on the basis of the year's end, differential 1,000 and amount 550.00; an `Arrival` is a purchase at its market value; no purchase in the year gives no average cost and 0.00 |
| Domain.UnitTests (`Tax/QuickSaleGainTests`) | S1 p.15: 12,000.00, on the 4,000 shares sold and not on the differential; S8 Example 4: 800.00 with a dividend share of 200.00; S8 Example 5: 2,160.00 and −6,000.00; example 2 to the cent, with its three matches; a sale takes the most recent purchase first, and the variant of example 2 gives no dividend share where first bought first sold would give 75.00; a sale of more than was bought in the year is matched in part, with its proceeds pro rata; a sale before the first purchase matches nothing; a dividend before the purchase or after the sale of a match gives no share; a dividend booked while nothing is held gives none; a `Departure` is a sale at its market value; a `Move`, a `Reorganisation` and a `Correction` are neither; a purchase on one Account and a sale on another are matched |
| Domain.UnitTests (`Tax/FifIncomeCvTests`) | S1 p.14: 2,000, 10,000 and −20,000; S2 Example 1: 4,125.00; S8 Examples 1 to 3: 25,000, 3,000 and −15,000; S8 Example 4: 17,000; example 1 of §1.1: 752.78 and 1,740.87; a dividend counts at its gross plus the imputation credit, and the foreign tax and the RWT are not added again; a reinvested dividend is a gain and a cost; fees are in the cost and off the proceeds; an `Arrival` is a cost and a `Departure` a gain |
| Domain.UnitTests (`Tax/FifIncomeTotalsTests`) | Example 3: the adjustment is 360.00, the lesser of the two totals, where the sum of lessers is 550.00; S8 Example 5: a total gain below zero gives an adjustment of 0.00; S1 p.14: the CV income is 0.00 for a sum of −8,000; `lower` is `Fdr`, `Cv` and `Equal`; a total is the sum of the rounded figures of the tax holdings; `NotClassified` and `HoldingIncomplete` give no totals; `FdrNotAllowed` alone keeps the totals with `complete` `false`; an exempt and a `NotForeign` tax holding are `NotRequired` and in no total; a Customer with nothing: 0.00, `Equal`, complete |
| Domain.UnitTests (`Tax/CustomerTaxYear*Tests`) | `Compute` carries the income on each tax holding and the totals; `complete` of a tax holding, of the tax year and of the threshold test do not change when the income is incomplete; the income is the same whatever the outcome of the threshold test; the existing cases are unchanged |
| Application.FunctionalTests (`TaxYears/GetCustomerTaxYearIncomeTests`) | The worked example of tax-year-events through the API: the figures of example 1 for both tax years; example 3 through the API, the fields of §8.1; an `AttributingNoFdr` tax holding beside them; a tax holding with no closing price gives `HoldingIncomplete` and no totals; a purchase reversed later is in no quick sale; an entry backdated on the External account changes the next read |
| Application.FunctionalTests (`TaxYears/GetCustomerTaxYearAccessTests`) | Unchanged cases, green. Two-tenant fixture: no Transaction, price or tax holding of the other Tenant's Customer is in any total, and the other Tenant's ids → 404 |

No Application.UnitTests: there is no new check. No Infrastructure.IntegrationTests: there is no script and no mapping.

---

## 11. Locked in this spec

No new ADR.

| Item | Lock |
| --- | --- |
| Which tax holdings | `Attributing` and complete: computed under both methods. `AttributingNoFdr`, `NotClassified` and an incomplete one: not computed, with a reason. The rest: not required |
| Inputs | The figures of the tax year read, and nothing else |
| Purchases and sales | `Acquisition` and `Arrival`; `Disposal` and `Departure`; fees in the cost and off the proceeds; an arrival and a departure at market value |
| Rate | 0.05 for tax year 2009 and every later one |
| FDR amount | The rate times the opening value of the tax holding |
| Quick sale | Comparable quantities; the differential from the peak of the read; the average cost over every purchase of the year; sales matched to the purchases of the year, last bought first sold; dividends pro rata to the matched shares held |
| Quick sale adjustment | The lesser of the two totals over all computed tax holdings; a total gain below zero is zero |
| CV amount | `(closing + sales + dividends) − (opening + purchases)`; a dividend at its gross plus the imputation credit |
| CV income | The sum, not below zero |
| Lower | A comparison of the two totals; no choice |
| Incomplete | The three reasons of §4.6; no totals unless every reason is `FdrNotAllowed`; apart from `complete` of the tax year and of the threshold test |
| Rounding | Per tax holding, to NZD's places, away from zero; a total is a sum of rounded figures |
| Where | Fields of `GET /users/customers/{customerId}/tax-years/{taxYear}` |
| Routes, scripts, permissions, problem codes, events, ports, jobs, settings | None |

### Points confirmed

Confirmed on 2026-11-28, every one as recommended. Each point states the choice, written into the rules above, and the alternative that was not taken.

1. **The income is a field of the tax year read** (R1, R26). Recommended: it is arithmetic over that read's own figures, so one answer shows the result with every price, rate and event it came from, at one `ledgerReadAt`; roadmap TR4 allows "added to the TR2 read or as a read of its own", and the tax cost was placed the same way. The cost: the item grows again. Alternative: a route of its own (`…/tax-years/{taxYear}/fif-income`), which reads the ledger a second time and may show another state of it.
2. **The income is computed whatever the threshold test says** (R2). Recommended: roadmap TR4, "TR3 decides whether the result applies, not how it is computed"; a person below the threshold may choose to apply the rules; and the report shows both branches (roadmap Q14). Alternative: `null` when the outcome is `NotAbove`.
3. **A tax holding for which FDR is not allowed is listed and not computed, and the totals stay** (R3, R23). Recommended: roadmap Q12 for "listed and not computed" and for `complete` `false`. The totals are those of the interests the Customer may choose a method for, and such a holding is by law outside that choice, so its absence does not make them wrong. This refines Q12, which did not say what the totals do. Alternative A: no totals at all while such a holding is listed. Alternative B (not chosen in Q12): compute it under CV alone, with its loss allowed, as its own figure.
4. **An incomplete tax holding is not computed at all, and the totals are then not given** (R4, R23). Recommended: roadmap T5. A sum that leaves a holding out is not a floor here, as it is for the threshold test: a quick sale gain and a CV amount may be below zero. And a holding with half of its figures invites reading the half as the whole. The cost: one missing closing price hides the totals of a Customer with twenty good holdings; each holding's own figures are still there. Alternative A: totals over the computed holdings, marked incomplete. Alternative B: figure by figure, so the FDR amount is given when only the closing price is missing.
5. **An arrival is a purchase and a departure is a sale, at the market value of the day** (R6). Recommended: roadmap Q8 says so for both methods, and each already carries its notice. The cost: a transfer to another broker that the move rule did not pair looks like a sale, and can make a quick sale. Alternative: a tax holding with an arrival or a departure in the year is not computed.
6. **A correction changes the quantity and is neither a purchase nor a sale** (R6). Recommended: as the tax year read and the tax cost treat it, the caller's number with its notice (tax-year-events point 13, tax-cost-threshold point 8). The cost: shares that appear through a correction have no cost under CV and no opening value under FDR, and the figures are then wrong in a way the notice does not measure. Alternative: a tax holding with a correction of the quantity in the year is not computed, so that a person looks first.
7. **Trade fees are in the cost of a purchase and off the proceeds of a sale, under both methods** (R6). Recommended: roadmap Q10; S2 ¶24 and S8 for the purchase. No source read says that a sell fee comes off the proceeds; "amounts received from ... disposing" (S1 p.11) is read as what reached the Account. Section EX 51 was not read. Alternative: the principal alone, on one side or on both. The read gives `principal` and `fee` apart, so the change would be small.
8. **The rate is a value per tax year, 0.05 from 2009 with no end** (R7). Recommended: roadmap T6. Unlike the threshold, no change to the rate is before Parliament in any source read, so no tax year is left without a value. Alternative: a value only up to the last tax year a source was read for (2026), so that every later year needs an amendment before it can be computed.
9. **The quick sale is worked out on comparable quantities** (R9). Recommended: the tax year read gives them for this purpose, and Inland Revenue's 2011 amendment says both amounts take a reorganisation between the purchase and the sale into account. Alternative: quantities as booked, which is wrong across a split.
10. **The average cost is over every purchase of the year, an arrival too, also those after the sale** (R11). Recommended: the guide's sentence and the 2006 commentary's definition. In the guide's own example every purchase costs $22 a share, so the example cannot tell this from an average over the purchases before the sale. Alternative: only the purchases before each sale; or the actual cost of the matched shares.
11. **A sale is matched to the purchases of the year, last bought first sold, and the gain is on the matched shares** (R13, R14). Recommended: roadmap Q11. The guide's example counts the gain on the 4,000 shares sold, where the differential is 2,000. Alternative: the gain on the differential's quantity, which the 2006 examples do not rule out, because there the two are equal.
12. **A dividend counts pro rata to the matched shares held when it was booked** (R14). Recommended: the 2006 commentary's Example 4, and roadmap Q11. It is not in the guide's example, which has no dividend. The shares held are the Customer's at the dividend's `bookedAt`, not the entitled shares at its ex-date (see "Found while writing"). Alternative A: no dividend in the gain. Alternative B: pro rata to the `shares` of the dividend detail.
13. **The quick sale adjustment is the lesser of two totals over all tax holdings** (R20). Recommended: roadmap TR4, the 2006 commentary ("a pooled approach") and its Example 5. Alternative: the lesser per tax holding, summed, which is higher whenever one quick sale made a loss.
14. **A dividend is a CV gain at its gross plus the imputation credit, and no foreign tax is a cost** (R17, R18). Recommended: this answers roadmap TR4's question on S1 p.11: the foreign tax withheld is part of the gross and is not added again. The guide also lists "foreign income tax a person is liable to pay in another country" among the costs; the platform knows no such tax apart from the withholding, and taking the withholding off again would tax the dividend net and still leave a credit for it. Alternative A: the gross alone, no imputation credit. Alternative B: the withheld foreign tax also as a cost.
15. **The CV income is the sum over the computed tax holdings, not below zero** (R21). Recommended: the guide and section EX 51(8) as the 2012 commentary gives it.
16. **Figures are rounded per tax holding, and a total is a sum of rounded figures** (R24). Recommended: every total can be checked from the figures shown, as in the tax year read. The cost: the FDR income can differ by a cent or two from the rate times the total opening value. Alternative: round once, on the totals.
17. **`lower` is given** (R22). Recommended: roadmap §4 point 4 asks which total is lower. It is one comparison, and the report would make it anyway. Alternative: no field, so that nothing looks like a recommendation.
18. **One spec for both methods** (roadmap TR4). Recommended: they share every input, the quick sale belongs to FDR, and the totals compare the two.

### Found while writing

- **The Act was not read.** `legislation.govt.nz`, NZLII and AustLII refused automated reads again (403), and an archived copy could not be reached. Roadmap Q11 asked for sections EX 52 and EX 54, and Q10 for EX 51. R13 and R14 rest on the guide, on the 2006 and 2008 commentaries and on roadmap Q11. A person should still read the three sections; they were not read when the spec was accepted.
- **Inland Revenue's calculator was not used.** Roadmap TR4 names it as a second check (S3). It is a web form, and no number of this spec was typed into it. A person should still type example 2 and the guide's quick sale example in; it was not done when the spec was accepted.
- **The commentaries were read through a fetch tool** that gives back a restatement with short exact quotes, not the page itself (§14). The figures of S8's examples are as restated and were worked through again here: each agrees with the rules of §4 to the dollar.
- **Two names for one amount.** The guide says "actual gain" (S1 p.15). The Act, as the 2011 commentary cites it, says "quick sale gain amount". This spec uses the Act's name and gives the guide's in R14.
- **Roadmap Q11 cites S1 p.15 and S9 for the dividends.** Neither mentions them. The pro rata share of a dividend is in S8, Example 4.
- **The guide's quick sale example is silent on three points.** Its purchases all cost $22 a share, so it does not show which purchases the average is over. It has no dividend. It has one company, so it does not show whether the lesser is taken per company. The sentences above the example settle the first, and S8 the other two.
- **The 2006 examples are silent on one point.** In Examples 4 and 5 the shares sold equal the peak holding differential, so they do not show which quantity the gain is on. The guide's example does: 4,000 against 2,000.
- **The guide's quick sale example gives a CV income of zero.** (254,000 + 100,000) − (200,000 + 154,000) = 0. The guide does not say so; it is the figure of R16. A person with only that holding would return nothing under CV and $12,200 under FDR.
- **"The closing price of the last day of the previous year" was found.** tax-year-events "Found while writing" could not find the sentence roadmap Q5 rests on. It is in S8: "the close of trading price on the last day of the preceding income year is acceptable to Inland Revenue".
- **A dividend is placed by its `bookedAt`, not by its ex-date.** Shares bought between the ex-date and the payment are held when the dividend is booked and did not earn it, and shares sold in between earned it and are not held. R14 then gives the wrong share to a match. The tax year read has the same choice in its R11.
- **A dividend is booked per Account.** A tax holding on two Accounts gets two dividends when both record one. When an External account does not, the dividend of the other is still shared over the shares of both in R14.
- **A move between the two postings of one split** changes the comparable quantity of the tax holding, because each Account has its own factor (tax-year-events "Found while writing", shares in transit). The peak, and so the differential, can then be too high or too low. Nothing marks it.
- **A correction can raise the peak.** A correction that adds shares, followed by a sale, gives a differential above 0 with no purchase. R11 then gives no average cost and both amounts are 0.00. With a purchase in the same year, the differential counts the corrected shares at the average cost of the purchase. Point 6 has the alternative.
- **S8 prints one result without cents.** Its cost method example gives 5 % of $22,050 as $1,102. It is the only hint of a rounding rule in the sources read, and it is about another method.
- **S8 has a slip in Example 5:** the line for Co.B names "Foreign Co.A". The figures are Co.B's.
- **A change of method is a deemed sale and purchase at market value** at the start of the year (S7), and it is ignored for the quick sale. It changes no figure here: both methods start from the opening market value.
- **A treatment that changed at a year end** (tax-cost-threshold R22) has the same effect: the deemed purchase at market value at the start of the year is the opening value both methods use. This slice needs no mark for it.

Still open, not this slice:

- The CV income of a non-ordinary share, and the deemed rate of return (roadmap Q12, §7)
- Sections EX 51, EX 52 and EX 54 of the Act, and the calculator as a second check
- A dividend's ex-date in the quick sale gain
- Foreign income tax paid by the Customer themselves, fee rebates
- Ledger replay and period close (roadmap §1.2)

---

## 12. Changes to other docs

Each doc was changed in place when this spec was accepted (2026-11-28).

| Doc | Change |
| --- | --- |
| [tax-year-events.md](tax-year-events.md) | §2 Out: FIF income, the differential and the matching are this spec's, no longer "TR4". R10: the fee is in both methods (R6 here); the two parts stay apart. R25: the peak is read by R10 here. R31: the item and each holding carry `fifIncome` (R26 here). §5: `CustomerTaxYear.FifIncome`, `TaxHolding.Income`. "Found while writing": the sentence of roadmap Q5 was found in S8. "Still open": the first line goes. History row |
| [tax-cost-threshold.md](tax-cost-threshold.md) | §2 Out and "Still open": FIF income and the average cost of the quick sale are this spec's (R11 here). A History section with its first row |
| [instrument-tax-attributes.md](instrument-tax-attributes.md) | The table of treatments: `Attributing` is computed under both methods and `AttributingNoFdr` is listed and not computed (R3 here), where it says "Roadmap Q12 says what the calculation does with it". "Still open" |
| [dividends.md](dividends.md), [external-income.md](external-income.md) | A sentence each: a dividend of an attributing tax holding is a gain of the comparative value method at its gross plus the imputation credit, and adds nothing under the fair dividend rate method (R8, R17 here). The tax statement stays TR5's |
| [trade-fees.md](trade-fees.md) | §11: a fee is part of the cost of a purchase and comes off the proceeds of a sale in FIF income (R6 here). History row |
| [in-kind-transfers.md](in-kind-transfers.md), [migration-batches.md](migration-batches.md) | A sentence each: for FIF income a holding that arrives is a purchase at the market value of the day, and one that leaves is a sale (R6 here) |
| [adjustments.md](adjustments.md), [split.md](split.md) | A sentence each: a correction is neither a purchase nor a sale in FIF income (R6); a reorganisation is neither, and the quick sale is counted on the basis of the year's end (R9) |
| [api-design.md](../api-design.md) | §7.4: the tax year item carries FIF income. Change log |
| [domain-model.md](../domain-model.md) | §8.24: `FairDividendRate`, `FifIncome`, `TaxHoldingIncome`, `QuickSale`, the enums and the invariants of §5. Change log |
| [glossary.md](../glossary.md) | FIF income, Fair dividend rate (FDR), Quick sale adjustment and Comparative value (CV) become accepted. "FIF treatment", "Threshold test" and "Tax cost" point here where they say TR4 |
| [fif-roadmap.md](../roadmaps/fif-roadmap.md) | §6 TR4: the spec line. §8: Q11 and Q12 written as rules here; Q8 and Q10 for the two methods. §9 row 4: accepted. §11: S8 read again |
| [features/README.md](README.md), [future-plans.md](../future-plans.md) §1 step 6 | Status: TR4 accepted |

---

## 13. Suggested commits

Removed when the slice landed (2026-11-28). The commits are in git.

---

## 14. Sources

Read on 2026-11-28. The codes are those of [fif-roadmap.md](../roadmaps/fif-roadmap.md) §11.

| # | Source | Used for | Read |
| --- | --- | --- | --- |
| S1 | Inland Revenue, *Guide to foreign investment funds*, IR461, April 2026 edition, the PDF linked from `ird.govt.nz/fif` (page last updated 31 March 2026) | The two formulas, gains and costs, and who may use CV (p.11); market value and currency conversion (p.13); the FDR general rule, one method for all investments, the option to compare with CV, "the total result cannot be less than zero", the example of three companies (p.14); the quick sale adjustment, the peak holding differential, the average cost, the actual gain and their example (p.15); switching between the methods from year to year, non-ordinary shares, a loss on them under CV (p.16); losses (p.19); a share bought or sold during the year, purchases and sales not at market value (p.22 notes 6 and 7) | Yes, pages 11 to 24 |
| S2 | Inland Revenue, fact sheet IS 24/10 FS 1, *Income tax – Share investments (whether the FIF rules apply)*, 18 December 2024 (`taxtechnical.ird.govt.nz`) | A person below the threshold may choose to apply the rules (¶8); one method across all attributing interests in a year (¶16); FDR, the opening value and the quick sale (¶17); CV (¶18); either method each year (¶19); Example 1, Jax: no income in 2022, FDR $2,750 and CV $4,125 in 2023; brokerage "taken into account in the relevant FIF calculation method" (¶24) | Yes, pages 3 to 8 |
| S6 | Inland Revenue Tax Technical, "Choice of FIF calculation method" (commentary on the 2012 Act) | Who may use CV; CV for one interest means no FDR for the others; an overall loss gives zero income, section EX 51(8); non-ordinary shares use CV and their loss can be used, section EX 46(10) | Through a fetch tool: a restatement with short quotes |
| S7 | Inland Revenue Tax Technical, "Technical amendments to the offshore portfolio share investment rules" | "the average cost basis is used for both the peak holding adjustment and quick sale gains components"; last in first out for the quick sale gains; a deemed sale on a change of method is ignored for the quick sale; CV gains grossed up by trans-Tasman imputation credits | Through a fetch tool: a restatement with short quotes |
| S8 | Inland Revenue Tax Technical, "New tax rules for offshore portfolio investment in shares" (commentary on the 2006 Act) | The closing price of the last day of the year before as the opening value; the quick sale adjustment, the peak holding adjustment "5% x quick sales x average cost", the average cost, the quick sale gains pooled and not below zero, last in first out; holding costs left out; a split between purchase and sale; Examples 1 to 5 | Through a fetch tool: the first 79,542 of 101,653 characters, as a restatement with short quotes. The rest (changing method, entering and leaving the rules) was not read and is not used |
| S9 | Inland Revenue Tax Technical, "Fair dividend rate method: quick sale gain amount" (2011 remedial amendment) | Section EX 52(7): the lesser of the peak holding method amount and the quick sale gain amount; sections EX 52(13) and EX 54(1): the average cost after a share reorganisation, for both amounts, for income years from 1 April 2008 | Through a fetch tool: a restatement with short quotes. The page has no formula and no example |
| S3 | Inland Revenue, "Calculate my foreign investment fund income" (`ird.govt.nz/calculate-my-fif-income`) | The second check of roadmap TR4 | Not read again and not used. See "Found while writing" |
| — | Income Tax Act 2007, sections EX 51, EX 52 and EX 54 | The comparative value method, the fair dividend rate annual method with its quick sale adjustment, and the average cost after a reorganisation, in the Act's own words | Not read: `legislation.govt.nz`, NZLII and AustLII refused automated reads (403) |

What follows from the refusal: R13 and R14 (the matching, the quantity the gain is on, the average cost and the dividend share) follow roadmap Q11, the guide and the two commentaries. R6 (a sell fee off the proceeds) follows roadmap Q10. R17 and R18 (which tax and which credit are in the gains and the costs) follow one sentence of the guide. Each is a point of §11. The two formulas, the 5 % rate, the peak holding method amount, the lesser of the two amounts, and a CV total not below zero are stated by Inland Revenue in the sources read.

---

## 15. History

What changed in this file after it was accepted. The text of each change is in git (`git log -p -- docs/features/fif-income.md`).

| Date | With | Change here |
| --- | --- | --- |
| 2026-11-28 | [fif-report.md](fif-report.md) | §2 Out, R2, §9 and "Still open": what the report prints for an incomplete result and for a holding where FDR is not allowed is its R17, R20 and §4.7; the foreign tax beside the income is its R22; there is no run over every Customer (its R5). The two TR5 lines go |
| 2026-11-28 | [income-summary.md](income-summary.md) | §2 Out and "Still open": the income summary is that spec; the TR6 line goes |
