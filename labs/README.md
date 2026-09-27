---
title: Labs
status: draft
language: en
created: 2026-09-27
updated: 2026-09-27
---

# Labs

Experimental trees that are **not** product source of truth.

Product design stays under `docs/`. Product frontend stays React + Redux + Vite + Tailwind (`adviser-portal`, ADR 0003). A coding agent on a **non-lab** task must not read, edit, or create files under `labs/`.

| Path | Owns |
| --- | --- |
| [LAB_AGENTS.md](LAB_AGENTS.md) | Coding-agent rules when the task is a lab |
| [docs/](docs/README.md) | Lab contracts (English) |
| [docs/adviser-portal-angular.md](docs/adviser-portal-angular.md) | Angular Adviser Portal lab |
| [ADVISER_PORTAL_ANGULAR_CLI_BRIEF.md](ADVISER_PORTAL_ANGULAR_CLI_BRIEF.md) | Brief for grok-build-cli |
| `adviser-portal-angular/` | Implementation app (lives in the GitHub repo, not in this discussion space until built) |

Do not promote anything here to `accepted` product status. Do not register `customer-portal` or a Back Office client from a lab.
