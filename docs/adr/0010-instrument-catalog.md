---
title: "ADR 0010: Instrument is an in-database catalog"
status: accepted
phase: 2
language: en
date: 2026-08-31
updated: 2026-09-12
related:
  - 0009-currencies-catalog.md
  - 0011-ledger-cash-holdings-reversal.md
  - ../function-plan.md
  - ../domain-model.md
---

# ADR 0010: Instrument is an in-database catalog

Status: accepted (Phase 2 direction)

**Phase 2 ledger, first slice.** Phase 1 does not create this table, expose an API, or introduce market-data ports.

When Phase 2 opens, review this ADR again and expand column-level design. Do not treat this file as an implementable Feature Spec.

## Context

Free-text name/symbol on a holding cannot be de-duplicated and cannot attach a quote. The catalog is not built in Phase 1, but “tenant catalog, not free text” is locked so Phase 2 does not have to change a holding model that never existed.

## Decision

- `Instruments` ships at the start of the Phase-2 ledger. Phase-1 scripts do not include this table.
- **Tenant catalog** (locked; no longer “platform vs tenant”).
- A holding stores `InstrumentId` only.
- An Adviser may create. Update / disable is TenantAdmin only. `QuoteCurrency` is immutable after create. Cost currency equals quote currency.
- `IMarketData` and `IFxRate` are introduced with this slice, mocked. Same-currency FX is 1. Cross-currency is never treated as 1. Phase 1 does not build these ports.

Column details (whether Isin / Exchange are reserved now, uniqueness, widths) are locked when the phase opens.

## Alternatives considered

| Option | Why not |
| --- | --- |
| Keep free-text symbols | No mark-to-market, no corporate actions |
| Global ISIN master data in Phase 1 | Outside the current phase |
| Platform-level catalog | Harder isolation and “Adviser may create”; tenant scope is locked |

## Consequences

Inside the ledger domain, instruments come before accounts and holdings. Nothing from this ADR enters Phase-1 scripts or policy names.
