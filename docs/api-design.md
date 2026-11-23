---
title: API design
status: accepted
language: en
created: 2026-09-11
updated: 2026-11-23
related:
  - README.md
  - glossary.md
  - function-plan.md
  - architecture.md
  - domain-model.md
  - database-design.md
  - adr/0014-openiddict-authorization-code-pkce.md
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
  ├── redirect / callback / token / revocation / logout  →  identity (src/IdentityHost)
  └── Bearer resource calls                              →  webapi (src/Web)
```

| Host | Aspire name | Role |
| --- | --- | --- |
| `src/IdentityHost` | `identity` | Authorization server: OpenIddict default paths + hosted login |
| `src/Web` | `webapi` | Resource API: Minimal API → MediatR; Scalar; Bearer validation only |

The portal (`adviser-portal`) does not issue tokens. It redirects to `identity`, stores access / refresh after the callback, and calls `webapi` with Bearer.

A third process, the simulator (`simulator`, [features/simulator.md](features/simulator.md) §8, landed 2026-11-19), has its own HTTP surfaces: a control surface for a person behind sign-in on client `simulator`, and a party surface for the platform behind an API key on an internal port. Neither is a `webapi` resource, and the conventions in this document do not apply to them.

Phase 1 registers one public OIDC client: `adviser-portal`. IdentityHost allows TenantAdmin and Adviser on that client (SystemAdmin removed by identity-auth amendment 2026-11-11; SystemAdmin signs in through `back-office`). A Customer with a correct password does not receive an authorization code for it. Customer Portal is a later second client; it is not registered in Phase 1. Adviser-management resource routes stay 403 for Customer.

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
| CORS | Loose in Development; tighten in production without blocking Phase 1 |
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
| SystemAdmin | Hosted login on `back-office`, no tenantCode. `adviser-portal` issues no code (identity-auth amendment 2026-11-11) | `/tenants` manage + `GET /tenants/by-code/{code}`, `/users/tenant-admins`, `/users/me`, `/currencies` |
| TenantAdmin | Hosted login on `adviser-portal` + tenantCode | `/users/advisers`, tenant `/users/customers`, `GET /tenants/by-code/{own}`, `/users/me`, `/currencies` |
| Adviser | Hosted login on `adviser-portal` + tenantCode | Assigned `/users/customers`, `GET /tenants/by-code/{own}`, `/users/me`, `/currencies` |
| Customer | Login principal; `adviser-portal` issues no code. Tokens wait for `customer-portal` | `/users/me` and `/currencies` only, on a client that allows Customer. `/users/advisers`, `/users/customers`, `/users/tenant-admins`, `/tenants` including by-code → 403 |

Disable / enable for people: `POST /{id}/disable`, `POST /{id}/enable` (`Disabled` ↔ `Active`). Not a physical delete. Disabling an Adviser requires assigned non-disabled Customers to be handled first. Disabling the last TenantAdmin is allowed in Phase 1 (known gap).

Disabling a tenant does **not** bulk-update `User.Status`. Person status is independent; login checks both. After a tenant is disabled, that Code must not complete login, and existing refresh fails once the outbox delivers `TenantDisabled`. Its people get 403 from `webapi` at once (identity-auth amendment 2026-11-13).

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
| `accounts.read` | ✓ | ✓ | ✓ | ✓ | [accounts](features/accounts.md); Customer: Customer Portal amendment 2026-11-02 |
| `accounts.create` | ✓ | ✓ | ✓ | | [accounts](features/accounts.md) |
| `accounts.manage` | ✓ | ✓ | ✓ | | [accounts](features/accounts.md) |
| `transactions.read` | ✓ | ✓ | ✓ | ✓ | [posting](features/posting.md); Customer: Customer Portal amendment 2026-11-02 |
| `transactions.create` | ✓ | ✓ | ✓ | | [posting](features/posting.md) |
| `transactions.adjust` | | ✓ | | | [adjustments](features/adjustments.md) (accepted 2026-11-19) |
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
| `corporate-actions.read` | | ✓ | ✓ | | [corporate-actions](features/corporate-actions.md) (accepted 2026-11-22) |
| `corporate-actions.manage` | | ✓ | | | [corporate-actions](features/corporate-actions.md) (accepted 2026-11-22) |
| `orders.read` | | ✓ | ✓ | ✓ | [orders](features/orders.md) (accepted 2026-11-23) |
| `orders.place` | | ✓ | ✓ | | [orders](features/orders.md) (accepted 2026-11-23) |
| `trade-settlements.manage` | | ✓ | | | [trade-settlement](features/trade-settlement.md) (accepted 2026-11-23) |
| `rebalances.read` | | ✓ | ✓ | | [rebalances](features/rebalances.md) (accepted 2026-11-23) |
| `rebalances.confirm` | | ✓ | ✓ | | [rebalances](features/rebalances.md) (accepted 2026-11-23) |
| `migration-batches.manage` | | ✓ | | | [migration-batches](features/migration-batches.md) (accepted 2026-11-24) |
| `portfolios.read` | ✓ | ✓ | ✓ | | [portfolios](features/portfolios.md) |
| `portfolios.manage` | ✓ | ✓ | ✓ | | [portfolios](features/portfolios.md) |
| `reports.customer.read` | | ✓ | ✓ | ✓ | [reports](features/reports.md) |
| `reports.customer.create` | | ✓ | ✓ | ✓ | [reports](features/reports.md); Customer: Customer Portal amendment 2026-11-02 |
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
| Tenant disabled or Status ≠ Active, with a still-valid access token | 403 on every route that needs a permission (identity-auth amendment 2026-11-13) | `webapi` current-user load |
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

Status of this section: `accepted` (2026-11-13). The build contract for it is [features/portfolios.md](features/portfolios.md), amendment 2026-11-13, part 1. Landed 2026-11-13: every code in the registry below is a `ProblemException` subclass.

**Why.** One HTTP status already carries several meanings. `webapi` returns 409 for a stale `rowVersion`, a reused `Idempotency-Key`, a changed FX rate, a report that is not ready, and a portfolio already assigned to the account. `POST /transactions/fx` alone can return two different 409s today, and only the first has a field to tell them apart (`errors.expectedRate`). The portfolios amendment 2026-11-13 adds a sixth. A client that branches on the status alone shows the wrong message as soon as one route can return two of them. A **problem code** names the meaning. The status stays the category.

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
| `portfolio_shared` | 409 | The caller is an Adviser and the portfolio is a Shared portfolio. | — | [features/portfolios.md](features/portfolios.md) amendment 2026-11-13 |
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

**Phase 1 client**

| Item | Value |
| --- | --- |
| ClientId | `adviser-portal` |
| Type | Public client + PKCE |
| Grant | Authorization code + refresh. Password grant off |
| Redirect | `{portalOrigin}/callback` |
| Post-logout | `{portalOrigin}/` |

**Out (authorization server)**

- Custom `POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout` as a token issuer
- Resource Owner Password Credentials
- The later `customer-portal` client
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
| Instruments | `/instruments` | list / get / create / update / disable / enable | `instruments.read` / `instruments.create` / `instruments.manage` |
| Accounts | `/accounts` | list / get / create / update / close / reopen; `GET …/cash-balance`; `GET …/deposit-instructions` (features/client-money-accounts.md); sub-resource `…/closures`: request (`Idempotency-Key`) / list / get (features/account-closure.md); `GET …/interest-accruals` (features/interest-run.md) | `accounts.read` / `accounts.create` / `accounts.manage`; closures `account-closures.read` / `.request` |
| Transactions | `/transactions` | list / get / create / reverse | `transactions.read` / `transactions.create` |
| Net worth | `/net-worth`, `/users/customers/{id}/net-worth` | GET. See features/net-worth.md; `breakdowns.byCustodyMode` in features/custody-mode-grouping.md | `accounts.read` |
| Portfolios | `/portfolios`, `/accounts/{id}/portfolio`, `/accounts/{id}/rebalance` | list / get / create / update / disable; assign / unassign / rebalance. See features/portfolios.md | `portfolios.read` / `portfolios.manage` + `transactions.create` |
| Fees | `/fees/trade-schedules` (namespace `/fees`) | list / get / create / update / delete. See features/trade-fees.md | `fees.read` / `fees.manage` |
| Interest | `/interest/rate-schedules`, `/interest/runs` (namespace `/interest`) | rate schedules: list / get / create / update / delete; runs: list / get / create (start a run). See features/interest-run.md | `interest-rates.read` / `.manage`; `interest-runs.read` / `.start` |
| Corporate actions | `/corporate-actions` | list / get / create / update; cancel / apply; entitlements. See features/corporate-actions.md | `corporate-actions.read` / `corporate-actions.manage` |
| Orders | `/orders` | list / get / create (`Idempotency-Key`) / preview; cancel; resolve the break of a fill (`transactions.adjust`). No PUT: an order never changes. See features/orders.md and features/trade-settlement.md | `orders.read` / `orders.place` |
| Trade settlements | `/trade-settlements` | list / get; pay again / set aside. No create: the `worker` creates them from the broker's `Settlement` report. See features/trade-settlement.md | `trade-settlements.manage` |
| Rebalances | `/rebalances` | list / get; confirm / cancel. No create: `POST /accounts/{id}/rebalance`, assign and the rebalance job propose. See features/rebalances.md | `rebalances.read` / `rebalances.confirm` |
| Migration batches | `/migration-batches` | list / get / create / replace (PUT); submit / return / cancel / post. Funding is `POST /statement-lines/{id}/match` with `migrationBatchId`. See features/migration-batches.md | `migration-batches.manage` |
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

Item: `{ code, name, decimalPlaces, isActive }`. Sorted by `code`. No “disabled-only” query. No pagination. No write API. No `GET /currencies/{code}`. Column/JSON amendment 2026-09-21: `IsEnabled` → `IsActive`.

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

- POST body: `name`, `code`, `reportingCurrency`
- PUT body: `name`, `reportingCurrency`, `rowVersion`
- `code` is immutable after create (login key). Reporting currency may change in this phase (no ledger balances keyed on it yet).
- After disable, that Code must not complete login; existing refresh fails once the outbox delivers `TenantDisabled`. Its people get 403 from `webapi` at once. Person `Status` is not bulk-updated.
- Field rules: [features/tenants.md](features/tenants.md).
- Amendment 2026-11-18 (tenant custody modes, accepted, landed; field rules in [features/tenant-custody-modes.md](features/tenant-custody-modes.md)): POST body adds required `allowedCustodyModes` (array of `External` / `Custodial`, non-empty, no repeats); PUT body may carry it (omitted = unchanged); the item on list, get and by-code carries it. Bad shape → 400 `errors.AllowedCustodyModes`. `POST /accounts` with a custody mode the Tenant does not allow → 400 `errors.CustodyMode`. No new route, policy or problem code.
- Amendment 2026-11-20 (client money accounts, accepted; field rules in [features/client-money-accounts.md](features/client-money-accounts.md)): sub-resource `/tenants/{tenantId}/client-money-accounts` with list, get, create (201 `{ id }`), `POST …/{id}/disable` and `POST …/{id}/enable` (`rowVersion`), all `tenants.manage`. No PUT. One active row per currency and an account number active on one row only, else 400 `errors.Currency` / `errors.AccountNumber`. The tenant item on list, get and by-code carries `clientMoneyCurrencies`. No new policy or problem code.
- Amendment 2026-11-20 (bank feed and deposit matching, accepted; field rules in [features/deposit-matching.md](features/deposit-matching.md)): creating a client money account requires `bankConnection`, a name from configuration (else 400 `errors.BankConnection`); the item carries `bankConnection` and `bankFeed` (`{ lastSucceededAt, lastFailedAt, lastFailure }` or `null`). `GET /bank-connections` (`tenants.manage`) lists the configured connections without secrets.
- Amendment 2026-11-23 (orders and settlement, accepted; field rules in [features/orders.md](features/orders.md) R3, R4): POST and PUT bodies take an optional `brokerConnection`, a name from configuration (else 400 `errors.BrokerConnection`); omitted or `null` on PUT leaves it unchanged. The item carries `brokerConnection` (`null` when none). `GET /broker-connections` (`tenants.manage`) lists the configured names with the state of each execution report feed, and `GET /execution-reports` (`tenants.manage`) lists the stored reports.

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

- Amendment 2026-11-22 (interest run, accepted; field rules in [features/interest-run.md](features/interest-run.md) R1–R8): sub-resource `GET` / `PUT /users/customers/{customerId}/tax-profile` (`customers.manage` or `customers.manage-own`, scope as every `/users/customers` route; a Customer → 403) and `GET /users/me/tax-profile` (`users.me`; a caller who is not a Customer → 404). The item is `{ taxResidency, irdNumberHeld, rwtRatePercent, withholding: { kind, ratePercent, basis }, updatedBy, updatedAt, rowVersion }`; `PUT` takes the three attributes and `rowVersion` (`null` for the first write) and answers 204; stale → 409 `stale_row_version`. The IRD number itself is never stored.
- Amendment 2026-11-22 (corporate actions, accepted; field rules in [features/corporate-actions.md](features/corporate-actions.md) R26, R27, R32, R33, R37): source kind `CorporateAction` marks an entitlement posting (`Split` or `Bonus` written by the platform, one security leg of the quantity difference with cost 0); it is not hand-reversible. The item gains `corporateAction` `{ id, kind, ratioNew, ratioHeld, exDate, recordDate, paymentDate, quantityBefore, quantityAfter }` on an entitlement posting and its Reversal, `null` otherwise. A hand `Split`, `Bonus` or `Scrip` on a Custodial account stays refused (400 `errors.Type`).

### 7.5 Instruments

Field rules: [features/instruments.md](features/instruments.md).

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/instruments` | `instruments.read` | 200 envelope |
| GET | `/instruments/{id}` | `instruments.read` | 200 item |
| POST | `/instruments` | `instruments.create` | 201 `{ id }` |
| PUT | `/instruments/{id}` | `instruments.manage` | 204 |
| POST | `/instruments/{id}/disable` | `instruments.manage` | 204 |
| POST | `/instruments/{id}/enable` | `instruments.manage` | 204 |

- TenantAdmin / Adviser: current tenant. Body / list query must omit `tenantId`.
- SystemAdmin: list and create require tenant PublicId. Get / PUT / disable / enable may cross tenants.
- Customer → 403.
- Item includes `id`, `tenantId`, `symbol`, `name`, `quoteCurrency`, `isActive`, `rowVersion`. No price.
- Amendment 2026-11-22 (simulated instruments, accepted; field rules in [features/simulated-instruments.md](features/simulated-instruments.md) R6): the item gains `marketDataConnection`, the name of the market data connection whose symbol suffix the symbol ends with, or `null`. Worked out from the symbol and the configuration; no filter on it. Still no price.
- Create on a disabled tenant → §4.1 `target=tenant`.

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

- TenantAdmin / Adviser: current tenant. Body / list query must omit `tenantId`. Adviser: assigned Customers only.
- SystemAdmin: list and create require tenant PublicId. Get / PUT / close / reopen may cross tenants.
- Customer → 403.
- Item includes `id`, `tenantId`, `customerId`, `name`, `type`, `currency`, `status`, `isActive`, `cashBalance`, `rowVersion`. `cashBalance` is computed; added by [features/posting.md](features/posting.md).
- Close rejected while cash `SUM ≠ 0` (posting amendment).
- `GET /accounts/{id}/cash-balance` → `{ accountId, currency, cashBalance }` (`accounts.read`).
- Create on a disabled tenant → §4.1 `target=tenant`. Create or reopen on a Disabled Customer → §4.1 `target=user`.
- Amendment 2026-11-17 (account custody mode, accepted; field rules in [features/account-custody-mode.md](features/account-custody-mode.md)): the item also includes `custodyMode` (`External` / `Custodial`). `POST /accounts` requires `custodyMode`; only `External` can be selected, and a missing, reserved or unknown value is a field 400 with no problem code. A PUT body that includes `custodyMode` → 400. `GET /accounts` takes an optional `custodyMode` filter.
- Amendment 2026-11-17 (custody modes, accepted, landed; field rules in [features/custody-modes.md](features/custody-modes.md)): `POST /accounts` accepts `custodyMode: "Custodial"`, with `type` `Brokerage` only; another type → 400 `errors.Type`. `POST /accounts/{id}/portfolio` and `POST /accounts/{id}/rebalance` require a Custodial account, else 400 `account_not_eligible`.
- Amendment 2026-11-20 (client money accounts and deposit references, accepted; field rules in [features/client-money-accounts.md](features/client-money-accounts.md)): `POST /accounts` with `Custodial` in a currency where the Tenant has no active client money account → 400 `errors.Currency`. The item includes `depositReference` (server-generated on a Custodial account, `null` on an External one); a POST or PUT body that carries it → 400. `GET /accounts/{id}/deposit-instructions` (`accounts.read`) → `{ accountId, currency, reference, bankName, accountName, accountNumber }`, or 404 for an External or Closed account or when the currency has no active client money account.
- Amendment 2026-11-20 (deposit matching, accepted; field rules in [features/deposit-matching.md](features/deposit-matching.md)): `GET /accounts` takes an optional `depositReference` (exact after trim and upper-casing; an invalid value returns an empty list).
- Amendment 2026-11-20 (withdrawals, accepted; field rules in [features/withdrawals.md](features/withdrawals.md)): the item and `GET /accounts/{id}/cash-balance` include `reservedCash` (the sum of open withdrawals; 0 on an External account) and `availableCash` (cash balance minus reserved cash).
- Amendment 2026-11-21 (receipt returns, recalls and other debit lines, accepted; field rules in [features/bank-returns.md](features/bank-returns.md)): no new route or permission. `POST /statement-lines/{id}/request-return` also creates the return payment the bank sends; `reopen` of a `ReturnRequested` line is refused (400 `errors.Status`) once the bank was asked. `ignore` (from `Unmatched`) and `reopen` (from `Ignored`) apply to debit lines too. The statement line item gains statuses `ReturnSent` and `Returned`, reasons `UnknownPayment`, `UnknownReturn`, `RecallNotCovered`, `ReturnRejected`, method `Recall`, and `returnPayment: { status, attempts, lastFailure }` (`null` without a return payment); `match.accountId`, `match.customerId` and `match.transactionId` are `null` on a match with no Transaction. A recalled deposit is reversed by the flow (source `BankFeed`).
- Amendment 2026-11-22 (account closure, accepted; field rules in [features/account-closure.md](features/account-closure.md)): sub-resource `/accounts/{accountId}/closures` — `POST` (`account-closures.request`, `Idempotency-Key`, 201 `{ id }`), `GET` and `GET …/{id}` (`account-closures.read`); SystemAdmin has neither. The item gains `closure` (the latest closure, or `null`). `POST /accounts/{id}/close` on an Open Custodial account → 400 for every caller; External accounts unchanged. While a closure is `InProgress` the Account is closing and every hand write on it → 400. No problem code.
- Amendment 2026-11-22 (interest run, accepted; field rules in [features/interest-run.md](features/interest-run.md) R33, R35, R36): the item gains `accruedInterest` (unposted daily accruals, 8 decimals; `0` on an External account) and `interestAccruedThrough` (a date or `null`). `GET /accounts/{accountId}/interest-accruals` (`accounts.read`, the Account's scope) → list envelope of the daily accrual rows, newest first; filters `from`, `to`, `transactionId`, `posted`. `POST /accounts/{accountId}/closures` posts the interest accrued through yesterday before the payout is worked out.
- Amendment 2026-11-23 (orders and settlement, accepted; field rules in [features/orders.md](features/orders.md) R39–R41, R44, R47): `reservedCash` also counts the account's live buy orders. The item and `GET /accounts/{id}/cash-balance` gain `unsettledCash` (the net cash of posted sell fills that are not settled) and `withdrawableCash` (available cash minus unsettled cash, never below 0); on an External account `0` and the cash balance. The holding item of `GET /accounts/{id}/holdings` gains `reservedQuantity` and `availableQuantity`. An account closure is refused while the account has a live order or a fill that is not settled.
- Amendment 2026-11-23 (rebalance through orders, accepted; field rules in [features/rebalances.md](features/rebalances.md) R10, R25): `POST /accounts/{id}/rebalance` and `POST /accounts/{id}/portfolio` answer 200 `{ status, rebalanceId }` with `status` `Proposed` or `NoChange`; a failed plan is 400 with its problem code. `GET /accounts/{id}/portfolio` loses `lastRebalanceAt`, `lastRebalanceStatus` and `lastRebalanceMessage` and gains `rebalance` (`{ id, status, failure, proposedAt, endedAt }`).

### 7.7 Transactions

Field rules: [features/posting.md](features/posting.md) for cash types, [features/holdings.md](features/holdings.md) for `OpeningHoldings`, [features/buy-sell.md](features/buy-sell.md) for `Buy` / `Sell` / `StockIn` / `StockOut`, and [features/split.md](features/split.md) for `Split` / `Bonus` / `Scrip`. The transaction item has `cash` and `security` arrays; every leg carries its own `accountId` and `customerId`, and only legs on accounts the caller can see are returned ([features/leg-accounts.md](features/leg-accounts.md) §8.1). There is no top-level `accountId` / `customerId` and no flat `amount` on the item. Create body for a cash type still sends `amount`. `Split` / `Bonus` / `Scrip` send the new total quantity and omit cash.

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/transactions` | `transactions.read` | 200 envelope |
| GET | `/transactions/{id}` | `transactions.read` | 200 item |
| POST | `/transactions` | `transactions.create` | 201 `{ id }` |
| POST | `/transactions/transfer` | `transactions.create` | 201 `{ id }` |
| POST | `/transactions/fx` | `transactions.create` | 201 `{ id }` |
| POST | `/transactions/fx/quote` | `transactions.create` | 200 quote |
| POST | `/transactions/adjustment` | `transactions.adjust` | 201 `{ id }` |
| POST | `/transactions/{id}/reverse` | `transactions.create` | 201 `{ id }` |

- Create and reverse require `Idempotency-Key` ([ADR 0015](adr/0015-idempotency-keys.md)).
- TenantAdmin / Adviser omit `tenantId`. SystemAdmin list/create require tenant PublicId.
- Customer → 403.
- Optional list filters: `accountId`, `customerId`, `type`.
- The item has an `fx` object `{ baseCurrency, quoteCurrency, rate, rateAsOfDate }` when the Transaction has an FX detail (an `FxConversion` and its Reversal); otherwise `fx` is `null` ([features/fx-conversions.md](features/fx-conversions.md) R22).
- Amendment 2026-11-17 (custody modes, accepted, landed; field rules in [features/custody-modes.md](features/custody-modes.md)): the Account's custody mode decides what a create body may carry. On a Custodial account `POST /transactions` refuses `Interest`, `Opening`, `OpeningHoldings`, `StockIn`, `StockOut`, `Split`, `Bonus` and `Scrip` (400 `errors.Type`), and the server sets `bookedAt`: a body that sends it to `POST /transactions`, `/transactions/transfer`, `/transactions/fx` or `/transactions/fx/quote` → 400 `errors.BookedAt`. On an External account `bookedAt` stays required. A transfer, conversion or quote between a Custodial and an External account → 400 `errors.ToAccountId`. All are field 400s with no problem code.
- Amendment 2026-11-18 (External caller numbers, accepted, landed; field rules in [features/external-caller-numbers.md](features/external-caller-numbers.md)): on an External account `POST /transactions` `Buy` / `Sell` require `security[0].price` (executed price), accept an optional `fee`, and require the new `fxRate` when the instrument's quote currency differs from the Account currency; no price, rate or schedule is read. On a Custodial account all three → 400. `POST /transactions/fx` between External Accounts requires both `sellAmount` and `buyAmount` and refuses `expectedRate`; the rate is derived. `POST /transactions/fx/quote` between External Accounts → 400 `errors.FromAccountId`. The item shape does not change: `fee.scheduleId` is `null` for an entered fee, and `fx` is filled for an External `Buy` / `Sell` with a rate. No new route, policy or problem code.
- Amendment 2026-11-19 (transaction source, accepted; field rules in [features/transaction-source.md](features/transaction-source.md)): the item gains `source: { kind, id }` (`Manual`, `Seed` or `Rebalance`; `id` is the rebalance handle on a rebalance trade, else `null`). No body or query field. Rebalance trades have `reference: null`. On a Custodial account `POST /transactions/{id}/reverse` of a Transaction whose source is not `Manual` or `Rebalance` → 400.
- Amendment 2026-11-19 (adjustments, accepted; field rules in [features/adjustments.md](features/adjustments.md)): `POST /transactions/adjustment` (`transactions.adjust`, TenantAdmin only) posts an `Adjustment` on one Custodial account: optional signed `amount`, optional `security` `{ instrumentId, quantityChange, costChange }`, required `reason` and `memo`; no `bookedAt`, `tenantId` or currency. External account → 400 `errors.AccountId`. `POST /transactions` refuses `Adjustment`. Reverse of an `Adjustment` without `transactions.adjust` → 403. The item gains `adjustment: { reason }` (`null` on other types). No problem code.
- Amendment 2026-11-20 (bank feed and deposit matching, accepted; field rules in [features/deposit-matching.md](features/deposit-matching.md)): on a Custodial account `POST /transactions` refuses `TransferIn` (400 `errors.Type`): deposits come from the bank statement. A deposit is a `TransferIn` with `source: { kind: "BankFeed", id: <statement line id> }` and the new item field `bankReceipt: { valueDate, reference, payerName, payerAccount }` (`null` on other Transactions); a hand reverse of it → 400. New resource `/statement-lines` (`statement-lines.manage`, TenantAdmin only): list (filters `status`, `currency`, `clientMoneyAccountId`), get, and `POST …/{id}/match` `{ accountId, rowVersion, note? }`, `…/ignore`, `…/request-return`, `…/unmatch` `{ note, rowVersion }`, `…/reopen` `{ rowVersion }`, each 204; stale `rowVersion` → 409 `stale_row_version`. No `Idempotency-Key` on these actions. No problem code added.
- Amendment 2026-11-20 (withdrawals, accepted; field rules in [features/withdrawals.md](features/withdrawals.md)): on a Custodial account `POST /transactions` refuses `TransferOut` (400 `errors.Type`, every caller) and a `CloseOut` of more than a remainder below the currency's minor unit (400 `errors.Type`); `POST /transactions/transfer` between Custodial accounts of two Customers → 400 `errors.ToAccountId`. Every Custodial write that lowers cash (create, transfer, fx, adjustment, reverse, rebalance, unmatch) is refused when it would leave available cash below 0. A payout is a `TransferOut` with `source: { kind: "Payment", id: <withdrawal id> }` and the new item field `payment: { valueDate, reference, payeeName, payeeAccount }` (`null` on other Transactions); a hand reverse of it → 400. New resources `/nominated-bank-accounts` and `/withdrawals` (§6). `GET /statement-lines` takes an optional `direction` (`Credit`, `Debit`; else 400 `errors.Direction`); the line item gains `relatedLineId`, `withdrawalId` and `returnReason`. No problem code added.
- Amendment 2026-11-22 (account closure, accepted; field rules in [features/account-closure.md](features/account-closure.md)): on a Custodial account `POST /transactions` refuses `CloseOut` for every amount (400 `errors.Type`, every caller). Source kind `AccountClosure` marks the remainder write-off; it is not hand-reversible. Every create, quote and reverse with a leg on a closing account → 400 under the empty `errors` key.
- Amendment 2026-11-22 (interest run, accepted; field rules in [features/interest-run.md](features/interest-run.md) R23–R27, R34, R39): source kind `InterestRun` marks an interest posting; it is not hand-reversible. A cash leg's `role` may be `Tax`; every cash leg gains `tax` (`{ kind, ratePercent, basisAmount, amount, currency }` on a `Tax` leg, `null` otherwise) and the item gains `interest` (`{ periodFrom, periodTo, days, accruedAmount, grossAmount }` on an interest posting and its Reversal, `null` otherwise). A hand `Interest` on a Custodial account stays refused. The statement line item gains `kind` (`Transfer`, `Interest`), and `GET /statement-lines` an optional `kind` filter (else 400 `errors.Kind`).
- Amendment 2026-11-23 (orders and settlement, accepted; field rules in [features/orders.md](features/orders.md) R32, R35, R37, R38, R45, R48): on a Custodial account `POST /transactions` refuses `Buy` and `Sell` (400 `errors.Type`, `Trades on a Custodial account go through the broker. Place an order.`); the caller places an order (§7.12). Source kind `OrderFill` marks the `Buy` or `Sell` the platform posts for a fill, with the order's PublicId as source id; it is not hand-reversible. The item gains `execution` `{ orderId, fillId, price, tradeDate, settlementDate, brokerReference, settledAt }` on a fill and its Reversal, `null` otherwise. `POST /transactions/transfer`, `POST /transactions/fx` and the FX quote from a Custodial account refuse an amount that would take out cash from sales that have not settled (400).
- Amendment 2026-11-24 (migration batches, accepted; field rules in [features/migration-batches.md](features/migration-batches.md) R25–R27, R31): source kind `Migration` marks an `Opening` or `OpeningHoldings` written by a migration batch, with the batch's PublicId as source id; it is not hand-reversible. The item gains `migration` (`{ asOfDate, previousProvider }` on such a Transaction, `null` otherwise). On a Custodial account a hand `Opening` or `OpeningHoldings` stays refused (400 `errors.Type`) with the message `Opening balances on a Custodial account come from a migration batch.`

**Type-specific create routes.** When a type's create body differs from the common shape (one top-level `accountId`), that type gets its own `POST /transactions/{action}`. Each type has exactly one create route: a type with its own route is rejected by `POST /transactions` (400). Read (`GET /transactions`, `GET /transactions/{id}`) and reverse (`POST /transactions/{id}/reverse`) stay shared by every type. Every new type gets its own `POST /transactions/{action}` route, whatever its body shape (decided 2026-11-10). Existing types keep `POST /transactions` until each is moved in its own spec amendment ([future-plans.md](future-plans.md) §4).

Type-specific routes: `POST /transactions/transfer` for `InternalTransfer` ([features/internal-transfers.md](features/internal-transfers.md)); `POST /transactions/fx` for `FxConversion` and the read-only `POST /transactions/fx/quote` ([features/fx-conversions.md](features/fx-conversions.md)); `POST /transactions/adjustment` for `Adjustment` ([features/adjustments.md](features/adjustments.md)). The quote is a `POST` because it takes the create body and the same validation; it writes nothing, takes no `Idempotency-Key` and holds no rate. Create confirms the quoted rate with `expectedRate`; a different stored rate → 409 with `expectedRate` in `errors`.

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

Field rules: [features/trade-fees.md](features/trade-fees.md). Update or delete of a schedule row in effect → 409 `fee_schedule_in_effect`. Every cash leg in the transaction item carries `role` (`Principal` or `Fee`) and `fee` (the fee detail on a `Fee` leg, null otherwise).

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
| GET | `/corporate-actions` | `corporate-actions.read` | 200 envelope (filters `status`, `instrumentId`, `origin`) |
| GET | `/corporate-actions/{id}` | `corporate-actions.read` | 200 item |
| GET | `/corporate-actions/{id}/entitlements` | `corporate-actions.read` | 200 envelope (filter `status`; an Adviser sees assigned Customers' accounts only) |
| POST | `/corporate-actions` | `corporate-actions.manage` | 201 `{ id }` |
| PUT | `/corporate-actions/{id}` | `corporate-actions.manage` | 204 |
| POST | `/corporate-actions/{id}/cancel` | `corporate-actions.manage` | 204 |
| POST | `/corporate-actions/{id}/apply` | `corporate-actions.manage` | 204 |
| GET | `/corporate-action-announcements` | `tenants.manage` | 200 envelope (filters `connection`, `status`) |

Field rules: [features/corporate-actions.md](features/corporate-actions.md). No `Idempotency-Key`: the status and `rowVersion` make a repeat a 400 or a 409 `stale_row_version`. A served instrument takes no hand entry (400 `errors.InstrumentId`); a `Feed` action cannot be changed by hand (400 `errors.Origin`). No new problem code.

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

Amendment 2026-11-23 (trade settlement and broken trades, accepted; field rules in [features/trade-settlement.md](features/trade-settlement.md)):

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/trade-settlements` | `trade-settlements.manage` | 200 envelope (filters `status`, `clientMoneyAccountId`, `open`) |
| GET | `/trade-settlements/{id}` | `trade-settlements.manage` | 200 item with its fills and payments |
| POST | `/trade-settlements/{id}/pay` | `trade-settlements.manage` | 204 |
| POST | `/trade-settlements/{id}/set-aside` | `trade-settlements.manage` | 204 |
| POST | `/orders/{id}/fills/{fillId}/resolve-break` | `transactions.adjust` | 204 |

A fill item gains `brokerSettlementId`, `tradeSettlementId` and `break`, and its `status` may be `Broken`; `execution` on the transaction item gains `brokenAt`. The statement line item gains `tradeSettlementId`, match method `Settlement` and unmatched reason `SettlementMismatch`. The bank reconciliation item gains four kinds, and the run gains `openSettlements`. `GET /broker-connections` gains `settlementCurrencies`; `GET /execution-reports` gains the two kinds, three skip reasons and five fields. Writes take `rowVersion` (409 `stale_row_version`) and no `Idempotency-Key`. No new problem code.

Amendment 2026-11-23 (rebalance through orders, accepted; field rules in [features/rebalances.md](features/rebalances.md)):

| Method | Route | Policy | Success |
| --- | --- | --- | --- |
| GET | `/rebalances` | `rebalances.read` | 200 envelope (filters `accountId`, `customerId`, `portfolioId`, `status`, `open`) |
| GET | `/rebalances/{id}` | `rebalances.read` | 200 item with its targets, lines and orders |
| POST | `/rebalances/{id}/confirm` | `rebalances.confirm` | 204 |
| POST | `/rebalances/{id}/cancel` | `rebalances.confirm` | 204 |

The order item gains `rebalanceId`, and `GET /orders` the filter `rebalanceId`. `POST /orders/{id}/cancel` of a rebalance order → 400 `errors.Status`. Confirm and cancel take `rowVersion` (409 `stale_row_version`) and no `Idempotency-Key`. Nothing is sent to the broker in the request: the advance step of `broker-sync:{connection}` places the later waves. On a Custodial account a Transaction with source `Rebalance` is no longer hand-reversible. Five new problem codes (§4.2).

### 7.13 Migration batches

Amendment 2026-11-24 (migration batches, accepted; field rules in [features/migration-batches.md](features/migration-batches.md)):

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

An id in the body that is not in the Tenant → 400 at its place in the body. Submit and post of a batch with an issue → 400 `migration_batch_not_valid`. Post by the person who submitted → 400 `errors.Status`. Writes take `rowVersion` (409 `stale_row_version`) and no `Idempotency-Key`. `POST /statement-lines/{id}/match` takes `migrationBatchId` in place of `accountId`; the statement line item gains `migrationBatchId` and method `Migration`; the bank reconciliation item gains the kind `MigrationNotPosted`.

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

| Date | Change |
| --- | --- |
| 2026-09-11 | First English draft. Two HTTP surfaces; OpenIddict default protocol paths; no resource-API token issuer; `/users` namespace for people collections; `/users/me` on `webapi`; Customer may complete the authorization server; no ledger routes in Phase 1. |
| 2026-09-12 | identity-auth locks: Razor `/login`; JWT claim names; 15 min access / 14 day absolute refresh; UserName = PublicId. |
| 2026-09-13 | Currencies: `enabledOnly` (omit/`false` = all, `true` = enabled only); item includes `isEnabled`. |
| 2026-09-13 | §4.1 shared disabled error (`code=disabled` + `target`). People lists reuse tenants envelope; `enabledOnly` aligned with currencies. TenantAdmin field rules in [features/tenant-admins.md](features/tenant-admins.md). |
| 2026-09-14 | Adviser field rules in [features/advisers.md](features/advisers.md). Disable-adviser assigned-customer 400 is ordinary validation, not §4.1. |
| 2026-09-14 | Customer field rules in [features/customers.md](features/customers.md). Client allow-list: `adviser-portal` issues no Customer code. `GET /tenants/by-code/{code}` + `tenants.read`. |
| 2026-09-20 | `/instruments` catalog + §7.5. SystemAdmin list/create take tenant PublicId. Field rules in [features/instruments.md](features/instruments.md). Landed `9ea2f2a`. |
| 2026-09-21 | Catalog JSON `isEnabled` → `isActive`. Column rename script `0011`. People list/get JSON adds `isActive`. |
| 2026-09-25 | `/transactions` + account `cashBalance` / `/cash-balance`. Close-zero guard. Field rules in [features/posting.md](features/posting.md). |
| 2026-10-11 | Buy / Sell / StockIn / StockOut and transaction item shape (`cash` + `security`). Field rules in [features/buy-sell.md](features/buy-sell.md). |
| 2026-10-12 | Split / Bonus / Scrip accepted. New total quantity, no cash, total cost unchanged. Field rules in [features/split.md](features/split.md). |
| 2026-10-12 | Net-worth read model accepted and landed (`af422c2`). `GET /net-worth` and `GET /users/customers/{id}/net-worth`. Current point-in-time, per-currency arrays, breakdowns. Field rules in [features/net-worth.md](features/net-worth.md). BFF allow-list adds `/net-worth`. |
| 2026-11-02 | Portfolios draft. `/portfolios` and assignment/rebalance routes sketched in [features/portfolios.md](features/portfolios.md). Async 202 returns jobId + per-account handles. |
| 2026-11-02 | Portfolios accepted. Assign and `POST /accounts/{id}/rebalance` return 200 `{ status }`. Weight change and `POST /portfolios/{id}/rebalance` return 202. `POST /portfolios/{id}/disable` unassigns. |
| 2026-10-26 | Reports: `PUT /reports/{id}` for catalog IsActive. False cascades revoke of all TenantReports for that report. |
| 2026-10-26 | Reports: `GET /reports/catalog` lists every catalog row for SystemAdmin. Tenant grant list omits inactive rows. Granting an inactive catalog report is 404. |
| 2026-11-10 | Transactions: type-specific create routes convention (§7.7). Internal transfers draft: `POST /transactions/transfer`, `type` list filter ([features/internal-transfers.md](features/internal-transfers.md)). |
| 2026-11-11 | FX conversions accepted and landed. `POST /transactions/fx` returns 201 `{ id }` with `Location`, requires `Idempotency-Key` and `expectedRate` (mismatch → 409, `errors.expectedRate`). `POST /transactions/fx/quote` returns 200 with rate, as-of date and both amounts; no write. `POST /transactions` rejects `FxConversion`. The transaction item gains `fx` (null for other types). |
| 2026-11-10 | Internal transfers accepted. `POST /transactions/transfer` returns 201 `{ id }` with `Location: /transactions/{id}` and requires `Idempotency-Key`. `POST /transactions` rejects `InternalTransfer`. `GET /transactions` takes an optional `type` (exact `TransactionType` name; anything else → 400). |
| 2026-11-13 | `webapi` re-checks person `Status` and the tenant flag on every request. A disabled person, or any person of a disabled tenant, gets 403 with a still-valid token. §4.1 `target=tenant` on create is reached only by SystemAdmin ([features/identity-auth.md](features/identity-auth.md) amendment 2026-11-13). |
| 2026-11-14 | §2 Instants and §4 rows: a request instant needs `Z` or an offset and is converted to UTC; responses use `Z`; an unreadable body gets a `ProblemDetails` in every environment ([fixes/utc-instants.md](fixes/utc-instants.md)). |
| 2026-11-13 | §4.2 problem codes (accepted): every 409 and every named business 400 carries a stable `code`; one registry; clients branch on `code`. Build contract: [features/portfolios.md](features/portfolios.md) amendment 2026-11-13 part 1. |
| 2026-11-15 | Trade fees accepted: `/fees` namespace and `/fees/trade-schedules` (`fees.read` / `fees.manage`, §7.9); problem code `fee_schedule_in_effect`; cash legs in the transaction item gain `role` and `fee` ([features/trade-fees.md](features/trade-fees.md)). |
| 2026-11-15 | §3.1 role permission table: the one table of grants, matching `RolePermissions`. Moved from ADR 0013 and the instruments, accounts, posting and identity-auth specs; adds the portfolios, reports and fees rows and the Customer Portal grants that were missing there. |
| 2026-11-16 | §4 rows: a `bookedAt` in the future and a cash `amount` with too many decimal places are field 400s. A reverse of a security transaction that is not the latest security activity on its holding is an ordinary 400. No new problem code ([fixes/ledger-write-guards.md](fixes/ledger-write-guards.md)). |
| 2026-11-17 | §7.6: account custody mode in `review`. `custodyMode` on the account item, required on `POST /accounts`, refused on PUT, optional list filter. No new route, policy or problem code ([features/account-custody-mode.md](features/account-custody-mode.md)). |
| 2026-11-17 | §7.6: account custody mode accepted. |
| 2026-11-17 | §7.6: account custody mode landed. |
| 2026-11-17 | §7.6, §7.7: custody modes in `review`. A Custodial account can be opened (`Brokerage` only); manual entry matrix, same custody mode on both Accounts of a transfer or conversion, and server-set `bookedAt` on Custodial accounts; portfolio assignment and rebalance on Custodial accounts only. No new route, policy or problem code ([features/custody-modes.md](features/custody-modes.md)). |
| 2026-11-17 | §7.6, §7.7: custody modes accepted. |
| 2026-11-17 | §7.6, §7.7: custody modes landed. |
| 2026-11-18 | §7.3: tenant custody modes in `review`. `allowedCustodyModes` on the tenant bodies and item; `POST /accounts` checks it. No new route, policy or problem code ([features/tenant-custody-modes.md](features/tenant-custody-modes.md)). |
| 2026-11-18 | §7.3: tenant custody modes accepted. |
| 2026-11-18 | §7.3: tenant custody modes landed. |
| 2026-11-18 | §7.7: External caller numbers in `review`. Caller `price`, `fee` and `fxRate` on External `Buy` / `Sell`; both amounts and no `expectedRate` or quote on External conversions. No new route, policy or problem code ([features/external-caller-numbers.md](features/external-caller-numbers.md)). |
| 2026-11-18 | §7.7: External caller numbers accepted. |
| 2026-11-18 | §7.7: External caller numbers landed. |
| 2026-11-18 | §7 catalog, §7.8: grouping by custody mode in `review`. `breakdowns.byCustodyMode` on both net-worth reads; a Custody column and subtotals per custody mode in the Adviser Balances PDF; a Custody column and a note in the Customer Transactions PDF. No new route, policy, parameter or problem code ([features/custody-mode-grouping.md](features/custody-mode-grouping.md)). |
| 2026-11-18 | §7 catalog, §7.8: grouping by custody mode accepted. |
| 2026-11-19 | §7 catalog, §7.8: grouping by custody mode landed. |
| 2026-11-19 | §1, §3.1: simulator accepted. A third process with its own control and party surfaces; role `SimulatorOperator` has no permission and gets 401 on `webapi`. No `webapi` route or policy added ([features/simulator.md](features/simulator.md)). |
| 2026-11-19 | §3.1, §7.7: adjustments accepted. New permission `transactions.adjust` (TenantAdmin only), route `POST /transactions/adjustment`, item field `adjustment`, 403 on reverse of an `Adjustment` without the permission. No problem code. |
| 2026-11-20 | §6, §7.3, §7.6: client money accounts and deposit references accepted. Sub-resource `/tenants/{tenantId}/client-money-accounts` (`tenants.manage`); tenant item `clientMoneyCurrencies`; account item `depositReference`; `GET /accounts/{id}/deposit-instructions`; Custodial open needs an active client money account in its currency. No new policy or problem code ([features/client-money-accounts.md](features/client-money-accounts.md)). |
| 2026-11-20 | §3.1, §6, §7.3, §7.6, §7.7: bank feed and deposit matching accepted. Permission `statement-lines.manage` (TenantAdmin only); resources `/statement-lines` and `/bank-connections`; client money account `bankConnection` and `bankFeed`; `GET /accounts?depositReference=`; transaction item `bankReceipt` and source kind `BankFeed`; hand `TransferIn` refused on a Custodial account. No problem code ([features/deposit-matching.md](features/deposit-matching.md)). |
| 2026-11-20 | §3.1, §6, §7.6, §7.7: nominated bank accounts and withdrawals accepted. Six permissions (`nominated-bank-accounts.read` / `.manage` / `.verify`, `withdrawals.read` / `.request` / `.approve`); resources `/nominated-bank-accounts` and `/withdrawals`; account item `reservedCash` and `availableCash`; transaction item `payment` and source kind `Payment`; hand `TransferOut` refused and `CloseOut` limited on a Custodial account; Custodial transfers between Customers refused; statement line `direction` filter and payout fields. No problem code ([features/withdrawals.md](features/withdrawals.md)). |
| 2026-11-21 | §6, §7.7: receipt returns, recalls and other debit lines accepted. No route, permission or problem code; statement line statuses `ReturnSent`, `Returned`, four unmatched reasons, method `Recall`, item `returnPayment`, nullable match ids; ignore and reopen on debit lines; request return sends the payment ([features/bank-returns.md](features/bank-returns.md)). |
| 2026-11-21 | §3.1, §6, §7.8: bank reconciliation accepted. Permissions `bank-reconciliations.manage`, `reports.tenant.read` and `reports.tenant.create` (TenantAdmin only); resource `/bank-reconciliations` (overview, list, get, run now); report scope `Tenant` and report `bank-reconciliation`. No problem code ([features/bank-reconciliation.md](features/bank-reconciliation.md)). |
| 2026-11-22 | §3.1, §6, §7.6, §7.7: account closure accepted. Permissions `account-closures.read` and `account-closures.request`; sub-resource `/accounts/{accountId}/closures` (request, list, get); account item `closure`, withdrawal item `accountClosureId`; source kind `AccountClosure`; hand `CloseOut` and `POST /accounts/{id}/close` refused on a Custodial account; a closing account refuses every hand write. No problem code ([features/account-closure.md](features/account-closure.md)). |
| 2026-11-22 | §2, §3.1, §4.2, §6, §7.4, §7.6, §7.7, §7.10: interest run accepted. Namespace `/interest` with `/interest/rate-schedules` and `/interest/runs`; permissions `interest-rates.read`, `interest-rates.manage`, `interest-runs.read`, `interest-runs.start`; problem code `interest_schedule_in_effect`; sub-resources `/users/customers/{customerId}/tax-profile`, `/users/me/tax-profile` and `/accounts/{accountId}/interest-accruals`; account item `accruedInterest`, `interestAccruedThrough`; transaction item `interest`, leg role `Tax` and leg `tax`; source kind `InterestRun`; statement line `kind` and its filter ([features/interest-run.md](features/interest-run.md)). |
| 2026-11-22 | §6, §7.5: simulated instruments accepted. Resource `/market-data-connections` (`tenants.manage`, list from configuration with the price feed state); instrument item `marketDataConnection`. No new permission or problem code. |
| 2026-11-22 | §3.1, §6, §7.7, §7.11: corporate actions accepted. Permissions `corporate-actions.read` and `corporate-actions.manage`; resource `/corporate-actions` (list, get, create, update, cancel, apply, entitlements) and `/corporate-action-announcements` (`tenants.manage`); source kind `CorporateAction`; transaction item `corporateAction`; connection item `corporateActions`. No new problem code. |
| 2026-11-23 | §3.1, §6, §7.3, §7.6, §7.7, §7.12: orders and settlement accepted. Permissions `orders.read` and `orders.place`; resource `/orders` (list, get, create, preview, cancel), `/broker-connections` and `/execution-reports` (`tenants.manage`); `brokerConnection` on the tenant; source kind `OrderFill`; transaction item `execution`; account item `unsettledCash` and `withdrawableCash`; holding item `reservedQuantity` and `availableQuantity`; hand `Buy` / `Sell` refused on a Custodial account. No new problem code. |
| 2026-11-23 | §3.1, §6, §7.12: trade settlement and broken trades accepted. Permission `trade-settlements.manage`; resource `/trade-settlements` (list, get, pay, set aside); `POST /orders/{id}/fills/{fillId}/resolve-break` (`transactions.adjust`); fill `break`, transaction `execution.brokenAt`, statement line `tradeSettlementId`, reconciliation `openSettlements` and four item kinds, connection `settlementCurrencies`. No new problem code. |
| 2026-11-23 | §3.1, §4.2, §6, §7.6, §7.12: rebalance through orders accepted. Permissions `rebalances.read` and `rebalances.confirm`; resource `/rebalances` (list, get, confirm, cancel); problem codes `stale_price`, `instrument_inactive`, `currency_mismatch`, `no_broker_connection`, `rebalance_in_progress`; propose and assign answer `{ status, rebalanceId }`; assignment item `rebalance` replaces the three `lastRebalance…` fields; order item `rebalanceId` and filter; `Rebalance` no longer hand-reversible on a Custodial account. |
| 2026-11-24 | §3.1, §4.2, §6, §7.7, §7.13: migration batches accepted. Permission `migration-batches.manage`; resource `/migration-batches` (list, get, create, replace, submit, return, cancel, post); problem code `migration_batch_not_valid`; source kind `Migration`; transaction item `migration`; statement line `migrationBatchId` and match method `Migration`; reconciliation item kind `MigrationNotPosted`. |
