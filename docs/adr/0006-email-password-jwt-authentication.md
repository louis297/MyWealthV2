---
title: "ADR 0006: Email + password + ASP.NET Identity (issuer is 0014)"
status: accepted
language: en
date: 2026-08-18
updated: 2026-09-12
related:
  - 0014-openiddict-authorization-code-pkce.md
  - 0012-user-activation-invite-deferred.md
  - 0013-roles-authorization-single-user-table.md
---

# ADR 0006: Email + password + ASP.NET Identity (issuer is 0014)

Status: accepted

**As token issuer and host, this ADR is superseded by 0014.**  
What still holds: ASP.NET Identity stores users and password hashes; the login key is email + tenantCode; a Customer is a login principal.

## Context

A separated frontend needs tokens. A custom `POST /auth/login` on `webapi` that mints JWTs is enough for one SPA and is replaced when a second portal or an external IdP attaches.

## Decision (what this file still owns)

- Credential store: ASP.NET Identity (`ApplicationUser` / `AspNetUsers`). Do not expose `MapIdentityApi` as the product surface. Do not use `AspNetRoles`.
- Login key: email + password + **tenantCode** (SystemAdmin omits tenantCode). Resolve Domain `Users` by tenant, then verify the password.
- A Customer **may obtain a session**. Phase 1 has no Customer Portal client.
- Invitation and forgot-password are not built in Phase 1 (ADR 0012).
- Application authorization: named policies + code map + handler scope (ADR 0013). Permissions are not expanded into the JWT.

## Moved to 0014

- Who issues tokens, which grant, where refresh lives, protocol paths, hosted login, Aspire resource `identity`
- **No** custom `/auth/login` issuer, **no** custom `RefreshTokens` table, **no** password grant

## Alternatives considered

See ADR 0014. This file no longer lists “the resource API mints JWTs” as an option.

## Consequences

The identity slice still dual-writes `Users` + `AspNetUsers`. Password change and disable trigger revocation. How revocation reaches the token store is the port described in 0014.
