# MyWealthV2

A multi-tenant SaaS wealth management platform for advice firms.

Built with **.NET 10 + Aspire** and Clean Architecture, two React portals behind their own BFFs, a Blazor Back Office, one shared SQL Server database, and a simulator host that plays the outside parties (bank, broker, custodian, market data vendor).
The design source of truth lives in [`docs/`](docs/). Every behaviour is specified there first and built test-first.

## What it does

- **Firms and people.** A SystemAdmin runs the platform and its tenants (advice firms). Each tenant has TenantAdmins, Advisers and Customers. An Adviser works on the Customers assigned to them (their Book).
- **Ledger.** Accounts under each Customer, with a journal of transactions and cash and security legs: transfers in and out, interest, dividends, Opening balances and holdings, Buy / Sell / StockIn / StockOut, Split / Bonus / Scrip, reversals, internal transfers between Accounts, and FX conversions. Every Transaction records its source (manual entry, seed, rebalance, bank feed, payment, and so on).
- **Custody modes.** Every Account is `External` (held elsewhere; the adviser types the executed price, fee and rate) or `Custodial` (held by the firm; the platform runs the money and the trades). Each Tenant chooses which modes it allows. Net worth, reports and both portals group by custody mode and show funds under management.
- **Custodial operations.** For Custodial accounts:
  - **Money:** a client money (trust) bank account per Tenant and currency, deposit references, a bank feed that matches receipts to deposits with a suspense queue, nominated bank accounts and withdrawals approved by a second person, bank returns, recalls and dishonours, a daily bank reconciliation, and account closure with a final payout.
  - **Trades:** orders sent to a broker, fills, settlement through the trust account, and broken trades. A model portfolio rebalance is a proposal that a person confirms and that runs as orders, sells first.
  - **Securities:** in-kind transfers in and out through a custodian safekeeping account, with custodian reconciliation, and migration batches that load opening cash and holdings from a previous provider against control totals.
  - **Income and events:** a daily interest run with RWT from each Customer's tax profile, corporate actions (split, consolidation, bonus), and cash dividends with reinvestment elections.
  - **Adjustments:** a TenantAdmin corrects cash or a holding with a reason. The change is a request that another TenantAdmin approves before it posts.
- **Fees.** Tenant trade fee schedules per quote currency (marginal bands, minimum, cap, effective dates). Every `Buy` / `Sell` carries a `Fee` leg, and rebalances take fees into account.
- **Holdings and net worth.** An average-cost holdings projection and a point-in-time net-worth read model, in each currency, with breakdowns by customer, account type, top holdings and custody mode.
- **Valuation of a past day.** `GET /accounts/{id}/valuation?date=` values one account at the end of a UTC day from the ledger and the stored prices and rates: each holding in its quote currency and in the account's currency. A price or rate older than 4 days, or none, leaves the holding not valued and the valuation incomplete; nothing is counted as 0.
- **Daily valuation run.** Every day at 13:00 UTC the `worker` stores the valuation of every Custodial account for each day not yet stored, up to the day before yesterday, when its prices and rates are final: the totals, the `complete` mark and each holding with the price and rate it used. A stored day is never changed, and a missed day is caught up by the next run. `GET /accounts/{id}/valuations` reads the series; a SystemAdmin can run a Tenant through a date from the Back Office (`POST /valuation-runs`).
- **Valuation history.** `GET /net-worth/funds-under-management` gives the funds under management of the firm, an Adviser's Customers or one Customer for every day of a range, from the stored valuations, per currency and folded into the firm's reporting currency at the stored rate of each day; a day says when an account or a currency is left out. Net worth now carries the same fold at today's rates (`folded`). The Adviser Balances Report can be made for the end of a past day. Both Dashboards and the page of a Custodial account chart the figures.
- **Model portfolios.** Target weights and a base currency, assigned to Custodial accounts and rebalanced through orders.
- **Reports.** A platform catalog granted per tenant. Customer, Book and Tenant reports as PDF, generated on request or on a schedule.
- **Market data.** End-of-day instrument prices (EODHD) and FX rates (Frankfurter), stored by the worker for every date from a configured history floor date, with a final rule: a row two or more days old never changes. `GET /instruments/{id}/prices` and `GET /fx-rates` read the history. Instruments with the `.SIM` suffix are priced by the simulator market instead.
- **Simulator.** A separate host with its own database that plays the bank, broker, custodian, market data vendor and issuer behind the platform's ports. A SimulatorOperator controls its clock, outages and resets through an OIDC-protected API with Scalar.

## Current status

**Phase 1 — platform foundation.** Landed. Identity (OpenIddict, hosted login), tenants, currencies, people (SystemAdmin / TenantAdmin / Adviser / Customer), Adviser Portal shell and BFF.

**Phase 2 — ledger domain.** Landed. Instruments, accounts, posting, holdings, buy/sell, split/bonus/scrip, net worth, portfolios, per-leg accounts, internal transfers, FX conversions. Adviser Portal Dashboard, ledger and portfolio pages.

**Phase 3 — platform services and the Customer Portal.** Landed. Reports (catalog, grants, generation, schedules, history), transactional outbox, FX rates, instrument prices, Customer Portal with its own BFF and confidential client.

**Production hosting.** Landed and deployed. The showcase runs on one VPS with Docker Compose behind Caddy. GitHub Actions builds the images, deploys them, runs a Playwright smoke test against both portals and rolls back on failure. A demo tenant can be seeded with `webapi --seed-demo`. The simulator runs on the VPS behind a compose profile that is off unless `.env` turns it on. See [`docs/deployment.md`](docs/deployment.md).

**Custody modes.** Landed. External and Custodial accounts, the allowed modes per Tenant, caller-supplied numbers on External accounts, and grouping by custody mode in net worth, reports and both portals ([ADR 0020](docs/adr/0020-account-custody-mode.md)).

**Custodial roadmap.** Steps 1–15 and 17 landed: transaction source, the simulator, adjustments, client money accounts, deposit matching, withdrawals, bank returns, bank reconciliation, account closure, the interest run, simulated instruments, corporate actions, orders, trade settlement, rebalances through orders, migration batches, in-kind transfers, dividends, and adjustment review. Step 16, an end-to-end run across all parties, is next and not yet specified. See [`docs/custodial-roadmap.md`](docs/custodial-roadmap.md).

**Business roadmap.** New Zealand rules, one step at a time ([`docs/future-plans.md`](docs/future-plans.md) §1). Step 1, trade fees, landed. Step 3, historical valuation, has its own plan in [`docs/valuation-roadmap.md`](docs/valuation-roadmap.md): V1, price and rate history, V2, valuation as of a date, and V3, the daily valuation run, landed, which completes step 3. V4, valuation history (the funds under management series, the FX fold and the Adviser Balances Report for a past date), landed too. The advisory fee, performance report (TWR / MWR) and FIF tax report follow.

**Later.** Cross-currency trades are in draft. KYC, households, advice documents, and invitation and activation follow one domain at a time.

See [`docs/function-plan.md`](docs/function-plan.md) and [`docs/features/README.md`](docs/features/README.md) for the feature map. Accepted gaps are listed in [`docs/known-issues/`](docs/known-issues/README.md).

## Tech stack

| Layer | Technology |
|-------|------------|
| Orchestration (Development) | .NET Aspire |
| Backend | .NET 10, ASP.NET Core Minimal APIs, MediatR (CQRS), EF Core |
| Identity | OpenIddict (authorization code + PKCE, confidential clients), hosted login with lockout |
| Database | SQL Server. Schema truth is versioned SQL scripts in `database/schema/` |
| Adviser Portal / Customer Portal | React, Redux Toolkit, TypeScript, Vite, Tailwind CSS, shadcn/ui |
| Back Office | Blazor + MudBlazor |
| Background work | Hangfire in the worker host |
| Reports | QuestPDF |
| Tests | NUnit, Shouldly, Respawn, Aspire testing, Vitest + Testing Library, Playwright (smoke) |
| Simulator | ASP.NET Core host with its own SQL Server database, Scalar for its control API |
| Observability | OpenTelemetry via Aspire |
| Delivery | GitHub Actions, GHCR, Docker Compose, Caddy |

## Architecture highlights

- Clean Architecture layers: Domain → Application → Infrastructure → Web
- Multi-tenant shared database with dual-check isolation: EF global filter, explicit checks, and cross-tenant tests (another tenant's id → 404)
- BFF pattern for both portals (`bff-adviser-portal`, `bff-customer-portal`): the SPA never holds tokens; the BFF serves the SPA build in production
- Public ids are UUIDs; internal integer keys never appear on the wire
- No EF migrations: the Web API applies versioned SQL scripts on start
- Transactional outbox for domain events, delivered by the worker
- Outside parties sit behind ports (bank feed and payments, broker, custodian, market prices), with simulator adapters; every pull job survives the simulator being down ([ADR 0021](docs/adr/0021-outside-parties-behind-ports.md))
- Every 409 carries a problem code the portals can branch on
- Production hosts: chiseled non-root images, health checks on a separate port, per-host Data Protection keys, forwarded headers trusted from the internal network only

Full details: [`docs/architecture.md`](docs/architecture.md).

## Quick start (Development)

**Prerequisites**

- .NET 10 SDK
- Node.js and npm (for the two portals)
- Docker (for the SQL Server container)

```bash
git clone https://github.com/louis297/MyWealthV2.git
cd MyWealthV2

dotnet run --project src/AppHost
```

The AppHost asks for its secret parameters on first run: the four OIDC client secrets (Adviser Portal, Customer Portal, Back Office, simulator), the simulator party API key and the EODHD API key. You can also set them as user secrets on `src/AppHost`. Development seeds a test tenant with Custodial accounts, a SystemAdmin and a SimulatorOperator, and the simulator opens the matching trust, safekeeping and market accounts.

The Aspire dashboard opens and lists every resource URL: Identity, Web API, both BFFs and portals, Back Office, the worker and the simulator.

**Tests.** `dotnet test` runs the .NET suites; the database-backed suites start their own SQL Server container. On a developer machine, the test SQL Server can run on tmpfs: see [`tests/TestAppHost/sql-tmpfs/README.md`](tests/TestAppHost/sql-tmpfs/README.md).

## Documentation

| Document | Purpose |
|----------|---------|
| [`docs/README.md`](docs/README.md) | How documentation works and the current phase |
| [`docs/function-plan.md`](docs/function-plan.md) | Scope, roles, and phase map |
| [`docs/features/README.md`](docs/features/README.md) | Feature Spec index and status |
| [`docs/custodial-roadmap.md`](docs/custodial-roadmap.md) | Plan and order of the Custodial account work |
| [`docs/valuation-roadmap.md`](docs/valuation-roadmap.md) | Plan for historical valuation (V1–V4) |
| [`docs/architecture.md`](docs/architecture.md) | Architecture and layering |
| [`docs/adr/`](docs/adr/) | Architecture decision records |
| [`docs/domain-model.md`](docs/domain-model.md) | Domain model |
| [`docs/database-design.md`](docs/database-design.md) | Schema design notes |
| [`docs/api-design.md`](docs/api-design.md) | HTTP conventions |
| [`docs/glossary.md`](docs/glossary.md) | Shared terms |
| [`docs/adviser-portal/`](docs/adviser-portal/) | Adviser Portal page contracts |
| [`docs/customer-portal/`](docs/customer-portal/) | Customer Portal page contract |
| [`docs/backoffice/`](docs/backoffice/) | Back Office |
| [`docs/deployment.md`](docs/deployment.md) | VPS topology, CD pipeline, secrets, rebuild runbook |
| [`docs/smoke-test.md`](docs/smoke-test.md) | Post-deploy smoke test and the smoke tenant |
| [`docs/known-issues/`](docs/known-issues/) | Accepted gaps and how to check them |
| [`docs/fixes/`](docs/fixes/) | Fix specs |
| [`docs/future-plans.md`](docs/future-plans.md) | Deferred ideas |

## Repository layout

```text
src/
  AppHost/                 # Aspire orchestration (Development)
  IdentityHost/            # OpenIddict authorization server + hosted login
  Web/                     # Resource API (Minimal APIs)
  BffAdviserPortal/        # Adviser Portal BFF
  BffCustomerPortal/       # Customer Portal BFF
  AdviserPortal/           # React Adviser Portal
  CustomerPortal/          # React Customer Portal
  BackOffice/              # Blazor Back Office (SystemAdmin)
  Worker/                  # Hangfire background worker
  Simulator/               # Outside parties: bank, broker, custodian, market
  Schema/                  # Schema applicator for the versioned SQL scripts
  ServiceDefaults/         # Shared host start-up
  Shared/                  # Code shared by the hosts
  Domain/ Application/ Infrastructure/   # Clean Architecture layers
tests/
  Domain.UnitTests/ Application.UnitTests/
  Application.FunctionalTests/ Infrastructure.IntegrationTests/
  Simulator.Tests/
  Smoke.Tests/             # Playwright post-deploy smoke test
  TestAppHost/ Common/     # Test infrastructure
database/schema/           # Versioned SQL scripts (source of truth)
database/simulator/        # SQL scripts of the simulator database
deploy/                    # Compose project, Caddy, deploy and rollback scripts
.github/workflows/         # CI, deploy, smoke
docs/                      # Design source of truth
```

## Contributing / agents

Coding and commit rules for agents live in the repository-root `CLAUDE.md`.

## License

Not yet decided. This is a learning and showcase project.
