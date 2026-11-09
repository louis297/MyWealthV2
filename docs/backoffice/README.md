---
title: Back Office
status: accepted
language: en
created: 2026-10-24
updated: 2026-11-09
related:
  - ../README.md
  - ../function-plan.md
  - ../architecture.md
  - ../glossary.md
  - back-office.md
  - back-office-instruments.md
---

# Back Office

Platform-ops UI for SystemAdmin. Independent of the Adviser Portal.

The Adviser Portal (`docs/adviser-portal/`) is a first-party firm UI (React + BFF).  
This folder owns the Back Office host only.

| Doc | Owns |
| --- | --- |
| [back-office.md](back-office.md) | Aspire resource, auth model, routes, page slices (accepted 2026-10-25; instruments routes added 2026-11-09) |
| [back-office-instruments.md](back-office-instruments.md) | Instruments catalog management page contract (accepted 2026-11-09) |

Backend verbs stay in Feature Specs (`tenants`, `tenant-admins`, report grants, and later ledger slices).  
The Back Office is a client of the existing `webapi` resource API. It does not replace those endpoints.
