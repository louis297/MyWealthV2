---
title: Net worth
status: accepted
phase: 2
language: en
owner: ""
created: 2026-10-12
last_updated: 2026-10-12
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../architecture.md
  - ../glossary.md
  - ../adr/0011-ledger-cash-holdings-reversal.md
  - instruments.md
  - accounts.md
  - posting.md
  - holdings.md
  - buy-sell.md
  - split.md
---

# Net worth

Read model of current net worth on `webapi`. No new table. No write path. Aggregates existing cash balances and holding market values (mocked `IMarketData`) into per-currency arrays. Two reads: firm / adviser scope and one Customer. Adviser Portal Dashboard consumes these reads; other ledger pages stay out of this slice.

**Status is `accepted` and landed (2026-10-12, `af422c2`).** Implementation follows this file. No script. Reads reuse `accounts.read`. Current point-in-time only. BFF allow-list adds `/net-worth`.

---

## 1. Summary

A TenantAdmin or Adviser reads current net worth for the customers they can already see. SystemAdmin does the same on Scalar for a chosen tenant. Customer receives 403.

Cash is the existing computed `SUM` of posted cash legs. Holding market value is current quantity × current mocked price in the instrument quote currency. Results are grouped by currency. There is no FX fold into a single number. Closed accounts are excluded. Phase 2 has no Credit, so liabilities are empty.

Two endpoints: a firm / adviser list and a single-Customer read. Both return the same shape. The Customer read reuses the existing assignment and isolation rules.

---

## 2. Scope

**In**

- Application: `GetNetWorth` (firm / adviser scope) and `GetCustomerNetWorth`
- No Domain change. No new aggregate
- No script. No new column or table
- Policy: `accounts.read` (and `instruments.read` only if the price call requires it; prefer reusing `accounts.read` when the caller already holds it)
- `GET /net-worth` and `GET /users/customers/{id}/net-worth`
- Isolation tests + Development / TestAppHost `TestSeed` (existing demo accounts and holdings are enough)
- Current point-in-time only

**Out**

- Daily snapshots, historical series, “vs yesterday”
- FX fold to one number
- Live vendor prices or live FX
- Credit / Property as selectable types
- A hand-edited account-level `CurrentValue`
- Portal pages other than the Dashboard consumption contract
- New write policies
- Lots, realized gain, performance attribution
- Customer ledger-write

---

## 3. Stories

1. As an Adviser I open the Dashboard and see current net worth, per currency, for my assigned customers.
2. As a TenantAdmin I see the same for the whole firm.
3. As an Adviser I open one customer and see that customer’s current net worth and the same breakdowns.
4. As an Adviser I see cash, holding market value, and net by currency; Closed accounts do not appear.
5. As an Adviser I see breakdowns by customer, by account type, and top holdings by market value.
6. As a Customer I receive 403.
7. As an Adviser I cannot see another adviser’s customer (404).

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Missing / dead Bearer → 401, never 302. Policy fail → 403. TenantAdmin / Adviser: other-tenant, unassigned Customer, missing id → 404. SystemAdmin: missing id → 404; another tenant’s id is allowed. |
| R2 | Path and JSON `id` are `PublicId`. Internal ints never appear. |
| R3 | Reads are `GET /net-worth` (firm / adviser scope) and `GET /users/customers/{id}/net-worth` (one Customer). The Customer route sits under the existing people namespace. |
| R4 | TenantAdmin / Adviser omit `tenantId` (400 if present). SystemAdmin may pass `tenantId` on the firm read, same as other ledger lists. The Customer read uses the customer PublicId only. |
| R5 | Closed accounts are excluded. Open accounts only. |
| R6 | Results are arrays per currency. Do not fold with FX. Same-currency FX is not applied. |
| R7 | Cash = existing computed cash balance (SUM of posted cash legs). Holding market value = quantity × mocked price in quote currency. No price → market value 0, and the holding is still listed. Quantity ≤ 0 is omitted. |
| R8 | Phase 2 has no Credit. Liabilities array is empty. Do not invent a negative-cash rule. |
| R9 | Adviser sees only assigned Customers. TenantAdmin sees the firm. SystemAdmin sees the chosen tenant. |
| R10 | Current point-in-time only. No `asOf` parameter. No stored snapshot. |

---

## 5. Domain

No new types. Reads existing `Account`, cash-leg projection, `Holding`, and `Instrument` (via `IMarketData`).

---

## 6. Database

None. No script.

---

## 7. Application use cases

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Query | `GetNetWorth` | Per-currency totals + breakdowns for the caller’s scope | `accounts.read`; isolation; Open accounts only |
| Query | `GetCustomerNetWorth` | Same shape for one Customer | `accounts.read`; Customer exists and is in scope; else 404 |

---

## 8. API

| Method | Route | Policy | Success | Failure |
| --- | --- | --- | --- | --- |
| GET | `/net-worth` | `accounts.read` | 200 item | 401 / 403 / 404 |
| GET | `/users/customers/{id}/net-worth` | `accounts.read` | 200 item | 401 / 403 / 404 |

Optional query on the firm read: `customerId` (PublicId) to narrow to one Customer without changing route. Prefer the dedicated Customer route for the detail page.

### 8.1 Body (success)

```json
{
  "asOf": "2026-10-12T16:00:00Z",
  "currencies": [
    {
      "currency": "NZD",
      "assets": { "cash": 1000.00, "holdings": 5000.00, "total": 6000.00 },
      "liabilities": { "total": 0 },
      "net": 6000.00
    }
  ],
  "breakdowns": {
    "byCustomer": [
      { "customerId": "<publicId>", "name": "Demo Customer", "currencies": [ /* same currency shape */ ] }
    ],
    "byAccountType": [
      { "type": "Brokerage", "currencies": [ /* same currency shape */ ] }
    ],
    "topHoldings": [
      { "instrumentId": "<publicId>", "symbol": "DEMO", "quantity": 100, "marketValue": 5000.00, "currency": "NZD" }
    ]
  }
}
```

Field rules stay in this file. Update the resource catalog in [api-design.md](../api-design.md) on accept.

### 8.2 Errors

Ordinary validation stays in `errors`. Cross-tenant or unassigned Customer → 404. No new `code` unless api-design already names it.

---

## 9. UI

Adviser Portal Dashboard (later portal cut) consumes `GET /net-worth` for the home view and `GET /users/customers/{id}/net-worth` on the customer detail. Existing Customers, Accounts, and holdings reads stay available for the rest of the page.

Recommended first Dashboard widgets (from discussion):

- Per-currency totals (assets / liabilities / net)
- Breakdown by customer, by account type, top holdings
- Customer list with net worth (click through to customer detail)
- Recent activity (reuse existing transactions list)

Defer: charts, historical comparison, FX fold, Credit/Property, snapshots.

No portal page in this slice. Pages live in [portals/](../portals/README.md).

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Application.FunctionalTests | No Bearer → 401; wrong role → 403; happy path firm and customer; Closed accounts excluded; Adviser cannot see unassigned Customer → 404; SystemAdmin with `tenantId` |
| Infrastructure.IntegrationTests | Two-tenant fixture: other tenant’s customer id → 404 |

---

## 11. Locked in this spec

No new ADR.

| Item | Lock |
| --- | --- |
| Scripts / policy | None. Reuse `accounts.read`. Add `instruments.read` only if the price path requires a second policy. |
| Caller | TenantAdmin, Adviser, SystemAdmin (Scalar). Customer 403. |
| Scope | Current point-in-time. Per-currency arrays. No FX fold. Open accounts only. Two routes. Breakdowns as sketched. |
| No-price / zero-qty | No price → market value 0 (holding still listed in topHoldings). Quantity ≤ 0 omitted. |
| Breakdown sort | Currencies: ascending by code. `byCustomer`: by `name` then `customerId`. `byAccountType`: by enum name. `topHoldings`: `marketValue` descending, then `symbol`, then `instrumentId`. |

Still open, not this slice:

- Daily snapshots and historical series
- Live market data
- Portal page implementation (Dashboard and ledger pages)
- Top-N limits for breakdowns (sort is locked; limit is not)

---

## 12. Suggested commits

The repository should build after each commit. Agents split each line into red then green.

1. Failing functional tests for `GET /net-worth` and `GET /users/customers/{id}/net-worth` (401 / 403 / happy path / isolation).
2. Query handlers + endpoint wiring + mocked price call.
3. TestSeed verification that existing demo holdings appear in market value.
4. Accept change: status, companion edits to api-design / function-plan / glossary / features README.
