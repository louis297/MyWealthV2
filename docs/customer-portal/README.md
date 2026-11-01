---
title: Customer Portal
status: accepted
language: en
created: 2026-11-02
updated: 2026-11-02
related:
  - ../README.md
  - ../function-plan.md
  - ../features/bff-customer-portal.md
  - customer-portal.md
---

# Customer Portal

UI and OIDC-client contract for the Customer Portal. Backend handlers stay in [features/](../features/README.md).

This folder owns the Customer Portal SPA only. Session edge is the BFF Feature Spec. Adviser Portal lives in [../adviser-portal/](../adviser-portal/). Back Office lives in [../backoffice/](../backoffice/).

| Doc | Owns |
| --- | --- |
| [customer-portal.md](customer-portal.md) | Aspire SPA resource, routes, page slices (accepted). OIDC client is held by the BFF |
| [../features/bff-customer-portal.md](../features/bff-customer-portal.md) | Session edge (accepted). Not a page spec |

[customer-portal.md](customer-portal.md) is **accepted**. Implementation follows this file and the BFF Feature Spec.

Frontend conventions currently live with the Adviser Portal ([../adviser-portal/frontend-conventions.md](../adviser-portal/frontend-conventions.md)). Customer Portal reuses the same stack (ADR 0003) and BFF session shape (ADR 0016).
