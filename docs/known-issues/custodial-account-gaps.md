---
title: "Known issue — Custodial accounts still rely on hand entry, and some writes are missing"
status: open
language: en
created: 2026-11-17
updated: 2026-11-25
related:
  - ../features/custody-modes.md
  - ../features/account-custody-mode.md
  - ../adr/0020-account-custody-mode.md
  - ../future-plans.md
  - ../features/adjustments.md
  - ../features/corporate-actions.md
  - ../features/deposit-matching.md
  - ../features/withdrawals.md
  - ../features/account-closure.md
  - ../features/interest-run.md
  - ../features/migration-batches.md
  - ../features/in-kind-transfers.md
  - ../custodial-roadmap.md
---

# Known issue: Custodial accounts still rely on hand entry, and some writes are missing

**Status: open.** It applies since [custody-modes](../features/custody-modes.md) landed (2026-11-17), the change that lets a Custodial account be opened.

## Problem

A Custodial account is meant to be the platform's own record: the server produces the numbers, and each movement comes from a platform-side flow. [custody-modes](../features/custody-modes.md) ships the account and its refusals before those flows exist. Each refused or hand-entered type in its manual entry matrix (§4.2) leaves a gap until its flow is built.

## Current behaviour

| Gap | Effect on a Custodial account | Closed by |
| --- | --- | --- |
| History cannot be entered | The server sets `bookedAt`. A deposit entered today cannot be dated yesterday, and no past activity can be built through the API | Replay with booking at an outside date ([custodial-roadmap.md](../custodial-roadmap.md) X2, D2); the test seed until then. The simulator's clock does not close it: the platform keeps the real clock and books at processing time (roadmap D9). A migration batch brings balances across, not history |

External accounts have none of these gaps: they keep every hand-entered type.

## What we are not doing now

- Allowing `Split`, `StockIn` or `Interest` on a Custodial account "for now". Each would put a hand-entered number into the record that the later flows have to trust.
- A switch that lets one tenant or one caller bypass the matrix.
- Turning an External account into a Custodial one to bring its positions across.

## Revisit when

- Each flow in the last column is specified. Its spec changes one cell of the manual entry matrix and removes its row here. Every flow of the roadmap has landed (2026-11-25); the one row left waits for replay.

The order of the flows is in [custodial-roadmap.md](../custodial-roadmap.md) §13.
