---
title: "<Portal name>"
status: draft
phase: 1
language: en
created: YYYY-MM-DD
updated: YYYY-MM-DD
related:
  - README.md
  - frontend-conventions.md
  - ../function-plan.md
  - ../features/identity-auth.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
---

# <Portal name>

Aspire resource + OIDC client contract + the **current** page slice. Backend verbs live in Feature Specs, not here.

Phase 1 public client is `adviser-portal` only. Do not add a `customer-portal.md` until that slice opens.

---

## Current slice

**In**

- 

**Out**

- Dashboard / Accounts / Transactions / Instruments
- Customer Portal / Back Office
- React password login (hosted login on `identity`)

---

## Routes

| Path | Who | Notes |
| --- | --- | --- |
| `/callback` | all sessions | PKCE redeem; single-flight |
| `/` | | |
| `/forbidden` | | wrong-role leftover token |

---

## Session

- Authorize against `identity` discovery. No custom `/auth/login` on `webapi`.
- Session probe: `GET /users/me`. 401 → refresh once, then re-authorize.
- Sign out: revoke refresh, then end-session; next visit is hosted `/login`.

---

## Acceptance (Cn)

| ID | Behaviour |
| --- | --- |
| C1 | |

---

## Tests

Portal tests assert routes, role visibility, and API-client shapes. Do not re-prove backend isolation here.
