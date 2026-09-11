---
title: "ADR 0004: Money is decimal plus a currency"
status: accepted
language: en
date: 2026-08-18
updated: 2026-09-12
related:
  - 0009-currencies-catalog.md
  - ../domain-model.md
---

# ADR 0004: Money is decimal plus a currency

Status: accepted

The currency catalog is ADR 0009.

## Context

Amounts must not use floating point. Phase 1 has no balance column. If the ledger invented a second amount type, Phase-1 Domain would have to change.

## Decision

- Domain has a `Money` value object: `decimal` amount + a three-letter ISO currency. Equality is currency plus amount. Cross-currency add/subtract is forbidden.
- When money columns exist (Phase 2), store `decimal(18,4)` + `char(3)` FK to `Currencies`.
- Do not take a third-party Money library in Phase 1. Decimal places come from `Currencies.DecimalPlaces`.
- **`Money` lives in the Domain assembly in Phase 1 even though there is no balance column yet.**

## Alternatives considered

| Option | Why not |
| --- | --- |
| `double` / `float` | Forbidden |
| Define Money only when the ledger starts | Would force a Phase-1 Domain change |
| A mature Money library | Too much surface for Phase 1 |

## Consequences

Callers must align currencies before arithmetic. Cross-currency work goes through the FX port; a rate of 1 is never assumed. The hot path reads the `char(3)` on the row and does not JOIN the catalog.
