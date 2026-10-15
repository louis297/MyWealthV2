---
title: "Reports"
status: accepted
phase: 3
language: en
owner: ""
created: 2026-10-12
last_updated: 2026-10-14
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../architecture.md
  - ../glossary.md
  - ../adr/0017-background-worker.md
  - net-worth.md
  - accounts.md
  - holdings.md
---

# Reports

Phase 3 domain. A catalog of reports plus execution infrastructure on `webapi` and the background `worker` (ADR 0017). Reports are not a single artefact. Each report declares its scope (`Customer` or `Book`), allowed triggers, and the parameters it requires. A shared pipeline handles grant checks, trigger checks, and enqueue; each report supplies its own parameter validation and scope rules.

First phase ships two reports and both UI entry points:

- **Customer Transactions Report** (`Customer` scope) — transactions in a required date range for one Customer. Surfaced on the Customer page.
- **Adviser Balances Report** (`Book` scope) — point-in-time cash and holding lines for every account under an Adviser’s customers. Surfaced on the Adviser Portal Reports tab.

Both support manual trigger and editable recurring schedules in the first phase. Event plumbing is present; concrete events are not locked.

**Status is `accepted`.** Implementation follows this file.

---

## 1. Summary

The platform owns a report catalog. Each report has an id, name, function description, scope (`Customer` or `Book`), and allowed trigger methods (manual / scheduled / event). Tenants are granted access via `TenantReports`. Schedules and executions must name a `ReportId`.

Policies are split by scope:

- `reports.customer.read` / `reports.customer.create`
- `reports.book.read` / `reports.book.create`

Customer receives only `reports.customer.read`. TenantAdmin and Adviser receive all four. SystemAdmin receives none of them. Grant and revoke use `tenants.manage`.

A shared request pipeline checks the coarse policy, the tenant grant, and allowed triggers, then delegates parameter and scope validation to the specific report. The frontend sends `reportId` plus a free-form `parameters` object; it never sends a class or method name. A registry maps `ReportId` to an `IReportGenerator`. Generation runs on the worker via Hangfire. Results are stored and downloadable.

---

## 2. Scope

**In**

- Report catalog (`Reports`): platform table. PublicId (external), Code (stable internal key), Name, Function, Scope, AllowedTriggers, IsActive. No tenant column.
- Tenant–report grant (`TenantReports`): a row must exist before the tenant can use the report. Generator checks Report.IsActive **and** the TenantReport row.
- `ReportSchedule`: must name `ReportId`; carries the parameters the report requires; editable (cron, active flag).
- `ReportExecution`: one run (manual, scheduled, or event).
- Shared pipeline: policy → grant → allowed trigger → report-specific parameter + scope checks → persist → enqueue.
- Manual trigger and editable recurring schedules for both reports.
- Event enqueue hook (no concrete events required to land).
- Worker (Hangfire). Generation re-validates scope.
- Two first-phase reports:
  - Customer Transactions Report
  - Adviser Balances Report
- Output: PDF only, built with QuestPDF inside the generator. No Markdown-to-PDF. No external template files.
- Result files on a shared volume. Execution stores a relative key, not an absolute path.
- `parameters` JSON max 8 KB. Generator accepts only its known fields.
- Policies as above.
- Adviser Portal:
  - Customer page: list + generate + schedules + history for Customer-scoped reports.
  - Reports tab: list + generate + schedules + history for Book-scoped reports.
- Isolation tests.

**Out**

- Per-report policies finer than customer / book.
- Historical as-of snapshots.
- FX fold.
- Live vendor prices.
- Email delivery of results.
- Customer Portal (Customer uses Adviser Portal? No — Customer has no portal client yet; read policy is for a future client or for API completeness. First-phase UI is Adviser Portal only).
- Concrete event bindings.
- Fees, invitation, or other domains.

Note: Customer has `reports.customer.read` but no first-phase UI. The policy exists so a later Customer Portal (or other client) can list and download without a policy change.

---

## 3. Stories

1. As an Adviser I open a Customer and see the Customer-scoped reports I can run for them; I can generate now or create a schedule.
2. As an Adviser I open the Reports tab and see Book-scoped reports; I can generate now or create a schedule for my own book.
3. As a TenantAdmin I can do the same, and on Book-scoped reports I can select a subordinate Adviser.
4. As a Customer (API) I can list and download Customer-scoped executions that target me; I cannot create or schedule.
5. As a SystemAdmin I grant or revoke reports for a tenant (Scalar).
6. As any allowed caller I can change or disable a schedule; the next Hangfire run uses the new definition.
7. As the system, when a registered event occurs, I can enqueue an execution for a report that allows it and that the tenant has been granted.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Policy fail → 403. |
| R2 | Path `{id}` and JSON `id` are PublicId. Internal ints never appear. |
| R3 | A schedule or manual request must name a ReportId. The report must exist, be granted to the tenant, and allow the requested trigger. Otherwise 404. |
| R4 | Coarse policy is required before report-specific checks: Customer-scoped mutations need `reports.customer.create`; Book-scoped mutations need `reports.book.create`. Reads follow the same split. |
| R5 | Customer role has only `reports.customer.read`. Customer may read executions targeted at self. Customer cannot create or schedule. |
| R6 | TenantAdmin and Adviser have all four policies. SystemAdmin has none of them. SystemAdmin grants and revokes with `tenants.manage` and cannot list, request, schedule, or download. |
| R7 | Adviser on a Book-scoped report may only target self. TenantAdmin may target any Adviser in the same tenant. Cross-tenant or unknown → 404. |
| R8 | Adviser on a Customer-scoped report may only target an assigned Customer. TenantAdmin may target any Customer in the tenant. Customer (read) only self. |
| R9 | Each report declares its required parameters and performs its own scope validation after the shared pipeline. |
| R10 | All trigger paths for a given report call the same generation service. |
| R11 | A schedule change updates the Hangfire recurring job before the next fire. |
| R12 | Only succeeded executions expose a download. |
| R13 | Customer Transactions Report: `customerId` + `from` + `to` required; `includeClosedAccounts` defaults false; reversals marked and placed next to the original. |
| R14 | Adviser Balances Report: server sets `asOf` at job creation; zero-balance accounts omitted; holdings with quantity ≤ 0 omitted; `includeClosedAccounts` defaults false; subtotals required; Adviser may omit `adviserId`, other roles may not. |

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| Report | entity (catalog) | Platform-owned. PublicId, Code (stable), Name, Function, Scope, AllowedTriggers, IsActive. |
| TenantReport | entity | Grant: TenantId + ReportId. Presence of the row is the tenant configuration. |
| ReportSchedule | entity | Recurring definition. References ReportId. Stores the parameter set the report requires (e.g. customerId or adviserId). |
| ReportExecution | entity | One run. References ReportId (and optionally ScheduleId). TriggerKind, Status, result reference. |

Invariants:

- A schedule references exactly one Report.
- An execution references exactly one Report.
- A tenant can use a report only when Report.IsActive is true **and** a TenantReport row exists.
- Scope on the Report selects which policy pair applies.
- AllowedTriggers gates manual / scheduled / event.

Domain events:

- Open. Enqueue hook only in this slice.

Update [domain-model.md](../domain-model.md) when accepted.

---

## 6. Database

| Table | Change | Index / FK |
| --- | --- | --- |
| Reports | add | Platform table. PublicId unique; Code unique; Scope; AllowedTriggers; IsActive. No TenantId |
| TenantReports | add | TenantId + ReportId unique. Presence = configured for that tenant |
| ReportSchedules | add | ReportId, TenantId, ParametersJson (max 8 KB), IsActive, Cron. RowVersion + audit |
| ReportExecutions | add | ReportId, ScheduleId (nullable), TenantId, TriggerKind, Status, ParametersJson, AsOf (nullable), TriggeredByUserId, ResultStorageKey, ErrorMessage. RowVersion + audit |

Script: `database/schema/0019_reports.sql`.

Hangfire tables in schema `Hangfire`.

Seed the two first-phase reports (by Code) and (in Development) example TenantReport grants. New tenants are **not** auto-granted.

### 6.1 Scope and AllowedTriggers columns

These are ordinary `nvarchar` columns on `Reports`, not extra tables and not integer enums. Strings keep later value changes free of a schema rewrite.

- `Scope`: `"Customer"` or `"Book"`. Selects which policy pair applies (`reports.customer.*` vs `reports.book.*`) and which parameter set the generator expects.
- `AllowedTriggers`: which of manual / scheduled / event this report accepts. Stored as a JSON array, for example `["manual","scheduled"]`. The pipeline rejects a trigger the report does not allow.
- Seeded codes: `customer-transactions` (PublicId `8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11`, scope `Customer`) and `adviser-balances` (PublicId `5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22`, scope `Book`). Both allow `manual` and `scheduled` only.

No separate lookup table in this phase.

### 6.2 Parameters JSON limit

`parameters` is JSON, but not unbounded.

- API rejects a body whose `parameters` object exceeds 8 KB (400).
- Generator binds only its known fields. Unknown fields are rejected (400).
- String fields inside parameters have their own max lengths (dates, ids). A hacked oversized payload never reaches storage or the generator’s happy path.
- Stored `ParametersJson` is the bound snapshot, not the raw request body.

### 6.3 Result file storage

PDF bytes are not stored in SQL. The file name is stored in the database as part of the relative key.

- Aspire mounts one volume into both `worker` and `webapi`. Not a separate storage container.
- Development: Aspire bind-mounts a host folder into both processes at the same container path (for example `/data/reports`).
- The worker writes the PDF; `webapi` reads it for download. Both see the same files.
- File name / relative key: `{tenantPublicId}/{executionPublicId}.pdf`
- `ReportExecutions.ResultStorageKey` stores that relative key (directory + file name). Absolute paths stay out of the database.
- Download resolves configured root + `ResultStorageKey`.
- Failed runs leave no file and no key. Only Succeeded executions are downloadable.
- No automatic purge in this phase.
- A later environment may use a named Docker volume or object storage; the relative key does not change.

Hangfire tables: created by Hangfire in schema `Hangfire` (documented exception to script-only schema).

Update [database-design.md](../database-design.md) when accepted.

---

## 7. Application use cases

Shared pipeline then report-specific strategy.

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Query | GetReports | granted reports (filterable by scope) | Policy by scope or both; tenant grant |
| Command | GrantTenantReport / Revoke | — | SystemAdmin |
| Command | CreateReportSchedule | schedule id | Policy by report scope; grant; allows scheduled; report-specific params + scope |
| Command | UpdateReportSchedule | — | Same + ownership |
| Command | DisableReportSchedule | — | Same |
| Command | RequestReport | execution id | Policy by report scope; grant; allows manual; report-specific params + scope; enqueue |
| Query | GetReportExecutions | paged | Policy + scope (Customer sees only self) |
| Query | DownloadReportResult | file | Succeeded + same scope rules |
| Internal | EnqueueFromEvent | — | Grant; allows event; report-specific scope |

Generation is resolved by ReportId. Worker calls the same Application service.

### 7.1 Request shape, parameters, and generator registry

The frontend never names a class or method.

Manual request and schedule create both use:

```json
{
  "reportId": "uuid",
  "parameters": { }
}
```

`parameters` is a free-form JSON object. Its contents are defined by the report:

- Customer Transactions Report: `{ "customerId": "uuid", "from": "2026-01-01", "to": "2026-03-31", "includeClosedAccounts": false }`
- Adviser Balances Report: `{ "adviserId": "uuid", "includeClosedAccounts": false }` (Adviser may omit `adviserId`)

Schedule create adds `cron` and `isActive` beside `parameters`, not inside it.

**Registry.** At startup each concrete report registers an `IReportGenerator` against its `ReportId` (or a stable code that resolves to the catalog row):

```text
ReportId → IReportGenerator
  ValidateAndBind(JsonElement parameters) → typed parameters or validation error
  CheckScope(bound, caller)             → 404 on out-of-scope
  Generate(bound)                       → writes the result
```

Shared pipeline:

1. Load catalog row by `reportId` (scope, allowed triggers).
2. Coarse policy, TenantReport grant, allowed-trigger checks.
3. Resolve `IReportGenerator` from the registry.
4. `ValidateAndBind` then `CheckScope`.
5. Persist `ReportSchedule` or `ReportExecution` with the parameter snapshot (JSON).
6. Enqueue Hangfire: `ReportJob.Execute(executionId)` (one fixed entry point).

The Hangfire job loads the execution, resolves the same generator by `reportId`, and calls `Generate` with the stored parameters. Adding a report means a new generator class, a registry entry, and a catalog row. The shared endpoint and the Hangfire entry point do not change.

First phase may hard-code the two parameter forms in the Adviser Portal. A parameter schema on the catalog (for dynamic forms) is out of this slice.

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/tenants/{tenantId}/reports` | `tenants.manage` | 200 grant list, including inactive catalog rows | 401 / 403 / 404 |
| POST | `/tenants/{tenantId}/reports` | `tenants.manage` | 201 `{ "id" }` the first time, 204 when the grant already exists. Inactive catalog rows may be granted | 401 / 403 / 404 |
| DELETE | `/tenants/{tenantId}/reports/{reportId}` | `tenants.manage` | 204 when removed or already absent. Schedule and execution rows stay | 401 / 403 / 404 |
| GET | `/reports` | `reports.customer.read` or `reports.book.read` (filter by scope) | 200 list | 401 / 403 |
| POST | `/reports/schedules` | by report scope (`*.create`) | 201 `{ "id" }` | 400 / 403 / 404 |
| PUT | `/reports/schedules/{id}` | by report scope | 204 | 400 / 403 / 404 / 409 |
| POST | `/reports/schedules/{id}/disable` | by report scope | 204 | 403 / 404 |
| POST | `/reports/request` | by report scope (`*.create`) | 202 `{ "id" }` | 400 / 403 / 404 |
| GET | `/reports/executions` | by scope of the listed reports | 200 list | 401 / 403 |
| GET | `/reports/executions/{id}` | by that execution’s report scope | 200 | 403 / 404 |
| GET | `/reports/executions/{id}/download` | by that execution’s report scope | 200 file | 403 / 404 / 409 |

Customer calling a Book-scoped route or a create route → 403.  
Tenant grant endpoints: SystemAdmin / Scalar.

### 8.1 Bodies

Shared shape. Report-specific fields live under `parameters`.

Customer Transactions (schedule or request):

```json
{
  "reportId": "uuid-of-customer-transactions",
  "parameters": {
    "customerId": "uuid",
    "from": "2026-01-01",
    "to": "2026-03-31",
    "includeClosedAccounts": false
  },
  "cron": "0 9 1 * *",
  "isActive": true
}
```

Adviser Balances (manual request):

```json
{
  "reportId": "uuid-of-adviser-balances",
  "parameters": {
    "adviserId": "uuid",
    "includeClosedAccounts": false
  }
}
```

Manual request omits `cron` / `isActive`. Adviser may omit `adviserId` (defaults to self). TenantAdmin and SystemAdmin must supply `adviserId` or the generator returns 400. `asOf` is never accepted from the client; the server writes it when the job is created. Unknown or missing required parameters → 400 from that report’s `ValidateAndBind`.

### 8.2 Errors

Unknown report, not granted, or trigger not allowed → 404.  
Out-of-scope Customer or Adviser → 404.  
Wrong role for the policy → 403.

---

## 9. First-phase report contracts

### 9.1 Customer Transactions Report

Scope: `Customer`.

Parameters:

| Field | Required | Notes |
| --- | --- | --- |
| `customerId` | yes | PublicId |
| `from` | yes | Date. No “all history” option. No max span in this phase |
| `to` | yes | Date |
| `includeClosedAccounts` | no | Default `false`. When false, transactions on Closed accounts are omitted |

Rules:

- Filter on `bookedAt`: `from` ≤ `bookedAt` < `to` (date interpreted in UTC for the first cut; timezone lock can follow).
- Include all transaction types in range.
- Reversals are marked (type or flag) and listed immediately after their original transaction.
- Suggested columns: bookedAt, account, type, cash amount, instrument, quantity, memo.
- Empty range still produces a PDF that states there are no rows.
- Closed-account filter uses the account’s status at generation time.

### 9.2 Adviser Balances Report

Scope: `Book`.

Parameters:

| Field | Required | Notes |
| --- | --- | --- |
| `adviserId` | Adviser: no (defaults to self). TenantAdmin / SystemAdmin: yes | Omit → 400 for non-Adviser |
| `includeClosedAccounts` | no | Default `false` |

Rules:

- Point-in-time. Server sets `asOf` (UTC datetime) when the execution/job is created. Client cannot send it.
- One line per cash balance and one line per holding. Not a single blended “balance”.
- Cash line: customer, account, account type, currency, cash balance (sum of posted cash legs). Omit the account if cash balance is 0.
- Holding line: customer, account, instrument, quantity, cost, market value. Market value uses current mocked price; missing price → 0. Omit holdings with quantity ≤ 0.
- Closed accounts excluded unless `includeClosedAccounts` is true.
- No FX fold.
- Sort: customer, then account, then cash line (if any) then holding lines.
- PDF ends with per-currency subtotals (cash total and market-value total).
- Header includes report name, tenant, Adviser, `asOf`, and `includeClosedAccounts`.

### 9.3 PDF

- Format: PDF only.
- Built in code inside the report’s `IReportGenerator` (QuestPDF or equivalent).
- No Markdown-to-PDF path. No external template files in this phase.
- Header on both reports: report name, tenant, generation time, and the parameters that define the run (customer or adviser, date range or `asOf`, include-closed flag).
- Amounts formatted with the currency’s `DecimalPlaces`.

---

## 10. UI


Adviser Portal only in this phase.

**Customer page**

- Section listing Customer-scoped reports granted to the tenant.
- Generate now, manage schedules, list executions, download.
- Implicit customerId = the page’s Customer.

**Reports tab**

- Listing Book-scoped reports granted to the tenant.
- Generate now, manage schedules, list executions, download.
- Adviser: adviserId defaults to self (hidden).
- TenantAdmin: Adviser picker.

Catalog and tenant grants stay on Scalar.

---

## 11. Tests

- Customer has only `reports.customer.read`; create and all book routes → 403.
- Adviser and TenantAdmin have all four policies. SystemAdmin has none of them.
- Tenant without grant cannot see or run the report.
- Customer Transactions rejects an unassigned Customer for an Adviser; requires from/to; default excludes Closed; reversal sits next to original.
- Adviser Balances: non-Adviser omitting adviserId → 400; zero cash accounts omitted; quantity ≤ 0 holdings omitted; asOf set server-side; subtotals present.
- Schedule update changes the Hangfire recurring definition.
- Both reports can be scheduled and run manually.
- Generation rules for each report (includeClosedAccounts default false, missing price 0, no FX).

---

## 12. Locked in this spec

No new ADR beyond [0017](../adr/0017-background-worker.md).

| Item | Lock |
| --- | --- |
| Catalog | Platform table. PublicId + stable Code. IsActive (no update API). Scope and AllowedTriggers are nvarchar columns |
| Grant | TenantReports row required **and** Report.IsActive. No auto-grant for new tenants |
| Schedule / request | Must name ReportId; parameters are a free-form JSON object per report |
| Generator | Registry: ReportId → IReportGenerator. Hangfire entry point is fixed (`Execute(executionId)`) |
| Policies | `reports.customer.read/create`, `reports.book.read/create` |
| Roles | Customer: `reports.customer.read` only. TenantAdmin and Adviser: all four. SystemAdmin: none of the four; grants use `tenants.manage` |
| First reports | Customer Transactions Report + Adviser Balances Report |
| PDF | QuestPDF. No MD→PDF. No template files |
| Result storage | Aspire-mounted volume shared by worker and webapi. `ResultStorageKey` stores relative key including file name: `{tenantPublicId}/{executionPublicId}.pdf` |
| Parameters | JSON, max 8 KB, known fields only |
| asOf | Server-set on Adviser Balances when the execution is created. Not a client parameter |
| Schedules | Multiple active schedules allowed. Disable only (no hard delete). Recurring job id = Schedule.PublicId |
| Errors | Bad parameters 400. Unknown report / not granted / out of scope 404. Policy fail 403 |
| Triggers in phase | Manual + scheduled for both; event plumbing only |
| UI | Customer page section + Reports tab |

Still open:

- Concrete events
- Whether Customer will ever get `reports.customer.create`
- Date-boundary timezone (first cut: UTC day)
- Exact reversal mark in the PDF (type label vs also flag the original)
- Hangfire Dashboard auth beyond Development
- Overlapping runs of the same schedule (allowed for now)
- Result retention / purge

---

## 13. Suggested commits

1. Schema + catalog + TenantReports + seed both reports
2. Policies + role map + list granted reports
3. Shared pipeline + Customer Transactions generation + manual + schedule
4. Adviser Balances generation + manual + schedule
5. Executions list + download
6. Event enqueue hook
7. Adviser Portal: Customer page section + Reports tab
