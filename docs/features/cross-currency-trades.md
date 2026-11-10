---
title: Cross-currency trades
status: draft
phase: 2
language: en
owner: ""
created: 2026-11-10
last_updated: 2026-11-10
related:
  - fx-conversions.md
  - buy-sell.md
  - holdings.md
  - portfolios.md
  - fx-rates.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../adviser-portal/adviser-portal-portfolios.md
  - ../future-plans.md
  - ../glossary.md
---

# Cross-currency trades

Amendment to [buy-sell.md](buy-sell.md), [holdings.md](holdings.md) and [portfolios.md](portfolios.md) on `webapi`. It sets when a security trade may have an instrument quote currency that differs from its Account's currency, and it stores the rate of every automatic FX in `TransactionFxDetails` ([fx-conversions.md](fx-conversions.md) §6). No new type, no new route, no new policy, no script.

Depends on [fx-conversions.md](fx-conversions.md): the table, the item `fx` object and the staleness limit come from there. Accept and land that spec first.

---

## 1. Summary

A manual trade that adds a holding must be in the Account's currency; to buy a USD instrument, the Adviser first converts cash into a USD Account (`FxConversion`). Two paths keep the automatic FX of buy-sell R12: portfolio rebalance, so a model portfolio can mix currencies in one Account, and manual `Sell`, so a holding in another currency can always be sold. Every automatic FX stores its rate.

---

## 2. Scope

**In**

- Application: currency check on manual `Buy`, `StockIn`, `OpeningHoldings`; FX detail on every `Buy` / `Sell` whose cash leg uses FX (manual or rebalance); staleness limit on that FX; cash-leg rounding to the Account currency's `DecimalPlaces`
- Domain: `Buy` / `Sell` factories take an optional `AppliedFxRate`; `Reverse` already copies the FX detail ([fx-conversions.md](fx-conversions.md) §5)
- HTTP: item `fx` filled for `Buy` / `Sell` with FX; new 400 cases on `POST /transactions`
- Portfolios: rebalance writes the FX detail; new failure code `stale_fx`; R18 unchanged
- Adviser Portal: instrument picker eligibility; assign confirmation lists the holdings that will be sold
- Tests listed in §10

**Out**

- Multi-currency cash in one Account ([future-plans.md](../future-plans.md) §4)
- Back-filling FX details on posted `Buy` / `Sell`
- A quote route for `Buy` / `Sell`
- Fee or spread
- Dividends in a foreign currency (no Dividend type yet)
- Changing holding cost currency or cost rounding (cost stays in the quote currency, buy-sell R11 / R15)

---

## 3. Stories

1. As an Adviser I cannot buy a USD instrument in an NZD Account; the error tells me to convert into a USD Account first.
2. As an Adviser I can sell a USD holding left in an NZD Account (after a portfolio was unassigned, or from older data), and the proceeds arrive in NZD at a stored rate.
3. As an Adviser I assign a portfolio that mixes NZD and USD instruments to an NZD Account, and rebalance converts automatically at a stored rate.
4. As an Adviser I see the rate a trade used on the transaction.
5. As an Adviser I see which holdings will be sold before I confirm a portfolio assignment.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | **Currency mismatch** means the instrument's `QuoteCurrency` differs from the security leg Account's `Currency`. Both are immutable (instruments R8, accounts R9), so the answer does not change over time. |
| R2 | **Manual adds.** On `POST /transactions`, `Buy`, `StockIn` and `OpeningHoldings` with a currency mismatch → 400. The message names both currencies and points to `POST /transactions/fx`. |
| R3 | **Manual Sell.** `Sell` with a currency mismatch is allowed. The cash leg uses buy-sell R12 (`IFxRate` quote → Account currency). |
| R4 | **Unaffected types.** `StockOut`, `Split`, `Bonus`, `Scrip` have no cash leg and do not add a new holding; no currency check. |
| R5 | **Rebalance.** Rebalance-generated `Buy` / `Sell` keep portfolios R10: a mismatch is allowed and the cash leg converts quote → Account currency. R2 applies only to the HTTP create path; rebalance calls the Domain factories directly. |
| R6 | **Portfolio lock.** Portfolios R18 is unchanged: while an assignment is Active, manual `Sell` is still rejected. R3 is the way out after unassign (portfolios R15) or disable (portfolios R22). |
| R7 | **FX detail.** Every `Buy` / `Sell` whose cash leg used a rate other than the same-currency 1 gets one `TransactionFxDetails` row: `BaseCurrency` = instrument quote currency, `QuoteCurrency` = Account currency, `Rate`, `RateAsOfDate`, `RateSource` of the `FxRates` row used. Written in the same unit of work. Same-currency trades have no row and `fx: null`. |
| R8 | **Staleness.** The automatic FX in R3 and R5 uses the staleness limit of fx-conversions R10 (more than 4 calendar days between the rate's as-of date and `bookedAt`). Manual `Sell` → 400. Rebalance fails for that Account with code `stale_fx`; no transactions are written (portfolios R10, R13). |
| R9 | **Cash rounding.** The `Buy` / `Sell` cash leg is rounded to the Account currency's `DecimalPlaces`, `MidpointRounding.AwayFromZero`, for every trade with or without FX. Replaces the 4-decimal rounding of the cash amount. Security-leg cost and released cost keep 4 decimals in the quote currency (buy-sell R11, R15). |
| R10 | **Reverse.** Reverse of a `Buy` / `Sell` copies the opposite cash and security legs, as today, and copies the FX detail when there is one. The current rate is not read. Posted trades without an FX detail reverse as before. |
| R11 | **Existing data.** Posted `Buy` / `Sell` with a mismatch and no FX detail stay as they are and show `fx: null`. Existing mismatched holdings are sold with R3. No back-fill. |
| R12 | **Valuation.** Net worth and portfolio valuation are unchanged (market value in quote currency; rebalance converts quote → base with `IFxRate`). |

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `Transaction` | Aggregate | `PostBuy` / `PostSell` take an optional `AppliedFxRate` and the Account currency's decimal places. With a rate, the cash amount is `Round(cost × rate, dp, AwayFromZero)` and the FX detail of R7 is attached; without one, the instrument and Account currencies must match. |
| `AppliedFxRate`, `TransactionFxDetail` | VO / entity | From [fx-conversions.md](fx-conversions.md) §5. Unchanged. |

Invariants:

- A `Buy` / `Sell` with an FX detail has base = the security leg's quote currency and quote = the cash leg's currency, and these differ.
- A `Buy` / `Sell` without an FX detail has the same currency on both legs, except posted rows from before this amendment (R11), which the Domain never rebuilds.

The currency rule of R2 lives in Application, because rebalance must be able to post a mismatched `Buy`. Update [domain-model.md](../domain-model.md) when this spec is accepted.

---

## 6. Database

None. `TransactionFxDetails` comes from `0026_fx_conversions.sql` ([fx-conversions.md](fx-conversions.md) §6).

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Command | `CreateTransaction` | R2 for `Buy` / `StockIn` / `OpeningHoldings`; R3, R7, R8, R9 for `Buy` / `Sell` |
| Command | `RebalanceAccount` | R5, R7, R8 (`stale_fx`), R9 |
| Command | `ReverseTransaction` | R10 |
| Query | `GetTransactions` / `GetTransactionById` | `fx` filled for `Buy` / `Sell` with an FX detail |

---

## 8. API

No new route. `POST /transactions` gains the 400 of R2 and the staleness 400 of R8. The item's `fx` object (fx-conversions R22) is filled for `Buy` / `Sell` with FX:

```json
{
  "type": "Buy",
  "cash": [ { "accountId": "<NZD account>", "customerId": "<customer>", "amount": -1691.47, "currency": "NZD" } ],
  "security": [ { "accountId": "<NZD account>", "customerId": "<customer>", "instrumentId": "<US ETF>", "quantity": 10, "cost": 1000.0000, "costCurrency": "USD" } ],
  "fx": { "baseCurrency": "USD", "quoteCurrency": "NZD", "rate": 1.69147000, "rateAsOfDate": "2026-11-09" }
}
```

Portfolios failure codes add:

| code | When |
| --- | --- |
| `stale_fx` | A needed FX row exists but is older than the staleness limit (R8): `quote → base` for valuation or `quote → account currency` for the cash leg |

---

## 9. UI

Adviser Portal. Amend [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) and [adviser-portal-portfolios.md](../adviser-portal/adviser-portal-portfolios.md) when this spec is accepted.

- **Buy / StockIn / OpeningHoldings forms.** The instrument picker greys out instruments whose quote currency differs from the Account currency, with the reason "Different currency — convert into a <QuoteCurrency> account first". The client filters; the server still enforces R2.
- **Sell form.** Lists every holding, including mismatched ones. A mismatched holding shows "Proceeds converted to <Account currency>".
- **Transaction lists.** A `Buy` / `Sell` with `fx` shows the rate line, as `FxConversion` does.
- **Assign confirmation.** Before `POST /accounts/{id}/portfolio`, the confirm step lists the current holdings that are not in the portfolio's lines and says they will be sold in full (portfolios R8). Frontend only; the API is unchanged (portfolios R25).
- **Rebalance status.** `stale_fx` is shown with its message, like `missing_fx`.

Customer Portal: the rate line on `Buy` / `Sell` with `fx`. No other change.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `PostBuy` / `PostSell` with a rate: cash amount and rounding to 0 and 2 decimal places, FX detail fields; without a rate and mismatched currencies → rejected; same-currency cash rounded to `DecimalPlaces`; Reverse copies the FX detail |
| Application.FunctionalTests | Manual `Buy`, `StockIn`, `OpeningHoldings` with mismatch → 400 and nothing written; same-currency `Buy` succeeds with `fx: null`; manual `Sell` with mismatch succeeds, writes the FX detail and shows `fx`; stale rate on that `Sell` → 400; manual `Sell` on an assigned Account still → 400 (R6); after unassign, `Sell` of a mismatched holding succeeds; rebalance with mixed-currency lines writes FX details on each mismatched trade; rebalance with a stale rate → `stale_fx` and no transactions; reverse of a `Buy` with FX copies the detail and uses the original amounts; posted `Buy` without detail reverses as before |
| Infrastructure.IntegrationTests | None new |
| Application.FunctionalTests (two tenants) | Unchanged; existing transaction isolation tests cover the new 400 paths |
| Frontend | Picker greys out mismatched instruments with the reason; Sell lists mismatched holdings with the proceeds note; rate line on `fx`; assign confirmation lists holdings that will be sold; `stale_fx` message |

---

## 11. Locked in this spec (discussion, 2026-11-10)

No new ADR.

| Item | Lock |
| --- | --- |
| Manual adds | `Buy`, `StockIn`, `OpeningHoldings` must match the Account currency |
| Manual Sell | Always allowed; automatic FX with a stored rate |
| Rebalance | Mixed-currency portfolios allowed; automatic FX with a stored rate |
| Rate record | `TransactionFxDetails`, base = instrument quote currency, quote = Account currency |
| Staleness | Same 4-day limit as `FxConversion`; rebalance code `stale_fx` |
| Rounding | Cash leg to Account currency `DecimalPlaces`, `AwayFromZero`; cost keeps 4 decimals |
| Existing data | No back-fill; `fx: null` |
| Assign | Frontend lists holdings to be sold; API unchanged |

Still open, not this slice:

- Whether `Account.Currency` must equal the portfolio `BaseCurrency` on assign (not required today).
- Foreign-currency dividends ([future-plans.md](../future-plans.md) §4).

---

## 12. Suggested commits

Only after `accepted`, and after fx-conversions has landed. The tree must build after each commit. Split each behaviour into red then green.

1. Cash-leg rounding to `DecimalPlaces` for `Buy` / `Sell` (R9) + tests.
2. Domain `PostBuy` / `PostSell` with `AppliedFxRate` and FX detail (R7) + unit tests.
3. Manual `Buy` / `StockIn` / `OpeningHoldings` currency check (R2) + functional tests.
4. Manual `Sell` with FX detail and staleness (R3, R8) + tests; item `fx` for `Buy` / `Sell`.
5. Rebalance writes FX details; `stale_fx` (R5, R8) + tests.
6. Reverse copies the FX detail on `Buy` / `Sell` (R10) + tests.
7. Adviser Portal picker, Sell note, rate line, assign confirmation, `stale_fx` + frontend tests. Customer Portal rate line.
8. Repo docs: this file `accepted`; amend buy-sell R12, holdings, portfolios R10 and failure codes, domain-model, api-design change log, adviser-portal-dashboard, adviser-portal-portfolios, features README status.
