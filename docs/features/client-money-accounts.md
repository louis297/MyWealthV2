---
title: Client money accounts and deposit references
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-19
last_updated: 2026-11-20
related:
  - ../custodial-roadmap.md
  - ../adr/0021-outside-parties-behind-ports.md
  - tenants.md
  - tenant-custody-modes.md
  - accounts.md
  - custody-modes.md
  - account-custody-mode.md
  - demo-tenant-seed.md
  - ../backoffice/back-office.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../customer-portal/customer-portal.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
---

# Client money accounts and deposit references

New Tenant configuration and an amendment to account open on `webapi`. It is step 4 of [custodial-roadmap.md](../custodial-roadmap.md) §13 and covers roadmap items **B1** (client money bank accounts) and **B2** (deposit reference on a Custodial account). It amends [tenants.md](tenants.md), [accounts.md](accounts.md), the Back Office, Adviser Portal and Customer Portal contracts.

SystemAdmin records, per Tenant and currency, the **client money account**: the trust bank account the firm holds Customers' cash in. A Custodial account can be opened only in a currency that has an active client money account. Every Custodial account gets a **deposit reference** when it is opened. Together they are the account's **deposit instructions**, which the Adviser and the Customer can read.

New routes under `/tenants/{tenantId}/client-money-accounts` (`tenants.manage`) and `GET /accounts/{id}/deposit-instructions` (`accounts.read`). New item fields `depositReference` (account) and `clientMoneyCurrencies` (tenant). Scripts `0038_client_money_accounts.sql` and `0039_accounts_deposit_reference.sql`. No new policy, problem code, transaction type or source kind.

**Status is `accepted` (2026-11-20), landed 2026-11-20.** The eleven points of §11 were confirmed as recommended, and the redraw after a save that hits the unique index (R17 b) was added before acceptance. The amendments to accepted docs listed in §12 are in force from the same change.

Notes from landing:

- `IRandomSymbols`, the random source `DepositReference.Generate` takes, lives in the Domain next to `DepositReference`. Infrastructure implements it with `RandomNumberGenerator` (`CryptoRandomSymbols`), and `RandomDepositReferenceSource` implements `IDepositReferenceSource` with it.
- A `POST /accounts` whose save fails on a constraint other than `UX_Accounts_DepositReference` keeps the existing answer for a unique violation (400 with an `errors` entry under the empty key). It is not retried (R17).
- BO16 "a 400 is shown at its field": the Back Office page tests only render a page; none submits an interactive form. The rule is proven in two parts: `ValidationErrors.From` maps a 400 `errors` body to its fields (unit test), and the page passes that map to each field's error text.
- D32: a deposit instructions read that fails with a status other than 404 shows "Deposit instructions could not be loaded." The 404 sentence is as specified.

Nothing in this slice moves money. Deposits are still entered by hand as `TransferIn` until roadmap B3 and B4 read the bank statement and match receipts by the deposit reference. This slice gives those flows the two things they match on.

---

## 1. Summary

| | Client money account (B1) | Deposit reference (B2) |
| --- | --- | --- |
| What | The firm's trust bank account for one currency: bank name, account name, account number | A short code that identifies one Custodial account in a bank payment |
| Belongs to | A Tenant, one currency | A Custodial account |
| Set by | SystemAdmin (`tenants.manage`), in the Back Office or Scalar | The server, when the account is opened |
| Changes | Never. Only disable and enable | Never, also not on close or reopen |
| Read by | SystemAdmin (full list); everyone who can read a Custodial account (through its deposit instructions); the Tenant's people see only which currencies have one | Everyone who can read the account |
| Rule | At most one active per (Tenant, currency). A Custodial account opens only in a currency that has one | Only Custodial accounts have one. Unique on the platform |

The rule that distinguishes this slice: from now on a Custodial account can exist only where the firm has a real bank account to receive its cash. Existing Custodial accounts are not touched by that rule; they get a deposit reference from the script.

---

## 2. Scope

**In**

- Domain: `ClientMoneyAccount` aggregate (create, disable, enable); `DepositReference` value object (generate, parse, check symbol); `Account` holds a deposit reference, required when Custodial and absent when External
- Scripts: `database/schema/0038_client_money_accounts.sql` (new table) and `database/schema/0039_accounts_deposit_reference.sql` (new column on `Accounts`, back-fill of existing Custodial accounts, CHECK and unique index)
- Application: client money account commands and queries; `CreateAccount` checks for an active client money account and draws a deposit reference; `GetDepositInstructions`; the account reads map `depositReference`; the tenant reads map `clientMoneyCurrencies`
- HTTP: five client money account routes; `GET /accounts/{id}/deposit-instructions`; `depositReference` on the account item; `clientMoneyCurrencies` on the tenant item
- Back Office: a "Client money accounts" section on the tenant configuration page
- Adviser Portal: deposit instructions on a Custodial account page; the New account form offers for a Custodial account only the currencies with a client money account
- Customer Portal: "How to pay in" on a Custodial account page
- Seeds: `TestSeed` adds client money accounts to its firms
- Tests listed in §10

**Out**

- Reading a bank statement, a bank feed port, a connection or a cursor (roadmap B3). The client money account names no connection yet (§11 point 9)
- Matching receipts by reference, suspense, returning a payment (roadmap B4). Hand `TransferIn` on a Custodial account stays allowed
- A Customer's nominated bank account and withdrawals (roadmap B5, B6)
- Bank reconciliation and the bank's balance (roadmap B7)
- Interest the bank pays on the trust account (roadmap B7, I2)
- Deposit references on External accounts. Their cash is paid to banks the platform does not hold
- Changing a client money account's details. A wrong detail is corrected by disabling the row and adding a new one (R7)
- A TenantAdmin or Adviser write, or a TenantAdmin list, of client money accounts
- Searching accounts by deposit reference (`GET /accounts?search=`). B4's manual match decides what it needs
- A domain event or outbox row for any change in this slice
- The demo seed. It adds no client money account (§11 point 10)
- Showing the deposit reference in the reports

---

## 3. Stories

1. As a SystemAdmin I record a firm's client money account for a currency, with the bank name, account name and account number, so the firm can open Custodial accounts in that currency.
2. As a SystemAdmin I disable a client money account that is no longer used, and add the replacement. I enable an old one again if it comes back into use.
3. As a SystemAdmin I see a firm's client money accounts in the Back Office, and on the tenant item which currencies have an active one.
4. As a TenantAdmin or Adviser I open a Custodial account only in a currency my firm has a client money account for. Any other currency → 400 with a message that says so. The New account form offers only those currencies for a Custodial account.
5. As an Adviser I see the deposit instructions of my Customer's Custodial account, so I can tell the Customer how to pay in.
6. As a Customer I see on my Custodial account where to pay and which reference to quote.
7. As a later deposit matching flow (B4) I find the Custodial account a receipt belongs to by its deposit reference, and a mistyped reference fails its check symbol instead of matching another account.

---

## 4. Rules

### 4.1 Client money accounts

| ID | Rule |
| --- | --- |
| R1 | **Access.** Every client money account route requires `tenants.manage` (SystemAdmin only). TenantAdmin, Adviser and Customer → 403. Missing / dead Bearer → 401, never 302. Path `{tenantId}` and `{id}` are PublicIds. Unknown tenant → 404. An `{id}` that is not a client money account of `{tenantId}` → 404, also when it belongs to another tenant. |
| R2 | **Fields.** A client money account has `currency`, `bankName`, `accountName`, `accountNumber` and `isActive`. `currency`: required, an **enabled** catalog code ([currencies.md](currencies.md)), stored upper-case. `bankName`: required, trimmed, 1–100 characters. `accountName` (the name the bank account is held in, for example "North Advisory Ltd Client Funds Trust Account"): required, trimmed, 1–140. `accountNumber`: required, trimmed, 1–34 characters of letters, digits, spaces and hyphens, with no two spaces in a row; it holds whatever the bank publishes (an NZ account number, a BSB and account number, an IBAN). Each field outside its rule → 400 `errors.<Field>`. |
| R3 | **Full account number.** The account number is stored and returned in full, not masked. Customers need the whole number to pay in, and it is not a Customer's personal data. |
| R4 | **One active per currency.** A Tenant has at most one active client money account per currency. Create in a currency that already has an active one → 400 `errors.Currency` with the message `The tenant already has an active client money account in USD.` (with the code). Enable when another active one exists in the same currency → 400 with the same message. Inactive rows in that currency do not count. |
| R5 | **Account number unique among active rows.** No two active client money accounts on the platform have the same `accountNumber` (compared without regard to case). Create or enable that would break it → 400 `errors.AccountNumber` with the message `This account number is already in use by an active client money account.` The message does not say which tenant has it. Inactive rows do not count, so a corrected row can reuse the number of the row it replaces. |
| R6 | **Create.** `POST /tenants/{tenantId}/client-money-accounts` with the R2 fields → 201 `{ "id" }`, `Location` on the item route. The new row is active. A disabled tenant → 400 `disabled` / `target=tenant` (api-design §4.1). The body must not carry `isActive`, `tenantId` or `id` (400). |
| R7 | **No edit.** There is no PUT. `currency`, `bankName`, `accountName` and `accountNumber` never change after create. A wrong detail is corrected by disabling the row and creating a new one. Deposit instructions a Customer has seen therefore always belong to one stored row. |
| R8 | **Disable and enable.** `POST …/{id}/disable` and `POST …/{id}/enable` with `rowVersion` → 204. Missing `rowVersion` → 400; stale → 409 `stale_row_version`. Already in the target state → 204, nothing written (idempotent like accounts R12). Enable checks R4 and R5. Disable is always allowed, also while Custodial accounts in that currency exist (R13). |
| R9 | **Read.** `GET /tenants/{tenantId}/client-money-accounts` → list envelope, sorted by currency, then active before inactive, then newest first. Optional `enabledOnly` (omitted / `false` = all, `true` = active only; any other value → 400). `page` and `pageSize` as everywhere. `GET …/{id}` → the item. |
| R10 | **No other effect.** Creating, disabling or enabling a client money account changes no Account, Transaction or deposit reference. It raises no domain event and writes no outbox row. A disabled Tenant keeps its client money accounts. |

### 4.2 Opening a Custodial account

| ID | Rule |
| --- | --- |
| R11 | **Needs a client money account.** `POST /accounts` with `custodyMode: "Custodial"` and a `currency` in which the account's Tenant has no active client money account → 400 `errors.Currency` with the message `The tenant has no active client money account in USD, so a Custodial account cannot be opened in USD.` (with the code). No account is written. An External account is not checked. |
| R12 | **Order.** R11 runs in the `CreateAccount` handler directly after the allowed custody modes check (tenant-custody-modes R7), so after every check that already runs before it (tenant, disabled tenant, Customer, disabled Customer, currency). A Tenant that does not allow Custodial accounts still gets the tenant-custody-modes message. The validator rules on `type` and `custodyMode` still run first. |
| R13 | **Only new accounts.** R11 reads the client money accounts only on `POST /accounts`. Disabling the last client money account of a currency changes no existing Custodial account in that currency: every route works as before, **reopen** included. Only their deposit instructions are unavailable (R21). The same pattern as tenant-custody-modes R9. |

### 4.3 Deposit reference

| ID | Rule |
| --- | --- |
| R14 | **Who has one.** Every Custodial account has a deposit reference. No External account has one. The account item carries `depositReference`: the reference on a Custodial account, `null` on an External one. Every caller who can read the account sees it, the Customer included. |
| R15 | **Format.** Nine characters from the alphabet `0123456789ABCDEFGHJKMNPQRSTVWXYZ` (32 symbols; no `I`, `L`, `O`, `U`). The first character is a letter. Characters 1–8 are random; character 9 is a check symbol over characters 1–8, Luhn mod 32 over that alphabet (§5). Upper case, no separators. Example: `K7M3Q9PXB`. |
| R16 | **Generated by the server.** The reference is drawn from a cryptographic random source when the account is opened, in the `CreateAccount` handler. A `POST /accounts` body that carries `depositReference` → 400 `errors.DepositReference`. A PUT body that carries it → 400, as for every field other than `name` (accounts R14). |
| R17 | **Unique on the platform.** No two Accounts have the same deposit reference, across all tenants, Open or Closed. Randomness alone does not guarantee this: with about a million Custodial accounts a draw that hits an existing reference becomes likely somewhere. Uniqueness is held by the unique index `UX_Accounts_DepositReference`, and the handler makes a collision invisible to the caller: (a) before saving it checks that the drawn reference is unused across tenants, and draws again if it is used; (b) when the save fails on that index (two opens drew the same unused reference at the same time), it builds the account again with a new reference and saves again. Each draw, in (a) or (b), counts towards **at most five draws** per request. Five used draws in a row → the request fails (500); at 7.6 × 10¹¹ possible references that does not happen. A save that fails on any other constraint is not retried. |
| R18 | **Never changes, never reused.** The reference is set once. Close, reopen and rename keep it. A Closed account keeps its reference, so no other account can get it. |
| R19 | **Existing Custodial accounts.** Script `0039` gives every existing Custodial account a reference by the R15 rules, unique as R17. |

### 4.4 Deposit instructions

| ID | Rule |
| --- | --- |
| R20 | **Route.** `GET /accounts/{id}/deposit-instructions` requires `accounts.read`, with the visibility of `GET /accounts/{id}`: TenantAdmin the tenant, Adviser assigned Customers, Customer own accounts, SystemAdmin any tenant. An account the caller cannot see → 404. |
| R21 | **Available or 404.** The route returns 200 when the account is Open, Custodial, and its Tenant has an active client money account in the account's currency. Otherwise 404: an External account, a Closed account, or no active client money account in that currency. The 404 carries no problem `code` (api-design §4.2 C1). |
| R22 | **Body.** `{ accountId, currency, reference, bankName, accountName, accountNumber }`: the account's PublicId, currency and deposit reference, and the three details of the active client money account in that currency. No client money account id, no tenant id. |
| R23 | **Tenant item.** The tenant item on `GET /tenants`, `GET /tenants/{id}` and `GET /tenants/by-code/{code}` gains `clientMoneyCurrencies`: the currency codes in which the Tenant has an active client money account, sorted A–Z, `[]` when none. It carries no bank details. TenantAdmin and Adviser read it on their own firm's item (tenants R1a), which is how the Adviser Portal knows which currencies a Custodial account can be opened in. |

Policies: none added. `tenants.manage` writes and lists client money accounts. `accounts.read` reads deposit instructions. `tenants.read` reads `clientMoneyCurrencies` on the own firm's item. Neither BFF's allow-list changes: the Adviser Portal BFF already proxies `/tenants` and `/accounts`, the Customer Portal BFF `/accounts`.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `ClientMoneyAccount` | Aggregate (new) | `TenantId`, `PublicId`, `Currency`, `BankName`, `AccountName`, `AccountNumber`, `IsActive`, `RowVersion`, audit. `Create`, `Disable`, `Enable`. No setter for the four details |
| `DepositReference` | Value object (new) | `Value` (nine characters). `Generate(IRandomSymbols)`, `TryParse(string, out DepositReference)`, `IsValid(string)`. Equality by value |
| `Account` | Aggregate (existing) | New `DepositReference` (nullable). `Create` gains an optional `DepositReference? depositReference` |
| `Tenant` | Aggregate (existing) | Unchanged. The one-active-per-currency rule is held by Application and an index, not by the Tenant (R4) |
| Events | | None added |

```text
ClientMoneyAccount.Create(tenantId, currency, bankName, accountName, accountNumber, publicId?)
  currency supplied by Application as an enabled catalog code
  bankName 1–100, accountName 1–140 after trim
  accountNumber 1–34 after trim, [A-Za-z0-9 -] only, no "  "
  IsActive = true

Disable()   already inactive → no-op, else IsActive = false
Enable()    already active → no-op, else IsActive = true      // R4 and R5 are checked by Application first

Account.Create(tenantId, customerId, name, type, custodyMode, currency,
               depositReference? = null, publicId? = null)
  Custodial and depositReference is null      → DomainException
  External and depositReference is not null   → DomainException
```

Deposit reference, check symbol (Luhn mod 32). Let `index(c)` be the position of `c` in `0123456789ABCDEFGHJKMNPQRSTVWXYZ`.

```text
check(s1..s8):
  factor = 2; sum = 0
  for c in s8, s7, …, s1:                 // right to left
    a = factor × index(c)
    sum += a div 32 + a mod 32
    factor = (factor == 2) ? 1 : 2
  return symbol at (32 − sum mod 32) mod 32

valid(s1..s9):                            // nine characters, first a letter, all in the alphabet
  same loop over s9 … s1 with factor starting at 1; valid when sum mod 32 = 0
```

A Luhn check catches every single wrong character and every swap of two neighbours, the two typing errors a payer makes most. `K7M3Q9PXB` is valid; `K7MAQ9PXB` and `K73MQ9PXB` are not.

Invariants:

- A Custodial account has a deposit reference and an External account has none. Held by `Account.Create` and `CK_Accounts_DepositReference`.
- A deposit reference is never changed. No method sets it after create.
- A deposit reference is unique on the platform. Held by Application (R17) and `UX_Accounts_DepositReference`.
- A Tenant has at most one active client money account per currency, and an account number is active on at most one row. Held by Application (R4, R5) and two filtered unique indexes. They are not invariants of one aggregate, so the Domain does not hold them.
- A Custodial account is opened only where an active client money account exists **at the time of open**. Held in Application (R11), like tenant-custody-modes R7. Not an invariant of existing accounts (R13).

Recorded in [domain-model.md](../domain-model.md) §8.5 and §8.8.

---

## 6. Database

Forward only. Do not add EF migrations.

### 6.1 Script `0038_client_money_accounts.sql`

| Table | Change | Index / FK |
| --- | --- | --- |
| `ClientMoneyAccounts` | Add | PK `Id`. Unique `PublicId`. FK `TenantId` → `Tenants` RESTRICT. FK `Currency` → `Currencies.Code` RESTRICT. Unique `UX_ClientMoneyAccounts_TenantId_Currency_Active` on `(TenantId, Currency)` WHERE `IsActive = 1`. Unique `UX_ClientMoneyAccounts_AccountNumber_Active` on `(AccountNumber)` WHERE `IsActive = 1`. Index `(TenantId)` |

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| Id | int identity | no | Internal PK |
| PublicId | uniqueidentifier | no | HTTP id |
| TenantId | int | no | FK Tenants |
| Currency | char(3) | no | FK Currencies. Immutable |
| BankName | nvarchar(100) | no | Immutable |
| AccountName | nvarchar(140) | no | Immutable |
| AccountNumber | nvarchar(34) | no | Immutable. Compared case-insensitively by the database collation |
| IsActive | bit | no | The only flag, as on catalog rows (glossary IsActive) |
| Created, CreatedBy, LastModified, LastModifiedBy | audit | | As every auditable table |
| RowVersion | rowversion | no | |

No `CHECK` on the account number's characters; the Domain and the validator hold R2.

### 6.2 Script `0039_accounts_deposit_reference.sql`

| Table | Change | Index / FK |
| --- | --- | --- |
| `Accounts` | Add `DepositReference char(9) NULL`. Back-fill (below). Add `CK_Accounts_DepositReference` CHECK (`([CustodyMode] = 1 AND [DepositReference] IS NOT NULL) OR ([CustodyMode] = 0 AND [DepositReference] IS NULL)`). Add unique `UX_Accounts_DepositReference` on `(DepositReference)` WHERE `DepositReference IS NOT NULL` | Unique filtered index; no FK |

Back-fill, in the same script, before the CHECK and the index:

- For each Custodial account (`CustodyMode = 1`), in `Id` order: draw eight symbols from `CRYPT_GEN_RANDOM`, the first from the 22 letters of the alphabet and the rest from all 32; add the R15 check symbol; draw again while the value is already used. Write it.
- External accounts stay `NULL`.
- The applicator runs a script without a surrounding transaction (as `0035` notes), so the script does the back-fill with a loop that only fills `NULL` rows. A run that stops half-way leaves a database the same script can finish.

The check symbol is computed in T-SQL and in C#. The integration test (§10) proves they agree: every back-filled value passes `DepositReference.IsValid`.

Existing data:

- Development: the `Demo custodial brokerage` account of each TestSeed firm gets a reference from `0039`, and TestSeed adds the client money accounts (§9.1).
- VPS showcase: every existing Custodial account gets a reference. No Tenant has a client money account after the scripts, so **no new Custodial account can be opened** until SystemAdmin adds one in the Back Office. Existing Custodial accounts keep working (R13). The deployment notes of this slice say so.
- Functional tests create their own data (§10 fan-out).

Recorded in [database-design.md](../database-design.md) §9, §11.3 and §11.8.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Command | `CreateClientMoneyAccount` (new) | `tenants.manage`. Validator: R2, R6 body fields. Handler: tenant by PublicId (404), disabled tenant (§4.1), enabled currency, R4, R5, `ClientMoneyAccount.Create`, save |
| Command | `DisableClientMoneyAccount`, `EnableClientMoneyAccount` (new) | `tenants.manage`. Row by (tenant PublicId, row PublicId), else 404. `rowVersion`. Enable: R4, R5 |
| Query | `GetClientMoneyAccounts`, `GetClientMoneyAccountById` (new) | `tenants.manage`. R9 |
| Command | `CreateAccount` | Validator: `depositReference` must be absent (R16). Handler: after the allowed custody modes check, R11 for a Custodial account; then draw the reference through `IDepositReferenceSource` and check it is unused across tenants (R17 a); pass it to `Account.Create`; on `DepositReferenceTakenException` from the save, detach the account, draw again and save a new `Account` with the same inputs (R17 b) |
| Command | `UpdateAccount` | `depositReference` in the body → 400 (R16) |
| Command | `CloseAccount`, `ReopenAccount` | Unchanged (R13, R18) |
| Query | `GetAccounts`, `GetAccountById` | `AccountDto` gains `DepositReference` (R14) |
| Query | `GetDepositInstructions` (new) | `accounts.read` and the account visibility of `GetAccountById`; R21, R22 |
| Query | `GetTenants`, `GetTenantById`, `GetTenantByCode` | `TenantDto` gains `ClientMoneyCurrencies` (R23) |
| Port | `IDepositReferenceSource` (new, Application) | `DepositReference Next()`. Infrastructure implementation draws from `RandomNumberGenerator`. Functional tests may replace it to prove the redraws of R17 |
| Exception | `DepositReferenceTakenException` (new, Application) | Raised by `ApplicationDbContext.SaveChangesAsync` when SQL Server reports a unique violation (2601 / 2627) on `UX_Accounts_DepositReference`, and only then. Application does not see `SqlException` |

Where each rule lives:

| Rule | Where |
| --- | --- |
| Field shapes (R2, R16) | Validators. No database read |
| One active per currency, account number unique (R4, R5) | Handlers, with the two filtered indexes as backstop |
| Client money account on open (R11) | `CreateAccountCommandHandler`, next to the allowed custody modes check |
| Reference present iff Custodial | `Account.Create` and the CHECK |
| Reference unique (R17) | The unique index; `CreateAccountCommandHandler` checks before the save (query that ignores the tenant filter) and draws again after a save that failed on the index |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/tenants/{tenantId}/client-money-accounts` | `tenants.manage` | 200 envelope | 400 / 401 / 403 / 404 |
| GET | `/tenants/{tenantId}/client-money-accounts/{id}` | `tenants.manage` | 200 item | 401 / 403 / 404 |
| POST | `/tenants/{tenantId}/client-money-accounts` | `tenants.manage` | 201 `{ "id" }` | 400 / 401 / 403 / 404 |
| POST | `/tenants/{tenantId}/client-money-accounts/{id}/disable` | `tenants.manage` | 204 | 400 / 401 / 403 / 404 / 409 |
| POST | `/tenants/{tenantId}/client-money-accounts/{id}/enable` | `tenants.manage` | 204 | 400 / 401 / 403 / 404 / 409 |
| GET | `/accounts/{id}/deposit-instructions` | `accounts.read` | 200 | 401 / 403 / 404 |
| POST | `/accounts` | `accounts.create` (unchanged) | — | adds 400 `errors.Currency` (R11) and `errors.DepositReference` (R16) |
| GET | `/accounts`, `/accounts/{id}` | `accounts.read` (unchanged) | item has `depositReference` | — |
| GET | `/tenants`, `/tenants/{id}`, `/tenants/by-code/{code}` | unchanged | item has `clientMoneyCurrencies` | — |

### 8.1 Bodies

Create a client money account:

```json
{
  "currency": "USD",
  "bankName": "Simulator Bank",
  "accountName": "North Advisory Ltd Client Funds Trust Account",
  "accountNumber": "02-0100-0123456-00"
}
```

Client money account item:

```json
{
  "id": "<publicId>",
  "tenantId": "<tenant publicId>",
  "currency": "USD",
  "bankName": "Simulator Bank",
  "accountName": "North Advisory Ltd Client Funds Trust Account",
  "accountNumber": "02-0100-0123456-00",
  "isActive": true,
  "rowVersion": "<base64>",
  "created": "2026-11-19T03:00:00Z"
}
```

Disable / enable body: `{ "rowVersion": "<base64>" }`.

Account item (other fields as in [accounts.md](accounts.md) §8.1 and its amendments):

```json
{
  "id": "<account publicId>",
  "name": "Managed portfolio",
  "type": "Brokerage",
  "custodyMode": "Custodial",
  "currency": "USD",
  "depositReference": "K7M3Q9PXB",
  "status": "Open",
  "...": "..."
}
```

An External account: `"depositReference": null`.

Deposit instructions:

```json
{
  "accountId": "<account publicId>",
  "currency": "USD",
  "reference": "K7M3Q9PXB",
  "bankName": "Simulator Bank",
  "accountName": "North Advisory Ltd Client Funds Trust Account",
  "accountNumber": "02-0100-0123456-00"
}
```

Tenant item (other fields as in [tenant-custody-modes.md](tenant-custody-modes.md) §8.1):

```json
{
  "id": "<publicId>",
  "code": "north-advisory",
  "allowedCustodyModes": ["External", "Custodial"],
  "clientMoneyCurrencies": ["NZD", "USD"],
  "...": "..."
}
```

### 8.2 Errors

All refusals are ordinary validation (400 with `errors`, no problem `code`) except where named.

| Case | Response |
| --- | --- |
| Caller without `tenants.manage` on a client money account route | 403 |
| Unknown tenant; a row id that is not under `{tenantId}` | 404 |
| A field outside R2; `isActive`, `tenantId` or `id` in the create body | 400, `errors.<Field>` |
| Create or enable with another active row in the currency | 400 `errors.Currency` (R4) |
| Create or enable with the account number active on another row | 400 `errors.AccountNumber` (R5) |
| Create on a disabled tenant | 400 `disabled` / `target=tenant` |
| Disable / enable: missing `rowVersion` / stale | 400 / 409 `stale_row_version` |
| `POST /accounts` Custodial with no active client money account in the currency | 400 `errors.Currency` (R11) |
| `POST /accounts` or `PUT /accounts/{id}` with `depositReference` | 400 `errors.DepositReference` |
| Deposit instructions of an External, Closed or uncovered account, or of an account the caller cannot see | 404 |

Cross-tenant or wrong-collection id → **404**, not 403. Keep the resource catalog in [api-design.md](../api-design.md) §7.3 and §7.6 pointing here for these rules.

---

## 9. UI

Page contracts: [back-office.md](../backoffice/back-office.md) (BO16, BO17), [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (D32, D33) and [customer-portal.md](../customer-portal/customer-portal.md) (C19, C20), amended and accepted together with this spec. No BFF changes.

**Back Office**

| ID | Behaviour |
| --- | --- |
| BO16 | `/tenants/{id}/config` gains a "Client money accounts" section under the report grants: a table from `GET /tenants/{tenantId}/client-money-accounts` with Currency, Bank, Account name, Account number and Active, and Disable / Enable per row (sending its `rowVersion`). An "Add client money account" form: currency picker over enabled currencies (`GET /currencies?enabledOnly=true`), bank name, account name, account number. A 400 is shown at its field. After a write the table reloads. |
| BO17 | No edit action. Under the table: "Details cannot be changed. To correct them, disable the account and add a new one. Disabling the last account in a currency stops new Custodial accounts in that currency." |

**Adviser Portal**

| ID | Behaviour |
| --- | --- |
| D32 | `/accounts/:id` of a Custodial account shows "Deposit reference" with the item's `depositReference`, Open or Closed. On an Open Custodial account it also shows a "Deposit instructions" panel from `GET /accounts/{id}/deposit-instructions`: Bank, Account name, Account number, Reference, Currency. A 404 shows "No client money account in <currency>. Ask the platform operator to add one." External accounts show neither. |
| D33 | The New account form (`/customers/:id/accounts/new`, D17 and D21): while Custodial is chosen, the currency picker offers only the enabled currencies in the firm's `clientMoneyCurrencies` from the tenant item. When that list is empty, the form shows "Your firm has no client money account, so a Custodial account cannot be opened." and cannot be submitted with Custodial. A 400 with `errors.Currency` (the setting changed after the page loaded) is shown at the currency picker. External keeps every enabled currency. |

**Customer Portal**

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C19 | Customer with an Open Custodial account whose currency has a client money account | Account page | A "How to pay in" section under the cash balance: Bank, Account name, Account number, Reference, and the sentence "Pay in <currency> only, and quote the reference exactly so the payment is matched to this account." |
| C20 | Customer with a Closed Custodial account, or an Open one whose deposit instructions return 404 | Account page | A Closed account shows no section. An Open one shows "Deposit instructions are not available yet. Contact your adviser." An External account shows no section and calls no route. |

### 9.1 Seeds

- `TestSeed` (Development and TestAppHost): on each firm it seeds (`demo`, `demo2`), ensure an active client money account in USD and one in NZD, idempotent on (tenant, currency, active): bank name `Simulator Bank`, account name `<tenant name> Client Funds Trust Account`, account number `SIM-<TENANT CODE>-<CURRENCY>` (for example `SIM-DEMO-USD`). The `Demo custodial brokerage` (USD) then has deposit instructions.
- `TestSeed` opens `Demo custodial brokerage` through `Account.Create` with a reference from `DepositReference.Generate`. On a database that existed before this slice, `0039` gave it one and the seed does not change it.
- `DevelopmentSmokeSeeder`: no change. The smoke account is External.
- Demo seed ([demo-tenant-seed.md](demo-tenant-seed.md)): no change. On the VPS SystemAdmin adds the demo tenant's client money accounts in the Back Office (§11 point 10).

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `ClientMoneyAccount.Create`: active; each field at its limits; a blank, too long or bad-character field → `DomainException`; two spaces in a row → throws. `Disable` / `Enable` idempotent. `DepositReference`: `Generate` with scripted symbols gives the expected value; 1 000 generated values are valid, first character a letter; `IsValid` true for `K7M3Q9PXB`, false for one changed character, for two neighbours swapped, for 8 or 10 characters, for `I`, `L`, `O`, `U`, lower case, and a first digit. `Account.Create`: Custodial without a reference → throws; External with one → throws; the reference is kept on close and reopen |
| Application.FunctionalTests — client money accounts | No Bearer → 401 not 302; TenantAdmin, Adviser, Customer → 403 on every route. SystemAdmin: create → 201 and get returns the fields; list sorted per R9, `enabledOnly`; each R2 400 with its field; `isActive` in the body → 400; second active in the currency → 400 `errors.Currency`; second after the first is disabled → 201; enable the first again → 400 `errors.Currency`; the same account number active in another tenant → 400 `errors.AccountNumber` without the tenant name in the message; reuse after disable → 201; disable / enable idempotent; stale `rowVersion` → 409 `stale_row_version`; disabled tenant → 400 `disabled` / `target=tenant`. Two-tenant: tenant B's row id under tenant A's path → 404 on get, disable and enable. Tenant item: `clientMoneyCurrencies` sorted, `[]` without rows, a disabled row not listed; TenantAdmin and Adviser see it on by-code |
| Application.FunctionalTests — open | Tenant with an active USD client money account: TenantAdmin and Adviser open Custodial USD → 201 with a valid `depositReference`; Custodial NZD → 400 `errors.Currency` with the R11 message, no account row; External NZD → 201 with `depositReference: null`. Tenant that does not allow Custodial and has no client money account → the tenant-custody-modes message (R12 order). SystemAdmin with `tenantId` follows the target tenant's rows. `depositReference` in the POST or PUT body → 400. Disable the USD row: the existing Custodial account takes a `TransferIn`, closes and **reopens**; a new Custodial USD open → 400. Redraw before save (R17 a): a replaced `IDepositReferenceSource` that returns an existing reference first, then a new one → 201 with the second. Redraw after save (R17 b): a test save interceptor inserts another account with the drawn reference just before the first save of the open → 201 with the next drawn reference, one account written by the request, no 500. Five used draws → 500 and no account row. A save that fails on another constraint is not retried (the source is called once). Two accounts opened in two tenants get different references. Two-tenant: an Adviser of tenant A naming tenant B's Customer → 404, not 400 |
| Application.FunctionalTests — deposit instructions | Open Custodial USD with an active USD row → 200 with the R22 body, for TenantAdmin, the assigned Adviser, the Customer (own) and SystemAdmin. External → 404; Closed → 404; the USD row disabled → 404; then a new USD row → 200 with its details. Unassigned Adviser, another Customer, another tenant's TenantAdmin → 404. No Bearer → 401 |
| Infrastructure.IntegrationTests | `0038` on a scratch database: a second active row in one (tenant, currency) violates `UX_ClientMoneyAccounts_TenantId_Currency_Active`; a second inactive one inserts; the same account number active twice violates the account number index, once active and once inactive inserts. `0039` on a scratch database with External and Custodial accounts: every Custodial row gets a value that passes `DepositReference.IsValid`, all distinct; External rows stay `NULL`; a Custodial insert without a reference and an External insert with one violate `CK_Accounts_DepositReference`; a duplicate reference violates `UX_Accounts_DepositReference`. EF round-trip of both. `TestSeed` twice: one USD and one NZD row per firm |
| Back Office (`BackOfficePageTests`) | BO16: the table from the list; the add form posts the four fields; a 400 is shown at its field; Disable / Enable send `rowVersion`. BO17: no edit control, the sentence is shown |
| Adviser Portal | D32: reference shown on a Custodial account, Open and Closed; panel from the route on an Open one; the 404 sentence; nothing on External. D33: with Custodial, only `clientMoneyCurrencies`; empty list → sentence and no submit; 400 `errors.Currency` at the picker; External offers every enabled currency |
| Customer Portal | C19, C20 |

Fan-out in the tests:

- Every functional test that opens a Custodial account needs an active client money account in its currency. `AccountHttp.OpenAccountAsync` (and through it `CustodialHttp.OpenCustodialAsync`, about 20 files) ensures one, with `custodyMode: "Custodial"`, for the Customer's tenant in the database before it posts. The tests that post a Custodial body themselves (`AccountCustodyModeCreateTests`, `TenantCustodyModesNarrowingTests`, `TenantCustodyModesOpenTests`) call the same helper in their arrange step. The new R11 tests post without it.
- The callers of `Account.Create` with `CustodyMode.Custodial` (`TestSeed`, `AccountTests`, `RebalanceJobWorkerTests`, `RebalanceJobTenantScopeTests`) pass `DepositReference.Generate(...)`.
- Raw-SQL `INSERT INTO [Accounts]` with `CustodyMode = 1` on a database at `0039` or later adds a valid `DepositReference` (`CustodyModeSchemaTests`, `RemoveExternalPortfolioAssignmentsSchemaTests`, `TenantCompositeKeysSchemaTests`, `ScratchSchemaDatabase` where they insert Custodial rows). Inserts on a scratch database stopped before `0039` do not change.
- `TenantDto` and the Adviser Portal `Tenant` type gain `clientMoneyCurrencies`; the Adviser Portal `Account` type gains `depositReference`; the Customer Portal `Account` type too.
- The Back Office `IPlatformApi` gains the five client money account calls; the fake in `BackOfficePageTests` follows.

---

## 11. Locked in this spec

No new ADR. Roadmap B1, B2 and decisions D5 and D19 gave the direction; [ADR 0021](../adr/0021-outside-parties-behind-ports.md) rule 3 names the client money account as the place a bank connection is chosen later. [ADR 0020](../adr/0020-account-custody-mode.md) item 1 (a Tenant gets at most one custody setting) is not touched: a client money account is the firm's bank details, not a custody mode setting, and R11 is a check on open in the same way as that one setting.

| Item | Lock |
| --- | --- |
| Client money account | Per Tenant and currency; bank name, account name, full account number; immutable; disable / enable only; at most one active per currency; account number unique among active rows |
| Writer | SystemAdmin (`tenants.manage`); Back Office tenant configuration page |
| Open rule | `POST /accounts` Custodial needs an active client money account in the currency; nothing else reads it; reopen not checked |
| Deposit reference | Nine characters, Crockford-style alphabet, first a letter, eight random, Luhn mod 32 check symbol; server-generated; unique on the platform by a unique index, with a redraw before the save and after a save that hit the index (at most five draws); never changed or reused; Custodial accounts only |
| Deposit instructions | `GET /accounts/{id}/deposit-instructions`, `accounts.read`, 200 or 404 |
| Tenant item | `clientMoneyCurrencies` |
| Scripts | `0038_client_money_accounts.sql`, `0039_accounts_deposit_reference.sql` (with the back-fill) |
| Policies, problem codes, events | None added |

### Confirmed when this spec was accepted (2026-11-20)

Each point was confirmed as recommended and is written into the rules above as stated here. The alternatives are kept for the record.

1. **SystemAdmin sets the client money accounts** (R1). Decided: the platform operator, in the Back Office, like the allowed custody modes. The account number is where every Customer's money goes, so a change to it is the classic payment redirection fraud; and the bank connection that B3 adds is platform configuration (ADR 0021 rules 3 and 17), which only the operator controls. Alternative: TenantAdmin in the Adviser Portal, as the roadmap's B1 also offered.
2. **Full account number, not masked** (R3). Decided: full. The roadmap said "masked account number", but the Customer needs the full number to pay in, and the trust account number is the firm's, not personal data. Masking stays the rule for the Customer's nominated bank account (B5). Alternative: store it masked and keep the full number elsewhere, which leaves deposit instructions without a number to show.
3. **Immutable details, no PUT** (R7). Decided: a correction is disable plus a new row, so every instruction a Customer saw maps to one stored row, and B7 can say which account a statement belongs to without history. Alternative: a PUT for `bankName` and `accountName` only, or for all fields with an audit trail.
4. **One active client money account per currency** (R4). Decided: yes. Deposit instructions then name exactly one account, and B4 matches a receipt to one Tenant. Alternative: several active per currency, with one marked as the one to show.
5. **Reference format and uniqueness** (R15, R17). Decided: nine characters with a check symbol, first a letter, random, unique on the platform. Nine fits the 12-character reference field of NZ banks; the check symbol turns most typos into an unmatched receipt instead of a deposit on someone else's account; a random code reveals nothing about how many accounts exist and makes a typo very unlikely to hit another real reference; a first letter stops a spreadsheet or bank export from reading it as a number. The roadmap said "unique in the Tenant"; platform-wide costs nothing and lets an operator find an account from a reference alone. Alternative: a sequential number per Tenant, or no check symbol.
6. **Reference only on Custodial accounts** (R14). Decided: yes; External accounts are funded at banks the platform does not hold. Alternative: give every account one, for later use.
7. **Open rule only on create; reopen not checked** (R11, R13). Decided: the same as tenant-custody-modes R9: the rule blocks a new reference and leaves existing ones working. Alternative: treat reopen as an open and refuse it without a client money account.
8. **Deposit instructions as their own route, 404 when unavailable** (R20, R21). Decided: a separate read that joins the account with the client money account, like `GET /accounts/{id}/portfolio` returns 404 without an assignment, so the account item does not carry bank details on every list row. Alternative: a `depositInstructions` object on the account item, `null` when unavailable.
9. **No bank connection yet** (§2 Out). Decided: B3 adds how a client money account names its connection (ADR 0021 rule 3), with a back-fill that points every existing row at the simulator bank (D19). Today there is no reader, and the shape (a configured connection name, a party account id issued by the bank) belongs to the bank feed spec. Alternative: add a connection column now.
10. **No client money account in the demo seed** (§9.1). Decided: the VPS operator adds them in the Back Office, because their account numbers must match the simulator bank's accounts once S1 exists, which this slice cannot know. Until then no new Custodial account opens on the showcase. Alternative: the demo seed adds a USD row with a placeholder number.
11. **`clientMoneyCurrencies` on the tenant item** (R23). Decided: so the Adviser Portal offers only the currencies that will be accepted, as D21 does for custody modes, without giving TenantAdmin or Adviser the bank details list. Alternative: no field; the form shows the 400.

Still open, not this slice:

- The bank connection of a client money account, and whether an inactive one is still read by the feed (roadmap B3)
- How a reference is found in a statement line's text, and how a mistyped one is normalised (`O` → `0`, `I` and `L` → `1`) before the check (roadmap B4)
- Search by deposit reference for the operator's manual match (roadmap B4)
- What the simulator bank does with the platform's client money accounts across a simulator reset (roadmap S1)
- Interest paid by the bank on the trust account (roadmap B7, I2)

---

## 12. Suggested commits

The repository should build after each commit. Agents split each behaviour into red then green.

1. Store client money accounts (R2, R10): `ClientMoneyAccount`, script `0038`, EF mapping + Domain unit tests and the `0038` integration tests.
2. The client money account routes (R1, R4–R9) + functional tests, including two-tenant.
3. `clientMoneyCurrencies` on the tenant item (R23) + functional tests.
4. `POST /accounts` needs a client money account for a Custodial account (R11–R13) + functional tests. The test helpers of §10 fan-out change here.
5. Store the deposit reference (R14, R15, R18, R19): `DepositReference`, `Account.Create` takes it, script `0039` with the back-fill, EF mapping, `TestSeed` + Domain unit tests and the `0039` integration tests. The `Account.Create` callers and raw inserts of §10 fan-out change here.
6. The server draws the reference on open (R16, R17 a and b, `DepositReferenceTakenException`) and the account item carries it + functional tests.
7. `GET /accounts/{id}/deposit-instructions` (R20–R22) + functional tests.
8. `TestSeed` client money accounts (§9.1) + the seed integration test.
9. Back Office: client money accounts on the configuration page (BO16, BO17) + page tests.
10. Adviser Portal (D32, D33) and Customer Portal (C19, C20) + frontend tests.
11. Repo docs: mark this file landed; roadmap §13 step 4; the deployment note that the showcase needs client money accounts before a new Custodial account opens.

Do not put a bank feed, a connection, matching, suspense, nominated bank accounts or withdrawals in these commits.

### Docs amended when this spec was accepted

[tenants.md](tenants.md) (amendment: `clientMoneyCurrencies` on the item; the client money account routes), [accounts.md](accounts.md) (amendment: R8 open rule, `depositReference` on the item, refused on POST and PUT, the deposit instructions route), [custody-modes.md](custody-modes.md) (a Custodial account opens only in a covered currency), [demo-tenant-seed.md](demo-tenant-seed.md) (no client money accounts), [api-design.md](../api-design.md) (§7.3 and §7.6 rows, change log), [domain-model.md](../domain-model.md) §8.5 and the new aggregate, [database-design.md](../database-design.md) §9, §11.3 and the new table, [back-office.md](../backoffice/back-office.md) (BO16, BO17), [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (D32, D33), [customer-portal.md](../customer-portal/customer-portal.md) (C19, C20), [function-plan.md](../function-plan.md) §5 item 9, [features/README.md](README.md) row status, the roadmap §5 B1 / B2 and §13 status.
