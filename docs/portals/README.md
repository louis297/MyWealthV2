---
title: Portals
status: review
language: en
created: 2026-09-12
updated: 2026-10-26
related:
  - ../README.md
  - ../function-plan.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
---

# Portals

UI and OIDC-client contracts. Backend handlers stay in [features/](../features/README.md).

Phase 1 has one frontend: [adviser-portal.md](adviser-portal.md). Customer Portal is not registered. Back Office lives in [../backoffice/](../backoffice/) and is independent of this folder.

| Doc | Owns |
| --- | --- |
| [adviser-portal.md](adviser-portal.md) | Aspire SPA resource, routes, page slices. OIDC client is held by the BFF |
| [adviser-portal-bff-cut.md](adviser-portal-bff-cut.md) | Landed SPA cut record (`6aae5c7`). Not a second feature spec |
| [adviser-portal-dashboard.md](adviser-portal-dashboard.md) | Phase 2 Dashboard, ledger pages, and write forms (Buy / Sell / Split / Reverse) (accepted). Consumes net-worth and existing ledger endpoints |
| [adviser-portal-reports.md](adviser-portal-reports.md) | Phase 3 Reports UI (accepted). Detailed page contract for Customer page section + Reports tab (routes, field bindings, states, polling, acceptance). |
| [../features/bff-adviser-portal.md](../features/bff-adviser-portal.md) | Session edge (accepted, landed `5e85e9c`). Not a page spec |
| [frontend-conventions.md](frontend-conventions.md) | Folder layout, naming, session state, API client |
| [frontend-implementation-notes.md](frontend-implementation-notes.md) | Page-by-page construction for the current pages cut |

[adviser-portal.md](adviser-portal.md) is **accepted** (shell + pages cut C). Dashboard cut is **accepted**. Conventions and construction notes stay `review`. Customer Portal is not registered. Back Office is independent and accepted: [../backoffice/](../backoffice/).

---

## Change summary (2026-10-26)

Reports page and catalog configuration updates to support multiple reports and platform-level control.

**Adviser Portal** ([adviser-portal-reports.md](adviser-portal-reports.md))

- Customer page Reports section and `/reports` tab now use a shared layout: Generate section, Schedules panel, History panel.
- Generate: report dropdown (`GET /reports?scope=…`). Selecting a report shows its hard-coded parameter form and Generate button.
- History: separate panel. Defaults to paged list of all reports in the scope. Optional report filter adds `reportId` to `GET /reports/executions`.
- Schedule create is shown only when the selected report’s `allowedTriggers` includes `"scheduled"`.

**Backend / Back Office** (see [../features/reports.md](../features/reports.md) and [../backoffice/back-office.md](../backoffice/back-office.md))

- `PUT /reports/{id}` (`tenants.manage`) writes catalog `isActive` and/or `allowedTriggers`.
- `isActive` false cascades revoke of every `TenantReports` row for that report (Hangfire cleaned; schedule and execution rows stay). Re-enable restores no grants.
- Removing `"scheduled"` from `allowedTriggers` disables every existing schedule for that report (rows stay; no grant revoke). Adding it does not re-enable those schedules.
- Back Office `/reports` page configures both switches. Tenant grants remain on `/tenants/{id}/config`.
- `GET /reports/executions` accepts optional `reportId` (filter then `totalCount`).
