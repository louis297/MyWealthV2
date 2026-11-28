---
title: "The FIF report"
status: accepted
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
  - tax-cost-threshold.md
  - fif-income.md
  - instrument-tax-attributes.md
  - interest-run.md
  - dividends.md
  - external-income.md
  - custody-mode-grouping.md
  - account-valuation.md
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

# The FIF report

Step 5 of [fif-roadmap.md](../roadmaps/fif-roadmap.md) §9. It covers roadmap item **TR5** and writes the decisions **Q13** (which tax years can be reported), **Q14** (whether the platform stores that the rules apply), **Q15** (a backdated entry after a report), **Q16** (whether a PDF is enough) and **Q17** (how foreign tax is shown) as rules. It answers the questions the three earlier specs left to it: what the report prints for each outcome of the threshold test and for an incomplete result, the day of the highest total in New Zealand time, and whether there is a run over every Customer of a Tenant.

The tax year read ([tax-year-events.md](tax-year-events.md)) already answers, for one Customer and one tax year, everything the roadmap's target (§4) asks for: which holdings are attributing interests, the threshold test ([tax-cost-threshold.md](tax-cost-threshold.md)), FIF income under both methods ([fif-income.md](fif-income.md)), the tax taken from each dividend, and every price and rate behind a figure. It answers as JSON, to a caller who knows the route. This slice is the report: a fourth entry in the reports catalog ([reports.md](reports.md)), scope `Customer`, that prints that answer as a PDF and stores it as an execution, with an entry point in each portal.

The report is a reader. It computes no tax figure of its own: every number it prints is a figure of the tax year read, or a sum of such figures that this spec names. It adds the words: what each figure is, what it rests on, what is missing, and what the report does not say.

New: catalog report `customer-fif` (script `0062_fif_report.sql`, one row); its parameters `{ customerId, taxYear }`; Application types `CustomerFifBinder`, `CustomerFifReport`, `CustomerFifPdf` and `CustomerFifReportGenerator`; the Domain rule `NewZealandTime`; a branch in `RequestReport`; the fourth grant of the demo seed and of `TestSeed`; a parameter form on the Customer page of the Adviser Portal and on the Reports page of the Customer Portal. No route, table, permission, problem code, domain event, `TransactionType`, port, job or setting. No write to the ledger. `CustomerTaxLedger` and the tax year read do not change. No simulator change.

**Status is `accepted` (2026-11-28).** The nineteen points of §11 were confirmed on 2026-11-28, every one as recommended, and are written into the rules. The changes to the docs listed in §12 were made when it was accepted. It is a build contract. The sentences the report prints about the tax rules were not read by a person who knows the rules when it was accepted; that stays open (§11). Landed 2026-11-28.

Notes from landing:

- §7: `CustomerFifReport.Build(read, names, residency, generatedAt, ledgerReadAt)` takes `CustomerTaxLedgerRead` and a `CustomerFifNames` (the Tenant's and the Customer's names, the account names by internal id, the decimal places of each currency) and gives a `CustomerFifContent`: the header lines, the sections, each a list of `CustomerFifText` (a line, or a heading) and `CustomerFifTable` (column titles and rows of cells), and what the footer says. `CustomerFifPdf.Render` lays that out and chooses no word. The tax residency is read by `CustomerFifBinder.ResidencyAsync`, which both the request and the generator call, and the refusal is `CustomerFifBinder.OverseasMessage` in both places.
- R6: the binder reads numbers strictly, because the web defaults of `System.Text.Json` read a number from a string; `"2026"` is then 400 `errors.parameters`.
- R9: the generator takes the platform time when the job starts as the generation time, and again just before the one call of `CustomerTaxLedger.ReadAsync` as `ledgerReadAt`. The account names come from `Accounts`, and the decimal places from the currency catalog.
- R14: a quantity or a rate is rounded to 8 places away from zero before its trailing zeros are dropped.
- R15: the count of holdings affected is over every tax holding of the year; a reason with no holding (`NoThreshold`) makes the report incomplete with "0 of {m} holdings are affected".
- R18: the table of a group has the columns Symbol, Name, Issuer country, Treatment, Classified from, Note, Opening, Closing and FIF income. The note of `ExemptOther` is in its label, not again in the Note column. The two sentences under the second and the third group are printed also when the group is "None.".
- R19: the table of the holdings that count lists the `Attributing` and `AttributingNoFdr` tax holdings, "not known: {cause} since {time}" for an unknown cost; with none, "No attributing interest in this tax year.". The sentence of the outcome is in section 4 only when a threshold is set; without one, the first line of the section says so.
- R20: a row of a holding that is not computed has its symbol and its status, and empty cells.
- R21: sections 5 to 9 take the tax holdings in the order of section 3, by group, then as the read sorts them. The block is headed "{symbol} {name}", then the issuer country, the label and the tax year of the row on one line. The rate cell of a line with no conversion reads "no conversion". An event whose amount is missing for a reason of §4.6 reads "not available ({label})"; a share reorganisation and a correction, which have no amount, leave the cell empty. A cost change of 0 is left empty; a cost that is not known reads "not known". The quick sale prints every match, and the sales and dividend shares that have shares matched. A holding that is `HoldingIncomplete` reads "Its FIF income is not computed: a figure it needs is missing. See section 9.".
- R22: two tables, one per tax holding and one per country with the row "Total". A holding not computed reads "not computed" in the FDR and CV columns.
- R23: a rate row's pair is read from what it converted: the currency of the price of a line or of an arrival, of the trade or the dividend of an event, or of the carried cost; otherwise the Instrument's quote currency. Prices are listed in the order of the holdings, then by date; rates by pair, then by date. With none, "None.".
- R24: "What is missing" lists the reasons of every tax holding, also one of the second group, so that an incomplete report always says why; "What these figures rest on" lists the notices of the first and the third group only. `NoThreshold` is the first line.
- §4.4: a column of a table is as wide as its longest cell, between 3 and 50 characters, so that an amount such as a move keeps to one line. The footer reads "{customer}  FIF Report, tax year {Y}  Page {n} of {m}" and then "Incomplete".
- §10: the content tests build the tax year with `TaxWorkedExample` of Domain.UnitTests, linked into Application.UnitTests, and change it with `with`. The functional catalog seed (`FunctionalTestSetup`) gains the row, and the tests that list the catalog or the grants (`GetReportCatalogTests`, `GetTenantReportsTests`, `RevokeTenantReportTests`, `DevelopmentSeedSecondFirmTests`) count four reports.
- §9: each portal has `reportableTaxYears` and `taxYearLabel` in `src/shared/time/taxYears.ts`. The Customer Portal's Reports page now shows the messages of a 400 at the form for both of its forms, and keeps "Report is not available." for any other failure.

The tax statements the report makes about itself were read again from Inland Revenue on 2026-11-28 (§14), this time from the pages of the two PDFs themselves. The slice states no rule of the Income Tax Act that an earlier spec did not state, so no section of the Act was asked for. Nothing here is tax advice, and the report says the same of itself.

---

## 1. Summary

| | The tax year read today | This slice |
| --- | --- | --- |
| Form | JSON, worked out on each call | A PDF, made once and stored as a report execution |
| Who finds it | A caller who knows the route | An Adviser on the Customer page; a Customer on their Reports page |
| Words | Enum names and field names | Sentences: what a figure is, what it rests on, what is missing |
| The Customer's tax residency | Not read | Read. An `Overseas` Customer gets no report (roadmap Q14) |
| What was used | In the answer, until the next call | Printed: every price, rate, classification and the instant the ledger was read (roadmap T9) |
| A later change to the ledger | Changes the next answer | Does not change a stored report. A new request is a new execution (roadmap Q15) |
| Times | Instants in UTC | New Zealand time, by the rule that cuts the tax year |

The rule that distinguishes this slice: the report adds no figure. A reader who has the PDF and the tax year read of the same instant finds the same numbers in both.

### 1.1 Worked example

Roadmap T12 asks for a worked example, and names it: the example of tax-year-events §1.1, printed. One Customer, a New Zealand resident, with two Accounts (A1 "Trading", Custodial, in USD; A2 "Old broker", External, in NZD) and one Instrument, `AAA`, issuer country `US`, `Attributing` from tax year 2025. The figures are those of tax-year-events §1.1, of tax-cost-threshold example 1 and of fif-income example 1. The report is asked for tax year 2026 and made at 03:12:45 UTC on 28 November 2026. The tests use the text below as written.

```text
FIF Report
Tenant: Demo Advisory
Customer: Cee Customer
Tax year: 2026 (1 April 2025 to 31 March 2026, New Zealand time)
Tax year in UTC: 2025-03-31 11:00 to 2026-03-31 11:00
Generated: 2026-11-28 16:12 New Zealand time (2026-11-28 03:12:45 UTC)
Ledger read at: 2026-11-28 16:12 New Zealand time (2026-11-28 03:12:45 UTC)
Tax residency on file: New Zealand
All amounts are in New Zealand dollars (NZD). All times are New Zealand time.
Status: Complete

1. Summary
Threshold test: Not above the threshold. The total cost of your attributing interests
was at most 12355.70, at the start of the tax year. The threshold for tax year 2026 is 50000.00.
FIF income, fair dividend rate method: 655.42
FIF income, comparative value method: 1740.87
Lower: fair dividend rate
These figures apply if the FIF rules apply to you. The threshold test above is one part of that question.
Holdings: 1 attributing interest, 0 exempt or not foreign, 0 not classified.

3. Holdings and classification
Attributing interests
AAA  Alpha Inc  United States of America  Attributing interest  classified from tax year 2025
     opening 140  closing 240.8  FIF income: computed
Exempt or not foreign
None.
These holdings are not in the FIF calculation. ...
Not classified
None.
A holding that is not classified may be an attributing interest. ...

4. Threshold test
Threshold for tax year 2026: 50000.00
Total cost at the start: 12355.70    Highest: 12355.70, at the start of the tax year    At the end: 10792.33
Not above the threshold. The total cost of your attributing interests was at most 12355.70, ...
A person at or below the threshold does not apply the FIF rules unless they choose to. ...
AAA  tax cost at the start 12355.70  at the end 10792.33
2025-04-01 09:15  AAA  Sale      -2647.65   9708.05
2025-09-15 14:30  AAA  Purchase    +37.63   9745.68
2026-02-03 10:40  AAA  Purchase  +1046.65  10792.33

5. FIF income
Fair dividend rate method (rate 0.05)
Opening value 13108.48    0.05 x opening value 655.42
Peak holding method amounts 0.00    Quick sale gain amounts 0.00    Quick sale adjustment 0.00
FIF income 655.42
Comparative value method
Total 1740.87    FIF income 1740.87
AAA  computed  opening value 13108.48  FDR amount 655.42  peak holding method amount 0.00
     quick sale gain amount 0.00  CV amount 1740.87
The quick sale adjustment is the lesser of the two totals over all holdings, not a sum per holding.

6. Each holding
AAA Alpha Inc
United States of America  Attributing interest  classified from tax year 2025
Opening, valued as of 2025-03-31
  Trading     Held on platform  100    53.20 USD (2025-03-31)  5320.00 USD  x 1.76 (2025-03-31)   9363.20
  Old broker  Held elsewhere     40    53.20 USD (2025-03-31)  2128.00 USD  x 1.76 (2025-03-31)   3745.28
Total 140 (280 on the basis of the year's end)  13108.48
Closing, valued as of 2026-03-31
  Trading     Held on platform  240.8  31.00 USD (2026-03-31)  7464.80 USD  x 1.745 (2026-03-31)  13026.08
Total 240.8  13026.08
Events
  1  2025-04-01 09:15  Sale                         Trading     -30   110    220    2789.60 (2798.40 less a fee of 8.80)   x 1.76 (2025-03-31)   -2647.65   9708.05
  2  2025-05-20 14:10  Dividend                     Trading       0   110    220      59.15                                x 1.69 (2025-05-20)               9708.05
  3  2025-07-01 12:00  Share reorganisation         Old broker  +40   150    220                                                                            9708.05
  4  2025-07-03 00:01  Share reorganisation         Trading     +70   220    220                                                                            9708.05
  5  2025-09-15 14:30  Dividend                     Trading       0   220    220      58.80                                x 1.68 (2025-09-15)               9708.05
  6  2025-09-15 14:30  Purchase                     Trading    +0.8   220.8  220.8    37.63                                x 1.68 (2025-09-15)     +37.63   9745.68
  7  2025-11-14 16:20  Moved between your accounts  Trading       0   220.8  220.8   80 from Old broker (2025-11-10 13:00) to Trading                        9745.68
  8  2026-02-03 10:40  Purchase                     Trading     +20   240.8  240.8  1046.65 (1038.00 and a fee of 8.65)    x 1.73 (2026-02-02)   +1046.65  10792.33
Dividends
  2   70  59.15  8.87  10.65  0.00  0.00  Foreign
  5  140  58.80  8.82  10.58  0.00  0.00  Foreign
Tax cost: 12355.70 at the start, 10792.33 at the end
Fair dividend rate: 0.05 x 13108.48 = 655.42
Quick sale: opening 280, peak 280, closing 240.8; peak holding differential 0
Purchases of the year: 20.8 shares for 1084.28, average cost 52.12884615
Peak holding method amount 0.00    Shares matched 0    Quick sale gain amount 0.00
Comparative value: (13026.08 + 2789.60 + 117.95) - (13108.48 + 1084.28) = 1740.87
Notices: Held elsewhere; Moved between your accounts. See section 9.

7. Tax taken from dividends
AAA  United States of America  dividends 117.95  foreign tax 17.69  RWT 21.23  NRWT 0.00  imputation credits 0.00  FDR amount 655.42  CV amount 1740.87
United States of America       dividends 117.95  foreign tax 17.69  RWT 21.23  NRWT 0.00  imputation credits 0.00
Total                          dividends 117.95  foreign tax 17.69  RWT 21.23  NRWT 0.00  imputation credits 0.00
Foreign tax taken from a dividend may give a foreign tax credit. ...

8. Prices, rates and values used
Prices
  AAA  2025-03-31  53.20 USD  EODHD
  AAA  2026-03-31  31.00 USD  EODHD
Rates
  USD to NZD  2025-03-31  1.76   Frankfurter
  USD to NZD  2025-05-20  1.69   Frankfurter
  USD to NZD  2025-09-15  1.68   Frankfurter
  USD to NZD  2026-02-02  1.73   Frankfurter
  USD to NZD  2026-03-31  1.745  Frankfurter
FIF threshold of tax year 2026: 50000.00    Fair dividend rate of tax year 2026: 0.05
Ledger read at 2026-11-28 16:12
The tax cost at the start of the tax year comes from purchases of earlier tax years. Their rates are not listed here.

9. What to know about these figures
What these figures rest on
AAA: Some of these figures come from an account held elsewhere. ...
AAA: A transfer out of one of your accounts and a transfer into another were read as one move. ...
```

Section 2 of the PDF, "About this report", holds the statements of R16 and is left out above. The sentences of section 9 are those of §4.7 in full.

What the example shows:

- **Times are New Zealand time** (R13). Event 1 was booked at 20:15 UTC on 31 March 2025. In the PDF it is 09:15 on 1 April, the first morning of the tax year, where the UTC time would read as the day before the year began. Event 4 moves to 3 July and event 8 to 3 February.
- **A rate keeps its own date** (R23). Event 8 is printed on 3 February and converted at the rate of 2 February: the rate is the stored row of the UTC date of `bookedAt` (tax-year-events R18), and the PDF prints the date of the row, not a date worked out again.
- **The report adds no figure** (R9). Every number above is in the tax year read, except the three sums of section 7 and the counts of section 1, which R22 and R17 name.
- **The threshold test and the income are both there** (R17). The Customer is not above the threshold, and the report still shows both methods: a person below it may choose to apply the rules (roadmap Q14).
- **The tax taken from the dividends is listed, and no credit is worked out** (R22): 17.69 of foreign tax and 21.23 of RWT, per tax holding and per country, beside the income of the holding under each method.

What the summary says in other cases:

| Case | Status | Section 1 says |
| --- | --- | --- |
| tax-cost-threshold example 3: `BBB` and `DDD` attributing, `EEE` exempt | Complete, if every holding is valued | "Above the threshold. The total cost of your attributing interests reached 50046.40 on 2025-10-10 08:30. The threshold for tax year 2026 is 50000.00. Above it at any time in the year, every attributing interest is in the FIF rules for the whole year." `EEE` is in section 3 only |
| fif-income example 3: `QQQ` and `RRR` | Complete | "FIF income, fair dividend rate method: 1360.00", "comparative value method: 0.00", "Lower: comparative value" |
| The same with `SSS`, `AttributingNoFdr` | Incomplete | The same two totals, and "These totals do not include SSS: the fair dividend rate method is not allowed for it. Its income is worked out separately and added to the method you use." |
| A tax holding with no closing price | Incomplete | "FIF income totals are not given. 1 holding that belongs in them could not be computed: ..." No figure takes their place |
| A holding nobody classified | Incomplete | The same for the income; the threshold test says "could not be completed", or "Above" when the known costs alone are above |
| Tax year 2027, before the threshold of that year is set | Incomplete | "No FIF threshold is set on this platform for tax year 2027." Both methods are still shown |
| A Customer with nothing in the year | Complete | "No holdings in this tax year." |

---

## 2. Scope

**In**

- Database: script `0062_fif_report.sql`, one row in `Reports`
- Domain: `NewZealandTime` (the New Zealand time of an instant)
- Application: `CustomerFifBinder` (parameters), a branch in `RequestReport` (scope and tax residency), `CustomerFifReport` (the content of the report from a tax year), `CustomerFifPdf` (the PDF), `CustomerFifReportGenerator`
- Infrastructure: the demo seed and `TestSeed` grant the report
- Adviser Portal: the parameter form on the Customer page
- Customer Portal: the parameter form on the Reports page
- Tests listed in §10

**Out**

- Any tax figure the tax year read does not give. The report prints and sums; it does not compute
- Tax payable, the foreign tax credit allowed, the limit on an imputation credit, a refund of RWT, the IR3 return and the IR1261 overseas income summary, and a filing with Inland Revenue (roadmap §7). The report lists the tax taken from each dividend (R22)
- The choice of a method, and whether the FIF rules apply to the Customer. Both branches are printed (roadmap Q14, T8)
- A transitional resident, a change of residence during a tax year, a date on the tax residency, and the choice to apply the rules below the threshold (roadmap §7, Q14)
- The income summary under the ordinary rules: dividends of exempt and New Zealand holdings, and interest. That is TR6, a report of its own. Such holdings are listed here with their classification and nothing else (R18)
- A schedule and an event trigger (R4)
- A run over every Customer of a Tenant or of an Adviser's book (R5)
- A data file beside the PDF: CSV, a spreadsheet or the JSON of the read (roadmap Q16; R11)
- A page in either portal that shows the tax year on screen. The JSON read stays reachable through both BFFs (tax-year-events §9), and no page calls it
- An estimate for a tax year in progress (roadmap Q13)
- A report for an `Overseas` Customer (roadmap Q14; R8)
- Marking a tax year as reported or final, period close and ledger replay (roadmap §1.2, Q15)
- Email delivery, retention and purge of the result file (reports, Out)
- A change to the tax attributes of the demo catalog (§9.1)
- Te reo Māori or any second language in the PDF

---

## 3. Stories

1. As an Adviser I open one of my Customers, choose the FIF Report and a tax year that has ended, and download a PDF with the threshold test, the FIF income under both methods and the figures behind them.
2. As a Customer I do the same for myself in the Customer Portal.
3. As a reader I see on the first page whether the report is complete, and if not, which holding is missing what, so that I never take a partial figure for the whole.
4. As a reader I can check a figure from the PDF alone: each value has its price and rate with their dates, each amount its rate, and each total the figures it is the sum of.
5. As a reader I see what the report does not say: that it is not tax advice, that it covers what the platform's ledger knows, and that figures of an account held elsewhere are the adviser's.
6. As a reader I find the tax already taken from my dividends, per holding and per country, beside the income figures.
7. As an Adviser I keep the PDF I gave a Customer. A report I make next month for the same tax year is a new one, and the old one does not change.
8. As an Adviser I cannot make the report for another Adviser's Customer, and a firm cannot make it for another firm's (404).
9. As a TenantAdmin I am told at once when I ask for the report of a Customer whose tax residency is `Overseas`.

---

## 4. Rules

### 4.1 The report in the catalog

| ID | Rule |
| --- | --- |
| R1 | **Catalog row.** Report `customer-fif` (PublicId `7d2f6b48-1a9c-4e35-a7d0-5c8e3b1f9a44`), name "FIF Report", function "Foreign investment fund (FIF) figures of one customer for one New Zealand tax year: the threshold test, and FIF income under the fair dividend rate and the comparative value methods.", scope `Customer`, allowed triggers `["manual"]`, active. Script `0062` inserts it when no row has the code. A SystemAdmin grants it per Tenant like any report (reports R3); a new Tenant is not granted it. |
| R2 | **Access.** No new permission. The rules of reports R1 to R8 hold as they do for the Customer Transactions Report: `reports.customer.create` to request and `reports.customer.read` to list and download. A TenantAdmin asks for any Customer of the Tenant, an Adviser for an assigned Customer, a Customer for themselves. Any other `customerId`, a Customer of another Tenant, or a missing one → 404. A report that is not granted, or not active → 404. Missing / dead Bearer → 401, never 302. A SystemAdmin has none of the `reports.*` policies → 403. |
| R3 | **Who sees an execution.** As every Customer-scoped execution (reports §8.1): a TenantAdmin every one of the Tenant, an Adviser those of assigned Customers, a Customer their own. The snapshot carries `customerId`, which is what the visibility reads. An Adviser who is no longer assigned to the Customer no longer sees the execution. |
| R4 | **Manual only (roadmap TR5).** The report has no schedule and no event trigger. `POST /reports/schedules` and `PUT /reports/schedules/{id}` that name it → 404 (reports R3). This holds also when a SystemAdmin has added `"scheduled"` to the catalog row with `PUT /reports/{id}`: the generator has no scheduled form, and a recurring job that names the report queues nothing. A tax year is asked for after it has ended and does not change by the clock; a schedule with a fixed `taxYear` would make the same report again, and a schedule has no relative parameter (reports, Out). |
| R5 | **One Customer per request.** There is no run over every Customer of a Tenant or of an Adviser. The tax year read is worked out per Customer, from that Customer's first Transaction on (tax-cost-threshold R5), so this slice adds no index and no batch read. This answers the question tax-year-events §6, tax-cost-threshold and fif-income left here. |

### 4.2 Parameters

| ID | Rule |
| --- | --- |
| R6 | **Parameters.** `{ "customerId": "uuid", "taxYear": 2026 }`. Both are required: a missing `customerId` → 400 `errors.customerId`, `customerId is required.`; a missing `taxYear` → 400 `errors.taxYear`, `taxYear is required.` `taxYear` is a JSON whole number; a string, a fraction or any other type → 400 `errors.parameters`, `parameters are not valid.`, as a wrong type is in every report. Any other field → 400 `errors.parameters`, `unknown field.` (reports §6.2). The stored snapshot is `{ customerId, taxYear }`. |
| R7 | **Which tax years (roadmap Q13).** Those the tax year read answers (tax-year-events R4), checked with the platform's clock when the request is made. Below 2009 → 400 `errors.taxYear`, `taxYear must be 2009 or later.` A tax year that has not ended, or whose 31 March is not final yet → 400 `errors.taxYear`, `Tax year {Y} can be reported from 2 April {Y}.` There is no report of a tax year in progress. A tax year in which the Customer held nothing is reported (R25). |
| R8 | **Tax residency (roadmap Q14).** After the scope check of R2, the request reads the Customer's tax profile (interest-run R2; a Customer with no saved profile reads as `NewZealand`). `Overseas` → 400 `errors.customerId`, `A FIF report is made for a customer whose tax residency is New Zealand.` The check runs after the scope check, so a caller who may not read the Customer gets 404 and learns nothing. The generator reads the profile again; `Overseas` then → the execution is `Failed` and no file is written. The profile has no date and no history: the residency is the one on file when the report is asked for, not the one of the tax year. The PDF prints it and says so (R15, R16). The platform does not store whether the Customer is a transitional resident or chose to apply the rules; for a `NewZealand` Customer the report always shows the threshold test and both methods. Sources: S2 ¶4, "The FIF rules apply only to a New Zealand tax resident"; S2 ¶5; S1 p.4. |

### 4.3 Generation

| ID | Rule |
| --- | --- |
| R9 | **What is read.** The generator calls `CustomerTaxLedger.ReadAsync` once, for the Customer and the tax year of the snapshot, and takes the platform time just before the call as `ledgerReadAt`. It also reads the Tenant's name, the Customer's name and tax residency, and the names of the Customer's Accounts. Every tax figure of the PDF is a figure of that one read. The report computes three kinds of figure and no other: the sums of R22 (the tax taken from dividends, per tax holding, per country and in total), the counts of R17, and whether the report is complete (R12). It reads no price, no rate and no Transaction of its own. |
| R10 | **This step only reads (roadmap T10).** Generation writes the result file and the status of its execution, as every report does (reports §7.1), and nothing else. It posts no Transaction, marks no tax year and changes no tax attribute. |
| R11 | **The PDF is the report, and its only stored copy (roadmap T9, Q16).** The file is stored and downloaded as reports §6.3 says. The PDF prints what the report used (R23): every price and rate with its date and source, the classification of each Instrument (instrument-tax-attributes R28), the threshold and the rate of the tax year, and `ledgerReadAt`. Nothing of the read is stored as data, and there is no second file. The numbers are readable as JSON through the tax year read, which answers the ledger as it stands at the time of that call. The execution has no `asOf`: the instant the ledger was read is known only when the worker runs, and it is in the PDF. |
| R12 | **Complete.** The report is complete when the tax year (tax-year-events R27), the threshold test (tax-cost-threshold R20) and the FIF income (fif-income R23) are each complete. Otherwise it is incomplete. An incomplete report is still made, and its execution is `Succeeded`: what is missing is a finding, not a failure (roadmap T5). The first page says "Incomplete" and how many holdings are affected, section 9 says what is missing (R24), and every page says "Incomplete" in its footer. |
| R13 | **New Zealand time.** Every instant of the PDF is printed in New Zealand time as `yyyy-MM-dd HH:mm`, by the Domain rule `NewZealandTime`: New Zealand is 13 hours ahead of UTC from 14:00 UTC on the day before the last Sunday of September until 14:00 UTC on the day before the first Sunday of April, and 12 hours ahead otherwise. It is the rule behind the fixed instants of tax-year-events R3, written out for the whole year, so a printed time never disagrees with the tax year an event is in: `TaxYear.Start(Y)` is 00:00 on 1 April. The rule holds for instants from 14:00 UTC on 29 September 2007; an earlier instant, which only the first cause of an unknown tax cost can be, is printed in UTC with "UTC" after it. The header also gives the instants of the tax year, the generation time and `ledgerReadAt` in UTC. A date with no zone (a valuation day, the date of a price or a rate, `acquiredOn`) is printed as it is. This answers the question tax-cost-threshold point 10 left here. Source: S3. |
| R14 | **Figures.** An NZD figure is printed with NZD's decimal places and no currency code. A figure in another currency (a price, a market value in the quote currency, a carried cost) is printed with its currency's decimal places and its code. Neither has a thousands separator, as the other reports print amounts (reports §9.3). A quantity and a rate are printed as the read gives them, with up to 8 decimal places and no trailing zeros. A figure the read gives as `null` is printed as "not available", never as 0 and never as an empty cell (roadmap T5). |

### 4.4 What the PDF prints

The PDF is built in code with QuestPDF, A4 landscape, as reports §9.3 says. It has a header, nine numbered sections in the order below, and a footer on every page with the Customer's name, "FIF Report, tax year {Y}", "Page {n} of {m}" and, when R12 says so, "Incomplete". The words of each sentence and label are those of this section and of §4.6 and §4.7.

| ID | Rule |
| --- | --- |
| R15 | **Header.** The report name; "Tenant: {name}"; "Customer: {name}"; "Tax year: {Y} (1 April {Y − 1} to 31 March {Y}, New Zealand time)"; "Tax year in UTC: {from} to {to}"; "Generated: {New Zealand time} New Zealand time ({UTC} UTC)"; "Ledger read at:" in the same form; "Tax residency on file: New Zealand"; "All amounts are in New Zealand dollars (NZD). All times are New Zealand time."; and "Status: Complete" or "Status: Incomplete. {n} of {m} holdings are affected. See section 9.", where a holding is affected when it is named by a reason of R24. |
| R16 | **Section 2, "About this report".** The statements of the table below, each printed as written. They are the statements the report makes about itself (roadmap TR5). |
| R17 | **Section 1, "Summary".** One sentence for the threshold test, by its outcome (§4.6). Then the FIF income: when the totals are given (fif-income R23), "FIF income, fair dividend rate method: {fdr.income}", "FIF income, comparative value method: {cv.income}" and "Lower: fair dividend rate", "Lower: comparative value" or "The two totals are equal"; with every reason `FdrNotAllowed`, also "These totals do not include {symbols}: the fair dividend rate method is not allowed for {it / them}. {Its / Their} income is worked out separately and added to the method you use."; when the totals are not given, "FIF income totals are not given. {n} holding(s) that belong in them could not be computed: {symbols}. A total without them would be neither a minimum nor an estimate." Then "These figures apply if the FIF rules apply to you. The threshold test above is one part of that question." Then the counts: "Holdings: {a} attributing interest(s), {b} exempt or not foreign, {c} not classified." With no tax holding in the year, R25 replaces all of it. The summary does not say which method to use, and "Lower" is a comparison of two figures (fif-income R22). |
| R18 | **Section 3, "Holdings and classification".** Every tax holding of the year, sorted as the read sorts them, in three groups: "Attributing interests" (`Attributing`, `AttributingNoFdr`), "Exempt or not foreign" (`ExemptAsxListed`, `ExemptOther`, `NotForeign`) and "Not classified". A row has the symbol, the name, the issuer country by its name (instrument-tax-attributes R6; "not stated" when there is none), the label of the treatment (§4.6), "classified from tax year {rowTaxYear}", the note when there is one, the opening and the closing quantity, and the status of its FIF income as "computed", "not computed" or "not in the FIF rules". An empty group is printed with "None." Under the second group: "These holdings are not in the FIF calculation. Their dividends and sales are taxed under the ordinary rules and are not in this report." Under the third: "A holding that is not classified may be an attributing interest. Ask your adviser to have it classified." A holding of the second group appears in no later section. Sources: S2 ¶7 for the ordinary rules on an exempt interest. |
| R19 | **Section 4, "Threshold test".** "Threshold for tax year {Y}: {amount}", or "No FIF threshold is set on this platform for tax year {Y}."; the total cost at the start, the highest total with when it was reached ("at the start of the tax year" or "on {New Zealand time}"), and the total at the end; the sentence of the outcome (§4.6); a table of the tax holdings that count (tax-cost-threshold R16) with the tax cost at the start and at the end, or "not known" with the cause (§4.7); and the steps of the test (its R24): time, symbol, the label of the event, the change and the total after it. With the highest total at the start and no step, the table of steps is replaced by "The total cost did not change during the tax year." When the test is incomplete, each reason is in section 9 and the section says "Incomplete: see section 9." |
| R20 | **Section 5, "FIF income".** The rate of the year. Under "Fair dividend rate method": the sum of the opening values, the sum of the FDR amounts, the sum of the peak holding method amounts, the sum of the quick sale gain amounts, the quick sale adjustment and the FIF income (the fields of `fifIncome.fdr`). Under "Comparative value method": the sum of the CV amounts and the FIF income (`fifIncome.cv`), and when the sum is below zero, "The total is below zero. It gives an income of 0.00 and cannot be used elsewhere." Then a table with one row per tax holding of the first and third group of R18: the symbol, the status, and when it is computed the opening value, the FDR amount, the peak holding method amount, the quick sale gain amount and the CV amount. Under the table: "The quick sale adjustment is the lesser of the two totals over all holdings, not a sum per holding." When the totals are not given, the two blocks are replaced by the sentence of R17 and the table stays. |
| R21 | **Section 6, "Each holding": one block per tax holding, in the order of section 3, for the first and third group.** (a) A title line as the row of R18. (b) "Opening, valued as of {valuationDay}" and "Closing, valued as of {valuationDay}": one row per line (tax-year-events R31) with the Account's name, the custody label, the quantity, the price with its currency and date, the market value in the quote currency, the rate with its date, and the value in NZD, or the label of the line's status when it is not valued; then the total quantity, with "({comparableQuantity} on the basis of the year's end)" when the two differ, and the total value. A quantity of 0 prints "Nothing held." (c) "Events": one row per event with its sequence, time, label (§4.6), Account, quantity, quantity after, comparable quantity after, amount, the rate with its date, the label of its status when the amount is missing, and the change of the tax cost with the tax cost after. An `Acquisition` with a fee prints "{amount} ({principal} and a fee of {fee})", a `Disposal` "{amount} ({principal} less a fee of {fee})". An `Arrival` or a `Departure` prints its price with its date, and an arrival its carried cost with its currency, the rate it was converted at and `acquiredOn` when stated. A `Move` prints "{quantity} from {account} ({time}) to {account}". No event in the year prints "No events in this tax year." (d) "Dividends", when there is one: per dividend the sequence, the shares, the gross, the foreign tax, the RWT, the NRWT, the imputation credit and the tax source. (e) "Tax cost: {opening} at the start, {closing} at the end", or "not known" with its cause and the time of that cause. (f) The FIF income of the holding when it is computed: "Fair dividend rate: {rate} x {openingValue} = {amount}"; the quick sale figures of fif-income R26 when the holding has a purchase and a sale in the year (the three quantities, the differential, the shares and the cost of the year's purchases with the average cost, the peak holding method amount, each match as "{quantity} of purchase {n} sold in sale {m}", each sale with its matched part of the proceeds, each dividend share, the cost of the matched shares and the gain amount), and otherwise "No quick sale in this tax year."; and "Comparative value: ({closingValue} + {sales} + {dividends}) - ({openingValue} + {purchases}) = {amount}". When it is not computed: the sentence of its reason (§4.7), and no figure of either method. (g) The reasons of the holding as "Missing: {short names}. See section 9." and its notices as "Notices: {short names}. See section 9.", by the short names of §4.7. |
| R22 | **Section 7, "Tax taken from dividends" (roadmap Q17).** One row per tax holding of the first and third group that has a `Dividend` in the year: the symbol, the issuer country by its name, and the sums over the dividends of the year of the gross, the foreign tax, the RWT, the NRWT and the imputation credit, each a sum of the NZD figures of the events (tax-year-events R11), and beside them the FDR amount and the CV amount of the holding when its income is computed, so that the tax taken stands next to the income of the same holding. Then one row per country with the sums of its holdings, countries in the order of their names and a holding with no country under "Country not stated", and a row "Total". A dividend with a figure that could not be converted leaves its row and the sums it is in as "not available". Under the table: "Foreign tax taken from a dividend may give a foreign tax credit. The credit is limited, and this report does not work it out. Resident withholding tax (RWT) is offset against the tax you owe. See Inland Revenue's guide to foreign investment funds (IR461)." With no dividend: "No dividends on these holdings in this tax year." The amounts are as booked; the report computes no credit and no tax. Sources: S1 p.20, the credit "is generally limited to the lesser of the tax paid and New Zealand liability on the segment of foreign income", and RWT "is used to offset tax payable with any excess refundable"; S1 p.21 and S2 ¶10 for the overseas income summary. |
| R23 | **Section 8, "Prices, rates and values used" (roadmap T9).** "Prices": every price row behind an opening line, a closing line, an arrival or a departure, once each: the symbol, the date, the amount with its currency and the source. "Rates": every rate row behind a line, an event or the cost of an event (tax-cost-threshold R24), once each, grouped by pair: the pair as "{currency} to NZD", the date, the rate and the source. Then "FIF threshold of tax year {Y}: {amount}" or "not set", "Fair dividend rate of tax year {Y}: {rate}", and "Ledger read at {time}". Then: "The tax cost at the start of the tax year comes from purchases of earlier tax years. Their rates are not listed here." A date is the date of the row, not of the event that used it. The classifications are in section 3. |
| R24 | **Section 9, "What to know about these figures".** First "What is missing", when the report is incomplete: one line per reason and tax holding, with the sentence of §4.7, for the reasons of the tax holding (tax-year-events R27), of the threshold test (tax-cost-threshold R20) and of the FIF income (fif-income R23). A reason that says the same of the same holding is printed once: `NotClassified` is one line, and `HoldingIncomplete` is not printed beside the reasons of the holding it stands for. Then "What these figures rest on": one line per notice and tax holding (tax-year-events R28), with the sentence of §4.7. With neither: "Nothing to report." |
| R25 | **Nothing held.** When the tax year has no tax holding, the PDF has its header, section 2 and one line: "No holdings in this tax year." The report is complete. |
| R26 | **What the report does not print.** No tax payable, no tax rate of the Customer, no foreign tax credit, no recommendation of a method and no statement that the rules apply or do not apply to the Customer (R8, fif-income R25). No IRD number: the platform holds none (interest-run R2). No holding of the Customer that is not in the ledger. |

Statements of R16, in this order:

| # | Statement | When |
| --- | --- | --- |
| 1 | "This report is not tax advice. It does not say whether the foreign investment fund (FIF) rules apply to you, which method to use, or what tax you owe." | Always |
| 2 | "The FIF rules apply to a New Zealand tax resident who is not a transitional resident. Your tax residency on file is New Zealand, as recorded when this report was made. The platform does not record whether you are a transitional resident, or what your residency was during the tax year." | Always |
| 3 | "This report covers the holdings recorded on this platform. Shares you hold elsewhere are in it only if your adviser has recorded them here. Shares that are not recorded count toward the threshold all the same." | Always |
| 4 | "Held elsewhere: recorded by your adviser from outside statements. The platform has not checked these figures." | A tax holding has the notice `ExternalFigures`. The sentence is that of custody-mode-grouping R15, word for word |
| 5 | "Both methods are shown. A person uses one method for all the holdings the totals cover in a tax year. The choice is yours." | Always |
| 6 | "Amounts in another currency are converted to New Zealand dollars at the exchange rate of their day. Section 8 lists every price and rate used." | Always |
| 7 | "This report shows the ledger as it was read at the time above. A report made later for the same tax year can differ, when a transaction is entered, changed in date or reversed after that time, or when a price or a rate is added." | Always |

Sources for the statements: S1 p.4 and S2 ¶4, ¶5 for 2; roadmap §4 for 3; S1 p.14 and S2 ¶16, as fif-income R19 cites them, for 5; tax-year-events R18 for 6; roadmap Q15 for 7. Statement 2 is the platform's summary of the two sources, not a quotation.

### 4.5 Entry points and seeds

| ID | Rule |
| --- | --- |
| R27 | **Adviser Portal.** On the Customer page, the Generate dropdown of the Reports section lists the report when it is granted. Its form has one field, "Tax year": a select of every tax year that can be reported today, the latest first and selected, each as "{Y} (1 April {Y − 1} to 31 March {Y})". `customerId` is the page's Customer. There is no schedule form, because the report does not allow `scheduled` (reports §10). The History panel shows "Tax year {Y}" as the parameters summary. A 400 is shown at the form with the server's message. The page contract is [adviser-portal-reports.md](../adviser-portal/adviser-portal-reports.md), D114 (§12). |
| R28 | **Customer Portal.** On the Reports page, the same form with `customerId` set to the Customer themselves. A Customer whose tax residency is `Overseas` sees the report in the list, and the 400 of R8 at the form. The page contract is [customer-portal.md](../customer-portal/customer-portal.md), C50 and C51 (§12). |
| R29 | **Grants.** `webapi --seed-demo` and `TestSeed` grant `customer-fif` to the demo tenant, with the three reports they grant today (demo-tenant-seed R8, bank-reconciliation R23): four grants. The rule is unchanged: idempotent on `(TenantId, ReportId)`, so a run on a demo tenant that has three grants adds the fourth; a platform-inactive report is not granted; a missing catalog code is an error. The log line of demo-tenant-seed R12 then reads `reports 4 granted, 0 existing` on a new tenant. Other tenants are not auto-granted. |

Policies: none added.

### 4.6 Labels and the sentences of an outcome

The labels of a treatment (R18):

| `fifTreatment` | Label |
| --- | --- |
| `Attributing` | Attributing interest |
| `AttributingNoFdr` | Attributing interest, fair dividend rate not allowed |
| `ExemptAsxListed` | Exempt: ASX-listed Australian share |
| `ExemptOther` | Exempt: {note} |
| `NotForeign` | Not foreign (New Zealand) |
| `NotClassified` | Not classified |

The labels of a tax event (R19, R21):

| `kind` | Label |
| --- | --- |
| `Acquisition` | Purchase |
| `Disposal` | Sale |
| `Dividend` | Dividend |
| `Reorganisation` | Share reorganisation |
| `Arrival` | Transferred in, at market value |
| `Departure` | Transferred out, at market value |
| `Move` | Moved between your accounts |
| `Correction` | Correction |

The labels of a status that leaves a figure missing (R21): `NoRate` "No exchange rate"; `StaleRate` "Exchange rate too old"; `NoPrice` "No price"; `StalePrice` "Price too old"; `PriceBeforeCorporateAction` "Price from before a corporate action"; `NegativeQuantity` "Quantity below 0". The last four are the words of valuation-history R18. A custody mode is printed as "Held on platform" or "Held elsewhere" (custody-mode-grouping R14).

The sentence of the threshold test (R17, R19), where {when} is "at the start of the tax year" or "on {New Zealand time}":

| `outcome` | `complete` | Sentence |
| --- | --- | --- |
| `NotAbove` | `true` | "Not above the threshold. The total cost of your attributing interests was at most {peak.total}, {when}. The threshold for tax year {Y} is {amount}." Section 4 adds: "A person at or below the threshold does not apply the FIF rules unless they choose to. The ordinary tax rules then apply to their dividends." |
| `Above` | `true` | "Above the threshold. The total cost of your attributing interests reached {peak.total} {when}. The threshold for tax year {Y} is {amount}. Above it at any time in the year, every attributing interest is in the FIF rules for the whole year." |
| `Above` | `false` | "Above the threshold. The costs that are known reached {peak.total} {when}. The threshold for tax year {Y} is {amount}. Some costs are not known. They can only add to the total." |
| `Unknown` | `false`, with a threshold | "The threshold test could not be completed. The costs that are known reached {peak.total} {when}. The threshold for tax year {Y} is {amount}. Some costs are not known, so the total may have been above it." |
| `Unknown` | `false`, with no threshold | "No FIF threshold is set on this platform for tax year {Y}. The total cost of your attributing interests that is known reached {peak.total} {when}." |

Sources: S1 p.5, individuals "do not need to apply the FIF rules if the total cost of their attributing interests is below $50,000 throughout the year"; S2 ¶2, "If the FIF rules do not apply, the ordinary tax rules apply"; S2 ¶8, "If at any time during the income year ... then you are in the FIF rules for that year" and "You may also choose to apply the FIF rules". The outcome itself is tax-cost-threshold R19.

### 4.7 The sentences of section 9

Each sentence starts with the symbol of its tax holding and a colon, except `NoThreshold`.

What is missing (R24):

| From | Reason | Sentence |
| --- | --- | --- |
| Tax holding, threshold test, FIF income | `NotClassified` | "Not classified for tax year {Y}. It may be an attributing interest. It is in no total, the threshold test cannot be completed, and FIF income totals are not given." |
| Tax holding | `OpeningNotValued` | "No market value at the start of the tax year ({label of the line's status}). Its FIF income is not computed." |
| Tax holding | `ClosingNotValued` | "No market value at the end of the tax year ({label of the line's status}). Its FIF income is not computed." |
| Tax holding | `AmountMissing` | "An amount of event {sequence} could not be worked out ({label of its status}). Its FIF income is not computed." One line per event |
| Tax holding | `NegativeQuantity` | "The quantity is below 0 at some point of the tax year. An entry is missing or was dated too late." |
| Tax holding | `NoComparableQuantity` | "A share reorganisation was booked on an account that held nothing, so quantities before and after it cannot be compared." |
| Threshold test | `CostUnknown` | "The cost is not known: {sentence of the cause}. It is not in the total of the threshold test." |
| Threshold test | `TreatmentChanged` | "Its classification changed while you held it. Tax law then starts the cost again from a market value, which this platform does not work out. The cost shown is the cost paid." |
| Threshold test | `NoThreshold` | "No FIF threshold is set on this platform for tax year {Y}." |
| FIF income | `FdrNotAllowed` | "The fair dividend rate method is not allowed for this holding. Its income is not computed here and is in no total. It is worked out under the comparative value method on its own, or the deemed rate of return." |

The causes of an unknown tax cost (tax-cost-threshold R13), each followed by "since {time}": `NoCarriedCost` "it was transferred in with no cost stated"; `NoRate` "no exchange rate is stored for the day of a purchase"; `StaleRate` "the exchange rate stored for the day of a purchase is too old"; `NegativeQuantity` "more was sold or transferred out than was held"; `NegativeCost` "a correction left a cost below 0".

The short names of R21 (g), in the order of the tables of this section: `NotClassified` "Not classified"; `OpeningNotValued` "No market value at the start"; `ClosingNotValued` "No market value at the end"; `AmountMissing` "An amount missing"; `NegativeQuantity` "Quantity below 0"; `NoComparableQuantity` "Quantities not comparable"; `CostUnknown` "Cost not known"; `TreatmentChanged` "Classification changed"; `FdrNotAllowed` "Fair dividend rate not allowed"; `ExternalFigures` "Held elsewhere"; `ArrivalAtMarketValue` "Transferred in at market value"; `DepartureAtMarketValue` "Transferred out at market value"; `Move` "Moved between your accounts"; `Scrip` "Shares in place of a dividend"; `Correction` "Correction"; `DividendTaxSourceDisagrees` "Tax source differs from the issuer country"; `CarriedCost` "Cost carried in".

What these figures rest on (R24):

| Notice | Sentence |
| --- | --- |
| `ExternalFigures` | "Some of these figures come from an account held elsewhere. They were recorded by your adviser from outside statements, and the platform has not checked them." |
| `ArrivalAtMarketValue` | "Shares were transferred in. They are counted as bought at the market value of that day, because the platform does not know when they were first acquired." |
| `DepartureAtMarketValue` | "Shares were transferred out. They are counted as sold at the market value of that day, because the platform does not know what became of them." |
| `Move` | "A transfer out of one of your accounts and a transfer into another were read as one move, not as a sale and a purchase. Nothing in the ledger links the two entries." |
| `Scrip` | "Shares were issued in place of a dividend. They are read as a share reorganisation. The platform cannot know whether the issue was a taxable dividend." |
| `Correction` | "A correction changed this holding. It is not counted as a purchase or a sale." |
| `DividendTaxSourceDisagrees` | "A dividend states a tax source that does not agree with the issuer country of the holding. Both are shown as recorded." |
| `CarriedCost` | "The cost of shares that were transferred in is the cost stated at the transfer, converted at one exchange rate." |

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `NewZealandTime` | Static (new, `src/Domain/Tax`) | `Offset(instant)` → 13 or 12 hours by the rule of R13, or none for an instant before 14:00 UTC on 29 September 2007. `Local(instant)` → the date and time in New Zealand. It reads no clock, no setting and no time zone data of the host |
| `TaxYear` | Value (existing) | No change. `NewZealandTime.Local(TaxYear.Start(Y))` is 00:00 on 1 April `Y − 1` for every `Y` from 2009 |
| `CustomerTaxYear`, `TaxHolding`, `ThresholdTest`, `FifIncome` and the other types of [domain-model.md](../domain-model.md) §8.24 | Existing | No change. The report reads them |
| `Report`, `TenantReport`, `ReportExecution`, `ReportSchedule` | Existing | No change. The catalog gains a row, not a type |

```text
NZDT (UTC+13)   from 14:00 UTC on the Saturday before the last Sunday of September
                until 14:00 UTC on the Saturday before the first Sunday of April
NZST (UTC+12)   otherwise
the changes     02:00 NZST -> 03:00 NZDT in September; 03:00 NZDT -> 02:00 NZST in April     (R13)
```

Invariants:

- `NewZealandTime.Local(TaxYear.Start(Y))` is midnight at the start of 1 April, for every tax year that can be read.
- The offset is 12 or 13 hours, and it changes only at the two instants of a year the rule names.
- The report prints no tax figure that the tax year read of the same instant does not hold, apart from the sums and counts of R9.
- A stored report is never changed.

Domain events: none.

[domain-model.md](../domain-model.md) §8.24 holds `NewZealandTime`.

---

## 6. Database

| Table | Change | Index / FK |
| --- | --- | --- |
| `Reports` | One row, `customer-fif` (R1) | None |

Script: `database/schema/0062_fif_report.sql`. It inserts the row when no row has the code, as `0045` does for `bank-reconciliation`. No table, column, constraint or index. `CK_Reports_Scope` already allows `Customer`.

The generator reads the tables of tax-year-events §6 through `CustomerTaxLedger`, and `Tenants`, `Users`, `CustomerTaxProfiles` and `Accounts` for the names and the tax residency. Nothing new is stored: `ReportExecutions` keeps the snapshot of R6 and the key of the file (reports §6.3).

[database-design.md](../database-design.md) §9 and §14 list the script and the row. Do not add EF migrations.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Static | `CustomerFifBinder` (new) | `Code` `customer-fif`. `Bind(parameters, today)` → `{ CustomerId, TaxYear, SnapshotJson }`, with the checks of R6 and R7; `today` is the platform's UTC date, for `TaxYear.IsReadable` |
| Command | `RequestReport` (existing) | A branch for the code: bind, the Customer scope check the Customer Transactions Report uses, then the tax residency of R8. No `asOf` |
| Static | `CustomerFifReport` (new) | `Build(read, names, residency, generatedAt, ledgerReadAt)` → the content of the PDF as plain records: the header, the nine sections, the sentences and the labels of §4.4 to §4.7. No I/O. The one place the words of the report are chosen |
| Static | `CustomerFifPdf` (new) | `Render(content)` → the PDF bytes, with QuestPDF. Layout only |
| Service | `CustomerFifReportGenerator` (new) | `IReportGenerator` for the code: R8 again, R9, then `Build` and `Render`. Registered beside the other generators |
| Service | `CustomerTaxLedger` (existing) | No change. The generator is its second caller, on `worker` |
| Command | `CreateReportSchedule`, `UpdateReportSchedule`; job `ReportJob.RunScheduled` | No change. Each names the report codes that can be scheduled, and `customer-fif` is not among them (R4) |
| Seed | `DemoCatalog.ReportCodes` | Gains `customer-fif` (R29); the demo seed and `TestSeed` read the list |

Where each rule lives:

| Rule | Where |
| --- | --- |
| Parameters and which tax years (R6, R7) | `CustomerFifBinder` |
| Scope and tax residency at the request (R2, R8) | `RequestReport` |
| Tax residency at generation, what is read (R8, R9) | `CustomerFifReportGenerator` |
| Complete, the sums and counts, every sentence and label (R12, R14 to R26) | `CustomerFifReport.Build` |
| New Zealand time (R13) | `NewZealandTime` |
| Pages, tables, footer (§4.4) | `CustomerFifPdf` |

---

## 8. API

No new route. The report uses the routes of reports §8.

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| POST | `/reports/request` | `reports.customer.create` | 202 `{ "id" }` | 400 / 401 / 403 / 404 |
| POST | `/reports/schedules` | `reports.customer.create` | — | 404 for this report (R4) |
| GET | `/reports/executions`, `/reports/executions/{id}` | `reports.customer.read` | 200, `parameters` `{ customerId, taxYear }`, no `asOf` | as reports §8 |
| GET | `/reports/executions/{id}/download` | `reports.customer.read` | 200 `application/pdf` | 403 / 404 / 409 |

### 8.1 Bodies

`POST /reports/request`:

```json
{
  "reportId": "7d2f6b48-1a9c-4e35-a7d0-5c8e3b1f9a44",
  "parameters": {
    "customerId": "<customer publicId>",
    "taxYear": 2026
  }
}
```

An item of `GET /reports/executions`:

```json
{
  "id": "<execution publicId>",
  "reportId": "7d2f6b48-1a9c-4e35-a7d0-5c8e3b1f9a44",
  "status": "Succeeded",
  "triggerKind": "Manual",
  "created": "2026-11-28T03:12:40Z",
  "parameters": { "customerId": "<customer publicId>", "taxYear": 2026 }
}
```

### 8.2 Errors

All refusals are ordinary validation (400 with `errors`, no problem `code`).

| Case | Response |
| --- | --- |
| No Bearer, or a dead one | 401, never 302 |
| A SystemAdmin | 403 |
| The report is not granted to the Tenant, or not active; a Customer of another Tenant, an unassigned Customer, another Customer; a missing id | 404 |
| A schedule that names the report | 404 |
| `customerId` or `taxYear` missing | 400 `errors.customerId` / `errors.taxYear` |
| `taxYear` not a JSON whole number; an unknown field | 400 `errors.parameters` |
| `taxYear` below 2009; not ended, or its last day not final yet | 400 `errors.taxYear` (R7) |
| The Customer's tax residency is `Overseas` | 400 `errors.customerId` (R8) |
| The download of an execution that is not `Succeeded` | 409 `report_not_ready` (reports) |

An incomplete report is not an error: the execution is `Succeeded` and the PDF says what is missing.

Cross-tenant or wrong-collection id → **404**, not 403. Keep the resource catalog in [api-design.md](../api-design.md) pointing to reports for the routes and here for the parameters of this report.

---

## 9. UI

**Adviser Portal.** The Customer page (`/customers/:id`) gains the form of R27 in its Reports section. Nothing else on the page changes: the dropdown, the History panel, polling and download are those of [adviser-portal-reports.md](../adviser-portal/adviser-portal-reports.md). The Reports tab (`/reports`) does not list the report: it is not Book-scoped. The BFF already proxies `/reports`.

**Customer Portal.** The Reports page gains the form of R28. Its dropdown already lists every Customer-scoped report that is granted. The BFF already proxies `/reports`.

**Back Office.** No change. The `/reports` page lists the new catalog row from `GET /reports/catalog`, and the tenant configuration page offers it for a grant, as for any catalog row.

The tax years a form offers are worked out in the browser from today's UTC date: tax year `Y` is offered from 2 April `Y`, down to 2009. The server checks the year again (R7).

### 9.1 Seeds and hosting

- `0062` runs with the schema applicator on the next start of `webapi` or `worker`. The row is active and granted to nobody.
- The demo tenant on the VPS gets the grant when an operator runs `webapi --seed-demo` again ([deployment.md](../deployment.md) §12). `TestSeed` grants it in Development on the next start.
- No change to AppHost, `compose.yml` or any setting. The generator runs on `worker`, which already has the stores `CustomerTaxLedger` reads.

What a report can be complete for is what the tax year read can be complete for (tax-year-events §9.1, tax-cost-threshold §9.1, fif-income §9.1), and the report checks none of it:

- Tax year 2026 is the only one that can be asked for until 2 April 2027.
- In the demo tenant the catalog is classified from tax year 2027. A report of tax year 2026 lists every holding as not classified: it is incomplete, and it has no totals. A person can add a row for tax year 2026 to an Instrument in the Back Office.
- In Development the history floor date is 2026-01-01, so a holding held on 31 March 2025 has no opening price in tax year 2026.
- From tax year 2027 the threshold has no value until tax-cost-threshold R15 is amended. The report then says so and is incomplete.

So the first complete report of a holding of the demo catalog that was held over a year end is one of tax year 2027, from 2 April 2027, once the threshold of that year is set. The functional tests write their own Instruments, prices, rates and Transactions and do not depend on any of this.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests (`Tax/NewZealandTimeTests`) | 13 hours one second before 14:00 UTC on 3 April 2027 and 12 hours at it; 12 hours one second before 14:00 UTC on 25 September 2027 and 13 hours at it; the same for the changes of 26 September 2026 and 1 April 2028 (UTC); `Local(TaxYear.Start(Y))` is 00:00 on 1 April for 2009, 2026 and 2027, also in a year whose 1 April is a Sunday; the four times of §1.1 (09:15 on 1 April, 00:01 on 3 July, 16:20 on 14 November, 10:40 on 3 February); no offset before 14:00 UTC on 29 September 2007 |
| Application.UnitTests (`Reports/CustomerFifBinderTests`) | The two required fields; `taxYear` as a string or a fraction; an unknown field; 2008; a year in progress; 1 April of the year (not final yet) and 2 April; the snapshot holds the two fields only |
| Application.UnitTests (`Reports/CustomerFifReportTests`) | The content of §1.1 from the tax year of the worked example; each sentence of the threshold test (§4.6); the totals given, given without an `AttributingNoFdr` holding, and not given; the three groups of section 3 and an exempt holding in no later section; the sums of section 7 per holding, per country and in total, with "Country not stated" and a dividend with no rate; each price and each rate once in section 8; every reason and every notice gives its sentence of §4.7; `NotClassified` is one line and `HoldingIncomplete` is not printed beside the holding's own reasons; complete and incomplete with the count of affected holdings; a `null` figure is "not available"; a Customer with nothing; statement 4 only with `ExternalFigures` |
| Application.UnitTests (`Reports/CustomerFifPdfTests`) | `Render` gives a PDF whose text holds the header lines, the nine section titles and the figures of §1.1; the footer has the page count and "Incomplete" on every page of an incomplete report; a report of many events runs over several pages |
| Application.FunctionalTests (`Reports/RequestCustomerFifTests`) | No Bearer → 401 not 302; a TenantAdmin, the assigned Adviser and the Customer → 202 with the snapshot; a SystemAdmin → 403; an unassigned Adviser, another Customer and an unknown id → 404; the report not granted → 404; each 400 of §8.2; an `Overseas` Customer → 400 at `customerId`, and an out-of-scope caller of that Customer still gets 404; a Customer with no saved tax profile → 202; `POST /reports/schedules` for the report → 404, also after `scheduled` was added to the catalog row. Two-tenant fixture: the other Tenant's Customer → 404, and the other Tenant's executions are in no list |
| Application.FunctionalTests (`Reports/GenerateCustomerFifTests`) | The worked example of tax-year-events through the API, then the job: the execution is `Succeeded`, has no `asOf`, and the text of the downloaded PDF holds the figures of §1.1; an entry backdated on the External account after the first report leaves the first file as it is, and a second request gives another figure; a holding with no closing price gives a `Succeeded` execution whose PDF says "Incomplete" and gives no totals; the tax residency set to `Overseas` between the request and the job gives `Failed` and no file; no Transaction, price or holding of another Tenant's Customer is in the PDF |
| Infrastructure.IntegrationTests (`AppliedSchema/ReportsSchemaTests`, `AppliedSchema/TestSeedReportsTests`, the demo seed tests) | The catalog row of R1 after the scripts ran, once after a second run; `TestSeed` and the demo seed grant four reports; a demo tenant with three grants gets the fourth and keeps the three |
| Adviser Portal and Customer Portal tests | The form appears for the report's code; the tax years offered on 1 April and on 2 April of a year; the body of §8.1; no schedule form; "Tax year {Y}" in the History summary; a 400 message at the form |

Also: `Reports/CustomerFifReportDetailTests` (sections 6 to 9) and `Reports/FifReportFixture` in Application.UnitTests; `shared/time/taxYears.test.ts` and `features/customers/CustomerDetailPage.test.tsx` in the Adviser Portal; `shared/time/taxYears.test.ts` and `portal.test.tsx` in the Customer Portal.

The schema change touches `database/schema/`, so the whole of Infrastructure.IntegrationTests runs before its commit.

---

## 11. Locked in this spec

No new ADR.

| Item | Lock |
| --- | --- |
| Catalog row | `customer-fif`, scope `Customer`, manual only, script `0062` |
| Access | The policies and the scope of the Customer Transactions Report; a Customer for themselves |
| Parameters | `{ customerId, taxYear }`; the tax years of the tax year read |
| Tax residency | `Overseas` → 400 at the request, `Failed` at generation; the residency on file, with no date |
| Source | One call of `CustomerTaxLedger`; no figure of the report's own apart from the sums of section 7 and the counts |
| Stored copy | The PDF, and nothing else; no `asOf` |
| Complete | The tax year, the threshold test and the FIF income each complete; an incomplete report is made and says so |
| Time | New Zealand time by `NewZealandTime`; UTC beside it in the header |
| Sections | Summary; About this report; Holdings and classification; Threshold test; FIF income; a block per holding; Tax taken from dividends; Prices, rates and values used; What to know about these figures |
| Words | The statements of §4.4, the labels and sentences of §4.6 and §4.7 |
| Foreign tax | As booked, per tax holding and issuer country; no credit computed |
| Entry points | The Customer page of the Adviser Portal; the Reports page of the Customer Portal |
| Demo | The fourth grant; the tax attributes of the demo catalog unchanged |
| Routes, tables, permissions, problem codes, events, ports, jobs, settings | None |

### Points confirmed

Confirmed on 2026-11-28, every one as recommended. Each point states the choice, written into the rules above, and the alternative that was not taken.

1. **One catalog report, `customer-fif`, for FIF only** (R1). Recommended: roadmap TR5. The income summary (TR6) stays a report of its own, because it is what a Customer below the threshold returns and it reads interest, which no rule here touches. Alternative: one report with the income summary as a section, which makes the optional item part of this one.
2. **Manual only, also when the catalog row is changed** (R4). Recommended by the roadmap. The schedule code already names the codes it accepts, so the report is refused there without a new rule. Alternative: `PUT /reports/{id}` refuses `"scheduled"` for a report that has no scheduled form, which is a new rule of the catalog.
3. **A Customer may ask for their own report** (R2). Recommended by the roadmap, "as the Customer Transactions Report". The report shows nothing the Customer cannot read through the tax year read already (tax-year-events point 17). Alternative: staff only, so that an Adviser looks at an incomplete report before the Customer sees it.
4. **The tax years of the read, checked at the request** (R7). Recommended: roadmap Q13, already written as tax-year-events R4. Alternative: the generator checks, and a wrong year is a `Failed` execution with no message in the list.
5. **An `Overseas` Customer is refused with a 400 at the request** (R8). Recommended: roadmap Q14 says the report is refused, and a person who asks should learn why at once and not from a `Failed` row. Alternative A: the report is made with a banner "The FIF rules do not apply to a non-resident". Alternative B: 404, which hides the reason.
6. **The tax residency is the one on file when the report is asked for** (R8). Recommended: it is all the platform has. The profile has no date (roadmap §3), so a Customer who left New Zealand after the tax year gets no report for it, and one who arrived after it gets one. The PDF says which residency it read and that it knows no other. Alternative: a dated residency, which is a slice of its own on the tax profile.
7. **An incomplete report is made, and marked on every page** (R12). Recommended: roadmap T5 says incomplete, not refused, and the report is how a person finds out what is missing. Alternative: the request or the job fails while anything is incomplete, so that no partial PDF exists.
8. **The report shows the threshold test and both methods whatever the test says** (R17). Recommended: roadmap Q14, "the Customer reads the branch that applies", and fif-income R2. Alternative: no income section when the outcome is `NotAbove`.
9. **The PDF is the only stored copy, and the execution has no `asOf`** (R11). Recommended: roadmap Q16. The PDF prints everything roadmap T9 lists, so the stored report can be read without the platform. Alternative A: the worker also stores the JSON of the read beside the PDF, with a download, as a record that a program can compare with a later run. Alternative B: the worker writes `ledgerReadAt` to `asOf` of the execution, so that the History panel shows it; that makes `asOf` mean two things, the request time for the Adviser Balances Report and the read time here.
10. **New Zealand time, by a rule in Domain** (R13). Recommended: the reader is a New Zealand taxpayer and the tax year is cut in New Zealand time, so a UTC time prints the first morning of a tax year as the day before it began. The rule is two sentences, needs no time zone data on the host, and is the same rule tax-year-events R3 rests on, so the PDF cannot disagree with the cut. The cost: a change of New Zealand's daylight time needs an amendment, as it does there. Alternative A: UTC throughout, as the other reports print, with the UTC instants of the tax year in the header. Alternative B: the zone `Pacific/Auckland` from the host, which tax-year-events point 1 chose not to depend on.
11. **The rates behind the opening tax cost are not listed** (R23). Recommended: the tax cost at the start of the year comes from every purchase since the Customer's first, and listing each rate of each earlier year would make a Customer of ten years a report of mostly old rates. The PDF says that they are not listed. Alternative: `CustomerTaxLedger` gives the rate rows of the whole walk and section 8 lists them all, which is roadmap T9 read to the letter.
12. **Exempt and New Zealand holdings are listed with their classification only** (R18). Recommended: roadmap §4 point 1 asks which holdings are exempt and under which exemption. Their dividends belong to the income summary (TR6), and no price is read for them (tax-year-events R23). Alternative: a block with their events and dividends too, which is half of TR6.
13. **The tax taken from dividends is per tax holding and per issuer country, as booked, with no credit** (R22). Recommended: roadmap Q17. The country is the issuer country of the Instrument, the one country the platform knows; a dividend's own statement is `NewZealand` or `Foreign`, and a disagreement is a notice. Alternative: per dividend only, with no subtotal by country.
14. **A tax holding that is not classified is printed in full** (R20 to R22). Recommended: it may be an attributing interest, the read values it, and the person who will classify it needs its figures. Alternative: listed in section 3 only, until it is classified.
15. **One Customer per request, and no run over a Tenant** (R5). Recommended: the earlier specs asked the question and none needs the answer to be yes. A report is asked for once a year per Customer, and the walk is per Customer. Alternative: a Book-scoped or Tenant-scoped report that makes one PDF per Customer, which needs a queue of executions and a batch read of rates.
16. **No page that shows the tax year on screen** (§2 Out). Recommended: the PDF is the report (roadmap Q16), and a page would need every label and sentence of §4.6 and §4.7 a second time, in TypeScript. Alternative: a "Tax" tab on the Customer page that shows the summary from the JSON read before a report is made.
17. **The form offers every tax year that can be reported** (R27). Recommended: the server accepts each of them, and a cut-off would be a number with no source. The list is eighteen years long today. Alternative: the latest seven.
18. **The demo seed grants the report and leaves the tax attributes alone** (R29, §9.1). Recommended: roadmap TR5 names the grant only. The cost: no report of the demo catalog can be complete before 2 April 2027, because the catalog is classified from tax year 2027. Alternative: the seeds also write rows for tax year 2026, which is the year a person checked in Inland Revenue's tool (instrument-tax-attributes point 9). That changes instrument-tax-attributes R24 and demo-tenant-seed R6, and it does not reach a demo tenant that is already classified.
19. **The words of the PDF are part of the contract** (§4.4, §4.6, §4.7). Recommended: a tax document is read by people who will act on a sentence, so each sentence is reviewed once, here, and the tests assert it. Alternative: the spec fixes the sections and the figures, and the words are the implementer's.

### Found while writing

- **The sources were read as pages.** The fetch tool saved the two PDFs, and the pages named in §14 were read from them directly, not through a restatement. The pages on the threshold (S1 p.9, p.10) and on the two methods (p.11 to p.16) were not read again: this slice states no rule on them, and tax-cost-threshold and fif-income cite them.
- **The sentences of the report are the platform's, not Inland Revenue's.** Statement 2 of R16, the sentences of the threshold test and the note under section 7 summarise the sources of §14. A person who knows the rules should still read them; it was not done when the spec was accepted.
- **"By country" is not in the pages read.** Roadmap §2 says the overseas income summary (IR1261) is "by country", citing S1 p.21 and S2 ¶10 to ¶11. S1 p.21 says individuals "must file an Overseas income summary – IR1261 form", and S2 ¶10 says it is filed "to declare your overseas income sources and claim foreign tax credits". Neither says "by country". R22 groups by issuer country because roadmap Q17 decided it; the form itself was not read.
- **The tax profile was written for withholding.** interest-run R2 says tax residency "decides the withholding of R4 and nothing else". With this slice it also decides whether a FIF report is made.
- **A New Zealand Customer can have NRWT.** A dividend paid while the profile said `Overseas` has an `Nrwt` leg. The report prints the column as booked and says nothing about it.
- **The tax taken is the same table for both branches.** A person the rules apply to reads section 7 for the tax already paid; a person below the threshold reads it for the dividends of their attributing holdings, which the ordinary rules then tax. The dividends of exempt and New Zealand holdings are not in it.
- **A rate date and a printed time can be a day apart** (§1.1, event 8). The conversion uses the UTC date of `bookedAt`, and the PDF prints the event in New Zealand time. Both are right, and the PDF prints the date of the rate so that a reader can look the row up.
- **The first cause of an unknown tax cost can be older than the rule of R13.** A holding that arrived with no cost in 2005 has its cause in 2005, before the daylight time rule of 2007. R13 prints that one instant in UTC.
- **The schedule code refuses the report by itself.** `ReportScheduleRules.EnsureAvailableAsync` and `ReportJob.RunScheduled` accept the three codes they name and answer "not found" or do nothing for any other, so R4 needs no change there. Read from the code on 2026-11-28.
- **The report can be long.** A Customer with a hundred trades in a year gets a block of a hundred rows per holding. There is no limit and no short form.
- **Three older texts are behind.** [deployment.md](../deployment.md) §12 step 14 says the demo seed writes "its two report grants"; it writes three today. [adviser-portal-reports.md](../adviser-portal/adviser-portal-reports.md) "Out" says the Customer has no portal client; the Customer Portal has existed since 2026-11-02. [reports.md](reports.md) §8 gives the `scope` of a catalog item and of the two list filters as `Customer` or `Book`, where its R4 says three scopes. The first is corrected with §12; the other two are reported here and not changed by this slice.
- **The name of a country is the name of the platform's list.** §1.1 first printed "United States"; the list of instrument-tax-attributes R6 names `US` "United States of America", and R18 prints that name. The example was corrected when the spec was accepted.
- **Inland Revenue's calculator is still not used.** fif-income left it open as a second check. A PDF of this report for the guide's quick sale example would be the natural thing to hold beside it.

Still open, not this slice:

- The income summary under the ordinary rules: TR6
- A dated tax residency, a transitional resident and the choice to apply the rules below the threshold (roadmap §7)
- A data file beside the PDF, and a page that shows the tax year on screen
- A report over every Customer of an Adviser or of a Tenant
- The threshold of tax year 2027 and later (tax-cost-threshold R15)
- The tax attributes of the demo catalog for tax year 2026
- The foreign tax credit allowed, tax payable and the IR3 and IR1261 forms (roadmap §7)
- Retention and purge of result files (reports, still open)
- Ledger replay and period close (roadmap §1.2). Until then a stored report is the only record of what was reported

---

## 12. Changes to other docs

Each doc was changed in place when this spec was accepted (2026-11-28).

| Doc | Change |
| --- | --- |
| [reports.md](reports.md) | Intro and §2: a fourth report. §6 and §6.1: script `0062`, the seeded code and PublicId, triggers `manual` only, four demo grants. §7.1: the parameters `{ customerId, taxYear }`. A new §9.2b "FIF Report" that points here, as §9.2a does for the bank reconciliation. §10: the Customer page has two forms. §12 "First reports" and "Triggers in phase". History row |
| [adviser-portal-reports.md](../adviser-portal/adviser-portal-reports.md) | Backend prerequisites: four seeded reports and the new PublicId. A new section "FIF Report" with D114: the form of R27, no schedule form, the History summary, the 400 at the form, and its tests. "Still open": the hard-coded forms are three shapes. History row |
| [customer-portal.md](../customer-portal/customer-portal.md) | A new section "FIF Report" with C50 (the form of R28 and the body of §8.1) and C51 (an `Overseas` Customer sees the server's message at the form). "Reports page (field bindings)": a second form. "Still open": the line on forms beyond Customer Transactions goes. History row |
| [demo-tenant-seed.md](demo-tenant-seed.md) | §1, R8 and R12: four report grants, `customer-fif` among them. The intro of the features index row |
| [deployment.md](../deployment.md) | §12 step 14: the demo seed writes four report grants (it says two), and a second run adds the new one |
| [tax-year-events.md](tax-year-events.md) | §2 Out, §9 and "Still open": the report and its entry points are this spec's, no longer "TR5". R2: what a report does for an `Overseas` Customer is R8 here. §7: `CustomerTaxLedger` is called by the report generator on `worker`. History row |
| [tax-cost-threshold.md](tax-cost-threshold.md) | R2, R18, point 10 and "Still open": the report prints the highest total in New Zealand time (R13 here) and a sentence per outcome (§4.6 here); there is no run over every Customer (R5 here). History row |
| [fif-income.md](fif-income.md) | §2 Out, R2, §9 and "Still open": what the report prints for an incomplete result and for a holding where FDR is not allowed is R17, R20 and §4.7 here; the foreign tax beside the income is R22 here. A History section with its first row |
| [instrument-tax-attributes.md](instrument-tax-attributes.md) | R18 and R28: the FIF report prints the treatment it used, the tax year of the row, the note and the issuer country (R18 here), and a stored PDF does not change when a classification does. "Still open" |
| [interest-run.md](interest-run.md) | R2: tax residency also decides whether a FIF report is made (R8 here). §2 Out and point 19: the FIF figures of a tax year have a report; the income summary and a tax certificate stay out (TR6). History row |
| [dividends.md](dividends.md), [external-income.md](external-income.md) | §2 Out: the tax taken from the dividends of attributing holdings is listed in the FIF report (R22 here); a dividend report under the ordinary rules stays out (TR6) |
| [custody-mode-grouping.md](custody-mode-grouping.md) | R15: the note is also printed in the FIF report (statement 4 of R16 here) |
| [api-design.md](../api-design.md) | §7.8: the report `customer-fif`, its parameters and its 400 cases; manual only. Change log |
| [domain-model.md](../domain-model.md) | §8.24: `NewZealandTime` and its invariant. §11: a fourth catalog row is data, not a type. Change log |
| [database-design.md](../database-design.md) | §9: `0062_fif_report.sql`. §14: the catalog row. Change log |
| [function-plan.md](../function-plan.md) | §6: the FIF Report beside the first-phase reports |
| [glossary.md](../glossary.md) | "FIF report" and "New Zealand time" become accepted. "Demo seed": four report grants. "Tax year": the FIF report prints its instants in New Zealand time |
| [fif-roadmap.md](../roadmaps/fif-roadmap.md) | §6 TR5: the spec line. §8: Q13 to Q17 written as rules here. §9 row 5: accepted. §3 "Reports" and "Tax documents": what exists now. §2 "Return and disclosure": the pages read do not say "by country" |
| [features/README.md](README.md), [future-plans.md](../future-plans.md) §1 step 6 | Status: TR5 accepted |

---

## 13. Suggested commits

Removed when the slice landed (2026-11-28). The commits are in git.

---

## 14. Sources

Read on 2026-11-28. S1 and S2 are the codes of [fif-roadmap.md](../roadmaps/fif-roadmap.md) §11; S3 is new here.

| # | Source | Used for | Read |
| --- | --- | --- | --- |
| S1 | Inland Revenue, *Guide to foreign investment funds*, IR461, April 2026 edition, the PDF linked from `ird.govt.nz/fif` (page last updated 31 March 2026) | "The rules apply to New Zealand tax residents who are not transitional residents" (p.4); individuals "do not need to apply the FIF rules if the total cost of their attributing interests is below $50,000 throughout the year" (p.5); foreign tax credits, their limit "to the lesser of the tax paid and New Zealand liability on the segment of foreign income", "No credit is available if the segment has a loss. Unused credits cannot be refunded or carried forward", imputation credits and RWT on some Australian dividends, RWT "used to offset tax payable with any excess refundable" (p.20); disclosure and "individuals must file an Overseas income summary – IR1261 form" (p.21); the other notes (p.22) | Yes: pages 4 to 6 and 20 to 23, from the PDF |
| S2 | Inland Revenue, fact sheet IS 24/10 FS 1, *Income tax – Share investments (whether the FIF rules apply)*, 18 December 2024 (`taxtechnical.ird.govt.nz`) | "If the FIF rules do not apply, the ordinary tax rules apply" (¶2); "The FIF rules apply only to a New Zealand tax resident" and the transitional resident (¶4, ¶5); an exempt interest stays under the ordinary rules (¶7); above the threshold at any time, and the choice to apply the rules below it (¶8); the IR3 return and the IR1261 overseas income summary (¶10); when no FIF disclosure is needed (¶11) | Yes: pages 1 to 4, from the PDF |
| S3 | New Zealand Government, "Daylight saving" (`govt.nz`, last updated 11 August 2026) and its page "Governing legislation" (last updated 15 September 2021) | "Daylight saving starts each year at 2am on the last Sunday in September, and ends at 3am on the first Sunday in April"; the dates of 2026 to 2029 (27 September 2026, 4 April 2027, 26 September 2027, 2 April 2028); "The current times have been fixed since 2007"; the New Zealand Daylight Time Order 2007 and the Time Act 1974 | Through a fetch tool: a restatement with short quotes |
| — | New Zealand Daylight Time Order 2007 | The rule of R13 in the Order's own words | Not read. tax-year-events §14 did not read it either |
| — | Inland Revenue, the IR1261 form and its guide | Whether overseas income is entered by country (roadmap §2) | Not read |

What follows: R13 rests on the Government's page, which gives the two clock times and says they have been fixed since 2007, and on the dates it lists, which the tests use. R22 groups by country because roadmap Q17 says so, not because a source read here asks for it. Every other tax statement of this slice is a sentence of the report about itself; the rules behind the figures are those of tax-year-events, tax-cost-threshold and fif-income, with their own sources.
