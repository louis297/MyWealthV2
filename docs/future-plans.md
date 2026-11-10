---
title: Future plans
status: note
language: en
created: 2026-11-10
updated: 2026-11-10
related:
  - README.md
  - function-plan.md
  - features/portfolios.md
  - features/fx-rates.md
  - features/internal-transfers.md
  - api-design.md
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

Exchange cash from one currency to another, for example NZD to USD before buying a US instrument.

Today: an Account has one cash currency, and a cash leg must be in its Account's currency. FX rates are cached for valuation ([features/fx-rates.md](features/fx-rates.md)). There is no transaction that converts cash between currencies.

Questions to settle when this opens:

- Shape: a conversion between two Accounts of different currencies (close to a cross-currency transfer), or multi-currency cash inside one Account. The second changes the Account model.
- Rate: the cached reference rate, a rate entered by the user, or a quoted rate. Whether the rate used is stored on the transaction.
- Spread or fee, and how it is shown.
- Reversal: reverse at the original rate.
- Relation to the deferred cross-currency transfer; they may be one feature.

---

## 3. Type-specific transaction create routes

Move existing types from `POST /transactions` to their own `POST /transactions/{action}` routes, so each create body is small and has no type-dependent field rules.

Today ([api-design.md](api-design.md) §7.7): the convention exists, and only `InternalTransfer` uses it (`POST /transactions/transfer`, [features/internal-transfers.md](features/internal-transfers.md)). Every other type still creates through `POST /transactions` with a `type` field.

Questions to settle when this opens:

- Which types to move, and in what groups (for example buy / sell together, corporate actions together).
- Transition: how long both routes accept a moved type, given the rule of one create route per type.
- Portal changes and the BFF allow-list.
- Whether `POST /transactions` disappears once every type has moved.
