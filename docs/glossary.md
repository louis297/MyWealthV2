---
title: Glossary
status: draft
language: en
created: 2026-09-05
updated: 2026-09-13
related:
  - function-plan.md
  - domain-model.md
  - database-design.md
  - architecture.md
  - adr/0014-openiddict-authorization-code-pkce.md
---

# Glossary

Shared words for MyWealthV2. When a Feature Spec or domain doc introduces a new term, add it here in the same change.

If another document disagrees with this file, this file wins and the other document is updated.

Phase-2 ledger words are kept so names stay stable. They are **not** Phase-1 schema or API.

---

## Product / domain

| Term | Meaning | Not |
| --- | --- | --- |
| MyWealthV2 | This wealth-management SaaS. | — |
| Tenant / Firm / Client | A contracted firm on the platform. One firm = one tenant. | End-client **Customer** |
| TenantCode | Login identifier for a tenant. Phase 1 uses a code; no subdomain routing. | `TenantId` (internal integer key) |
| User | A person in the domain. All four roles share one Domain `Users` table. | The ASP.NET Identity login row by itself |
| SystemAdmin | Platform-level operator. Not bound to a tenant. | TenantAdmin |
| TenantAdmin | Highest authority inside one tenant. | SystemAdmin |
| Adviser | Tenant operator who manages assigned customers (and, from Phase 2, their ledger). | Customer |
| Customer | Account holder. Can obtain tokens from the authorization server. Phase 1 has no Customer Portal client. | A business row with no login principal |
| ApplicationUser / Identity user | ASP.NET Identity user in Infrastructure. All four roles share this table set. | Domain `User` |
| Permission / Policy | Named capability on an endpoint (for example `accounts.close`). Role → permission is mapped in code. | Per-tenant custom role tables; `AspNetRoles` |
| UserStatus | `PendingActivation` / `Active` / `Disabled`. | A single boolean `IsEnabled` with no activation state |
| Invitation | Admin does not set a password. Invitee sets a password via a one-time link. | Passing a password on the create API (Phase-1 transition path) |
| UserToken | One-time credential for invite / reset (hash only stored). Phase 1 reserves the table; no invite flow. | JWT access token |
| Account | Value container under a Customer (bank, cash, brokerage, property, liability, …). Phase 2. | An Identity login |
| Account.Currency | Currency of that account’s cash ledger. Immutable after open. | Instrument quote currency; tenant reporting currency |
| Cash ledger | Cash entries and balance in the account’s booking currency. Phase 2. | Holding quantity |
| Holding | Position of one instrument inside an account (quantity + cost). Phase 2. | A single transaction |
| Instrument | Holdable object from an in-database catalog. Phase 2. | A free-text name stored on Holding |
| QuoteCurrency | Currency used for the instrument’s price, market data, and cost accumulation. | Account booking currency |
| CostBasis | Holding cost as `Money`. Currency must equal the instrument quote currency. | Market value |
| Opening / initial holding | The only entry that may write quantity and cost directly. Phase 2. | Day-to-day holding edits |
| Transaction | A cash or trade posting on an account. Append-only. Phase 2. | EF `SaveChanges` |
| Reversal | A new opposite posting against a booked transaction (`Type = Reversal`). Original row is not updated or deleted. Phase 2. | `UPDATE`/`DELETE` of the original; an adjustment that does not point at the original |
| TransactionType | Buy / Sell / TransferIn / TransferOut / Dividend / Interest / Opening / Reversal. | User-defined Category |
| Category | Optional custom label on a transaction. Not in Phase 1. Not an account type. | Account type |
| Currency | ISO 4217 three-letter code. One row in the platform catalog. | A C# enum |
| Currency catalog | `Currencies` table + in-memory `ICurrencyCatalog`. | Joining `Currencies` on every hot path; a per-tenant allow-list |
| Currency.IsEnabled | Platform flag: the code may be used as a **new** reference. | A per-tenant switch; rewriting existing `ReportingCurrency` rows |
| DecimalPlaces | ISO minor units for input / display / validation (JPY = 0, most fiat = 2). | The scale of a money column (`decimal(18,4)` when amounts exist) |
| ReportingCurrency | Tenant currency for aggregated reports. May remain a later-disabled catalog code. | Account booking currency (may happen to match) |
| Money | Amount + currency code. Never a bare decimal. Domain VO in Phase 1; no catalog lookup, no rounding to DecimalPlaces. | `double` / currency-less `decimal`; a persisted Phase-1 column |
| Net worth | Assets minus liabilities at a point in time. Phase 2 returns per-currency arrays; no FX fold. | A single account balance; historical snapshots |
| Market value | Quantity × price (quote currency), then FX if needed. Phase 2 uses mocked prices. | Cost basis used as if it were net worth |
| Tenant isolation | A business row belongs to exactly one tenant. Cross-tenant read/write must fail. | Relying only on an easy-to-miss EF global query filter |
| TenantId | Tenant foreign key on business tables. | The string claim in the JWT by itself |

---

## Roles and portals

| Term | Meaning |
| --- | --- |
| Adviser Portal | Only frontend in Phase 1. Aspire resource name and OIDC client id: `adviser-portal`. Redirects to the authorization server; does not issue tokens. |
| Session probe | First portal page after callback: renders `GET /users/me` to prove the handshake. Not a product Dashboard. |
| Customer Portal | Later public OIDC client (`customer-portal`) on the same authorization server. Not registered in Phase 1. |
| Back Office | Platform-ops UI. Optional in Phase 1; SystemAdmin uses API / Scalar. |
| Hosted login | Password page on `identity` (tenantCode + email + password). Shared by all first-party portals. |

---

## Solution / engineering

| Term | Meaning | Not |
| --- | --- | --- |
| AppHost | `src/AppHost`. Aspire orchestration. | — |
| webapi | Aspire resource name for `src/Web`. Resource API only. | Generic name `Web` / `Frontend`; the authorization server |
| identity | Aspire resource name for `src/IdentityHost`. OpenIddict + hosted login. | A second SQL database; OpenIddict inside `webapi` |
| MyWealthDbV2 | SQL Server database name and Aspire connection-string name. | `MyWealthDb` (old name) |
| SchemaVersions | Record of applied SQL scripts. Schema-source-of-truth migration ledger. | EF Core `__EFMigrationsHistory` as the schema source of truth |
| Schema applicator | On startup, runs scripts that have not been applied. Does not `EnsureDeleted` in the normal path. | `EnsureCreated` / `EnsureDeleted` as daily boot |
| Command | MediatR write use case. | — |
| Query | MediatR read use case. | — |
| Endpoint group | Minimal API group under `src/Web/Endpoints`. | MVC controllers as the default style |
| Feature spec | One deliverable vertical slice under `docs/features/`. | A phase folder of leftover notes |
| ADR | A decision under `docs/adr/` that is not re-litigated in place. | Rewriting an accepted ADR’s conclusion |
| IMarketData | Instrument price port. Mocked when the ledger exists. Not introduced in Phase 1: port surface not locked. | Calling a live vendor from a handler |
| IFxRate | FX port. Same currency = 1. Cross-currency is not silently treated as 1. | Hard-coded 1 for every pair |
| IEmailSender | Invite / reset mail. Phase 1 is no-op or log-only. | Built-in SMTP in Domain |
| Authorization server | Issues and revokes tokens, exposes discovery and JWKS. Hosted in Aspire `identity` via OpenIddict. | A custom resource-API `/auth/login` that mints JWTs; OpenIddict inside `webapi` |
| OpenIddict | The authorization-server library. Tables replace a custom refresh store. Protocol paths stay on library defaults (`/connect/*`, well-known discovery / JWKS); do not remap. | Duende IdentityServer; an external CIAM tenant; a custom `/auth` token issuer |
| OIDC client | Registered application that runs authorization code + PKCE. Phase 1: `adviser-portal` only. | A second token issuer per portal |
| Authorization code + PKCE | Portal redirect grant. The Phase-1 and later-portal session protocol. | Resource Owner Password Credentials grant |
| JWT / access token | Short-lived bearer issued by OpenIddict. Not the only session source of truth. | Long-lived access token with no refresh; tokens minted by a resource endpoint |
| Refresh token | Stored by OpenIddict. Revoked on password change, disable, and logout. | A custom `RefreshTokens` table; refresh kept only in the browser |
| RowVersion | Conflict detection on key tables. HTTP 409. | Last-write-wins with no check |
| Isolation test | Cross-tenant assertion. CI gate from the Tenants slice onward. | A single shared happy-path test |
| PublicId | UUID column used by API and UI. | Clustered primary key; login short code `TenantCode` |
| `/users` prefix | Namespace for people resource APIs (`/users/me`, `/users/tenant-admins`, `/users/advisers`, `/users/customers`). Not a parent resource and not a catch-all `GET/POST /users`. | `/users/{id}/advisers`; putting tenants or ledger routes under `/users` |
| Id | `int` identity on `BaseEntity`. Internal only. | Value exposed in routes or JSON |

---

## Account types (Phase 2 names; reserved)

| Value | Notes |
| --- | --- |
| Bank | Current / savings and similar. |
| Cash | Physical or not-yet-banked cash. |
| Brokerage | Equities, funds, ETFs, and similar positions. |
| Property | Real property. |
| Credit | Cards and loans. Counts as a liability in net worth. |
| Other | Catch-all. |

Account type does **not** replace the cash ledger. Cash balance comes from cash postings. Do not infer cash by branching on `AccountType == Bank` and summing transactions.

---

## Status of this glossary

- Product terms follow locked decisions as of 2026-09-11.
- Login context is **TenantCode** (Phase 1 does not parse a host subdomain).
- Session protocol is OpenIddict authorization code + PKCE (ADR 0014).
- Four roles share one Domain `Users` table and one `ApplicationUser` table (ADR 0013).
- Internal key is `int` identity; external key is `PublicId` (ADR 0007).
- Reversal and instrument catalog wording is reserved for the Phase-2 ledger. Phase 1 does not implement those tables or APIs.
- Invitation / password-reset wording is reserved. Phase 1 keeps `UserStatus`, `UserTokens`, and `IEmailSender` seams only.
