---
title: Future plans
status: note
language: en
created: 2026-11-10
updated: 2026-11-23
related:
  - README.md
  - function-plan.md
  - features/posting.md
  - features/buy-sell.md
  - features/leg-accounts.md
  - features/instrument-prices.md
  - features/trade-fees.md
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
  - features/account-custody-mode.md
  - features/custody-modes.md
  - adr/0020-account-custody-mode.md
  - features/tenant-custody-modes.md
  - features/external-caller-numbers.md
  - features/custody-mode-grouping.md
  - custodial-roadmap.md
---

# Future plans

A reminder list of large directions the product may take. This file is not a build contract and not a backlog. Only §1 and §9 give an order, and that order is for discussion, not implementation. Do not implement, create tables, or create routes from anything here. Each item needs its own discussion and Feature Spec before any work starts.

Names used here are working descriptions. They are not glossary terms until a spec adopts them.

---

## 1. Business domain roadmap

Decided in discussion (2026-11-15): pause new technical infrastructure and move to the business side of the wealth platform. MyWealthV2 stays a learning project. The goal is finance domain knowledge for a developer, shown by reports that follow real rules, not a release.

Scope of the direction:

- **Jurisdiction:** New Zealand. Rule sources are IRD guidance, the FMA and the GIPS standards. Each tax rule is checked against the primary source when its spec is written. This note does not settle any rate or threshold.
- **Focus:** algorithmic topics first, where published worked examples can become tests. Content-type reports (what a client statement must contain, adviser conduct duties) come later.
- **Not the main line:** a general ledger. A one-off exercise that maps existing postings to debit and credit entries and a trial balance may check that the ledger is consistent. It does not add a GL module.

Fees are split into two topics at different points of the order:

- **Transaction fee** (trade fee in the spec): an extra cash leg on a Transaction (brokerage on a trade, FX spread, transfer fee). It only depends on posting.
- **Advisory fee:** computed over a period from account value, then posted as its own Transaction. It depends on historical valuation.

Order. Each step depends only on earlier steps:

| Step | Topic | Why here | Depends on |
| --- | --- | --- | --- |
| 1 | Transaction fee leg ([features/trade-fees.md](features/trade-fees.md), `accepted`) | Changes the posting leg shape. Income events add similar deduction legs, so the shape is settled first. Closes the fee work deferred in buy-sell, fx-conversions, internal-transfers and leg-accounts. | posting, buy-sell |
| 2 | Income events | Dividend and interest, with imputation credit, RWT and foreign withholding tax. Every tax report needs them. Foreign-currency dividends need an answer from §5 or automatic FX. The `Tax` cash leg with its tax detail and the Customer tax profile exist since [features/interest-run.md](features/interest-run.md) (accepted 2026-11-22), which posts interest on Custodial accounts; dividends reuse them. Withholding on a hand-entered `Interest` of an External account stays here | Step 1 |
| 3 | Historical valuation | Prices and FX rates as of a date, and holding snapshots. Every period report needs them. Closes the open daily snapshots in [function-plan.md](function-plan.md) §5.4. | instrument-prices, fx-rates |
| 4 | Advisory fee | Tiered or blended rates, daily accrual, pro rata for part periods, billing in advance or in arrears, and what happens when cash is short. | Step 3 |
| 5 | Performance report | Time-weighted return (TWR) and money-weighted return (MWR), gross and net of fees. | Steps 3, 4 |
| 6 | FIF tax report | Main goal. FDR and CV methods, the de minimis test, the quick sale adjustment, exempt ASX-listed shares, and the NZ tax year (1 April to 31 March). FIF reports published by NZ investment platforms can serve as test oracles. | Steps 2, 3 |

Working method: work each topic out by hand in a spreadsheet with a real or synthetic data set before its spec is written. The worked numbers become the tests.

Questions to settle when step 1 opens:

- Which leg carries the fee, and in which currency.
- Whether a trade fee adds to the holding cost.
- How a reversal treats the fee.

To check against IRD when the related step opens:

- Whether brokerage is part of the cost base, and how the CV method treats it (steps 1, 6).
- GST on advisory fees (step 4).
- Whether individuals can deduct advisory fees (steps 4, 6).

---

## 2. Automatic rebalance

Rebalance assigned accounts without an Adviser clicking Rebalance.

Today ([features/portfolios.md](features/portfolios.md)): rebalance runs on assign, on a weight change, and on a manual call. Scheduled and drift-triggered rebalance are out of scope there.

Questions to settle when this opens:

- Trigger: a schedule (for example monthly), drift beyond a threshold, new cash above the residual, or a mix.
- Who configures it: per portfolio, per assignment, or per tenant.
- Guard rails: minimum interval between runs, what happens when prices or FX rates are missing, how failures are reported to the Adviser.
- Whether the Customer is told before or after a run.

---

## 3. FX conversion trades

Moved to a Feature Spec: [features/fx-conversions.md](features/fx-conversions.md) (`draft`). Decided in discussion (2026-11-10):

- An Account keeps one cash currency. A conversion is one Transaction across two Accounts of different currencies of the same Customer. Moving cash to another Customer in another currency is a conversion followed by an `InternalTransfer`; there is no cross-Customer conversion.
- The server prices the conversion from the cached reference rate and stores the rate used.
- Manual trades that add a holding require the instrument quote currency to equal the Account currency. Portfolio rebalance and manual `Sell` keep the automatic FX of buy-sell R12 and store the rate. That change is an amendment to buy-sell and portfolios: [features/cross-currency-trades.md](features/cross-currency-trades.md) (`draft`).

---

## 4. Type-specific transaction create routes

Move existing types from `POST /transactions` to their own `POST /transactions/{action}` routes, so each create body is small and has no type-dependent field rules.

Decided (2026-11-10): every new `TransactionType` gets its own `POST /transactions/{action}` route ([api-design.md](api-design.md) §7.7). `InternalTransfer` (`/transactions/transfer`) and `FxConversion` (`/transactions/fx`) are the first two. Existing types move off `POST /transactions` step by step, each move in its own spec amendment.

Questions to settle when a move opens:

- Which types to move, and in what groups (for example buy / sell together, corporate actions together).
- Transition: how long both routes accept a moved type, given the rule of one create route per type.
- Portal changes and the BFF allow-list.
- Whether `POST /transactions` disappears once every type has moved.

---

## 5. Multi-currency cash in one Account

Hold cash in more than one currency inside one Account, for example keeping a USD dividend in USD inside an NZD brokerage Account.

Today: an Account has one cash currency (ADR 0011 §6, posting R8). FX happens through a conversion between two Accounts ([features/fx-conversions.md](features/fx-conversions.md)) or through the automatic FX of portfolio rebalance and `Sell`.

Considered and not chosen (2026-11-10): a portfolio as a group of hidden per-currency sub-accounts. It would need an Account group, automatic opening of sub-accounts, a multi-Account rebalance with FX legs, and a decision on every list, report and net worth view about hidden Accounts. If multi-currency cash is wanted, change the Account model directly (cash legs carry their own currency, balance per `(Account, Currency)`), starting with an amendment to ADR 0011.

Questions to settle when this opens:

- What `Account.Currency` means once cash can be in several currencies (base or reporting currency).
- Close rule, net worth breakdown and Opening per currency.
- Whether automatic FX in rebalance and `Sell` is replaced by holding the foreign cash.
- Dividends in a foreign quote currency (a later Dividend spec needs either this or automatic FX).

---

## 6. Demo data for visitors

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

## 7. Adviser mobile app

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
- API compatibility: old app versions stay installed, so breaking `webapi` changes need a support window or versioning. This affects §4 (moving types off `POST /transactions`).
- Which pages the first release has, and which roles may use the client.
- Report download: today it uses the BFF cookie and a browser `Blob`. The app needs a Bearer download and the system share sheet.
- Local development: a device or emulator reaching `identity` over https.
- Build and release: EAS builds, signing, store review, over-the-air updates, a separate CI path filter, and the domain files for universal links ([deployment.md](deployment.md)).

---

## 8. Platform billing

The platform charges each tenant for using MyWealthV2.

Recorded in discussion (2026-11-15), not planned for now. This is different from the fees in §1. Those are booked in a Customer's ledger. Platform billing is the platform company's own receivable from a tenant: a business-to-business invoice that never touches a Customer ledger.

Questions to settle when this opens:

- Pricing model: a subscription, a charge per open Account, basis points on the assets the tenant manages, or a mix.
- Invoices: numbering, GST invoice content, credit notes.
- Where the platform's own books live. This is where double-entry accounting (general ledger, receivables, revenue recognition) is used, which §1 keeps out of the Customer ledger.
- Whether a wrap-style platform administration fee, deducted from Customer cash, is also wanted. That one is a standalone fee in the Customer ledger (§1 step 4), not platform billing.

---

## 9. Custody modes

Tell apart the accounts the platform holds and trades from the accounts that only record assets held elsewhere.

Decided in discussion (2026-11-16), recorded in [ADR 0020](adr/0020-account-custody-mode.md) (accepted 2026-11-17): every Account has a custody mode, `External` or `Custodial`, on the Account and not on the Tenant. This comes before any work that turns hand-entered transactions into platform-side flows, because each such flow replaces a manual type on Custodial accounts only.

Slices. All five have a spec. Slices 3 to 5 do not depend on each other:

| Slice | Content | Behaviour change |
| --- | --- | --- |
| 1 | The attribute on the Account. Only `External` can be opened; `Custodial` is a reserved value. [features/account-custody-mode.md](features/account-custody-mode.md) (accepted, landed 2026-11-17) | None |
| 2 | Custodial accounts can be opened, with all of their rules in one spec: which account types they may have, which manual transaction types they refuse, the same custody mode on every leg account of a Transaction, who sets `bookedAt`, and portfolio assignment and rebalance by custody mode. [features/custody-modes.md](features/custody-modes.md) (accepted, landed 2026-11-17) | Most of the change |
| 3 | A tenant setting for which custody modes may be opened, checked when an account is opened. [features/tenant-custody-modes.md](features/tenant-custody-modes.md) (accepted, landed 2026-11-18) | Small |
| 4 | External accounts take the caller's numbers: executed price and fee on `Buy` / `Sell`, both amounts on an `FxConversion`. Server pricing, trade fee schedules and the reference rate then apply to Custodial accounts only. [features/external-caller-numbers.md](features/external-caller-numbers.md) (accepted, landed 2026-11-18) | Medium |
| 5 | Read side: net worth, the two reports and the portal pages show the two modes as two groups; the Custodial group is the funds under management, and the Customer is told that External figures are not checked by the platform. [features/custody-mode-grouping.md](features/custody-mode-grouping.md) (accepted 2026-11-18, landed 2026-11-19) | Read only |

After slice 2, some writes on a Custodial account are still entered by hand or are missing. Each later item replaces one of them. The plan for these items, their order, the decisions they need and the simulator is [custodial-roadmap.md](custodial-roadmap.md) (`note`, 2026-11-19):

- Deposits and withdrawals matched to a bank statement, replacing hand-entered `TransferIn` / `TransferOut`.
- An interest job and income events (§1 step 2), replacing hand-entered `Interest`. The interest job is [features/interest-run.md](features/interest-run.md) (accepted 2026-11-22).
- Corporate action events, replacing hand-entered `Split` / `Bonus` / `Scrip`. Splits, consolidations and bonus issues are [features/corporate-actions.md](features/corporate-actions.md) (accepted 2026-11-22); `Scrip` and dividends come later.
- In-kind transfer and migration batches, replacing `Opening`, `OpeningHoldings`, `StockIn` and `StockOut`.
- Orders, replacing the immediate `Buy` / `Sell`. Orders, fills and settlement are [features/orders.md](features/orders.md) (accepted 2026-11-23); settlement cash through the client money account and broken trades are [features/trade-settlement.md](features/trade-settlement.md) (accepted 2026-11-23); rebalance through orders comes later.
- A controlled manual adjustment as the only hand-entered write.
- A simulator of the outside parties (a bank first, then a broker) to drive these flows without real connections.
- Rebuilding an account's position by replaying its transactions in order. Deferred until after the items above are under way.

Related: the advisory fee (§1 step 4) and platform billing (§8) can then use Custodial accounts as their base.

Questions that the slice 2 spec answers. Its answers were confirmed when that spec was accepted on 2026-11-17 ([features/custody-modes.md](features/custody-modes.md) §11):

- Whether the server sets `bookedAt` on a Custodial account. Decided: yes. It stops backdating without a replay, but a hand-entered deposit cannot be dated yesterday, and history can only be built by the seed.
- What happens to existing portfolio assignments. Decided: a script removes them, and every existing account stays External. The other option turns accounts with an Active assignment into Custodial ones and keeps hand-entered history in them.
- Whether to accept that a Custodial holding cannot record a split until corporate action events exist. Decided: yes, recorded in [known-issues/custodial-account-gaps.md](known-issues/custodial-account-gaps.md).
- Whether `CloseOut` is refused on a Custodial account. Decided: no, it stays until an account closure flow exists, because it is the only type that can clear a balance smaller than the currency's minor unit.

Still open:

- Whether a valuation-only record for property and loans replaces the `Other`, `Property` and `Credit` account types.
