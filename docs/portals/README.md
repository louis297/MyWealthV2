---
title: Portals
status: review
language: en
created: 2026-09-12
updated: 2026-09-14
related:
  - ../README.md
  - ../function-plan.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
---

# Portals

UI and OIDC-client contracts. Backend handlers stay in [features/](../features/README.md).

Phase 1 has one frontend: [adviser-portal.md](adviser-portal.md). Do not add `customer-portal` or a Back Office file until that slice opens.

| Doc | Owns |
| --- | --- |
| [adviser-portal.md](adviser-portal.md) | Aspire resource, OIDC client wiring, routes, page slices |
| [frontend-conventions.md](frontend-conventions.md) | Folder layout, naming, session state, API client |
| [frontend-implementation-notes.md](frontend-implementation-notes.md) | Page-by-page construction for the current pages cut |

Implement only the **current portal slice** marked in adviser-portal.md. Pages wait for identity-auth amendment A and tenants amendment B. Do not add `customer-portal` or a Back Office file until that slice opens.
