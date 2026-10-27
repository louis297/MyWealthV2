---
title: Back Office
status: accepted
phase: 1
language: en
created: 2026-10-24
updated: 2026-10-27
related:
  - README.md
  - ../function-plan.md
  - ../architecture.md
  - ../glossary.md
  - ../features/identity-auth.md
  - ../features/tenants.md
  - ../features/tenant-admins.md
  - ../features/reports.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
---

# Back Office

Aspire resource `back-office`. Platform-ops UI for SystemAdmin.

Independent of the Adviser Portal. No BFF. The host is a confidential OpenIddict client; it holds the cookie session and access/refresh tokens server-side, then calls `webapi` with Bearer.

Accepted 2026-10-25. SystemAdmin still uses Scalar for the same verbs. The Back Office does not remove or replace the existing resource API surface. The host is landed.

---

## Current slice

**In**

- Aspire resource `back-office` (Blazor Web App + Interactive Server, MudBlazor)
- Confidential OIDC client registered on `identity` (ClientId `back-office`)
- SystemAdmin login via existing hosted login on `identity` (no `tenantCode`)
- Pages for Tenant manage and TenantAdmin manage (list / create / update / disable / enable)
- Tenant configuration: report grants for active catalog rows only (enable / disable a report for a tenant). A platform-inactive report is not shown
- Report catalog page `/reports`: platform IsActive (disable cascades revoke of every tenant grant for that report) and AllowedTriggers (removing `"scheduled"` disables existing schedules for that report). List is `GET /reports/catalog`
- Direct calls to existing `webapi` endpoints under policy `tenants.manage` (and `tenant-admins.manage`)

**Out**

- BFF (not required; the Blazor host holds the session)
- React / public SPA token stack
- Physical delete (model is disable)
- Adviser / Customer manage in this slice (SystemAdmin currently receives 403 on `/users/advisers` and `/users/customers`; do not reopen Phase 1 people routes here)
- Report catalog AllowedTriggers is in (platform-level schedule permission)
- Running or scheduling reports (those are firm verbs; SystemAdmin has no `reports.*.*` policies)
- Ledger pages (Instruments, Accounts, Transactions) — later slices
- Impersonation / “enter this tenant” header switching

---

## Host and auth

| Concern | Choice |
| --- | --- |
| Aspire resource | `back-office` |
| Project | `src/BackOffice` |
| UI stack | Blazor Web App + Interactive Server. Component library: MudBlazor. No functional change from the accepted page contract |
| OIDC client | Confidential, `ClientId = back-office`, secret via Aspire parameter (not in git) |
| Login | Redirect to hosted `/login` on `identity`. SystemAdmin omits `tenantCode` |
| Session | Cookie on the Back Office host. Access + refresh held server-side |
| Identity cookie | Isolated. IdentityHost uses a per-client cookie scheme for `back-office`. Login on Adviser Portal does not authenticate Back Office, and the reverse. See identity-auth amendment 2026-10-27. |
| Resource calls | Typed `HttpClient` to `webapi` with Bearer access token |
| CORS | None required on `webapi` (server-to-server) |

Redirect URI and post-logout follow the same pattern as the Adviser Portal BFF (`{backOfficeOrigin}/signin-oidc`, post-logout `{backOfficeOrigin}/`). Development launchSettings port is not locked.

---

## Routes

| Path | Who | Notes |
| --- | --- | --- |
| `/` | SystemAdmin | Home / navigation |
| `/tenants` | SystemAdmin | List + filter; links to create / detail |
| `/tenants/new` | SystemAdmin | Create (name, code, reportingCurrency) |
| `/tenants/{id}` | SystemAdmin | Detail + core settings (rename / reporting currency / disable / enable) |
| `/tenants/{id}/config` | SystemAdmin | Tenant configuration (grants for active catalog reports only) |
| `/reports` | SystemAdmin | Platform catalog. `GET /reports/catalog`. Set IsActive and AllowedTriggers |
| `/tenant-admins` | SystemAdmin | List (filter by tenant, search); links to create / detail |
| `/tenant-admins/new` | SystemAdmin | Create (tenant, name, email, password) |
| `/tenant-admins/{id}` | SystemAdmin | Detail + rename / disable / enable |
| `/forbidden` | any | Wrong role or missing policy |

All routes require an authenticated SystemAdmin. Other roles that reach the host are shown `/forbidden`.

---

## Pages (field bindings)

Consumes existing endpoints plus the catalog IsActive write. Field rules and validation live in the Feature Specs.

### Tenants

- List: `GET /tenants` (envelope, `isEnabled`, search). The live tenants query name is `isEnabled`, not `enabledOnly`.
- Create: `POST /tenants` — name, code, reportingCurrency
- Update: `PUT /tenants/{id}` — name and/or reportingCurrency (Code immutable)
- Disable / Enable: existing disable/enable verbs
- Display `isActive` / `status` as returned

### TenantAdmins

- List: `GET /users/tenant-admins` (filter by tenant PublicId, search, `enabledOnly`)
- Create: `POST /users/tenant-admins` — tenantId, name, email, password (lands Active)
- Update: rename only (email and tenant immutable)
- Disable / Enable: existing verbs (last-admin disable allowed)
- Display `status` and `isActive`

### Tenant configuration

Reached from tenant detail (`/tenants/{id}/config`). All verbs use `tenants.manage`.

**Report grants** (enable / disable an active catalog report for this tenant)

- List: `GET /tenants/{tenantId}/reports` — active catalog rows for that tenant. Each item includes `granted`. Platform-inactive reports are omitted
- The page also skips a row whose `isActive` is false
- Each row shows Code, Name, Scope, and `granted`. There is no Catalog column
- Enable when `granted` is false calls `POST /tenants/{tenantId}/reports` with that report id
- Disable when `granted` is true calls `DELETE /tenants/{tenantId}/reports/{reportId}`
- The page does not ask for a report id
- New tenants have no grants; the UI does not auto-grant
- Revoke also removes that tenant’s Hangfire recurring jobs for the report (existing behaviour); schedule and execution rows stay
- Do not offer “run” or “schedule” actions — SystemAdmin has no `reports.*.*` policies

Core tenant settings (name, reporting currency, tenant IsActive) stay on the detail page. Configuration is the place for per-tenant feature flags and grants.

### Report catalog

Platform page (`/reports`). All verbs use `tenants.manage`.

- List: `GET /reports/catalog`. Every catalog row (Code, Name, Scope, IsActive, AllowedTriggers). Inactive rows are included. No `granted` field.
- Set IsActive: `PUT /reports/{id}` with `{ "isActive": true|false }`.
- Transition to false revokes every `TenantReports` row for that report and removes the related Hangfire recurring jobs. `ReportSchedule` and `ReportExecution` rows stay.
- Transition to true restores no grants. Each tenant must be re-granted on its configuration page.
- Set AllowedTriggers: same `PUT /reports/{id}` may include `{ "allowedTriggers": ["manual","scheduled"] }` (or a subset). Known values only: `manual`, `scheduled`, `event`.
- Removing `"scheduled"` disables every existing `ReportSchedule` for that report (sets IsActive false and removes the Hangfire job). Rows stay. Grants are not revoked.
- Adding `"scheduled"` does not re-enable previously disabled schedules.
- Do not offer run or schedule actions.

---

## Acceptance (BO)

| ID | Behaviour |
| --- | --- |
| BO1 | Unauthenticated visit to any page redirects to hosted login on `identity` |
| BO2 | SystemAdmin completes login without `tenantCode` and lands on `/` |
| BO3 | Non-SystemAdmin who obtains a token for this client is shown `/forbidden` |
| BO4 | Tenant list, create, rename, change reporting currency, disable, and enable succeed against the existing `/tenants` endpoints |
| BO5 | TenantAdmin list (filtered by tenant), create, rename, disable, and enable succeed against the existing `/users/tenant-admins` endpoints |
| BO6 | Code and email remain immutable in the UI |
| BO7 | Disable is the only removal path; no physical delete action is offered |
| BO8 | From a tenant, SystemAdmin sees every active catalog report with Code, Name, Scope, and `granted` (Granted or Not granted). A platform-inactive report is not shown, including when the client payload still contains one. Enable calls `POST` when `granted` is false. Disable calls `DELETE` when `granted` is true. The page does not ask for a report id and does not run or schedule |
| BO9 | The configuration page does not offer run or schedule actions |
| BO10 | Sign-out revokes refresh and ends the session; next visit is hosted login. IdentityHost clears only the `back-office` scheme cookie (identity-auth A16). |
| BO11 | `/reports` lists `GET /reports/catalog`, including inactive rows. Setting IsActive to false calls `PUT /reports/{id}` and the backend revokes every tenant grant for that report (Hangfire cleaned; schedule and execution rows stay; schedule `IsActive` unchanged). Setting it back to true restores no grants. Setting AllowedTriggers to omit `"scheduled"` disables every schedule for that report (rows stay; no grant revoke). Adding `"scheduled"` does not re-enable those schedules |
| BO12 | After a successful Adviser Portal login in the same browser, an unauthenticated Back Office visit still shows hosted `/login`. No silent code for the Portal subject. |

---

## Tests

Back Office tests assert routes, role gate, and that the UI client sends the shapes expected by the existing Feature Specs. Do not re-prove backend isolation or dual-write here.

---

## Later slices

- Advisers (requires policy amendment: grant `advisers.manage` to SystemAdmin and accept `tenantId` on create)
- Instruments catalog management
- Cross-tenant ledger inspection (Accounts, Transactions, Net worth)
- Further tenant configuration (fees, invitation settings, etc.) as those slices land

These stay out until their own page slices are written.
