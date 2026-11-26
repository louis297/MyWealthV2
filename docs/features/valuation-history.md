---
title: Valuation history
status: accepted
phase: 3
language: en
owner: ""
created: 2026-11-26
last_updated: 2026-11-26
related:
  - ../valuation-roadmap.md
  - ../future-plans.md
  - valuation-run.md
  - account-valuation.md
  - market-data-history.md
  - fx-rates.md
  - net-worth.md
  - custody-mode-grouping.md
  - reports.md
  - account-closure.md
  - bff-adviser-portal.md
  - bff-customer-portal.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../adviser-portal/adviser-portal-reports.md
  - ../customer-portal/customer-portal.md
  - ../domain-model.md
  - ../api-design.md
  - ../architecture.md
  - ../glossary.md
---

# Valuation history

Step 4 of [valuation-roadmap.md](../valuation-roadmap.md) §8. It covers roadmap item **V4** and writes the read half of decision **Q7** (a fold into the reporting currency is done on read, from the stored Account currency value and the rate of the same day) as rules. It is the read side for people of what [valuation-run.md](valuation-run.md) stores. No later roadmap step waits for it.

Three reads are added or changed:

- **Funds under management series.** The funds under management of a scope (the firm, an Adviser's Customers, one Customer) for every day of a range, from the stored valuations: per currency, and **folded** into the Tenant's reporting currency. `GET /net-worth/funds-under-management?from=&to=`.
- **Net worth now, folded.** The two net-worth reads gain `folded`: the per-currency net worth of now in the reporting currency, at today's rates.
- **Adviser Balances Report for a past date.** The report takes an optional `date` and then lists the cash and holdings of the end of that day.

And three page changes: a chart and the folded total on the Adviser Portal Dashboard and the Customer Portal Dashboard, a chart on the account page of a Custodial account in both portals, and a date on the Adviser Balances form.

New: route `GET /net-worth/funds-under-management`; field `folded` on `GET /net-worth` and `GET /users/customers/{id}/net-worth`; parameter `date` of the Adviser Balances Report; Domain function `FxFold.Compute` with `FoldLineStatus`; Application query `GetFundsUnderManagement`. No script, table, index, permission, problem code, domain event, `TransactionType`, port, job, setting or simulator change. The BFFs do not change: both already proxy `/net-worth` and `/accounts`.

**Status is `accepted` (2026-11-26), landed 2026-11-26.** The twelve points of §11 were confirmed as recommended. Point 5 refines valuation-run R26, which was reworded.

Notes from landing:

- R6: the expected days of an account are one helper, `ValuationDays` (`src/Application/Valuations`), which the valuation run (valuation-run R3) and the series both use. A Closed account with no completed closure has no last day, as in the run.
- R10: the rows of each pair are listed once over `from` minus 4 days to `to` (`IFxRate.ListRatesAsync`), and the latest row before that window is read once (`GetRateDetailAsync`), so a day before the first listed row is `StaleRate` with that row, not `NoRate`.
- R12, §8.1: `total` and `value` are rounded to the decimal places of their currency in the response (the stored totals have four), and `fx.rate` has no trailing zeros.
- R16, R20: the Adviser Balances binder binds in one of three modes: a request (checked against the last final day of its moment), a schedule (refuses `date`), and a stored snapshot (bound as stored, for the job, the download scope and a schedule's run). The snapshot writes `date` only when it was sent, so the snapshots of the report of now are unchanged. A `date` that is not a date answers `date must be a date as yyyy-MM-dd.`
- R18: a price is shown with at least the decimal places of its quote currency and every place it was stored with (`95.20 (2026-03-11)`). A restated quantity is shown as `200 (ledger 100)`. The PDF of a past date has the columns Customer, Account, Type, Custody, Instrument, Quantity, Price, Balance, Market value and Status, and no "Include closed accounts" line.
- §9: each portal has its own plain SVG `ValueChart` (`src/*/src/shared/components`). The Customer Portal has no currency catalog (bff-customer-portal R20), so its figures are shown as sent, grouped (`8,500`); the Adviser Portal pads them to the currency's places from `/currencies`.
- §10: the worked example of §1.1 is built on the account-valuation world (A1) and a USD account B1 made Custodial in the database, as in valuation-run §10; the 10:00 read runs the job through Tuesday 10 and a manual run through Wednesday 11 that fails on B1.

---

## 1. Summary

| | After step 3 | After this slice |
| --- | --- | --- |
| Funds under management of a past day | A sum of the stored valuations (valuation-run R26), read by no route | `GET /net-worth/funds-under-management?from=&to=`: one item per day, per currency and folded |
| A total in one currency | None. Every total is per currency (net-worth R6) | The series and net worth now are also folded into the Tenant's reporting currency, with the rate each currency used |
| A currency with no fresh rate | — | Not folded; the folded total says it is incomplete. No rate of 1 and no value of 0 |
| The Adviser Balances Report | Now only (reports R14) | Now, or the end of a past day up to the last final day |
| Charts | None in either portal | Funds under management over time on both Dashboards; the value of a Custodial account over time on its page |
| External accounts on a past day | Valued on read, one account and day at a time | Unchanged. No series; the Adviser Balances Report of a past day values them on read |

The rule that distinguishes this slice: a figure of a past day shown to a person is a sum of stored figures, converted with stored rates of that same day, and it says when something is left out. Nothing on these pages is valued again or counted as 0.

### 1.1 Worked example

The Tenant of [valuation-run.md](valuation-run.md) §1.1, with reporting currency NZD and two Custodial accounts:

- **A1**, Customer A, assigned to Adviser Ana: the NZD account of [account-valuation.md](account-valuation.md) §1.1. Its stored totals are those of valuation-run §1.1; Wednesday 11 March is incomplete (`CCC` `StalePrice`).
- **B1**, Customer B, assigned to Adviser Ben: a USD account opened on Wednesday 4 March 2026 at 10:00 UTC with a deposit of 7,500.25 USD, and no other activity. Its first day is Wednesday 4. Each of its stored valuations is cash 7,500.25 USD, no line, complete.

The rates USD → NZD are those of account-valuation §1.1. On Friday 13 March the manual run of 09:05 stores A1's Wednesday 11 and fails on B1's Wednesday 11 (a database timeout, valuation-run R12): B1 is valued through Tuesday 10 until the daily run of 13:00 stores its Wednesday 11.

**The TenantAdmin reads at 10:00 on Friday 13 March:** `GET /net-worth/funds-under-management?from=2026-03-02&to=2026-03-11`. The last final day is Wednesday 11.

| Day | NZD (stored) | USD (stored) | USD → NZD | USD in NZD | Total (NZD) | Accounts | Complete |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Mon 2 | 41,550.00 | — | — | — | 41,550.00 | 1 | yes |
| Tue 3 | 41,983.00 | — | — | — | 41,983.00 | 1 | yes |
| Wed 4 | 44,133.32 | 7,500.25 | 1.7180 | 12,885.43 | 57,018.75 | 2 | yes |
| Thu 5 | 44,434.00 | 7,500.25 | 1.7200 | 12,900.43 | 57,334.43 | 2 | yes |
| Fri 6 | 42,790.45 | 7,500.25 | 1.7250 | 12,937.93 | 55,728.38 | 2 | yes |
| Sat 7 | 42,790.45 | 7,500.25 | 1.7250 | 12,937.93 | 55,728.38 | 2 | yes |
| Sun 8 | 42,804.72 | 7,500.25 | 1.7260 | 12,945.43 | 55,750.15 | 2 | yes |
| Mon 9 | 43,092.48 | 7,500.25 | 1.7300 | 12,975.43 | 56,067.91 | 2 | yes |
| Tue 10 | 42,930.09 | 7,500.25 | 1.7280 | 12,960.43 | 55,890.52 | 2 | yes |
| Wed 11 | 34,750.72 | — | — | — | 34,750.72 | 2 | **no**: 1 incomplete (A1), 1 missing (B1) |

**The same read at 14:00**, after the daily run stored B1's Wednesday 11: that day is NZD 34,750.72 and USD 7,500.25 × 1.7310 = 12,982.93, total 47,733.65, still incomplete (A1). Every other day is the same as at 10:00.

**Adviser Ana** reads the same range: A1 only. Every day has the NZD column alone, `accounts` 1, and Wednesday 11 is incomplete with no missing account. **Customer B** reads it: B1 only, folded into NZD, so Wednesday 4 is 12,885.43 NZD; Monday 2 and Tuesday 3 have no account and a total of 0.

What the example shows:

- **The fold** (R9, R10). Each currency's stored totals are added up, then converted once with the rate of that day: 7,500.25 × 1.7180 = 12,885.4295, shown as 12,885.43. The NZD column needs no rate.
- **A weekend.** Saturday and Sunday have rate rows of their own; Sunday's total differs from Saturday's by A1's own rate change and by the rate of the fold.
- **A day before an account existed** (R6). Monday 2 and Tuesday 3 count one account: B1's first day is Wednesday 4, so it is not missing on those days.
- **An incomplete account and a missing account** (R7). At 10:00 Wednesday 11 is incomplete twice over. At 14:00 the missing account has been stored and the day is still incomplete, because A1's stored row is incomplete and stays so (valuation-run R10).
- **A stored figure, not a new valuation.** Wednesday 11 of A1 is 34,750.72 at both times, although the valuation read of that day gives 43,371.10 once the late `CCC` bar arrives on Monday 16 (valuation-run §1.1).

**Net worth now, folded.** The Customer of [custody-mode-grouping.md](custody-mode-grouping.md) §4.2 (NZD 8,500.00 and USD 800.00 of net worth), read on Friday 13 March when the USD → NZD row of that day is 1.7320: `folded` is NZD 8,500.00 + 800.00 × 1.7320 = 1,385.60, total 9,885.60, complete.

**The Adviser Balances Report for Adviser Ana, `date` 2026-03-11**, requested at 10:00 on Friday 13 March:

| Customer | Account | Custody | Line | Quantity | Price (date) | Market value | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A | A1 | Custodial | Cash NZD | | | 14,356.60 | |
| A | A1 | Custodial | AAA | 1,500 | 2.61 (2026-03-11) | 3,915.00 NZD | |
| A | A1 | Custodial | BBB | 100 | 95.20 (2026-03-11) | 9,520.00 USD | |
| A | A1 | Custodial | CCC | 120 | 41.30 (2026-03-06) | — | Price too old |

Subtotals, Custodial and All accounts: NZD cash 14,356.60, market value 3,915.00; USD cash 0.00, market value 9,520.00. The header says the report is incomplete: one holding not valued.

---

## 2. Scope

**In**

- Domain: `FxFold.Compute` (a function with no I/O, `src/Domain/Valuations`), `FoldLine`, `FoldedTotal`, `FoldLineStatus`
- Application: query `GetFundsUnderManagement`; the net-worth reader adds `folded`; the Adviser Balances binder and generator take `date`
- HTTP: `GET /net-worth/funds-under-management`; `folded` on the two net-worth reads; the `date` parameter of `adviser-balances`
- Adviser Portal: the Dashboard chart and folded total, the account page chart, the date on the Adviser Balances form
- Customer Portal: the Dashboard chart and folded total, the account page chart
- Tests listed in §10

**Out**

- A series of net worth that counts External accounts, valued on read through account-valuation (§11 point 1). External accounts appear in no series
- `GET /net-worth?date=`: net worth of a past day keeps no route of its own; the funds under management series is the past-day read (§11 point 1)
- A series narrowed to one Customer on a staff route (`customerId`), and a chart on the Adviser Portal customer page (D5 shows no breakdowns there)
- A fold of the breakdowns of net worth now (`byCustomer`, `byAccountType`, `byCustodyMode`, `topHoldings`) and of the Dashboard figures "Funds under management" and "External assets"
- A fold in either report. The Adviser Balances Report stays per currency
- A relative date in a report schedule ("the last day of the previous month"). A schedule keeps reporting now (R20)
- A past date for the Customer Transactions Report: it already has a date range
- Cost, average cost and unrealised gain on a past day (ledger replay)
- A second rate set, a rate entered by a caller, an inverse pair, a cross rate through a third currency
- A reporting currency per Customer, or chosen by the reader
- Changing a stored valuation (valuation-run R10)
- A chart library (§11 point 9)
- Seed changes (§9.1)

---

## 3. Stories

1. As a TenantAdmin I see on the Dashboard how the firm's funds under management moved over the last 90 days, in the firm's reporting currency, and which days are incomplete.
2. As an Adviser I see the same for my own Customers.
3. As a Customer I see how what the platform holds for me moved over time.
4. As a TenantAdmin, an Adviser or a Customer I see net worth now as one total in the reporting currency next to the per-currency totals, and which currency could not be converted.
5. As an Adviser or a Customer I see the value of one Custodial account over time on its page.
6. As an Adviser I run the Adviser Balances Report for the end of a past day and get the cash and holdings of that day, with the price and its date on each holding and the holdings that could not be valued marked.
7. As a reader of any of these figures I can work each one out by hand from the stored valuations, `GET /fx-rates` and `GET /instruments/{id}/prices`.
8. As an Adviser I cannot see another Adviser's Customers in a series, and a firm cannot see another firm's (404 for a named Customer, as today).

---

## 4. Rules

### 4.1 Access

| ID | Rule |
| --- | --- |
| R1 | **Access.** `GET /net-worth/funds-under-management` takes `accounts.read` and the scope of `GET /net-worth` (net-worth R1, R4, R9): a TenantAdmin the firm, an Adviser the Customers assigned now, a Customer their own accounts, a SystemAdmin the Tenant named by `tenantId` (required; missing → 400 `errors.TenantId`, unknown → 404). A TenantAdmin, Adviser or Customer who sends `tenantId` → 400. Missing / dead Bearer → 401, never 302. Policy fail → 403. A disabled current tenant → 403. |
| R2 | **Unchanged access elsewhere.** `folded` follows the access of the net-worth reads; the `date` parameter follows reports R5–R9; the account page charts read `GET /accounts/{id}/valuations` with valuation-run R1. No permission is added. |

### 4.2 The funds under management series

| ID | Rule |
| --- | --- |
| R3 | **Route.** `GET /net-worth/funds-under-management?from=&to=` → 200 with the item of R12. `from` and `to` are days (`yyyy-MM-dd`), inclusive. `to` defaults to the last final day (valuation-run R4); `from` defaults to `to` minus 89 days. Not a date → 400 `errors.From` / `errors.To`. `to` after the last final day → 400 `errors.To` (`Funds under management can be read only up to the day before yesterday.`). `from` after `to` → 400 `errors.From`. More than 366 days → 400 `errors.From` (`A series covers at most 366 days.`). There is no earliest date. |
| R4 | **The accounts of the scope.** The Custodial accounts of the Customers in the scope now (R1), whatever the status of the Account or the Customer. An Adviser's series is the history of the Customers assigned to them now, not of the book as it was on each day: the platform keeps no history of assignments. External accounts are never counted (roadmap Q6). |
| R5 | **What a day counts.** For day `d`, the stored valuations of `d` of the accounts of R4 (valuation-run R8). Per currency, `total` is the sum of their `total`, not rounded again (it is already rounded line by line, account-valuation R15). Nothing is valued again: a stored valuation is read as it was stored, and a price or rate row inserted after it was stored changes nothing (valuation-run R10). |
| R6 | **Expected accounts.** An account of R4 is **expected** on day `d` when `d` is on or after its first day (the UTC date of `Accounts.Created`) and, for a Closed account, on or before the UTC date of `CompletedAt` of its latest completed account closure: the days valuation-run R3 stores for it. A reopened account is expected again from its first day on, and its Closed days are stored as empty days. `accounts` on a day is the number of accounts expected on it. |
| R7 | **Complete.** A day is **complete** when every account expected on it has a stored valuation of `d` and each of them is complete, and every currency of the day is `Folded` (R10). `incompleteAccounts` is the number of stored valuations of the day with `complete` `false`; `missingAccounts` the number of expected accounts with no stored valuation of `d` (a run failed, or has not reached the day). An incomplete day still has its totals; they leave the missing parts out. This refines the completeness of valuation-run R26, which did not exclude a Closed account after its closure (§11 point 5); R26 was reworded to match. |
| R8 | **Every day of the range.** The series has one item per calendar day from `from` to `to`, oldest first, also for a day with no expected account (`accounts` 0, no currency, `total` 0, `complete` `true`). There is no paging. |

### 4.3 The fold

| ID | Rule |
| --- | --- |
| R9 | **Reporting currency.** A fold converts into the Tenant's `ReportingCurrency` as it is when the read is made, for every day the read covers. A change of the reporting currency changes every folded figure of the past on the next read; the stored valuations do not change. The response names the currency (`reportingCurrency`, `folded.currency`), so a Customer, who has no `tenants.read`, can show it. |
| R10 | **Fold of one day.** Input: the per-currency totals of the day, and the day `d`. For each currency `C`: when `C` is the reporting currency `R`, the line is `Folded` with no `fx` and `value` = the total. Otherwise the rate is `IFxRate.GetRateDetailAsync(C, R, d)`: the stored row of that directed pair with the latest `AsOfDate` on or before `d`, with its date and source (account-valuation R10; the inverse pair is not used). No row → `NoRate`, no `fx`, no `value`. A row more than 4 days older than `d` → `StaleRate`, with the row in `fx` and no `value` (the age limit of account-valuation R11). Else `Folded`, `value = Round(total × rate, dp of R, AwayFromZero)` (account-valuation R15). `FxRateNotFoundException` is caught and becomes `NoRate`; it does not fail the read (fx-rates R7). |
| R11 | **Folded total.** `total` (of a series day) or `folded.total` (of net worth now) is the sum of the `value` of the `Folded` lines. When a line is not `Folded`, the total leaves that currency out and the figure is incomplete; it is never counted at a rate of 1 or a value of 0. Lines are sorted by currency code. |

### 4.4 The series item

| ID | Rule |
| --- | --- |
| R12 | **Item.** `{ reportingCurrency, from, to, days }`. A day is `{ date, currencies, total, complete, accounts, incompleteAccounts, missingAccounts }`. A currency line is `{ currency, total, status, fx, value }`: `total` in that currency (R5), `status` one of `Folded`, `NoRate`, `StaleRate`, `fx` `{ rate, date, source }` or `null`, `value` in the reporting currency or `null`. Every caller who may read the series sees every field. |
| R13 | **A read only.** It writes nothing, takes no lock and takes no `Idempotency-Key`. It reads `StoredValuations`, `Accounts`, `AccountClosures` and `FxRates`, and calls no outside party. |

### 4.5 Net worth now, folded

| ID | Rule |
| --- | --- |
| R14 | **`folded`.** `GET /net-worth` and `GET /users/customers/{id}/net-worth` gain a top-level `folded`: `{ currency, total, complete, currencies }`. Its input is the `net` of each entry of the top-level `currencies` (net-worth §8.1), and the day is the platform's UTC date. Lines and total follow R10 and R11; `complete` is `true` when every line is `Folded`. With no Open account, `currencies` is empty, `total` 0 and `complete` `true`. |
| R15 | **Only the fold is new.** The values that are folded are those of net worth now, unchanged: Open accounts only (net-worth R5), a holding with no price counted as 0 and a price of any age (net-worth R7). `folded.complete` speaks of the rates only. The breakdowns are not folded. `folded` is additive: a client that ignores it keeps working (net-worth R6 is narrowed, not dropped: `currencies` and every breakdown stay per currency). |

### 4.6 The Adviser Balances Report for a past date

| ID | Rule |
| --- | --- |
| R16 | **Parameter.** `adviser-balances` takes an optional `date` (`yyyy-MM-dd`) beside `adviserId` and `includeClosedAccounts`. Without it the report is the report of now, unchanged (reports R14, §9.2). Not a date → 400 with `errors` key `date`. A date after the last final day of the moment of the request → 400 `date` (`A report for a past date can be made up to the day before yesterday (UTC).`). `date` with `includeClosedAccounts` `true` → 400 `includeClosedAccounts` (`A report for a past date includes every account that held cash or holdings on that day.`). There is no earliest date. |
| R17 | **Accounts.** With a `date`, every account of the Adviser's Customers (assigned now), of either custody mode and whatever its status. A Custodial account is read from its stored valuation of `date`. An External account is read from the valuation read of `date` (account-valuation §7, `ValueAccount.ForDayAsync`), worked out when the report is generated: the ledger as it stands then (account-valuation R8). A Custodial account expected on `date` (R6) with no stored valuation is listed as one line "Not valued yet" with no figures. An account with cash 0 and no holding on that day is left out, as reports R14 leaves out a zero-balance account. |
| R18 | **Lines.** The cash line shows the cash of the day in the Account's currency (left out when 0). A holding line is one valuation line: the quantity of the day (with the ledger quantity in brackets when R18 of account-valuation restated it), the price with its date, the market value in the quote currency, and a status for a line that is not `Valued` (labels: NoPrice "No price", StalePrice "Price too old", PriceBeforeCorporateAction "Price from before a corporate action", NegativeQuantity "Quantity below 0", NoRate and StaleRate none). There is no cost column: cost on a past day is not available (account-valuation R17). The order is that of reports §9.2. |
| R19 | **Subtotals and header.** Subtotals per custody mode, then "All accounts", per currency, as custody-mode-grouping R12: the cash total, and the market value total of the lines that have a market value. A line with no market value (NoPrice, StalePrice, PriceBeforeCorporateAction, NegativeQuantity, "Not valued yet") is listed and left out. A line with `NoRate` or `StaleRate` has its market value in the quote currency and counts: the report converts nothing. The header shows "Balances at the end of <date> (UTC)", the generation time (`asOf`, unchanged, set by the server) and, when any line has no market value, "Incomplete: <n> holdings not valued, <m> accounts not valued yet. The subtotals leave them out." Under the subtotals: "Custodial accounts: the daily valuation of that day. External accounts: the ledger as it stood when this report was made." |
| R20 | **Manual only.** `date` is refused on `POST /reports/schedules` and `PUT /reports/schedules/{id}` (400 `date`: `A schedule reports the balances at the time it runs.`). The parameter snapshot of an execution keeps `date`; the execution list shows it with the other parameters. The generator reads the stored valuation of `date` when the job runs; a stored valuation never changes (valuation-run R10), so a repeat of the same request gives the same Custodial figures. |

Policies: none added.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `FxFold` | Static (new, `src/Domain/Valuations`) | `Compute(totals, reportingCurrency, decimalPlaces, rates, day)` → `FoldedTotal`. `totals` is one amount per currency; `rates` the `StoredRate` (account-valuation §5) found for each other currency, or none. Holds R10 and R11. Reads no clock and no store |
| `FoldLine` | Value (new) | Currency, total, `FoldLineStatus`, the rate row or none, value or none |
| `FoldedTotal` | Value (new) | Reporting currency, lines, `Total`, `Complete` |
| `FoldLineStatus` | Enum (new) | `Folded = 0`, `NoRate = 1`, `StaleRate = 2` |
| `ValuationRules` | Static (existing) | No change. `IsFresh` and `LastFinalDay` are used as they are |
| `StoredValuation`, `AccountValuation`, `Account` | Existing | No change |

```text
one day d, scope S, reporting currency R
  expected(d)  = Custodial accounts of S with firstDay <= d (<= closure day if Closed)   (R6)
  stored(d)    = StoredValuations of d of expected(d)                                    (R5)
  per currency C: total_C = sum(stored.total where currency = C)
  fold: C = R           -> value = total_C
        rate(C->R, d)   none -> NoRate; older than 4 days -> StaleRate
        else            value = Round(total_C x rate, dp(R))                              (R10)
  total    = sum(value of Folded lines)                                                    (R11)
  complete = no missing, no incomplete stored, every line Folded                           (R7)
```

Invariants:

- A fold line has a `value` if and only if its status is `Folded`.
- A folded total equals the sum of the values of its `Folded` lines, to the last decimal.
- A rate more than 4 days older than the day never produces a value.
- A series day is complete only when no expected account is missing, none of its stored valuations is incomplete and every currency is folded.

Domain events: none.

[domain-model.md](../domain-model.md) §8.22 lists `FxFold` beside the valuation of a day.

---

## 6. Database

None. No script.

The series reads `StoredValuations` by `(TenantId, ValuationDate)`, which the index of valuation-run §6.1 (INCLUDE `AccountId, Currency, Total, Complete`) was made for (R26), limited to the account ids of the scope; the expected accounts from `Accounts` (`Created`, `CustodyMode`, `Status`) and `AccountClosures` (`CompletedAt`); the rates with `IFxRate.ListRatesAsync` once per currency pair over `from` minus 4 days to `to`. A year of a firm is one grouped query of at most 366 × the number of currencies rows. No new index.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Query | `GetFundsUnderManagement` (new, `src/Application/NetWorth`) | R1, R3–R13: the scope as `NetWorthReader` finds it (Custodial accounts, any status), the expected days, the grouped sums, the rates, then `FxFold.Compute` per day |
| Query | `GetNetWorth`, `GetCustomerNetWorth` (existing) | The reader adds `folded` (R14, R15) with `IFxRate.GetRateDetailAsync` per currency and `ICurrencyCatalog` for the decimal places |
| Internal | Adviser Balances binder (existing) | Binds `date` (R16, R20), with the platform's clock for the last final day; knows whether it binds a request or a schedule |
| Internal | Adviser Balances generator (existing) | With `date`: R17–R19. Reads `StoredValuations` and `StoredValuationLines` for Custodial accounts, calls `IValueAccount.ForDayAsync` for External accounts. The row builder takes the valuation lines; the PDF model gains the past-date header, the price and status columns, and drops the cost column for such a report |
| Service | `ValueAccount`, `StoreAccountValuations` (existing) | No change |

Where each rule lives:

| Rule | Where |
| --- | --- |
| The fold (R10, R11) | `FxFold.Compute` |
| Expected accounts (R6) | `GetFundsUnderManagement`, from the same inputs as `StoreAccountValuations` uses for valuation-run R3; one helper serves both |
| Scope (R1, R4) | The scope of `NetWorthReader` |
| The last final day (R3, R16) | `ValuationRules.LastFinalDay`, with the platform's clock |
| The past-date lines and subtotals (R17–R19) | The Adviser Balances row builder, which has unit tests today |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/net-worth/funds-under-management` | `accounts.read` | 200 item | 400 / 401 / 403 / 404 |
| GET | `/net-worth`, `/users/customers/{id}/net-worth` | unchanged | item has `folded` | unchanged |
| POST | `/reports/request` (`adviser-balances`) | unchanged | 202 `{ "id" }` | 400 `date`, `includeClosedAccounts` added |
| POST / PUT | `/reports/schedules[/{id}]` (`adviser-balances`) | unchanged | unchanged | 400 `date` added |

### 8.1 Bodies

`GET /net-worth/funds-under-management?from=2026-03-10&to=2026-03-11`, the TenantAdmin of §1.1 at 10:00 on Friday 13 March:

```json
{
  "reportingCurrency": "NZD",
  "from": "2026-03-10",
  "to": "2026-03-11",
  "days": [
    {
      "date": "2026-03-10",
      "currencies": [
        { "currency": "NZD", "total": 42930.09, "status": "Folded", "fx": null, "value": 42930.09 },
        { "currency": "USD", "total": 7500.25, "status": "Folded", "fx": { "rate": 1.728, "date": "2026-03-10", "source": "Frankfurter" }, "value": 12960.43 }
      ],
      "total": 55890.52,
      "complete": true,
      "accounts": 2,
      "incompleteAccounts": 0,
      "missingAccounts": 0
    },
    {
      "date": "2026-03-11",
      "currencies": [
        { "currency": "NZD", "total": 34750.72, "status": "Folded", "fx": null, "value": 34750.72 }
      ],
      "total": 34750.72,
      "complete": false,
      "accounts": 2,
      "incompleteAccounts": 1,
      "missingAccounts": 1
    }
  ]
}
```

`folded` on net worth now (§1.1; other fields as net-worth §8.1 and custody-mode-grouping §8.1):

```json
{
  "asOf": "2026-03-13T01:00:00Z",
  "currencies": [ /* unchanged */ ],
  "folded": {
    "currency": "NZD",
    "total": 9885.60,
    "complete": true,
    "currencies": [
      { "currency": "NZD", "total": 8500.00, "status": "Folded", "fx": null, "value": 8500.00 },
      { "currency": "USD", "total": 800.00, "status": "Folded", "fx": { "rate": 1.732, "date": "2026-03-13", "source": "Frankfurter" }, "value": 1385.60 }
    ]
  },
  "breakdowns": { /* unchanged */ }
}
```

Adviser Balances for a past date:

```json
{
  "reportId": "5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22",
  "parameters": { "date": "2026-03-11" }
}
```

`date`, `from` and `to` are dates with no zone ([api-design.md](../api-design.md) §2). `total` and `value` have the decimal places of their currency; `fx.rate` up to 8.

### 8.2 Errors

All refusals are ordinary validation (400 with `errors`, no problem `code`).

| Case | Response |
| --- | --- |
| No Bearer, or a dead one | 401, never 302 |
| `tenantId` from a TenantAdmin, Adviser or Customer; missing from a SystemAdmin | 400 `errors.TenantId` |
| An unknown `tenantId` from a SystemAdmin | 404 |
| `from` or `to` not a date; `to` after the last final day; `from` after `to`; more than 366 days | 400 `errors.From` / `errors.To` |
| `adviser-balances`: `date` not a date, after the last final day, or on a schedule; `date` with `includeClosedAccounts` `true` | 400 `date` / `includeClosedAccounts` |

A currency that cannot be folded, an incomplete stored valuation and a missing account are not errors: the answer is 200 with `complete` `false`.

Keep the resource catalog in [api-design.md](../api-design.md) pointing here for these rules.

---

## 9. UI

Page contracts amended when this spec was accepted: [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (D107–D109), [adviser-portal-reports.md](../adviser-portal/adviser-portal-reports.md) (D110) and [customer-portal.md](../customer-portal/customer-portal.md) (C45–C47). Neither BFF changes: `/net-worth` and `/accounts` are on both allow-lists.

Both portals draw the charts as plain SVG with no chart library (§11 point 9): a line of the daily total, a vertical axis in the currency's major units, the dates on the horizontal axis. An incomplete day is drawn with a hollow marker. Hovering or focusing a point shows the date, the total and, for an incomplete day, the reason. The figures of the chart are also in a table under it ("Show figures"), so that every figure can be read without the chart.

**Adviser Portal**

| ID | Behaviour |
| --- | --- |
| D107 | `/dashboard`: under the "Funds under management" and "External assets" figures (D24), a chart "Funds under management over time" from `GET /net-worth/funds-under-management` with a range choice of 30 days, 90 days (the default) and 1 year, sent as `from` with no `to`. Above it: "In <reportingCurrency>, at each day's exchange rate. Up to <to>; a day is added once its prices are final." The line starts at the first day with `accounts` above 0; with none in the range the chart shows "No funds under management in this period." The point of an incomplete day says "Incomplete:" with the parts that apply: "<n> accounts not fully valued", "<n> accounts not valued yet", "<currency> not converted (no exchange rate)" or "(exchange rate too old)". |
| D108 | `/dashboard`: under the per-currency totals (D2), one line "Total in <folded.currency>: <folded.total> at today's exchange rates". When `folded.complete` is `false` it adds "Not counted: <currency> (no exchange rate)" or "(exchange rate too old)" for each such line. With one currency only, which is the reporting currency, the line is not shown. |
| D109 | `/accounts/:id` of a Custodial account: a chart "Value over the last 90 days" from `GET /accounts/{id}/valuations?from=&to=&pageSize=100` (`to` the account's `valuedThrough`, `from` 89 days before it), in the account's currency, oldest day first, with the hollow marker for `complete` `false` ("Some holdings were not valued on this day."). With `valuedThrough` `null`: "Not valued yet. The first value appears two days after the account was opened." An External account shows no chart. |
| D110 | Reports tab, Adviser Balances generate form: an optional "Balances at the end of" date input, empty by default (empty = now), with no date after today minus two days (UTC) offered. While a date is set, "Include closed accounts" is unchecked and disabled, with "A report for a past date includes every account that held cash or holdings that day." `parameters.date` is sent only when set. The schedule form has no date input. A 400 on `date` is shown at the input. History shows "At the end of <date>" in the parameters summary when the snapshot has `date`. |

**Customer Portal**

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C45 | Customer with a Custodial account | Dashboard | Under "Held on platform and elsewhere" (C14), a chart "Held on platform over time" from `GET /net-worth/funds-under-management` (self scope), 90 days, with the choice of 1 year. Above it: "In <reportingCurrency>, at each day's exchange rate. The latest two days are added once their prices are final." An incomplete day's point says "Some holdings could not be valued on this day." No Custodial account in the range → the chart is not shown. External accounts are not in it, and the "Held elsewhere" note (C14) is not repeated. |
| C46 | Customer | Dashboard | Under the per-currency totals, "Total in <folded.currency>: <folded.total> at today's exchange rates", as D108, with "Not counted: <currency>" when a line is not folded. With one currency only, which is the reporting currency, the line is not shown. |
| C47 | Customer with a Custodial account | Account page | The chart of D109, with the Customer's words for an incomplete day ("Some holdings could not be valued on this day.") and "Not valued yet. The first value appears two days after the account was opened." An External account shows no chart. |

### 9.1 Seeds and hosting

- `TestSeed`: no change. Its demo Tenant reports in USD and its demo Custodial account is in USD, so the Development Dashboard shows a series from 2026-01-19 with no fold; from 2026-04-05 its days are incomplete (`StalePrice`, valuation-run §9.1) unless the price job has stored real bars. A fold is proved by the functional tests, which write their own rates.
- `DevelopmentSmokeSeeder` and `webapi --seed-demo`: no change. The smoke Tenant holds cash only in its reporting currency ([smoke-test.md](../smoke-test.md) §3) and has no Custodial account, so its Dashboard shows no chart and no folded line.
- AppHost, TestAppHost and the VPS: no new setting, secret or container. `worker` needs the valuation read for an External account in a past-date report; it already runs Application.

---

## 10. Tests

The tests are the executable form of §4. Where they live:

| Project | Where |
| --- | --- |
| Domain.UnitTests | `Valuations/FxFoldTests` (R10, R11, §1.1) |
| Application.UnitTests | `Reports/AdviserBalancesBinderTests` (R16, R20), `Reports/AdviserBalancesPastRowsTests` (R17–R19), `Reports/AdviserBalancesPastPdfTests` (R18, R19) |
| Application.FunctionalTests | Folder `NetWorth/`: `FundsUnderManagementAccessTests` (R1, R4 and the cross-tenant isolation), `FundsUnderManagementRangeTests` (R3, R8), `FundsUnderManagementTests` (§1.1, R4–R8), `FundsUnderManagementFoldTests` (§1.1, §8.1, R9–R12), `NetWorthFoldTests` (R14, R15) |
| Application.FunctionalTests | Folder `Reports/`: `AdviserBalancesDateTests` (R16, R20 and the cross-tenant 404), `AdviserBalancesPastDateTests` (§1.1 through the job, R17–R19) |
| Adviser Portal | `DashboardPage.test.tsx` (D107, D108), `AccountDetailPage.test.tsx` (D109), `ReportsPage.test.tsx` (D110) |
| Customer Portal | `portal.test.tsx` (C45–C47) |

No Infrastructure.IntegrationTests: there is no script and no mapping. No Simulator.Tests.

---

## 11. Locked in this spec

No new ADR. [ADR 0003](../adr/0003-react-redux-typescript-vite-tailwind-frontend.md) is unchanged: point 9 adds no library.

| Item | Lock |
| --- | --- |
| Series | Funds under management only, from stored valuations; every day of a range up to the last final day, at most 366 days; the Customers in scope now; Custodial accounts of any status |
| Complete | No missing expected account, no incomplete stored valuation, every currency folded |
| Fold | On read, per currency per day, with the stored rate of the directed pair into the Tenant's reporting currency now; the age limit of 4 days; rounded to the reporting currency; a currency not folded is left out and marks the figure incomplete |
| Net worth now | A top-level `folded` at today's rates; values and breakdowns unchanged |
| Adviser Balances | Optional `date` up to the last final day, manual only; every account of the Adviser's Customers; Custodial from stored valuations, External from the valuation read; no cost; not-valued lines listed and left out of the subtotals |
| Route | `GET /net-worth/funds-under-management` (`accounts.read`) |
| Pages | Dashboard chart and folded total in both portals; account page chart of a Custodial account in both portals; date on the Adviser Balances form |
| Scripts, permissions, problem codes, events, ports, jobs, settings | None |

### Points to confirm

Each point was confirmed as recommended (2026-11-26) and is written into the rules above as stated here. The alternatives are kept for the record.

1. **The series covers funds under management only** (R4, §2 Out). Recommended in roadmap V4: it is a sum of stored rows that never change, it is the figure the advisory fee will use, and a read of a year needs one grouped query. Net worth of a past day with External accounts would value each External account on read for each day (a year of 20 accounts is 7,300 valuation reads), and the figures would change when a caller enters a backdated Transaction (account-valuation R8). Alternatives: `GET /net-worth?date=` for one day with External accounts valued on read; a series with External accounts valued on read, limited to a short range.
2. **The route sits under `/net-worth`** (R3). Recommended: both BFFs already proxy `/net-worth`, funds under management is the `Custodial` breakdown of net worth (custody-mode-grouping R8), and the route answers the scope of `GET /net-worth`. Alternative: a top-level `/funds-under-management`, which adds a prefix to both BFF allow-lists.
3. **A range with defaults, up to 366 days, every day listed, no paging** (R3, R8). Recommended: a chart reads one range at once; 366 items are small; listing a day with no account keeps the axis even. `to` after the last final day is refused rather than cut, so that a reader never takes a missing tail for a fall. Alternatives: a paged list as `GET /accounts/{id}/valuations`; leave out days with no account; cut `to` silently.
4. **The fold: per currency per day, at the rate of that day, into the reporting currency now** (R9, R10). Recommended: roadmap Q7 says the fold is done on read with the rate of the same day; converting the sum of each currency once (not each account) gives one rate and one rounding per currency, which a reader can check against `GET /fx-rates`. The age limit and the statuses are those of a valuation line, so one rule covers both. The reporting currency is read now, because the platform keeps no history of it. Alternatives: fold each stored valuation and add the results; fold at today's rate for every day (a chart that moves with today's rate, not with the account); keep the reporting currency of each day.
5. **A Closed account is not expected after the day its closure completed** (R6, R7). Recommended: valuation-run R3 stores no day after it, so R26 as written would mark every later day of a scope incomplete for ever once one account had been closed. This refines R26, which was reworded. Alternative: none that keeps the series usable.
6. **Net worth now gains a top-level `folded`** (R14, R15). Recommended: the roadmap closes "FX fold" of net-worth Out with this step, and a Customer or a firm with two currencies sees one total. It folds the values net worth has, a missing price counted as 0 included, and says so (D108). Alternatives: no fold on net worth now, only on the series; also fold every breakdown and the Dashboard figures.
7. **The Adviser Balances Report of a past date uses the stored valuations for Custodial accounts and the valuation read for External accounts** (R17–R19). Recommended: the Custodial figures then equal the chart of the same day and the fee base (valuation-run R25), and an External account, which has no stored valuation, is valued by the one rule that values a day. Alternatives: the valuation read for every account (a Custodial figure could then differ from the stored one after a late price); Custodial accounts only.
8. **`date` on a manual request only, up to the last final day, any account status, no cost** (R16, R20). Recommended: a fixed past date in a recurring schedule repeats one report; a relative date is a parameter of its own (§2 Out). The last final day keeps Custodial and External figures on the same final rows. Status is not read because an account closed since `date` held money on it. Alternatives: a date up to today, with today and yesterday from the valuation read; `includeClosedAccounts` kept and read as status now.
9. **Charts are plain SVG drawn by each portal, with a figures table** (§9). Recommended: one line chart per page needs no dependency, and the table keeps every figure readable and testable. Alternative: add a chart library (Recharts) to both portals.
10. **The Customer sees the fold in the Tenant's reporting currency** (R9, C45, C46). Recommended: the platform has one reporting currency per Tenant, and the response names it. The risk is a Customer with one USD account at an NZD firm, whose chart is in NZD; the account page chart (C47) stays in the account's currency. Alternative: the Customer's series is folded into the currency of their largest account.
11. **An account page chart of a Custodial account in both portals** (D109, C47). Recommended: `GET /accounts/{id}/valuations` exists and both BFFs proxy it, so the chart costs no backend work, and it is the first page that shows a stored valuation. Alternative: Dashboard charts only.
12. **No script, permission or index** (§6). Recommended: the index of valuation-run §6.1 was made for R26. Alternative: a summary table of funds under management per Tenant, currency and day, which would be a second copy of the rows (roadmap V3 rules it out).

### Found while writing, not changed here

- **valuation-run R26** counted every Custodial account of a scope whose first day is on or before the day, a Closed one after its closure too. Point 5 refines it, and R26 was reworded.
- **The Adviser of a Customer can change, and the series follows the assignment now.** A book that grew by reassignment shows a rise in its past that no money caused. Fixing it needs a history of assignments, which no spec owns.
- **The reporting currency can change** ([api-design.md](../api-design.md) §7.3, `PUT /tenants/{id}`), and every folded past figure changes with it. A later fee or performance report that folds should store the currency it used.
- **The Adviser Balances PDF of now still counts a missing price as 0** (reports §9.2). With this spec the same report for a past date lists such a holding as not valued, so the two kinds of report treat it differently. Changing the report of now is a change to reports R14, not made here.
- **Net worth now and the last point of the series disagree by design**: now counts Open accounts, External accounts and a missing price as 0, at today's prices; the series ends two days back and counts only stored Custodial figures. D107 and D108 sit on one page; their wording keeps them apart.

Still open, not this slice:

- Net worth of a past day with External accounts (point 1)
- A series of one Customer on a staff route, and a chart on the Adviser Portal customer page
- A relative date in a report schedule
- A fold in a report; a fold of the breakdowns
- Cost and unrealised gain on a past day (ledger replay)
- A history of Adviser assignments and of the reporting currency

---

## 12. Changes to other docs

Made when this spec was accepted (2026-11-26):

[net-worth.md](net-worth.md) (R6: the fold is `folded`, the arrays stay per currency; §2 Out and "Still open": the series and the fold point here; §8.1 `folded`; §9 charts point here), [custody-mode-grouping.md](custody-mode-grouping.md) (Out and "Still open": the history of funds under management is the series here; the fold of funds under management stays open), [valuation-run.md](valuation-run.md) (R26: the expected accounts of R6 here, and "no route reads it" becomes this route; §2 Out and "Still open": V4 points here), [account-valuation.md](account-valuation.md) (§2 Out and "Still open": V4 points here), [reports.md](reports.md) (R14 and §9.2: the optional `date`; §2 Out "Historical as-of snapshots" points here; §7.1 the `date` field; History row), [fx-rates.md](fx-rates.md) (Out: "Net-worth FX fold" points here; R7: a fold catches a missing pair as a valuation does), [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (a section "Valuation history" with D107–D109; Out "Charts, historical comparison, FX fold" and "Still open: Daily snapshots and historical charts" point here), [adviser-portal-reports.md](../adviser-portal/adviser-portal-reports.md) (D110; Out "Historical as-of snapshots" points here), [customer-portal.md](../customer-portal/customer-portal.md) (a section "Valuation history" with C45–C47; Out and "Still open" charts point here), [bff-adviser-portal.md](bff-adviser-portal.md) and [bff-customer-portal.md](bff-customer-portal.md) (no change to the allow-list; a note that `/net-worth/funds-under-management` is under the existing prefix), [api-design.md](../api-design.md) (§6 the net-worth resource row; §7.18 the series; change log), [domain-model.md](../domain-model.md) (§8.22 `FxFold`), [architecture.md](../architecture.md) (none expected; checked on accept), [function-plan.md](../function-plan.md) (§5.4: the historical series), [glossary.md](../glossary.md) (the review terms become accepted; "Net worth" gains its folded total; "Funds under management" points at the series), [features/README.md](README.md) row status, [future-plans.md](../future-plans.md) §1 (no change: V4 is not a business roadmap step), and [valuation-roadmap.md](../valuation-roadmap.md) (§5 V4, §8; Q7 points here for the read half).
