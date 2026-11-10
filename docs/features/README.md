---
title: Feature Specs
status: draft
language: en
created: 2026-09-12
updated: 2026-11-11
related:
  - ../function-plan.md
  - ../api-design.md
  - ../README.md
---

# Feature Specs

One file per vertical slice, flat in this folder. Do not split by phase. Put the phase in front matter and in this README.

Scope lives in [function-plan.md](../function-plan.md). HTTP conventions live in [api-design.md](../api-design.md). Field contracts live in the Feature Spec, not back in api-design.

Do not add empty files for a phase that has not opened.

---

## Discipline

- **In** scope is only what that slice ships. `draft` / `review` are not build contracts. `accepted` is.
- `note` is discussion only. Do not create tables or routes from a note.
- From Tenants onward, every tenant-scoped slice includes cross-tenant isolation tests.
- `/users` is a namespace prefix for people collections, not a parent resource. Do not nest `/users/{id}/advisers`. `/tenants` and `/currencies` stay at the root.
- The Adviser Portal is not a backend slice. Portal scope lives in [adviser-portal/](../adviser-portal/README.md).
- Implementation plans mark suggested git commits. The repository should build after each commit.

---

## Phase 1 — platform foundation

The vertical cut stops at people in a firm, the currency catalog, and session. No ledger tables or ledger APIs.

| Spec | Status | Who | Ships | Why it stays its own slice |
| --- | --- | --- | --- | --- |
| [identity-auth](identity-auth.md) | accepted (landed in repo 2026-09-14) | Login-capable roles | OpenIddict defaults, hosted `/login`, policies including `tenants.read`, `/users/me`, revocation, UserStatus, `UserTokens` seam, **client × role allow-list** | Two processes, one product capability |
| [currencies](currencies.md) | accepted (HTTP landed in repo 2026-09-13) | Authenticated (all four roles) | Catalog + `ICurrencyCatalog` + `GET /currencies` (`enabledOnly`) + `Tenants.ReportingCurrency` + Domain `Money` | Own table; HTTP after Bearer validation works |
| [tenants](tenants.md) | accepted (landed in repo 2026-09-14) | SystemAdmin manage; TenantAdmin / Adviser read-own | `/tenants` manage + `GET /tenants/by-code/{code}` (`tenants.read`) | Platform resource; path is not under `/users` |
| [tenant-admins](tenant-admins.md) | accepted (landed in repo 2026-09-13) | SystemAdmin | `/users/tenant-admins`; dual-write; last-admin disable allowed; disabled tenant → 400 `disabled`/`tenant` | Caller and gap belong here |
| [advisers](advisers.md) | accepted (landed in repo 2026-09-14 `8e5568c`) | TenantAdmin | `/users/advisers`; current tenant; cross-tenant 404; `DisableAdviser(bool)` guard | Different guard and portal list |
| [customers](customers.md) | accepted (landed in repo 2026-09-14 `b16d059`) | TenantAdmin; Adviser (assigned) | `/users/customers`; login principal; reassign + `CustomerAdviserReassigned`; no portal; no ledger guard | Assignment scope and rebind |

Do not merge the three people slices into `users.md`.

Not a Feature Spec:

| Capability | Lives in |
| --- | --- |
| Schema applicator | ADR 0008, architecture, database-design |
| Isolation tests | Each business slice from tenants onward |
| Invitation delivery | Out of identity-auth; table and email port reserved |
| Ledger policy names | Phase 2 |
| Adviser Portal (OIDC shell + pages cut C) | [adviser-portal/](../adviser-portal/README.md) |
| SystemAdmin UI | None — Scalar |

### Implementation order

```text
schema
    └── identity-auth
            └── adviser-portal shell + callback   (landed)
                    └── currencies
                            └── tenants
                                    └── tenant-admins
                                            └── advisers
                                                    └── customers
                                                            └── A  identity-auth amendment (allow-list)   (landed; A1–A10 passed)
                                                                    └── B  tenants.read + by-code                 (landed; B1–B11 passed)
                                                                            └── C  adviser-portal pages                 (accepted; landed 2026-09-14)
isolation-tests
```

Repo master (2026-09-14, `09dc6ec`): people collections, currencies HTTP, amendments A/B, portal pages C, logout/callback fixes, and isolated TestAppHost SQL (`dbserver-test` / `MyWealthDbV2-test`) are in the tree. A1–A10 and B1–B11 passed (confirmed 2026-09-15). Do not rewrite those specs.

Acceptance: [identity-auth](identity-auth.md) amendment A (A1–A10), [tenants](tenants.md) amendment B (B1–B11), [adviser-portal](../adviser-portal/adviser-portal.md) cut C (C1–C15). Construction notes (`review`): [adviser-portal/frontend-implementation-notes.md](../adviser-portal/frontend-implementation-notes.md).

`GET /currencies` is not public. Register no `currencies.read` policy. Use default `.RequireAuthorization()`. Query `enabledOnly`: omitted / `false` = all rows; `true` = enabled only. Every item includes `isActive`.

---

## Phase 2 — ledger domain (opened 2026-09-15)

One domain, several slices. Tendencies: [function-plan](../function-plan.md) §5, [ADR 0010](../adr/0010-instrument-catalog.md), [ADR 0011](../adr/0011-ledger-cash-holdings-reversal.md).

Working mode: feature map in [function-plan](../function-plan.md) §5 → accepted spec in this folder → CLI writes tests in the implementation repo → human writes most production code.

Lock table and port shape in the Feature Spec. Do not implement from ADR 0010 / 0011 alone. Do not add empty files for slices that have not been discussed.

| Spec | Status | Who | Ships | Why it stays its own slice |
| --- | --- | --- | --- | --- |
| [instruments](instruments.md) | accepted (landed in repo 2026-09-20 `9ea2f2a`; IsActive rename `bbd0f26`) | TenantAdmin; Adviser (create + read); SystemAdmin (all verbs, Scalar) | Tenant catalog, `/instruments`, TestSeed. Prices and FX are the later platform caches | Holdings store `InstrumentId` only; catalog must exist first |
| [accounts](accounts.md) | accepted (landed in repo 2026-09-21 `3e8b8f3`) | TenantAdmin; Adviser (assigned Customers); SystemAdmin (all verbs, Scalar) | Account container under a Customer, `/accounts`, close/reopen, `Status` + derived `IsActive`, Disable-Customer guard, TestSeed | Cash / holdings / Opening need a container first |
| [posting](posting.md) | accepted (2026-09-25; landed) | TenantAdmin; Adviser (assigned Customers); SystemAdmin (Scalar) | `/transactions` + cash legs; `0013_transactions.sql`; close amends accounts R16; `cashBalance` | Write model for booked activity; holdings / securities may amend this file or split later |
| [holdings](holdings.md) | accepted (landed in repo 2026-10-11 `676e77a`) | TenantAdmin; Adviser (assigned Customers); SystemAdmin (Scalar) | `OpeningHoldings`, security legs, `Holdings` projection, `0014_holdings.sql`, `0015_transaction_type_opening_holdings.sql`, `GET /accounts/{id}/holdings`. Portal out | Opening of holdings. Posting keeps the header and cash leg |
| [buy-sell](buy-sell.md) | accepted (landed in repo 2026-10-12 `821c715`) | TenantAdmin; Adviser (assigned Customers); SystemAdmin (Scalar) | `Buy` / `Sell` / `StockIn` / `StockOut` on the same Transaction; server price on Buy/Sell; client cost on StockIn; average-cost release. Item shape `cash` + `security`. `0016_transaction_type_stock_in_out.sql`. Portal out | Security activity after Opening. Split / scrip next. |
| [split](split.md) | accepted (2026-10-12) | TenantAdmin; Adviser (assigned Customers); SystemAdmin (Scalar) | `Split` / `Bonus` / `Scrip`; security-only; new total quantity; total cost unchanged. No ratio on this write. `0017_transaction_type_split.sql` widens the type check to 0–14. Portal out | Corporate-action quantity change. Not StockIn/StockOut. |
| [net-worth](net-worth.md) | accepted (landed 2026-10-12 `af422c2`) | TenantAdmin; Adviser (assigned Customers); SystemAdmin (Scalar) | Current point-in-time read model. `GET /net-worth` and `GET /users/customers/{id}/net-worth`. Per-currency arrays + breakdowns. No script. Reuse `accounts.read`. BFF prefix `/net-worth`. Portal Dashboard accepted | Read model after securities activity. No new tables. |
| [portfolios](portfolios.md) | accepted (2026-11-02) | TenantAdmin; Adviser (assigned Customers); SystemAdmin (Scalar). Customer 403 on writes | Model portfolios with base currency and target weights. Assign rebalances synchronously. Weight change and bulk rebalance are async (202). While assigned, manual security trades rejected; cash in/out allowed. One Active assignment per account. Brokerage/Other only. Script `0023_portfolios.sql`. Portal out | Strategy layer on top of existing holdings and transactions. |
| [leg-accounts](leg-accounts.md) | accepted (2026-11-10; landed on `feature/leg-accounts`) | Same callers as posting | Header drops `AccountId`; cash and security legs carry `AccountId`; per-leg item JSON; read = any visible leg account, legs filtered; write = every leg account. `0024_leg_accounts.sql`. No new type or route | Structural groundwork for cross-account transfer (posting §13). Transfer itself is the next increment |
| [internal-transfers](internal-transfers.md) | accepted (2026-11-10; landed on `feature/internal-transfers`) | TenantAdmin; Adviser (both Customers assigned); SystemAdmin (Scalar). Customer read only | `InternalTransfer` type; two cash legs on two Accounts in one tenant, same currency, cash only; `POST /transactions/transfer`; optional `type` list filter; `0025` widens the type check to 0–15; Adviser Portal two-step destination picker | First type whose legs book into two Accounts. Builds on leg-accounts |
| [fx-conversions](fx-conversions.md) | accepted (2026-11-10; landed on `feature/fx-conversions`) | TenantAdmin; Adviser (Customer assigned); SystemAdmin (Scalar). Customer read only | `FxConversion` type; two cash legs on two Accounts of different currencies of the same Customer; server-priced from `FxRates`; `POST /transactions/fx` and read-only `POST /transactions/fx/quote`; `TransactionFxDetails`; `0026` | First type with legs in two currencies and a stored rate. Builds on internal-transfers |
| [cross-currency-trades](cross-currency-trades.md) | draft (2026-11-10) | TenantAdmin; Adviser; rebalance | Amends buy-sell, holdings, portfolios: manual `Buy` / `StockIn` / `OpeningHoldings` must match the Account currency; manual `Sell` and rebalance keep automatic FX and store the rate in `TransactionFxDetails`; cash leg rounded to `DecimalPlaces`; `stale_fx`. No script | Needs fx-conversions first |
| [bff-adviser-portal](bff-adviser-portal.md) | accepted (2026-10-01; landed `5e85e9c`) | Adviser Portal session | Aspire `bff-adviser-portal`; confidential client; cookie + OIDC + HttpClient proxy; tokens out of the browser | Platform session increment, not a ledger slice. ADR 0016 accepted. Portal cut: [adviser-portal-bff-cut.md](../adviser-portal/adviser-portal-bff-cut.md) |

Suggested order (tendency, not a locked backlog). Cash is not its own spec; it lives in posting:

```text
instruments
    └── account container
            └── posting (journal + cash legs; first increment)
                    └── securities / holdings / Opening holdings
                            └── Buy / Sell / StockIn / StockOut
                                    └── split / scrip / bonus
                                            └── net-worth read model + Dashboard
```

---

## Phase 3 — Reports (opened for design 2026-10-12)

One domain. Background execution lives in the general-purpose `worker` (ADR 0017).

| Spec | Status | Who | Ships | Why it stays its own slice |
| --- | --- | --- | --- | --- |
| [reports](reports.md) | accepted | Customer (`reports.customer.read` only); Adviser; TenantAdmin (all four report policies). SystemAdmin has none of the four. SystemAdmin lists the platform catalog with `GET /reports/catalog` and grants active reports with `tenants.manage` | Catalog with scope (`Customer` \| `Book`). TenantReports. Shared pipeline. Customer Transactions + Adviser Balances. PDF via code layout. Manual + scheduled both. Policies `reports.customer.*` / `reports.book.*`. | Two scopes force the catalog and policy split. |
| [outbox](outbox.md) | accepted | Platform (no HTTP caller) | Transactional `OutboxMessages`. Worker delivers domain events via MediatR. Replaces immediate post-save dispatch. ADR 0018 accepted. Script `0020_outbox.sql`. | Cross-cutting delivery. Not a report and not a ledger slice. |
| [fx-rates](fx-rates.md) | accepted (2026-11-02) | Platform (worker job; any Application caller of `IFxRate`) | Hourly Hangfire job pulls Frankfurter daily rates for active catalog currencies when today's source date is not already stored. Table `FxRates`. Script `0021_fx_rates.sql`. ADR 0019 accepted. HTTP read surface deferred. | Platform rate cache. Consumers (net-worth fold, instrument prices) are later. |
| [instrument-prices](instrument-prices.md) | accepted (2026-11-02) | Platform (worker job; any Application caller of `IMarketData`) | Daily Hangfire job at 12:00 UTC pulls EODHD EOD prices for active instruments when today's source date is not already stored. Table `InstrumentPrices`. Script `0022_instrument_prices.sql`. No new ADR (follows ADR 0019). HTTP read surface deferred. | Platform price cache. `IMarketData` reads the table. |

Invitation / Activation and Fees are deferred.

---

## Phase 3+ — Customer Portal (accepted 2026-11-02)

Second first-party portal. One BFF + one confidential client, same shape as the Adviser Portal (ADR 0016). Portal page contract lives in [customer-portal/](../customer-portal/README.md).

| Spec | Status | Who | Ships | Why it stays its own slice |
| --- | --- | --- | --- | --- |
| [bff-customer-portal](bff-customer-portal.md) | accepted | Customer Portal session | Aspire `bff-customer-portal`; confidential client `customer-portal`; cookie + OIDC + HttpClient proxy; tokens out of the browser; prefix allow-list for self reads + report request | Platform session increment, not a ledger slice. Portal: [../customer-portal/customer-portal.md](../customer-portal/customer-portal.md) |

Required backend amendments (self-scope reads and report request for Customer) are accepted in identity-auth, accounts, posting, net-worth, reports, and holdings. They land with this slice; do not rebuild the resource API.
