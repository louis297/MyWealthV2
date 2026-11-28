---
title: Adjustments
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-19
last_updated: 2026-11-28
related:
  - ../roadmaps/custodial-roadmap.md
  - transaction-source.md
  - posting.md
  - holdings.md
  - custody-modes.md
  - portfolios.md
  - leg-accounts.md
  - reports.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../customer-portal/customer-portal.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
  - ../known-issues/custodial-account-gaps.md
  - withdrawals.md
  - account-closure.md
  - rebalances.md
  - migration-batches.md
  - in-kind-transfers.md
  - dividends.md
  - adjustment-review.md
---

# Adjustments

New ledger write on `webapi`. It is step 3 of [custodial-roadmap.md](../roadmaps/custodial-roadmap.md) §13 and covers roadmap item **F3** (controlled adjustment, first version, no review). It amends [posting.md](posting.md), [holdings.md](holdings.md), [custody-modes.md](custody-modes.md), [portfolios.md](portfolios.md), [reports.md](reports.md) and the two portal contracts.

This spec owns what an adjustment is: the `TransactionType` `Adjustment`, its shape, its reason, its checks, the permission `transactions.adjust` (TenantAdmin only) and the item field `adjustment`. How one is entered is [adjustment-review.md](adjustment-review.md): a TenantAdmin requests it and another TenantAdmin approves it, and the approval posts the Transaction. Script `0037_adjustments.sql` widens the type check and adds `TransactionAdjustmentDetails`. No problem code.

This file states the rules as they are today. §13 lists what changed here, by date.

**Status is `accepted` (2026-11-19), landed 2026-11-19.** The eight points of §11 were confirmed as recommended.

One change from §10 when it landed: there is no functional test of a security part on a Bank or Cash account. A Custodial account must be a Brokerage account (custody-modes R3), and R5 refuses an External account first, so the case cannot be reached through the route. The Domain test covers it.

---

## 1. Summary

An **adjustment** is a hand-entered correction on one Custodial account: a cash change, a holding change (quantity and cost of one instrument), or both, with a required **adjustment reason** and a required memo. A TenantAdmin requests one, and a second TenantAdmin approves it. Nobody reverses one: a wrong adjustment is corrected by another.

Why now: [transaction-source](transaction-source.md) took away the hand reverse of platform writes on a Custodial account (roadmap P6), and every later flow will write Transactions that a person cannot reverse. A break that is not a wrong hand entry, or a hand entry that can no longer be reversed (a `Buy` that is not the latest security activity on its holding), has no fix today. The adjustment is that fix.

The rule that distinguishes this slice: an adjustment is a separate write with its own permission and a stated reason, not a hand type that anyone with `transactions.create` can enter. A second person reviews it before it is posted ([adjustment-review.md](adjustment-review.md)); a Transaction still has no status.

---

## 2. Scope

**In**

- Domain: `TransactionType.Adjustment` (17); `AdjustmentReason` enum; `TransactionAdjustmentDetail` (child of `Transaction`); factory `Transaction.PostAdjustment`; `Reverse` copies the detail; the manual entry matrix row for `Adjustment`
- Application: `CreateAdjustment` command; `ReverseTransaction` gains the permission check for an `Adjustment` (R17); the transaction reads map `adjustment`
- Infrastructure: EF mapping of the new table
- Script `database/schema/0037_adjustments.sql` (type check 0–17, new table; forward only)
- Security: permission `transactions.adjust`, TenantAdmin only
- HTTP: `POST /transactions/adjustment`; `adjustment` on the transaction item; `POST /transactions` refuses the type
- Reports: the Customer Transactions Report shows the reason on an adjustment row
- Adviser Portal: Adjust form on a Custodial account for a TenantAdmin; Reason line on the detail page; Reverse of an adjustment for a TenantAdmin only
- Customer Portal: the "Adjustment" label
- Tests listed in §10

**Out**

- Review by a second person, a Transaction status, an approve command (roadmap X1)
- Adjustments on External accounts (R5). They keep every hand type
- A new source kind. A hand adjustment is `Manual` (R4). A later flow that books an adjustment-like write (for example the rounding write-off of account closure, roadmap B8) decides in its own spec whether it uses this type with its own source
- More than one instrument, or more than one Account, in one adjustment
- An operations role (roadmap D5) and any grant to Adviser or SystemAdmin
- A reconciliation, a break list, or a link from an adjustment to a break (roadmap B7, T3)
- Corporate action events (roadmap C1). R7 allows the reason `CorporateAction` so a split can be recorded by hand until then
- A `reason` list filter on `GET /transactions`. The existing `type=Adjustment` filter is enough
- Any change to the Adviser Balances Report, net worth or holdings reads beyond the numbers the new legs change

---

## 3. Stories

1. As a TenantAdmin I correct the cash balance of a Custodial account with an adjustment, giving a reason and a memo, when a hand reverse is not possible.
2. As a TenantAdmin I correct the quantity and cost of one holding on a Custodial account, for example after a real split that the platform cannot record yet.
3. As a TenantAdmin I correct cash and a holding together in one adjustment, so the two changes are one event.
4. As a TenantAdmin I reverse an adjustment I or another TenantAdmin entered, when it is wrong.
5. As an Adviser I see adjustments on my Customers' accounts with their reason, but I cannot enter or reverse one (403).
6. As a Customer I see an adjustment on my account labelled as one, with its memo, in the Customer Portal and in my Transactions Report.
7. As a TenantAdmin I cannot enter an adjustment on an External account (400); I enter the matching hand type there.

---

## 4. Rules

### 4.1 Access

| ID | Rule |
| --- | --- |
| R1 | **Unchanged access rules.** posting R1–R4, leg-accounts R6–R8. 401 / 403 / 404, PublicId only. The Account must be in the caller's tenant; another tenant's Account or a missing one → 404. A disabled current tenant → 403. |
| R2 | **Permission.** An adjustment is requested with `POST /adjustment-requests`, which requires the permission `transactions.adjust`. Only TenantAdmin holds it. Adviser, Customer and SystemAdmin → 403. A TenantAdmin other than the requester approves the request, and the approval posts the `Adjustment` (adjustment-review R1–R9). No route posts an adjustment at once. |
| R3 | **No `tenantId`.** The body must omit `tenantId` (400 if present), as for every TenantAdmin write (posting R4). No caller of this route names a tenant. |

### 4.2 The adjustment

| ID | Rule |
| --- | --- |
| R4 | **Type and source.** An adjustment is a Transaction with `Type = Adjustment` (17). Posted from a request it has source `AdjustmentRequest` (11) with the request's PublicId, and `CreatedBy` is the approver (adjustment-review R13). An adjustment posted before 2026-11-25 has source `Manual` and keeps it. |
| R5 | **Custodial only.** The Account must be Custodial. An External account → 400 with `errors.AccountId` and the message `An adjustment can be entered only on a Custodial account.` Nothing is written. The manual entry matrix (custody-modes R5) gains the row `Adjustment`: External **refused**, Custodial **allowed on its own route, with `transactions.adjust`**. |
| R6 | **One Account, at most one cash leg and one security leg.** An adjustment books into the one `accountId` of the body. It has a cash part (`amount`), a holding part (`security`), or both. Neither → 400 with `errors` on the body (`At least one of amount and security is required.`). |
| R7 | **Reason.** `reason` is required and is one of the `AdjustmentReason` names, matched exactly: `EntryError` (a hand entry was wrong and cannot be reversed), `CorporateAction` (a real corporate action the platform cannot record yet), `ReconciliationBreak` (the books disagree with an outside statement), `Other`. Missing or unknown → 400 `errors.Reason`. The list changes only in a spec. `CorporateAction` corrects one account after a corporate action was applied (for example a trade booked at the new price before the posting), not the whole event; a wrong ratio is corrected for every account by the inverse corporate action (corporate-actions R32). |
| R8 | **Memo.** `memo` is required: trimmed, 1–200 characters. Missing or blank → 400 `errors.Memo`. The memo is shown to the Customer (§9). `reference` stays optional (posting R23). |
| R9 | **Cash part.** `amount` is a signed book amount in the Account currency (posting R8): non-zero, with no more decimal places than the currency's `DecimalPlaces`. The body must not send a currency. After the adjustment the cash `SUM` of the Account must be `≥ 0` (posting R9), else 400. The cash leg has role `Principal` and no fee detail. With a negative `amount`, the Account's **available cash** (cash `SUM` minus reserved cash, which counts open withdrawals and live buy orders) must also be `≥ 0`, checked under the Account lock: when the `SUM` stays `≥ 0` but falls below reserved cash → 400 with `Available cash cannot become negative: 300.00 USD is reserved for withdrawals.` (withdrawals R36, R37; orders R39). |
| R10 | **Holding part.** `security` names one `instrumentId` (Instrument PublicId in the tenant, else 404), a signed `quantityChange` and a signed `costChange`. Either may be `0`, not both (400 `errors.Security`). `quantityChange` has at most 8 decimal places, `costChange` at most 4. Cost is in the instrument's quote currency (holdings R10); the body must not send a currency. |
| R11 | **Holding after the adjustment.** Let quantity and cost be the holding's projection before the adjustment (0 and 0 when there is none). After it: quantity `≥ 0` and cost `≥ 0`; when quantity is `0`, cost must be `0`, because the projection row is deleted at zero (holdings R14). Any other result → 400 `errors.Security` with the message `The holding cannot have a negative quantity or cost, or a cost without a quantity.` A cost-only change (`quantityChange: 0`) needs an existing holding. An adjustment that lowers a holding's quantity must also leave its **available quantity** `≥ 0` (quantity minus the quantity reserved for live sell orders and open in-kind transfers `Out`); otherwise 400 `errors.Security` with the message `40 ACME.SIM is reserved for sell orders and transfers out.` (orders R40, in-kind-transfers R43). |
| R12 | **Instrument state.** A disabled instrument is allowed only when the Account already holds it (a correction of an existing position). A disabled instrument with no holding → 400, as holdings R7. |
| R13 | **Cost is the caller's number.** The server does not read a price, a rate or a fee schedule for an adjustment. The new security leg stores `quantityChange` and `costChange` as they are. It does not call the average-cost release (buy-sell): a negative `costChange` is the cost the TenantAdmin removes. |
| R14 | **Booking time.** The server sets `bookedAt`: the time of the approval (custody-modes R10, R12; adjustment-review R9). A request body that sends `bookedAt` → 400 `errors.BookedAt` with the custody-modes R10 message. |
| R15 | **Account and Customer state.** Closed Account → 400 (posting R6). Disabled Customer → 400 `disabled` / `target=user` (posting R7). Checked after the Account is found and locked, before R5. An adjustment on a closing account (an account closure `InProgress`) → 400 with `This account is being closed and accepts no new activity.` under the empty `errors` key, directly after the Closed-account check (account-closure R12). |
| R16 | **Not a trade.** The portfolio trade lock (portfolios R18) does not apply to an adjustment or its reverse. An Active assignment stays as it is, and no rebalance is enqueued. The next rebalance sees the adjusted holding. The tax year read takes an adjustment with a security leg as a `Correction` with a notice and no amount ([tax-year-events.md](tax-year-events.md) R16). The tax cost of the threshold test changes by its `quantityChange` and its `costChange` as entered, the cost at the rate of the UTC date of `bookedAt` ([tax-cost-threshold.md](tax-cost-threshold.md) R11). |

Order in the handler: request shape (R3, R6–R8, R10 decimal places, R14) → Account found and locked (R1) → R15 → R5 → idempotency → instrument (R10, R12) → R9 cash and R11 holding checks → insert. A refusal after the idempotency check writes no idempotency record, as on the other create routes.

R3 and R5–R16 are checked twice: when the adjustment is requested and again, under the Account lock, when it is approved (adjustment-review R4, R9).

What an adjustment corrects, since no platform write on a Custodial account is reversed by hand:

- A fill booked wrongly. With reason `ReconciliationBreak` it also removes the cause of a fill that waits for cash or shares, which the next run then posts (orders R36, R37).
- A rebalance trade posted before rebalances went through orders (rebalances R28).
- A wrong entry of a posted migration batch, with reason `EntryError` (migration-batches R25).
- A custody movement in suspense, a transfer set aside, or a wrong cost or quantity on a completed in-kind transfer (in-kind-transfers R18, R39, R41).
- A dividend paid to one account in error, with reason `CorporateAction`. No flow reverses a paid dividend (dividends R42).

The remainder write-off of an account closure is not an adjustment: it is a `CloseOut` with source `AccountClosure`, and R7 has no `Rounding` reason ([account-closure.md](account-closure.md) §11 point 6).

### 4.3 Reverse

| ID | Rule |
| --- | --- |
| R17 | **No reverse.** An `Adjustment` is never reversed by hand, by any caller, whatever its source: `POST /transactions/{id}/reverse` → 400 with `An adjustment cannot be reversed. Request a new adjustment that corrects it.` (adjustment-review R15). The check runs after the Transaction is found and visible (a Transaction the caller cannot see is still 404). |
| R18 | Removed 2026-11-25 (adjustment-review R15): the ordinary reverse of an adjustment. No new Reversal carries an adjustment detail; a Reversal posted before keeps its copy. An adjustment with a security leg is still security activity that blocks the reverse of earlier trades on its holding. |

### 4.4 Other routes and reads

| ID | Rule |
| --- | --- |
| R19 | **One create route.** `POST /transactions` with `type: "Adjustment"` → 400 `errors.Type`, the same refusal as `InternalTransfer` and `FxConversion` (api-design §7.7, type-specific create routes). |
| R20 | **Idempotency.** `POST /adjustment-requests` requires `Idempotency-Key` (ADR 0015; adjustment-review R3). |
| R21 | **Item.** The transaction item gains `adjustment`: `{ "reason": "<AdjustmentReason name>" }` on an `Adjustment` and on its Reversal, `null` on every other Transaction. Every caller who can read the Transaction sees it, the Customer included. `GET /transactions?type=Adjustment` lists adjustments (internal-transfers, `type` filter). |
| R22 | **Balances and reads.** Cash balance, holdings, net worth and the Adviser Balances Report add the adjustment's legs like any other legs. They do not distinguish adjustments. |
| R23 | **Customer Transactions Report.** An adjustment row shows the type cell `Adjustment (<reason label>)` with the labels of §9, and its memo as on every row. Its Reversal shows `Reversal` as today. Parameters and the rest of the report do not change. |
| R24 | **External accounts.** No change beyond the refusal of R5 and the new item field. |

Policies: `transactions.adjust` (new). Grants: TenantAdmin only. Update the role permission table in [api-design.md](../api-design.md) §3.1 when this spec is accepted.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `TransactionType` | Enum (existing) | New `Adjustment = 17`. |
| `AdjustmentReason` | Enum (new) | `EntryError = 0`, `CorporateAction = 1`, `ReconciliationBreak = 2`, `Other = 3`. |
| `TransactionAdjustmentDetail` | Entity (new, child of `Transaction`) | `TenantId`, `TransactionId`, `Reason`. Exactly one on an `Adjustment` and on its Reversal; none on any other Transaction. Immutable. `Copy()` for the Reversal, as `TransactionFxDetail`. |
| `Transaction` | Aggregate (existing) | New `AdjustmentDetail` (0..1). New factory `PostAdjustment`. `Reverse` copies the detail when there is one. |
| `CustodyRules` | Static rules (existing) | `RefusalMessage(External, Adjustment)` returns the R5 message; `RefusalMessage(Custodial, Adjustment)` returns `null`. |

```text
PostAdjustment(tenantId, accountId, accountType, accountCurrency,
               reason, memo, reference, bookedAt, source,
               cash?: amount, currentCashSum,
               security?: (instrumentId, quoteCurrency, quantityChange, costChange,
                           currentQuantity, currentCost))
  at least one of cash and security
  memo not blank (after trim)
  cash:     amount != 0; currentCashSum + amount >= 0
  security: account type allows holdings (Brokerage or Other)
            quantityChange != 0 or costChange != 0
            q = currentQuantity + quantityChange >= 0
            c = currentCost + costChange >= 0
            q == 0  =>  c == 0
  legs:     cash leg (Principal, amount, account currency) if cash
            security leg (quantityChange, costChange, quote currency) if security
  detail:   TransactionAdjustmentDetail(reason)
  raise TransactionPosted
```

Invariants:

- An `Adjustment` has exactly one adjustment detail, at most one cash leg and at most one security leg, all on one Account, and at least one leg.
- An adjustment's legs never take a holding below zero quantity or cost, and never leave cost on a zero quantity.
- `PostAdjustment` does not read the custody mode. The Custodial-only rule (R5) and the permission (R2, R17) are Application rules, the same split as the manual entry matrix (custody-modes R7).
- A Reversal of an `Adjustment` carries a copy of its detail. No other Reversal has one.

Domain events: `TransactionPosted` / `TransactionReversed`, unchanged. No new event.

Update [domain-model.md](../domain-model.md) §8.6 when this spec is accepted.

---

## 6. Database

Script: `database/schema/0037_adjustments.sql`. Forward only. Do not add EF migrations.

| Table | Change | Index / FK |
| --- | --- | --- |
| `Transactions` | Drop and re-add `CK_Transactions_Type` as `Type BETWEEN 0 AND 17` | none |
| `TransactionAdjustmentDetails` | Add: `TransactionId int NOT NULL` (PK), `TenantId int NOT NULL`, `Reason int NOT NULL`. `CK_TransactionAdjustmentDetails_Reason`: `Reason BETWEEN 0 AND 3` | FK `TenantId` → `Tenants` RESTRICT; FK `(TenantId, TransactionId)` → `Transactions (TenantId, Id)` RESTRICT, as `TransactionFxDetails` after `0028`; index `(TenantId)` |
| Every other table | none | none |

No `PublicId` or `RowVersion` on the new table: the row is part of an immutable Transaction, like `TransactionFxDetails`. That an `Adjustment` has a detail and other types do not is held by the Domain and Application, as for FX details.

Existing data: none changes. No back-fill.

Update [database-design.md](../database-design.md) §9 and §11.4 when this spec is accepted.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Command | `CreateAdjustment` (new) | R1–R16, R20, in the order of §4.2. Locks the Account, reads the cash `SUM` and, when `security` is present, the holding projection; calls `PostAdjustment` with `TransactionSource.Manual` and the server clock (custody-modes R12); upserts the projection (holdings R14). |
| Command | `ReverseTransaction` | R17 before every other check, after the Transaction is found. Otherwise unchanged; the Domain copies the detail. |
| Command | `CreateTransaction` | R19: `Adjustment` joins the types refused on `POST /transactions`. |
| Query | `GetTransactions`, `GetTransactionById` | Map `adjustment` (R21). |
| Report | Customer Transactions generator | R23. |
| Query | Net worth, holdings, accounts, Adviser Balances | Unchanged (R22). |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| POST | `/adjustment-requests` | `transactions.adjust` | 201 `{ "id" }` of the request ([adjustment-review.md](adjustment-review.md) §8). `POST /transactions/adjustment` was removed on 2026-11-25 | 400 / 401 / 403 / 404 / 409 |
| POST | `/transactions/{id}/reverse` | `transactions.create` (unchanged) | — | 400 for an `Adjustment` (R17) |
| POST | `/transactions` | `transactions.create` (unchanged) | — | 400 for `type: "Adjustment"` (R19) |
| GET | `/transactions`, `/transactions/{id}` | `transactions.read` (unchanged) | item has `adjustment` | — |

The route sits under `/transactions`, so neither BFF allow-list changes.

### 8.1 Bodies

Create, cash and holding together (a split of 1:2 recorded by hand would send only `security` with `costChange: 0`):

```json
{
  "accountId": "<account publicId>",
  "reason": "EntryError",
  "memo": "Buy of 2026-11-18 was filled at 25.40, not 25.10",
  "reference": null,
  "amount": -0.60,
  "security": {
    "instrumentId": "<instrument publicId>",
    "quantityChange": 0,
    "costChange": 0.60
  }
}
```

Item (other fields as in [transaction-source.md](transaction-source.md) §8.1):

```json
{
  "id": "<transaction publicId>",
  "tenantId": "<tenant publicId>",
  "type": "Adjustment",
  "source": { "kind": "Manual", "id": null },
  "adjustment": { "reason": "EntryError" },
  "cash": [
    { "accountId": "<account publicId>", "customerId": "<customer publicId>", "amount": -0.60, "currency": "USD", "role": "Principal", "fee": null }
  ],
  "security": [
    { "accountId": "<account publicId>", "customerId": "<customer publicId>", "instrumentId": "<instrument publicId>", "quantity": 0, "cost": 0.60, "costCurrency": "USD" }
  ],
  "bookedAt": "2026-11-19T03:00:00Z",
  "memo": "Buy of 2026-11-18 was filled at 25.40, not 25.10",
  "reference": null,
  "originalTransactionId": null,
  "reversedByTransactionId": null,
  "rowVersion": "<base64>",
  "fx": null
}
```

Every other Transaction: `"adjustment": null`.

### 8.2 Errors

| Case | Response |
| --- | --- |
| Caller without `transactions.adjust` on the create route | 403 |
| Reverse of an `Adjustment` without `transactions.adjust` | 403 (after 404) |
| `tenantId`, `bookedAt`, `type` or a currency in the body | 400, field `errors` |
| Neither `amount` nor `security`; `amount: 0`; both changes `0`; too many decimal places | 400, field `errors` |
| Missing or unknown `reason`; missing or blank `memo` | 400 `errors.Reason` / `errors.Memo` |
| External account | 400 `errors.AccountId` (R5) |
| Cash `SUM < 0` or holding result refused (R11) | 400, ordinary `errors` |
| Closed Account | 400, ordinary `errors` |
| Disabled Customer | 400 `disabled` / `target=user` |
| Unknown or other-tenant `accountId` or `instrumentId` | 404 |
| Missing `Idempotency-Key` / same key, different hash | 400 / 409 |

No new problem code. Keep the resource catalog in [api-design.md](../api-design.md) §7.7 pointing here for these rules.

---

## 9. UI

Page contracts: [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) and [customer-portal.md](../customer-portal/customer-portal.md), amended in the same change that accepts this spec. Neither BFF changes.

Reason labels, used by both portals and the report:

| `reason` | Label |
| --- | --- |
| `EntryError` | Entry error |
| `CorporateAction` | Corporate action |
| `ReconciliationBreak` | Reconciliation |
| `Other` | Other |

**Adviser Portal** (acceptance rows D28–D31 of the page contract)

| ID | Behaviour |
| --- | --- |
| D28 | `/accounts/:id` shows an "Adjust" action only to a TenantAdmin, only on an Open Custodial account. It opens `/accounts/:id/adjust`. An Adviser never sees it. |
| D29 | `/accounts/:id/adjust`: Reason (select, required), Memo (required, with the hint "The customer sees this memo"), an optional cash change (signed, in the Account currency), and an optional holding change (instrument picker from the Account's holdings plus `GET /instruments?enabledOnly=true`, signed quantity change, signed cost change in the quote currency). No booked-at input. Review lists the changes as entered ("Cash −0.60 USD; AAPL quantity 0, cost +0.60 USD"); the form does no balance arithmetic. Confirm posts `POST /transactions/adjustment` with one `Idempotency-Key` per form open. A 400 is shown at its field, or as the sentence when it has none. The form posts an adjustment request (adjustment-review D102–D106). |
| D30 | `/transactions/:id` of an `Adjustment` or its Reversal shows a "Reason" line with the label, under the Source line (D26). `/transactions` shows the type as "Adjustment" like any other type name. |
| D31 | Removed 2026-11-25: an adjustment is not reversed (adjustment-review D102–D106). |

**Customer Portal**

- `/accounts/:id` activity labels an `Adjustment` "Adjustment" and shows its memo, as for other rows. The reason is not shown. A Reversal stays "Reversal".

**Back Office**

None.

### 9.1 Seeds

`TestSeed` and `DevelopmentSmokeSeeder` add no adjustment. The Development TenantAdmin can enter one on "Demo custodial brokerage".

---

## 10. Tests

The tests are the executable form of §4. Where they live:

| Project | Where |
| --- | --- |
| Domain.UnitTests | `Entities/AdjustmentTests.cs`; folder `AdjustmentRequests/` |
| Application.FunctionalTests | `Transactions/Adjustment*Tests.cs` (fixture `AdjustmentWorld`); folder `AdjustmentRequests/` |
| Infrastructure.IntegrationTests | `AppliedSchema/AdjustmentsSchemaTests.cs`, `AppliedSchema/AdjustmentRequestsSchemaTests.cs` |

---

## 11. Locked in this spec

No new ADR. Roadmap F3, principle P6 and decision D5 gave the direction.

| Item | Lock |
| --- | --- |
| Type | `Adjustment = 17`, posted only by the approval of an adjustment request |
| Permission | `transactions.adjust`, TenantAdmin only |
| Custody mode | Custodial only |
| Shape | One Account; optional signed cash `amount`; optional one-instrument holding change (`quantityChange`, `costChange`); at least one |
| Reason | Required `AdjustmentReason`: `EntryError`, `CorporateAction`, `ReconciliationBreak`, `Other`; memo required |
| Source | `AdjustmentRequest` with the request's PublicId; `Manual` on adjustments posted before 2026-11-25 |
| Storage | `TransactionAdjustmentDetails`, one per adjustment |
| Trade lock | Does not apply |
| Item | `adjustment: { reason }` or `null` |
| Script | `0037_adjustments.sql`, forward only |
| Review | On the adjustment request, before anything is posted ([adjustment-review.md](adjustment-review.md)); no Transaction status |

### Confirmed when this spec was accepted (2026-11-19)

Each point was confirmed as recommended. The alternatives are kept for the record. Points 3 and 4 were later replaced by [adjustment-review.md](adjustment-review.md): an adjustment is never reversed, and one posted from a request has source `AdjustmentRequest`.

1. **Custodial only** (R5). Decided: refuse on External accounts. They keep every hand type (`Interest`, `StockIn`, `StockOut`, `Split`, …), so they have a correction already, and roadmap target 5 keeps them as they are. Alternative: allow on both modes, so an External account also gets a reason-coded correction.
2. **TenantAdmin only, SystemAdmin not granted** (R2). Decided: as roadmap F3 says, and like the four report policies that SystemAdmin does not hold. Alternative: grant SystemAdmin too, with `tenantId` on the body as on `POST /transactions`, for support through Scalar.
3. **Reverse needs `transactions.adjust`** (R17). Decided: yes, otherwise an Adviser could undo a TenantAdmin's correction with `transactions.create`. Alternative: any caller with `transactions.create` may reverse it, as every other `Manual` row.
4. **Source `Manual`, no `Adjustment` source kind** (R4). Decided: the type already says what the Transaction is, and transaction-source R4 says a person's write is `Manual`. Roadmap F1 listed an `Adjustment` kind; this spec drops it. Alternative: a new kind `Adjustment` (3), which widens the source check in `0037`.
5. **Reason list** (R7). Decided: four values. `Rounding` is left out because R9 refuses sub-unit cash (`CloseOut` still covers that), and a firm's compensation payment is real money in, which later comes through the bank feed (roadmap B4). Alternative: a longer list now, or a free-text reason.
6. **Holding change as two signed deltas** (R10, R13). Decided: `quantityChange` and `costChange`, which is what the security leg stores and what a reverse undoes. Alternative: the target quantity and cost (like `Split` sends the new total), which is easier to type from a statement but depends on the balance the caller saw.
7. **Not subject to the trade lock** (R16). Decided: an adjustment corrects the record, it does not trade, the same as corporate actions later (roadmap C1). Alternative: apply portfolios R18, so a TenantAdmin must unassign first.
8. **Reason not shown in the Customer Portal, shown in the report** (§9, R23). Decided: the report is the formal statement and names why; the portal keeps its labels short. Alternative: show the reason in both, or in neither.

Still open, not this slice:

- An operations role when one TenantAdmin doing everything breaks the four-eyes rule (roadmap D5)
- Linking an adjustment to a reconciliation break (roadmap B7, T3)

---

## 12. Docs changed when this spec was accepted

[posting.md](posting.md) (amendment note; R16 type set), [holdings.md](holdings.md) (amendment note: a holding change outside the trade types), [custody-modes.md](custody-modes.md) (matrix row `Adjustment`), [portfolios.md](portfolios.md) (R18 does not apply), [reports.md](reports.md) (§9.1 type cell), [api-design.md](../api-design.md) (§3.1 permission row, §7.7 route and item note, change log), [domain-model.md](../domain-model.md) §8.6, [database-design.md](../database-design.md) §9 and §11.4, [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (D28–D31), [customer-portal.md](../customer-portal/customer-portal.md) (label), [function-plan.md](../function-plan.md) §9 item, [features/README.md](README.md) row status, the roadmap §13 status.

---

## 13. History

What changed in this file after it was accepted. The text of each change is in git (`git log -p -- docs/features/adjustments.md`).

| Date | With | Change here |
| --- | --- | --- |
| 2026-11-20 | [withdrawals.md](withdrawals.md) | R9: available cash |
| 2026-11-22 | [account-closure.md](account-closure.md), [corporate-actions.md](corporate-actions.md) | R15: a closing account. R7: what `CorporateAction` corrects |
| 2026-11-23 | [orders.md](orders.md), [rebalances.md](rebalances.md) | R9, R11: reserved cash and available quantity. §4.2: what an adjustment corrects |
| 2026-11-24 | [migration-batches.md](migration-batches.md), [in-kind-transfers.md](in-kind-transfers.md), [dividends.md](dividends.md) | R11: transfers out reserve quantity. §4.2: what an adjustment corrects |
| 2026-11-25 | [adjustment-review.md](adjustment-review.md) | §1, R2, R4, R14, R17, R18, R20, §8, §11: an adjustment is requested and approved, and never reversed |
| 2026-11-26 | — | The changes above were folded into the body. No rule changed |
| 2026-11-28 | [tax-year-events.md](tax-year-events.md) | R16: an adjustment of a holding is a `Correction` in the tax year read |
| 2026-11-28 | [tax-cost-threshold.md](tax-cost-threshold.md) | R16: a correction changes the tax cost by its two deltas |
