---
title: Back Office
status: accepted
phase: 1
language: en
created: 2026-10-24
updated: 2026-11-23
related:
  - README.md
  - ../function-plan.md
  - ../architecture.md
  - ../glossary.md
  - ../features/identity-auth.md
  - ../features/tenants.md
  - ../features/tenant-admins.md
  - ../features/reports.md
  - ../features/instruments.md
  - back-office-instruments.md
  - ../features/tenant-custody-modes.md
  - ../features/client-money-accounts.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
  - ../features/deposit-matching.md
  - ../features/interest-run.md
  - ../features/trade-settlement.md
  - ../features/in-kind-transfers.md
---

# Back Office

Aspire resource `back-office`. Platform-ops UI for SystemAdmin.

Independent of the Adviser Portal. No BFF. The host is a confidential OpenIddict client; it keeps the access and refresh tokens in its encrypted HttpOnly session cookie (`SaveTokens`; only the host can decrypt it; JavaScript cannot read it; no server-side session store), then calls `webapi` with Bearer.

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
- Instruments catalog management for a chosen tenant: list / create / rename / disable / enable. Contract: [back-office-instruments.md](back-office-instruments.md)
- Direct calls to existing `webapi` endpoints under policy `tenants.manage` (and `tenant-admins.manage`) and the instrument policies (`instruments.read` / `instruments.create` / `instruments.manage`)

**Out**

- BFF (not required; the Blazor host holds the session)
- React / public SPA token stack
- Physical delete (model is disable)
- Adviser / Customer manage in this slice (SystemAdmin currently receives 403 on `/users/advisers` and `/users/customers`; do not reopen Phase 1 people routes here)
- Report catalog AllowedTriggers is in (platform-level schedule permission)
- Running or scheduling reports (those are firm verbs; SystemAdmin has no `reports.*.*` policies)
- Cross-tenant ledger pages (Accounts, Transactions, Holdings, Net worth) — later slice
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
| Session | Encrypted HttpOnly cookie on the Back Office host. Access + refresh live in the cookie; no server-side session store |
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
| `/instruments` | SystemAdmin | Tenant instrument catalog. Requires tenant selection. See [back-office-instruments.md](back-office-instruments.md) |
| `/instruments/new` | SystemAdmin | Create instrument for the chosen tenant |
| `/instruments/{id}` | SystemAdmin | Detail + rename / disable / enable |
| `/market-data` | SystemAdmin | Market data connections and the state of each price feed. `GET /market-data-connections`. See the amendment of 2026-11-22 (simulated instruments); the corporate action feed and the newest announcements in the amendment of 2026-11-22 (corporate actions) |
| `/broker` | SystemAdmin | Broker connections, the state of each execution report feed and the newest execution reports. `GET /broker-connections`, `GET /execution-reports`. See the amendment of 2026-11-23 (orders and settlement) |
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
- Tenant fields show the tenant Name, never the PublicId. The list filter is a tenant picker over every tenant (`GET /tenants`, no `isEnabled`), with an "All tenants" option. The create form is a tenant picker over enabled tenants only (`GET /tenants?isEnabled=true`). The detail page loads `GET /tenants/{tenantId}` and shows the Name as a link to the tenant detail. The PublicId is still what the page sends on the wire

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

### Instruments

Tenant instrument catalog for a chosen tenant. Full field bindings and acceptance: [back-office-instruments.md](back-office-instruments.md).

- List: `GET /instruments?tenantId=` (required). Search, `enabledOnly`, paging.
- Create: `POST /instruments` with `tenantId`, `symbol`, `name`, `quoteCurrency`.
- Detail: `GET /instruments/{id}`. Rename via `PUT` (symbol and/or name + `rowVersion`). Disable / enable via `POST …/disable` and `POST …/enable` with `rowVersion`.
- QuoteCurrency immutable. No price. No physical delete. No ledger navigation from this page.

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
| BO13 | Instruments list requires a tenant selection and calls `GET /instruments?tenantId=`. Create, rename, disable, and enable send the shapes in [back-office-instruments.md](back-office-instruments.md). QuoteCurrency is not editable. No physical delete. |

---

## Tests

Back Office tests assert routes, role gate, and that the UI client sends the shapes expected by the existing Feature Specs. Do not re-prove backend isolation or dual-write here.

---

## Amendment 2026-11-13 (refresh exchange moves to Shared) — accepted

Status of this amendment: `accepted` (2026-11-13). It is a build contract. Landed 2026-11-13 (`RotatedRefreshToken_PresentedThirtyMinutesLater_CallsTheTokenEndpoint`). The rest of this file stays accepted. The owner is [bff-adviser-portal.md](../features/bff-adviser-portal.md), amendment 2026-11-13 (refresh single-flight).

- The exchange part of `BackOfficeTokenRefresh` (concurrent grants joined, rotated refresh tokens remembered, chain follow) moves to `src/Shared` `RefreshTokenExchange`, so both BFFs use the same code. Back Office keeps `ValidatePrincipalAsync` and `CookieAccessTokens`. Behaviour is unchanged, and `BackOfficeRefreshTests` stay green.
- New: a remembered rotation is dropped 30 minutes after it is written (RF6). Today the map never drops an entry. One added test: a rotated refresh token presented 30 minutes after the rotation calls the token endpoint.

---

## Amendment 2026-11-18 (allowed custody modes) — accepted, landed

Status of this amendment: `accepted` (2026-11-18), together with [tenant-custody-modes.md](../features/tenant-custody-modes.md). No new route.

- `/tenants/new`: a required "Custody modes" group with two checkboxes, "External accounts" and "Custodial accounts", both checked when the form opens. `POST /tenants` carries the checked values as `allowedCustodyModes`.
- `/tenants/{id}`: shows the allowed custody modes next to the reporting currency. The edit form has the same two checkboxes, set from the item, and every `PUT /tenants/{id}` carries `allowedCustodyModes` with name, reporting currency and `rowVersion`. Under the group: "Changing this affects only new accounts."
- The setting is on the detail page, not on `/tenants/{id}/config`, because `PUT /tenants/{id}` writes it together with the name and `rowVersion` the detail page already holds.
- `/tenants` list: no new column.

| ID | Behaviour |
| --- | --- |
| BO14 | New tenant: both custody mode boxes are checked on open. Create is disabled while neither is checked. The POST carries exactly the checked values. A 400 on `errors.AllowedCustodyModes` is shown on the form |
| BO15 | Tenant detail: the boxes reflect the item's `allowedCustodyModes`. Save sends `allowedCustodyModes` with name, reporting currency and `rowVersion`, and is disabled while neither is checked. The page shows "Changing this affects only new accounts." |

---

## Amendment 2026-11-20 (client money accounts) — accepted

Status of this amendment: `accepted` (2026-11-20), together with [client-money-accounts.md](../features/client-money-accounts.md). Field rules live there.

- `/tenants/{id}/config` gains a "Client money accounts" section under the report grants. Calls: `GET /tenants/{tenantId}/client-money-accounts`, `POST /tenants/{tenantId}/client-money-accounts`, `POST …/{id}/disable` and `POST …/{id}/enable` with `rowVersion`, and `GET /currencies?enabledOnly=true` for the currency picker. All use `tenants.manage`.
- No edit action: a client money account has no PUT.

| ID | Behaviour |
| --- | --- |
| BO16 | `/tenants/{id}/config` gains a "Client money accounts" section under the report grants: a table from `GET /tenants/{tenantId}/client-money-accounts` with Currency, Bank, Account name, Account number and Active, and Disable / Enable per row (sending its `rowVersion`). An "Add client money account" form: currency picker over enabled currencies (`GET /currencies?enabledOnly=true`), bank name, account name, account number. A 400 is shown at its field. After a write the table reloads. |
| BO17 | No edit action. Under the table: "Details cannot be changed. To correct them, disable the account and add a new one. Disabling the last account in a currency stops new Custodial accounts in that currency." |

---

## Amendment 2026-11-20 (bank connection and feed status) — accepted

Status of this amendment: `accepted` (2026-11-20), together with [deposit-matching.md](../features/deposit-matching.md). Field rules live there.

- The client money accounts section (BO16) shows each row's bank connection and the state of its statement feed, and the add form names the connection. New call: `GET /bank-connections` (`tenants.manage`). The create call sends `bankConnection`.

| ID | Behaviour |
| --- | --- |
| BO18 | The client money accounts table (BO16) gains a "Connection" column (`bankConnection`) and a "Statement" column from `bankFeed`: `null` → "Not read yet"; `lastFailedAt` later than `lastSucceededAt` (or no success) → "Failing since <lastFailedAt>: <failure label>"; otherwise "Read <lastSucceededAt>". Failure labels: Unreachable "Bank not reachable", Timeout "Bank did not answer in time", Unauthorized "Bank refused the key", Unavailable "Bank unavailable", UnknownAccount "Bank does not know this account", BadResponse "Bank answer not understood", InvalidLine "Statement line rejected". |
| BO19 | The add form gains a required "Bank connection" select from `GET /bank-connections`. With one connection it is preselected. With none the form shows "No bank connection is configured." and cannot be submitted. A 400 `errors.BankConnection` is shown at the select. |

---

## Amendment 2026-11-22 (interest runs) — accepted

Status of this amendment: `accepted` (2026-11-22), together with [interest-run.md](../features/interest-run.md). Field rules live there. The tenant configuration page gains the Tenant's interest runs and the manual trigger of roadmap D18. Calls: `GET /interest/runs?tenantId=` (`interest-runs.read`) and `POST /interest/runs` (`interest-runs.start`). No interest rate page: a SystemAdmin has no interest rate permission.

| ID | Behaviour |
| --- | --- |
| BO20 | `/tenants/{id}/config` gains an "Interest runs" section: the newest ten runs from `GET /interest/runs?tenantId=` with As of, Trigger, Accounts, Failed, Posted and the totals per currency. |
| BO21 | A "Run for a date" form in that section: an as-of date (default yesterday, UTC) and Run, which posts `POST /interest/runs` and reloads the table. A 400 `errors.AsOfDate` is shown at the date. Under the form: "Runs every day by itself. Use this after a missed run. A run for a day already done changes nothing." |

---

## Amendment 2026-11-22 (simulated instruments) — accepted

Status of this amendment: `accepted` (2026-11-22), together with [simulated-instruments.md](../features/simulated-instruments.md). Field rules live there. The Instruments pages show which instruments a market data connection prices, and a new page lists the connections. New call: `GET /market-data-connections` (`tenants.manage`). The instrument item carries `marketDataConnection`.

| ID | Behaviour |
| --- | --- |
| BO22 | The Instruments list and detail pages ([back-office-instruments.md](back-office-instruments.md)) show "Prices from <connection>" under the symbol of an instrument whose `marketDataConnection` is set. The create form notes: "A symbol that ends with a market data suffix (for example .SIM) is priced by that connection." |
| BO23 | A new page "Market data" (`/market-data`, in the navigation) lists `GET /market-data-connections`: Name, Adapter, Suffix and Prices: `null` → "Not read yet"; `lastFailedAt` later than `lastSucceededAt` (or no success) → "Failing since <lastFailedAt>: <failure label>"; otherwise "Read <lastSucceededAt>". Times as BO18. Failure labels: Unreachable "Market data source not reachable", Timeout "Market data source did not answer in time", Unauthorized "Market data source refused the key", Unavailable "Market data source unavailable", BadResponse "Market data answer not understood". With no connection: "No market data connection is configured. Every instrument is priced by EODHD." |

---

## Amendment 2026-11-22 (corporate actions) — accepted

Status of this amendment: `accepted` (2026-11-22), together with [corporate-actions.md](../features/corporate-actions.md). Field rules live there. The Market data page shows the state of each connection's corporate action feed and the newest announcements. New call: `GET /corporate-action-announcements` (`tenants.manage`). The connection item carries `corporateActions`.

| ID | Behaviour |
| --- | --- |
| BO24 | The Market data page (BO23) gains a "Corporate actions" column from `corporateActions`, with the same three states as the Prices column; InvalidItem is labelled "The party sent a record that cannot be read". |
| BO25 | Under the connections, the newest twenty announcements from `GET /corporate-action-announcements`: Received, Connection, Symbol, Kind ("Unknown" for `null`), Ratio, Ex-date, Party status, Version, Status and Reason (UnsupportedKind "Kind not supported", Invalid "Not valid", NoInstrument "No firm lists this instrument", AlreadyEffective "Already applied", Superseded "An older version"). A status filter (default All). A row skipped as `AlreadyEffective` is shown in red: it needs a person. |

---

## Amendment 2026-11-23 (orders and settlement) — accepted

Status of this amendment: `accepted` (2026-11-23), together with [orders.md](../features/orders.md). Field rules live there. The Tenant forms set the firm's broker connection, and a new Broker page shows each connection's execution report feed and the newest reports. New calls: `GET /broker-connections` and `GET /execution-reports` (`tenants.manage`). The tenant item carries `brokerConnection`.

| ID | Behaviour |
| --- | --- |
| BO26 | The Tenant create and edit forms gain "Broker connection": a select of `GET /broker-connections` names, with "None" only on create. The Tenant page shows it. An unknown name's 400 is shown at the field. |
| BO27 | A "Broker" page lists `GET /broker-connections`: Name, Adapter and "Reports" with the three states of the Prices column of BO23 (never read, last success, failing since with the failure label; InvalidItem "The party sent a record that cannot be read"). |
| BO28 | Under the connections, the newest twenty reports from `GET /execution-reports`: Received, Connection, Kind ("Unknown" for `null`), Client order id, Quantity, Price, Status and Reason (UnsupportedKind "Kind not supported", UnknownOrder "No such order", UnknownFill "No such fill", Overfill "More than the order", Repeated "Already known"). A status filter (default All). A row skipped as `UnknownOrder`, `UnknownFill` or `Overfill` is shown in red: it needs a person. |

---

## Later slices

- Advisers (requires policy amendment: grant `advisers.manage` to SystemAdmin and accept `tenantId` on create)
- Cross-tenant ledger inspection (Accounts, Transactions, Holdings, Net worth)
- Portfolios catalog management (same SystemAdmin + `tenantId` pattern as instruments; page contract not yet written)
- Further tenant configuration (invitation settings, etc.) as those slices land. Trade fee schedules: no Back Office page for now (decided 2026-11-15); SystemAdmin uses `/fees/trade-schedules` through Scalar ([features/trade-fees.md](../features/trade-fees.md) §9)

These stay out until their own page slices are written.

---

## Amendment 2026-11-23 (trade settlement and broken trades) — accepted

Status of this amendment: `accepted` (2026-11-23), together with [trade-settlement](../features/trade-settlement.md). Field rules live there.

| ID | Behaviour |
| --- | --- |
| BO29 | Back Office Broker page (BO27, BO28): the connection row shows "Settles in USD, NZD" from `settlementCurrencies`; the report list shows kinds `Broken` and `Settlement` with the settlement id, and the reason labels FillSettled "The trade has already settled", FillBroken "The trade was cancelled", UnknownSettlement "No trades for this settlement". `FillSettled` and `UnknownSettlement` rows are red. |

---

## Amendment 2026-11-24 (in-kind transfers) — accepted

Status of this amendment: `accepted` (2026-11-24), together with [in-kind-transfers](../features/in-kind-transfers.md). Field rules live there (R2–R7). Calls: `/tenants/{tenantId}/safekeeping-accounts` and `GET /custodian-connections`, both `tenants.manage`.

| ID | Behaviour |
| --- | --- |
| BO30 | The Tenant page gains a "Safekeeping accounts" section from `GET /tenants/{tenantId}/safekeeping-accounts`: Currency, Custodian, Account name, Account number, Connection, Active, and the feed with the three states of the Prices column of BO23 (never read, last success, failing since with the failure label) and "Positions as at". "Add" opens a form with the R3 fields and a select of `GET /custodian-connections` names. Disable and Enable act with `rowVersion`. A 400 (a second active account in the currency, a number in use) is shown at its field. |
| BO31 | A "Custodian" page lists `GET /custodian-connections`: Name and Adapter. |
| BO32 | The failure labels add UnknownAccount "The custodian does not know this account". |
