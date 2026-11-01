---
title: Frontend conventions
status: review
language: en
created: 2026-09-12
updated: 2026-10-01
related:
  - README.md
  - adviser-portal.md
  - ../features/bff-adviser-portal.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
  - ../adr/0016-bff-for-first-party-portals.md
---

# Frontend conventions

Adviser Portal only. Page inventory lives in [adviser-portal.md](adviser-portal.md).

## Stack

ADR 0003: React + Redux Toolkit + TypeScript + Vite + Tailwind + React Router.

## Layout

```text
src/
├── app/          store, hooks, router, providers
├── features/     session, profile, customers, advisers
├── shared/       api, components, hooks, types, utils
├── layouts/
├── App.tsx
└── main.tsx
```

Feature code lives under `features/<name>/` (`session`, then `profile`, `customers`, `advisers`). Do not introduce a global `pages/` tree.

## Naming

- Components PascalCase; file name matches the component.
- One async style for the repo: RTK Query for REST; a small session slice for the current user. Do not store tokens in that slice.

## Session

- The browser holds the BFF session cookie only. Access and refresh tokens must not live in Redux, `sessionStorage`, or `localStorage`.
- After `me`, if `tenantCode` is set, load `GET /api/tenants/by-code/{tenantCode}` into the store for the shell Name. Do not expect `tenantName` on `/users/me`. SystemAdmin has no tenantCode; do not call that route.
- Resource ids in the UI are PublicId values only.
- Do not put a password field on this app that **issues tokens**. Create-person and profile-change password fields are allowed.

## API

- One client in `shared/api`. Base URL `/api` on the BFF origin. `credentials: 'include'`.
- Do not set `Authorization`. Mutating calls send `X-MyWealth-Request: 1`.
- On 401, do not call `identity`. The BFF already refreshed once. Clear local user state and send the browser to `/bff/login` unless sign-out is already in progress.
- No SPA `/callback`. Keep React StrictMode.
- Sign out is `POST /bff/logout` with `X-MyWealth-Request: 1`. No portal `/auth/logout`. No SPA call to `/connect/revocation`.
- Do not treat a cross-tenant 404 as 403.

## Style

Tailwind utilities only. No second CSS-in-JS stack.

## Agent discipline

Implementation plans list suggested commits. One feature folder per commit series. The repo must build after each commit.
