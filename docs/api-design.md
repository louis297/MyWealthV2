---
title: API design
status: accepted
language: en
created: 2026-09-11
updated: 2026-11-27
related:
  - README.md
  - glossary.md
  - function-plan.md
  - architecture.md
  - domain-model.md
  - database-design.md
  - adr/0014-openiddict-authorization-code-pkce.md
  - features/dividends.md
  - features/adjustment-review.md
---

# API design

This document owns **HTTP conventions and the resource catalog**. Field-level contracts live in the Feature Spec for the slice. Scope and phasing live in [function-plan.md](function-plan.md). Session protocol detail lives in [ADR 0014](adr/0014-openiddict-authorization-code-pkce.md); this file only lists what each HTTP surface exposes.

**Product:** MyWealthV2.

Phase 1 closes the platform base: Adviser Portal + Scalar + the authorization-server hosted login. Phase 2 accepted and landed: `/instruments`, `/accounts`, `/transactions` (posting 2026-09-25, `79129eb`).

---

## 1. Two surfaces

Two processes, two HTTP surfaces. Do not add a custom `POST /auth/login` on `webapi` that issues tokens.

```text
Browser
  ├── authorize redirect / hosted login                  →  identity (src/IdentityHost)
  └── same-origin calls with the session cookie          →  portal BFF
        ├── token / refresh / revocation / logout        →  identity
        └── Bearer resource calls                        →  webapi (src/Web)
```

| Host | Aspire name | Role |
| --- | --- | --- |
| `src/IdentityHost` | `identity` | Authorization server: OpenIddict default paths + hosted login |
| `src/Web` | `webapi` | Resource API: Minimal API → MediatR; Scalar; Bearer validation only |

A first-party portal does not issue tokens, and its browser does not hold them. The portal's BFF is the OIDC client: it runs the authorization code flow against `identity`, keeps the access and refresh tokens server-side and calls `webapi` with Bearer ([ADR 0016](adr/0016-bff-for-first-party-portals.md), [features/bff-adviser-portal.md](features/bff-adviser-portal.md), [features/bff-customer-portal.md](features/bff-customer-portal.md)).

A third process, the simulator (`simulator`, [features/simulator.md](features/simulator.md) §8, landed 2026-11-19), has its own HTTP surfaces: a control surface for a person behind sign-in on client `simulator`, and a party surface for the platform behind an API key on an internal port. Neither is a `webapi` resource, and the conventions in this document do not apply to them.

OIDC clients and the roles each allows: `adviser-portal` (TenantAdmin, Adviser), `customer-portal` (Customer), `back-office` (SystemAdmin) and `simulator` (SimulatorOperator). The two portal clients are confidential and held by their BFF (ADR 0016). The client × role allow-list is identity-auth's: a Customer with a correct password does not receive an authorization code for `adviser-portal`. Adviser-management resource routes stay 403 for Customer.

---

## 2. Conventions (`webapi`)

Minimal API, TypedResults, endpoints dispatch MediatR only. Paths are kebab-case. JSON properties are camelCase (`System.Text.Json` default). No API versioning. Scalar at `/scalar`.

| Topic | Convention |
| --- | --- |
| Resource id | Paths and JSON use `PublicId` only. Internal `int` keys never appear in routes or JSON. Create response `{ id }` is the PublicId |
| Currency | Three-letter upper-case ISO code, not an enum |
| Instants | A request instant (`DateTimeOffset`, for example `bookedAt`) carries `Z` or an offset; the server converts it to UTC. No offset → 400 (§4). Every response instant is UTC with `Z`: `yyyy-MM-ddTHH:mm:ssZ`, with fraction digits only when not zero. Date-only values (`rateAsOfDate`, report `from` / `to`) are `yyyy-MM-dd` and carry no zone. See [fixes/utc-instants.md](fixes/utc-instants.md) |
| Login key | Non-SystemAdmin must send tenantCode on the **hosted login** page, not in a resource-API body |
| Tokens | Short-lived JWT access; refresh in the OpenIddict store. The resource API does **not** issue tokens and does **not** expose `/auth/refresh` |
| Concurrency | Writes to Tenant / User accept `rowVersion`; conflict → 409 |
| Customer | May complete the authorization-server flow; management routes → 403 |
| Authorization | Named policies on endpoints; `RolePermissions` mapped in code; assignment scope in the handler (ADR 0013) |
| CORS | None. `webapi` registers no CORS policy: a portal's browser calls only its BFF, on the BFF's origin, and Scalar is served from the `webapi` origin ([features/bff-adviser-portal.md](features/bff-adviser-portal.md) R15, [architecture.md](architecture.md)) |
| Docs | `[EndpointSummary]` / `[EndpointDescription]` on every action |
| People paths | `/users` is a **namespace prefix**, not a parent resource. People collections hang under it. Do not nest advisers / customers under `/users/{id}`. Tenants, currencies, and ledger routes stay outside this prefix |
| Fee paths | `/fees` is a namespace prefix for fee configuration and calculation, like `/users`; it is not a resource. Fee transactions stay under `/transactions` (§7.9) |
| Interest paths | `/interest` is a namespace prefix for interest configuration and runs, like `/fees`; it is not a resource. Interest postings stay under `/transactions` (§7.10) |

Success codes:

| Action | HTTP |
| --- | --- |
| Create | 201 + `{ id }` (PublicId) |
| Update / disable / enable / password change | 204 |
| List / get one | 200 |
| Validation / business rule | 400 |
| Unauthenticated, invalid token, cannot complete login | 401 |
| Authenticated but policy fails | 403 |
| Cross-tenant / cross-adviser / missing | **404** (do not leak existence) |
| `RowVersion` | 409 |
| Unhandled | 500 |

`webapi` returns 401 on auth failure. It must **never** 302 to hosted login. Login-page cookies stay on the `identity` origin.

---

## 3. Authentication and visibility

The login gate is on `identity`: tenant enabled, `UserStatus = Active`, valid Identity link. The resource API then reads JWT claims and applies the dual tenant check.

JWT claims (permissions are not expanded): `sub` = user PublicId, `email`, `role`, `tenant_id` (nullable), `tenant_code` (nullable). No internal ints. Tenant claims are empty for SystemAdmin.

Protocol scopes (not permissions): `openid`, `profile`, `offline_access`, `api`. Role and tenant are claims, not scopes.

| Role | How tokens are obtained | Phase 1 resource scope |
| --- | --- | --- |
| SystemAdmin | Hosted login on `back-office`, no tenantCode. `adviser-portal` issues no code (identity-auth §4.2) | `/tenants` manage + `GET /tenants/by-code/{code}`, `/users/tenant-admins`, `/users/me`, `/currencies` |
| TenantAdmin | Hosted login on `adviser-portal` + tenantCode | `/users/advisers`, tenant `/users/customers`, `GET /tenants/by-code/{own}`, `/users/me`, `/currencies` |
| Adviser | Hosted login on `adviser-portal` + tenantCode | Assigned `/users/customers`, `GET /tenants/by-code/{own}`, `/users/me`, `/currencies` |
| Customer | Hosted login on `customer-portal` + tenantCode. `adviser-portal` issues no code | The routes §3.1 grants a Customer, for the caller's own data only. `/users/advisers`, `/users/customers`, `/users/tenant-admins`, `/tenants` including by-code → 403 |

Disable / enable for people: `POST /{id}/disable`, `POST /{id}/enable` (`Disabled` ↔ `Active`). Not a physical delete. Disabling an Adviser requires assigned non-disabled Customers to be handled first. Disabling the last TenantAdmin is allowed in Phase 1 (known gap).

Disabling a tenant does **not** bulk-update `User.Status`. Person status is independent; login checks both. After a tenant is disabled, that Code must not complete login, and existing refresh fails once the outbox delivers `TenantDisabled`. Its people get 403 from `webapi` at once (identity-auth §4.5).

Password change, disable, and logout revoke that subject’s OpenIddict tokens. The User aggregate does not hold tokens.

### 3.1 Role permissions

The only role × permission table. It matches `RolePermissions` in `src/Application/Common/Security/RolePermissions.cs`. A permission lets a role call the endpoint; the handler still checks the scope (tenant, the Adviser's assigned Customers, the Customer's own rows). Permissions are not claims in the JWT ([ADR 0013](adr/0013-roles-authorization-single-user-table.md)).

A spec that adds a permission or changes a grant updates this table in the change that accepts it. Other docs link here and do not copy it.

The fifth role `SimulatorOperator` ([ADR 0013](adr/0013-roles-authorization-single-user-table.md) amendment 2026-11-19) holds no permission and has no column. `webapi` answers 401 to its token on every route ([features/simulator.md](features/simulator.md) R3).

| Permission | SystemAdmin | TenantAdmin | Adviser | Customer | Owner |
| --- | --- | --- | --- | --- | --- |
| `tenants.manage` | ✓ | | | | [tenants](features/tenants.md) |
| `tenants.read` | ✓ | ✓ | ✓ | | [tenants](features/tenants.md) |
| `tenant-admins.manage` | ✓ | | | | [tenant-admins](features/tenant-admins.md) |
| `advisers.manage` | | ✓ | | | [advisers](features/advisers.md) |
| `customers.manage` | | ✓ | | | [customers](features/customers.md) |
| `customers.manage-own` | | | ✓ | | [customers](features/customers.md) |
| `users.me` | ✓ | ✓ | ✓ | ✓ | [identity-auth](features/identity-auth.md) |
| `instruments.read` | ✓ | ✓ | ✓ | | [instruments](features/instruments.md) |
| `instruments.create` | ✓ | ✓ | ✓ | | [instruments](features/instruments.md) |
| `instruments.manage` | ✓ | ✓ | | | [instruments](features/instruments.md) |
| `accounts.read` | ✓ | ✓ | ✓ | ✓ | [accounts](features/accounts.md); Customer: Customer Portal, 2026-11-02 |
| `accounts.create` | ✓ | ✓ | ✓ | | [accounts](features/accounts.md) |
| `accounts.manage` | ✓ | ✓ | ✓ | | [accounts](features/accounts.md) |
| `transactions.read` | ✓ | ✓ | ✓ | ✓ | [posting](features/posting.md); Customer: Customer Portal, 2026-11-02 |
| `transactions.create` | ✓ | ✓ | ✓ | | [posting](features/posting.md) |
| `transactions.adjust` | | ✓ | | | [adjustments](features/adjustments.md) (accepted 2026-11-19); every `/adjustment-requests` route since [adjustment-review](features/adjustment-review.md) (accepted 2026-11-25) |
| `statement-lines.manage` | | ✓ | | | [deposit-matching](features/deposit-matching.md) (accepted 2026-11-20) |
| `nominated-bank-accounts.read` | | ✓ | ✓ | ✓ | [withdrawals](features/withdrawals.md) (accepted 2026-11-20) |
| `nominated-bank-accounts.manage` | | ✓ | ✓ | | [withdrawals](features/withdrawals.md) (accepted 2026-11-20) |
| `nominated-bank-accounts.verify` | | ✓ | | | [withdrawals](features/withdrawals.md) (accepted 2026-11-20) |
| `withdrawals.read` | | ✓ | ✓ | ✓ | [withdrawals](features/withdrawals.md) (accepted 2026-11-20) |
| `withdrawals.request` | | ✓ | ✓ | | [withdrawals](features/withdrawals.md) (accepted 2026-11-20) |
| `withdrawals.approve` | | ✓ | | | [withdrawals](features/withdrawals.md) (accepted 2026-11-20) |
| `account-closures.read` | | ✓ | ✓ | ✓ | [account-closure](features/account-closure.md) (accepted 2026-11-22) |
| `account-closures.request` | | ✓ | ✓ | | [account-closure](features/account-closure.md) (accepted 2026-11-22) |
| `bank-reconciliations.manage` | | ✓ | | | [bank-reconciliation](features/bank-reconciliation.md) (accepted 2026-11-21) |
| `interest-rates.read` | | ✓ | ✓ | | [interest-run](features/interest-run.md) (accepted 2026-11-22) |
| `interest-rates.manage` | | ✓ | | | [interest-run](features/interest-run.md) (accepted 2026-11-22) |
| `interest-runs.read` | ✓ | ✓ | | | [interest-run](features/interest-run.md) (accepted 2026-11-22) |
| `interest-runs.start` | ✓ | | | | [interest-run](features/interest-run.md) (accepted 2026-11-22) |
| `valuation-runs.read` | ✓ | ✓ | | | [valuation-run](features/valuation-run.md) (accepted 2026-11-26) |
| `valuation-runs.start` | ✓ | | | | [valuation-run](features/valuation-run.md) (accepted 2026-11-26) |
| `corporate-actions.read` | | ✓ | ✓ | | [corporate-actions](features/corporate-actions.md) (accepted 2026-11-22) |
| `corporate-actions.manage` | | ✓ | | | [corporate-actions](features/corporate-actions.md) (accepted 2026-11-22) |
| `orders.read` | | ✓ | ✓ | ✓ | [orders](features/orders.md) (accepted 2026-11-23) |
| `orders.place` | | ✓ | ✓ | | [orders](features/orders.md) (accepted 2026-11-23) |
| `trade-settlements.manage` | | ✓ | | | [trade-settlement](features/trade-settlement.md) (accepted 2026-11-23) |
| `rebalances.read` | | ✓ | ✓ | | [rebalances](features/rebalances.md) (accepted 2026-11-23) |
| `rebalances.confirm` | | ✓ | ✓ | | [rebalances](features/rebalances.md) (accepted 2026-11-23) |
| `migration-batches.manage` | | ✓ | | | [migration-batches](features/migration-batches.md) (accepted 2026-11-24) |
| `in-kind-transfers.read` | | ✓ | ✓ | ✓ | [in-kind-transfers](features/in-kind-transfers.md) (accepted 2026-11-24) |
| `in-kind-transfers.request` | | ✓ | ✓ | | [in-kind-transfers](features/in-kind-transfers.md) (accepted 2026-11-24) |
| `in-kind-transfers.approve` | | ✓ | | | [in-kind-transfers](features/in-kind-transfers.md) (accepted 2026-11-24) |
| `custody-movements.manage` | | ✓ | | | [in-kind-transfers](features/in-kind-transfers.md) (accepted 2026-11-24) |
| `custodian-reconciliations.manage` | | ✓ | | | [in-kind-transfers](features/in-kind-transfers.md) (accepted 2026-11-24) |
| `portfolios.read` | ✓ | ✓ | ✓ | | [portfolios](features/portfolios.md) |
| `portfolios.manage` | ✓ | ✓ | ✓ | | [portfolios](features/portfolios.md) |
| `reports.customer.read` | | ✓ | ✓ | ✓ | [reports](features/reports.md) |
| `reports.customer.create` | | ✓ | ✓ | ✓ | [reports](features/reports.md); Customer: Customer Portal, 2026-11-02 |
| `reports.book.read` | | ✓ | ✓ | | [reports](features/reports.md) |
| `reports.book.create` | | ✓ | ✓ | | [reports](features/reports.md) |
| `reports.tenant.read` | | ✓ | | | [reports](features/reports.md); scope `Tenant`: [bank-reconciliation](features/bank-reconciliation.md) (accepted 2026-11-21) |
| `reports.tenant.create` | | ✓ | | | [reports](features/reports.md); scope `Tenant`: [bank-reconciliation](features/bank-reconciliation.md) (accepted 2026-11-21) |
| `fees.read` | ✓ | ✓ | ✓ | | [trade-fees](features/trade-fees.md) (landed) |
| `fees.manage` | ✓ | ✓ | | | [trade-fees](features/trade-fees.md) (landed) |

---

## 4. Errors

| Case | HTTP | Who |
| --- | --- | --- |
| Validation / business rule (Adviser still has Customers, duplicate email, illegal reporting currency, …) | 400 | `webapi` |
| Request instant with no offset, or not an ISO 8601 date and time | 400, `errors.<Field>` (for example `errors.BookedAt`) | `webapi` |
| `bookedAt` later than the server clock plus 5 minutes ([fixes/ledger-write-guards.md](fixes/ledger-write-guards.md)) | 400, `errors.BookedAt` | `webapi` |
| Cash `amount` with more decimal places than the Account currency's `DecimalPlaces` ([fixes/ledger-write-guards.md](fixes/ledger-write-guards.md)) | 400, `errors.Amount` | `webapi` |
| Any other body that cannot be read (malformed JSON, wrong type) | 400 (415 for a wrong content type), `ProblemDetails` titled `Bad request`, in every environment | `webapi` |
| Target exists but is disabled and the caller tries to create / attach / post on it (§4.1) | 400 `disabled` | `webapi` |
| Missing / invalid Bearer; calling resources after refresh was revoked | 401 | `webapi` |
| Tenant disabled, Status ≠ Active, bad password, wrong tenant (at login) | Authorization does not complete (login failure to the caller) | Gate on `identity` |
| Tenant disabled or Status ≠ Active, with a still-valid access token | 403 on every route that needs a permission (identity-auth §4.5) | `webapi` current-user load |
| Authenticated but policy fails (including Customer on adviser-management routes) | 403 | `webapi` |
| Cross-tenant / cross-adviser / missing | 404 | `webapi` |
| State conflict: stale `rowVersion`, reused `Idempotency-Key`, changed FX rate, … | 409 with a problem `code` (§4.2) | `webapi` |
| Unhandled | 500 | Either host |

Whether login failures distinguish Disabled from bad credentials is **still open** (§9). Until locked, treat them as a uniform failure and do not enumerate the reason. A disabled tenant or Disabled person at **hosted login** is not §4.1: that is a uniform failure on `identity`, not this JSON.

### 4.1 Target is disabled (shared error)

Tenant, User, and later Account can all be disabled. When the caller writes against a target that **exists but is disabled**, use one shape. Do not invent a new code per slice.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Tenant is disabled",
  "status": 400,
  "detail": "Cannot create a TenantAdmin while the tenant is disabled. Enable the tenant first.",
  "code": "disabled",
  "target": "tenant",
  "targetId": "<publicId>"
}
```

| Field | Rule |
| --- | --- |
| HTTP | 400. Unknown target is still 404. Not 409 / 403. |
| `code` | Always `disabled` |
| `target` | `tenant` / `user` / `account`. Phase 1 uses the first two; reserve `account` so Phase 2 does not change the contract |
| `targetId` | PublicId of the disabled target |
| `title` | Fixed per target: `Tenant is disabled` / `User is disabled` / `Account is disabled` |
| `detail` | This action (which command was rejected, what to enable) |
| `type` | Default RFC9110 400. Do not add a custom type URI namespace |

Phase 1 call site:

- `POST /users/tenant-admins` on a disabled tenant → `target=tenant` ([features/tenant-admins.md](features/tenant-admins.md))
- Create or reassign a Customer onto a disabled Adviser → `target=user` (detail in [features/customers.md](features/customers.md))
- Rejecting a disabled catalog currency as a **new** reporting currency stays a plain validation 400 (Currency has no PublicId and is not this lifecycle)

Implement as one Application `TargetDisabled` exception (kind + targetId + detail) mapped once in `webapi`. Do not assemble this JSON in three handlers.

List filters are separate: currencies and people collections use `enabledOnly` (omitted / `false` = all, `true` = enabled / Active only). Do not retrofit landed `GET /tenants?isEnabled=`.

### 4.2 Problem codes (accepted, 2026-11-13)

Status of this section: `accepted` (2026-11-13). It was built with the portfolios change of 2026-11-13 ([features/portfolios.md](features/portfolios.md) §12). Landed 2026-11-13: every code in the registry below is a `ProblemException` subclass.

**Why.** One HTTP status already carries several meanings. `webapi` returns 409 for a stale `rowVersion`, a reused `Idempotency-Key`, a changed FX rate, a report that is not ready, and a portfolio already assigned to the account. `POST /transactions/fx` alone can return two different 409s today, and only the first has a field to tell them apart (`errors.expectedRate`). Portfolios §4.1 adds a sixth. A client that branches on the status alone shows the wrong message as soon as one route can return two of them. A **problem code** names the meaning. The status stays the category.

| Id | Rule |
| --- | --- |
| C1 | Every 409 from `webapi` carries a problem `code`. Every 400 that is a named business refusal carries one too (§4.1 `disabled`, the rebalance failure codes). Field validation stays a 400 with `errors` and no `code`. 401, 403 and 404 carry no `code`. |
| C2 | `status` is the category; `code` is the meaning. A client that needs to treat two refusals differently branches on `code`. Branching on the status alone is allowed only for 401, 403, 404 and field validation 400. |
| C3 | A code is lower snake_case English and names the condition, not the remedy (`stale_row_version`, not `reload_needed`). Once a code ships it is never renamed, never given a second meaning and never moved to another status. To retire a code, stop returning it and mark its registry row retired. Do not reuse the name. |
| C4 | One meaning, one code, on every route. Several routes return the same code when the meaning is the same (`stale_row_version` on every `rowVersion` write). Do not add a slice-specific code for a meaning that already has one (same rule as §4.1). |
| C5 | Body: RFC 9110 ProblemDetails with `status`, `title`, `detail` and `code`. `title` is fixed for the code. `detail` describes this request, is safe to show to the caller, and never names or counts rows the caller cannot read. Other extension fields only when the registry row lists them. Default RFC 9110 `type`; no custom type URIs. |
| C6 | A spec that adds a code adds its registry row in the same change. It states the status, the meaning and any extra fields. It also lists the client pages on the routes that return it and says whether they need special handling. |
| C7 | A client that receives a code it does not handle, or a coded 4xx with no special handling, shows `detail` and does nothing else (no refetch, no retry, no form reset). So a new code is safe in every client without a client change. A client change is needed only for special handling, such as refetching after `stale_row_version` or fetching a new quote after `rate_changed`. |
| C8 | A page that today branches on `status === 409` stays correct while every route it calls returns one 409 code. When a spec adds a second 409 code to a route, the pages that call that route move to branching on `code` in the same change. |
| C9 | Back end: `webapi` maps coded refusals in one place. Application has one abstract exception for them, `ProblemException`, with `Kind` (`Conflict` → 409, `Rule` → 400), `Code`, `Title`, `Detail` and optional extensions. `ProblemDetailsExceptionHandler` maps every `ProblemException` in one arm. A new code is a new subclass in Application. The handler, the Web layer and other slices do not change. Application never holds HTTP status numbers. |

**Registry.** The only list of problem codes. A code that is not here must not be returned.

| `code` | Status | Meaning | Extra fields | Owner |
| --- | --- | --- | --- | --- |
| `stale_row_version` | 409 | The request's `rowVersion` is not the stored one. Reload and try again. | — | §2 Concurrency; every write that takes `rowVersion` |
| `idempotency_key_reused` | 409 | This `Idempotency-Key` was used before with a different request. | — | [ADR 0015](adr/0015-idempotency-keys.md) |
| `rate_changed` | 409 | The stored FX rate is not the request's `expectedRate`. | `errors.expectedRate` | [features/fx-conversions.md](features/fx-conversions.md) R12 |
| `report_not_ready` | 409 | The report execution has no result to download yet. | — | [features/reports.md](features/reports.md) |
| `portfolio_already_assigned` | 409 | The account's Active assignment is already this portfolio. | — | [features/portfolios.md](features/portfolios.md) §7 `AssignPortfolio` |
| `portfolio_shared` | 409 | The caller is an Adviser and the portfolio is a Shared portfolio. | — | [features/portfolios.md](features/portfolios.md) §4.1 |
| `fee_schedule_in_effect` | 409 | The trade fee schedule row may already apply to a posted trade, so it cannot be updated or deleted. Add a new row instead. | — | [features/trade-fees.md](features/trade-fees.md) R11 |
| `interest_schedule_in_effect` | 409 | The interest rate schedule row may already apply to an accrued day, so it cannot be updated or deleted. Add a new row instead. | — | [features/interest-run.md](features/interest-run.md) R13 |
| `disabled` | 400 | §4.1 | `target`, `targetId` | §4.1 |
| `missing_price`, `missing_fx`, `insufficient_cash`, `account_not_eligible`, `portfolio_inactive`, `concurrent_modification` | 400 | Rebalance failure | — | [features/portfolios.md](features/portfolios.md) §11 |
| `stale_price`, `instrument_inactive`, `currency_mismatch`, `no_broker_connection` | 400 | A rebalance cannot be planned or cannot place orders: a price more than 4 days old, an inactive instrument, a target line quoted in another currency than the account's, or no configured broker connection | — | [features/rebalances.md](features/rebalances.md) R6, R8 |
| `rebalance_in_progress` | 400 | A rebalance of the account is in progress, so no new proposal or assignment is made | — | [features/rebalances.md](features/rebalances.md) R4, R23 |
| `migration_batch_not_valid` | 400 | A migration batch has validation issues, so it cannot be submitted or posted; the issues are on the batch item | — | [features/migration-batches.md](features/migration-batches.md) R17, R21 |

The five 409 codes above `portfolio_shared` are added to responses that exist today. Their status, `title`, `detail` and other fields do not change, so the change is additive for every client. The 400 codes already ship with these names.

---

## 5. Authorization server (`identity`)

Tables and package rules live in database design / ADR 0014. This section lists the caller-visible surface.

Protocol paths use **OpenIddict defaults. Do not remap.** Portals and `webapi` follow discovery; do not hard-code these strings in business code.

| Surface | Default path | Notes |
| --- | --- | --- |
| Discovery | `GET /.well-known/openid-configuration` | Authoritative endpoint table |
| JWKS | `GET /.well-known/jwks` | `webapi` validates Bearer; asymmetric signing |
| Authorize | `GET /connect/authorize` | Authorization code + PKCE. Unauthenticated callers hit hosted login |
| Token | `POST /connect/token` | Exchange for access / refresh; also used to refresh. No password grant |
| Revocation | `POST /connect/revocation` | Revoke refresh on logout (default name is revocation, not revoke) |
| End session | `GET/POST /connect/logout` | Portal returns to registered `{portalOrigin}/`. IdentityHost passthrough requires the SignOut action in identity-auth R21 |
| Userinfo | `GET /connect/userinfo` | Library default; Phase 1 portal mainly uses access-token claims |
| Hosted login | Password page on `identity` | email + password + tenantCode (SystemAdmin omits tenantCode). **Not** a `/connect` protocol endpoint. Razor Pages at `/login` |

**Portal client**

| Item | Value |
| --- | --- |
| ClientId | `adviser-portal`. `customer-portal` has the same shape ([features/bff-customer-portal.md](features/bff-customer-portal.md) R10, R11) |
| Type | Confidential client + PKCE, held by the portal's BFF; the secret is in host configuration (ADR 0016) |
| Grant | Authorization code + refresh. Password grant off |
| Redirect | `{bffOrigin}/signin-oidc` ([features/bff-adviser-portal.md](features/bff-adviser-portal.md) R11) |
| Post-logout | `{bffOrigin}/` |

**Out (authorization server)**

- Custom `POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout` as a token issuer
- Resource Owner Password Credentials
- A public client that keeps tokens in the browser (ADR 0016)
- A separate identity SQL database
- Invitation, forgot-password, MFA, external IdP

Access lifetime is 15 minutes. Refresh lifetime is 14 days absolute. Hosted login is Razor Pages at `/login`. See identity-auth.

---

## 6. Phase 1 resource catalog (`webapi`)

`/users` is the people-management namespace:

```text
/users/me
/users/tenant-admins
/users/advisers
/users/customers
```

This is not “a User with child collections”. Do not use `/users/{id}/advisers`. Do not expose a catch-all `GET/POST /users` that branches on `role` in the body.

| Resource | Base path | Verbs | Policy |
| --- | --- | --- | --- |
| Current user | `/users/me` | GET, PUT, PUT password | `users.me` |
| Tenant admins | `/users/tenant-admins` | list / get / create / update / disable / enable | `tenant-admins.manage` |
| Advisers | `/users/advisers` | same | `advisers.manage` |
| Customers | `/users/customers` | same; sub-resource `…/{customerId}/tax-profile` GET / PUT (features/interest-run.md) | `customers.manage` or `customers.manage-own` |
| Currencies | `/currencies` | GET | Authenticated |
| FX rates | `/fx-rates` | GET (the stored rates of one directed pair; platform data). See features/market-data-history.md | Authenticated |
| Tenants | `/tenants` | list / get / create / update / disable / enable | `tenants.manage` |
| Client money accounts | `/tenants/{tenantId}/client-money-accounts` | list / get / create / disable / enable. See features/client-money-accounts.md; `bankConnection`, `bankFeed` in features/deposit-matching.md | `tenants.manage` |
| Bank connections | `/bank-connections` | list (names and adapters from configuration). See features/deposit-matching.md | `tenants.manage` |
| Market data connections | `/market-data-connections` | list (names, adapters, symbol suffixes from configuration, and the state of each price feed and corporate action feed). See features/simulated-instruments.md and features/corporate-actions.md | `tenants.manage` |
| Corporate action announcements | `/corporate-action-announcements` | list (platform data, newest first; filters `connection`, `status`). See features/corporate-actions.md | `tenants.manage` |
| Broker connections | `/broker-connections` | list (names and adapters from configuration, and the state of each execution report feed). See features/orders.md | `tenants.manage` |
| Execution reports | `/execution-reports` | list (platform data, newest first; filters `connection`, `status`). See features/orders.md | `tenants.manage` |
| Statement lines | `/statement-lines` | list / get; match / ignore / request-return / reopen / unmatch. See features/deposit-matching.md and features/bank-returns.md (returns, recalls, debit lines) | `statement-lines.manage` |
| Nominated bank accounts | `/nominated-bank-accounts` | list / get / create; verify / reject / remove. No PUT. See features/withdrawals.md | `nominated-bank-accounts.read` / `.manage` / `.verify` |
| Withdrawals | `/withdrawals` | list / get / create (`Idempotency-Key`); approve / reject / cancel. See features/withdrawals.md | `withdrawals.read` / `.request` / `.approve` |
| Bank reconciliations | `/bank-reconciliations` | overview / list / get / create (run now). No PUT or DELETE: a run never changes. See features/bank-reconciliation.md | `bank-reconciliations.manage` |
| Instruments | `/instruments` | list / get / create / update / disable / enable; `GET …/prices` (features/market-data-history.md) | `instruments.read` / `instruments.create` / `instruments.manage` |
| Accounts | `/accounts` | list / get / create / update / close / reopen; `GET …/cash-balance`; `GET …/deposit-instructions` (features/client-money-accounts.md); sub-resource `…/closures`: request (`Idempotency-Key`) / list / get (features/account-closure.md); `GET …/interest-accruals` (features/interest-run.md); `GET …/valuation?date=` (features/account-valuation.md); `GET …/valuations`, `GET …/valuations/{date}` (features/valuation-run.md) | `accounts.read` / `accounts.create` / `accounts.manage`; closures `account-closures.read` / `.request` |
| Transactions | `/transactions` | list / get / create / reverse | `transactions.read` / `transactions.create` |
| Net worth | `/net-worth`, `/users/customers/{id}/net-worth`, `/net-worth/funds-under-management` | GET. See features/net-worth.md; `breakdowns.byCustodyMode` in features/custody-mode-grouping.md; `folded` and the funds under management series (`from`, `to`) in features/valuation-history.md | `accounts.read` |
| Portfolios | `/portfolios`, `/accounts/{id}/portfolio`, `/accounts/{id}/rebalance` | list / get / create / update / disable; assign / unassign / rebalance. See features/portfolios.md | `portfolios.read` / `portfolios.manage` + `transactions.create` |
| Fees | `/fees/trade-schedules` (namespace `/fees`) | list / get / create / update / delete. See features/trade-fees.md | `fees.read` / `fees.manage` |
| Interest | `/interest/rate-schedules`, `/interest/runs` (namespace `/interest`) | rate schedules: list / get / create / update / delete; runs: list / get / create (start a run). See features/interest-run.md | `interest-rates.read` / `.manage`; `interest-runs.read` / `.start` |
| Valuation runs | `/valuation-runs` | list / get / create (start a run through a date). No PUT or DELETE: a run never changes. See features/valuation-run.md | `valuation-runs.read` / `.start` |
| Corporate actions | `/corporate-actions` | list / get / create / update; cancel / apply; entitlements. See features/corporate-actions.md | `corporate-actions.read` / `corporate-actions.manage` |
| Orders | `/orders` | list / get / create (`Idempotency-Key`) / preview; cancel; resolve the break of a fill (`transactions.adjust`). No PUT: an order never changes. See features/orders.md and features/trade-settlement.md | `orders.read` / `orders.place` |
| Trade settlements | `/trade-settlements` | list / get; pay again / set aside. No create: the `worker` creates them from the broker's `Settlement` report. See features/trade-settlement.md | `trade-settlements.manage` |
| Rebalances | `/rebalances` | list / get; confirm / cancel. No create: `POST /accounts/{id}/rebalance`, assign and the rebalance job propose. See features/rebalances.md | `rebalances.read` / `rebalances.confirm` |
| Migration batches | `/migration-batches` | list / get / create / replace (PUT); submit / return / cancel / post. Funding is `POST /statement-lines/{id}/match` with `migrationBatchId`. See features/migration-batches.md | `migration-batches.manage` |
| In-kind transfers | `/in-kind-transfers` | list / get / request; approve / reject / cancel / set aside. See features/in-kind-transfers.md | `in-kind-transfers.read`, `.request`, `.approve` |
| Custody movements | `/custody-movements` | list / get; ignore / reopen. See features/in-kind-transfers.md | `custody-movements.manage` |
| Custodian reconciliations | `/custodian-reconciliations` | overview / list / get / run now. See features/in-kind-transfers.md | `custodian-reconciliations.manage` |
| Adjustment requests | `/adjustment-requests` | list / get / request (`Idempotency-Key`); approve / reject / cancel. No PUT: a request never changes. See features/adjustment-review.md | `transactions.adjust` |
| Reports | `/reports` | list / request / schedules / executions / download | `reports.customer.read` / `reports.customer.create` / `reports.book.read` / `reports.book.create` / `reports.tenant.read` / `reports.tenant.create` |

Path `{id}` is always PublicId.

Phase 1 has **no** `/auth/*`, `/holdings`, `/dashboard`, invitation, or forgot-password. `/instruments`, `/accounts`, and `/transactions` are landed.

---

## 7. Endpoint notes

Field rules belong in each Feature Spec. This section only locks the catalog and cross-slice conventions.

### 7.1 Current user

| Method | Route | Success |
| --- | --- | --- |
| GET | `/users/me` | 200 profile |
| PUT | `/users/me` | 204 |
| PUT | `/users/me/password` | 204 |

- Every authenticated role may call these (including Customer and SystemAdmin).
- Profile PUT updates `name` only (may include `rowVersion`). Email / Role / TenantId / Status cannot change here.
- Password change requires the current password. On success, revoke that subject’s OpenIddict tokens. The caller must run the authorization-code flow again.
- Not a token endpoint. Do not return tokens.

### 7.2 Currencies

`GET /currencies` and `GET /currencies?enabledOnly=true|false`.

| `enabledOnly` | Behaviour |
| --- | --- |
| Omitted or `false` | All rows (including disabled) |
| `true` | Enabled rows only |
| Any other value | 400 |

Item: `{ code, name, decimalPlaces, isActive }`. Sorted by `code`. No “disabled-only” query. No pagination. No write API. No `GET /currencies/{code}`. Column and JSON renamed 2026-09-21: `IsEnabled` → `IsActive`.

Any authenticated caller (default Authorize; no `currencies.read`). Seed: NZD, AUD, USD, EUR, GBP, JPY.

Field rules and `Money` live in [features/currencies.md](features/currencies.md).

### 7.3 Tenants

Manage verbs: SystemAdmin only (`tenants.manage`).

Lookup: `GET /tenants/by-code/{code}` uses `tenants.read` (SystemAdmin, TenantAdmin, Adviser). TenantAdmin / Adviser may read only their own code (else 404). Customer → 403.

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/tenants/by-code/{code}` | `tenants.read` | 200 item |
| GET | `/tenants` | `tenants.manage` | 200 paged list |
| GET | `/tenants/{id}` | `tenants.manage` | 200 |
| POST | `/tenants` | `tenants.manage` | 201 `{ id }` |
| PUT | `/tenants/{id}` | `tenants.manage` | 204 |
| POST | `/tenants/{id}/disable` | `tenants.manage` | 204 |
| POST | `/tenants/{id}/enable` | `tenants.manage` | 204 |

- POST body: `name`, `code`, `reportingCurrency`, required `allowedCustodyModes` (array of `External` / `Custodial`, non-empty, no repeats; bad shape → 400 `errors.AllowedCustodyModes`), optional `brokerConnection` (a name from configuration, else 400 `errors.BrokerConnection`)
- PUT body: `name`, `reportingCurrency`, `rowVersion`; optional `allowedCustodyModes` and `brokerConnection` (omitted, or `null` for the connection, = unchanged)
- The item on list, get and by-code carries `allowedCustodyModes`, `clientMoneyCurrencies` and `brokerConnection` (`null` when none).
- `code` is immutable after create (login key). Reporting currency may change (no ledger balance is keyed on it).
- After disable, that Code must not complete login; existing refresh fails once the outbox delivers `TenantDisabled`. Its people get 403 from `webapi` at once. Person `Status` is not bulk-updated.
- Field rules: [features/tenants.md](features/tenants.md), [features/tenant-custody-modes.md](features/tenant-custody-modes.md), [features/orders.md](features/orders.md) R3, R4.

Sub-resources and configuration lists of a Tenant, all `tenants.manage`, with no problem code of their own:

| Route | Verbs | Notes |
| --- | --- | --- |
| `/tenants/{tenantId}/client-money-accounts` | list, get, create (201 `{ id }`), `POST …/{id}/disable`, `POST …/{id}/enable` (`rowVersion`). No PUT | One active row per currency and an account number active on one row only, else 400 `errors.Currency` / `errors.AccountNumber`. Create requires `bankConnection`, a name from configuration (else 400 `errors.BankConnection`). The item carries `bankConnection` and `bankFeed` (`{ lastSucceededAt, lastFailedAt, lastFailure }` or `null`). [features/client-money-accounts.md](features/client-money-accounts.md), [features/deposit-matching.md](features/deposit-matching.md) |
| `/tenants/{tenantId}/safekeeping-accounts` | list, get, create, disable, enable. No PUT | One active per currency. [features/in-kind-transfers.md](features/in-kind-transfers.md) R2–R7 |
| `GET /bank-connections` | list | The configured bank connections, without secrets |
| `GET /broker-connections` | list | The configured names with the state of each execution report feed and `settlementCurrencies` |
| `GET /execution-reports` | list | The stored reports (filters `connection`, `status`) |
| `GET /custodian-connections` | list | The configured custodian connections |

### 7.4 People (TenantAdmin / Adviser / Customer)

Create is a dual write: `AspNetUsers` first (`TenantId` projection), then `Users.IdentityUserId`. Responses never include the password. Create returns 201 `{ id }`.

Phase 1 create may set a password and land in `Active` (invitation is not built; that is not a reason to collapse the status machine to a boolean).

| Resource | Caller | Create required | List scope |
| --- | --- | --- | --- |
| `/users/tenant-admins` | SystemAdmin | `tenantId` (PublicId), `name`, `email`, `password` | Optional tenant filter. Field rules: [features/tenant-admins.md](features/tenant-admins.md) |
| `/users/advisers` | TenantAdmin | `name`, `email`, `password` | Current tenant. Field rules: [features/advisers.md](features/advisers.md) |
| `/users/customers` | TenantAdmin; Adviser (assigned) | `name`, `email`, `password`, `adviserId` | TenantAdmin: tenant; Adviser: self only. Field rules: [features/customers.md](features/customers.md) |

Shared actions (`{collection}` = `tenant-admins` / `advisers` / `customers`):

| Method | Route | Success |
| --- | --- | --- |
| GET | `/users/{collection}` | 200 paged; simple search on name / email |
| GET | `/users/{collection}/{id}` | 200 |
| POST | `/users/{collection}` | 201 `{ id }` |
| PUT | `/users/{collection}/{id}` | 204 (`name`; Customer `adviserId` is TenantAdmin only) |
| POST | `/users/{collection}/{id}/disable` | 204 |
| POST | `/users/{collection}/{id}/enable` | 204 |

Rule summary:

- Email is unique inside a tenant. SystemAdmin email is unique globally. The same email may exist in two tenants.
- Role / TenantId / Email cannot change after create.
- Disable Adviser: reject with 400 while any assigned Customer is not Disabled.
- Disable Customer: no ledger guard in Phase 1.
- An Adviser creating a Customer may only set `adviserId` to self; otherwise 400.
- An Adviser reading another Adviser’s Customer → 404. Cross-tenant is always 404.
- Customer calling `/users/advisers`, `/users/customers`, `/users/tenant-admins`, or `/tenants` → 403.
- Disable and password change revoke that person’s OpenIddict tokens.

People lists reuse the tenants envelope `{ items, page, pageSize, totalCount }` (page default 1, size default 20, max 100). Currencies, people collections, and instruments use `enabledOnly` (omit / `false` = all, `true` = enabled / Active only). Each person item includes `status`. Create on a disabled tenant uses §4.1.

Tax profile ([features/interest-run.md](features/interest-run.md) R1–R8): sub-resource `GET` / `PUT /users/customers/{customerId}/tax-profile` (`customers.manage` or `customers.manage-own`, scope as every `/users/customers` route; a Customer → 403) and `GET /users/me/tax-profile` (`users.me`; a caller who is not a Customer → 404). The item is `{ taxResidency, irdNumberHeld, rwtRatePercent, withholding: { kind, ratePercent, basis }, updatedBy, updatedAt, rowVersion }`; `PUT` takes the three attributes and `rowVersion` (`null` for the first write) and answers 204; stale → 409 `stale_row_version`. The IRD number itself is never stored.

### 7.5 Instruments

Field rules: [features/instruments.md](features/instruments.md).

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/instruments` | `instruments.read` | 200 envelope |
| GET | `/instruments/{id}` | `instruments.read` | 200 item |
| GET | `/instruments/{id}/prices` | `instruments.read` | 200 envelope of `{ date, price, currency, source, fetchedAt }`, newest first |
| POST | `/instruments` | `instruments.create` | 201 `{ id }` |
| PUT | `/instruments/{id}` | `instruments.manage` | 204 |
| POST | `/instruments/{id}/disable` | `instruments.manage` | 204 |
| POST | `/instruments/{id}/enable` | `instruments.manage` | 204 |

- TenantAdmin / Adviser: current tenant. Body / list query must omit `tenantId`.
- SystemAdmin: list and create require tenant PublicId. Get / PUT / disable / enable may cross tenants.
- Customer → 403.
- Item includes `id`, `tenantId`, `symbol`, `name`, `quoteCurrency`, `isActive`, `marketDataConnection`, `rowVersion`. No price. `marketDataConnection` is the name of the market data connection whose symbol suffix the symbol ends with, or `null`; it is worked out from the symbol and the configuration, and there is no filter on it ([features/simulated-instruments.md](features/simulated-instruments.md) R6).
- Create on a disabled tenant → §4.1 `target=tenant`.
- Prices ([features/market-data-history.md](features/market-data-history.md) R19, R21–R23): optional `from` and `to` (`yyyy-MM-dd`, inclusive) and paging. The instrument is found as `GET /instruments/{id}` finds it (other Tenant or missing → 404; SystemAdmin any Tenant). An inactive instrument is readable. `?to={date}&pageSize=1` answers the row a price read as of that date chooses.

### 7.6 Accounts

Field rules: [features/accounts.md](features/accounts.md).

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/accounts` | `accounts.read` | 200 envelope |
| GET | `/accounts/{id}` | `accounts.read` | 200 item |
| POST | `/accounts` | `accounts.create` | 201 `{ id }` |
| PUT | `/accounts/{id}` | `accounts.manage` | 204 |
| POST | `/accounts/{id}/close` | `accounts.manage` | 204 |
| POST | `/accounts/{id}/reopen` | `accounts.manage` | 204 |
| GET | `/accounts/{id}/cash-balance` | `accounts.read` | 200 `{ accountId, currency, cashBalance, reservedCash, availableCash, unsettledCash, withdrawableCash }` |
| GET | `/accounts/{id}/deposit-instructions` | `accounts.read` | 200 `{ accountId, currency, reference, bankName, accountName, accountNumber }` |
| GET | `/accounts/{accountId}/interest-accruals` | `accounts.read` | 200 envelope, newest first (filters `from`, `to`, `transactionId`, `posted`) |
| GET | `/accounts/{id}/valuation?date=` | `accounts.read` | 200 item |
| GET | `/accounts/{id}/valuations` | `accounts.read` | 200 envelope, newest day first (filters `from`, `to`, `complete`) |
| GET | `/accounts/{id}/valuations/{date}` | `accounts.read` | 200 item (the stored valuation of the day) |
| PUT | `/accounts/{accountId}/dividend-reinvestment` | `accounts.manage` | 204 |
| POST | `/accounts/{accountId}/closures` | `account-closures.request` | 201 `{ id }` (`Idempotency-Key`) |
| GET | `/accounts/{accountId}/closures`, `…/{id}` | `account-closures.read` | 200 |

Who:

- TenantAdmin / Adviser: current tenant. Body / list query must omit `tenantId`. Adviser: assigned Customers only.
- SystemAdmin: list and create require tenant PublicId. Get / PUT / close / reopen may cross tenants. SystemAdmin has neither closure permission.
- A Customer reads their own accounts (`accounts.read`, §3.1) and writes nothing.

Item: `id`, `tenantId`, `customerId`, `name`, `type`, `custodyMode` (`External` / `Custodial`), `currency`, `status`, `isActive`, `rowVersion`, and the computed fields below. A POST or PUT body that carries `depositReference` → 400.

| Field | Is | External account | Field rules |
| --- | --- | --- | --- |
| `cashBalance` | The cash `SUM` | — | [features/posting.md](features/posting.md) |
| `depositReference` | Server-generated on open | `null` | [features/client-money-accounts.md](features/client-money-accounts.md) |
| `reservedCash` | Open withdrawals plus live buy orders | `0` | [features/withdrawals.md](features/withdrawals.md), [features/orders.md](features/orders.md) R39 |
| `availableCash` | Cash balance minus reserved cash | The cash balance | [features/withdrawals.md](features/withdrawals.md) |
| `unsettledCash` | The net cash of posted sell fills that are not settled | `0` | [features/orders.md](features/orders.md) R44 |
| `withdrawableCash` | Available cash minus unsettled cash, never below 0 | The cash balance | [features/orders.md](features/orders.md) R44 |
| `closure` | The latest account closure, or `null` | `null` | [features/account-closure.md](features/account-closure.md) |
| `accruedInterest`, `interestAccruedThrough` | Unposted daily accruals (8 decimals); a date or `null` | `0`, `null` | [features/interest-run.md](features/interest-run.md) R33, R35, R36 |
| `reinvestDividends`, `pendingDividends` | The account's election; dividends not paid yet | — | [features/dividends.md](features/dividends.md) §8 |

Rules:

- **Create.** `custodyMode` is required; a missing or unknown value is a field 400 with no problem code. `Custodial` needs `type` `Brokerage` (else 400 `errors.Type`), a Tenant that allows the mode (else 400 `errors.CustodyMode`) and an active client money account of the Tenant in the currency (else 400 `errors.Currency`). Create on a disabled tenant → §4.1 `target=tenant`. Create or reopen on a Disabled Customer → §4.1 `target=user`. Field rules: [features/account-custody-mode.md](features/account-custody-mode.md), [features/custody-modes.md](features/custody-modes.md), [features/tenant-custody-modes.md](features/tenant-custody-modes.md).
- **Update.** A PUT body that includes `custodyMode` → 400.
- **List.** Optional filters `custodyMode` and `depositReference` (exact after trim and upper-casing; an invalid value returns an empty list).
- **Close.** On an External account close is rejected while cash `SUM ≠ 0` or a holding remains. `POST /accounts/{id}/close` on an Open Custodial account → 400 for every caller: it closes through `POST /accounts/{accountId}/closures`. The request posts the interest accrued through yesterday before the payout is worked out, and is refused while the account has a live order, a fill that is not settled or a pending dividend. While a closure is `InProgress` the Account is closing and every hand write on it → 400. No problem code.
- **Deposit instructions.** 404 for an External or Closed account, or when the currency has no active client money account.
- **Holdings.** The item of `GET /accounts/{id}/holdings` carries `reservedQuantity`, `availableQuantity`, `unsettledQuantity` and `transferableQuantity` ([features/orders.md](features/orders.md) R40, R41; [features/in-kind-transfers.md](features/in-kind-transfers.md) R43–R45).
- **Valuation.** `GET /accounts/{id}/valuation?date=yyyy-MM-dd` values the Account for one UTC day: `{ accountId, date, currency, cash, holdings, holdingsValue, total, complete, unvaluedCount }`. `date` is required and not after today (else 400 `errors.Date`). A holding that cannot be valued has a status and no value, and the valuation is incomplete; it is never an error. A read only, no `Idempotency-Key`. Field rules: [features/account-valuation.md](features/account-valuation.md).
- **Stored valuations.** `GET /accounts/{id}/valuations` and `GET /accounts/{id}/valuations/{date}` read what the valuation run stored for a Custodial account (§7.18). The account item carries `valuedThrough`, the last day stored (`null` before the first and on an External account).
- **Portfolio.** `POST /accounts/{id}/portfolio` and `POST /accounts/{id}/rebalance` require a Custodial account, else 400 `account_not_eligible`. Both answer 200 `{ status, rebalanceId }` with `status` `Proposed` or `NoChange`; a failed plan is 400 with its problem code. `GET /accounts/{id}/portfolio` carries `rebalance` (`{ id, status, failure, proposedAt, endedAt }`) ([features/rebalances.md](features/rebalances.md) R10, R25).

### 7.7 Transactions

Field rules: [features/posting.md](features/posting.md) for the aggregate, the cash types and the item; the spec of each other type is named in posting §5. The transaction item has `cash` and `security` arrays; every leg carries its own `accountId` and `customerId`, and only legs on accounts the caller can see are returned ([features/leg-accounts.md](features/leg-accounts.md) §8.1). There is no top-level `accountId` / `customerId` and no flat `amount` on the item. Create body for a cash type sends `amount`. `Split` / `Bonus` / `Scrip` send the new total quantity and omit cash.

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/transactions` | `transactions.read` | 200 envelope |
| GET | `/transactions/{id}` | `transactions.read` | 200 item |
| POST | `/transactions` | `transactions.create` | 201 `{ id }` |
| POST | `/transactions/transfer` | `transactions.create` | 201 `{ id }` |
| POST | `/transactions/fx` | `transactions.create` | 201 `{ id }` |
| POST | `/transactions/fx/quote` | `transactions.create` | 200 quote |
| POST | `/transactions/{id}/reverse` | `transactions.create` | 201 `{ id }` |

`POST /transactions/adjustment` was removed on 2026-11-25: an `Adjustment` is posted only by the approval of an adjustment request (§7.15).

- Create and reverse require `Idempotency-Key` ([ADR 0015](adr/0015-idempotency-keys.md)).
- TenantAdmin / Adviser omit `tenantId`. SystemAdmin list/create require tenant PublicId.
- A Customer reads the transactions of their own accounts (`transactions.read`, §3.1) and writes nothing.
- Optional list filters: `accountId`, `customerId`, `type`.

**By custody mode.** The custody mode of the Account decides what a person may enter and what a body may carry. All refusals here are field 400s with no problem code.

| | External account | Custodial account |
| --- | --- | --- |
| `POST /transactions` | Every type the route accepts | Every type is refused (400 `errors.Type`): the platform writes them from its flows. The matrix and the messages are [features/custody-modes.md](features/custody-modes.md) §4.2 |
| `bookedAt` | Required | Set by the server; a body that sends it to `POST /transactions`, `/transactions/transfer`, `/transactions/fx` or `/transactions/fx/quote` → 400 `errors.BookedAt` |
| Transfer, conversion, quote | Only with another External account, else 400 `errors.ToAccountId` | Only with another Custodial account; a transfer only between accounts of one Customer (400 `errors.ToAccountId`) |
| `Buy` / `Sell` numbers | Require `security[0].price` (the executed price), accept an optional `fee`, and require `fxRate` when the instrument's quote currency differs from the Account currency; no price, rate or schedule is read ([features/external-caller-numbers.md](features/external-caller-numbers.md)) | Not entered by hand: the caller places an order (§7.12) |
| `POST /transactions/fx` | Requires both `sellAmount` and `buyAmount` and refuses `expectedRate`; the rate is derived. The quote → 400 `errors.FromAccountId` | One amount, the reference rate and `expectedRate`; the quote applies |
| Cash that may leave | Cash `SUM ≥ 0` | Every write that lowers cash is refused when it would leave available cash below 0; a transfer, a conversion and the quote also refuse cash from sales that have not settled ([features/withdrawals.md](features/withdrawals.md), [features/orders.md](features/orders.md) R45) |
| `POST /transactions/{id}/reverse` | Every source | Only source `Manual`; another source → 400 ([features/transaction-source.md](features/transaction-source.md)) |

On both custody modes: reverse of an `Adjustment` → 400 for every caller ([features/adjustment-review.md](features/adjustment-review.md) R15), and every create, quote and reverse with a leg on a closing account → 400 under the empty `errors` key ([features/account-closure.md](features/account-closure.md)).

**Item fields by source.** The item carries `source: { kind, id }`; no body or query field sets it. Every cash leg carries `role` (`Principal`, `Fee`, `Tax`, `Reinvestment`), `fee` (the fee detail on a `Fee` leg, `null` otherwise; `fee.scheduleId` is `null` for an entered fee) and `tax` (`{ kind, ratePercent, basisAmount, amount, currency }` on a `Tax` leg, `null` otherwise). `fx` is `{ baseCurrency, quoteCurrency, rate, rateAsOfDate }` when the Transaction has an FX detail, else `null` ([features/fx-conversions.md](features/fx-conversions.md) R22). Each field below is `null` on every other Transaction, and is copied to a Reversal:

| Source kind | Marks | `source.id` | Item field |
| --- | --- | --- | --- |
| `Manual` | What a person entered | `null` | — |
| `Seed` | Seed data | `null` | — |
| `Rebalance` | A rebalance trade posted before rebalances went through orders; `reference` is `null` | The rebalance handle | — |
| `BankFeed` | A deposit (`TransferIn`) | The statement line | `bankReceipt: { valueDate, reference, payerName, payerAccount }` |
| `Payment` | A payout (`TransferOut`) | The withdrawal | `payment: { valueDate, reference, payeeName, payeeAccount }` |
| `AccountClosure` | The remainder write-off (`CloseOut`) | The account closure | — |
| `InterestRun` | An interest posting | The interest run | `interest: { periodFrom, periodTo, days, accruedAmount, grossAmount }` |
| `CorporateAction` | An entitlement posting (`Split` or `Bonus`: one security leg of the quantity difference with cost 0) | The corporate action | `corporateAction: { id, kind, ratioNew, ratioHeld, exDate, recordDate, paymentDate, quantityBefore, quantityAfter }` |
| `CorporateAction` | A `Dividend` | The corporate action | `dividend` ([features/dividends.md](features/dividends.md) §8) |
| `OrderFill` | The `Buy` or `Sell` of a fill | The order | `execution: { orderId, fillId, price, tradeDate, settlementDate, brokerReference, settledAt, brokenAt }` |
| `Migration` | An `Opening` or `OpeningHoldings` of a migration batch | The migration batch | `migration: { asOfDate, previousProvider }` |
| `InKindTransfer` | The `StockIn` or `StockOut` of an in-kind transfer | The in-kind transfer | `inKindTransfer: { transferId, direction, counterpartyName, counterpartyAccount, changesBeneficialOwner, acquiredOn, settlementDate, custodianReference }` |
| `AdjustmentRequest` | An `Adjustment` | The adjustment request | `adjustment: { reason }` |

**Type-specific create routes.** When a type's create body differs from the common shape (one top-level `accountId`), that type gets its own `POST /transactions/{action}`. Each type has exactly one create route: a type with its own route is rejected by `POST /transactions` (400). Read (`GET /transactions`, `GET /transactions/{id}`) and reverse (`POST /transactions/{id}/reverse`) stay shared by every type. Every new type gets its own `POST /transactions/{action}` route, whatever its body shape (decided 2026-11-10). Existing types keep `POST /transactions` until each is moved in its own spec change ([future-plans.md](future-plans.md) §4).

Type-specific routes: `POST /transactions/transfer` for `InternalTransfer` ([features/internal-transfers.md](features/internal-transfers.md)); `POST /transactions/fx` for `FxConversion` and the read-only `POST /transactions/fx/quote` ([features/fx-conversions.md](features/fx-conversions.md)). `Adjustment` has no create route under `/transactions`: it is requested and approved under `/adjustment-requests` ([features/adjustment-review.md](features/adjustment-review.md)). The quote is a `POST` because it takes the create body and the same validation; it writes nothing, takes no `Idempotency-Key` and holds no rate. Create confirms the quoted rate with `expectedRate`; a different stored rate → 409 with `expectedRate` in `errors`.

---

### 7.8 Reports

Field rules: [features/reports.md](features/reports.md). Accepted.

Grouping by custody mode (accepted, [features/custody-mode-grouping.md](features/custody-mode-grouping.md)) changes the content of the two PDFs only: a Custody column in both, subtotals per custody mode in Adviser Balances, a note on External rows in Customer Transactions. The routes do not change.

Bank reconciliation (accepted, [features/bank-reconciliation.md](features/bank-reconciliation.md) R22–R25) adds a third scope, `Tenant`, with policies `reports.tenant.read` and `reports.tenant.create` for TenantAdmin only, and the report `bank-reconciliation`. "By scope" in the table below includes it; Adviser and Customer get 403 on a Tenant-scoped report. The routes do not change.

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/reports/catalog` | `tenants.manage` | 200 every catalog row, including inactive, ordered by code. No `granted` |
| GET | `/tenants/{tenantId}/reports` | `tenants.manage` | 200 active catalog rows for that tenant, each item includes `granted`. Inactive catalog rows are omitted |
| POST | `/tenants/{tenantId}/reports` | `tenants.manage` | 201 `{ id }` the first time, 204 when the grant already exists. Inactive catalog report → 404 and no row |
| DELETE | `/tenants/{tenantId}/reports/{reportId}` | `tenants.manage` | 204 when removed or already absent |
| GET | `/reports` | `reports.customer.read`, `reports.book.read` or `reports.tenant.read` | 200 list (granted + active only) |
| PUT | `/reports/{id}` | `tenants.manage` | 204. Body may include `isActive` and/or `allowedTriggers`. Neither field → 400. Unknown trigger → 400. `isActive` false cascades revoke of every TenantReports row (schedule `IsActive` unchanged). Removing `"scheduled"` from `allowedTriggers` disables every schedule for that report (rows stay; no grant revoke) |
| POST | `/reports/schedules` | by report scope (`*.create`) | 201 `{ id }` |
| GET | `/reports/schedules` | by scope | 200 list |
| PUT | `/reports/schedules/{id}` | by report scope | 204 |
| POST | `/reports/schedules/{id}/disable` | by report scope | 204 |
| POST | `/reports/request` | by report scope (`*.create`) | 202 `{ id }` |
| GET | `/reports/executions` | by scope | 200 paged list. Optional `reportId` filters to one report |
| GET | `/reports/executions/{id}` | by that execution’s report scope | 200 |
| GET | `/reports/executions/{id}/download` | by that execution’s report scope | 200 `application/pdf` |

Customer has `reports.customer.read` only. TenantAdmin and Adviser have all four. SystemAdmin has none of the four and cannot call `GET /reports`, request, schedule, or download. Bad parameters → 400. Unknown report, not granted, inactive catalog row, or out of scope → 404 on those firm routes. Policy fail → 403.

`GET /reports/catalog` is `tenants.manage`. It returns every catalog row, ordered by code, including inactive rows, with no `granted`. Other roles → 403. It is not `GET /reports`.

`GET /tenants/{tenantId}/reports` is `tenants.manage`. It returns the active catalog for that tenant, ordered by code, each with `granted`. Platform-inactive rows are omitted. A new tenant is not auto-granted. Unknown tenant → 404. Other roles → 403. Granting a platform-inactive report is 404 and inserts no row. Field rules are in [features/reports.md](features/reports.md).

BFF allow-list adds the `/reports` prefix. `GET /reports/catalog` and `PUT /reports/{id}` sit on that prefix and reject non-SystemAdmin callers with 403. The Adviser Portal does not call them.

Back Office tenant configuration lists the active catalog and calls the existing grant `POST` and revoke `DELETE`. It does not show a platform-inactive report. Catalog `IsActive` and `AllowedTriggers` are written with `PUT /reports/{id}` (`tenants.manage`). The platform page lists `GET /reports/catalog`. Disabling cascades a revoke of every tenant grant for that report. Scalar can call the same verbs.

---

### 7.9 Fees

`/fees` is a namespace prefix, like `/users`: there is no `GET /fees`. Fee configuration and calculation resources hang under it. Fee transactions do not: a standalone fee type gets its own `POST /transactions/{action}` route (§7.7).

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/fees/trade-schedules` | `fees.read` | 200 envelope |
| GET | `/fees/trade-schedules/{id}` | `fees.read` | 200 item |
| POST | `/fees/trade-schedules` | `fees.manage` | 201 `{ id }` |
| PUT | `/fees/trade-schedules/{id}` | `fees.manage` | 204 |
| DELETE | `/fees/trade-schedules/{id}` | `fees.manage` | 204 |
| POST | `/fees/trade-schedules/preview` | `fees.read` | 200 fee preview ([features/trade-fees.md](features/trade-fees.md) R30–R33; no write) |

Field rules: [features/trade-fees.md](features/trade-fees.md). Update or delete of a schedule row in effect → 409 `fee_schedule_in_effect`. The `role` and `fee` of a cash leg are in §7.7.

### 7.10 Interest

`/interest` is a namespace prefix, like `/fees`: there is no `GET /interest`. Interest postings are Transactions (§7.7), never entered by hand on a Custodial account.

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/interest/rate-schedules` | `interest-rates.read` | 200 envelope (optional `currency`) |
| GET | `/interest/rate-schedules/{id}` | `interest-rates.read` | 200 item |
| POST | `/interest/rate-schedules` | `interest-rates.manage` | 201 `{ id }` |
| PUT | `/interest/rate-schedules/{id}` | `interest-rates.manage` | 204 |
| DELETE | `/interest/rate-schedules/{id}` | `interest-rates.manage` | 204 |
| GET | `/interest/runs` | `interest-runs.read` | 200 envelope (SystemAdmin must send `tenantId`; filters `trigger`, `from`, `to`) |
| GET | `/interest/runs/{id}` | `interest-runs.read` | 200 item |
| POST | `/interest/runs` | `interest-runs.start` | 201 `{ id }` (`{ tenantId, asOfDate }`; runs at once) |

Field rules: [features/interest-run.md](features/interest-run.md). Update or delete of a rate schedule row in effect → 409 `interest_schedule_in_effect`. `asOfDate` after yesterday (UTC) → 400 `errors.AsOfDate` unless `Interest:AllowFutureAsOfDates` is on. No `Idempotency-Key` on these routes.

### 7.11 Corporate actions

A corporate action is an event of one Instrument of the caller's Tenant. Its entitlement postings are Transactions (§7.7), never entered by hand on a Custodial account. No caller names a tenant: `tenantId` in a query or body → 400. `{id}` is a GUID.

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/corporate-actions` | `corporate-actions.read` | 200 envelope (filters `status`, `instrumentId`, `origin`, `kind`) |
| GET | `/corporate-actions/{id}` | `corporate-actions.read` | 200 item |
| GET | `/corporate-actions/{id}/entitlements` | `corporate-actions.read` | 200 envelope (filter `status`; an Adviser sees assigned Customers' accounts only) |
| POST | `/corporate-actions` | `corporate-actions.manage` | 201 `{ id }` |
| PUT | `/corporate-actions/{id}` | `corporate-actions.manage` | 204 |
| POST | `/corporate-actions/{id}/cancel` | `corporate-actions.manage` | 204 |
| POST | `/corporate-actions/{id}/apply` | `corporate-actions.manage` | 204 |
| POST | `/corporate-actions/{id}/pay-in-cash` | `corporate-actions.manage` | 204 (a dividend waiting for shares) |
| GET | `/corporate-action-announcements` | `tenants.manage` | 200 envelope (filters `connection`, `status`) |

Field rules: [features/corporate-actions.md](features/corporate-actions.md). No `Idempotency-Key`: the status and `rowVersion` make a repeat a 400 or a 409 `stale_row_version`. A served instrument takes no hand entry (400 `errors.InstrumentId`); a `Feed` action cannot be changed by hand (400 `errors.Origin`). No new problem code.

Dividends ([features/dividends.md](features/dividends.md) §8): kind `Dividend` with its terms in place of a ratio on create and update; `reinvestmentPrice` only from the feed. The item carries `dividend`, `payment` and `election`; the entitlement item the dividend fields. `POST /corporate-actions/{id}/pay-in-cash` releases an accepted election whose shares did not arrive. A dividend's entitlements are posted as `Dividend` Transactions (§7.7) once its receipt is matched (§7.17).

### 7.12 Orders

An order is an instruction to buy or sell a quantity of one Instrument for one Custodial account at the Tenant's broker. It is not a Transaction; each fill the broker reports is posted as a `Buy` or `Sell` (§7.7). No caller names a tenant: `tenantId` in a query or body → 400. `{id}` is a GUID.

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/orders` | `orders.read` | 200 envelope (filters `accountId`, `customerId`, `instrumentId`, `side`, `status`, `pendingFill`) |
| GET | `/orders/{id}` | `orders.read` | 200 item with its fills |
| POST | `/orders` | `orders.place` | 201 `{ id }` |
| POST | `/orders/preview` | `orders.place` | 200 preview (writes nothing) |
| POST | `/orders/{id}/cancel` | `orders.place` | 204 |
| GET | `/broker-connections` | `tenants.manage` | 200 envelope |
| GET | `/execution-reports` | `tenants.manage` | 200 envelope (filters `connection`, `status`) |

Field rules: [features/orders.md](features/orders.md). `POST /orders` requires `Idempotency-Key` ([ADR 0015](adr/0015-idempotency-keys.md)); cancel takes `rowVersion` and no key, and a stale one is 409 `stale_row_version`. The body carries no price: the platform sets a price limit from the latest stored price, reserves the cash of a buy or the quantity of a sell, and the `worker` sends the order. Nothing is sent to the broker in the request. A Customer reads own orders and writes nothing. No new problem code.

Trade settlements and broken trades ([features/trade-settlement.md](features/trade-settlement.md)):

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/trade-settlements` | `trade-settlements.manage` | 200 envelope (filters `status`, `clientMoneyAccountId`, `open`) |
| GET | `/trade-settlements/{id}` | `trade-settlements.manage` | 200 item with its fills and payments |
| POST | `/trade-settlements/{id}/pay` | `trade-settlements.manage` | 204 |
| POST | `/trade-settlements/{id}/set-aside` | `trade-settlements.manage` | 204 |
| POST | `/orders/{id}/fills/{fillId}/resolve-break` | `transactions.adjust` | 204 |

A fill item carries `brokerSettlementId`, `tradeSettlementId` and `break`, and its `status` may be `Broken`. The bank reconciliation run carries `openSettlements`. Writes take `rowVersion` (409 `stale_row_version`) and no `Idempotency-Key`. No new problem code.

Rebalances ([features/rebalances.md](features/rebalances.md)):

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/rebalances` | `rebalances.read` | 200 envelope (filters `accountId`, `customerId`, `portfolioId`, `status`, `open`) |
| GET | `/rebalances/{id}` | `rebalances.read` | 200 item with its targets, lines and orders |
| POST | `/rebalances/{id}/confirm` | `rebalances.confirm` | 204 |
| POST | `/rebalances/{id}/cancel` | `rebalances.confirm` | 204 |

The order item carries `rebalanceId`, and `GET /orders` has the filter `rebalanceId`. `POST /orders/{id}/cancel` of a rebalance order → 400 `errors.Status`. Confirm and cancel take `rowVersion` (409 `stale_row_version`) and no `Idempotency-Key`. Nothing is sent to the broker in the request: the advance step of `broker-sync:{connection}` places the later waves. Five problem codes (§4.2).

### 7.13 Migration batches

Field rules: [features/migration-batches.md](features/migration-batches.md).

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/migration-batches` | `migration-batches.manage` | 200 envelope (filter `status`) |
| GET | `/migration-batches/{id}` | `migration-batches.manage` | 200 item with control, computed and funding totals, funding lines, entries and `validation` |
| POST | `/migration-batches` | `migration-batches.manage` | 201 `{ id }` |
| PUT | `/migration-batches/{id}` | `migration-batches.manage` | 204 |
| POST | `/migration-batches/{id}/submit` | `migration-batches.manage` | 204 |
| POST | `/migration-batches/{id}/return` | `migration-batches.manage` | 204 |
| POST | `/migration-batches/{id}/cancel` | `migration-batches.manage` | 204 |
| POST | `/migration-batches/{id}/post` | `migration-batches.manage` | 204 |

An id in the body that is not in the Tenant → 400 at its place in the body. Submit and post of a batch with an issue → 400 `migration_batch_not_valid`. Post by the person who submitted → 400 `errors.Status`. Writes take `rowVersion` (409 `stale_row_version`) and no `Idempotency-Key`. A batch is funded by matching statement lines to it (§7.17).

### 7.14 In-kind transfers, custody movements and custodian reconciliations

Field rules: [features/in-kind-transfers.md](features/in-kind-transfers.md).

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/in-kind-transfers` | `in-kind-transfers.read` | 200 envelope (filters `accountId`, `customerId`, `instrumentId`, `direction`, `status`) |
| GET | `/in-kind-transfers/{id}` | `in-kind-transfers.read` | 200 item |
| POST | `/in-kind-transfers` | `in-kind-transfers.request` | 201 `{ id }` (requires `Idempotency-Key`) |
| POST | `/in-kind-transfers/{id}/approve`, `…/reject`, `…/set-aside` | `in-kind-transfers.approve` | 204 |
| POST | `/in-kind-transfers/{id}/cancel` | `in-kind-transfers.request` | 204 |
| GET | `/custody-movements` | `custody-movements.manage` | 200 envelope (filters `status`, `direction`, `kind`, `instrumentId`, `safekeepingAccountId`) |
| GET | `/custody-movements/{id}` | `custody-movements.manage` | 200 item |
| POST | `/custody-movements/{id}/ignore`, `…/reopen` | `custody-movements.manage` | 204 |
| GET | `/custodian-reconciliations/overview` | `custodian-reconciliations.manage` | 200 envelope |
| GET | `/custodian-reconciliations` | `custodian-reconciliations.manage` | 200 envelope (filters `safekeepingAccountId`, `status`, `from`, `to`) |
| GET | `/custodian-reconciliations/{id}` | `custodian-reconciliations.manage` | 200 item with every line |
| POST | `/custodian-reconciliations` | `custodian-reconciliations.manage` | 201 `{ id }` |

Approve or reject by the requester → 400 `errors.Status`. Actions take `rowVersion` (409 `stale_row_version`) and no `Idempotency-Key`. No new problem code. Cross-tenant or wrong-collection id → 404.

### 7.15 Adjustment requests

Field rules: [features/adjustment-review.md](features/adjustment-review.md).

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/adjustment-requests` | `transactions.adjust` | 200 envelope (filters `status`, `accountId`, `customerId`) |
| GET | `/adjustment-requests/{id}` | `transactions.adjust` | 200 item |
| POST | `/adjustment-requests` | `transactions.adjust` | 201 `{ id }` (requires `Idempotency-Key`) |
| POST | `/adjustment-requests/{id}/approve`, `…/reject`, `…/cancel` | `transactions.adjust` | 204 |

The request body is: optional signed `amount`, optional `security` `{ instrumentId, quantityChange, costChange }`, required `reason` and `memo`; no `bookedAt`, `tenantId` or currency ([features/adjustments.md](features/adjustments.md)). An External account → 400 `errors.AccountId`. Approve posts the `Adjustment` in the same database transaction and may answer any 400 of the adjustment itself; the request then stays `Requested`. Approve or reject by the requester → 400 `errors.Status`. Actions take `rowVersion` (409 `stale_row_version`) and no `Idempotency-Key`. No new problem code. Cross-tenant or wrong-collection id → 404.

### 7.16 FX rates

Field rules: [features/market-data-history.md](features/market-data-history.md) R20–R22.

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/fx-rates?base={code}&quote={code}` | Authenticated | 200 envelope of `{ date, base, quote, rate, source, fetchedAt }`, newest first |

Rates are platform reference data with no Tenant, read by any authenticated caller as `GET /currencies`. `base` and `quote` are required catalog codes (active or not), compared without regard to case; otherwise 400 `errors.Base` / `errors.Quote`; the same code twice → 400 `errors.Quote`. The directed pair only: the inverse pair is not used. Optional `from` and `to` (`yyyy-MM-dd`, inclusive; not a date or `from` after `to` → 400 `errors.From` / `errors.To`) and paging. No rows is an empty page. No write route.

### 7.17 Statement lines

Resource `/statement-lines` (`statement-lines.manage`, TenantAdmin only). Field rules: [features/deposit-matching.md](features/deposit-matching.md), and for each kind of match the spec in the table.

| Method | Route | Body | Success |
| --- | --- | --- | --- |
| GET | `/statement-lines` | Filters `status`, `currency`, `clientMoneyAccountId`, `direction` (`Credit`, `Debit`; else 400 `errors.Direction`), `kind` (`Transfer`, `Interest`; else 400 `errors.Kind`) | 200 envelope |
| GET | `/statement-lines/{id}` | — | 200 item |
| POST | `/statement-lines/{id}/match` | `{ accountId, rowVersion, note? }`; or `migrationBatchId` or `corporateActionId` in place of `accountId` | 204 |
| POST | `/statement-lines/{id}/ignore`, `…/request-return`, `…/unmatch` | `{ note, rowVersion }` | 204 |
| POST | `/statement-lines/{id}/reopen` | `{ rowVersion }` | 204 |

- A stale `rowVersion` → 409 `stale_row_version`. No `Idempotency-Key` on these actions. No problem code of its own.
- A match to a migration batch funds it ([features/migration-batches.md](features/migration-batches.md)). A match to a dividend is its receipt, also needs `corporate-actions.manage`, and cannot be unmatched ([features/dividends.md](features/dividends.md) §8).
- `request-return` also creates the return payment the bank sends; `reopen` of a `ReturnRequested` line is refused (400 `errors.Status`) once the bank was asked. `ignore` (from `Unmatched`) and `reopen` (from `Ignored`) apply to debit lines too. A recalled deposit is reversed by the flow, with source `BankFeed` ([features/bank-returns.md](features/bank-returns.md)).
- The item carries `kind`, `relatedLineId`, `withdrawalId`, `returnReason`, `tradeSettlementId`, `migrationBatchId` and `returnPayment: { status, attempts, lastFailure }` (`null` without a return payment). `match.accountId`, `match.customerId` and `match.transactionId` are `null` on a match with no Transaction. The statuses, unmatched reasons and match methods are listed in [domain-model.md](domain-model.md) §8.9.

### 7.18 Valuations

Field rules: [features/valuation-run.md](features/valuation-run.md).

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/accounts/{id}/valuations` | `accounts.read` | 200 envelope of `{ date, currency, cash, holdingsValue, total, complete, unvaluedCount }`, newest day first |
| GET | `/accounts/{id}/valuations/{date}` | `accounts.read` | 200 item: the item of §7.6 valuation plus `runId` and `valuedAt` |
| GET | `/valuation-runs` | `valuation-runs.read` | 200 envelope (SystemAdmin must send `tenantId`; filters `trigger`, `from`, `to`) |
| GET | `/valuation-runs/{id}` | `valuation-runs.read` | 200 item |
| POST | `/valuation-runs` | `valuation-runs.start` | 201 `{ id }` (`{ tenantId, throughDate }`; runs at once) |

- A stored valuation is written only by the valuation run and never changes; no route writes or deletes one.
- `from` and `to` (`yyyy-MM-dd`, inclusive) on the account series filter the day; on the runs they filter the through date. Not a date, or `from` after `to` → 400 `errors.From` / `errors.To`. `{date}` not a date → 400 `errors.Date`; a day not stored → 404. An External account, or one not valued yet, answers an empty page.
- `throughDate` missing, not a date, or after the last final day (today minus two days, UTC) → 400 `errors.ThroughDate`; there is no option for a later date. An unknown `trigger` → 400 `errors.Trigger`. A TenantAdmin never sends `tenantId` (400).
- No `Idempotency-Key`: a repeated run stores nothing twice and answers a run with zero counts. No problem code of its own.

**Read for people** ([features/valuation-history.md](features/valuation-history.md)):

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/net-worth/funds-under-management?from=&to=` | `accounts.read` | 200 `{ reportingCurrency, from, to, days }`: one day per calendar day, oldest first, each `{ date, currencies, total, complete, accounts, incompleteAccounts, missingAccounts }` |

- The scope is that of `GET /net-worth`: the firm, an Adviser's Customers now, a Customer's own accounts; a SystemAdmin must send `tenantId`. Custodial accounts of any status; External accounts are never counted.
- `to` defaults to the last final day and may not be after it; `from` defaults to `to` minus 89 days; at most 366 days. Not a date, `to` too late, `from` after `to`, or too long → 400 `errors.From` / `errors.To`. No paging.
- A currency line is `{ currency, total, status, fx, value }` with `status` `Folded`, `NoRate` or `StaleRate`: the fold into the Tenant's reporting currency at the stored rate of that day, with the age limit of a valuation. A currency that is not folded, a missing account and an incomplete stored valuation make the day incomplete; never an error.
- `GET /net-worth` and `GET /users/customers/{id}/net-worth` carry a top-level `folded` `{ currency, total, complete, currencies }` with the same line shape, at today's rates.
- The Adviser Balances Report takes an optional `date` on a manual request (§7 reports; features/reports.md §9.2).

---

## 8. Explicitly out (until a later phase promotes them)


- `/auth/login`, `/auth/refresh`, `/auth/logout` on `webapi`
- Password grant
- A second OIDC client in Phase 1
- Currency write APIs, per-tenant currency allow-lists, FX
- `/holdings`, `/dashboard`
- Invitation, forgot-password, self-registration, MFA, external IdP
- Subdomain tenant resolution; Header-based SystemAdmin tenant switching
- Internal `int` ids in routes or JSON
- A catch-all `GET/POST /users`; fake children such as `/users/{id}/advisers`
- Putting `/tenants`, `/currencies`, or ledger routes under `/users`

Further ledger routes wait for their Feature Specs. The session stack and these conventions stay.

---

## 9. Still open in Phase 1

List page size is locked: tenants envelope (page 1 / size 20 / max 100), reused by people lists. `enabledOnly` is locked for currencies and people collections.

Locked in identity-auth: `AspNetUsers.UserName` = Domain `Users.PublicId`; uniform login failure; hosted login is Razor Pages at `/login`; access 15 minutes; refresh 14 days absolute; JWT claims `sub` / `email` / `role` / `tenant_id` / `tenant_code`.

---

## 10. Change log

One row per change. The text of each change is in git (`git log -p -- docs/api-design.md`); the last commit of the amendment form is the tag `docs-pre-consolidation`.

| Date | Change |
| --- | --- |
| 2026-09-11 – 09-14 | First draft: two HTTP surfaces, OpenIddict default paths, the `/users` namespace; identity-auth locks; currencies; §4.1 shared disabled error; people field rules; `GET /tenants/by-code/{code}` |
| 2026-09-20 – 09-25 | `/instruments` (§7.5); `isEnabled` → `isActive`; `/accounts` (§7.6); `/transactions` and `cashBalance` (§7.7) |
| 2026-10-11 – 10-26 | Security types and the item shape with `cash` + `security`; net worth; reports (§7.8) |
| 2026-11-02 | Portfolios |
| 2026-11-10 – 11-11 | Type-specific create routes (§7.7); internal transfers; FX conversions |
| 2026-11-13 – 11-16 | Per-request status check; §4.2 problem codes; UTC instants (§2); trade fees (§7.9); §3.1 role permission table; ledger write guards (§4) |
| 2026-11-17 – 11-19 | Account custody mode and custody modes (§7.6, §7.7); tenant custody modes (§7.3); External caller numbers (§7.7); grouping by custody mode (§7.8); simulator (§1, §3.1); transaction source and adjustments (§7.7) |
| 2026-11-20 – 11-21 | Client money accounts (§7.3, §7.6); statement lines and deposit matching (§7.17); nominated bank accounts and withdrawals (§6); bank returns; bank reconciliation (§7.8) |
| 2026-11-22 | Account closure (§7.6); interest (§7.10, §7.4); simulated instruments (§7.5); corporate actions (§7.11) |
| 2026-11-23 | Orders, trade settlements and rebalances (§7.12) |
| 2026-11-24 | Migration batches (§7.13); in-kind transfers (§7.14); dividends (§7.11) |
| 2026-11-25 | Adjustment requests (§7.15); `POST /transactions/adjustment` removed; price and FX rate history (§7.5, §7.16) |
| 2026-11-26 | The amendments were folded into §7. §1, §3 and §5 now describe the portal clients as [ADR 0016](adr/0016-bff-for-first-party-portals.md) and the BFF specs define them. §7.17 gathers the statement line notes. No rule changed |
| 2026-11-26 | Account valuation (§6, §7.6): `GET /accounts/{id}/valuation?date=` with `accounts.read`. No new permission or problem code |
| 2026-11-26 | Valuation run (§3.1, §6, §7.6, §7.18): `valuation-runs.read` and `valuation-runs.start`; `/valuation-runs`; `GET /accounts/{id}/valuations[/{date}]`; `valuedThrough` on the account item. No problem code |
| 2026-11-26 | Valuation history (§6, §7.18): `GET /net-worth/funds-under-management`; `folded` on the net-worth reads; `date` of the Adviser Balances Report. No permission or problem code |
| 2026-11-27 | Docs caught up with the code: §1 diagram shows the BFF between the browser and `webapi`; §2 CORS is none on `webapi` |
