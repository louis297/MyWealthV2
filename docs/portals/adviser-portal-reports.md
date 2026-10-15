---
title: Adviser Portal — Reports
status: accepted
phase: 3
language: en
created: 2026-10-14
updated: 2026-10-14
related:
  - adviser-portal.md
  - frontend-conventions.md
  - ../function-plan.md
  - ../features/reports.md
  - ../adr/0017-background-worker.md
---

# Adviser Portal — Reports

Phase 3 page slice. Backend contract: [features/reports.md](../features/reports.md) (accepted). This file is not a Feature Spec.

Two entry points. Both talk to `webapi` through the existing BFF (`/reports` on the allow-list).

---

## In

- Customer page: a Reports section for **Customer-scoped** reports granted to the tenant and active in the catalog.
- Shell: a Reports tab for **Book-scoped** reports granted to the tenant and active in the catalog.
- Generate now, create / edit / disable schedule, list executions, download PDF when Succeeded.
- Poll execution status. No SignalR.

## Out

- Catalog or tenant-grant management (Scalar).
- Customer Portal.
- Raw cron editor (use presets: daily / weekly / monthly + time).
- Email delivery of the PDF.

---

## Customer page section

- Lists Customer-scoped reports only.
- `customerId` is implicit (the page’s Customer).
- Parameters for Customer Transactions: `from`, `to` (required), `includeClosedAccounts` (default false).
- Adviser may only use an assigned Customer. TenantAdmin may use any Customer in the tenant.

## Reports tab

- Lists Book-scoped reports only.
- Adviser: `adviserId` omitted (server defaults to self).
- TenantAdmin: Adviser picker required.
- Parameters for Adviser Balances: `includeClosedAccounts` (default false). `asOf` is not a field.

---

## Routes (suggested)

| Path | Who | Notes |
| --- | --- | --- |
| `/customers/{id}` | Adviser, TenantAdmin | Existing page; Reports section added |
| `/reports` | Adviser, TenantAdmin | Book-scoped list, generate, schedules, history |

Customer role has no portal client. `reports.customer.read` is API-only until a later client.

---

## Acceptance

| ID | Behaviour |
| --- | --- |
| P1 | Customer page shows only Customer-scoped granted reports. |
| P2 | Reports tab shows only Book-scoped granted reports. |
| P3 | Generate and schedule call `POST /reports/request` and `POST /reports/schedules` with `reportId` + `parameters`. |
| P4 | Succeeded execution offers a PDF download. |
| P5 | TenantAdmin must pick an Adviser on the Reports tab. Adviser does not see that picker. |
