---
title: Income on External accounts
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-27
last_updated: 2026-11-27
related:
  - ../future-plans.md
  - dividends.md
  - interest-run.md
  - external-caller-numbers.md
  - custody-modes.md
  - posting.md
  - leg-accounts.md
  - transaction-source.md
  - holdings.md
  - buy-sell.md
  - customers.md
  - reports.md
  - bank-reconciliation.md
  - account-valuation.md
  - bff-adviser-portal.md
  - ../fixes/ledger-write-guards.md
  - ../fixes/utc-instants.md
  - ../adr/0015-idempotency-keys.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../customer-portal/customer-portal.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
---

# Income on External accounts

What is left of step 2 of the business roadmap in [future-plans.md](../future-plans.md) §1 (income events). Dividends and interest on Custodial accounts are paid by the platform, with their tax ([dividends.md](dividends.md), [interest-run.md](interest-run.md)). On an External account the money was paid by an outside party, and a person records it. Today that record cannot hold the tax: a hand `Dividend` is not accepted at all, and a hand `Interest` is one cash leg with no `Tax` leg.

After this slice an Adviser or TenantAdmin records a dividend on an External account with `POST /transactions/dividend`: the Instrument, the shares it was paid on, the gross amount, each tax that was withheld and the imputation credit. A hand `Interest` on an External account may carry the tax that was withheld. Both use the `Tax` cash leg and the tax detail of interest-run R25 and the dividend detail of dividends R39, so a later tax report reads one shape for both custody modes.

New: route `POST /transactions/dividend`; the field `taxes` on `POST /transactions` for `Interest`; Domain value `EnteredTax` and factories `Transaction.PostEnteredDividend` and `Transaction.PostEnteredInterest`; a dividend detail may have no amount per share and no ex-date. Script `0060_external_income.sql`. No new permission, `TransactionType`, tax kind, cash leg role, source kind, job, port, problem code or domain event. The simulator and the BFFs do not change.

**Status is `accepted` (2026-11-27), landed 2026-11-27.** The twelve points of §11 were confirmed on 2026-11-27, every one as recommended, and are written into the rules. The changes to the docs listed in §12 were made when it was accepted. It is a build contract.

Notes from landing:

- R19, §7: `ReverseTransaction` loaded the FX, adjustment and fee details of the Transaction it reverses, but not its tax and dividend details, so a hand reverse of a Transaction with `Tax` legs failed. It now loads both; `Transaction.Reverse` copies them as before and nothing is read again.
- R3: the request-shape rules run in the validator before the Account is loaded, so a body that breaks one is a 400 even for another tenant's Account; with a valid shape another tenant's Account or Instrument is a 404, before `bookedAt`, the custody mode and every rule that needs the Account.
- R5, R11, R14: the messages the spec leaves open are `Kind must be Rwt, Nrwt, or ForeignWithholding.`, `RatePercent must be above 0 and at most 100, with at most 2 decimal places.`, `Amount must be greater than 0.` and `At most 3 taxes can be entered.` for a row, `Quantity must be greater than 0, with at most 8 decimal places.`, `GrossAmount must be greater than 0.`, `TaxSource must be NewZealand or Foreign.` and, for a gross that converts to 0, `GrossAmount converts to 0 in the account currency.` A field the body declares and refuses (`type`, `amount`, `currency`, `cash`, `security`, `fee`) is a 400 at that field; any other unknown field is a 400 with no `errors`.
- D112: the Adviser Portal had no form for a hand `Interest`, although D18 offers every hand type on an External account. The type selector now offers "Interest" and "Dividend" on an Open External account of any type.
- D113: the transactions list shows every row with `Tax` legs as its net with "incl. tax <amount>" under it, as D61 says, where it showed each leg before; a Custodial interest posting and dividend read the same way.

Tax figures in the worked examples come from dividends R33 and interest-run R4, which name their Inland Revenue sources. This slice adds no rate: on an External account the caller states what the payer withheld (R4).

---

## 1. Summary

| | Today | After this slice |
| --- | --- | --- |
| A dividend on an External account | Not accepted. It can only be typed as a `TransferIn`, which loses the Instrument, the gross and the tax | `POST /transactions/dividend`: one `Dividend` Transaction with a `Principal` leg of the gross, one `Tax` leg per tax withheld, and a dividend detail with the imputation credit |
| Tax on a hand `Interest` | Not recorded. The Adviser enters the net | Optional `taxes`: the `Principal` leg is the gross and each tax is a `Tax` leg with its tax detail |
| Where the numbers come from | — | The caller, as every number on an External account (custody-modes R19). The tax profile is not read and no rate is applied |
| A dividend in another currency than the Account's | — | Amounts are entered in the Instrument's quote currency with the caller's `fxRate`, as an External `Buy` (external-caller-numbers R7) |
| Custodial accounts | Dividends and interest are paid by the platform | Unchanged |

The rule that distinguishes this slice: the platform records the payer's figures and works out none of them. It converts with the caller's rate and adds the legs up; it does not multiply shares by an amount per share and does not check a tax against a rate.

Worked examples, used by the tests. NZD and USD have 2 decimal places.

| Case | Entered | Cash legs on the Account | Net |
| --- | --- | --- | --- |
| A. New Zealand company, NZD account (the example of IR284) | 1 000 shares, gross 2 000.00, `NewZealand`, imputation credit 200.00, tax `Rwt` 33 % 526.00 | `Principal` +2 000.00; `Tax` −526.00 (basis 2 200.00) | 1 474.00 |
| B. Foreign company, USD account | 100 shares, gross 100.00, `Foreign`, taxes `ForeignWithholding` 15 % 15.00 and `Rwt` 33 % 18.00 | `Principal` +100.00; `Tax` −15.00 (basis 100.00); `Tax` −18.00 (basis 100.00) | 67.00 |
| C. USD company, NZD account | 10 shares, 0.26 per share, gross 2.60, `Foreign`, tax `ForeignWithholding` 15 % 0.39, `fxRate` 1.69147 | `Principal` +4.40 (`2.60 × 1.69147 = 4.397822`); `Tax` −0.66 (`0.39 × 1.69147 = 0.6596733`, basis 4.40); FX detail USD → NZD `1.69147000`, source `Caller` | 3.74 |
| D. No tax | 50 shares, gross 12.50, `NewZealand`, no `taxes` | `Principal` +12.50 | 12.50 |
| E. Interest, NZD bank account | `Interest`, `amount` 12.34, tax `Rwt` 33 % 4.07 | `Principal` +12.34; `Tax` −4.07 (basis 12.34) | 8.27 |
| F. Interest, AUD bank account of a New Zealand Customer | `Interest`, `amount` 50.00, tax `ForeignWithholding` 10 % 5.00 | `Principal` +50.00; `Tax` −5.00 (basis 50.00) | 45.00 |

---

## 2. Scope

**In**

- Domain: `EnteredTax`; `EnteredTaxes` (the rules of R5–R7); factories `Transaction.PostEnteredDividend` and `Transaction.PostEnteredInterest`; `TransactionDividendDetail` with an optional amount per share and an optional ex-date; `CustodyRules` allows `Dividend` by hand on an External account; a `Tax` leg of kind `ForeignWithholding` on an `Interest`
- Application: command `CreateDividend`; `CreateTransaction` takes `taxes` on an `Interest` and no longer lets `Dividend` through; the transaction reads map the two optional fields
- Script: `database/schema/0060_external_income.sql`
- HTTP (`webapi`): `POST /transactions/dividend`; `taxes` on `POST /transactions`
- Adviser Portal: the dividend form, the interest form with its tax rows, the hand-entered dividend and interest on the ledger pages
- Customer Portal: the row of a hand-entered dividend and of a hand-entered interest with tax
- Tests listed in §10

**Out**

- A reinvested dividend on an External account. It is entered as the `Dividend` and a `Buy` at the plan price (§11 point 7). `Scrip` stays as it is ([split.md](split.md))
- Working out any tax: the IR284 formula, the rates of the tax profile, a suggested amount on the server. The Custodial flows keep theirs
- A check of the entered tax against the Customer's tax profile or against its own rate
- A check of the shares against a holding, and any change to a holding. An account may record a dividend of shares it no longer holds, or never recorded
- The country of the payer of interest, and a tax source on an `Interest`
- Taxable bonus issues, returns of capital, and a dividend paid in another currency than the Instrument's quote currency (dividends §2 leaves the same out)
- A dividend or income report, a tax statement for the Customer, the FIF rules and a foreign tax credit in NZD ([future-plans.md](../future-plans.md) §1 step 6)
- Moving `Interest` to its own create route ([future-plans.md](../future-plans.md) §4)
- Entering a dividend once for several accounts, an import from a file, and applying a corporate action to External accounts by the flow
- A change to a Custodial account, the simulator, the corporate action pages or the bank reconciliation

---

## 3. Stories

1. As an Adviser I record the dividend my Customer got on shares held at another broker, with the gross, the tax taken off and the imputation credit, as the dividend statement shows them.
2. As an Adviser I record a dividend of a US share that an outside broker paid into an NZD account, with the US tax and the broker's exchange rate.
3. As an Adviser I record the interest a bank paid on my Customer's account with the RWT it withheld, so that the account's balance and the tax both match the bank's statement.
4. As a Customer I see each dividend and each interest payment on my outside accounts with the gross, each tax and the net, as I see them on my Custodial accounts.
5. As an Adviser I reverse a dividend I entered wrongly and enter it again.
6. As the platform I store hand-entered income in the same legs and details as the income the platform pays, so that a tax report can read both.

---

## 4. Rules

### 4.1 Access and common rules

| ID | Rule |
| --- | --- |
| R1 | **Access.** No new permission. `POST /transactions/dividend` takes `transactions.create`, as every create route under `/transactions`, with the rules of posting R1–R4a and leg-accounts R6: missing / dead Bearer → 401, never 302; Customer → 403; a disabled current tenant → 403; TenantAdmin / Adviser omit `tenantId` (400 if present); SystemAdmin must send it (missing → 400, unknown → 404, disabled → 400 `disabled` / `target=tenant`). An Account of another tenant, of an unassigned Customer (Adviser), or missing → 404, before any rule of this spec is evaluated. |
| R2 | **Custody mode.** This slice is about External accounts. On a Custodial account `POST /transactions/dividend` → 400 `errors.AccountId` with the message of dividends R43, `Dividends are paid by the platform from the company's announcement.`, and a hand `Interest` is still refused by the manual entry matrix (custody-modes R5), with or without `taxes`. Nothing is written. |
| R3 | **Order of checks.** Request-shape validation runs first: a field that is present must have the shape these rules give it, whatever the Account. In the handler the order is that of custody-modes R6 and external-caller-numbers R3: the Account is loaded and locked; Closed (posting R6), closing (account-closure R12) and Disabled Customer (posting R7); the custody mode (R2); `bookedAt` (custody-modes R11); then the Instrument (R10) and the rules that need it (R11, R12, R14). |
| R4 | **The caller's numbers.** Every amount, quantity, rate and date of this slice is stated by the caller and stored as entered, or converted with the caller's `fxRate` (R14). The server reads no price, no FX rate, no corporate action, no holding and no tax profile. It does not multiply the shares by the amount per share, and it does not check a tax amount against its rate. It checks only what R5–R7 and R11–R13 say. |

### 4.2 Entered taxes

Shared by the dividend (§4.3) and the interest (§4.4). Amends [interest-run.md](interest-run.md) R25.

| ID | Rule |
| --- | --- |
| R5 | **Shape.** `taxes` is optional: an array of 0 to 3 rows `{ kind, ratePercent, amount }`, each one tax an outside payer withheld. `kind`: `Rwt`, `Nrwt` or `ForeignWithholding`, by exact name (else 400 `errors["Taxes[i].Kind"]`). `ratePercent`: the rate the payer stated, `> 0`, at most `100`, at most 2 decimal places (else 400 `errors["Taxes[i].RatePercent"]`). `amount`: `> 0` (else 400 `errors["Taxes[i].Amount"]`). All three are required in a row. An empty array is the same as no `taxes`. |
| R6 | **Kinds.** A kind appears at most once (interest-run R25: at most one tax detail per `(Transaction, kind)`), and `Rwt` and `Nrwt` never appear together: a payer treats the holder as resident or as non-resident. Else 400 `errors.Taxes` with `Each tax kind can be entered once.` or `RWT and NRWT cannot both be withheld from one payment.` Which kind fits the Customer is the caller's statement; the tax profile is not read (R4). |
| R7 | **Not more than the gross.** The taxes of a Transaction add up to at most its gross, after the conversion of R14: else 400 `errors.Taxes`, `Tax withheld cannot be more than the gross amount.` So the net is never below 0 and the Transaction never lowers cash. |
| R8 | **Legs and tax detail.** Each row becomes one cash leg with role `Tax` on the Account, in its currency, of minus the amount, with one tax detail: the kind, `ratePercent` as entered, the tax amount, the currency, and the basis amount. The basis is the gross in the Account's currency; for `Rwt` on a dividend it is the gross plus the imputation credit, as dividends R38, so one meaning holds on both custody modes. The detail does not say that the amount was entered: the Transaction's source `Manual` does. |
| R9 | **Which types.** `taxes` is taken on `POST /transactions/dividend` and, on `POST /transactions`, on `Interest` only. On every other type of `POST /transactions` a `taxes` that is not empty → 400 `errors.Taxes`, `Taxes can be set on Interest only.`, in the validator. The Domain allows a `Tax` leg on `Interest` and on `Dividend`, of all three kinds on both, and on no other type. A `ForeignWithholding` leg on an `Interest` comes only from hand entry: an interest run still withholds `Rwt` or `Nrwt` (interest-run R24). |

### 4.3 A dividend on an External account

Amends [dividends.md](dividends.md) R39, R43 and R50, [custody-modes.md](custody-modes.md) R5 and §4.2.1, and [posting.md](posting.md) R16 and §5.

| ID | Rule |
| --- | --- |
| R10 | **Route and Instrument.** `POST /transactions/dividend` creates a `Dividend` on one External Account. `POST /transactions` with `type` `Dividend` → 400 `errors.Type` in the validator, on both custody modes, with the message of every type that route does not take (posting R16): there is one create route per type ([api-design.md](../api-design.md) §7.7). `instrumentId` names an Instrument of the Account's tenant; missing or another tenant's → 404. The Instrument may be disabled: nothing is bought, and a dividend of a share that has since been delisted can still be recorded. The Account may be of any type that can be opened (Bank, Cash, Brokerage, Other): a dividend of shares held at a registry is often paid into a bank account. |
| R11 | **Body.** Required: `accountId`, `instrumentId`, `quantity`, `grossAmount`, `taxSource`, `bookedAt`. Optional: `amountPerShare`, `imputationCredit` (default `0`), `taxes` (§4.2), `fxRate` (R14), `exDate`, `recordDate`, `paymentDate`, `memo`, `reference` (posting R23). The body has no `type`, `amount`, `currency`, `cash`, `security`, `fee` or reinvestment field; any of them present → 400. `quantity`: the shares the dividend was paid on, `> 0`, at most 8 decimal places. `grossAmount`: the dividend before any tax, in the Instrument's quote currency, `> 0`. `amountPerShare`: the amount the company announced per share, in the quote currency, `> 0`, at most `1 000 000`, at most 8 decimal places; stored and shown as given, never multiplied (R4). `taxSource`: `NewZealand` or `Foreign` by exact name (dividends R3); it is not checked against the Instrument's issuer country ([instrument-tax-attributes.md](instrument-tax-attributes.md) R23). The three dates are dates; they are stored and shown as given and decide nothing. A violation → 400 at the field. |
| R12 | **Tax source rules.** `imputationCredit`: `≥ 0`, not above `grossAmount`, and above 0 only with `NewZealand` (else 400 `errors.ImputationCredit`; with `Foreign`: `Only a New Zealand company attaches imputation credits.`). A `ForeignWithholding` tax only with `Foreign`, and an `Nrwt` tax only with `NewZealand` (else 400 `errors["Taxes[i].Kind"]`: `Foreign tax is withheld from a foreign company's dividend only.`, `NRWT is withheld from a New Zealand company's dividend only.`). `Rwt` goes with either. These are the pairs of dividends R3 and R33. |
| R13 | **Decimal places.** `grossAmount`, `imputationCredit` and every tax `amount` are in the quote currency and have no more decimal places than its `DecimalPlaces`, else 400 at the field with `{Field} can have at most {n} decimal places.`, checked after the Instrument is loaded (as the fee of external-caller-numbers R6). |
| R14 | **Currency and FX rate.** A dividend is in the quote currency of its Instrument (dividends R4). When that is the Account's currency, `fxRate` → 400 `errors.FxRate`, `FxRate cannot be set when the instrument currency is the account currency.`, and every amount is booked as entered. When it differs, `fxRate` is required: Account currency per 1 unit of quote currency, `> 0`, at most 8 decimal places, with the messages of external-caller-numbers R7. The gross, the imputation credit and each tax amount are then each converted as `Round(amount × fxRate, dp, AwayFromZero)`, `dp` the Account currency's `DecimalPlaces` (the conversion of dividends R32), and the Transaction gets an FX detail: base = quote currency, quote = Account currency, the rate, `RateAsOfDate` = the UTC date of `bookedAt`, `RateSource` = `Caller` (external-caller-numbers R10). The server does not call `IFxRate`. A tax that converts to `0` books no leg; a gross that converts to `0` → 400 `errors.GrossAmount`. |
| R15 | **Booking time.** `bookedAt` is the caller's, required, with `Z` or an offset, and not later than the server clock plus 5 minutes (custody-modes R11, posting R24). It is the instant the money reached the account. `paymentDate` is the company's date and may differ. |
| R16 | **The Transaction.** Type `Dividend` (6). Cash legs on the Account, in its currency: one `Principal` leg of plus the gross, and one `Tax` leg per tax (R8). No `Reinvestment` leg and no security leg: a hand-entered dividend touches no holding and is not a security activity ([ledger-write-guards](../fixes/ledger-write-guards.md) Decision 1). Source `Manual` with no source id (transaction-source R2); `memo` and `reference` as entered; a dividend detail (R17); an FX detail with R14. Built by `Transaction.PostEnteredDividend`, which takes only source `Manual`. `Transaction.PostDividend` still takes only source `CorporateAction`. |
| R17 | **Dividend detail.** Exactly one, as dividends R39: the Instrument, `quantity`, `amountPerShare` or none, the quote currency, `taxSource`, the imputation credit in the Account's currency, and the three dates or none. No reinvestment. Dividends R39 changes with it: the amount per share and the ex-date are required on a dividend that a corporate action posts, and optional on a hand-entered one. The imputation credit is not money and is on no leg. |
| R18 | **Cash and other rules.** The legs add up to the net, which is `≥ 0` (R7), so cash `SUM ≥ 0` holds (posting R9). A `Dividend` leg counts as another Transaction on the Account, so a cash `Opening` after it → 400 (posting R19). An External account has no portfolio assignment (custody-modes R14), so no trade lock applies and nothing is rebalanced. No interest is accrued on an External account (interest-run R20). |
| R19 | **Reverse.** `POST /transactions/{id}/reverse` reverses a hand-entered dividend like any Transaction on an External account (custody-modes R5). `Transaction.Reverse` already copies the `Tax` legs with their details, the dividend detail and the FX detail (dividends R42); nothing is read again. The reverse takes the net back out, so it → 400 when the Account's cash `SUM` would go below 0 (posting R11). A wrong dividend is corrected by reversing it and entering it again; no update exists (posting R10). |
| R20 | **Idempotency.** `Idempotency-Key` is required (ADR 0015): missing → 400; the same key and request hash → the original `201 { id }`; the same key with another hash → 409. The request path and every field of the body are in the hash. |
| R21 | **Matrix.** The manual entry matrix cell `Dividend` / External becomes **allowed**, on `POST /transactions/dividend`. `Dividend` / Custodial stays **refused**; its message is now given on the new route under `errors.AccountId` (R2). `CustodyRules.RefusalMessage` gives `null` for `Dividend` on an External account. |

### 4.4 Tax on a hand-entered `Interest`

Amends [posting.md](posting.md) R16 and R20 and [interest-run.md](interest-run.md) R29.

| ID | Rule |
| --- | --- |
| R22 | **Body.** `POST /transactions` with `type` `Interest` takes `taxes` (§4.2). With no tax the Transaction is what it is today: one `Principal` leg of `amount`, of either sign (posting R20). With at least one tax, `amount` is the **gross** interest and must be `> 0`: else 400 `errors.Amount`, `Interest with tax withheld needs a gross amount above 0.` Each tax `amount` is in the Account's currency and has no more decimal places than its `DecimalPlaces` (posting R8). There is no `fxRate`: interest on an account is in the account's currency. |
| R23 | **The Transaction.** Type `Interest`: one `Principal` leg of plus `amount` and one `Tax` leg per tax (R8), source `Manual`, `bookedAt`, `memo` and `reference` as entered. It has no interest detail: that is the period of an interest run's posting (interest-run R26), and `interest` on the item stays `null`. Built by `Transaction.PostEnteredInterest`. The cash rules of R18 hold. |
| R24 | **Reverse and idempotency.** Reverse is R19. `taxes` joins the request hash of `POST /transactions`; a retry that crosses the deploy with a key stored before it gets 409, which is accepted as in external-caller-numbers R14. |
| R25 | **Matrix.** Unchanged: `Interest` is allowed by hand on an External account and refused on a Custodial one (R2). Interest-run R29 changes: a hand `Interest` on an External account has one `Principal` leg and may have `Tax` legs. |

### 4.5 Reads and other flows

| ID | Rule |
| --- | --- |
| R26 | **Transaction item.** A hand-entered dividend has `dividend` as dividends R50, with `corporateActionId` `null`, `amountPerShare` and `exDate` `null` when they were not entered, and `reinvestedQuantity` and `reinvestmentPrice` `null`. Its `source` is `{ "kind": "Manual", "id": null }`. A `Tax` leg carries `tax` as interest-run R34, with `kind` any of the three on an `Interest`. Every caller who can read the Transaction sees all of it, the Customer included. The `type` filter of the list already takes `Dividend`. |
| R27 | **Reports, balances and valuation.** Cash balance, net worth, the valuation read and the Adviser Balances Report sum the cash legs and do not change. The Customer Transactions Report shows the net in the cash amount column and the sum of the `Tax` legs in the Tax column, as it does for a Custodial dividend and an interest posting (reports §9.1). No code changes there. |
| R28 | **Bank reconciliation.** Unchanged. An External account is in no client money account's run, and `TaxWithheld` counts the `Tax` legs of Custodial accounts only (interest-run R41, dividends R45): tax withheld by an outside payer was never in the firm's trust account. |
| R29 | **Existing data.** No Transaction is changed. A hand `Interest` posted before this slice keeps its one leg. Every existing dividend detail keeps its amount per share and its ex-date. |

Policies: none added. `transactions.create` posts the dividend and the interest.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `EnteredTax` | Value object (new) | `Kind`, `RatePercent`, `Amount`. One tax an outside payer withheld, as the caller stated it |
| `EnteredTaxes` | Static (new) | `RefusalOf(taxes)`: R5, R6. `RefusalOf(taxes, taxSource)`: R12. Used by Application before the factory, and by the factories |
| `Transaction` | Aggregate (existing) | `PostEnteredDividend(tenantId, accountId, accountCurrency, decimalPlaces, instrumentId, quoteCurrency, quantity, amountPerShare?, grossAmount, taxSource, imputationCredit, taxes, fxRate?, exDate?, recordDate?, paymentDate?, bookedAt, memo?, reference?, source)`: R14, R16, R17; the source kind must be `Manual`; gross `> 0`; taxes at most the gross. `PostEnteredInterest(tenantId, accountId, accountCurrency, grossAmount, taxes, bookedAt, memo?, reference?, source)`: R22, R23; the source kind must be `Manual`; at least one tax; gross `> 0`; taxes at most the gross. `PostDividend`, `PostInterest`, `Post` and `Reverse` do not change |
| `TransactionDividendDetail` | Entity (existing) | `AmountPerShare?`, `ExDate?`. `Create` no longer requires them; `PostDividend` still passes both |
| `TransactionTaxDetail` | Entity (existing) | No change of shape. Kind `ForeignWithholding` may sit on an `Interest` |
| `CustodyRules` | Static (existing) | `Dividend` is allowed by hand on an External account. The Custodial message stays |
| `TransactionType`, `TaxKind`, `CashLegRole`, `DividendTaxSource` | Enums (existing) | No new value |

```text
A hand-entered dividend (quote currency Q, Account currency A)
  entered in Q:  grossAmount, imputationCredit, taxes[i].amount
  Q = A:   booked as entered, no fxRate
  Q <> A:  each amount -> Round(amount x fxRate, dp(A)), FX detail with source Caller
  legs:    Principal +gross
           Tax       -tax, one per row, with its tax detail (rate as entered)
  net    = gross - sum of taxes >= 0
  detail:  Instrument, quantity, amountPerShare?, Q, taxSource, imputationCredit (in A), dates?

A hand-entered Interest with tax (Account currency A)
  legs:    Principal +amount (the gross, > 0)
           Tax       -tax, one per row
```

Invariants:

- A `Dividend` has exactly one dividend detail, and a `Principal` leg above 0. Held by both factories.
- A `Dividend` with source `Manual` has no `Reinvestment` leg and no security leg. Held by `PostEnteredDividend`.
- The `Tax` legs of a Transaction add up to at most its `Principal` leg. Held by both new factories.
- A Transaction never has an `Rwt` and an `Nrwt` leg together. Held by `EnteredTaxes` for hand entry and by `DividendWithholding.For` and `Withholding` for the flows.
- On a Custodial account no person enters a `Dividend` or an `Interest`. Held by `CustodyRules` (unchanged).

Domain events: none new. Each posting raises `TransactionPosted` as every Transaction does.

[domain-model.md](../domain-model.md) carries these factories and invariants.

---

## 6. Database

Forward only. Do not add EF migrations.

Script `database/schema/0060_external_income.sql`:

| Table | Change | Index / FK |
| --- | --- | --- |
| `TransactionDividendDetails` | `AmountPerShare` becomes `NULL`. `ExDate` becomes `NULL` | Drop and re-add `CK_TransactionDividendDetails_AmountPerShare` as `AmountPerShare IS NULL OR AmountPerShare > 0` |

Nothing else changes. `CK_Transactions_Type` allows 6, `CK_TransactionTaxDetails_Kind` allows 0–2 on any Transaction, and the rate, basis and amount CHECKs of `TransactionTaxDetails` hold for an entered tax (R5). The schema does not tie the two columns to the source: that a corporate action's dividend has both is held by `PostDividend`. No back-fill.

[database-design.md](../database-design.md) carries the two nullable columns.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Command | `CreateDividend` (new) | `POST /transactions/dividend`: R1–R3, R10–R21. Returns the new id through `IdempotentResult`, as `CreateInternalTransfer` |
| Command | `CreateTransaction` (existing) | `taxes` on `Interest` (R22, R23); refused on every other type (R9); `Dividend` leaves the type check (R10) |
| Command | `ReverseTransaction` (existing) | Source `Manual` on an External account is reversible (R19). It loads the tax and dividend details of the Transaction it reverses, so `Transaction.Reverse` copies them |
| Query | `GetTransactions`, `GetTransactionById` (existing) | `dividend.amountPerShare` and `dividend.exDate` may be `null` (R26) |
| Query | The Customer Transactions Report (existing) | No change in code (R27) |

Where each rule lives:

| Rule | Where |
| --- | --- |
| Shapes that need no Account (R5, R6, R11, R12, the `fxRate` shape) | The validators, through `EnteredTaxes` |
| Custody mode, `bookedAt`, decimal places, `fxRate` required or refused (R2, R13–R15) | The handlers, after the Account and the Instrument are loaded |
| Conversion, legs, details, taxes at most the gross (R7, R8, R14, R16, R17, R23) | `Transaction.PostEnteredDividend`, `Transaction.PostEnteredInterest` |
| The matrix (R21, R25) | `CustodyRules` |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| POST | `/transactions/dividend` | `transactions.create` | 201 `{ "id" }` (R10–R20) | 400 / 401 / 403 / 404 / 409 |
| POST | `/transactions` | unchanged | 201 `{ "id" }`; `Interest` takes `taxes` (R22) | 400 for `taxes` on another type, and for `type` `Dividend` |
| POST | `/transactions/{id}/reverse` | unchanged | 201 `{ "id" }` for a hand-entered dividend or interest (R19) | 400 |
| GET | `/transactions`, `/transactions/{id}` | unchanged | `dividend.amountPerShare` and `dividend.exDate` may be `null` (R26) | — |

`{id}` routes take a GUID only. `Idempotency-Key` is required on both create routes.

### 8.1 Bodies

A dividend of a US share paid into an NZD account (example C):

```json
{
  "accountId": "<account publicId>",
  "instrumentId": "<instrument publicId>",
  "quantity": 10,
  "amountPerShare": 0.26,
  "grossAmount": 2.60,
  "taxSource": "Foreign",
  "imputationCredit": 0,
  "taxes": [
    { "kind": "ForeignWithholding", "ratePercent": 15, "amount": 0.39 }
  ],
  "fxRate": 1.69147,
  "bookedAt": "2026-11-20T00:00:00Z",
  "exDate": "2026-11-09",
  "recordDate": "2026-11-10",
  "paymentDate": "2026-11-13",
  "memo": "Quarterly dividend",
  "reference": "HATCH-2026-11"
}
```

Its item (fields of posting and dividends R50 not shown are unchanged):

```json
{
  "id": "<transaction publicId>",
  "type": "Dividend",
  "bookedAt": "2026-11-20T00:00:00Z",
  "memo": "Quarterly dividend",
  "reference": "HATCH-2026-11",
  "source": { "kind": "Manual", "id": null },
  "fx": { "baseCurrency": "USD", "quoteCurrency": "NZD", "rate": 1.69147000, "rateAsOfDate": "2026-11-20" },
  "dividend": {
    "corporateActionId": null,
    "instrument": { "id": "<instrument publicId>", "symbol": "AAPL", "name": "Apple Inc." },
    "quantity": 10.00000000, "amountPerShare": 0.26, "currency": "USD", "taxSource": "Foreign",
    "imputationCredit": 0.00, "reinvestedQuantity": null, "reinvestmentPrice": null,
    "exDate": "2026-11-09", "recordDate": "2026-11-10", "paymentDate": "2026-11-13"
  },
  "cash": [
    { "accountId": "<account publicId>", "role": "Principal", "amount": 4.40, "currency": "NZD", "tax": null },
    { "accountId": "<account publicId>", "role": "Tax", "amount": -0.66, "currency": "NZD",
      "tax": { "kind": "ForeignWithholding", "ratePercent": 15, "basisAmount": 4.40, "amount": 0.66, "currency": "NZD" } }
  ],
  "security": []
}
```

A dividend of a New Zealand company, with nothing optional but the tax and the credit (example A): `{ "accountId": "…", "instrumentId": "…", "quantity": 1000, "grossAmount": 2000.00, "taxSource": "NewZealand", "imputationCredit": 200.00, "taxes": [ { "kind": "Rwt", "ratePercent": 33, "amount": 526.00 } ], "bookedAt": "2026-11-20T00:00:00Z" }`. Its `Rwt` leg has `basisAmount` `2200.00`.

Interest with RWT (example E):

```json
{
  "accountId": "<account publicId>",
  "type": "Interest",
  "amount": 12.34,
  "taxes": [
    { "kind": "Rwt", "ratePercent": 33, "amount": 4.07 }
  ],
  "bookedAt": "2026-11-30T00:00:00Z",
  "memo": "November interest"
}
```

### 8.2 Errors

All refusals are ordinary validation (400 with `errors`, no problem `code`) except where named.

| Case | Response |
| --- | --- |
| `POST /transactions/dividend` on a Custodial account | 400 `errors.AccountId` (R2) |
| `POST /transactions` with `type` `Dividend` | 400 `errors.Type` (R10) |
| A field the dividend body does not have | 400 (R11) |
| `quantity`, `grossAmount`, `amountPerShare`, `taxSource` or a date outside R11 | 400 at the field |
| Imputation credit above the gross, or with `Foreign` | 400 `errors.ImputationCredit` |
| A tax row outside R5; a kind that does not fit the tax source | 400 `errors["Taxes[i].Kind"]`, `["Taxes[i].RatePercent"]` or `["Taxes[i].Amount"]` |
| A kind twice; `Rwt` with `Nrwt`; taxes above the gross | 400 `errors.Taxes` |
| More decimal places than the currency has | 400 at the field (R13, R22) |
| `fxRate` missing across currencies, or sent within one currency | 400 `errors.FxRate` (R14) |
| `taxes` on a type other than `Interest` | 400 `errors.Taxes` (R9) |
| `Interest` with a tax and `amount` not above 0 | 400 `errors.Amount` (R22) |
| `bookedAt` missing, without an offset, or in the future | 400 `errors.BookedAt` |
| Closed or closing account; Disabled Customer | 400, as posting R6, R7 |
| Reverse that would take cash below 0 | 400 (posting R11) |
| Missing `Idempotency-Key`; the same key with another body | 400; 409 |
| Unknown or another tenant's Account or Instrument | 404 |

Cross-tenant or wrong-collection id → **404**, not 403. Keep the resource catalog in [api-design.md](../api-design.md) pointing here for these rules.

---

## 9. UI

Page contracts: [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (D111–D113) and [customer-portal.md](../customer-portal/customer-portal.md) (C48, C49). Neither BFF gains a prefix: `/transactions` is already allowed.

**Adviser Portal**

| ID | Behaviour |
| --- | --- |
| D111 | On an External account the type selector of `/accounts/:id/transactions/new` (D18) offers "Dividend". The form has: an instrument picker; "Shares"; "Gross amount (<quote currency>)"; an optional "Amount per share (<quote currency>)"; "Company" ("New Zealand" / "Foreign", nothing preselected); "Imputation credit (<quote currency>)", shown only with New Zealand; a "Tax withheld" list where "Add tax" adds a row of Kind, Rate % and Amount (<quote currency>), up to three rows; only when the quote currency differs from the Account currency, a required "FX rate" labelled "1 <quote currency> = ? <Account currency>" (as D22); "Booked at"; optional Ex-date, Record date and Payment date; Memo and Reference. The Kind options follow the Company: New Zealand offers "RWT" and "NRWT", Foreign offers "Foreign tax" and "RWT". Under the list the form shows "Net <amount> <quote currency>" as display text; it does no other arithmetic and fills in no rate or amount. Submit posts `POST /transactions/dividend` with one `Idempotency-Key` per form open. A 400 is shown at its input; `errors.Taxes` under the list. The portal's copy of the matrix (D18) allows `Dividend` on an External account. A Custodial account never offers it. |
| D112 | On an External account the type selector of D18 offers "Interest", whose form has an "Amount (<Account currency>)" input (posting R20: either sign without tax), "Booked at", Memo, Reference and the "Tax withheld" list of D111, with amounts in the Account currency and Kind "RWT", "NRWT" or "Foreign tax". With at least one row the amount input is labelled "Gross interest" and must be above 0, and "Net <amount>" is shown under the list. The POST carries `taxes` only when the list has a row. |
| D113 | `/transactions/:id` of a hand-entered dividend shows what D100 shows for a Custodial one, with these differences: the Source line (D26) reads "Entered by hand"; there is no link to a corporate action; the shares line reads "<quantity> shares × <amountPerShare>" or, with no amount per share, "<quantity> shares"; a date that was not entered is not shown; Reverse is offered by D27. A hand-entered `Interest` with tax shows Gross, each tax as "<kind label> <rate> %" with its amount, and Net. The transactions list and account activity show "Dividend <symbol>" or "Interest" with the net and "incl. tax <amount>" under it, as D61. |

**Customer Portal**

| ID | Given | When | Then |
| --- | --- | --- | --- |
| C48 | A Customer with a hand-entered `Dividend` on an External account | Account page activity | The row of C42, with "<quantity> shares" alone when there is no amount per share. |
| C49 | A Customer with a hand-entered `Interest` that has tax | Account page activity | The row is labelled "Interest" and shows Gross, "Tax withheld (<kind label> <rate> %)" per tax and the net amount, as C30 without a period. Without tax the row is today's. |

### 9.1 Seeds and hosting

- `TestSeed` and the demo tenant seed: no change.
- No new configuration, secret, container, job or queue.

---

## 10. Tests

| Project | Where |
| --- | --- |
| Domain.UnitTests | `ValueObjects/EnteredTaxesTests.cs`, `Entities/EnteredDividendTests.cs`, `Entities/EnteredInterestTests.cs`, `Custody/CustodyRulesTests.cs` |
| Application.FunctionalTests | `Transactions/ExternalDividendTests.cs`, `Transactions/ExternalDividendIsolationTests.cs` (two tenants), `Transactions/ExternalInterestTaxTests.cs`; `Custody/CustodialManualEntryTests.cs` for the matrix of `POST /transactions` |
| Infrastructure.IntegrationTests | `AppliedSchema/TransactionsExternalIncomeSchemaTests.cs` |
| Adviser Portal | `src/features/transactions/CreateTransactionPage.test.tsx` (D111, D112), `src/features/transactions/EnteredIncomePages.test.tsx` (D113) |
| Customer Portal | `src/portal.test.tsx`, "income on External accounts" (C48, C49) |

--- | --- |
| Domain.UnitTests | `EnteredTaxes`: the shapes, a kind twice, `Rwt` with `Nrwt`, the pairs with the tax source. `PostEnteredDividend`: examples A–D leg by leg with their tax details and the dividend detail; the conversion and the FX detail of example C; a tax that converts to 0; taxes above the gross; a source other than `Manual`; no security leg. `PostEnteredInterest`: examples E and F; gross not above 0; no tax. `Reverse` of both copies the legs, the tax details and the dividend detail. `CustodyRules`: `Dividend` allowed on External, refused on Custodial |
| Application.FunctionalTests (`Transactions/`) | `POST /transactions/dividend`: no Bearer → 401 not 302; Customer → 403; examples A–D through the item; every 400 of §8.2; a Custodial account → 400 `errors.AccountId`; a Bank account and a disabled Instrument are accepted; no holding is created or changed; replay and 409 of the `Idempotency-Key`; reverse, and reverse refused when the cash was spent; SystemAdmin with and without `tenantId`. `POST /transactions`: `Interest` with `taxes` (examples E, F); `taxes` on `TransferIn` → 400; `Dividend` → 400 `errors.Type` on both custody modes; an `Interest` without `taxes` is unchanged. The Customer reads both through `GET /transactions`. The Customer Transactions Report shows the net and the Tax column for both. Two-tenant fixture: another tenant's Account → 404, another tenant's Instrument → 404, an Adviser's unassigned Customer → 404 |
| Infrastructure.IntegrationTests (`AppliedSchema/TransactionsExternalIncomeSchemaTests.cs`) | A dividend detail with no amount per share and no ex-date is stored; `AmountPerShare` of 0 is refused; existing rows keep their values |
| Adviser Portal, Customer Portal (component tests) | D111–D113, C48, C49 |

All of `Infrastructure.IntegrationTests` runs for this slice, because it adds a script.

---

## 11. Locked in this spec

No new ADR. ADR 0020 and custody-modes §1 apply as written: on an External account the caller decides the numbers. ADR 0015 applies to the new route.

| Item | Lock |
| --- | --- |
| Dividend on an External account | `POST /transactions/dividend`: one `Dividend` with a `Principal` leg of the gross, a `Tax` leg per entered tax, a dividend detail, source `Manual`; no security leg |
| Tax on a hand `Interest` | `taxes` on `POST /transactions`; the amount is then the gross |
| Entered tax | Kind, rate and amount as the caller states them; at most one per kind; never `Rwt` with `Nrwt`; at most the gross; nothing computed, nothing checked against a rate or the tax profile |
| Currency | Amounts of a dividend in the quote currency; the caller's `fxRate` across currencies, each amount rounded to the Account currency's decimal places |
| Matrix | `Dividend` / External allowed on its own route; Custodial unchanged |
| Script | `0060_external_income.sql` |
| Permissions, types, tax kinds, source kinds, jobs, problem codes, events | None added |

### Points confirmed

Each point states the recommendation, written into the rules above, and the alternative. All twelve were confirmed as recommended on 2026-11-27.

1. **One spec for the dividend and the tax on interest.** Recommended: both are the same idea (income an outside party paid, recorded with its tax), they share §4.2 and the portal's tax list, and together they close step 2 of the business roadmap. Alternative: two specs, the dividend first; the interest part is then R9, §4.4, D112 and C49.
2. **A route of its own for the dividend; `taxes` on `POST /transactions` for interest** (R10, R22). Recommended: a dividend's body shares almost nothing with the common body, and [api-design.md](../api-design.md) §7.7 gives a type that is new to hand entry its own route; `Interest` already lives on `POST /transactions`, and moving it is [future-plans.md](../future-plans.md) §4. Alternative: `POST /transactions/interest` now, with a decision on how long both routes take `Interest`.
3. **The caller states every tax; the tax profile is not read** (R4, R6). Recommended: the outside payer decided the tax, and the record must match the payer's statement; custody-modes §1 already puts every number of an External account with the caller. Alternative: the server works the tax out from the tax profile as it does on a Custodial account, which would disagree with the statement whenever the payer used another rate.
4. **The gross is entered as an amount; the amount per share is optional and never multiplied** (R11, R17). Recommended: brokers and registries round a dividend in their own way, and with an entered gross the account's cash always equals the statement. Alternative: require the amount per share and compute the gross as dividends R14 does, which keeps the two columns required and may differ from the statement by a minor unit.
5. **A dividend's amounts are in the quote currency, with the caller's `fxRate` across currencies** (R14). Recommended: it is the shape of an External `Buy` (external-caller-numbers R7) and of a Custodial dividend (dividends R32), a dividend statement states the dividend's own currency, and the FX detail keeps the rate for a later tax report. The net can differ from the outside statement by a minor unit when the broker converted the net in one step. Alternative: every amount in the Account's currency with no rate, which is exact but makes the Adviser convert the gross and the tax by hand.
6. **`ratePercent` is required on an entered tax** (R5). Recommended: the tax detail needs no change, the ledger pages already show "RWT 33 %", and a payer's statement normally states the rate. Alternative: an optional rate and a nullable column, for statements that show only an amount.
7. **No reinvestment on an External account** (§2 Out, R16). Recommended: a `Dividend` followed by a `Buy` at the plan price gives the same cash, quantity, cost and taxable income, with no security leg, holding rule or reverse guard in this slice. The link between the two Transactions is not recorded. Alternative: an optional `reinvestment: { quantity, price }` that adds the `Reinvestment` leg and the security leg of dividends R40.
8. **No holding check, any account type, a disabled Instrument allowed** (R10). Recommended: the shares may be held at a registry and the dividend paid into a bank account, an External ledger is often entered after the fact and out of order, and nothing is bought. Alternative: require a holding of the Instrument on the Account, and a Brokerage or Other account as buy-sell R6 does.
9. **`ForeignWithholding` on a hand `Interest`** (R9). Recommended: an External account may be an overseas bank account, where the other country withholds from the interest (example F). Alternative: `Rwt` and `Nrwt` only, as on an interest run.
10. **The three dates are optional, the ex-date included** (R11, R17). Recommended: a dividend statement shows the record date and the payment date and rarely the ex-date, and no rule of this slice reads a date. Alternative: require the ex-date, as a corporate action does, and keep the column `NOT NULL`.
11. **`POST /transactions` refuses `Dividend` in the validator, and the Custodial message moves to the new route** (R2, R10, R21). Recommended: one create route per type. It changes the note from landing of dividends R43: the two messages a `Dividend` got on `POST /transactions` are replaced by the message for a type that route does not take. Alternative: keep `Dividend` passing the type check of `POST /transactions` so that a Custodial account still gets the R43 message there.
12. **No new permission; reverse by hand** (R1, R19). Recommended: it is hand entry on an External account like every other type there. Alternative: none considered.

Still open, not this slice:

- A reinvested dividend as one Transaction on an External account; the future of the `Scrip` name ([future-plans.md](../future-plans.md) §4)
- An income or dividend report, a tax statement for the Customer, the FIF rules and a foreign tax credit in NZD ([future-plans.md](../future-plans.md) §1 step 6)
- Whether hand-entered interest needs a tax source (New Zealand or foreign payer) for a tax report, when it has no tax leg to tell
- Applying an announced corporate action to External accounts, and entering one dividend for several accounts
- Moving `Interest` and the other hand types to their own routes ([future-plans.md](../future-plans.md) §4)
- Multi-currency cash in one Account ([future-plans.md](../future-plans.md) §5): the `fxRate` of R14 is the answer for External accounts until then
- Everything dividends §11 leaves open for Custodial accounts

---

## 12. Changes to other docs

Each doc was changed in place when this spec was accepted (2026-11-27).

| Doc | Change |
| --- | --- |
| [dividends.md](dividends.md) | R39: the amount per share and the ex-date are optional on a hand-entered dividend. R43 and its note from landing: `Dividend` / External is allowed on `POST /transactions/dividend`; `POST /transactions` refuses the type in the validator. R50: `corporateActionId`, `amountPerShare` and `exDate` may be `null`. §2 Out and "still open": answered here |
| [interest-run.md](interest-run.md) | R25: a `Tax` leg on `Interest` may be of all three kinds; `ForeignWithholding` there comes from hand entry. R29: a hand `Interest` may have `Tax` legs. §2 Out: answered here |
| [custody-modes.md](custody-modes.md) | R5 matrix row `Dividend`: External allowed. §4.2.1: the Custodial `Dividend` message on the new route under `errors.AccountId`. "What follows from the matrix": the last bullet goes |
| [posting.md](posting.md) | R16: `Dividend` is not entered on `POST /transactions`. R20: a hand `Interest` with tax has a `Principal` leg above 0. §5 type table: `Dividend` by hand on `POST /transactions/dividend`; `Interest` may have `Tax` legs on an External account |
| [external-caller-numbers.md](external-caller-numbers.md) | §1: a row for dividends and interest. R4: `taxes` is refused on every type but `Interest` |
| [transaction-source.md](transaction-source.md) | Kind `Manual` also marks a hand-entered `Dividend` |
| [customers.md](customers.md) | The tax profile decides withholding on Custodial accounts only |
| [reports.md](reports.md) | §9.1: a hand-entered dividend and interest with tax show the net and the Tax column |
| [api-design.md](../api-design.md) | §7.7: the route, the by-custody-mode table, the type-specific routes paragraph; change log |
| [domain-model.md](../domain-model.md), [database-design.md](../database-design.md) | The factories and `EnteredTax`; the two nullable columns and script `0060` |
| [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md), [customer-portal.md](../customer-portal/customer-portal.md) | D111–D113; C48, C49; C30 and C42 point to them |
| [glossary.md](../glossary.md) | Entered dividend and Entered tax become accepted; Dividend, Dividend detail, Tax detail and Withholding tax gain the hand-entered case |
| [features/README.md](README.md), [future-plans.md](../future-plans.md) §1 step 2 and §9, [custodial-roadmap.md](../roadmaps/custodial-roadmap.md) §13.1 | Status: step 2 of the business roadmap is complete |
