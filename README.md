# MyWealthV2

A multi-tenant SaaS wealth management platform for advice firms.

Built with **.NET 10 + Aspire** and Clean Architecture, two React portals behind their own BFFs, a Blazor Back Office, and one shared SQL Server database.
The design source of truth lives in [`docs/`](docs/). Every behaviour is specified there first and built test-first.

## What it does

- **Firms and people.** A SystemAdmin runs the platform and its tenants (advice firms). Each tenant has TenantAdmins, Advisers and Customers. An Adviser works on the Customers assigned to them (their Book).
- **Ledger.** Accounts under each Customer, with a journal of transactions and cash and security legs: transfers in and out, interest, dividends, Opening balances and holdings, Buy / Sell / StockIn / StockOut, Split / Bonus / Scrip, reversals, internal transfers between Accounts, and FX conversions priced from stored rates.
- **Holdings and net worth.** An average-cost holdings projection and a point-in-time net-worth read model, in each currency, with breakdowns.
- **Model portfolios.** Target weights and a base currency. Assigning a portfolio to an Account rebalances it; weight changes and bulk rebalances run in the background.
- **Reports.** A platform catalog granted per tenant. Customer and Book reports as PDF, generated on request or on a schedule.
- **Market data.** Daily FX rates (Frankfurter) and end-of-day instrument prices (EODHD), cached by the worker.

## Current status

**Phase 1 — platform foundation.** Landed. Identity (OpenIddict, hosted login), tenants, currencies, people (SystemAdmin / TenantAdmin / Adviser / Customer), Adviser Portal shell and BFF.

**Phase 2 — ledger domain.** Landed. Instruments, accounts, posting, holdings, buy/sell, split/bonus/scrip, net worth, portfolios, per-leg accounts, internal transfers, FX conversions. Adviser Portal Dashboard, ledger and portfolio pages.

**Phase 3 — platform services and the Customer Portal.** Landed. Reports (catalog, grants, generation, schedules, history), transactional outbox, FX rates, instrument prices, Customer Portal with its own BFF and confidential client.

**Production hosting.** Landed and deployed. The showcase runs on one VPS with Docker Compose behind Caddy. GitHub Actions builds the images, deploys them, runs a Playwright smoke test against both portals and rolls back on failure. See [`docs/deployment.md`](docs/deployment.md).

**Next.** Cross-currency trades are in design. Later domains (KYC, households, advice documents, fees, invitation and activation) follow one domain at a time.

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
| Observability | OpenTelemetry via Aspire |
| Delivery | GitHub Actions, GHCR, Docker Compose, Caddy |

## Architecture highlights

- Clean Architecture layers: Domain → Application → Infrastructure → Web
- Multi-tenant shared database with dual-check isolation: EF global filter, explicit checks, and cross-tenant tests (another tenant's id → 404)
- BFF pattern for both portals (`bff-adviser-portal`, `bff-customer-portal`): the SPA never holds tokens; the BFF serves the SPA build in production
- Public ids are UUIDs; internal integer keys never appear on the wire
- No EF migrations: the Web API applies versioned SQL scripts on start
- Transactional outbox for domain events, delivered by the worker
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

The AppHost asks for its secret parameters (the three OIDC client secrets and the EODHD API key) on first run, or you can set them as user secrets on `src/AppHost`. Development seeds a test tenant and a SystemAdmin.

The Aspire dashboard opens and lists every resource URL: Identity, Web API, both BFFs and portals, Back Office and the worker.

## Documentation

| Document | Purpose |
|----------|---------|
| [`docs/README.md`](docs/README.md) | How documentation works and the current phase |
| [`docs/function-plan.md`](docs/function-plan.md) | Scope, roles, and phase map |
| [`docs/features/README.md`](docs/features/README.md) | Feature Spec index and status |
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
  ServiceDefaults/         # Shared host start-up
  Shared/                  # Code shared by the hosts
  Domain/ Application/ Infrastructure/   # Clean Architecture layers
tests/
  Domain.UnitTests/ Application.UnitTests/
  Application.FunctionalTests/ Infrastructure.IntegrationTests/
  Smoke.Tests/             # Playwright post-deploy smoke test
  TestAppHost/ Common/     # Test infrastructure
database/schema/           # Versioned SQL scripts (source of truth)
deploy/                    # Compose project, Caddy, deploy and rollback scripts
.github/workflows/         # CI, deploy, smoke
docs/                      # Design source of truth
```

## Contributing / agents

Coding and commit rules for agents live in the repository-root `CLAUDE.md`.

## License

Not yet decided. This is a learning and showcase project.
