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
| [identity-auth](identity-auth.md) | accepted (landed in repo 2026-09-14) | Login-capable roles | OpenIddict defaults, hosted `/login`, policies including `tenants.read`, `/users/me`, revocation, UserStatus, `UserTokens` seam, **client × role allow-list** | Two processes, one product capability |
| [currencies](currencies.md) | review (HTTP landed in repo 2026-09-13) | Authenticated (all four roles) | Catalog + `ICurrencyCatalog` + `GET /currencies` (`enabledOnly`) + `Tenants.ReportingCurrency` + Domain `Money` | Own table; HTTP after Bearer validation works |
| [tenants](tenants.md) | accepted (landed in repo 2026-09-14) | SystemAdmin manage; TenantAdmin / Adviser read-own | `/tenants` manage + `GET /tenants/by-code/{code}` (`tenants.read`) | Platform resource; path is not under `/users` |
| [tenant-admins](tenant-admins.md) | accepted (landed in repo 2026-09-13) | SystemAdmin | `/users/tenant-admins`; dual-write; last-admin disable allowed; disabled tenant → 400 `disabled`/`tenant` | Caller and gap belong here |
| [advisers](advisers.md) | accepted (landed in repo 2026-09-14 `8e5568c`) | TenantAdmin | `/users/advisers`; current tenant; cross-tenant 404; `DisableAdviser(bool)` guard | Different guard and portal list |
| [customers](customers.md) | accepted (landed in repo 2026-09-14 `b16d059`) | TenantAdmin; Adviser (assigned) | `/users/customers`; login principal; reassign + `CustomerAdviserReassigned`; no portal; no ledger guard | Assignment scope and rebind |

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
            └── adviser-portal shell + callback   (landed)
                    └── currencies
                            └── tenants
                                    └── tenant-admins
                                            └── advisers
                                                    └── customers
                                                            └── A  identity-auth amendment (allow-list)
                                                                    └── B  tenants.read + by-code
                                                                            └── C  adviser-portal pages
isolation-tests
```

Repo master (2026-09-14, `b16d059`): people collections are in the tree. Currencies Feature Spec is still `review`. Amendments A/B are **docs-only** until coded. Portal pages wait for A and B.

Acceptance: [identity-auth](identity-auth.md) amendment A (A1–A10), [tenants](tenants.md) amendment B (B1–B11), [adviser-portal](../portals/adviser-portal.md) cut C (C1–C15). Construction notes (`review`): [portals/frontend-implementation-notes.md](../portals/frontend-implementation-notes.md).

`GET /currencies` is not public. Register no `currencies.read` policy. Use default `.RequireAuthorization()`. Query `enabledOnly`: omitted / `false` = all rows; `true` = enabled only. Every item includes `isEnabled`.

---

## Later phases

The ledger is one domain, sliced when that phase opens. Tendencies live in the function plan and ADR 0010 / 0011. This folder does not keep empty Phase-2 files.
