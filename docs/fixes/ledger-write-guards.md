---
title: Fix - Reversal order, cash decimal places and future bookedAt
status: accepted
language: en
created: 2026-11-16
updated: 2026-11-16
related:
  - ../features/posting.md
  - ../features/holdings.md
  - ../features/buy-sell.md
  - ../features/split.md
  - ../features/internal-transfers.md
  - ../features/fx-conversions.md
  - ../features/trade-fees.md
  - ../features/leg-accounts.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../api-design.md
  - ../domain-model.md
  - ../glossary.md
  - utc-instants.md
---

# Fix: Reversal order, cash decimal places and future bookedAt

Status: accepted (2026-11-16). Build contract. Not landed.

Three guards on ledger writes in `webapi`. No script, no new route, no new policy, no new problem code, no portal change.

## Problem

Found by a design review on 2026-11-16. Each case was reproduced with a functional test against `master` `0a9690e`. The tests were a spike and were not committed.

1. **Reversing an old security transaction corrupts the holding cost.** The `Holdings` projection is the sum of the security legs' quantity and cost per `(Account, Instrument)` ([holdings](../features/holdings.md) R14). A Reversal's legs are the arithmetic opposite of the original legs ([buy-sell](../features/buy-sell.md) R16). Quantity can be added and subtracted like this. Cost cannot: the cost that a `Sell` or `StockOut` releases depends on the average at that moment ([buy-sell](../features/buy-sell.md) R15). Reverse checks only that quantity and cash stay `≥ 0`.

   | Step | Holding after the step |
   | --- | --- |
   | `StockIn` 100, cost 1,000 | 100 / 1,000 |
   | `StockIn` 100, cost 2,000 | 200 / 3,000 |
   | `StockOut` 100 (releases 1,500) | 100 / 1,500 |
   | Reverse the first `StockIn` → 201 | no row. The legs still sum to cost 500 |
   | `StockIn` 10, cost 100 | 10 / **600**. Expected 10 / 100 |

   With a second `StockIn` of cost 100 instead of 2,000, the legs sum to cost −450 after the reverse. A later `StockIn` of 10 with cost 100 then fails with 400 `Holding cost cannot be negative.` The same happens to every add on that holding whose cost does not bring the sum back to zero or more. `Buy` / `Sell` use the same `Reverse` and the same projection.

2. **A cash amount is stored with fewer decimals than the caller sent.** `POST /transactions` with `TransferIn` and `amount` `10.12345` on a USD Account returns 201. The cash balance reads `10.1235`. The request is not refused, and USD has 2 decimal places. [fx-conversions](../features/fx-conversions.md) R11 already refuses this for a conversion. The other cash types do not.

3. **`bookedAt` accepts any instant.** [posting](../features/posting.md) R24 only requires an instant. A `TransferIn` booked in 2035 returns 201, and a `TransferOut` booked in 2021 can spend it. A transaction dated in the future is not a booked event. It also forces other rules to plan for it: the cut-off date in [trade-fees](../features/trade-fees.md) R9 has to look at the latest trade date as well as today.

Booking a transaction **before** an earlier one (backdating) is not fixed here. A `StockOut` booked in 2020 against a holding that a `StockIn` created in 2026 still returns 201. Refusing that needs the balance as of a date, which the ledger cannot compute yet. See Out of scope.

## Decision

1. **A Transaction with a security leg can be reversed only while it is the latest security activity on each of its holdings.** For every security leg of the Transaction, no live later security leg may exist on the same `(Account, Instrument)`.
   - **Later** means posting order: the other leg's Transaction was written after this one (`Transactions.Id` is greater). `bookedAt` is not used.
   - **Live** means the other leg's Transaction is not a `Reversal` and has not been reversed (`ReversedByTransactionId` is null). A reversed Transaction and its Reversal cancel each other exactly, because the Reversal was itself only allowed while its original was the latest security activity.
   - One leg that fails refuses the whole reverse. There is no partial reverse ([leg-accounts](../features/leg-accounts.md) R12).
   - A Transaction with cash legs only is not affected. Cash is the plain sum of the legs.
   - To reverse an older security transaction, reverse the later ones first, newest first.
2. **The refusal is a 400 with ordinary `errors`**, the same shape as "already reversed" ([posting](../features/posting.md) §8.2). Message: `A later transaction changed this holding. Reverse the later transactions first.` No problem code. Nothing is written.
3. **The order check runs inside the reverse command, after every leg account is locked** ([leg-accounts](../features/leg-accounts.md) R11) and after the existing checks for 404, Closed account, Disabled Customer, the portfolio trade lock and already reversed. When this rule and a `≥ 0` rule for quantity or cash would both refuse, the response is a 400 either way; which of the two messages it carries is not fixed.
4. **The caller's cash amount must fit the Account currency's `DecimalPlaces`.** This covers `amount` on `POST /transactions` for `TransferIn`, `TransferOut`, `Interest` and cash `Opening`, and `amount` on `POST /transactions/transfer`. More decimal places → 400 `errors.Amount`, nothing written. The comparison is by value, as in [fx-conversions](../features/fx-conversions.md) R11: `10.10` and `10.100` both fit a currency with 2 places. JPY has 0 places, so `100.5` JPY is refused.
5. **`CloseOut` is exempt from Decision 4.** Its amount is not chosen by the caller: it must equal the current cash `SUM` ([posting](../features/posting.md) R17). A `Buy` / `Sell` cash leg is rounded to 4 decimals today ([buy-sell](../features/buy-sell.md) R12), so a balance can hold a part smaller than the currency's minor unit. `CloseOut` is the only way to bring such a balance to zero so that the Account can close.
6. **`bookedAt` must not be later than the server clock plus 5 minutes.** This covers `POST /transactions`, `POST /transactions/transfer`, `POST /transactions/fx`, and `POST /transactions/fx/quote` when the body carries `bookedAt`. Later → 400 `errors.BookedAt` with the message `bookedAt cannot be in the future.` Nothing is written. The 5 minutes allow for a browser clock that runs ahead of the server.
7. **Decisions 4 and 6 are request checks in Application.** Decision 6 is a validator rule next to the existing required rule, so it runs before the idempotency lookup. Decision 4 needs the Account's currency, so the handler checks it after it has loaded the Account. A refused request stores no idempotency record in either case. The `Transaction` factories do not change: the test seed, rebalance and Reversal build Transactions directly, and none of them takes a caller's `bookedAt` or cash amount. Rebalance and Reversal use the server clock already.
8. **Posted rows are not changed.** No script and no back-fill. A cash leg that already holds more decimals, a Transaction that is already dated in the future, and a holding whose legs already sum to a wrong cost stay as they are.
9. **[trade-fees](../features/trade-fees.md) R9 does not change.** The cut-off date is still the later of today and the latest trade date. After this fix the latest trade date can be after today only inside the 5 minute allowance around UTC midnight, or for a row posted before the fix.

### Where each rule lives

| Rule | Where |
| --- | --- |
| Find a live later security leg for each of the original's `(Account, Instrument)` | `ReverseTransaction` handler, after the Account locks. One query on `TransactionSecurityLegs` joined to `Transactions` |
| Refuse the reverse | `Transaction.Reverse` takes the result as a parameter and throws `DomainException`, as it does for a negative quantity. Application passes facts; the Domain holds the invariant |
| Cash decimal places | The validators of `CreateTransaction` and `CreateInternalTransfer` cannot see the Account, so the handlers check after loading it, with `ICurrencyCatalog` and the same comparison as `FxConversionPricing.FitsDecimalPlaces`. The 400 is a `ValidationException` on `Amount` |
| Future `bookedAt` | One shared rule used by the validators of `CreateTransaction`, `CreateInternalTransfer`, `CreateFxConversion` and `GetFxConversionQuote`. It reads the clock from `TimeProvider`. The allowance is one named constant |

`webapi` already registers `TimeProvider`; `ReverseTransaction` uses it. `CreateTransaction` reads `DateTimeOffset.UtcNow` for its idempotency record today. That call may stay.

### Client pages

No portal change is required.

- Reverse: the transactions list and detail pages already show the sentence of a 400 on reverse. The Reverse button stays visible on a Transaction that is not the latest security activity; the server answers. A `reversible` field on the item is out of scope.
- Create forms: the 400s arrive as `errors.Amount` and `errors.BookedAt`, which the forms already show for other rules on those fields.

## Out of scope

- **Backdating.** A `bookedAt` in the past is still accepted without a check against the balance or the holding on that date. That check needs the position as of a date.
- Rebuilding a holding's cost by replaying its transactions in order. This fix refuses the reverse that would need it.
- Lots, realized gain, and any other change to the cost rule.
- A reason or memo on a Reversal.
- Decimal places of a caller's `cost` or `quantity` on `OpeningHoldings`, `StockIn` and the other security types. They follow the column scale ([holdings](../features/holdings.md) R13) and are not checked here.
- Rounding the `Buy` / `Sell` cash leg to the currency's `DecimalPlaces`. That is [cross-currency-trades](../features/cross-currency-trades.md) R9 (`draft`).
- Repairing holdings that an earlier out-of-order reverse already left with a wrong cost.
- A problem code for any of the three refusals.
- A tenant time zone. "The future" is measured against the server clock in UTC.
- A `max` attribute or other client-side check on the portal date input.

## Behaviours

The tree compiles after every commit. Each behaviour is a red commit and a green commit. The three behaviours do not depend on each other.

### B1. Reverse is refused when a later live security leg exists

Domain unit (`Transaction.Reverse`):

- Told that a later live security leg exists on one of its holdings → `DomainException` with the message in Decision 2.
- Told that none exists → the Reversal is built as today.
- A Transaction with cash legs only ignores the fact.

Functional:

- The case in Problem 1: `StockIn`, `StockIn`, `StockOut`, then reverse the first `StockIn` → 400 with the message in `errors`. The holding still reads 100 / 1,500 and the Transaction count is unchanged.
- The same three, reversed newest first (the `StockOut`, the second `StockIn`, the first `StockIn`) → 201 each time and no holding row at the end. A new `StockIn` of 10 with cost 100 then reads 10 / 100.
- A reversed pair is not live: `StockIn` A, `StockIn` B, reverse B, reverse A → 201 both times.
- `Buy` 100, `Buy` 100, `Sell` 100, then reverse the first `Buy` → 400 with the message in `errors`. Quantity and cash would both stay `≥ 0` here, so only this rule refuses. The existing `ReverseBuy_Returns400WhenQuantityWouldGoNegative` keeps passing.
- `OpeningHoldings` with two instruments, then a `StockIn` on one of them, then reverse the `OpeningHoldings` → 400. Nothing is written for either instrument.
- `Split`, then a `StockIn` on the same holding, then reverse the `Split` → 400.
- The same instrument on another Account of the same Customer does not count: `StockIn` on Account 1, `StockIn` on Account 2, reverse the first → 201.
- Cash only: two `TransferIn`, reverse the first → 201. Existing tests cover this and keep passing.

Existing reverse tests reverse the latest activity or already expect 400, so none of them changes.

### B2. A cash amount with too many decimal places → 400

Functional:

- `TransferIn` `10.123` on a USD Account → 400 with `errors.Amount`. No Transaction and no idempotency record is written.
- The same for `TransferOut`, `Interest` (either sign) and cash `Opening`.
- `TransferIn` `100.5` on a JPY Account → 400. `100` → 201.
- `TransferIn` `10.10` and `10.100` on a USD Account → 201 both times.
- `POST /transactions/transfer` with `amount` `10.123` between two USD Accounts → 400 with `errors.Amount`.
- `CloseOut` is exempt: an Account whose balance is `0.0034` (a cash leg inserted directly by the test) accepts `CloseOut` `-0.0034` and then closes.

### B3. A `bookedAt` in the future → 400

Before the red commit, one test-only commit changes the Fees functional tests, so that the rule does not change what they prove:

- `TradeFeeTradeTests` (6 trades) and `TradeFeeReaderTests` (4 trades) create a schedule from tomorrow over HTTP and then post a trade dated after it, only to get a fee on the trade. They insert the schedule directly with an `effectiveFrom` of today or earlier instead, and post the trade with a `bookedAt` that is not in the future. `TradeFeeRebalanceTests.AddScheduleStartingTodayAsync` already inserts a schedule this way; it moves to `FeeHttp`.
- Three tests in `TradeFeeScheduleTests` prove the trade-date branch of trade-fees R9: `Create_NotAfterAFutureDatedBuy_Returns400_OtherCurrenciesAreNotAffected`, `UpdateAndDelete_InEffect_Return409FeeScheduleInEffect` and `OtherTenantsTrades_DoNotMoveTheCutOff`. They insert the future-dated `Buy` directly through `Transaction.PostBuy`, as a row posted before this fix. Their assertions do not change.
- `GenerateCustomerTransactionsTests` posts its "outside-window" `TransferIn` at `2099-01-01`. It posts it on `2025-12-31` instead, before the report's `from` date, so the row is still outside the window.

That commit passes on its own. Then:

Application unit (the shared rule, with a fixed `TimeProvider`):

- `bookedAt` equal to the clock → valid.
- The clock plus exactly 5 minutes → valid.
- The clock plus 5 minutes and 1 second → invalid, key `BookedAt`, message in Decision 6.
- A `bookedAt` years in the past → valid.

Functional, for `POST /transactions`, `/transactions/transfer` and `/transactions/fx`:

- `bookedAt` one hour after now → 400 with `errors.BookedAt`. No Transaction and no idempotency record is written.
- `bookedAt` now → 201.

Functional, for `POST /transactions/fx/quote`:

- `bookedAt` one hour after now → 400 with `errors.BookedAt`.
- No `bookedAt` → 200, as today.

## Doc changes (applied on acceptance, 2026-11-16)

- [posting](../features/posting.md): amendment note. R8 gains the decimal place rule and the `CloseOut` exemption. R11 gains the order rule for a Transaction with a security leg. R24 gains "not in the future".
- [holdings](../features/holdings.md) R15, [buy-sell](../features/buy-sell.md) R16 and [split](../features/split.md) R14: amendment notes for the order rule. holdings and split also note that R18 / R16 follow posting R24.
- [internal-transfers](../features/internal-transfers.md): amendment note for R3 and R7 (`amount` decimal places) and `bookedAt`.
- [fx-conversions](../features/fx-conversions.md): amendment note for `bookedAt` on create and on the quote (R3, R20).
- [trade-fees](../features/trade-fees.md): amendment note. R9 is unchanged; a trade can no longer be dated after today.
- [adviser-portal-dashboard](../adviser-portal/adviser-portal-dashboard.md): D13 names the new refusal.
- [api-design.md](../api-design.md) §4: rows for the two field 400s.
- [domain-model.md](../domain-model.md) §8.6: the reversal invariant.
- [glossary.md](../glossary.md): "Latest security activity"; the Reversal row links it.
