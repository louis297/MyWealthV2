---
title: Demo tenant seed
status: accepted
phase: 3
language: en
owner: ""
created: 2026-11-14
last_updated: 2026-11-27
related:
  - ../deployment.md
  - ../future-plans.md
  - ../glossary.md
  - tenants.md
  - instruments.md
  - instrument-prices.md
  - fx-rates.md
  - reports.md
  - tenant-custody-modes.md
  - production-hosting.md
  - ../known-issues/eodhd-nz-jp-coverage.md
  - instrument-tax-attributes.md
---

# Demo tenant seed

An operator runs a one-shot command on `webapi`, `--seed-demo`, to create the demo tenant and its configuration in any environment, production included. The command writes only the tenant row, its instrument catalog and its report grants. It creates no people and no ledger data. It adds no script, no route and no policy. It amends [tenants.md](tenants.md) R17, which today allows seeding only in Development.

---

## 1. Summary

The **demo seed** is an explicit command, not a start-up step. It creates tenant `demo` (`Demo Advisory`, reporting currency `USD`), the 18 instruments of the **demo catalog** with their tax attributes ([instrument-tax-attributes.md](instrument-tax-attributes.md) R24), and grants for the five catalog reports (`customer-transactions`, `adviser-balances`, `bank-reconciliation`, `customer-fif`, `customer-income-summary`). It is idempotent on natural keys and never changes a row that already exists, except that it classifies an existing demo catalog instrument that has no tax attribute at all (R6). Prices and FX rates come from the existing worker jobs, not from this command.

This is the first part of [future-plans.md](../future-plans.md) §6 (demo data for visitors). The shared demo logins and the nightly reset are still open there.

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
- People: TenantAdmin, Adviser, Customer, or any login for visitors ([future-plans.md](../future-plans.md) §6)
- Accounts, Transactions, Holdings, Portfolios, report schedules
- Instrument prices and FX rates. The worker jobs own both ([instrument-prices.md](instrument-prices.md), [fx-rates.md](fx-rates.md))
- Changing or repairing an existing tenant, instrument or grant, apart from classifying an instrument with no tax attribute (R6)
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
| R4 | Tenant: Code `demo`, Name `Demo Advisory`, reporting currency `USD`, enabled. It is created through `Tenant.Create`, with the same validation as a tenant created through the API. The tenant allows both custody modes (`Tenant.Create` allows both). Its `brokerConnection` is set to `simulator` when a broker connection of that name is configured on `webapi`; otherwise it gets none (orders §9.1). |
| R5 | When a tenant with Code `demo` already exists, the seed does not change it: name, reporting currency and enabled flag stay as they are. It still ensures the instruments (R6) and grants (R8) for that tenant. A disabled demo tenant is logged as a warning, not an error. That includes its allowed custody modes and its broker connection. The seed adds no client money account, on a new or an existing `demo` tenant: until SystemAdmin adds one in the Back Office, no new Custodial account can be opened in the demo tenant (client-money-accounts R11). The account numbers must match the simulator bank's accounts, which the seed cannot know. |
| R6 | Instruments: every row of the demo catalog (§5.1), all active, with the issuer country and the tax year 2027 FIF treatment of §5.1. Idempotent on `(TenantId, Symbol)`. An existing row with the same symbol is left unchanged, even if its name, quote currency or `IsActive` differ, with one exception: when it has no tax attribute at all (no issuer country and no FIF treatment row), the seed writes the tax attributes of §5.1 on it. An instrument with either is left unchanged ([instrument-tax-attributes.md](instrument-tax-attributes.md) R24). |
| R7 | The NZD and JPY rows stay in the catalog, although EODHD has no price for them ([known-issues/eodhd-nz-jp-coverage.md](../known-issues/eodhd-nz-jp-coverage.md)). Until it does, valuation, Buy and Sell see no price for them, as on any tenant. |
| R8 | Report grants: `customer-transactions`, `adviser-balances`, `bank-reconciliation` (bank-reconciliation R23) `customer-fif` (fif-report R29) and `customer-income-summary` (income-summary R36). A demo tenant that has the four earlier grants gets the fifth on the next run and keeps the four. Idempotent on `(TenantId, ReportId)`. A platform-inactive catalog report is not granted and is logged as a warning, matching `POST /tenants/{tenantId}/reports`. A missing catalog code is an error (R11). |
| R9 | The seed writes nothing else besides the tax attributes of R6: no people, accounts, transactions, holdings, portfolios, schedules, instrument prices or FX rates. No other tenant is read or written. |
| R10 | Audit `CreatedBy` = `system`, the same value as `TestSeed`. |
| R11 | All writes of one run are in one database transaction. Any failure rolls the whole run back, logs the error, and exits with code 1. Success exits with code 0. A run that races another run fails on a unique index and can be run again. |
| R12 | The log names the tenant and counts what was created and what already existed, for example `Demo seed: tenant demo created; instruments 18 created, 0 existing; instruments classified 18, already classified 0; reports 5 granted, 0 existing`. "Classified" counts the instruments the run wrote tax attributes on; "already classified" counts the existing ones it left unchanged because they had a tax attribute. It never logs a connection string or a secret. |
| R13 | The demo catalog has one definition. `TestSeed` uses it for `demo` and `demo2` in Development and adds its own Development-only extras on top: the disabled `NZD-CASH` row (not classified), `ACME.SIM` (`US`, `2027 Attributing`) and `KIWI.SIM` (`NZ`, `2027 NotForeign`), and the fake prices and FX rates. It writes the tax attributes by the rule of R6 ([instrument-tax-attributes.md](instrument-tax-attributes.md) R25). Those extras never reach the command. |
| R14 | The Development demo tenant keeps reporting currency `NZD`. `DevelopmentIdentitySeeder` creates it first, so R5 means the command does not change it when run locally. Only a database without a `demo` tenant gets `USD`. |

---

## 5. Domain

No new types. The seed uses `Tenant.Create`, `Instrument.Create`, `Instrument.SetTaxAttributes` and `TenantReport.Grant` as they are.

### 5.1 Demo catalog

Symbols are stored as in `TestSeed` today. The instrument-price job maps them to EODHD symbols ([instrument-prices.md](instrument-prices.md) §11).

| Symbol | Name | Quote currency | Issuer country | FIF treatment, tax year 2027 |
| --- | --- | --- | --- | --- |
| VTI | Vanguard Total Stock Market ETF | USD | `US` | `Attributing` |
| AAPL | Apple Inc. | USD | `US` | `Attributing` |
| MSFT | Microsoft Corporation | USD | `US` | `Attributing` |
| AIA | Auckland International Airport | NZD | `NZ` | `NotForeign` |
| FPH | Fisher & Paykel Healthcare | NZD | `NZ` | `NotForeign` |
| MEL | Meridian Energy | NZD | `NZ` | `NotForeign` |
| BHP | BHP Group | AUD | `AU` | `ExemptAsxListed` |
| CBA | Commonwealth Bank of Australia | AUD | `AU` | `ExemptAsxListed` |
| WOW | Woolworths Group | AUD | `AU` | `ExemptAsxListed` |
| SAP | SAP SE | EUR | `DE` | `Attributing` |
| AIR | Airbus SE | EUR | `NL` | `Attributing` |
| OR | L'Oréal | EUR | `FR` | `Attributing` |
| SHEL | Shell plc | GBP | `GB` | `Attributing` |
| HSBA | HSBC Holdings | GBP | `GB` | `Attributing` |
| ULVR | Unilever | GBP | `GB` | `Attributing` |
| 7203 | Toyota Motor | JPY | `JP` | `Attributing` |
| 6758 | Sony Group | JPY | `JP` | `Attributing` |
| 9984 | SoftBank Group | JPY | `JP` | `Attributing` |

The tax attributes are those of [instrument-tax-attributes.md](instrument-tax-attributes.md) §5.1, which gives the reasons. The three `ExemptAsxListed` rows carry the note `Assumed from tax year 2026: on 2026-11-27 Inland Revenue's FIF Australian listed share exemption tool showed the exemption for 1 April 2025 to 31 March 2026 only.` An exemption stands for its own tax year only, so for tax year 2028 the three are not classified until a person adds a row.

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
| Infrastructure.IntegrationTests | Empty database after the schema applicator: one run creates tenant `demo` / `Demo Advisory` / `USD`, enabled; 18 active instruments matching §5.1, each with its issuer country and one FIF treatment row for tax year 2027; grants for the five reports of R8 |
| Infrastructure.IntegrationTests | Second run creates no rows and leaves every row unchanged |
| Infrastructure.IntegrationTests | Existing `demo` tenant with reporting currency `NZD`, one catalog instrument disabled and one missing: the run keeps `NZD`, keeps the disabled row disabled, adds the missing row, adds missing grants |
| Infrastructure.IntegrationTests | An existing demo instrument with no tax attribute gets those of §5.1; one with an issuer country only, or with a FIF treatment row, is left unchanged; the log counts both |
| Infrastructure.IntegrationTests | A platform-inactive report is not granted; the others are |
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
| Catalog | The 18 rows in §5.1, all active, NZD and JPY rows included, classified for tax year 2027. An existing instrument is classified only when it has no tax attribute |
| Reports | `customer-transactions`, `adviser-balances`, `bank-reconciliation` |
| Writes | Tenant, instruments and their tax attributes, grants only. One transaction |
| Scripts / policy | None |

Amends (accepted 2026-11-14):

- [tenants.md](tenants.md) R17: the demo tenant may be created outside Development, by this command only.
- [instruments.md](instruments.md) §2 Out and §9.1 "Not Production": the demo catalog may be written in production by this command.
- [reports.md](reports.md) seed paragraph "Not Production": the demo tenant's report grants (R8) may be written in production by this command.

Still open, not this slice ([future-plans.md](../future-plans.md) §6):

- Shared Adviser and Customer logins for visitors, and which routes they may not call
- The nightly reset job and whether it reuses this seed
- Where the demo credentials are shown

---

## 12. History

What changed in this file after it was accepted. The text of each change is in git (`git log -p -- docs/features/demo-tenant-seed.md`).

| Date | With | Change here |
| --- | --- | --- |
| 2026-11-18 | [tenant-custody-modes.md](tenant-custody-modes.md) | R4, R5: the demo tenant allows both custody modes |
| 2026-11-20 | [client-money-accounts.md](client-money-accounts.md) | R5: the seed adds no client money account |
| 2026-11-23 | [orders.md](orders.md) | R4: the broker connection of a new demo tenant |
| 2026-11-26 | — | The changes above were folded into the body. No rule changed |
| 2026-11-27 | [bank-reconciliation.md](bank-reconciliation.md) | §1, R8, R12, §10, §11: the seed also grants `bank-reconciliation`, as bank-reconciliation R23 says and the code does |
| 2026-11-27 | [instrument-tax-attributes.md](instrument-tax-attributes.md) | §1, R6, R9, R12, R13, §5, §5.1, §10, §11: the seed writes the tax attributes of the demo catalog, also on an existing instrument with none |
| 2026-11-28 | [fif-report.md](fif-report.md) | §1, R8, R12, §10: the seed also grants `customer-fif` |
| 2026-11-28 | [income-summary.md](income-summary.md) | §1, R8, R12, §10: the seed also grants `customer-income-summary`; five grants |
