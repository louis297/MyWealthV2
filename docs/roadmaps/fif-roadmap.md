---
title: FIF tax report roadmap
status: note
language: en
created: 2026-11-27
updated: 2026-11-28
related:
  - ../future-plans.md
  - ../function-plan.md
  - valuation-roadmap.md
  - custodial-roadmap.md
  - ../features/instruments.md
  - ../features/instrument-tax-attributes.md
  - ../features/tax-year-events.md
  - ../features/tax-cost-threshold.md
  - ../features/fif-income.md
  - ../features/holdings.md
  - ../features/posting.md
  - ../features/buy-sell.md
  - ../features/split.md
  - ../features/trade-fees.md
  - ../features/custody-modes.md
  - ../features/external-caller-numbers.md
  - ../features/interest-run.md
  - ../features/dividends.md
  - ../features/external-income.md
  - ../features/corporate-actions.md
  - ../features/in-kind-transfers.md
  - ../features/migration-batches.md
  - ../features/fx-rates.md
  - ../features/instrument-prices.md
  - ../features/market-data-history.md
  - ../features/account-valuation.md
  - ../features/valuation-run.md
  - ../features/reports.md
  - ../features/customers.md
  - ../features/demo-tenant-seed.md
  - ../backoffice/back-office-instruments.md
  - ../adr/0019-fx-rate-source-and-refresh.md
  - ../known-issues/eodhd-nz-jp-coverage.md
---

# FIF tax report roadmap

This document plans step 6 of the business roadmap in [future-plans.md](../future-plans.md) §1: the FIF tax report. FIF means foreign investment fund. The FIF rules are how New Zealand taxes a resident's shares in foreign companies: not on the dividends received, but on an income figure worked out from market values. The report gives one Customer, for one tax year, that figure and the numbers behind it.

Step 6 is the main goal of the business roadmap. The roadmap row names five topics: the FDR and CV methods, the de minimis test, the quick sale adjustment, exempt ASX-listed shares and the New Zealand tax year. This document adds what the ledger must be able to say before any of them can be computed.

How to use this document:

- It is a `note`, not a build contract. Each item in §6 needs its own discussion and Feature Spec before any work starts. Do not create tables, routes or handlers from this file.
- Names in this file are working names. They become glossary terms only when a spec adopts them.
- The rules in §2 were read from Inland Revenue on 2026-11-27 (§11). They are a summary for planning. Each spec reads its rules again from the primary source and cites it, as [interest-run](../features/interest-run.md) R4 does. This file settles no rate and no threshold.
- The decisions in §8 are **decided** (2026-11-27). The first spec that needs one writes it as a rule and may refine it. Q9, Q10 and Q11 are working answers until their spec has read the Act (§11).
- The order in §9 is a recommendation. When an item gets a spec, or lands, update its row in §9.
- Nothing here is tax advice, and the report will say the same of itself.

---

## 1. Assessment

Three questions decide the shape of the step.

### 1.1 One feature or several

**Several.** Step 6 is five items and one optional item. Reasons:

1. **The computation is the small part.** The FDR and CV formulas are a few lines each. Most of the work is in their inputs: which Instruments are foreign, what a Transaction means for tax, what a purchase cost in NZD, and where a tax year starts. Each input has its own owner and its own test layer.
2. **The inputs do not exist yet, and they are of different kinds.** An Instrument has no country and no tax attribute (TR1). The ledger has no notion of a tax year or of an amount in NZD (TR2). Cost is kept per Account in the quote currency, and the rules ask for cost per person in NZD (TR3).
3. **An accepted spec is a build contract.** One spec would fix the shape of the report before the event mapping and the cost rule have been checked against a worked example.
4. **The rules arrive with their own examples.** Inland Revenue publishes worked examples for the threshold, for FDR with a quick sale and for CV (§11). They are tests for a Domain function with no I/O (TR4), which can be accepted and built before any report page exists.
5. **The report is a reader of the rest.** The PDF, its place in the reports catalog and the portal entry points (TR5) change nothing in the calculation.

### 1.2 Does it open ledger replay and period close

**No.** Step 6 opens neither ledger replay (X2) nor period close (X3) of [custodial-roadmap.md](custodial-roadmap.md) (Q2). Earlier plans expected both at the latest with this step.

- Ledger replay is a **write**: it rebuilds the `Holdings` projection after a Transaction is inserted or removed in the middle. The FIF report is a **read**. It needs the Transactions of a Customer walked in order (TR2, TR3), and it writes nothing back.
- Most FIF inputs are not path-dependent. Quantities add up. Purchases, sales and dividends of a year are sums of the legs booked in that year. Market values need two days a year.
- One input is path-dependent: the cost of what is still held, for the threshold test. TR3 works it out in the same ordered walk. The walk needs an ordering key for Transactions, which is the first decision a ledger replay spec would also have to make. TR2 makes it, so ledger replay can reuse it later.
- Period close would stop a backdated entry on an External account from changing a tax year already reported. Without it, the stored report execution is the record of what was reported, and a later run may differ (Q15). That is accepted for a learning project and written on the report.

### 1.3 Does it wait for steps 4 and 5

**No.** The roadmap row depends on steps 2 and 3, and both are complete. The advisory fee (step 4) is not part of FIF income under either method. The performance report (step 5) shares no rule with it. Step 6 can open now.

---

## 2. The rules in short

A planning summary of the rules for an individual. Sources are in §11; the code in the last column points there. "Individual" is Inland Revenue's "natural person".

| Topic | Rule | Source |
| --- | --- | --- |
| Who | A New Zealand tax resident who is not a transitional resident. A transitional resident or a non-resident has no FIF income | S1 p.4, S2 ¶4–5 |
| What a FIF is | A foreign company, which includes a foreign unit trust; a foreign superannuation scheme; a foreign life insurance policy. Not a bank account, a term deposit or a loan. A foreign company is one that is not tax resident in New Zealand | S1 p.4, p.24 |
| Attributing interest | A right in a FIF that no exemption covers. Only attributing interests are in the calculation and in the threshold | S1 p.5 |
| ASX exemption | Shares in a company that is on the official list of ASX Limited, is Australian resident (and not treated as resident elsewhere under a treaty), maintains a franking account, and is not stapled stock. The holding is then taxed like New Zealand shares. Inland Revenue has a lookup tool per ASX code and financial year. The listing is tested at the start of the income year, or on the day of acquisition for a share first bought during the year | S1 p.7, S5, S7 |
| Other exemptions | Australian unit trusts with adequate turnover or distributions, interests of 10 % or more, venture capital, employee share schemes and others | S1 p.6–7 |
| The $50,000 threshold | An individual whose attributing interests cost NZ$50,000 or less in total **at all times in the year** does not apply the FIF rules. Dividends are then taxed as received. Above it on any day, every attributing interest is in the rules for that year; the first $50,000 is not exempt | S1 p.9–10 |
| Cost for the threshold | What was paid in NZD at the time of purchase, including transaction fees. Not the market value. Exempt interests are not counted. A share from a split or a non-taxable bonus takes a part of the original cost. An interest bought before 1 January 2000 may use half its market value on 1 April 2007 | S2 ¶7–8, S1 p.23 |
| Joint holdings | Each person counts their share of the cost | S1 p.9, S2 ¶9 |
| Opting in | A person below the threshold may choose to apply the rules. The choice then binds later years until a condition over the four previous tax years is met | S1 p.9, S6 |
| Methods | Six: fair dividend rate (FDR), comparative value (CV), cost, deemed rate of return, revenue account, attributable FIF income. An individual with listed ordinary shares uses FDR or CV | S1 p.10–12 |
| FDR | `0.05 × opening market value + quick sale adjustment`. The opening market value is the total market value of the attributing interests held at the start of the income year. A share bought during the year has an opening value of zero. Dividends and gains are not taxed separately. No loss can arise | S1 p.11, p.14, p.22 |
| Quick sale adjustment | Applies when a holding in the same FIF is increased and decreased in the same year. It is the lesser of the **peak holding method amount** and the **actual gain**. Peak holding method amount = `5 % × peak holding differential × average cost`. The differential is the lesser of (greatest holding in the year − holding at the start) and (greatest holding − holding at the end). The average cost is over all purchases of that share in the year. The actual gain is worked out for each disposal as far as it follows purchases made earlier in the year, last bought first sold, and counts the dividends on those shares | S1 p.15, S8, S9 |
| CV | `(closing market value + gains) − (opening market value + costs)`. Gains are amounts received from holding or disposing of the interest: sale proceeds, dividends, and foreign withholding tax or other credits. Costs are what was spent on buying, plus foreign income tax the person is liable to pay | S1 p.11 |
| Choice of method | An individual may choose FDR or CV each year, but one method for all interests where both are allowed. Under CV the total over those interests cannot be below zero, and the loss cannot be used elsewhere | S1 p.14, p.16, p.19 |
| Non-ordinary shares | FDR is not allowed for fixed-rate shares and some other kinds. They use CV, or the deemed rate of return | S1 p.16 |
| Market value | The share price on a recognised exchange, or a published unit price. Inland Revenue accepts the closing price of the last day of the previous income year as the opening value | S1 p.13, S8 |
| The income year | For an individual, 1 April to 31 March. The year is named after the year it ends in: the 2026 income year ends on 31 March 2026 | S2 ¶17–18 and its example |
| Currency conversion | One way, used for all attributing interests: the actual rate of the day for each amount and each market value, or the rolling 12-month average rate. Inland Revenue accepts its mid-month rate as the actual rate of any day in that month, and the 31 March end-of-month rate as the rate of 1 April. A person who starts using the rolling average must keep using it every year. Rates from the Reserve Bank or another central bank may be used, from one source consistently | S1 p.13, S4 |
| Foreign tax credit | Foreign tax withheld on a dividend from a FIF may be a credit. The credit is limited to the New Zealand tax on that segment of foreign income, which is the FIF income, not the dividend. No credit when the segment has a loss. An unused credit is lost | S1 p.20 |
| RWT and imputation | Resident withholding tax taken from a dividend offsets tax payable and the excess is refunded, even when the FIF income is zero. A New Zealand imputation credit on an Australian dividend is limited by the FIF income of that interest | S1 p.20 |
| Return and disclosure | FIF income goes in the IR3 return, with an overseas income summary (IR1261) by country. An individual who holds less than 10 % of a company in a treaty country and uses FDR or CV usually files no separate FIF disclosure | S1 p.21, S2 ¶10–11 |
| Recent and proposed changes | From 1 April 2025 the revenue account method exists for some new migrants and unlisted shares. A 2026 Bill raises the threshold to NZ$100,000 "for the 2026–27 and later income years". Its commentary was read from Inland Revenue on 2026-11-28 (tax-cost-threshold §14, S5); the Bill was at select committee on 12 October 2026 and was not law in any source read. The April 2026 edition of the guide says $50,000 | S1 p.17, S10 |

---

## 3. Where we start (2026-11-27)

| Area | Today | Where |
| --- | --- | --- |
| Whose tax it is | A Customer is one person. There is no joint holder, trust or company | interest-run §2 Out, ADR 0020 |
| Tax residency | The tax profile says `NewZealand` or `Overseas`. Nothing says transitional resident, and nothing has a date | interest-run R2 |
| What is foreign | An Instrument had a symbol, a name and a quote currency, and no country, exchange, ISIN or kind. Since TR1 (accepted 2026-11-27) it also has an issuer country and a FIF treatment per tax year; exchange, ISIN and kind stay out. A dividend states its tax source (`NewZealand` or `Foreign`), on the dividend; it is not checked against the issuer country | instruments §6.1, instrument-tax-attributes, dividends R39 |
| Instruments | A Tenant's catalog. There is no platform-wide Instrument | instruments §5 |
| Quantity on a past instant | The sum of the security legs booked before it. Used per UTC day by the valuation read. Since TR2 (accepted 2026-11-28) a position can also be cut at an instant, and the tax year read sums a Customer's quantity over all their Accounts at the two instants of a tax year, with reversed Transactions left out | account-valuation R4, R18; tax-year-events R22 |
| Cost | Average cost per `(Account, Instrument)` in the quote currency, as it is now. No cost on a past day and no lots. The tax cost in NZD, one per tax holding over all the Customer's Accounts, is worked out on read for the threshold test ([tax-cost-threshold.md](../features/tax-cost-threshold.md)) | holdings R10, R14 |
| Purchases and sales | `Buy` and `Sell` carry a `Principal` cash leg in the Account's currency and a `Fee` cash leg with a fee detail. When the Instrument is quoted in another currency the Transaction stores the FX rate it used | buy-sell, trade-fees, glossary "FX detail" |
| Holdings that arrive without a purchase | `OpeningHoldings`, `StockIn`, an in-kind transfer and a migration batch carry one cost in the quote currency. Only an in-kind transfer may carry an acquisition date. The tax cost reads the carried cost at the rate of that date, or of the day it was booked; a cost of 0 is not stated ([tax-cost-threshold.md](../features/tax-cost-threshold.md) R9) | holdings R9, in-kind-transfers R25, migration-batches R4 |
| Share reorganisations | `Split`, `Bonus` and `Scrip` change the quantity and keep the total cost | split, corporate-actions |
| Dividends | A `Dividend` Transaction has the gross, a `Tax` leg per tax (`Rwt`, `Nrwt`, `ForeignWithholding`) with its tax detail, the imputation credit, the Instrument and the shares. On a Custodial account RWT is taken from a foreign dividend whatever the Customer's FIF position | dividends R37–R39, external-income R11 |
| Order of Transactions | `bookedAt` is set by the server on a Custodial account. On an External account the caller sends it, and it may be any past date. Since TR2 the ordering key of a walk is `bookedAt`, then the internal Transaction id | custody-modes R10, R11; tax-year-events R8 |
| Market value on a past day | The valuation read values one Account for one UTC day at the unadjusted close, with a 4-day age limit, in the quote currency and the Account's currency. Stored valuations exist for Custodial accounts only. Since TR2 the tax year read values each Account's position at the instant of a tax year's start or end, as of 31 March, in NZD | account-valuation, valuation-run, tax-year-events R22 |
| FX rates | Daily central bank reference rates from Frankfurter for every pair of active currencies, from the history floor date | ADR 0019, market-data-history |
| Prices | EODHD daily closes from the history floor date; the free plan serves one year. None for NZX and Tokyo | market-data-history, [known-issues/eodhd-nz-jp-coverage.md](../known-issues/eodhd-nz-jp-coverage.md) |
| A day | A UTC date everywhere | valuation-roadmap H2 |
| Reports | A catalog with scopes `Customer`, `Book` and `Tenant`, a generator per report, PDF output, a stored execution per run. The Customer Transactions Report takes a date range | reports §1, R13 |
| Tax documents | None. The interest and dividend specs left every tax statement to this step | interest-run §11 point 19, dividends §2 Out, external-income §2 Out |

---

## 4. Target

Step 6 is done when, for one Customer who is a New Zealand resident and one tax year that has ended, the platform produces a report that states:

1. **Which holdings are attributing interests**, which are exempt and under which exemption, and which could not be classified.
2. **The threshold test:** the highest total cost in NZD of the attributing interests on any day of the year, the day it was reached, and whether it was above the threshold of that year.
3. **Per attributing interest:** opening and closing quantity and market value, purchases, sales, dividends, the peak holding, the quick sale figures, the FDR income and the CV income.
4. **The totals under each method,** with the CV total not below zero, and which is lower.
5. **The tax already paid:** foreign withholding tax per interest and country, RWT and imputation credits.
6. **Every price and rate it used,** and anything that made a figure incomplete.

And when the published worked examples of §11 give the same results through the same code, to the cent.

The report does not say what tax is payable, which method to choose, or whether the rules apply to the person. It covers what the platform's ledger knows. A holding the Customer has elsewhere counts only if it is recorded in an External account.

---

## 5. Principles

Working rules the plan assumes. Each is open to change in the spec that first needs it.

| # | Principle | Why |
| --- | --- | --- |
| T1 | **The taxpayer is the Customer, not the Account.** Every figure is worked out over all Accounts of the Customer, of both custody modes, per Instrument. A move between two Accounts of the same Customer is not a purchase or a sale. | The rules are per person and per FIF. Holdings projections are per Account. |
| T2 | **A tax year is 1 April to 31 March in New Zealand time,** named after the year it ends in. Only the tax year of this plan is cut in New Zealand time (Q3). A day stays a UTC date in every other feature. | The income year of an individual. A sale late on 31 March UTC is already in the next tax year in New Zealand. valuation-roadmap H2 is not changed. |
| T3 | **Every figure is in NZD,** whatever the Account's currency or the Tenant's reporting currency. | The return is in NZD. |
| T4 | **One conversion rule for every amount of every year.** | Inland Revenue requires the chosen way to be used for all interests, consistently. |
| T5 | **No invented number.** An Instrument that is not classified, a holding with no price, or an amount with no rate makes the result incomplete. It is not counted as zero and not left out silently. | valuation-roadmap H5. A tax figure may not be a guess. |
| T6 | **A rate or a threshold is a value per tax year,** not one constant: the 5 % rate, the $50,000 threshold. | The threshold is before Parliament for change (§2). `WithholdingRates` already keeps tax rates as Domain constants that change only with a spec amendment. |
| T7 | **The calculation is a Domain function with no I/O** over an ordered list of tax events. | The published examples become unit tests, with no database. |
| T8 | **Both methods are computed and both are shown.** The platform does not choose. | The choice is the taxpayer's, and it depends on holdings the platform may not see. |
| T9 | **A report keeps what it used:** the prices and their dates, the rates and their dates, the classification of each Instrument, and the instant the ledger was read. A stored report is not changed; a new run is a new execution. | valuation-roadmap H6. A backdated External entry can change a later run (§1.2). |
| T10 | **This step only reads.** It posts no Transaction and changes no holding. | Tax is paid by the Customer through their return, not through the ledger. |
| T11 | **External figures are the caller's numbers,** and the report says so. | custody-mode-grouping tells the Customer the same on every read. |
| T12 | **Worked example first.** Each item is worked by hand in a spreadsheet before its spec is written, and the numbers become the tests. | The working method of future-plans §1. |

---

## 6. Items

### TR1. Tax attributes of an Instrument

- **What:** an Instrument can say what the FIF rules need to know about it: the country its issuer is tax resident in, and its FIF treatment for a tax year. Values of the treatment (as accepted): `NotForeign` (a New Zealand company or fund), `ExemptAsxListed` (an ASX-listed Australian share), `ExemptOther` (another exemption, named in a note), `Attributing` (FDR allowed) and `AttributingNoFdr` (FDR not allowed); with no row for a tax year the Instrument is not classified.
- **Why first:** every later item starts by asking which holdings count. Nothing else depends on fewer things.
- **Changes:** fields on the Instrument and on its read and write routes; a treatment that can differ from one tax year to the next, because the ASX exemption is tested per year; the default "not classified" for every existing Instrument; a classification for the 18 Instruments of the demo catalog; the Back Office page that edits an Instrument ([back-office-instruments.md](../backoffice/back-office-instruments.md)).
- **Depends on:** nothing.
- **Also decide:** who classifies (Q6); whether the country is free text, an ISO code or a catalog; whether the treatment is one value with a year from which it applies or one row per year; whether a dividend's tax source must agree with the Instrument's country, or only a warning is shown; what happens to two Instrument rows for one company listed on two exchanges (recommended: two interests, as entered).
- **Amends:** instruments, demo-tenant-seed, back-office-instruments.
- **Worked example:** the demo catalog classified by hand, each Australian symbol checked in Inland Revenue's lookup tool for one year.
- **Spec:** [instrument-tax-attributes](../features/instrument-tax-attributes.md) (`accepted` and landed, 2026-11-27). It names the values `NotForeign`, `ExemptAsxListed`, `Attributing` and `AttributingNoFdr`, and adds `ExemptOther` for the exemptions of §7 that the platform does not check. Its points 1 to 13 answer "Also decide" and were confirmed as recommended. A person checked the three Australian symbols in the lookup tool on 2026-11-27: it answered for tax year 2026 only, so the 2027 rows of the demo catalog are assumed from 2026.

### TR2. Tax year events in NZD

- **What:** a read that turns the ledger of one Customer into what the tax rules see for one tax year, per Instrument: the quantity and the market value at the start and at the end, and between them an ordered list of **tax events**, each with a quantity and an NZD amount. No table and no job.
- **Why its own item:** it holds every rule about what a Transaction means for tax, where a year starts and how an amount becomes NZD. TR3 and TR4 only compute over its result.
- **Changes:** the tax year as a pair of instants in New Zealand time (Q3); the ordering key of Transactions within the walk (`bookedAt`, then a tie-break; Q2); the event mapping below; the NZD amount of each event (Q4); the opening and closing market value through the valuation function of [account-valuation](../features/account-valuation.md), cut at the tax year's instants (Q5); the incomplete marks of T5; a read route for one Customer and one tax year (working shape), with the permissions of the Customer's other reads and the usual 404.
- **Event mapping (settled by the spec, tax-year-events R9):**

| Ledger | Tax event |
| --- | --- |
| `Buy` | Acquisition. Amount: principal plus fee (Q10) |
| `Sell` | Disposal. Amount: principal less fee (Q10) |
| `Dividend` paid in cash | Dividend: gross, foreign tax, RWT or NRWT, imputation credit |
| `Dividend` with a reinvestment | The dividend, and an acquisition for the reinvested amount |
| `Split`, `Bonus` | Reorganisation: the quantity changes, no amount. Earlier quantities of the year are restated so that a peak is compared like with like |
| `Scrip` | Reorganisation, with a notice: the platform cannot know whether the issue was taxable (tax-year-events R13) |
| `OpeningHoldings`, `StockIn`, an in-kind transfer in, a migration batch | Arrival without a purchase (Q8) |
| `StockOut`, an in-kind transfer out | Departure without a sale (Q8) |
| A move between two Accounts of the same Customer | One `Move` with no amount, recognised on read (T1; tax-year-events R15) |
| `Adjustment` with a security leg | Correction, with a notice and no amount. A correction is not a trade (tax-year-events R16) |
| A Transaction and its `Reversal` | Both left out, wherever they fall. Tax looks at what happened, not at how it was booked. This differs from valuation-roadmap H3, on purpose |
| Cash-only types, `FxConversion` | Nothing. Cash in a foreign currency is not a FIF. `Interest` is read by TR6 only |

- **Depends on:** TR1 for which Instruments to walk. The read can be built for all Instruments and filtered later.
- **Also decide:** whether a position is cut at the New Zealand instant while the price is the market's own close of 31 March (Q5); the history floor date, which must be on or before the 31 March that opens the first tax year to be reported, and what the EODHD plan in use can serve; whether the peak holding is read after every Transaction or at the end of each day (recommended: after every Transaction, so that a buy and a sell on one day is a quick sale).
- **Risk to test:** a trade between 11:00 UTC and 24:00 UTC on 31 March. It is in the next tax year (T2) and in the UTC day the valuation run stores. The FIF figures and the stored valuation of that day then differ, correctly.
- **Amends:** holdings and posting (the walk and its ordering key), account-valuation (a cut at an instant, not only at the end of a UTC day).
- **Worked example:** one Customer with two Accounts in two currencies over two tax years: a buy and a sell either side of the year end, a split, a cash dividend with foreign tax, a reinvested dividend, a reversal, and a move between the two Accounts.
- **Spec:** [tax-year-events](../features/tax-year-events.md) (`accepted` and landed, 2026-11-28). It names the events `Acquisition`, `Disposal`, `Dividend`, `Reorganisation`, `Arrival`, `Departure`, `Move` and `Correction`, and calls one Instrument over all Accounts of a Customer a tax holding. It settles the two rows the draft mapping left open: a `Scrip` is a reorganisation with a notice, and an `Adjustment` of a holding is a correction with a notice. A move is recognised on read by a rule, because no Transaction links its two sides. Its points 1 to 17 answer "Also decide" and were confirmed as recommended. Sections EX 51, EX 52 and EX 57 of the Act could not be read; the spec says which rules rest on the guide alone.

### TR3. Cost in NZD and the threshold test

- **What:** the cost in NZD of the attributing interests a Customer holds, after every tax event, and from it the threshold test of a tax year: the highest total, the day it was reached, and above or not.
- **Why its own item:** it is the one path-dependent input (§1.2), and the only one that reaches back before the tax year. A wrong answer here decides whether everything else applies.
- **Changes:** a cost walk in Domain over the tax events from the Customer's first event on; the cost an acquisition adds (Q10); the cost a disposal removes (Q9); the cost of an arrival without a purchase, from the cost it carries and a rate (Q8); a reorganisation keeps the cost; the threshold as a value per tax year (T6); the test in the result of the TR2 read or beside it.
- **Depends on:** TR1, TR2.
- **Also decide:** that a holding with no cost in the ledger, or an arrival with no rate for its date, makes the test incomplete and not passed; whether the option of half the 1 April 2007 market value for interests bought before 2000 is offered (recommended: no); whether the test counts a day's highest point or its end (recommended: after every event).
- **Does not change:** the `Holdings` projection, its average cost in the quote currency, or any write.
- **Worked example:** the example of S2 (a person who goes above $50,000 once, sells half and stays in the rules for that year, then leaves them the next), and a second case that crosses the threshold through a foreign-currency purchase.
- **Spec:** [tax-cost-threshold](../features/tax-cost-threshold.md) (`accepted` and landed, 2026-11-28). It calls the figure the tax cost: one per tax holding, in NZD, from the Customer's first event on, with one average over all Accounts. The cost and the test are fields of the tax year read, not a route of their own. An arrival with a cost of 0 is read as "not stated". The test has its own `complete`, apart from the tax year's, and an incomplete test still says `Above` when the known costs alone are above. The threshold is NZ$50,000 for tax years 2009 to 2026 and has no value from 2027 until the Bill is law. A treatment that changed while the shares were held makes the test incomplete. Its points 1 to 18 answer "Also decide" and were confirmed on 2026-11-28, every one as recommended. Section CQ 5 of the Act could not be read; the spec says which rules rest on the guides alone.

### TR4. FIF income: FDR, the quick sale adjustment and CV

- **What:** the calculation. A Domain function that takes the TR2 result of the attributing interests of one tax year and returns, per interest and in total, the FDR income with its quick sale adjustment and the CV income, and the lower of the two totals.
- **Changes:** FDR as `rate × opening market value`; the peak holding method amount per interest; the actual gain per interest; the quick sale adjustment as the lesser of the two totals over all interests; CV per interest and its total, not below zero; the result added to the TR2 read or as a read of its own; the incomplete mark carried through.
- **Depends on:** TR2. TR3 decides whether the result applies, not how it is computed.
- **Also decide:** how a disposal is matched to the purchases of the same year, and at which cost (Q11); whether an interest with FDR not allowed is computed under CV alone or left out (Q12); rounding, once at the end or per interest; whether foreign tax withheld is added to CV gains, as S1 p.11 words it; one spec or two (recommended: one, the two methods share every input).
- **Tests from published examples:** S1 p.14 (three companies: FDR $15,000, CV reduced to 0); S1 p.15 (quick sale: peak holding amount $2,200, actual gain $12,000, FIF income $12,200); S2 example (FDR $2,750, CV $4,125); S8 examples 4 and 5 (a quick sale with a dividend; a negative total gain limited to zero), once read again from the source.
- **Second check:** Inland Revenue's online FIF calculator (S3) with the worked example's numbers typed in.
- **Spec:** [fif-income](../features/fif-income.md) (`accepted` and landed, 2026-11-28). One spec for both methods. The income is a field of the tax year read, computed from that read's own figures and whatever the threshold test says. The quick sale is worked out on comparable quantities: a sale is matched to the purchases of the same tax year, last bought first sold; the gain is on the matched shares, at the average cost of every purchase of the year, with a share of the dividends pro rata to the matched shares held. The adjustment is the lesser of the two totals over all tax holdings. A dividend is a CV gain at its gross plus the imputation credit, so the foreign tax withheld is in it once. Fees are in the cost and off the proceeds. Figures are rounded per tax holding and a total is their sum. A tax holding that is incomplete or not classified is not computed and the totals are then not given; one where FDR is not allowed is listed and not computed (Q12), and the totals stay. Its points 1 to 18 answer "Also decide" and were confirmed on 2026-11-28, every one as recommended. Sections EX 51, EX 52 and EX 54 of the Act could not be read, and the calculator was not used; both stay open in the spec.

### TR5. The FIF report

- **What:** the report of §4 as an entry in the reports catalog, scope `Customer`, for one tax year: a PDF built from the TR2, TR3 and TR4 results, stored as an execution.
- **Changes:** a new report code and generator; the parameter (the tax year); the sections of §4; the list of prices, rates and classifications used (T9); the statements the report makes about itself (not tax advice, what it covers, External figures are the caller's); the entry points on the Customer page of the Adviser Portal and in the Customer Portal; the report in the demo tenant's grants.
- **Depends on:** TR1 to TR4.
- **Also decide:** manual only or also scheduled (recommended: manual only); which tax years may be asked for (Q13); who may ask (recommended: as the Customer Transactions Report, the Customer for themselves); what the report shows for an `Overseas` Customer (Q14); whether the numbers are also offered as a data file, since the catalog is PDF only (Q16); how the foreign tax is shown (Q17).
- **Amends:** reports, the two portal page contracts, demo-tenant-seed.
- **Worked example:** the TR2 example, printed.

### TR6. Tax year income summary (optional)

- **What:** the income a Customer was paid in one tax year under the ordinary rules: dividends and interest, each with the tax withheld by kind, the imputation credits and the country, in NZD. A second catalog report, or a section of TR5.
- **Why:** it is what a Customer below the threshold returns, and what applies to exempt interests such as ASX-listed shares at any size. The interest and dividend specs left it to this step.
- **Why optional and last:** it is a list of sums with no algorithm, a content-type report in the sense of future-plans §1. No other item waits for it.
- **Depends on:** TR2 (the tax year, the NZD rule). TR1 for the country.

---

## 7. What this plan leaves out

| Item | Where it goes |
| --- | --- |
| Ledger replay as a write, cancel and rebook, period close | Not opened by this step (§1.2). They stay with the business roadmap until something needs a write in the past |
| Tax lots | Not needed: FDR and CV read no lot, and the quick sale rule matches only within one year (TR4) |
| Holders that are not one individual: joint holders, trusts, companies | The legal structure of the holder, still open (ADR 0020) |
| The cost method, the deemed rate of return, the revenue account method, the attributable FIF income method | Not planned. They are for unlisted shares, non-ordinary shares, some new migrants and holdings of 10 % or more |
| Foreign superannuation and life insurance | Not planned. No Account holds them |
| Exemptions other than the ASX one | An Instrument under another exemption is classified by hand as exempt (TR1). The platform checks none of them |
| Transitional residents, a change of residence during a year, an interest that becomes or stops being exempt during a year | Not planned. Each is a deemed sale and purchase at market value (S1 p.22) |
| The choice to apply the rules below the threshold, and its four-year rule | Not stored (Q14). The report shows both branches |
| The foreign tax credit allowed, tax payable, the IR3 and IR1261 forms, a filing with Inland Revenue | Not planned. They need the person's whole return |
| Whether an individual can deduct advisory fees, and GST on them | Step 4 of the business roadmap. Not part of FIF income under either method |
| A refund of RWT taken from a FIF dividend | Not a ledger flow. The report lists the RWT; the refund happens through the return |
| Taxable bonus issues and other non-cash dividends | Out of dividends too. Revisit with `Scrip` (TR2) |
| An estimate for a tax year still in progress | Not planned (Q13) |
| NZX and Tokyo prices | Unchanged. Most NZX holdings are not foreign and need no price here; an attributing interest with no price, such as a Tokyo holding, makes the report incomplete (T5) |

---

## 8. Decisions

Every question is **decided** (2026-11-27), as recommended, Q3 with its scope stated. The answer is what the items in §6 assume; the first spec that needs one writes it as a rule and may refine it. Q9, Q10 and Q11 are working answers: each names the section of the Act its spec reads first (§11).

| # | Question | Answer | First needed by |
| --- | --- | --- | --- |
| Q1 | One spec or several? | Several: TR1 to TR5, and TR6 if wanted (§1.1) | now |
| Q2 | Does step 6 open ledger replay or period close? | No. The report walks the Transactions as a read. TR2 decides the ordering key, which a later ledger replay reuses (§1.2). Written as tax-year-events R8: `bookedAt`, then the internal Transaction id | TR2 |
| Q3 | Is the tax year cut in New Zealand time or on UTC dates? | New Zealand time, for the tax year of this plan only (TR2 to TR6). The zone is one fixed zone, the same for every Tenant and not a setting, because the tax year is New Zealand law. The year starts at 00:00 on 1 April in New Zealand, which is 11:00 UTC on 31 March. Every other feature keeps the UTC date: the valuation day, the interest run, the date range of a report (valuation-roadmap H2). Not chosen: UTC dates here too, with up to 13 hours of trades in the wrong year. Written as tax-year-events R3 | TR2 |
| Q4 | Which rate turns an amount into NZD? | The actual rate of the day. A cash leg already in NZD is used as it is. Any other amount is converted at the `FxRates` rate of its day. Not chosen: Inland Revenue's own mid-month and end-of-month tables, which are a second rate set with a loader (valuation-roadmap §6 foresaw this); or the rolling 12-month average, which must then be kept every year. Written as tax-year-events R18–R21: the rate of the UTC date of `bookedAt` | TR2 |
| Q5 | Which price and which rate value a holding at the start and the end of the year? | The unadjusted close of the latest source date on or before 31 March, in the market's own dates, with the age limit of a valuation, converted at the rate of 31 March. The quantity is the one held at the New Zealand instant. The opening value of a year is then the closing value of the year before, by construction. Written as tax-year-events R22 | TR2 |
| Q6 | Who classifies an Instrument? | A person with `instruments.manage`, by hand, per Tenant, because the catalog is the Tenant's. The platform calls no Inland Revenue tool. Not classified is the default and makes a report incomplete. Written as instrument-tax-attributes R2 | TR1 |
| Q7 | What is one interest? | One Instrument held by one Customer, over all their Accounts (T1). Written as tax-year-events R5, the tax holding | TR2 |
| Q8 | How does a holding count that arrives without a purchase (`OpeningHoldings`, `StockIn`, an in-kind transfer in, a migration batch)? | For the threshold it brings the cost it carries, converted at the rate of its acquisition date when one is stated and of its `bookedAt` otherwise. For FDR and CV it is an acquisition at the market value of the day it arrives, and the interest is flagged: the platform cannot know whether it was held on 1 April. Sharesies' holdings report counts a transfer in with the purchases (S11). The remedy is to record the earlier history in an External account. A departure mirrors it. The market value and the flag are written as tax-year-events R14; the cost it brings as tax-cost-threshold R9; for FDR and CV as fif-income R6 | TR2, TR3, TR4 |
| Q9 | Which cost does a disposal remove, for the threshold? | The average cost in NZD of that interest. The guide does not say; check the Act. Written as tax-cost-threshold R7; the Act was not read (its §14) | TR3 |
| Q10 | Are trade fees part of cost and proceeds? | Yes: a buy fee adds to the cost, a sell fee comes off the proceeds. S2 ¶8 says so for the threshold. For CV, check section EX 51. This answers the question future-plans §1 left for this step. Written as tax-year-events R10, with the principal and the fee given apart; for both methods as fif-income R6 | TR2, TR4 |
| Q11 | How is the actual gain of a quick sale worked out? | A disposal is matched to the purchases made earlier in the same year, last bought first, and the gain uses the average cost of the year's purchases, with the dividends on the matched shares pro rata (S1 p.15, S9). The sources differ in detail; read sections EX 52 and EX 54 before the spec. Written as fif-income R11, R13 and R14; the Act was not read (its §14) | TR4 |
| Q12 | What happens to an interest where FDR is not allowed? | It is listed and not computed, and the report is incomplete. Not chosen: compute it under CV alone, with its loss allowed. Written as fif-income R3 and R23: the totals of the other interests stay | TR4 |
| Q13 | Which tax years can be reported? | A year that has ended and whose 31 March prices and rates are final, as a valuation day is final ([valuation-run](../features/valuation-run.md) R4). No estimate for a year in progress. Written early as tax-year-events R4, for the read | TR5 |
| Q14 | Does the platform store whether the rules apply to the Customer (transitional resident, the choice to opt in)? | No. For a `NewZealand` Customer the report always shows the threshold test and both methods, and the Customer reads the branch that applies. For an `Overseas` Customer the report is refused | TR5 |
| Q15 | What if an External entry is backdated into a year already reported? | Nothing stops it. The stored execution is what was reported; a new run shows the ledger as it is now, with its own instant. Revisit with period close | TR5 |
| Q16 | Is a PDF enough? | The PDF is the report. The numbers are also readable as JSON through the TR2 and TR4 reads, which the tests and the portal pages use. No spreadsheet file in the first version | TR5 |
| Q17 | How is foreign tax shown? | As paid, per interest and country, next to the FIF income of that interest. The allowed credit is not computed (§7) | TR5 |
| Q18 | Is the income summary (TR6) part of step 6? | Yes, as the last and optional item | TR6 |

---

## 9. Recommended order and tracking

Each step depends only on earlier steps.

| Step | Item | Closes | Spec | Status |
| --- | --- | --- | --- | --- |
| 1 | TR1 tax attributes of an Instrument | "No ISIN / Exchange / Kind" stays; the country and the FIF treatment are new | [instrument-tax-attributes](../features/instrument-tax-attributes.md) | accepted and landed (2026-11-27) |
| 2 | TR2 tax year events in NZD | The ordering key of Transactions, for a later ledger replay; the open point on brokerage and the cost base (trade-fees §11) | [tax-year-events](../features/tax-year-events.md) | accepted and landed (2026-11-28) |
| 3 | TR3 cost in NZD and the threshold test | "Tax lots and a cost in NZD" of custodial-roadmap §10, as far as this step needs it | [tax-cost-threshold](../features/tax-cost-threshold.md) | accepted and landed (2026-11-28) |
| 4 | TR4 FIF income: FDR, quick sale, CV | — | [fif-income](../features/fif-income.md) | accepted and landed (2026-11-28) |
| 5 | TR5 the FIF report | "A tax statement for the Customer" (interest-run §11 point 19; dividends and external-income Out), for FIF income | — | not started |
| 6 | TR6 tax year income summary | The same, for dividends and interest | — | optional; not started |

Steps 1 and 2 do not depend on each other and may swap. Step 4 may come before step 3: it needs only TR2.

When step 5 has landed, step 6 of [future-plans.md](../future-plans.md) §1 is complete. Steps 4 and 5 of that roadmap (the advisory fee and the performance report) do not depend on this plan, and this plan does not depend on them.

---

## 10. Existing documents each item touches

| Document | Change | Item |
| --- | --- | --- |
| [features/instruments.md](../features/instruments.md), [backoffice/back-office-instruments.md](../backoffice/back-office-instruments.md), [features/demo-tenant-seed.md](../features/demo-tenant-seed.md) | The country and the FIF treatment; the demo catalog classified | TR1 |
| [features/holdings.md](../features/holdings.md), [features/posting.md](../features/posting.md) | The ordered walk and its ordering key | TR2 |
| [features/account-valuation.md](../features/account-valuation.md) | A position cut at an instant | TR2 |
| [features/market-data-history.md](../features/market-data-history.md) | What the floor date must cover for a tax year | TR2 |
| [features/in-kind-transfers.md](../features/in-kind-transfers.md), [features/migration-batches.md](../features/migration-batches.md) | The carried cost and the acquisition date are now read by a rule | TR3 |
| [features/trade-fees.md](../features/trade-fees.md) | Its open point on brokerage and the cost base is answered | TR2 |
| [features/reports.md](../features/reports.md), Adviser Portal and Customer Portal page contracts | A third and maybe a fourth report; entry points | TR5, TR6 |
| [features/dividends.md](../features/dividends.md), [features/external-income.md](../features/external-income.md), [features/interest-run.md](../features/interest-run.md) | "No tax statement" points to the report | TR5, TR6 |
| [custodial-roadmap.md](custodial-roadmap.md) §13.1, [glossary.md](../glossary.md) "Ledger replay", [future-plans.md](../future-plans.md) §9 | No longer say that ledger replay and period close open at the latest with step 6 (reworded 2026-11-27). [valuation-roadmap.md](valuation-roadmap.md) §6 points to Q2, Q3 and Q4 | Q2 |
| [domain-model.md](../domain-model.md), [database-design.md](../database-design.md), [api-design.md](../api-design.md), [glossary.md](../glossary.md), [features/README.md](../features/README.md) | With each spec | all |
| [future-plans.md](../future-plans.md) §1 | Points here for step 6 | now |

---

## 11. Sources

Read on 2026-11-27. A spec that writes a rule reads the source again and cites it with its own date.

| # | Source | Used for |
| --- | --- | --- |
| S1 | Inland Revenue, *Guide to foreign investment funds*, IR461, April 2026 edition (linked from `ird.govt.nz/fif`). Page numbers in §2 are this edition's | Most of §2: definitions, exemptions, the threshold, the methods, the quick sale example, currency conversion, losses, foreign tax credits |
| S2 | Inland Revenue, fact sheet IS 24/10 FS 1, *Income tax – Share investments (whether the FIF rules apply)*, 18 December 2024 (`taxtechnical.ird.govt.nz`) | Who the rules apply to, cost including transaction fees, the income year, the worked example over four years |
| S3 | Inland Revenue, "Calculate my foreign investment fund income" and its online help (`ird.govt.nz/calculate-my-fif-income`, last updated 5 October 2026; help 5 July 2021) | The calculator as a second check; one method for all interests |
| S4 | Inland Revenue, "Overseas currency conversion to NZ dollars" (`ird.govt.nz/overseas-currency`, last updated 4 November 2026) | The rate tables, their source, and which rates are accepted |
| S5 | Inland Revenue, "Foreign investment fund Australian listed share exemption tool" (`ird.govt.nz/fif-australia-tool`, last updated 5 October 2026) | The lookup per ASX code and year |
| S6 | Inland Revenue Tax Technical, "Exemption for natural persons with less than $50,000 of FIF interests" and "Choice of FIF calculation method" (commentary on the 2012 Act) | Opting in and its four-year rule; switching between FDR and CV |
| S7 | Inland Revenue Tax Technical, "Technical amendments to the offshore portfolio share investment rules" | When the ASX listing is tested; average cost in the quick sale rule |
| S8 | Inland Revenue Tax Technical, "New tax rules for offshore portfolio investment in shares" (commentary on the 2006 Act). Its later part was read through a summary, so its examples must be read again before they become tests. Examples 1 to 5 were read again on 2026-11-28: fif-income §14 | The closing price of the previous year as opening value; FDR and CV worked examples |
| S9 | Inland Revenue Tax Technical, "Fair dividend rate method: quick sale gain amount" (2011 remedial amendment) | The two amounts of the quick sale adjustment; share reorganisations |
| S10 | Press and adviser summaries of Budget 2026 and of the 2026 annual rates Bill | The proposed NZ$100,000 threshold. Secondary sources only on 2026-11-27. Inland Revenue's commentary on the Bill was read on 2026-11-28: tax-cost-threshold §14, S5 |
| S11 | Help pages of Hatch, Sharesies and Sharesight on their FIF reports | How platforms lay out a FIF report, treat brokerage and transfers, and match quick sales. Layout and cross-checks only, not rules |

Not read: the Income Tax Act 2007 itself (sections CQ 5, EX 31, EX 51, EX 52, EX 54, EX 57). `legislation.govt.nz` refused automated reads on the day. Q9, Q10 and Q11 each name the section their spec must read.

Published FIF reports of platforms can be compared with the platform's own only as far as the inputs match: each uses its own prices and rates. They are a check on layout and on the order of magnitude. The tests come from S1, S2 and S8 and from the hand-worked examples of each item.
