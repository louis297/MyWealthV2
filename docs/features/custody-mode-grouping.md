---
title: Grouping by custody mode
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-18
last_updated: 2026-11-19
related:
  - account-custody-mode.md
  - custody-modes.md
  - tenant-custody-modes.md
  - external-caller-numbers.md
  - net-worth.md
  - reports.md
  - accounts.md
  - leg-accounts.md
  - trade-fees.md
  - ../adr/0020-account-custody-mode.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../customer-portal/customer-portal.md
  - ../api-design.md
  - ../glossary.md
  - ../future-plans.md
---

# Grouping by custody mode

Read-side amendment to [net-worth.md](net-worth.md) and [reports.md](reports.md) on `webapi`, and to the Adviser Portal Dashboard and the Customer Portal. It is the fifth and last slice of the custody modes thread ([future-plans.md](../future-plans.md) §9), after [account-custody-mode.md](account-custody-mode.md), [custody-modes.md](custody-modes.md), [tenant-custody-modes.md](tenant-custody-modes.md) and [external-caller-numbers.md](external-caller-numbers.md).

Since slice 1 every account has a custody mode, and the account pages show it. The totals do not: net worth, the Adviser Balances Report and the Customer Transactions Report count a Custodial account and an External account in the same sums, and neither portal tells the two apart above the account row. The two modes are not the same kind of number ([ADR 0020](../adr/0020-account-custody-mode.md)). The platform holds a Custodial account's assets and is the authoritative record for them; that total is the firm's **funds under management**. An External account's numbers were entered from somebody else's statement and the platform has not checked them. This slice shows the two modes as two groups wherever a total is shown, and tells the Customer which figures the platform has not checked.

Read only. No write rule changes, no value is computed differently, no script, no new route, no new policy, no new query parameter. One new response field: `breakdowns.byCustodyMode` on the two net-worth reads.

**Status is `accepted` (2026-11-18) and landed** (2026-11-19) on branch `feature/custody-mode-grouping`: `breakdowns.byCustodyMode` in the net-worth reader, the Custody column and the subtotals per custody mode in the Adviser Balances Report, the Custody column and the note in the Customer Transactions Report, the two Dashboard figures in the Adviser Portal, and the Dashboard rows, grouped accounts list and account page note in the Customer Portal. The amendments in net-worth, reports, the Adviser Portal Dashboard and Customer Portal page contracts, api-design and the glossary are in force.

Implementation notes (2026-11-19), where the rules left a detail open:

- Adviser Balances PDF: the subtotal block headings are the enum names (`Custodial`, `External`) and "All accounts", each under the existing "Subtotals" heading. The row builder returns the mode blocks beside today's per-currency subtotals, which stay the "All accounts" block.
- Customer Transactions: the row builder passes its source records through unchanged, so the custody mode of each row is proved by a functional generation test (`GenerateCustomerTransactionsTests`), not by a row-builder unit test. A functional generation test also proves the Adviser Balances wiring.
- Adviser Portal: each figure is a table with Currency and Assets columns.
- Customer Portal: the Dashboard section is a table with Custody and Assets columns; the note sits under "Held elsewhere" in the Custody cell. On the accounts list the note is under the "Held elsewhere" heading, above the table.

---

## 1. Summary

| Where | Today | After this slice |
| --- | --- | --- |
| `GET /net-worth`, `GET /users/customers/{id}/net-worth` | Per-currency totals; breakdowns by customer, by account type, top holdings | Same, plus `breakdowns.byCustodyMode`: the same per-currency totals for the Custodial accounts and for the External accounts in scope |
| Adviser Balances Report (PDF) | One line per cash balance and holding; subtotals per currency | Each line shows the account's custody mode. Subtotals per currency for each custody mode, then for all accounts |
| Customer Transactions Report (PDF) | One row per transaction and account | Each row shows the account's custody mode. A note under the table when any row is on an External account |
| Adviser Portal Dashboard | Totals and the three breakdowns | Two more figures: **Funds under management** (Custodial) and **External assets** (External), per currency |
| Customer Portal | Dashboard totals and breakdowns; one accounts list | Dashboard shows "Held on platform" and "Held elsewhere" per currency. The accounts list is two groups. The "Held elsewhere" figures carry a note that the platform has not checked them |

---

## 2. Scope

**In**

- Application: `GetNetWorth` and `GetCustomerNetWorth` add `byCustodyMode`. The Adviser Balances generator puts the custody mode on every line and adds subtotals per custody mode. The Customer Transactions generator puts the custody mode on every row and adds the note
- HTTP: `breakdowns.byCustodyMode` on both net-worth reads. Additive; no field changes or goes away
- Adviser Portal: the two Dashboard figures
- Customer Portal: the Dashboard figures, the grouped accounts list, the note on the account page of an External account
- Tests listed in §10

**Out**

- Any write rule, any change to how a value is computed (cash `SUM`, market value from `IMarketData`, missing price → 0), the Closed-account rule, liabilities
- An FX fold of funds under management or of any other total
- A `custodyMode` query parameter on the net-worth reads or a custody parameter on either report
- A custody split inside `byCustomer`, `byAccountType` or `topHoldings`, and on the Adviser Portal customer page (`/customers/:id` shows no breakdowns, D5)
- A custody filter control on the Adviser Portal `/accounts` list (the API filter from slice 1 already exists)
- Daily snapshots or a history of funds under management
- The advisory fee and platform billing that will use Custodial accounts as their base ([future-plans.md](../future-plans.md) §1 step 4, §8)
- A mark on a single transaction that a number was entered by the caller (the FX detail source `Caller` and a fee detail without a schedule already record it, [external-caller-numbers.md](external-caller-numbers.md))
- Back Office, the two BFFs, the seeds

---

## 3. Stories

1. As a TenantAdmin I open the Dashboard and see the firm's funds under management, per currency, apart from the External assets my advisers record.
2. As an Adviser I see the same for my own Customers.
3. As an Adviser I run the Adviser Balances Report and see on each line whether the account is held on the platform, with subtotals for Custodial accounts, for External accounts, and for both.
4. As a Customer I open my Dashboard and see what the platform holds for me apart from what my adviser records for me from elsewhere.
5. As a Customer I open my accounts and see the accounts held on the platform first and the accounts held elsewhere second, with a note that the platform has not checked the figures of the second group.
6. As a Customer I download my Customer Transactions Report and see, on each row, whether the account is held on the platform or elsewhere.
7. As an Adviser I cannot see another adviser's Customer in any of these groups (404, as today).

---

## 4. Rules

### 4.1 Common

| ID | Rule |
| --- | --- |
| R1 | **Access unchanged.** net-worth R1–R4 and R9 (and its Customer Portal amendment), reports R1–R14, the Customer Portal backend amendments. Who may read what, the 401 / 403 / 404 rules, PublicId only, the `tenantId` rules. This slice adds no route, no policy and no parameter. |
| R2 | **Grouping key.** The group of a value is the `CustodyMode` of the Account it belongs to: a cash balance belongs to its Account, a holding to the Account that holds it, a report row to its account row (leg-accounts R20). The custody mode is immutable (account-custody-mode R2), so a report generated later puts an account in the same group. |
| R3 | **Same values.** Grouping only splits the existing values. Cash, holding market value, the missing-price rule, the Closed-account rule, the zero-balance rule of the Adviser Balances Report and per-currency totals with no FX fold are unchanged. An External holding is valued at the stored price like a Custodial one (external-caller-numbers R24). |

### 4.2 Net worth

| ID | Rule |
| --- | --- |
| R4 | **New breakdown.** `breakdowns.byCustodyMode` is an array of `{ custodyMode, currencies }`. `custodyMode` is the enum name (`Custodial` or `External`). `currencies` has the shape and rules of the top-level `currencies` (net-worth §8.1, R6–R8), computed over the Open accounts in scope with that custody mode. |
| R5 | **Entries.** One entry per custody mode that has at least one Open account in scope, as `byAccountType` has one entry per account type. An Open account with no cash legs and no holdings still makes an entry, with zero amounts in the account's currency (the existing `byAccountType` behaviour). No Open account of a mode → no entry for it. No Open account at all → an empty array. Sorted by enum name: `Custodial`, then `External`. |
| R6 | **Partition.** For every currency, the sums over the `byCustodyMode` entries of `assets.cash`, `assets.holdings`, `assets.total` and `net` equal the top-level values for that currency. |
| R7 | **Both routes.** `GET /net-worth` (firm, adviser or Customer self scope, with the existing optional `customerId`) and `GET /users/customers/{id}/net-worth` return the breakdown for their scope. A Customer gets their own. |
| R8 | **Funds under management.** The `Custodial` entry is the scope's funds under management. The `External` entry is its External assets. Both are per currency. |
| R9 | **Additive.** `currencies`, `byCustomer`, `byAccountType` and `topHoldings` do not change. A client that ignores the new field keeps working. |

Worked example. A Customer has an NZD Custodial account with cash `1,000.00` and holdings worth `5,000.00`, an NZD External account with cash `2,500.00`, and a USD External account with holdings worth `800.00`. Top-level: NZD cash `3,500.00`, holdings `5,000.00`, total `8,500.00`; USD holdings `800.00`. `byCustodyMode`: `Custodial` → NZD `1,000.00` / `5,000.00` / `6,000.00`; `External` → NZD `2,500.00` / `0` / `2,500.00` and USD `0` / `800.00` / `800.00`.

### 4.3 Adviser Balances Report

| ID | Rule |
| --- | --- |
| R10 | **Custody on each line.** Every cash line and every holding line carries its account's custody mode. The PDF table has a "Custody" column after "Type", filled on both kinds of line with the enum name (`Custodial` or `External`). |
| R11 | **Order unchanged.** Lines stay sorted by customer, then account, then the cash line, then holding lines (reports §9.2). The report is not split into one section per custody mode. |
| R12 | **Subtotals.** The subtotals section has a block per custody mode that has at least one line, `Custodial` first, then `External`; each block lists, per currency, the cash total and the market value total of that mode's lines. Then an "All accounts" block with today's per-currency totals. For every currency, the two mode blocks add up to the "All accounts" block. A report with no lines has no subtotals, as today. |
| R13 | **Unchanged.** Parameters (`adviserId`, `includeClosedAccounts`, an unknown field → 400), `asOf`, the header, the zero-cash and zero-quantity omissions, the missing-price rule. |

PDF subtotals, for the accounts of the worked example under one Adviser:

```text
Subtotals
Custodial
NZD cash 1,000.00 market value 5,000.00
External
NZD cash 2,500.00 market value 0.00
USD cash 0.00 market value 800.00
All accounts
NZD cash 3,500.00 market value 5,000.00
USD cash 0.00 market value 800.00
```

A currency appears in a block when the block has a line in it; a cash line counts for the account currency and a holding line for its market value currency, as today.

### 4.4 Customer Transactions Report

| ID | Rule |
| --- | --- |
| R14 | **Custody on each row.** Every row carries its account row's custody mode. The PDF table has a "Custody" column after "Account". The cell uses the Customer Portal labels: `Custodial` → "Held on platform", `External` → "Held elsewhere". |
| R15 | **Note.** When at least one row is on an External account, one line under the table: "Held elsewhere: recorded by your adviser from outside statements. The platform has not checked these figures." No External row → no note. An empty range has no rows and no note. |
| R16 | **Unchanged.** Parameters, the `bookedAt` filter, the order and the reversal placement, the per-account rows of leg-accounts R20, the Fee column (trade-fees R22), the closed-account filter. |

The note sentence of R15 is the one the Customer Portal shows (§9). Keep the two the same.

Policies: none added.

---

## 5. Domain

None. `Account.CustodyMode` already exists. No new type, invariant or event.

---

## 6. Database

None. No script. `Accounts.CustodyMode` (script `0031`) is read.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Query | `GetNetWorth`, `GetCustomerNetWorth` | The shared reader selects `Account.CustodyMode` with each account and adds `byCustodyMode` (R4–R9). The account, cash and holding queries are otherwise unchanged |
| Internal | Adviser Balances generator | Selects `Account.CustodyMode`. The account source, both line kinds and the subtotals carry the custody mode (R10–R12). The PDF model gains the Custody column and the subtotal blocks |
| Internal | Customer Transactions generator | Selects `Account.CustodyMode` for each account row (R14). The PDF model gains the Custody column and the note (R15) |
| Command / query | Every other | Unchanged |

The row selection and the subtotals of the Adviser Balances Report stay in its pure row builder, which has unit tests today; the custody mode is one more field on its input. The Customer Transactions row builder only passes the custody mode through on its input records.

---

## 8. API

No new routes. No new policies. No new parameters.

| Method | Route | Change |
| --- | --- | --- |
| GET | `/net-worth` | `breakdowns.byCustodyMode` added |
| GET | `/users/customers/{id}/net-worth` | `breakdowns.byCustodyMode` added |
| GET | `/reports/executions/{id}/download` | The PDF content changes (R10–R15). The route does not |

### 8.1 Body (success)

The worked example of §4.2, other fields as in net-worth §8.1:

```json
{
  "asOf": "2026-11-18T03:00:00Z",
  "currencies": [
    { "currency": "NZD", "assets": { "cash": 3500.00, "holdings": 5000.00, "total": 8500.00 }, "liabilities": { "total": 0 }, "net": 8500.00 },
    { "currency": "USD", "assets": { "cash": 0, "holdings": 800.00, "total": 800.00 }, "liabilities": { "total": 0 }, "net": 800.00 }
  ],
  "breakdowns": {
    "byCustomer": [ /* unchanged */ ],
    "byAccountType": [ /* unchanged */ ],
    "byCustodyMode": [
      {
        "custodyMode": "Custodial",
        "currencies": [
          { "currency": "NZD", "assets": { "cash": 1000.00, "holdings": 5000.00, "total": 6000.00 }, "liabilities": { "total": 0 }, "net": 6000.00 }
        ]
      },
      {
        "custodyMode": "External",
        "currencies": [
          { "currency": "NZD", "assets": { "cash": 2500.00, "holdings": 0, "total": 2500.00 }, "liabilities": { "total": 0 }, "net": 2500.00 },
          { "currency": "USD", "assets": { "cash": 0, "holdings": 800.00, "total": 800.00 }, "liabilities": { "total": 0 }, "net": 800.00 }
        ]
      }
    ],
    "topHoldings": [ /* unchanged */ ]
  }
}
```

### 8.2 Errors

None added. Cross-tenant or unassigned Customer → 404, as today.

Keep the resource catalog in [api-design.md](../api-design.md) pointing here for the new field.

---

## 9. UI

Page contracts: [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md) (D24, D25) and [customer-portal.md](../customer-portal/customer-portal.md) (C14–C17), amended and accepted together with this spec. Neither BFF changes: `/net-worth`, `/users` and `/accounts` are already proxied.

**Adviser Portal**

- `/dashboard`: below the per-currency totals and above "By customer", two figures side by side. "Funds under management" lists, per currency, `assets.total` of the `Custodial` entry of `byCustodyMode`. "External assets" lists the same for the `External` entry. A missing entry shows "None." The by-customer, by-account-type and top-holdings sections do not change.
- No other page changes. The account pages already show the custody mode (D16). The Reports page does not change; the PDFs do.

**Customer Portal**

- `/dashboard`: below the per-currency totals and above "By account type", a section "Held on platform and elsewhere" with two rows, "Held on platform" (`Custodial` entry) and "Held elsewhere" (`External` entry), each listing `assets.total` per currency. A missing entry is not shown; when both are missing the section shows "No balances." The "Held elsewhere" row has the note of R15 under it.
- `/accounts`: two groups, headed "Held on platform" and then "Held elsewhere". Each group is its own request, `GET /accounts?custodyMode=Custodial&page=1&pageSize=100` and `GET /accounts?custodyMode=External&page=1&pageSize=100`. A group with no accounts is not shown; when both are empty the page shows "No accounts." Under the "Held elsewhere" heading, the note of R15. The table columns stay as they are, Custody included (C13).
- `/accounts/:id` of an External account: the note of R15 under the "Custody" field. A Custodial account shows no note.
- `/reports`: no change. The downloaded PDF has the Custody column and the note (R14, R15).

### 9.1 Seeds

No change. `TestSeed` already has an External account ("Demo brokerage") and a Custodial account ("Demo custodial brokerage") under the demo Customer, so the Development Dashboard shows both figures. The demo tenant seed and the smoke seed open no accounts.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Application.UnitTests | Adviser Balances row builder: every cash and holding line carries its account's custody mode; mode subtotals per currency for the §4.2 accounts (Custodial NZD cash `1,000.00` / market value `5,000.00`; External NZD `2,500.00` / `0`, USD `0` / `800.00`); the "All accounts" subtotals equal today's; a mode with no lines has no block; a zero-cash External account with no holdings adds no line and no block. Adviser Balances PDF: "Custody" column header and cell text, the subtotal block headings and their order. Customer Transactions PDF: "Custody" column with "Held on platform" / "Held elsewhere"; the R15 note present with one External row and absent with none and on an empty range |
| Application.FunctionalTests — net worth | Firm read with one Custodial and one External account → two entries, `Custodial` first, values of each account; the §4.2 example → the §8.1 `byCustodyMode` and the partition of R6; External accounts only → one `External` entry; an Open account with no legs → an entry with zeros; a Closed Custodial account → no `Custodial` entry from it; Adviser scope → only assigned Customers' accounts in the entries; Customer route and Customer self read → own accounts only; no accounts → empty array. `TestSeedSmokeTests`: the demo tenant has both entries |
| Application.FunctionalTests — two tenants | Another tenant's Custodial account adds nothing to any entry; another tenant's Customer id on the Customer route → 404 (existing) |
| Application.FunctionalTests — reports | Adviser Balances generated for a Custodial and an External account: the PDF has the Custody column and the `Custodial`, `External` and "All accounts" blocks in that order. Customer Transactions generated for the same: each row shows "Held on platform" or "Held elsewhere" and the note is present |
| Adviser Portal | Dashboard renders "Funds under management" and "External assets" from `byCustodyMode`, per currency `assets.total`; a missing entry → "None."; the other sections are unchanged |
| Customer Portal | Dashboard renders the two rows and the note under "Held elsewhere"; a missing entry is not shown. Accounts list sends the two filtered requests and renders two groups in order with the note under "Held elsewhere"; an empty group is not shown; both empty → "No accounts.". Account page shows the note for an External account only |

Where the two-tenant tests live: net worth is a read with no new rule of scope, so the existing net-worth isolation fixture gains the one case above.

No assertion of an existing test is replaced. The existing Adviser Balances subtotal assertions hold for the "All accounts" block.

---

## 11. Locked in this spec

No new ADR. [ADR 0020](../adr/0020-account-custody-mode.md) said net worth still counts both modes and that showing them as two groups is a later read-model change; this is that change.

| Item | Lock |
| --- | --- |
| Grouping key | The custody mode of the value's Account |
| Values | Unchanged; grouping only partitions them. No FX fold |
| Net worth | `breakdowns.byCustodyMode`: entries for modes present, `Custodial` then `External`, same currency shape |
| Adviser Balances | Custody column with enum names; order unchanged; subtotal blocks per mode, then "All accounts" |
| Customer Transactions | Custody column with Customer labels; one note when any row is External |
| Adviser Portal | Dashboard figures "Funds under management" and "External assets" |
| Customer Portal | Dashboard rows, grouped accounts list, note on External figures |
| Routes, policies, parameters, scripts, problem codes | None added |

Confirmed when this spec was accepted (2026-11-18). The rules above are written with the first option of each:

1. **`byCustodyMode` lists only the modes present** (R5), like `byAccountType`. The alternative is always two entries, with empty `currencies` for a mode that has no Open account; the portals would not need a missing-entry case.
2. **The Adviser Balances Report keeps its customer order and adds subtotals per mode** (R11, R12). The alternative is two sections, Custodial lines then External lines, each sorted by customer; one Customer's accounts would then be split across the report.
3. **The Customer Transactions Report uses a Custody column with the Customer Portal labels** (R14), because the Customer reads it. The alternative is the enum names, as in the Adviser Balances Report, or the label after the account name in the Account column with no new column.
4. **The "not checked" note is shown to the Customer only**: on the Customer Portal and in the Customer Transactions Report (R15). The Adviser Portal and the Adviser Balances Report show no note; the Adviser entered those figures.
5. **The Customer Portal accounts list makes one request per group** with the existing `custodyMode` filter and `pageSize=100`. The alternative is today's single request (`pageSize=20`) grouped in the browser; a Customer with more than 20 accounts would then see an incomplete group.
6. **The Dashboard figures show `assets.total`** per currency. It equals `net` while there are no liabilities (net-worth R8); a later Credit account would count against `net` but not against funds under management.

Still open, not this slice:

- Funds under management per Customer (in `byCustomer` or on the Adviser Portal customer page)
- A custody filter on the Adviser Portal accounts list and on the net-worth reads
- An FX fold of funds under management into a reporting currency
- A history of funds under management
- The advisory fee and platform billing base

---

## 12. Suggested commits

Only after `accepted`. The repository should build after each commit. Agents split each behaviour into red then green.

1. Net worth `byCustodyMode` (R4–R9) + functional tests, including the two-tenant case and `TestSeedSmokeTests`.
2. Adviser Balances Report: custody on each line and subtotals per mode (R10–R13) + unit tests of the row builder and the PDF.
3. Customer Transactions Report: custody on each row and the note (R14–R16) + unit tests of the row builder and the PDF.
4. Adviser Portal: the two Dashboard figures (D24, D25) + frontend tests.
5. Customer Portal: Dashboard rows, grouped accounts list, account page note (C14–C17) + frontend tests.
6. Repo docs: mark this file landed.

Do not put a write rule, a new parameter, an FX fold or a custody split of the other breakdowns in these commits.
