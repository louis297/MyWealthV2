---
title: Instruments
status: accepted
phase: 2
language: en
owner: ""
created: 2026-09-18
last_updated: 2026-09-20
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
---

# Instruments

Tenant instrument catalog on `webapi`: list / get / create / rename / disable / enable. First Phase-2 ledger slice. Adds script `0010_instruments.sql`, policies `instruments.read` / `instruments.create` / `instruments.manage`, aggregate `Instrument`, and mocked ports `IMarketData` / `IFxRate`.

Holdings, accounts, and posting are out. This slice does not persist a price. Later slices store `InstrumentId` only.

**Status is `accepted`.** Implementation follows this file. Expand [ADR 0010](../adr/0010-instrument-catalog.md) in the same change.

---

## 1. Summary

A TenantAdmin or Adviser creates an instrument in the **current** tenant (`symbol` = instrument code, `name` = display name, `quoteCurrency`). SystemAdmin does the same for a chosen tenant (`tenantId` on create and on the list query). `QuoteCurrency` must be an enabled platform currency and is immutable after create. Cost currency equals quote currency (no extra column). An Adviser may create and read. Update / disable / enable are TenantAdmin or SystemAdmin. Disabled instruments stay in the catalog; they cannot be chosen as a **new** holding when that slice exists. Existing holdings (later) keep the id, same rule as a later-disabled `ReportingCurrency`.

The catalog is firm-wide, not per-Adviser. Customer receives 403. TenantAdmin / Adviser crossing tenants → 404. SystemAdmin may read and write any tenant’s row (Scalar now; Back Office later). Adviser Portal has no SystemAdmin instruments screen.

---

## 2. Scope

**In**

- Application: `CreateInstrument`, `UpdateInstrument`, `DisableInstrument`, `EnableInstrument`, `GetInstruments`, `GetInstrumentById`
- Domain: `Instrument` aggregate
- Script `0010_instruments.sql` + EF mapping
- Policies `instruments.read`, `instruments.create`, `instruments.manage` (register on ADR 0013 Phase-2 list)
- Ports `IMarketData` and `IFxRate` in Application; Infrastructure mock only
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
- Adviser Portal Instruments page (portal cut later)
- SystemAdmin ledger screens on `adviser-portal`
- Empty Account / Holding / Journal tables “for later”
- Seeding instruments from `0010_instruments.sql` or in Production

---

## 3. Stories

1. As an Adviser I create an instrument in my firm (`symbol`, `name`, `quoteCurrency`) so holdings can point at an id instead of free text.
2. As a TenantAdmin I create the same way, and I rename or disable an instrument the firm should no longer pick.
3. As an Adviser I list and open instruments in my firm (default = all rows, each item has `isEnabled`; `enabledOnly=true` for enabled only).
4. As an Adviser I cannot PUT / disable / enable (403).
5. As a TenantAdmin in another firm I receive 404 for someone else’s instrument id.
6. As a SystemAdmin I list / create / rename / disable instruments for a chosen tenant on Scalar (and later Back Office), not on Adviser Portal.
7. As a Customer I receive 403 on every `/instruments` verb.
8. As a later holdings slice I reject a **new** holding of a disabled instrument; rows that already point at it stay.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Policy fail → 403. For TenantAdmin / Adviser: other-tenant or missing id → 404, not 403. SystemAdmin: missing id → 404; another tenant’s id is allowed. |
| R2 | Path `{id}` and JSON `id` are `PublicId`. Internal ints never appear. Create returns `201 { "id" }` only. |
| R3 | Routes live at `/instruments`. Not under `/users`. |
| R4 | TenantAdmin / Adviser: tenant = current user’s `TenantId`. Create / list body or query must **not** include `tenantId` (400). Missing current tenant → 404. Target tenant disabled on **create** → 400 `disabled` / `target=tenant`. List / get / PUT / disable / enable do not reject because the tenant is disabled. |
| R4a | SystemAdmin: no tenant on the token. Create **requires** `tenantId` (tenant PublicId). List **requires** `tenantId`. Omitted → 400. Unknown tenant → 404. Get / PUT / disable / enable use the instrument PublicId only and may cross tenants. |
| R5 | Catalog is tenant-scoped. An Adviser sees every instrument in the firm, not only “their” customers’ holdings (holdings do not exist yet). |
| R6 | `Symbol` is the instrument **code** (ticker-style catalog key), not the display name. Required, trim, stored upper case, compare CI. Length 1–32. Letters, digits, `.`, `-` only. Unique inside the tenant **including disabled rows**. Repeatable across tenants. |
| R7 | `Name` is the display name. Required, 1–200, trim, preserve caller casing. Duplicate names in one tenant are allowed. |
| R8 | `QuoteCurrency` required on create. Must exist in `ICurrencyCatalog` and be **enabled**. Stored upper-case `char(3)`. Immutable after create. Body on PUT that includes `quoteCurrency` → 400. |
| R9 | Cost currency = quote currency. No `CostCurrency` column. |
| R10 | A later disable of that catalog currency does not rewrite `QuoteCurrency`. New creates still require an enabled code. |
| R11 | Status is `IsEnabled` (bit), not a `UserStatus` machine. Disable / enable are `POST …/disable` and `POST …/enable`. Not a field on PUT. |
| R12 | HTTP idempotent: already disabled + disable, or already enabled + enable → 204, no second event. |
| R13 | PUT / disable / enable require `rowVersion`. Conflict 409. Missing 400. Same encoding as people. |
| R14 | PUT updates `name` and/or `symbol` only. Other fields in the body → 400. |
| R15 | No physical delete. Disable is the only way to take a row out of pickers. |
| R16 | This slice does **not** block disable because holdings exist (no holdings table). The holdings slice adds: new holding of a disabled instrument → 400; existing holdings unchanged. |
| R17 | List is one tenant. TenantAdmin / Adviser: current tenant. SystemAdmin: `tenantId` query. Every item includes `isEnabled` and `tenantId`. `enabledOnly` omitted/`false` = all; `true` = enabled only; any other value 400. `page` default 1, `pageSize` default 20 max 100. Optional `search`: Symbol / Name contains (CI) or PublicId exact. Sort symbol then name. |
| R18 | Customer has no instrument policy → 403 on every verb. SystemAdmin has `instruments.read`, `instruments.create`, and `instruments.manage`. Adviser Portal does not add SystemAdmin ledger nav. |
| R19 | Same-currency FX is 1. Cross-currency is never treated as 1. Ports are mocked. No live call. |
| R20 | Catalog HTTP does not return a price. Callers that need a price use `IMarketData` on the server in a later slice. |

Policies (register now; map in `RolePermissions`):

| Policy | TenantAdmin | Adviser | SystemAdmin | Customer |
| --- | --- | --- | --- | --- |
| `instruments.read` | ✓ | ✓ | ✓ | |
| `instruments.create` | ✓ | ✓ | ✓ | |
| `instruments.manage` | ✓ | | ✓ | |

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `Instrument` | Aggregate | Tenant catalog row. Not a holding. |
| `InstrumentCreated` / `InstrumentDisabled` / `InstrumentEnabled` | Event | Raise even with no subscriber yet. Rename raises nothing in this slice. |
| `IMarketData` | Port | Application. Mock in Infrastructure. |
| `IFxRate` | Port | Application. Mock in Infrastructure. |

Invariants:

- `TenantId` required. No platform row.
- `QuoteCurrency` set on create, then immutable.
- `Symbol` unique CI per tenant (including disabled).
- Enable / disable only flip `IsEnabled`.

```text
Create(tenantId, symbol, name, quoteCurrency)
  normalise symbol upper
  quoteCurrency must be supplied by the application as an enabled catalog code
  IsEnabled = true
  raise InstrumentCreated

Rename(name and/or symbol)
  symbol uniqueness is application + unique index

Disable()
  already disabled → no-op
  else IsEnabled = false, raise InstrumentDisabled

Enable()
  already enabled → no-op
  else IsEnabled = true, raise InstrumentEnabled
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
| IsEnabled | bit | no | Default 1 |
| Created | datetimeoffset | no | |
| CreatedBy | nvarchar(450) | no | |
| LastModified | datetimeoffset | yes | |
| LastModifiedBy | nvarchar(450) | yes | |
| RowVersion | rowversion | no | |

No `Isin`, `Exchange`, `InstrumentKind`, `CostCurrency`, price, or quantity columns.

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

IFxRate
  GetRate(from: string, to: string, asOf: DateTimeOffset?) → decimal
      Same code (CI) → 1.
      Else mock pair. Missing pair → fail (do not return 1).
```

Infrastructure ships an in-memory mock seeded by tests / Development. Production in Phase 2 is the same mock (no vendor). `asOf` may be ignored by the mock; keep the argument so a later adapter does not change the port.

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
  "isEnabled": true,
  "rowVersion": "<base64>"
}
```

GET list does not include a price.

`GET /instruments` for SystemAdmin requires `?tenantId=`. TenantAdmin / Adviser must omit it.

### 8.2 Errors

Ordinary validation (bad symbol, duplicate symbol, disabled quote currency, extra PUT fields) → 400 `errors`.

Create while the target tenant is disabled → §4.1 `code=disabled`, `target=tenant`.

Unknown id → 404. Other-tenant id → 404 for TenantAdmin / Adviser; 200 / 204 for SystemAdmin when the row exists.

Keep the resource catalog in [api-design.md](../api-design.md) pointing here for field rules.

---

## 9. UI

None in this slice.

Adviser Portal Instruments page is a later portal cut. Do not invent nav or form layout here.

Development mock prices are not a UI. SystemAdmin uses Scalar (and later Back Office), not this portal.

### 9.1 Test seed

Contribute to host `TestSeed` (Development and TestAppHost only). Not `0010_instruments.sql`. Not Production. Idempotent on `(TenantId, Symbol)`.

Target: the existing Development demo tenant (Phase 1 people seed). CreatedBy = system name.

| Symbol | Name | QuoteCurrency | IsEnabled |
| --- | --- | --- | --- |
| VTI | Vanguard Total Stock Market ETF | USD | true |
| AIA | Auckland International Airport | NZD | true |
| NZD-CASH | Demo disabled cash proxy | NZD | false |

Also seed mock `IMarketData` prices for VTI and AIA (not for the disabled row). Missing price on NZD-CASH is the empty-price case.

Functional tests do **not** rely on these rows for isolation; they create their own instruments. One smoke test may assert the three symbols exist after TestAppHost start.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | Create normalises symbol; `QuoteCurrency` cannot change; disable / enable idempotent; events only on actual change |
| Application.FunctionalTests | No Bearer → 401 not 302; Customer → 403; Adviser POST 201 and GET 200; Adviser PUT / disable → 403; TenantAdmin PUT / disable 204; SystemAdmin list/create without `tenantId` → 400; SystemAdmin with tenant PublicId POST 201 and PUT 204 on another tenant’s id; TenantAdmin other-tenant id → 404; duplicate symbol 400; disabled quote currency 400; `enabledOnly=true` omits disabled; list envelope paging; `rowVersion` 409 |
| Infrastructure.IntegrationTests | Two-tenant fixture: TenantAdmin other tenant’s PublicId → 404. Unique `(TenantId, Symbol)` allows the same symbol in tenant B. TestSeed idempotent on second run. `IFxRate` same-currency = 1; missing cross pair does not return 1. `IMarketData` missing price is empty |

No holdings fixture in this slice.

---

## 11. Locked in this spec

No new ADR. Expand [ADR 0010](../adr/0010-instrument-catalog.md) in the same change (columns + port names). Do not implement from 0010 alone.

Locked:

| Item | Lock |
| --- | --- |
| Scope | Tenant catalog, not platform, not free text on holdings |
| Callers | Adviser may create + read. Update / disable / enable TenantAdmin **or SystemAdmin**. Customer 403 |
| `QuoteCurrency` | Immutable after create. Cost currency = quote currency |
| Ports | Introduced here, mocked. Same-currency FX = 1. Cross-currency never silently 1 |
| HTTP namespace | `/instruments` at the root |
| Lifecycle | Disable / enable + `rowVersion`. No physical delete |
| Script | `0010_instruments.sql` |
| Policies | `instruments.read` / `instruments.create` / `instruments.manage`. SystemAdmin has all three |
| Symbol | Instrument code. Upper case, 1–32, `[A-Z0-9.-]`, unique per tenant including disabled. `Name` is the display name |
| Columns | No ISIN / Exchange / Kind / price |
| Port methods | `IMarketData.TryGetPrice`, `IFxRate.GetRate` |
| Portal | No page in this slice. SystemAdmin uses Scalar |
| TestSeed | VTI, AIA, NZD-CASH on the Development demo tenant; mock prices for the first two |

Still open, not this slice:

- Account / holding / posting / Opening / net-worth / Dashboard
- Whether close of an account forces liquidation
- Live market data
- InstrumentKind (Property remains a reserved **account** type, not an instrument type in Phase 2)
- Whether ISIN / Exchange are added in a later ALTER

---

## 12. Suggested commits

The repository should build after each commit. Agents split each line into red then green.

1. `0010_instruments.sql` + EF mapping + integration test “table exists”
2. `Instrument` aggregate + Domain.UnitTests
3. Policies registered in `RolePermissions`
4. Create / get / list handlers + functional tests (401 / 403 / isolation / unique symbol)
5. Update / disable / enable + `rowVersion` 409
6. `IMarketData` / `IFxRate` ports + mock + unit tests for the 1-vs-not-1 FX rule
7. `TestSeed` instruments + mock prices + idempotent integration test

Do not put holdings or portal pages in these commits.
