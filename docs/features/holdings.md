---
title: Holdings
status: draft
phase: 2
language: en
owner: ""
created: 2026-09-28
last_updated: 2026-09-28
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../api-design.md
  - ../architecture.md
  - ../glossary.md
  - ../adr/0004-money-as-decimal-with-currency.md
  - ../adr/0010-instrument-catalog.md
  - ../adr/0011-ledger-cash-holdings-reversal.md
  - ../adr/0015-idempotency-keys.md
  - instruments.md
  - accounts.md
  - posting.md
---

# Holdings

Draft only. Not a build contract. Do not create tables, routes, or policies from this file until status is `accepted`.

Next Phase-2 design slice after posting: **security legs**, a **holdings read**, and **Opening of holdings**. Public write stays the existing Transaction aggregate (`POST /transactions`). Adviser Portal Accounts / activity / holdings pages wait for a later portal cut. Net worth and Dashboard wait for their own slice.

This file records inherited locks, a lean (not locked) shape so the open list is concrete, and the questions that still need a decision. If a sentence here disagrees with an accepted spec, the accepted spec wins.

---

## 1. Summary

A TenantAdmin or Adviser books security activity on an **Open** Brokerage or Other account of an assigned / same-tenant Customer. SystemAdmin does the same on Scalar for a chosen tenant. Customer receives 403. Bank and Cash refuse every security leg.

Quantity of one instrument in one account is the `SUM` of signed security-leg quantities — same idea as cash `SUM`. Day-to-day quantity and cost are not hand-edited. Opening of holdings is the only command that may set quantity and cost directly. Buy / sell / scrip (if this increment accepts them) go through the same header + legs write as cash.

No portal page in this increment.

---

## 2. Scope

**In (proposed for this increment — not locked; see Q1)**

- Domain: security leg on the existing `Transaction` aggregate; holdings read (computed or projected)
- Application: extend `CreateTransaction` / `ReverseTransaction` for security types; list / get holdings
- Schema script after `0013_transactions.sql` (likely `0014_…`)
- Isolation tests + Development / TestAppHost `TestSeed` rows
- Amend [posting.md](posting.md) type table and item JSON when accepted
- Amend [accounts.md](accounts.md) close rule if holdings must be flat before close

**Out (this increment, unless a question below pulls one in)**

- Adviser Portal Accounts / holdings / activity pages
- Net-worth read model, daily snapshots, Dashboard, live `IMarketData` HTTP
- Cross-account transfer of cash or stock
- Live FX / treating a cross pair as rate 1
- Property / Credit selectable; Credit negative-cash exception
- Lot / tax-lot tracking, wash sales, realized-gain reports
- Fees as their own leg, corporate actions beyond a simple ratio split / bonus / scrip
- Short positions (negative quantity) unless Q8 says otherwise
- Customer ledger-write; Customer Portal
- SystemAdmin ledger screens on `adviser-portal`
- Empty tables “for later” without an accepted spec
- Seeding from the schema script or in Production
- A second POST collection beside `/transactions`
- `UPDATE` / `DELETE` of a posted transaction
- Hand-edited holding quantity or cost

---

## 3. Stories (draft)

1. As an Adviser I record an Opening of holdings on a Customer’s Brokerage account so quantity and cost exist without inventing a buy.
2. As an Adviser I cannot book a security leg on a Bank or Cash account (400).
3. As an Adviser I cannot pick a disabled instrument for a **new** security leg (400). Existing legs that already point at it stay.
4. As an Adviser I list holdings for an account and see quantity + cost per instrument (not market value — that is net worth).
5. As an Adviser I reverse a posted security transaction; the original rows stay; quantity / cost move by the opposite legs.
6. As an Adviser I cannot close an account while holdings remain (if Q11 locks that).
7. As a Customer I receive 403 on every new holdings / security verb.
8. As a later increment I add Buy / Sell / scrip on the same header if this increment deferred them.

---

## 4. Inherited locks (do not reopen here)

From [posting.md](posting.md), [accounts.md](accounts.md), [instruments.md](instruments.md), ADR 0011, function-plan §5:

| Item | Lock |
| --- | --- |
| Write model | One Transaction header + legs in one command. Write = posted. No capture table. No `Created` + event auto-approve. |
| Cash | `TransactionCashLeg`. Balance = `SUM` of signed cash amounts. No `Account.Balance`. |
| Public route | `/transactions`, policies `transactions.read` / `transactions.create`. Ledger routes stay off `/users`. |
| Sign | Glossary **Signed book amount**: `+` increases that container’s book. |
| Reversal | New `Type = Reversal` pointing at the original. Original not updated. One reversal per original this phase. Reverse of a reversal out. Closed account rejects create and reverse. |
| Cash Opening | `amount > 0`. At most one cash Opening per account. Allowed only while the account has **no** other transaction. New account starts at cash `SUM = 0` with no auto row. |
| Close (cash) | Reject while cash `SUM ≠ 0`. `CloseOut` zeroes cash to an off-books counterparty. `Account.Close()` does not write books. |
| Types reserved | Buy / Sell / Dividend / split / scrip / bonus named, not accepted on HTTP yet. |
| Account types | Bank / Cash forbid holdings. Brokerage / Other may hold. Type and cash currency immutable. |
| Instrument | Holding stores `InstrumentId` only. `QuoteCurrency` immutable. Cost currency = quote currency. Disabled instrument: no **new** holding; existing ids stay. |
| FX | Same currency = 1. Cross-currency is never treated as 1. Ports mocked. No live vendor. |
| Customer | No ledger-write policy. |
| SystemAdmin | `tenantId` on list/create. Scalar now. |
| Portal | No Accounts / activity / holdings page in this increment. |
| Idempotency | `Idempotency-Key` on posting writes (ADR 0015 proposed). Reuse, do not invent a second scheme. |
| Concurrency | Server locks the Account row on post. Client does not send Account `rowVersion` on create / reverse. |

---

## 5. Lean (discussion default — **not** a spec lock)

Use these as the starting answer for each question in §8. Accept, reject, or replace in discussion. Do not implement from this section.

1. **Packaging.** New Feature Spec `holdings.md` owns holdings read + security-leg storage + Opening-of-quantity rules. It **amends** posting (allowed types, item JSON, reverse of mixed legs). It does not add `/journals` or a second write resource.
2. **Legs.** `TransactionSecurityLeg` (name open — Q3) on the same header. Buy / sell = cash leg + security leg. Scrip / split / bonus = security only (cash omitted or 0, **total cost unchanged**). Opening of holdings = security leg with explicit cost; cash omitted unless the same header is also the account’s cash Opening (Q6).
3. **Quantity.** Signed decimal on the security leg. `+` increases that instrument’s quantity in that account. Position quantity = `SUM` of posted security-leg quantities for `(AccountId, InstrumentId)`. No hand edit.
4. **Holdings row.** Either no table (read = `SUM` + cost rule) or a projection upserted in the **same command** as the legs. Not a second write path. Unique `(AccountId, InstrumentId)`. No lots in this increment.
5. **Cost.** Average cost in instrument `QuoteCurrency`. Opening and buys add cost; sells release average cost × quantity sold; scrip / split / bonus do not change total cost. Realized P/L is not a product read in this increment (may be implied by the cost release).
6. **Types this increment.** At minimum: Opening-of-holdings + Reversal of those rows + holdings read + Bank/Cash guard + disabled-instrument guard + (likely) close-requires-flat-holdings. Buy / Sell / Dividend / split in the **same** increment only if Q1 says the first capability batch includes them. Function-plan already grouped Opening of holdings with the securities ledger.
7. **No shorts.** After insert, quantity `SUM` for that `(Account, Instrument)` must be `≥ 0`. Phase 2 has no Credit accounts and no short book.
8. **HTTP.** Writes stay `POST /transactions` with a `security` object (and `cash` when both exist). Reads: `GET /accounts/{id}/holdings` and/or `GET /holdings?accountId=&customerId=`. Item JSON stops flattening only `amount` once a security leg exists (posting §8.1 already warned this).
9. **Policies.** Writes reuse `transactions.create`. Holdings list uses `accounts.read` or `transactions.read` (Q14) — do not invent `holdings.create`.
10. **Market value.** Out. List returns quantity + cost (+ currency). Price stays on `IMarketData` for the net-worth slice.

---

## 6. Domain (proposed names only)

| Type | Kind | Notes |
| --- | --- | --- |
| `Transaction` | Aggregate (existing) | Grows optional security legs. Still one write. |
| `TransactionSecurityLeg` | Entity (proposed) | Hits one instrument book inside one account. |
| `Holding` | Read model or projection | Per `(Account, Instrument)`: quantity + cost. Not a hand-edited aggregate. |
| `TransactionType` | Enum (existing) | May add names if cash `Opening` cannot carry holdings (Q6). |

Invariants to lock when accepted:

- Tenant of the header, account, and instrument match.
- Account type allows holdings.
- Instrument is enabled for a **new** security direction (reversal of an existing id still allowed).
- Security-leg cost currency = instrument quote currency.
- Quantity `SUM ≥ 0` after insert (if Q8).
- Posted rows immutable; correction = reversal of **all** legs of the original.

---

## 7. Database / HTTP (not locked)

Do not create these objects from a draft.

Likely script: `database/schema/0014_….sql` (name follows the accepted scope).

Candidates to decide in Q3 / Q4:

- `TransactionSecurityLegs` — one row per instrument on a header? unique `(TransactionId, InstrumentId)`?
- `Holdings` — projection `(AccountId, InstrumentId)` with Quantity + CostAmount + CostCurrency, or omit the table
- Audit + `RowVersion` on new rows, same pair as cash legs
- No quantity / cost columns on `Accounts`
- No free-text symbol on a holding

HTTP candidates: §5 lean item 8. Create still returns `201 { "id" }` (transaction PublicId). Holdings rows may use instrument id + account id as the natural key and need no PublicId — that is Q15.

---

## 8. Open questions

Answer these before moving the spec to `review`. A short “accept lean / reject lean” is enough; do not invent extra product surface to close a question.

### A. Slice packaging

**Q1. What ships in this increment?**

Options:

- **A1 (lean, thin):** security-leg table + Opening of holdings + holdings read + reverse of those + type/instrument guards + close-flat-holdings. Defer Buy / Sell / Dividend / split / scrip HTTP.
- **A2 (function-plan batch):** A1 + Buy / Sell + security-only scrip / split / bonus.
- **A3:** A2 + cash Dividend on an instrument (still no DRIP).

Why it matters: Buy forces cash+security in one command, cost-release on sell, and the item JSON break. Opening-only is smaller and still proves the leg model. Function-plan §5 grouped “holdings / securities ledger / Opening of holdings” as one step, not three.

**Q2. Does this file stay the owner, or does posting.md absorb security legs?**

Posting already says holdings may split out when that file gets large. Lean: this file owns holdings + Opening-of-quantity; posting keeps the header / cash / reverse machine and is amended. Rejecting lean means one growing posting spec.

### B. Storage and cost

**Q3. Physical security-leg row?**

Lean: yes, `TransactionSecurityLegs`, not extra columns on `Transactions` or on `TransactionCashLegs`. One instrument per security-leg row.

Open inside Q3:

- May one header carry **several** security legs (one Opening booking many instruments)?
- Unique `(TransactionId)` (one instrument per ticket) vs `(TransactionId, InstrumentId)`?

**Q4. Is there a `Holdings` table?**

Cash chose “no projection, read = `SUM`”. Cost is not a plain `SUM` of signed cost amounts once sells release **average** cost rather than the ticket’s raw cash.

Options:

- **H1:** No table. Read recomputes quantity `SUM` and walks legs for average cost. Simple, matches cash, can get slow and easy to get wrong on sell.
- **H2 (lean):** Projection table, upserted in the same Account-locked command as the legs. Read is cheap. Still not a hand-edit path. Close / tests assert projection == `SUM` of quantities.
- **H3:** Lots table now. Reject for this increment unless we explicitly want tax lots.

**Q5. Cost method?**

Lean: **average cost** in quote currency. Opening / buy increase total cost; sell decreases total cost by average × qty sold; scrip / split / bonus: quantity changes, total cost unchanged.

Alternatives: explicit cost on every sell body; FIFO lots. FIFO is “once, not twice” only if we are sure Phase 3 tax needs lots **and** average would be deleted. If unsure, wait (lots).

Also: is realized gain a stored field, a computed field on the sell transaction, or out until net worth / activity reports?

### C. Opening of holdings vs cash Opening

**Q6. Type name and how many Openings?**

Cash already took `TransactionType.Opening` with R19: at most **one per account**, and **only while the account has no other transaction**.

That rule cannot host “open AAPL this week, open MSFT next month” if Opening remains a single account-level event.

Options:

- **O1:** New type `OpeningHoldings` (name open). Cash `Opening` rules unchanged. Per `(Account, Instrument)` at most one holdings Opening. Holdings Opening allowed after cash activity. Cash Opening still cannot follow any other header, including a holdings Opening — so cash Opening must come first, or the book never gets a cash Opening (cash stays 0 + later TransferIn).
- **O2:** Same type `Opening`. Relax R19 so an Opening header may be cash-only, security-only, or both; allow **multiple** Opening headers per account as long as each instrument (and cash) is opened at most once.
- **O3:** One Opening header per account that may list many security legs + optional cash. After that header, further instruments enter only via Buy / TransferIn-of-stock. Matches “one Opening event” but is clumsy for a book that grows over years.

Lean leaning **O1**: do not silently break posting R19. Call holdings Opening a distinct type. Document the cash-Opening ordering trap.

**Q7. Holdings Opening payload and guards?**

To lock:

- Quantity `> 0`? (lean: yes)
- Cost `> 0`, or cost `≥ 0` allowed (gift / zero-cost spin)? (lean: cost `≥ 0`, quantity `> 0`)
- Cost currency omitted and taken from instrument quote? (lean: yes; body must not send a different currency)
- At most one holdings Opening per `(Account, Instrument)`?
- Allowed when that instrument already has a Buy / scrip? (lean: no — Opening is first activity **for that instrument**, other types allowed first **for other instruments** and for cash)
- Allowed after a cash Opening / TransferIn? (lean: yes under O1)
- Cash amount on a holdings Opening header? (lean: omit; do not smuggle a deposit into OpeningHoldings)
- Auto zero-quantity Opening? (lean: no, same as cash)

### D. Quantity, close, reverse

**Q8. Negative quantity?**

Lean: no. After every insert, `SUM(qty) ≥ 0` per `(Account, Instrument)`. Sell / reverse that would go short → 400.

**Q9. Quantity scale?**

Cash / money is `decimal(18,4)`. Units are not money.

Options: `(18,4)` same as money; `(18,8)` for fractional ETFs / crypto-later; integer shares only.

Lean: `decimal(18,8)` stored, application does not invent a per-instrument DecimalPlaces in this increment (catalog has no such column). Confirm we will not add Instrument.QuantityScale later in a way that rewrites the column.

**Q10. Reverse of a mixed / security transaction?**

Posting reverse already copies the opposite cash amount. Extend:

- Opposite security quantity and opposite cost movement (not “recompute average from scratch on the reversal row”).
- Still one reversal per original.
- Reverse of OpeningHoldings allowed while the account is Open and the result stays non-short.
- Reverse does not require rowVersion (already locked).

Any extra rule when later legs exist on the same instrument (e.g. cannot reverse Opening after a Sell)? Lean: **no extra rule** if quantity stays ≥ 0; the original Opening row stays as history.

**Q11. Close account while holdings remain?**

Accounts R16 only guards cash `SUM = 0`. Holdings “still do not block close (no holdings table)”.

Lean: amend R16 — close rejected unless cash `SUM = 0` **and** every instrument quantity `SUM = 0`. Cost should then be 0 if the cost rule is consistent. No bulk-liquidate inside `Account.Close()`.

**Q12. Holdings analog of `CloseOut`?**

Cash CloseOut sends leftover cash off-books. For stock:

- **C1:** No CloseOut-of-stock. Caller must Sell, TransferOut-of-stock, or reverse until flat, then close.
- **C2:** `CloseOut` may include security legs that zero each remaining quantity and write cost to 0 (off-books). Dangerous if it looks like a sale with no cash.

Lean: **C1** this increment. Name the deferral like posting §13.

**Q13. Security TransferIn / TransferOut (stock in/out, no cash)?**

Needed for inherited portfolios moved without a taxable buy, and for flattening before close if Q12 is C1.

Lean: **defer** unless Q1 picks A2 and we explicitly want a no-cash stock movement other than Opening. Opening already covers “I already hold this”. Out-of-account stock movement waits with cross-account transfer.

### E. Types if this increment goes past Opening

Only answer if Q1 is A2 or A3.

**Q16. Buy / Sell signs and price?**

Lean:

- Buy: cash `amount < 0` in **account** currency; security qty `> 0`; cost `> 0` in quote currency.
- Sell: cash `amount > 0`; security qty `< 0`; cost movement = −(average × |qty|) computed by the server (client does not send cost-out).
- Price is not stored. Not inferred as a required column. Memo/reference optional as today.

Open: when quote currency ≠ account currency, what is the cash amount? Options: reject the pair until live FX (strict, matches “never treat cross as 1”); allow cash in account currency and cost in quote (two numbers, no implied rate stored); require an explicit rate field (probably wait).

**Q17. Split / scrip / bonus?**

Direction already: security only, cash 0 / omitted, total cost unchanged.

Open: ratio vs explicit new quantity? One instrument only? Reverse of a split in scope?

**Q18. Dividend?**

Cash-only, tied to an instrument id (new optional header field or a security leg with qty 0)? DRIP = Buy + cash Dividend, not a third model. Lean: **defer Dividend HTTP** unless Q1 is A3. Instrument id on a cash-only dividend is still a holdings concern (must be an instrument the account may hold; not necessarily one it currently holds).

### F. HTTP, policies, seed

**Q14. Read API and policy?**

Options:

- `GET /accounts/{id}/holdings` under `accounts.read` (mirrors `/cash-balance`)
- `GET /holdings?accountId=&customerId=` under `transactions.read` or a new `holdings.read`
- Both

Lean: account-nested read for the obvious screen; collection list only if we already know the portal will want a firm-wide holdings grid. Policy: do **not** add `holdings.create`. Prefer `accounts.read` for the nested read so a caller who can see the account can see its positions.

Does account list/item grow a `holdingsCount` or embedded array? Lean: **no** embed on list; nested resource only.

**Q15. Holding identity?**

Projection without PublicId (key = account + instrument public ids) vs Holdings.PublicId for a future `/holdings/{id}`.

Lean: no PublicId until a write or a deep link needs it. Reads return `accountId`, `instrumentId`, `symbol`, `name`, `quantity`, `cost`, `costCurrency`.

**Q19. Create body shape?**

Posting item today flattens cash as `amount` / `currency`. Lean for create:

```json
{
  "accountId": "<account publicId>",
  "type": "OpeningHoldings",
  "bookedAt": "2026-09-27T09:00:00+12:00",
  "memo": null,
  "reference": null,
  "security": {
    "instrumentId": "<instrument publicId>",
    "quantity": 100,
    "cost": 1500.00
  }
}
```

Cash types keep today’s flat `amount` for one more increment **or** immediately wrap as `"cash": { "amount", "currency?" }` so Buy does not invent a third shape. Prefer one wrap when the first security type ships, and keep a compatibility note on cash-only items.

**Q20. TestSeed?**

Lean: on Demo brokerage (USD, already seeded) + an existing TestSeed instrument in that tenant, insert one holdings Opening (quantity + cost). Do not seed a Buy that looks like a live trade if Buy is out of increment. Functional tests still create their own rows.

**Q21. Disable instrument / disable customer / closed account?**

Already inherited. Confirm only:

- New security leg of a disabled instrument → 400; reversal of an existing one → allowed.
- Closed account → 400 on create/reverse (posting R6).
- Disabled Customer → 400 on create/reverse (posting R7).
- Does disable-instrument stay allowed while holdings exist? Instruments R16 left that to this slice. Lean: **yes, disable still allowed** (picker drops it; positions remain).

### G. Cross-cutting — confirm still deferred

Not questions to design now; confirm they stay out so they do not sneak into the first holdings PR:

- Portal Accounts / activity / holdings pages
- Net worth, snapshots, Dashboard widgets
- Cross-account stock or cash transfer
- Live FX and stored trade FX rate
- Fees, withholding, corporate-action pipelines
- Lots / CGT
- Property / Credit
- Idempotency redesign (reuse ADR 0015)

---

## 9. UI

None in this increment. Pages live in [docs/portals/](../portals/). Scalar is enough for SystemAdmin and local smoke.

---

## 10. Tests (only after `accepted`)

Sketch so the question list stays honest. Not a CLI brief.

| Project | Assert (expected once locked) |
| --- | --- |
| Domain.UnitTests | Quantity sign; cost rule; no mutate posted header; Opening-of-holdings guards; Bank/Cash refuse security |
| Application.FunctionalTests | 401 / Customer 403; OpeningHoldings happy path; second Opening same instrument 400; disabled instrument 400; Bank account 400; reverse; close rejected while qty ≠ 0 |
| Infrastructure.IntegrationTests | Other tenant 404; Adviser unassigned 404; no quantity column on `Accounts`; no free-text symbol on a holding |

---

## 11. What this draft does **not** lock

Column lists, script number, policy names beyond the lean, Buy/Sell HTTP, `Holdings` table yes/no, Opening type name, close-flat-holdings, quantity scale.

ADR 0011 stays direction until this spec is accepted and 0011 is expanded.

---

## 12. Suggested discussion order

Resolve in this order; later questions collapse once early ones land:

1. Q1 increment contents
2. Q6 Opening type vs cash R19
3. Q4 / Q5 Holdings table + cost method
4. Q11 / Q12 close and CloseOut-of-stock
5. Q8 / Q9 quantity sign and scale
6. Q14 / Q15 / Q19 HTTP
7. Q16–Q18 only if Q1 includes those types
8. Q20 TestSeed

Then rewrite this file toward `review` (rules table, domain commands, script name, item JSON) and amend posting / accounts / glossary / ADR 0011 in the same accept change.
