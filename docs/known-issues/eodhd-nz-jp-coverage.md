---
title: "Known issue — EODHD does not cover NZX or Tokyo"
status: open
language: en
created: 2026-11-08
updated: 2026-11-08
related:
  - ../features/instrument-prices.md
  - ../adr/0010-instrument-catalog.md
---

# Known issue: EODHD does not cover NZX or Tokyo

**Status: open.** Demo continues without real EOD prices for the NZD and JPY seed instruments. Do not treat this as a permanent product decision.

## Problem

Instrument prices switched from Twelve Data to EODHD (see [features/instrument-prices.md](../features/instrument-prices.md)). EODHD’s free plan covers global end-of-day data, which fixed the US-only limit of Twelve Data free Basic.

The public supported-exchanges list (60+ venues) includes `US`, `AU`, `LSE`, `XETRA`, `PA` and others. It does **not** list New Zealand (`NZ` / `NZX` / `XNZE`) or Tokyo (`T` / `TSE` / `XTKS`).

Public financial-summary pages for the seed symbols return 404:

- `AIA.NZ`, `AIA.NZX`, `AIA.XNZE`, `FPH.NZ`, `MEL.NZ`
- `7203.T`, `7203.TSE`, `7203.XTKS`, `6758.T`, `9984.T`

The locked seed map in the instrument-prices spec therefore cannot be resolved for those rows. The job logs a per-symbol rejection and skips them. TestSeed fake prices stay in place. Dual-listed ASX rows (for example `AIA.AU`) are AUD, not the instrument’s NZD quote currency, so the currency check rejects them.

## Current behaviour

| Seed | QuoteCurrency | EODHD attempt | Result |
| --- | --- | --- | --- |
| AIA, FPH, MEL | NZD | `*.NZ` / `*.NZX` | Not found. Skipped. |
| 7203, 6758, 9984 | JPY | `*.T` / `*.TSE` / `*.XTKS` | Not found. Skipped. |
| VTI, AAPL, MSFT | USD | `*.US` | Works. |
| BHP, CBA, WOW | AUD | `*.AU` | Works. |
| SAP | EUR | `*.XETRA` | Works. |
| AIR, OR | EUR | `*.PA` | Works. |
| SHEL, HSBA, ULVR | GBP | `*.LSE` | Works. |

Instruments stay tenant-scoped. No schema change. No new exchange column in this issue.

## What we are not doing now

- Adding an `Exchange` column or a venue catalog.
- Replacing the NZ/JP demo instruments with other listings.
- Calling a second price source for the missing markets.
- Treating an ASX dual listing as the NZD price.

## Revisit when

- EODHD adds NZX or Tokyo to `/exchanges-list` (confirm with a real key, not only the public page), or
- We pick a different source that covers those markets, or
- The demo set is changed to instruments EODHD already covers.

Until then, NZD and JPY seed instruments have no real EOD price. Valuation and Buy/Sell for those instruments continue to see empty from `IMarketData` unless a TestSeed row is present.
