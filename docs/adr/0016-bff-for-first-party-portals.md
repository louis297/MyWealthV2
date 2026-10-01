---
title: "ADR 0016: BFF holds first-party portal OIDC and tokens"
status: accepted
language: en
date: 2026-10-01
updated: 2026-10-01
deciders: Louis
related:
  - ../architecture.md
  - ../function-plan.md
  - ../glossary.md
  - 0003-react-redux-typescript-vite-tailwind-frontend.md
  - 0014-openiddict-authorization-code-pkce.md
  - ../features/identity-auth.md
  - ../features/bff-adviser-portal.md
  - ../portals/adviser-portal.md
---

# ADR 0016: BFF holds first-party portal OIDC and tokens

Status: accepted

Write an ADR when the choice would surprise a later reader (host, identity, schema lifecycle, keys, tenancy, money). Everyday slice contracts belong in the Feature Spec [bff-adviser-portal.md](../features/bff-adviser-portal.md).

## Context

Phase 1 landed a public OIDC client in the browser: Aspire resource and `ClientId` `adviser-portal`, authorization code + PKCE, tokens in Redux + `sessionStorage`, SPA talking to `identity` (`/connect/*`) and to `webapi` with `Authorization: Bearer`.

That shape works. It also puts access and refresh tokens in JavaScript-reachable storage and makes every new portal re-implement the protocol client.

A later Customer Portal is already planned as a second client on the same `identity` host (ADR 0014). Repeating a public SPA token stack would be the throwaway. Moving tokens out of the browser now is the shape that second portal should copy.

Constraints that stay (do not reopen):

- OpenIddict stays in Aspire resource `identity` (`src/IdentityHost`). Protocol paths stay on library defaults. No custom `/auth/login` issuer. No password grant. No custom `RefreshTokens` table.
- `webapi` is the resource API only. It validates Bearer via discovery / JWKS. Auth failure is 401, never 302 to hosted login.
- Hosted login stays on `identity` at `/login`. Portals do not collect a password that issues tokens.
- Client × role allow-list stays on IdentityHost (identity-auth R4 / R20).
- Claims, scopes, revocation events, and `/users/me` on `webapi` stay as identity-auth.
- One SQL database `MyWealthDbV2`. The BFF does not own schema and does not run the applicator.
- Adviser Portal UI stack stays ADR 0003 (React + Redux + TypeScript + Vite + Tailwind).

Locks this ADR **replaces** when accepted:

- Architecture: portals receive and store access / refresh tokens.
- Adviser Portal client type: public + PKCE, no client secret; token store Redux + `sessionStorage`.
- Frontend conventions: SPA calls `identity` `/connect/token` and `/connect/revocation`.
- Function plan wording that a later Customer Portal is a second **public** OIDC client in the browser.

## Decision

- First-party portals talk to a **Backend-for-Frontend** process, not to `identity` or `webapi` from JavaScript.
- The Adviser Portal BFF is a new Aspire resource `bff-adviser-portal` at `src/BffAdviserPortal`. One portal, one BFF. Customer Portal later is a second host + second client, not a shared BFF. Host codes: [glossary](../glossary.md).
- `ClientId` stays `adviser-portal`. The **holder** of that client is the BFF, not the browser. The client is **confidential**: client secret lives in Aspire configuration; authorization code + **PKCE** stay on. Password grant stays off.
- The BFF runs ASP.NET Core cookie authentication plus the OpenID Connect handler, and YARP (or the equivalent reverse-proxy package already acceptable in this stack) to `webapi`. Do not take Duende.BFF.
- The BFF is the only party that redeems the code, stores access and refresh tokens, refreshes, and revokes.
- The browser holds a session **cookie** only (`__Host-` prefix, no `Domain`). Access and refresh tokens must never appear in JavaScript-reachable storage. First slice may keep the OIDC ticket inside the encrypted cookie; a later amendment may move that ticket to a table through `SessionStore`. No session table in this decision.
- Mutating browser calls carry `X-MyWealth-Request: 1`. The BFF proxies only listed `webapi` prefixes and drops the browser `Cookie` and `Authorization`. The BFF does not enable credentialed CORS.
- Redirect URI and post-logout URI are on the **BFF origin** (`/signin-oidc`, `/`). Do not remap OpenIddict `/connect/*`.
- `webapi` does not change its auth surface: Bearer from the BFF, JWKS against `identity`. Scalar and non-portal callers still hit `webapi` directly.
- The BFF does not mint JWTs, does not host OpenIddict, does not host the password page, and does not become a second resource API.

## Alternatives considered

| Option | Why not |
| --- | --- |
| Keep public SPA + PKCE + `sessionStorage` | Tokens stay in JS; every new portal copies the protocol client. |
| Put the BFF inside `webapi` | Mixes the resource API with a browser session. `webapi` would start 302-ing or growing cookie auth. Conflicts with ADR 0014 process split. |
| One shared BFF for every portal | Allow-lists, cookies, and origins tangle. Customer Portal should be a second client registration plus a second host. |
| Duende.BFF | Extra licensed surface. Cookie + OIDC handler + YARP is enough for one first-party SPA. |
| `webapi` accepts the portal cookie | Second auth mode on the resource API; Scalar and future non-browser callers stay Bearer. Do not fork that. |
| Confidential client without PKCE | Secret in config is not a reason to drop PKCE. Keep both. |
| Browser still public, BFF only proxies with a Bearer the SPA sends | That is not a BFF; tokens remain in JS. |

## Consequences

**Good**

- Customer Portal copies this host pattern: new BFF, new `ClientId`, same `identity`, same `webapi`.
- XSS in the SPA cannot read refresh. The blast radius of a stolen cookie is the session, bounded by a 15-minute access token and refresh rotation.
- `webapi` CORS for the Vite origin can go away once the browser only talks to the BFF. Scalar origin stays.
- SPA session code shrinks to cookie + `/api` + `/users/me`.

**Cost / follow-up**

- Aspire grows a fourth product process (`identity`, `webapi`, `adviser-portal`, `bff-adviser-portal`). Vite is reached through the BFF origin in the product path.
- OpenIddict application row becomes confidential; secret and redirect URIs are upserted for the BFF origin (including the Aspire dashboard alias in Development).
- Functional tests that boot the portal session start the BFF. Identity-auth A1–A10 stay; do not re-prove allow-list here except “Customer still gets no session on this client”.
- Production multi-instance BFF needs shared Data Protection keys. Development single instance does not lock that store.
- Companion docs when this ADR is accepted: architecture host map, function-plan §0 session bullet, glossary OIDC-client rows, identity-auth amendment (client type + who redeems the code), [adviser-portal.md](../portals/adviser-portal.md) client table, [frontend-conventions.md](../portals/frontend-conventions.md).

**Still not this decision**

- Separate identity SQL database.
- Registering `customer-portal`.
- Changing named policies, claims, or `/users/me`.
- Serving the React app from `webapi`.

## Links

- Feature spec: [features/bff-adviser-portal.md](../features/bff-adviser-portal.md)
- Related ADRs: 0014 (issuer and grant), 0003 (SPA stack), 0006 (password store; issuer still 0014)
- Portal: [portals/adviser-portal.md](../portals/adviser-portal.md)
- Identity: [features/identity-auth.md](../features/identity-auth.md)
---
