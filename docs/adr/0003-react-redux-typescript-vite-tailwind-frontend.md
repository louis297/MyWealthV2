---
title: "ADR 0003: React + Redux + TypeScript + Vite + Tailwind frontend"
status: accepted
language: en
date: 2026-08-18
updated: 2026-09-12
related:
  - 0014-openiddict-authorization-code-pkce.md
  - ../architecture.md
---

# ADR 0003: React + Redux + TypeScript + Vite + Tailwind frontend

Status: accepted

Session protocol is ADR 0014.

## Context

Lists, filters, and session state need predictable state and types. Phase 1 only has an adviser-facing product surface.

## Decision

- React + Redux Toolkit + TypeScript + Vite + Tailwind + React Router.
- Frontend and backend talk over HTTP only. Portals **do not issue tokens**: they redirect to hosted login on `identity` and call `webapi` with Bearer after the callback.
- The only Phase-1 frontend and the only public OIDC client: Aspire resource name and ClientId are both `adviser-portal`.
- Phase-1 pages: shell, Profile, Customers, Advisers. Default home is Customers. No ledger pages.
- A Customer must not use this client. SystemAdmin uses Scalar. There is no Back Office in Phase 1.

## Alternatives considered

| Option | Why not |
| --- | --- |
| Next.js SSR | Not needed in Phase 1 |
| A portal-owned password form that mints JWTs | Rejected by ADR 0014 |
| Registering Customer Portal in Phase 1 | The server shape is already final; registering the client waits for that slice |

## Consequences

A later `customer-portal` or `back-office` is a new client plus a role gate. It does not change the frontend stack or the authorization-server host.
