---
title: Tenant custody modes
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-18
last_updated: 2026-11-26
related:
  - tenants.md
  - account-custody-mode.md
  - custody-modes.md
  - accounts.md
  - demo-tenant-seed.md
  - ../adr/0020-account-custody-mode.md
  - ../backoffice/back-office.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
  - ../future-plans.md
---

# Tenant custody modes

Amendment to [tenants.md](tenants.md) and to account open in [accounts.md](accounts.md) on `webapi`. It is the third slice of the custody modes thread ([future-plans.md](../future-plans.md) §9), after [account-custody-mode.md](account-custody-mode.md) and [custody-modes.md](custody-modes.md). [ADR 0020](../adr/0020-account-custody-mode.md) item 1 allows a Tenant one setting: which custody modes its accounts may be opened with. This slice ships that setting.

SystemAdmin sets the **allowed custody modes** of a Tenant on `POST /tenants` and `PUT /tenants/{id}`, and in the Back Office. `POST /accounts` refuses a custody mode the Tenant does not allow. Nothing else reads the setting. Script `0033_tenants_allowed_custody_modes.sql`. No new route, no new policy, no problem code.

**Status is `accepted` (2026-11-18) and landed** on branch `feature/tenant-custody-modes`: `Tenant.SetAllowedCustodyModes` and `Allows`, script `0033_tenants_allowed_custody_modes.sql`, `allowedCustodyModes` on the tenant routes and item, the check on `POST /accounts`, the Back Office tenant pages and the Adviser Portal New account form. The amendments in tenants, accounts and demo-tenant-seed, in the Back Office and Adviser Portal page contracts, in domain-model, database-design, api-design and the glossary are in force from the same change.

---

## 1. Summary

The platform operator decides which kinds of account a firm may open: External accounts, Custodial accounts, or both. The setting is a check when an account is opened, not a branch through the system: narrowing it changes no existing account and no other rule. Every existing Tenant allows both, so the slice changes no behaviour until SystemAdmin narrows a Tenant.

| | Allowed custody modes |
| --- | --- |
| Values | `External`, `Custodial`, or both. Never none |
| Who sets it | SystemAdmin (`tenants.manage`), on create (required) and on update (optional) |
| Who reads it | SystemAdmin on every tenant item; TenantAdmin and Adviser on their own firm's item (`GET /tenants/by-code/{code}`) |
| What it checks | `POST /accounts` only |
| Existing Tenants | Both (script `0033`) |
| Existing accounts | Untouched by any change of the setting |

---

## 2. Scope

**In**

- Domain: `Tenant` holds its allowed custody modes. A new Tenant allows both. `SetAllowedCustodyModes` replaces them and refuses an empty set. `Allows(CustodyMode)`
- Script `database/schema/0033_tenants_allowed_custody_modes.sql`: two bit columns on `Tenants`, back-filled to allow both, with a CHECK that at least one is set
- Application: `CreateTenant` requires the setting; `UpdateTenant` may change it; the three tenant reads return it; `CreateAccount` refuses a custody mode the Tenant does not allow
- HTTP: `allowedCustodyModes` on the `POST /tenants` and `PUT /tenants/{id}` bodies and on the tenant item
- Back Office: the setting on the New tenant form and on the tenant detail page
- Adviser Portal: the New account form offers only the custody modes the firm allows
- Tests listed in §10

**Out**

- Any rule other than account open that reads the setting: transactions, transfers, conversions, reopen, portfolio assignment and rebalance on an existing account do not check it (R8)
- Closing, converting or flagging existing accounts when a Tenant is narrowed
- A per-Adviser or per-Customer setting
- A setting that switches off the Custodial rules of [custody-modes.md](custody-modes.md) for a Tenant. The rules belong to the Account, not to the Tenant (ADR 0020 item 1)
- Other per-tenant feature flags, quotas or billing (tenants §2 Out)
- A tenant event or outbox row for a change of the setting
- Customer Portal changes. A Customer cannot read the tenant item (tenants R1a)
- Caller-supplied numbers on External accounts and read-model grouping (custody modes slices 4 and 5)

---

## 3. Stories

1. As a SystemAdmin I create a tenant and state which custody modes its accounts may be opened with.
2. As a SystemAdmin I change a tenant's allowed custody modes. Accounts the firm already has are not affected.
3. As a SystemAdmin I see a tenant's allowed custody modes on its item, in Scalar and in the Back Office.
4. As a TenantAdmin or Adviser I open an account only in a custody mode my firm allows. Any other → 400 with a message that says so.
5. As an Adviser, the New account form offers only the custody modes my firm allows.
6. As an Adviser in a firm that no longer allows Custodial accounts, I keep working on the Custodial accounts the firm already has: deposits, trades, transfers, portfolio, close and reopen.
7. As a SystemAdmin opening an account in a tenant through Scalar, I follow that tenant's setting like its own people.

---

## 4. Rules

### 4.1 The setting

| ID | Rule |
| --- | --- |
| R1 | Unchanged access rules: tenants R1, R1a, R2 and R13 (policies, 401 / 403 / 404, PublicId only, `rowVersion` and 409). Only `tenants.manage` writes the setting. |
| R2 | Every Tenant has **allowed custody modes**: a non-empty set drawn from `External` and `Custodial`. |
| R3 | **Create.** `POST /tenants` requires `allowedCustodyModes`: a JSON array of custody mode names, matched by exact enum name like account-custody-mode R3. Missing, `null`, empty, an unknown name, a different case (`"custodial"`) or the same name twice → 400 with `errors.AllowedCustodyModes` and the message `AllowedCustodyModes must list External, Custodial or both, each once.` No tenant is written. The order of the array has no meaning. |
| R4 | **Update.** On `PUT /tenants/{id}` the field is optional. Omitted or `null` → the setting is unchanged. Present → the R3 shape rules, and the array replaces the setting. `rowVersion` is required as today (tenants R13). The setting is changed only by PUT; disable and enable do not touch it. |
| R5 | **Read.** The tenant item on `GET /tenants`, `GET /tenants/{id}` and `GET /tenants/by-code/{code}` carries `allowedCustodyModes`, always in the order `External`, `Custodial`. |
| R6 | **Existing and seeded Tenants.** Script `0033` sets every existing Tenant to allow both. A Tenant created through the Domain factory without a stated setting (the Development seeds, the demo seed, test helpers) allows both. Only `POST /tenants` requires a stated setting. |

### 4.2 Opening an account

| ID | Rule |
| --- | --- |
| R7 | **Check on open.** `POST /accounts` with a `custodyMode` that the account's Tenant does not allow → 400 with `errors.CustodyMode` and the message `The tenant does not allow {custodyMode} accounts.` (for example `The tenant does not allow Custodial accounts.`). No account is written. The Tenant is the caller's own for TenantAdmin and Adviser, and the one named by `tenantId` for SystemAdmin. |
| R8 | **Order.** Request-shape validation runs first, as today: an unknown `custodyMode` and `Custodial` with a type other than `Brokerage` are still refused by the validator (account-custody-mode R3, custody-modes R3), whatever the Tenant allows. In the handler, R7 runs after the Tenant is resolved (SystemAdmin: unknown `tenantId` → 404), after the Disabled-tenant check, after the Customer lookup (a Customer the caller cannot see is still 404) and the Disabled-Customer check, and after the currency check (accounts R9). |
| R9 | **Only new accounts.** Changing the setting changes no Account, Transaction, holding or portfolio assignment. Only `POST /accounts` reads it. On an existing account of a mode the Tenant no longer allows, every route works as before: transactions and reverse, transfers and conversions between two such accounts, rename, close, **reopen**, portfolio assignment and rebalance of a Custodial account, and every read. The same pattern as a disabled currency: it blocks a new reference, and existing references stay. |
| R10 | **No other effect.** The setting does not change the manual entry matrix, the same-custody-mode rule, the `bookedAt` rule or portfolio eligibility ([custody-modes.md](custody-modes.md) §4). It raises no domain event and writes no outbox row. A Disabled Tenant keeps its setting. |

Policies: none added. `tenants.manage` writes the setting. `tenants.read` reads it on the own firm's item. `accounts.create` is checked against it.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `Tenant` | Aggregate (existing) | Holds the allowed custody modes as two flags, `AllowsExternalAccounts` and `AllowsCustodialAccounts`. `Create` keeps its signature and starts with both set. `SetAllowedCustodyModes` and `Allows` are new. `AllowedCustodyModes` is a read-only list in enum order, not mapped |
| `CustodyMode`, `Account` | (existing) | Unchanged. `Account.Create` does not read the Tenant |
| Events | | None added. `SetAllowedCustodyModes` raises nothing |

```text
Tenant.Create(name, code, reportingCurrency, publicId?)   // unchanged; allows External and Custodial

SetAllowedCustodyModes(IReadOnlyCollection<CustodyMode> modes)
  modes empty → DomainException
  sets each flag to whether its mode is in modes (a repeated mode counts once)

Allows(CustodyMode mode) -> bool
AllowedCustodyModes -> IReadOnlyList<CustodyMode>          // External before Custodial
```

Invariants:

- A Tenant allows at least one custody mode. Held by `SetAllowedCustodyModes` and by `CK_Tenants_AllowedCustodyModes`.
- An account is opened only in a custody mode its Tenant allows **at the time of open**. Held in Application (R7), like the Disabled-tenant check on open. It is not an invariant of existing accounts (R9), so there is no database rule between `Accounts` and `Tenants` for it.

The duplicate and empty 400s of R3 are raised by the validator with their field key before the Domain is called; the Domain check is the backstop for callers that do not go through HTTP.

Recorded in [domain-model.md](../domain-model.md) §4.1.

---

## 6. Database

Script: `database/schema/0033_tenants_allowed_custody_modes.sql`. Forward only. Do not add EF migrations.

| Table | Change | Index / FK |
| --- | --- | --- |
| `Tenants` | Add `AllowsExternalAccounts bit NOT NULL` with constraint `DF_Tenants_AllowsExternalAccounts` DEFAULT 1, and `AllowsCustodialAccounts bit NOT NULL` with `DF_Tenants_AllowsCustodialAccounts` DEFAULT 1. Existing rows get 1 in both. Add `CK_Tenants_AllowedCustodyModes` CHECK (`[AllowsExternalAccounts] = 1 OR [AllowsCustodialAccounts] = 1`) | none |
| `Accounts` and every other table | none | none |

- The defaults **stay** after the script, unlike `0031`. Allowing both is the setting every Tenant had before this slice, so a row that does not name the columns gets no new meaning. The raw-SQL fixture inserts into `Tenants` in Infrastructure.IntegrationTests (five today) keep working.
- The EF mapping does not declare the defaults (no `HasDefaultValue`), so EF always writes both columns from the Domain.
- Two flags, not one bit-mask column or a JSON list: each is readable on its own in SQL, the CHECK is one line, and a third custody mode is not planned. The HTTP shape is a list (R3) and does not depend on this.

Existing data:

- Every environment: all Tenants allow both after the script. No behaviour changes until SystemAdmin narrows one.
- Functional tests create their own tenants and are not affected by the back-fill.

Recorded in [database-design.md](../database-design.md) §6.5 and §9.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Command | `CreateTenant` | `AllowedCustodyModes` (list of strings) required. Validator: R3. Handler: `Tenant.Create`, then `SetAllowedCustodyModes` with the parsed values, in the same save |
| Command | `UpdateTenant` | `AllowedCustodyModes` optional. Validator: R3 when not `null`. Handler: after the `rowVersion` check, `SetAllowedCustodyModes` when present |
| Query | `GetTenants`, `GetTenantById`, `GetTenantByCode` | `TenantDto` gains `AllowedCustodyModes` (enum names, R5 order) |
| Command | `CreateAccount` | After the currency check: `tenant.Allows(custodyMode)`, else `ValidationException` on `CustodyMode` (R7, R8). The handler already loads the Tenant |
| Command | `DisableTenant`, `EnableTenant`, `GrantTenantReport`, `RevokeTenantReport` | Unchanged |
| Command / Query | Every account, transaction, portfolio and read use case other than `CreateAccount` | Unchanged (R9) |

Where each rule lives:

| Rule | Where |
| --- | --- |
| Shape of `allowedCustodyModes` | One validator rule shared by `CreateTenantCommandValidator` and `UpdateTenantCommandValidator`. It needs no database read |
| At least one mode | The validator for the 400; `Tenant.SetAllowedCustodyModes` and the CHECK for the invariant |
| Custody mode allowed on open | `CreateAccountCommandHandler`, next to the Disabled-tenant check |

A refused `POST /accounts` writes nothing: the check throws before `Account.Create`.

---

## 8. API

No new routes. No new policies.

| Method | Route | Change |
| --- | --- | --- |
| POST | `/tenants` | `allowedCustodyModes` required |
| PUT | `/tenants/{id}` | `allowedCustodyModes` optional |
| GET | `/tenants`, `/tenants/{id}`, `/tenants/by-code/{code}` | Item carries `allowedCustodyModes` |
| POST | `/accounts` | `custodyMode` must be allowed by the account's Tenant |

### 8.1 Item

```json
{
  "id": "<publicId>",
  "name": "North Advisory",
  "code": "north-advisory",
  "reportingCurrency": "NZD",
  "allowedCustodyModes": ["External", "Custodial"],
  "isActive": true,
  "rowVersion": "<opaque>",
  "created": "2026-09-13T00:00:00+00:00"
}
```

### 8.2 Bodies

POST `/tenants`:

```json
{
  "name": "North Advisory",
  "code": "north-advisory",
  "reportingCurrency": "NZD",
  "allowedCustodyModes": ["External"]
}
```

PUT `/tenants/{id}` (name and `rowVersion` stay required, tenants §8.3):

```json
{
  "name": "North Advisory",
  "reportingCurrency": "NZD",
  "allowedCustodyModes": ["External", "Custodial"],
  "rowVersion": "<opaque>"
}
```

### 8.3 Errors

All are ordinary validation: 400 with `errors`, no problem `code` ([api-design.md](../api-design.md) §4.2 C1).

| Case | Response |
| --- | --- |
| `POST /tenants` without `allowedCustodyModes`, or with `null`, `[]`, an unknown name, a different case, or a repeated name | 400, `errors.AllowedCustodyModes` |
| `PUT /tenants/{id}` with `[]`, an unknown name, a different case, or a repeated name | 400, `errors.AllowedCustodyModes` |
| `POST /accounts` with a custody mode the Tenant does not allow | 400, `errors.CustodyMode` |

This is a breaking change for one kind of caller: a client that creates tenants without `allowedCustodyModes` now gets 400. The only such clients are the Back Office and Scalar; the Back Office ships in the same deploy.

Cross-tenant or wrong-collection id → **404**, not 403, before R7 is evaluated.

Keep the resource catalog in [api-design.md](../api-design.md) §7.3 pointing here for these rules.

---

## 9. UI

Page contracts: [back-office.md](../backoffice/back-office.md) (BO14, BO15) and [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (D21), amended and accepted together with this spec. The Adviser Portal BFF does not change: `GET /tenants/by-code/{code}` is already proxied.

**Back Office**

- New tenant form (`/tenants/new`): a required "Custody modes" group with two checkboxes, "External accounts" and "Custodial accounts". Both are checked when the form opens. Create is disabled while neither is checked. `POST /tenants` carries the checked values.
- Tenant detail (`/tenants/{id}`): shows the allowed custody modes next to the reporting currency. The edit form has the same two checkboxes, set from the item, and sends `allowedCustodyModes` on every `PUT` together with name, reporting currency and `rowVersion`. Save is disabled while neither is checked. Under the group: "Changing this affects only new accounts."
- The setting is on the detail page, not on `/tenants/{id}/config`: it is written by `PUT /tenants/{id}`, which also carries the name and the `rowVersion` that the detail page already holds.
- The tenant list shows no new column.

**Adviser Portal**

- The New account form (`/customers/:id/accounts/new`) offers only the custody modes in the firm's `allowedCustodyModes`, read from the tenant item the shell already loads (`GET /tenants/by-code/{code}`).
- Two allowed: D17 is unchanged (nothing preselected).
- One allowed: the Custody choice shows that option alone, already selected; the form needs no other custody input. If it is Custodial, Type is Brokerage and the Type control is disabled, as in D17.
- Until the tenant item has loaded, no custody option is shown and the form cannot be submitted.
- If `POST /accounts` returns 400 `errors.CustodyMode` (the setting changed after the page loaded), the form shows that sentence at the Custody choice.

**Customer Portal**

None.

### 9.1 Seeds

No seed code changes. Every seeded Tenant allows both (R6):

- `TestSeed` and the Development identity seed (`demo`, `demo2`), and the Development smoke seed (`smoke`): created through `Tenant.Create`, so both. On a database that existed before this slice, script `0033` gives them both.
- The demo seed ([demo-tenant-seed.md](demo-tenant-seed.md)): a new `demo` tenant allows both. An existing `demo` tenant is not changed (demo-tenant-seed R5), and that includes this setting.
- In production the smoke tenant is created by hand in the Back Office; both boxes are checked by default, and the smoke account is External.

---

## 10. Tests

The tests are the executable form of §4. Where they live:

| Project | Where |
| --- | --- |
| Domain.UnitTests | `Entities/TenantTests.cs` |
| Application.FunctionalTests | `Tenants/TenantAllowedCustodyModesTests.cs`, `Custody/TenantCustodyModesNarrowingTests.cs` |
| Infrastructure.IntegrationTests | `AppliedSchema/TenantsAllowedCustodyModesSchemaTests.cs`, `DemoTenantSeedTests.cs` |

---

## 11. Locked in this spec

No new ADR. [ADR 0020](../adr/0020-account-custody-mode.md) item 1 decided that the Tenant gets at most this one setting, and that it is a check on open.

| Item | Lock |
| --- | --- |
| Values | `External`, `Custodial` or both; never none |
| Writer | SystemAdmin (`tenants.manage`); required on create, optional on update |
| Reader of the rule | `POST /accounts` only |
| Narrowing | Changes no existing data and no other route; reopen still allowed |
| Existing Tenants | Both, by script `0033` |
| Storage | Two bit columns, DEFAULT 1 kept, CHECK at least one |
| Domain | `Tenant.Create` signature unchanged; a new Tenant allows both |
| Routes, policies, problem codes, events | None added |
| Back Office place | Tenant detail page, not the configuration page |

Confirmed when this spec was accepted (2026-11-18). Each is written into the rules above as stated here:

1. **`allowedCustodyModes` is required on `POST /tenants`** (R3). It makes the operator state the setting for a new firm. The alternative is to make it optional with a default of both: the setting can be changed at any time and narrowing it has no effect on existing data, so a default is less risky here than it was for `Account.CustodyMode`.
2. **Narrowing does not block reopen** (R9). A Closed account of a mode the Tenant no longer allows can be reopened. The alternative is to treat reopen as an open and refuse it.
3. **The database defaults stay** (§6). The alternative is to drop them as `0031` did, so that an insert must name the columns; that changes five fixture inserts and gives no extra safety, because both is the neutral value.
4. **With one allowed mode, the Adviser Portal preselects it** (§9). D17's "nothing is preselected" holds only when there is a real choice.
5. **The setting is on the Back Office tenant detail page** (§9), because it goes through `PUT /tenants/{id}`. The alternative is the configuration page, which would then have to send the name and `rowVersion` as well, or a separate route.

Still open, not this slice:

- Caller-supplied numbers on External accounts (custody modes slice 4)
- Read-model and report grouping by custody mode (slice 5)
- Any further per-tenant feature flags
