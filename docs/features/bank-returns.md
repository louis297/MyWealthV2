---
title: Receipt returns, recalls and other debit lines
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-20
last_updated: 2026-11-22
related:
  - ../custodial-roadmap.md
  - ../adr/0021-outside-parties-behind-ports.md
  - withdrawals.md
  - deposit-matching.md
  - simulator.md
  - transaction-source.md
  - adjustments.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
  - account-closure.md
  - interest-run.md
---

# Receipt returns, recalls and other debit lines

Step 6b of [custodial-roadmap.md](../custodial-roadmap.md) §13. It finishes the bank statement work that [deposit-matching.md](deposit-matching.md) left for B6 and that [withdrawals.md](withdrawals.md) (step 6) did not take: a receipt marked for return is actually sent back to its payer (deposit-matching R28), a receipt the payer's bank recalls or dishonours is taken off the Custodial account, and every other debit line on the trust account lands in suspense instead of waiting as `Received` (deposit-matching R14). It reuses the outgoing payment, the `IBankPayments` port and the send step of [withdrawals.md](withdrawals.md) and follows [ADR 0021](../adr/0021-outside-parties-behind-ports.md) rules 1–20. It amends [deposit-matching.md](deposit-matching.md), [withdrawals.md](withdrawals.md), [simulator.md](simulator.md) and the Adviser Portal contract.

New: outgoing payment kind `ReceiptReturn`; statement line statuses `ReturnSent` and `Returned`, four unmatched reasons, match method `Recall`; ignore and reopen on a debit line; `returnPayment` on the statement line item; the simulator's return payment and recall. Scripts `0044_bank_returns.sql`; simulator script `database/simulator/0005_bank_returns.sql`. No new route, permission, `TransactionType`, source kind, problem code or domain event.

**Status is `accepted` (2026-11-21), landed 2026-11-21.** It depends on [withdrawals.md](withdrawals.md), which landed 2026-11-21. The points of §11 were confirmed in discussion on 2026-11-20, every one as recommended, and are written into the rules; point 8 was decided on acceptance (2026-11-21). The amendments to accepted docs listed in §12 were made when it was accepted.

Notes from landing:

- §5 names: the rejected return is `MarkReturnRejected()`, and `MarkReturned()` takes no time, since a `Returned` line stores none. `Reopen(by, at, returnAttempted)` treats an `Accepted` return payment as attempted.
- `0044` drops and adds again the `WithdrawalId` FK and index of `OutgoingPayments` around the `ALTER COLUMN`, and names the kind CHECK `CK_OutgoingPayments_Kind` and the one-target CHECK `CK_OutgoingPayments_Target`.
- R2 order in code: payout (withdrawals R40), return paid (R7), any other `endToEndId` (`UnknownPayment`), a `returnOf` (R3), any other debit (`UnknownPayment`). R7 needs the payment of the line's Tenant and client money account; a debit naming a return payment that was never attempted, or one already `Paid`, is `UnknownPayment`.
- R3: a recall whose receipt is still `Received` stays `Received` until that receipt is processed (it is older, so the same run takes it first). The Reversal's `CreatedBy` is the worker's system name. Not covered means: the Account is Closed, or the cash `SUM` after the Reversal is below 0, or below reserved cash.
- R2 (tenant scope): the pull now links a `returnOf` only to a stored line of the same Tenant and client money account. Before, a line naming another Tenant's line on the shared connection broke the composite FK and failed the page.
- R16: `SimulatorBankPayments` leaves `payee`, `reference` and `returnOf` out of the body when they are null.
- Simulator (§6.2 changed): `BankPayments` keeps `ReturnOf` (the outside id as sent, `nvarchar(100)`) next to `ReturnOfLineId`, because a return of a line the account does not have is rejected as `UnknownLine` and still recorded for R51; the CHECK is exactly one of a payee and `ReturnOf`. "Returned before" includes a held return. A recall's payer fields are the receipt's payer; the recall route answers 200 `{ id }`, an unknown line 404. `GET /control/bank/payments` shows `returnOfLineId` (the outside id) and null payee fields on a return. The hash of a payment with a payee is unchanged, so earlier answers replay.
- Adviser Portal: the RecallNotCovered link reads the recalled receipt's match for the account; a match with no Transaction shows the method, the time and the related line only.

---

## 1. Summary

| | After step 6 | After this slice |
| --- | --- | --- |
| A receipt marked "return to payer" | Status `ReturnRequested` only; no money moves | The `worker` asks the bank to send it back; the line becomes `ReturnSent`, then `Returned` when the bank's debit line shows it |
| A receipt the payer's bank recalls or dishonours | The debit line waits as `Received` | If the receipt was credited, its deposit is reversed while the cash is still available; otherwise the line waits in suspense for an adjustment |
| A debit the platform did not cause (a bank fee, an error) | Waits as `Received` | `Unmatched` in suspense; the TenantAdmin ignores it with a note |
| Actions on a debit line | None | Ignore and reopen |

The rule that distinguishes this slice: every line of the trust account's statement ends in a status a person can see, and the ledger moves only for a line the platform can tie to a Custodial account.

---

## 2. Scope

**In**

- Domain: `OutgoingPaymentKind`; `OutgoingPayment.ForReceiptReturn`; `PaymentRejectReason.UnknownLine`; `BankStatementLine` statuses `ReturnSent`, `Returned`, reasons `UnknownPayment`, `UnknownReturn`, `RecallNotCovered`, `ReturnRejected`, method `Recall`, and the transitions of R3–R9
- Application: `RequestStatementLineReturn` creates the payment; `ReopenStatementLine` cancels it; `IgnoreStatementLine` and `ReopenStatementLine` on debit lines; `SendBankPayments` sends returns; `ProcessStatementLines` processes every remaining debit line and returns; the statement line reads map `returnPayment`
- Infrastructure: `SimulatorBankPayments` sends `returnOf`; EF mappings
- Scripts: `database/schema/0044_bank_returns.sql`; `database/simulator/0005_bank_returns.sql`
- HTTP (`webapi`): the changes to the existing `/statement-lines` routes and item. No new route or permission
- Simulator: a payment that returns a receipt; recall of a receipt on the control surface
- Adviser Portal: labels and actions on the Bank receipts pages (D50)
- Tests listed in §10

**Out**

- A second person's approval of a receipt return (§11 point 6)
- Returning part of a receipt
- A Customer Portal change: a recalled deposit's Reversal shows as "Reversal", as today (customer-portal C21)
- Bank reconciliation of ignored and unmatched debit lines (B7)
- A real bank adapter
- Anything in [withdrawals.md](withdrawals.md)

---

## 3. Stories

1. As a TenantAdmin I mark a receipt that is not Customer money for return, and the platform sends it back to the payer through the bank; I see when the bank accepted it and when it left.
2. As a TenantAdmin I change my mind before the bank was asked, and reopen the receipt.
3. As a TenantAdmin I see a receipt the payer's bank recalled taken off the Customer's account, or, when the cash is gone, I am told to correct the account with an adjustment.
4. As a TenantAdmin I see every debit on the trust account that the platform did not cause, and I ignore it with a note once I know what it is.
5. As the simulator operator I recall a receipt, and I see the bank pay back a receipt the platform returned.

---

## 4. Rules

### 4.1 Access

| ID | Rule |
| --- | --- |
| R1 | **Unchanged.** Every `/statement-lines` route keeps deposit-matching R21: permission `statement-lines.manage` (TenantAdmin only), 401 / 403 / 404, `rowVersion` (R24). This slice adds no route and no permission. |

### 4.2 Processing every remaining line

Amendment to [deposit-matching.md](deposit-matching.md) R13, R14 and [withdrawals.md](withdrawals.md) R39.

| ID | Rule |
| --- | --- |
| R2 | **What is processed.** `ProcessStatementLines` now processes every `Received` line of the connection, debit and credit, in deposit-matching R13 order and one line per database transaction. Withdrawals R39 already took payout lines and returned payout lines. The rest is classified in this order: a debit with an `endToEndId` that names a receipt return of the line's Tenant (R7); a debit with any other `endToEndId` (`UnknownPayment`, also one that names a withdrawal R40 cannot pay: not a GUID, a payment `Rejected` or `Cancelled`, or one from another client money account); a debit with a `returnOf` (R3); any other debit (R4); a credit with a `returnOf` (R8); any other credit (deposit matching, unchanged). Debit lines stored before this slice are processed by its first run. Every line a line names (`returnOf`, `endToEndId`) is looked up in the line's Tenant and client money account only; a line of another Tenant counts as no line. |
| R3 | **Recall of a receipt.** A debit line whose `returnOf` is a stored credit line of the same connection that is itself a receipt (no `returnOf`), with the same amount and no `endToEndId`, is a recall or dishonour by the payer's bank. When that credit line is `Matched`: the platform posts the Reversal of its deposit with source `BankFeed` and that line's PublicId (as deposit-matching R33), if the Account is Open and available cash stays `≥ 0` ([withdrawals.md](withdrawals.md) R36); the credit line becomes `Returned` and loses its match (as an undo match); the debit line becomes `Matched` with method `Recall` (Account and Reversal). A Disabled Customer does not stop it. When the cash is not available or the Account is Closed: nothing is posted, the debit line becomes `Unmatched` with reason `RecallNotCovered`, and the TenantAdmin corrects the account with an adjustment ([adjustments.md](adjustments.md)) and then ignores the line. When that credit line is `Unmatched`, `Ignored` or `ReturnRequested`: the credit line becomes `Returned` and the debit line `Matched` with method `Recall`, with no Account and no Transaction; a return payment of that line with no attempt becomes `Cancelled`, and one already attempted is left to its answer, which then changes only the payment (R6). A different amount, or a credit line already `Returned` or `ReturnSent` → `UnknownReturn`. |
| R4 | **Other debit lines.** A debit the platform did not cause becomes `Unmatched` with reason `UnknownPayment`. Nothing is written to the ledger. A bank fee on the trust account is the firm's, not a Customer's; the TenantAdmin ignores it (R9). |

### 4.3 Returning a receipt

| ID | Rule |
| --- | --- |
| R5 | **Request return creates the payment.** Deposit-matching R28 changes: request return also creates, in the same database transaction, an outgoing payment ([withdrawals.md](withdrawals.md) R31) of kind `ReceiptReturn`: `Pending`, instruction id = the payment's own new PublicId (§11 point 8), amount and currency of the line, the line as the line returned, no payee and no reference, from the line's client money account. The bank pays it back to the original payer; the platform never holds the payer's full account number (deposit-matching R11). A line has at most one open return payment (`Pending` or `Accepted`); a `Rejected` or `Cancelled` one stays as a record, and a new request return creates a new payment with a new instruction id. Reopen (deposit-matching R29) from `ReturnRequested` is allowed only while the open payment has no attempt, and sets it `Cancelled`; after an attempt → 400 `errors.Status`, `The bank has been asked to return this receipt, so it can no longer be reopened.` |
| R6 | **Sending the return.** The send step (withdrawals R32) sends `ReceiptReturn` payments too, with the same rules for order, attempts, disabled Tenants and inactive client money accounts. The port call names the line's outside id as `returnOf` and carries no payee. Accepted → payment `Accepted`, line `ReturnSent`. Rejected → payment `Rejected`, line `Unmatched` with reason `ReturnRejected`; the TenantAdmin decides again (match, ignore or request return). A failure of the call → the payment stays `Pending`, the line stays `ReturnRequested`. When the line is already `Returned` (a recall came first, R3), an answer changes only the payment. Rejection reasons gain `UnknownLine` (withdrawals R34). |
| R7 | **Return paid.** A debit line whose `endToEndId` is the instruction id of a `ReceiptReturn` payment of the same Tenant and client money account, `Pending` with an attempt (the accepted answer was lost) or `Accepted`, whose line is `ReturnSent` or `ReturnRequested`, with the same amount: the credit line becomes `Returned`, the payment `Paid`, and the debit line `Matched` with method `Payment`, with no Account and no Transaction, and `relatedLineId` = the returned line. Nothing is written to the ledger. A different amount → `PaymentMismatch`. |
| R8 | **Credit lines with a `returnOf`.** A credit line whose `returnOf` names the debit line of a receipt return (the payer's bank sent the money back again), or names no line withdrawals R42 can act on, becomes `Unmatched` with reason `UnknownReturn`. The TenantAdmin matches, ignores or returns it like any receipt. |

### 4.4 Actions, statuses and the item

| ID | Rule |
| --- | --- |
| R9 | **Actions on debit lines.** Deposit-matching R23 changes: on a debit line, ignore (from `Unmatched`, note required) and reopen (from `Ignored`) are allowed; match, request return and undo match stay refused with `errors.Status`. `ReturnSent` and `Returned` lines, and every `Matched` debit line, accept no action. A matched payout, return or recall is undone only by its flow. |
| R10 | **New statuses, reasons and methods.** Statuses `ReturnSent = 5`, `Returned = 6`. Unmatched reasons `UnknownPayment = 8`, `UnknownReturn = 9`, `RecallNotCovered = 10`, `ReturnRejected = 11`. Match method `Recall = 4`. A `Matched` line has either an Account and a Transaction (methods `Reference`, `Manual`, `Payment` of a payout, `PaymentReturn`, `Recall` with a Reversal), or no Account, no Transaction and a related line (method `Payment` of a receipt return, `Recall` of a receipt never credited). A `Returned` line has no match. |
| R11 | **Item.** `relatedLineId` is now also set on a recall line and on the debit line of a receipt return. The statement line item gains `returnPayment: { status, attempts, lastFailure }` on a credit line with a `ReceiptReturn` payment (the newest one), else `null`. `match.accountId`, `match.customerId` and `match.transactionId` are `null` where R10 has no Transaction. |

### 4.5 The simulator bank

Amendment to [simulator.md](simulator.md), [deposit-matching.md](deposit-matching.md) §4.6 and [withdrawals.md](withdrawals.md) §4.7.

| ID | Rule |
| --- | --- |
| R12 | **A payment that returns a receipt.** The payment route (withdrawals R49) takes `returnOf` (the outside id of a credit line on that account) in place of `payee`; exactly one of the two, else 400. A `returnOf` that names no credit line of the account, a line that is itself a return, or a line already returned or recalled → 200 `Rejected` with reason `UnknownLine`. Otherwise the payment mode decides (withdrawals R52). |
| R13 | **Executing a return.** The debit line of an executed return carries `endToEndId` = the `instructionId`, `returnOf` = the returned line's id, and the original payer in the payer fields (withdrawals R48). |
| R14 | **Recall a receipt.** `POST /control/bank/lines/{outsideId}/recall` `{ "reason", "availableAfter"? }` on a credit line that is a receipt (not a return), not returned or recalled before (else 400 `errors.Status`), adds a **debit** line with the same amount, `returnOf` = that line, `returnReason` = `reason` (1–140), no `endToEndId`; `availableAfter` as deposit-matching R41. Log action `BankReceiptRecalled`. |
| R15 | **Its own books.** `GET /control/bank/payments` shows return payments with the line they returned. A reset is unchanged (withdrawals R56). |
| R16 | **The adapter.** `SimulatorBankPayments` sends `returnOf` for a `ReceiptReturn` payment and maps the reason `UnknownLine`. |

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `OutgoingPaymentKind` | Enum (new) | `Withdrawal = 0`, `ReceiptReturn = 1` |
| `OutgoingPayment` | Aggregate (existing, [withdrawals.md](withdrawals.md)) | `Kind`; `WithdrawalId` becomes optional; `ReturnOfLineId`. `ForReceiptReturn(line, clientMoneyAccount)` with a new PublicId as instruction id. Exactly one of `WithdrawalId` and `ReturnOfLineId`, by kind |
| `PaymentRejectReason` | Enum (existing) | Add `UnknownLine = 4` |
| `BankStatementLine` | Aggregate (existing) | Statuses, reasons and method of R10. `MarkReturnSent()`, `MarkReturned(at)`, `ReturnRejected()`, `MarkRecall(...)` (with or without Account and Transaction), `MarkReturnPaid(relatedLineId, at)`, `MarkUnknown(reason)`. `Ignore` from `Unmatched` and `Reopen` from `Ignored` on a debit line. `Reopen` from `ReturnRequested` takes whether the return payment had an attempt (R5) |

```text
credit receipt:
  Unmatched -> ReturnRequested (payment Pending)      request return (R5)
  ReturnRequested -> Unmatched                         reopen, payment not attempted
  ReturnRequested -> ReturnSent                        bank accepted (R6)
  ReturnRequested -> Unmatched (ReturnRejected)        bank rejected (R6)
  ReturnRequested | ReturnSent -> Returned             return debit line (R7)
  Matched | Unmatched | Ignored | ReturnRequested -> Returned   recall (R3)
debit line:
  Received -> Matched (Payment | Recall) | Unmatched (reason)   processing (R2–R4, R7)
  Unmatched -> Ignored -> Unmatched                    ignore, reopen (R9)
```

Invariants:

- A receipt has at most one open return payment (`Pending` or `Accepted`); each return payment has its own instruction id (withdrawals R31).
- A `Returned` line has no match and never changes again.
- A recall reverses a deposit only while the account's available cash allows it.

Domain events: none.

Update [domain-model.md](../domain-model.md) §8.9 and §8.11 when this spec is accepted.

---

## 6. Database

Forward only. Do not add EF migrations.

### 6.1 Script `0044_bank_returns.sql`

| Table | Change | Index / FK |
| --- | --- | --- |
| `OutgoingPayments` | Add `Kind int NOT NULL` (temporary default 0 for existing rows, then dropped; CHECK 0–1) and `ReturnOfLineId int NULL`. `WithdrawalId` becomes NULL. CHECK `(Kind = 0 AND WithdrawalId IS NOT NULL AND ReturnOfLineId IS NULL) OR (Kind = 1 AND WithdrawalId IS NULL AND ReturnOfLineId IS NOT NULL)`. Widen `RejectReason` CHECK to 0–4 | FK `(TenantId, ReturnOfLineId)` → `BankStatementLines (TenantId, Id)` RESTRICT; unique filtered index `UX_OutgoingPayments_ReturnOfLineId_Open` on `ReturnOfLineId` `WHERE Kind = 1 AND Status IN (0, 1)` |
| `BankStatementLines` | Widen `Status` CHECK to 0–6, `UnmatchedReason` to 0–11, `MatchMethod` to 0–4. Replace the Matched CHECK: `Status = 1` needs `MatchMethod` and `MatchedAt`, and either both `MatchedAccountId` and `MatchedTransactionId`, or both null with `RelatedLineId` set; any other status has no match columns | none |

No back-fill: existing outgoing payments are withdrawals (`Kind = 0`); existing lines keep their statuses.

### 6.2 Simulator script `database/simulator/0005_bank_returns.sql`

| Table | Content |
| --- | --- |
| `BankPayments` | `PayeeAccountNumber` and `PayeeName` become NULL; add `ReturnOf nvarchar(100) NULL` (the outside id as sent) and `ReturnOfLineId bigint NULL` FK `BankLines` (that line, when the account has it); CHECK exactly one of a payee and `ReturnOf` (`CK_BankPayments_Target`) |

Update [database-design.md](../database-design.md) §11.9, §11.11 and §15 when this spec is accepted.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Command | `RequestStatementLineReturn` (existing) | Creates the `ReceiptReturn` payment (R5) |
| Command | `ReopenStatementLine` (existing) | Cancels the payment; refused after an attempt (R5); from `Ignored` on a debit line (R9) |
| Command | `IgnoreStatementLine` (existing) | On an `Unmatched` debit line (R9) |
| Service | `SendBankPayments` (existing, [withdrawals.md](withdrawals.md)) | Sends `ReceiptReturn` payments (R6) |
| Service | `ProcessStatementLines` (existing) | R2–R4, R7, R8 |
| Port | `IBankPayments` (existing) | `BankPaymentRequest` takes `ReturnOfOutsideId` in place of a payee |
| Query | `GetStatementLines`, `GetStatementLineById` | `returnPayment` and the R11 fields |

---

## 8. API

No new route. Changes to existing routes:

| Method | Route | Change |
| --- | --- | --- |
| GET | `/statement-lines[/{id}]` | New statuses, reasons and method in the item and the `status` filter; `returnPayment`; null match ids where R10 has no Transaction |
| POST | `/statement-lines/{id}/ignore` | Allowed on an `Unmatched` debit line |
| POST | `/statement-lines/{id}/reopen` | Allowed on an `Ignored` debit line; 400 `errors.Status` on a `ReturnRequested` line whose return payment was attempted |
| POST | `/statement-lines/{id}/request-return` | Unchanged body; also creates the return payment |

Item of a receipt that is being returned (other fields as deposit-matching §8.1):

```json
{
  "direction": "Credit",
  "status": "ReturnSent",
  "unmatchedReason": "UnknownReference",
  "relatedLineId": null,
  "returnPayment": { "status": "Accepted", "attempts": 1, "lastFailure": null },
  "match": null
}
```

Simulator, party port: a return payment sends `"returnOf": "3f2c…e1-L7"` in place of `payee`. Control: `POST /control/bank/lines/3f2c…e1-L7/recall` `{ "reason": "Payer's bank recalled the payment", "availableAfter": "P1D" }`. Control log actions gain `BankReceiptRecalled`.

Errors: no new case beyond the reopen refusal above and the existing ones of deposit-matching §8.2. Keep the resource catalog in [api-design.md](../api-design.md) pointing here for these rules.

---

## 9. UI

Page contract: [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (D50), amended and accepted together with this spec. The BFFs do not change.

| ID | Behaviour |
| --- | --- |
| D50 | The Bank receipts pages (D34, D35, D49) gain status labels ReturnSent "Return sent" and Returned "Returned"; the status filter gains both. Reason labels: UnknownPayment "Payment out not recognised", UnknownReturn "Return not recognised", RecallNotCovered "Recalled, but the cash is gone", ReturnRejected "Bank refused the return". Method label: Recall "Recalled by the payer's bank". A debit line offers Ignore (Unmatched) and Reopen (Ignored). On a To be returned line the return payment's status shows under the status ("Waiting to send", "Sending failed: <failure label>"), and Reopen is offered only while `returnPayment.attempts` is 0. On a RecallNotCovered line the page says "Correct the account with an adjustment, then ignore this line." with a link to the account. |

Customer Portal: none.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `OutgoingPayment.ForReceiptReturn`: kind, line, no withdrawal, no payee. `BankStatementLine`: every transition of §5; ignore and reopen on a debit line; match, request return, undo match on a debit line → throws; no action on `ReturnSent` or `Returned` |
| Application.FunctionalTests | With fakes for `IBankPayments` and `IBankFeed`. Request return → outgoing payment `ReceiptReturn`, Pending, its own instruction id, the line as the line returned; a new request return after a rejection → a second payment with a new instruction id; reopen before the attempt → 204, payment Cancelled; after → 400. Send step calls the port with `returnOf` and no payee; Accepted → line ReturnSent; Rejected → Unmatched `ReturnRejected`; failure → stays ReturnRequested. The debit with the payment's instruction id → line Returned, debit Matched (Payment), no Transaction, `relatedLineId` set; with the accepted answer lost → the same; different amount → `PaymentMismatch`. Recall of a matched receipt → deposit reversed (source `BankFeed`), credit line Returned without a match, debit Matched (Recall); with the cash spent, or reserved by a withdrawal, or the account Closed → `RecallNotCovered`, nothing posted; of an Unmatched or Ignored receipt → credit Returned, no Transaction; of a ReturnRequested receipt with no attempt → payment Cancelled; with an attempt → the later answer changes only the payment; of a line already Returned → `UnknownReturn`. A debit with an unknown `endToEndId`, and one with neither field → `UnknownPayment`; ignore → 204; reopen → 204; match, request return, undo match on it → 400. A credit returning a receipt return → `UnknownReturn`. A debit line stored before this slice → processed by the first run. Processing twice → one Reversal. Two-tenant: a `returnOf` or `endToEndId` naming tenant B's line on tenant A's account → `UnknownReturn` / `UnknownPayment` |
| Infrastructure.IntegrationTests | `0044`: existing outgoing payments get `Kind = 0`; the kind CHECK; the new Matched CHECK (account and transaction together, or a related line); `Status = 7` violates; a second open return payment of one line violates the filtered index. `SimulatorBankPayments`: the `returnOf` body; `UnknownLine` maps |
| Simulator.Tests | `0005` applies. Payment with `returnOf` of a receipt → executed debit line with `endToEndId` and `returnOf`, payer fields = the original payer; both or neither of `payee` and `returnOf` → 400; an unknown line, a return line, or a line already returned or recalled → Rejected `UnknownLine`. Recall → debit line with `returnOf`, reason, after `availableAfter`; twice → 400; of a return line → 400. Control log row for a recall; a 400 writes none |
| Adviser Portal | D50 labels, filter, debit actions, the return payment status, the reopen rule, the RecallNotCovered sentence |

---

## 11. Locked in this spec

No new ADR. It follows [ADR 0021](../adr/0021-outside-parties-behind-ports.md) rules 1–20 and reuses the outgoing payment and port of [withdrawals.md](withdrawals.md).

| Item | Lock |
| --- | --- |
| Receipt return | An outgoing payment of kind `ReceiptReturn` with its own instruction id, created by request return, cancelled by reopen until the first attempt, naming the returned line, no payee; at most one open per line |
| Recall | A debit line naming a receipt; the deposit reversed by the flow while available cash allows; otherwise suspense and an adjustment |
| Other debit lines | Suspense with `UnknownPayment`; ignore and reopen only |
| Statement lines | Statuses `ReturnSent`, `Returned`; reasons `UnknownPayment`, `UnknownReturn`, `RecallNotCovered`, `ReturnRejected`; method `Recall` |
| Scripts | `0044_bank_returns.sql`; simulator `0005_bank_returns.sql` |
| Routes, permissions, problem codes, events | None added |

### Confirmed in review (2026-11-20)

Each point was confirmed in discussion on 2026-11-20 as recommended, and is written into the rules above as stated here. The alternatives are kept for the record.

1. **Its own spec, step 6b** (this file). Decided: splitting the debit lines off [withdrawals.md](withdrawals.md) keeps each spec small enough to review and land on its own; until this lands, those lines wait as deposit-matching left them. Alternative: everything in one spec for step 6.
2. **Request return creates the payment at once; reopen cancels it until the first attempt** (R5). Decided: one action records the decision and starts the payment, and the TenantAdmin can still change their mind until the bank is asked. Alternative: a separate "send return" action after request return.
3. **A rejected return goes back to suspense** (R6). Decided: the money is still the payer's and still in the trust account; the TenantAdmin decides again with the reason in front of them. Alternative: a final status `ReturnFailed`.
4. **A recall the cash no longer covers** (R3). Decided: as an undo match (deposit-matching §11 point 11): no balance goes negative and no reserved cash is taken; the TenantAdmin corrects with an adjustment and ignores the line. Alternative: post the Reversal and let the cash go negative.
5. **Unknown debit lines wait in suspense, and only ignore applies to them** (R4, R9). Decided: a bank fee or an error on the trust account is the firm's, not a Customer's; B7 shows ignored lines as known differences. Alternative: a TenantAdmin may match a debit line to a Custodial account by hand, which would be a hand withdrawal.
6. **One TenantAdmin returns a receipt, with no second approval** (R5). Decided: the money can only go back the way it came, to an account the platform never sees or types, so the risk a second person guards against for withdrawals is not there. Alternative: a second TenantAdmin approves each return, as for withdrawals.
7. **A credit line that returns a receipt return goes to suspense** (R8). Decided: the payer's bank refused the money back; it is a receipt again, for a person to place. Alternative: put the original line back to `Unmatched`.

8. **Each return payment has its own instruction id** (R5, R7; decided on acceptance, 2026-11-21). Decided: the bank answers a known instruction id with its first answer (withdrawals R51), so a return requested again after a rejection or a reopen needs a new id; the payment links to the line by `ReturnOfLineId`, with at most one open payment per line. Alternative: the line's PublicId as the instruction id, with no second return after a rejection.

Still open, not this slice:

- Reconciliation of ignored and unmatched debit lines and of returns sent but not yet on the statement (B7)
- Returning part of a receipt
- A real bank adapter

---

## 12. Suggested commits

The repository should build after each commit. Agents split each behaviour into red then green. Start after [withdrawals.md](withdrawals.md) has landed.

1. Script `0044`, `OutgoingPaymentKind`, the statement line statuses, reasons and method (R10) + Domain unit tests and the `0044` integration tests.
2. Other debit lines to suspense, ignore and reopen on debit lines (R2, R4, R9) + functional tests.
3. Receipt returns: the payment on request return, reopen, sending, return paid (R5–R7, R11) + functional tests; `SimulatorBankPayments` `returnOf` (R16) + integration tests.
4. Recalls and credit lines with a `returnOf` (R3, R8) + functional tests, two-tenant.
5. Simulator: script `0005`, return payments, recall (R12–R15) + `Simulator.Tests`.
6. Adviser Portal (D50) + frontend tests.
7. Repo docs: mark this file landed; roadmap §13 step 6b; the known issue.

### Docs amended when this spec was accepted

[deposit-matching.md](deposit-matching.md) (R13, R14, R18, R23, R28, R29 and the item: every debit line processed, returns sent, recalls applied, actions on debit lines), [withdrawals.md](withdrawals.md) (R31 kinds, R34 `UnknownLine`), [simulator.md](simulator.md) (R12–R15, script `0005`), [api-design.md](../api-design.md) (§7.7 row, change log), [domain-model.md](../domain-model.md), [database-design.md](../database-design.md), [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (D50), [features/README.md](README.md) row status, the roadmap §5 B4 / B6, §11 S1 and §13 status, [glossary.md](../glossary.md) (statement line statuses, suspense, receipt return, recall) and [function-plan.md](../function-plan.md) §5.

---

## Amendment 2026-11-22 (account closure) — accepted

Status of this amendment: `accepted` (2026-11-22), together with [account-closure.md](account-closure.md).

- R3: no rule changes. On a closing account (an account closure `InProgress`) available cash is the remainder below the minor unit, so a recall of one of its receipts is not covered and waits in suspense as `RecallNotCovered` (account-closure R15), as on any account whose cash is reserved for a withdrawal.

---

## Amendment 2026-11-22 (interest run) — accepted

Status of this amendment: `accepted` (2026-11-22), together with [interest-run.md](interest-run.md). Rule R40 lives there; this section records what changes here.

- R2–R4: no rule changes. A **debit** line of kind `Interest` (interest the bank charges on the trust account) gets no special rule: it is processed as any debit line and waits in suspense with reason `UnknownPayment`, where a TenantAdmin ignores it with a note.
