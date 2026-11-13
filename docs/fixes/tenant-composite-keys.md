---
title: Fix - Tenant composite foreign keys and TenantId on child tables
status: accepted
language: en
created: 2026-11-13
updated: 2026-11-13
related:
  - ../database-design.md
  - ../adr/0005-shared-database-tenantid-isolation.md
  - ../adr/0008-schema-sql-as-source-of-truth.md
  - tenant-query-filter.md
  - ../features/leg-accounts.md
  - ../features/portfolios.md
---

# Fix: Tenant composite foreign keys and TenantId on child tables

Status: accepted (2026-11-13). Build contract.

## Problem

ADR 0005 says that arbitrary `TenantId` values cannot be inserted. That holds for the `TenantId` column itself, which has a foreign key to `Tenants`. It does not hold for the other references between business rows.

1. **Every reference between tenant rows is a single-column FK.** `Holdings.AccountId → Accounts(Id)` only proves that the Account exists. The database accepts a Holding in tenant A that points at an Account or an Instrument in tenant B. The same is true for every other parent → child pair in the list below. Today only the handlers prevent this.
2. **Some child tables have no `TenantId`.** `TransactionCashLegs`, `TransactionSecurityLegs`, `TransactionFxDetails` and `PortfolioLines` carry none. The [tenant query filter](tenant-query-filter.md) cannot cover them, and a composite FK cannot be built from them. Since [leg accounts](../features/leg-accounts.md) (`0024`), the Account of a Transaction is stored **only** on the legs. So the Transaction → Account reference, the one that drives balances and holdings, has no tenant check in the database at all.

## Decision

1. **Every child table of a tenant row carries `TenantId`.** `TransactionCashLegs`, `TransactionSecurityLegs`, `TransactionFxDetails` and `PortfolioLines` get `TenantId int NOT NULL`, back-filled from the parent row.
2. **Every reference between two tenant rows is a tenant composite FK.** The child's `(TenantId, XId)` references the parent's `(TenantId, Id)`. Each parent gets `UNIQUE (TenantId, Id)` so the FK has a key to reference. The composite FK **replaces** the single-column FK. It is not added next to it.
3. **References to platform rows stay single-column.** FKs to `Tenants`, `Currencies`, `Reports` and `AspNetUsers` do not change.
4. **The child's `TenantId` is a domain property.** The aggregate that builds the child row passes its own `TenantId` down: `Transaction` for cash legs, security legs and the FX detail, `Portfolio` for lines. No EF shadow property, and no value filled in during `SaveChanges`.
5. **EF does not map the composite FKs.** [ADR 0008](../adr/0008-schema-sql-as-source-of-truth.md): the SQL scripts are the schema truth. EF keeps its single-column relationships for navigation and insert order. The only EF change is the new `TenantId` property on the four child entities. The tenant query filter picks them up automatically, because it applies to every entity that has a `TenantId` property.
6. **`RebalanceJob` engages the tenant filter.** It loads the Account by public id and engages the filter with `account.TenantId` before it rebalances, as `ReportJob` already does with the execution row.

### Child tables that get `TenantId` (script `0027`)

| Table | Back-filled from | Nullable |
| --- | --- | --- |
| `TransactionCashLegs` | `Transactions.TenantId` via `TransactionId` | no |
| `TransactionSecurityLegs` | `Transactions.TenantId` via `TransactionId` | no |
| `TransactionFxDetails` | `Transactions.TenantId` via `TransactionId` | no |
| `PortfolioLines` | `Portfolios.TenantId` via `PortfolioId` | no |

Each new column also gets a single-column FK → `Tenants(Id)` (RESTRICT), like every other business table.

`InstrumentPrices` is **not** in the list. Prices come from a platform market data source and are written by the worker per Instrument row. Reading another tenant's price leaks almost nothing. It keeps its single-column FK to `Instruments`.

### Parent keys (script `0028`)

| Parent | New constraint |
| --- | --- |
| `Users` | `UQ_Users_TenantId_Id` UNIQUE (`TenantId`, `Id`) |
| `Accounts` | `UQ_Accounts_TenantId_Id` |
| `Instruments` | `UQ_Instruments_TenantId_Id` |
| `Transactions` | `UQ_Transactions_TenantId_Id` |
| `Portfolios` | `UQ_Portfolios_TenantId_Id` |
| `ReportSchedules` | `UQ_ReportSchedules_TenantId_Id` |

`Users.TenantId` is nullable (SystemAdmin). A unique constraint on `(TenantId, Id)` still holds, because `Id` is unique on its own.

### Tenant composite FKs (script `0028`)

Name: `FK_<Child>_<Parent>_TenantId_<Column>`. All RESTRICT, like the FKs they replace.

| Child | Child columns | Parent | Replaces |
| --- | --- | --- | --- |
| `Users` | `TenantId`, `AdviserId` | `Users` | `FK_Users_Users_AdviserId` |
| `Accounts` | `TenantId`, `CustomerId` | `Users` | `FK_Accounts_Users_CustomerId` |
| `Holdings` | `TenantId`, `AccountId` | `Accounts` | `FK_Holdings_Accounts_AccountId` |
| `Holdings` | `TenantId`, `InstrumentId` | `Instruments` | `FK_Holdings_Instruments_InstrumentId` |
| `Transactions` | `TenantId`, `OriginalTransactionId` | `Transactions` | `FK_Transactions_Transactions_OriginalTransactionId` |
| `Transactions` | `TenantId`, `ReversedByTransactionId` | `Transactions` | `FK_Transactions_Transactions_ReversedByTransactionId` |
| `TransactionCashLegs` | `TenantId`, `TransactionId` | `Transactions` | `FK_TransactionCashLegs_Transactions_TransactionId` |
| `TransactionCashLegs` | `TenantId`, `AccountId` | `Accounts` | `FK_TransactionCashLegs_Accounts_AccountId` |
| `TransactionSecurityLegs` | `TenantId`, `TransactionId` | `Transactions` | `FK_TransactionSecurityLegs_Transactions_TransactionId` |
| `TransactionSecurityLegs` | `TenantId`, `AccountId` | `Accounts` | `FK_TransactionSecurityLegs_Accounts_AccountId` |
| `TransactionSecurityLegs` | `TenantId`, `InstrumentId` | `Instruments` | `FK_TransactionSecurityLegs_Instruments_InstrumentId` |
| `TransactionFxDetails` | `TenantId`, `TransactionId` | `Transactions` | `FK_TransactionFxDetails_Transactions_TransactionId` |
| `PortfolioLines` | `TenantId`, `PortfolioId` | `Portfolios` | `FK_PortfolioLines_Portfolios_PortfolioId` |
| `PortfolioLines` | `TenantId`, `InstrumentId` | `Instruments` | `FK_PortfolioLines_Instruments_InstrumentId` |
| `AccountPortfolioAssignments` | `TenantId`, `AccountId` | `Accounts` | `FK_AccountPortfolioAssignments_Accounts_AccountId` |
| `AccountPortfolioAssignments` | `TenantId`, `PortfolioId` | `Portfolios` | `FK_AccountPortfolioAssignments_Portfolios_PortfolioId` |
| `ReportExecutions` | `TenantId`, `ScheduleId` | `ReportSchedules` | `FK_ReportExecutions_ReportSchedules_ScheduleId` |
| `ReportExecutions` | `TenantId`, `TriggeredByUserId` | `Users` | `FK_ReportExecutions_Users_TriggeredByUserId` |

SQL Server does not check a composite FK when any of its columns is NULL. That is the intended result for every nullable reference above:

- `Users.AdviserId` is NULL for SystemAdmin, TenantAdmin and Adviser.
- `Transactions.OriginalTransactionId` and `ReversedByTransactionId` are NULL on most rows.
- `ReportExecutions.ScheduleId` is NULL for a manual run. `TriggeredByUserId` is NULL for a scheduled run. SystemAdmin cannot trigger a report ([reports](../features/reports.md) R6), so a non-null `TriggeredByUserId` always points at a user in the same tenant.

Existing indexes stay as they are. SQL Server does not need an index on the child columns of a FK. No parent row is deleted today, so the missing index costs nothing.

### Unchanged references

| Reference | Why it stays single-column |
| --- | --- |
| `* → Tenants (TenantId)` | The parent is the tenant itself |
| `* → Currencies` | Platform catalog |
| `ReportSchedules → Reports`, `ReportExecutions → Reports`, `TenantReports → Reports` | Platform catalog |
| `Users → AspNetUsers (IdentityUserId)` | Identity store; `AspNetUsers.TenantId` is a projection with no FK (ADR 0005) |
| `InstrumentPrices → Instruments` | Excluded above |
| `UserTokens`, `OutboxMessages`, `IdempotencyRecords` | No FK to a tenant row today |

### Pre-check before any change

The schema applicator runs each script without a surrounding transaction. A script that fails halfway leaves some constraints dropped and others not yet added. So `0028` starts with a read-only check and changes nothing if it fails.

- For each row of the composite FK table, count child rows whose parent exists but has a different `TenantId`.
- If any count is above zero, `THROW` with one message that names each failing FK and its row count, for example `0028_tenant_composite_keys: cross-tenant references found: FK_Holdings_Accounts_TenantId_AccountId=2`. Then stop.
- Only after the check passes: add the parent unique keys, drop the old FKs, add the composite FKs.

`0027` needs no pre-check. Its back-fill copies the parent's `TenantId`, so it cannot create a mismatch. It follows the `0024` pattern: add the column as NULL, back-fill, set it NOT NULL, then add the FK to `Tenants`. Statements that name the new column run inside `EXEC(N'...')`, so they compile after the column exists.

Before the first deploy that carries `0028`, run the pre-check query by hand against production. If it finds rows, stop and decide what to do with them. Do not edit `0028` to skip them.

## Out of scope

- Making the tenant query filter fail-closed (a three-state accessor). See the [tenant query filter](tenant-query-filter.md) fix.
- Engaging the filter anywhere other than `RebalanceJob`.
- `TenantId` on `InstrumentPrices`.
- Modelling the composite FKs or alternate keys in EF.
- SQL Row-Level Security (ADR 0005).
- Any change to handlers, routes or the public API.

## Behaviours

The tree compiles after every commit. Each behaviour is a red commit and a green commit.

### B1. Child rows carry their parent's `TenantId`

Script `0027_child_tenant_ids.sql`, the `TenantId` property on `TransactionCashLeg`, `TransactionSecurityLeg`, `TransactionFxDetail` and `PortfolioLine`, the aggregate factories that set it, and the EF mapping all land in **one** green commit. The column is NOT NULL, so EF inserts fail as soon as the script runs without the mapping, and the mapping fails without the column.

Tests:

- Domain unit: every factory on `Transaction` sets each cash leg, security leg and FX detail `TenantId` to the Transaction's `TenantId`. This includes the Reversal, which copies legs and the FX detail. `Portfolio` create and `ReplaceLines` set each line's `TenantId` to the Portfolio's.
- Integration (`AppliedSchema`): the four columns exist, are `int NOT NULL`, and have a FK to `Tenants`. Applying `0027` to a database that already holds legs, an FX detail and portfolio lines from two tenants back-fills each row with its parent's `TenantId` (same pattern as `Apply0024_BackFillsEveryLegFromItsHeaderAccount`).
- Integration (`Data/TenantQueryFilterTests`): with the filter engaged to tenant A, `TransactionCashLegs`, `TransactionSecurityLegs`, `TransactionFxDetails` and `PortfolioLines` return no tenant B rows. With the filter not engaged they return both.

### B2. The database rejects a reference to another tenant's row

Script `0028_tenant_composite_keys.sql`.

Tests (integration, `AppliedSchema`):

- For each row of the composite FK table: insert a parent in tenant B and a child in tenant A that points at it with raw SQL. The insert fails with SQL error 547 and the new constraint name. The same insert with matching tenants succeeds.
- The six parent unique keys exist.
- None of the replaced single-column FK names still exist.
- Pre-check: on a database with every script up to `0027` applied and one cross-tenant Holding inserted, applying `0028` throws with a message that contains `FK_Holdings_Accounts_TenantId_AccountId=1`. Afterwards `FK_Holdings_Accounts_AccountId` still exists and no composite FK exists.

Existing schema tests that assert a replaced FK name are updated in the green commit. That is a spec change, not a weakened test.

### B3. `RebalanceJob` engages the tenant filter

Test (application or functional, whichever layer already runs `RebalanceJob`): after `RebalanceJob.Execute` for an Account in tenant A, the scope's `ITenantIdAccessor` is engaged with tenant A's id. For an unknown account id the job behaves as it does today.

## Doc changes when this is accepted

- [database-design.md](../database-design.md): the tenancy convention row in §3, the FK table in §7, the note under §6.6 that a Customer's same-tenant Adviser is enforced only in the application, §11.4 and §11.6 ("No TenantId" on `TransactionFxDetails` and `PortfolioLines`), the script order in §9, and the change log.
- [ADR 0005](../adr/0005-shared-database-tenantid-isolation.md): Decision item 1 adds the tenant composite FK rule.
- [tenant-query-filter.md](tenant-query-filter.md): the sentence "Cash and security legs have no `TenantId` and stay unfiltered" no longer holds.
- [glossary.md](../glossary.md): "Tenant composite FK" (added with this fix).
