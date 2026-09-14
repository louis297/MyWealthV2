---
title: "<Name>"
status: draft
phase: 1
language: en
owner: ""
created: YYYY-MM-DD
last_updated: YYYY-MM-DD
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../architecture.md
---

# <Name>

<!-- One paragraph: who calls it, which verbs, what already exists, whether this slice adds a script or a policy name. -->

---

## 1. Summary

<!-- Two or three sentences: who, what lands, the one rule that distinguishes this slice. -->

---

## 2. Scope

**In**

- Application:
- Domain:
- HTTP:
- Tests:

**Out**

- 
- Invitation / forgot-password / Phase-2 ledger tables

---

## 3. Stories

1. As a … I … so that …
2. 

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Wrong role → 403. |
| R2 | Path `{id}` and JSON `id` are PublicId. Internal ints never appear. |
| R3 | |

---

## 5. Domain

| Type | Kind | Notes |
| --- | --- | --- |
| | aggregate / entity / VO / event | |

Invariants:

- 

Domain events:

- 

Update [domain-model.md](../domain-model.md) in the same change.

---

## 6. Database

| Table | Change | Index / FK |
| --- | --- | --- |
| | add / alter / none | |

Script: `database/schema/NNNN_<name>.sql` (or **none**).

Update [database-design.md](../database-design.md) in the same change. Do not add EF migrations.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Command | | | |
| Query | | | |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| | | | | |

List envelope (if any): `{ items, page, pageSize, totalCount }`; page default 1, pageSize default 20 max 100.

Create returns `201 { "id" }` only unless this spec says otherwise.

### 8.1 Bodies

```json
{}
```

### 8.2 Errors

Use [api-design.md](../api-design.md) §4.1 only where this spec names `code` / `target`. Ordinary validation stays in `errors`.

Cross-tenant or wrong-collection id → **404**, not 403.

Keep the resource catalog in [api-design.md](../api-design.md) pointing here for field rules.

---

## 9. UI

<!-- Portal impact, or "None in this slice. Pages live in docs/portals/." -->

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | |
| Application.FunctionalTests | No Bearer → 401 not 302; wrong role → 403; happy path; validation; `rowVersion` 409 if used |
| Infrastructure.IntegrationTests | Two-tenant fixture from Tenants onward: other tenant’s id → 404 |

---

## 11. Locked in this spec

No new ADR. / New ADR: `adr/NNNN-….md`

| Item | Lock |
| --- | --- |
| Scripts / policy | |
| Caller | |
| Scope | |

Still open, not this slice:

- 

---

## 12. Suggested commits

The repository should build after each commit. Agents split each line into red then green.

1. 
2. 
