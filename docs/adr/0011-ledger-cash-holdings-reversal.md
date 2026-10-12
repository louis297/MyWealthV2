---
title: "ADR 0011: Ledger direction and reversals"
status: accepted
phase: 2
language: en
date: 2026-08-31
updated: 2026-10-13
related:
  - 0010-instrument-catalog.md
  - ../function-plan.md
  - ../domain-model.md
---

# ADR 0011: Ledger direction and reversals

Status: accepted (Phase 2 direction)

**Phase 2 ledger discussion.** Phase 1 does not implement this, create tables, or register ledger policies.

Storage shape for holdings / security legs / Opening of quantity is locked in [features/holdings.md](../features/holdings.md) and landed on master `676e77a` (2026-10-11): `TransactionSecurityLegs` + `Holdings` projection, type `OpeningHoldings`, no cash leg on that type. Scripts `0014_holdings.sql` and `0015_transaction_type_opening_holdings.sql`. Buy / Sell / StockIn / StockOut are accepted and landed (`821c715`, `0016_transaction_type_stock_in_out.sql`) in [features/buy-sell.md](../features/buy-sell.md). Cash booking stays [features/posting.md](../features/posting.md).

Phase 2 is open. Instruments, Accounts, posting (`/transactions`, `0013`), and holdings Opening are landed.

## Context

A single `Type` column that pretends to be both cash and security, plus hand-edited holdings, collapses when a real ledger starts. Updating or deleting an original posting destroys the audit trail.

## Decision (direction, not a Phase-1 invariant)

1. Cash and security are **two legs**, not two values of one `Type` column. A buy/sell has a cash leg and a security leg. Transfer / interest / dividend may be cash-only. Split / Bonus / Scrip is security-only: no cash, total cost unchanged, client sends the new total quantity ([features/split.md](../features/split.md)).
2. Holdings and security-leg tables shipped with the holdings spec (`0014_holdings.sql`). `Transactions` + `TransactionCashLegs` + the idempotency table shipped with posting (`0013_transactions.sql`). There is no separate Journal / CashLedger table.
3. A posted entry is not `UPDATE`d or `DELETE`d, **except** a single write of `ReversedByTransactionId` on the original when a Reversal is created (script `0018_transaction_reversed_by.sql`). After that write the original is immutable again. Economic legs are never updated. Correction is a **full reversal**: the new posting points at the original (`OriginalTransactionId`); the original points at the Reversal (`ReversedByTransactionId`). Reversal is not a separate Feature slice; it lives in the posting domain. Already reversed is `ReversedByTransactionId IS NOT NULL` — no self-join on list / get.
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
| `UPDATE` the original posting | No audit trail. The 2026-10-13 exception is only the single `ReversedByTransactionId` write; economic legs stay immutable |
| Keep hand-edited holdings | Two write paths |

## Consequences

Phase-1 Domain defines `Money`. Instruments, Accounts, posting, holdings Opening, Buy / Sell / StockIn / StockOut (`821c715`), and Split / Bonus / Scrip ([features/split.md](../features/split.md), `0017_transaction_type_split.sql`) have landed. Bidirectional reverse link (`ReversedByTransactionId`, script `0018`) is an accepted amendment to posting (2026-10-13). Next design: net worth.
