---
title: Glossary
status: draft
language: en
created: 2026-09-05
updated: 2026-11-12
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
| SystemAdmin | Platform-level operator. Not bound to a tenant. Signs in to Back Office only. | TenantAdmin |
| TenantAdmin | Highest authority inside one tenant. | SystemAdmin |
| Adviser | Tenant operator who manages assigned customers (and, from Phase 2, their ledger). | Customer |
| Customer | Account holder. Login principal. Phase 1 client `adviser-portal` issues no code. Client `customer-portal` is accepted ([customer-portal/customer-portal.md](customer-portal/customer-portal.md)). | A business row with no login principal |
| Report | Platform catalog entry: PublicId, stable Code, name, function, scope (`Customer` or `Book`), allowed triggers, IsActive. Disable cascades revoke of all TenantReports. | A tenant-owned row; a generated file |
| Book | An Adviser’s set of assigned Customers. Book-scoped reports run across that set. TenantAdmin selects an Adviser. | The whole tenant; a single Customer |
| TenantReport | Grant of a Report to a Tenant. Required before the tenant can schedule or run it. | An execution |
| ReportSchedule | Editable recurring definition. Must name a ReportId. Stores a free-form `parameters` JSON object. | A one-off manual request |
| IReportGenerator | Per-report strategy. Registered by ReportId. Binds and validates `parameters`, checks scope, generates the result. | A Hangfire job type per report |
| asOf | Server-written UTC timestamp on Adviser Balances Report, set when the job is created. Not a client parameter. | A date the caller sends |
| Worker | Aspire resource `worker` (`src/Worker`). General-purpose Hangfire host for async tasks. | A second resource API |
| ApplicationUser / Identity user | ASP.NET Identity user in Infrastructure. All four roles share this table set. | Domain `User` |
| Permission / Policy | Named capability on an endpoint (for example `accounts.close`). Role → permission is mapped in code. | Per-tenant custom role tables; `AspNetRoles` |
| UserStatus | `PendingActivation` / `Active` / `Disabled`. Source of truth for a person. | Writing `IsActive` by itself |
| User.IsActive | Derived filter bit. `1` only when `Status = Active`. `PendingActivation` and `Disabled` are `0`. | A second writable switch; replacing `UserStatus` |
| Invitation | Admin does not set a password. Invitee sets a password via a one-time link. | Passing a password on the create API (Phase-1 transition path) |
| UserToken | One-time credential for invite / reset (hash only stored). Phase 1 reserves the table; no invite flow. | JWT access token |
| Account | Value container under a Customer (bank, cash, brokerage, property, liability, …). Phase 2. | An Identity login |
| Account.Currency | Currency of that account’s cash ledger. Immutable after open. | Instrument quote currency; tenant reporting currency |
| Account.Type | Capability gate on the container. Immutable after open. Phase 2 selectable: Bank, Cash, Brokerage, Other. | A product module; a stand-in that uses Other for Property or Credit |
| AccountStatus | `Open` / `Closed`. Source of truth for an account. HTTP close / reopen. | Instrument disable; `UserStatus` |
| Account.IsActive | Derived filter bit. `1` iff `Status = Open`. Inactive accounts drop out of later net worth. | A writable body field; `IsEnabled` |
| Cash ledger | Cash entries and balance in the account’s booking currency. Phase 2. Owned by the posting spec, not a separate Feature Spec. | Holding quantity |
| Cash balance | `SUM` of posted cash-leg signed amounts on one Account. Computed. Not a column on `Accounts`. | A persisted `Account.Balance`; net worth |
| Holding | Position of one instrument inside an account (quantity + cost). Phase 2. | A single transaction |
| Portfolio | Target-weight model of instruments with a mandatory base currency. Accepted. An Adviser assigns at most one Active portfolio to a Brokerage or Other account. | A new Account.Type; a free-form basket |
| AccountPortfolioAssignment | Link that binds one Account to one Active Portfolio. Accepted. While Active, manual security trades are rejected. | A second holdings book |
| Instrument | Holdable object from an in-database catalog. Phase 2. | A free-text name stored on Holding |
| Symbol | Instrument code on the tenant catalog (ticker-style unique key inside one tenant). | Display `Name`; ISIN |
| QuoteCurrency | Currency used for the instrument’s price, market data, and cost accumulation. | Account booking currency |
| CostBasis | Holding cost as `Money`. Currency must equal the instrument quote currency. | Market value |
| Opening / initial holding | The only entry that may write quantity and cost directly. Phase 2. | Day-to-day holding edits |
| Transaction | Booked business event in a tenant (type, bookedAt, memo). Belongs to its leg accounts, not to one Account. Public aggregate and `/transactions` resource. Write = posted in the current draft. Header + legs in one command. Not a pre-accounting capture row. | A Created row that an event handler later copies into a journal; EF `SaveChanges` |
| Leg (cash / security) | Accounting line that hits one book: the book of its own Account (`AccountId` on the leg). Cash leg and security leg live on the **same** Transaction. Buy/Sell = both. StockIn/StockOut and OpeningHoldings = security only. Split/Bonus/Scrip = security only, total cost unchanged, new total quantity ([features/split.md](features/split.md)). | A second HTTP resource; “posting means cash only”; a wide row that holds both cash and stock columns |
| Leg account | The Account whose book one leg hits. A Transaction belongs to its leg accounts; the header has no Account. See [features/leg-accounts.md](features/leg-accounts.md). | An `AccountId` on the Transaction header; a Customer on the header |
| Journal | Older discussion word for the Transaction header. Prefer *Transaction* in new text. | A second table beside Transactions |
| Signed book amount | Stored signed decimal on a leg, from **that container’s book**. `+` increases that book; `−` decreases it. Balance / quantity is `SUM` of those signs. Read models may flip a sign for display (Credit as liability in net worth). | Debit/credit columns; a new sign rule per Feature Spec; storing outflows as positive and inferring direction from `Type` |
| Reversal | A new opposite Transaction (`Type = Reversal`). The Reversal points at the original (`OriginalTransactionId`). The original receives a single write of `ReversedByTransactionId` pointing at the Reversal; after that write the original is immutable again. Economic legs are never updated. Cash leg = arithmetic negation of the original signed amount. Already reversed = `ReversedByTransactionId` is not null. Phase 2. | An open `UPDATE`/`DELETE` of the original; an adjustment that does not link both ways; a self-join to discover whether a row is reversed |
| TransactionType | TransferIn / TransferOut / InternalTransfer / Interest / Opening / OpeningHoldings / Buy / Sell / StockIn / StockOut / Split / Bonus / Scrip / CloseOut / Reversal. Dividend reserved. Split / Bonus / Scrip are accepted in [features/split.md](features/split.md). InternalTransfer is accepted in [features/internal-transfers.md](features/internal-transfers.md). FxConversion is accepted in [features/fx-conversions.md](features/fx-conversions.md). | User-defined Category |
| InternalTransfer | Cash transfer between two Accounts in the same tenant: one Transaction, a negative cash leg on the from Account and a positive cash leg on the to Account, same currency. The Accounts may belong to two Customers. Created by `POST /transactions/transfer`. Accepted: [features/internal-transfers.md](features/internal-transfers.md). | `TransferIn` / `TransferOut` (one cash leg, off-books counterparty, also used for cross-tenant movement); a pair of unlinked transactions |
| FxConversion | Cash conversion between two Accounts of different currencies of the same Customer: one Transaction, a negative cash leg in the sell currency on the from Account and a positive cash leg in the buy currency on the to Account, priced by the server from the cached reference rate. Across Customers: convert first, then `InternalTransfer`. Created by `POST /transactions/fx`. Accepted: [features/fx-conversions.md](features/fx-conversions.md). | `InternalTransfer` (same currency); automatic FX inside Buy / Sell; a rate entered by the caller |
| FX detail | The rate a Transaction used: base and quote currency, rate, rate as-of date and source, stored 0..1 per Transaction in `TransactionFxDetails` and copied to its Reversal. Accepted: [features/fx-conversions.md](features/fx-conversions.md). | A row in `FxRates` (the cache the rate was read from); a quote |
| Applied FX rate | Domain value object `AppliedFxRate`: the rate a Transaction is priced at, with base and quote currency (different), rate `> 0` (quote per 1 base), as-of date and source. Read from `FxRates` by Application and passed to the Domain factory; stored as the FX detail. Accepted: [features/fx-conversions.md](features/fx-conversions.md). | A rate typed by the caller; the `FxRates` row itself |
| FX quote | Read-only answer to `POST /transactions/fx/quote`: rate, as-of date and both amounts for a proposed FxConversion. Not stored; does not hold the rate. The create call confirms it with `expectedRate`. | A rate lock; a stored quote resource |
| Category | Optional custom label on a transaction. Not in Phase 1. Not an account type. | Account type |
| Currency | ISO 4217 three-letter code. One row in the platform catalog. | A C# enum |
| Currency catalog | `Currencies` table + in-memory `ICurrencyCatalog`. | Joining `Currencies` on every hot path; a per-tenant allow-list |
| Currency.IsActive | Platform flag: the code may be used as a **new** reference. Column was `IsEnabled` until script `0011`. | A per-tenant switch; rewriting existing `ReportingCurrency` rows |
| IsActive | Filter bit. Catalogs (Tenant, Currency, Instrument): the only flag. Users and Accounts: derived from `Status`. | `IsEnabled`; an independently writable second machine |
| DecimalPlaces | ISO minor units for input / display / validation (JPY = 0, most fiat = 2). | The scale of a money column (`decimal(18,4)` when amounts exist) |
| ReportingCurrency | Tenant currency for aggregated reports. May remain a later-disabled catalog code. | Account booking currency (may happen to match) |
| Money | Amount + currency code. Never a bare decimal. Domain VO in Phase 1; no catalog lookup, no rounding to DecimalPlaces. Ledger **direction** lives on the signed book amount of a leg, not inside `Money`. | `double` / currency-less `decimal`; a persisted Phase-1 column |
| Net worth | Assets minus liabilities at a point in time. Phase 2 returns per-currency arrays; no FX fold. | A single account balance; historical snapshots |
| Market value | Quantity × price (quote currency), then FX if needed. Price comes from `IMarketData` (`InstrumentPrices`). | Cost basis used as if it were net worth |
| Tenant isolation | A business row belongs to exactly one tenant. Cross-tenant read/write must fail. Dual check (handler `Where`) + EF global query filter as safety net + isolation tests. | Relying only on the EF global query filter |
| TenantId | Tenant foreign key on business tables. | The string claim in the JWT by itself |

---

## Roles and portals

| Term | Meaning |
| --- | --- |
| Adviser Portal | Only frontend in Phase 1. Aspire SPA resource and OIDC `ClientId`: `adviser-portal`. Does not issue tokens. Browser talks to `bff-adviser-portal`, not to `identity` / `webapi` (ADR 0016). |
| Session probe | First portal page after callback: renders `GET /users/me` to prove the handshake. Not a product Dashboard. |
| Customer Portal | Second client (`customer-portal`) on the same authorization server. Accepted: [customer-portal/customer-portal.md](customer-portal/customer-portal.md). Shape is a second BFF + client (`bff-customer-portal`), not a public SPA token stack. |
| Back Office | Platform-ops UI for SystemAdmin. Landed: [backoffice/back-office.md](backoffice/back-office.md). Blazor host `back-office`, no BFF. Scalar remains available. |
| Hosted login | Password page on `identity` (tenantCode + email + password). Shared by all first-party portals. |

---

## Host codes

One row per process. Aspire resource names are the words used in docs and service discovery. Project names are the C# / folder names. Do not invent a second spelling.

`bff` is always the first token on a BFF resource, project, and feature-spec file. The other three keep the names already in the tree. OIDC `ClientId` is not a host code: it stays `adviser-portal` when the holder moves to the BFF.

| Host | Aspire resource | Project | Path | Spec file |
| --- | --- | --- | --- | --- |
| Authorization server | `identity` | `IdentityHost` | `src/IdentityHost` | [features/identity-auth.md](features/identity-auth.md) |
| Resource API | `webapi` | `Web` | `src/Web` | Feature specs in `features/` (unprefixed) |
| Adviser Portal SPA | `adviser-portal` | `AdviserPortal` | `src/AdviserPortal` | [adviser-portal/adviser-portal.md](adviser-portal/adviser-portal.md) |
| Adviser Portal BFF | `bff-adviser-portal` | `BffAdviserPortal` | `src/BffAdviserPortal` | [features/bff-adviser-portal.md](features/bff-adviser-portal.md) (accepted) |
| Customer Portal SPA | `customer-portal` | `CustomerPortal` | `src/CustomerPortal` | [customer-portal/customer-portal.md](customer-portal/customer-portal.md) (accepted) |
| Customer Portal BFF | `bff-customer-portal` | `BffCustomerPortal` | `src/BffCustomerPortal` | [features/bff-customer-portal.md](features/bff-customer-portal.md) (accepted) |
| Back Office | `back-office` | `BackOffice` | `src/BackOffice` | [backoffice/back-office.md](backoffice/back-office.md) (accepted) |

Later portals copy the last BFF row: `bff-customer-portal`, `BffCustomerPortal`, `src/BffCustomerPortal`, `features/bff-customer-portal.md`. Do not use `customer-portal-bff` or `CustomerPortalBff`.

---

## Solution / engineering

| Term | Meaning | Not |
| --- | --- | --- |
| AppHost | `src/AppHost`. Aspire orchestration. | — |
| webapi | Aspire resource name for `src/Web`. Resource API only. | Generic name `Web` / `Frontend`; the authorization server |
| identity | Aspire resource name for `src/IdentityHost`. OpenIddict + hosted login. | A second SQL database; OpenIddict inside `webapi`; the portal BFF |
| adviser-portal-bff | Do not use. Superseded spelling. | Resource is `bff-adviser-portal` |
| MyWealthDbV2 | SQL Server database name and Aspire connection-string name. | `MyWealthDb` (old name) |
| SchemaVersions | Record of applied SQL scripts. Schema-source-of-truth migration ledger. | EF Core `__EFMigrationsHistory` as the schema source of truth |
| Schema applicator | On startup, runs scripts that have not been applied. Does not `EnsureDeleted` in the normal path. | `EnsureCreated` / `EnsureDeleted` as daily boot |
| Command | MediatR write use case. | — |
| Query | MediatR read use case. | — |
| Domain event | Fact raised by an aggregate. Persisted to `OutboxMessages` in the same transaction (ADR 0018, accepted). Delivered at-least-once by the worker via MediatR. Contract: [features/outbox.md](features/outbox.md). | Immediate post-`SaveChanges` dispatch; an integration event or message-broker message |
| Outbox message | Row in `OutboxMessages`. `Type` + `Payload` (serialised domain event) identify the fact. `Status` drives claim and retry. `TenantId` is a column for context. | A Hangfire job; a command record |
| Endpoint group | Minimal API group under `src/Web/Endpoints`. | MVC controllers as the default style |
| Feature spec | One deliverable vertical slice under `docs/features/`. | A phase folder of leftover notes |
| ADR | A decision under `docs/adr/` that is not re-litigated in place. | Rewriting an accepted ADR’s conclusion |
| IMarketData | Instrument price port. `TryGetPrice(instrumentId, asOf?) → Money?`. Missing → empty, not 0. Backed by local `InstrumentPrices` table; not a live call. Contract: [features/instrument-prices.md](features/instrument-prices.md). | Calling a live vendor from a handler; inventing 0 |
| IFxRate | FX port. Same currency = 1. Missing pair throws `FxRateNotFoundException` (carries base, quote, as-of). Backed by local `FxRates` table; not a live call. Contract: [features/fx-rates.md](features/fx-rates.md), ADR 0019. | Hard-coded 1 for every pair; calling Frankfurter on the read path |
| IEmailSender | Invite / reset mail. Phase 1 is no-op or log-only. | Built-in SMTP in Domain |
| Authorization server | Issues and revokes tokens, exposes discovery and JWKS. Hosted in Aspire `identity` via OpenIddict. | A custom resource-API `/auth/login` that mints JWTs; OpenIddict inside `webapi` |
| OpenIddict | The authorization-server library. Tables replace a custom refresh store. Protocol paths stay on library defaults (`/connect/*`, well-known discovery / JWKS); do not remap. | Duende IdentityServer; an external CIAM tenant; a custom `/auth` token issuer |
| OIDC client | Registered application that runs authorization code + PKCE. Client id `adviser-portal` is confidential and held by `bff-adviser-portal`. Client id `back-office` is confidential and held by the Back Office host. | A second token issuer per portal |
| Identity cookie scheme | Named cookie authentication scheme on `identity`, one per first-party client. `/connect/authorize` and hosted `/login` use only the scheme for the current `client_id`. Logout clears only that scheme. See identity-auth amendment 2026-10-27. | One shared identity cookie for every client; relying on client-sent `prompt=login` |
| Security stamp | Random value on `AspNetUsers.SecurityStamp`, copied into the identity cookie at hosted login. ASP.NET Identity writes a new one on password change. An identity cookie whose stamp no longer matches is rejected. See identity-auth amendment 2026-11-13 (security stamp). | `Users.RowVersion`; a token revocation flag |
| Authorization code + PKCE | Redirect grant. First-party session protocol. BFF uses PKCE with a client secret. | Resource Owner Password Credentials grant |
| JWT / access token | Short-lived bearer issued by OpenIddict. The portal browser must not store it. | Long-lived access token with no refresh; tokens minted by a resource endpoint or by the BFF |
| Refresh token | Stored by OpenIddict. Revoked on password change, disable, and logout. Rotated when the BFF presents it. Only the BFF holds the handle. | A custom `RefreshTokens` table |
| BFF | Backend for Frontend. First-party portal edge. Resource and project names put `bff` first. `bff-adviser-portal` is accepted. | Putting cookie auth on `webapi`; Duende.BFF; `adviser-portal-bff` |
| RowVersion | Conflict detection on key tables. HTTP 409. | Last-write-wins with no check |
| Isolation test | Cross-tenant assertion. CI gate from the Tenants slice onward. | A single shared happy-path test |
| PublicId | UUID column used by API and UI. | Clustered primary key; login short code `TenantCode` |
| `/users` prefix | Namespace for people resource APIs (`/users/me`, `/users/tenant-admins`, `/users/advisers`, `/users/customers`). Not a parent resource and not a catch-all `GET/POST /users`. | `/users/{id}/advisers`; putting tenants or ledger routes under `/users` |
| Id | `int` identity on `BaseEntity`. Internal only. | Value exposed in routes or JSON |
| Smoke tenant | Tenant used only by the smoke test (TenantCode `smoke`). Created by hand in production after the first deploy; seeded in Development when the smoke passwords are configured. Stays enabled. See [smoke-test.md](smoke-test.md) §3. | A demo tenant for visitors; `demo` / `demo2` |
| Smoke test | Playwright suite `tests/Smoke.Tests` that signs in to each portal and reads one page, to prove the system is on. Reads only. Runs locally and after each deploy. See [smoke-test.md](smoke-test.md). | The functional test suite; an end-to-end suite of business rules |
| Post-deploy smoke test | The smoke test run from GitHub Actions against the public hostnames after each deploy. A failure fails the deploy and rolls back the image tag. | Running the smoke test on every pull request |
| Public host | A host that Caddy forwards browser traffic to: `identity`, `bff-adviser-portal`, `bff-customer-portal`, `back-office`. See [features/production-hosting.md](features/production-hosting.md). | `webapi`, `worker` (internal only) |
| Health port | Second HTTP port of every host (default `8081`). Outside Development, `/health` and `/alive` answer only there. The reverse proxy never forwards to it. See [features/production-hosting.md](features/production-hosting.md). | The traffic port `8080`; a public status page |
| Bootstrap SystemAdmin | The first SystemAdmin in a production database, created by `webapi` from configuration when no SystemAdmin exists. See [features/identity-auth.md](features/identity-auth.md) amendment 2026-11-12. | The Development seed SystemAdmin `system-admin@localhost`; a SystemAdmin created on every start |
| Deploy folder | Repository folder `deploy/`: compose file, Caddy files and deploy scripts, copied to the VPS on every deploy. Holds no secrets. See [deployment.md](deployment.md) §5. | `.env`, `certs/` or `data/` on the VPS |

---

## Account types (Phase 2 names; reserved)

| Value | Notes |
| --- | --- |
| Bank | Deposit-style cash at a bank: current / savings and similar. Not the bank as an institution. Phase 2 openable; cash book only; no holdings. |
| Cash | Physical or not-yet-banked cash. Phase 2 openable; cash book only; no holdings. |
| Brokerage | Equities, funds, ETFs, and similar positions. Phase 2 openable; may hold instruments. |
| Property | Real property held as instruments in this account. Reserved name; not selectable in Phase 2. |
| Credit | Cards and loans. Counts as a liability in net worth. Reserved name; not selectable in Phase 2. |
| Other | Catch-all. Phase 2 openable; may hold instruments. Not a substitute for Property or Credit. |

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
