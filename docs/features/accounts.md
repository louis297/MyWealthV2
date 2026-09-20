---
title: Accounts
status: accepted
phase: 2
language: en
owner: ""
created: 2026-09-21
last_updated: 2026-09-21
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
  - instruments.md
  - customers.md
---

# Accounts

Account container on `webapi`: list / get / open / rename / close / reopen. Second Phase-2 ledger slice. Adds script `0012_accounts.sql`, policies `accounts.read` / `accounts.create` / `accounts.manage`, aggregate `Account`, and the Disable-Customer open-account guard.

Cash postings, holdings, Opening, reversals, and net worth are out. This slice does not persist a balance. Later slices store `AccountId` only.

**Status is `accepted`.** Implementation follows this file. Expand [ADR 0011](../adr/0011-ledger-cash-holdings-reversal.md) for the container only — posting storage stays direction.

Landed in the repo 2026-09-21 (`0012_accounts.sql`, `/accounts`, Disable-Customer guard, TestSeed). Depends on accepted [instruments](instruments.md) (landed `9ea2f2a`) and the IsActive rename (`bbd0f26`, `0011`) only as prior slices. Accounts do not reference `InstrumentId`.

---

## 1. Summary

A TenantAdmin or Adviser opens an account under a **Customer** in the current tenant (`name`, `type`, `currency`, `customerId`). SystemAdmin does the same for a chosen tenant (`tenantId` on create and on the list query). `Type` and `Currency` are immutable after open. `Currency` is the cash-book currency (must be an enabled platform code). Phase 2 may open **Bank, Cash, Brokerage, Other** only. Property and Credit stay reserved type names; the body must not select them.

The container is per-Customer, not per-Adviser. Adviser visibility is the existing `AdviserId` assignment — no second adviser FK on Account. Customer receives 403. TenantAdmin / Adviser crossing tenants or an Adviser touching an unassigned Customer → 404. SystemAdmin may read and write any tenant’s row (Scalar now; Back Office later). Adviser Portal has no Accounts page in this slice.

Close / reopen change `Status` (`Open` ↔ `Closed`). `IsActive` follows: Open = 1, Closed = 0. Never an `IsEnabled` column. Never write `IsActive` from HTTP. No physical delete. Disable Customer rejects while any account for that person is still active (guard added here; the customers spec reserved it).

Confirmed 2026-09-21: (1) Adviser may read / open / rename / close / reopen accounts of assigned Customers; (2) HTTP verbs are close / reopen; (3) TestSeed creates demo Customer `customer@localhost` when the demo tenant exists.

---

## 2. Scope

**In**

- Application: `CreateAccount`, `UpdateAccount`, `CloseAccount`, `ReopenAccount`, `GetAccounts`, `GetAccountById`
- Application: Disable Customer rejects while the Customer has an active account (handler / guard on the existing `DisableCustomer` path)
- Domain: `Account` aggregate; `AccountType` (all six names); `AccountStatus` (`Open` / `Closed`)
- Script `0012_accounts.sql` + EF mapping
- Policies `accounts.read`, `accounts.create`, `accounts.manage` (register on ADR 0013 Phase-2 list)
- `webapi` `/accounts` (root, not under `/users`)
- List envelope + `enabledOnly` (filters `IsActive`) + optional `customerId` / `type` + isolation tests
- `TestSeed` rows for Development / TestAppHost (not the schema script)

**Out**

- Cash ledger, holdings, postings, Opening, reversals, net worth, Dashboard
- Balance, quantity, or price on the account row
- Institution name, account number, sort code, IBAN
- A hand-edited `CurrentValue`
- Selecting Property or Credit
- Changing `Type` or `Currency` after open
- Close-and-liquidate (still open; this slice always allows close because there is no cash / holding yet)
- Physical delete
- Customer Portal or ledger-write for Customer
- Adviser Portal Accounts page (portal cut later)
- SystemAdmin ledger screens on `adviser-portal`
- Empty Holding / Journal / CashLedger tables “for later”
- Seeding accounts from `0012_accounts.sql` or in Production
- Reopening Phase 1 people routes for SystemAdmin

---

## 3. Stories

1. As an Adviser I open a Bank / Cash / Brokerage / Other account under an assigned Customer so later cash and holdings have a container.
2. As a TenantAdmin I open the same way for any Customer in my firm, and I rename, close, or reopen that account.
3. As an Adviser I list and open accounts for assigned Customers (default = all rows, each item has `status` and `isActive`; `enabledOnly=true` for active only).
4. As an Adviser I cannot see or write another Adviser’s Customer’s account (404).
5. As a TenantAdmin in another firm I receive 404 for someone else’s account id.
6. As a SystemAdmin I list / open / rename / close accounts for a chosen tenant on Scalar (and later Back Office), not on Adviser Portal.
7. As a Customer I receive 403 on every `/accounts` verb.
8. As a TenantAdmin I cannot disable a Customer who still has an active account (400). Closed accounts do not block disable.
9. As a later cash / holdings slice I attach postings to `AccountId` only. Bank and Cash must refuse holdings when that slice exists.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Policy fail → 403. For TenantAdmin / Adviser: other-tenant, unassigned Customer, or missing id → 404, not 403. SystemAdmin: missing id → 404; another tenant’s id is allowed. |
| R2 | Path `{id}` and JSON `id` are `PublicId`. Internal ints never appear. Create returns `201 { "id" }` only. |
| R3 | Routes live at `/accounts`. Not under `/users`. Not `/users/customers/{id}/accounts`. |
| R4 | TenantAdmin / Adviser: tenant = current user’s `TenantId`. Create / list body or query must **not** include `tenantId` (400). Missing current tenant → 404. Target tenant disabled on **create** → 400 `disabled` / `target=tenant`. List / get / PUT / close / reopen do not reject because the tenant is disabled. |
| R4a | SystemAdmin: no tenant on the token. Create **requires** `tenantId` (tenant PublicId). List **requires** `tenantId`. Omitted → 400. Unknown tenant → 404. Get / PUT / close / reopen use the account PublicId only and may cross tenants. |
| R5 | An account belongs to one Customer in the same tenant. `customerId` is the Customer PublicId. Target must exist, Role = Customer, same tenant; else 404. Adviser: that Customer must be assigned to the caller; else 404. |
| R6 | Create or reopen while the Customer is Disabled → 400 `disabled` / `target=user` (api-design §4.1). Close / rename / get / list of existing rows still work. |
| R7 | `Name` is the display name. Required, 1–200, trim, preserve caller casing. Duplicate names under one Customer are allowed. |
| R8 | `Type` required on create. Phase 2 allowed: `Bank`, `Cash`, `Brokerage`, `Other`. `Property` or `Credit` → 400. Unknown value → 400. Immutable after create. Body on PUT that includes `type` → 400. |
| R9 | `Currency` required on create. Must exist in `ICurrencyCatalog` and be **enabled**. Stored upper-case `char(3)`. Immutable after create. Body on PUT that includes `currency` → 400. This is the cash-book currency, not instrument quote currency. |
| R10 | A later disable of that catalog currency does not rewrite `Currency`. New opens still require an enabled code. |
| R11 | Account has **both** `Status` and `IsActive`. `Status` is the machine: `Open` / `Closed`. `IsActive` is derived: `Open` → 1, `Closed` → 0. Close / reopen are `POST …/close` and `POST …/reopen`. Not fields on PUT. Create lands `Open` / `IsActive = 1`. HTTP never accepts `status` or `isActive` as a writable body field. |
| R12 | HTTP idempotent: already closed + close, or already open + reopen → 204, no second event. |
| R13 | PUT / close / reopen require `rowVersion`. Conflict 409. Missing 400. Same encoding as people / instruments. |
| R14 | PUT updates `name` only. Other fields in the body → 400. |
| R15 | No physical delete. Close is the only way to take a row out of “open” pickers. |
| R16 | This slice does **not** block close because cash ≠ 0 or holdings exist (no those tables). Later slices may add a close guard; they must not change close to `UPDATE` a posting. |
| R17 | List is one tenant. TenantAdmin: current tenant, every Customer. Adviser: current tenant, assigned Customers only. SystemAdmin: `tenantId` query. Every item includes `status`, `isActive`, `tenantId`, `customerId`, `type`, `currency`. `enabledOnly` omitted/`false` = all; `true` = `IsActive` only; any other value 400. Optional `customerId` (Customer PublicId). Optional `type` (exact enum name). `page` default 1, `pageSize` default 20 max 100. Optional `search`: Name contains (CI) or PublicId exact. Sort name then type. |
| R18 | Customer has no account policy → 403 on every verb. SystemAdmin has all three account policies. Adviser Portal does not add SystemAdmin ledger nav. |
| R19 | Disable Customer (existing people route): if any Account for that Customer has `IsActive = true` → 400 ordinary `errors` (not §4.1). Closed-only Customers may be disabled. Do not bulk-close on disable. |
| R20 | Bank / Cash (and reserved Credit) must not hold instruments **when holdings exist**. This slice does not create holdings and does not need a holdings guard. Brokerage / Other (and reserved Property) may hold later. |
| R21 | Type is a capability gate, not a product module. Do not use Other as a stand-in for Property or Credit. |

Policies (register now; map in `RolePermissions`):

| Policy | TenantAdmin | Adviser | SystemAdmin | Customer |
| --- | --- | --- | --- | --- |
| `accounts.read` | ✓ | ✓ | ✓ | |
| `accounts.create` | ✓ | ✓ | ✓ | |
| `accounts.manage` | ✓ | ✓ | ✓ | |

Adviser `manage` is assigned-Customer scope in the handler (same 404 rule as read / create). This is wider than instruments `manage` because the Adviser operates the Customer’s book. Catalog rename / disable stays TenantAdmin-only.

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `Account` | Aggregate | Container under a Customer. Not a cash row. Not a holding. |
| `AccountStatus` | Enum | `Open`, `Closed`. Phase 2 has no third state. |
| `AccountType` | Enum | `Bank`, `Cash`, `Brokerage`, `Other`, `Property`, `Credit`. All six names exist so a later phase does not reshape the type. |
| `AccountOpened` / `AccountClosed` / `AccountReopened` | Event | Raise even with no subscriber yet. Rename raises nothing in this slice. |

Invariants:

- `TenantId` required. `CustomerId` required. Same tenant as the Customer row (application dual check).
- `Type` and `Currency` set on create, then immutable.
- Lifecycle is `Status`. `IsActive` always equals (`Status == Open`). Do not set the bit independently.
- No balance field. No `InstrumentId`.

```text
Create(tenantId, customerId, name, type, currency)
  currency must be supplied by the application as an enabled catalog code
  type is any AccountType; Application rejects Property / Credit in Phase 2
  Status = Open
  IsActive = true   // derived
  raise AccountOpened

Rename(name)
  name non-empty after trim

Close()
  already Closed → no-op
  else Status = Closed, IsActive = false, raise AccountClosed

Reopen()
  already Open → no-op
  else Status = Open, IsActive = true, raise AccountReopened
```

The entity does not call `ICurrencyCatalog`. Application looks up the currency and the Customer, then calls `Create`.

Capability matrix (product; holdings slice enforces the holdings column):

| Type | Phase 2 selectable | Cash book | Holdings (later) | Net worth (later) |
| --- | --- | --- | --- | --- |
| Bank | Yes | Yes | Forbidden | Asset = cash |
| Cash | Yes | Yes | Forbidden | Asset = cash |
| Brokerage | Yes | Yes | Allowed | Asset = cash + holding market value |
| Other | Yes | Yes | Allowed | Asset = cash + holding market value |
| Property | No | Yes | Allowed | Asset = cash + holding market value |
| Credit | No | Yes; may be negative later | Forbidden | Liability = cash |

Bank means deposit-style cash (current / savings), not an institution and not a brokerage.

Keep [domain-model.md](../domain-model.md) in the same change.

---

## 6. Database

Script: `database/schema/0012_accounts.sql`.

`0011` is the last applied script. Do not back-fill. Do not add EF migrations.

| Table | Change | Index / FK |
| --- | --- | --- |
| `Accounts` | add | PK `Id`. Unique `PublicId`. FK `TenantId` → `Tenants` RESTRICT. FK `CustomerId` → `Users` RESTRICT. FK `Currency` → `Currencies.Code` RESTRICT. Index `(TenantId, CustomerId)`. Index `(TenantId, IsActive)`. CHECK `Type` in 0–5. CHECK `Status` in 0–1 |

### 6.1 `Accounts`

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | int identity | no | Internal PK |
| PublicId | uniqueidentifier | no | Default `NEWSEQUENTIALID()` or app UUID. HTTP id |
| TenantId | int | no | FK Tenants. Denormalised from Customer for isolation / list |
| CustomerId | int | no | FK Users. Application: Role = Customer, same TenantId |
| Name | nvarchar(200) | no | Display name |
| Type | int | no | 0 Bank, 1 Cash, 2 Brokerage, 3 Other, 4 Property, 5 Credit |
| Status | int | no | 0 Open, 1 Closed. Source of truth |
| Currency | char(3) | no | FK Currencies. Immutable in Domain |
| IsActive | bit | no | Derived persisted computed. `1` iff `Status = 0` (Open). Same pattern as `Users.IsActive` |
| Created | datetimeoffset | no | |
| CreatedBy | nvarchar(450) | no | |
| LastModified | datetimeoffset | yes | |
| LastModifiedBy | nvarchar(450) | yes | |
| RowVersion | rowversion | no | |

```sql
[IsActive] AS ISNULL(CAST(CASE WHEN [Status] = 0 THEN 1 ELSE 0 END AS bit), 0) PERSISTED
```

No balance, institution, account-number, InstrumentId, or value columns.

Keep [database-design.md](../database-design.md) in the same change.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreateAccount` | PublicId | `accounts.create`; tenant from caller or SystemAdmin `tenantId`; Customer in that tenant; Adviser assigned; enabled currency; Phase-2 type |
| Command | `UpdateAccount` | void | `accounts.manage`; `rowVersion`; name only; Adviser assigned; SystemAdmin may cross tenants |
| Command | `CloseAccount` | void | `accounts.manage`; `rowVersion`; idempotent |
| Command | `ReopenAccount` | void | `accounts.manage`; `rowVersion`; idempotent; Customer not Disabled |
| Query | `GetAccounts` | list envelope | `accounts.read`; one tenant; Adviser assigned filter; `enabledOnly`; `customerId`; `type`; search; page |
| Query | `GetAccountById` | one item | `accounts.read`; TenantAdmin same tenant; Adviser assigned; SystemAdmin any tenant |
| Command | `DisableCustomer` (existing) | void | add open-account guard; 400 while any `IsActive` |

### 7.1 No new ports

Do not add `IMarketData` / `IFxRate` callers. Those ports already shipped with instruments.

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/accounts` | `accounts.read` | 200 envelope | 401 / 403 / 400 |
| GET | `/accounts/{id}` | `accounts.read` | 200 item | 401 / 403 / 404 |
| POST | `/accounts` | `accounts.create` | 201 `{ "id" }` | 401 / 403 / 400 / 404 |
| PUT | `/accounts/{id}` | `accounts.manage` | 204 | 401 / 403 / 400 / 404 / 409 |
| POST | `/accounts/{id}/close` | `accounts.manage` | 204 | 401 / 403 / 400 / 404 / 409 |
| POST | `/accounts/{id}/reopen` | `accounts.manage` | 204 | 401 / 403 / 400 / 404 / 409 |

List envelope: `{ items, page, pageSize, totalCount }`.

### 8.1 Bodies

Create (TenantAdmin / Adviser):

```json
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Everyday spending",
  "type": "Bank",
  "currency": "NZD"
}
```

Create (SystemAdmin — `tenantId` required):

```json
{
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Everyday spending",
  "type": "Bank",
  "currency": "NZD"
}
```

PUT:

```json
{
  "name": "Everyday spending",
  "rowVersion": "<base64>"
}
```

Close / reopen body:

```json
{
  "rowVersion": "<base64>"
}
```

List / get item:

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Everyday spending",
  "type": "Bank",
  "currency": "NZD",
  "status": "Open",
  "isActive": true,
  "rowVersion": "<base64>"
}
```

GET list does not include a balance.

`GET /accounts` for SystemAdmin requires `?tenantId=`. TenantAdmin / Adviser must omit it.

`type` and `status` in JSON are enum names, not ints.

### 8.2 Errors

Ordinary validation (bad type, reserved type, disabled currency, extra PUT fields, open accounts on Disable Customer) → 400 `errors`.

Create while the target tenant is disabled → §4.1 `code=disabled`, `target=tenant`.

Create or reopen while the target Customer is Disabled → §4.1 `code=disabled`, `target=user`.

Unknown id → 404. Other-tenant / unassigned id → 404 for TenantAdmin / Adviser; 200 / 204 for SystemAdmin when the row exists.

Keep the resource catalog in [api-design.md](../api-design.md) pointing here for field rules.

---

## 9. UI

None in this slice.

Adviser Portal Accounts page is a later portal cut. Do not invent nav or form layout here.

SystemAdmin uses Scalar (and later Back Office), not this portal.

### 9.1 Test seed

Contribute to host `TestSeed` (Development and TestAppHost only). Not `0012_accounts.sql`. Not Production.

If the Development demo tenant (`DevelopmentIdentitySeeder.DemoTenantCode` = `demo`) is missing, return (same no-op as instruments). TestAppHost skips the demo tenant today.

If the demo tenant exists and the demo Adviser (`adviser@localhost`) exists but Customer `customer@localhost` does not, create that Customer (Active, assigned to the demo Adviser, password same pattern as the demo people, idempotent on email). Do not seed a Customer when the demo tenant is absent.

Then ensure accounts idempotent on `(CustomerId, Name)`:

| Name | Type | Currency | Status | IsActive |
| --- | --- | --- | --- | --- |
| Demo everyday bank | Bank | NZD | Open | true |
| Demo brokerage | Brokerage | USD | Open | true |
| Demo cash tin | Cash | NZD | Open | true |
| Demo closed other | Other | NZD | Closed | false |

CreatedBy = `TestSeed.CreatedBy` (`system`).

Functional tests do **not** rely on these rows for isolation; they create their own accounts and Customers. One smoke test may assert the four names exist after a host start that includes the demo tenant.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | Create stores type and currency; type / currency cannot change; close / reopen idempotent; events only on actual change; name trim |
| Application.FunctionalTests | No Bearer → 401 not 302; Customer → 403; Adviser POST 201 and GET 200 for assigned Customer; Adviser other Adviser’s Customer → 404; TenantAdmin PUT / close 204; SystemAdmin list/create without `tenantId` → 400; SystemAdmin with tenant PublicId POST 201 and PUT 204 on another tenant’s id; TenantAdmin other-tenant id → 404; Property / Credit 400; disabled currency 400; `enabledOnly=true` omits closed; list envelope paging; `rowVersion` 409; Disable Customer with open account → 400; Disable Customer with only closed accounts → 204; reopen on Disabled Customer → 400 `disabled`/`user` |
| Infrastructure.IntegrationTests | Two-tenant fixture: TenantAdmin other tenant’s PublicId → 404. Same type name allowed in tenant B. TestSeed idempotent on second run when demo tenant exists. Table + unique PublicId + FKs exist. `IsActive` computed from `Status` |

No cash or holdings fixture in this slice.

---

## 11. Locked in this spec

No new ADR. Expand [ADR 0011](../adr/0011-ledger-cash-holdings-reversal.md) for the container (Customer parent, immutable type + currency, `Status` + derived `IsActive`, capability matrix). Do not implement from 0011 alone.

| Item | Lock |
| --- | --- |
| Scope | Container under a Customer. No cash or holding columns |
| Callers | Adviser read / create / manage assigned Customers. TenantAdmin all in tenant. SystemAdmin all verbs, Scalar. Customer 403 |
| `Type` / `Currency` | Immutable after open. Phase 2 selectable: Bank, Cash, Brokerage, Other |
| HTTP namespace | `/accounts` at the root |
| Lifecycle | `Status` Open / Closed. Derived `IsActive` persisted computed. Close / reopen + `rowVersion`. No physical delete |
| Script | `0012_accounts.sql` |
| Policies | `accounts.read` / `accounts.create` / `accounts.manage`. SystemAdmin has all three. Adviser has all three (assigned scope) |
| Disable Customer | Reject while any account is active |
| Portal | No page in this slice. SystemAdmin uses Scalar |
| TestSeed | Demo Customer `customer@localhost` if missing; four named accounts on the demo tenant |

Still open, not this slice:

- Cash / holding / posting / Opening / net-worth / Dashboard
- Whether close of an account forces liquidation
- Institution / account-number columns
- Live market data
- Opening Property or Credit
- Adviser Portal Accounts page

---

## 12. Suggested commits

The repository should build after each commit. Agents split each line into red then green.

1. `0012_accounts.sql` + EF mapping + integration test “table exists”
2. `Account` aggregate + `AccountType` + `AccountStatus` + Domain.UnitTests
3. Policies registered in `RolePermissions`
4. Create / get / list handlers + functional tests (401 / 403 / isolation / assigned scope / reserved type)
5. Update / close / reopen + `rowVersion` 409
6. Disable Customer open-account guard + functional tests
7. `TestSeed` demo Customer (if missing) + four accounts + idempotent integration test

Do not put cash, holdings, Opening, or portal pages in these commits.
