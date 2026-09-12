---
title: Adviser Portal
status: draft
phase: 1
language: en
created: 2026-09-12
updated: 2026-09-13
related:
  - README.md
  - frontend-conventions.md
  - ../function-plan.md
  - ../features/identity-auth.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
---

# Adviser Portal

Only Phase-1 frontend and only public OIDC client. Aspire resource name and ClientId are both `adviser-portal`.

The portal **does not issue tokens**. Password collection stays on hosted login (`identity`). This file is not a backend Feature Spec.

---

## Current slice — shell and callback

**Landed in repo master (2026-09-13).** Do not rebuild it. Next backend slice is [currencies](../features/currencies.md) (`review`). Portal pages still wait for people APIs.

### In

- Vite app on Aspire as `adviser-portal` (ADR 0003).
- Folder layout from [frontend-conventions.md](frontend-conventions.md).
- Discover identity from `{authority}/.well-known/openid-configuration`. Do not hard-code `/connect/*` paths beyond what discovery already lists.
- Unauthenticated visit → authorize redirect (authorization code + PKCE S256 + `offline_access`).
- Route `/callback` → exchange `code` at the token endpoint → store tokens → `GET {webapi}/users/me`.
- A session probe page that renders the `/users/me` JSON (or display name + role + tenant). Enough to prove the handshake.
- Register the Vite origin callback on the OpenIddict client row (replace the seed default `https://localhost/callback`).
- AppHost injects identity authority and webapi base URL into the portal.
- SystemAdmin **is allowed** on the probe (Development seed is SystemAdmin). A Customer who completes login may see the probe in this slice; the later role gate still belongs with the Customers pages.

### Out

- A password / tenantCode form in React.
- Profile edit, Customers, Advisers, navigation chrome beyond a one-line shell.
- Refresh-token product UX beyond “401 → try refresh once → re-authorize”.
- Customer Portal client. Ledger pages.

### Routes

| Path | Behaviour |
| --- | --- |
| `/` | If no access token, start authorize. If signed in, show the session probe. |
| `/callback` | PKCE exchange only. Then `/`. |
| `/login` | Do **not** exist as a password page. If present, it only redirects to authorize. |

### Client

| Item | Value |
| --- | --- |
| ClientId | `adviser-portal` |
| Type | Public + PKCE. No client secret. Password grant off. |
| Scopes | `openid`, `profile`, `offline_access`, `api` |
| Redirect | `{portalOrigin}/callback` |
| Post-logout | `{portalOrigin}/` |
| Token store | Redux + `sessionStorage` |

### Acceptance

1. `dotnet run --project src/AppHost` shows `adviser-portal` Healthy.
2. Open the portal → identity `/login` → seed SystemAdmin (`system-admin@localhost` / `Administrator1!`, empty tenantCode) → return to the portal.
3. Probe shows `/users/me` for that user. No hand-copied `code`.

Suggested commits: (1) Vite + Aspire resource + env; (2) authorize + `/callback` + token store; (3) probe `GET /users/me`.

---

## Later slices (do not implement now)

After people APIs exist:

| Page | Who | Notes |
| --- | --- | --- |
| Shell / nav | TenantAdmin, Adviser | Role-filtered. Default home = Customers. |
| Profile | Authenticated | `PUT /users/me`, `PUT /users/me/password` |
| Customers | TenantAdmin; Adviser (assigned) | List / detail / form |
| Advisers | TenantAdmin | List / form |

**Still out of Phase 1:** Dashboard, Accounts, Transactions, Instruments, Customer Portal, SystemAdmin tenant UI (Scalar).

When those pages ship, block Customer from this client after `/users/me` (role gate). SystemAdmin is directed to Scalar for tenant work; the probe may remain as a dev aid.
