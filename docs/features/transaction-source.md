---
title: Transaction source
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-19
last_updated: 2026-11-22
related:
  - ../custodial-roadmap.md
  - posting.md
  - portfolios.md
  - custody-modes.md
  - leg-accounts.md
  - ../adviser-portal/adviser-portal-dashboard.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../glossary.md
  - ../known-issues/custodial-account-gaps.md
  - deposit-matching.md
  - withdrawals.md
  - account-closure.md
  - interest-run.md
---

# Transaction source

Amendment to the ledger on `webapi`: [posting.md](posting.md), [portfolios.md](portfolios.md) and [custody-modes.md](custody-modes.md). It is step 1 of [custodial-roadmap.md](../custodial-roadmap.md) §13 and covers two roadmap items: **F1** (every Transaction records which process wrote it) and **F4** (on a Custodial account a person reverses only what a person entered, principle P6).

No new route, no new policy, no new `TransactionType`, no problem code. Script `0035_transaction_source.sql` adds two columns to `Transactions` and back-fills them.

**Status is `accepted` (2026-11-19), landed 2026-11-19.** The eight points of §11 were confirmed as recommended.

The §10 case "`POST /portfolios/{id}/rebalance` → every trade of an account has the `handle` of that account in the 202 body" is proved at the infrastructure layer. The functional host has no Worker, and the job runs there under `SystemUser`, so `RebalanceJobWorkerTests` runs `RebalanceJob` with the Worker's services and asserts that its trades have source `(Rebalance, handle)` and `CreatedBy` `system`. Before 2026-11-19 the Worker's `SystemUser` had no id, so the job could not post a trade (`Transactions.CreatedBy` is NOT NULL). It now writes the system name, as [database-design.md](../database-design.md) allows ("the actor's Identity id or a system name").

---

## 1. Summary

Every Transaction gets a **transaction source**: a source kind and an optional source id. This slice has three kinds:

| Kind | Written by | Source id |
| --- | --- | --- |
| `Manual` | A person, through a create route or reverse | none |
| `Seed` | The Development seeds (`TestSeed`, the Development smoke seed) | none |
| `Rebalance` | A portfolio rebalance (assign, single-account rebalance, the rebalance job) | the rebalance handle |

Later platform-side flows add their own kinds (bank feed, payment, order fill, …) in their own specs.

Three things change for callers:

1. The transaction item shows `source`.
2. Rebalance stops putting an internal `InstrumentId` into `reference`. Existing rebalance rows are cleaned by the script. This closes the internal-id leak on the wire (api-design §1: internal ints never appear).
3. On a Custodial account a hand reverse needs a **hand-reversible source** (`Manual` or `Rebalance`). A `Seed` Transaction there → 400. External accounts do not change.

The rule that distinguishes this slice: the source is set once, when the Transaction is built, by the code that builds it. A caller never sends it.

---

## 2. Scope

**In**

- Domain: `TransactionSourceKind` enum and `TransactionSource` value object (kind plus optional id). Every `Transaction` factory and `Reverse` take a source as a required parameter. `CustodyRules.AllowsHandReverse(custodyMode, sourceKind)`
- Application: the four create handlers and `ReverseTransaction` pass `Manual`; `AccountRebalancer` passes `Rebalance` with the handle and writes no `reference`; `ReverseTransaction` applies the reverse-by-source rule; the transaction reads map the source
- Infrastructure: EF mapping of the two columns; `TestSeed` and `DevelopmentSmokeSeeder` pass `Seed`
- Script `database/schema/0035_transaction_source.sql` (columns, back-fill, checks; forward only)
- HTTP: `source` on the transaction item (list and get). No new route, field in a body, filter or policy
- Adviser Portal: the source on the transaction detail page; Reverse hidden on Transactions whose source is not hand-reversible
- Tests listed in §10

**Out**

- Source kinds of later flows (`BankFeed`, `Payment`, `OrderFill`, `CorporateAction`, `InterestRun`, `InKindTransfer`, `Migration`, `Adjustment`). Each lands with its flow's spec and widens the check
- A source table, a rebalance run record, or an FK from the source id to anything. Source tables do not exist yet
- A `source` filter on `GET /transactions`. It comes with its first reader (for example bank reconciliation, roadmap B7)
- The source in the Customer Transactions Report and in the Customer Portal
- Any change to what a caller may enter (the manual entry matrix of custody-modes R5 is unchanged; only its reverse row changes)
- Any change to External accounts beyond the new item field
- A controlled adjustment (roadmap F3). Until it exists, a Transaction that cannot be reversed by hand cannot be corrected by hand
- Who may reverse rebalance trades once rebalance goes through orders (roadmap O3, D15)

---

## 3. Stories

1. As an Adviser I see on a transaction whether a person entered it or the platform wrote it, and for a rebalance trade, which rebalance run wrote it.
2. As a Customer or an Adviser I no longer see an internal integer in a rebalance trade's `reference`.
3. As an Adviser I reverse a Transaction I entered by hand on a Custodial account, as today.
4. As an Adviser I reverse a rebalance trade on a Custodial account, as today, when the trade lock allows it.
5. As an Adviser I cannot reverse a Transaction the platform wrote on a Custodial account (400). The Adviser Portal does not offer Reverse on it.
6. As a later flow I set my own source kind and source id on the Transactions I write, and the reverse rule keeps people from undoing them.

---

## 4. Rules

### 4.1 The source on every Transaction

| ID | Rule |
| --- | --- |
| R1 | Unchanged access rules: posting R1–R4a, R15, leg-accounts R6–R8. 401 / 403 / 404, PublicId only, `tenantId` rules, Customer read scope. |
| R2 | **Every Transaction has a source.** A source kind and an optional source id. Both are set when the Transaction is built and never change (posting R10). Kinds in this slice: `Manual` (0), `Seed` (1), `Rebalance` (2). A later spec that adds a kind adds the next value and widens the check in its own script. |
| R3 | **Source id.** `Manual` and `Seed` have no source id. `Rebalance` has one: the rebalance handle (R5). A later kind states in its spec whether it has an id; when it has one, it is the PublicId of the instruction, event or run that wrote the Transaction. The id has no foreign key. |
| R4 | **Hand-written Transactions are `Manual`.** `POST /transactions`, `POST /transactions/transfer`, `POST /transactions/fx` and `POST /transactions/{id}/reverse` write `Manual` for every caller, SystemAdmin included. The person is in the audit columns (`CreatedBy`), not in the source. |
| R5 | **Rebalance trades are `Rebalance`.** Every `Buy` / `Sell` that `AccountRebalancer` posts (assign, `POST /accounts/{id}/rebalance`, the rebalance job) has kind `Rebalance` and the rebalance handle as its source id. All trades of one run share the id. For an asynchronous rebalance it is the `handle` in the 202 body (portfolios R23); a synchronous call does not return its handle. |
| R6 | **Seeded Transactions are `Seed`.** Every Transaction that `TestSeed` and `DevelopmentSmokeSeeder` build has kind `Seed`. Both run in Development and in the TestAppHost only. The demo tenant seed writes no Transaction ([demo-tenant-seed.md](demo-tenant-seed.md) R9). |
| R7 | **A Reversal has its own source.** The source of a Reversal is the process that reversed, not a copy of the original's source. A hand reverse writes `Manual` with no id, whatever the original's source. A later flow that undoes its own write passes its own source. |
| R8 | **Not a request field.** No create or reverse body has a source. A `source` field in a body is ignored like any other unknown field. Factories have no default source, so every writer states one. |
| R9 | **Rebalance writes no `reference`.** A rebalance trade has `reference: null` and memo `Rebalance` (unchanged). The string `rb:{handle}:{B|S}:{InstrumentId}` is no longer stored. Rebalance idempotency (the key derived from account, handle, instrument and side, and its request hash) is unchanged; the hash input may keep that string, because it is never stored as a reference or returned. |

### 4.2 Reads

| ID | Rule |
| --- | --- |
| R10 | **Item.** The transaction item on `GET /transactions` and `GET /transactions/{id}` has `source: { "kind": "<enum name>", "id": <PublicId or null> }`. `kind` is the enum name (`Manual`, `Seed`, `Rebalance`). Every caller who can read the Transaction sees it, the Customer included. Nothing else on the item changes. |
| R11 | **No filter.** `GET /transactions` has no `source` query parameter in this slice. An unknown query parameter is ignored, as today. |

### 4.3 Reverse by source

| ID | Rule |
| --- | --- |
| R12 | **Hand-reversible sources.** `Manual` and `Rebalance` are hand-reversible. `Seed` is not. Every later kind is not, unless its spec says otherwise. `Rebalance` counts as hand-reversible because an Adviser started it (roadmap D15); this ends when rebalance goes through orders (roadmap O3). |
| R13 | **Custodial: only a hand-reversible source.** `POST /transactions/{id}/reverse` of a Transaction whose leg accounts are Custodial and whose source is not hand-reversible → 400, ordinary `errors` like the other reverse refusals (posting R11), with the message `This transaction was written by the platform and cannot be reversed by hand.` No Reversal and no idempotency record is written. Every leg account of a Transaction has the same custody mode (custody-modes R9), so the first leg account decides. |
| R14 | **Order.** R13 runs in the reverse handler after the idempotency replay check, the Closed-account and Disabled-Customer checks (posting R6, R7) and the Account locks. It runs before the portfolio trade lock (portfolios R18) and before the already-reversed check, the same place as the manual entry matrix on create (custody-modes R6). A Transaction the caller cannot see is still 404. An idempotent replay still returns the original response. |
| R15 | **External: unchanged.** On an External account every source can be reversed by hand, as today. The manual entry matrix (custody-modes R5) row "Reversal" becomes: External allowed; Custodial allowed for a hand-reversible source only. |
| R16 | **Where the rule lives.** `CustodyRules.AllowsHandReverse(custodyMode, sourceKind)` in Domain holds R12, R13 and R15. Application calls it once, in `ReverseTransaction`. `Transaction.Reverse` does not call it, so a later flow can reverse its own write. |

### 4.4 Existing data

| ID | Rule |
| --- | --- |
| R17 | **Back-fill.** Script `0035` gives every existing Transaction a source: a rebalance trade (§6) → `Rebalance` with the handle parsed from its reference, and its `reference` set to null; a Transaction whose reference is one of the seed references in §6 → `Seed`; every other row, Reversals included → `Manual`. This is the second one-time data write on posted rows after `0029`; posting R10 is otherwise unchanged. |
| R18 | **No other change.** No leg, amount, date, memo or audit column changes. `RowVersion` changes on every row the script updates. |

Policies: none added.

---

## 5. Domain

| Type | Kind | Change |
| --- | --- | --- |
| `TransactionSourceKind` | Enum (new) | `Manual = 0`, `Seed = 1`, `Rebalance = 2`. |
| `TransactionSource` | Value object (new) | `Kind` and `PublicId` (`Guid?`). Built by `TransactionSource.Manual`, `TransactionSource.Seed` and `TransactionSource.Rebalance(Guid handle)`. `Manual` and `Seed` have no id; `Rebalance` needs a non-empty id (`DomainException` otherwise). |
| `Transaction` | Aggregate (existing) | New `Source` (stored as two columns). Every factory (`Post`, `PostInternalTransfer`, `PostFxConversion`, `PostFxConversionWithAmounts`, `PostOpeningHoldings`, `PostBuy`, `PostSell`, `PostStockIn`, `PostStockOut`, `PostSplit`) and `Reverse` take a `TransactionSource` as a required parameter with no default. No factory reads the custody mode for it. |
| `CustodyRules` | Static rules (existing) | New `AllowsHandReverse(CustodyMode, TransactionSourceKind)`: `true` on External; on Custodial `true` for `Manual` and `Rebalance`, `false` otherwise. |

Invariants:

- Every Transaction has a source. It is set by the factory or by `Reverse` and never changes.
- A source of kind `Manual` or `Seed` has no id. A source of kind `Rebalance` has an id.
- A Reversal's source is the one passed to `Reverse`, not the original's.
- On a Custodial account a person reverses only a Transaction with a hand-reversible source. Held in Application through `CustodyRules.AllowsHandReverse`, like the manual entry matrix.

```text
TransactionSource.Manual            -> (Manual, null)
TransactionSource.Seed              -> (Seed, null)
TransactionSource.Rebalance(handle) -> (Rebalance, handle); handle not empty

CustodyRules.AllowsHandReverse(custodyMode, sourceKind) -> bool
  External:  true
  Custodial: sourceKind in { Manual, Rebalance }
```

Update [domain-model.md](../domain-model.md) §8.6 when this spec is accepted.

---

## 6. Database

Script: `database/schema/0035_transaction_source.sql`. Forward only. Do not add EF migrations.

| Table | Change | Index / FK |
| --- | --- | --- |
| `Transactions` | Add `SourceKind int NOT NULL` (temporary default 0, dropped at the end, as in `0031`) and `SourcePublicId uniqueidentifier NULL`. Back-fill (below). `CK_Transactions_SourceKind`: `SourceKind BETWEEN 0 AND 2`. `CK_Transactions_SourcePublicId`: `(SourceKind IN (0, 1) AND SourcePublicId IS NULL) OR (SourceKind = 2 AND SourcePublicId IS NOT NULL)` | No index, no FK |
| Every other table | none | none |

Back-fill, in this order:

1. **Pre-check** (read only, before any change, as in `0028`). A rebalance trade is a row whose `Reference` matches `rb:` + 32 hexadecimal digits + `:` + `B` or `S` + `:` + digits. If such a row has a `Type` other than `Buy` (7) or `Sell` (8), the script stops with an error and changes nothing.
2. **Rebalance.** Each rebalance trade gets `SourceKind = 2`, `SourcePublicId` = the 32 digits written as a hyphenated GUID (8-4-4-4-12), and `Reference = NULL`.
3. **Seed.** A row whose `Reference` is exactly one of these gets `SourceKind = 1`: `TestSeed-DemoCustodial-Funding`, `TestSeed-DemoCustodial-BuyVti`, `TestSeed-DemoCustodial-BuyAapl`, `TestSeed-DemoBrokerage-OpeningHoldings`, `TestSeed-DemoBrokerage-Funding`, `TestSeed-DemoBrokerage-Buy`, `TestSeed-DemoBrokerage-StockIn`, `TestSeed-DemoBrokerage-Split`, `TestSeed-DemoEverydayBank-OpeningTransfer`, `Smoke-Cash-TransferIn`. The seeds keep their references, which they use to stay idempotent.
4. **Manual.** Every other row keeps `SourceKind = 0` from the temporary default.
5. Drop the default, add the two checks. An insert that does not name `SourceKind` then fails.

The applicator runs the script without a surrounding transaction. Each `UPDATE` is atomic. A second run fails at the `ADD COLUMN`, as every other forward script does; the applicator runs each script once.

Existing data:

- Development databases: the seeded rows become `Seed`, rebalance trades become `Rebalance`, the rest `Manual`.
- Production: the seeds do not run there, so no row becomes `Seed`. A row becomes `Rebalance` only if a rebalance ran on production. Before the deploy, count the rows whose reference starts with `rb:`.
- Functional tests create their own rows and are not affected.

Update [database-design.md](../database-design.md) §9 and §11.4 when this spec is accepted.

---

## 7. Application use cases

| Kind | Name | Change |
| --- | --- | --- |
| Command | `CreateTransaction`, `CreateInternalTransfer`, `CreateFxConversion` | Pass `TransactionSource.Manual` to the factory (R4). No new check. |
| Command | `ReverseTransaction` | Passes `TransactionSource.Manual` to `Reverse` (R7). Applies R13 in the order of R14. |
| Command | `AssignPortfolio`, `RebalanceAccount`, the rebalance job (`AccountRebalancer`) | Pass `TransactionSource.Rebalance(handle)` and `reference: null` (R5, R9). Idempotency unchanged. |
| Query | `GetTransactions`, `GetTransactionById` | Map `source` onto the item (R10). |
| Query | Reports, net worth, holdings, accounts | Unchanged. |
| Seed | `TestSeed`, `DevelopmentSmokeSeeder` | Pass `TransactionSource.Seed` (R6). References unchanged. |

A refused reverse stores no idempotency record: the new check throws inside the command's database transaction, before any insert.

---

## 8. API

No new routes. No new policies. No new body or query field.

| Method | Route | Change |
| --- | --- | --- |
| GET | `/transactions`, `/transactions/{id}` | The item has `source` |
| POST | `/transactions/{id}/reverse` | 400 for a Custodial Transaction whose source is not hand-reversible |
| POST | `/accounts/{id}/portfolio`, `/accounts/{id}/rebalance`, `/portfolios/{id}/rebalance`, `PUT /portfolios/{id}` | The trades they post have source `Rebalance` and `reference: null` |

### 8.1 Bodies

Item of a rebalance trade (other fields as in [leg-accounts.md](leg-accounts.md) §8.1 and [trade-fees.md](trade-fees.md) §8.1):

```json
{
  "id": "<transaction publicId>",
  "tenantId": "<tenant publicId>",
  "type": "Buy",
  "source": { "kind": "Rebalance", "id": "<rebalance handle>" },
  "cash": [ "..." ],
  "security": [ "..." ],
  "bookedAt": "2026-11-19T02:00:00Z",
  "memo": "Rebalance",
  "reference": null,
  "originalTransactionId": null,
  "reversedByTransactionId": null,
  "rowVersion": "<base64>",
  "fx": null
}
```

A hand-entered Transaction and every hand Reversal: `"source": { "kind": "Manual", "id": null }`.

### 8.2 Errors

| Case | Response |
| --- | --- |
| Reverse of a Custodial Transaction whose source is not hand-reversible | 400, ordinary `errors` (R13) |

Cross-tenant or wrong-collection id → **404**, not 403, before R13 is evaluated.

This is a change for callers that read `reference` on rebalance trades: it is now `null`, on old rows too. No caller in the repo reads it.

Keep the resource catalog in [api-design.md](../api-design.md) §7.7 pointing here for these rules.

---

## 9. UI

Page contract: [adviser-portal-dashboard.md](../adviser-portal/adviser-portal-dashboard.md), amended in the same change that accepts this spec (D26, D27). Neither BFF changes.

**Adviser Portal**

- `/transactions/:id`: a "Source" line under the type: `Entered by hand` for `Manual`, `Rebalance` for `Rebalance`, `Test seed` for `Seed`. No source id is shown.
- `/transactions`, `/transactions/:id` and the recent activity on `/accounts/:id`: Reverse is offered only when, in addition to today's conditions (not a Reversal, not reversed), `source.kind` is `Manual` or `Rebalance`. The portal does not read the custody mode for this. The only difference from the server rule: a `Seed` Transaction on an External account (Development only) shows no Reverse although the server would accept it.

**Customer Portal**

None. The item gains a field the pages do not read.

### 9.1 Seeds

`TestSeed` and `DevelopmentSmokeSeeder` keep their data and references and set `Seed` on every Transaction they build. The three seeded Transactions on "Demo custodial brokerage" (custody-modes §9.1) can no longer be reversed by hand. They were already blocked by the trade lock while `Demo Growth` is assigned there.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `TransactionSource`: `Manual` and `Seed` have no id; `Rebalance(Guid.Empty)` throws; `Rebalance(handle)` keeps the handle. Each factory stores the source it was given. `Reverse` stores the source it was given, not the original's (a `Rebalance` original reversed with `Manual` → Reversal `Manual`). `CustodyRules.AllowsHandReverse`: one case per `(custody mode, source kind)` pair, equal to R12, R13 and R15 |
| Application.FunctionalTests — source on writes | `POST /transactions` (a cash type and a `Buy`), `/transactions/transfer`, `/transactions/fx` and reverse → item `source` `{ Manual, null }`, for an Adviser, a TenantAdmin and a SystemAdmin. A body with `"source": { "kind": "Seed" }` → 201 and `Manual`. Assign and `POST /accounts/{id}/rebalance` → every trade has `{ Rebalance, <id> }`, one id per run, and `reference: null`. `POST /portfolios/{id}/rebalance` → every trade of an account has the `handle` of that account in the 202 body. The Customer reads `source` on their own Transactions. Cross-tenant: tenant B's Transaction id → 404 |
| Application.FunctionalTests — reverse by source | On a Custodial account: reverse of a `Manual` `TransferIn` → 201; reverse of a `Rebalance` trade after unassign → 201; reverse of a `Seed` Transaction (built by the test with the factory and saved directly, as in `CashAmountDecimalPlacesTests`) → 400 with the R13 message, no Reversal, no idempotency record, and a retry with the same key → 400 again. On an External account: reverse of a `Seed` Transaction → 201. Closed account or Disabled Customer with a `Seed` Transaction → the existing 400 (R14 order). A `Seed` Transaction on an assigned Custodial account → the R13 message, not the trade lock. Another tenant's Transaction → 404, not 400 |
| Infrastructure.IntegrationTests | Script `0035` on a scratch database (as for `0024`, `0031`, `0032`): a `Buy` and a `Sell` with an `rb:` reference become `Rebalance` with the handle as GUID and a null reference; a row with each listed seed reference becomes `Seed` with its reference kept; a hand row, a Reversal, and a row with reference `TestSeed-Other` stay `Manual`; leg counts and amounts are unchanged; an `rb:` row of type `TransferIn` stops the script with nothing changed; after the script an insert without `SourceKind` fails, and `(Rebalance, NULL)` and `(Manual, <guid>)` violate the check. `TestSeed`: every seeded Transaction is `Seed`; a second run adds nothing |
| Adviser Portal | Detail page shows the Source line for each of the three kinds. Reverse is absent on a `Seed` row and present on a `Manual` and a `Rebalance` row, on the list, the detail and the account page; a 400 from reverse shows the sentence |

Fan-out in the existing tests:

- Every Domain unit test that calls a factory or `Reverse` passes a source (about twelve files in `tests/Domain.UnitTests/Entities`). Use a test helper that returns `TransactionSource.Manual`. Mechanical; in the same commit as the parameter (§12 commit 1).
- Functional and integration tests that build Transactions directly (`FeeHttp`, `CashAmountDecimalPlacesTests`, `GetReportExecutionsTests`, `TenantQueryFilterTests`, `EnteredTradeFeesSchemaTests`) pass a source the same way.
- `TestSeedSmokeTests` and the seed integration tests gain the `Seed` assertion.
- No existing test asserts the `rb:` reference.

---

## 11. Locked in this spec

No new ADR. The roadmap principles P5 and P6 and decision D15 gave the direction; this spec makes them rules.

| Item | Lock |
| --- | --- |
| Shape | Source kind (`int`) plus optional source PublicId on `Transactions`. No FK, no source table |
| Kinds in this slice | `Manual`, `Seed`, `Rebalance`. Later kinds come with their flows |
| Rebalance source id | The rebalance handle |
| Rebalance `reference` | Not written; existing values cleared by script `0035` |
| Reversal source | The reversing process; a hand reverse is `Manual` |
| Reverse by source | Custodial only. Hand-reversible: `Manual`, `Rebalance` |
| Item | `source: { kind, id }` on list and get |
| Script | `0035_transaction_source.sql`, forward only |
| Routes, policies, problem codes, filters | None added |

### Confirmed when this spec was accepted (2026-11-19)

Each point was confirmed as recommended and is written into the rules above as stated here. The alternatives are kept for the record.

1. **Scope of the reverse rule** (R13, R15). Decided: Custodial accounts only, as the roadmap F4 says, so External accounts do not change (roadmap target 5). Alternative: the rule applies on every account. In production this changes nothing for External accounts (no `Seed` rows there, and no flow writes to them), and the portal could follow the server exactly without the custody mode. The cost is that the rule no longer reads "External accounts are unchanged".
2. **`Seed` is not hand-reversible** (R12). Decided: not hand-reversible, because a seeded row on a Custodial account stands in for a platform write, and it gives F4 a case that can be tested now. Alternative: treat `Seed` like `Manual`. Then F4 has no observable effect until the first flow lands, and the demo Custodial rows stay reversible.
3. **Clear `reference` on existing rebalance trades** (R17). Decided: clear it, which closes the leak for old rows too; `0029` already changed posted rows once. Alternatives: keep old references (the leak stays on old rows), or rewrite the integer as the instrument's PublicId (keeps the B/S and instrument text, which the legs already hold).
4. **The rebalance handle as the source id** (R5). Decided: yes. It groups the trades of one run, and the async 202 body already shows it. It is not a resource: no route returns a rebalance run, and a synchronous call never shows its handle. Alternative: no id for `Rebalance` until a rebalance run record exists.
5. **Wire shape** (R10). Decided: one nested object `source: { kind, id }`, like `fx`. Alternative: two flat fields `source` and `sourceId`, which a later list filter (`?source=`) would match more directly.
6. **Seed back-fill by exact reference** (§6 step 3). Decided: the exact list, because a person may type any reference and a prefix match would mark a hand row as `Seed` (and make it unreversible on a Custodial account). The roadmap said "`TestSeed-` references". The exact list also covers `Smoke-Cash-TransferIn`, which has no `TestSeed-` prefix.
7. **Where the source is shown** (§2 Out, §9). Decided: only the item and the Adviser Portal detail page. Not in the Customer Transactions Report (roadmap F1 said "may"), not in the Customer Portal, no list filter. Each can be added by the spec that first needs it.
8. **Kind values reserved now** (R2). Decided: only the three kinds this slice writes; each later flow adds its kind. Alternative: reserve the whole roadmap list now. That locks names before their specs are discussed.

Still open, not this slice:

- A correction for a Transaction that cannot be reversed by hand: the controlled adjustment (roadmap F3, step 3)
- Rebalance through orders and what happens then to the hand reverse of `Rebalance` trades (roadmap O3, D15)
- A `source` list filter and an index on `(TenantId, SourceKind, SourcePublicId)`, when a flow or a reconciliation reads by source

---

## 12. Suggested commits

The repository should build after each commit. Agents split each behaviour into red then green.

1. Store the source (R2–R8, R17, R18): `TransactionSourceKind`, `TransactionSource`, the parameter on every factory and `Reverse`, EF mapping, script `0035`, every writer passes its source (handlers `Manual`, seeds `Seed`, rebalance `Rebalance` with the handle). Domain unit tests, the script and seed integration tests. The test fan-out of §10 lands here. The script and the mapping must land together: after the script an insert without `SourceKind` fails.
2. Rebalance writes no `reference` (R9) + functional tests.
3. `source` on the transaction item (R10, R11) + functional tests.
4. Reverse by source (R12–R16): `CustodyRules.AllowsHandReverse` with unit tests; `ReverseTransaction`; functional tests.
5. Adviser Portal: Source line and Reverse by source (D26, D27) + frontend tests.
6. Repo docs: mark this file landed; update roadmap §13 step 1.

Do not put an adjustment, a new flow's source kind, or a list filter in these commits.

---

## Amendment 2026-11-20 (bank feed and deposit matching) — accepted

Status of this amendment: `accepted` (2026-11-20), together with [deposit-matching.md](deposit-matching.md).

- R2: new kind `BankFeed` (3), written by the deposit flow. Script `0041` widens `CK_Transactions_SourceKind` to 0–3 and requires a `SourcePublicId` for kinds 2 and 3.
- R3: the source id of `BankFeed` is the PublicId of the bank statement line the deposit came from. A deposit and the Reversal of its undo share it; a line matched again after an undo gives a second deposit with the same id.
- R10: `kind` may be `BankFeed`.
- R12: `BankFeed` is not hand-reversible. On a Custodial account a hand reverse of a deposit → 400 (R13). The deposit flow undoes its own write by an undo match (deposit-matching R30, R33).
- §5: `TransactionSource.BankFeed(Guid statementLinePublicId)`; an empty id → `DomainException`.

---

## Amendment 2026-11-20 (nominated bank accounts and withdrawals) — accepted

Status of this amendment: `accepted` (2026-11-20), together with [withdrawals.md](withdrawals.md).

- R2: new kind `Payment` (4), written by the withdrawal flow. Script `0043` widens `CK_Transactions_SourceKind` to 0–4 and requires a `SourcePublicId` for kinds 2, 3 and 4.
- R3: the source id of `Payment` is the PublicId of the withdrawal. A payout and the Reversal of a returned payout share it.
- R10: `kind` may be `Payment`.
- R12: `Payment` is not hand-reversible. On a Custodial account a hand reverse of a payout or its Reversal → 400 (R13). The withdrawal flow undoes its own write when the bank returns the payout.
- §5: `TransactionSource.Payment(Guid withdrawalPublicId)`; an empty id → `DomainException`.

---

## Amendment 2026-11-22 (account closure) — accepted

Status of this amendment: `accepted` (2026-11-22), together with [account-closure.md](account-closure.md).

- R2, R4: new source kind `AccountClosure = 5`, with the account closure's PublicId as source id (required). It marks the remainder write-off (a `CloseOut`, account-closure R18). `TransactionSource.AccountClosure(Guid closurePublicId)`; an empty id → `DomainException`.
- R12, R13: `AccountClosure` is not hand-reversible. `POST /transactions/{id}/reverse` of a write-off → 400 with the R13 message.
- §6: script `0046_account_closures.sql` widens `CK_Transactions_SourceKind` to 0–5 and requires a `SourcePublicId` for kinds 2–5.
- R18 (item): `source.kind` may be `AccountClosure`, with `id` the closure's PublicId.

---

## Amendment 2026-11-22 (interest run) — accepted

Status of this amendment: `accepted` (2026-11-22), together with [interest-run.md](interest-run.md). Rules R23 and R27 live there; this section records what changes here.

- R2, R4: new source kind `InterestRun = 6`, with the interest run's PublicId as source id (required). `TransactionSource.InterestRun(Guid runPublicId)` refuses an empty id.
- R12, R13: `InterestRun` is not hand-reversible. `POST /transactions/{id}/reverse` of an interest posting → 400 by R13.
- §6: script `0048_interest.sql` widens `CK_Transactions_SourceKind` to 0–6 and requires a `SourcePublicId` for kinds 2–6.
- R18 (item): `source.kind` may be `InterestRun`, with `id` the run's PublicId.

---

## Amendment 2026-11-22 (corporate actions) — accepted

Status of this amendment: `accepted` (2026-11-22), together with [corporate-actions.md](corporate-actions.md). Rules R26, R27 and R32 live there; this section records what changes here.

- R2, R4: new source kind `CorporateAction = 7`, with the corporate action's PublicId as source id (required). `TransactionSource.CorporateAction(Guid corporateActionPublicId)` refuses an empty id.
- R12, R13: `CorporateAction` is not hand-reversible. `POST /transactions/{id}/reverse` of an entitlement posting → 400 by R13.
- §6: script `0050_corporate_actions.sql` widens `CK_Transactions_SourceKind` to 0–7 and requires a `SourcePublicId` for kinds 2–7.
- R18 (item): `source.kind` may be `CorporateAction`, with `id` the corporate action's PublicId.
