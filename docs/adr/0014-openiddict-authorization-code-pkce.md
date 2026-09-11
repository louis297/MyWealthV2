---
title: "ADR 0014: OpenIddict authorization code + PKCE"
status: accepted
language: en
date: 2026-09-11
updated: 2026-09-12
deciders: Louis
related:
  - ../function-plan.md
  - ../glossary.md
  - ../architecture.md
---

# ADR 0014: OpenIddict authorization code + PKCE

Status: accepted

## Context

MyWealthV2 already needs a login-capable Customer and will add a Customer Portal later. A custom `POST /auth/login` that mints JWTs from the resource API would work for one SPA and then be replaced.

The product is a learning platform. When a second first-party client is already planned, the token protocol should be the one that client will use.

A later hosting split of the authorization server was previously deferred. That split is now treated as certain: keeping OpenIddict inside `webapi` would force a process move when a second backend or an external IdP attaches. Phase 1 therefore hosts the authorization server in its own process.

Constraints that stay:

- ASP.NET Identity remains the user and password store.
- Four roles stay on Domain `Users` and `ApplicationUser`.
- Login still requires tenantCode except for SystemAdmin.
- Application authorization stays named policies plus handler scope checks.
- Invitation, forgot-password, MFA, and external IdP are not Phase 1.
- One SQL database in Phase 1: `MyWealthDbV2`. A separate identity database is not decided.

## Decision

- Host **OpenIddict** in a dedicated Aspire resource `identity` (`src/IdentityHost`). Do not put the authorization server in `webapi`.
- `webapi` is the resource API only. It validates bearer tokens via OpenID Connect discovery / JWKS against `identity`. Do not use `UseLocalServer()` as the production validation path (same-machine development may still share keys through configuration).
- Signing keys are **asymmetric**. Public keys are published on JWKS.
- First-party portals use **authorization code + PKCE + refresh**. Do not enable the resource-owner password grant.
- Phase 1 registers one public client: `adviser-portal`. Customer Portal is a later second client on the same `identity` host, not a second issuer.
- Password collection happens on a **hosted login page** on `identity`. Portals redirect and handle the callback. They do not issue tokens.
- Access tokens are short-lived JWTs. Refresh tokens live in the OpenIddict store and are revoked on logout, password change, and disable.
- Claims include user PublicId, email, role, tenant PublicId, and tenantCode (tenant claims empty for SystemAdmin). Do not expand permissions into the token.
- `/users/me` stays a resource-API surface on `webapi`.
- There is no custom `RefreshTokens` table and no custom `/auth/login` token endpoint.
- Protocol paths stay on **OpenIddict defaults**. Do not remap. Phase 1 uses `/.well-known/openid-configuration`, `/.well-known/jwks`, `/connect/authorize`, `/connect/token`, `/connect/revocation`, `/connect/logout` (and the default `/connect/userinfo`). Clients and `webapi` follow discovery. The hosted login page is not a `/connect` endpoint; its own route is not locked.
- Phase 1 keeps **one database** (`MyWealthDbV2`). Both hosts share the schema and the Infrastructure mappings. Identity tables are not split into a second SQL database.

## Alternatives considered

| Option | Why not |
| --- | --- |
| Custom `/auth/login` JWT issuer | Second portal and SSO would replace it. |
| Password grant through OpenIddict | Looks like a portal login form; still thrown away for a second SPA. |
| OpenIddict inside `webapi` | Second host or external IdP would move the issuer. Protocol would survive; process topology would not. |
| Separate identity SQL database in Phase 1 | Dual-write and revocation become distributed. Not required for a second portal. |
| Duende IdentityServer | Strong product; license and surface area are more than this repo needs. |
| Auth0 / Entra External ID / Keycloak | Moves tenantCode, UserStatus, and invite seams into someone else’s model too early. |

## Consequences

**Good**

- Customer Portal is a client registration plus a role gate, not a new session stack.
- External IdP later attaches only to `identity`.
- `webapi` can scale and deploy without hosting the login page.
- Token revocation and discovery are library features instead of a private protocol.

**Cost / follow-up**

- Phase 1 includes a second .NET host, two composition roots, CORS / redirect / issuer wiring through Aspire.
- Login-page cookies live on the `identity` origin; resource API failures on `webapi` must return 401, never a login redirect.
- Functional tests start `identity` and `webapi`.
- Schema applicator still runs **once** against `MyWealthDbV2` (owner: `webapi` startup, or an explicit apply step). `identity` does not race a second applicator.
- Person create / password change stay on `webapi` resource APIs. They write `Users` + `AspNetUsers` through shared Infrastructure on the same database. Revocation uses a port that talks to the OpenIddict store (same database in Phase 1). Replacing that port with HTTP is what a later identity-database split would change.

## Links

- Function plan §4.2
- Glossary: authorization server, OpenIddict, OIDC client, PKCE, identity
