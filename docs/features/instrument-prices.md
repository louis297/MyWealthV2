---
title: "Instrument Prices"
status: accepted
phase: 3
language: en
owner: ""
created: 2026-11-02
last_updated: 2026-11-26
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../architecture.md
  - ../adr/0004-money-as-decimal-with-currency.md
  - ../adr/0010-instrument-catalog.md
  - ../adr/0017-background-worker.md
  - ../adr/0019-fx-rate-source-and-refresh.md
  - instruments.md
  - fx-rates.md
  - buy-sell.md
  - net-worth.md
---

# Instrument Prices

Local end-of-day (EOD) price cache for instruments. Hangfire job pulls daily reference prices for instruments that exist in the platform catalog and upserts them. `IMarketData` reads the local table. Follows the same pattern as [FX Rates](fx-rates.md) / [ADR 0019](../adr/0019-fx-rate-source-and-refresh.md). Adds script `0022_instrument_prices.sql`. No new HTTP policy. No portal page.

**Status is `accepted`.** Implementation follows this file.

---

## 1. Summary

The worker fetches the latest EOD prices for instruments that are active in any tenant catalog and stores one price per instrument per as-of date. Reads go through the existing `IMarketData` port against the table. A missing price returns empty (not 0). Prices are platform data, not tenant-scoped. Quote currency is the instrument’s `QuoteCurrency`.

---

## 2. Scope

**In**

- Table `InstrumentPrices` (application schema).
- Hangfire recurring job `InstrumentPriceSyncJob` (daily at 12:00 UTC).
- Application port implementation of `IMarketData` that reads the table.
- Upsert by `(InstrumentId, AsOfDate)`.
- Fetch for active instruments across all tenants (union of symbols).
- Tests for upsert, as-of selection, missing price returns empty, and job behaviour on source failure.

**Out**

- Real-time or intraday prices.
- Per-tenant price overrides.
- A back-fill before the history floor date ([market-data-history.md](market-data-history.md)).
- A second rate source or failover provider.
- HTTP endpoint for prices (deferred; a read surface is expected later for Scalar / ops, not a permanent exclusion).
- Instrument reference data (ISIN, exchange, kind) — stays out of the catalog.
- Changing the Buy / Sell or net-worth contracts. They already call `IMarketData`; this slice only supplies the implementation.

---

## 3. Stories

1. As the platform, I store daily reference prices for instruments that exist in any firm’s catalog so valuation and Buy/Sell do not call an external API on the request path.
2. As the worker, I refresh prices on a schedule so a new trading day’s prices appear without a deploy.
3. As a caller of `IMarketData`, I get the latest price on or before the requested as-of date, or empty when no row exists, so conversion is never silent.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Source is EODHD (`https://eodhd.com/api`). Free plan: 20 API calls/day, 20/min. API key via Aspire parameter, not in git. Use the End-of-Day endpoint. Symbols use `SYMBOL.EXCHANGE` form (e.g. `AIA.NZ`, `AAPL.US`). EODHD is the source of every instrument that no market data connection serves. An instrument whose symbol ends with the symbol suffix of a configured market data connection (for example `.SIM`) is **served** by that connection and gets its prices from it, through the port `IMarketPrices` and the `worker` job `market-data-sync:{connection}` (queue `market`, every `PollMinutes` of the connection) (simulated-instruments R4, R7–R13). `Price` is the unadjusted `close` of the EODHD bar; `adjusted_close` is never read, and a bar without a date or a `close`, or with a `close` that is not `> 0`, is skipped. With `MarketDataHistory:FloorDate` set on `worker`, the daily call per symbol asks with `from` = the later of the floor date and today minus `Eodhd:HistoryDays` (default 360), and the job stores every bar of the answer on or after that date; without the setting it asks for the latest bar (market-data-history R1–R11). The rows of one symbol group are stored in one transaction; a failure at one symbol leaves the symbols already stored in place. |
| R2 | Job runs **daily at 12:00 UTC** via Hangfire recurring job (`0 12 * * *`). Business logic lives in Application; the worker only hosts it. |
| R3 | Store one price per instrument per as-of date for instruments that are **active** in at least one tenant. Job reads the union of active `Instruments` on every run. Inactive instruments are not fetched; existing rows stay. |
| R4 | Price is in the instrument’s `QuoteCurrency`. Do not convert on write. Callers that need another currency use `IFxRate`. |
| R5 | Upsert key is `(InstrumentId, AsOfDate)`. `AsOfDate` is the date the source returns (previous working day on weekends / holidays). On each successful run, if the `AsOfDate` already exists for the instrument, update `Price` and `FetchedAt`; if it is a new date, insert. A row written from a connection has `AsOfDate` = the platform's UTC date at the run, never the date the party sent, so a simulator clock that runs ahead cannot date a price in the platform's future (ADR 0021 rule 11); each run of the day replaces today's row, and `Source` is the adapter name (`Simulator`). A row whose `AsOfDate` is two or more days before the platform's UTC date is **final**: it is never updated or deleted, and a missing one may still be inserted. A row dated today or yesterday is updated as above. This holds for `instrument-price-sync` and for `market-data-sync:{connection}`; `TestSeed` and `webapi --seed-demo` are not bound by it. |
| R6 | `IMarketData.TryGetPrice(instrumentId, asOf?)` returns the latest row with `AsOfDate` ≤ the UTC date of `asOf` (or today's UTC date if omitted). No row at all for that instrument → empty (`null`). Never invent 0. `IMarketData.TryGetPriceDetail(instrumentId, asOf)` returns the same row with its `AsOfDate` and `Source`. `IMarketData.ListPricesAsync` lists the stored rows of one instrument, newest first. |
| R7 | Prices are platform data. No `TenantId` on the price row. The same symbol in two tenants shares the same price (instrument rows are tenant-scoped; price is looked up by `InstrumentId`, so each tenant’s instrument has its own price rows even if the symbol matches). |
| R8 | On fetch failure the job logs and leaves the previous rows in place. It does not delete. |
| R9 | Read path never calls the external source. Only the job does. |
| R10 | Price precision is `decimal(18,8)`. |
| R11 | No in-memory cache (`IMemoryCache`) in this slice. The table is the source of truth. |
| R12 | **Read route.** `GET /instruments/{id}/prices` (`instruments.read`) lists the stored prices of an instrument the caller may read, newest first, with `from`, `to` and paging (market-data-history R19, R21–R23). The read path never calls a party. |
| R13 | Index on `FetchedAt` for “latest successful sync” observability. Index on `(InstrumentId, AsOfDate)` for the as-of lookup. |
| R14 | The job always runs. It calls the source only when some active instrument has no row whose `AsOfDate` is today’s UTC date. Once every active instrument has today’s source date, later runs that UTC day do not call the source. A run that stored an older source date still calls again later the same UTC day. On weekends and holidays the source date is not today, so the job still calls and upserts that `AsOfDate`. The job leaves out every served instrument: from the instruments it fetches and from its test whether every active instrument has today's date. No EODHD call is spent on a served symbol. |
| R15 | Same symbol across tenants is fetched once per run (by symbol + quote currency) and the price is written to every matching active `InstrumentId`. A connection's run also writes one price to every active instrument, in any Tenant, with the item's symbol and with `QuoteCurrency` equal to the item's currency. |

---

## 5. Domain

No new aggregate. `IMarketData` is an existing port (introduced in instruments). This slice supplies the infrastructure implementation.

| Type | Kind | Notes |
| --- | --- | --- |
| `IMarketData` | port | `TryGetPrice(instrumentId, asOf?) → Money?`. Missing → empty. Quote currency of the instrument. |

Invariants:

- A price row is infrastructure. It is not a domain entity.
- Buy / Sell and net-worth already require a price from this port. They do not change.

Domain events:

- None in this slice.

---

## 6. Database

| Table | Change | Index / FK |
| --- | --- | --- |
| `InstrumentPrices` | add | PK `(InstrumentId, AsOfDate)`; FK `InstrumentId` → `Instruments.Id` (RESTRICT); index on `FetchedAt` |

Script: `database/schema/0022_instrument_prices.sql`.

Columns (minimum):

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| InstrumentId | int | no | FK → Instruments.Id |
| Price | decimal(18,8) | no | In the instrument’s QuoteCurrency |
| AsOfDate | date | no | Date the source published |
| Source | nvarchar(50) | no | `'EODHD'` |
| FetchedAt | datetimeoffset | no | When the job wrote the row |

No PublicId. No RowVersion. No audit columns beyond `FetchedAt`. No seed in the script — the job populates it. Development / TestAppHost uses `TestSeed` with round, obviously-fake prices (see §11).

Update [database-design.md](../database-design.md) in the same change.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Job | `InstrumentPriceSyncJob` | void | Active instruments; source reachable; upsert |
| Query | `IMarketData.TryGetPrice` | Money? | Latest row with `AsOfDate` ≤ the UTC date of `asOf` (or today's UTC date if omitted); no row → empty |

---

## 8. API

None in this slice.

### 8.1 Bodies

n/a

### 8.2 Errors

n/a (no HTTP). Application-level missing price is `null` from the port. Buy / Sell already map empty price to 400.

---

## 9. UI

None in this slice. Pages live in docs/adviser-portal/ when a consumer needs to show a price.

---

## 10. Tests

The tests are the executable form of §4. Where they live:

| Project | Where |
| --- | --- |
| Application.UnitTests | Folder `MarketData/` |
| Application.FunctionalTests | `Instruments/GetInstrumentPricesTests.cs`; folder `MarketData/` |
| Infrastructure.IntegrationTests | Folder `MarketData/`; `AppliedSchema/InstrumentPrice*Tests.cs`, `MarketData*Tests.cs` |

No cross-tenant tests on the table (platform data).

---

## 11. Locked in this spec

Follows ADR 0019 pattern. No new ADR.

| Item | Lock |
| --- | --- |
| Scripts / policy | `0022_instrument_prices.sql`; no new policy |
| Caller | Worker job only for writes; any Application caller for `IMarketData` |
| Scope | Platform prices for active instruments (union across tenants); daily EOD; local read |
| Schedule | Daily at 12:00 UTC (`0 12 * * *`). Skip external call once every active instrument has today’s source date (R14). |
| Source | EODHD free plan (20 calls/day, 20/min). Key via Aspire parameter. EOD endpoint. One call per symbol, with `from`; the job stores every bar from that date. |
| Columns | Keep both `AsOfDate` (source date) and `FetchedAt` (when we wrote). On same `AsOfDate`, update `Price` + `FetchedAt`. |
| Cache | No `IMemoryCache` this slice |
| HTTP | Deferred. A read surface is expected later for Scalar / ops. Not in this slice; not a permanent exclusion. |
| Failure | Job logs and keeps previous rows. Read with zero rows for an instrument returns empty. |
| Missing price | Empty (`null`). Callers (Buy/Sell) already treat empty as 400. |
| Fetch | One request per symbol (or batched if the source supports it). Same symbol + quote currency across tenants is fetched once and written to every matching `InstrumentId`. |
| Index | Index on `FetchedAt`; index on `(InstrumentId, AsOfDate)` |
| TestSeed | Round, obviously-fake prices for the expanded demo set (several per seed currency). Disabled instrument has no price. |

### TestSeed instruments and prices (fixed date `2026-01-01`)

Demo catalog is expanded so each platform seed currency has several instruments. Existing instruments seed (VTI, AIA, NZD-CASH) stays; this slice (or the instruments TestSeed) adds the rows below. Disabled instruments get no price.

| Symbol | Name | QuoteCurrency | IsActive | Price |
| --- | --- | --- | --- | --- |
| VTI | Vanguard Total Stock Market ETF | USD | true | 250.00000000 |
| AAPL | Apple Inc. | USD | true | 180.00000000 |
| MSFT | Microsoft Corporation | USD | true | 400.00000000 |
| AIA | Auckland International Airport | NZD | true | 8.50000000 |
| FPH | Fisher & Paykel Healthcare | NZD | true | 30.00000000 |
| MEL | Meridian Energy | NZD | true | 5.50000000 |
| BHP | BHP Group | AUD | true | 45.00000000 |
| CBA | Commonwealth Bank of Australia | AUD | true | 130.00000000 |
| WOW | Woolworths Group | AUD | true | 35.00000000 |
| SAP | SAP SE | EUR | true | 180.00000000 |
| AIR | Airbus SE | EUR | true | 150.00000000 |
| OR | L'Oréal | EUR | true | 400.00000000 |
| SHEL | Shell plc | GBP | true | 28.00000000 |
| HSBA | HSBC Holdings | GBP | true | 7.00000000 |
| ULVR | Unilever | GBP | true | 45.00000000 |
| 7203 | Toyota Motor | JPY | true | 3000.00000000 |
| 6758 | Sony Group | JPY | true | 13000.00000000 |
| 9984 | SoftBank Group | JPY | true | 8000.00000000 |
| NZD-CASH | Demo disabled cash proxy | NZD | false | (no price) |

Obvious test values, not real market prices. Additional historical dates may be added in TestSeed for as-of tests. The rows above are the minimum deterministic set.

Locked with the implementation:

- Endpoint: `GET https://eodhd.com/api/eod/{SYMBOL.EXCHANGE}?api_token={key}&fmt=json`. One call per symbol returns the full history. The job takes the latest bar. Free plan limit is 20 calls/day; the daily run only calls when today's source date is missing (R14).
- `date` is `AsOfDate`. `close` is `Price`.
- Seed symbols use `SYMBOL.EXCHANGE`: `AIA.NZ`, `FPH.NZ`, `MEL.NZ`, `BHP.AU`, `CBA.AU`, `WOW.AU`, `SAP.XETRA`, `AIR.PA`, `OR.PA`, `SHEL.LSE`, `HSBA.LSE`, `ULVR.LSE`, `7203.TSE`, `6758.TSE`, `9984.TSE`, `VTI.US`, `AAPL.US`, `MSFT.US`. Any other symbol is sent as stored (caller must supply the exchange suffix if required).
- A per-symbol rejection (unknown symbol, or a source currency that is not the instrument quote currency) is logged and skipped. Other symbols in that run are still written. A transport failure (network, 401, 429, 5xx) logs and does not upsert.
- API key: Aspire parameter `eodhd-api-key`, passed only to the worker as `Eodhd__ApiKey`. Not in git.

Still open, not this slice:

- Net-worth FX fold (still later).
- Exact shape of the future HTTP read surface. Deferred until a consumer needs it.
- Real-time prices.

---

## 12. History

What changed in this file after it was accepted. The text of each change is in git (`git log -p -- docs/features/instrument-prices.md`).

| Date | With | Change here |
| --- | --- | --- |
| 2026-11-08 | — | Source switched from Twelve Data to EODHD; the job runs daily at 12:00 UTC |
| 2026-11-14 | [utc-instants](../fixes/utc-instants.md) | R6: the UTC date of `asOf` |
| 2026-11-22 | [simulated-instruments.md](simulated-instruments.md) | R1, R5, R14, R15: instruments served by a market data connection |
| 2026-11-25 | [market-data-history.md](market-data-history.md) | R1, R5, R6, R12, §11: history from the floor date, final rows, the price read route |
| 2026-11-26 | — | The changes above were folded into the body. No rule changed |
