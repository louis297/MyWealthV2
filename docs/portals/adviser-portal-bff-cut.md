---
title: Adviser Portal BFF cut
status: landed
phase: 2
language: en
created: 2026-10-02
updated: 2026-10-02
related:
  - adviser-portal.md
  - frontend-conventions.md
  - frontend-implementation-notes.md
  - ../features/bff-adviser-portal.md
  - ../adr/0016-bff-for-first-party-portals.md
---

# Adviser Portal BFF cut

Record of the SPA cut that points `adviser-portal` at `bff-adviser-portal`. Not a second Feature Spec. Session rules stay in [bff-adviser-portal.md](../features/bff-adviser-portal.md). Page inventory stays in [adviser-portal.md](adviser-portal.md).

Landed on repo master `6aae5c7` (slice head `5e85e9c`, 2026-10-01). Do not replay it. Construction notes under the BFF banner in [frontend-implementation-notes.md](frontend-implementation-notes.md) describe the retired public-client shell.

---

## 1. What changed

The browser stops being the OIDC client. It talks only to the BFF origin.

| Before | After |
| --- | --- |
| SPA authorization code + PKCE | `GET /bff/login?returnUrl=` |
| SPA `/callback` redeems the code | BFF `/signin-oidc` redeems once |
| Tokens in Redux + `sessionStorage` | HttpOnly cookie `__Host-bff-adviser-portal` |
| `Authorization: Bearer` on `webapi` | `credentials: 'include'` to `/api` on the BFF |
| 401 refresh at `identity` `/connect/token` | BFF refreshes once; SPA clears user state and goes to `/bff/login` |
| `VITE_WEBAPI_BASE_URL` | Same-origin `/api` |
| `webapi` portal CORS | Removed (`5e85e9c`) |

`ClientId` stays `adviser-portal`. The holder is the BFF. The secret is Aspire parameter `adviser-portal-client-secret`, not a Vite env var.

---

## 2. Browser contract

- Login: `startLogin` assigns `/bff/login?returnUrl=`. `returnUrl` must be an in-app path (`/`, `/profile`, `/session`, `/forbidden`, `/customers…`, `/advisers…`). Anything else becomes `/`.
- Sign-out: set `sessionStorage` key `adviser-portal.signOutInProgress` (flag only, not a token), `POST /bff/logout` with `X-MyWealth-Request: 1` and `credentials: 'include'`, then `GET /bff/logout/continue`.
- API: RTK Query `baseUrl` is `{window.location.origin}/api`, `credentials: 'include'`. Mutations set `X-MyWealth-Request: 1`. No `Authorization`.
- 401: `clearSession`, then `startLogin` unless sign-out is already in progress. Do not call `/connect/token`.
- No React route `/callback`. No password page that issues tokens.

Vite `vite.config.ts` has no proxy. In Development the BFF `MapFallback` forwards document requests to `Spa:DevServerUrl` and refuses `/api`, `/bff`, `/signin-oidc`, `/signout-callback-oidc`, `/health`, `/alive`. Product auth origin remains the BFF. Do not set the session cookie to `SameSite=None` to paper over a raw Vite origin.

---

## 3. Files (landed)

| Path | Role |
| --- | --- |
| `src/AdviserPortal/src/features/session/session.ts` | `startLogin`, `startEndSession`, in-app path check, sign-out flag |
| `src/AdviserPortal/src/shared/api/api.ts` | `/api`, credentials, mutation header, 401 → login |
| `src/AdviserPortal/src/app/router.tsx` | `/callback` removed |
| `src/AdviserPortal/src/app/store.ts` | No token hydrate |
| `src/BffAdviserPortal/Program.cs` | `/bff/login`, SPA fallback |
| `src/AppHost/Program.cs` | Parameter, env injection, `Spa__DevServerUrl` |
| `src/Web/Program.cs` | Portal CORS removed |

Tests that imported `@/features/session/oidc` or asserted Bearer / `sessionStorage` token keys were retargeted in the same commit.

---

## 4. Aspire secret

AppHost declares `AddParameter("adviser-portal-client-secret", secret: true)` with no git default.

| Consumer | Env |
| --- | --- |
| `identity` | `Identity__AdviserPortalClientSecret` |
| `bff-adviser-portal` | `Authentication__ClientSecret` |

`OpenIddictSeeder` throws if the value is missing, then upserts client `adviser-portal` as confidential. First `dotnet run --project src/AppHost` prompts. Persist with:

```bash
dotnet user-secrets set "Parameters:adviser-portal-client-secret" "<dev-secret>" --project src/AppHost
```

This is not gated on a later portal cut. The portal never reads the secret.

---

## 5. Not in this cut

- Do not add YARP. The landed proxy is `ApiProxy` (`/users`, `/tenants`, `/currencies`, `/accounts`, `/instruments`, `/transactions`).
- Do not put tokens back in the SPA to “finish” HMR.
- Do not register the raw Vite origin as an OIDC redirect.
- Customer Portal BFF is a later file (`bff-customer-portal`).
- Production Data Protection key ring and a `BffSessions` table stay open on the BFF spec.

---

## 6. Development entry

Product redirect remains `{bffOrigin}/signin-oidc`. The current Aspire dashboard link is `https://localhost:7190/` (launchSettings https). That port is not a contract. Do not register `http://localhost:5290` or the Vite origin. Upsert a `*.dev.localhost` alias only when the browser actually uses that host. Ignoring `X-Forwarded-Host` is a Development workaround so the challenge matches this https entry.

`DevelopmentSpaProxy` may catch a Vite `ResponseEnded` and return 502 so the BFF resource is not marked failed. That catch is Development-only. It does not apply to `/api` or the login callback. HMR through this proxy is not a goal.

## 7. Residual

- Copy this origin wording into repo `docs/` on the next docs commit. Agent edits that pin `https://localhost:7190` as the product origin should be replaced with this section.
- Confirm `ApiProxy` drops inbound `Cookie` and `Authorization` before it sets Bearer (spec R21). The landed file forwards other inbound headers, including `Idempotency-Key`.
- `frontend-implementation-notes.md` body under the BFF banner is historical. Do not treat `/callback` or browser refresh-once as current.
