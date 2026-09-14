---
title: Currencies
status: accepted
phase: 1
language: en
owner: ""
created: 2026-09-13
last_updated: 2026-09-13
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../architecture.md
  - ../adr/0004-money-as-decimal-with-currency.md
  - ../adr/0008-schema-sql-as-source-of-truth.md
  - ../adr/0009-currencies-catalog.md
  - identity-auth.md
---

# Currencies

Platform currency catalog, in-process `ICurrencyCatalog`, read-only `GET /currencies`, and the `Tenants.ReportingCurrency` column / FK. Same slice places `Money` in the Domain assembly (ADR 0004: no balance column yet, the type still ships).

This is not a tenant-scoped resource. No cross-tenant isolation tests. People CRUD, tenant HTTP, and portal pages are out.

Chinese discussion draft: `v2draft/features/currencies.md`.

---

## 1. Summary

Every authenticated role may read the platform currency list. The table is platform master data, not per-tenant. The hot path reads `ICurrencyCatalog` in memory and does not JOIN `Currencies`. The only Phase-1 consumer column is `Tenants.ReportingCurrency`: this slice adds the column and FK and does **not** expose `/tenants`. A disabled currency cannot be used as a **new** reporting currency. Tenants that already reference it keep the historical code.

---

## 2. Scope

**In**

- Script `0008_currencies.sql`: platform table `Currencies` + six-row seed
- Script `0009_tenants_reporting_currency.sql`: `Tenants.ReportingCurrency char(3) NOT NULL` + FK → `Currencies.Code` (RESTRICT)
- Domain: `Currency` (platform entity), `Money` (value object), `Tenant.ReportingCurrency` + `SetReportingCurrency`
- Port `ICurrencyCatalog` (Application) + Infrastructure load at startup / `Reload`
- `GET /currencies` on `webapi`: authenticated, **not** anonymous
- EF Fluent mapping for `Currencies` and the new Tenant column
- After the applicator runs: the six seed rows exist

**Out**

- Currency write APIs (create / rename / enable / disable)
- Per-tenant allow-lists
- FX, `IFxRate`, a rates table
- Anonymous `GET /currencies`
- A `currencies.read` policy (ADR 0013 Phase-1 list does not grow)
- `/tenants` HTTP (tenants slice)
- Ledger amount columns, `Instrument.QuoteCurrency`
- Back-filling `0002_currencies.sql` (identity-auth already used `0003`–`0007`; only forward `0008+`)
- An Adviser Portal currencies page
- A third-party Money library

---

## 3. Stories

1. As an authenticated user (including Customer and SystemAdmin) I call `GET /currencies` so later tenant create / display can show supported codes and names.
2. As an unauthenticated caller I get 401 on `GET /currencies` so the catalog is not a public surface.
3. As the later tenants slice I validate reporting currency through `ICurrencyCatalog` (must exist and be enabled), not a JOIN and not this HTTP call.
4. As the domain I have `Money` (amount + three-letter code) in Phase 1 so the ledger does not invent a second amount shape.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | `Code` is ISO 4217, three letters, stored and returned upper case. Comparisons may be CI; writes normalise to upper case. |
| R2 | No PublicId, no RowVersion, no audit columns. The natural key is `Code`. |
| R3 | `DecimalPlaces` is the ISO minor-unit count for **input / display / validation**, not the SQL scale. Seed: JPY = 0; NZD / AUD / USD / EUR / GBP = 2. When money columns exist (Phase 2), storage is `decimal(18,4)` for every currency (ADR 0004). This slice does not persist amounts. |
| R4 | `GET /currencies` requires a valid Bearer. Missing / dead token → 401. **Never** 302 to hosted login. All four roles get 200. |
| R5 | Do not register `currencies.read`. Use default `.RequireAuthorization()` / `[Authorize]`. |
| R6 | Query name is **`enabledOnly`** (not `isEnabled`: that name collides with the item field and reads as “false = disabled rows only”). Boolean only. Omitted or `false` = **all rows**. `true` = **enabled rows only**. No “disabled-only” filter. No pagination. No search. |
| R7 | Every item includes `isEnabled`: `{ code, name, decimalPlaces, isEnabled }`. The full list uses that field to tell a new reporting-currency picker what is still allowed. |
| R8 | `Currencies.IsEnabled` is **platform-wide**, not per tenant. Disabling a code does not UPDATE tenant rows. A disabled code cannot be a **new** `ReportingCurrency`. Existing tenants keep the historical value. `SetReportingCurrency` rejects a disabled code. |
| R9 | Phase 1 may change a tenant’s reporting currency (no ledger balances are keyed on it yet). The HTTP for that change is the tenants slice. This slice only supplies the domain method and the catalog. |
| R10 | The hot path does not JOIN `Currencies`. Lookups go through `ICurrencyCatalog` or the `char(3)` already on the row. |
| R11 | `Money` equality is code + amount. Cross-currency add/subtract is forbidden. No balance column in Phase 1; the VO still lives in Domain. The VO does **not** depend on `ICurrencyCatalog` and does **not** round to `DecimalPlaces`. |
| R12 | Seed: NZD, AUD, USD, EUR, GBP, JPY. English official names in §6. |
| R13 | Existing tenant rows: script adds `ReportingCurrency` with a temporary DEFAULT `'NZD'`, then drops the default. Final contract is NOT NULL, no default. An empty table also applies. |
| R14 | No currency domain events in this slice. |

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| `Currency` | Platform entity | `Code` / `Name` / `DecimalPlaces` / `IsEnabled`. No tenant. Not a workflow aggregate. |
| `Money` | Value object | `decimal Amount` + `string Currency` (three-letter code). No catalog dependency. |
| `Tenant` | Aggregate (column added) | `ReportingCurrency`. `Create` requires an enabled code. `SetReportingCurrency` checks the catalog. |
| `ICurrencyCatalog` | Port | In-process read. Not a Domain entity. |

Invariants:

- `Currency.Code` is length 3, A–Z.
- `DecimalPlaces` is a reasonable 0–4 (seed uses 0 or 2).
- `Money` rejects cross-currency arithmetic; it does not assume rate 1.
- `Tenant.Create` reporting currency must be an **enabled** catalog code.
- An existing tenant may keep a code that is later disabled. Only a **change** to a new code re-checks enabled.

Events:

- None. `TenantDisabled` stays with identity / tenants.

Keep [domain-model.md](../domain-model.md) in the same change.

`Tenant.Create` changes from identity-auth `(name, code)` to `(name, code, reportingCurrency)`. This slice updates Domain, mapping, and every test that constructs a Tenant.

---

## 6. Database

| Table | Change | Indexes / FK |
| --- | --- | --- |
| `Currencies` | add | PK `Code char(3)`. No other unique index. |
| `Tenants` | alter | `ReportingCurrency char(3) NOT NULL`; `FK_Tenants_Currencies_ReportingCurrency` → `Currencies.Code` ON DELETE RESTRICT |

Scripts:

```text
0008_currencies.sql
0009_tenants_reporting_currency.sql
```

Do not invent `0002`. identity-auth already shipped `0001` and `0003`–`0007`.

### 6.1 `Currencies`

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Code | char(3) | no | PK, ISO upper case |
| Name | nvarchar(100) | no | |
| DecimalPlaces | tinyint | no | Minor units. Not the money-column scale |
| IsEnabled | bit | no | Default 1. Platform flag |

No `NumericCode` (ADR 0009).

Seed (`IsEnabled = 1`):

| Code | Name | DecimalPlaces |
| --- | --- | --- |
| NZD | New Zealand Dollar | 2 |
| AUD | Australian Dollar | 2 |
| USD | United States Dollar | 2 |
| EUR | Euro | 2 |
| GBP | Pound Sterling | 2 |
| JPY | Japanese Yen | 0 |

### 6.2 `Tenants.ReportingCurrency`

identity-auth `0005_tenants.sql` does **not** have this column. This slice ALTERs forward.

Final contract matches [database-design.md](../database-design.md) §6.5: `char(3) NOT NULL` + FK, no column default.

Apply 0008 (table + seed) then 0009 (ALTER). 0009:

1. `ADD ReportingCurrency char(3) NOT NULL CONSTRAINT DF_Tenants_ReportingCurrency DEFAULT ('NZD')`
2. `ALTER TABLE … ADD CONSTRAINT FK_…`
3. `ALTER TABLE … DROP CONSTRAINT DF_Tenants_ReportingCurrency`

Keep [database-design.md](../database-design.md) §9 in the same change.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Query | `GetCurrencies` | `{ code, name, decimalPlaces, isEnabled }[]` | Valid Bearer; `enabledOnly=true` keeps enabled rows; omitted / `false` returns all; stable `Code` sort |
| Port | `ICurrencyCatalog.Get` / `TryGet` | One row or empty | Normalise code to upper case; missing → empty, do not throw |
| Port | `ICurrencyCatalog.List` | Read-only list | Optional enabled-only; loaded at startup |
| Port | `ICurrencyCatalog.IsEnabled` | bool | Missing code = false |
| Port | `ICurrencyCatalog.Reload` | void | Tests / startup. No write API in Phase 1; production hot path does not depend on Reload |
| Domain | `Money.Create` / add / subtract | `Money` | Same currency only |
| Domain | `Tenant.Create` / `SetReportingCurrency` | Tenant | New code must be enabled. Application looks up the catalog; do not inject the port into the entity |

`ICurrencyCatalog` lives in `Application/Common/Interfaces`. Implementation lives in Infrastructure and loads the table into memory at startup. Product `GET` does not query the table every time (integration tests may).

No Command in this slice. Disabling a currency is SQL / a later slice, not Phase-1 HTTP.

---

## 8. API

One resource route. Root path, not under `/users`.

| Method | Route | Auth | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/currencies` | Authenticated (default Authorize) | 200 array | 401 |

`GET /currencies`  
`GET /currencies?enabledOnly=true`  
`GET /currencies?enabledOnly=false`

| `enabledOnly` | Behaviour |
| --- | --- |
| Omitted | Same as `false`: all rows, including disabled |
| `false` | All rows |
| `true` | Only `isEnabled = true` |
| Any other string | 400 |

No “disabled-only” query. Callers that need disabled rows use the default list and filter on item `isEnabled`.

200 example (default / `enabledOnly=false`; seed rows are all enabled):

```json
[
  {
    "code": "AUD",
    "name": "Australian Dollar",
    "decimalPlaces": 2,
    "isEnabled": true
  },
  {
    "code": "JPY",
    "name": "Japanese Yen",
    "decimalPlaces": 0,
    "isEnabled": true
  }
]
```

- Array sorted by `code` ascending.
- Every item has `isEnabled`.
- No write. No `GET /currencies/{code}` (the list is small; historical codes use the full list + `isEnabled`, or `ICurrencyCatalog.Get` on the server).
- Customer / SystemAdmin / TenantAdmin / Adviser all 200, never 403.
- Missing Bearer → 401, not 404.

Keep [api-design.md](../api-design.md) §7.2 in the same change.

---

## 9. UI

None — API only.

The Adviser Portal current slice has already landed (session probe). Do not add a currencies page. Tenant forms consume `GET /currencies` later.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `Money` same-currency add/subtract, cross-currency fails, equality; `Tenant.Create` rejects an empty code; `SetReportingCurrency` rejects a disabled code and accepts an enabled code |
| Application.FunctionalTests | No Bearer → 401 and not 302; 200 for each of the four roles; default list contains the six seed rows and every item has `isEnabled`; `enabledOnly=true` omits a disabled row (test inserts one via SQL and Reloads); `enabledOnly=false` matches omit and includes the disabled row; illegal value → 400; sort by `code` |
| Infrastructure.IntegrationTests | After 0008/0009: table exists, six seed rows, `Tenants.ReportingCurrency` exists, FK exists, no `0002` file |

No cross-tenant isolation tests (platform table, no TenantId). Isolation starts at the tenants slice.

---

## 11. Locked in this spec

Do not open a new ADR (0004 and 0009 are accepted).

| Item | Lock |
| --- | --- |
| Script numbers | `0008` table + seed, `0009` ALTER reporting currency. Do not back-fill 0002 |
| HTTP auth | Default Authorize. No `currencies.read` |
| Query | `enabledOnly` (bool). Omitted / `false` = all; `true` = enabled only. No disabled-only list |
| Response | `{ code, name, decimalPlaces, isEnabled }` per row |
| `IsEnabled` | Platform catalog flag. Not a tenant column. Disable does not rewrite tenant rows |
| `DecimalPlaces` | Minor units for UI / validation. Not `decimal(18,4)` scale |
| `Money` | Domain VO + unit tests only. No table, no EF converter, no HTTP, no catalog dependency |
| Seed English names | Table in §6 |
| Portal | No page |
| List page size | Not this slice (catalog is not paged) |

Still open, not this slice:

- `/tenants` reporting-currency copy and PUT contract → tenants
- FX port shape → Phase 2

---

## 12. Suggested commits

The repository should build after each commit.

1. `0008_currencies.sql` + EF `Currency` mapping + integration test “table + six seed rows”
2. `0009_tenants_reporting_currency.sql` + `Tenant.ReportingCurrency` mapping + `Create` signature change
3. `Money` VO + Domain.UnitTests
4. `ICurrencyCatalog` + Infrastructure startup load / Reload
5. `GetCurrencies` + `GET /currencies` + 401 / four-role 200 functional tests

Do not put tenants HTTP in these commits.
