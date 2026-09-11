---
title: Feature Specs
status: draft
language: en
created: 2026-09-12
updated: 2026-09-12
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
- The Adviser Portal is not a backend slice. Portal scope belongs under `portals/` when that file exists.
- Implementation plans mark suggested git commits. The repository should build after each commit.

---

## Phase 1 — platform foundation

The vertical cut stops at people in a firm, the currency catalog, and session. No ledger tables or ledger APIs.

| Spec | Status | Who | Ships | Why it stays its own slice |
| --- | --- | --- | --- | --- |
| [identity-auth](identity-auth.md) | accepted | Login-capable roles | OpenIddict defaults, hosted `/login`, policies, `/users/me`, revocation, UserStatus, `UserTokens` seam | Two processes, one product capability |
| `currencies.md` | not opened | Authenticated (all four roles) | Catalog + `ICurrencyCatalog` + `GET /currencies` (not anonymous) | Own table; HTTP after Bearer validation works |
| `tenants.md` | not opened | SystemAdmin | `/tenants` | Platform resource; path is not under `/users` |
| `tenant-admins.md` | not opened | SystemAdmin | `/users/tenant-admins`; dual-write; last-admin disable allowed | Caller and gap belong here |
| `advisers.md` | not opened | TenantAdmin | `/users/advisers`; disable blocked while assigned Customers are not Disabled | Different guard and portal list |
| `customers.md` | not opened | TenantAdmin; Adviser (assigned) | `/users/customers`; login principal; no portal; no ledger guard | Assignment scope and rebind |

Do not merge the three people slices into `users.md`.

Not a Feature Spec:

| Capability | Lives in |
| --- | --- |
| Schema applicator | ADR 0008, architecture, database-design |
| Isolation tests | Each business slice from tenants onward |
| Invitation delivery | Out of identity-auth; table and email port reserved |
| Ledger policy names | Phase 2 |
| Adviser Portal pages | `portals/` |
| SystemAdmin UI | None — Scalar |

### Implementation order

```text
schema
    └── identity-auth
            └── currencies
                    └── tenants
                            └── tenant-admins
                                    └── advisers
                                            └── customers
isolation-tests
```

`GET /currencies` is not public. Register no `currencies.read` policy. Use default `.RequireAuthorization()`.

---

## Later phases

The ledger is one domain, sliced when that phase opens. Tendencies live in the function plan and ADR 0010 / 0011. This folder does not keep empty Phase-2 files.
