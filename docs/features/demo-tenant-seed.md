---
title: Demo tenant seed
status: review
phase: 3
language: en
owner: ""
created: 2026-11-14
last_updated: 2026-11-14
related:
  - ../deployment.md
  - ../future-plans.md
  - ../glossary.md
  - tenants.md
  - instruments.md
  - instrument-prices.md
  - fx-rates.md
  - reports.md
  - production-hosting.md
  - ../known-issues/eodhd-nz-jp-coverage.md
---

# Demo tenant seed

An operator runs a one-shot command on `webapi`, `--seed-demo`, to create the demo tenant and its configuration in any environment, production included. The command writes only the tenant row, its instrument catalog and its report grants. It creates no people and no ledger data. It adds no script, no route and no policy. It amends [tenants.md](tenants.md) R17, which today allows seeding only in Development.

---

## 1. Summary

The **demo seed** is an explicit command, not a start-up step. It creates tenant `demo` (`Demo Advisory`, reporting currency `USD`), the 18 instruments of the **demo catalog**, and grants for both first-phase reports. It is idempotent on natural keys and never changes a row that already exists. Prices and FX rates come from the existing worker jobs, not from this command.

This is the first part of [future-plans.md](../future-plans.md) §5 (demo data for visitors). The shared demo logins and the nightly reset are still open there.

---

## 2. Scope

**In**

- Host: `webapi` accepts `--seed-demo` as its first argument, runs the seed and exits without listening
- Infrastructure: `DemoTenantSeed` (the seed itself) and `DemoCatalog` (the 18 instruments), shared with `TestSeed`
- Development: `TestSeed` and `DevelopmentIdentitySeeder` take the demo tenant code, name and catalog from the same place
- Docs: rebuild runbook step in [deployment.md](../deployment.md) §12, landed with the code
- Tests: Infrastructure.IntegrationTests for the seed, a unit test for the argument check

**Out**

- Running the seed on start in any environment, or from `deploy.sh` or the deploy workflow
- People: TenantAdmin, Adviser, Customer, or any login for visitors ([future-plans.md](../future-plans.md) §5)
- Accounts, Transactions, Holdings, Portfolios, report schedules
- Instrument prices and FX rates. The worker jobs own both ([instrument-prices.md](instrument-prices.md), [fx-rates.md](fx-rates.md))
- Changing or repairing an existing tenant, instrument or grant
- A reset of the demo tenant, and deleting any row
- An HTTP route or a Back Office page for the seed
- A second demo tenant (`demo2` stays a Development fixture)

---

## 3. Stories

1. As the operator, after the first deploy, I run one command so the demo tenant has a working instrument catalog and reports without typing 18 instruments in the Adviser Portal.
2. As the operator, I can run the same command again after a later deploy, and it adds what is missing without touching what is there.
3. As a developer, the local demo tenant and the production demo tenant use the same code, name and instrument list.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | `webapi` runs the seed only when its first argument is exactly `--seed-demo`. It builds the host services, runs the schema applicator and reloads the currency catalog as start-up does, runs the seed, then exits. It does not listen on any port. |
| R2 | In seed mode `webapi` does not run the Development seeds (`DevelopmentIdentitySeeder`, `TestSeed`, `DevelopmentSmokeSeeder`) or `SystemAdminBootstrap`. |
| R3 | The command runs in every environment. Nothing runs it automatically: not host start-up, not `deploy.sh`, not the deploy workflow. |
| R4 | Tenant: Code `demo`, Name `Demo Advisory`, reporting currency `USD`, enabled. It is created through `Tenant.Create`, with the same validation as a tenant created through the API. |
| R5 | When a tenant with Code `demo` already exists, the seed does not change it: name, reporting currency and enabled flag stay as they are. It still ensures the instruments (R6) and grants (R8) for that tenant. A disabled demo tenant is logged as a warning, not an error. |
| R6 | Instruments: every row of the demo catalog (§5.1), all active. Idempotent on `(TenantId, Symbol)`. An existing row with the same symbol is left unchanged, even if its name, quote currency or `IsActive` differ. |
| R7 | The NZD and JPY rows stay in the catalog, although EODHD has no price for them ([known-issues/eodhd-nz-jp-coverage.md](../known-issues/eodhd-nz-jp-coverage.md)). Until it does, valuation, Buy and Sell see no price for them, as on any tenant. |
| R8 | Report grants: `customer-transactions` and `adviser-balances`. Idempotent on `(TenantId, ReportId)`. A platform-inactive catalog report is not granted and is logged as a warning, matching `POST /tenants/{tenantId}/reports`. A missing catalog code is an error (R11). |
| R9 | The seed writes nothing else: no people, accounts, transactions, holdings, portfolios, schedules, instrument prices or FX rates. No other tenant is read or written. |
| R10 | Audit `CreatedBy` = `system`, the same value as `TestSeed`. |
| R11 | All writes of one run are in one database transaction. Any failure rolls the whole run back, logs the error, and exits with code 1. Success exits with code 0. A run that races another run fails on a unique index and can be run again. |
| R12 | The log names the tenant and counts what was created and what already existed, for example `Demo seed: tenant created; instruments 18 created, 0 existing; reports 2 granted, 0 existing`. It never logs a connection string or a secret. |
| R13 | The demo catalog has one definition. `TestSeed` uses it for `demo` and `demo2` in Development and adds its own Development-only extras on top: the disabled `NZD-CASH` row and the fake prices and FX rates. Those extras never reach the command. |
| R14 | The Development demo tenant keeps reporting currency `NZD`. `DevelopmentIdentitySeeder` creates it first, so R5 means the command does not change it when run locally. Only a database without a `demo` tenant gets `USD`. |

---

## 5. Domain

No new types. The seed uses `Tenant.Create`, `Instrument.Create` and `TenantReport.Grant` as they are.

### 5.1 Demo catalog

Symbols are stored as in `TestSeed` today. The instrument-price job maps them to EODHD symbols ([instrument-prices.md](instrument-prices.md) §11).

| Symbol | Name | Quote currency |
| --- | --- | --- |
| VTI | Vanguard Total Stock Market ETF | USD |
| AAPL | Apple Inc. | USD |
| MSFT | Microsoft Corporation | USD |
| AIA | Auckland International Airport | NZD |
| FPH | Fisher & Paykel Healthcare | NZD |
| MEL | Meridian Energy | NZD |
| BHP | BHP Group | AUD |
| CBA | Commonwealth Bank of Australia | AUD |
| WOW | Woolworths Group | AUD |
| SAP | SAP SE | EUR |
| AIR | Airbus SE | EUR |
| OR | L'Oréal | EUR |
| SHEL | Shell plc | GBP |
| HSBA | HSBC Holdings | GBP |
| ULVR | Unilever | GBP |
| 7203 | Toyota Motor | JPY |
| 6758 | Sony Group | JPY |
| 9984 | SoftBank Group | JPY |

18 symbols fit the EODHD free plan (20 calls a day). No other tenant in production has instruments today. A later tenant that adds symbols outside this list shares the same daily limit.

---

## 6. Database

| Table | Change | Index / FK |
| --- | --- | --- |
| — | none | Relies on the existing unique `Tenants.Code`, `(TenantId, Symbol)` on `Instruments` and `(TenantId, ReportId)` on `TenantReports` |

Script: **none**. The seed is not a schema script. Schema scripts run in every database, test databases included, and stay free of tenant rows.

---

## 7. Application use cases

None. The seed is Infrastructure code called from the `webapi` entry point, like `SystemAdminBootstrap`. It is not a command on the HTTP pipeline.

---

## 8. API

None. No route, no policy.

### 8.1 Running it

In production, on the VPS, from `/opt/mywealth`:

```bash
docker compose run --rm webapi --seed-demo
```

`docker compose run` uses the `webapi` service's image, environment and network, so the command reaches `sql` with the same connection string. The running `webapi` container is not affected.

Locally: `dotnet run --project src/Web -- --seed-demo`, with the same connection string the AppHost gives `webapi`.

---

## 9. Deployment

[deployment.md](../deployment.md) §12 gains a step after the smoke tenant step, landed with the code: run the demo seed once. Running it is optional for the deploy; the smoke test does not depend on the demo tenant.

Prices and rates arrive on their own: the instrument-price job on its next daily run (12:00 UTC), and the FX job on its next hourly run. Net worth in `USD` needs both for non-USD instruments.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Application.UnitTests (`Hosting/`, next to `HealthCheckCommandTests`) | `--seed-demo` as first argument is detected; any other first argument, or `--seed-demo` later in the list, is not |
| Infrastructure.IntegrationTests | Empty database after the schema applicator: one run creates tenant `demo` / `Demo Advisory` / `USD`, enabled; 18 active instruments matching §5.1; grants for both reports |
| Infrastructure.IntegrationTests | Second run creates no rows and leaves every row unchanged |
| Infrastructure.IntegrationTests | Existing `demo` tenant with reporting currency `NZD`, one catalog instrument disabled and one missing: the run keeps `NZD`, keeps the disabled row disabled, adds the missing row, adds missing grants |
| Infrastructure.IntegrationTests | A platform-inactive report is not granted; the other is |
| Infrastructure.IntegrationTests | A second tenant with its own instruments and grants is untouched. No rows appear in users, accounts, transactions, portfolios, `InstrumentPrices` or `FxRates` |
| Infrastructure.IntegrationTests | A failure after the tenant insert (forced, for example a missing report code) leaves no `demo` tenant behind |
| Infrastructure.IntegrationTests | Existing `TestSeed` idempotency test still passes with the shared demo catalog |

No functional test: the command has no HTTP surface, and functional tests never rely on seeded tenants (tenants R17).

---

## 11. Locked in this spec

No new ADR.

| Item | Lock |
| --- | --- |
| Caller | Operator, through `webapi --seed-demo`. No automatic caller |
| Tenant | Code `demo`, Name `Demo Advisory`, reporting `USD` on create; existing tenant never changed |
| Catalog | The 18 rows in §5.1, all active, NZD and JPY rows included |
| Reports | `customer-transactions`, `adviser-balances` |
| Writes | Tenant, instruments, grants only. One transaction |
| Scripts / policy | None |

Amends, once this spec is accepted:

- [tenants.md](tenants.md) R17: the demo tenant may be created outside Development, by this command only (amendment 2026-11-14 there).
- [instruments.md](instruments.md) §2 Out and §9.1 "Not Production": the demo catalog may be written in production by this command.
- [reports.md](reports.md) seed paragraph "Not Production": the demo tenant's two grants may be written in production by this command.

Still open, not this slice ([future-plans.md](../future-plans.md) §5):

- Shared Adviser and Customer logins for visitors, and which routes they may not call
- The nightly reset job and whether it reuses this seed
- Where the demo credentials are shown

---

## 12. Suggested commits

The repository should build after each commit. Agents split each line into red then green.

1. Move the demo tenant code, name and the 18-row demo catalog into one shared definition used by `DevelopmentIdentitySeeder` and `TestSeed` (refactor; existing `TestSeed` tests stay green)
2. `DemoTenantSeed`: tenant, instruments and grants, idempotent, one transaction (R4–R12) + integration tests
3. `webapi --seed-demo` argument check and entry point: run the seed, exit 0 or 1, do not listen (R1–R3) + unit test
4. [deployment.md](../deployment.md) §12 runbook step and §13 item 7 updated (docs)
