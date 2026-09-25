---
title: Posting
status: accepted
phase: 2
language: en
owner: ""
created: 2026-09-25
last_updated: 2026-09-25
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../architecture.md
  - ../adr/0004-money-as-decimal-with-currency.md
  - ../adr/0009-currencies-catalog.md
  - ../adr/0011-ledger-cash-holdings-reversal.md
  - ../adr/0013-roles-authorization-single-user-table.md
  - ../adr/0015-idempotency-keys.md
  - accounts.md
  - instruments.md
  - customers.md
---

# Posting

Write model for booked activity on `webapi`. Public name is **Transaction** (business event). Accounting is **legs** on that same aggregate, assembled in the same command. First increment: cash legs only. Also: reverse a booked transaction, read cash balance as `SUM`, amend Account close so a book may close only at cash zero.

There is **no separate cash-ledger Feature Spec.** Cash legs live here. There is **no capture `Transactions` row that is later copied into a `Journals` table.** Security legs, holdings, and Opening-of-quantity stay out of this increment. If this file becomes too large, split holdings / securities into their own spec; do not split cash back out.

**Status is `accepted`.** Implementation follows this file. Not yet landed in GitHub `louis297/MyWealthV2`. Script `0013_transactions.sql`, routes `/transactions`, policies `transactions.read` / `transactions.create`. Amends [accounts.md](accounts.md) R16 (close requires cash `SUM = 0`) and account JSON (`cashBalance`). Expand [ADR 0011](../adr/0011-ledger-cash-holdings-reversal.md). Idempotency: [ADR 0015](../adr/0015-idempotency-keys.md).

---

## 1. Summary

A TenantAdmin or Adviser records an append-only **Transaction** against one **Open** Account of an assigned / same-tenant Customer. SystemAdmin does the same for a chosen tenant.

Two **layers**, one **step**:

```text
POST /transactions
  → insert Transaction (Posted) + cash leg
  → same transaction: lock Account, SUM ≥ 0, Opening / CloseOut rules
  → raise TransactionPosted (no handler required)
```

The header is the business event (`type`, `bookedAt`, `memo`, which Account). The cash leg is the accounting line that the cash `SUM` reads. Application code picks the legs from `type`. This increment always builds exactly one cash leg. Later Buy / Sell add a security leg on the **same** header; split / scrip / bonus are the same model with a security leg only (cash omitted or 0, total cost unchanged).

Write = posted. No `Created` status. No event handler that “approves” or copies into another table. Future maker-checker is a status on this same row plus an approve command — not a second resource and not a swapped subscriber.

Cash-book currency is `Account.Currency`. Balance is not stored on `Account`; it is the sum of posted cash legs.

Close remains `POST /accounts/{id}/close`. This spec **amends** [accounts.md](accounts.md) R16: close is rejected while cash `SUM ≠ 0`. `CloseOut` brings the book to zero (counterparty off-books). Closed accounts reject every new transaction, including reversals — reopen first.

Customer receives 403. No Adviser Portal activity page in this increment.

---

## 2. Scope

**In (first increment)**

- Domain: `Transaction` aggregate (header) + `TransactionCashLeg`; `TransactionType` enum (full name list reserved; this increment accepts a cash subset)
- Application: `CreateTransaction`, `ReverseTransaction`, list / get, `GetAccountCashBalance`; amend `CloseAccount` with a cash-zero guard
- Script `database/schema/0013_transactions.sql` + EF mapping
- Policies `transactions.read` / `transactions.create` (no update / delete policy)
- `webapi` `/transactions` (root, not under `/users`)
- `GET /accounts/{id}` and `GET /accounts` gain computed `cashBalance` + `currency`; `GET /accounts/{id}/cash-balance` returns the same `SUM`
- List envelope + isolation tests
- `Idempotency-Key` on create and reverse ([ADR 0015](../adr/0015-idempotency-keys.md), proposed)
- Development / TestAppHost `TestSeed` rows (not the schema script, not Production)

**Out**

- A second capture table whose approve step writes this aggregate
- `Created` / `PendingApproval` status; event-handler auto-approve
- Security / holding legs, Holdings table, quantity and cost edits
- Opening of holdings (quantity + cost). Cash `Opening` is in this increment
- Buy / Sell / Split / scrip (same aggregate later; not this increment)
- Cross-account transfer — deferred on purpose; see §13
- Live FX / cross-currency legs (same currency = 1; never treat a cross pair as 1)
- Net-worth read model, daily snapshots, Dashboard
- Adviser Portal Accounts / activity / holdings pages
- Property / Credit selectable; Credit negative-cash exception is written only, not built
- Draft / unposted rows
- `UPDATE` / `DELETE` of a posted transaction
- Customer ledger-write; Customer Portal
- SystemAdmin ledger screens on `adviser-portal`
- Empty holdings tables “for later”
- Seeding from the SQL script or in Production
- Debit/credit columns; a wide row that is both cash and security

---

## 3. Stories

1. As an Adviser I record an external deposit or withdrawal on an assigned Customer’s Bank account so the cash book is no longer empty.
2. As an Adviser I record interest on that book in the account currency (either sign).
3. As an Adviser I record a cash `Opening` as the initial inbound (`amount > 0`), at most once, and only while the book still has no other transaction.
4. As an Adviser I record a `CloseOut` equal to the current balance so I can close the account.
5. As an Adviser I cannot close an account while cash `SUM ≠ 0` (400).
6. As an Adviser I cannot record a transaction on a Closed account (400). I reopen first.
7. As an Adviser I reverse a posted transaction with a new opposite transaction that points at the original. I do not edit the original row.
8. As a TenantAdmin I do the same for any Customer in my firm.
9. As a SystemAdmin I do the same on Scalar for a chosen tenant.
10. As a Customer I receive 403 on every `/transactions` verb.
11. As a later increment I add a security leg to the same `Transaction`. Bank / Cash still refuse holdings. Scrip is that increment, not a new write model.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Policy fail → 403. TenantAdmin / Adviser: other-tenant, unassigned Customer, missing id → 404. SystemAdmin: missing id → 404; another tenant’s transaction id is allowed. |
| R2 | Path `{id}` and JSON `id` are `PublicId`. Internal ints never appear. Create / reverse return `201 { "id" }` only. |
| R3 | Routes live at `/transactions`. Not under `/users`. Not nested under `/accounts/{id}` as the collection root (account filter is a query). |
| R4 | TenantAdmin / Adviser: tenant = token `TenantId`. Create / list must **omit** `tenantId` (400 if present). Missing current tenant → 404. Disabled tenant on **create** → 400 `disabled` / `target=tenant`. List / get do not reject because the tenant is disabled. |
| R4a | SystemAdmin: create and list **require** `tenantId` (tenant PublicId). Omitted → 400. Unknown tenant → 404. Get / reverse use transaction PublicId only and may cross tenants. |
| R5 | A transaction belongs to one Account in the same tenant. `accountId` is Account PublicId. Missing / other-tenant / Adviser-unassigned Customer of that account → 404. |
| R6 | Create or reverse while the Account is **Closed** → 400 ordinary `errors`. Reopen the account first. Close / reopen stay on `/accounts`. |
| R7 | Create or reverse while the Customer is Disabled → 400 `disabled` / `target=user`. List / get of existing rows still work. |
| R8 | Cash amount is a signed book amount in `Account.Currency` only. Body must not send a different currency. Store `decimal(18,4)` + `char(3)`. Application uses Domain `Money` for currency equality; direction lives on the signed decimal, not inside `Money`. |
| R9 | After insert, cash `SUM` for that account must be `≥ 0`. Phase 2 opens no Credit accounts. Violation → 400. Compute in the same command (lock the Account row or equivalent). No persisted balance column. |
| R10 | Posted rows are not `UPDATE`d or `DELETE`d. Correction is a new transaction with `Type = Reversal` and `originalTransactionId` set. |
| R11 | Reversal must target a posted transaction in the same account. Already reversed → 400 (one reversal per original in this increment). Reversal of a reversal is out. |
| R12 | `CloseAccount`: if cash `SUM ≠ 0` → 400 ordinary `errors`. `SUM = 0` (including never booked) may close. Amends accounts R16. Do not bulk-insert on close. Do not write cash inside `Account.Close()`. |
| R13 | Closed accounts stay readable. Net worth (later) excludes them. Balance APIs still return the `SUM` (expected 0 after a successful close). |
| R14 | List envelope `{ items, page, pageSize, totalCount }`; page default 1, size default 20, max 100. Optional `accountId`, optional `customerId`. Neither set → tenant scope (Adviser = assigned Customers). Sort `bookedAt` then id. |
| R15 | Customer has no transaction policy → 403 on every verb. |
| R16 | Type on create: only the increment’s allowed cash set (§5). `Buy` / `Sell` / `Dividend` / unknown → 400 even though the enum lists them. |
| R17 | `CloseOut`: cash leaves to an off-books counterparty. Signed amount must be `< 0` and equal in magnitude to the current `SUM`, so after this row `SUM = 0`. Not a cross-account transfer. Not a holdings liquidation. Amount `0` not allowed; already zero → just `Close`. |
| R18 | Do not treat a pair of unlinked `TransferOut` + `TransferIn` on two accounts as a transfer. Cross-account transfer is out (§13). |
| R19 | Cash `Opening`: `amount > 0`. At most one per account. Allowed only while the account has **no** other transaction. After any non-Opening row exists, `Opening` → 400. Create Account does not insert a row; a new book’s `SUM` is already 0. |
| R20 | `TransferIn` / cash `Opening` → `amount > 0`. `TransferOut` / `CloseOut` → `amount < 0`. `Interest` either sign. `Reversal` = arithmetic opposite of the original cash amount. |
| R21 | `POST /transactions` and `POST /transactions/{id}/reverse` require `Idempotency-Key` (ADR 0015). Missing → 400. Replay same hash → original `201 { id }`. Same key different hash → 409. |
| R22 | Create / reverse do not take Account `rowVersion` from the client. Reverse does **not** take the original transaction `rowVersion`. Server locks the Account row while inserting and summing. |
| R23 | Optional `memo`: trim; if present, 1–200. Optional `reference`: stored nullable, no uniqueness in this increment. |
| R24 | `bookedAt` is required `datetimeoffset` (offset stored). Audit `Created` is separate. |

Policies (register when accepted; map in `RolePermissions`):

| Policy | TenantAdmin | Adviser | SystemAdmin | Customer |
| --- | --- | --- | --- | --- |
| `transactions.read` | ✓ | ✓ | ✓ | |
| `transactions.create` | ✓ | ✓ | ✓ | |

Adviser scope is **assigned Customers** of the transaction’s Account (404 if not). Same width as `accounts.*`. Reverse uses `transactions.create`.

`GET /accounts/{id}/cash-balance` uses `accounts.read` (same row the caller can already get).

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `Transaction` | Aggregate | Business-event header. Not an Account. Not a balance. Not a second “journal” copy. |
| `TransactionCashLeg` | Entity | Accounting line on the cash book. One per transaction in this increment. |
| `TransactionType` | Enum | Full list reserved. HTTP accepts a subset now. |
| `TransactionPosted` / `TransactionReversed` | Event | Raise even with no subscriber. Must **not** be the approve / copy path. |

`TransactionType`:

| Name | This increment | Legs now | Notes |
| --- | --- | --- | --- |
| `TransferIn` | yes | cash + | External deposit onto this account |
| `TransferOut` | yes | cash − | External withdrawal from this account |
| `Interest` | yes | cash + or − | Only extra rule is `SUM ≥ 0` after insert |
| `CloseOut` | yes | cash − equal to current `SUM` | Off-books empty before close |
| `Opening` | yes (cash only) | cash + | Initial inbound. See R19 |
| `Reversal` | yes | opposite cash leg | `POST /transactions/{id}/reverse` |
| `Dividend` | no | cash | Wait until an instrument is in scope |
| `Buy` / `Sell` | no | cash + security | Same aggregate, later increment |
| Split / scrip / bonus | no | security only | Same aggregate, later increment; cash 0 or omitted; total cost unchanged |

Invariants:

- `TenantId` and `AccountId` required. Tenant matches the Account row (application dual check).
- Insert lands **Posted**. No draft / created status in this increment.
- Header is the aggregate root. Cash `SUM` is not stored on the header.
- Cash-leg currency equals `Account.Currency` (denormalised).
- Original transaction is immutable after insert (audit columns aside).
- `Account.Close()` stays a status flip. Zero-cash is an application guard.
- One cash leg per header in this increment (`PostingId`-style unique on `TransactionId`).

```text
Create(accountId, type, cashAmount, bookedAt, memo?, reference?)
  account Open (application)
  type in allowed cash set
  Opening / CloseOut / sign rules
  cash currency = account currency
  insert header + cash leg
  SUM(account) >= 0
  raise TransactionPosted

Reverse(original)
  original in same account, not already reversed
  account Open
  new type = Reversal, originalTransactionId = original
  cash leg = opposite of original cash leg
  SUM(account) >= 0
  raise TransactionReversed
```

Keep [domain-model.md](../domain-model.md) in the same change **when this spec is accepted**.

---

## 6. Database

Script (when accepted): `database/schema/0013_transactions.sql`.

Last applied schema in the repo at Accounts landing is `0012`. Do not back-fill. Do not add EF migrations.

Idempotency **rules** are [ADR 0015](../adr/0015-idempotency-keys.md) (`proposed`). ADR 0013 / 0014 are roles and OpenIddict — not schema files. Next *schema* script after `0012_accounts.sql` is `0013_transactions.sql`. Create the idempotency table **in that same `0013` script** so this slice cannot boot without it. Do not invent an ADR 0013/0014 for the table.

| Table | Change | Index / FK |
| --- | --- | --- |
| `Transactions` | add | PK `Id`. Unique `PublicId`. FK `TenantId` → `Tenants` RESTRICT. FK `AccountId` → `Accounts` RESTRICT. Optional FK `OriginalTransactionId` → `Transactions` RESTRICT. Index `(TenantId, AccountId, BookedAt)`. |
| `TransactionCashLegs` | add | PK `Id`. Unique `(TransactionId)` this increment. FK `TransactionId` → `Transactions` RESTRICT. Amount `decimal(18,4)`. Currency `char(3)` FK `Currencies.Code` RESTRICT. Audit + `RowVersion` same as header. |

Header **and** cash-leg rows use the same audit + concurrency pair as `Accounts` / `Instruments`: `Created`, `CreatedBy`, `LastModified`, `LastModifiedBy`, `RowVersion`. `AuditableEntityInterceptor` fills the four audit fields on both. Posted rows are not updated, so `LastModified*` stay null unless a later spec adds a harmless touch.

Cash-leg columns: `Id`, `TransactionId`, `Amount`, `Currency`, plus the four audit columns and `RowVersion`. HTTP still addresses the header `PublicId` only; the leg `RowVersion` is not a client token in this increment.

No `Accounts.Balance`. No Holdings table. No `Journals` table. No capture table beside `Transactions`.

When accepted, update [database-design.md](../database-design.md) in the same change.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreateTransaction` | new PublicId | R1–R10, R16–R24 |
| Command | `ReverseTransaction` | new PublicId | R10–R11, R6–R9, R21–R22 |
| Command | `CloseAccount` (existing) | 204 | **add** cash `SUM = 0` |
| Query | `GetTransactions` | list envelope | tenant / assignment / R14 |
| Query | `GetTransactionById` | item | 404 rules |
| Query | `GetAccountCashBalance` | `{ accountId, currency, cashBalance }` | `accounts.read` scope |

`cashBalance` on account list/item is the same query, not a second store.

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/transactions` | `transactions.read` | 200 envelope | 400 / 401 / 403 / 404 |
| GET | `/transactions/{id}` | `transactions.read` | 200 item | 401 / 403 / 404 |
| POST | `/transactions` | `transactions.create` | 201 `{ "id" }` | 400 / 401 / 403 / 404 / 409 |
| POST | `/transactions/{id}/reverse` | `transactions.create` | 201 `{ "id" }` | 400 / 401 / 403 / 404 / 409 |
| GET | `/accounts/{id}/cash-balance` | `accounts.read` | 200 | 401 / 403 / 404 |

Create and reverse require `Idempotency-Key`.

### 8.1 Bodies

Create:

```json
{
  "accountId": "<account publicId>",
  "type": "TransferIn",
  "amount": 100.00,
  "bookedAt": "2026-09-24T09:00:00+12:00",
  "memo": "Salary",
  "reference": null
}
```

SystemAdmin create adds `tenantId`. TenantAdmin / Adviser must omit it.

Item:

```json
{
  "id": "<transaction publicId>",
  "tenantId": "<tenant publicId>",
  "accountId": "<account publicId>",
  "customerId": "<customer publicId>",
  "type": "TransferIn",
  "amount": 100.00,
  "currency": "NZD",
  "bookedAt": "2026-09-24T09:00:00+12:00",
  "memo": "Salary",
  "reference": null,
  "originalTransactionId": null,
  "rowVersion": "<base64>"
}
```

This increment flattens the single cash leg onto the item (`amount`, `currency`). When a security leg exists, the item should expose `cash` / `security` objects instead of overloading `amount`; do not invent that shape now.

Account item and account list (amendment when accepted):

```json
{
  "cashBalance": 100.00,
  "currency": "NZD"
}
```

`GET /accounts/{id}/cash-balance` returns `{ "accountId", "currency", "cashBalance" }` with the same numbers.

### 8.2 Errors

Use [api-design.md](../api-design.md) §4.1 only where this spec names `code` / `target`. Ordinary validation stays in `errors` (close-while-cash, post-while-closed, Opening-too-late, CloseOut amount, idempotency mismatch → 409).

Cross-tenant or wrong-collection id → **404**, not 403.

---

## 9. UI

None in this increment. Pages live in [docs/portals/](../portals/). Scalar is enough for SystemAdmin and for local smoke.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | Create / reverse invariants; SUM sign; cannot mutate posted header; Opening / CloseOut rules if placed on the entity |
| Application.FunctionalTests | No Bearer → 401 not 302; Customer → 403; happy TransferIn/Out/Interest/Opening/CloseOut; Opening rejected after another type; second Opening rejected; close rejected while SUM ≠ 0; create rejected while Closed; reverse new id, original untouched; idempotent replay; hash mismatch 409; missing key 400; account JSON includes `cashBalance` |
| Infrastructure.IntegrationTests | Other tenant’s id → 404; Adviser unassigned Customer → 404; no `Accounts.Balance`; no `Journals` table |

TestSeed (Development / TestAppHost only): one `TransferIn` on “Demo everyday bank” so Scalar shows a non-zero `SUM`. Functional tests still create their own rows. Do not seed a fake two-account transfer.

---

## 11. Locked in this spec (discussion, 2026-09-25)

Idempotency: [ADR 0015](../adr/0015-idempotency-keys.md) (`proposed`). Expand [ADR 0011](../adr/0011-ledger-cash-holdings-reversal.md) when this file becomes `accepted`.

| Item | Lock |
| --- | --- |
| Slice packaging | No standalone cash Feature Spec. Cash legs belong here. Holdings / securities may split later. |
| Public model | **Transaction** = business-event header. **Leg** = accounting line. Two layers, one HTTP write. |
| Not in model | Capture table + approve copy into Journal. `Created` + event-handler auto-approve. Two POST collections (`/journals` and `/transactions`). |
| Events | `TransactionPosted` / `TransactionReversed` are side effects. They must not write the books. |
| Future review | Same row, new status + approve command. Not a swapped handler. |
| Later scrip / buy | Same `Transaction` + more legs. Not a new write model. |
| Public route | `/transactions`, policies `transactions.*`, script `0013_transactions.sql`. |
| Balance storage | `SUM` of posted cash legs. No `Account.Balance`. No projection table. |
| Account HTTP | Item + list include computed `cashBalance` + `currency`. Also `GET /accounts/{id}/cash-balance`. |
| Close | Reject if cash `SUM ≠ 0`. Closed forbids new transactions including reversal. `CloseOut` zeroes the book. `Account.Close()` does not write cash. |
| Transfer | Cross-account transfer out (§13). |
| Legs | Header + `TransactionCashLegs` (one cash leg now). Not cash columns on the header. Header **and** cash-leg rows have Created/CreatedBy/LastModified/LastModifiedBy + `RowVersion`. Reverse does not require any `rowVersion`. |
| Mutation | No `UPDATE`/`DELETE` of posted transactions. |
| Line sign | Glossary **Signed book amount**. |
| `bookedAt` | Required `datetimeoffset`. |
| Opening / types / memo / reference / idempotency / concurrency | R19–R24 and the type table in §5. |
| Customer | No ledger-write policy. |
| SystemAdmin | `tenantId` on list/create. |
| Portal | No activity page in this increment. |

Still out of **Phase 2 product**, unchanged: live FX, Customer Portal, Property / Credit selectable.

---

## 12. Still open (does not block `review`)

None that block review. Audit + `RowVersion` on header **and** cash leg, idempotency table in `0013_transactions.sql`, and reverse without `rowVersion` are locked in §4 / §6. Nothing in this section reopens the Transaction-vs-Journal two-step design.

---

## 13. Deferred on purpose — cross-account transfer

Not in this increment. Keep this section after accept:

- A transfer is two cash legs on two accounts and must commit atomically. That is already a two-legged transaction, even with no security.
- Counterparty account may be Closed, different currency, or (if ever allowed) a different Customer.
- Phase 2 has no live FX. Cross-currency transfer is not “amount in = amount out” and must not use rate 1.
- Two unlinked transactions are not a transfer.

When a later increment opens it: same Customer, same currency first; one header or an explicit pair id; both accounts Open; both SUMs valid after the pair; still no Customer writer.

Do not implement a silent shortcut in TestSeed that looks like a transfer.

---

## 14. Suggested commits

Only after `accepted`. Tree must build after each commit. Split each line red then green.

1. `0013_transactions.sql` (+ idempotency table if not `0014`) + EF mapping + integration test that header + cash-leg exist and `Accounts` still has no balance column.
2. Domain `Transaction` + cash leg + unit tests.
3. `CreateTransaction` / `GetTransactions` / `GetTransactionById` + policies + `Idempotency-Key` + functional tests (401 / 403 / 404 / currency / Closed).
4. Cash-zero guard on `CloseAccount` + `CloseOut` / `Opening` rules + tests.
5. `ReverseTransaction` + tests.
6. Account `cashBalance` fields + `/cash-balance` + TestSeed + repo docs (this file `accepted`, ADR 0011 / 0015, domain-model, database-design, api-design, accounts R16 + item JSON).
