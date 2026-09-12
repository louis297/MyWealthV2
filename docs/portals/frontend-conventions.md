---
title: Frontend conventions
status: draft
language: en
created: 2026-09-12
updated: 2026-09-12
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
├── features/     session (this slice); later customers, advisers, profile
├── shared/       api, components, hooks, types, utils
├── layouts/
├── App.tsx
└── main.tsx
```

Feature code lives under `features/<name>/`. Do not introduce a global `pages/` tree.

## Naming

- Components PascalCase; file name matches the component.
- One async style for the repo: RTK Query for REST; a small session slice for tokens and the current user.

## Session

- Access token, refresh token, and the last `/users/me` payload live in Redux. Persist tokens in `sessionStorage` (not `localStorage`).
- Resource ids in the UI are PublicId values only.
- Do not put a password field on this app.

## API

- One client in `shared/api`. Base URL from Aspire / env (`webapi`).
- `Authorization: Bearer <access>`.
- On 401, try refresh once against identity `/connect/token`; if that fails, clear the session and restart authorize.
- Do not treat a cross-tenant 404 as 403.

## Style

Tailwind utilities only. No second CSS-in-JS stack.

## Agent discipline

Implementation plans list suggested commits. One feature folder per commit series. The repo must build after each commit.
