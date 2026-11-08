---
title: "Instrument Prices"
status: accepted
phase: 3
language: en
owner: ""
created: 2026-11-02
last_updated: 2026-11-08
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
- Hangfire recurring job `InstrumentPriceSyncJob` (hourly).
- Application port implementation of `IMarketData` that reads the table.
- Upsert by `(InstrumentId, AsOfDate)`.
- Fetch for active instruments across all tenants (union of symbols).
- Tests for upsert, as-of selection, missing price returns empty, and job behaviour on source failure.

**Out**

- Real-time or intraday prices.
- Per-tenant price overrides.
- Historical back-fill beyond what the job naturally accumulates.
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
| R1 | Source is EODHD (`https://eodhd.com/api`). Free plan: 20 API calls/day, 20/min. API key via Aspire parameter, not in git. Use the End-of-Day endpoint. Symbols use `SYMBOL.EXCHANGE` form (e.g. `AIA.NZ`, `AAPL.US`). |
| R2 | Job runs **hourly** via Hangfire recurring job, schedule in **UTC**. Business logic lives in Application; the worker only hosts it. |
| R3 | Store one price per instrument per as-of date for instruments that are **active** in at least one tenant. Job reads the union of active `Instruments` on every run. Inactive instruments are not fetched; existing rows stay. |
| R4 | Price is in the instrument’s `QuoteCurrency`. Do not convert on write. Callers that need another currency use `IFxRate`. |
| R5 | Upsert key is `(InstrumentId, AsOfDate)`. `AsOfDate` is the date the source returns (previous working day on weekends / holidays). On each successful run, if the `AsOfDate` already exists for the instrument, update `Price` and `FetchedAt`; if it is a new date, insert. |
| R6 | `IMarketData.TryGetPrice(instrumentId, asOf?)` returns the latest row with `AsOfDate` ≤ the requested date (or today if omitted). No row at all for that instrument → empty (`null`). Never invent 0. |
| R7 | Prices are platform data. No `TenantId` on the price row. The same symbol in two tenants shares the same price (instrument rows are tenant-scoped; price is looked up by `InstrumentId`, so each tenant’s instrument has its own price rows even if the symbol matches). |
| R8 | On fetch failure the job logs and leaves the previous rows in place. It does not delete. |
| R9 | Read path never calls the external source. Only the job does. |
| R10 | Price precision is `decimal(18,8)`. |
| R11 | No in-memory cache (`IMemoryCache`) in this slice. The table is the source of truth. |
| R12 | No HTTP read endpoint in this slice. A read surface (e.g. `GET /instrument-prices`) is expected later for Scalar / ops; design it when a consumer needs it. Do not treat the absence as a permanent decision. |
| R13 | Index on `FetchedAt` for “latest successful sync” observability. Index on `(InstrumentId, AsOfDate)` for the as-of lookup. |
| R14 | The job always runs. It calls the source only when some active instrument has no row whose `AsOfDate` is today’s UTC date. Once every active instrument has today’s source date, later runs that UTC day do not call the source. A run that stored an older source date still calls again later the same UTC day. On weekends and holidays the source date is not today, so the job still calls and upserts that `AsOfDate`. |
| R15 | Same symbol across tenants is fetched once per run (by symbol + quote currency) and the price is written to every matching active `InstrumentId`. |

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
| Query | `IMarketData.TryGetPrice` | Money? | Latest row with `AsOfDate` ≤ requested date (or today if omitted); no row → empty |

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

| Project | Assert |
| --- | --- |
| Application.UnitTests | As-of selection; missing price returns empty; same-day upsert updates price + FetchedAt |
| Infrastructure.IntegrationTests | Script applies; FK rejects unknown instrument; upsert by key |
| Worker / job | Fetch failure leaves previous rows; successful fetch writes expected instruments; skip call once today’s date is stored for all active instruments |

No cross-tenant tests on the price table itself (platform data). Isolation remains on the instrument catalog.

---

## 11. Locked in this spec

Follows ADR 0019 pattern. No new ADR.

| Item | Lock |
| --- | --- |
| Scripts / policy | `0022_instrument_prices.sql`; no new policy |
| Caller | Worker job only for writes; any Application caller for `IMarketData` |
| Scope | Platform prices for active instruments (union across tenants); daily EOD; local read |
| Schedule | Hourly, UTC. Skip external call once every active instrument has today’s source date (R14). |
| Source | EODHD free plan (20 calls/day, 20/min). Key via Aspire parameter. EOD endpoint. One call returns full history for one symbol. |
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

- Endpoint: `GET https://eodhd.com/api/eod/{SYMBOL.EXCHANGE}?api_token={key}&fmt=json`. One call per symbol returns the full history. The job takes the latest bar. Free plan limit is 20 calls/day; hourly runs only call when today's source date is missing (R14).
- `date` is `AsOfDate`. `close` is `Price`.
- Seed symbols use `SYMBOL.EXCHANGE`: `AIA.NZ`, `FPH.NZ`, `MEL.NZ`, `BHP.AU`, `CBA.AU`, `WOW.AU`, `SAP.XETRA`, `AIR.PA`, `OR.PA`, `SHEL.LSE`, `HSBA.LSE`, `ULVR.LSE`, `7203.TSE`, `6758.TSE`, `9984.TSE`, `VTI.US`, `AAPL.US`, `MSFT.US`. Any other symbol is sent as stored (caller must supply the exchange suffix if required).
- A per-symbol rejection (unknown symbol, or a source currency that is not the instrument quote currency) is logged and skipped. Other symbols in that run are still written. A transport failure (network, 401, 429, 5xx) logs and does not upsert.
- API key: Aspire parameter `eodhd-api-key`, passed only to the worker as `Eodhd__ApiKey`. Not in git.

Still open, not this slice:

- Net-worth FX fold (still later).
- Exact shape of the future HTTP read surface. Deferred until a consumer needs it.
- Real-time prices.

---

## 12. Suggested commits

1. Failing tests for `IMarketData` as-of selection and missing → empty.
2. Script `0022_instrument_prices.sql` + EF mapping + integration test.
3. `InstrumentPriceSyncJob` + source client (or stub) + skip-if-today-stored.
4. Wire `IMarketData` to the table (composition root used by the worker and webapi).
5. TestSeed fake prices for VTI / AIA.
6. Docs: this file to accepted, database-design, architecture, function-plan, domain-model port note.

---

## Change summary

2026-11-02 — Accepted. Local EOD price cache for active instruments, same pattern as FX rates. `IMarketData` reads the table. Source is Twelve Data free Basic. Job is hourly with skip-if-today-stored. Demo catalog expanded to several instruments per seed currency.

2026-11-02 — Landed. Script `0022_instrument_prices.sql`. Twelve Data `GET /eod`, hourly Hangfire job `instrument-price-sync`, `IMarketData` reads `InstrumentPrices`. TestSeed writes the fake catalog prices.

2026-11-08 — Source switched from Twelve Data to EODHD. Free plan provides global EOD coverage (US free tier on Twelve Data was insufficient for NZ/AU/UK/EU/JP seeds). Endpoint `GET /api/eod/{SYMBOL.EXCHANGE}`. Source column value `'EODHD'`. Aspire parameter `eodhd-api-key` (`Eodhd__ApiKey`). One call = one symbol full history; the job stores the latest bar. Seed symbols use the locked `SYMBOL.EXCHANGE` map. Landed.
