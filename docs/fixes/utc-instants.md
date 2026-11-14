---
title: Fix - UTC instants on the wire, in storage and for business dates
status: review
language: en
created: 2026-11-14
updated: 2026-11-14
related:
  - ../api-design.md
  - ../database-design.md
  - ../features/posting.md
  - ../features/fx-conversions.md
  - ../features/buy-sell.md
  - ../features/fx-rates.md
  - ../features/instrument-prices.md
  - ../features/reports.md
  - ../adviser-portal/frontend-conventions.md
  - ../customer-portal/customer-portal.md
---

# Fix: UTC instants on the wire, in storage and for business dates

Status: review. Not a build contract until it is `accepted`.

## Problem

1. **The portals send `bookedAt` with no offset.** The Adviser Portal posts the raw `<input type="datetime-local">` value, for example `"2026-11-14T16:13"`. `System.Text.Json` reads a value with no offset in the **server's** local time zone. Production containers run in UTC, so a wall-clock time typed in New Zealand is stored as `+00:00` and the instant is 13 hours late. Development and test machines run in New Zealand, so the bug does not show there.
2. **The stored offset is whatever the request carried.** [posting](../features/posting.md) R24 says "offset stored". `Transactions.BookedAt` can hold `+13:00`, `+12:00` or `+00:00` for rows that are otherwise alike. `TransactionDto` echoes that offset back.
3. **Business dates follow the request offset.** FX rate lookup, price lookup and the FX staleness check take the calendar date of `bookedAt` in its own offset (`DateOnly.FromDateTime(bookedAt.DateTime)`). The same instant sent as `+13:00` and as `Z` can read different rates. Reports already use the UTC day.
4. **The idempotency hash includes the offset.** The same instant with a different offset gives a different hash, so a retry that changes only the offset gets 409.

## Decision

1. **The server works in UTC only.** Every instant it reads from a request is converted to UTC (offset `+00:00`) before Application sees it. Every instant it stores has offset `+00:00`.
2. **An instant without an offset is rejected.** A request instant must carry `Z` or an explicit offset. A value with no offset, a date-only value, or a value that is not an ISO 8601 date and time → 400 with `errors.<Field>` (for example `errors.BookedAt`) and the message `Send an ISO 8601 date and time with Z or an offset, for example 2026-11-14T03:13:00Z.` Nothing is written. A JSON `null` is not an instant; the field's own required rule still answers it.
3. **The API returns every instant in UTC with `Z`.** Format `yyyy-MM-ddTHH:mm:ssZ`, with up to seven fraction digits only when they are not zero (`2026-11-14T03:13:00Z`, `2026-11-14T03:13:00.5Z`). This applies to `bookedAt`, `created`, `asOf`, `lastRebalanceAt` and every other `DateTimeOffset` in a response.
4. **The business date is the UTC calendar date.** Where a rule needs "the date of" an instant, it uses the UTC date. That covers [fx-conversions](../features/fx-conversions.md) R9 and R10, [buy-sell](../features/buy-sell.md) R10, the `asOf` date of `IFxRate.GetRate` / `GetRateDetail` ([fx-rates](../features/fx-rates.md)) and of `IMarketData.TryGetPrice` ([instrument-prices](../features/instrument-prices.md) R6). [Reports](../features/reports.md) already filter by UTC day; that stays.
5. **Date-only values are not instants.** `rateAsOfDate`, `AsOfDate`, report `from` / `to` and similar `date` values stay `yyyy-MM-dd` strings. They carry no time zone and are not converted.
6. **The portals convert at the edge.** A portal converts a `datetime-local` value from browser local time to a UTC ISO string before it sends it, and shows an instant from the API in browser local time. It never parses a date-only value as a `Date`.
7. **Existing rows keep their instant.** Script `0029` rewrites every `Transactions.BookedAt` with a non-zero offset to the same instant at `+00:00`.

### Where each rule lives

| Rule | Where |
| --- | --- |
| Reject an instant without an offset; convert to UTC on read | `webapi` JSON options: one `DateTimeOffset` converter for every request body (it also covers `DateTimeOffset?`) |
| Write `Z` | The same converter, for every response body |
| 400 with `errors.<Field>` | `ProblemDetailsExceptionHandler` maps the converter's error to a `ValidationProblemDetails`. The key is the JSON path without `$.`, first letter upper-case (`$.bookedAt` → `BookedAt`), the same key the required rule uses |
| Same 400 in every environment | `RouteHandlerOptions.ThrowOnBadRequest = true` in every environment. Without it, Production answers a body that cannot be read with an empty 400 while Development throws |
| Other unreadable bodies | Any other `BadHttpRequestException` → `ProblemDetails` with its own status code (400 or 415), title `Bad request`. Status codes do not change |
| Stored offset is `+00:00` | Every `Transaction` factory stores `bookedAt.ToUniversalTime()`. This also covers callers that do not come through HTTP: seeds, reversal, rebalance |
| `TransactionDto.bookedAt` | A `DateTimeOffset`, written by the converter. The hand-made string format (`yyyy-MM-ddTHH:mm:sszzz`) is removed |
| UTC business date | `FxConversionPricing.DateOf`, `FxRateStore` and `InstrumentPriceStore` take `DateOnly.FromDateTime(value.UtcDateTime)` |
| Idempotency hash | Unchanged code. It hashes the converted value, so the same instant with any offset gives the same hash |

### Script `0029_transactions_booked_at_utc.sql`

```sql
UPDATE [Transactions]
SET [BookedAt] = SWITCHOFFSET([BookedAt], '+00:00')
WHERE DATEPART(TZOFFSET, [BookedAt]) <> 0;
```

- `SWITCHOFFSET` keeps the instant. Only the stored offset changes. Ordering and the `(TenantId, BookedAt)` index are unaffected, because SQL Server compares `datetimeoffset` by instant.
- The schema applicator runs the script without a surrounding transaction. A single `UPDATE` is atomic, and the `WHERE` makes a rerun change nothing.
- Rows already at `+00:00` are left as they are. Rows saved through the Adviser Portal before this fix hold a local wall-clock time recorded as `+00:00`. The true offset cannot be recovered, so `0029` does not guess. Correct them by hand if needed.
- Column types do not change. `datetimeoffset` stays.

### Portal helpers

| Portal | Helper | Behaviour |
| --- | --- | --- |
| Adviser Portal | `toUtcIso(datetimeLocal)` in `src/shared/time/instant.ts` | Accepts only `yyyy-MM-ddTHH:mm` with optional seconds and milliseconds, else throws. Returns `new Date(value).toISOString()` (browser local time → UTC). Used for every `bookedAt` sent, including the FX quote body |
| Adviser Portal, Customer Portal | `formatInstant(iso)` in `src/shared/time/instant.ts` | Returns browser local `yyyy-MM-dd HH:mm`, built from the local date parts, not `Intl`, so the output does not depend on the locale. An unparseable value is returned unchanged |

Portal tests pin the time zone to `Pacific/Auckland` in `vite.config.ts`, and one test checks that the pin holds. Date-only values keep their current string handling (`created.slice(0, 10)`, `rateLine`, report `from` / `to`).

## Out of scope

- Report schedule cron. It stays UTC with the existing help text ([adviser-portal-reports](../adviser-portal/adviser-portal-reports.md)).
- A tenant or user time zone setting.
- Changing column types to `datetime2`.
- `DateTimeOffset.UtcNow` in the domain event timestamps of the three create handlers. It is already UTC.
- Rewriting existing `+00:00` rows that hold a local wall-clock time.
- Back Office. It sends and shows no instants today.

## Behaviours

The tree compiles after every commit. Each behaviour is a red commit and a green commit. Before B1, one test-only commit rewrites the `+12:00` / `+13:00` literals in functional test bodies to the same digits with `Z`, so that the date rule change in B4 does not change what those tests prove. `CreateTransactionTests` keeps an offset, because it proves the conversion.

### B1. Responses carry UTC instants with `Z`

Functional: a transaction posted with `bookedAt` `2026-09-24T09:00:00+12:00` reads back as `2026-09-23T21:00:00Z` from `GET /transactions/{id}` and from the list. An adviser's `created` and the net-worth `asOf` end in `Z`.

### B2. A request instant without an offset → 400

Functional, for `POST /transactions`, `/transactions/transfer`, `/transactions/fx` and `/transactions/fx/quote`:

- `bookedAt` `2026-11-14T16:13` → 400, `errors.BookedAt` with the message in Decision 2, nothing written.
- `bookedAt` `yesterday` → the same 400.
- The same `Idempotency-Key` sent first with `2026-11-14T09:00:00+13:00` and then with `2026-11-13T20:00:00Z` → 201 both times, same id.
- In the Production environment, `RouteHandlerOptions.ThrowOnBadRequest` is true.

### B3. Every `Transaction` factory stores UTC

Domain unit: each factory given a `+13:00` `bookedAt` stores the same instant with offset zero. All factories, including Reversal.

### B4. The business date is the UTC date

- Application unit: `FxConversionPricing.DateOf(2026-11-14T01:00+13:00)` is `2026-11-13`. `IsStale` uses that date.
- Integration (`AppliedSchema`): `GetRateAsync` and `GetRateDetailAsync` for `2026-10-11T10:00+13:00` read the `2026-10-10` row. `2026-10-11T00:30Z` reads the `2026-10-11` row. Same two cases for `TryGetPrice`.

### B5. `0029` moves stored offsets to UTC

Integration (`AppliedSchema`): with every script before `0029` applied, insert one Transaction at `+13:00` and one at `+00:00`, then apply `0029`. Every `BookedAt` has offset zero, both instants are unchanged, and the `+00:00` row's `RowVersion` is unchanged.

### B6. The Adviser Portal sends UTC

Vitest: `toUtcIso("2026-11-14T16:13")` is `"2026-11-14T03:13:00.000Z"` under `Pacific/Auckland`, and a date-only value throws. Every page that posts `bookedAt` (create transaction, internal transfer, FX conversion and its quote, account detail) sends the UTC string.

### B7. The Adviser Portal shows instants in local time

Vitest: the transactions list and account detail show `2026-10-11 10:00` for `2026-10-10T21:00:00Z`. Report history shows `asOf` in local time.

### B8. The Customer Portal shows instants in local time

Vitest: account detail shows `bookedAt` in local time.

## Doc changes when this is accepted

- [posting](../features/posting.md): R24 and the `bookedAt` field row read "required instant with `Z` or an offset; stored and returned in UTC" and link this fix. Response examples use `Z`.
- [fx-conversions](../features/fx-conversions.md): R9 and R10 read "the UTC date of `bookedAt`". Response examples use `Z`.
- [buy-sell](../features/buy-sell.md): R10 reads the price for the UTC date of `bookedAt`. R18, and the matching rows in [split](../features/split.md) R16 and [holdings](../features/holdings.md) R18, follow posting R24.
- [fx-rates](../features/fx-rates.md) and [instrument-prices](../features/instrument-prices.md) R6: "requested date" is the UTC date of `asOf`.
- [reports](../features/reports.md): the `asOf` example uses `Z`. The "first cut" UTC-day note and the open item link this fix as the rule.
- [api-design.md](../api-design.md) §2: an Instants row (Decisions 2, 3 and 5). §4: the 400 for an instant without an offset.
- [database-design.md](../database-design.md) §3: `datetimeoffset` columns always hold `+00:00`. §9 script order: `0029`. Change log.
- [frontend-conventions](../adviser-portal/frontend-conventions.md) API section: `toUtcIso` on send, `formatInstant` on display, date-only values stay strings, tests pin the time zone.
- [customer-portal](../customer-portal/customer-portal.md): instants are shown with `formatInstant`.
- [glossary.md](../glossary.md): "Instant" and "Business date" (added with this fix).
