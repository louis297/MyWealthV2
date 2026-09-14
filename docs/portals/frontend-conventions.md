---
title: Frontend conventions
status: review
language: en
created: 2026-09-12
updated: 2026-09-14
related:
  - README.md
  - adviser-portal.md
  - ../adr/0003-react-redux-typescript-vite-tailwind-frontend.md
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
- One async style for the repo: RTK Query for REST; a small session slice for tokens and the current user.

## Session

- Access token, refresh token, and the last `/users/me` payload live in Redux. Persist tokens in `sessionStorage` (not `localStorage`).
- After `me`, if `tenantCode` is set, load `GET /tenants/by-code/{tenantCode}` into the store for the shell Name. Do not expect `tenantName` on `/users/me`.
- Resource ids in the UI are PublicId values only.
- Do not put a password field on this app that **issues tokens**. Create-person and profile-change password fields are allowed.

## API

- One client in `shared/api`. Base URL from Aspire / env (`webapi`).
- `Authorization: Bearer <access>`.
- On 401, try refresh once against identity `/connect/token`; if that fails, clear the session and restart authorize unless sign-out is already in progress.
- `/callback` redeems a given authorization code once. Keep React StrictMode.
- Sign out revokes refresh at `/connect/revocation`, then identity `/connect/logout`. No portal `/auth/logout`.
- Do not treat a cross-tenant 404 as 403.

## Style

Tailwind utilities only. No second CSS-in-JS stack.

## Agent discipline

Implementation plans list suggested commits. One feature folder per commit series. The repo must build after each commit.
