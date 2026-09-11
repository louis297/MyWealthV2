---
title: "ADR 0009: Platform currency catalog"
status: accepted
language: en
date: 2026-08-31
updated: 2026-09-12
related:
  - 0004-money-as-decimal-with-currency.md
  - ../database-design.md
  - ../api-design.md
---

# ADR 0009: Platform currency catalog

Status: accepted

Ships in Phase 1.

## Context

Phase 1 already needs a supported-currency list, and later account / quote / reporting currencies need a place to attach. An enum puts truth back in code and cannot carry decimal places cleanly.

## Decision

- Platform table `Currencies` (`Code` PK, `Name`, `DecimalPlaces`, `IsEnabled`). No PublicId. No RowVersion.
- Currency columns are `char(3)` with an **FK** to `Currencies.Code`. The only Phase-1 consumer is `Tenants.ReportingCurrency`.
- In-process `ICurrencyCatalog`. The hot path does not JOIN.
- Phase 1: read-only `GET /currencies`. No per-tenant allow-list, no FX, no `IFxRate` port.
- Seed: NZD, AUD, USD, EUR, GBP, JPY.
- A disabled currency cannot be used as a **new** ReportingCurrency. Tenants that already reference it keep the historical value. Phase 1 may change a tenant’s reporting currency (no ledger balances are keyed on it yet).

## Alternatives considered

| Option | Why not |
| --- | --- |
| C# enum | Not stored as ISO; adding a currency requires a release |
| Application validation only, no FK | Conflicts with schema-first |
| Introduce FX in Phase 1 | Port surface is not locked; adding it later does not rewrite Phase 1 |

## Consequences

Adding a currency is an insert plus a catalog refresh. Money representation stays ADR 0004.
