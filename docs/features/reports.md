---
title: "Reports"
status: draft
phase: 3
language: en
owner: ""
created: 2026-10-12
last_updated: 2026-10-12
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

Phase 3 domain. A report infrastructure on `webapi` plus the background `worker` (ADR 0017). First concrete report is the Adviser Funds Report: current cash balances and holding market values for every open account under one Adviser, grouped by currency. Manual trigger and editable recurring schedule. No new ledger aggregates.

**Status is `draft`.** Not a build contract.

---

## 1. Summary

A TenantAdmin or Adviser requests an Adviser Funds Report for one Adviser. An Adviser may only request their own book. A TenantAdmin selects one subordinate Adviser. The report is generated asynchronously by Hangfire (manual one-off or recurring). The result is stored and downloadable. Recurring schedules can be created, updated, or disabled at any time; the next run uses the updated definition.

Cash is the existing computed sum of posted cash legs. Holding market value uses the current mocked price (missing price → 0), same as net-worth. Results stay per-currency. No FX fold. Closed accounts are excluded.

---

## 2. Scope

**In**

- Application: create / update / disable schedule; enqueue manual generation; list executions; download result.
- Domain: `ReportSchedule` and `ReportExecution` entities (or equivalent). No change to Account, Holding, or Transaction.
- HTTP: schedule and execution endpoints under `/reports`.
- Worker: Hangfire job that calls the same Application generation service used by the manual path. Scope is re-validated.
- Script: `0018_reports.sql` (or next free number) for `ReportSchedules` and `ReportExecutions`. Hangfire tables in the `Hangfire` schema (created by Hangfire or an explicit script).
- Policy: new `reports.read` / `reports.create` (or reuse `accounts.read` for the first cut — decide before accept).
- Adviser Portal: minimal pages to trigger, list, download, and manage own schedules. TenantAdmin gains an Adviser picker.
- Isolation tests.

**Out**

- Historical as-of snapshots or “vs yesterday”.
- FX fold to a single currency.
- Live vendor prices.
- Performance, TWR, fees, or invoice generation.
- Customer Portal access.
- SystemAdmin UI (Scalar is enough for the first cut).
- Multiple report types beyond Adviser Funds Report.
- Email delivery of the finished report (later).

---

## 3. Stories

1. As an Adviser I manually request a funds report for my assigned customers and accounts, so that I can download a point-in-time statement.
2. As a TenantAdmin I select one subordinate Adviser and request the same report for that Adviser’s book.
3. As an Adviser I create a recurring schedule (frequency and time) for my own funds report, and I can change or disable it at any time.
4. As a TenantAdmin I create or edit a recurring schedule bound to one subordinate Adviser.
5. As an Adviser or TenantAdmin I list past executions and download a succeeded result.
6. As an Adviser I cannot request or see a report for another Adviser’s customers.
7. As a Customer I receive 403.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Policy fail → 403. |
| R2 | Path `{id}` and JSON `id` are PublicId. Internal ints never appear. |
| R3 | Adviser may only target self. TenantAdmin may target any Adviser in the same tenant. Cross-tenant or unknown Adviser → 404. |
| R4 | Generation re-validates the Adviser’s current assignments. Customers or accounts no longer in scope are omitted (or the run fails — lock before accept). |
| R5 | Report is current point-in-time only. Closed accounts are excluded. Missing instrument price contributes 0. |
| R6 | Results are grouped by currency. No FX conversion. |
| R7 | A schedule change (cron, active flag, target Adviser) updates the Hangfire recurring job before the next fire. |
| R8 | Manual and recurring paths call the same Application generation service. |
| R9 | Only succeeded executions expose a download. Failed executions expose an error message. |
| R10 | Customer receives 403 on all report endpoints. |

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| ReportSchedule | entity | Owns recurring definition and scope (TenantId + AdviserId). |
| ReportExecution | entity | One run (manual or from a schedule). Status, timestamps, result reference, error. |
| ReportType | enum / string | First value: `AdviserFunds`. |

Invariants:

- A schedule is bound to exactly one Adviser inside one Tenant.
- An execution is bound to exactly one Adviser inside one Tenant.
- Only one active schedule per (Tenant, Adviser, ReportType) in the first cut (open: allow multiple).

Domain events:

- None required for the first cut.

Update [domain-model.md](../domain-model.md) when this spec is accepted.

---

## 6. Database

| Table | Change | Index / FK |
| --- | --- | --- |
| ReportSchedules | add | TenantId, AdviserId, ReportType, IsActive; unique filtered index on active (TenantId, AdviserId, ReportType) if R-invariant holds |
| ReportExecutions | add | ScheduleId (nullable), TenantId, AdviserId, Status, StartedAt; FK to ReportSchedules |

Script: `database/schema/0018_reports.sql` (number may shift).

Hangfire storage tables live in schema `Hangfire`. Prefer letting Hangfire create them on startup; document the exception to “scripts are the only schema changes”. Alternative: an explicit script if the team wants full control.

Update [database-design.md](../database-design.md) when this spec is accepted. Do not add EF migrations.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | CreateReportSchedule | schedule id | Caller scope; Adviser exists and is in tenant |
| Command | UpdateReportSchedule | — | Ownership / TenantAdmin; updates Hangfire recurring job |
| Command | DisableReportSchedule | — | Same |
| Command | RequestAdviserFundsReport | execution id | Scope; enqueues Hangfire one-off |
| Query | GetReportExecutions | paged list | Scope |
| Query | GetReportExecution | item | Scope |
| Query | DownloadReportResult | file stream | Succeeded only; scope |

Generation service (called by Hangfire job):

- Loads the target Adviser’s open accounts and holdings.
- Computes cash and holding market value per currency (reuse net-worth calculation style).
- Writes the result and marks the execution Succeeded or Failed.

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| POST | `/reports/schedules` | `reports.create` | 201 `{ "id" }` | 400 / 403 / 404 |
| PUT | `/reports/schedules/{id}` | `reports.create` | 204 | 400 / 403 / 404 / 409 |
| POST | `/reports/schedules/{id}/disable` | `reports.create` | 204 | 403 / 404 |
| POST | `/reports/adviser-funds` | `reports.create` | 202 `{ "id" }` (execution) | 400 / 403 / 404 |
| GET | `/reports/executions` | `reports.read` | 200 list | 401 / 403 |
| GET | `/reports/executions/{id}` | `reports.read` | 200 item | 403 / 404 |
| GET | `/reports/executions/{id}/download` | `reports.read` | 200 file | 403 / 404 / 409 (not ready) |

List envelope: `{ items, page, pageSize, totalCount }`.

### 8.1 Bodies

Create / update schedule (sketch):

```json
{
  "adviserId": "uuid-or-omit-for-self",
  "cron": "0 9 1 * *",
  "isActive": true
}
```

Manual request:

```json
{
  "adviserId": "uuid-or-omit-for-self"
}
```

### 8.2 Errors

Cross-tenant or out-of-scope Adviser → 404.  
Customer → 403.  
Download of non-succeeded execution → 409.

---

## 9. UI

Adviser Portal gains a Reports area (exact routes in a later portal note):

- Request now
- List executions and download
- Manage my schedules (create / edit / disable)

TenantAdmin sees an Adviser picker on the request and schedule forms. Adviser does not.

Portal pages are not required to accept this backend slice; they can follow.

---

## 10. Tests

- Isolation: Adviser cannot see or trigger another Adviser’s report.
- TenantAdmin can target any Adviser in the tenant; cannot target another tenant.
- Schedule update changes the Hangfire recurring definition.
- Generation omits closed accounts and treats missing price as 0.
- Manual and recurring paths produce the same shape for the same inputs.

---

## 11. Locked in this spec

No new ADR beyond [0017](../adr/0017-background-worker.md).

| Item | Lock |
| --- | --- |
| Worker | General-purpose `worker` + Hangfire (ADR 0017) |
| First report | Adviser Funds Report, current point-in-time, per-currency, no FX |
| Triggers | Manual one-off + editable recurring |
| Scope | Adviser = self; TenantAdmin = one chosen Adviser |

Still open, not this slice:

- Exact policy names (`reports.*` vs reuse `accounts.read`)
- Result storage (DB blob vs file vs later object storage)
- Whether multiple active schedules per Adviser + type are allowed
- As-of historical reports
- Email delivery
- Additional report types
- SystemAdmin / Back Office screens

---

## 12. Suggested commits

The repository should build after each commit. Agents split each line into red then green.

1. Schema script + entities + Hangfire wiring in `worker`
2. Schedule commands + recurring job registration
3. Manual request + generation service + execution queries
4. Download endpoint
5. Adviser Portal pages (optional follow-up)
