# MyWealthV2

A multi-tenant SaaS wealth management platform.

Built with **.NET Aspire** + Clean Architecture, a React Adviser Portal, and a shared SQL Server database.  
The design source of truth lives in [`docs/`](docs/).

## Current status

**Phase 1** (platform foundation) — accepted and landed:  
identity (OpenIddict), tenancy, currencies, people (SystemAdmin / TenantAdmin / Adviser / Customer), Adviser Portal shell + BFF.

**Phase 2** (ledger domain) — largely landed:  
instruments, accounts, posting (cash + journal), holdings, buy/sell/stock in-out, split/bonus/scrip, net-worth read model, portfolios (model portfolios + rebalance).  
Adviser Portal Dashboard and ledger pages accepted. Portfolios portal contract accepted.

**Phase 3** (platform services + second portal) — accepted and largely landed:  
reports (catalog, grants, generate, schedules, history), transactional outbox, FX rates (Frankfurter), instrument prices (EODHD EOD).  
Customer Portal (second BFF + confidential client) accepted.

Later domains (KYC, households, advice documents, fees, etc.) are planned one domain at a time.

See [`docs/function-plan.md`](docs/function-plan.md) and [`docs/features/README.md`](docs/features/README.md) for the detailed feature map and landing status.

## Tech stack

| Layer | Technology |
|-------|------------|
| Orchestration | .NET Aspire |
| Backend | .NET 10, ASP.NET Core Minimal APIs, MediatR (CQRS), EF Core |
| Identity | OpenIddict (authorization code + PKCE), hosted login |
| Database | SQL Server (Docker, managed by Aspire). Schema truth = versioned SQL scripts in `database/schema/` |
| Adviser Portal | React + Redux Toolkit + TypeScript + Vite + Tailwind CSS |
| Customer Portal | React (second first-party portal, accepted) |
| Back Office | Blazor + MudBlazor |
| Background | Hangfire (worker host) |
| Observability | OpenTelemetry via Aspire |

## Architecture highlights

- Clean Architecture layers: Domain → Application → Infrastructure → Web
- Multi-tenant shared database with dual-check isolation (EF global filter + explicit checks + tests)
- BFF pattern for first-party portals (`bff-adviser-portal`, `bff-customer-portal`) — SPA never holds tokens
- Public IDs are UUIDs; internal integer keys never appear on the wire
- No EF migrations — schema is applied from versioned SQL scripts
- Transactional outbox for reliable domain event delivery
- Platform market data caches: FX rates and instrument EOD prices

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

The Aspire dashboard opens automatically and shows all resource URLs (Identity, Web API, BFF, Adviser Portal, Back Office, worker, etc.).

## Documentation

| Document | Purpose |
|----------|---------|
| [`docs/README.md`](docs/README.md) | How documentation works and current phase |
| [`docs/function-plan.md`](docs/function-plan.md) | Scope, roles, and phase map |
| [`docs/features/README.md`](docs/features/README.md) | Feature Spec index and landing status |
| [`docs/architecture.md`](docs/architecture.md) | Architecture, ADRs, and layering |
| [`docs/domain-model.md`](docs/domain-model.md) | Domain model |
| [`docs/database-design.md`](docs/database-design.md) | Schema design notes |
| [`docs/api-design.md`](docs/api-design.md) | HTTP conventions |
| [`docs/glossary.md`](docs/glossary.md) | Shared terms |
| [`docs/adviser-portal/`](docs/adviser-portal/) | Adviser Portal page contracts |
| [`docs/customer-portal/`](docs/customer-portal/) | Customer Portal page contract |
| [`docs/backoffice/`](docs/backoffice/) | Back Office |

## Repository layout

```text
src/
  AppHost/                 # Aspire orchestration
  IdentityHost/            # OpenIddict authorization server
  Web/                     # Resource API (Minimal APIs)
  BffAdviserPortal/        # Adviser Portal BFF
  BffCustomerPortal/       # Customer Portal BFF (accepted)
  AdviserPortal/           # React Adviser Portal
  BackOffice/              # Blazor Back Office
  Worker/                  # Hangfire background worker
  Domain/ Application/ Infrastructure/   # Clean Architecture layers
database/schema/           # Versioned SQL scripts (source of truth)
docs/                      # Design source of truth
```

## Contributing / agents

Coding and commit rules for agents live in the repository-root `AGENTS.md`.

## License

Not yet decided. This repository is currently private / learning-oriented.
