---
title: "ADR 0011: Ledger direction and reversals"
status: accepted
phase: 2
language: en
date: 2026-08-31
updated: 2026-09-25
related:
  - 0010-instrument-catalog.md
  - ../function-plan.md
  - ../domain-model.md
---

# ADR 0011: Ledger direction and reversals

Status: accepted (Phase 2 direction)

**Phase 2 ledger discussion.** Phase 1 does not implement this, create tables, or register ledger policies.

Storage shape for **holdings / security legs / Opening of quantity** is still reviewed when that slice opens. Cash booking is locked in [features/posting.md](../features/posting.md) (accepted 2026-09-25): `Transaction` header + `TransactionCashLeg`, not a wide row that is both cash and security.

Phase 2 is open. Instruments and Accounts are landed. Posting is accepted, not yet landed.

## Context

A single `Type` column that pretends to be both cash and security, plus hand-edited holdings, collapses when a real ledger starts. Updating or deleting an original posting destroys the audit trail.

## Decision (direction, not a Phase-1 invariant)

1. Cash and security are **two legs**, not two values of one `Type` column. A buy/sell has a cash leg and a security leg. Transfer / interest / dividend may be cash-only. Split / bonus is security-only: cash 0, total cost unchanged.
2. **Do not create tables while storage is unlocked.** Holdings and security-leg tables wait for their spec. `Transactions` + `TransactionCashLegs` + the idempotency table ship with posting (`0013_transactions.sql`). There is no separate Journal / CashLedger table.
3. A posted entry is not `UPDATE`d or `DELETE`d. Correction tends toward a **full reversal**: a new posting points at the original. Reversal is not a separate Feature slice; it lives in the posting domain.
4. After posting: non-Credit cash must not go negative. Credit cash may be negative (liability).
5. Day-to-day quantity / cost edits are forbidden. The only direct write of quantity and cost is called Opening (shape not locked).
6. An account is a container under a Customer. `Account.Currency` is the cash-book currency and is immutable after open. `Account.Type` is immutable after open. Phase 2 selectable types: Bank, Cash, Brokerage, Other. `Status` is Open / Closed; `IsActive` is derived (`Open` → 1). HTTP close / reopen. Type does not replace the cash book. Field rules: [features/accounts.md](../features/accounts.md).
7. Net worth is an array per currency. Do not FX-fold to one number. Closed accounts are excluded. Credit is a liability.
8. A Customer who can obtain a token still has no ledger-write policy.

## Alternatives considered

| Option | Why not |
| --- | --- |
| Ship a simplified Transactions table in Phase 1 and split later | Breaks “once, not twice”: today’s design would have to change |
| Empty ledger tables in Phase 1 | Shape is not locked |
| `UPDATE` the original posting | No audit trail |
| Keep hand-edited holdings | Two write paths |

## Consequences

Phase-1 Domain defines `Money`. Instruments and Accounts shipped. Posting / cash book accepted as Transaction + cash leg; implement from [features/posting.md](../features/posting.md). Next design: holdings / security legs → net worth.
