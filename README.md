# MyWealthV2

A multi-tenant SaaS wealth management platform for advice firms: a ledger of cash and securities, custody operations, valuations, reports, and portals for advisers and their customers.

Built with **.NET 10 + Aspire** and Clean Architecture. Two React portals sit behind their own BFFs, next to a Blazor Back Office, one shared SQL Server database, and a simulator host that plays the outside parties (bank, broker, custodian, market data vendor).

This is a learning and showcase project. The design lives in [`docs/`](docs/): every behaviour is specified there first and built test-first.

## What it does

- **Firms and people.** A SystemAdmin runs the platform and its tenants (advice firms). Each tenant has TenantAdmins, Advisers and Customers.
- **Ledger.** Accounts with a journal of cash and security legs: transfers, interest, dividends, buys and sells, corporate actions, reversals, internal transfers and FX conversions.
- **Custody modes.** An account is `External` (held elsewhere, recorded by the adviser, who enters the outside party's numbers: prices, fees, FX rates, and dividends and interest with the tax withheld) or `Custodial` (held by the firm, which runs the money and the trades).
- **Custodial operations.** Client money accounts, deposit matching, withdrawals approved by a second person, bank and custodian reconciliation, orders and settlement through a broker, in-kind transfers, interest runs, corporate actions and dividends.
- **Holdings, net worth and valuation.** Average-cost holdings, net worth per currency, a daily valuation of every Custodial account, and funds under management over time in the firm's reporting currency.
- **Model portfolios and fees.** Target weights rebalanced through orders, and trade fee schedules per tenant.
- **Reports.** Customer, Book and Tenant reports as PDF, on request or on a schedule.
- **Market data.** End-of-day prices (EODHD) and FX rates (Frankfurter), with history.
- **Simulator.** A separate host that plays the bank, broker, custodian and market behind the platform's ports, with its own clock and outages.

The full feature map is in [`docs/function-plan.md`](docs/function-plan.md), and every feature has a spec under [`docs/features/`](docs/features/README.md).

## Status

The platform foundation, the ledger, reports, the Customer Portal, custody modes, most of the Custodial roadmap, trade fees, historical valuation and income events (dividends and interest with their tax, on both custody modes) have landed. The FIF tax report has its five parts: each Instrument states its issuer country and its FIF treatment per tax year, a read turns one Customer's ledger into the tax events of a tax year in NZD, and the same read gives the cost in NZD of each holding, the NZ$50,000 threshold test of the year, and the FIF income under the fair dividend rate method, with its quick sale adjustment, and under the comparative value method. The FIF Report prints that read as a PDF for one Customer and one tax year, from the Customer page of the Adviser Portal and the Reports page of the Customer Portal. The Tax Year Income Summary, from the same two places, lists the dividends and interest of one Customer and one tax year in NZD, with the tax taken and the imputation credits. The showcase is deployed on one VPS with Docker Compose behind Caddy, by GitHub Actions.

Next: an end-to-end run across all outside parties, then the advisory fee, performance reports (TWR / MWR) and the FIF tax report.

| For | See |
|-----|-----|
| Feature Specs and their status | [`docs/features/README.md`](docs/features/README.md) |
| Custodial account work | [`docs/roadmaps/custodial-roadmap.md`](docs/roadmaps/custodial-roadmap.md) |
| Historical valuation | [`docs/roadmaps/valuation-roadmap.md`](docs/roadmaps/valuation-roadmap.md) |
| What comes later | [`docs/future-plans.md`](docs/future-plans.md) |
| Accepted gaps | [`docs/known-issues/`](docs/known-issues/README.md) |

## Tech stack

| Layer | Technology |
|-------|------------|
| Backend | .NET 10, ASP.NET Core Minimal APIs, MediatR (CQRS), EF Core, Hangfire |
| Identity | OpenIddict (authorization code + PKCE), hosted login |
| Database | SQL Server, with versioned SQL scripts in `database/schema/` as schema truth |
| Portals | React, Redux Toolkit, TypeScript, Vite, Tailwind CSS, shadcn/ui |
| Back Office | Blazor + MudBlazor |
| Tests | NUnit, Shouldly, Respawn, Aspire testing, Vitest, Playwright |
| Delivery | .NET Aspire (Development), GitHub Actions, GHCR, Docker Compose, Caddy |

## Architecture

- Clean Architecture layers: Domain → Application → Infrastructure → Web.
- One shared database for all tenants. Isolation is checked twice (EF global filter and explicit checks) and proven by cross-tenant tests.
- BFF pattern for both portals: the SPA never holds tokens.
- Public ids are UUIDs. Internal integer keys never appear on the wire.
- No EF migrations: versioned SQL scripts are applied on start.
- Domain events go through a transactional outbox, delivered by the worker.
- Outside parties sit behind ports, with simulator adapters.

More in [`docs/architecture.md`](docs/architecture.md) and the decision records in [`docs/adr/`](docs/adr/README.md).

## Quick start

You need the .NET 10 SDK, Node.js with npm, and Docker.

```bash
git clone https://github.com/louis297/MyWealthV2.git
cd MyWealthV2
dotnet run --project src/AppHost
```

On first run the AppHost asks for its secret parameters: the OIDC client secrets, the simulator API key and the EODHD API key. The Aspire dashboard then lists the URL of every resource. Development seeds a test tenant, a SystemAdmin and a SimulatorOperator.

`dotnet test` runs the .NET suites. The database-backed suites start their own SQL Server container.

## Documentation

Start at [`docs/README.md`](docs/README.md). Other entry points:

- [`docs/glossary.md`](docs/glossary.md): shared terms
- [`docs/domain-model.md`](docs/domain-model.md), [`docs/database-design.md`](docs/database-design.md), [`docs/api-design.md`](docs/api-design.md): domain, schema and HTTP conventions
- [`docs/adviser-portal/`](docs/adviser-portal/README.md), [`docs/customer-portal/`](docs/customer-portal/README.md), [`docs/backoffice/`](docs/backoffice/README.md): the three user interfaces
- [`docs/deployment.md`](docs/deployment.md) and [`docs/smoke-test.md`](docs/smoke-test.md): hosting, the delivery pipeline and the post-deploy smoke test

Coding and commit rules for agents are in [`CLAUDE.md`](CLAUDE.md).

## License

Not yet decided.
