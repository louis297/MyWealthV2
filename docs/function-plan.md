---
title: Function plan
status: draft
language: en
created: 2026-09-05
updated: 2026-09-20
related:
  - README.md
  - glossary.md
  - architecture.md
  - adr/0014-openiddict-authorization-code-pkce.md
---

# Function plan

**Product:** MyWealthV2  
**Status:** draft  
**Updated:** 2026-09-20

This document owns **what / who / when**. How for the current phase lives in architecture, domain model, database design, API design, and Feature Specs.

**Phasing rule:** if a later phase will use it and today’s design would have to change, ship the final infrastructure now. If later use or shape is not decided, wait. Phase 1 is the platform base that should still be there later. The ledger is one Phase-2 domain, sliced internally. From Phase 3 onward, one domain at a time.

How this file relates to the rest of the tree is in [README.md](README.md). This file shows every phase. Phase 1 is written in enough detail to schedule work. Later phases list only what has already been discussed. Do not invent the rest.

---

## 0. Locked (do not reopen)

- Stack: Aspire + Clean Architecture + React / Redux / TypeScript + .NET 10 + EF Core + SQL Server.
- Schema: versioned SQL scripts are the source of truth. Do not use `EnsureDeleted` as the normal boot path.
- Tenancy: shared database + dual check + isolation tests. Login carries **tenantCode** (no subdomain). Email is unique inside a tenant.
- Identity: ASP.NET Identity stores users and password hashes. **OpenIddict** lives in Aspire resource `identity` (`src/IdentityHost`). Authorization code + PKCE + revocable refresh. No custom `/auth/login` token issuer. Invitation and forgot-password are not in Phase 1. Keep `UserStatus`, `UserTokens`, and the email port. Phase 1 uses one database (`MyWealthDbV2`); not a separate identity SQL database.
- Customer: may obtain tokens through the authorization server. No Customer Portal client in Phase 1.
- Roles: four. One Domain `Users` table and one `ApplicationUser` table hold all four. Named endpoint policies, code-mapped role → permission, handler scope checks (ADR 0013).
- Identity link: only `Users.IdentityUserId` → `AspNetUsers`. No reverse database FK.
- Keys: internal `int` identity, external `PublicId` UUID.
- Currency: platform `Currencies` table, not an enum. Ships in Phase 1.
- Instrument catalog, accounts, holdings, transactions, and net worth are the **Phase-2 ledger domain**. Phase 1 does not create those tables or expose those APIs.

---

## 1. Purpose by phase

- **Phase 1:** platform foundation. The vertical cut stops at “people in a firm + currency catalog”. Session uses the same OIDC shape a second portal will reuse.
- **Phase 2:** ledger domain (instrument catalog first, then account container, cash ledger, securities / holdings, posting and reversal, read models). One domain, several slices. Model it as real sub-ledgers. Do not ship a throwaway single-row `Transactions` design that pretends to be both cash and securities.
- **Phase 3+:** one domain at a time (Customer Portal as a second OIDC client, KYC, household groups, advice documents, and so on).

---

## 2. Roles

| Role | Can log in | Phase 1 UI | Notes |
| --- | --- | --- | --- |
| SystemAdmin | Yes, no tenantCode | None (Scalar / API). `adviser-portal` allow-list yes (probe). | Platform-level. No TenantId. Tenant manage is not the Adviser Portal. |
| TenantAdmin | Yes + tenantCode | Adviser Portal | Highest authority inside the tenant. |
| Adviser | Yes + tenantCode | Adviser Portal | Manages assigned Customers. No ledger yet in Phase 1. |
| Customer | Yes + tenantCode | None | Login principal. `adviser-portal` issues no code. No Customer Portal client yet. |

---

## 3. Phase map

```text
Phase 1  Platform foundation
         schema / currency catalog → OpenIddict + hosted login + policies
         → tenants → TenantAdmin → Adviser → Customer
         Portal: OIDC shell + callback first; Profile / Customers / Advisers after people APIs

Phase 2  Ledger domain (one domain, several slices)
         Instruments first, then account container → cash ledger
         → securities / holdings → posting and reversal → net-worth read model
         Tendencies in §5. Table shape is locked when that phase opens.
         Session stack does not change.

Phase 3+ Other domains, one at a time
         Customer Portal = second public OIDC client on the same authorization server
         Invitation delivery, KYC, households, advice documents, custody, tax, …
```

---

## 4. Phase 1 (detailed)

### 4.1 Platform and data lifecycle

| Capability | Notes | UI | Who |
| --- | --- | --- | --- |
| Versioned SQL schema | `database/schema/`. identity-auth: Identity, OpenIddict, Tenants (no ReportingCurrency), Users, UserTokens. Currencies + ReportingCurrency in the currencies slice. | None | Engineering |
| Apply on startup | `SchemaVersions`. Default path does not drop the database. | None | Engineering |
| Explicit local reset | drop → scripts → seed (Identity through `UserManager`; OpenIddict clients in SQL or startup seed) | None | Development |
| Tenant isolation tests | Cross-tenant read/write must fail. From Tenants onward, every business slice. CI gate. | None | Engineering |
| Concurrency | `RowVersion` on Tenant and User. | None | Built-in |

Phase 1 scripts **do not** include Instruments, Accounts, Holdings, or Transactions. There is no custom `RefreshTokens` table; refresh lives in the OpenIddict token store.

### 4.2 Identity and session

| Capability | Notes | UI | Who |
| --- | --- | --- | --- |
| Authorization server | OpenIddict in `identity`. Library default paths, do not remap: discovery, JWKS, `/connect/authorize`, `/connect/token`, `/connect/revocation`, `/connect/logout`. | Hosted login page on `identity` | Engineering |
| OIDC client | One public client: `adviser-portal`. Authorization code + PKCE + refresh. | Portal redirect / callback | Adviser Portal |
| Login | Hosted page: email + password + tenantCode (SystemAdmin omits tenantCode). | Hosted login | Login-capable roles |
| Logout | End session + revoke refresh. | Global | Authenticated |
| Refresh | OpenIddict token endpoint. Invalidated on password change, disable, logout. | None | Authenticated |
| `/users/me` | Resource API under the `/users` namespace. Read/update non-password profile. Password change requires the current password. | Profile page | Authenticated |
| UserStatus | `PendingActivation` / `Active` / `Disabled`. Non-Active cannot complete login. | None | Built-in |
| Activation seams | `UserTokens` + no-op `IEmailSender` | None | Reserved |
| Customer tokens | Login principal remains. `adviser-portal` issues no code. Tokens wait for `customer-portal`. Adviser-management routes stay 403. | None | Customer |

Scopes in Phase 1: `openid`, `profile`, `offline_access`, `api`. Roles and tenant claims are JWT claims, not scopes. Permissions are not expanded into the token (ADR 0013, ADR 0014).

**Out:** invitation, forgot password, MFA, external IdP / SSO, self-registration, password grant, a second OIDC client, a separate identity SQL database, custom `/auth/login` as the token issuer.

### 4.3 Tenants and people

| Capability | Notes | UI | Who |
| --- | --- | --- | --- |
| Tenant CRUD / enable-disable | `/tenants`. Name, Code, ReportingCurrency, IsEnabled | API / Scalar | SystemAdmin |
| TenantAdmin CRUD / disable | `/users/tenant-admins`. May set a password and land in Active. Email unique inside the tenant. | API / Scalar | SystemAdmin |
| Adviser CRUD / disable | `/users/advisers`. Same password path. Assigned Customers must be handled before disable. | Adviser list | TenantAdmin |
| Customer CRUD / disable | `/users/customers`. `AdviserId` required. An Adviser caller may only assign self. Create also creates Identity. | Customer list / detail | TenantAdmin, Adviser (assigned) |

Disabling the last TenantAdmin is allowed in Phase 1 (known gap). After a tenant is disabled, login with that Code fails. Password change, disable, and logout revoke OpenIddict tokens for that subject.

### 4.4 Currency catalog

| Capability | Notes | UI | Who |
| --- | --- | --- | --- |
| Platform currency list | Seed NZD, AUD, USD, EUR, GBP, JPY. `GET /currencies?enabledOnly=` omitted/`false` = all; `true` = enabled only. Item includes `isEnabled`. | None | Authenticated, read-only |
| References | Phase 1 consumer: `Tenant.ReportingCurrency` | None | Built-in |
| In-memory catalog | `ICurrencyCatalog`. Hot path does not JOIN. | None | Built-in |

**Out:** per-tenant allow-lists, FX, currency write APIs.

### 4.5 Phase 1 frontend

Adviser Portal only. Split in two implementation slices (see [portals/adviser-portal.md](portals/adviser-portal.md)):

1. **Shell + callback (landed in repo 2026-09-13):** Vite on Aspire `adviser-portal`, authorize redirect, `/callback`, session probe via `GET /users/me`, 401 refresh-once. No password form. SystemAdmin may use the probe (Development seed). OpenIddict redirect URIs upsert from the portal origin, including the Aspire dashboard alias.
2. **Pages (cut C, accepted, landed 2026-09-14):** role-filtered shell, Profile, Customers, Advisers. Default home = Customers. IdentityHost refuses Customer on this client. SystemAdmin uses Scalar for tenant manage. Shell name from `GET /tenants/by-code/{code}`. Construction notes (`review`): [portals/frontend-implementation-notes.md](portals/frontend-implementation-notes.md).

**Out:** Accounts, Instruments, Transactions, Dashboard. No Customer Portal client in Phase 1.

---

## 5. Phase 2 — ledger domain (feature map; not a table contract)

One domain. Feature Specs come after this map. Suggested internal slice order (not a locked table design):

1. Instruments (tenant catalog; Adviser may create and read; update/disable is TenantAdmin or SystemAdmin)
2. Account container
3. Cash sub-ledger
4. Holdings / securities ledger
5. Posting, reversal, and Opening
6. Net-worth read model + Adviser Portal Dashboard

Session, OpenIddict, and the Adviser Portal client do not change in this phase. Add ledger policy names only.

### 5.1 In scope (agreed 2026-09-17)

- Tenant instrument catalog + mocked `IMarketData` / `IFxRate`.
- Account container under a Customer.
- **Phase 2 may open:** Bank, Cash, Brokerage, Other.
- **Reserved names, not selectable in Phase 2:** Property, Credit (personal loans, cards, real property). Keep the names and the capability matrix so a later phase can add them without reshaping Account.
- Cash sub-ledger. Holdings. Posting with cash and/or security legs. Full reversal. Opening (initial cash and initial holdings).
- Net worth as a per-currency array (no FX fold). Adviser Portal pages: Dashboard, Accounts, holdings, activity/postings, Instruments.

**Account type is a capability gate, not a separate product module.** Do not use Other as a stand-in for Property or Credit.

| Type | Phase 2 | Means | Cash book | Holdings (any Instrument) | Net worth |
| --- | --- | --- | --- | --- | --- |
| Bank | Open | Deposit-style cash at a bank (current / savings and similar). Not “the bank as an institution”. | Yes | Forbidden | Asset = cash |
| Cash | Open | Physical or not-yet-banked cash | Yes | Forbidden | Asset = cash |
| Brokerage | Open | Equities, funds, ETFs, and similar | Yes | Allowed | Asset = cash + holding market value |
| Other | Open | Catch-all. Not a substitute for Property or Credit. | Yes | Allowed | Asset = cash + holding market value |
| Property | Reserved | Real property held as instruments in this account | Yes | Allowed | Asset = cash + holding market value |
| Credit | Reserved | Cards and loans | Yes; may be negative | Forbidden | Liability = cash |

- “No holdings” means **no Holding of any Instrument** — not only listed equities.
- `Account.Type` is immutable after open.
- `Account.Currency` remains the cash-book currency and is immutable after open.
- Non-cash value lives only on holdings. Do not add a hand-edited account-level `CurrentValue`.
- Phase 2 has no Credit accounts, so posted cash must not go negative. The Credit exception stays written for the reserved type; do not invent a second cash rule later.

### 5.2 Locked way of thinking (still not columns)

- Do not ship one `Type` row that pretends to be both cash and securities. Think cash legs and security legs.
- Booked rows are not `UPDATE`/`DELETE`. Correction tends to be a full reversal posting.
- After a posting, cash must not go negative on non-Credit accounts. Credit may be negative when that type is opened in a later phase. Phase 2 selectable types are all non-Credit.
- Day-to-day holding quantity and cost are not edited by hand. Opening is the only direct write of quantity and cost.
- Splits / scrip issues need scrip-only postings (cash 0, total cost unchanged). Do not fake them with manual holding edits. Not built until that slice exists.
- Same-currency FX is 1. Cross-currency is never treated as 1.
- Customer has no ledger-write policy. Disable Customer rejects while an account is still open.

### 5.3 Phase 2 conventions (agreed 2026-09-20)

- **SystemAdmin on ledger APIs.** Phase 2 resource APIs grant SystemAdmin the same write surface as TenantAdmin (and read). The caller has no `TenantId`; list and create take the target tenant’s PublicId. Get / update / disable by resource PublicId may cross tenants. Cross-tenant 404 applies to TenantAdmin / Adviser only. Adviser Portal still has no SystemAdmin ledger screens — Scalar now, Back Office later. Do not reopen Phase 1 people routes (`/users/advisers`, `/users/customers`) for SystemAdmin in this phase.
- **Test seed per slice.** Each Phase 2 Feature Spec adds rows to a dedicated Development / TestAppHost seeder (`TestSeed`). Not a schema script. Not production. Idempotent on natural keys. Functional tests still create their own isolation data; the seeder is for local Scalar / host smoke.

### 5.4 Not locked (do not invent tables from this list)

Journal header; one vs two physical rows for a buy; Opening column list; whether close forces liquidation; daily snapshots; Dashboard widget list.

### 5.5 Out of Phase 2

Live market data / live FX, Customer Portal, invitation delivery, KYC, households, advice documents, custody, fees, model portfolios, rebalancing. Opening Property or Credit accounts. Property address / appraisal workflow, credit limits / billing cycles, linking a loan to a property.

Invitation delivery, Customer Portal, audit query, and custom role tables may run in parallel with the ledger or later. They are not prerequisites of the ledger domain.

---

## 6. Phase 3+ (names only)

- Customer Portal — second public OIDC client (`customer-portal`) on the same authorization server; same hosted login; role check rejects the wrong portal
- KYC / AML, IRD, tax residency
- Households / joint / trusts
- Advice documents / SoA
- Custody / bank connectivity
- Fees, model portfolios, rebalancing
- Live market data / live FX / cross-currency legs
- NZ regulation and record retention
- External IdP (for example Entra) attached only to the authorization server
- Identity remains the `identity` Aspire resource; external IdP attaches there

---

## 7. Explicitly out (until a later phase promotes them)

- Avatars, in-app notifications, social login, MFA
- Subdomain tenant resolution in Phase 1
- Database-per-tenant or schema-per-tenant
- Customer without an `ApplicationUser`
- Any ledger API or ledger table in Phase 1
- Resource Owner Password Credentials grant
- A custom resource-API login that issues tokens
- A second OIDC client in Phase 1
- A separate identity SQL database in Phase 1

---

## 8. Phase 1 slice order

```text
schema
    └── identity-auth          (OpenIddict, hosted login, policies, UserTokens no-op; Tenants+Users tables, no Currencies)
            └── currencies     (catalog + Tenants.ReportingCurrency)
                    └── tenants
                            └── tenant-admins
                                    └── advisers
                                            └── customers
                                                    └── A allow-list / B tenants.read / C portal pages  (landed)
isolation-tests                (add from tenants onward on every business slice)
```

---

## 9. Locked from Phase 1 / open in Phase 2

List page size is locked in tenants and reused by people lists (page 1 / size 20 / max 100). People lists use `enabledOnly` like currencies.

Locked in identity-auth: `AspNetUsers.UserName` = Domain `Users.PublicId`; uniform login failure; hosted login is Razor Pages at `/login`; access 15 minutes; refresh 14 days absolute.

Phase 1 platform slices are accepted and tested (A1–A10, B1–B11, C1–C15). Phase 2 is open. Feature map in §5 is agreed. Instruments is accepted. Write the next Feature Specs one slice at a time. Do not invent table shape outside the spec that owns it.
