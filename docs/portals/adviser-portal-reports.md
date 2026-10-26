---
title: Adviser Portal — Reports
status: accepted
phase: 3
language: en
created: 2026-10-14
updated: 2026-10-26
related:
  - adviser-portal.md
  - adviser-portal-dashboard.md
  - frontend-conventions.md
  - ../function-plan.md
  - ../features/reports.md
  - ../features/bff-adviser-portal.md
  - ../adr/0017-background-worker.md
---

# Adviser Portal — Reports

Phase 3 page slice. Backend contract lives in [features/reports.md](../features/reports.md) (accepted, landed `a7145dc`). This file owns the Adviser Portal routes, page structure, field bindings, states, and acceptance for the two first-phase report entry points. It is not a Feature Spec.

Both entry points talk to `webapi` through the existing BFF. The BFF allow-list already includes the `/reports` prefix. Session remains the BFF cookie. All API calls go through `/api`. Do not re-introduce SPA token storage or `/callback`.

**Status is `accepted`.** Implementation follows this file and the Feature Spec. Construction detail may live in [frontend-implementation-notes.md](frontend-implementation-notes.md).

Backend prerequisites (landed):

- Report catalog, TenantReports grants, ReportSchedule, ReportExecution (`0019_reports.sql`).
- Shared pipeline + generators for Customer Transactions and Adviser Balances.
- Hangfire worker (ADR 0017). PDF via QuestPDF. Shared volume for result files.
- Policies: `reports.customer.read` / `reports.customer.create` and `reports.book.read` / `reports.book.create`. Customer has only the customer read policy. TenantAdmin and Adviser have all four. SystemAdmin has none of them.
- TestSeed grants both seeded reports to the demo tenant.
- Seeded PublicIds (stable):
  - Customer Transactions: `8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11` (code `customer-transactions`, scope `Customer`).
  - Adviser Balances: `5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22` (code `adviser-balances`, scope `Book`).

---

## Current slice

**In**

- Customer page (`/customers/:id`): a Reports section that lists only Customer-scoped reports granted to the tenant and active in the catalog. Implicit `customerId` = the page’s Customer.
- Shell: a Reports tab (`/reports`) that lists only Book-scoped reports granted to the tenant and active in the catalog.
- For each listed report: Generate now, create / edit / disable schedule, list executions (paged), download PDF when status is `Succeeded`.
- Poll execution status until terminal (`Succeeded` or `Failed`). No SignalR.
- Role difference on Book-scoped reports: Adviser omits `adviserId` (server defaults to self). TenantAdmin must pick a subordinate Adviser.
- Cron presets only (daily / weekly / monthly + time). No raw cron editor.
- Empty, loading, error, and success states for every list and form.

**Out**

- Catalog or tenant-grant management (Back Office tenant configuration and Scalar, `tenants.manage`).
- Customer Portal (Customer has `reports.customer.read` but no portal client).
- Raw cron string editor.
- Email delivery of the PDF.
- Historical as-of snapshots, FX fold, live vendor prices.
- Event-triggered executions in the UI (plumbing exists on the backend; no concrete events are bound).
- SystemAdmin reports screens.
- Parameter schema driven dynamic forms (first phase hard-codes the two known parameter shapes; the Generate dropdown still switches on the selected report’s code / PublicId).

---

## Routes

| Path | Who | Behaviour |
| --- | --- | --- |
| `/customers/:id` | TenantAdmin, Adviser | Existing customer detail. Adds a Reports section below the person + accounts. Customer-scoped reports only. |
| `/reports` | TenantAdmin, Adviser | New shell nav item. Book-scoped reports only. Generate, schedules, history. |
| `/` | TenantAdmin, Adviser | Unchanged (Dashboard). Reports is a peer nav item, not the default home. |

SystemAdmin still has no workspace and receives none of the report policies. Customer role has no portal client. Unassigned or other-tenant customer id on `/customers/:id` remains 404 (existing rule).

---

## Session

Unchanged from the BFF cut. Probe is still `GET /api/users/me`. 401 does not refresh in the browser. Sign-out remains `POST /bff/logout`. All report calls use the shared API client (`credentials: 'include'`, `X-MyWealth-Request: 1` on mutating calls).

---

## API calls used by the portal

All routes are under `/api`. Policy is enforced by the backend; the portal only hides create controls when the caller lacks the create policy (Adviser and TenantAdmin always have both create policies).

| Call | When | Notes |
| --- | --- | --- |
| `GET /reports?scope=Customer` | Customer page Reports section | Granted + active only. |
| `GET /reports?scope=Book` | Reports tab | Granted + active only. |
| `POST /reports/request` | Generate now | Body: `{ reportId, parameters }`. 202 `{ id }`. |
| `POST /reports/schedules` | Create schedule | Body: `{ reportId, parameters, cron, isActive }`. 201 `{ id }`. |
| `GET /reports/schedules?scope=…` | Schedules list | Array. Includes disabled. |
| `PUT /reports/schedules/{id}` | Edit schedule | Body includes `rowVersion`. 204. Stale → 409. |
| `POST /reports/schedules/{id}/disable` | Disable | No body. 204. |
| `GET /reports/executions?scope=…&reportId=&page=&pageSize=` | History | Paged `{ items, page, pageSize, totalCount }`. Optional `reportId` filters to one report. Sort created desc. |
| `GET /reports/executions/{id}` | Poll / detail | Single execution. |
| `GET /reports/executions/{id}/download` | Download | 200 `application/pdf` only when `Succeeded`. |

Error mapping the portal must surface:

- 400 — bad parameters (missing from/to, non-Adviser omitted adviserId, unknown fields, oversized parameters, bad cron, missing rowVersion).
- 403 — policy fail (should not occur for TenantAdmin / Adviser on these routes).
- 404 — unknown report, not granted, trigger not allowed, out-of-scope Customer or Adviser.
- 409 — stale rowVersion on schedule update, or download of a non-Succeeded execution.

Do not treat a cross-tenant or out-of-scope 404 as 403.

---

## Page structure

The Customer page shows a Generate section, a Schedules panel, and a History panel in one section. `/reports` shows those same three panels on three tabs: Reports, Schedules, and History. Reports is selected by default, and only the selected panel is shown. Scope and implicit parameters differ by entry point: `customerId` on the Customer page, and the adviserId rules on `/reports`.

### Customer page — Reports section

Appears on `/customers/:id` after the existing person fields and accounts list. Visible only to TenantAdmin and Adviser.

**Header.** “Reports” with a short note that these run for this customer.

**Generate section.**

- Report dropdown: items from `GET /reports?scope=Customer`. Shows name (and function as secondary text if space allows). Empty: “No reports available for this customer.”
- Selecting a report reveals its parameter form and a Generate button. The form is hard-coded per known report (switch on code / PublicId). Unknown reports in the list show a short “parameters not available in this version” note and disable Generate.
- customerId is always hidden and set to the page’s Customer PublicId.

**Generate form (Customer Transactions).**

| Field | Binding | Required | Notes |
| --- | --- | --- | --- |
| From | date input → `parameters.from` | yes | ISO date `YYYY-MM-DD` |
| To | date input → `parameters.to` | yes | ISO date. Client may reject `to < from` before submit |
| Include closed accounts | checkbox → `parameters.includeClosedAccounts` | no | Default false |
| customerId | hidden | yes | Always the page’s Customer PublicId |

Submit calls `POST /reports/request` with:

```json
{
  "reportId": "<selected report PublicId>",
  "parameters": {
    "customerId": "<page customer PublicId>",
    "from": "2026-01-01",
    "to": "2026-03-31",
    "includeClosedAccounts": false
  }
}
```

On 202: show a toast “Report queued”, clear or leave the form, and refresh the History panel (the new row appears with status Queued). On 400: show the server message next to the form. On 404: “Report is not available.”

**Schedules panel.**

- Lists `GET /reports/schedules?scope=Customer`. Optional client-side filter by report (dropdown of the same Customer-scoped reports, plus All). Backend already scopes by caller; further filter to the page customer’s `parameters.customerId` is allowed.
- Each item shows report name (resolved from reportId), cron (humanised from the preset if possible), isActive, and actions Edit / Disable.
- Create schedule is available only when the selected report’s `allowedTriggers` includes `"scheduled"`. The form uses the same parameter fields as Generate now, plus frequency / time / active (see Cron presets).
- Edit sends `PUT` with the same shape plus `rowVersion` from the list item. Disable calls the disable endpoint. No hard delete.
- If the selected report does not allow scheduled, the create form is hidden and an existing schedule for that report is shown as read-only (or with Disable only).

**History panel.** Separate panel.

- Default: `GET /reports/executions?scope=Customer&page=1&pageSize=20` (all Customer-scoped executions the caller can see).
- Report filter dropdown (All + the Customer-scoped reports). Selecting one report calls the same endpoint with `&reportId=<PublicId>`. Pagination uses the returned `totalCount` after the filter.
- Columns: report name, status, triggerKind, created, parameters summary (date range), asOf if present. Download link only when status is `Succeeded`. Failed rows show status only (no error text in the list payload).

### Reports page (`/reports`)

Shell nav item labelled “Reports”. Visible to TenantAdmin and Adviser. SystemAdmin does not see it.

**Header.** “Reports” with a short note that these run for an adviser’s book. The header stays above the tabs.

**Tabs.** Reports, Schedules, and History. Reports is selected when the page opens. Selecting a tab shows that panel.

**Reports tab (Generate).**

- Report dropdown: items from `GET /reports?scope=Book`. Empty: “No reports available.”
- Selecting a report reveals its parameter form and a Generate button (hard-coded per known report). The chosen report stays selected when the user opens Schedules.

**Generate form (Adviser Balances).**

| Field | Binding | Required | Notes |
| --- | --- | --- | --- |
| Adviser | select → `parameters.adviserId` | TenantAdmin: yes. Adviser: hidden | Adviser omits the field. TenantAdmin must choose one of the tenant’s Advisers (reuse existing advisers list). |
| Include closed accounts | checkbox → `parameters.includeClosedAccounts` | no | Default false |
| asOf | — | never | Not a field. Server sets it. |

Adviser submit body:

```json
{
  "reportId": "<selected report PublicId>",
  "parameters": {
    "includeClosedAccounts": false
  }
}
```

TenantAdmin submit body includes `"adviserId": "<selected PublicId>"` inside `parameters`. Omitting it as TenantAdmin is a 400; the form must prevent submit until an Adviser is selected.

**Schedules tab.** The Schedules panel from the Customer section, scoped to Book. Schedule create requires the Adviser picker for TenantAdmin and is shown only when the report selected on the Reports tab has `allowedTriggers` that include `"scheduled"`.

**History tab.** The History panel from the Customer section, scoped to Book. The list is `GET /reports/executions?scope=Book` (optional `reportId`). An Adviser sees only executions whose snapshot `adviserId` is self. A TenantAdmin sees the tenant. A run queued from the Reports tab appears in this list.

---

## Cron presets

The portal never shows a free-text cron field. Map presets to a 5-field expression (minute hour day-of-month month day-of-week). Time is local to the browser for display; the first cut sends the chosen hour/minute as-is (backend treats cron as UTC for the first phase — document this limitation in the form help text).

| Preset | Example (09:00) | Cron |
| --- | --- | --- |
| Daily | every day at 09:00 | `0 9 * * *` |
| Weekly | every Monday at 09:00 | `0 9 * * 1` |
| Monthly | 1st of the month at 09:00 | `0 9 1 * *` |

Weekly day-of-week is fixed to Monday in this phase (no day picker). Monthly is fixed to the 1st. Help text: “Times are sent as chosen. The server interprets the schedule in UTC.”

---

## Polling

After a successful Generate now (202 with an execution id):

1. Immediately insert or refresh the history list so the new row appears with status `Queued`.
2. Poll `GET /reports/executions/{id}` every 2 seconds while status is `Queued` or `Running`.
3. Stop on `Succeeded` (enable Download) or `Failed` (show Failed, no download).
4. Cap at 60 seconds (30 polls). After the cap, stop and leave the row as-is; the user can refresh the history list.
5. Do not poll in the background when the section is unmounted. Cancel the timer on unmount.

No SignalR. No optimistic “Succeeded”.

---

## States

| Surface | Loading | Empty | Error | Success |
| --- | --- | --- | --- | --- |
| Report list | Skeleton or “Loading reports…” | “No reports available.” | Inline message + Retry | Cards / rows |
| Generate form | Disable submit while in flight | — | Field or form-level message from 400 / 404 | Toast “Report queued” + history refresh |
| Schedules list | “Loading schedules…” | “No schedules.” | Inline + Retry | Rows with Edit / Disable |
| History list | “Loading history…” | “No runs yet.” | Inline + Retry | Rows; Download only on Succeeded |
| Download | Button shows spinner | — | 409 or network → “Download not available.” | Browser receives PDF |

Use the existing portal empty / error patterns. Do not invent a second toast library.

---

## Role matrix

| Action | Adviser | TenantAdmin | SystemAdmin | Customer |
| --- | --- | --- | --- | --- |
| See Customer page Reports section | yes (assigned customers) | yes | no | no portal |
| See Reports tab | yes | yes | no | no portal |
| Generate / schedule Customer-scoped | yes (assigned) | yes | no | no |
| Generate / schedule Book-scoped | yes (self only; no adviser picker) | yes (must pick Adviser) | no | no |
| Download Succeeded PDF | yes (in scope) | yes | no | no |
| Manage catalog / grants | no | no | Back Office and Scalar | no |

---

## Acceptance (R)

| ID | Behaviour |
| --- | --- |
| R1 | `/customers/:id` shows a Reports section whose Generate dropdown lists only Customer-scoped granted active reports. Book-scoped reports do not appear. |
| R2 | `/reports` shows a Generate dropdown of only Book-scoped granted active reports. Customer-scoped reports do not appear. Empty list when the tenant has none. |
| R3 | Generate now on a selected Customer Transactions report sends `POST /reports/request` with `reportId` + `parameters.customerId` (page customer) + `from` + `to` + optional `includeClosedAccounts`. 202 queues the run and the History panel refreshes. |
| R4 | Generate now on a selected Adviser Balances report: Adviser omits `adviserId`. TenantAdmin must select an Adviser or the form blocks submit. `asOf` is never sent. |
| R5 | Schedule create / edit / disable use the schedule endpoints. Create is offered only when the selected report’s `allowedTriggers` includes `"scheduled"`. Edit sends `rowVersion`. Stale rowVersion shows the 409. Disable leaves the row visible with `isActive` false. |
| R6 | History is its own panel on the customer page and the History tab on `/reports`. It is paged and defaults to all reports in the scope. A report filter dropdown adds `reportId` to the executions query. Download appears only for `Succeeded`. |
| R7 | After Generate now the UI polls the execution until Succeeded / Failed or 60 s. Polling stops on unmount. |
| R8 | TenantAdmin sees the Adviser picker on the Reports tab. Adviser does not. |
| R9 | All calls go through the BFF `/api` prefix. No SPA token storage. SystemAdmin does not see the Reports tab or the Customer page Reports section. |
| R10 | 400 (bad parameters) and 404 (not granted / out of scope) are shown as form or inline messages. Cross-tenant or unassigned customer remains 404 on the page itself. |
| R11 | Cron is chosen from presets only. No free-text cron field is rendered. |
| R12 | Forms omit `tenantId`. Parameters contain only the known fields for that report. |

---

## Tests

Portal tests assert routes, role visibility, that the Customer section calls `scope=Customer` and the Reports page calls `scope=Book`, that the Generate dropdown switches the hard-coded parameter form, that Generate and Schedule send the documented body shapes, that Schedule create is hidden when `allowedTriggers` omits `"scheduled"`, that History defaults to all reports and the report filter adds `reportId`, that the Adviser picker is present only for TenantAdmin, and that Download is offered only for `Succeeded`. On `/reports`, tests also assert Reports, Schedules, and History are tabs and that only the selected panel is shown. The customer Reports section keeps the three panels in one section. Do not re-prove backend isolation, PDF content, or Hangfire behaviour here.

---

## Still open

- Exact visual layout (cards vs table, modal vs inline form).
- Whether weekly allows a day-of-week picker (locked to Monday for now).
- Timezone display vs UTC cron (help text documents the first-cut limitation).
- Result retention / purge (no UI for it).
- Event-triggered executions (no UI).
- Parameter schema for dynamic forms (hard-coded switch on the selected report for the two known shapes).
