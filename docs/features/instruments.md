---
title: Instruments
status: accepted
phase: 2
language: en
owner: ""
created: 2026-09-18
last_updated: 2026-11-27
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../architecture.md
  - ../adr/0004-money-as-decimal-with-currency.md
  - ../adr/0009-currencies-catalog.md
  - ../adr/0010-instrument-catalog.md
  - ../adr/0011-ledger-cash-holdings-reversal.md
  - ../adr/0013-roles-authorization-single-user-table.md
  - currencies.md
  - instrument-tax-attributes.md
---

# Instruments

Tenant instrument catalog on `webapi`: list / get / create / rename / disable / enable. First Phase-2 ledger slice. Adds script `0010_instruments.sql`, policies `instruments.read` / `instruments.create` / `instruments.manage`, aggregate `Instrument`, and ports `IMarketData` / `IFxRate`.

Holdings, accounts, and posting are out. This slice does not persist a price. Later slices store `InstrumentId` only.

An Instrument also has tax attributes: an issuer country and a FIF treatment per tax year. They are owned by [instrument-tax-attributes.md](instrument-tax-attributes.md) (accepted 2026-11-27), which this file points to.

**Status is `accepted`.** Implementation follows this file.

Landed in GitHub `louis297/MyWealthV2` master 2026-09-20, last slice commit `9ea2f2a`. Slice commits `e1475ca` … `9ea2f2a` match §12.

**Naming amendment 2026-09-21, landed** in GitHub master `bbd0f26` (slice `ad2d8b53` … `bbd0f26`). Column and JSON field are `IsActive` / `isActive`. Script `0010` shipped `IsEnabled`; `0011_rename_is_enabled_to_is_active.sql` renames it. HTTP verbs stay `POST …/disable` and `POST …/enable`. List query stays `enabledOnly` (filters `IsActive`). Do not leave an `IsEnabled` column.

---

## 1. Summary

A TenantAdmin or Adviser creates an instrument in the **current** tenant (`symbol` = instrument code, `name` = display name, `quoteCurrency`). SystemAdmin does the same for a chosen tenant (`tenantId` on create and on the list query). `QuoteCurrency` must be an enabled platform currency and is immutable after create. Cost currency equals quote currency (no extra column). An Adviser may create and read. Update / disable / enable are TenantAdmin or SystemAdmin. Disabled instruments stay in the catalog; they cannot be chosen as a **new** holding when that slice exists. Existing holdings (later) keep the id, same rule as a later-disabled `ReportingCurrency`.

The catalog is firm-wide, not per-Adviser. Its tax attributes (issuer country, FIF treatment per tax year) are set by `instruments.manage` through their own route ([instrument-tax-attributes.md](instrument-tax-attributes.md)). Customer receives 403. TenantAdmin / Adviser crossing tenants → 404. SystemAdmin may read and write any tenant’s row (Scalar or Back Office; page contract [../backoffice/back-office-instruments.md](../backoffice/back-office-instruments.md)). Adviser Portal has no SystemAdmin instruments screen.

---

## 2. Scope

**In**

- Application: `CreateInstrument`, `UpdateInstrument`, `DisableInstrument`, `EnableInstrument`, `GetInstruments`, `GetInstrumentById`
- Domain: `Instrument` aggregate
- Script `0010_instruments.sql` + EF mapping
- Policies `instruments.read`, `instruments.create`, `instruments.manage` (register on ADR 0013 Phase-2 list)
- Ports `IMarketData` and `IFxRate` in Application. `IMarketData` reads `InstrumentPrices` ([instrument-prices.md](instrument-prices.md)). `IFxRate` reads `FxRates` ([fx-rates.md](fx-rates.md)).
- `webapi` `/instruments` (root, not under `/users`)
- List envelope + `enabledOnly` + isolation tests
- `TestSeed` rows for Development / TestAppHost (not the schema script)

**Out**

- Accounts, holdings, postings, Opening, reversals, net worth
- Price or FX HTTP
- Live vendor adapters
- InstrumentKind / Property-as-instrument product fields
- ISIN / Exchange / Kind / price columns
- A platform-wide catalog (rows still have `TenantId`)
- Physical delete
- Changing `QuoteCurrency` after create
- A second cost-currency column
- Customer Portal or ledger-write for Customer
- Adviser Portal Instruments page. Catalog management is Scalar or the Back Office ([../backoffice/back-office-instruments.md](../backoffice/back-office-instruments.md)).
- SystemAdmin ledger screens on `adviser-portal`
- Empty Account / Holding / Journal tables “for later”
- Seeding instruments from `0010_instruments.sql`. In Production only the demo catalog of `webapi --seed-demo` is written (§9.1)

---

## 3. Stories

1. As an Adviser I create an instrument in my firm (`symbol`, `name`, `quoteCurrency`) so holdings can point at an id instead of free text.
2. As a TenantAdmin I create the same way, and I rename or disable an instrument the firm should no longer pick.
3. As an Adviser I list and open instruments in my firm (default = all rows, each item has `isActive`; `enabledOnly=true` for active only).
4. As an Adviser I cannot PUT / disable / enable (403).
5. As a TenantAdmin in another firm I receive 404 for someone else’s instrument id.
6. As a SystemAdmin I list / create / rename / disable instruments for a chosen tenant on Scalar or the Back Office, not on Adviser Portal.
7. As a Customer I receive 403 on every `/instruments` verb.
8. As a later holdings slice I reject a **new** holding of a disabled instrument; rows that already point at it stay.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Policy fail → 403. For TenantAdmin / Adviser: other-tenant or missing id → 404, not 403. SystemAdmin: missing id → 404; another tenant’s id is allowed. |
| R2 | Path `{id}` and JSON `id` are `PublicId`. Internal ints never appear. Create returns `201 { "id" }` only. |
| R3 | Routes live at `/instruments`. Not under `/users`. |
| R4 | TenantAdmin / Adviser: tenant = current user’s `TenantId`. Create / list body or query must **not** include `tenantId` (400). Missing current tenant → 404. Current tenant disabled → 403 on every verb, reads included (identity-auth §4.5). SystemAdmin naming a disabled tenant on **create** → 400 `disabled` / `target=tenant`. |
| R4a | SystemAdmin: no tenant on the token. Create **requires** `tenantId` (tenant PublicId). List **requires** `tenantId`. Omitted → 400. Unknown tenant → 404. Get / PUT / disable / enable use the instrument PublicId only and may cross tenants. |
| R5 | Catalog is tenant-scoped. An Adviser sees every instrument in the firm, not only “their” customers’ holdings (holdings do not exist yet). |
| R6 | `Symbol` is the instrument **code** (ticker-style catalog key), not the display name. Required, trim, stored upper case, compare CI. Length 1–32. Letters, digits, `.`, `-` only. Unique inside the tenant **including disabled rows**. Repeatable across tenants. |
| R7 | `Name` is the display name. Required, 1–200, trim, preserve caller casing. Duplicate names in one tenant are allowed. |
| R8 | `QuoteCurrency` required on create. Must exist in `ICurrencyCatalog` and be **enabled**. Stored upper-case `char(3)`. Immutable after create. Body on PUT that includes `quoteCurrency` → 400. |
| R9 | Cost currency = quote currency. No `CostCurrency` column. |
| R10 | A later disable of that catalog currency does not rewrite `QuoteCurrency`. New creates still require an enabled code. |
| R11 | Status is `IsActive` (bit), not a `UserStatus` machine. Disable / enable are `POST …/disable` and `POST …/enable`. Not a field on PUT. Never an `IsEnabled` column. |
| R12 | HTTP idempotent: already disabled + disable, or already enabled + enable → 204, no second event. |
| R13 | PUT / disable / enable require `rowVersion`. Conflict 409. Missing 400. Same encoding as people. |
| R14 | PUT updates `name` and/or `symbol` only. Other fields in the body → 400. Renaming the symbol can change `marketDataConnection` (R23), and with it where the instrument's price comes from, from the next price run. |
| R15 | No physical delete. Disable is the only way to take a row out of pickers. |
| R16 | This slice does **not** block disable because holdings exist (no holdings table). The holdings slice adds: new holding of a disabled instrument → 400; existing holdings unchanged. |
| R17 | List is one tenant. TenantAdmin / Adviser: current tenant. SystemAdmin: `tenantId` query. Every item includes `isActive` and `tenantId`. `enabledOnly` omitted/`false` = all; `true` = `IsActive` only; any other value 400. `page` default 1, `pageSize` default 20 max 100. Optional `search`: Symbol / Name contains (CI) or PublicId exact. Sort symbol then name. |
| R18 | Customer has no instrument policy → 403 on every verb. SystemAdmin has `instruments.read`, `instruments.create`, and `instruments.manage`. Adviser Portal does not add SystemAdmin ledger nav. |
| R19 | Same-currency FX is 1. Cross-currency is never treated as 1. Ports read local tables. No live call. |
| R20 | Catalog HTTP does not return a price. Callers that need a price use `IMarketData` on the server in a later slice. |

Policies (register now; map in `RolePermissions`):

`instruments.read`, `instruments.create`, `instruments.manage`. Grants per role are in the role permission table, [api-design.md](../api-design.md) §3.1 (moved there 2026-11-15).

Added by later specs:

| ID | Rule |
| --- | --- |
| R23 | **`marketDataConnection`.** The instrument item of `GET /instruments` and `GET /instruments/{id}` carries `marketDataConnection`: the name of the configured market data connection whose symbol suffix the symbol ends with (compared without regard to case), or `null`. Every caller who can read the instrument sees it. No filter on it, and no column: it is worked out from the symbol and the configuration on each read. A symbol like `ACME.SIM` is an ordinary catalog symbol under R6 (simulated-instruments R4, R6). |
| R24 | **Corporate actions.** A disabled Instrument may still get a corporate action, by hand or from the feed, because it may still be held. An Instrument served by a market data connection takes no hand-entered corporate action; its corporate actions come from the connection's feed (corporate-actions R12, R15, R16). |
| R25 | **Prices.** `GET /instruments/{id}/prices` lists the stored prices ([instrument-prices.md](instrument-prices.md) R12). The item itself carries no price. |
| R26 | **Tax attributes on the item.** The item of `GET /instruments` and `GET /instruments/{id}` carries `issuerCountry` (an ISO 3166-1 alpha-2 code or `null`) and `fifTreatments` (every row as `{ taxYear, treatment, note }`, sorted by `taxYear`, an empty list when there is none). Every caller who can read the instrument sees them ([instrument-tax-attributes.md](instrument-tax-attributes.md) R21). |
| R27 | **Tax attributes are written on their own route.** `PUT /instruments/{id}/tax-attributes` (`instruments.manage`) replaces the issuer country and every FIF treatment row ([instrument-tax-attributes.md](instrument-tax-attributes.md) R16). `POST /instruments` takes no tax attribute, and `PUT /instruments/{id}` refuses one with 400 under R14. Renaming, disabling or enabling does not change them. |

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `Instrument` | Aggregate | Tenant catalog row. Not a holding. |
| `InstrumentCreated` / `InstrumentDisabled` / `InstrumentEnabled` | Event | Raise even with no subscriber yet. Rename raises nothing in this slice. |
| `Instrument.IssuerCountry`, `Instrument.FifTreatments`, `SetTaxAttributes`, `FifTreatmentFor`, `FifTreatmentRowFor` | Members | Owned by [instrument-tax-attributes.md](instrument-tax-attributes.md) §5. `SetTaxAttributes` raises no event |
| `InstrumentFifTreatment` | Entity, child of `Instrument` | One FIF treatment of one tax year. Built only by `Instrument`, which passes its `TenantId` down |
| `IMarketData` | Port | Application. Reads `InstrumentPrices`. |
| `IFxRate` | Port | Application. Reads `FxRates`. |

Invariants:

- `TenantId` required. No platform row.
- `QuoteCurrency` set on create, then immutable.
- `Symbol` unique CI per tenant (including disabled).
- Enable / disable only flip `IsActive`.

```text
Create(tenantId, symbol, name, quoteCurrency)
  normalise symbol upper
  quoteCurrency must be supplied by the application as an enabled catalog code
  IsActive = true
  raise InstrumentCreated

Rename(name and/or symbol)
  symbol uniqueness is application + unique index

Disable()
  already disabled → no-op
  else IsActive = false, raise InstrumentDisabled

Enable()
  already enabled → no-op
  else IsActive = true, raise InstrumentEnabled
```

The entity does not call `ICurrencyCatalog` or the market ports. Application looks up the currency, then calls `Create`.

Keep [domain-model.md](../domain-model.md) in the same change.

---

## 6. Database

Script: `database/schema/0010_instruments.sql`.

Phase 1 ends at `0009`. Do not back-fill. Do not add EF migrations.

| Table | Change | Index / FK |
| --- | --- | --- |
| `Instruments` | add | PK `Id`. Unique `PublicId`. Unique CI `(TenantId, Symbol)`. FK `TenantId` → `Tenants` RESTRICT. FK `QuoteCurrency` → `Currencies.Code` RESTRICT |

### 6.1 `Instruments`

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | int identity | no | Internal PK |
| PublicId | uniqueidentifier | no | Default `NEWSEQUENTIALID()` or app UUID. HTTP id |
| TenantId | int | no | FK Tenants |
| Symbol | nvarchar(32) | no | Upper case. Unique per tenant CI |
| Name | nvarchar(200) | no | |
| QuoteCurrency | char(3) | no | FK Currencies. Immutable in Domain |
| IsActive | bit | no | Default 1. Shipped as `IsEnabled`; `0011` renames. |
| Created | datetimeoffset | no | |
| CreatedBy | nvarchar(450) | no | |
| LastModified | datetimeoffset | yes | |
| LastModifiedBy | nvarchar(450) | yes | |
| RowVersion | rowversion | no | |
| IssuerCountry | char(2) | yes | ISO 3166-1 alpha-2, upper case. Added by `0061_instrument_tax_attributes.sql` ([instrument-tax-attributes.md](instrument-tax-attributes.md) §6) |

No `Isin`, `Exchange`, `InstrumentKind`, `CostCurrency`, price, or quantity columns. The issuer country and the FIF treatment rows (table `InstrumentFifTreatments`) are tax attributes, not an ISIN, an exchange or a kind; those stay out.

Keep [database-design.md](../database-design.md) in the same change.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | `CreateInstrument` | PublicId | `instruments.create`; tenant from caller or SystemAdmin `tenantId`; enabled currency; unique symbol |
| Command | `UpdateInstrument` | void | `instruments.manage`; `rowVersion`; name/symbol only; SystemAdmin may cross tenants |
| Command | `DisableInstrument` | void | `instruments.manage`; `rowVersion`; idempotent |
| Command | `EnableInstrument` | void | `instruments.manage`; `rowVersion`; idempotent |
| Query | `GetInstruments` | list envelope | `instruments.read`; one tenant; `enabledOnly`; search; page |
| Query | `GetInstrumentById` | one item | `instruments.read`; TenantAdmin / Adviser same tenant or 404; SystemAdmin any tenant |

### 7.1 Ports

```text
IMarketData
  TryGetPrice(instrumentId: int, asOf: DateTimeOffset?) → Money?
      Quote currency of that instrument. Missing → empty, do not invent 0.
      Reads local `InstrumentPrices` (see instrument-prices.md).

IFxRate
  GetRate(from: string, to: string, asOf: DateTimeOffset?) → decimal
      Same code (CI) → 1.
      Else latest row from `FxRates` with AsOfDate ≤ requested date. Missing pair → fail (do not return 1).
```

Infrastructure reads the local tables. Development / TestSeed writes deterministic fake rows for tests. Request paths do not call vendors. `asOf` is honoured by both implementations.

This slice does not have to call the ports from a handler. It **registers** them so holdings / net-worth do not invent a second pair.

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/instruments` | `instruments.read` | 200 envelope | 401 / 403 / 400 |
| GET | `/instruments/{id}` | `instruments.read` | 200 item | 401 / 403 / 404 |
| POST | `/instruments` | `instruments.create` | 201 `{ "id" }` | 401 / 403 / 400 / 404 |
| PUT | `/instruments/{id}` | `instruments.manage` | 204 | 401 / 403 / 400 / 404 / 409 |
| POST | `/instruments/{id}/disable` | `instruments.manage` | 204 | 401 / 403 / 400 / 404 / 409 |
| POST | `/instruments/{id}/enable` | `instruments.manage` | 204 | 401 / 403 / 400 / 404 / 409 |
| PUT | `/instruments/{id}/tax-attributes` | `instruments.manage` | 204 | 401 / 403 / 400 / 404 / 409. Owned by [instrument-tax-attributes.md](instrument-tax-attributes.md) §8 |

List envelope: `{ items, page, pageSize, totalCount }`.

### 8.1 Bodies

Create (TenantAdmin / Adviser):

```json
{
  "symbol": "VTI",
  "name": "Vanguard Total Stock Market ETF",
  "quoteCurrency": "USD"
}
```

Create (SystemAdmin — `tenantId` required):

```json
{
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "symbol": "VTI",
  "name": "Vanguard Total Stock Market ETF",
  "quoteCurrency": "USD"
}
```

PUT:

```json
{
  "symbol": "VTI",
  "name": "Vanguard Total Stock Market ETF",
  "rowVersion": "<base64>"
}
```

Disable / enable body:

```json
{
  "rowVersion": "<base64>"
}
```

List / get item:

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "symbol": "VTI",
  "name": "Vanguard Total Stock Market ETF",
  "quoteCurrency": "USD",
  "isActive": true,
  "marketDataConnection": null,
  "issuerCountry": "US",
  "fifTreatments": [
    { "taxYear": 2027, "treatment": "Attributing", "note": null }
  ],
  "rowVersion": "<base64>"
}
```

GET list does not include a price. `issuerCountry` and `fifTreatments` are R26.

`GET /instruments` for SystemAdmin requires `?tenantId=`. TenantAdmin / Adviser must omit it.

### 8.2 Errors

Ordinary validation (bad symbol, duplicate symbol, disabled quote currency, extra PUT fields) → 400 `errors`.

SystemAdmin create while the named tenant is disabled → §4.1 `code=disabled`, `target=tenant`. A TenantAdmin / Adviser of a disabled tenant gets 403 first (identity-auth §4.5).

Unknown id → 404. Other-tenant id → 404 for TenantAdmin / Adviser; 200 / 204 for SystemAdmin when the row exists.

Keep the resource catalog in [api-design.md](../api-design.md) pointing here for field rules.

---

## 9. UI

None in the original instruments slice.

Adviser Portal has no Instruments page. The catalog is consumed only as a picker on Buy / Sell / StockIn / StockOut / Split forms (`GET /instruments?enabledOnly=true`). Create / rename / disable stay on Scalar or the Back Office.

Back Office page contract (accepted 2026-11-09): [../backoffice/back-office-instruments.md](../backoffice/back-office-instruments.md). SystemAdmin list / create / rename / disable / enable for a chosen tenant. QuoteCurrency immutable. No price. No physical delete.

Development seed prices are not a UI.

### 9.1 Test seed

Contribute to host `TestSeed` (Development and TestAppHost only). Not `0010_instruments.sql`. Idempotent on `(TenantId, Symbol)`.

The 18-row demo catalog ([demo-tenant-seed.md](demo-tenant-seed.md) §5.1) may be written for tenant `demo` in any environment by `webapi --seed-demo`, without prices. `TestSeed` takes the same catalog and keeps its Development-only extras: the disabled `NZD-CASH` row, the fake table prices, and `ACME.SIM` (USD) and `KIWI.SIM` (NZD) on each firm it seeds ([simulated-instruments.md](simulated-instruments.md)).

Target: the existing Development demo tenant (Phase 1 people seed). CreatedBy = system name.

| Symbol | Name | QuoteCurrency | IsActive |
| --- | --- | --- | --- |
| VTI | Vanguard Total Stock Market ETF | USD | true |
| AIA | Auckland International Airport | NZD | true |
| NZD-CASH | Demo disabled cash proxy | NZD | false |

Also seed table prices for VTI and AIA (not for the disabled row). Missing price on NZD-CASH is the empty-price case. Later expanded by [instrument-prices.md](instrument-prices.md).

`TestSeed` also writes the tax attributes of the demo catalog ([demo-tenant-seed.md](demo-tenant-seed.md) §5.1), and `ACME.SIM` gets `US` with `2027 Attributing`, `KIWI.SIM` gets `NZ` with `2027 NotForeign`, and `NZD-CASH` gets none ([instrument-tax-attributes.md](instrument-tax-attributes.md) R25).

Functional tests do **not** rely on these rows for isolation; they create their own instruments. One smoke test may assert the three symbols exist after TestAppHost start.

---

## 10. Tests

The tests are the executable form of §4. Where they live:

| Project | Where |
| --- | --- |
| Domain.UnitTests | `Entities/InstrumentTests.cs` |
| Application.FunctionalTests | Folder `Instruments/` |
| Infrastructure.IntegrationTests | `AppliedSchema/InstrumentsSchemaTests.cs`, `AppliedSchema/TestSeedInstrumentsTests.cs` |

---

## 11. Locked in this spec

No new ADR. Expand [ADR 0010](../adr/0010-instrument-catalog.md) in the same change (columns + port names). Do not implement from 0010 alone.

Locked:

| Item | Lock |
| --- | --- |
| Scope | Tenant catalog, not platform, not free text on holdings |
| Callers | Adviser may create + read. Update / disable / enable TenantAdmin **or SystemAdmin**. Customer 403 |
| `QuoteCurrency` | Immutable after create. Cost currency = quote currency |
| Ports | Introduced here. `IMarketData` reads `InstrumentPrices`. `IFxRate` reads `FxRates`. Same-currency FX = 1. Cross-currency never silently 1 |
| HTTP namespace | `/instruments` at the root |
| Lifecycle | Disable / enable + `rowVersion`. No physical delete |
| Script | `0010_instruments.sql` |
| Policies | `instruments.read` / `instruments.create` / `instruments.manage`. SystemAdmin has all three |
| Symbol | Instrument code. Upper case, 1–32, `[A-Z0-9.-]`, unique per tenant including disabled. `Name` is the display name |
| Columns | No ISIN / Exchange / Kind / price |
| Port methods | `IMarketData.TryGetPrice`, `IFxRate.GetRate` |
| Portal | Adviser Portal has no Instruments management page. Back Office page contract: [../backoffice/back-office-instruments.md](../backoffice/back-office-instruments.md) (accepted 2026-11-09). SystemAdmin also uses Scalar |
| TestSeed | VTI, AIA, NZD-CASH on the Development demo tenant. Later expanded, with table prices, by [instrument-prices.md](instrument-prices.md) |
| Tax attributes | Issuer country and FIF treatment per tax year, on their own route; owned by [instrument-tax-attributes.md](instrument-tax-attributes.md) |

Still open, not this slice:

- Account / holding / posting / Opening / net-worth / Dashboard
- Whether close of an account forces liquidation
- Live market data
- InstrumentKind (Property remains a reserved **account** type, not an instrument type in Phase 2)
- Whether ISIN / Exchange are added in a later ALTER

---

## 12. History

What changed in this file after it was accepted. The text of each change is in git (`git log -p -- docs/features/instruments.md`).

| Date | With | Change here |
| --- | --- | --- |
| 2026-11-14 | [demo-tenant-seed.md](demo-tenant-seed.md) | §2, §9.1: the demo catalog may be written in any environment |
| 2026-11-22 | [simulated-instruments.md](simulated-instruments.md), [corporate-actions.md](corporate-actions.md) | R23, R24, R14: `marketDataConnection`; corporate actions on an Instrument |
| 2026-11-25 | [market-data-history.md](market-data-history.md) | R25: the price read route |
| 2026-11-26 | — | The changes above were folded into the body. No rule changed |
| 2026-11-27 | [instrument-tax-attributes.md](instrument-tax-attributes.md) | Intro, §1, R26, R27, §5, §6.1, §8, §9.1, §11: the tax attributes of an Instrument |
