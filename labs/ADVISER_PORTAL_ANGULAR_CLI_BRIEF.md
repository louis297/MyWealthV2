# CLI brief: Angular Adviser Portal lab

Copy everything below the line into Grok CLI in https://github.com/louis297/MyWealthV2.

---

Implement the **draft** lab spec `labs/docs/adviser-portal-angular.md` on current `master`.

This is a **lab**. It is not the product Adviser Portal. Product frontend stays React (`adviser-portal`, ADR 0003).

If discussion-space `labs/docs/adviser-portal-angular.md` is more recent than the repo copy, **the discussion file wins**. Add or update `labs/docs/` in the repo so they match this brief.

## Working mode

Lab exception to Phase 2 “tests only / human writes production code”: you may generate the Angular app, Aspire Development wiring, and the OpenIddict seed row.

This is a lab task: read `labs/LAB_AGENTS.md` first. Repo-root `AGENTS.md` still owns commit discipline: tree builds after every commit, one behaviour per cycle, stop after 3 variants on the same failing assertion. The Phase 2 “tests only / human writes production code” split does not apply inside `labs/`.

## Do not touch

- Product Vite app and its packages
- `docs/portals/adviser-portal.md`, ADR 0003, accepted Feature Specs (except you may add `labs/` copies)
- ClientIds `customer-portal` and Back Office
- Password grant; custom `/auth/login` on `webapi`
- Hosted-login HTML, `/connect/authorize` start from this app, `/callback` token redeem, end-session from this app
- New people / ledger HTTP, schema scripts, policies
- NgRx global Store / Effects
- Tailwind in the lab app
- SSR

## Contract

### Repo layout

```text
labs/
├── README.md
├── docs/
│   ├── README.md
│   └── adviser-portal-angular.md
├── ADVISER_PORTAL_ANGULAR_CLI_BRIEF.md
└── adviser-portal-angular/     ← Angular 22.x standalone app
```

Do not put the Angular project under `src/`.

### Stack

- Angular current stable 22.x (`ng new` in `labs/adviser-portal-angular`)
- Angular Material + CDK
- `@ngrx/signals` SignalStore only
- standalone + `provideRouter` + `provideHttpClient`
- `sessionStorage` for tokens

### Session

Lab bearer panel at `/session`. Operator pastes access token (optional refresh). Probe `GET /users/me`. No field on this app issues tokens.

401: refresh once only when a refresh token is present; otherwise clear session and go to `/session`.

Sign out: clear store + `sessionStorage` only. Do not call `/connect/revocation` or `/connect/logout` in this cut.

### Host (Development only)

- Aspire resource name and ClientId: `adviser-portal-angular`
- Seed OpenIddict public client, PKCE on, password grant off, scopes `openid profile offline_access api`
- Allow-list map: `adviser-portal-angular` → SystemAdmin, TenantAdmin, Adviser (same as `adviser-portal`)
- CORS: lab origin on `webapi`
- Default product `aspire run` path must still boot the React portal. Lab resource may start in Development when AppHost includes it; do not replace the React resource

Do not change A1–A10 for client `adviser-portal`.

### Pages

Implement product cut C routes and L1–L15 in `labs/docs/adviser-portal-angular.md`. HTTP shapes stay in the accepted people specs.

SystemAdmin: no Customers workspace on `/`. Customer token → `/forbidden`.

## Suggested commits

1. `labs/` README + docs copied from this brief’s spec
2. `ng new` Angular app in `labs/adviser-portal-angular` + Material + SignalStore wiring
3. Aspire resource + CORS + OpenIddict seed row (no authorize from the app)
4. Session panel + `GET /users/me` + role shell
5. Customers pages (list / new / detail / edit / disable / enable)
6. Advisers pages (TenantAdmin)
7. Profile + sign out
8. L1–L15 component / route tests that can run without IdentityHost hosted login
