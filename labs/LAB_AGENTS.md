# Labs — agent rules

This file is `labs/LAB_AGENTS.md`. Do not rename it to `AGENTS.md` in the discussion space; the project file list hides that name.

This file applies **only** when the current task is explicitly a lab (Angular Adviser Portal or another task that names `labs/`).

If the task is product work (Phase 2 ledger, people APIs, React `adviser-portal`, accepted Feature Specs): **stop**. Do not read the rest of this file. Do not edit `labs/`. Follow the repo-root `AGENTS.md` only.

---

## What labs are

`labs/` is not product source of truth. Product design is `docs/`. Product frontend is React + Redux + Vite + Tailwind (`adviser-portal`, ADR 0003). Nothing in `labs/` is `accepted` product status.

Lab contracts: `labs/docs/`. Lab CLI brief for this app: `labs/ADVISER_PORTAL_ANGULAR_CLI_BRIEF.md`. Page and session rules: `labs/docs/adviser-portal-angular.md`.

When a lab file disagrees with an **accepted** product spec, the product spec wins for server behaviour. The lab file wins for lab UI, session, and folder layout.

---

## Lab exception (working mode)

Repo-root Phase 2 rule (“CLI writes tests only; human writes production code”) **does not apply** inside `labs/`.

You may generate the Angular app, Development Aspire wiring, and the OpenIddict seed row named in the lab spec.

Still: one behaviour per commit, tree builds after every commit, stop after 3 variants on the same failing assertion. Do not invent HTTP, tables, or ClientIds.

---

## Stack (this lab)

| Item | Use |
| --- | --- |
| App path | `labs/adviser-portal-angular/` |
| UI | Angular current stable 22.x + Material + CDK |
| State | NgRx SignalStore only (`@ngrx/signals`) |
| Shell | standalone + `provideRouter` + `provideHttpClient` |
| Tokens | SignalStore + `sessionStorage` |
| Session this cut | `/session` bearer paste → `GET /users/me`. No hosted login |

Do **not** apply ADR 0003, product `docs/portals/frontend-conventions.md`, Redux, RTK Query, or Tailwind inside `labs/`.

---

## Do not touch

- Product Vite app and its packages
- Accepted product specs except a pointer that `labs/` exists (already in discussion `docs/README.md`)
- ClientIds `customer-portal` and Back Office
- Password grant; any lab field that issues tokens
- `/connect/authorize`, `/callback` redeem, `/connect/revocation`, `/connect/logout` from this app (this cut)
- New `webapi` routes, policies, or schema scripts
- NgRx global Store / Effects / ComponentStore
- SSR / Universal
- Promoting lab docs to product `accepted`

Allowed Development-only host edits on a lab task: CORS origin for the lab, OpenIddict seed `adviser-portal-angular`, allow-list key with the same roles as `adviser-portal`, Aspire resource `adviser-portal-angular`. Do not change A1–A10 for client `adviser-portal`.

---

## Read order on a lab task

1. This file
2. `labs/docs/adviser-portal-angular.md`
3. `labs/ADVISER_PORTAL_ANGULAR_CLI_BRIEF.md` when the implementer is the build CLI
4. Product people specs only for HTTP shapes (`customers`, `advisers`, `identity-auth` `/users/me`, `tenants` by-code)

Do not implement the lab from `docs/portals/adviser-portal.md` alone. That file is the React product cut, including hosted login.
