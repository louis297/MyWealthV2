---
title: Future plans
status: note
language: en
created: 2026-11-10
updated: 2026-11-15
related:
  - README.md
  - function-plan.md
  - features/portfolios.md
  - features/fx-rates.md
  - features/internal-transfers.md
  - api-design.md
  - features/fx-conversions.md
  - deployment.md
  - features/tenants.md
  - adr/0016-bff-for-first-party-portals.md
  - features/identity-auth.md
  - adviser-portal/adviser-portal.md
---

# Future plans

A reminder list of large directions the product may take. This file is not a build contract, not a backlog, and not a priority order. Do not implement, create tables, or create routes from anything here. Each item needs its own discussion and Feature Spec before any work starts.

Names used here are working descriptions. They are not glossary terms until a spec adopts them.

---

## 1. Automatic rebalance

Rebalance assigned accounts without an Adviser clicking Rebalance.

Today ([features/portfolios.md](features/portfolios.md)): rebalance runs on assign, on a weight change, and on a manual call. Scheduled and drift-triggered rebalance are out of scope there.

Questions to settle when this opens:

- Trigger: a schedule (for example monthly), drift beyond a threshold, new cash above the residual, or a mix.
- Who configures it: per portfolio, per assignment, or per tenant.
- Guard rails: minimum interval between runs, what happens when prices or FX rates are missing, how failures are reported to the Adviser.
- Whether the Customer is told before or after a run.

---

## 2. FX conversion trades

Moved to a Feature Spec: [features/fx-conversions.md](features/fx-conversions.md) (`draft`). Decided in discussion (2026-11-10):

- An Account keeps one cash currency. A conversion is one Transaction across two Accounts of different currencies of the same Customer. Moving cash to another Customer in another currency is a conversion followed by an `InternalTransfer`; there is no cross-Customer conversion.
- The server prices the conversion from the cached reference rate and stores the rate used.
- Manual trades that add a holding require the instrument quote currency to equal the Account currency. Portfolio rebalance and manual `Sell` keep the automatic FX of buy-sell R12 and store the rate. That change is an amendment to buy-sell and portfolios: [features/cross-currency-trades.md](features/cross-currency-trades.md) (`draft`).

---

## 3. Type-specific transaction create routes

Move existing types from `POST /transactions` to their own `POST /transactions/{action}` routes, so each create body is small and has no type-dependent field rules.

Decided (2026-11-10): every new `TransactionType` gets its own `POST /transactions/{action}` route ([api-design.md](api-design.md) §7.7). `InternalTransfer` (`/transactions/transfer`) and `FxConversion` (`/transactions/fx`) are the first two. Existing types move off `POST /transactions` step by step, each move in its own spec amendment.

Questions to settle when a move opens:

- Which types to move, and in what groups (for example buy / sell together, corporate actions together).
- Transition: how long both routes accept a moved type, given the rule of one create route per type.
- Portal changes and the BFF allow-list.
- Whether `POST /transactions` disappears once every type has moved.

---

## 4. Multi-currency cash in one Account

Hold cash in more than one currency inside one Account, for example keeping a USD dividend in USD inside an NZD brokerage Account.

Today: an Account has one cash currency (ADR 0011 §6, posting R8). FX happens through a conversion between two Accounts ([features/fx-conversions.md](features/fx-conversions.md)) or through the automatic FX of portfolio rebalance and `Sell`.

Considered and not chosen (2026-11-10): a portfolio as a group of hidden per-currency sub-accounts. It would need an Account group, automatic opening of sub-accounts, a multi-Account rebalance with FX legs, and a decision on every list, report and net worth view about hidden Accounts. If multi-currency cash is wanted, change the Account model directly (cash legs carry their own currency, balance per `(Account, Currency)`), starting with an amendment to ADR 0011.

Questions to settle when this opens:

- What `Account.Currency` means once cash can be in several currencies (base or reporting currency).
- Close rule, net worth breakdown and Opening per currency.
- Whether automatic FX in rebalance and `Sell` is replaced by holding the foreign cash.
- Dividends in a foreign quote currency (a later Dividend spec needs either this or automatic FX).

---

## 5. Demo data for visitors

Let visitors of the public showcase try the portals without an invitation.

Today ([deployment.md](deployment.md) §13 item 7): the first deploy has no demo data. Production holds only the smoke tenant, whose passwords are secret. [features/tenants.md](features/tenants.md) R17 allows seeding only in Development.

Specified and accepted 2026-11-14: [features/demo-tenant-seed.md](features/demo-tenant-seed.md). The operator command `webapi --seed-demo` creates the demo tenant `demo` (reporting `USD`), its 18-instrument catalog and its two report grants in any environment, and amends tenants R17. It seeds no people. The rest of this section is still open.

Direction chosen in discussion (2026-11-12), not yet a spec: a writable demo tenant with shared Adviser and Customer accounts, reset to a known state every night by a `worker` job. A read-only demo would need a new read-only role and a second authorization model, so it was not chosen.

Questions to settle when this opens:

- Whether the nightly reset reuses the demo seed, and how the shared logins are created (the tenant itself is settled by demo-tenant-seed).
- What the reset does: delete and recreate the demo tenant's rows, or restore a fixed data set, and how it avoids clashing with outbox events still in flight.
- Demo accounts must not change their own password or email, or one visitor locks the others out. This needs a rule that the change-password and profile routes reject demo accounts.
- What else a visitor must not do: invite people (email is a no-op anyway), disable the demo tenant's users, or create data large enough to slow the VPS.
- How the credentials are shown: on the hosted login page, on the portal landing page, or in the repository README.

---

## 6. Adviser mobile app

A native mobile app for Advisers, built with React Native (Expo), next to the Adviser Portal web app.

Today: the Adviser Portal is a browser SPA (ADR 0003) behind `bff-adviser-portal` (ADR 0016). It has no mobile layout acceptance ([adviser-portal/frontend-implementation-notes.md](adviser-portal/frontend-implementation-notes.md) 13.24).

Direction from discussion (2026-11-15), not planned for now:

- Keep the web Adviser Portal. The app adds a second front end; it does not replace the SPA. The portal is desktop-shaped (tables, sidebar, dialogs, portfolio weights, report schedules), so the app starts with a read-heavy subset: Dashboard, Customers, net worth, viewing reports. Admin flows stay on the web.
- Keep it in this repository, for example `src/AdviserMobile/`, with npm workspaces and a shared package for the non-UI TypeScript: the RTK Query `*Api.ts` slices, shared types, `problem.ts`, `roles.ts`, `cron.ts`. The design docs stay the single source of truth, and an API change and its app change land in one change. The same package can end the `components/ui` duplication between the Adviser and Customer portals. Split into its own repository only if a separate team or release owner appears.
- Rough size: `webapi` needs almost no change (it already validates Bearer through JWKS, and ADR 0016 lets non-portal callers reach it directly). `identity` needs a new client. The UI (about 7.4k lines of `.tsx`) and most component tests are a rewrite, because shadcn, Base UI, Tailwind, lucide-react and React Router are DOM-only. About 1.3k lines of non-UI TypeScript are reusable; only the base query changes.

Sign-in shape (needs a new ADR next to ADR 0016):

- A new **public** OIDC client (working name `adviser-mobile`), authorization code + PKCE, no client secret (RFC 8252).
- Hosted login on `identity` opens in the system browser (ASWebAuthenticationSession / Custom Tabs). The redirect is a custom scheme or a universal link.
- Tokens are kept in the OS secure store (Keychain / Keystore). The app calls `webapi` directly with Bearer and refreshes on 401. There is no BFF for the app.
- Code touched: `OpenIddictSeeder` (it only seeds confidential clients today), `ClientCookieSchemes`, `ClientRoleAllowList`, logout and revocation.

Questions to settle when this opens:

- Whether a responsive web portal or a PWA is enough. A native app is worth it only for push notifications, biometric unlock, offline use or a store listing.
- API compatibility: old app versions stay installed, so breaking `webapi` changes need a support window or versioning. This affects §3 (moving types off `POST /transactions`).
- Which pages the first release has, and which roles may use the client.
- Report download: today it uses the BFF cookie and a browser `Blob`. The app needs a Bearer download and the system share sheet.
- Local development: a device or emulator reaching `identity` over https.
- Build and release: EAS builds, signing, store review, over-the-air updates, a separate CI path filter, and the domain files for universal links ([deployment.md](deployment.md)).
