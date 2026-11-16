---
title: Account custody mode
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-17
last_updated: 2026-11-17
related:
  - accounts.md
  - posting.md
  - portfolios.md
  - ../adr/0020-account-custody-mode.md
  - ../adr/0011-ledger-cash-holdings-reversal.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../customer-portal/customer-portal.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
  - ../future-plans.md
---

# Account custody mode

Structural amendment to the Account container on `webapi` ([accounts.md](accounts.md)). Every Account gains a **custody mode**: `External` or `Custodial`. It is chosen when the account is opened and never changes. The decision and the rejected options are in [ADR 0020](../adr/0020-account-custody-mode.md).

This slice adds the attribute and carries it through every layer: Domain, database, the create body, the account item, a list filter, both portals and the seeds. It adds **no rule that depends on the mode**. Only `External` can be selected; `Custodial` exists as a reserved value, the same way the `Property` and `Credit` account types are reserved. After this slice every account is `External` and the system behaves as it does today.

The rules for Custodial accounts are the **next** increment and are not specified here. No new route, no new policy, no new `TransactionType`, no problem code. Script `0031_accounts_custody_mode.sql`.

**Status is `accepted` (2026-11-17).** Not landed. ADR 0020 is accepted with it. The amendments to accounts, ADR 0011, domain-model, database-design, api-design, smoke-test, the glossary and the two portal page contracts are in force from the same change.

---

## 1. Summary

```text
Before                              After
Accounts (Type, Currency, Status)   Accounts (Type, CustodyMode, Currency, Status)
POST /accounts { type, currency }   POST /accounts { type, custodyMode, currency }
```

| Value | Meaning | In this slice |
| --- | --- | --- |
| `External` | The assets are held outside the platform. The platform records what happened elsewhere. | The only value that can be selected. Every existing account gets it. |
| `Custodial` | The assets are held by the platform, and the platform executes the trades. | Reserved. `POST /accounts` refuses it. |

Custody mode is independent of `Account.Type`. `Type` says whether the account may hold instruments. Custody mode says where the account's numbers come from and which controls apply to it. In this slice no control reads it.

Why the attribute comes first: it is one change with no behaviour, like [leg-accounts.md](leg-accounts.md) before [internal-transfers.md](internal-transfers.md). Once the field is in the API, the portals, the seeds and the tests, each Custodial rule is one small refusal that a failing test can show. The reserved value keeps the gap closed in between: nobody can open a Custodial account before its rules exist.

---

## 2. Scope

**In**

- Domain: `CustodyMode` enum (`External`, `Custodial`); `Account.CustodyMode`; `Account.Create` takes it; no method changes it
- Script `database/schema/0031_accounts_custody_mode.sql` (forward only; existing rows become `External`) + EF mapping
- Application: `CreateAccount` requires and validates `custodyMode`; `UpdateAccount` refuses it; `GetAccounts` takes an optional `custodyMode` filter; the account item carries it
- HTTP: `custodyMode` on the create body and on the list / get item; `custodyMode` query on `GET /accounts`
- Adviser Portal: the New account form sends `External`; account pages show the custody mode
- Customer Portal: account pages show the custody mode
- `TestSeed` and the Development smoke seed pass `External`
- Tests listed in §10

**Out**

- Opening a Custodial account, and every rule that depends on the mode — next increment. Named here only so the next spec does not reopen them: which account types a Custodial account may have, which transaction types it accepts, same custody mode on every leg account of a Transaction, portfolio assignment by custody mode, and who sets `bookedAt`
- A tenant setting for which custody modes may be opened
- Caller-supplied price, fee and rate for trades and conversions on External accounts. Server pricing stays as it is
- Grouping by custody mode in net worth, in the two reports and on the portal dashboards
- Changing the custody mode of an existing account
- Any change to `Account.Type`, its capability matrix, or the reserved `Property` and `Credit`
- A valuation-only record for property and loans
- The legal structure of the account holder (individual, joint, trust, company)
- Any change to transactions, holdings, portfolios, trade fees, net worth or reports

---

## 3. Stories

1. As an Adviser I open an account for an assigned Customer and state that it is `External`. Everything else about the account works as today.
2. As an Adviser I see on the account list and the account page which custody mode an account has.
3. As a Customer I see the custody mode of my own accounts.
4. As a SystemAdmin I filter a tenant's accounts by custody mode.
5. As any caller I cannot open a `Custodial` account yet (400), and I cannot change the custody mode of an account after it is opened (400).
6. As the next increment I add the rules for Custodial accounts without another schema change to `Accounts` and without touching the create body again.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Unchanged from accounts R1–R6: 401 / 403 / 404, PublicId only, `/accounts` root, `tenantId` rules for TenantAdmin / Adviser / SystemAdmin, Customer and tenant state. Customer scope is unchanged: reads of own accounts only (accounts amendment 2026-11-02). |
| R2 | Every Account has a custody mode, `External` or `Custodial`. It is set on create and is immutable. Rename, close and reopen do not change it. |
| R3 | `POST /accounts` requires `custodyMode`. Missing or `null` → 400 with `errors.CustodyMode`. There is no default. The value is the exact enum name, matched the same way as `type`: `external` is not `External`. |
| R4 | In this slice only `External` can be selected. `Custodial` → 400 with `errors.CustodyMode`, the same treatment as `Property` and `Credit` in accounts R8. Any other value → 400. Nothing is written. |
| R5 | R3 and R4 apply to every caller that can create an account: TenantAdmin, Adviser, and SystemAdmin with `tenantId`. No tenant restricts the value in this slice. |
| R6 | `PUT /accounts/{id}` with `custodyMode` in the body → 400, also when the value equals the stored one. Same rule as `type` and `currency` (accounts R8, R9, R14). The name is not changed. |
| R7 | The list and get item include `custodyMode` as the enum name. A Customer reading an own account receives it too. |
| R8 | `GET /accounts` takes an optional `custodyMode` query: the exact enum name, `External` or `Custodial`. Any other value → 400. It combines with `customerId`, `type`, `enabledOnly` and `search` (all must match) and is applied before `totalCount`. `Custodial` is a valid filter and returns no rows in this slice. Sort is unchanged (accounts R17). |
| R9 | No other behaviour reads the custody mode in this slice. Every transaction type, the cash and holdings rules, close and reopen, portfolio assignment and rebalance, trade fees, net worth and both reports treat an External account exactly as they treat an account today. |
| R10 | Every Account that exists when script `0031` runs becomes `External`. Its transactions are not touched. |
| R11 | The Domain accepts both values on `Account.Create`. Application refuses `Custodial` (R4). This is the same split as `AccountType`: all names exist in the Domain so a later slice does not reshape the type. |

Policies: none added. `accounts.read` / `accounts.create` / `accounts.manage` as in [api-design.md](../api-design.md) §3.1.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `CustodyMode` | Enum | New. `External` = 0, `Custodial` = 1. |
| `Account` | Aggregate (existing) | Adds `CustodyMode`. `Create` takes it as a required parameter. No method changes it. |
| `AccountOpened` / `AccountClosed` / `AccountReopened` | Event | Unchanged. |

Invariants:

- `CustodyMode` is set on create, then immutable, like `Type` and `Currency`.
- `Type` and `CustodyMode` are independent. This slice adds no rule that combines them.

```text
Create(tenantId, customerId, name, type, custodyMode, currency)
  custodyMode is any CustodyMode; Application rejects Custodial in this slice
  everything else as in accounts §5
```

`Account.Create` has no default for `custodyMode`. Every caller names the value: `CreateAccount`, `TestSeed`, the Development smoke seed and the tests.

Update [domain-model.md](../domain-model.md) §8.5 in the same change.

---

## 6. Database

Script: `database/schema/0031_accounts_custody_mode.sql`. Forward only. Do not edit `0012`. Do not add EF migrations.

| Table | Change | Index / FK |
| --- | --- | --- |
| `Accounts` | add `CustodyMode int NOT NULL` | CHECK `CustodyMode` in 0–1 (`CK_Accounts_CustodyMode`). No new index. No FK. |

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| CustodyMode | int | no | 0 External, 1 Custodial. Immutable in Domain |

Script order:

1. Add `CustodyMode int NOT NULL` with a default of 0. Existing rows are filled by the default.
2. Drop the default. After the script, an insert that does not name the column fails. The API has no default (R3), so the table has none either.
3. Add the CHECK.

Unlike `0030` (`TransactionCashLegs.Role`), the default does not stay.

Existing data: every environment's accounts become `External`. Production holds the smoke tenant's `Smoke Cash` account ([smoke-test.md](../smoke-test.md) §3) and any account made by hand; they need no other step. Functional tests create their own rows and are not affected by the back-fill.

No index: the list is always inside one tenant, and `(TenantId, CustomerId)` and `(TenantId, IsActive)` already serve it.

Update [database-design.md](../database-design.md) in the same change.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Command | `CreateAccount` | Takes `custodyMode`. Required; `External` only (R3, R4). Passes it to `Account.Create`. |
| Command | `UpdateAccount` | A body that includes `custodyMode` → 400 (R6). |
| Query | `GetAccounts` | Optional `custodyMode` filter (R8). Item carries `custodyMode`. |
| Query | `GetAccountById` | Item carries `custodyMode`. |
| Command | `CloseAccount`, `ReopenAccount` | Unchanged. |

R3 and R4 are validator rules next to the existing `type` rule: they need no database read.

No other handler changes. In particular `CreateTransaction`, `CreateInternalTransfer`, `CreateFxConversion`, `ReverseTransaction`, `AssignPortfolio` and `RebalanceAccount` do not read the custody mode in this slice.

---

## 8. API

No new routes. No new policies.

| Method | Route | Change |
| --- | --- | --- |
| POST | `/accounts` | Body gains required `custodyMode` |
| PUT | `/accounts/{id}` | `custodyMode` in the body → 400 |
| GET | `/accounts` | Item gains `custodyMode`. New optional query `custodyMode` |
| GET | `/accounts/{id}` | Item gains `custodyMode` |

### 8.1 Bodies

Create (TenantAdmin / Adviser). SystemAdmin adds `tenantId` as today:

```json
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Everyday spending",
  "type": "Bank",
  "custodyMode": "External",
  "currency": "NZD"
}
```

List / get item:

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Everyday spending",
  "type": "Bank",
  "custodyMode": "External",
  "currency": "NZD",
  "status": "Open",
  "isActive": true,
  "cashBalance": 0.00,
  "rowVersion": "<base64>"
}
```

`custodyMode` in JSON is the enum name, not an int.

List filter: `GET /accounts?custodyMode=External`.

The PUT, close and reopen bodies do not change.

### 8.2 Errors

Missing, reserved or unknown `custodyMode` on create, `custodyMode` on PUT, and an unknown `custodyMode` filter are ordinary validation: 400 with `errors`, no problem `code` ([api-design.md](../api-design.md) §4.2 C1).

This is a breaking change for callers of `POST /accounts`: a body without `custodyMode` that returned 201 before now returns 400. `webapi` and both portals ship in the same deploy. A caller that uses Scalar adds the field by hand.

Keep the resource catalog in [api-design.md](../api-design.md) §7.6 pointing to [accounts.md](accounts.md) and this file for field rules.

---

## 9. UI

Page contracts: [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) and [customer-portal.md](../customer-portal/customer-portal.md), amended in the same change that accepts this spec. Neither BFF changes: `/accounts` is already proxied, and the query string passes through.

**Adviser Portal**

- New account form (`/customers/:id/accounts/new`): no custody control in this slice. The form always sends `custodyMode: "External"`. The selector arrives with the increment that opens Custodial accounts.
- A "Custody" value is shown next to "Type" wherever a page shows an account's type: the accounts list (`/accounts`), the accounts table on the customer page (`/customers/:id`) and the account page (`/accounts/:id`). The text is the enum name, as for Type.
- No filter control for custody mode in this slice.

**Customer Portal**

- A "Custody" value is shown next to "Type" on the accounts list (`/accounts`) and the account page (`/accounts/:id`).
- The text is a label, not the enum name: `External` → "Held elsewhere", `Custodial` → "Held on platform". An unknown value is shown as received.
- No grouping and no explanatory sentence in this slice.

### 9.1 Seeds

- `TestSeed`: the four demo accounts of accounts §9.1 are created with `External`. No new demo account. Rows that already exist are not rewritten by the seed; script `0031` has already made them `External`.
- Development smoke seed (`Smoke Cash`, [smoke-test.md](../smoke-test.md) §3): created with `External`.
- The demo seed ([demo-tenant-seed.md](demo-tenant-seed.md)) creates no accounts and does not change.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `Create` stores the given custody mode, for each of the two values; rename, close and reopen leave it unchanged |
| Application.FunctionalTests | Create with `External` → 201, and get returns `custodyMode: "External"`; create without `custodyMode` → 400 with `errors.CustodyMode` and no account is written; `Custodial` → 400; an unknown value and `external` (wrong case) → 400; SystemAdmin create follows the same rules; PUT with `custodyMode` → 400 (also with the stored value) and the name is unchanged; list items carry `custodyMode`; `?custodyMode=External` returns the caller's accounts, `?custodyMode=Custodial` returns an empty page with `totalCount` 0, `?custodyMode=nope` → 400; the filter combines with `customerId` and `type`; a Customer reading an own account gets `custodyMode`; every existing suite still passes once its account opening sends `External` |
| Infrastructure.IntegrationTests | `Accounts.CustodyMode` is `int NOT NULL`; the CHECK refuses 2; the column has no default (an insert that omits it fails); `0031` turns an account row that existed before it into `External` (scratch database, as for `0024`); two-tenant fixture: the `custodyMode` filter used by tenant A's TenantAdmin returns no account of tenant B; `TestSeed` still creates the four demo accounts, all `External`, and is idempotent on a second run |
| Adviser Portal | The New account form posts `custodyMode: "External"`; the accounts list, the customer page and the account page render the custody mode |
| Customer Portal | The accounts list and the account page render the label for the custody mode |

Fan-out in the functional tests. Twenty-eight test files post to `/accounts`, and sixteen of them have their own `OpenAccountAsync`. Before the field becomes required, one test-only commit moves plain account opening to a single shared method on `AccountHttp` (§12, commit 1). That commit is a pure refactor and passes on its own. Tests that assert on the create request itself (`CreateAccountTests` and similar) keep their own bodies and add the field when R3 lands.

---

## 11. Locked in this spec

ADR: [adr/0020-account-custody-mode.md](../adr/0020-account-custody-mode.md) (`accepted` 2026-11-17, together with this spec).

| Item | Lock |
| --- | --- |
| Name | Custody mode. `Account.CustodyMode`, JSON `custodyMode`, enum `CustodyMode`. Not "book type" |
| Values | `External` = 0, `Custodial` = 1 |
| Where | On the Account. Not on the Tenant, not on the Customer, not a new `Account.Type` |
| Lifetime | Set at open, immutable. No convert command |
| Create | `custodyMode` required. No default |
| This slice | Only `External` can be selected. `Custodial` is reserved → 400 |
| Behaviour | None depends on the mode in this slice |
| Existing rows | All `External`, by script `0031` |
| Script | `0031_accounts_custody_mode.sql`, forward only, no default left on the column |
| Routes and policies | None added |

Confirmed when this spec was accepted (2026-11-17). Each is written into the rules above as stated here:

1. **`custodyMode` is required on create** (R3), instead of defaulting to `External` when omitted. The cost is the test-only commit in §12.
2. **Every existing account becomes `External`** (R10).
3. **Customer Portal wording**: "Held elsewhere" and "Held on platform" (§9).

Still open, not this slice:

- The rules for Custodial accounts and the point at which `Custodial` can be selected
- The tenant setting for allowed custody modes
- Caller-supplied numbers on External accounts
- Read-model and report grouping by custody mode
- What happens to `Other`, `Property` and `Credit`

---

## 12. Suggested commits

Only after `accepted`. The repository should build after each commit. Agents split each behaviour into red then green.

1. Test-only refactor: `AccountHttp.OpenAccountAsync` replaces the private account-opening helpers and the plain `POST /accounts` calls in the functional tests. No production change. Passes on its own.
2. `0031_accounts_custody_mode.sql` + EF mapping + `CustodyMode` enum + `Account.CustodyMode` + the `Account.Create` parameter, with Domain unit tests and the integration tests for the column, the CHECK, the missing default and the back-fill. `CreateAccount`, `TestSeed` and the Development smoke seed pass `External` in the same commit so the tree builds; existing functional tests stay green.
3. Account item carries `custodyMode`; `GET /accounts` takes the `custodyMode` filter (R7, R8) + functional tests and the two-tenant test.
4. Adviser Portal: the form sends `External`; the three pages show the custody mode + frontend tests. `webapi` ignores a body field it does not know, so the form may send the field before commit 6 requires it.
5. Customer Portal: the two pages show the label + frontend tests.
6. `POST /accounts` requires `custodyMode` and accepts only `External` (R3–R5) + functional tests. The shared test method from commit 1 starts sending `External`.
7. `PUT /accounts/{id}` refuses `custodyMode` (R6) + functional tests.
8. Repo docs: mark this file landed. (This file, ADR 0020 and the amendments in accounts, the portal specs, ADR 0011, domain-model, database-design, api-design, smoke-test and the glossary were accepted on 2026-11-17, before commit 1.)

Do not put any Custodial rule, tenant setting or read-model grouping in these commits.
