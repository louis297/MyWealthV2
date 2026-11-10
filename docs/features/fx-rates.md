---
title: "FX Rates"
status: accepted
phase: 3
language: en
owner: ""
created: 2026-11-02
last_updated: 2026-11-11
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../architecture.md
  - ../adr/0004-money-as-decimal-with-currency.md
  - ../adr/0009-currencies-catalog.md
  - ../adr/0017-background-worker.md
  - ../adr/0019-fx-rate-source-and-refresh.md
  - currencies.md
---

# FX Rates

Platform FX rate cache. Hangfire hourly job pulls daily reference rates from Frankfurter for the active currency catalog and upserts them. `IFxRate` reads the local table. Decision: [ADR 0019](../adr/0019-fx-rate-source-and-refresh.md) (accepted). Adds script `0021_fx_rates.sql`. No new HTTP policy. No portal page.

**Status is `accepted`.** Implementation follows this file.

---

## 1. Summary

The worker fetches the latest Frankfurter rates every hour and stores directed pairs for the active platform currencies (seed: NZD, AUD, USD, EUR, GBP, JPY). Reads go through `IFxRate` against the table. Same currency returns 1. A missing pair fails. Rates are platform data, not tenant-scoped.

---

## 2. Scope

**In**

- Table `FxRates` (application schema).
- Hangfire recurring job `FxRateSyncJob` (hourly).
- Application port implementation of `IFxRate` that reads the table.
- Upsert by `(BaseCurrency, QuoteCurrency, AsOfDate)`.
- Fetch for the active currencies in `Currencies` (or the six seed codes if the catalog query is deferred).
- Tests for upsert, same-currency = 1, missing pair fails, and job behaviour on source failure.

**Out**

- Instrument prices / `IMarketData` (accepted separately: [instrument-prices.md](instrument-prices.md)).
- Net-worth FX fold (later; current net-worth stays per-currency).
- HTTP endpoint for rates (deferred; a read surface is expected later for Scalar / ops, not a permanent exclusion).
- Real-time or intraday rates.
- Per-tenant rate overrides.
- Historical back-fill beyond what the hourly job naturally accumulates.
- A second rate source or failover provider.

---

## 3. Stories

1. As the platform, I store daily reference rates for the seeded currencies so later valuation and conversion do not call an external API on the request path.
2. As the worker, I refresh rates every hour so a new trading day’s rates appear without a deploy.
3. As a caller of `IFxRate`, I get 1 for the same currency and a failure for a missing cross pair so conversion is never silent.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Source is Frankfurter (`https://api.frankfurter.dev`). No API key. |
| R2 | Job runs hourly via Hangfire recurring job, schedule in **UTC**. Business logic lives in Application; the worker only hosts it. |
| R3 | Store directed pairs among the **active** currencies in the platform catalog. Seed set is NZD, AUD, USD, EUR, GBP, JPY. Job reads `ICurrencyCatalog` active codes on every run. |
| R4 | Because Frankfurter supports arbitrary base + symbols and the pair count is small (6 currencies → 30 directed pairs), the job fetches the actual pair rates from the source. Do not derive from a single base in memory. Relative-to-USD is only a fallback if a future source cannot supply pairs. |
| R5 | Upsert key is `(BaseCurrency, QuoteCurrency, AsOfDate)`. `AsOfDate` is the date the source returns (previous working day on weekends / holidays). On each successful run, if the `AsOfDate` already exists for a pair, update `Rate` and `FetchedAt`; if it is a new date, insert. Keep both columns. |
| R6 | Same currency → `IFxRate` returns 1. No row is required. |
| R7 | Missing cross pair (no row at all for that pair) → throw `FxRateNotFoundException`. Never return 1. Callers do not catch and substitute. |
| R8 | Rates are platform data. No `TenantId`. |
| R9 | On fetch failure the job logs and leaves the previous rows in place. It does not delete. |
| R10 | Read path never calls Frankfurter. Only the job does. |
| R11 | Rate precision is `decimal(18,8)`. |
| R12 | Inactive currencies are not fetched. Existing rows for a currency that later becomes inactive stay. |
| R13 | No in-memory cache (`IMemoryCache`) in this slice. The table is the source of truth. |
| R14 | No HTTP read endpoint in this slice. A read surface (e.g. `GET /fx-rates`) is expected later for Scalar / ops; design it when a consumer needs it. Do not treat the absence as a permanent decision. |
| R15 | Index on `FetchedAt` for “latest successful sync” observability. |
| R16 | The hourly job always runs. It calls Frankfurter only when some active directed pair has no row whose `AsOfDate` is today's UTC date. Once every active pair has today's source date, later hours that UTC day do not call Frankfurter. A run that stored an older source date still calls again later the same UTC day. On weekends and holidays the source date is not today, so the job still calls each hour and upserts that `AsOfDate`. |

---

## 5. Domain

No new aggregate. `IFxRate` is an existing port (named in domain-model / architecture). This slice supplies the infrastructure implementation.

| Type | Kind | Notes |
| --- | --- | --- |
| `IFxRate` | port | `GetRate(from, to, asOf?)`. Same currency = 1. Missing pair → `FxRateNotFoundException`. `GetRateDetail(from, to, asOf)` (added 2026-11-11 for [fx-conversions.md](fx-conversions.md) R9, R14) returns the same row's rate with its `AsOfDate` and `Source`; the directed pair only, missing → `FxRateNotFoundException`. |

Invariants:

- A rate row is infrastructure. It is not a domain entity.
- Cross-currency arithmetic must go through the port. `Money` still forbids silent cross-currency add/subtract (ADR 0004).

Domain events:

- None in this slice.

Update [domain-model.md](../domain-model.md) when this spec is accepted (port implementation note only).

---

## 6. Database

| Table | Change | Index / FK |
| --- | --- | --- |
| `FxRates` | add | PK `(BaseCurrency, QuoteCurrency, AsOfDate)`; FK both currency columns → `Currencies.Code` (RESTRICT); index on `FetchedAt` |

Script: `database/schema/0021_fx_rates.sql`.

Columns (minimum):

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| BaseCurrency | char(3) | no | FK → Currencies.Code |
| QuoteCurrency | char(3) | no | FK → Currencies.Code |
| Rate | decimal(18,8) | no | How many QuoteCurrency per 1 BaseCurrency |
| AsOfDate | date | no | Date the source published |
| Source | nvarchar(50) | no | `'Frankfurter'` |
| FetchedAt | datetimeoffset | no | When the job wrote the row |

No PublicId. No RowVersion. No audit columns beyond `FetchedAt`. No seed in the script — the job populates it. Development / TestAppHost uses `TestSeed` with round, obviously-fake rates (see §11).

Update [database-design.md](../database-design.md) in the same change.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Job | `FxRateSyncJob` | void | Active currencies; Frankfurter reachable; upsert |
| Query | `IFxRate.GetRate` | decimal | Same currency = 1; else latest row with `AsOfDate` ≤ requested date (or today if omitted); no row at all for the pair → `FxRateNotFoundException` |

---

## 8. API

None in this slice.

### 8.1 Bodies

n/a

### 8.2 Errors

n/a (no HTTP). Application-level failure on missing pair is a domain / application exception, not an HTTP `code` / `target` pair.

---

## 9. UI

None in this slice. Pages live in docs/adviser-portal/ when a consumer needs to show a converted figure.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Application.UnitTests | Same currency returns 1; missing pair fails; rate selection by as-of date |
| Infrastructure.IntegrationTests | Script applies; FK rejects unknown currency; upsert by key |
| Worker / job | Fetch failure leaves previous rows; successful fetch writes expected pairs |

No cross-tenant tests (platform data).

---

## 11. Locked in this spec

New ADR: `adr/0019-fx-rate-source-and-refresh.md`

| Item | Lock |
| --- | --- |
| Scripts / policy | `0021_fx_rates.sql`; no new policy |
| Caller | Worker job only for writes; any Application caller for `IFxRate` |
| Scope | Platform pairs for active catalog currencies; hourly refresh (UTC); local read |
| Schedule | Hourly, UTC |
| Fetch strategy | Fetch actual directed pairs from Frankfurter (pair count is small). Do not derive from a single base. |
| Catalog | Job reads `ICurrencyCatalog` active codes on every run |
| Columns | Keep both `AsOfDate` (source date) and `FetchedAt` (when we wrote). On same `AsOfDate`, update `Rate` + `FetchedAt`. |
| Cache | No `IMemoryCache` this slice |
| HTTP | Deferred. A read surface (e.g. `GET /fx-rates`) is expected later for Scalar / ops. Not in this slice; not a permanent exclusion. |
| Failure | Job logs and keeps previous rows. Read with zero rows for a pair throws `FxRateNotFoundException`. |
| Exception | `FxRateNotFoundException` (Application). Carries base, quote, and the as-of date that was requested. |
| Fetch | One Frankfurter call per base (`/v2/rates?base=X&quotes=...`). 6 currencies → 6 calls. Batch symbols per base; do not issue one request per pair unless the endpoint forces it. |
| Index | Index on `FetchedAt` |
| TestSeed | Round, obviously-fake rates (below) |

### TestSeed rates (fixed date `2026-01-01`)

Obvious test values, not real market rates:

| Base | Quote | Rate |
| --- | --- | --- |
| USD | NZD | 1.50000000 |
| USD | AUD | 1.40000000 |
| USD | EUR | 0.90000000 |
| USD | GBP | 0.80000000 |
| USD | JPY | 150.00000000 |
| NZD | USD | 0.66666667 |
| AUD | USD | 0.71428571 |
| EUR | USD | 1.11111111 |
| GBP | USD | 1.25000000 |
| JPY | USD | 0.00666667 |

(Additional pairs among NZD/AUD/EUR/GBP/JPY may be derived in TestSeed for completeness, or left for the job. The values above are the minimum deterministic set.)

Still open, not this slice:

- Net-worth fold and instrument price consumption. Later features.
- Exact shape of the future HTTP read surface (route, auth, envelope). Deferred until a consumer needs it.

---

## 12. Suggested commits

The repository should build after each commit. Agents split each line into red then green.

1. Script `0021_fx_rates.sql` + EF mapping + integration test that the table exists and the FK rejects an unknown code.
2. `IFxRate` infrastructure implementation that reads `FxRates` (same currency = 1; missing fails) + unit tests.
3. `FxRateSyncJob` + Hangfire recurring registration + test that a failed fetch leaves previous rows.
4. Documentation updates: database-design.md, architecture.md port note, domain-model.md port note, features/README.md, adr/README.md.
