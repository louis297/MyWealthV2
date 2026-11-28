---
title: "Tax year income summary"
status: review
phase: 2
language: en
owner: ""
created: 2026-11-28
last_updated: 2026-11-28
related:
  - ../roadmaps/fif-roadmap.md
  - ../future-plans.md
  - reports.md
  - tax-year-events.md
  - fif-report.md
  - fif-income.md
  - tax-cost-threshold.md
  - instrument-tax-attributes.md
  - dividends.md
  - external-income.md
  - interest-run.md
  - custody-mode-grouping.md
  - fx-rates.md
  - market-data-history.md
  - demo-tenant-seed.md
  - customers.md
  - bff-adviser-portal.md
  - bff-customer-portal.md
  - ../adviser-portal/adviser-portal-reports.md
  - ../customer-portal/customer-portal.md
  - ../deployment.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
---

# Tax year income summary

Step 6 of [fif-roadmap.md](../roadmaps/fif-roadmap.md) §9. It covers roadmap item **TR6**, the last and optional item, and writes decision **Q18** as rules. It answers what the earlier specs left to it: `Interest` in a tax year (tax-year-events R17), the dividends of exempt and New Zealand holdings (tax-year-events R26, fif-report R18), and the tax statement for dividends and interest that interest-run point 19, dividends and external-income left to this step.

The FIF report says what the FIF rules make of a Customer's foreign shares. It says nothing of what the Customer was paid under the ordinary tax rules: the dividends of New Zealand companies and of exempt holdings, the dividends of attributing holdings when the FIF rules do not apply, and interest. The ledger holds each of these payments with the tax taken from it, in the Account's currency, by UTC day. This slice adds them up for one Customer and one tax year, in NZD: the **income summary**.

It has two parts. The tax year read ([tax-year-events.md](tax-year-events.md)) gains the interest of the year and the sums, as two fields. A fifth entry in the reports catalog ([reports.md](reports.md)), scope `Customer`, prints them as a PDF, with an entry point in each portal.

New: fields `interest` and `income` on `GET /users/customers/{customerId}/tax-years/{taxYear}`; Domain types `TaxInterest`, `TaxYearInterest`, `TaxYearIncome`, `IncomeGroup`, `IncomeIncompleteReason` and `IncomeNotice`, and the function `TaxLedger.BuildInterest`; catalog report `customer-income-summary` (script `0063_income_summary_report.sql`, one row); Application types `CustomerIncomeSummaryBinder`, `CustomerIncomeSummaryReport`, `CustomerIncomeSummaryPdf` and `CustomerIncomeSummaryReportGenerator`; a branch in `RequestReport`; the fifth grant of the demo seed and of `TestSeed`; the tax year form of each portal offered for a second report. No route, table, permission, problem code, domain event, `TransactionType`, port, job or setting. No write to the ledger. The FIF report does not change. No simulator change.

**Status is `review` (2026-11-28).** It is not a build contract. The twenty points of §11 wait for confirmation; each states a recommendation, and the rules are written as recommended. The changes to other docs in §12 are a plan until the spec is accepted.

The tax statements in this spec were read from Inland Revenue on 2026-11-28 (§14): the individual return guide and the overseas income summary form for the first time, the FIF guide and the fact sheet again, each from the pages of its PDF. The sentences the report prints about the return are the platform's summary of those pages. Nothing here is tax advice, and the report says the same of itself.

---

## 1. Summary

| | Today | This slice |
| --- | --- | --- |
| Dividends of a tax year | In the tax year read, as events of each tax holding. The FIF report adds them up for attributing and not classified holdings only | Added up for every tax holding, in four groups by the FIF treatment of the Instrument |
| Interest of a tax year | Not read by anything | Read: every `Interest` Transaction of the Customer's Accounts in the tax year, in NZD |
| Tax taken | On each `Tax` leg | Added up by kind: RWT, NRWT, foreign tax; and the imputation credits |
| Form | — | Two fields of the tax year read (JSON), and a PDF stored as a report execution |
| An `Overseas` Customer | Gets no FIF report | Gets this report. It lists what was paid; it does not turn on the FIF rules |
| Prices | Needed for the FIF report | Not needed. A holding with no price does not make the income summary incomplete |

The rule that distinguishes this slice: it adds up what was paid and what was taken, and decides nothing. It does not say which of the dividends a person returns, which rules apply to them, or what tax follows.

### 1.1 Worked example

Roadmap T12 asks for a worked example. This one extends the example of tax-year-events §1.1: the same Customer, a New Zealand resident, with Account **A1** "Trading" (Custodial, USD) and **A2** "Old broker" (External, NZD), and the Instrument `AAA` (issuer country `US`, `Attributing` from tax year 2025) with its two dividends T4 and T9. The figures are made up; the tests use them as written. All times in the tables are UTC. Tax year 2026 runs from 31 March 2025 11:00 to 31 March 2026 11:00.

Two more Instruments, both held on A2 since before tax year 2026: `KIW` "Kiwi Power Ltd", quoted in NZD, issuer country `NZ`, `NotForeign` from tax year 2025; and `OZB` "Oz Bank Ltd", quoted in AUD, issuer country `AU`, `ExemptAsxListed` for tax year 2026. AUD has 2 decimal places.

The Tenant's USD interest rate schedule is effective from 1 January 2026. The Customer's tax profile: `NewZealand`, IRD number held, RWT rate 33.

The ledger, besides T1 to T12:

| # | `bookedAt` | Account | Transaction | Cash legs |
| --- | --- | --- | --- | --- |
| I1 | 30 Sep 2025 00:00 | A2 | `Interest` by hand: 120.00, tax `Rwt` 17.5 % 21.00 | +120.00 and tax −21.00 NZD |
| I2 | 15 Oct 2025 00:00 | A2 | `Interest` by hand: −8.00, interest the broker charged | −8.00 NZD |
| D1 | 12 Dec 2025 00:00 | A2 | `Dividend` by hand, `KIW`: 1,000 shares, gross 720.00 NZD, tax source `NewZealand`, imputation credit 280.00, tax `Rwt` 33 % 50.00 | +720.00 and tax −50.00 NZD |
| I3 | 1 Feb 2026 00:30 | A1 | `Interest` (interest run), 1 to 31 January 2026, 31 days | +4.10 and tax −1.35 (`Rwt`) USD |
| I4 | 1 Mar 2026 00:30 | A1 | `Interest` (interest run), 1 to 28 February 2026, 28 days | +3.80 and tax −1.25 (`Rwt`) USD |
| D2 | 20 Mar 2026 00:00 | A2 | `Dividend` by hand, `OZB`: 200 shares, gross 300.00 AUD, tax source `Foreign`, tax `ForeignWithholding` 15 % 45.00 AUD, the caller's rate 1.0900 | +327.00 and tax −49.05 NZD |
| I5 | 1 Apr 2026 00:30 | A1 | `Interest` (interest run), 1 to 31 March 2026, 31 days | +4.30 and tax −1.42 (`Rwt`) USD |

Stored rates USD → NZD, besides those of tax-year-events §1.1: 1.7200 for 30 January 2026 and 1.7500 for 27 February 2026. 1 February and 1 March 2026 are Sundays and have no row of their own.

**Dividends of tax year 2026.**

| Group | Holding | Country | Payments | Dividends | Imputation credits | Gross dividends | Foreign tax | RWT | NRWT |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| New Zealand companies | `KIW` | New Zealand | 1 | 720.00 | 280.00 | 1,000.00 | 0.00 | 50.00 | 0.00 |
| Foreign companies outside the FIF rules | `OZB` | Australia | 1 | 327.00 | 0.00 | 327.00 | 49.05 | 0.00 | 0.00 |
| Attributing interests | `AAA` | United States of America | 2 | 117.95 | 0.00 | 117.95 | 17.69 | 21.23 | 0.00 |
| Not classified | — | | 0 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 |
| All dividends | | | 4 | 1,164.95 | 280.00 | 1,444.95 | 66.74 | 71.23 | 0.00 |

`AAA` is T4 and T9 as tax-year-events §1.1 converts them: 59.15 + 58.80, 8.87 + 8.82, 10.65 + 10.58. `KIW` and `OZB` were booked on an NZD Account and are used as booked.

**Interest of tax year 2026.**

| # | From | Account | In the Account's currency | Rate | Interest | RWT |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | I1 | A2 | 120.00 NZD, RWT 21.00 | as booked | 120.00 | 21.00 |
| 2 | I2 | A2 | −8.00 NZD | as booked | −8.00, interest charged | 0.00 |
| 3 | I3 | A1 | 4.10 USD, RWT 1.35 | 1.7200 (30 January 2026) | 4.10 × 1.72 = 7.05 | 1.35 × 1.72 = 2.32 |
| 4 | I4 | A1 | 3.80 USD, RWT 1.25 | 1.7500 (27 February 2026) | 3.80 × 1.75 = 6.65 | 1.25 × 1.75 = 2.19 |

| Account | Payments | Interest received | Interest charged | RWT | NRWT | Foreign tax |
| --- | --- | --- | --- | --- | --- | --- |
| A2 "Old broker", held elsewhere, NZD | 2 | 120.00 | 8.00 | 21.00 | 0.00 | 0.00 |
| A1 "Trading", held on platform, USD | 2 | 13.70 | 0.00 | 4.51 | 0.00 | 0.00 |
| All interest | 4 | 133.70 | 8.00 | 25.51 | 0.00 | 0.00 |

**Tax taken, over dividends and interest:** RWT 96.74, NRWT 0.00, foreign tax 66.74. Imputation credits 280.00.

The income summary is complete. It carries the notices `ExternalFigures` and `InterestCharged`.

What the example shows:

- **Interest is in the tax year it reached the account** (R4). I5 is the interest of March 2026. The interest run posted it at 00:30 UTC on 1 April, which is 13:30 on 1 April in New Zealand: it is in tax year 2027. Tax year 2026 holds the postings made from 1 April 2025 to 1 March 2026.
- **The rate of a Sunday is Friday's** (R5). I3 and I4 are converted at the latest stored row on or before the UTC date of `bookedAt`, as every amount of the tax year read is.
- **An NZD leg is used as booked** (R5). D2 was paid in AUD and entered on an NZD Account with the caller's rate. Its cash legs are the amounts. The stored AUD rate is not read.
- **Interest charged is not taken off interest received** (R8). I2 is listed, and the total of interest received is 133.70, not 125.70.
- **The gross dividend of a New Zealand company includes its imputation credits** (R7). `KIW` paid 720.00 with credits of 280.00, and Inland Revenue's return asks for 1,000.00.
- **The dividends of an attributing interest are listed, and the report does not say whether they are returned** (R6). If the FIF rules apply to the Customer, FIF income takes their place; if not, they are overseas dividends. The FIF report of the same year shows the threshold test.
- **No price is read.** `KIW` has no stored price (NZX is not covered). The income summary is complete all the same.

---

## 2. Scope

**In**

- Domain: `TaxInterest`, `TaxYearInterest`, `TaxYearIncome` and its enums; `TaxLedger.BuildInterest`; `CustomerTaxYear` gains `Interest` and `Income`; `LedgerFact` gains the interest detail
- Application: `CustomerTaxLedger` also reads the `Interest` Transactions of the tax year; `GetCustomerTaxYear` maps the two fields; `CustomerIncomeSummaryBinder`, a branch in `RequestReport`, `CustomerIncomeSummaryReport`, `CustomerIncomeSummaryPdf`, `CustomerIncomeSummaryReportGenerator`
- Database: script `0063_income_summary_report.sql`, one row in `Reports`
- Infrastructure: the demo seed and `TestSeed` grant the report
- Adviser Portal and Customer Portal: the tax year form is offered for the report
- Tests listed in §10

**Out**

- Tax payable, the foreign tax credit allowed, the limit on an imputation credit, a refund of RWT, a total of taxable income, and which of the dividends a person returns (roadmap §7). The summary lists and adds up
- Whether the FIF rules apply to the Customer. The dividends of attributing interests are shown for both answers (roadmap Q14)
- The IR3 return, the overseas income summary form (IR1261), an RWT or NRWT withholding certificate, a dividend statement of a company, and a filing with Inland Revenue. The summary is none of them
- Paying the withheld tax to Inland Revenue and the investment income report a payer files (interest-run, dividends: still out)
- Income that is not a dividend or interest: a gain or a loss on a sale, a taxable bonus issue, a fee rebate, rent, salary. A `Scrip` gets a notice (R12)
- The country of interest. An Account has no country (R14)
- Interest accrued and not yet posted. Only a posted `Interest` Transaction is income (R3)
- A change to the FIF report: its section 7 keeps its own sums of the tax taken from dividends (fif-report R22)
- A schedule, an event trigger, and a run over every Customer of a Tenant (R17)
- A data file beside the PDF, and a portal page that shows the figures on screen
- An estimate for a tax year in progress (roadmap Q13)
- A dated tax residency, and a transitional resident (roadmap §7)
- A change to the demo catalog or its tax attributes (§9.1)

---

## 3. Stories

1. As a Customer I download, for a tax year that has ended, one PDF with every dividend and every interest payment recorded for me, in NZD, with the tax taken from each and the imputation credits.
2. As an Adviser I do the same for one of my Customers, on the Customer page.
3. As a reader below the FIF threshold I find the dividends of my foreign shares, per holding and per country, with the foreign tax, so that I can return them as overseas income.
4. As a reader I see my New Zealand dividends apart from my foreign ones, with the gross dividend as Inland Revenue's return asks for it.
5. As a reader I see on the first page whether the summary is complete, and if not, which payment is missing what.
6. As a reader I can check a figure from the PDF alone: each payment is listed with its amount in the account's currency and its rate with its date.
7. As a developer I read the same sums as JSON from the tax year read.
8. As an Adviser I cannot make the report for another Adviser's Customer, and a firm cannot make it for another firm's (404).

---

## 4. Rules

### 4.1 The figures

| ID | Rule |
| --- | --- |
| R1 | **Where they are.** `GET /users/customers/{customerId}/tax-years/{taxYear}` gains two fields: `interest`, the interest items of the tax year (R3), and `income`, the income summary (R13). The route, its policy `accounts.read`, its scope, its 404 and its refusals do not change (tax-year-events R1, R4, R30). Every caller who may read the Customer sees both fields. Nothing is stored and nothing else of the answer changes: `complete` and `incompleteCount` of the tax year stay what tax-year-events R27 says, and the income summary has its own `complete` (R11). |
| R2 | **Dividends.** The dividends of the income summary are the `Dividend` events of the tax year read (tax-year-events R11), of every tax holding whatever its treatment, with the NZD figures the read already gives them: `gross`, `foreignTax`, `rwt`, `nrwt` and `imputationCredit`. This slice reads no dividend a second time and converts none. A reinvested dividend counts once, at its gross: the reinvested amount is the purchase of tax-year-events R12 and not a second payment. A reversed dividend and its Reversal are in no event and so in no sum. |
| R3 | **Interest items.** One per `Interest` Transaction that has a cash leg on an Account of the Customer, of either custody mode and of any status, with `Start(Y) ≤ bookedAt < End(Y)`, and that tax-year-events R7 does not leave out: a reversed Transaction and a `Reversal` give none. From the legs on the Account: `gross` is the `Principal` leg, signed; `rwt`, `nrwt` and `foreignTax` are the `Tax` legs of those kinds, each as a positive figure and `0` when there is no such leg. `period` is the interest detail of an interest run's posting (interest-run R26): `{ from, to, days }`; it is `null` on a hand-entered `Interest` (external-income R23). The items are in the order of tax-year-events R8, with `sequence` 1 for the first of the year. Interest accrued and not posted is not an item. |
| R4 | **The tax year of a payment.** A dividend and an interest item are in the tax year of their `bookedAt`, the instant the money reached the account (tax-year-events R3, R11). On a Custodial account the interest run posts the interest of a month after the month has ended (interest-run R21, R23), so the interest of March is booked in April and is in the next tax year. On an External account `bookedAt` is the caller's. Sources: S3 p.15, interest "you received" between 1 April and 31 March; p.17, "in the income year you received the interest". |
| R5 | **An amount in NZD (roadmap T3, T4).** Each figure of an interest item is turned into NZD by tax-year-events R18 and R21, as each figure of a dividend is: a leg of an NZD Account is used as booked, with the status `Stated`; a leg of an Account in another currency is converted at the stored `FxRates` row of that currency → NZD with the latest date on or before the UTC date of `bookedAt`, rounded to NZD's decimal places, with the status `Converted` and the rate row it used. A row older than 4 calendar days, or no row, leaves every figure of the item `null` with the status `StaleRate` or `NoRate`. Each figure is converted on its own, and a sum is a sum of converted figures. There is one rule for dividends and interest, of every Account, of every tax year. Sources: S3 p.19 and S4, "the exchange rate for the day you received your overseas income"; S5, rates "from other countries' central banks". |
| R6 | **Groups.** A dividend is in the group of its tax holding, by the FIF treatment of the Instrument for the tax year (tax-year-events R26): `NewZealand` for `NotForeign`; `ForeignExempt` for `ExemptAsxListed` and `ExemptOther`; `Attributing` for `Attributing` and `AttributingNoFdr`; `NotClassified` for `NotClassified`. The four groups are always given, in this order, an empty one with no holding and sums of 0. The group does not follow the `taxSource` of the dividend: a dividend whose tax source disagrees with the issuer country is in the group of its holding, as booked, with a notice (R12). The group says how the Instrument is classified. It does not say which rules a person applies: the dividends of the `Attributing` group are overseas dividends when the FIF rules do not apply to the person, and are not returned as dividends when they do. Sources: S1 p.7, p.9, p.10; S2 ¶1, ¶2, ¶7, ¶10. |
| R7 | **Dividend sums.** A sum is `{ count, gross, imputationCredit, grossWithImputation, foreignTax, rwt, nrwt }`: the number of dividends, and the sums of their NZD figures. `grossWithImputation` is `gross + imputationCredit`: the dividend before any tax was taken, with its imputation credits, which is the gross dividend Inland Revenue's return asks for. Sums are given per tax holding that has a dividend in the year; per issuer country within the `ForeignExempt` and the `Attributing` group, a holding with no issuer country under `null`; per group; and over all dividends. Source: S3 p.17, the net dividend, the imputation credit and the RWT credits: "Add all these amounts together to work out your gross dividend." |
| R8 | **Interest sums.** Per Account that has an interest item in the year, and over all Accounts: `{ count, received, charged, rwt, nrwt, foreignTax }`. `received` is the sum of the items whose `gross` is above 0. `charged` is the sum of the items whose `gross` is below 0, as a positive figure. The two are never netted: interest charged is not negative income, and whether it reduces what a person returns depends on what it was charged for. `count` counts both. Source: S3 p.15, "negative interest" of a broken term deposit "may reduce the amount of interest you need to declare", in one case and not in others. |
| R9 | **Tax taken.** `taxWithheld` is `{ rwt, nrwt, foreignTax }` over all dividends and all interest items, and `imputationCredit` is the sum over all dividends. The amounts are as booked. The summary computes no credit and no tax (R14). |
| R10 | **A figure that is missing.** A dividend or an interest item whose figures could not be converted (R5; tax-year-events R21) is listed with its status and `null` figures. Every sum it belongs in is `null`: of its holding or Account, of its country, of its group, the total, and `taxWithheld`. Its `count` still counts it. No number takes the place of a missing one, and a sum never leaves a payment out silently (roadmap T5). |
| R11 | **Complete.** The income summary lists its reasons as `{ reason, instrumentId, accountId }`, each once. `AmountMissing`: a dividend of that tax holding, or an interest item of that Account, has a missing figure (R10). `NotClassified`: a tax holding that is not classified has a dividend in the year; the sums are there, and the summary cannot say which group they belong in. `income.complete` is `true` when there is no reason. It does not follow `complete` of the tax year: a holding with no price, an unknown tax cost and a holding with no dividend that nobody classified do not make the income summary incomplete. An incomplete summary is not an error: the answer is 200. |
| R12 | **Notices.** `{ notice, instrumentId, accountId }`, each once; a notice does not make the summary incomplete. `ExternalFigures`: a dividend or an interest item of the year is on an External account, and the figures are the caller's ([custody-mode-grouping](custody-mode-grouping.md)); one per Account. `DividendTaxSourceDisagrees`: the tax holding has that notice (tax-year-events R29) and a dividend in the year. `Scrip`: the tax holding has the notice `Scrip` (tax-year-events R13): shares were issued in place of a dividend, they are in no sum, and the platform cannot know whether the issue was a taxable dividend. `InterestCharged`: the Account has an interest item below 0. Source for `Scrip`: S3 p.18, "shares from a taxable bonus issue or a non-cash dividend" are income. |
| R13 | **Item.** `interest` is a list of `{ sequence, bookedAt, transactionId, accountId, custodyMode, amount, status, ledger, fx, rwt, nrwt, foreignTax, period }`: `amount` is the gross in NZD, signed; `status` is `Stated`, `Converted`, `NoRate` or `StaleRate`; `ledger` is `{ currency, gross, rwt, nrwt, foreignTax }` in the Account's currency; `fx` is the rate row or `null`. `income` is `{ dividends, interest, taxWithheld, imputationCredit, complete, incomplete, notices }`. `income.dividends` is `{ groups, total }`; a group is `{ group, holdings, countries, total }`, a holding row `{ instrumentId, issuerCountry }` with the sum of R7, a country row `{ issuerCountry }` with the sum; `countries` is an empty list for the `NewZealand` and the `NotClassified` group. `income.interest` is `{ accounts, total }`; an account row is `{ accountId, custodyMode, currency }` with the sum of R8. Holdings are in the order of the read's holdings, countries by code with `null` last, accounts in the order of their first interest item. §8.1 shows the shape. |
| R14 | **What the summary does not compute.** No tax payable, no foreign tax credit, no limit on an imputation credit and no total of taxable income: each needs the person's whole return (roadmap §7). No statement that the FIF rules apply or do not apply (roadmap Q14). No country for interest: an Account has a currency and a custody mode and no country, so an interest item is not said to be New Zealand interest or overseas interest. No income of a holding or an account that is not in the ledger. |

### 4.2 The report in the catalog

| ID | Rule |
| --- | --- |
| R15 | **Catalog row.** Report `customer-income-summary` (PublicId `4b6e2d91-3f7a-4c18-9d52-8a1e7c0b6f55`), name "Tax Year Income Summary", function "Dividends and interest paid to one customer in one New Zealand tax year, in New Zealand dollars, with the tax taken from them and the imputation credits.", scope `Customer`, allowed triggers `["manual"]`, active. Script `0063` inserts it when no row has the code. A SystemAdmin grants it per Tenant like any report (reports R3); a new Tenant is not granted it. |
| R16 | **Access.** No new permission. As fif-report R2 and R3: `reports.customer.create` to request and `reports.customer.read` to list and download; a TenantAdmin for any Customer of the Tenant, an Adviser for an assigned Customer, a Customer for themselves; any other `customerId`, a Customer of another Tenant, a missing one, a report that is not granted or not active → 404; missing / dead Bearer → 401, never 302; a SystemAdmin → 403. An execution is seen as every Customer-scoped execution is (reports §8.1). |
| R17 | **Manual only, one Customer per request.** As fif-report R4 and R5: no schedule and no event trigger, a schedule that names the report → 404, also when a SystemAdmin has added `"scheduled"` to the catalog row; no run over every Customer of a Tenant or of an Adviser. |
| R18 | **Parameters.** `{ "customerId": "uuid", "taxYear": 2026 }`, with the checks and the messages of fif-report R6 and R7, word for word: both required (400 `errors.customerId`, `errors.taxYear`); `taxYear` a JSON whole number, and no other field (400 `errors.parameters`); below 2009 → 400 `errors.taxYear`, `taxYear must be 2009 or later.`; a tax year that has not ended, or whose 31 March is not final yet → 400 `errors.taxYear`, `Tax year {Y} can be reported from 2 April {Y}.` The stored snapshot is `{ customerId, taxYear }`. The summary reads no price, but it is a field of the tax year read and takes the tax years that read answers (tax-year-events R4). |
| R19 | **Any tax residency.** The report is made for a Customer whose tax residency is `NewZealand` or `Overseas`, and the request does not read the tax profile. The summary lists what was paid and what was taken, which does not depend on the residency; NRWT is taken only from an `Overseas` Customer (interest-run R4, dividends R33), and this is the report that lists it. The generator reads the residency on file, and the PDF prints it and chooses statement 2 by it (R25). The profile has no date: it is the residency when the report is made, not that of the tax year. Sources: S2 ¶5, a person who is not tax resident applies "the ordinary tax rules to your New Zealand sourced income"; S6, "If you are not a New Zealand tax resident, you'll need to file an IR3NR instead." |

### 4.3 Generation

| ID | Rule |
| --- | --- |
| R20 | **What is read.** The generator calls `CustomerTaxLedger.ReadAsync` once, for the Customer and the tax year of the snapshot, and takes the platform time just before the call as `ledgerReadAt`. It also reads the Tenant's name, the Customer's name and tax residency, and the names of the Customer's Accounts. Every figure of the PDF is a figure of `interest`, of `income` or of a `Dividend` event of that one read. The report computes no figure of its own, not a sum and not a count. |
| R21 | **A read, and one stored copy.** As fif-report R10 and R11: generation writes the result file and the status of its execution and nothing else; the PDF is the only stored copy and prints what it used (R29, R30); the execution has no `asOf`; a later request is a new execution and can differ (roadmap Q15, T9). |
| R22 | **Complete.** The report is complete when `income.complete` is `true` (R11). An incomplete report is still made, and its execution is `Succeeded`. The first page says "Incomplete", section 7 says what is missing, and every page says "Incomplete" in its footer. Whether the tax year or the FIF report of the same year is complete does not matter here. |
| R23 | **Times and figures.** As fif-report R13 and R14: every instant is printed in New Zealand time as `yyyy-MM-dd HH:mm` by `NewZealandTime`; a date with no zone (the date of a rate, the days of a period) is printed as it is; an NZD figure has NZD's decimal places and no currency code; a figure in another currency has its decimal places and its code; no thousands separator; a rate has up to 8 decimal places and no trailing zeros; a figure that is `null` is printed as "not available", never as 0 and never as an empty cell. |

### 4.4 What the PDF prints

The PDF is built in code with QuestPDF, A4 landscape, as reports §9.3 says. It has a header, seven numbered sections in the order below, and a footer on every page with the Customer's name, "Tax Year Income Summary, tax year {Y}", "Page {n} of {m}" and, when R22 says so, "Incomplete". The words of each sentence and label are those of this section and of §4.6.

| ID | Rule |
| --- | --- |
| R24 | **Header.** The report name; "Tenant: {name}"; "Customer: {name}"; "Tax year: {Y} (1 April {Y − 1} to 31 March {Y}, New Zealand time)"; "Tax year in UTC: {from} to {to}"; "Generated: {New Zealand time} New Zealand time ({UTC} UTC)"; "Ledger read at:" in the same form; "Tax residency on file: New Zealand" or "Tax residency on file: Overseas"; "All amounts are in New Zealand dollars (NZD). All times are New Zealand time."; and "Status: Complete" or "Status: Incomplete. See section 7." |
| R25 | **Section 2, "About this report".** The statements of the table below, each printed as written. |
| R26 | **Section 1, "Summary".** A table with one row per group of R6 that has a dividend, then "All dividends", "Interest received" and "Tax taken and credits": the columns Payments, Amount, Imputation credits, Gross dividends, Foreign tax, RWT and NRWT. "Amount" is `gross` of a dividend sum and `received` of the interest total; the two credit columns are empty on the interest row; the last row has `taxWithheld` and `imputationCredit` and an empty Amount. With interest charged: "Interest charged: {charged}. It is not taken off the interest received." Under the table: "RWT and imputation credits are credits against New Zealand tax. Overseas tax paid may give a foreign tax credit. The credit is limited, and this report does not work it out." With no dividend and no interest item, R32 replaces all of it. Sources: S3 p.16, RWT "you can claim a credit for this"; p.22, the credit "is the lesser of the actual amount of tax paid on the overseas income or the amount of tax you would pay in New Zealand on the foreign income". |
| R27 | **Section 3, "Dividends".** The four groups in the order of R6, each with its title and its sentence (§4.6). A group has a table with one row per holding row: the symbol, the name, the issuer country by its name (instrument-tax-attributes R6; "not stated" when there is none), the label of the treatment (fif-report §4.6), and the sum of R7 in the columns Payments, Dividends, Imputation credits, Gross dividends, Foreign tax, RWT and NRWT; then, in the two foreign groups, one row per country row, "Country not stated" last; then the row "Total". A group with no dividend prints its title and "None.", and no sentence. Under the last group: "Dividends are before any tax was taken. Gross dividends are the dividends with their imputation credits." With no dividend in the year, the section prints "No dividends in this tax year." |
| R28 | **Section 4, "Interest".** A table with one row per account row: the Account's name, the custody label ("Held on platform" or "Held elsewhere", custody-mode-grouping R14), the Account's currency, and the sum of R8 in the columns Payments, Interest received, Interest charged, RWT, NRWT and Foreign tax; then the row "Total". Under it the sentence on the country of interest, and with interest charged the sentence on it (§4.6). With no interest item: "No interest in this tax year." |
| R29 | **Section 5, "Each payment" (roadmap T9).** "Dividends": one row per dividend, in the order of the groups, then of the holdings, then of the events: the time, the symbol, the Account's name, the custody label, the shares, the tax source, the gross in the Account's currency with its code, the rate with its date or "no conversion", and in NZD the dividend, the imputation credit, the foreign tax, the RWT and the NRWT, or "not available ({label of its status})". "Interest": one row per interest item, by `sequence`: the time, the Account's name, the custody label, the period as "{from} to {to}, {days} days" or "entered by hand", the gross in the Account's currency with its code, the rate with its date or "no conversion", and in NZD the interest, the RWT, the NRWT and the foreign tax. An item below 0 is labelled "charged". |
| R30 | **Section 6, "Exchange rates used".** Every rate row behind a dividend or an interest item, once each, grouped by pair: the pair as "{currency} to NZD", the date, the rate and the source. A date is the date of the row, not of the payment that used it. With none: "None. Every payment was booked in New Zealand dollars." Then "Ledger read at {time}". |
| R31 | **Section 7, "What to know about these figures".** First "What is missing", when the report is incomplete: one line per payment with a missing figure and one per reason `NotClassified`. Then "What these figures rest on": one line per notice. The sentences are those of §4.6. With neither: "Nothing to report." |
| R32 | **Nothing paid.** When the tax year has no dividend and no interest item, the PDF has its header, section 2 and one line: "No dividends and no interest in this tax year." The report is complete. |
| R33 | **What the report does not print.** No tax payable, no tax rate of the Customer, no credit allowed and no total of income to return (R14). No box number of a return. No IRD number: the platform holds none (interest-run R2). No holding, quantity, price or market value: the summary reads none. |

Statements of R25, in this order:

| # | Statement | When |
| --- | --- | --- |
| 1 | "This report is not tax advice. It lists the dividends and interest recorded for you on this platform and the tax taken from them. It does not say what income you return or what tax you owe." | Always |
| 2 | "Your tax residency on file is New Zealand, as recorded when this report was made. The platform does not record whether you are a transitional resident, or what your residency was during the tax year." | The residency is `NewZealand` |
| 2 | "Your tax residency on file is Overseas, as recorded when this report was made. What this report says about the return is written for a New Zealand tax resident. A person who is not a New Zealand tax resident files a different return." | The residency is `Overseas` |
| 3 | "This report covers the accounts recorded on this platform. Dividends and interest you were paid elsewhere are in it only if your adviser has recorded them here. It lists no other income: no gain on a sale, no rent and no salary." | Always |
| 4 | "Held elsewhere: recorded by your adviser from outside statements. The platform has not checked these figures." | The summary has the notice `ExternalFigures`. The sentence is that of custody-mode-grouping R15, word for word |
| 5 | "A payment is in the tax year in which it reached your account. Interest earned in March and paid in April is in the next tax year." | Always |
| 6 | "Amounts in another currency are converted to New Zealand dollars at the exchange rate of their day. Section 6 lists every rate used." | Always |
| 7 | "This report is not a form of Inland Revenue and not a withholding tax certificate. The payer of a dividend or of interest may send you a statement of its own." | Always |
| 8 | "This report shows the ledger as it was read at the time above. A report made later for the same tax year can differ, when a payment is entered, changed in date or reversed after that time, or when a rate is added." | Always |

Sources for the statements: S3 p.23 for the transitional resident in 2; S6 for the different return in 2; R4 for 5; R5 for 6; S3 p.16, "The interest payer will usually send you a RWT withholding certificate or similar statement", and p.17, "will send you a dividend statement", for 7; roadmap Q15 for 8.

### 4.5 Entry points and seeds

| ID | Rule |
| --- | --- |
| R34 | **Adviser Portal.** On the Customer page, the Generate dropdown of the Reports section lists the report when it is granted. Its form is the tax year form of fif-report R27: one field, "Tax year", a select of every tax year that can be reported today, the latest first and selected. `customerId` is the page's Customer. There is no schedule form. The History panel shows "Tax year {Y}" as the parameters summary. A 400 is shown at the form with the server's message. The page contract is [adviser-portal-reports.md](../adviser-portal/adviser-portal-reports.md), D115 (§12). |
| R35 | **Customer Portal.** On the Reports page, the same form with `customerId` set to the Customer themselves. A Customer whose tax residency is `Overseas` gets the report like any other. The page contract is [customer-portal.md](../customer-portal/customer-portal.md), C52 (§12). |
| R36 | **Grants.** `webapi --seed-demo` and `TestSeed` grant `customer-income-summary` to the demo tenant, with the four reports they grant today (demo-tenant-seed R8, fif-report R29): five grants. The rule is unchanged: idempotent on `(TenantId, ReportId)`, so a run on a demo tenant that has four grants adds the fifth; a platform-inactive report is not granted; a missing catalog code is an error. The log line of demo-tenant-seed R12 then reads `reports 5 granted, 0 existing` on a new tenant. Other tenants are not auto-granted. |

Policies: none added.

### 4.6 Labels and sentences

The groups of section 3 (R27):

| `group` | Title | Sentence |
| --- | --- | --- |
| `NewZealand` | New Zealand companies | "Inland Revenue's individual return asks for New Zealand dividends as the gross dividend, with the imputation credits and the RWT." |
| `ForeignExempt` | Foreign companies outside the FIF rules | "These holdings are classified as exempt from the FIF rules. Their dividends are overseas income under the ordinary tax rules. Inland Revenue's overseas income summary (IR1261) asks for overseas income by income type and by country, with the overseas tax paid. RWT is New Zealand tax, not overseas tax." |
| `Attributing` | Attributing interests | "If the FIF rules apply to you, these dividends are not returned as dividends: FIF income is returned in their place. If the FIF rules do not apply to you, they are overseas income under the ordinary tax rules, like the dividends of the group above. This report does not say which is the case. The FIF Report of the same tax year shows the threshold test and FIF income." |
| `NotClassified` | Not classified | "These holdings are not classified for tax year {Y}. This report cannot say which of the groups above their dividends belong to. Ask your adviser to have them classified." |

Sources: S3 p.17 for the first; S1 p.7, an individual with an exempt ASX-listed share "will usually need to account for dividends in their tax return", S2 ¶7, "you still apply the ordinary tax rules to those interests", S3 p.19 and S4, "a separate section for each jurisdiction and for each income type", and S3 p.24, an overseas dividend with "RWT in New Zealand", for the second; S2 ¶1, "If the FIF rules apply, you do not apply the ordinary tax rules in relation to any dividends you receive", S2 ¶2, S1 p.9, "you will pay tax only on dividends received", and S1 p.10, "dividends and other gains are not usually taxed separately", for the third. Each sentence is the platform's summary, not a quotation.

The sentences of section 4 (R28):

- The country of interest, always: "Interest from a New Zealand payer is New Zealand interest. Interest from overseas is overseas income. The platform does not record the country of an account, so this report does not say which of the two an amount is."
- Interest charged, with the notice `InterestCharged`: "Interest charged to an account is listed here and is not taken off the interest received. Whether it reduces your income depends on what it was charged for."

Sources: S3 p.15 and p.16, "If you received interest from overseas, convert your overseas interest and tax credits to New Zealand dollars and show the amounts at Question 17"; S3 p.15 for negative interest.

The labels of a status that leaves a figure missing (R29, R31): `NoRate` "No exchange rate"; `StaleRate` "Exchange rate too old", as fif-report §4.6. A tax source is printed as "New Zealand" or "Foreign".

What is missing (R31):

| Reason | Sentence |
| --- | --- |
| `AmountMissing`, a dividend | "{symbol}: The dividend of {time} could not be converted to New Zealand dollars ({label of its status}). It is in no total." |
| `AmountMissing`, an interest item | "{account}: The interest of {time} could not be converted to New Zealand dollars ({label of its status}). It is in no total." |
| `NotClassified` | "{symbol}: Not classified for tax year {Y}. Its dividends are in a group of their own." |

What these figures rest on (R31):

| Notice | Sentence |
| --- | --- |
| `ExternalFigures` | "{account}: The payments of this account were recorded by your adviser from outside statements. The platform has not checked them." |
| `DividendTaxSourceDisagrees` | "{symbol}: A dividend states a tax source that does not agree with the issuer country of the holding. Both are shown as recorded." |
| `Scrip` | "{symbol}: Shares were issued in place of a dividend. They are not in this report. The platform cannot know whether the issue was a taxable dividend." |
| `InterestCharged` | "{account}: Interest was charged to this account. It is listed in section 4 and is in no total of interest received." |

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `LedgerFact` | Value (existing) | Gains `Interest`: the period of the interest detail, `{ PeriodFrom, PeriodTo, Days }`, where the Transaction has one. Application fills it |
| `TaxInterest` | Value (new, `src/Domain/Tax`) | One `Interest` Transaction of the Customer as R3 reads it: the ordering key, `bookedAt`, the Account and its custody mode, the gross and the three taxes in the Account's currency, and the period |
| `TaxLedger` | Static (existing) | Gains `BuildInterest(facts)` → the interest of the Customer in the order of tax-year-events R8, with a reversed Transaction and a `Reversal` left out. `Build` does not change and still gives no event for an `Interest` |
| `TaxYearInterest` | Value (new) | A `TaxInterest` of the tax year with its `sequence`, its NZD figures, its status and the rate row it used (R5) |
| `IncomeGroup` | Enum (new) | `NewZealand = 0`, `ForeignExempt = 1`, `Attributing = 2`, `NotClassified = 3` (R6) |
| `IncomeIncompleteReason`, `IncomeNotice` | Enums (new) | `AmountMissing = 0`, `NotClassified = 1`; `ExternalFigures = 0`, `DividendTaxSourceDisagrees = 1`, `Scrip = 2`, `InterestCharged = 3` (R11, R12) |
| `TaxYearIncome` | Value (new) | The income summary. `Compute(holdings, interest)` holds R6 to R12: the groups, every sum, the missing figure, the reasons and the notices. No I/O, no clock |
| `CustomerTaxYear` | Value (existing) | Gains `Interest` and `Income`. `Compute` also takes the interest of the Customer and the rate rows it needs, slices it at the two instants of the tax year and converts it (R3, R5), then calls `TaxYearIncome.Compute` |
| `TaxHolding`, `TaxYearEvent`, `ThresholdTest`, `FifIncome`, `NewZealandTime` | Existing | No change |
| `Report`, `TenantReport`, `ReportExecution` | Existing | No change. The catalog gains a row, not a type |

```text
interest item         one per Interest Transaction on an Account of the Customer,
                      Start(Y) <= bookedAt < End(Y), not reversed, not a Reversal      (R3)
NZD                   NZD leg as booked; else Round(leg x rate(C -> NZD, UTC date), dp)  (R5)
group of a dividend   NotForeign -> NewZealand; Exempt* -> ForeignExempt;
                      Attributing* -> Attributing; no row -> NotClassified             (R6)
gross dividend        gross + imputation credit                                        (R7)
interest              received = sum of items above 0; charged = sum of items below 0  (R8)
a missing figure      every sum it belongs in is null                                  (R10)
```

Invariants:

- The total of the dividend sums equals the sum of its four groups, figure by figure, and a group's total equals the sum of its holding rows.
- Every `Dividend` event of the tax year is counted in exactly one holding row, and every interest item in exactly one account row.
- `grossWithImputation` equals `gross + imputationCredit` wherever both are given.
- `received` and `charged` are never below 0.
- A sum is `null` if and only if a payment it counts has a missing figure.
- `income.complete` is `true` if and only if `income.incomplete` is empty.
- A reversed Transaction and a Reversal are in no interest item and no sum.

Domain events: none. Nothing is an entity and nothing is stored.

[domain-model.md](../domain-model.md) §8.24 gains these types when this spec is accepted.

---

## 6. Database

| Table | Change | Index / FK |
| --- | --- | --- |
| `Reports` | One row, `customer-income-summary` (R15) | None |

Script: `database/schema/0063_income_summary_report.sql`. It inserts the row when no row has the code, as `0062` does for `customer-fif`. No table, column, constraint or index.

The read also uses `Transactions` of type `Interest` with their `TransactionCashLegs`, `TransactionTaxDetails` and `TransactionInterestDetails`, of the Customer's Accounts, within the tax year. The legs' `AccountId` indexes already reach them. Nothing new is stored.

[database-design.md](../database-design.md) §9 and §14 list the script and the row when this spec is accepted. Do not add EF migrations.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Service | `CustomerTaxLedger` (existing) | `ReadAsync` also loads the `Interest` Transactions of the Customer's Accounts booked in the tax year, not reversed and not a Reversal, with their interest detail; reads the rate rows their UTC dates need, as it does for the events; and passes them to `CustomerTaxYear.Compute`. Nothing else of the read changes. Its callers stay the query on `webapi` and the report generators on `worker` |
| Query | `GetCustomerTaxYear` (existing) | Maps `interest` and `income` onto the item (R13) |
| Static | `CustomerIncomeSummaryBinder` (new) | `Code` `customer-income-summary`. `Bind(parameters, today)` → `{ CustomerId, TaxYear, SnapshotJson }`, with the checks of R18. It may share the reading of the parameters with `CustomerFifBinder` |
| Command | `RequestReport` (existing) | A branch for the code: bind, then the Customer scope check the Customer Transactions Report uses. No tax residency check (R19). No `asOf` |
| Static | `CustomerIncomeSummaryReport` (new) | `Build(read, names, residency, generatedAt, ledgerReadAt)` → the content of the PDF as plain records: the header, the seven sections, the sentences and the labels of §4.4 and §4.6. No I/O and no arithmetic. The one place the words of the report are chosen |
| Static | `CustomerIncomeSummaryPdf` (new) | `Render(content)` → the PDF bytes, with QuestPDF. Layout only |
| Service | `CustomerIncomeSummaryReportGenerator` (new) | `IReportGenerator` for the code: R20, then `Build` and `Render`. Registered beside the other generators |
| Service | `CustomerFifReportGenerator`, `CustomerFifReport` (existing) | No change. The FIF report reads neither new field |
| Command | `CreateReportSchedule`, `UpdateReportSchedule`; job `ReportJob.RunScheduled` | No change. Each names the report codes that can be scheduled, and the new code is not among them (R17) |
| Seed | `DemoCatalog.ReportCodes` | Gains `customer-income-summary` (R36) |

Where each rule lives:

| Rule | Where |
| --- | --- |
| Which `Interest` Transactions, in which order (R3) | `CustomerTaxLedger` loads the facts; `TaxLedger.BuildInterest` decides |
| The tax year of an item and its NZD figures (R4, R5) | `CustomerTaxYear.Compute` |
| Groups, sums, the missing figure, reasons and notices (R6 to R12) | `TaxYearIncome.Compute` |
| Parameters and which tax years (R18) | `CustomerIncomeSummaryBinder` |
| Scope at the request (R16) | `RequestReport` |
| Every sentence and label (R24 to R33) | `CustomerIncomeSummaryReport.Build` |
| Pages, tables, footer (§4.4) | `CustomerIncomeSummaryPdf` |

---

## 8. API

No new route.

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/users/customers/{customerId}/tax-years/{taxYear}` | `accounts.read` | 200, the item with `interest` and `income` | as tax-year-events §8 |
| POST | `/reports/request` | `reports.customer.create` | 202 `{ "id" }` | 400 / 401 / 403 / 404 |
| POST | `/reports/schedules` | `reports.customer.create` | — | 404 for this report (R17) |
| GET | `/reports/executions`, `/reports/executions/{id}` | `reports.customer.read` | 200, `parameters` `{ customerId, taxYear }`, no `asOf` | as reports §8 |
| GET | `/reports/executions/{id}/download` | `reports.customer.read` | 200 `application/pdf` | 403 / 404 / 409 |

### 8.1 Bodies

`POST /reports/request`:

```json
{
  "reportId": "4b6e2d91-3f7a-4c18-9d52-8a1e7c0b6f55",
  "parameters": {
    "customerId": "<customer publicId>",
    "taxYear": 2026
  }
}
```

The two new fields of `GET /users/customers/{customerId}/tax-years/2026` for §1.1, with two of its four interest items and two of its four groups:

```json
{
  "interest": [
    {
      "sequence": 2, "bookedAt": "2025-10-15T00:00:00Z",
      "transactionId": "<I2 publicId>", "accountId": "<A2 publicId>", "custodyMode": "External",
      "amount": -8.00, "status": "Stated",
      "ledger": { "currency": "NZD", "gross": -8.00, "rwt": 0, "nrwt": 0, "foreignTax": 0 },
      "fx": null, "rwt": 0, "nrwt": 0, "foreignTax": 0, "period": null
    },
    {
      "sequence": 3, "bookedAt": "2026-02-01T00:30:00Z",
      "transactionId": "<I3 publicId>", "accountId": "<A1 publicId>", "custodyMode": "Custodial",
      "amount": 7.05, "status": "Converted",
      "ledger": { "currency": "USD", "gross": 4.10, "rwt": 1.35, "nrwt": 0, "foreignTax": 0 },
      "fx": { "rate": 1.72, "date": "2026-01-30", "source": "Frankfurter" },
      "rwt": 2.32, "nrwt": 0, "foreignTax": 0,
      "period": { "from": "2026-01-01", "to": "2026-01-31", "days": 31 }
    }
  ],
  "income": {
    "dividends": {
      "groups": [
        {
          "group": "NewZealand",
          "holdings": [ { "instrumentId": "<KIW publicId>", "issuerCountry": "NZ", "count": 1, "gross": 720.00, "imputationCredit": 280.00, "grossWithImputation": 1000.00, "foreignTax": 0, "rwt": 50.00, "nrwt": 0 } ],
          "countries": [],
          "total": { "count": 1, "gross": 720.00, "imputationCredit": 280.00, "grossWithImputation": 1000.00, "foreignTax": 0, "rwt": 50.00, "nrwt": 0 }
        },
        {
          "group": "ForeignExempt",
          "holdings": [ { "instrumentId": "<OZB publicId>", "issuerCountry": "AU", "count": 1, "gross": 327.00, "imputationCredit": 0, "grossWithImputation": 327.00, "foreignTax": 49.05, "rwt": 0, "nrwt": 0 } ],
          "countries": [ { "issuerCountry": "AU", "count": 1, "gross": 327.00, "imputationCredit": 0, "grossWithImputation": 327.00, "foreignTax": 49.05, "rwt": 0, "nrwt": 0 } ],
          "total": { "count": 1, "gross": 327.00, "imputationCredit": 0, "grossWithImputation": 327.00, "foreignTax": 49.05, "rwt": 0, "nrwt": 0 }
        }
      ],
      "total": { "count": 4, "gross": 1164.95, "imputationCredit": 280.00, "grossWithImputation": 1444.95, "foreignTax": 66.74, "rwt": 71.23, "nrwt": 0 }
    },
    "interest": {
      "accounts": [
        { "accountId": "<A2 publicId>", "custodyMode": "External", "currency": "NZD", "count": 2, "received": 120.00, "charged": 8.00, "rwt": 21.00, "nrwt": 0, "foreignTax": 0 },
        { "accountId": "<A1 publicId>", "custodyMode": "Custodial", "currency": "USD", "count": 2, "received": 13.70, "charged": 0, "rwt": 4.51, "nrwt": 0, "foreignTax": 0 }
      ],
      "total": { "count": 4, "received": 133.70, "charged": 8.00, "rwt": 25.51, "nrwt": 0, "foreignTax": 0 }
    },
    "taxWithheld": { "rwt": 96.74, "nrwt": 0, "foreignTax": 66.74 },
    "imputationCredit": 280.00,
    "complete": true,
    "incomplete": [],
    "notices": [
      { "notice": "ExternalFigures", "instrumentId": null, "accountId": "<A2 publicId>" },
      { "notice": "InterestCharged", "instrumentId": null, "accountId": "<A2 publicId>" }
    ]
  }
}
```

`bookedAt` is an instant in UTC. The `date` of a rate and the days of a `period` are dates with no zone ([api-design.md](../api-design.md) §2). An NZD figure has NZD's decimal places. `instrumentId`, `accountId` and `transactionId` are PublicIds.

### 8.2 Errors

All refusals are ordinary validation (400 with `errors`, no problem `code`).

| Case | Response |
| --- | --- |
| No Bearer, or a dead one | 401, never 302 |
| A SystemAdmin, on the report routes | 403 |
| The report is not granted to the Tenant, or not active; a Customer of another Tenant, an unassigned Customer, another Customer; a missing id | 404 |
| A schedule that names the report | 404 |
| `customerId` or `taxYear` missing | 400 `errors.customerId` / `errors.taxYear` |
| `taxYear` not a JSON whole number; an unknown field | 400 `errors.parameters` |
| `taxYear` below 2009; not ended, or its last day not final yet | 400 `errors.taxYear` (R18) |
| The download of an execution that is not `Succeeded` | 409 `report_not_ready` (reports) |

An incomplete summary is not an error: the read answers 200, and the execution is `Succeeded` with a PDF that says what is missing. An `Overseas` Customer is not refused (R19).

Cross-tenant or wrong-collection id → **404**, not 403. Keep the resource catalog in [api-design.md](../api-design.md) pointing to reports for the routes and here for the parameters of this report and the two fields of the read.

---

## 9. UI

**Adviser Portal.** The Customer page (`/customers/:id`) offers the tax year form for the report in its Reports section (R34). The form, the History summary and the 400 at the form are those the page has for the FIF Report; the page chooses the form by the report's code, and two codes now share it. The Reports tab (`/reports`) does not list the report: it is not Book-scoped.

**Customer Portal.** The Reports page offers the same form for the report (R35).

**Back Office.** No change. The `/reports` page lists the new catalog row, and the tenant configuration page offers it for a grant, as for any catalog row.

No page shows the figures on screen. Both BFFs already proxy `/users` and `/reports`.

### 9.1 Seeds and hosting

- `0063` runs with the schema applicator on the next start of `webapi` or `worker`. The row is active and granted to nobody.
- The demo tenant on the VPS gets the grant when an operator runs `webapi --seed-demo` again ([deployment.md](../deployment.md) §12). `TestSeed` grants it in Development on the next start.
- No change to AppHost, `compose.yml` or any setting.

What a summary can be complete for, which the report does not check:

- It needs no price, so the missing NZX and Tokyo prices do not matter ([known issue](../known-issues/eodhd-nz-jp-coverage.md)).
- A payment on an Account that is not in NZD needs a stored rate of its day. The history floor date decides how far back rates go (market-data-history R1); in Development it is 2026-01-01, so a USD payment of 2025 has no rate and the summary of tax year 2026 is incomplete for it.
- In the demo tenant the catalog is classified from tax year 2027. A summary of tax year 2026 lists every dividend under "Not classified" and is incomplete; its sums are there. A person can add a row for tax year 2026 to an Instrument in the Back Office.
- Tax year 2026 is the only one that can be asked for until 2 April 2027.

The functional tests write their own Instruments, rates and Transactions and depend on none of this.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests (`Tax/TaxLedgerInterestTests`) | An `Interest` of an interest run and one entered by hand give an item each, in the order of `bookedAt` then id; a reversed `Interest` and its Reversal give none; an `Interest` with the three tax kinds; a principal below 0; `Build` still gives no event for an `Interest` |
| Domain.UnitTests (`Tax/CustomerTaxYearInterestTests`) | §1.1: the four items of tax year 2026 to the cent, and I5 in tax year 2027; an item at 10:59 and one at 11:00 UTC on 31 March fall in two tax years; an NZD leg is used as booked; each figure is converted on its own; the rate of a Sunday is Friday's; `NoRate` and `StaleRate` leave `null` figures |
| Domain.UnitTests (`Tax/TaxYearIncomeTests`) | §1.1: every sum of the two tables and `taxWithheld`; each treatment falls in its group, and the four groups are always given; a dividend whose tax source disagrees stays in the group of its holding; per-country rows in the two foreign groups, with `null` for no country; a reinvested dividend counts once; received and charged are not netted; a missing figure makes its sums `null` up to the total and still counts; `AmountMissing` for a dividend and for an interest item; `NotClassified` only with a dividend in the year; a holding with no price does not make the summary incomplete; each notice; a tax year with nothing gives four empty groups and `complete` `true`; the invariants of §5 |
| Application.UnitTests (`Reports/CustomerIncomeSummaryBinderTests`) | The checks of R18, as `CustomerFifBinderTests` |
| Application.UnitTests (`Reports/CustomerIncomeSummaryReportTests`) | The content of §1.1; the header for `NewZealand` and for `Overseas`, with statement 2 of each; statement 4 only with `ExternalFigures`; the row of each group in section 1 and no row for a group with no dividend; each group's sentence, and "None." with no sentence; the country rows; the two sentences of section 4; each payment of section 5 with "no conversion", a period and "entered by hand"; each rate once in section 6; every sentence of §4.6; a `null` figure is "not available"; complete and incomplete; nothing paid |
| Application.UnitTests (`Reports/CustomerIncomeSummaryPdfTests`) | `Render` gives a PDF whose text holds the header lines, the seven section titles and the figures of §1.1; the footer has the page count and "Incomplete" on every page of an incomplete report |
| Application.FunctionalTests (`TaxYears/GetCustomerTaxYearIncomeTests`) | §1.1 through the API: `interest` and `income` as §8.1; an `Interest` entered with an earlier `bookedAt` on the External account changes the next read; a reversed hand `Interest` is in no item; `complete` of the tax year does not change with a missing rate of an interest item, and `income.complete` does. Two-tenant fixture: no Transaction of the other Tenant is in any item or sum, and the other Tenant's Customer → 404 |
| Application.FunctionalTests (`Reports/RequestCustomerIncomeSummaryTests`) | No Bearer → 401 not 302; a TenantAdmin, the assigned Adviser and the Customer → 202 with the snapshot; a SystemAdmin → 403; an unassigned Adviser, another Customer and an unknown id → 404; the report not granted → 404; each 400 of §8.2; an `Overseas` Customer → 202; a schedule for the report → 404. Two-tenant fixture: the other Tenant's Customer → 404, and the other Tenant's executions are in no list |
| Application.FunctionalTests (`Reports/GenerateCustomerIncomeSummaryTests`) | §1.1 through the API, then the job: the execution is `Succeeded`, has no `asOf`, and the text of the downloaded PDF holds the figures of §1.1; a payment backdated on the External account after the first report leaves the first file as it is, and a second request gives another figure; a payment with no rate gives a `Succeeded` execution whose PDF says "Incomplete"; an `Overseas` Customer gets a PDF with "Tax residency on file: Overseas"; no payment of another Tenant's Customer is in the PDF |
| Infrastructure.IntegrationTests (`AppliedSchema/ReportsSchemaTests`, `AppliedSchema/TestSeedReportsTests`, the demo seed tests) | The catalog row of R15 after the scripts ran, once after a second run; `TestSeed` and the demo seed grant five reports; a demo tenant with four grants gets the fifth and keeps the four |
| Adviser Portal and Customer Portal tests | The tax year form appears for the report's code; the body of §8.1; no schedule form; "Tax year {Y}" in the History summary; a 400 message at the form; the FIF Report's form is unchanged |

The tests that list the catalog or the grants count five reports. The existing tests of the tax year read, of the FIF report and of `Valuations/` stay green: nothing they assert changes.

The schema change touches `database/schema/`, so the whole of Infrastructure.IntegrationTests runs before its commit. The change to `CustomerTaxLedger` and `CustomerTaxYear` is shared code of the tax year read and the FIF report, so the partitions `TaxYears` and `Reports` run before each commit that touches them.

---

## 11. Locked in this spec

No new ADR.

| Item | Lock |
| --- | --- |
| Figures | Two fields of the tax year read, `interest` and `income`, worked out in Domain |
| Dividends | The `Dividend` events of the read, of every tax holding, in four groups by the FIF treatment of the Instrument |
| Interest | Every `Interest` Transaction of the Customer's Accounts booked in the tax year, reversed ones left out; per Account; received and charged apart; no country |
| NZD | The rule of tax-year-events R18 and R21, for interest as for dividends |
| Gross dividend | The dividend with its imputation credits, given beside its parts |
| Complete | The summary's own: a missing figure or a not classified holding with a dividend; `null`, never 0 |
| Catalog row | `customer-income-summary`, scope `Customer`, manual only, script `0063` |
| Access and parameters | Those of the FIF report: `{ customerId, taxYear }`, the tax years of the read |
| Tax residency | Not checked; printed, with a statement for each |
| Stored copy | The PDF, and nothing else; no `asOf` |
| Sections | Summary; About this report; Dividends; Interest; Each payment; Exchange rates used; What to know about these figures |
| Words | The statements of §4.4, the labels and sentences of §4.6 |
| Entry points | The tax year form on the Customer page of the Adviser Portal and on the Reports page of the Customer Portal |
| Demo | The fifth grant; the demo catalog unchanged |
| Routes, tables, permissions, problem codes, events, ports, jobs, settings | None |

### Points to confirm

Each point states the recommendation, written into the rules above, and the alternative.

1. **A report of its own, not a section of the FIF report** (R15). Recommended: fif-report point 1 already chose it. The summary is what a Customer below the threshold needs, it is made for an `Overseas` Customer too, and it needs no price, so it is complete where a FIF report is not. Alternative: a section of the FIF report, which then cannot be made for an `Overseas` Customer and reopens an accepted spec.
2. **The name "Tax Year Income Summary" and the code `customer-income-summary`** (R15). Recommended: the roadmap's name, and the word the other specs already use. The cost: Inland Revenue has an "Overseas income summary" (IR1261), and "income" can be read as all of a person's income; statements 3 and 7 say what the report is not. Alternative: "Dividend and Interest Report" with the code `customer-dividends-interest`, which says what is in it and uses no word of Inland Revenue's.
3. **The figures are fields of the tax year read, worked out in Domain** (R1, R13). Recommended: tax-cost-threshold and fif-income added their figures the same way; roadmap Q16 wants the numbers readable as JSON; the rule on a missing figure is then tested with no database; and the report only prints. The cost: the read grows for every caller, and `CustomerTaxLedger` loads the interest also when the FIF report asks. Alternative A: the report adds up the events itself, as section 7 of the FIF report does, and nothing is readable as JSON. Alternative B: a route of its own, `…/tax-years/{taxYear}/income`, with a second read of the ledger.
4. **Dividends are grouped by the FIF treatment of the Instrument, in four groups** (R6). Recommended: the treatment is what decides which rules a dividend is under; a New Zealand dividend and a foreign one go to different places of the return; and an attributing interest must stay apart because its dividends are returned only when the FIF rules do not apply. Alternative A: two groups by the dividend's own `taxSource`, which hides the FIF question. Alternative B: leave the dividends of attributing interests out and point to section 7 of the FIF report, which an `Overseas` Customer cannot get.
5. **A not classified holding with a dividend makes the summary incomplete** (R11). Recommended: roadmap T5 and instrument-tax-attributes R27. The sums are there, in a group of their own; what is missing is which rules they are under. Alternative: a notice only, since no figure is missing.
6. **A holding with no dividend never makes the summary incomplete, whatever else it lacks** (R11). Recommended: the summary reads no price, no cost and no quantity. Alternative: follow `complete` of the tax year, so that the two reports of a year always agree on "Incomplete".
7. **Interest is in the tax year of its `bookedAt`** (R4). Recommended: it is the rule of every dividend (tax-year-events R11) and the instant the money reached the account. The cost: the interest run posts a month after it ends, so tax year `Y` holds the interest of March `Y − 1` to February `Y`. A bank that credits on the last day of March puts that month a year earlier. The PDF prints the period of each posting and says so in statement 5. Alternative: the tax year of the last day of the period, which a hand-entered `Interest` does not have, and which calls income received what the account did not hold yet.
8. **Interest has no country** (R14). Recommended: no Account has a country, and inventing one from the currency or the custody mode would be a guess (roadmap T5). The PDF says that the platform does not know. Alternative A: interest on a Custodial account is called New Zealand interest, because the firm pays it and takes RWT. Alternative B: a country on an External account, which is a write change and a slice of its own.
9. **Interest charged is listed apart and never netted** (R8). Recommended: Inland Revenue lets "negative interest" reduce the interest to declare in one case and not in others (S3 p.15), so a net figure would decide what the report must not decide. Alternative: one net figure per Account.
10. **One NZD rule: the stored rate of the UTC date of `bookedAt`** (R5). Recommended: roadmap T4, and the rule of the tax year read; for income under the ordinary rules Inland Revenue names the rate of the day the income was received and rates of central banks (S4, S5). Alternative: Inland Revenue's mid-month table, a second rate set (roadmap Q4, not chosen).
11. **The gross dividend, the dividend with its imputation credits, is given as a figure** (R7). Recommended: it is the figure the return asks for (S3 p.17), and "gross" in the ledger means the dividend before tax without the credits, so a reader would otherwise add the two up by hand or take the wrong one. Alternative: the two parts only.
12. **The report is made for an `Overseas` Customer** (R19). Recommended: it lists payments and tax, which do not depend on the residency; NRWT exists only for an `Overseas` Customer and no other document shows it; and the residency on file has no date, so a Customer who left New Zealand after the tax year would otherwise get no tax document at all. Alternative: refuse with a 400 as the FIF report does, so that the two reports have one rule.
13. **Manual only, one Customer per request, the tax years of the read** (R17, R18). Recommended: as the FIF report, for the same reasons. The summary needs no final price, so it could be made on 1 April; it waits for 2 April because it is a field of the tax year read. Alternative: its own rule, from the end of the tax year.
14. **A Customer may ask for their own report** (R16). Recommended: as the FIF report and the Customer Transactions Report; it shows nothing the Customer cannot read in their own Transactions. Alternative: staff only.
15. **The PDF lists every payment and every rate** (R29, R30). Recommended: roadmap T9, and a person checks a tax document against bank and broker statements payment by payment. The cost: a Customer with monthly interest on five accounts gets sixty rows. Alternative: the sums only, with the Customer Transactions Report for the payments.
16. **The words of the PDF are part of the contract** (§4.4, §4.6). Recommended: as fif-report point 19. Alternative: the spec fixes the sections and the figures, and the words are the implementer's.
17. **No box of a return is named** (R33). Recommended: the question and box numbers of the IR3 change from year to year, and naming one says where a figure goes, which is advice. The sentences name the return and the form, not a box. Alternative: print "Question 13", "Question 14" and "Question 17" beside the groups.
18. **The FIF report keeps its own sums** (§2 Out). Recommended: it is accepted and landed, and its section 7 covers attributing and not classified holdings only. The two reports add up the same events, so they agree by construction. Alternative: `CustomerFifReport` reads its sums from `income`, a refactor of landed code.
19. **One tax year form for two reports** (R34, R35). Recommended: the parameters are the same, and the form already exists in both portals. Alternative: a form of its own, in case the two reports later differ.
20. **The demo seed grants the report and adds no demo data** (R36, §9.1). Recommended: as fif-report point 18. The cost: in the demo tenant a summary of tax year 2026 is incomplete, because the catalog is classified from tax year 2027. Alternative: the seeds also classify the demo catalog for tax year 2026, which changes instrument-tax-attributes R24.

### Found while writing

- **The sources were read as pages.** The fetch tool saved the PDFs of the return guide, the FIF guide, the fact sheet and the form, and the pages named in §14 were read from their text. Three web pages were read through the fetch tool, as a restatement with short quotes.
- **"By country" is now read.** Roadmap §2 says the overseas income summary is "by country", and fif-report could not find it in the pages it read. The return guide (S3 p.19) and the form itself (S4) say "a separate section for each jurisdiction and for each income type". The form is the March 2024 edition; no later one was found.
- **The form lists FIF income as an income type of its own**, beside "Dividends" and "Interest" (S4). So a person in the FIF rules enters FIF income per country, and a person outside them enters dividends per country. The summary gives the second; section 7 of the FIF report gives the tax per country for the first.
- **RWT on a foreign dividend.** On a Custodial account the platform takes RWT from a foreign dividend (dividends R33). The return guide has this case: an overseas dividend received "through an agent or trustee, who has deducted RWT in New Zealand" (S3 p.24). The summary keeps RWT and foreign tax in two columns for this reason.
- **A New Zealand imputation credit on an Australian dividend cannot be entered.** The return guide says dividends of Australian companies "may have a New Zealand imputation credit" (S3 p.24). The ledger allows an imputation credit only with the tax source `NewZealand` (dividends R3, external-income R12). A person who enters such a dividend with `NewZealand` gets the notice `DividendTaxSourceDisagrees`, and the dividend stays in the group of its holding. Not changed here.
- **Australian franking credits are in no figure.** The ledger has no field for them, and the guide says not to include them (S3 p.24). Nothing to do.
- **"Gross" means two things.** In the ledger and in the tax year read, the gross of a dividend is the amount before tax, without the imputation credit. In Inland Revenue's return the gross dividend includes it. The PDF prints "Dividends" for the first and "Gross dividends" for the second, and says so under section 3.
- **A hand-entered dividend needs no holding.** It touches no holding (external-income R16), so a tax holding can have a dividend in a year and a quantity of 0 throughout. It is listed in the read (tax-year-events R23) and in the summary like any other.
- **The interest of March.** See point 7. On a Custodial account the March interest of the year a Customer joined is in their first tax year only if it was posted by an account closure before 1 April.
- **An account closure posts interest at once** (interest-run R33), with the `bookedAt` of the closure. It is an interest item like any other.
- **NRWT on a New Zealand Customer, and RWT on an `Overseas` one.** A payment made while the profile said otherwise keeps its tax kind. The summary prints both columns as booked and says nothing about it, as fif-report found.
- **The fact sheet on the ordinary rules was not read.** S2 ¶2 points to IS 24/10 FS 2, "Income tax – Share investments (dividends and taxable share sales)". The summary states no rule from it; a later look should check the sentences of §4.6 against it.

Still open, not this slice:

- A person who knows the rules reading the sentences of §4.4 and §4.6, as for the FIF report
- A country on an Account, and New Zealand interest told from overseas interest
- A taxable bonus issue and any non-cash dividend (tax-year-events, still open)
- A New Zealand imputation credit on a dividend of a foreign company
- A dated tax residency and a transitional resident (roadmap §7)
- An RWT withholding certificate, the investment income report a payer files, and paying the withheld tax to Inland Revenue (interest-run, dividends)
- The foreign tax credit allowed, tax payable and the IR3 and IR1261 forms (roadmap §7)
- A data file beside the PDF, a page that shows the figures on screen, and a run over every Customer
- Ledger replay and period close (roadmap §1.2)

---

## 12. Changes to other docs

A plan while this spec is in `review`. Each doc is changed in place when it is accepted.

| Doc | Change |
| --- | --- |
| [tax-year-events.md](tax-year-events.md) | §2 Out, R17, R26 and "Still open": `Interest` and the income summary are this spec's, no longer "TR6". R31: the item also carries `interest` and `income` (R13 here). §5: `LedgerFact.Interest`, `TaxLedger.BuildInterest`, `CustomerTaxYear.Interest` and `Income`. §6 and §7: the read also loads the `Interest` Transactions of the tax year. History row |
| [reports.md](reports.md) | Intro and §2: a fifth report. §6 and §6.1: script `0063`, the seeded code and PublicId, triggers `manual` only, five demo grants. §7.1: the parameters `{ customerId, taxYear }`. A new §9.2c "Tax Year Income Summary" that points here. §10: the tax year form serves two reports. §12 "First reports" and "Triggers in phase". History row |
| [adviser-portal-reports.md](../adviser-portal/adviser-portal-reports.md) | Backend prerequisites: five seeded reports and the new PublicId. A new section "Tax Year Income Summary" with D115: the tax year form of D114 for the report's code, no schedule form, the History summary, the 400 at the form, and its tests. History row |
| [customer-portal.md](../customer-portal/customer-portal.md) | A new section "Tax Year Income Summary" with C52: the form of C50 for the report's code, also for a Customer whose tax residency is `Overseas`. "Reports page (field bindings)": the tax year form serves two reports. History row |
| [demo-tenant-seed.md](demo-tenant-seed.md) | §1, R8, R12 and §10: five report grants, `customer-income-summary` among them. History row |
| [deployment.md](../deployment.md) | §12 step 14: the demo seed writes five report grants |
| [fif-report.md](fif-report.md) | §2 Out, point 12 and "Still open": the income summary is this spec, no longer "TR6". "Found while writing": the form and the return guide were read here, and say "for each jurisdiction and for each income type". History section with its first row. No rule and no word of the PDF changes |
| [fif-income.md](fif-income.md), [tax-cost-threshold.md](tax-cost-threshold.md) | §2 Out and "Still open": the income summary is this spec. History row |
| [instrument-tax-attributes.md](instrument-tax-attributes.md) | R3, §2 Out and "Still open": the income summary reads the treatment of the year to group dividends, and prints the issuer country (R6, R27 here) |
| [interest-run.md](interest-run.md) | §2 Out and point 19: the dividends and interest of a tax year have a report (this spec); a withholding certificate, the investment income report and paying the tax stay out. R23: an interest posting is in the tax year of its `bookedAt` (R4 here). History row |
| [dividends.md](dividends.md), [external-income.md](external-income.md) | §2 Out and "Still open": a dividend report under the ordinary rules is this spec. Dividends R37 and external-income R15, R23: the tax statement for a dividend and for interest is the income summary; a hand-entered `Interest` is an interest item (R3 here) |
| [custody-mode-grouping.md](custody-mode-grouping.md) | R15: the note is also printed in the income summary (statement 4 of R25 here) |
| [api-design.md](../api-design.md) | §7.4: the two fields of the tax year read. §7.8: the report `customer-income-summary`, its parameters and its 400 cases; manual only; not refused for `Overseas`. Change log |
| [domain-model.md](../domain-model.md) | §8.24: the types and invariants of §5. §11: a fifth catalog row is data, not a type. Change log |
| [database-design.md](../database-design.md) | §9: `0063_income_summary_report.sql`. §14: the catalog row, and five demo grants. Change log |
| [function-plan.md](../function-plan.md) | §6: the Tax Year Income Summary beside the FIF Report |
| [glossary.md](../glossary.md) | "Income summary" and "Interest item" become accepted. "Demo seed": five report grants. "FIF report": its "Not" column points here. "Imputation credit": the gross dividend of the income summary |
| [fif-roadmap.md](../roadmaps/fif-roadmap.md) | §6 TR6: the spec line. §8: Q18 written as rules here. §9 row 6: accepted. §3 "Tax documents" and "Reports": what exists now |
| [features/README.md](README.md), [future-plans.md](../future-plans.md) §1 step 6, root `README.md` | Status: TR6 accepted; the root README when it lands |

---

## 13. Suggested commits

Removed when the slice lands. The repository builds after each commit. Each line is one behaviour; an agent splits it into red then green.

1. `TaxLedger.BuildInterest`: the interest of a Customer from the facts, in order, with reversed Transactions left out.
2. `CustomerTaxYear`: the interest items of a tax year in NZD, with their status and rate.
3. `TaxYearIncome.Compute`: the groups, the dividend sums, the interest sums, the tax taken, the missing figure, the reasons and the notices.
4. `CustomerTaxLedger` and `GetCustomerTaxYear`: `interest` and `income` on the tax year read, with the tenant isolation.
5. Script `0063`: the catalog row `customer-income-summary`, with the demo seed and `TestSeed` granting five reports.
6. `CustomerIncomeSummaryBinder` and `POST /reports/request` for the report: the parameters, the scope, the 404 cases, an `Overseas` Customer accepted, the snapshot; a schedule is 404. With the tenant isolation.
7. `CustomerIncomeSummaryReport.Build`: the header, the statements and sections 1 to 4.
8. `CustomerIncomeSummaryReport.Build`: sections 5 to 7, with every sentence of §4.6.
9. `CustomerIncomeSummaryPdf.Render`: the pages, the footer and the text of §1.1.
10. `CustomerIncomeSummaryReportGenerator`: the worked example through the API and the job, a stored report that does not change, an incomplete report, and an `Overseas` Customer.
11. Adviser Portal: the tax year form for the report on the Customer page (D115).
12. Customer Portal: the tax year form for the report on the Reports page (C52).
13. Landing: the docs of §12, with the landing notes.

---

## 14. Sources

Read on 2026-11-28. S1 and S2 are the codes of [fif-roadmap.md](../roadmaps/fif-roadmap.md) §11; S3 to S6 are this spec's own.

| # | Source | Used for | Read |
| --- | --- | --- | --- |
| S1 | Inland Revenue, *Guide to foreign investment funds*, IR461, April 2026 edition, the PDF linked from `ird.govt.nz/fif` (page last updated 31 March 2026) | An individual with an exempt ASX-listed share has no FIF income "but they will usually need to account for dividends in their tax return" (p.7); a person outside the rules "will report dividends in his IR3 return" and "complete an overseas income summary" (p.8, example); at or below the threshold "you will pay tax only on dividends received and gains from the disposal of shares" (p.9); under the first four methods "dividends and other gains are not usually taxed separately" (p.10, and p.14 for the fair dividend rate method) | Yes: pages 7 to 10 and 14, from the PDF |
| S2 | Inland Revenue, fact sheet IS 24/10 FS 1, *Income tax – Share investments (whether the FIF rules apply)*, 18 December 2024 (`taxtechnical.ird.govt.nz`) | "If the FIF rules apply, you do not apply the ordinary tax rules in relation to any dividends you receive or share sales" (¶1); "If the FIF rules do not apply, the ordinary tax rules apply" (¶2); a person who is not tax resident applies "the ordinary tax rules to your New Zealand sourced income" (¶5); "If you have exempt interests, you don't apply the FIF rules, but you still apply the ordinary tax rules to those interests" (¶7); "You do not apply the ordinary tax rules to any income you receive from your FIF interests" (¶10) | Yes: pages 1 to 4, from the PDF |
| S3 | Inland Revenue, *Individual income tax return guide*, IR3G, 2026, August 2026 edition, the PDF linked from the page "Complete my individual income tax return - IR3" (last updated 21 August 2026) | New Zealand interest "you received" between 1 April 2025 and 31 March 2026, "negative interest" of a broken term deposit, interest from overseas shown with overseas income (p.15, p.16); RWT on interest, "you can claim a credit for this", and the payer's "RWT withholding certificate or similar statement" (p.16); interest in "the income year you received the interest" (p.17); the net dividend, the imputation credit and the RWT credits, "Add all these amounts together to work out your gross dividend", and RWT "to bring the total credits withheld up to 33% of the gross dividend" (p.17); "shares from a taxable bonus issue or a non-cash dividend" (p.18); overseas income converted to New Zealand dollars, the IR1261 with "a separate section for each jurisdiction and for each income type" (p.19); tax paid overseas, "the lesser of the actual amount of tax paid on the overseas income or the amount of tax you would pay in New Zealand on the foreign income" (p.22); New Zealand tax credits on overseas dividends and the transitional resident (p.23); "Australian dividends from non-FIF companies", "Do not include any Australian imputed or franking credits", and an overseas dividend with "RWT in New Zealand" (p.24) | Yes: pages 15 to 19 and 22 to 24, from the PDF |
| S4 | Inland Revenue, *Overseas income summary*, form IR1261, March 2024 edition, the PDF linked from the page "Reporting your overseas income" (last updated 29 May 2026) | The income types, "Interest", "Dividends" and "FIF income" among them; the jurisdiction, "the country or territory you have received overseas income from"; the gross amount and the tax credit in NZD; "a separate section for each jurisdiction and for each income type"; conversion by Inland Revenue's rate tables or "the exchange rate for the day you received your overseas income" | Yes: both pages, from the PDF. The 2025 and 2026 editions were not found (404) |
| S5 | Inland Revenue, "Overseas currency conversion to NZ dollars" (`ird.govt.nz/overseas-currency`, last updated 4 November 2026); S4 of the roadmap | Rates "from the Reserve Bank of New Zealand" and "from other countries' central banks"; the FIF rules as an exception with their own methods; "It's important to use the same exchange rate sources and methods consistently over time." | Through a fetch tool: a restatement with short quotes |
| S6 | Inland Revenue, "Individual income tax return - IR3" (`ird.govt.nz`, last updated 24 September 2026) | "The tax year is from 1 April to 31 March."; "If you are not a New Zealand tax resident, you'll need to file an IR3NR instead." | Through a fetch tool: a restatement with short quotes |
| — | Inland Revenue, "Non-resident withholding tax (NRWT)" (last updated 13 March 2026) | NRWT is "a tax withheld from New Zealand payments of interest, dividends, and royalties to non-residents". Whether it is a final tax was not found on the page | Through a fetch tool |
| — | Inland Revenue, fact sheet IS 24/10 FS 2, *Income tax – Share investments (dividends and taxable share sales)* | How the ordinary rules apply to a dividend, in Inland Revenue's own summary | Not read. See "Found while writing" |
| — | Inland Revenue, the page "Interest and dividends" under "Types of individual income" | — | Not read: the address answered 404 |
| — | Income Tax Act 2007 | The rules behind the guides, in the Act's own words | Not read, as in every spec of this roadmap |

What follows: this slice computes no tax figure. Its rules that rest on a tax source are four: a payment is in the tax year it was received (R4), an amount is converted at the rate of its day (R5), the gross dividend includes the imputation credits (R7), and interest charged is not netted (R8). Each rests on the return guide. The grouping of R6 rests on the classification of tax-year-events and instrument-tax-attributes. Every other tax statement is a sentence the report prints, which is the platform's summary of the pages above.
