---
title: "ADR 0020: Custody mode on the Account"
status: accepted
language: en
date: 2026-11-17
updated: 2026-11-17
related:
  - 0011-ledger-cash-holdings-reversal.md
  - 0005-shared-database-tenantid-isolation.md
  - ../features/account-custody-mode.md
  - ../features/custody-modes.md
  - ../features/accounts.md
  - ../glossary.md
  - ../future-plans.md
---

# ADR 0020: Custody mode on the Account

Status: accepted (2026-11-17)

## Context

The ledger keeps two different kinds of account in one book, and nothing tells them apart.

- **Assets the platform holds and trades.** The platform's ledger is the authoritative record. Prices, costs, FX rates and fees have to come from the platform, and every entry must later agree with a bank or a custodian.
- **Assets held somewhere else**: a deposit at a bank, shares at another broker. The platform only records what happened there. The numbers come from the Adviser or the Customer.

`Account.Type` (Bank, Cash, Brokerage, Other) does not separate them. It is a capability gate: may the account hold instruments, and is it an asset or a liability. The manual transaction types (`Opening`, `OpeningHoldings`, `StockIn`, `StockOut`, `Split`, a hand-entered `Interest`) fit the second kind. The later work (server-priced `Buy` / `Sell`, trade fee schedules, FX conversions, portfolio rebalance) fits the first kind. Today both sets of rules apply to every account, so neither kind is right: an account the platform trades can still have its holdings typed in by hand, and an account that mirrors another broker is priced by the server at a price that broker never used.

A firm needs both kinds for the same Customer. The split has to exist before the platform-side flows (deposits matched to a bank, orders, corporate actions) are built, because each of those flows replaces a manual type on the first kind of account only.

Decided in discussion on 2026-11-16.

## Decision

Every Account has a **custody mode**, set when the account is opened and never changed.

| Value | Meaning | Where the numbers come from |
| --- | --- | --- |
| `External` | The assets are held outside the platform. The platform records what happened elsewhere. | The caller |
| `Custodial` | The assets are held by the platform and trades are executed by the platform. The platform's ledger is the authoritative record. | The server |

1. **The mode is on the Account, not on the Tenant.** A Tenant gets at most one setting: which custody modes its accounts may be opened with. That setting is a check when an account is opened, not a branch through the system.
2. **The mode is immutable after open.** The history of the two kinds of account has a different nature and cannot change meaning halfway. Moving outside assets onto the platform is a deposit or an in-kind transfer into a Custodial account, not a change of mode.
3. **The mode is independent of `Account.Type`.** `Type` answers "may this account hold instruments". Custody mode answers "where do the numbers come from, and which controls apply". Direction: a Custodial account is a `Brokerage` account (cash plus held positions); an External account may be any openable type.
4. **One ledger.** `Transaction`, legs, the `Holdings` projection, net worth and reports are shared by both modes. The mode decides only two things: which writes an account accepts, and which controls apply to them.
5. **Direction for Custodial accounts.** These rules are not shipped by this ADR. They become rules in the Feature Spec that lets a Custodial account be opened:
   - Every leg account of one Transaction has the same custody mode. Custodial cash is real money in a client money account; External cash is a mirror. One Transaction that moves both cannot be reconciled with the bank.
   - On a Custodial account the server decides the price, cost, FX rate and fees. The caller states an intent: what to buy and how much. Whether the server also sets the booking time is decided in that spec.
   - Manual types that write a position or a balance directly are refused on a Custodial account, each until a platform-side flow replaces it.
6. **Existing accounts are `External`.** Their history contains manual types, which are valid on an External account. Custodial accounts start with a clean history.
7. **The name is custody mode.** "Book type" was not used, because Book already means an Adviser's set of assigned Customers ([glossary.md](../glossary.md)).

The first slice adds the attribute only: [features/account-custody-mode.md](../features/account-custody-mode.md). In that slice `Custodial` is a reserved value that cannot be selected, the same way the `Property` and `Credit` account types are reserved.

## Alternatives considered

| Option | Why not |
| --- | --- |
| A platform type on the Tenant (a custodial firm or a record-keeping firm) | The same firm needs both: a Customer has assets on the platform and assets elsewhere. A firm that starts with records and later holds assets itself is a common path, and a tenant type could not change. A tenant type is global context, so the branch would reach every handler and every test. |
| New `Account.Type` values, or giving the existing types a custody meaning | `Type` already answers a different question. One enum would have to carry the product of both. |
| A second ledger (other tables and routes) for outside assets | Two write models, and every read model (holdings, net worth, reports) would have to merge them. The existing rows are already in the one ledger. A simpler valuation-only record for property and loans stays open; it does not replace External accounts that have transactions. |
| A mutable mode | An account's past entries would change meaning: entries typed in by hand would sit in a history that claims to be the platform's own record. |
| Default `External` when the caller omits the mode | The mode selects the whole rule set of the account and cannot be changed later. It must be a stated choice. |
| Ship the Custodial rules first and add the attribute afterwards | The rules have nothing to attach to. With the attribute in place, each rule is one small refusal on a Custodial account that a failing test can show. |

## Consequences

- One enum and one column. Script `0031` back-fills every existing account to `External`.
- `POST /accounts` gains a required field. This breaks every caller that does not send it, including the functional test helpers.
- Each later rule has the form "a Custodial account refuses X" or "an External account takes X from the caller". The rule matrix lives in one place.
- Platform-side flows (deposits and withdrawals, orders, corporate actions, interest) replace the manual types on Custodial accounts one at a time. External accounts keep the manual types.
- Fees that depend on assets under the platform's control (advisory fee, platform billing in [future-plans.md](../future-plans.md)) can use Custodial accounts as their base and can only be deducted from Custodial cash.
- Net worth still counts both modes. Showing them as two groups is a later read-model change.
- Still open: a valuation-only model for property and loans, and what happens to the `Other`, `Property` and `Credit` account types then. The legal structure of the holder (individual, joint, trust, company) is another dimension and belongs to a later KYC domain.

## Links

- Feature spec: [features/account-custody-mode.md](../features/account-custody-mode.md)
- Rules for Custodial accounts (item 5): [features/custody-modes.md](../features/custody-modes.md), accepted 2026-11-17. It decides that the server sets the booking time on a Custodial account, the point item 5 left to that spec. This ADR does not change.
- Related ADRs: 0011 (account container, item 6), 0005 (tenancy)
