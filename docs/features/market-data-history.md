---
title: Market data history
status: accepted
phase: 3
language: en
owner: ""
created: 2026-11-25
last_updated: 2026-11-25
related:
  - ../valuation-roadmap.md
  - ../future-plans.md
  - instrument-prices.md
  - fx-rates.md
  - ../adr/0019-fx-rate-source-and-refresh.md
  - simulated-instruments.md
  - instruments.md
  - orders.md
  - fx-conversions.md
  - ../known-issues/eodhd-nz-jp-coverage.md
  - ../architecture.md
  - ../deployment.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
---

# Market data history

Step 1 of [valuation-roadmap.md](../valuation-roadmap.md) §8. It covers roadmap item **V1** and writes the decisions **Q2** (the unadjusted close), **Q3** (one floor date) and **Q9** (one spec for prices and rates) as rules. It amends [instrument-prices.md](instrument-prices.md), [fx-rates.md](fx-rates.md) and [ADR 0019](../adr/0019-fx-rate-source-and-refresh.md).

`InstrumentPrices` and `FxRates` get a row for every source date from a configured **history floor date** on, not only from the day a job first ran. A row that is two or more days old becomes **final**: it is never changed again. Both tables get a read route.

New: configuration `MarketDataHistory:FloorDate` and `Eodhd:HistoryDays` on `worker`; a range read on each source port; `worker` job `fx-rate-history-sync`; the Domain rule `MarketDataRow.IsFinal`; read ports `IMarketData.TryGetPriceDetail` / `ListPricesAsync` and `IFxRate.ListRatesAsync`; routes `GET /instruments/{id}/prices` and `GET /fx-rates`. No script, table, permission, problem code, domain event or `TransactionType`. No simulator change.

**Status is `accepted` (2026-11-25), landed 2026-11-25.** The ten points of §11 were confirmed as recommended.

Notes from landing:

- R4–R6: the stores take "today" from the UTC date of the write's `fetchedAt`. Every job passes the current time. `FxRateStore` reads only the rows of the batch's bases and dates, not the whole table.
- R7, R8: `IInstrumentPriceSource.GetBarsAsync` replaces `GetLatestAsync`. `EodhdClient` returns every dated bar when asked with `from`, and the latest one without it. A bar without a readable `close` comes back with no close, and the job skips it.
- R9, R11: a source failure still ends the run and is logged (instrument-prices R8). The symbol groups written before it stay, because each group is its own write.
- R12, R15: `FxRateHistorySyncJob` (Application) throws `FxRateHistorySyncFailedException` naming the failed bases. Hangfire runs it through `FxRateHistoryHangfireJob` (Infrastructure), which carries the no-retry attribute.
- R19: `InstrumentVisibility.Readable` is the one lookup that both `GET /instruments/{id}` and the prices route use.
- R21: `from` and `to` bind as text and the validator parses them as `yyyy-MM-dd`, so a bad date answers `errors.From` / `errors.To` rather than a binding error.

---

## 1. Summary

| | Today | After this slice |
| --- | --- | --- |
| Price rows of an instrument | From the day the EODHD job first ran for it. A missed run leaves a hole | Every trading day from the floor date. Each daily run repairs holes |
| The EODHD response | The full history of the symbol; the job keeps the latest bar | The bars from the floor date; the job stores each one it lacks. Still one call per symbol |
| Rate rows of a pair | From the day the Frankfurter job first ran | Every day from the floor date, by a second job once a day |
| A stored row of a past day | Written again whenever the source returns that date | Final after one day: inserted when missing, never updated |
| Reading stored prices and rates | Only through the ports, inside the platform | `GET /instruments/{id}/prices` and `GET /fx-rates` |
| The date of the price a read chose | Not returned by `IMarketData` | `TryGetPriceDetail` returns the price with its date and source |

The rule that distinguishes this slice: the past is final. A valuation of a past day (roadmap V2) reads the same price and the same rate every time it is asked.

### 1.1 Worked example

Read from the two sources on 2026-11-25 (EODHD with its public demo key; Frankfurter v2). The tests use stored copies of these answers, not live calls.

**A split.** `AAPL.US` around its 4-for-1 split of 2020-08-31:

| `date` | `close` | `adjusted_close` |
| --- | --- | --- |
| 2020-08-27 | 500.04 | 121.0599 |
| 2020-08-28 | 499.23 | 120.8638 |
| 2020-08-31 | 129.04 | 124.9626 |
| 2020-09-01 | 134.18 | 129.9402 |

The platform stores `close`: 499.23 for 2020-08-28 and 129.04 for 2020-08-31 (R7). An account that held 10 shares before the split and 40 after it is worth 10 × 499.23 = 4,992.30 on the 28th and 40 × 129.04 = 5,161.60 on the 31st. With `adjusted_close` the 28th would be 10 × 120.8638 = 1,208.64, which is wrong. EODHD also recomputes `adjusted_close` whenever a later dividend is paid, so it is not a stable figure.

**A holiday and a weekend.** `AAPL.US` has bars for 2025-12-31 (271.86), 2026-01-02 (271.01) and 2026-01-05 (267.26): none for New Year's Day and none for 3 and 4 January. With a floor date of 2025-12-29, `GET /instruments/{id}/prices?to=2026-01-04&pageSize=1` answers the row of 2026-01-02, and `to=2026-01-01` answers the row of 2025-12-31. With a floor date of 2026-01-01 the second request answers no row, because the job stores nothing before the floor (R2).

**A currency pair over the same days.** Frankfurter v2 answers a rate for every calendar day:

| `date` | USD → NZD | USD → AUD |
| --- | --- | --- |
| 2026-01-01 | 1.7294 | 1.4954 |
| 2026-01-02 | 1.7313 | 1.4952 |
| 2026-01-03 | 1.7315 | 1.4951 |
| 2026-01-04 | 1.7320 | 1.4951 |
| 2026-01-05 | 1.7350 | 1.4966 |

So `FxRates` holds a row for the weekend too, and the rate of 2026-01-04 is 1.7320, not Friday's.

---

## 2. Scope

**In**

- Configuration: `MarketDataHistory:FloorDate` and `Eodhd:HistoryDays` on `worker`, with a start-up check
- Domain: `MarketDataRow.IsFinal(asOfDate, today)`; `MarketDataHistoryWindow` (the first date a source is asked for)
- Application: `IInstrumentPriceSource.GetBarsAsync`; `IFxRateSource.GetRangeAsync`; `InstrumentPriceSyncJob` stores every bar from the floor date; new `FxRateHistorySyncJob`; both stores insert or update by the final rule; `IMarketData.TryGetPriceDetail` and `ListPricesAsync`; `IFxRate.ListRatesAsync`; queries `GetInstrumentPrices` and `GetFxRates`
- Infrastructure: `EodhdClient` sends `from`; `FrankfurterClient` sends `from` and `to`; recurring job `fx-rate-history-sync`
- HTTP: `GET /instruments/{id}/prices`, `GET /fx-rates`
- AppHost and deploy entries for the two settings
- Tests listed in §10

**Out**

- Valuing anything. The age limit of a price or rate, the incomplete mark and the position of a past day are roadmap V2
- Stored valuations and their run (V3); net worth history and the FX fold (V4)
- A back-fill for an instrument served by a market data connection. Its rows are the ones the polls and the seed wrote (simulated-instruments R10). No simulator change
- Rows before the floor date, and history older than the EODHD plan in use serves (R3)
- A correction the source makes to a final row. It is not picked up (roadmap §6)
- A second price source, and prices for NZX and Tokyo ([known issue](../known-issues/eodhd-nz-jp-coverage.md), unchanged)
- An adjusted close, open, high, low or volume
- Any write route for prices or rates, and any portal page
- Changing the hourly `fx-rate-sync` schedule or its skip rule (fx-rates R16), or the daily `instrument-price-sync` schedule and its skip rule (instrument-prices R14)
- `TestSeed` rows. The seed keeps its one fixed date

---

## 3. Stories

1. As the platform I hold a price for every trading day and a rate for every day since the floor date, so that a past day can be valued.
2. As the platform I repair a missed run by myself: the next daily run stores the days the last one did not.
3. As a caller of the ports I get the same price and the same rate for a past day every time I ask.
4. As a developer working a valuation out by hand, I read the stored prices of an instrument and the stored rates of a pair, with the date and source of each row, and compare them with my sheet.
5. As a TenantAdmin or Adviser I can read the price history of an instrument in my firm's catalog, and not of another firm's.

---

## 4. Rules

### 4.1 The floor date and the first date asked for

| ID | Rule |
| --- | --- |
| R1 | **History floor date.** `MarketDataHistory:FloorDate` on `worker`, a date as `yyyy-MM-dd`. It is the earliest `AsOfDate` a history read may store, the same date for prices and rates (roadmap Q3). Not set: no history is read, and both sources are asked for the latest value only, as before this slice; R4 and R5 still apply. A value that is not a date, or a date after the platform's UTC date, stops `worker` at start-up, before it listens. The rule reads the configuration, not the environment name. |
| R2 | **Nothing before the floor.** A history read stores no row with `AsOfDate` before the floor date. Rows that are already stored stay, whatever their date. Setting a later floor deletes nothing and only shortens what is asked for. Setting an earlier floor makes the next daily run ask from the new date. A price read as of a day before the first stored row finds nothing, so the floor date should be some days before the first day anyone needs to value (§1.1). |
| R3 | **How far back EODHD is asked.** `Eodhd:HistoryDays` on `worker`: a whole number from 1 to 36500, default 360. EODHD is asked from the later of the floor date and today (UTC) minus that many days. The free plan serves the past year only, so the default keeps the request inside it; a plan with more history sets a larger number. A value outside the range stops `worker` at start-up. Frankfurter has no such limit and is asked from the floor date. |

### 4.2 Final rows

Amendment to [instrument-prices.md](instrument-prices.md) R5 and [fx-rates.md](fx-rates.md) R5.

| ID | Rule |
| --- | --- |
| R4 | **Final.** A row of `InstrumentPrices` or `FxRates` is **final** when its `AsOfDate` is two or more days before the platform's UTC date. A final row is never updated or deleted. A missing row of such a date may be inserted. |
| R5 | **Today and yesterday.** A row dated today or yesterday (UTC) may be updated: `Price` or `Rate`, `Source` and `FetchedAt` take the new values, as instrument-prices R5 and fx-rates R5 say today. So a price or rate of day `D` can still change on `D + 1`, and is fixed from `D + 2` on. |
| R6 | **Every job follows it.** R4 and R5 hold for `instrument-price-sync`, `market-data-sync:{connection}`, `fx-rate-sync` and `fx-rate-history-sync`. A job that gets a value for a date whose row is final leaves the row as it is and writes no log line for it. A connection's run writes today's row only (simulated-instruments R10), so nothing changes for it. `TestSeed` and `webapi --seed-demo` are not jobs and are not bound by this rule. |
| R7 | **The price is the unadjusted close** (roadmap Q2). `Price` is the `close` field of the EODHD bar, as today: the price as traded on that date, not adjusted for any later split or dividend. `adjusted_close` is never read or stored. A bar without a `date` or a `close`, or with a `close` that is not `> 0`, is skipped. |

### 4.3 Price history

Amendment to [instrument-prices.md](instrument-prices.md) R5, §2 (Out) and §11.

| ID | Rule |
| --- | --- |
| R8 | **One call, every bar.** With a floor date set, `instrument-price-sync` asks EODHD for each symbol with `from` = the first date of R3 and stores every bar of the answer whose date is on or after that date, for every active instrument of the group (instrument-prices R15), by R4 and R5. It is the same single call per symbol as today, so a run spends no extra call. The latest bar is stored like the others; there is no separate "latest" request. |
| R9 | **Unchanged around it.** When the job runs, whether it calls the source (instrument-prices R14), which instruments it fetches (active ones, instrument-prices R3; served ones left out, simulated-instruments R12), the currency check, the spacing between calls and what a failure does (instrument-prices R8) do not change. A symbol EODHD rejects gets no history, as it gets no price today. |
| R10 | **A new or re-activated instrument** is in the next daily run like any other, so its history from the floor date arrives with its first price. Nothing marks an instrument as "back-filled": every run asks for the whole window and stores what is missing. |
| R11 | **All rows of a symbol in one transaction.** The rows of one symbol group are stored together. A failure at one symbol leaves the symbols already stored in place. |

### 4.4 Rate history

Amendment to [fx-rates.md](fx-rates.md) R5, §2 (Out) and §11, and [ADR 0019](../adr/0019-fx-rate-source-and-refresh.md) (Consequences).

| ID | Rule |
| --- | --- |
| R12 | **A second job.** `worker` registers the recurring job `fx-rate-history-sync` on the existing queue `fx`, daily at 11:30 UTC (cron `30 11 * * *`), before `instrument-price-sync` at 12:00. With no floor date set the job ends at once. No automatic Hangfire retries; the next day's run is the retry. |
| R13 | **A run.** For each active currency as base, the job asks Frankfurter once for the range from the floor date to yesterday (UTC), with the other active currencies as quotes: `GET /v2/rates?base={base}&quotes={quotes}&from={floor}&to={yesterday}`. It stores every record of the answer by R4 and R5. So yesterday's row, which `fx-rate-sync` wrote during yesterday, takes the value the source gives one day later, and is final from the next day on. |
| R14 | **Records the job skips.** A record dated before the floor date or after yesterday; a record whose base and quote are the same; a record with a currency that is not active in the catalog; a record with a rate that is not `> 0`. The last two are logged as a warning. |
| R15 | **Failure.** A base whose call fails (no connection, a timeout, any status other than 200, a body that cannot be read) is logged with the base and stores nothing; the other bases are still asked. The rows of one base are stored in one transaction. The job is reported to Hangfire as failed when any base failed, after every base was tried. |
| R16 | **`fx-rate-sync` is unchanged** in schedule, skip rule and request (the latest rates). Its writes follow R4 and R5: it updates today's and yesterday's rows and leaves a final row alone. |

### 4.5 Read ports

| ID | Rule |
| --- | --- |
| R17 | **Price with its date.** `IMarketData.TryGetPriceDetail(instrumentId, asOf)` returns the row `TryGetPrice` reads for `asOf` (instrument-prices R6) as `{ price, asOfDate, source }`, or empty when there is none. `TryGetPrice` returns the `price` of the same row. It is the twin of `IFxRate.GetRateDetail`. |
| R18 | **Lists.** `IMarketData.ListPricesAsync(instrumentId, from?, to?, page, pageSize)` and `IFxRate.ListRatesAsync(base, quote, from?, to?, page, pageSize)` return the stored rows whose `AsOfDate` is inside the inclusive range, newest first, with the total count. `ListRatesAsync` reads the directed pair only; it does not use the inverse pair and returns nothing for a currency against itself. Neither calls a source (instrument-prices R9, fx-rates R10). |

### 4.6 Read routes

| ID | Rule |
| --- | --- |
| R19 | **Prices of one instrument.** `GET /instruments/{id}/prices` (`instruments.read`) → 200 list envelope of `{ date, price, currency, source, fetchedAt }`, sorted by `date` descending. `currency` is the instrument's `QuoteCurrency`. The instrument is found as `GET /instruments/{id}` finds it: a TenantAdmin or Adviser gets 404 for another Tenant's or a missing id; a SystemAdmin may read any Tenant's instrument; a Customer gets 403. An inactive instrument is readable: its rows stay (instrument-prices R3). |
| R20 | **Rates of one pair.** `GET /fx-rates?base={code}&quote={code}` → 200 list envelope of `{ date, base, quote, rate, source, fetchedAt }`, sorted by `date` descending. Any authenticated caller, as `GET /currencies`: rates are platform reference data with no Tenant. `base` and `quote` are required, three letters, compared without regard to case, and must be codes of the catalog, active or not; otherwise 400 `errors.Base` or `errors.Quote`. The same code twice → 400 `errors.Quote` (`No rate is stored for a currency against itself.`). |
| R21 | **Range and paging, both routes.** Optional `from` and `to` as `yyyy-MM-dd`, both inclusive; a value that is not a date → 400 `errors.From` or `errors.To`; `from` after `to` → 400 `errors.From`. `page` default 1, `pageSize` default 20, at most 100. No rows is an empty page, not 404. |
| R22 | **The row a read would choose.** Because the list is newest first, `?to={date}&pageSize=1` answers the row that `IMarketData` or `IFxRate` reads as of that date, or an empty page when there is none. There is no separate "as of" route. |
| R23 | **No tenant on the price row.** Two Tenants that list the same symbol get the same prices from their own instrument ids (instrument-prices R7, R15). A caller reaches a price only through an instrument it may read. |

Policies: none added. `instruments.read` reads the prices route.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `MarketDataRow` | Static rule (new, `src/Domain/MarketData`) | `IsFinal(asOfDate, today)`: true when `asOfDate` is two or more days before `today` (R4) |
| `MarketDataHistoryWindow` | Value (new) | From the floor date, today and an optional number of days: `From`, the later of the floor date and today minus the days (R3). `Contains(date)` |
| `Instrument`, `Currency` | Existing | No change |

Invariants:

- A final row is never updated. Held by the two stores, which ask `MarketDataRow.IsFinal` before they write.
- No history read stores a row before the floor date. Held by the two sync jobs.

Domain events: none. A price or rate row stays infrastructure and is not a domain entity (instrument-prices §5, fx-rates §5).

Update [domain-model.md](../domain-model.md) when this spec is accepted (the two port notes only).

---

## 6. Database

None. No script.

`InstrumentPrices` (PK `(InstrumentId, AsOfDate)`) and `FxRates` (PK `(BaseCurrency, QuoteCurrency, AsOfDate)`) keep their columns and indexes; both primary keys already serve the range reads of R18. The tables grow by about 250 price rows per instrument and 365 rate rows per directed pair a year.

`FetchedAt` of a final row is the time the row was last written, which is no longer the time of the last run.

Update [database-design.md](../database-design.md) when this spec is accepted (a note on the two tables: history from the floor date, final rows; no script).

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Port | `IInstrumentPriceSource` | `GetBarsAsync(symbol, quoteCurrency, from?)` → the bars with their dates, or the rejection of today. Replaces `GetLatestAsync`; with no `from` it returns the latest bar only |
| Port | `IFxRateSource` | Adds `GetRangeAsync(base, quotes, from, to)` |
| Port | `IInstrumentPriceStore`, `IFxRateStore` | The write applies R4 and R5 against the UTC date of its `fetchedAt`, the time of the write: every job passes the current time, so that date is today |
| Port | `IMarketData` | Adds `TryGetPriceDetail` and `ListPricesAsync` (R17, R18) |
| Port | `IFxRate` | Adds `ListRatesAsync` (R18) |
| Job | `InstrumentPriceSyncJob` (existing) | R8–R11 |
| Job | `FxRateHistorySyncJob` (new) | R12–R15. A plain class in Application, hosted by `worker`, like `FxRateSyncJob` |
| Job | `FxRateSyncJob` (existing) | R16 |
| Query | `GetInstrumentPrices` (new) | R19, R21 |
| Query | `GetFxRates` (new) | R20, R21 |

Where each rule lives:

| Rule | Where |
| --- | --- |
| The floor date and the days (R1, R3) | A configuration reader `worker` calls at start-up, before it builds (the pattern of market data connections) |
| The first date asked for (R3) | `MarketDataHistoryWindow` |
| Final or not (R4, R5) | `MarketDataRow.IsFinal`, called by the stores |
| Which records are skipped (R7, R14) | The sync jobs |
| Instrument visibility (R19) | The lookup `GetInstrumentById` already uses |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/instruments/{id}/prices` | `instruments.read` | 200 envelope | 400 / 401 / 403 / 404 |
| GET | `/fx-rates` | authenticated | 200 envelope | 400 / 401 |

List envelope: `{ items, page, pageSize, totalCount }`; page default 1, pageSize default 20, max 100.

### 8.1 Bodies

`GET /instruments/{id}/prices?from=2025-12-29&to=2026-01-05`:

```json
{
  "items": [
    { "date": "2026-01-05", "price": 267.26, "currency": "USD", "source": "EODHD", "fetchedAt": "2026-11-26T12:00:04Z" },
    { "date": "2026-01-02", "price": 271.01, "currency": "USD", "source": "EODHD", "fetchedAt": "2026-11-26T12:00:04Z" },
    { "date": "2025-12-31", "price": 271.86, "currency": "USD", "source": "EODHD", "fetchedAt": "2026-11-26T12:00:04Z" },
    { "date": "2025-12-30", "price": 273.08, "currency": "USD", "source": "EODHD", "fetchedAt": "2026-11-26T12:00:04Z" },
    { "date": "2025-12-29", "price": 273.76, "currency": "USD", "source": "EODHD", "fetchedAt": "2026-11-26T12:00:04Z" }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 5
}
```

`GET /fx-rates?base=USD&quote=NZD&to=2026-01-04&pageSize=1`:

```json
{
  "items": [
    { "date": "2026-01-04", "base": "USD", "quote": "NZD", "rate": 1.732, "source": "Frankfurter", "fetchedAt": "2026-11-26T11:30:02Z" }
  ],
  "page": 1,
  "pageSize": 1,
  "totalCount": 4
}
```

`date` is a date with no zone ([api-design.md](../api-design.md) §2). `price` and `rate` are JSON numbers with up to 8 decimal places. `source` is the stored `Source` (`EODHD`, `Simulator`, `Frankfurter`).

### 8.2 Errors

All refusals are ordinary validation (400 with `errors`, no problem `code`).

| Case | Response |
| --- | --- |
| No Bearer, or a dead one | 401, never 302 |
| Customer on `GET /instruments/{id}/prices` | 403 |
| TenantAdmin or Adviser with another Tenant's instrument id, or a missing id | 404 |
| `from` or `to` that is not a date; `from` after `to` | 400 `errors.From` / `errors.To` |
| `base` or `quote` missing, not three letters, or not in the catalog; the same code twice | 400 `errors.Base` / `errors.Quote` |
| `pageSize` above 100 or below 1; `page` below 1 | 400, as every list |

Keep the resource catalog in [api-design.md](../api-design.md) pointing here for these rules.

---

## 9. UI

None in this slice. No Adviser Portal, Customer Portal or Back Office page shows a price or a rate history.

`bff-adviser-portal` already proxies the prefix `/instruments`, so the prices route can be reached through it by a signed-in TenantAdmin or Adviser; no page calls it. `/fx-rates` is on no BFF allow-list and is reached with a Bearer token (Scalar).

### 9.1 Seeds and hosting

- `TestSeed`, `DevelopmentSmokeSeeder` and `webapi --seed-demo`: no change.
- AppHost: `worker` gets `MarketDataHistory__FloorDate=2026-01-01`. TestAppHost: not set; the tests that need a floor date configure it on their host.
- VPS: `compose.yml` gives `worker` `MarketDataHistory__FloorDate` from `.env` (`2026-01-01`). `Eodhd__HistoryDays` is not set; the default applies.
- First run after the deploy: `instrument-price-sync` stores the bars from the floor date for every instrument EODHD knows (at most the past 360 days), and `fx-rate-history-sync` stores every day from the floor date for the 30 directed pairs.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `MarketDataRow.IsFinal`: today and yesterday are not final; two days ago is. `MarketDataHistoryWindow`: the later of the floor date and today minus the days; no days → the floor date; `Contains` at both edges |
| Application.UnitTests | `InstrumentPriceSyncJob` with a fake source and store: with a floor date it asks from the window's first date and passes every bar on or after it; a bar before the window is dropped; a bar with no close or a close of 0 is skipped; with no floor date it asks for the latest bar as today; a served instrument is never asked; the same symbol in two Tenants gets every bar in both. `FxRateHistorySyncJob`: one range call per active base, from the floor date to yesterday; the records of R14 are skipped; a failed base stores nothing and the others are stored; no floor date → no call |
| Infrastructure.IntegrationTests | The two stores against the database: a missing past date is inserted; a final row keeps its `Price` / `Rate`, `Source` and `FetchedAt` when the source sends another value; yesterday's and today's rows are updated. `EodhdClient` against the stored answer of §1.1: `from` is in the query string; every bar is returned with `close`, never `adjusted_close` (499.23 and 129.04 across the split); an empty array and a 404 are a rejection. `FrankfurterClient` against the stored answer of §1.1: `from` and `to` in the query string; the weekend dates are returned. Start-up check: a floor date that is not a date or is in the future, and `HistoryDays` of 0 or 36501, stop the host; no floor date starts. `fx-rate-history-sync` is registered on queue `fx` with cron `30 11 * * *`. The selection of `TryGetPriceDetail` (latest on or before, with its date and source; none → empty; a SQL query of the store, so tested against the database). `ListPricesAsync` and `ListRatesAsync`: range, order, paging, the directed pair only |
| Application.FunctionalTests | `GET /instruments/{id}/prices`: no Bearer → 401 not 302; Customer → 403; TenantAdmin and Adviser read their instrument, newest first; `from`, `to` and both; `to` with `pageSize=1` answers the row of the latest date on or before it (the weekend and holiday of §1.1); a date before the first row → empty page; an inactive instrument is readable; each 400 of R21; SystemAdmin reads an instrument of any Tenant. `GET /fx-rates`: no Bearer → 401; TenantAdmin, Adviser, Customer and SystemAdmin → 200; the pair is directed (NZD → USD does not answer USD → NZD rows); each 400 of R20 and R21 |
| Application.FunctionalTests — isolation | Two-tenant fixture: a TenantAdmin and an Adviser of Tenant A with the id of Tenant B's instrument → 404, also when both Tenants list the same symbol |

No isolation test on `/fx-rates` (platform data, fx-rates §10).

---

## 11. Locked in this spec

No new ADR. ADR 0019 keeps its decision (Frankfurter, a local table, the read path never calls the source); its Consequences gain the history job and the final rule.

| Item | Lock |
| --- | --- |
| Floor date | `MarketDataHistory:FloorDate` on `worker`; one date for prices and rates; not set = no history |
| EODHD reach | `Eodhd:HistoryDays`, default 360; asked from the later of the floor date and today minus the days |
| Final row | `AsOfDate` two or more days before today (UTC): insert when missing, never update |
| Price | The unadjusted `close` |
| Price history | The existing daily call per symbol with `from`; every bar stored; no marker |
| Rate history | `fx-rate-history-sync`, queue `fx`, daily at 11:30 UTC; one range call per base from the floor date to yesterday |
| Read ports | `IMarketData.TryGetPriceDetail`, `ListPricesAsync`; `IFxRate.ListRatesAsync` |
| Routes | `GET /instruments/{id}/prices` (`instruments.read`); `GET /fx-rates` (authenticated) |
| Scripts, permissions, problem codes, events | None |

### Points to confirm

Each point was confirmed as recommended (2026-11-25) and is written into the rules above as stated here. The alternatives are kept for the record.

1. **The floor date is a setting on `worker`, and no setting means no history** (R1). Recommended: a deployment chooses its own date, tests and the TestAppHost keep today's behaviour unless they opt in, and the date can move. Development and the VPS use 2026-01-01, the seed's date. Alternative: a required setting; a date fixed in code.
2. **No marker of what is already stored; every daily run asks for the whole window and inserts what is missing** (R8, R10, R13). Recommended: no table, a missed day repairs itself, and a new instrument or currency needs no special step. The cost is a larger answer each day: about 250 bars per symbol and 365 records per pair for each year since the floor date. Alternative: a stored mark per instrument and per pair of the dates already covered, which keeps each request small and adds two tables.
3. **A row is final from the second day on** (R4, R5). Recommended: yesterday's rate is read once more by the history job, after every provider of the Frankfurter blend has published for that day, and then never again. Roadmap Q5 values yesterday after today's jobs, so a valuation reads the settled figure. Alternative A: only today's row may change, and the rate of a day is the first one the source gave that day. Alternative B: keep updating any date the source returns, as today, and accept that the value of a past day can change without a trace.
4. **`Eodhd:HistoryDays`, default 360** (R3). Recommended: the free plan serves one year, and the vendor's documentation does not say what it answers to a `from` before that. A request that stays inside the plan cannot fail for this reason on the day the floor date becomes older than a year. Alternative: always ask from the floor date and rely on the plan to shorten the answer.
5. **Rate history is a job of its own, once a day** (R12, R16). Recommended: `fx-rate-sync` runs every hour and on a quiet day keeps calling; a range request from the floor date belongs in one run a day. 11:30 UTC is after every provider has published for yesterday and before the price job. Alternative: a range request inside the hourly job.
6. **Prices are read under their instrument with `instruments.read`; rates by any authenticated caller** (R19, R20). Recommended: a price belongs to an instrument row, and the instrument's own visibility rule then covers the price; rates have no Tenant, like the currency catalog. Alternative: both routes for SystemAdmin only (`tenants.manage`).
7. **Newest first, and no "as of" route** (R21, R22). Recommended: one list answers both questions, and `to` with `pageSize=1` shows exactly the row a valuation will choose. Alternative: `GET /instruments/{id}/prices/as-of?date=`.
8. **`TryGetPriceDetail` is added now** (R17). Recommended: the roadmap puts it in V1, and it is the same selection the list route shows; V2 is its first caller outside tests. Orders R10 keeps `LatestDateAsync` until a later change moves it. Alternative: add it with V2.
9. **No back-fill for served instruments, no `TestSeed` change, no simulator change** (§2 Out). Recommended: the simulator's day is not the platform's day (simulated-instruments R10), so its past days cannot be stored under platform dates. Seeded history for Development comes with roadmap V3. Alternative: seed a short price history now.
10. **A correction to a final row is not picked up** (§2 Out). Recommended: it is the price of being reproducible. What happens when a base of a fee changes is a question for roadmap step 4. Alternative: a SystemAdmin command that reads one instrument's history again.

### Found while writing, not changed here

- **EODHD bars carry no `currency` field** (fields: `date`, `open`, `high`, `low`, `close`, `adjusted_close`, `volume`). `EodhdClient` reads one when it is there, so the currency check of instrument-prices §11 ("a source currency that is not the instrument quote currency") never fires for EODHD. The check stays meaningful for a market data connection.
- **To check with the real key:** whether EODHD quotes London shares in pence. If it does, the stored price of a `GBP` instrument such as `SHEL` is 100 times too high. This would be a defect of the price sync as it is today, not of this slice, but history would multiply it. It needs its own fix before V2 values such a holding.

Still open, not this slice:

- NZX and Tokyo prices ([known issue](../known-issues/eodhd-nz-jp-coverage.md))
- A larger EODHD plan, for history older than a year
- Which rates Inland Revenue accepts for the FIF conversions (roadmap §6, step 6)
- A price history page in a portal

---

## 12. Suggested commits

The repository should build after each commit. Agents split each behaviour into red then green.

1. `MarketDataRow.IsFinal` and `MarketDataHistoryWindow` (R3, R4) + Domain unit tests.
2. The two stores write by the final rule (R4–R6, R16) + integration tests.
3. The configuration reader and the `worker` start-up check (R1, R3) + integration tests.
4. `IInstrumentPriceSource.GetBarsAsync`, `EodhdClient` with `from`, `InstrumentPriceSyncJob` stores every bar (R7–R11) + unit and integration tests.
5. `IFxRateSource.GetRangeAsync`, `FrankfurterClient` range, `FxRateHistorySyncJob` and its recurring job (R12–R15) + unit and integration tests.
6. `TryGetPriceDetail`, `ListPricesAsync`, `ListRatesAsync` (R17, R18) + unit and integration tests.
7. `GET /instruments/{id}/prices` (R19, R21–R23) + functional and isolation tests.
8. `GET /fx-rates` (R20, R21) + functional tests.
9. AppHost and deploy entries (§9.1).
10. Repo docs: mark this file landed; valuation roadmap §8 step 1.

Do not put a valuation, an age limit or a seeded history in these commits.

### Docs to amend when this spec is accepted

[instrument-prices.md](instrument-prices.md) (R5, §2 Out "Historical back-fill", R12, §11: every bar from the floor date, final rows, the read route), [fx-rates.md](fx-rates.md) (R5, §2 Out, R14, §5 port: the history job, final rows, the read route), [ADR 0019](../adr/0019-fx-rate-source-and-refresh.md) (Consequences), [api-design.md](../api-design.md) (§6 resource rows, §7.5 note, a rates note, change log), [architecture.md](../architecture.md) (the two ports, the job `fx-rate-history-sync`), [domain-model.md](../domain-model.md) (port notes), [database-design.md](../database-design.md) (the note on the two tables), [deployment.md](../deployment.md) (the `worker` setting), [glossary.md](../glossary.md) (the review terms become accepted; `IMarketData` and `IFxRate` rows), [features/README.md](README.md) row status, and [valuation-roadmap.md](../valuation-roadmap.md) §5 V1 and §8.
