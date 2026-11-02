---
title: "ADR 0019: FX rate source and refresh"
status: proposed
language: en
date: 2026-11-02
updated: 2026-11-02
related:
  - 0004-money-as-decimal-with-currency.md
  - 0009-currencies-catalog.md
  - 0017-background-worker.md
  - ../features/currencies.md
  - ../features/fx-rates.md
---

# ADR 0019: FX rate source and refresh

Status: accepted

## Context

ADR 0009 and [features/currencies.md](../features/currencies.md) explicitly deferred FX. Net-worth is currently per-currency only (no fold). Domain already names the port `IFxRate` (same currency = 1; missing cross pair fails). Before any consumer (net-worth fold, buy/sell cash conversion, reports) can use real rates, the platform needs a durable source, a storage shape, and a refresh rule.

The platform currency catalog is small and stable (seed: NZD, AUD, USD, EUR, GBP, JPY). Rates are reference rates for valuation and conversion, not live trading quotes.

## Decision

- **Source**: Frankfurter (`https://api.frankfurter.dev`). Daily reference rates from central banks. No API key. Free for commercial use of its blended rates.
- **Refresh**: Hourly Hangfire recurring job, schedule in UTC. Each run fetches the latest rates and upserts by as-of date. No real-time feed.
- **Storage model**: Directed pairs among the **active** currencies in the platform catalog. Because Frankfurter supports arbitrary base + symbols and the pair count is small, the job fetches the actual pair rates. Store one row per ordered pair per as-of date. Relative-to-USD is the fallback only if a future source cannot supply arbitrary pairs.
- **Columns**: Keep both `AsOfDate` (the date the source published) and `FetchedAt` (when the job wrote the row). On a repeated run for the same `AsOfDate`, update `Rate` and `FetchedAt`.
- **Port**: `IFxRate` is implemented against the local table (not a live HTTP call on the read path). Same currency returns 1. Missing pair fails (does not silently return 1).
- **Platform data**: Rates are not tenant-scoped. One shared table.

## Alternatives considered

| Option | Why not |
| --- | --- |
| Live call on every `IFxRate` read | Rate limits, latency, and inconsistent rates across a single request |
| Store only relative to USD | Works, but user preference and Frankfurter capability favour explicit pairs for the small catalog |
| Alpha Vantage / paid FX | Unnecessary for daily reference rates; free tier is tighter |
| No local table (cache only) | Loses auditability and multi-instance consistency |
| Real-time / intraday feed | Out of scope for reference valuation; daily is sufficient |

## Consequences

- A new table and a Hangfire job. Script number is `0021_fx_rates.sql`.
- `IFxRate` can be wired once the table exists. Consumers (net-worth fold, instrument prices) remain later slices.
- Weekends and holidays store the as-of date the source returns (previous working day).
- Growing the currency catalog later requires the job to include the new code (or a config list).
- Production still uses the same source; no separate “dev” rate provider.

## Links

- Feature spec: [features/fx-rates.md](../features/fx-rates.md)
- Related ADRs: 0004, 0009, 0017
