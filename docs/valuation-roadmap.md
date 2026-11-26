---
title: Historical valuation roadmap
status: note
language: en
created: 2026-11-25
updated: 2026-11-26
related:
  - future-plans.md
  - function-plan.md
  - custodial-roadmap.md
  - features/instrument-prices.md
  - features/fx-rates.md
  - adr/0019-fx-rate-source-and-refresh.md
  - features/simulated-instruments.md
  - features/net-worth.md
  - features/custody-mode-grouping.md
  - features/holdings.md
  - features/posting.md
  - features/custody-modes.md
  - features/interest-run.md
  - features/corporate-actions.md
  - features/dividends.md
  - features/orders.md
  - features/reports.md
  - adviser-portal/adviser-portal-dashboard.md
  - known-issues/eodhd-nz-jp-coverage.md
  - features/market-data-history.md
  - features/account-valuation.md
---

# Historical valuation roadmap

This document plans step 3 of the business roadmap in [future-plans.md](future-plans.md) §1: historical valuation. That row asks for "prices and FX rates as of a date, and holding snapshots", so that every period report (the advisory fee, the performance report, the FIF tax report) can read what an account was worth on a past day.

It was written to answer one question first: is step 3 one feature or several? The answer is several (§1). The rest of the document plans them.

How to use this document:

- It is a `note`, not a build contract. Each item in §5 needs its own discussion and Feature Spec before any work starts. Do not create tables, routes or handlers from this file.
- Names in this file are working names. They become glossary terms only when a spec adopts them.
- The decisions in §7 were confirmed in discussion on 2026-11-25, every one as recommended. The first spec that needs one writes it as a rule.
- The order in §8 is a recommendation. Change it here when a decision changes it. When an item gets a spec, or lands, update its row in §8.

---

## 1. Assessment: one feature or several

**Several.** Step 3 is four items, of which three are needed before step 4 can open. Reasons:

1. **It is three kinds of data with three owners.** Prices and rates are platform data with no Tenant, written by `worker` jobs from outside sources. A position on a past day is a read over the ledger. A stored valuation is a Tenant's row written by a date-driven run. Each kind has its own test layer, and only the last two need cross-tenant isolation tests.
2. **The gap is uneven.** Part of the title already exists (§2): both ports select a row as of a date, and the interest run already works out a cash balance per day. What is missing is not one thing: the history of prices and rates, the quantity of a holding on a past day, the rules of a valuation, and a place to store one.
3. **An accepted spec is a build contract.** One spec would have to fix the shape of the stored rows before the valuation rules have been checked against a worked example. The working method of [future-plans.md](future-plans.md) §1 (work it out by hand first) fits one small spec at a time.
4. **The later steps need different parts.** The advisory fee needs a value per account per day that never changes afterwards (V3). The FIF report needs two days a year per holding. A chart on the Dashboard needs a series and an FX fold, and no later step waits for it (V4).
5. **Size.** One spec would add two back-fills, one read model, one run with its tables, job and manual trigger, and amendments to instrument-prices, fx-rates, net-worth, reports and two portals. The specs in this repository that carry one flow each (interest-run, corporate-actions) are already long.

---

## 2. Where we start (2026-11-25)

| Area | Today | Where |
| --- | --- | --- |
| Price as of a date | `IMarketData.TryGetPrice(instrumentId, asOf?)` returns the latest row with `AsOfDate` on or before the UTC date. It returns no date, and it has no age limit | instrument-prices R6, `InstrumentPriceStore` |
| Price history | Rows exist only from the day the job first ran for an instrument. The EODHD response holds the full history of a symbol; `EodhdClient` keeps only the latest bar and reads its `close` | instrument-prices R5 and §11, Out: "Historical back-fill" |
| Prices of simulated instruments | Stored under the platform's UTC date at each poll; the last poll of the day wins. History starts at the first poll | simulated-instruments, instrument-prices R1 and R5 |
| Prices of NZX and Tokyo instruments | None from EODHD | [known-issues/eodhd-nz-jp-coverage.md](known-issues/eodhd-nz-jp-coverage.md) |
| FX rate as of a date | `IFxRate` returns the latest row of the directed pair on or before the UTC date; `GetRateDetail` also returns the row's date. A missing pair throws | fx-rates R6, R7 |
| FX rate history | Rows exist only from the day the job first ran. `FrankfurterClient` asks for the latest rates only | fx-rates Out: "Historical back-fill" |
| Read routes | None for prices or rates. Both specs deferred them "until a consumer needs it" | instrument-prices R12, fx-rates R14 |
| Age limit | Only where a write needs it: an order's reference price and a rebalance plan refuse a price more than 4 days old | orders R10, rebalances R8 |
| Cash on a past day | The interest run works out the day balance of a Custodial account from its cash legs booked before the next UTC day. It stores a row only for a day that earned interest | interest-run R16–R18, `AccountInterest` |
| Quantity on a past day | Not available. `Holdings` holds the current quantity and cost only. Security legs carry signed quantities, so the quantity on a day is a sum | holdings, glossary "Ledger replay" |
| Cost on a past day | Not available, and not a sum: cost is path-dependent | glossary "Ledger replay" |
| `bookedAt` | Set by the server on a Custodial account, so its past never changes. Sent by the caller on an External account: not in the future, but any past date | custody-modes R10, R11 |
| Net worth | Now only. Per currency, no FX fold. No price → market value 0. Open accounts only | net-worth R5–R7, R10 |
| Adviser Balances Report | `asOf` is the time the execution was created. The generator reads the current holdings and the latest price, so it cannot be run for a past date | reports R14 |
| Seed | `TestSeed` writes prices and rates for one fixed date, 2026-01-01 | instrument-prices §11, fx-rates §11 |

---

## 3. Target

Step 3 is done when the later steps of the business roadmap can read these without a new source of numbers:

| Later step | What it reads | Grain | Must stay as first computed |
| --- | --- | --- | --- |
| 4 Advisory fee | The value of each Custodial account, in the account's currency | Every day | Yes. A fee accrued on a day's value must be explained by the same figures later |
| 5 Performance report | The value of an account at the start, at the end and on each day cash or securities came in or went out | Every day | Yes, for a period already reported |
| 6 FIF tax report | The market value of each holding at the start and the end of the tax year, in NZD | Two days a year, per holding | Yes, once reported |
| Read side (not a roadmap step) | Net worth on a past day and as a series, folded into one currency | Every day | No |

What each later step still has to add itself is in §6.

---

## 4. Principles

Working rules the plan assumes. Each is open to change in the spec that first needs it.

| # | Principle | Why |
| --- | --- | --- |
| H1 | **A valuation has three inputs, each with one owner:** positions from the ledger, prices from `InstrumentPrices`, rates from `FxRates`. | No second copy of a price or a quantity that can disagree with the first. |
| H2 | **A day is a UTC date**, and the position of a day is the sum of the legs booked before 00:00 UTC of the next day. | The interest run already uses this rule for cash (interest-run R16). One rule for cash and quantity. |
| H3 | **As booked.** A Reversal counts from its own `bookedAt`. A day between a Transaction and its Reversal shows the original. | The ledger is not rewritten; a valuation of a past day must not be either. |
| H4 | **The price is the unadjusted close, and the quantity is the quantity held that day.** An adjusted close is never stored or used. | An adjusted series restates past prices for later splits. Multiplied by the quantity actually held, it gives a wrong value. |
| H5 | **No invented number.** A holding with no price, or with a price that is too old, is unpriced, and the valuation says it is incomplete. It is not valued at 0. A missing or old rate does the same: the line is not valued and the valuation is incomplete; it does not fail the read ([account-valuation](features/account-valuation.md) R14). | Net worth may show 0 for a missing price (net-worth R7). A fee base or a tax figure may not. |
| H6 | **A stored valuation keeps what it used:** the price and its date, the rate and its date. It is never changed. | Fees and reports are computed from it. The platform already stores the rate used on every conversion. |
| H7 | **Stored valuations only where the past cannot change.** Custodial accounts first. | An External `bookedAt` can be any past date, so a stored day could stop matching the ledger. |
| H8 | **The run follows the interest run:** at most one result per account and day, a manual run for a date, and catch-up after a missed day (custodial-roadmap D18). | One pattern for every date-driven job. |
| H9 | **Worked example first.** Each item is worked by hand in a spreadsheet before its spec is written, and the numbers become the tests. | The working method of future-plans §1. |
| H10 | **The read path never calls a vendor.** | instrument-prices R9, fx-rates R10. |

---

## 5. Items

### V1. Price and rate history

- **What:** `InstrumentPrices` and `FxRates` hold a row for every source date from a floor date on, not only from the day the job first ran. Both get a read route.
- **Why first:** every later item reads a price as of a past day. Today that read returns nothing for a day before the first job run, and the price of the wrong day after a missed run.
- **Changes:** the EODHD source returns every bar from the floor date instead of the latest one (the response already holds them, so no extra call is spent); the price job stores them and so repairs a missed day; the Frankfurter source gets a call for a date range; a new instrument or currency is back-filled on its first run; a back-fill never replaces a row written by another source; a price read that also returns the row's date, as `GetRateDetail` does for a rate; `GET` routes for stored prices and rates, which the worked examples need.
- **Depends on:** nothing.
- **Also decide:** the value of the floor date (Q3); whether the unadjusted `close` is what EODHD sends (H4; check a real response across a known split); how far back the EODHD plan in use goes; whether a served (simulated) instrument gets any back-fill (recommended: no, its history is what the seed and the polls wrote, because the simulator's day is not the platform's day); who may call the read routes.
- **Amends:** instrument-prices (Out, R5, R12, §11), fx-rates (Out, R14), ADR 0019 (consequences).
- **Worked example:** one US instrument across a real split, one instrument over a weekend and a market holiday, one currency pair over the same days.
- **Spec:** [market-data-history.md](features/market-data-history.md) (accepted and landed 2026-11-25; the ten points of its §11 confirmed as recommended). It locks: the floor date as a `worker` setting, with no setting meaning no history; no marker of what is stored, so that every daily run asks for the whole window and inserts what is missing; a row that is final from the second day on; a limit on how far back EODHD is asked, because the free plan serves one year; a daily job of its own for the rate history; `GET /instruments/{id}/prices` and `GET /fx-rates`; no script. Its worked example confirms that EODHD's `close` is the unadjusted close.

### V2. Valuation as of a date

- **What:** a read that values one Account for one day: its cash balance and the quantity of each holding at the end of the day, the price and the rate as of that day, and each value in the instrument's quote currency and in the Account's currency. No table and no job.
- **Why its own item:** it holds every rule about what a valuation is. V3 only stores its result, and an External account can be valued through it without being stored.
- **Changes:** a read of the position on a day (cash by the day balance rule, quantity as the sum of security legs, a holding at 0 left out); a valuation function in Domain with no I/O, tested from the worksheet; the age limit and the incomplete mark (H5); an account that is Closed today but was Open on that day is valued; `GET /accounts/{id}/valuation?date=` (working shape), with the account read permission and the usual 404 for another Tenant's or an unassigned Customer's account.
- **Depends on:** V1.
- **Also decide:** the age limit (Q4); the rounding of a value; whether income that is earned but not posted counts (Q8); that reserved and unsettled amounts count in full, because the ledger books on the trade date; that cost and unrealised gain are not returned for a past day (§6).
- **Risk to test:** on the ex-date of a split, the price and the quantity must be on the same side of the split. The price changes with the source's date; the quantity changes when the platform applies the corporate action.
- **Worked example:** one account with cash and three instruments in two currencies over ten days, with a buy, a sell, a split, a reversal and a weekend.
- **Spec:** [account-valuation.md](features/account-valuation.md) (accepted 2026-11-26; the eight points of its §11 confirmed as recommended). It locks: a missing or old rate marks the line and does not fail the read, which follows Q4 (H5 reworded); an incomplete valuation still has a total, with its mark; rounding line by line to the currency's decimal places; the quantity restated on the ex-date of a hand-entered split, because the price job stores a US bar a day after its date and the entitlement is booked then; a price from before a corporate action is not used; any Account status and no earliest date; an External account valued from the ledger as it stands; `GET /accounts/{id}/valuation?date=` with `accounts.read`. No script.

### V3. Daily valuation run and stored valuations

- **What:** a `worker` run that stores the V2 result of every Custodial account for every day it was Open, and reads for one account's series. The stored rows are the "holding snapshots" of the roadmap row and close the open "daily snapshots" of [function-plan.md](function-plan.md) §5.4.
- **Changes:** tables for a valuation per account and day and its lines per holding (H6); a marker per account for the last day valued, as `InterestAccruedThrough`; a daily job after the price job; a runs resource and a manual run for a date or a range, as `/interest/runs` (interest-run R30–R32); isolation tests; `TestSeed` history (past Transactions, prices and rates) so that a run in Development has more than one day to value.
- **Depends on:** V2.
- **Also decide:** when a day is final (Q5); whether an incomplete day is stored with its mark or waits (recommended: stored with its mark, so that the fee step can see it and decide); External accounts (Q6); what is stored for a day with no cash and no holding; funds under management per day as a sum of the rows, not a second table.
- **After it:** step 4 of the business roadmap can open.

### V4. Net worth history and reports as of a date

- **What:** the read side for people: net worth on a past day and as a series, folded into the Tenant's reporting currency; a chart on the Adviser Portal Dashboard and in the Customer Portal; the Adviser Balances Report for a past date.
- **Why last:** no later roadmap step waits for it. It can open any time after V3, or stay closed.
- **Changes:** amendments to net-worth (R6 the fold, R10 the date) and reports (R14); page notes under `docs/adviser-portal/` and `docs/customer-portal/`.
- **Depends on:** V3. The fold also needs V1.
- **Also decide:** the FX fold (Q7), which net-worth has left open since it was accepted; whether the series covers External accounts, valued on read through V2, or funds under management only (recommended: funds under management first).

---

## 6. What this plan leaves out

| Item | Where it goes |
| --- | --- |
| Cost and unrealised gain on a past day, tax lots, cost in NZD | Ledger replay ([custodial-roadmap.md](custodial-roadmap.md) X2). Opens when a step needs cost on a past day, at the latest step 6 |
| Period close | Custodial-roadmap X3, after replay. It is what would let External accounts get stored valuations |
| Changing a stored valuation after a late or corrected price | Not planned. Decide with step 4, which has to say what happens to a fee when its base changes |
| Which movements are external flows, and gross or net of fees | Step 5 |
| Which exchange rates Inland Revenue accepts for the FIF conversions, and the boundary of the tax year in New Zealand time | Step 6, checked against the primary source. If the answer is a published rate table, it is a second rate set and not `FxRates` |
| Income earned but not posted, as part of a value | Left out of the first version (Q8); revisit in step 5 |
| Real-time or intraday prices, a second price source, NZX and Tokyo prices | Unchanged. The known issue stays open; a holding in those markets is unpriced (H5) |
| A valuation-only record for property and loans | future-plans §9, still open |
| Multi-currency cash in one Account | future-plans §5 |

---

## 7. Decisions

Every question is **decided** (2026-11-25), as recommended. The answer is what the items in §5 assume; the first spec that needs one writes it as a rule and may refine it.

| # | Question | Answer | First needed by |
| --- | --- | --- | --- |
| Q1 | Is a valuation day a UTC date or the Tenant's local date? | UTC date (H2). A price row is already dated by its market, so only the cut of the position depends on this | V2 |
| Q2 | Which price values a holding? | The unadjusted close of the latest source date on or before the day (H4) | V1 |
| Q3 | How far back does the history go? | One configured floor date for both sources, early enough for the first tax year to be reported. No row before it | V1 |
| Q4 | How old may a price or a rate be? | 4 calendar days, the limit orders R10 already uses. Older, or none: unpriced and incomplete (H5) | V2 |
| Q5 | When is a day final? | The run values yesterday after the price job of today has run, as the interest run posts for yesterday. What it stores is final. A price that arrives later changes nothing (§6) | V3 |
| Q6 | Are External accounts stored? | No. Custodial accounts only (H7); an External account is valued on read through V2. Revisit with period close | V3 |
| Q7 | Into which currency is a value folded, and where? | A stored line keeps the quote currency value and the Account currency value with the rate used. A fold into the reporting currency is done on read, from the stored Account currency value and the rate of the same day | V3, V4 |
| Q8 | Does income earned but not posted count (daily interest accruals not yet posted, a dividend between its ex-date and its payment)? | Not in the first version: a valuation is the ledger's cash and holdings. Record it as a known understatement | V2 |
| Q9 | One spec for prices and rates, or two? | One. The decisions are the same (floor date, back-fill rule, read route), and each half is small | V1 |
| Q10 | Is V2 a spec of its own or part of V3? | Its own. It adds no script, and its rules should be accepted before the stored shape is fixed | V2 |

---

## 8. Recommended order and tracking

Each step depends only on earlier steps.

| Step | Item | Closes | Spec | Status |
| --- | --- | --- | --- | --- |
| 1 | V1 price and rate history | "Historical back-fill" and the deferred read routes of instrument-prices and fx-rates | [market-data-history](features/market-data-history.md) | landed 2026-11-25 |
| 2 | V2 valuation as of a date | — | [account-valuation](features/account-valuation.md) | accepted 2026-11-26 |
| 3 | V3 daily valuation run and stored valuations | "Still open: daily snapshots" (function-plan §5.4) | — | not specified |
| 4 | V4 net worth history and reports as of a date | "Daily snapshots, historical series" and "FX fold" (net-worth Out) | — | not specified; optional for the business roadmap |

Steps 1 to 3 are step 3 of the business roadmap. When step 3 here has landed, step 4 of [future-plans.md](future-plans.md) §1 (the advisory fee) can open. Step 4 here can run beside it.

What is left of step 2 of the business roadmap (dividends and withholding on External accounts) does not depend on this plan, and this plan does not depend on it.

---

## 9. Existing documents each item touches

| Document | Change | Item |
| --- | --- | --- |
| [features/instrument-prices.md](features/instrument-prices.md), [features/fx-rates.md](features/fx-rates.md), [adr/0019](adr/0019-fx-rate-source-and-refresh.md) | Back-fill, the price read with its date, the read routes | V1 |
| [features/simulated-instruments.md](features/simulated-instruments.md) | A served instrument gets no back-fill | V1 |
| [features/holdings.md](features/holdings.md), [features/posting.md](features/posting.md) | The position on a past day | V2 |
| [features/interest-run.md](features/interest-run.md) | The day balance rule becomes the shared rule for a day's position | V2 |
| [features/net-worth.md](features/net-worth.md), [features/custody-mode-grouping.md](features/custody-mode-grouping.md) | A past date, a series, the FX fold; funds under management per day | V4 |
| [features/reports.md](features/reports.md) | Adviser Balances Report for a past date | V4 |
| Adviser Portal and Customer Portal page contracts | Charts | V4 |
| [function-plan.md](function-plan.md) §5.4 | "Daily snapshots" is no longer open | V3 |
| [known-issues/eodhd-nz-jp-coverage.md](known-issues/eodhd-nz-jp-coverage.md) | Its effect on a valuation (unpriced, incomplete) | V2 |
| [domain-model.md](domain-model.md), [database-design.md](database-design.md), [api-design.md](api-design.md), [architecture.md](architecture.md), [glossary.md](glossary.md) | With each spec | all |
| [future-plans.md](future-plans.md) §1 | Points here for step 3 | now |
