# MyWealthV2

A multi-tenant SaaS wealth management platform.

Built with **.NET Aspire** + Clean Architecture, a React Adviser Portal, and a shared SQL Server database.  
The design source of truth lives in [`docs/`](docs/).

## Current status

- **Phase 1** (platform foundation) — accepted and landed: identity (OpenIddict), tenancy, currencies, people (SystemAdmin / TenantAdmin / Adviser / Customer), Adviser Portal shell + BFF.
- **Phase 2** (ledger domain) — in progress: instruments, accounts, posting, holdings, buy/sell, split/bonus/scrip, net-worth, reports.
- Later phases (Customer Portal, KYC, households, advice documents, fees, etc.) are planned one domain at a time.

See [`docs/function-plan.md`](docs/function-plan.md) and [`docs/README.md`](docs/README.md) for the phase map and documentation rules.

## Tech stack

| Layer | Technology |
|-------|------------|
| Orchestration | .NET Aspire |
| Backend | .NET 10, ASP.NET Core Minimal APIs, MediatR (CQRS), EF Core |
| Identity | OpenIddict (authorization code + PKCE), hosted login |
| Database | SQL Server (Docker, managed by Aspire). Schema truth = versioned SQL scripts in `database/schema/` |
| Adviser Portal | React + Redux Toolkit + TypeScript + Vite + Tailwind CSS |
| Back Office | Blazor + MudBlazor |
| Background | Hangfire (worker host) |
| Observability | OpenTelemetry via Aspire |

## Architecture highlights

- Clean Architecture layers: Domain → Application → Infrastructure → Web
- Multi-tenant shared database with dual-check isolation (EF global filter + explicit checks + tests)
- BFF pattern for the Adviser Portal (`bff-adviser-portal`) — SPA never holds tokens
- Public IDs are UUIDs; internal integer keys never appear on the wire
- No EF migrations — schema is applied from versioned SQL scripts

Full details: [`docs/architecture.md`](docs/architecture.md).

## Quick start

**Prerequisites**

- .NET 10 SDK
- Docker (for the SQL Server container and Aspire orchestration)

```bash
# Clone and run
git clone https://github.com/louis297/MyWealthV2.git
cd MyWealthV2

dotnet run --project src/AppHost
```

The Aspire dashboard opens automatically and shows all resource URLs (Identity, Web API, BFF, Adviser Portal, Back Office, etc.).

## Documentation

| Document | Purpose |
|----------|---------|
| [`docs/README.md`](docs/README.md) | How documentation works and current phase |
| [`docs/function-plan.md`](docs/function-plan.md) | Scope, roles, and phase map |
| [`docs/architecture.md`](docs/architecture.md) | Hosts, layers, ports, cross-cutting concerns |
| [`docs/glossary.md`](docs/glossary.md) | Shared terminology |
| [`docs/features/`](docs/features/) | Feature Specs (the build contracts for accepted slices) |
| [`docs/portals/`](docs/portals/) | Adviser Portal page contracts |

Only **accepted** specs for the current phase are implementation contracts.

## Development notes

Scaffolding commands, EditorConfig usage, and template-specific instructions that were previously in this README have been moved to [`DEVELOPMENT.md`](DEVELOPMENT.md).

Agent-specific rules (TDD, commit discipline, hand-off protocol) live in [`AGENTS.md`](AGENTS.md).

## Project structure

```
src/
  AppHost/              # Aspire orchestration
  IdentityHost/         # OpenIddict authorization server
  Web/                  # Resource API (Minimal APIs + MediatR)
  BffAdviserPortal/     # BFF for the Adviser Portal
  AdviserPortal/        # React SPA
  BackOffice/           # Blazor Back Office
  Worker/               # Hangfire background worker
  Application/          # Commands, queries, ports
  Domain/               # Entities, value objects
  Infrastructure/       # EF mappings, OpenIddict stores, adapters
database/schema/        # Versioned SQL scripts (schema source of truth)
docs/                   # Design source of truth
tests/                  # Unit, integration, and functional tests
```

## License

Not yet decided. This repository is currently private / learning-oriented.
