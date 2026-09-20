---
title: "ADR 0010: Instrument is an in-database catalog"
status: accepted
phase: 2
language: en
date: 2026-08-31
updated: 2026-09-20
related:
  - 0009-currencies-catalog.md
  - 0011-ledger-cash-holdings-reversal.md
  - 0013-roles-authorization-single-user-table.md
  - ../function-plan.md
  - ../domain-model.md
  - ../features/instruments.md
---

# ADR 0010: Instrument is an in-database catalog

Status: accepted

Feature contract: [features/instruments.md](../features/instruments.md). This ADR records why the catalog looks like this. Do not implement from this file alone.

## Context

Free-text name/symbol on a holding cannot be de-duplicated and cannot attach a quote. The catalog is a tenant table so an Adviser can create rows without a platform master-data team.

## Decision

- `Instruments` is the first Phase-2 ledger table (`0010_instruments.sql`).
- **Tenant catalog.** A holding (later) stores `InstrumentId` only.
- `Symbol` is the instrument code (unique CI per tenant, including disabled). `Name` is the display name (duplicates allowed).
- No `Isin`, `Exchange`, `InstrumentKind`, or price column in this slice. Price lives on `IMarketData`, not on the row.
- An Adviser may create and read. Update / disable / enable is TenantAdmin or SystemAdmin. Customer has no policy. SystemAdmin list/create takes the tenant PublicId.
- `QuoteCurrency` is immutable after create. Cost currency equals quote currency (no second column).
- `IMarketData.TryGetPrice` and `IFxRate.GetRate` ship with this slice, mocked. Same-currency FX is 1. Cross-currency is never treated as 1.

## Alternatives considered

| Option | Why not |
| --- | --- |
| Keep free-text symbols | No mark-to-market, no corporate actions |
| Global ISIN master data | Not needed for mock prices keyed by `InstrumentId` |
| Platform-level catalog | Harder isolation and “Adviser may create” |
| Price column on `Instruments` | Second write path; prices change; conflicts with `IMarketData` |
| ISIN / Exchange now | Optional later ALTER; this slice does not consume them |
| SystemAdmin 403 on ledger APIs | Back Office / Scalar must manage any tenant |

## Consequences

Instruments come before accounts and holdings. Later slices must not store a free-text symbol on a holding. Live vendor adapters wait for a later phase; the port signatures stay.
