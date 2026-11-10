---
title: Internal transfers
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-10
last_updated: 2026-11-10
related:
  - leg-accounts.md
  - posting.md
  - portfolios.md
  - reports.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../customer-portal/customer-portal.md
  - ../adr/0011-ledger-cash-holdings-reversal.md
  - ../adr/0015-idempotency-keys.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
---

# Internal transfers

Cash transfer between two Accounts in the same tenant, on `webapi`. One Transaction of the new type `InternalTransfer` has two cash legs: a negative leg on the source Account and a positive leg on the destination Account. The two Accounts may belong to the same Customer or to two Customers of the same tenant.

This is the increment that [leg-accounts.md](leg-accounts.md) prepared (§12, §13) and [posting.md](posting.md) §13 deferred. It adds one `TransactionType`, one create route `POST /transactions/transfer`, an optional `type` filter on the transaction list, and script `0025`. No new policy. Read and reverse reuse the existing `/transactions` routes.

---

## 1. Summary

A TenantAdmin or Adviser moves cash from one Open Account to another Open Account with the same currency, in one tenant, in one command. Both legs commit together or not at all. The rule that distinguishes this slice: the create body names two Accounts (`fromAccountId`, `toAccountId`), so it has its own create route, and the caller must be able to write to both Accounts.

---

## 2. Scope

**In**

- Domain: `TransactionType.InternalTransfer = 15`; factory that builds two cash legs on two Accounts
- Application: `CreateInternalTransfer` command; existing reverse, list, get, cash balance, net worth, reports and portfolio rules applied to the new type
- Script `database/schema/0025_transaction_type_internal_transfer.sql` (widens `CK_Transactions_Type` to 0–15)
- HTTP: `POST /transactions/transfer`; `POST /transactions` rejects `InternalTransfer`; optional `type` filter on `GET /transactions`
- Adviser Portal: Transfer form with a two-step destination picker (§9)
- Tests listed in §10

**Out**

- Cross-tenant transfer. A movement across tenants stays two unlinked Transactions: `TransferOut` in one tenant, `TransferIn` in the other (leg-accounts §13, posting R18)
- Cross-currency transfer (see [future-plans.md](../future-plans.md) §2)
- In-kind transfer (security legs on two Accounts)
- Fees on a transfer
- A counterparty field on the transaction item
- `GET /transfers` or any transfer-only read route
- Automatic rebalance after a transfer
- Customer-initiated transfer (Customer has no ledger write)
- Account search that matches Customer name, and `currency` / `status` filters on `GET /accounts` (later; §9)
- Moving existing types to their own create routes (api-design §7.7 convention; see [future-plans.md](../future-plans.md) §3)

---

## 3. Stories

1. As an Adviser I move cash from one Account of my Customer to another Account of the same Customer, so the money is where it will be used.
2. As an Adviser I move cash from an Account of one assigned Customer to an Account of another assigned Customer in the same tenant.
3. As an Adviser I cannot transfer to or from an Account whose Customer is not assigned to me (404).
4. As an Adviser I reverse a transfer, and both Accounts get the opposite leg in one Reversal.
5. As an Adviser I find the destination Account by first choosing the Customer (default: the source Account's Customer), then choosing one of that Customer's eligible Accounts.
6. As a Customer I see a transfer that touches one of my Accounts. I see only the legs on my Accounts.
7. As an Adviser I filter the transaction list to `InternalTransfer`.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Unchanged from posting R1–R4a and leg-accounts R1: 401 / 403 / 404, PublicId only, `tenantId` rules for TenantAdmin / Adviser (omit) and SystemAdmin (required on create). Customer → 403. Disabled tenant on create → 400 `disabled` / `target=tenant`. |
| R2 | **Route.** `POST /transactions/transfer` creates an `InternalTransfer`. `POST /transactions` with `type = InternalTransfer` → 400. There is one create route per type. |
| R3 | **Body.** `fromAccountId` and `toAccountId` (Account PublicIds) are required. `amount` is required and `> 0`. `bookedAt`, `memo`, `reference` follow posting (R23 for memo / reference). The body has no `type`, no `accountId`, no `currency`, no legs array. Any of those present → 400. |
| R4 | **Legs.** Application builds exactly two cash legs: `−amount` on the from Account and `+amount` on the to Account, both in the Accounts' currency. No security leg. |
| R5 | **Different Accounts.** `fromAccountId = toAccountId` → 400. |
| R6 | **Same tenant.** Both Accounts are in the command's tenant. An Account in another tenant, a missing Account, or (Adviser) an Account of an unassigned Customer → 404 for the whole command (leg-accounts R6). Nothing is written. |
| R7 | **Same currency.** `fromAccount.Currency ≠ toAccount.Currency` → 400. No FX is applied. Amount precision follows posting R8. |
| R8 | **Account types.** Any Phase-2 type (Bank, Cash, Brokerage, Other) may send to or receive from any other Phase-2 type. |
| R9 | **State.** Both Accounts must be Open (else 400). Both Customers must not be Disabled (else 400 `disabled` / `target=user`). Same rule on reverse (leg-accounts R8). |
| R10 | **Cash `SUM`.** After insert, the from Account's `SUM ≥ 0` (else 400). Checked inside the command with both Account rows locked in ascending `Accounts.Id` order (leg-accounts R10, R11). |
| R11 | **Opening.** An `InternalTransfer` leg counts as another transaction on both Accounts. After an Account sends or receives a transfer, cash `Opening` on it → 400 (posting R19, leg-accounts R13). No exception. |
| R12 | **Reverse.** `POST /transactions/{id}/reverse` on an `InternalTransfer` creates one `Reversal` with `+amount` on the from Account and `−amount` on the to Account (leg-accounts R12). Every check in R6, R9, R10 applies to both Accounts. If the to Account's `SUM` would go below 0 (the cash was spent), the whole reverse → 400. No partial reverse. |
| R13 | **Idempotency.** `Idempotency-Key` is required on `POST /transactions/transfer` (ADR 0015). Missing → 400. Same tenant + same key + same request hash → original `201 { id }`. Same key + different hash → 409. The request path is part of the hash. |
| R14 | **Read.** Get and list follow leg-accounts R7: the caller sees the transfer if they can see at least one of its two Accounts, and only the legs on Accounts they can see. A caller who sees one Account cannot tell from the item which Account or Customer is on the other side. |
| R15 | **List `type` filter.** `GET /transactions` takes an optional `type` (exact `TransactionType` name, any type). Unknown value → 400. Applied after the visibility scope, together with `accountId` / `customerId` (leg-accounts R18). |
| R16 | **Portfolios.** A transfer is cash-only, so it is allowed on an Account with an Active portfolio assignment (portfolios R18 locks security changes only; adviser-portal-portfolios P9). It does not enqueue or trigger a rebalance. The Adviser rebalances manually (portfolios R19). |
| R17 | **Close.** Unchanged. A transfer that empties an Account does not close it. `CloseOut` stays an off-books movement (posting R17). |
| R18 | **Customer Transactions report.** One row per leg account of the Customer (leg-accounts R20). A transfer between two Accounts of the same Customer shows two rows: the negative leg and the positive leg. |
| R19 | **Net worth.** Cash per Account from the leg `SUM`. A transfer between two Accounts of one Customer leaves that Customer's per-currency total unchanged. |
| R20 | Immutability (posting R10) and `bookedAt` rules: unchanged. |

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `TransactionType` | Enum | Add `InternalTransfer = 15`. `Dividend = 6` stays reserved. |
| `Transaction` | Aggregate | New factory for `InternalTransfer` taking from Account, to Account and a positive `Money` amount. Builds the two cash legs of R4. `Reverse` is unchanged: it already copies each leg's Account. |
| `TransactionPosted` / `TransactionReversed` | Event | Unchanged. |

Invariants for `InternalTransfer`:

- Exactly two cash legs and no security leg.
- The two legs are on two different Accounts.
- Both legs have the same currency.
- One leg is negative, the other is positive, and they sum to zero.

Existing invariants (at least one leg, one cash leg per `(Transaction, Account)`, immutability) are unchanged.

Update [domain-model.md](../domain-model.md) when this spec is accepted.

---

## 6. Database

Script: `database/schema/0025_transaction_type_internal_transfer.sql`. Forward only.

| Table | Change | Index / FK |
| --- | --- | --- |
| `Transactions` | `CK_Transactions_Type` → `BETWEEN 0 AND 15` | none |
| `TransactionCashLegs` | none | Existing unique `(TransactionId, AccountId)` already prevents two legs on one Account. Existing `(AccountId)` index serves the account filter. |

The leg shape of R4 is enforced in Domain and Application, not by a database check.

The `type` list filter (R15) needs no new index in this increment. `IX_Transactions_TenantId_BookedAt` already orders the scope.

Update [database-design.md](../database-design.md) when this spec is accepted. Do not add EF migrations.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreateInternalTransfer` | `{ id }` | `transactions.create`; idempotency; R3, R5–R10; lock both Accounts by ascending Id; insert header and two legs in one unit of work |
| Command | `CreateTransaction` | unchanged | Rejects `InternalTransfer` (R2) |
| Command | `ReverseTransaction` | unchanged | R12 via leg-accounts R12 |
| Query | `GetTransactions` | list envelope | Adds optional `type` (R15) |
| Query | `GetTransactionById` | item | Unchanged (R14) |

`CreateInternalTransfer` is its own command. It does not add a branch to `CreateTransaction`.

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| POST | `/transactions/transfer` | `transactions.create` | 201 `{ id }` | 400 / 401 / 403 / 404 / 409 |
| POST | `/transactions` | `transactions.create` | unchanged | 400 when `type = InternalTransfer` |
| GET | `/transactions` | `transactions.read` | 200 envelope | 400 unknown `type` |

`id` is the Transaction PublicId. `Location` is `/transactions/{id}`. Read and reverse use the existing routes. The route sits under `/transactions`, so the BFF allow-list needs no change.

### 8.1 Bodies

Create:

```json
{
  "fromAccountId": "<account publicId>",
  "toAccountId": "<account publicId>",
  "amount": 500.00,
  "bookedAt": "2026-11-10T09:00:00+13:00",
  "memo": "Move to savings",
  "reference": null
}
```

SystemAdmin adds `tenantId`. TenantAdmin / Adviser must omit it.

Item (caller sees both Accounts):

```json
{
  "id": "<transaction publicId>",
  "tenantId": "<tenant publicId>",
  "type": "InternalTransfer",
  "bookedAt": "2026-11-10T09:00:00+13:00",
  "memo": "Move to savings",
  "reference": null,
  "originalTransactionId": null,
  "reversedByTransactionId": null,
  "rowVersion": "<base64>",
  "cash": [
    { "accountId": "<from>", "customerId": "<customer>", "amount": -500.00, "currency": "NZD" },
    { "accountId": "<to>", "customerId": "<customer>", "amount": 500.00, "currency": "NZD" }
  ],
  "security": []
}
```

A caller who sees only the to Account gets one entry in `cash` (`+500.00`) and nothing about the from Account. Leg order in `cash` is not a contract; direction comes from the sign.

List:

`GET /transactions?type=InternalTransfer&accountId=<account publicId>&page=1&pageSize=20`

### 8.2 Errors

Ordinary validation in `errors`: same Account, currency mismatch, Closed Account, `amount ≤ 0`, forbidden body field, insufficient cash on create or reverse, `InternalTransfer` sent to `POST /transactions`, unknown `type` filter.

§4.1 `disabled`: `target=tenant` on a disabled tenant, `target=user` when either Customer is Disabled.

Any Account outside the caller's scope or tenant → **404**, not 403.

---

## 9. UI

Adviser Portal. Amend [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) when this spec is accepted.

- Account detail on an Open Account shows **Transfer** beside TransferIn / TransferOut.
- **Step 1 — Customer.** Defaults to the source Account's Customer. A search box changes it: debounced (~300 ms) `GET /users/customers?enabledOnly=true&search=`. An Adviser sees only assigned Customers; this is the same interaction as the Customers page.
- **Step 2 — Account.** `GET /accounts?customerId=<chosen>&pageSize=100`. The client marks an Account as not selectable when it is Closed, has a different currency, or is the source Account. Not-selectable Accounts stay in the list, greyed out, with the reason ("Closed", "Different currency", "Source account").
- **Amount, bookedAt, memo, reference.** Amount is positive; the form does not ask for a sign.
- **Confirm.** Summary: "Transfer 500.00 NZD from <account> (<customer>) to <account> (<customer>)". When the two Customers differ, the summary shows an extra warning line.
- Submit calls `POST /transactions/transfer` with a new `Idempotency-Key` per form open.
- Transaction lists show the leg on the Account being viewed: negative on the source, positive on the destination. The Reverse button rule is unchanged.

Customer Portal: read only, no new page. A transfer shows as "Transfer" with the amount of the visible leg. No counterparty is shown.

Later (not this slice): one search box that finds Accounts by Account name or Customer name, with server-side `currency` and `status` filters on `GET /accounts`. That is an accounts spec amendment.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | Factory builds two cash legs, `−amount` on from and `+amount` on to; rejects same Account, different currencies, `amount ≤ 0`; Reverse of a transfer produces the opposite leg on each Account |
| Application.FunctionalTests | No Bearer → 401 not 302; Customer → 403; happy path same Customer and two Customers (TenantAdmin, Adviser assigned to both); Adviser unassigned on either side → 404 and nothing written; same Account → 400; currency mismatch → 400; Closed from or to → 400; Disabled Customer on either side → 400 `disabled` / `target=user`; insufficient cash → 400; forbidden body fields (`type`, `accountId`, `currency`) → 400; `POST /transactions` with `InternalTransfer` → 400; missing `Idempotency-Key` → 400, replay → same id, different hash → 409; cash balances of both Accounts after create and after reverse; reverse after the to Account spent the cash → 400 and nothing written; Opening on an Account that received a transfer → 400; transfer allowed on an Account with an Active portfolio assignment and no rebalance is enqueued; list `type=InternalTransfer` with and without `accountId`; unknown `type` → 400; caller who sees one side gets one leg only |
| Infrastructure.IntegrationTests | `CK_Transactions_Type` accepts 15 and rejects 16 |
| Application.FunctionalTests (two tenants) | From or to Account in the other tenant → 404 and nothing written (TenantAdmin and SystemAdmin); other tenant's transfer id on get / reverse → 404. These assert HTTP status codes, so they live with the other transaction isolation tests in the functional layer (`InternalTransferIsolationTests`), not in the schema fixture |
| Frontend | Picker defaults to the source Customer; Customer search debounced; Closed / different-currency / source Accounts are greyed out with a reason; confirm summary warns when Customers differ |

---

## 11. Locked in this spec (discussion, 2026-11-10)

No new ADR.

| Item | Lock |
| --- | --- |
| Type name | `InternalTransfer = 15`. "Internal" = inside one tenant. `TransferIn` / `TransferOut` keep the off-books meaning and stay the cross-tenant tool. |
| Scope | Same tenant; same or different Customer; cash only; same currency. |
| Route | `POST /transactions/transfer` for create. Read and reverse on existing `/transactions` routes. No `GET /transfers`. `POST /transactions` rejects the type. |
| Route convention | A type whose create body differs from the common shape gets `POST /transactions/{action}`; one create route per type (api-design §7.7). Existing types are not moved in this slice. |
| Body | `fromAccountId`, `toAccountId`, positive `amount`; no `type`, `accountId`, `currency`. |
| Account types | Any of Bank / Cash / Brokerage / Other to any other. |
| Opening | A transfer counts as another transaction. No exception. |
| Reverse | Existing reverse; all-or-nothing; fails if the destination spent the cash. |
| Portfolios | Allowed on assigned Accounts; no automatic rebalance. |
| Item | No counterparty field. |
| List | Optional `type` filter, any type. |
| UI picker | Two steps: Customer (default source Customer, searchable), then Account (client-side eligibility, greyed with reason). Confirm summary with a warning across Customers. |
| Script | `0025_transaction_type_internal_transfer.sql`. |

Still open, not this slice:

- Cross-tenant transfer as a linked pair, if it is ever wanted.
- In-kind transfer.
- Fees.
- Account search by Customer name and server-side `currency` / `status` filters on `GET /accounts`.

---

## 12. Suggested commits

Only after `accepted`. The tree must build after each commit. Split each behaviour into red then green.

1. `0025_transaction_type_internal_transfer.sql` + `TransactionType.InternalTransfer` + integration test that the check accepts 15.
2. Domain factory and invariants + unit tests.
3. `CreateInternalTransfer` + `POST /transactions/transfer` happy path, idempotency, validation (R3, R5, R7, R9, R10) + functional tests.
4. Authority and tenant isolation (R6) + functional and two-tenant integration tests.
5. `POST /transactions` rejects `InternalTransfer` (R2); Opening after a transfer (R11) + tests.
6. Reverse of a transfer (R12) + tests.
7. List `type` filter (R15) + tests.
8. Portfolio interaction (R16) + tests.
9. Adviser Portal Transfer form and picker + frontend tests. Customer Portal label.
10. Repo docs: this file `accepted`; amend domain-model, database-design, api-design change log, adviser-portal-dashboard, customer-portal, features README status.
