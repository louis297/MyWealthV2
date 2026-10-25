---
title: Back Office
status: accepted
phase: 1
language: en
created: 2026-10-24
updated: 2026-10-25
related:
  - README.md
  - ../function-plan.md
  - ../architecture.md
  - ../glossary.md
  - ../features/identity-auth.md
  - ../features/tenants.md
  - ../features/tenant-admins.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
---

# Back Office

Aspire resource `back-office`. Platform-ops UI for SystemAdmin.

Independent of the Adviser Portal. No BFF. The host is a confidential OpenIddict client; it holds the cookie session and access/refresh tokens server-side, then calls `webapi` with Bearer.

Accepted 2026-10-25. SystemAdmin still uses Scalar for the same verbs. The Back Office does not remove or replace the existing resource API surface. The host is landed.

---

## Current slice

**In**

- Aspire resource `back-office` (Blazor Server / Blazor Web App + Interactive Server)
- Confidential OIDC client registered on `identity` (ClientId `back-office`)
- SystemAdmin login via existing hosted login on `identity` (no `tenantCode`)
- Pages for Tenant manage and TenantAdmin manage (list / create / update / disable / enable)
- Tenant configuration: report grants (enable / disable a report for a tenant)
- Direct calls to existing `webapi` endpoints under policy `tenants.manage` (and `tenant-admins.manage`)

**Out**

- BFF (not required; the Blazor host holds the session)
- React / public SPA token stack
- Physical delete (model is disable)
- Adviser / Customer manage in this slice (SystemAdmin currently receives 403 on `/users/advisers` and `/users/customers`; do not reopen Phase 1 people routes here)
- Report catalog management (platform `Reports` table; IsActive on the catalog stays Scalar / later)
- Running or scheduling reports (those are firm verbs; SystemAdmin has no `reports.*.*` policies)
- Ledger pages (Instruments, Accounts, Transactions) — later slices
- Impersonation / “enter this tenant” header switching

---

## Host and auth

| Concern | Choice |
| --- | --- |
| Aspire resource | `back-office` |
| Project | `src/BackOffice` |
| UI stack | Blazor Server (or .NET Blazor Web App, Interactive Server) |
| OIDC client | Confidential, `ClientId = back-office`, secret via Aspire parameter (not in git) |
| Login | Redirect to hosted `/login` on `identity`. SystemAdmin omits `tenantCode` |
| Session | Cookie on the Back Office host. Access + refresh held server-side |
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
| `/tenants/{id}/config` | SystemAdmin | Tenant configuration (report grants; room for later settings) |
| `/tenant-admins` | SystemAdmin | List (filter by tenant, search); links to create / detail |
| `/tenant-admins/new` | SystemAdmin | Create (tenant, name, email, password) |
| `/tenant-admins/{id}` | SystemAdmin | Detail + rename / disable / enable |
| `/forbidden` | any | Wrong role or missing policy |

All routes require an authenticated SystemAdmin. Other roles that reach the host are shown `/forbidden`.

---

## Pages (field bindings)

Consumes existing endpoints only. Field rules and validation live in the Feature Specs.

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

**Report grants** (enable / disable a report for this tenant)

- List: `GET /tenants/{tenantId}/reports` — catalog rows with grant state (including inactive catalog rows)
- Enable (grant): `POST /tenants/{tenantId}/reports` with the report id
- Disable (revoke): `DELETE /tenants/{tenantId}/reports/{reportId}`
- New tenants have no grants; the UI does not auto-grant
- Revoke also removes that tenant’s Hangfire recurring jobs for the report (existing behaviour); schedule and execution rows stay
- Do not offer “run” or “schedule” actions — SystemAdmin has no `reports.*.*` policies

Core tenant settings (name, reporting currency, tenant IsActive) stay on the detail page. Configuration is the place for per-tenant feature flags and grants.

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
| BO8 | From a tenant, SystemAdmin can list report grants and grant or revoke a report; the UI calls the existing `/tenants/{tenantId}/reports` endpoints |
| BO9 | The configuration page does not offer run or schedule actions |
| BO10 | Sign-out revokes refresh and ends the session; next visit is hosted login |

---

## Tests

Back Office tests assert routes, role gate, and that the UI client sends the shapes expected by the existing Feature Specs. Do not re-prove backend isolation or dual-write here.

---

## Later slices

- Advisers (requires policy amendment: grant `advisers.manage` to SystemAdmin and accept `tenantId` on create)
- Instruments catalog management
- Cross-tenant ledger inspection (Accounts, Transactions, Net worth)
- Further tenant configuration (fees, invitation settings, etc.) as those slices land
- Report catalog IsActive (platform-level; not per-tenant)

These stay out until their own page slices are written.
