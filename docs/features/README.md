---
title: Feature Specs
status: draft
language: en
created: 2026-09-12
updated: 2026-09-14
related:
  - ../function-plan.md
  - ../api-design.md
  - ../README.md
---

# Feature Specs

One file per vertical slice, flat in this folder. Do not split by phase. Put the phase in front matter and in this README.

Scope lives in [function-plan.md](../function-plan.md). HTTP conventions live in [api-design.md](../api-design.md). Field contracts live in the Feature Spec, not back in api-design.

Do not add empty files for a phase that has not opened.

---

## Discipline

- **In** scope is only what that slice ships. `draft` / `review` are not build contracts. `accepted` is.
- `note` is discussion only. Do not create tables or routes from a note.
- From Tenants onward, every tenant-scoped slice includes cross-tenant isolation tests.
- `/users` is a namespace prefix for people collections, not a parent resource. Do not nest `/users/{id}/advisers`. `/tenants` and `/currencies` stay at the root.
- The Adviser Portal is not a backend slice. Portal scope lives in [portals/](../portals/README.md).
- Implementation plans mark suggested git commits. The repository should build after each commit.

---

## Phase 1 — platform foundation

The vertical cut stops at people in a firm, the currency catalog, and session. No ledger tables or ledger APIs.

| Spec | Status | Who | Ships | Why it stays its own slice |
| --- | --- | --- | --- | --- |
| [identity-auth](identity-auth.md) | accepted (landed in repo) | Login-capable roles | OpenIddict defaults, hosted `/login`, policies, `/users/me`, revocation, UserStatus, `UserTokens` seam | Two processes, one product capability |
| [currencies](currencies.md) | review (HTTP landed in repo 2026-09-13) | Authenticated (all four roles) | Catalog + `ICurrencyCatalog` + `GET /currencies` (`enabledOnly`) + `Tenants.ReportingCurrency` + Domain `Money` | Own table; HTTP after Bearer validation works |
| [tenants](tenants.md) | accepted (landed in repo 2026-09-13) | SystemAdmin | `/tenants` list / get / create / update / disable / enable | Platform resource; path is not under `/users` |
| [tenant-admins](tenant-admins.md) | accepted (landed in repo 2026-09-13) | SystemAdmin | `/users/tenant-admins`; dual-write; last-admin disable allowed; disabled tenant → 400 `disabled`/`tenant` | Caller and gap belong here |
| [advisers](advisers.md) | accepted (landed in repo 2026-09-14 `8e5568c`) | TenantAdmin | `/users/advisers`; current tenant; cross-tenant 404; `DisableAdviser(bool)` guard | Different guard and portal list |
| [customers](customers.md) | review | TenantAdmin; Adviser (assigned) | `/users/customers`; login principal; reassign + `CustomerAdviserReassigned`; no portal; no ledger guard | Assignment scope and rebind |

Do not merge the three people slices into `users.md`.

Not a Feature Spec:

| Capability | Lives in |
| --- | --- |
| Schema applicator | ADR 0008, architecture, database-design |
| Isolation tests | Each business slice from tenants onward |
| Invitation delivery | Out of identity-auth; table and email port reserved |
| Ledger policy names | Phase 2 |
| Adviser Portal (OIDC shell, later pages) | [portals/](../portals/README.md) |
| SystemAdmin UI | None — Scalar |

### Implementation order

```text
schema
    └── identity-auth
            └── adviser-portal shell + callback   (portals/adviser-portal.md, current slice)
                    └── currencies
                            └── tenants
                                    └── tenant-admins
                                            └── advisers
                                                    └── customers
isolation-tests
```

Repo master (2026-09-14, `8e5568c`): identity-auth, the adviser-portal **current** slice (Vite resource, PKCE authorize / `/callback`, session probe via `GET /users/me`, 401 refresh-once, OpenIddict redirect upsert including the Aspire dashboard alias), `GET /currencies`, [tenants](tenants.md), [tenant-admins](tenant-admins.md), and [advisers](advisers.md) are in the tree. Currencies Feature Spec is still `review`. Next backend slice is [customers](customers.md) (`review`; not in the repo yet).

Portal pages (Profile / Customers / Advisers) still wait for those Feature Specs.

`GET /currencies` is not public. Register no `currencies.read` policy. Use default `.RequireAuthorization()`. Query `enabledOnly`: omitted / `false` = all rows; `true` = enabled only. Every item includes `isEnabled`.

---

## Later phases

The ledger is one domain, sliced when that phase opens. Tendencies live in the function plan and ADR 0010 / 0011. This folder does not keep empty Phase-2 files.
