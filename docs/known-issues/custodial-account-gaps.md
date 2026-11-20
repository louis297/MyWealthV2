---
title: "Known issue — Custodial accounts still rely on hand entry, and some writes are missing"
status: open
language: en
created: 2026-11-17
updated: 2026-11-20
related:
  - ../features/custody-modes.md
  - ../features/account-custody-mode.md
  - ../adr/0020-account-custody-mode.md
  - ../future-plans.md
  - ../features/adjustments.md
  - ../features/deposit-matching.md
---

# Known issue: Custodial accounts still rely on hand entry, and some writes are missing

**Status: open.** It applies since [custody-modes](../features/custody-modes.md) landed (2026-11-17), the change that lets a Custodial account be opened.

## Problem

A Custodial account is meant to be the platform's own record: the server produces the numbers, and each movement comes from a platform-side flow. [custody-modes](../features/custody-modes.md) ships the account and its refusals before those flows exist. Each refused or hand-entered type in its manual entry matrix (§4.2) leaves a gap until its flow is built.

## Current behaviour

| Gap | Effect on a Custodial account | Closed by |
| --- | --- | --- |
| Withdrawals and closing payments are entered by hand (`TransferOut`, `CloseOut`) | Anyone with `transactions.create` can record money leaving that no bank paid out. The amount is the caller's number. Deposits no longer have this gap: from [deposit-matching](../features/deposit-matching.md) (accepted 2026-11-20) a `TransferIn` on a Custodial account comes only from a matched bank statement line | Withdrawals with review (roadmap B6) and an account closure flow (B8), driven by the simulator bank |
| No interest | Cash on the platform earns nothing. `Interest` is refused | An interest job |
| A split cannot be recorded | If a held instrument really splits, the quantity stays as it was while the price falls. The holding's market value, net worth and the next rebalance of that account are wrong until the quantity is corrected | Corporate action events |
| Existing positions cannot be moved in | A Custodial account starts with cash only. `OpeningHoldings` and `StockIn` are refused, so a Customer's shares at another broker cannot arrive in kind | In-kind transfer |
| No opening balance | The same for cash: `Opening` is refused. A balance arrives as a `TransferIn` booked on the day it is entered | Migration batches |
| History cannot be entered | The server sets `bookedAt`. A deposit entered today cannot be dated yesterday, and no past activity can be built through the API | A simulator with its own clock; the test seed until then |

External accounts have none of these gaps: they keep every hand-entered type.

## What we are not doing now

- Allowing `Split`, `StockIn` or `Interest` on a Custodial account "for now". Each would put a hand-entered number into the record that the later flows have to trust.
- A switch that lets one tenant or one caller bypass the matrix.
- Moving the corporate action work in front of the bank simulator only to cover splits. Revisit this if a demo instrument splits.
- Turning an External account into a Custodial one to bring its positions across.

## Revisit when

- Each flow in the last column is specified. Its spec changes one cell of the manual entry matrix and removes its row here.
- A seeded demo instrument announces a split while Custodial demo accounts hold it. Until corporate action events exist, the way out is to sell the holding before the split date, or to rebuild the demo data.

The order of the flows is in [future-plans.md](../future-plans.md) §9.
