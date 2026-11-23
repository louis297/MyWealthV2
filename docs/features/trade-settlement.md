---
title: Trade settlement cash and broken trades
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-23
last_updated: 2026-11-23
related:
  - ../custodial-roadmap.md
  - ../adr/0021-outside-parties-behind-ports.md
  - orders.md
  - bank-reconciliation.md
  - withdrawals.md
  - bank-returns.md
  - deposit-matching.md
  - client-money-accounts.md
  - transaction-source.md
  - trade-fees.md
  - adjustments.md
  - account-closure.md
  - simulator.md
  - bff-adviser-portal.md
  - ../fixes/ledger-write-guards.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../customer-portal/customer-portal.md
  - ../backoffice/back-office.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
  - rebalances.md
  - migration-batches.md
---

# Trade settlement cash and broken trades

Step 11b of [custodial-roadmap.md](../custodial-roadmap.md) §13. It finishes the two parts that [orders.md](orders.md) (step 11) left out: §11 point 1, paying and receiving the cash of settled trades through the client money account and reconciling it, and §11 point 12, a trade the broker breaks after it was filled. It reuses the broker feed and the fills of [orders.md](orders.md), the outgoing payment, the `IBankPayments` port and the send step of [withdrawals.md](withdrawals.md), the statement lines of [deposit-matching.md](deposit-matching.md) and the run of [bank-reconciliation.md](bank-reconciliation.md). It follows [ADR 0021](../adr/0021-outside-parties-behind-ports.md) rules 1–20 and does not re-decide them.

When the broker settles the fills of a day, it nets them per trust account and currency into one **broker settlement** and reports it on the broker feed. The platform works out the same net from its own fills and stores a **trade settlement**. When the firm owes the broker, the `worker` pays the net from the client money account; when the broker owes the firm, the platform waits for the broker's receipt on the bank statement. Either way the statement line is matched to the trade settlement, with no Transaction: the fills were posted when they were reported. The bank reconciliation then explains the cash of order fills by their settlement instead of listing it as the known difference `Trades`.

A **broken trade** is a fill the broker takes back. The broker says so with a `Broken` execution report, and the platform posts the Reversal of the fill's Transaction, as a flow that undoes its own write (roadmap P6).

New: permission `trade-settlements.manage`; routes under `/trade-settlements` and `POST /orders/{id}/fills/{fillId}/resolve-break`; execution report kinds `Broken` and `Settlement`; outgoing payment kind `TradeSettlement`; statement line match method `Settlement` and unmatched reason `SettlementMismatch`; fill status `Broken`; four reconciliation item kinds; the broker's bank accounts in the broker connection configuration; the simulator broker's settlements, settlement mode and broken fills. Script `0052_trade_settlement.sql`; simulator script `database/simulator/0011_broker_settlement.sql`. No new `TransactionType`, source kind, port, job, problem code or domain event.

**Status is `accepted` (2026-11-23), landed 2026-11-23.** The sixteen points of §11 were confirmed on 2026-11-23, every one as recommended, and are written into the rules. The amendments to accepted docs listed in §12 were made when it was accepted. It is a build contract.

Notes from landing:

- §6.1: `0052` also adds `UQ_ClientMoneyAccounts_TenantId_Id`, the key the tenant composite FKs to a client money account need, and `CK_TradeSettlements_FillCount` (both counts at least 1).
- R2: the broker's bank accounts are read by `BrokerConnectionsConfiguration` on `webapi` and `worker`; `IBrokerConnections` answers `BankAccount(connection, currency)` and `SettlementCurrencies(connection)`.
- R3: `PlaceOrder` decides the settlement account (`SettlementAccountAsync`) and `Order.Place` takes it; the preview does not show it.
- R8–R13: `ProcessExecutionReports.SettleAsync` stores the trade settlements and, for an agreed net below 0, the `TradeSettlement` payment in the report's own transaction; their `CreatedBy` is `SystemActor.Name`.
- R17: the send step reads the payee from `IBrokerConnections` on the `worker`. A payment whose broker account is no longer configured there is not sent: it stays `Pending`, with a warning in the log.
- R18–R21: `BankSettlements` (Application) handles the paid debit line, the broker's receipt and the returned payment. A line matched to a trade settlement refuses `unmatch` with `This line is the cash of a trade settlement and cannot be unmatched.`
- R14: the refusal names the settlement's currency: `The broker connection has no bank account in <currency>.`
- R15: setting a `PaymentFailed` settlement aside clears its failure and failure detail, as the "set iff" CHECKs of `0052` ask.
- R23, R24: `ReconcileClientMoneyAccount` reads the fill of each `OrderFill` Transaction through its execution detail, and the trade settlement and break of that fill.
- R28–R30: `PostOrderFills.BreakAsync` is the one place a broken fill is reversed; it decides `NotApplied` before it builds the Reversal. The fee the order gives back is the fee detail's amount of the fill's Transaction.
- R33: account closure checks a waiting break before unsettled fills, and a broken fill is never an unsettled fill there.
- R35–R42: on the simulator a `Settlement` report names no order, so `BrokerReports.OrderId` becomes nullable in `0011`. The broker routes take `BrokerBankAccounts` (configuration `Simulator:Broker:BankAccounts`) to pay receipts. Breaking a fill by the outside id of a report that is not a `Fill` report answers 404.
- §9.1: the AppHost and `compose.yml` give the broker accounts `BRK-SIM-USD` and `BRK-SIM-NZD` to `webapi`, `worker` and the simulator; `.env` may override them.
- D76, D77: "Set aside" and "Mark resolved" use the note dialog of the Bank receipts pages. BO29 shows the settlement id in its own column.

---

## 1. Summary

```mermaid
sequenceDiagram
  participant Brk as Simulator broker (party surface)
  participant Job as worker: broker-sync / bank-statement-sync
  participant Bank as Simulator bank (party surface)
  participant Op as TenantAdmin (Adviser Portal)
  Note over Brk: the simulator clock reaches the settlement date
  Job->>Brk: GET reports after cursor
  Brk-->>Job: Settled x3 with settlement id, then Settlement (net -1 001.50 USD, 3 fills)
  Job->>Job: fills settled; trade settlement stored, own net agrees, AwaitingPayment
  Job->>Bank: POST payment 1 001.50 USD to the broker's account
  Bank-->>Job: Accepted
  Job->>Bank: GET statement lines after cursor
  Bank-->>Job: debit line with the payment's instruction id
  Job->>Job: line Matched (Settlement), trade settlement Completed
  Op->>Job: Run bank reconciliation: no Trades item for these fills
```

| | After step 11 | After this slice |
| --- | --- | --- |
| The cash of a settled trade at the bank | Never moves; the trust account keeps money the broker is owed | The net of each broker settlement is paid from, or received into, the client money account |
| Bank reconciliation of order fills | Every fill is in the known difference `Trades` | A fill's cash is a timing difference until its trade settlement completes, then no item; the trade fee is the known difference `TradeFees` |
| A settlement payment the bank refuses, or figures the broker and the platform do not agree on | — | A break, on the Trade settlements page, with "Pay again" and "Set aside" |
| A fill the broker breaks | A report of an unknown kind, stored and skipped | The fill's Transaction is reversed by the flow; when it cannot be, the break waits for a TenantAdmin |
| Rebalance trades, trades typed before orders, sells in another currency | `Trades` | Unchanged: still `Trades` |

The rule that distinguishes this slice: settlement cash is matched to fills that are already in the ledger. A settlement line posts no Transaction, and a broken trade is the only thing that takes a fill out of the ledger.

---

## 2. Scope

**In**

- Domain: `TradeSettlement` aggregate; `Order` settlement account; `OrderFill` broker settlement id, trade settlement and break; fill status `Broken`; execution report kinds `Broken`, `Settlement` and three skip reasons; `OutgoingPaymentKind.TradeSettlement`; statement line method `Settlement`, reason `SettlementMismatch`; four `ReconciliationItemKind` values; `BankReconciliation.Compute` with the new groups
- Application: `PlaceOrder` sets the settlement account; `SendBrokerOrders` sends it; `ProcessExecutionReports` handles `Settled` with a settlement id, `Settlement` and `Broken`; `SendBankPayments` and `ProcessStatementLines` handle settlement payments, receipts and returned payments; `ReconcileClientMoneyAccount` reads the new groups; commands `PayTradeSettlement`, `SetAsideTradeSettlement`, `ResolveBrokenFill`; queries `GetTradeSettlements`, `GetTradeSettlementById`; the order, transaction, statement line, account and connection reads map the new fields
- Infrastructure: the three simulator adapters read and send the new fields; broker connection configuration; EF mappings
- Scripts: `database/schema/0052_trade_settlement.sql`; `database/simulator/0011_broker_settlement.sql`
- Security: permission `trade-settlements.manage` (TenantAdmin only)
- HTTP (`webapi`): the routes of §8
- Simulator: `settlementAccount` on the order route; broker settlements, the `Settlement` report, the broker's receipt at the simulator bank, the settlement mode; breaking a fill
- Adviser Portal: Trade settlements pages, broken fills, new labels on the bank pages; BFF prefix `/trade-settlements`. Customer Portal: the row of a broken trade. Back Office: new kinds and reasons on the Broker page
- Tests listed in §10

**Out**

- Rebalance through orders (roadmap O3). Rebalance trades stay in `Trades`
- Settling a sell in another currency than its account's through the bank (orders R9, R34), and multi-currency cash (future-plans §5)
- A trade broken after it settled; the cash would go back through a new settlement (§11 point 14)
- Sweeping the trade fee out of the client money account, and the broker's own commission (orders §11 "still open")
- Rounding settlement amounts to the currency's minor unit (§11 point 6)
- A second person's approval of a settlement payment (§11 point 5)
- Changing withdrawable cash: a sale's proceeds stay withdrawable from the `Settled` report (§11 point 4)
- Matching a statement line to a trade settlement by hand, and paying part of a settlement
- A fill that fails to settle at the broker, and marking a fill settled by hand
- Custodian positions and their reconciliation with unsettled trades (roadmap T3)
- Notifying anyone of a break or a failed settlement
- A real broker or bank adapter; named scenarios and the scenario library

---

## 3. Stories

1. As the platform I pay the broker the net of each day's settled trades from the client money account, to the broker's account held in configuration, and I match the bank's debit line to that settlement.
2. As the platform I match the broker's receipt on the bank statement to the settlement it names, so the cash of settled sales is in the trust account.
3. As a TenantAdmin I see every trade settlement with the broker's figures and the platform's, and its state: waiting for the bank, completed, failed or disputed.
4. As a TenantAdmin I pay a settlement again after the bank refused or returned the payment, and I set aside a settlement that will be resolved outside the platform, with a note.
5. As a TenantAdmin I run the bank reconciliation and see order fills explained by their settlements, with only the trade fees left as a known difference.
6. As a Customer and as an Adviser I see that the broker cancelled a trade, and the cash and shares as they were before it.
7. As a TenantAdmin I see a broken trade the platform could not reverse and why, correct the account with an adjustment, and mark it resolved.
8. As the simulator operator I let the broker pay or hold its settlement receipts, pay a different amount, and break a fill.

---

## 4. Rules

### 4.1 Access

| ID | Rule |
| --- | --- |
| R1 | **Access.** Every `/trade-settlements` route requires the new permission `trade-settlements.manage`, which only TenantAdmin holds (roadmap D5). Adviser, Customer and SystemAdmin → 403, checked before anything is read. Missing / dead Bearer → 401, never 302. A disabled current tenant → 403. A trade settlement of another Tenant, or a missing one → 404. A `tenantId` in a query or body → 400. `POST /orders/{id}/fills/{fillId}/resolve-break` requires `transactions.adjust` (TenantAdmin, [adjustments.md](adjustments.md)) and the order in the caller's scope (orders R1), else 404. |

### 4.2 Which orders settle through the client money account

Amendment to [orders.md](orders.md) R2, R4, R6, R20 and R53.

| ID | Rule |
| --- | --- |
| R2 | **The broker's bank accounts.** A broker connection (orders R2) gains optional entries `BrokerConnections:{name}:BankAccounts:{currency}:AccountNumber` (the shape of deposit-matching R38) and `:AccountName` (1–140 characters), on `webapi` and `worker`: the broker's own bank account that the firm pays settlement cash to, per currency. They are standing instructions, never taken from the feed. A currency that is not a three-letter code, or an entry without both values, fails start-up before the host listens. `GET /broker-connections` gains `settlementCurrencies`: the currencies with an entry, sorted. |
| R3 | **The settlement account of an order.** When an order is placed, it gets a **settlement account** if all three hold: the Instrument's quote currency equals the Account's currency; the Tenant has an active client money account in that currency; and the order's broker connection has the broker's bank account for that currency (R2). The settlement account is that client money account, stored on the order with its account number, and it never changes. Otherwise the order has none. Having none never refuses an order. Orders placed before this slice have none. |
| R4 | **Sent with the order.** The send step (orders R20) adds `settlementAccount`, the trust account number, to the place call of an order that has one, and leaves the field out for an order that has none, so an order sent before this slice is sent again with the same body. |
| R5 | **What does not settle through the bank.** The fills of an order with no settlement account settle as in orders R43, with no cash at the bank, and their cash legs stay in the known difference `Trades` (R23). So do rebalance trades, trades typed before orders, and every sell in another currency than its account's. |

### 4.3 The broker's settlement on the feed

Amendment to [orders.md](orders.md) R23–R25 and R43.

| ID | Rule |
| --- | --- |
| R6 | **`Settled` names the broker's settlement.** A `Settled` report may carry a `settlementId` (1–100 characters, opaque, unique per connection for ever): the broker settlement that fill was settled in. Processing stores it on the fill. Everything else in orders R43–R46 is unchanged: the fill is settled by this report, and its sale proceeds are withdrawable from then. |
| R7 | **The `Settlement` report.** A new execution report kind. It closes one broker settlement: `settlementId`, `settlementAccount` (1–34 characters), `currency` (three letters), `settledDate`, `fillCount` (a whole number `≥ 1`) and `netAmount`, a decimal with at most 4 decimal places seen from the trust account: above 0 the broker pays the firm, below 0 the firm pays the broker. It names no order, so it needs no client order id. A page with a `Settlement` item outside these rules is refused as `InvalidItem` (orders R24). The broker sends it after the `Settled` reports of its fills. |
| R8 | **Processing a `Settlement` report.** In one database transaction with the report's status: the platform takes every fill of the connection that carries this settlement id and is in no trade settlement yet, groups them by the client money account of their order, and stores one trade settlement per group (R10). Normally there is one group. No such fill → `Skipped` with the new reason `UnknownSettlement`. A trade settlement with this connection and settlement id already exists → `Skipped` as `Repeated`. |
| R9 | **Agreed or disputed.** The platform's figures of a trade settlement are its own: `fillCount` = its fills, and `netAmount` = the sum over them of `Round(quantity × price, 4)`, plus for a sell and minus for a buy, whether the fill is posted or still `Pending` (the broker settles what it executed). The settlement is **agreed** when the report's `fillCount` and `netAmount` equal them and its `settlementAccount` and `currency` are the client money account's (the number compared without regard to case, spaces and hyphens). Otherwise it is stored `Disputed`, with both sets of figures, and nothing is paid or matched. |

### 4.4 The trade settlement

| ID | Rule |
| --- | --- |
| R10 | **What it is.** A trade settlement is the platform's record of one broker settlement for one client money account: the connection, the broker's settlement id, the settled date, its fills, the platform's and the broker's figures, and a status. It belongs to the Tenant of its client money account. It is not a Transaction and writes none. A fill is in at most one trade settlement. |

| Status | Means | Open | Next |
| --- | --- | --- | --- |
| `AwaitingPayment` | The firm owes the broker; an outgoing payment is `Pending` or `Accepted` | yes | `Completed`, `PaymentFailed`, `SetAside` (R15) |
| `AwaitingReceipt` | The broker owes the firm; no statement line has matched yet | yes | `Completed`, `SetAside` |
| `Completed` | The cash is on the bank statement, or the net was 0 | no | `PaymentFailed` only by R20 |
| `PaymentFailed` | The bank refused or returned the payment, or the broker's account is not configured | yes | `AwaitingPayment` (R14), `SetAside` |
| `Disputed` | The broker's figures and the platform's differ (R9) | yes | `SetAside` |
| `SetAside` | A TenantAdmin ended it by hand with a note | no | final |

| ID | Rule |
| --- | --- |
| R11 | **Net of 0.** An agreed settlement whose `netAmount` is 0 is `Completed` at once. Nothing is paid. |
| R12 | **The firm pays.** An agreed settlement with `netAmount < 0` is `AwaitingPayment`, and the same database transaction creates an outgoing payment (withdrawals R31) of the new kind `TradeSettlement`: `Pending`, from the settlement's client money account, amount `−netAmount`, instruction id = the payment's own new PublicId (as bank-returns R5), payee = the broker's bank account for the currency (R2), reference = the broker's settlement id when it is a valid payment reference (withdrawals R23), else none. With no broker bank account configured on the `worker`, no payment is created and the settlement is `PaymentFailed` with failure `NoBrokerAccount`. No person approves the payment, no Account row is locked and no available cash is checked: the cash left the Custodial accounts when the fills were posted. |
| R13 | **The broker pays.** An agreed settlement with `netAmount > 0` is `AwaitingReceipt` until a statement line matches it (R19). |
| R14 | **Pay again.** `POST /trade-settlements/{id}/pay` with `rowVersion` → 204, from `PaymentFailed` only (else 400 `errors.Status`, `This settlement is <status> and cannot be paid again.`). It creates a new outgoing payment as R12, with a new instruction id, and the settlement is `AwaitingPayment`. With no broker account configured on `webapi` → 400 `errors.Status` (`The broker connection has no bank account in <currency>.`). A settlement has at most one open payment (`Pending` or `Accepted`); earlier ones stay as a record. |
| R15 | **Set aside.** `POST /trade-settlements/{id}/set-aside` with `rowVersion` and `note` (trimmed, 1–200, required) → 204, from `AwaitingReceipt`, `PaymentFailed` or `Disputed`, or from `AwaitingPayment` while its payment has no send attempt, which becomes `Cancelled` in the same database transaction. After an attempt → 400 `errors.Status` (`The bank has been asked to pay this settlement, so it cannot be set aside.`). The settlement records the person, the time and the note, and never changes again. Its fills stay settled; from then on their cash counts as cash that did not pass through the trust account (R23), and a later statement line that names the settlement goes to suspense (R19). It is the way to end a settlement the firm and the broker resolve outside the platform. |
| R16 | **Reads.** `GET /trade-settlements` → list envelope, newest first. Optional filters: `status` (one or more names, comma-separated; unknown → 400 `errors.Status`), `clientMoneyAccountId`, `open=true`. `GET /trade-settlements/{id}` → the item with its fills and payments (§8.1). Stale `rowVersion` on a write → 409 `stale_row_version`; missing → 400. |

### 4.5 Settlement cash on the bank statement

Amendment to [withdrawals.md](withdrawals.md) R31–R33, [bank-returns.md](bank-returns.md) R2, R8 and R10, and [deposit-matching.md](deposit-matching.md) R13.

| ID | Rule |
| --- | --- |
| R17 | **Sending.** The send step (withdrawals R32) sends `TradeSettlement` payments too, with the same rules for order, attempts, disabled Tenants and inactive client money accounts. `Accepted` → the payment is `Accepted`; the settlement does not change. `Rejected` → the payment is `Rejected` and the settlement `PaymentFailed` with failure `PaymentRejected` and the reason's name. A failure of the call leaves the payment `Pending` for the next run. |
| R18 | **Settlement paid.** A debit line whose `endToEndId` is the instruction id of a `TradeSettlement` payment of the line's Tenant and client money account, `Pending` with an attempt or `Accepted`, with the same amount: in one database transaction the line becomes `Matched` with the new method `Settlement` (no Account, no Transaction, the trade settlement), the payment `Paid` and the settlement `Completed`. A different amount, or a payment in any other state → `Unmatched` with `PaymentMismatch`. This case joins the classification of bank-returns R2 next to the receipt return. |
| R19 | **Settlement received.** A credit line with no `returnOf` and kind `Transfer`, whose reference text, trimmed and compared without regard to case, equals the broker settlement id of a trade settlement of the line's Tenant and client money account, is settlement cash and is never matched as a deposit. When that settlement is `AwaitingReceipt` and the amount equals its `netAmount`: the line becomes `Matched` with method `Settlement` and the settlement `Completed`. Otherwise (another amount, or a settlement in any other status) the line becomes `Unmatched` with the new reason `SettlementMismatch`, and the settlement does not change. A TenantAdmin handles that line like any receipt, normally with request return (bank-returns R5); the broker then pays the right amount. This check runs after the `Interest` rule (interest-run R40) and before deposit matching. |
| R20 | **Settlement payment returned.** A credit line whose `returnOf` is the debit line of a `Paid` settlement payment, with the same amount: the line becomes `Matched` with method `PaymentReturn` (no Account, no Transaction, the trade settlement), the payment `Returned`, and the settlement `PaymentFailed` with failure `PaymentReturned` and the line's `returnReason`. A different amount → `Unmatched` with `PaymentMismatch`. |
| R21 | **Statement line additions.** Match method `Settlement = 5`; unmatched reason `SettlementMismatch = 13`. A `Matched` line has an Account and a Transaction, or a related line (bank-returns R10), or a trade settlement. The statement line item gains `tradeSettlementId`. A line matched to a trade settlement accepts no action: only R20 undoes it. |

### 4.6 Bank reconciliation

Amendment to [bank-reconciliation.md](bank-reconciliation.md) R9, R10, R13 and R14.

| ID | Rule |
| --- | --- |
| R22 | **Settlement lines.** A line `Matched` to a trade settlement is not an item, like every matched line: its amount equals the principal legs of the settlement's fills. A `SettlementMismatch` line is a `Suspense` break like any unmatched line. |
| R23 | **The cash legs of order fills.** For a run of client money account A, the cash legs of a Transaction with source `OrderFill`, and of the Reversal of one, are split by the fill's order. An order with no settlement account, and a fill in a `SetAside` trade settlement or whose break was resolved by hand (R31): every leg stays in `Trades` (Known), as today. An order whose settlement account is another client money account: `OtherClientMoneyAccount`, as bank-reconciliation R10. An order whose settlement account is A: its `Fee` legs make the new item `TradeFees` (Known), and its `Principal` legs follow the fill's state in the table below. |

| The fill | Item kind for its `Principal` legs | Class |
| --- | --- | --- |
| In a `Completed` trade settlement | none | — |
| In a `PaymentFailed` or `Disputed` trade settlement; or broken and not reversed (R30) | `TradesNeedingAction` | Break |
| Anything else: not settled yet, settled with no `Settlement` report processed yet, or in a settlement that waits for the bank | `UnsettledTrades` | Timing |

| ID | Rule |
| --- | --- |
| R24 | **Amounts and one more item.** Each of the three items is one item per run, with `amount` = minus the sum of its legs and `count` = its Transactions; an item of `0` is left out. A fill reversed by R29 adds a Transaction and a Reversal that cancel. A fill that is still `Pending` in a `Completed` trade settlement has moved the bank and not the ledger: the new item `UnbookedFills` (Break), with `amount` = the sum of `Round(quantity × price, 4)` of those fills, plus for a sell and minus for a buy, and `count` = the fills. The items of a run still add up to its difference. |
| R25 | **What the items mean, and information.** `TradeFees` is the trade fees the firm has charged and not taken out of the trust account; when the firm sweeps them, the bank's debit line goes to suspense and a TenantAdmin ignores it with a note, which offsets the item (as interest-run R42). `Trades` no longer holds any fill that settles through A. A run also records, without making them items, the count and the sum of `netAmount` of A's open trade settlements. Kinds: `UnsettledTrades = 15`, `TradesNeedingAction = 16`, `UnbookedFills = 17`, `TradeFees = 18`. |

### 4.7 A broken trade

Amendment to [orders.md](orders.md) R14, R24, R25, R28, R33, R36–R38, R44 and R47, and [transaction-source.md](transaction-source.md) R7.

| ID | Rule |
| --- | --- |
| R26 | **The `Broken` report.** A new execution report kind: the broker takes back the fill named by the outside id of its `Fill` report (`fillId`, as a `Settled` report names it), with an optional reason cut to 140 characters. It needs a client order id and the fill's outside id; a page with a `Broken` item without them is refused as `InvalidItem`. |
| R27 | **Processing.** In one database transaction with the report's status. No such fill of that order → `Skipped` as `UnknownFill`. A fill already broken → `Repeated`. A fill already settled → `Skipped` with the new reason `FillSettled`; a trade broken after settlement is out of this slice. Otherwise the fill becomes `Broken` with the time and the reason, by R28, R29 or R30. A `Settled` report that names a broken fill is `Skipped` with the new reason `FillBroken`. A break is processed whatever the state of the Tenant, the Customer or the order (orders R29). |
| R28 | **A fill that was not posted.** A `Pending` fill becomes `Broken` with outcome `NotPosted`. Nothing is written to the ledger, and it is no longer tried (orders R36). |
| R29 | **The flow reverses a posted fill.** With the Account row locked, the platform posts `Transaction.Reverse` of the fill's Transaction with source `OrderFill` and the same order PublicId (transaction-source R7), `bookedAt` = platform time, `CreatedBy` the worker's system name. The Reversal negates every leg and copies the execution, fee and FX details, so the fee of that fill is given back. The holdings projection follows. The fill is `Broken` with outcome `Reversed` and the Reversal. The portfolio trade lock and the reserved quantity rule (orders R40) are not checked. |
| R30 | **When it cannot be reversed.** Nothing is posted and the fill is `Broken` with outcome `NotApplied` and the first hold reason that applies: `AccountClosed`; `LaterActivity` (the fill's Transaction is not the latest security activity on its holding, [ledger-write-guards](../fixes/ledger-write-guards.md) Decision 1); `InsufficientCash` (a broken sell whose Reversal would leave available cash below 0, withdrawals R36). It is decided once, when the report is processed, and not tried again. A TenantAdmin corrects the account with an adjustment ([adjustments.md](adjustments.md)) and then resolves the break (R31). |
| R31 | **Resolve by hand.** `POST /orders/{id}/fills/{fillId}/resolve-break` with the order's `rowVersion` and `note` (trimmed, 1–200, required) → 204, only for a fill with outcome `NotApplied` (else 400 `errors.Status`, `This trade has no break that waits for a decision.`). The outcome becomes `ResolvedByHand` with the person, the time and the note. It writes nothing to the ledger; it records that a person has corrected the account. |
| R32 | **The order after a break.** A broken fill no longer counts in `filledQuantity`, `averagePrice`, or, when its outcome is `Reversed`, in the order's posted gross and charged fee. An order that is still open takes the quantity back as open quantity, its reservation is worked out again (orders R39, R40), and it is `PartlyFilled` or `Working` by its remaining fills. An order that was `Filled` becomes `Cancelled` with close reason `Trade broken`; a `Cancelled` or `Expired` order keeps its status. The reservation an open buy order takes back is covered by the cash the Reversal returned, in the same database transaction. |
| R33 | **Fees, cash and closure.** The fees of the order's other fills are not worked out again. The fee of a later fill (orders R33) is never below 0. A broken fill is not unsettled cash (orders R44) once its outcome is `Reversed`, `NotPosted` or `ResolvedByHand`; with `NotApplied` a broken sell still counts, so cash the broker will never pay cannot be withdrawn. Account-closure R5 gains a refusal, 400 `errors.AccountId`: `A broken trade on this account waits for a decision.` `GET /orders?pendingFill=true` also returns orders with a `NotApplied` break. |
| R34 | **Items.** A fill item gains `brokerSettlementId`, `tradeSettlementId` (`null` for a Customer) and `break`: `{ at, reason, outcome, holdReason, reversalTransactionId, resolvedBy, resolvedAt, note }` or `null`; a Customer sees it without `note`. `status` may be `Broken`. `execution` on the transaction item (orders R38) gains `brokenAt`. A hand reverse of a fill or of its Reversal stays refused (orders R37): `OrderFill` is not a hand-reversible source. |

### 4.8 The simulator broker (S2 remainder)

Amendment to [orders.md](orders.md) R53, R61, R63–R65 and [simulator.md](simulator.md). The simulator bank needs no new route.

| ID | Rule |
| --- | --- |
| R35 | **Settlement account on an order.** `POST /party/broker/orders` takes an optional `settlementAccount` (the shape of deposit-matching R38). It is part of the body the idempotency check compares only when it is sent. The broker stores it as text and does not read the platform (simulator R33). |
| R36 | **Broker settlements.** When fills settle (orders R61), the fills of orders with a settlement account are grouped by settlement account, currency and settlement date into one **broker settlement** each. Its id is the first 8 hexadecimal digits of the world id, `-S` and `n`, `n` counting the settlements of the world from 1, so it fits a bank reference of 18 characters. Each fill's `Settled` report carries it, and one `Settlement` report follows them with `netAmount` = the sum of `Round(quantity × price, 4)`, plus for a sell and minus for a buy, and `fillCount`. A fill of an order without a settlement account settles as before, with no settlement id and no `Settlement` report. |
| R37 | **The broker pays.** Configuration `Simulator:Broker:BankAccounts` lists the broker's own bank accounts (`Currency`, `AccountNumber`, `Name`); a bad entry stops start-up. With `netAmount > 0` and settlement mode `Pay`, the broker pays at once: a credit line on the trust account at the simulator bank, amount = the net, reference = the settlement id, payer = the broker's account for the currency (name `Simulator Broker` and no account when none is configured), entering the feed at once. When the settlement account is not an open account of the simulator bank in that currency, no line is added and the settlement shows `paid: false`. |
| R38 | **Settlement mode.** `POST /control/broker/settlement-mode` `{ "mode": "Pay" | "Hold" }` (default `Pay`; log action `BrokerSettlementModeChanged`). With `Hold` the broker settles and reports but pays nothing until `POST /control/broker/settlements/{settlementId}/pay` `{ "amount"? }`: `amount` defaults to the net and must be `> 0` with at most 4 decimal places (400 `errors.Amount`), so the operator can pay a wrong amount and then the right one. Only a settlement with `netAmount > 0` (400 `errors.Status`); unknown → 404; an unknown trust account → 400 `errors.SettlementAccount`. Log action `BrokerSettlementPaid`. |
| R39 | **Its own books.** `GET /control/broker/settlements` lists the settlements newest first (envelope as simulator R28): id, settlement account, currency, settlement date, `netAmount`, `fillCount`, `paid` and `paidAmount` (for a net above 0, what the broker paid; for a net below 0, the executed and not returned payments of the simulator bank from that trust account whose reference is the settlement id). `GET /control/broker` shows the settlement mode. |
| R40 | **Break a fill.** `POST /control/broker/fills/{fillReportId}/break` `{ "reason"? }` (1–140, default `Broken by the exchange`), where `fillReportId` is the outside id of the fill's `Fill` report. Only a fill that is neither settled nor broken (400 `errors.Status`); unknown → 404. The fill is broken and never settles, and a `Broken` report is appended. A working order takes the quantity back and stays working; a `Filled` order becomes `Cancelled`, with no report of its own. Log action `BrokerFillBroken`. |
| R41 | **Feed item.** The wire item (orders R63) gains `settlementId`, `settlementAccount`, `currency`, `netAmount` (a JSON number) and `fillCount`; `kind` may be `Broken` or `Settlement`. A field a kind does not use is `null`. |
| R42 | **Reset.** A reset empties the settlements and sets the mode to `Pay` (simulator R24). |

### 4.9 The platform adapters

| ID | Rule |
| --- | --- |
| R43 | **`SimulatorBrokerOrders` and `SimulatorBrokerFeed`.** The place body carries `settlementAccount` only when the order has one. The feed adapter maps `Broken` and `Settlement` to their kinds and reads the five new fields; a `netAmount` that is not a JSON number or a `fillCount` that is not a whole number comes through empty, so the pull refuses the page with `InvalidItem` (orders R67). |
| R44 | **`SimulatorBankPayments`.** A `TradeSettlement` payment is sent as a payment with a payee (withdrawals R49); the reference is left out when the settlement has none. No wire change. |

Policies: `trade-settlements.manage` (new). Grants: TenantAdmin only.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `TradeSettlement` | Aggregate (new) | `TenantId`, `PublicId`, `ClientMoneyAccountId`, `Connection`, `BrokerSettlementId`, `Currency`, `SettledDate`, `FillCount`, `NetAmount`, broker figures (`BrokerFillCount`, `BrokerNetAmount`, `BrokerSettlementAccount`), `ExecutionReportId`, `Status`, `Failure?`, `FailureDetail?`, `CompletedAt?`, set aside (`ByUserId`, `At`, `Note`), `RowVersion`. `Create(...)` decides `Disputed`, `Completed`, `AwaitingPayment`, `AwaitingReceipt` or `PaymentFailed` (R9, R11–R13); `Complete(at)`, `FailPayment(failure, detail)`, `PayAgain()`, `SetAside(by, note, at)`. Holds R10, R14, R15 |
| `TradeSettlementStatus`, `TradeSettlementFailure` | Enums (new) | `AwaitingPayment = 0`, `AwaitingReceipt = 1`, `Completed = 2`, `PaymentFailed = 3`, `Disputed = 4`, `SetAside = 5`; `PaymentRejected = 0`, `PaymentReturned = 1`, `NoBrokerAccount = 2` |
| `Order` | Aggregate (existing) | `ClientMoneyAccountId?`, `SettlementAccountNumber?`, both or neither, set by `Place` (R3). `BreakFill(fill, …)` applies R32 |
| `OrderFill` | Entity (existing) | `BrokerSettlementId?`, `TradeSettlementId?`; break: `BrokenAt`, `BreakReportId`, `BreakReason?`, `BreakOutcome`, `BreakHoldReason?`, `ReversalTransactionId?`, resolved (`ByUserId`, `At`, `Note`). `Break(...)` once, never on a settled fill; `ResolveBreak(by, note, at)` only from `NotApplied` |
| `OrderFillStatus` | Enum (existing) | Add `Broken = 2` |
| `FillBreakOutcome`, `FillBreakHoldReason` | Enums (new) | `Reversed = 0`, `NotPosted = 1`, `NotApplied = 2`, `ResolvedByHand = 3`; `AccountClosed = 0`, `LaterActivity = 1`, `InsufficientCash = 2` |
| `ExecutionReportKind`, `ExecutionReportSkipReason` | Enums (existing) | Add `Broken = 4`, `Settlement = 5`; add `FillSettled = 5`, `FillBroken = 6`, `UnknownSettlement = 7` |
| `ExecutionReport` | Aggregate (existing) | `SettlementId?`, `SettlementAccount?`, `Currency?`, `NetAmount?`, `FillCount?` |
| `OutgoingPaymentKind`, `OutgoingPayment` | Enum, aggregate (existing) | Add `TradeSettlement = 2`; `TradeSettlementId?`; `ForTradeSettlement(settlement, payee, reference?)`. Exactly one of a withdrawal, a returned line and a trade settlement, by kind |
| `StatementLineMatchMethod`, `UnmatchedReason` | Enums (existing) | Add `Settlement = 5`; add `SettlementMismatch = 13` |
| `BankStatementLine` | Aggregate (existing) | `TradeSettlementId?`; `MarkSettlement(tradeSettlementId, at)`; `MarkPaymentReturn` accepts a trade settlement in place of an Account and a Transaction |
| `ReconciliationItemKind` | Enum (existing) | Add `UnsettledTrades = 15` (Timing), `TradesNeedingAction = 16` (Break), `UnbookedFills = 17` (Break), `TradeFees = 18` (Known) |
| `BankReconciliation`, `ReconciliationInputs` | Aggregate, value object (existing) | The four sums with their counts, and the open trade settlements (count, amount); `Compute` builds the items of R23–R25 |
| `Transaction` | Aggregate (existing) | No change. `Reverse` already copies the execution detail (orders R35) and takes `hasLaterSecurityActivity` |

Invariants:

- A trade settlement is unique on (connection, broker settlement id, client money account), and a fill is in at most one. Held by Application (R8) and two unique indexes.
- A trade settlement has at most one open outgoing payment. Held by R14 and a filtered unique index.
- A trade settlement is `Completed` by exactly one statement line, or by a net of 0. Held by R18 and R19 under the settlement's `RowVersion`.
- A fill is broken at most once, and never after it settled. Held by `OrderFill.Break` and R27.
- A break posts at most one Reversal, and only while the fill's Transaction is the latest security activity on its holding. Held by R29 and R30.
- For an `Agreed` or `Breaks` run, the items still add up to the difference. Held by `Compute`.

Domain events: none new. The Reversal of a broken fill raises `TransactionPosted` as every Transaction does.

Recorded in [domain-model.md](../domain-model.md) §8.9, §8.11, §8.12, §8.16 and a new §8.17.

---

## 6. Database

Forward only. Do not add EF migrations.

### 6.1 Script `0052_trade_settlement.sql`

| Table | Change | Index / FK |
| --- | --- | --- |
| `TradeSettlements` | Add: `Id`, `PublicId`, `TenantId`, `ClientMoneyAccountId`, `Connection nvarchar(50)`, `BrokerSettlementId nvarchar(100)`, `Currency char(3)`, `SettledDate date`, `FillCount int`, `NetAmount decimal(18,4)`, `BrokerFillCount int`, `BrokerNetAmount decimal(18,4)`, `BrokerSettlementAccount nvarchar(34)`, `ExecutionReportId bigint`, `Status int` CHECK 0–5, `Failure int NULL` CHECK 0–2 (set iff `Status = 3`), `FailureDetail nvarchar(140) NULL`, `CompletedAt datetimeoffset NULL` (set iff `Status = 2`), `SetAsideByUserId int NULL`, `SetAsideAt datetimeoffset NULL`, `SetAsideNote nvarchar(200) NULL` (all set iff `Status = 5`), audit columns, `RowVersion` | PK `Id`. UQ `PublicId`. UQ `(TenantId, Id)`. UQ `(Connection, BrokerSettlementId, ClientMoneyAccountId)`. FK `TenantId` → `Tenants`; `(TenantId, ClientMoneyAccountId)` → `ClientMoneyAccounts`; `ExecutionReportId` → `ExecutionReports`; `(TenantId, SetAsideByUserId)` → `Users`; all RESTRICT. Index `(TenantId, Status, Id DESC)` |
| `Orders` | Add `ClientMoneyAccountId int NULL`, `SettlementAccountNumber nvarchar(34) NULL`; CHECK both or neither | FK `(TenantId, ClientMoneyAccountId)` → `ClientMoneyAccounts` RESTRICT |
| `OrderFills` | Add `BrokerSettlementId nvarchar(100) NULL`, `TradeSettlementId int NULL`, `BrokenAt datetimeoffset NULL`, `BreakReportId bigint NULL`, `BreakReason nvarchar(140) NULL`, `BreakOutcome int NULL` CHECK 0–3, `BreakHoldReason int NULL` CHECK 0–2 (`NULL` unless `BreakOutcome` is 2 or 3), `ReversalTransactionId int NULL` (set iff `BreakOutcome = 0`), `BreakResolvedByUserId int NULL`, `BreakResolvedAt datetimeoffset NULL`, `BreakNote nvarchar(200) NULL`. Widen `Status` to 0–2; `BrokenAt`, `BreakReportId` and `BreakOutcome` set iff `Status = 2`. Replace the `TransactionId` CHECK: set when `Status = 1`, `NULL` when `Status = 0` | FK `(TenantId, TradeSettlementId)` → `TradeSettlements`, `(TenantId, ReversalTransactionId)` → `Transactions`, `BreakReportId` → `ExecutionReports`, `(TenantId, BreakResolvedByUserId)` → `Users`, all RESTRICT. Filtered UQ `BreakReportId` `WHERE BreakReportId IS NOT NULL`. Index `(TradeSettlementId)`; filtered index `(BrokerSettlementId) WHERE TradeSettlementId IS NULL AND BrokerSettlementId IS NOT NULL` |
| `ExecutionReports` | Add `SettlementId nvarchar(100) NULL`, `SettlementAccount nvarchar(34) NULL`, `Currency char(3) NULL`, `NetAmount decimal(18,4) NULL`, `FillCount int NULL`. Widen `Kind` to 0–5 and `SkipReason` to 0–7 | none |
| `OutgoingPayments` | Add `TradeSettlementId int NULL`. Widen `Kind` to 0–2. Replace `CK_OutgoingPayments_Target` with three arms: exactly one of `WithdrawalId`, `ReturnOfLineId` and `TradeSettlementId`, by kind | FK `(TenantId, TradeSettlementId)` → `TradeSettlements` RESTRICT; filtered UQ `TradeSettlementId` `WHERE Kind = 2 AND Status IN (0, 1)` |
| `BankStatementLines` | Add `TradeSettlementId int NULL`. Widen `MatchMethod` to 0–5 and `UnmatchedReason` to 0–13. Replace the Matched CHECK: `Status = 1` needs `MatchMethod` and `MatchedAt` and exactly one of (an Account and a Transaction), (a related line with no Account) and (a trade settlement) | FK `(TenantId, TradeSettlementId)` → `TradeSettlements` RESTRICT |
| `BankReconciliationItems` | Widen `Kind` to 0–18 | none |
| `BankReconciliations` | Add `OpenSettlementsCount int NOT NULL` and `OpenSettlementsAmount decimal(18,4) NOT NULL`, default 0 for existing rows | none |

No back-fill. Existing orders have no settlement account, existing fills no settlement id and no break, existing payments keep their kind, and stored reconciliation runs keep their items.

### 6.2 Simulator script `database/simulator/0011_broker_settlement.sql`

| Table | Content |
| --- | --- |
| `BrokerSettings` | Add `SettlementMode int NOT NULL` CHECK 0–1, default 0 (`Pay`) |
| `BrokerOrders` | Add `SettlementAccount nvarchar(34) NULL` |
| `BrokerSettlements` | New: `Id int` identity PK, `Number int` (unique), `SettlementAccount nvarchar(34)`, `Currency char(3)`, `SettlementDate date`, `NetAmount decimal(18,4)`, `FillCount int`, `PaidAmount decimal(18,4)` |
| `BrokerFills` | Add `Broken bit NOT NULL` default 0, `SettlementId int NULL` FK `BrokerSettlements` |
| `BrokerReports` | Add `SettlementId int NULL` FK `BrokerSettlements`; widen the `Kind` CHECK to the two new kinds |

Recorded in [database-design.md](../database-design.md) §11.9, §11.11, §11.12, §11.16, a new §11.17 and §15.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Command | `PlaceOrder` | Sets the settlement account (R3). No new refusal |
| Service | `SendBrokerOrders` | Sends `settlementAccount` (R4) |
| Service | `ProcessExecutionReports` | `Settled` stores the settlement id (R6); `Settlement` creates trade settlements and the payment (R8–R13); `Broken` (R26–R30, R32); the new skip reasons |
| Service | `PostOrderFills` | Skips a broken fill; the fee of a fill is never below 0 (R28, R33). Gains the one place a fill's Transaction is reversed (R29, R30) |
| Service | `SendBankPayments` | Sends `TradeSettlement` payments, with the payee from the broker connection configuration (R17) |
| Service | `ProcessStatementLines` | Settlement paid, received and returned (R18–R20) |
| Service | `ReconcileClientMoneyAccount` | Reads the four sums and the open settlements (R23–R25) |
| Command | `PayTradeSettlement`, `SetAsideTradeSettlement` (new) | `trade-settlements.manage`; R14, R15 |
| Command | `ResolveBrokenFill` (new) | `transactions.adjust`; R31 |
| Command | `RequestAccountClosure` | The refusal of R33 |
| Query | `GetTradeSettlements`, `GetTradeSettlementById` (new) | `trade-settlements.manage`; R16 |
| Query | `GetOrders`, `GetOrderById`, `GetTransactions`, `GetTransactionById`, `GetStatementLines`, `GetStatementLineById`, `GetBrokerConnections`, bank reconciliation reads, account reads | Map the new fields; unsettled cash by R33 |
| Port | `IBrokerOrders`, `IBrokerFeed`, `IBankPayments` | No new member. The order request gains the settlement account; the report gains the five fields |

Where each rule lives:

| Rule | Where |
| --- | --- |
| Does an order settle through the bank (R3) | `PlaceOrder`, with the client money accounts and `BrokerConnectionsConfiguration` |
| The platform's net, agreed or disputed, the first status (R9, R11–R13) | `TradeSettlement.Create` |
| Can a broken fill be reversed (R30) | `PostOrderFills`, before `Transaction.Reverse`, with the later-activity query that `ReverseTransaction` uses |
| The order after a break (R32) | `Order.BreakFill` |
| The split of fill legs (R23, R24) | `ReconcileClientMoneyAccount` (SQL sums joined through `Transactions.SourcePublicId` → `Orders.PublicId` and the execution detail's fill → `OrderFills` → `TradeSettlements`), then `BankReconciliation.Compute` |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/trade-settlements` | `trade-settlements.manage` | 200 envelope | 400 / 401 / 403 |
| GET | `/trade-settlements/{id}` | `trade-settlements.manage` | 200 item | 401 / 403 / 404 |
| POST | `/trade-settlements/{id}/pay` | `trade-settlements.manage` | 204 | 400 / 401 / 403 / 404 / 409 |
| POST | `/trade-settlements/{id}/set-aside` | `trade-settlements.manage` | 204 | 400 / 401 / 403 / 404 / 409 |
| POST | `/orders/{id}/fills/{fillId}/resolve-break` | `transactions.adjust` | 204 | 400 / 401 / 403 / 404 / 409 |
| GET | `/orders`, `/orders/{id}` | unchanged | fill has `brokerSettlementId`, `tradeSettlementId`, `break`; `status` may be `Broken` | — |
| GET | `/transactions`, `/transactions/{id}` | unchanged | `execution` has `brokenAt` | — |
| GET | `/statement-lines[/{id}]` | unchanged | `tradeSettlementId`; method `Settlement`; reason `SettlementMismatch` | — |
| GET | `/bank-reconciliations[/{id}]` | unchanged | four item kinds; `openSettlements: { count, amount }` | — |
| GET | `/broker-connections`, `/execution-reports` | unchanged | `settlementCurrencies`; the two kinds, three skip reasons and five fields | — |

List envelope: `{ items, page, pageSize, totalCount }`; page default 1, pageSize default 20, max 100.

Simulator, control port: `POST /control/broker/settlement-mode`, `GET /control/broker/settlements`, `POST /control/broker/settlements/{settlementId}/pay`, `POST /control/broker/fills/{fillReportId}/break`. Party port: no new route; the order body and the feed item gain fields.

### 8.1 Bodies

Trade settlement item (the list item has no `fills` and no `payments`):

```json
{
  "id": "<trade settlement publicId>",
  "clientMoneyAccount": { "id": "<publicId>", "currency": "USD", "accountNumber": "SIM-DEMO-USD" },
  "connection": "simulator",
  "brokerSettlementId": "0f8fad5b-S3",
  "settledDate": "2026-11-25",
  "fillCount": 3,
  "netAmount": -1001.5000,
  "broker": { "fillCount": 3, "netAmount": -1001.5000, "settlementAccount": "SIM-DEMO-USD" },
  "status": "AwaitingPayment",
  "failure": null,
  "failureDetail": null,
  "completedAt": null,
  "statementLineId": null,
  "setAside": null,
  "payments": [ { "status": "Accepted", "attempts": 1, "lastFailure": null, "createdAt": "2026-11-25T02:00:04Z" } ],
  "fills": [ { "id": "<fill publicId>", "orderId": "<order publicId>", "accountId": "<account publicId>", "side": "Buy", "quantity": 4.00000000, "price": 100.12000000, "gross": 400.4800, "status": "Posted" } ],
  "rowVersion": "<base64>"
}
```

Set aside: `{ "rowVersion": "<base64>", "note": "Agreed with the broker by phone, ref 4471" }`. Pay again: `{ "rowVersion": "<base64>" }`. Resolve a break: `{ "rowVersion": "<order rowVersion>", "note": "Corrected with adjustment 2026-11-24" }`.

The break of a fill: `"status": "Broken", "break": { "at": "2026-11-23T04:10:02Z", "reason": "Broken by the exchange", "outcome": "Reversed", "holdReason": null, "reversalTransactionId": "<transaction publicId>", "resolvedBy": null, "resolvedAt": null, "note": null }`.

Broker feed, the two new kinds (fields not shown are `null`):

```json
{ "id": "0f8fad5bd9cb469fa16570867728950e-X9", "kind": "Settlement", "settlementId": "0f8fad5b-S3", "settlementAccount": "SIM-DEMO-USD", "currency": "USD", "settledDate": "2026-11-25", "netAmount": -1001.5, "fillCount": 3 }
```

```json
{ "id": "0f8fad5bd9cb469fa16570867728950e-X4", "kind": "Broken", "clientOrderId": "<order publicId>", "brokerOrderId": "0f8fad5bd9cb469fa16570867728950e-O1", "fillId": "0f8fad5bd9cb469fa16570867728950e-X1", "reason": "Broken by the exchange" }
```

### 8.2 Errors

All refusals are ordinary validation (400 with `errors`, no problem `code`) except where named.

| Case | Response |
| --- | --- |
| Caller without the permission of the route | 403 |
| A trade settlement, order or fill outside scope or of another Tenant, or a missing one | 404 |
| `tenantId` in a query or body | 400 |
| Pay again from a status other than `PaymentFailed`; set aside from `Completed`, `SetAside`, or after a send attempt; no broker account configured | 400 `errors.Status` |
| A missing or too long `note` on set aside or resolve | 400 `errors.Note` |
| Resolve a fill with no break that waits | 400 `errors.Status` |
| Missing `rowVersion`; stale | 400; 409 `stale_row_version` |
| Unknown `status` filter | 400 `errors.Status` |
| Account closure with a break that waits | 400 `errors.AccountId` |

Cross-tenant or wrong-collection id → **404**, not 403. Keep the resource catalog in [api-design.md](../api-design.md) pointing here for these rules.

---

## 9. UI

Page contracts amended when this spec was accepted: [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (D76–D78), [customer-portal.md](../customer-portal/customer-portal.md) (C36) and [back-office.md](../backoffice/back-office.md) (BO29). The Adviser Portal BFF gains the prefix `/trade-settlements` ([bff-adviser-portal.md](bff-adviser-portal.md) R20).

| ID | Behaviour |
| --- | --- |
| D76 | A TenantAdmin sees a nav item "Trade settlements" (`/trade-settlements`); an Adviser does not. The list: Settled date, Broker reference, Currency, Trades, Net ("Pay broker 1,001.50" or "Receive 2,340.00"), Status, with a status filter, default "Open". Labels: AwaitingPayment "Paying the broker", AwaitingReceipt "Waiting for the broker's payment", Completed "Completed", PaymentFailed "Payment failed", Disputed "Figures differ", SetAside "Set aside". `/trade-settlements/:id` shows the platform's and the broker's figures side by side (a difference in red), the fills with links to their orders, the payments with the labels of D46, the failure ("The bank refused the payment: <reason>", "The bank returned the payment: <reason>", "The broker connection has no bank account in <currency>"), the matched statement line as a link, and the set-aside note. "Pay again" on `PaymentFailed`; "Set aside" with a required note and the sentence "The platform stops waiting for this settlement. Its trades are then shown in the bank reconciliation as not settled through the trust account." |
| D77 | The fills table of `/orders/:id` (D72) shows a broken fill struck through with "Cancelled by the broker <date>: <reason>" and, by outcome: Reversed "Reversed" with a link to the Reversal; NotPosted "Was not booked"; NotApplied "Could not be reversed" with the reason (AccountClosed "The account is closed", LaterActivity "Later activity on this holding", InsufficientCash "The proceeds have been spent") and, for a TenantAdmin, "Correct the account with an adjustment, then mark this resolved." with a "Mark resolved" action that takes a note; ResolvedByHand "Resolved by <name>". "Needs attention" (D71) also marks an order with a NotApplied break. `/transactions/:id` of a broken fill shows "Cancelled by the broker" in the Trade section (D74). The Close account page shows the new refusal. |
| D78 | Bank receipts pages (D34, D50): method label Settlement "Trade settlement" with a link to it for a TenantAdmin; reason label SettlementMismatch "Does not match the trade settlement", with the sentence "Return it to the broker, or ignore it with a note." Bank reconciliation (D51): kind labels UnsettledTrades "Trades waiting for settlement", TradesNeedingAction "Trades that need action" (linking to `/trade-settlements?open=true`), UnbookedFills "Settled trades not booked yet", TradeFees "Trade fees held in the trust account"; Trades becomes "Trades not settled through the trust account"; under the items, "Open trade settlements: <n>, net <amount>". |
| C36 | Customer Portal, account activity: the Reversal of a fill is labelled "Trade cancelled by the broker: Bought 4 ACME.SIM at 100.12", and the fill's own row shows "Cancelled" in place of the settlement text. A broken fill that was never booked shows nothing. |
| BO29 | Back Office Broker page (BO27, BO28): the connection row shows "Settles in USD, NZD" from `settlementCurrencies`; the report list shows kinds `Broken` and `Settlement` with the settlement id, and the reason labels FillSettled "The trade has already settled", FillBroken "The trade was cancelled", UnknownSettlement "No trades for this settlement". `FillSettled` and `UnknownSettlement` rows are red. |

### 9.1 Seeds and hosting

- AppHost: `webapi` and `worker` get `BrokerConnections__simulator__BankAccounts__USD__AccountNumber` and `__AccountName` (and the same for the other currencies the Development seed opens client money accounts in); the simulator gets the same numbers in `Simulator:Broker:BankAccounts`. TestAppHost: none; the functional tests that need one configure it on their host.
- VPS: `compose.yml` passes the same entries from `.env`. No new container, secret, job, queue or hostname.
- `TestSeed` and `webapi --seed-demo`: no change. Seeded trades keep source `Seed` and stay in `Trades`.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `TradeSettlement.Create`: equal figures and a net below 0, above 0 and of 0 give `AwaitingPayment`, `AwaitingReceipt`, `Completed`; another count, net, account or currency gives `Disputed`; no broker account gives `PaymentFailed`. Every move of R10 and each refused one; set aside twice throws. `OrderFill.Break` once, not on a settled fill; `ResolveBreak` only from `NotApplied`. `Order.BreakFill`: an open order takes the quantity back and its reservation grows; a `Filled` order becomes `Cancelled`; a `Cancelled` order stays; posted gross and charged fee fall only with `Reversed`. The fee of a later fill is never below 0. `OutgoingPayment.ForTradeSettlement`. `BankStatementLine.MarkSettlement`. `BankReconciliation.Compute`: each row of the R23 table, `TradeFees`, `UnbookedFills`, a reversed fill adds nothing, the items add up to the difference |
| Application.FunctionalTests — settlement account | An order in the account's currency with an active client money account and a configured broker account has a settlement account and sends it; each of the three conditions missing gives none and still places; a sell in another currency has none. Two-tenant: tenant B's client money account is never used |
| Application.FunctionalTests — trade settlements | With fakes for `IBrokerFeed`, `IBankPayments` and `IBankFeed`. `Settled` reports with an id then a `Settlement` report create one trade settlement with the platform's net; a `Pending` fill counts. Net below 0: a `TradeSettlement` payment with its own instruction id, the configured payee and the reference; `Accepted` leaves `AwaitingPayment`; the debit line → line `Matched` (`Settlement`), payment `Paid`, settlement `Completed`; a different amount → `PaymentMismatch`; `Rejected` → `PaymentFailed`; pay again → a second payment with a new instruction id; a returned payment → `PaymentFailed`. Net above 0: the credit line with the settlement id → `Completed`; another amount → `SettlementMismatch` and the settlement unchanged; a second line → `SettlementMismatch`; the line is never matched to an account that has the same text as deposit reference. Net 0 → `Completed`. Other figures → `Disputed`, nothing paid. A `Settlement` report with no fills → `UnknownSettlement`; twice → `Repeated`. Set aside from each allowed status, refused after a send attempt; the unsent payment is `Cancelled`. Reads and filters. Adviser, Customer, SystemAdmin → 403; no token → 401; tenant B's settlement id → 404 on every route. Withdrawable cash rises at the `Settled` report, before the bank line |
| Application.FunctionalTests — reconciliation | A posted buy and sell with a settlement account: `UnsettledTrades` and `TradeFees`; after the settlement completes: only `TradeFees`, run `Agreed`. `PaymentFailed` and `Disputed` → `TradesNeedingAction`, run `Breaks`. Set aside → `Trades`. A `Pending` fill in a completed settlement → `UnbookedFills`. An order without a settlement account, a rebalance trade → `Trades`. A reversed broken fill adds nothing; a `NotApplied` one → `TradesNeedingAction`; resolved → `Trades`. `openSettlements` on the run. Every run's items add up to the difference |
| Application.FunctionalTests — broken trades | A `Broken` report for a posted buy: one Reversal with source `OrderFill`, the order's id and the execution detail; cash, holding and the fee are back; the fill is `Broken` / `Reversed`. For a `Pending` fill: `NotPosted`, nothing posted, never tried again. With a later fill on the holding → `NotApplied` / `LaterActivity`; a broken sell whose proceeds were spent → `InsufficientCash`; a Closed account → `AccountClosed`; nothing posted, and a second run posts nothing. Resolve: 204 for a TenantAdmin, 403 for an Adviser, 400 with no waiting break. An open order takes the quantity back and can be filled again without `Overfill`; a `Filled` order becomes `Cancelled`. A `Broken` report twice → `Repeated`; for a settled fill → `FillSettled`; `Settled` for a broken fill → `FillBroken`. A broken sell with `NotApplied` stays in `unsettledCash`; account closure → 400. Hand reverse of the Reversal → 400. `pendingFill=true` returns the order. The Customer reads `break` without the note |
| Infrastructure.IntegrationTests | `0052`: the unique indexes (one trade settlement per connection, id and account; one open payment per settlement; one fill per break report); the status and "set iff" CHECKs; the three arms of the payment target CHECK and of the Matched CHECK; `Kind = 6` on a report violates; existing rows unchanged. EF round-trips. `SimulatorBrokerFeed`: a good `Settlement` and `Broken` item; a `netAmount` that is not a number → `InvalidItem` on the pull. `SimulatorBrokerOrders`: `settlementAccount` in the body, left out when none. Start-up fails on a bad broker bank account entry |
| Simulator.Tests | `0011` applies. An order with a settlement account: on the settlement date its fills get one settlement id per account, currency and date, one `Settlement` report follows the `Settled` reports, the net and the count are right; an order without one settles as before. Net above 0 in mode `Pay` → a credit line on the trust account with the id as reference; an unknown trust account → no line, `paid: false`. Mode `Hold` → no line; pay with an amount and again. `paidAmount` of a net below 0 follows an executed and a returned bank payment. Break: a `Broken` report; a working order takes the quantity back; a `Filled` order is `Cancelled`; a settled or broken fill → 400; a broken fill never settles and is in no settlement. The same order body without `settlementAccount` replays its first answer. Reset. The control log rows |
| Adviser Portal, Customer Portal, Back Office | D76–D78; C36; BO29 |

---

## 11. Locked in this spec

No new ADR. ADR 0021 applies as written: rule 3 (the broker's account is configuration), 7 and 8 (the `Settlement` and `Broken` reports are stored, then processed), 9 (the Reversal has the flow's source), 12–14 (the payment is sent by the `worker`, idempotent on its instruction id, and confirmed by the statement line). Roadmap P6 and P7 apply.

| Item | Lock |
| --- | --- |
| Settlement account | Decided when the order is placed, sent with it; same currency, an active client money account, a configured broker account; never a refusal |
| Broker settlement | The broker nets per settlement account, currency and settlement date; `settlementId` on `Settled`, one `Settlement` report with the net and the count |
| Trade settlement | The platform's own net; `Disputed` when it differs; `AwaitingPayment`, `AwaitingReceipt`, `Completed`, `PaymentFailed`, `SetAside` |
| Cash | An outgoing payment of kind `TradeSettlement` with no approval; a receipt named by the settlement id; matched lines post nothing |
| Reconciliation | `UnsettledTrades` (Timing), `TradesNeedingAction` and `UnbookedFills` (Break), `TradeFees` (Known); `Trades` keeps everything that does not settle through the trust account |
| Broken trade | Report kind `Broken`; the flow reverses once, while the fill is the latest security activity and the cash allows; otherwise an adjustment and resolve by hand |
| Scripts | `0052_trade_settlement.sql`; simulator `0011_broker_settlement.sql` |
| Permissions | `trade-settlements.manage`; `transactions.adjust` for resolve |
| Ports, jobs, problem codes, events, `TransactionType`, source kinds | None added |

### Points confirmed

Each point states the decision, written into the rules above, and the alternative. All sixteen were confirmed on 2026-11-23 as recommended.

1. **One spec for both halves of step 11b.** Recommended: settlement cash and broken trades touch the same reports, fills and reconciliation rules, and a broken fill must be kept out of a settlement. Alternative: two specs, settlement cash first.
2. **The order carries its settlement account, and only some orders settle through the bank** (R3–R5). Recommended: the broker must know which trust account a trade settles against, and orders §11 point 3 gave it no firm; the trust account number on the order is the smallest addition. An order that cannot settle through the bank is still placed and stays in `Trades`, so the slice never blocks a trade. Alternatives: a broker account per Tenant, registered at the broker; refuse an order that cannot settle through the bank.
3. **The broker nets and reports the settlement; the platform works out its own net and compares** (R6–R9). Recommended: one payment per day and currency is how a firm and its broker settle, and the `Settlement` report tells the platform that the batch is complete, so it never pays part of one. Comparing first means the platform pays only a figure both sides hold. Alternatives: settle each fill gross; let the platform define the batches and tell the broker.
4. **`Settled` still decides withdrawable cash; the cash at the bank follows** (R6). Recommended: orders R43–R46 stay as accepted, and the broker's receipt arrives in the same or the next run. Alternative: a sale's proceeds become withdrawable only when its trade settlement is `Completed`, which protects the trust account from paying out cash the broker has not paid yet, at the cost of a second meaning of "settled".
5. **The payment to the broker needs no approval** (R12). Recommended: the payee is a standing account in configuration that no person types, the amount is the platform's own net agreed with the broker's, and a daily approval would hold up every settlement. Alternative: a TenantAdmin approves each settlement payment, as for a withdrawal.
6. **Settlement amounts keep the ledger's 4 decimals** (R9). Recommended: a statement line may carry 4 decimals (deposit-matching R10) and the simulator bank pays them, so a settlement line equals its fills' legs exactly. Alternative: round the net to the currency's minor unit, as a real bank pays, and list the rounding as a known difference.
7. **Trade fees stay in the trust account, as the known difference `TradeFees`** (R23, R25). Recommended: the same treatment as the tax withheld on interest; sweeping the firm's money out is its own later item. Alternative: a fee sweep in this slice.
8. **A failed settlement payment is paid again with a new instruction id, and "Set aside" is the manual end** (R14, R15). Recommended: as a receipt return after a rejection (bank-returns §11 point 8), and as "ignore" ends a statement line: a note, no ledger write, and the fills go back to `Trades`. Alternative: no manual end; a settlement that cannot complete stays a break for ever.
9. **A receipt that does not match goes to suspense and is not matched by hand** (R19). Recommended: the existing return flow sends it back and the broker pays again; a hand match would accept a difference the reconciliation then has to carry. Alternative: a TenantAdmin matches a line to a settlement with a difference item.
10. **Reconciliation classes** (R23–R25). Recommended: cash that will move by itself is timing; a settlement or a break that needs a person, and a fill settled but not booked, are breaks; fees are known. Alternative: unsettled trades as known, as `Trades` is today.
11. **A broken trade is reversed once, when its report is processed; otherwise it waits for a person** (R29–R31). Recommended: as a recall (bank-returns R3): no balance goes negative and the latest-security-activity rule holds, because average cost cannot take an earlier trade out from under later ones. Deciding once means an adjustment made in the meantime is never followed by a late automatic Reversal. Alternative: try again on every run, as a fill that cannot be posted (orders R36).
12. **A broken fill's quantity goes back to an order that is still open; a `Filled` order becomes `Cancelled`** (R32, R40). Recommended: it needs no new figure on the order on either side. Alternative: the quantity is never worked again, with a `brokenQuantity` on the order.
13. **The fees of the order's other fills are not worked out again** (R33). Recommended: a Reversal is an exact negation, the break is the broker's doing, and an open order catches up on its next fill. Alternative: charge the difference on the remaining fills.
14. **A trade broken after it settled is skipped** (R27, R40). Recommended: it is rare, and it needs cash to go back through a new settlement; the report is stored and shown in red. Alternative: reverse it and open a settlement the other way.
15. **Permissions** (R1). Recommended: `trade-settlements.manage` for TenantAdmin only, as the bank pages (roadmap D5); resolve uses `transactions.adjust`, because the person who may correct the account is the one who can say it was corrected. Alternative: a read permission for Advisers.
16. **The simulator: a settlement mode, a pay action with an amount, and breaking a fill; no scenario library** (R35–R42). Recommended: the smallest additions that show every path (roadmap P11). Alternative: breaking settled fills; a broker that checks its incoming payments.

Still open, not this slice:

- A trade broken after it settled
- Settling sells in another currency than their account's, and multi-currency cash
- Rounding settlement amounts to the minor unit
- Sweeping trade fees out of the client money account; the broker's commission
- Withdrawable cash that waits for the settlement's cash (point 4)
- Rebalance through orders (roadmap O3), which then settles the same way
- Custodian positions and their reconciliation (roadmap T3)
- A real broker and bank adapter, and how a real broker reports a settlement

---

## 12. Suggested commits

The repository should build after each commit. Agents split each behaviour into red then green.

1. Script `0052`, the new enums and enum values, `TradeSettlement` and its moves (R9–R15) + Domain unit tests and the `0052` integration tests.
2. The broker's bank accounts in configuration and `settlementCurrencies` (R2) + functional and start-up tests.
3. The settlement account on an order, sent with it (R3–R5, R43) + functional and adapter tests, two-tenant.
4. `Settled` with a settlement id and the `Settlement` report: pull, the trade settlement, agreed or disputed, a net of 0 (R6–R11, R13) + functional tests.
5. The settlement payment: created, sent, paid from the debit line (R12, R17, R18, R21, R44) + functional tests.
6. The broker's receipt and the mismatch (R19) + functional tests.
7. A returned settlement payment and pay again (R14, R20) + functional tests.
8. Set aside, the reads and the permission (R1, R15, R16) + functional tests, two-tenant.
9. Bank reconciliation: the four kinds and the open settlements (R22–R25) + Domain unit and functional tests.
10. The `Broken` report: a fill that was not posted, the Reversal by the flow, the order after a break (R26–R29, R32) + Domain unit and functional tests.
11. A break that cannot be reversed, resolve by hand, unsettled cash and account closure (R30, R31, R33, R34) + functional tests.
12. Simulator: script `0011`, the settlement account on an order, broker settlements and the `Settlement` report (R35, R36, R41) + `Simulator.Tests`.
13. Simulator: the broker's payment, the settlement mode, its own books, reset (R37–R39, R42) + `Simulator.Tests`.
14. Simulator: break a fill (R40) + `Simulator.Tests`.
15. AppHost and compose configuration (§9.1).
16. BFF prefix, Adviser Portal D76–D78 + frontend tests.
17. Customer Portal C36 and Back Office BO29 + tests.
18. Repo docs: mark this file landed; roadmap §13 step 11b.

Do not put rebalance through orders, a fee sweep or a break after settlement in these commits.

### Docs amended when this spec was accepted

[orders.md](orders.md) (R2, R4, R20, R53: the settlement account; R23–R25: the two kinds, their fields and skip reasons; R28, R33, R36–R38, R44, R47: the broken fill; §2 Out and §11 points 1 and 12 answered), [bank-reconciliation.md](bank-reconciliation.md) (R9, R10, R13, R14, §5 kinds, §11 point 6), [withdrawals.md](withdrawals.md) (R31 kinds, R32, R33), [bank-returns.md](bank-returns.md) (R2 classification, R8, R10), [deposit-matching.md](deposit-matching.md) (R13: settlement receipts before deposit matching; the line item), [transaction-source.md](transaction-source.md) (R7: a broken trade is reversed by its flow; `OrderFill` stays not hand-reversible), [account-closure.md](account-closure.md) (R5), [trade-fees.md](trade-fees.md) (a fill's fee is never below 0), [simulator.md](simulator.md) (R35–R42, script `0011`, the routes and log actions), [bff-adviser-portal.md](bff-adviser-portal.md) (prefix `/trade-settlements`), [api-design.md](../api-design.md) (§3.1 one permission row, §6 resource row, §7.12 notes, change log), [domain-model.md](../domain-model.md), [database-design.md](../database-design.md), [architecture.md](../architecture.md) (broker connection configuration), [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (D76–D78), [customer-portal.md](../customer-portal/customer-portal.md) (C36), [back-office.md](../backoffice/back-office.md) (BO29), [glossary.md](../glossary.md) (the review terms become accepted; Fill, Settled, Unsettled cash, Outgoing payment, Known difference, Timing difference, Break, Hand-reversible source and Broker connection change), [function-plan.md](../function-plan.md) §5, [features/README.md](README.md) row status, [future-plans.md](../future-plans.md) §9, and the roadmap §8 O1 and O2, §11 S1 and S2, §13 and §14.

---

## Amendment 2026-11-23 (rebalance through orders) — accepted

Status of this amendment: `accepted` (2026-11-23), together with [rebalances.md](rebalances.md). Rule R26 lives there; this section records what changes here.

- R5, R23: a rebalance order gets a settlement account like any order, so its fills settle through the client money account and leave the known difference `Trades`. `Trades` keeps the rebalance trades posted before this slice (source `Rebalance`).
- "Still open": rebalance orders are no longer open; they settle like any order.

---

## Amendment 2026-11-24 (migration batches) — accepted

Status of this amendment: `accepted` (2026-11-24), together with [migration-batches.md](migration-batches.md). Rule R16 lives there; this section records what changes here.

- R21: a `Matched` line now has an Account and a Transaction, or a related line, or a trade settlement, or a migration batch. `0054` replaces `CK_BankStatementLines_Match` with the four arms.
